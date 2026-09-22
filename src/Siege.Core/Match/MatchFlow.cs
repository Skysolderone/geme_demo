using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 对局顶层状态机（设计文档 §4、§5、§11、§12）：插旗 → 大回合 / 小回合推进 → 出局、弃赛与终局。
/// 它自己几乎不做计算，但决定<b>每件事发生的时机</b>。
/// </summary>
/// <remarks>
/// <para><b>时机（design.md Context）</b>：信物快照在小回合<b>开始</b>时读一次（<see cref="BeginTurn"/>）；先锋在大回合<b>结束</b>时
/// 单次拉取、不缓存（<see cref="EndMajorRound"/>，D4）；出局检查在每次合法批次结算后与每次 Pass 后对<b>全部</b>参赛玩家执行
/// （<see cref="CheckEliminations"/>，restore-go-core-rules D3）；整轮 Pass 用跨回合的连续 Pass 计数器（<see cref="PassStreak"/>，D2 / 裁决 3）。</para>
/// <para><b>结算接线</b>：本类以 <see cref="Hooks"/> 实现 <see cref="ISettlementHooks"/>，把手牌扣减（第 1 步）、信物揭示（第 4 步）、
/// 控制与势力重算（第 5 步）、出局与终局检查（第 6 步）按 §6.3 顺序接到 <see cref="SettlementDriver"/>。</para>
/// <para><b>事件</b>：全部流程事件只由本类的 <see cref="Emit"/> 在阶段切换点发出。</para>
/// <para><b>线程模型</b>（裁决 1）：单线程游戏循环；表现层与 AI 只消费 <see cref="Publish"/> 的快照。</para>
/// </remarks>
public sealed partial class MatchFlow
{
    /// <summary>构筑保护期的大回合数（设计文档 §4.2：第 1–3 大回合）。</summary>
    public const int BuildProtectionRounds = 3;

    private readonly ImmutableArray<PlayerId> _players;
    private readonly SortedDictionary<PlayerId, PlayerRecord> _records = [];
    private readonly List<FlowEvent> _events = [];
    private readonly List<InitiativeReport> _initiative = [];
    private readonly List<ResignationSnapshot> _resignations = [];
    private readonly RandomStream _setup;
    private readonly SettlementDriver _driver;
    private readonly List<TerrainEditRecord> _terrainEdits = [];

    private ImmutableArray<PlayerId> _order = [];
    private int _orderIndex;
    private int _passStreak;
    private int _eliminationSequence;
    private EndReason? _pendingEnd;
    private StagedBatch? _batch;
    private EffectSnapshot? _snapshot;
    private int _eventSequence;
    private Func<EffectSnapshot, EffectSnapshot>? _snapshotTransform;

    private MatchFlow(
        MapData map, GameBoard board, GameSeed seed, ImmutableArray<PlayerId> players,
        RelicLedger relics, HandLedger hands, BoardHistory history, MatchOptions options)
    {
        Board = board;
        Seed = seed;
        Options = options;
        _players = players;
        Relics = relics;
        Hands = hands;
        History = history;
        Scoreboard = new PowerScoreboard();
        _setup = seed.Stream(GameSeed.Setup);
        _driver = new SettlementDriver(board, history, new Hooks(this));
        Flags = new FlagPlanting(map, players, options.FlagTimeLimit);
        foreach (PlayerId player in players)
        {
            _records.Add(player, new PlayerRecord());
        }

        Debug = new MatchDebugAccess(this);
    }

    // ---------- 构造 ----------

    /// <summary>
    /// 创建一局：地图经静态校验，信物按 <c>relic-gen</c> 子流一次性生成，手牌账本派生 <c>recruit</c> 子流，本类持有 <c>setup</c> 子流。
    /// 创建后处于插旗阶段。
    /// </summary>
    public static MatchFlow Create(MapData map, GameSeed seed, IEnumerable<PlayerId> players, MatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        GameBoard board = GameBoard.Load(map);
        return Build(map, board, seed, players, RelicGenerator.Generate(map, seed), options ?? MatchOptions.Default);
    }

    /// <summary>测试专用：跳过地图校验并使用手工指定的信物分布。</summary>
    internal static MatchFlow CreateUnvalidated(
        MapData map, GameSeed seed, IEnumerable<PlayerId> players, RelicGenerationRecord relics, MatchOptions? options = null) =>
        Build(map, GameBoard.LoadUnvalidated(map), seed, players, relics, options ?? MatchOptions.Immediate);

    private static MatchFlow Build(
        MapData map, GameBoard board, GameSeed seed, IEnumerable<PlayerId> players, RelicGenerationRecord relics, MatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(players);
        ImmutableArray<PlayerId> list = [.. players.Order()];
        if (list.Length < 2)
        {
            throw new ArgumentException("对局至少需要两名玩家。", nameof(players));
        }

        if (list.Distinct().Count() != list.Length)
        {
            throw new ArgumentException("玩家重复。", nameof(players));
        }

        if (list.Length > map.MaxPlayers)
        {
            throw new ArgumentException($"地图 {map.Id} 最多支持 {map.MaxPlayers} 人，实际 {list.Length} 人。", nameof(players));
        }

        RecruitWeights.RequireValidArtisanWeight(options.ArtisanWeight);
        return new MatchFlow(map, board, seed, list, new RelicLedger(relics), new HandLedger(list, seed, options.ArtisanWeight), new BoardHistory(), options);
    }

    // ---------- 组成部分 ----------

    /// <summary>
    /// 当前地图数据。<b>地形不是对局内的不变量</b>（artisan-terrain-edit）：改造经 <see cref="GameBoard.ApplyTerrainEdits"/> 换上新快照，
    /// 这里直接读盘面的那一份，MUST NOT 在建局时拷贝成字段——那样改造之后就读到旧地形了。
    /// 开局那份（不含改造）见 <see cref="GameBoard.BaseMap"/>。
    /// </summary>
    public MapData Map => Board.Map;

    /// <summary>
    /// 本局已完成的地形改造（含大回合、改造方与是否直接导致提子），按发生顺序。供日志与分析；
    /// 与事件日志、征募记录一样属于遥测，<b>不随存档往返</b>——地形本身经盘面序列化的改造段持久化。
    /// </summary>
    public IReadOnlyList<TerrainEditRecord> TerrainEdits => _terrainEdits;

    public GameSeed Seed { get; }

    /// <summary>对局配置。开局固定。</summary>
    public MatchOptions Options { get; private set; }

    /// <summary>匠人征募权重（artisan-terrain-edit R-2）。对局配置，始终公开，入存档。</summary>
    public int ArtisanWeight => Options.ArtisanWeight;

    /// <summary>恢复自不含匠人权重字段的旧存档时为 <c>true</c>：按 <see cref="MatchOptions.DefaultArtisanWeight"/> 回填。</summary>
    public bool ArtisanWeightBackfilled { get; private set; }

    /// <summary>
    /// 恢复自不含地图内容摘要的旧存档（map-generator 之前）时为 <c>true</c>：恢复时<b>跳过了</b>"地图不一致"的比对——
    /// 调用方据此可知这张地图只按标识把过关、内容没有核对过。
    /// </summary>
    public bool MapDigestBackfilled { get; private set; }

    /// <summary>权威盘面。规则层内部使用；表现层与 AI 请消费 <see cref="Publish"/>。</summary>
    public GameBoard Board { get; }

    public BoardHistory History { get; }

    public RelicLedger Relics { get; }

    public HandLedger Hands { get; }

    public PowerScoreboard Scoreboard { get; }

    public FlagPlanting Flags { get; }

    /// <summary>测试专用接缝。</summary>
    internal MatchDebugAccess Debug { get; }

    // ---------- 顶层状态 ----------

    public MatchPhase Phase { get; private set; } = MatchPhase.FlagPlanting;

    /// <summary>当前大回合序号，从 1 起；插旗阶段为 0。</summary>
    public int MajorRound { get; private set; }

    /// <summary>当前小回合阶段。</summary>
    public TurnStage Stage { get; private set; } = TurnStage.Idle;

    /// <summary>本大回合的行动顺序。</summary>
    public ImmutableArray<PlayerId> ActionOrder => _order;

    /// <summary>当前（或下一个）行动玩家；不在对局进行中时为 <c>null</c>。</summary>
    public PlayerId? CurrentPlayer => Phase == MatchPhase.InProgress && _orderIndex < _order.Length ? _order[_orderIndex] : null;

    /// <summary>连续 Pass 计数（design.md D2）：任一玩家确认 ≥1 枚落子即清零，达到当前参赛人数触发终局条件 2。</summary>
    public int PassStreak => _passStreak;

    /// <summary>本小回合的效果快照；不在小回合内为 <c>null</c>。小回合内不变。</summary>
    public EffectSnapshot? CurrentSnapshot => _snapshot;

    /// <summary>本小回合的暂放批次；只在部署阶段非空。</summary>
    public StagedBatch? CurrentBatch => _batch;

    public MatchResult? Result { get; private set; }

    public IReadOnlyList<FlowEvent> Events => _events;

    public IReadOnlyList<InitiativeReport> InitiativeReports => _initiative;

    public IReadOnlyList<ResignationSnapshot> Resignations => _resignations;

    public ImmutableArray<PlayerId> Players => _players;

    public int ActiveCount => _records.Values.Count(r => r.Status == PlayerStatus.Active);

    public ImmutableArray<PlayerId> ActivePlayers => [.. _records.Where(kv => kv.Value.Status == PlayerStatus.Active).Select(kv => kv.Key)];

    /// <summary>名册：每名玩家的参赛状态，作为显式输入交给计分与信物层。</summary>
    public IReadOnlyDictionary<PlayerId, PlayerStatus> Roster => _records.ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value.Status);

    public PlayerFlowState StateOf(PlayerId player)
    {
        PlayerRecord r = Require(player);
        return new PlayerFlowState(player, r.Status, r.HasEstablishedPower, r.BirthZone, r.LastRoundPosition,
            r.EliminationOrder, r.EliminatedInMajorRound, r.ResignedInMajorRound, r.PowerAtResign);
    }

    public ImmutableArray<PlayerFlowState> PlayerStates => [.. _players.Select(StateOf)];

    // ---------- 插旗 ----------

    /// <summary>锁定插旗结果并开始第 1 大回合：首回合顺序与种子兜底顺序均由 <c>setup</c> 子流决定。</summary>
    public void LockFlags()
    {
        RequirePhase(MatchPhase.FlagPlanting);
        ImmutableSortedDictionary<PlayerId, int> zones = Flags.IsLocked
            ? _players.ToImmutableSortedDictionary(p => p, p => Flags.FlagOf(p)!.Value)
            : Flags.LockAll();
        foreach ((PlayerId player, int zone) in zones)
        {
            Require(player).BirthZone = zone;
        }

        _order = Shuffle(_players);
        ImmutableArray<PlayerId> tiebreak = Shuffle(_players);
        for (int i = 0; i < tiebreak.Length; i++)
        {
            Require(tiebreak[i]).SeedRank = i;
        }

        _orderIndex = 0;
        MajorRound = 1;
        Phase = MatchPhase.InProgress;
        RecalculateDerived();
        Emit(FlowEventKind.FlagsLocked, null, $"出生区 {string.Join(" ", zones.Select(kv => $"{kv.Key}:{BirthZoneLabel.Number(kv.Value)}"))}；首回合顺序 {string.Join(">", _order)}");
    }

    /// <summary>原型替代路径：依次插旗并锁定，产出与同时插旗完全相同的对局状态。</summary>
    public void PlantSequentially(IEnumerable<(PlayerId Player, int Zone)> choices)
    {
        RequirePhase(MatchPhase.FlagPlanting);
        Flags.PlantSequentially(choices);
        LockFlags();
    }

    /// <summary>
    /// 原型替代路径的一站式入口（frontier-map D4）：<paramref name="manual"/> 是人工指定的那一名玩家及其区号，其余玩家的区由
    /// <see cref="PrototypeZoneAssignment"/> 给出（区数不多于地图人数上限时按编号顺排，否则由种子的独立子流均匀选区），随后依次插旗并锁定。
    /// 批量跑局、终端版与图形版都走这里；返回每名玩家的选择（顺序同 <see cref="Players"/>）。
    /// </summary>
    public ImmutableArray<(PlayerId Player, int Zone)> PlantPrototype((PlayerId Player, int Zone)? manual = null)
    {
        RequirePhase(MatchPhase.FlagPlanting);
        ImmutableArray<(PlayerId Player, int Zone)> choices = PrototypeZoneAssignment.Assign(Board.BaseMap, Seed, _players, manual);
        PlantSequentially(choices);
        return choices;
    }

    // ---------- 小回合阶段机 ----------

    /// <summary>
    /// 开始当前玩家的小回合：第 1 阶段信物快照（三次跨层调用集中在此：持有类型数 → 效果快照 → 手牌账本开始），随后停在整理手牌阶段。
    /// </summary>
    public void BeginTurn()
    {
        RequirePhase(MatchPhase.InProgress);
        RequireStage(TurnStage.Idle);
        EnsureCurrentActive();
        if (Phase != MatchPhase.InProgress)
        {
            return;
        }

        PlayerId player = _order[_orderIndex];
        Emit(FlowEventKind.TurnStarted, player, $"第 {_orderIndex + 1} 位");
        SetStage(TurnStage.RelicSnapshot, player);

        int held = Hands.HeldTypeCount(player);
        // restore-go-core-rules D7：快照只读信物与当前大回合，MUST NOT 读取势力名次。
        EffectSnapshot snapshot = Relics.SnapshotFor(player, Board, Roster, held, MajorRound);
        if (_snapshotTransform is { } transform)
        {
            snapshot = transform(snapshot);
        }

        Hands.BeginTurn(player, snapshot);
        _snapshot = snapshot;

        SetStage(TurnStage.OrganizeHand, player);
    }

    /// <summary>当前行动玩家的私有手牌句柄（整理手牌 / 征募阶段的弃牌、选取操作）。只交给该玩家的控制者。</summary>
    public PlayerHandAccess CurrentHand()
    {
        RequireInTurn();
        return Hands.AccessFor(CurrentPlayer!.Value);
    }

    /// <summary>第 2 → 第 3 阶段：结束整理手牌，进入私人征募。超限未解除时拒绝并停在整理手牌阶段（强制弃牌门）。</summary>
    public RecruitPanelView EnterRecruit()
    {
        RequireStage(TurnStage.OrganizeHand);
        PlayerId player = CurrentPlayer!.Value;
        RecruitPanelView panel = Hands.AccessFor(player).EnterRecruit();
        SetStage(TurnStage.Recruit, player);
        return panel;
    }

    /// <summary>第 3 → 第 4 阶段：结束征募，按快照的部署上限、本层下发的合法落子范围与此刻的手牌库存建立暂放批次。</summary>
    public StagedBatch EnterDeploy()
    {
        RequireStage(TurnStage.Recruit);
        PlayerId player = CurrentPlayer!.Value;
        HandPrivateView hand = Hands.AccessFor(player).PrivateView();
        var context = new BatchContext
        {
            Player = player,
            DeployLimit = _snapshot!.DeployLimit,
            LegalRange = LegalRangeFor(player),
            Stock = hand.Entries.ToDictionary(kv => kv.Key, kv => kv.Value.Total),
        };
        _batch = new StagedBatch(Board, context);
        SetStage(TurnStage.Deploy, player);
        return _batch;
    }

    /// <summary>对当前暂放批次做零副作用的合法性预演。</summary>
    public RehearsalResult Rehearse()
    {
        RequireStage(TurnStage.Deploy);
        return _driver.Rehearse(_batch!.Context, _batch.Placements);
    }

    /// <summary>
    /// 第 5 阶段：确认当前暂放批次（0 枚即 Pass）。被拒绝时停留在部署阶段、暂放保留；接受后完成结算、结束小回合并推进到下一位。
    /// </summary>
    public SettlementOutcome Confirm()
    {
        RequireStage(TurnStage.Deploy);
        PlayerId player = CurrentPlayer!.Value;
        int majorRound = MajorRound;
        SettlementOutcome outcome = _driver.Confirm(_batch!);
        if (!outcome.Confirmed)
        {
            return outcome;
        }

        // 改造留痕（match-telemetry 第 4 条）：大回合取结算前的值——第 7 步可能已经推进了大回合。
        foreach (AppliedTerrainEdit applied in outcome.CaptureRecord?.Edits ?? [])
        {
            _terrainEdits.Add(new TerrainEditRecord(
                majorRound, _terrainEdits.Count + 1, player, applied.Edit, applied.ArtisanCoord, applied.CausedCapture));
        }

        SetStage(TurnStage.Settlement, player);
        CompleteTurn(player);
        return outcome;
    }

    /// <summary>Pass：清空暂放并确认 0 落子。</summary>
    public SettlementOutcome Pass()
    {
        RequireStage(TurnStage.Deploy);
        _batch!.Clear();
        return Confirm();
    }

    /// <summary>
    /// 对 <c>add-batch-deployment</c> 的契约（design.md D5）：第 1–3 大回合为该玩家锁定的出生区格集合（同区玩家自然共享），
    /// 从第 4 大回合起为全图可落子格集合。出生区数据本身不带限制，限制只由本方法按当前大回合序号决定。
    /// </summary>
    public IReadOnlySet<Coord> LegalRangeFor(PlayerId player)
    {
        RequirePhase(MatchPhase.InProgress);
        int zone = Require(player).BirthZone ?? throw new SiegeRuleException($"玩家 {player} 尚未锁定出生区。");

        // 可落子格按当前地形现算：本局架出来的桥必须立刻成为合法落点。
        // 这里曾是建局时算好的缓存字段，地形可变之后它是过期数据（2.6 缓存排查第 1 条）。
        return MajorRound <= BuildProtectionRounds
            ? Map.BirthZones[zone]
            : Board.AllCoords().Where(c => Board[c].Terrain == Terrain.Playable).ToImmutableHashSet();
    }

    // ---------- 弃赛 ----------

    /// <summary>
    /// 主动弃赛（设计文档 §12.2）。保护期内同样允许（裁决 2）。当前行动玩家可在任意阶段弃赛；其他玩家须在小回合边界弃赛。
    /// 弃赛时记录完整快照（裁决 5），遗留棋子照常参与覆盖与势力，先锋不再提供修正。
    /// </summary>
    public void Resign(PlayerId player)
    {
        RequirePhase(MatchPhase.InProgress);
        PlayerRecord record = Require(player);
        if (record.Status != PlayerStatus.Active)
        {
            throw new SiegeRuleException($"玩家 {player} 状态为 {record.Status}，不能弃赛。");
        }

        bool inOwnTurn = Stage != TurnStage.Idle && CurrentPlayer == player;
        if (Stage != TurnStage.Idle && !inOwnTurn)
        {
            throw new SiegeRuleException($"玩家 {CurrentPlayer} 的小回合进行中，{player} 请在小回合边界弃赛。");
        }

        // 先记快照（手牌两段账在弃赛撤销前的原样），再改状态。
        HandPrivateView hand = Hands.AccessFor(player).PrivateView();
        EffectSnapshot effects = inOwnTurn && _snapshot is not null
            ? _snapshot
            : Relics.SnapshotFor(player, Board, Roster, Hands.HeldTypeCount(player), MajorRound);
        ImmutableArray<Coord> controlled = [.. Relics.PublicStates().Where(s => s.Control.GrantsEffectTo(player)).Select(s => s.Coord)];
        BigInteger power = Scoreboard.Latest?.Of(player).Total ?? BigInteger.Zero;
        _resignations.Add(new ResignationSnapshot(player, MajorRound, Board.Serialize(), hand, effects, controlled, power));

        if (inOwnTurn)
        {
            _batch = null;
            _snapshot = null;
        }

        Hands.Resign(player);
        record.Status = PlayerStatus.Resigned;
        record.ResignedInMajorRound = MajorRound;
        record.PowerAtResign = power;
        Emit(FlowEventKind.PlayerResigned, player, $"弃赛时势力 {power}");
        RecalculateDerived();

        if (inOwnTurn)
        {
            Emit(FlowEventKind.TurnEnded, player, "弃赛结束小回合");
            SetStage(TurnStage.Idle, player);
        }

        CheckEndConditions();
        if (_pendingEnd is { } reason)
        {
            Finish(reason);
        }
        else if (inOwnTurn)
        {
            Advance();
        }
        else
        {
            EnsureCurrentActive();
        }
    }

    // ---------- 公开快照 ----------

    /// <summary>发布公开快照（裁决 1）。</summary>
    public MatchPublicView Publish()
    {
        PowerSnapshot? power = Scoreboard.Latest;
        // 地图标识取开局地图的（改造不改标识，二者恒等；写 BaseMap 是为了把"这是哪张图"与活地形分开）。
        return new(Board.BaseMap.Id, Seed.ToString(), Phase, MajorRound, ArtisanWeight, Stage, CurrentPlayer, _order, PlayerStates, Board.Clone(),
            Board.Serialize(), power, Relics.PublicStates(), Hands.PublicViews(), _passStreak, Result);
    }

    // ---------- 结算钩子（§6.3 顺序由 SettlementDriver 驱动） ----------

    private void OnDeductHand(PlayerId player, IReadOnlyDictionary<PieceType, int> deployed) => Hands.DeductHand(player, deployed);

    private void OnPass(PlayerId player) => Hands.OnPass(player);

    private void OnRevealRelics(SettlementContext context) => Relics.Reveal(context.Board, MajorRound);

    private void OnRecalculatePower(SettlementContext context)
    {
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = Roster;
        Relics.RecalculateControl(context.Board, roster);
        MarkEstablishedPower(Scoreboard.Recalculate(context.Board, roster, MajorRound));
    }

    private void OnCheckEndConditions(SettlementContext context)
    {
        // D2：连续 Pass 计数——落子 ≥1 枚即清零。
        _passStreak = context.IsPass ? _passStreak + 1 : 0;
        CheckEliminations();
        CheckEndConditions();
    }

    // ---------- 出局与终局 ----------

    /// <summary>
    /// 「曾建立正势力」单调标记（restore-go-core-rules 裁决 #3 / design D3）的<b>唯一</b>置位点：
    /// 任意一次势力重算后总势力 &gt; 0 即置位，此后 MUST NOT 复位（故用 <c>|=</c>，不是 <c>=</c>）。
    /// 挂在每一处 <see cref="PowerScoreboard.Recalculate"/> 之后——结算第 5 步、弃赛 / 大回合结束 / 恢复存档时的派生量重算都经过这里。
    /// </summary>
    private void MarkEstablishedPower(PowerSnapshot power)
    {
        foreach (PlayerId player in _players)
        {
            _records[player].HasEstablishedPower |= power.Of(player).Total > BigInteger.Zero;
        }
    }

    /// <summary>
    /// 出局检查（elimination-endgame「出局判定」）：每次合法批次结算后与每次 Pass 后，对<b>全部</b>参赛玩家（不只行动者）检查
    /// 「曾建立正势力且当前总势力为 0」。手牌是否还有棋子 MUST NOT 影响判定，构筑保护期 MUST NOT 豁免。
    /// </summary>
    /// <remarks>
    /// 同一次检查里同时满足条件的多名玩家<b>共享同一出局序号</b>（裁决：同时出局、出局次序相同），
    /// 终局名次据此让他们并列（<see cref="FinalStandings"/> 出局组）。序号因此每次检查至多自增一次。
    /// </remarks>
    private void CheckEliminations()
    {
        PowerSnapshot? power = Scoreboard.Latest;
        if (power is null)
        {
            return;
        }

        List<PlayerId> zeroed = [.. _players.Where(p =>
            _records[p].Status == PlayerStatus.Active
            && _records[p].HasEstablishedPower
            && power.Of(p).Total == BigInteger.Zero)];
        if (zeroed.Count == 0)
        {
            return;
        }

        int order = ++_eliminationSequence;
        foreach (PlayerId player in zeroed)
        {
            PlayerRecord record = _records[player];
            record.Status = PlayerStatus.Eliminated;
            record.EliminationOrder = order;
            record.EliminatedInMajorRound = MajorRound;
            Emit(FlowEventKind.PlayerEliminated, player, $"第 {order} 个出局（势力归零）");
        }
    }

    /// <summary>
    /// 三类终局条件（elimination-endgame「三类终局条件」）。只记录待处理的终局原因；收尾在当前小回合结束时进行。
    /// </summary>
    /// <remarks>
    /// 分支顺序即优先级：只剩一名参赛玩家 &gt; 棋盘填满 &gt; 整轮 Pass。
    /// 本方法 MUST NOT 再增加分支：既不按轮数结束，也不因势力领先幅度提前结束。
    /// </remarks>
    private void CheckEndConditions()
    {
        if (_pendingEnd is not null || Phase != MatchPhase.InProgress)
        {
            return;
        }

        int active = ActiveCount;
        if (active <= 1)
        {
            _pendingEnd = EndReason.LastPlayerStanding;
        }
        else if (!Board.HasPlayableEmptyCell())
        {
            _pendingEnd = EndReason.BoardFull;
        }
        else if (_passStreak >= active)
        {
            _pendingEnd = EndReason.AllPassed;
        }
    }

    private void Finish(EndReason reason)
    {
        RecalculateDerived();
        PowerSnapshot power = Scoreboard.Latest!;
        ImmutableArray<RelicPublicState> relics = Relics.PublicStates();
        var inputs = new List<StandingInput>(_players.Length);
        foreach (PlayerId player in _players)
        {
            PlayerRecord record = _records[player];
            PlayerPower detail = power.Of(player);
            inputs.Add(new StandingInput(
                player,
                record.Status,
                record.Status == PlayerStatus.Resigned ? record.PowerAtResign!.Value : detail.Total,
                relics.Count(s => s.Control.GrantsEffectTo(player)),
                detail.ExclusiveCells.Length,
                Board.GroupsOf(player).Sum(g => g.Size),
                record.EliminationOrder));
        }

        Result = new MatchResult(reason, MajorRound, FinalStandings.Compute(inputs));
        Phase = MatchPhase.Ended;
        _pendingEnd = null;
        Emit(FlowEventKind.MatchEnded, null, $"{reason}；名次 {string.Join(" ", Result.Standings.Select(s => $"{s.Rank}.{s.Player}"))}");
    }

    // ---------- 推进 ----------

    private void CompleteTurn(PlayerId player)
    {
        Hands.EndTurn(player);
        _batch = null;
        _snapshot = null;
        Emit(FlowEventKind.TurnEnded, player, _passStreak > 0 ? "Pass" : "落子");
        SetStage(TurnStage.Idle, player);

        if (_pendingEnd is { } reason)
        {
            Finish(reason);
        }
        else
        {
            Advance();
        }
    }

    private void Advance()
    {
        _orderIndex++;
        EnsureCurrentActive();
    }

    /// <summary>把指针推进到下一名参赛中的玩家；本大回合已无人可行动则结束大回合并生成下一轮顺序。</summary>
    private void EnsureCurrentActive()
    {
        while (Phase == MatchPhase.InProgress)
        {
            while (_orderIndex < _order.Length && _records[_order[_orderIndex]].Status != PlayerStatus.Active)
            {
                _orderIndex++;
            }

            if (_orderIndex < _order.Length)
            {
                return;
            }

            EndMajorRound();
        }
    }

    /// <summary>大回合结束：单次拉取先手修正（不缓存），按势力名次与先手值公式生成下一大回合顺序（设计文档 §11）。</summary>
    private void EndMajorRound()
    {
        int completed = MajorRound;
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = Roster;
        PowerSnapshot power = Scoreboard.Recalculate(Board, roster, completed);
        MarkEstablishedPower(power);
        ImmutableSortedDictionary<PlayerId, int> bonuses = Relics.ReadInitiativeBonuses(Board, roster);
        int active = ActiveCount;
        if (active == 0)
        {
            _pendingEnd = EndReason.LastPlayerStanding;
            Finish(EndReason.LastPlayerStanding);
            return;
        }

        // restore-go-core-rules 裁决 #4：大回合结束 MUST NOT 成为终局条件（大回合上限终局已删除），对局只因三类条件结束。

        ImmutableArray<InitiativeEntry>.Builder entries = ImmutableArray.CreateBuilder<InitiativeEntry>(active);
        foreach (PlayerId player in _players)
        {
            PlayerRecord record = _records[player];
            if (record.Status != PlayerStatus.Active)
            {
                continue;
            }

            int rank = power.RankOf(player) ?? throw new SiegeRuleException($"参赛玩家 {player} 没有势力名次。");
            int bonus = bonuses.TryGetValue(player, out int b) ? b : 0;
            int? previous = completed >= 2 ? _order.IndexOf(player) : null;
            entries.Add(new InitiativeEntry(player, rank, power.Of(player).Total, bonus,
                InitiativeOrder.ValueOf(active, rank, bonus), previous, record.SeedRank));
        }

        InitiativeReport report = InitiativeOrder.Generate(completed, entries.MoveToImmutable());
        _initiative.Add(report);
        foreach (PlayerId player in _players)
        {
            int index = _order.IndexOf(player);
            _records[player].LastRoundPosition = completed >= 2 && index >= 0 ? index : null;
        }

        _order = report.NextOrder;
        _orderIndex = 0;
        MajorRound = completed + 1;
        Emit(FlowEventKind.MajorRoundEnded, null,
            $"第 {completed} 大回合结束；先手值 {string.Join(" ", report.Entries.Select(e => $"{e.Player}={e.Value}(名次{e.Rank},修正{e.Bonus})"))}；下一轮 {string.Join(">", report.NextOrder)}");
    }

    // ---------- 内部 ----------

    private void RecalculateDerived()
    {
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = Roster;
        Relics.RecalculateControl(Board, roster);
        MarkEstablishedPower(Scoreboard.Recalculate(Board, roster, Math.Max(MajorRound, 1)));
    }

    /// <summary>流程事件的<b>唯一</b>发出点。</summary>
    private void Emit(FlowEventKind kind, PlayerId? player, string detail) =>
        _events.Add(new FlowEvent(++_eventSequence, kind, MajorRound, player, detail));

    private void SetStage(TurnStage stage, PlayerId player)
    {
        Stage = stage;
        Emit(FlowEventKind.StageEntered, player, stage.ToString());
    }

    private ImmutableArray<PlayerId> Shuffle(ImmutableArray<PlayerId> players)
    {
        PlayerId[] list = [.. players];
        for (int i = list.Length - 1; i > 0; i--)
        {
            int j = _setup.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return [.. list];
    }

    private PlayerRecord Require(PlayerId player) =>
        _records.TryGetValue(player, out PlayerRecord? record) ? record : throw new SiegeRuleException($"玩家 {player} 不在本局名单中。");

    private void RequirePhase(MatchPhase expected)
    {
        if (Phase != expected)
        {
            throw new SiegeRuleException($"对局阶段为 {Phase}，该操作须在 {expected} 阶段。");
        }
    }

    private void RequireStage(TurnStage expected)
    {
        RequirePhase(MatchPhase.InProgress);
        if (Stage != expected)
        {
            throw new SiegeRuleException($"当前小回合阶段为 {Stage}，该操作须在 {expected} 阶段；阶段不可跳过或调换。");
        }
    }

    private void RequireInTurn()
    {
        RequirePhase(MatchPhase.InProgress);
        if (Stage == TurnStage.Idle)
        {
            throw new SiegeRuleException("当前不在任何小回合内。");
        }
    }

    // ---------- 测试接缝 ----------

    internal void DebugSetMajorRound(int majorRound)
    {
        RequireStage(TurnStage.Idle);
        MajorRound = majorRound;
    }

    internal void DebugSetOrder(PlayerId[] order)
    {
        RequireStage(TurnStage.Idle);
        _order = [.. order];
        _orderIndex = 0;
    }

    internal void DebugSetPassStreak(int streak) => _passStreak = streak;

    internal void DebugRecalculate() => RecalculateDerived();

    /// <summary>
    /// 测试专用：改写小回合开始时生成的效果快照。棋子类型增至六种（artisan-terrain-edit）后，五槽装不下全部六种，
    /// 但靠信物只会把槽位<b>加多</b>；要造出"类型数超过槽位"仍需缩水的快照（兵站丢失路径除外），生成路径本身不变。
    /// </summary>
    internal void DebugSetSnapshotTransform(Func<EffectSnapshot, EffectSnapshot>? transform) => _snapshotTransform = transform;

    private sealed class PlayerRecord
    {
        internal PlayerStatus Status { get; set; } = PlayerStatus.Active;

        /// <summary>「曾建立正势力」单调标记（design D3）：只经 <see cref="MarkEstablishedPower"/> 置位，永不复位，随存档往返。</summary>
        internal bool HasEstablishedPower { get; set; }

        internal int? BirthZone { get; set; }

        internal int? LastRoundPosition { get; set; }

        internal int SeedRank { get; set; }

        internal int? EliminationOrder { get; set; }

        internal int? EliminatedInMajorRound { get; set; }

        internal int? ResignedInMajorRound { get; set; }

        internal BigInteger? PowerAtResign { get; set; }
    }

    /// <summary>复合结算钩子：把 §6.3 各步转发到对应的层。本类不自行计算任何一步。</summary>
    private sealed class Hooks(MatchFlow match) : ISettlementHooks
    {
        public void DeductHand(PlayerId player, IReadOnlyDictionary<PieceType, int> deployed) => match.OnDeductHand(player, deployed);

        public void OnRevealRelics(SettlementContext context) => match.OnRevealRelics(context);

        public void OnRecalculatePower(SettlementContext context) => match.OnRecalculatePower(context);

        public void OnCheckEndConditions(SettlementContext context) => match.OnCheckEndConditions(context);

        public void OnPass(PlayerId player) => match.OnPass(player);
    }
}
