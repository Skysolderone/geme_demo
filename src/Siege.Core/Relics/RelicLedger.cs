using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Relics;

/// <summary>
/// 一局的信物账本：内容（开局生成后不变）、揭示（单调）、控制（每次结算重算的派生量）、效果快照与先锋读取。
/// </summary>
/// <remarks>
/// <para><b>揭示 ≠ 授予</b>（design.md D3）：<c>已揭示</c> 与 <c>控制者</c> 分开存储，可表达「已揭示且无人控制」；
/// <see cref="Reveal"/> 只公开信息，不改任何参数。</para>
/// <para><b>控制判定不自己算覆盖</b>（D4）：唯一输入是 <see cref="CoverageMap.OwnershipOf"/>——它已把「直接占据优先于唯一覆盖」
/// 合成一个查询。本类不做任何邻接遍历。</para>
/// <para><b>接线</b>：<see cref="Reveal"/> 对应结算顺序第 4 步（<c>ISettlementHooks.OnRevealRelics</c>，此时提子已完成），
/// <see cref="RecalculateControl(GameBoard, IReadOnlyDictionary{PlayerId, PlayerStatus})"/> 对应第 5 步；
/// <see cref="SnapshotFor(PlayerId, GameBoard, IReadOnlyDictionary{PlayerId, PlayerStatus}, int, int)"/> 在小回合开始时由流程层调用一次；
/// <see cref="ReadInitiativeBonuses(GameBoard, IReadOnlyDictionary{PlayerId, PlayerStatus})"/> 在大回合结束时调用。
/// 正式接线属于 add-match-flow。</para>
/// </remarks>
public sealed class RelicLedger
{
    private readonly SortedDictionary<Coord, RelicState> _relics = [];
    private readonly List<RelicRevealEvent> _reveals = [];

    public RelicLedger(RelicGenerationRecord generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        Generation = generation;
        foreach (RelicPlacement placement in generation.Placements)
        {
            _relics.Add(placement.Coord, new RelicState(placement));
        }
    }

    /// <summary>开局生成记录（种子 + 全部分布）。</summary>
    public RelicGenerationRecord Generation { get; }

    /// <summary>全部揭示事件，按发生顺序。</summary>
    public IReadOnlyList<RelicRevealEvent> RevealEvents => _reveals;

    /// <summary>遥测：整局部署上限峰值及其首次出现的大回合；尚未生成过快照时为 <c>null</c>。</summary>
    public DeployLimitPeak? DeployLimitPeak { get; private set; }

    /// <summary>信物格坐标，字典序。</summary>
    public IEnumerable<Coord> Coords => _relics.Keys;

    /// <summary>某格的公开状态；不是信物格时抛出。</summary>
    public RelicPublicState PublicStateOf(Coord coord) => Require(coord).ToPublic();

    /// <summary>
    /// 全部信物的<b>真实</b>内容类型（坐标 → 类型，字典序）：正式结算的势力计算用它读取计分信物（more-pieces-relics D3）。
    /// 只供流程层的正式结算与测试使用；预演与 AI 只能用批次开始前已揭示的公开内容（<see cref="PublicStates"/>）。
    /// </summary>
    public ImmutableSortedDictionary<Coord, RelicType> TrueContents() =>
        _relics.Values.ToImmutableSortedDictionary(r => r.Coord, r => r.Content.Type);

    /// <summary>全部信物的公开状态，字典序。</summary>
    public ImmutableArray<RelicPublicState> PublicStates() => [.. _relics.Values.Select(r => r.ToPublic())];

    /// <summary>某格当前控制状态（最近一次重算的结果）。</summary>
    public RelicControl ControlOf(Coord coord) => Require(coord).Control;

    /// <summary>某格是否已揭示。</summary>
    public bool IsRevealed(Coord coord) => Require(coord).IsRevealed;

    /// <summary>
    /// 第 4 步：把首次进入任意玩家覆盖范围（或被直接占据）的信物永久公开。只依赖 <see cref="CoverageMap"/>，
    /// 争议格照常揭示；已揭示的信物不再产生事件。返回本次新揭示的事件。
    /// </summary>
    public ImmutableArray<RelicRevealEvent> Reveal(GameBoard board, int majorRound)
    {
        ArgumentNullException.ThrowIfNull(board);
        CoverageMap coverage = CoverageMap.Compute(board);
        ImmutableArray<RelicRevealEvent>.Builder revealed = ImmutableArray.CreateBuilder<RelicRevealEvent>();
        foreach (RelicState relic in _relics.Values)
        {
            OwnershipKind kind = coverage.OwnershipOf(relic.Coord).Kind;
            if (kind == OwnershipKind.Obstacle)
            {
                throw new SiegeRuleException($"信物格 {relic.Coord.ToNotation()} 是障碍格：地图数据不一致。");
            }

            // 首次进入任意玩家的覆盖范围 = 独占、争议或被直接占据；直接占据必然控制，控制而不揭示会让公开面板出现来源不明的加成（§13.1 / §14.3）。
            if (relic.IsRevealed || kind == OwnershipKind.Neutral)
            {
                continue;
            }

            relic.MarkRevealed(majorRound);
            var evt = new RelicRevealEvent(relic.Coord, relic.Content, majorRound);
            _reveals.Add(evt);
            revealed.Add(evt);
        }

        return revealed.ToImmutable();
    }

    /// <summary>
    /// 导出账本中<b>不能由种子重算</b>的部分供存档：揭示事件（含发生的大回合）与部署上限峰值遥测。
    /// 信物内容由 <see cref="Generation"/> 的种子重新生成；控制状态是派生量，恢复后按盘面重算。
    /// </summary>
    public RelicLedgerState ExportState() =>
        new([.. _reveals.Select(e => new RevealedRelic(e.Coord.ToNotation(), e.MajorRound))], DeployLimitPeak);

    /// <summary>从生成记录 + <see cref="ExportState"/> 的结果恢复账本。揭示按存档顺序重放；存档里出现非信物格即视为损坏。</summary>
    public static RelicLedger Restore(RelicGenerationRecord generation, RelicLedgerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var ledger = new RelicLedger(generation);
        foreach (RevealedRelic revealed in state.Revealed)
        {
            Coord coord = Coord.Parse(revealed.Coord);
            if (!ledger._relics.TryGetValue(coord, out RelicState? relic))
            {
                throw new FormatException($"存档中的揭示记录指向非信物格 {revealed.Coord}。");
            }

            relic.MarkRevealed(revealed.MajorRound);
            ledger._reveals.Add(new RelicRevealEvent(coord, relic.Content, revealed.MajorRound));
        }

        ledger.DeployLimitPeak = state.DeployLimitPeak;
        return ledger;
    }

    /// <summary>无名册重载：盘面上的全部玩家视为参赛中。只适用于尚无流程层状态的单元测试；正式对局 MUST 走带名册的重载。</summary>
    public void RecalculateControl(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        RecalculateCore(board, roster: null);
    }

    /// <summary>
    /// 第 5 步：按当前盘面重算全部信物的控制归属，结果只依赖盘面与名册，不保留任何历史。
    /// 名册 MUST 列出盘面上的每一名玩家（含已弃赛、已出局者），否则抛 <see cref="SiegeRuleException"/>。
    /// </summary>
    public void RecalculateControl(GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus> roster)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roster);
        RecalculateCore(board, roster);
    }

    /// <summary>无名册重载（测试便利）：盘面上的全部玩家视为参赛中。</summary>
    public EffectSnapshot SnapshotFor(PlayerId player, GameBoard board, int heldTypeCount, int majorRound)
    {
        ArgumentNullException.ThrowIfNull(board);
        RecalculateCore(board, roster: null);
        return BuildSnapshot(player, heldTypeCount, majorRound);
    }

    /// <summary>
    /// 小回合开始：读取此刻由 <paramref name="player"/> 控制的全部非先锋信物，生成本小回合的不可变快照。
    /// 内部先按当前盘面重算控制，因此快照只反映「此刻」；此后盘面再变也不会改动已返回的快照。
    /// 已弃赛 / 已出局玩家没有小回合，为其生成快照是接线错误，抛 <see cref="SiegeRuleException"/>。
    /// </summary>
    public EffectSnapshot SnapshotFor(
        PlayerId player, GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus> roster, int heldTypeCount, int majorRound)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roster);
        if (!roster.TryGetValue(player, out PlayerStatus status))
        {
            throw new SiegeRuleException($"名册中没有玩家 {player}，无法为其生成效果快照。");
        }

        if (status != PlayerStatus.Active)
        {
            throw new SiegeRuleException($"玩家 {player} 状态为 {status}，不再拥有小回合，也不消费任何信物效果。");
        }

        RecalculateCore(board, roster);
        return BuildSnapshot(player, heldTypeCount, majorRound);
    }

    /// <summary>无名册重载（测试便利）：盘面上的全部玩家视为参赛中。</summary>
    public ImmutableSortedDictionary<PlayerId, int> ReadInitiativeBonuses(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        RecalculateCore(board, roster: null);
        return SumVanguard(board.AllGroups().Select(g => g.Owner).Distinct());
    }

    /// <summary>
    /// 大回合结束：读取全部先锋信物，输出每名<b>参赛中</b>玩家的先手修正（无先锋为 0）。
    /// 这是先锋唯一的读取时机；已弃赛 / 已出局者不在输出中——他们不再获得先手收益。
    /// </summary>
    public ImmutableSortedDictionary<PlayerId, int> ReadInitiativeBonuses(GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus> roster)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roster);
        RecalculateCore(board, roster);
        return SumVanguard(roster.Where(kv => kv.Value == PlayerStatus.Active).Select(kv => kv.Key));
    }

    private ImmutableSortedDictionary<PlayerId, int> SumVanguard(IEnumerable<PlayerId> players)
    {
        ImmutableSortedDictionary<PlayerId, int>.Builder bonuses = ImmutableSortedDictionary.CreateBuilder<PlayerId, int>();
        foreach (PlayerId player in players)
        {
            int bonus = 0;
            foreach (RelicState relic in _relics.Values)
            {
                if (relic.Content.Type == RelicType.Vanguard && relic.Control.GrantsEffectTo(player))
                {
                    bonus += relic.Content.Magnitude;
                }
            }

            bonuses[player] = bonus;
        }

        return bonuses.ToImmutable();
    }

    /// <summary>重算全部信物的控制归属：逐格调用控制判定的唯一实现 <see cref="RelicControl.Of"/>。</summary>
    private void RecalculateCore(GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus>? roster)
    {
        CoverageMap coverage = CoverageMap.Compute(board);
        foreach (RelicState relic in _relics.Values)
        {
            relic.Control = RelicControl.Of(coverage, relic.Coord, roster);
        }
    }

    /// <summary>
    /// 按当前控制状态汇总非先锋信物。同类直接相加，无任何硬上限。
    /// 输入只有信物控制与当前大回合（restore-go-core-rules D7）：展示数 / 选取数 = 默认值 + 信物，部署上限 = 分阶段基础值 + 军令。
    /// </summary>
    /// <remarks>
    /// more-pieces-relics：驿站（D4）是"驿站加成"的唯一实现——每枚受控驿站加「受控信物总枚数 − 1」（总枚数含先锋与其他驿站，每枚按 1 计，
    /// 只数 <see cref="RelicControl.GrantsEffectTo"/> 的，争议 / 无人控制 / 封锁都不计）；工坊（D5）只置一个布尔标记，多枚不叠加。
    /// 连营 / 犄角是计分信物，不进快照（在势力计算时按当前控制读取）。
    /// </remarks>
    private EffectSnapshot BuildSnapshot(PlayerId player, int heldTypeCount, int majorRound)
    {
        int reveal = EffectSnapshot.BaseRevealCount;
        int freePick = EffectSnapshot.BaseFreePickCount;
        int slots = EffectSnapshot.BaseTypeSlots;
        int deploy = EffectSnapshot.BaseDeployLimitFor(majorRound);
        var emblems = new SortedDictionary<PieceType, int>();
        var relays = new List<Coord>();
        int controlled = 0;
        bool workshop = false;

        foreach (RelicState relic in _relics.Values)
        {
            if (!relic.Control.GrantsEffectTo(player))
            {
                continue;
            }

            controlled++;

            RelicContent content = relic.Content;
            switch (content.Type)
            {
                case RelicType.Prospecting:
                    reveal += content.Magnitude;
                    break;
                case RelicType.Conscription:
                    freePick += content.Magnitude;
                    break;
                case RelicType.Depot:
                    slots += content.Magnitude;
                    break;
                case RelicType.Command:
                    deploy += content.Magnitude;
                    break;
                case RelicType.SchoolEmblem:
                    PieceType piece = content.EmblemPiece!.Value;
                    emblems[piece] = (emblems.TryGetValue(piece, out int n) ? n : 0) + content.Magnitude;
                    break;
                case RelicType.Vanguard:
                    // 先锋不进小回合快照：它在大回合结束时由 ReadInitiativeBonuses 另行读取。
                    break;
                case RelicType.Relay:
                    relays.Add(relic.Coord);
                    break;
                case RelicType.Workshop:
                    workshop = true;
                    break;
                case RelicType.Encampment:
                case RelicType.Pincer:
                    // 计分信物不进快照：势力计算时按当前控制读取（PowerCalculator，D3）。
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(content), content.Type, "未知信物类型。");
            }
        }

        // 驿站：每枚计除自身以外的全部受控信物（含其他驿站与先锋），逐枚列出来源并并入展示数。
        ImmutableSortedDictionary<Coord, int> relaySources = relays.ToImmutableSortedDictionary(c => c, _ => controlled - 1);
        reveal += relaySources.Values.Sum();

        var snapshot = new EffectSnapshot(
            player, majorRound, reveal, freePick, slots, deploy, emblems.ToImmutableSortedDictionary(), heldTypeCount, relaySources, workshop);
        if (DeployLimitPeak is null || snapshot.DeployLimit > DeployLimitPeak.DeployLimit)
        {
            DeployLimitPeak = new DeployLimitPeak(snapshot.DeployLimit, majorRound, player);
        }

        return snapshot;
    }

    private RelicState Require(Coord coord) =>
        _relics.TryGetValue(coord, out RelicState? relic) ? relic : throw new KeyNotFoundException($"{coord.ToNotation()} 不是信物格。");

    /// <summary>单枚信物的账本状态。<see cref="IsRevealed"/> 单调（只能由假变真），<see cref="Control"/> 是每次重算整体替换的派生量。</summary>
    private sealed class RelicState
    {
        internal RelicState(RelicPlacement placement)
        {
            Coord = placement.Coord;
            Content = placement.Content;
            Spec = placement.Spec;
        }

        internal Coord Coord { get; }

        internal RelicContent Content { get; }

        internal RelicCellSpec Spec { get; }

        internal bool IsRevealed { get; private set; }

        internal int? RevealedInMajorRound { get; private set; }

        internal RelicControl Control { get; set; } = RelicControl.Uncontrolled;

        internal void MarkRevealed(int majorRound)
        {
            if (IsRevealed)
            {
                return;
            }

            IsRevealed = true;
            RevealedInMajorRound = majorRound;
        }

        internal RelicPublicState ToPublic() =>
            new(Coord, Spec, IsRevealed, IsRevealed ? Content : null, RevealedInMajorRound, Control);
    }
}
