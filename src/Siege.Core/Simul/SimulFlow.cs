using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Core.Simul;

/// <summary>
/// 新玩法（同时决策 + 同格冲突 + 全灭不判负 + 占满终局，simul-a）的<b>唯一</b>阶段机：插旗 → 进行中（准备 / 结算）→ 已结束。
/// 与现有轮流决策的 <see cref="MatchFlow"/> 并列，复用方式只有调用（盘面、提子判定、活形、势力、名次、插旗、手牌账本），不改它们的任何现有路径。
/// </summary>
/// <remarks>
/// <para>一回合（design J2）：回合开始固定结算顺位、记下 S0、按顺位逐人开准备并一次预抽征召候选（K5）→ 全员各自准备并提交 →
/// <see cref="ResolveRound"/>：揭晓、扣牌、同格冲突（d6 子流 <see cref="SimulRules.DiceStream"/>）、整批落下与改造、按顺位围杀、孤立自绝、活形锁定与势力、终局判定 → 下一回合。</para>
/// <para>没有任何淘汰：全灭的玩家照常参赛；认输者单位留在场上、排名固定末位。只支持链接小图（K4），不做存档续局；地图信物关闭（K1）。</para>
/// <para>规格：openspec/specs/simultaneous-round。</para>
/// </remarks>
public sealed class SimulFlow
{
    private readonly ImmutableArray<PlayerId> _players;
    private readonly RandomStream _setup;
    private readonly RandomStream _dice;
    private readonly Dictionary<PlayerId, Seat> _seats = [];
    private readonly List<SimulRoundReport> _reports = [];
    private ImmutableArray<PlayerId> _order = [];
    private GameBoard _s0;
    private LifeShapeReport _s0Life;

    private SimulFlow(MapData map, GameBoard board, GameSeed seed, ImmutableArray<PlayerId> players, SimulOptions options)
    {
        Board = board;
        Seed = seed;
        Options = options;
        _players = players;
        Hands = new HandLedger(players, seed, options.ArtisanWeight, options.ContentSet);
        Scoreboard = new PowerScoreboard();
        Flags = new FlagPlanting(map, players, TimeSpan.Zero, seed);
        _setup = seed.Stream(GameSeed.Setup);
        _dice = seed.Stream(SimulRules.DiceStream);
        foreach (PlayerId p in players)
        {
            _seats.Add(p, new Seat());
        }

        _s0 = board.Clone();
        _s0Life = LifeShapeReport.Analyze(_s0);
        Debug = new SimulDebugAccess(this);
    }

    // ---------- 构造 ----------

    /// <summary>建一局新玩法：地图经静态校验；只接受链接小图，对局人数恰等于地图人数（2–4）。创建后处于插旗阶段。</summary>
    public static SimulFlow Create(MapData map, GameSeed seed, IEnumerable<PlayerId> players, SimulOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Build(map, GameBoard.Load(map), seed, players, options ?? SimulOptions.Default);
    }

    /// <summary>测试专用：跳过地图校验（建局约束照常）。</summary>
    internal static SimulFlow CreateUnvalidated(MapData map, GameSeed seed, IEnumerable<PlayerId> players, SimulOptions? options = null) =>
        Build(map, GameBoard.LoadUnvalidated(map), seed, players, options ?? SimulOptions.Default);

    private static SimulFlow Build(MapData map, GameBoard board, GameSeed seed, IEnumerable<PlayerId> players, SimulOptions options)
    {
        ArgumentNullException.ThrowIfNull(players);
        ImmutableArray<PlayerId> list = [.. players.Order()];
        if (list.Distinct().Count() != list.Length)
        {
            throw new ArgumentException("玩家重复。", nameof(players));
        }

        if (list.Length < LinkMapId.MinPlayers || list.Length > LinkMapId.MaxPlayers)
        {
            throw new ArgumentException($"新玩法只支持 {LinkMapId.MinPlayers}–{LinkMapId.MaxPlayers} 人局，实际 {list.Length} 人。", nameof(players));
        }

        if (!LinkMapId.IsLinkMap(map.Id))
        {
            throw new ArgumentException($"新玩法只能配链接图（link:<地图种子>:<人数>），地图 {map.Id} 不是。", nameof(map));
        }

        if (map.Tier != MapTier.Small)
        {
            throw new ArgumentException($"新玩法只开小图（K4），地图 {map.Id} 是大图。", nameof(map));
        }

        if (list.Length != map.MaxPlayers)
        {
            throw new ArgumentException($"链接图 {map.Id} 是 {map.MaxPlayers} 人局，实际 {list.Length} 人。", nameof(players));
        }

        RecruitWeights.RequireValidArtisanWeight(options.ArtisanWeight);
        ContentSets.RequireValid(options.ContentSet);
        ScoringVersions.RequireValid(options.ScoringVersion);
        options = options with { RoundCap = options.RoundCap ?? MatchOptions.DefaultRoundCapFor(MapTier.Small) };
        if (options.RoundCap < 0)
        {
            throw new ArgumentException($"大回合上限须为非负整数（0 = 不启用），实际为 {options.RoundCap}。", nameof(options));
        }

        return new SimulFlow(map, board, seed, list, options);
    }

    // ---------- 组成部分与状态 ----------

    /// <summary>正式盘面（权威）。界面与 AI 请读 <see cref="PublicView"/>。</summary>
    public GameBoard Board { get; }

    /// <summary>当前地图（地形可被改造，直接读盘面那一份）。</summary>
    public MapData Map => Board.Map;

    public GameSeed Seed { get; }

    /// <summary>对局配置；<see cref="SimulOptions.RoundCap"/> 建局时已解析成整数。</summary>
    public SimulOptions Options { get; }

    /// <summary>大回合上限（0 = 不启用）。</summary>
    public int RoundCap => Options.RoundCap ?? MatchOptions.DefaultRoundCap;

    /// <summary>
    /// 手牌账本（征召与手牌两段账，沿用现有实现）。<c>internal</c>：玩家只经 <see cref="SimulPrep"/> 操作手牌——
    /// 直接拿账本的 <see cref="HandLedger.AccessFor"/> 会绕过提交锁定，并以现有的"打开面板时才抽"改写预抽好的候选（K5）。
    /// </summary>
    internal HandLedger Hands { get; }

    /// <summary>势力榜（现有实现，按对局计分版本全量重算；新玩法的修正见 <see cref="Power"/>）。</summary>
    public PowerScoreboard Scoreboard { get; }

    /// <summary>插旗（沿用现有实现）。</summary>
    public FlagPlanting Flags { get; }

    /// <summary>测试专用接缝。</summary>
    internal SimulDebugAccess Debug { get; }

    public SimulPhase Phase { get; private set; } = SimulPhase.FlagPlanting;

    public SimulStage Stage { get; private set; } = SimulStage.Prep;

    /// <summary>当前回合（大回合）序号，从 1 起；插旗阶段为 0。</summary>
    public int Round { get; private set; }

    /// <summary>全部玩家，按编号升序。</summary>
    public ImmutableArray<PlayerId> Players => _players;

    /// <summary>本回合的结算顺位（只含参赛中玩家）。</summary>
    public ImmutableArray<PlayerId> Order => _order;

    /// <summary>回合开始盘面 S0 的副本（本回合的校验与战力只读它）。</summary>
    public GameBoard RoundStartBoard => _s0.Clone();

    /// <summary>最近一次的新玩法势力；插旗阶段为 <c>null</c>。</summary>
    public SimulPowerSnapshot? Power { get; private set; }

    /// <summary>已结算回合的报告，按回合先后。</summary>
    public IReadOnlyList<SimulRoundReport> Reports => _reports;

    public SimulResult? Result { get; private set; }

    /// <summary><see cref="SimulRules.DiceStream"/> 子流已消费的次数（确定性守门用）。</summary>
    public long DiceConsumed => _dice.Consumed;

    /// <summary>名册：每名玩家的参赛状态（参赛中 / 已认输）。</summary>
    public IReadOnlyDictionary<PlayerId, PlayerStatus> Roster => _seats.ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value.Status);

    /// <summary>参赛中（未认输）的玩家，按编号升序。</summary>
    public ImmutableArray<PlayerId> ActivePlayers => [.. _players.Where(p => _seats[p].Status == PlayerStatus.Active)];

    public PlayerStatus StatusOf(PlayerId player) => Require(player).Status;

    /// <summary>锁定的出生区（出生座位）编号；未锁定为 <c>null</c>。</summary>
    public int? BirthZoneOf(PlayerId player) => Require(player).BirthZone;

    /// <summary>某玩家本回合是否已提交。</summary>
    public bool IsSubmitted(PlayerId player) => Require(player).Submitted;

    /// <summary>全部参赛中玩家都已提交（认输者不计）。</summary>
    public bool AllSubmitted => Phase == SimulPhase.InProgress && ActivePlayers.All(p => _seats[p].Submitted);

    // ---------- 插旗 ----------

    /// <summary>原型插旗：<paramref name="manual"/> 是人工指定的那一名玩家及其出生座位，其余玩家经 <see cref="PrototypeZoneAssignment.AssignBlind"/> 盲选（flag-pick 子流），随后锁定并开始第 1 回合。</summary>
    public ImmutableArray<(PlayerId Player, int Zone)> PlantPrototype((PlayerId Player, int Zone)? manual = null)
    {
        RequirePhase(SimulPhase.FlagPlanting);
        ImmutableArray<(PlayerId Player, int Zone)> choices = PrototypeZoneAssignment.AssignBlind(Board.BaseMap, Seed, _players, manual);
        PlantSequentially(choices);
        return choices;
    }

    /// <summary>依次插旗并锁定，开始第 1 回合。</summary>
    public void PlantSequentially(IEnumerable<(PlayerId Player, int Zone)> choices)
    {
        RequirePhase(SimulPhase.FlagPlanting);
        Flags.PlantSequentially(choices);
        LockFlags();
    }

    /// <summary>锁定插旗结果并开始第 1 回合：首回合顺位与种子兜底顺序均由 <c>setup</c> 子流洗出（与现有 <see cref="MatchFlow.LockFlags"/> 同一做法）。</summary>
    public void LockFlags()
    {
        RequirePhase(SimulPhase.FlagPlanting);
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

        Phase = SimulPhase.InProgress;
        Recalculate();
        StartRound(_order);
    }

    // ---------- 准备 ----------

    /// <summary>某名参赛玩家本回合的私有准备句柄（整理手牌、征召、暂放、预检、提交、撤回）。只交给该玩家的控制者。</summary>
    public SimulPrep PrepFor(PlayerId player)
    {
        RequirePhase(SimulPhase.InProgress);
        Seat seat = Require(player);
        if (seat.Status != PlayerStatus.Active)
        {
            throw new SiegeRuleException($"玩家 {player} 已认输，没有准备阶段。");
        }

        return new SimulPrep(this, player);
    }

    internal Seat SeatOf(PlayerId player) => Require(player);

    /// <summary>按 S0 与本人此刻的暂放、手牌校验（J2-3）。</summary>
    internal SimulBatchCheck Check(PlayerId player)
    {
        RequirePrep(player);
        Seat seat = _seats[player];
        HandPrivateView hand = Hands.PrivateViewOf(player);
        Dictionary<PieceType, int> stock = hand.Entries.ToDictionary(kv => kv.Key, kv => kv.Value.Total);
        return SimulRules.Validate(_s0, _s0Life, player, seat.BirthZone!.Value, Round, stock, seat.Staged);
    }

    internal void RequirePrep(PlayerId player)
    {
        RequirePhase(SimulPhase.InProgress);
        if (Stage != SimulStage.Prep)
        {
            throw new SiegeRuleException("本回合正在结算，不能操作准备阶段。");
        }

        if (Require(player).Status != PlayerStatus.Active)
        {
            throw new SiegeRuleException($"玩家 {player} 已认输。");
        }
    }

    // ---------- 认输 ----------

    /// <summary>
    /// 认输（Q11）：任意时刻都可以。本回合未揭晓的暂放直接作废、不扣牌（手牌账本按现有弃赛语义结束本小回合）；单位留在场上，可以被围杀；
    /// 不再有顺位、不再征召；终局排名固定末位。认输后参赛中玩家不超过 1 人即直接终局。
    /// </summary>
    public void Resign(PlayerId player)
    {
        RequirePhase(SimulPhase.InProgress);
        Seat seat = Require(player);
        if (seat.Status != PlayerStatus.Active)
        {
            throw new SiegeRuleException($"玩家 {player} 已认输，不能重复认输。");
        }

        seat.PowerAtResign = Power?.Of(player).Total ?? BigInteger.Zero;
        seat.ResignedInRound = Round;
        Hands.Resign(player);
        seat.Status = PlayerStatus.Resigned;
        seat.Staged.Clear();
        seat.Submitted = false;
        _order = [.. _order.Where(p => p != player)];
        Recalculate();

        if (ActivePlayers.Length <= 1)
        {
            foreach (PlayerId p in ActivePlayers)
            {
                CloseHandTurn(p, deployed: null);
            }

            Finish(SimulEndReason.LastPlayerStanding);
        }
    }

    // ---------- 结算 ----------

    /// <summary>
    /// 全员提交后统一揭晓并结算本回合（J2 第 4–10 步），返回本回合报告；未终局即开始下一回合（新顺位、新 S0、新一轮准备）。
    /// </summary>
    public SimulRoundReport ResolveRound()
    {
        RequirePhase(SimulPhase.InProgress);
        if (Stage != SimulStage.Prep)
        {
            throw new SiegeRuleException("本回合已在结算。");
        }

        ImmutableArray<PlayerId> pending = [.. ActivePlayers.Where(p => !_seats[p].Submitted)];
        if (!pending.IsEmpty)
        {
            throw new SiegeRuleException($"还有玩家未提交：{string.Join("、", pending)}。");
        }

        Stage = SimulStage.Resolving;
        ImmutableArray<PlayerId> order = _order;

        // 4. 揭晓：按顺位公开全部批次；提交的单位一律从手牌扣除（冲突落败的也扣）。
        // 顺位外的参赛玩家只可能来自测试接缝改写的顺位，排在最后揭晓、不执行围杀步骤。
        ImmutableArray<PlayerId> reveal = [.. order, .. ActivePlayers.Where(p => !order.Contains(p))];
        ImmutableArray<SimulRevealedBatch> batches = [.. reveal.Select(p => new SimulRevealedBatch(p, [.. _seats[p].Staged]))];
        foreach (SimulRevealedBatch batch in batches)
        {
            CloseHandTurn(batch.Player, batch.Placements);
        }

        // 5. 冲突：只读 S0。
        ImmutableArray<SimulConflict> conflicts = SimulRules.ResolveConflicts(_s0, batches, _dice);
        Dictionary<Coord, PlayerId> winnerAt = conflicts.ToDictionary(c => c.Cell, c => c.Winner);

        // 6. 落下：存活的部署同时放上正式盘面，再应用存活的改造（结算顺位靠前者优先，同目标的后者作废）。
        var landed = ImmutableArray.CreateBuilder<SimulLanded>();
        var editOutcomes = ImmutableArray.CreateBuilder<SimulEditOutcome>();
        var applied = new List<TerrainEdit>();
        foreach (SimulRevealedBatch batch in batches)
        {
            foreach (Placement p in batch.Placements)
            {
                bool survives = !winnerAt.TryGetValue(p.Coord, out PlayerId winner) || winner == batch.Player;
                if (survives)
                {
                    landed.Add(new SimulLanded(batch.Player, p));
                }

                if (p.Edit is { } edit)
                {
                    SimulEditVoidReason? voided = !survives ? SimulEditVoidReason.ConflictLost
                        : applied.Contains(edit) ? SimulEditVoidReason.DuplicateTarget
                        : null;
                    if (voided is null)
                    {
                        applied.Add(edit);
                    }

                    editOutcomes.Add(new SimulEditOutcome(batch.Player, p.Coord, edit, voided));
                }
            }
        }

        foreach (SimulLanded l in landed)
        {
            Board.Place(l.Placement.Coord, l.Player, l.Placement.Type);
        }

        Board.ApplyTerrainEdits(applied);

        // 7. 围杀：按结算顺位逐人提走非本人的无气整体；再孤立自绝（仍然无气的整体，不论属于谁）。
        var captures = ImmutableArray.CreateBuilder<SimulCaptureStep>();
        foreach (PlayerId p in order)
        {
            ImmutableArray<CapturedStone> removed = CaptureResolver.FindCaptured(Board, p);
            Board.RemoveStones(removed.Select(s => s.Coord));
            captures.Add(new SimulCaptureStep(p, removed));
        }

        ImmutableArray<CapturedStone> isolated = Isolated(Board);
        Board.RemoveStones(isolated.Select(s => s.Coord));
        captures.Add(new SimulCaptureStep(null, isolated));

        // 8. 活形锁定与势力。
        Recalculate();
        LifeShapeReport life = LifeShapeReport.Analyze(Board);

        // 9. 终局判定（先命中的为准）。
        SimulEndReason? end = ActivePlayers.Length <= 1 ? SimulEndReason.LastPlayerStanding
            : SimulRules.IsFull(Board, life) ? SimulEndReason.BoardFull
            : batches.All(b => b.Placements.IsEmpty) ? SimulEndReason.NoDeployRound
            : RoundCap > 0 && Round >= RoundCap ? SimulEndReason.RoundCap
            : null;

        var report = new SimulRoundReport(Round, order, batches, conflicts, landed.ToImmutable(), editOutcomes.ToImmutable(),
            captures.ToImmutable(), Power!, end);
        _reports.Add(report);

        if (end is { } reason)
        {
            Finish(reason);
        }
        else
        {
            // 10. 回合结束：按本回合势力生成下一回合顺位（没有任何淘汰）。
            StartRound(NextOrder());
        }

        return report;
    }

    // ---------- 公开视图 ----------

    /// <summary>公开视图：盘面副本、活形、势力、顺位、每人提交状态与手牌类型集合。不含任何人的暂放内容。</summary>
    public SimulPublicView PublicView()
    {
        GameBoard board = Board.Clone();
        ImmutableArray<SimulSeatView> seats =
        [
            .. _players.Select(p => new SimulSeatView(p, _seats[p].Status, _seats[p].Submitted, Hands.PublicView(p).Types)),
        ];
        return new SimulPublicView(Round, Phase, _order, board, LifeShapeReport.Analyze(board), Power, seats,
            _reports.Count > 0 ? _reports[^1] : null, Result);
    }

    // ---------- 内部 ----------

    /// <summary>回合开始（J2-1）：定顺位、记 S0、按顺位逐人开准备（部署上限 <see cref="SimulRules.DeployLimit"/>）并预抽征召候选（K5）。</summary>
    private void StartRound(ImmutableArray<PlayerId> order)
    {
        Round++;
        Stage = SimulStage.Prep;
        _order = order;
        _s0 = Board.Clone();
        _s0Life = LifeShapeReport.Analyze(_s0);
        foreach (PlayerId p in _order)
        {
            Seat seat = _seats[p];
            seat.Staged.Clear();
            seat.Submitted = false;
            EffectSnapshot defaults = EffectSnapshot.Defaults(p, Round, Hands.HeldTypeCount(p));
            var snapshot = new EffectSnapshot(p, Round, defaults.RevealCount, defaults.FreePickCount, defaults.TypeSlots,
                SimulRules.DeployLimit, defaults.HeldTypeCount);
            Hands.BeginTurn(p, snapshot);
            Hands.PredrawCandidates(p);
        }
    }

    /// <summary>结束某玩家本回合的手牌小回合：提交了单位即按类型扣牌；0 枚（或 <c>null</c>：被动收尾）不撤销本回合征召（G2）。</summary>
    private void CloseHandTurn(PlayerId player, ImmutableArray<Placement>? deployed)
    {
        if (Hands.PhaseOf(player) == TurnPhase.Organize)
        {
            Hands.EnterRecruitPredrawn(player);
        }

        if (deployed is { Length: > 0 } list)
        {
            Hands.DeductHand(player, list.GroupBy(p => p.Type).ToDictionary(g => g.Key, g => g.Count()));
        }
        else
        {
            Hands.SettleWithoutDeploy(player);
        }

        Hands.EndTurn(player);
    }

    /// <summary>下一回合顺位（Q05）：沿用现有先手值与同值链（<see cref="InitiativeOrder.Generate"/>），输入是本回合结束时的新玩法势力名次。</summary>
    private ImmutableArray<PlayerId> NextOrder()
    {
        SimulPowerSnapshot power = Power!;
        ImmutableArray<PlayerId> active = ActivePlayers;
        ImmutableArray<InitiativeEntry> entries =
        [
            .. active.Select(p =>
            {
                int rank = power.RankOf(p) ?? throw new SiegeRuleException($"参赛玩家 {p} 没有势力名次。");
                int? previous = Round >= 2 ? _order.IndexOf(p) : null;
                return new InitiativeEntry(p, rank, power.Of(p).Total, InitiativeOrder.ValueOf(active.Length, rank), previous, _seats[p].SeedRank);
            }),
        ];
        return InitiativeOrder.Generate(Round, entries).NextOrder;
    }

    /// <summary>孤立自绝：盘面上仍然无气的整体的全部单位，不论属于谁，按坐标序。</summary>
    private static ImmutableArray<CapturedStone> Isolated(GameBoard board)
    {
        var removed = new SortedDictionary<Coord, CapturedStone>();
        foreach (Group group in board.AllGroups())
        {
            if (!board.IsCaptured(group))
            {
                continue;
            }

            foreach (Coord c in group.Stones)
            {
                Occupant o = board[c].Occupant ?? throw new SiegeRuleException($"整体与盘面不一致：{c.ToNotation()} 已为空。");
                removed[c] = new CapturedStone(c, o.Owner, o.Type);
            }
        }

        return [.. removed.Values];
    }

    private void Recalculate()
    {
        PowerSnapshot @base = Scoreboard.Recalculate(Board, Roster, Math.Max(Round, 1), Options.ScoringVersion);
        Power = SimulRules.Power(Board, LifeShapeReport.Analyze(Board), @base);
    }

    private void Finish(SimulEndReason reason)
    {
        Recalculate();
        SimulPowerSnapshot power = Power!;
        var inputs = new List<StandingInput>(_players.Length);
        foreach (PlayerId p in _players)
        {
            Seat seat = _seats[p];
            SimulPlayerPower detail = power.Of(p);
            inputs.Add(new StandingInput(
                p,
                seat.Status,
                seat.Status == PlayerStatus.Resigned ? seat.PowerAtResign : detail.Total,
                Board.HeldRelicCount(p),
                detail.Territory.Length,
                Board.GroupsOf(p).Sum(g => g.Size),
                EliminationOrder: null));
        }

        ImmutableArray<Standing> standings = FinalStandings.Compute(inputs);
        ImmutableArray<PlayerId> placing =
        [
            .. standings
                .OrderBy(s => s.Group == StandingGroup.Finisher ? 0 : 1)
                .ThenBy(s => s.Rank)
                .ThenBy(s => _seats[s.Player].SeedRank)
                .Select(s => s.Player),
        ];
        ImmutableArray<PlayerId> winners =
        [
            .. placing.Where(p => _seats[p].Status == PlayerStatus.Active).Take(SimulRules.WinnerSlots(_players.Length)),
        ];
        Result = new SimulResult(reason, Round, standings, placing, winners);
        Phase = SimulPhase.Ended;
        Stage = SimulStage.Prep;
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

    private void RequirePhase(SimulPhase expected)
    {
        if (Phase != expected)
        {
            throw new SiegeRuleException($"对局阶段为 {Phase}，该操作须在 {expected} 阶段。");
        }
    }

    private Seat Require(PlayerId player) =>
        _seats.TryGetValue(player, out Seat? seat) ? seat : throw new SiegeRuleException($"玩家 {player} 不在本局名单中。");

    // ---------- 测试接缝 ----------

    /// <summary>测试专用：插旗阶段直接锁定给定座位（不改派、不消费 flag-pick），随后开始第 1 回合。</summary>
    internal void DebugLockAs(IEnumerable<(PlayerId Player, int Zone)> seats)
    {
        RequirePhase(SimulPhase.FlagPlanting);
        Flags.RestoreLocked(seats.Select(s => KeyValuePair.Create(s.Player, s.Zone)));
        LockFlags();
    }

    /// <summary>测试专用：把当前回合（准备阶段、无人提交）改成指定回合号与顺位，S0 与准备照旧。</summary>
    internal void DebugSetRound(int round, IEnumerable<PlayerId>? order)
    {
        RequirePhase(SimulPhase.InProgress);
        Round = round;
        if (order is not null)
        {
            _order = [.. order];
        }
    }

    /// <summary>测试专用：在正式盘面直接摆单位（绕过规则，只为构造局面）；进行中时同步刷新 S0 与势力。</summary>
    internal void DebugPlace(PlayerId owner, PieceType type, IEnumerable<Coord> cells)
    {
        foreach (Coord c in cells)
        {
            Board.Place(c, owner, type);
        }

        _s0 = Board.Clone();
        _s0Life = LifeShapeReport.Analyze(_s0);
        if (Phase != SimulPhase.FlagPlanting)
        {
            Recalculate();
        }
    }

    /// <summary>一名玩家的流程层状态。</summary>
    internal sealed class Seat
    {
        internal PlayerStatus Status { get; set; } = PlayerStatus.Active;

        internal int? BirthZone { get; set; }

        internal int SeedRank { get; set; }

        internal List<Placement> Staged { get; } = [];

        internal bool Submitted { get; set; }

        internal BigInteger PowerAtResign { get; set; }

        internal int? ResignedInRound { get; set; }
    }
}
