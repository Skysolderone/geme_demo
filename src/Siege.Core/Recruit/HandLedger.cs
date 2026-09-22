using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Recruit;

/// <summary>
/// 一局的手牌与征募账本：每名玩家的两段账手牌、小回合阶段、私人征募面板与征募记录。
/// </summary>
/// <remarks>
/// <para><b>两段账</b>（design.md D1）：类型 → (回合前基数, 本轮新增)。<see cref="BeginTurn"/> 把上回合总数折进基数；
/// 征募只累加新增；<see cref="DeductHand"/>（确认落子 ≥1 枚）先扣新增再把新增折进基数；<see cref="OnPass"/> 只清新增。
/// 开局 5 枚普通子直接计入基数（裁决记录 5）。</para>
/// <para><b>类型槽</b>（D2）：已占槽位 = 数量 &gt; 0 的类型数，派生不存储；数量归零的条目立即移除，槽位自动释放。</para>
/// <para><b>随机</b>（D4 / D5）：只消费 <see cref="GameSeed.Recruit"/> 子流，每个候选位各做一次
/// <see cref="RandomStream.WeightedPick"/>，权重表按当前快照在抽取时现算（D3）。</para>
/// <para><b>信息边界</b>（D6）：本类的公开成员只给出 <see cref="HandPublicView"/>；私有视图、面板与选取操作只经
/// <see cref="AccessFor"/> 返回的 <see cref="PlayerHandAccess"/> 交给该玩家本人。调试 AI 的全量读取走
/// <see cref="Debug"/>（<c>internal</c>，仅测试程序集可达，设计文档 §15.3）。</para>
/// <para><b>接线</b>：<see cref="BeginTurn"/> / <see cref="EndTurn"/> 对应流程层的小回合开始 / 结束事件；
/// <see cref="DeductHand"/> / <see cref="OnPass"/> 与 <see cref="ISettlementHooks"/> 同签名，由流程层的复合钩子转发。
/// 正式接线属于 add-match-flow。</para>
/// </remarks>
public sealed class HandLedger
{
    /// <summary>开局发放的普通子枚数（设计文档 §9.1）。</summary>
    public const int InitialBasicCount = 5;

    private readonly SortedDictionary<PlayerId, PlayerState> _players = [];
    private readonly RandomStream _recruit;
    private readonly List<RecruitTurnRecord> _records = [];
    private readonly int _artisanWeight;
    private int _sequence;

    /// <summary>建立账本（匠人权重取默认 10）。</summary>
    public HandLedger(IEnumerable<PlayerId> players, GameSeed seed)
        : this(players, seed, RecruitWeights.DefaultArtisanWeight)
    {
    }

    /// <summary>
    /// 建立账本：为每名玩家发放 5 枚普通子（计入回合前基数），并派生 <c>recruit</c> 子流。
    /// <paramref name="artisanWeight"/> 是对局配置的匠人征募权重（artisan-terrain-edit R-2），全程只在这里持有一份。
    /// </summary>
    public HandLedger(IEnumerable<PlayerId> players, GameSeed seed, int artisanWeight)
    {
        ArgumentNullException.ThrowIfNull(players);
        RecruitWeights.RequireValidArtisanWeight(artisanWeight);
        _artisanWeight = artisanWeight;
        foreach (PlayerId player in players)
        {
            if (_players.ContainsKey(player))
            {
                throw new ArgumentException($"玩家 {player} 重复。", nameof(players));
            }

            var state = new PlayerState();
            state.Hand[PieceType.Basic] = new HandEntry(InitialBasicCount, 0);
            _players.Add(player, state);
        }

        if (_players.Count == 0)
        {
            throw new ArgumentException("至少需要一名玩家。", nameof(players));
        }

        _recruit = seed.Stream(GameSeed.Recruit);
        Debug = new HandDebugAccess(this);
    }

    /// <summary>全部玩家，按标识排序。</summary>
    public IEnumerable<PlayerId> Players => _players.Keys;

    /// <summary>全部小回合的征募记录，按发生顺序（含进行中的小回合尚未写入）。</summary>
    public IReadOnlyList<RecruitTurnRecord> Records => _records.AsReadOnly();

    /// <summary><c>recruit</c> 子流已消费的随机数个数，供子流隔离回归断言。</summary>
    public long RecruitStreamConsumed => _recruit.Consumed;

    /// <summary>调试 AI 的全量读取旁路（设计文档 §15.3）。<c>internal</c>：只对测试程序集可达，面向玩家的对局拿不到。</summary>
    internal HandDebugAccess Debug { get; }

    // ---------- 公开信息 ----------

    /// <summary>某玩家的公开视图：只有类型集合与是否仍在行动。</summary>
    public HandPublicView PublicView(PlayerId player)
    {
        PlayerState state = Require(player);
        return new HandPublicView(player, state.Hand.Keys.ToImmutableSortedSet(), !state.Resigned);
    }

    /// <summary>全部玩家的公开视图。</summary>
    public ImmutableArray<HandPublicView> PublicViews() => [.. _players.Keys.Select(PublicView)];

    /// <summary>某玩家手牌是否为空，供 add-match-flow 判定出局条件（盘面无棋子且手牌无棋子）。</summary>
    public bool IsHandEmpty(PlayerId player) => Require(player).Hand.Count == 0;

    /// <summary>某玩家当前持有的类型数（= 已占用槽位），供流程层在生成效果快照时作为 <c>heldTypeCount</c> 传入。</summary>
    public int HeldTypeCount(PlayerId player) => Require(player).Hand.Count;

    /// <summary>某玩家当前所处的小回合阶段。</summary>
    public TurnPhase PhaseOf(PlayerId player) => Require(player).Phase;

    /// <summary>该玩家本人的私有访问句柄。流程层只把它交给该玩家的控制者（UI 或其对战 AI），不交给任何其他玩家。</summary>
    public PlayerHandAccess AccessFor(PlayerId player)
    {
        Require(player);
        return new PlayerHandAccess(this, player);
    }

    // ---------- 流程事件 ----------

    /// <summary>
    /// 小回合开始：折叠两段账（上回合总数 → 基数，新增清零），进入整理手牌阶段。
    /// <paramref name="snapshot"/> 是本小回合的效果快照，其 <see cref="EffectSnapshot.HeldTypeCount"/> MUST 与当前持有类型数一致，否则视为接线错误。
    /// </summary>
    public void BeginTurn(PlayerId player, EffectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        PlayerState state = Require(player);
        if (state.Resigned)
        {
            throw new SiegeRuleException($"玩家 {player} 已弃赛，不再拥有小回合。");
        }

        if (state.Phase != TurnPhase.Idle)
        {
            throw new SiegeRuleException($"玩家 {player} 的上一个小回合尚未结束（阶段 {state.Phase}）。");
        }

        if (snapshot.Player != player)
        {
            throw new SiegeRuleException($"效果快照属于 {snapshot.Player}，不是 {player}。");
        }

        if (snapshot.HeldTypeCount != state.Hand.Count)
        {
            throw new SiegeRuleException(
                $"效果快照记录的持有类型数 {snapshot.HeldTypeCount} 与玩家 {player} 实际持有的 {state.Hand.Count} 种不一致。");
        }

        if (snapshot.RevealCount < 0 || snapshot.FreePickCount < 0 || snapshot.TypeSlots < 0)
        {
            throw new SiegeRuleException("效果快照的展示数、免费选取数与类型槽不得为负。");
        }

        // 折叠两段账。合法流程里每条结算路径（DeductHand 折叠 / OnPass 清零）结束时新增已为 0，这里是对 D1 的防御性兑现，不依赖调用方守约。
        foreach (PieceType type in state.Hand.Keys.ToArray())
        {
            HandEntry e = state.Hand[type];
            state.Hand[type] = new HandEntry(e.Total, 0);
        }

        state.Snapshot = snapshot;
        state.Phase = TurnPhase.Organize;
        state.Turn = new TurnState(++_sequence, snapshot.MajorRound);
    }

    /// <summary>小回合结束：未选取的候选消失，写入本小回合的征募记录。MUST 在确认落子或 Pass 之后调用。</summary>
    public void EndTurn(PlayerId player)
    {
        PlayerState state = Require(player);
        if (state.Phase != TurnPhase.Settled)
        {
            throw new SiegeRuleException($"玩家 {player} 的小回合尚未结算（阶段 {state.Phase}），不能结束。");
        }

        _records.Add(state.Turn!.ToRecord(player));
        state.Turn = null;
        state.Snapshot = null;
        state.Phase = TurnPhase.Idle;
    }

    /// <summary>
    /// 结算第 1 步（与 <see cref="ISettlementHooks.DeductHand"/> 同签名）：按已部署棋子扣减库存，先扣本轮新增再扣基数；
    /// 至少落 1 枚即把本轮全部新增折进基数（设计文档 §5.5）。库存不足或未进入征募阶段视为接线错误。
    /// </summary>
    public void DeductHand(PlayerId player, IReadOnlyDictionary<PieceType, int> deployed)
    {
        ArgumentNullException.ThrowIfNull(deployed);
        PlayerState state = Require(player);
        RequirePhase(player, state, TurnPhase.Recruit, "扣减手牌");

        int total = 0;
        foreach ((PieceType type, int count) in deployed)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deployed), count, "部署数量不得为负。");
            }

            int stock = state.Hand.TryGetValue(type, out HandEntry e) ? e.Total : 0;
            if (count > stock)
            {
                throw new SiegeRuleException($"手牌库存不足：{BatchFailure.DisplayName(type)}库存 {stock} 枚，结算要求扣减 {count} 枚。");
            }

            total = checked(total + count);
        }

        if (total == 0)
        {
            throw new SiegeRuleException("0 落子是 Pass，应走 OnPass 而不是 DeductHand。");
        }

        foreach ((PieceType type, int count) in deployed)
        {
            if (count == 0)
            {
                continue;
            }

            HandEntry e = state.Hand[type];
            int fromGained = Math.Min(count, e.Gained);
            int fromCarried = count - fromGained;
            SetEntry(state, type, new HandEntry(e.Carried - fromCarried, e.Gained - fromGained));
        }

        // 至少落 1 枚：本轮征募所得全部保留 → 折进基数
        foreach (PieceType type in state.Hand.Keys.ToArray())
        {
            HandEntry e = state.Hand[type];
            state.Hand[type] = new HandEntry(e.Total, 0);
        }

        state.Turn!.DeployedCount = total;
        state.Phase = TurnPhase.Settled;
    }

    /// <summary>
    /// Pass 事件（与 <see cref="ISettlementHooks.OnPass"/> 同签名）：撤销本小回合全部新增征募——只清「本轮新增」，基数不动；
    /// 归零的类型释放槽位。本轮的主动整类弃牌不恢复（裁决记录 4）。
    /// </summary>
    public void OnPass(PlayerId player)
    {
        PlayerState state = Require(player);
        RequirePhase(player, state, TurnPhase.Recruit, "Pass");

        int revoked = 0;
        foreach (PieceType type in state.Hand.Keys.ToArray())
        {
            HandEntry e = state.Hand[type];
            revoked += e.Gained;
            SetEntry(state, type, new HandEntry(e.Carried, 0));
        }

        state.Turn!.Passed = true;
        state.Turn.RevokedCount = revoked;
        state.Phase = TurnPhase.Settled;
    }

    /// <summary>主动弃赛：保留最后的公开手牌类型并标记为不再行动；此后对该玩家的任何小回合操作都被拒绝。</summary>
    public void Resign(PlayerId player)
    {
        PlayerState state = Require(player);
        if (state.Resigned)
        {
            throw new SiegeRuleException($"玩家 {player} 已弃赛，不能重复弃赛。");
        }

        if (state.Phase == TurnPhase.Organize)
        {
            // 整理阶段弃赛：尚无面板与新增，直接视为 0 落子结算
            state.Turn!.Passed = true;
            state.Phase = TurnPhase.Settled;
        }
        else if (state.Phase == TurnPhase.Recruit)
        {
            // 征募/部署中途弃赛：未提交的新增征募随之作废（等价于 Pass），记录照常写入
            OnPass(player);
        }

        if (state.Phase == TurnPhase.Settled)
        {
            EndTurn(player);
        }

        state.Resigned = true;
    }

    // ---------- 持久化（小回合边界） ----------

    /// <summary>
    /// 导出账本状态供对局存档。只允许在<b>小回合边界</b>（全部玩家 <see cref="TurnPhase.Idle"/>）调用：
    /// 此时每条两段账的「本轮新增」必为 0，手牌只剩基数；进行中的面板与选取不在导出范围内。
    /// 征募记录是遥测，不随存档往返；<c>recruit</c> 子流只记录消费次数，恢复时从头派生再推进。
    /// </summary>
    public HandLedgerState Export()
    {
        ImmutableArray<PlayerHandState>.Builder players = ImmutableArray.CreateBuilder<PlayerHandState>(_players.Count);
        foreach ((PlayerId player, PlayerState state) in _players)
        {
            if (state.Phase != TurnPhase.Idle)
            {
                throw new SiegeRuleException($"玩家 {player} 正处于小回合（阶段 {state.Phase}），账本只能在小回合边界导出。");
            }

            ImmutableArray<HandStockEntry> hand = [.. state.Hand.Select(kv => new HandStockEntry(kv.Key, kv.Value.Total))];
            players.Add(new PlayerHandState(player.Value, hand, state.Resigned));
        }

        return new HandLedgerState(players.MoveToImmutable(), _recruit.Consumed, _sequence);
    }

    /// <summary>从 <see cref="Export"/> 的结果恢复账本：手牌全部计入基数，<c>recruit</c> 子流推进到相同消费位置。</summary>
    public static HandLedger Restore(GameSeed seed, HandLedgerState state) =>
        Restore(seed, state, RecruitWeights.DefaultArtisanWeight);

    /// <summary>从 <see cref="Export"/> 的结果恢复账本，并带上对局配置的匠人征募权重。</summary>
    public static HandLedger Restore(GameSeed seed, HandLedgerState state, int artisanWeight)
    {
        ArgumentNullException.ThrowIfNull(state);
        var ledger = new HandLedger(state.Players.Select(p => new PlayerId(p.Player)), seed, artisanWeight);
        foreach (PlayerHandState saved in state.Players)
        {
            PlayerState target = ledger._players[new PlayerId(saved.Player)];
            target.Hand.Clear();
            foreach (HandStockEntry entry in saved.Hand)
            {
                if (entry.Count <= 0)
                {
                    throw new FormatException($"存档中玩家 P{saved.Player} 的{BatchFailure.DisplayName(entry.Type)}数量 {entry.Count} 不合法。");
                }

                target.Hand[entry.Type] = new HandEntry(entry.Count, 0);
            }

            target.Resigned = saved.Resigned;
        }

        ledger._recruit.Advance(state.RecruitConsumed);
        ledger._sequence = state.Sequence;
        return ledger;
    }

    // ---------- 玩家私有操作（经 PlayerHandAccess / HandDebugAccess 到达） ----------

    internal HandPrivateView PrivateViewOf(PlayerId player)
    {
        PlayerState state = Require(player);
        return new HandPrivateView(player, state.Hand.ToImmutableSortedDictionary(), state.Phase, state.Snapshot?.TypeSlots);
    }

    internal void Discard(PlayerId player, PieceType type)
    {
        PlayerState state = Require(player);
        if (state.Phase != TurnPhase.Organize)
        {
            throw new SiegeRuleException($"整类弃牌只允许在整理手牌阶段进行；玩家 {player} 当前阶段为 {state.Phase}。");
        }

        if (!state.Hand.ContainsKey(type))
        {
            throw new SiegeRuleException($"玩家 {player} 未持有{BatchFailure.DisplayName(type)}，无从弃牌。");
        }

        state.Hand.Remove(type);
        state.Turn!.Discarded.Add(type);
    }

    internal void Discard(PlayerId player, PieceType type, int count)
    {
        PlayerState state = Require(player);
        int held = state.Hand.TryGetValue(type, out HandEntry e) ? e.Total : 0;
        if (count != held || held == 0)
        {
            throw new SiegeRuleException(
                $"弃牌必须整类进行：玩家 {player} 持有{BatchFailure.DisplayName(type)} {held} 枚，不能只弃 {count} 枚。");
        }

        Discard(player, type);
    }

    internal RecruitPanelView EnterRecruit(PlayerId player)
    {
        PlayerState state = Require(player);
        if (state.Phase != TurnPhase.Organize)
        {
            throw new SiegeRuleException($"玩家 {player} 当前阶段为 {state.Phase}，不能进入征募阶段。");
        }

        EffectSnapshot snapshot = state.Snapshot!;
        int overflow = state.Hand.Count - snapshot.TypeSlots;
        if (overflow > 0)
        {
            throw new SiegeRuleException(
                $"玩家 {player} 持有 {state.Hand.Count} 种类型，超出本小回合的 {snapshot.TypeSlots} 个类型槽；须先整类弃牌至少 {overflow} 种，征募阶段才能开始。");
        }

        // D3 / D4：权重按当前快照现算，每个候选位独立抽取、允许重复；只消费 recruit 子流。
        int[] table = RecruitWeights.AdjustedTable(snapshot, _artisanWeight);
        ImmutableArray<PieceType>.Builder candidates = ImmutableArray.CreateBuilder<PieceType>(snapshot.RevealCount);
        for (int i = 0; i < snapshot.RevealCount; i++)
        {
            candidates.Add(RecruitWeights.Order[_recruit.WeightedPick(table)]);
        }

        state.Turn!.Candidates = candidates.MoveToImmutable();
        state.Phase = TurnPhase.Recruit;
        return PanelOf(player);
    }

    internal RecruitPanelView PanelOf(PlayerId player)
    {
        PlayerState state = Require(player);
        if (state.Phase != TurnPhase.Recruit)
        {
            throw new SiegeRuleException($"玩家 {player} 当前没有征募面板（阶段 {state.Phase}）。");
        }

        EffectSnapshot snapshot = state.Snapshot!;
        TurnState turn = state.Turn!;
        ImmutableArray<RecruitCandidateView>.Builder views = ImmutableArray.CreateBuilder<RecruitCandidateView>(turn.Candidates.Length);
        for (int i = 0; i < turn.Candidates.Length; i++)
        {
            PieceType type = turn.Candidates[i];
            bool picked = turn.Picked.Contains(i);
            string? reason = picked ? null : RejectReason(state, snapshot, type);
            views.Add(new RecruitCandidateView(i, type, picked, IsSelectable: !picked && reason is null, reason));
        }

        return new RecruitPanelView(player, views.MoveToImmutable(), snapshot.FreePickCount, turn.Picked.Count,
            snapshot.TypeSlots, state.Hand.Count);
    }

    internal void Pick(PlayerId player, int candidateIndex)
    {
        PlayerState state = Require(player);
        if (state.Phase != TurnPhase.Recruit)
        {
            throw new SiegeRuleException($"玩家 {player} 当前阶段为 {state.Phase}，不能选取征募候选。");
        }

        TurnState turn = state.Turn!;
        if (candidateIndex < 0 || candidateIndex >= turn.Candidates.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateIndex), candidateIndex, $"候选位下标须在 0..{turn.Candidates.Length - 1}。");
        }

        if (turn.Picked.Contains(candidateIndex))
        {
            throw new SiegeRuleException($"候选位 #{candidateIndex} 已被选取。");
        }

        PieceType type = turn.Candidates[candidateIndex];
        string? reason = RejectReason(state, state.Snapshot!, type);
        if (reason is not null)
        {
            throw new SiegeRuleException($"不能选取候选位 #{candidateIndex}（{BatchFailure.DisplayName(type)}）：{reason}");
        }

        HandEntry e = state.Hand.TryGetValue(type, out HandEntry existing) ? existing : default;
        state.Hand[type] = new HandEntry(e.Carried, e.Gained + 1);
        turn.Picked.Add(candidateIndex);
    }

    /// <summary>测试专用：直接设定某玩家的手牌（全部计入回合前基数）。只允许在小回合之外调用，数量必须为正。</summary>
    internal void SeedHand(PlayerId player, IEnumerable<(PieceType Type, int Count)> entries)
    {
        PlayerState state = Require(player);
        if (state.Phase != TurnPhase.Idle)
        {
            throw new SiegeRuleException($"玩家 {player} 正处于小回合（阶段 {state.Phase}），不能直接设定手牌。");
        }

        state.Hand.Clear();
        foreach ((PieceType type, int count) in entries)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entries), count, "设定的数量必须为正；不持有的类型不要列出。");
            }

            state.Hand[type] = new HandEntry(count, 0);
        }
    }

    // ---------- 内部 ----------

    private static string? RejectReason(PlayerState state, EffectSnapshot snapshot, PieceType type)
    {
        if (state.Turn!.Picked.Count >= snapshot.FreePickCount)
        {
            return $"免费选取数已用完（本小回合最多 {snapshot.FreePickCount} 枚）。";
        }

        if (!state.Hand.ContainsKey(type) && state.Hand.Count >= snapshot.TypeSlots)
        {
            return $"无可用类型槽：{snapshot.TypeSlots} 个类型槽已被占满，且手牌中没有{BatchFailure.DisplayName(type)}；弃掉一整类即可选取。";
        }

        return null;
    }

    private static void SetEntry(PlayerState state, PieceType type, HandEntry entry)
    {
        if (entry.Total == 0)
        {
            state.Hand.Remove(type);
        }
        else
        {
            state.Hand[type] = entry;
        }
    }

    private static void RequirePhase(PlayerId player, PlayerState state, TurnPhase expected, string action)
    {
        if (state.Phase != expected)
        {
            throw new SiegeRuleException($"玩家 {player} 当前阶段为 {state.Phase}，不能{action}（须在 {expected} 阶段）。");
        }
    }

    private PlayerState Require(PlayerId player) =>
        _players.TryGetValue(player, out PlayerState? state)
            ? state
            : throw new SiegeRuleException($"账本中没有玩家 {player}。");

    private sealed class PlayerState
    {
        internal SortedDictionary<PieceType, HandEntry> Hand { get; } = [];

        internal TurnPhase Phase { get; set; } = TurnPhase.Idle;

        internal EffectSnapshot? Snapshot { get; set; }

        internal TurnState? Turn { get; set; }

        internal bool Resigned { get; set; }
    }

    private sealed class TurnState(int sequence, int majorRound)
    {
        internal ImmutableArray<PieceType> Candidates { get; set; } = [];

        internal List<int> Picked { get; } = [];

        internal List<PieceType> Discarded { get; } = [];

        internal bool Passed { get; set; }

        internal int DeployedCount { get; set; }

        internal int RevokedCount { get; set; }

        internal RecruitTurnRecord ToRecord(PlayerId player) =>
            new(sequence, player, majorRound, Candidates, [.. Picked], [.. Discarded], Passed, DeployedCount, RevokedCount);
    }
}
