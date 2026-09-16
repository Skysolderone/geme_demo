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
/// <see cref="SnapshotFor(PlayerId, GameBoard, IReadOnlyDictionary{PlayerId, PlayerStatus}, int, int, CatchUpBonus)"/> 在小回合开始时由流程层调用一次；
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
    public EffectSnapshot SnapshotFor(PlayerId player, GameBoard board, int heldTypeCount, int majorRound, CatchUpBonus catchUp = default)
    {
        ArgumentNullException.ThrowIfNull(board);
        RecalculateCore(board, roster: null);
        return BuildSnapshot(player, heldTypeCount, majorRound, catchUp);
    }

    /// <summary>
    /// 小回合开始：读取此刻由 <paramref name="player"/> 控制的全部非先锋信物，生成本小回合的不可变快照。
    /// 内部先按当前盘面重算控制，因此快照只反映「此刻」；此后盘面再变也不会改动已返回的快照。
    /// 已弃赛 / 已出局玩家没有小回合，为其生成快照是接线错误，抛 <see cref="SiegeRuleException"/>。
    /// </summary>
    public EffectSnapshot SnapshotFor(
        PlayerId player, GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus> roster, int heldTypeCount, int majorRound,
        CatchUpBonus catchUp = default)
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
        return BuildSnapshot(player, heldTypeCount, majorRound, catchUp);
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

    /// <summary>控制判定的唯一实现：读 <see cref="CoverageMap.OwnershipOf"/>，按名册把弃赛 / 出局者的控制标为封锁。</summary>
    private void RecalculateCore(GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus>? roster)
    {
        CoverageMap coverage = CoverageMap.Compute(board);
        foreach (RelicState relic in _relics.Values)
        {
            CellOwnership ownership = coverage.OwnershipOf(relic.Coord);
            relic.Control = ownership.Kind switch
            {
                OwnershipKind.Occupied or OwnershipKind.Exclusive => Resolve(ownership.Owner!.Value, roster, relic.Coord),
                OwnershipKind.Contested => RelicControl.Contested,
                OwnershipKind.Neutral => RelicControl.Uncontrolled,
                OwnershipKind.Obstacle => throw new SiegeRuleException($"信物格 {relic.Coord.ToNotation()} 是障碍格：地图数据不一致。"),
                _ => throw new ArgumentOutOfRangeException(nameof(ownership), ownership.Kind, "未知归属。"),
            };
        }
    }

    private static RelicControl Resolve(PlayerId owner, IReadOnlyDictionary<PlayerId, PlayerStatus>? roster, Coord coord)
    {
        if (roster is null)
        {
            return new RelicControl(RelicControlKind.Controlled, owner);
        }

        if (!roster.TryGetValue(owner, out PlayerStatus status))
        {
            throw new SiegeRuleException(
                $"盘面上出现名册外的玩家 {owner}（控制信物格 {coord.ToNotation()}）：信物控制的名册必须列出盘面上的每一名玩家，包括已弃赛与已出局者。");
        }

        return status == PlayerStatus.Active
            ? new RelicControl(RelicControlKind.Controlled, owner)
            : new RelicControl(RelicControlKind.Blocked, owner);
    }

    /// <summary>
    /// 按当前控制状态汇总非先锋信物。同类直接相加，无任何硬上限。
    /// <paramref name="catchUp"/> 是调用方在同一时刻算好的落后者征募补偿（catch-up-recruit）：本层只做相加，不读名次、不触发势力重算。
    /// </summary>
    private EffectSnapshot BuildSnapshot(PlayerId player, int heldTypeCount, int majorRound, CatchUpBonus catchUp)
    {
        int reveal = EffectSnapshot.BaseRevealCount + catchUp.RevealBonus;
        int freePick = EffectSnapshot.BaseFreePickCount + catchUp.PickBonus;
        int slots = EffectSnapshot.BaseTypeSlots;
        int deploy = EffectSnapshot.BaseDeployLimitFor(majorRound);
        var emblems = new SortedDictionary<PieceType, int>();

        foreach (RelicState relic in _relics.Values)
        {
            if (!relic.Control.GrantsEffectTo(player))
            {
                continue;
            }

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
                default:
                    throw new ArgumentOutOfRangeException(nameof(content), content.Type, "未知信物类型。");
            }
        }

        var snapshot = new EffectSnapshot(player, majorRound, reveal, freePick, slots, deploy, emblems.ToImmutableSortedDictionary(), heldTypeCount, catchUp);
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
