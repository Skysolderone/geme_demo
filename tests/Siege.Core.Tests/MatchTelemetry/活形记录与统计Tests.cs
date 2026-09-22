using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Sim.Analysis;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：life-shape 增量 match-telemetry —— Requirement: 活形记录与统计（tasks 4.1 / 4.2）。
/// 日志：快照的 <see cref="TurnSnapshot.Life"/>（活形确立 / 失去、两类拒绝计数、终局活形状态）+ <c>LifeRefused</c> 事件（暂放 / 确认被拒的坐标）。
/// 分析：<see cref="LifeShapeSection"/> 与报告「活形」一段；缺活形字段的旧日志整局排除并计数，他人致失活单列为规则缺陷。
/// </summary>
public class 活形记录与统计Tests
{
    private static readonly PlayerId P0 = MatchFixtures.P0;
    private static readonly PlayerId P1 = MatchFixtures.P1;

    /// <summary>P0 在出生区 0 角上的两眼活形（眼 A1、C1）：见 <c>合法落子范围的对外契约Tests.CornerA1C1</c>。</summary>
    private static readonly string[] CornerA1C1 = ["B1", "D1", "A2", "B2", "C2", "D2"];

    private static MatchLog RoundTrip(MatchLog log) => MatchLog.Parse(log.FullText());

    [Fact]
    public void 活形确立可查()
    {
        // 第 3 大回合，P0 角上已有 D1、A2–D2（A1–C1 三格空区，眼值 1 → 未定）；P0 本批落 B1 → A1、C1 两个单格眼 → 已确定活形。
        // 日志须可查：大回合 3、小回合 1、所有者 P0、棋串坐标（代表坐标 = 坐标序首格 B1）、眼空间与各眼值。
        MatchFlow match = MatchFixtures.Started().AtRound(3, [P0, P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(P0, "D1", "A2", "B2", "C2", "D2");
        Assert.Equal(LifeState.Undetermined, match.Publish().LifeShape.GroupLifeAt(TestMaps.At("A2"))!.Life);   // 前提：结算前未活
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config(turnLimit: 1));
        session.SetController(P0, new BlindController("B1"));

        MatchLog log = RoundTrip(session.Run());

        TurnSnapshot turn = log.Turns[0];
        Assert.Equal((1, 3, 0), (turn.Turn, turn.MajorRound, turn.Player));
        LifeChangeEntry change = Assert.Single(turn.Life!.Changes);
        Assert.Equal(LifeChangeEntry.Established, change.Kind);
        Assert.Equal(0, change.Owner);
        Assert.Equal(0, change.Actor);
        Assert.Equal("B1", change.At);
        Assert.Equal(["B1", "D1", "A2", "B2", "C2", "D2"], change.Stones);
        Assert.Equal(["A1=1", "C1=1"], change.EyeSpaces);
        Assert.Null(change.Cause);

        // 终局活形状态：P0 一条活形棋串、2 个受保护眼格；P1 无。
        LifePlayerEntry p0 = turn.Life.Players.Single(p => p.Player == 0);
        Assert.Equal((1, 0, 2, 2), (p0.AliveGroups, p0.SingleStoneAlive, p0.EyeSpaces, p0.EyeCells));
        Assert.Equal(0, turn.Life.Players.Single(p => p.Player == 1).AliveGroups);
        Assert.Equal(0, p0.TerrainSmallEyeSpaces);   // 平地角上的单格眼：棋盘外沿不算地形墙（R8 口径）
        Assert.Equal(2, turn.Life.ProtectedCells);
        Assert.Equal(81, turn.Life.PlayableCells);
    }

    [Fact]
    public void 自拆可查()
    {
        // P0 的角上活形（眼 A1、C1），P0 本批落 A1 填掉自己的一个眼 → 只剩 C1 → 未定：记一次失去，原因"所有者自拆"，棋串坐标取结算前。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [P0, P1, MatchFixtures.P2, MatchFixtures.P3]).Stones(P0, CornerA1C1);
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config(turnLimit: 1));
        session.SetController(P0, new BlindController("A1"));

        MatchLog log = RoundTrip(session.Run());

        LifeChangeEntry change = Assert.Single(log.Turns[0].Life!.Changes);
        Assert.Equal(LifeChangeEntry.Lost, change.Kind);
        Assert.Equal(LifeChangeEntry.OwnerFill, change.Cause);
        Assert.Equal((0, 0, "B1"), (change.Owner, change.Actor, change.At));
        Assert.Equal(["B1", "D1", "A2", "B2", "C2", "D2"], change.Stones);
        Assert.Equal(["A1=1", "C1=1"], change.EyeSpaces);   // 失去前的眼空间
        Assert.Equal(0, log.Turns[0].Life!.Players.Single(p => p.Player == 0).AliveGroups);
    }

    [Fact]
    public void 活形棋串加子不重复记确立()
    {
        // 按棋子归属判定：P0 的活形（眼 A1、C1）本批在 D3 加一子连上串，结算后仍是同一主的活形——不是"新确立"，也不是"失去"。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [P0, P1, MatchFixtures.P2, MatchFixtures.P3]).Stones(P0, CornerA1C1);
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config(turnLimit: 1));
        session.SetController(P0, new BlindController("D3"));

        MatchLog log = RoundTrip(session.Run());

        Assert.Equal(["D3:Basic"], log.Turns[0].Placements);   // 前提：确实加了子
        Assert.Empty(log.Turns[0].Life!.Changes);
        Assert.Equal(1, log.Turns[0].Life!.Players.Single(p => p.Player == 0).AliveGroups);
    }

    [Fact]
    public void 所有者的改造导致失去记为所有者的改造()
    {
        // 失去原因的第二个取值。B2 是未架桥深水（墙）：
        //   第 1 行  A1 ● · B1 眼 · C1 ● · D1 眼 · E1 ●
        //   第 2 行  A2 ●   B2 ~    C2 ●   D2 ●   E2 ●
        // C1–C2–D2–E2–E1 一串贴 B1、D1 两个单格眼 → 已确定活形（A1–A2 另成一串，只贴 B1，未定）。
        // P0 本批在 B3 落匠人并对 B2 搭桥 → B1、B2 连成 2 格空区（眼值 0），该串只剩 D1 → 未定：失去，原因"所有者的改造"（落点 B3 不在原眼空间里）。
        MatchFlow match = MatchFixtures.Started(TestMaps.Terrain(surfaces: [("B2", Surface.DeepWater)]))
            .AtRound(5, [P0, P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(P0, "A1", "C1", "E1", "A2", "C2", "D2", "E2");
        match.Debug.SeedHand(P0, (PieceType.Artisan, 1));
        Assert.Equal(LifeState.Alive, match.Publish().LifeShape.GroupLifeAt(TestMaps.At("C1"))!.Life);   // 前提
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config(turnLimit: 1));
        session.SetController(P0, new EditController("B3", TerrainEdit.Bridge(TestMaps.At("B2"))));

        MatchLog log = RoundTrip(session.Run());

        Assert.Single(log.Turns[0].Edits!);   // 前提：改造确实生效
        LifeChangeEntry lost = Assert.Single(log.Turns[0].Life!.Changes, c => c.Kind == LifeChangeEntry.Lost);
        Assert.Equal(LifeChangeEntry.OwnerEdit, lost.Cause);
        Assert.Equal(["C1", "E1", "C2", "D2", "E2"], lost.Stones);
        Assert.Equal(["B1=1", "D1=1"], lost.EyeSpaces);
    }

    [Fact]
    public void 拒绝尝试可查()
    {
        // P1 试图把一枚子暂放进 P0 的眼 A1（活棋禁入，暂放即被拒）→ 放弃、Pass。
        // 日志：LifeRefused 事件（行动玩家 P1、类别 LifeForbidden、坐标 A1、所有者 P0、环节 stage），快照计数 ForbiddenStaged = 1。
        // 期望值非 0：抓得到"写入端漏写"（testing.md「期望值是 0 / null 的遥测断言抓不到写入端漏写」）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [P1, P0, MatchFixtures.P2, MatchFixtures.P3]).Stones(P0, CornerA1C1);
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config(turnLimit: 1, retention: EventRetention.SnapshotsOnly));
        session.SetController(P1, new TryStageController("A1"));

        MatchLog log = RoundTrip(session.Run());

        TurnSnapshot turn = log.Turns[0];
        Assert.Equal(1, turn.Player);
        Assert.Equal(1, turn.Life!.ForbiddenStaged);
        Assert.Equal((0, 0, 0, 0), (turn.Life.ForbiddenRehearsed, turn.Life.ForbiddenRejected, turn.Life.BreaksRehearsed, turn.Life.BreaksRejected));

        // 非细粒度事件：只存快照的保留策略下照样在。
        LogEvent refused = Assert.Single(log.Events, e => e.Type == LogEventType.LifeRefused);
        Assert.Equal(1, refused.Player);
        Assert.Equal(nameof(BatchFailureKind.LifeForbidden), refused.FailureKind);
        Assert.Equal(["A1"], refused.Coords);
        Assert.Equal(0, (int)refused.Values!["Owner"]);
        Assert.StartsWith("stage", refused.Detail, StringComparison.Ordinal);
        Assert.Equal(1, refused.Turn);
    }

    [Fact]
    public void 真实跑局的活形字段自洽()
    {
        // 真实跑局（Standard，v5，完整事件流）：
        // ① 每条快照都带活形字段（新日志不是 null），旧日志判据才站得住；
        // ② 预演阶段"破坏活形"的计数 = 细粒度 Rehearsal 事件里同类别的条数（两条独立写入路径互证）；样本口径下界 > 0；
        // ③ 终局快照的逐玩家活形状态 = 测试侧在活对局上独立数出来的值；样本口径下界：终局至少一条活形棋串。
        RunConfig config = SimFixtures.Config(count: 1, seedStart: 1, turnLimit: 40, difficulty: AiDifficulty.Standard);
        MatchSession session = MatchSession.Create(config, config.SeedAt(0));
        MatchLog log = RoundTrip(session.Run());

        Assert.All(log.Turns, t => Assert.NotNull(t.Life));
        int breaks = log.Turns.Sum(t => t.Life!.BreaksRehearsed);
        Assert.True(breaks > 0, "样本里一次预演阶段的破坏活形都没有，本条什么都没证明。");
        Assert.Equal(
            log.Events.Count(e => e.Type == LogEventType.Rehearsal && e.FailureKind == nameof(BatchFailureKind.BreaksLife)),
            breaks);

        LifeShapeReport life = session.Match.Publish().LifeShape;
        LifeTurnEntry last = log.Turns[^1].Life!;
        foreach (PlayerId p in session.Match.Players)
        {
            GroupLife[] alive = [.. life.Groups.Where(g => g.Life == LifeState.Alive && g.Group.Owner == p)];
            EyeSpace[] eyes = [.. life.EyeSpaces.Where(s => s.Owner == p && life.GroupsOf(s).Any(g => g.Life == LifeState.Alive))];
            LifePlayerEntry entry = last.Players.Single(e => e.Player == p.Value);
            Assert.Equal(alive.Length, entry.AliveGroups);
            Assert.Equal(alive.Count(g => g.Group.Size == 1), entry.SingleStoneAlive);
            Assert.Equal(eyes.Length, entry.EyeSpaces);
            Assert.Equal(eyes.Sum(s => s.Cells.Length), entry.EyeCells);
        }

        Assert.True(last.Players.Sum(p => p.AliveGroups) > 0, "终局没有任何活形棋串，逐玩家比对是空证。");

        // R8 贴地形小空区：测试侧独立算——盘内几何方向数用坐标算术，与气边邻居数比较；样本口径下界 > 0（v5 的岩石 / 深水切出很多小空区）。
        GameBoard board = session.Match.Board;
        int InBoard(Coord c) => (c.X > 0 ? 1 : 0) + (c.X < board.Width - 1 ? 1 : 0) + (c.Y > 0 ? 1 : 0) + (c.Y < board.Height - 1 ? 1 : 0);
        int terrainSmall = life.EyeSpaces.Count(s => life.GroupsOf(s).Any(g => g.Life == LifeState.Alive)
            && s.Cells.Length <= 3 && s.Cells.Any(c => InBoard(c) > board.LibertyNeighbors(c).Length));
        Assert.True(terrainSmall > 0, "终局没有贴地形小空区形成的眼空间，R8 统计是空证。");
        Assert.Equal(terrainSmall, last.Players.Sum(p => p.TerrainSmallEyeSpaces));
        Assert.Equal(session.Match.Board.AllCoords().Count(c => session.Match.Board[c].Terrain == Terrain.Playable), last.PlayableCells);
    }

    // ---------- 分析（合成日志，手算期望） ----------

    private static LifeTurnEntry Life(
        LifeChangeEntry[]? changes = null, LifePlayerEntry[]? players = null, int protectedCells = 0, int playable = 100,
        int forbiddenStaged = 0, int breaksRehearsed = 0, int breaksRejected = 0) =>
        new()
        {
            Changes = [.. changes ?? []],
            Players = [.. players ?? []],
            ProtectedCells = protectedCells,
            PlayableCells = playable,
            ForbiddenStaged = forbiddenStaged,
            BreaksRehearsed = breaksRehearsed,
            BreaksRejected = breaksRejected,
        };

    private static LifeChangeEntry Established(int owner, params string[] stones) =>
        new() { Kind = LifeChangeEntry.Established, Owner = owner, Actor = owner, At = stones[0], Stones = [.. stones], EyeSpaces = ["A1=1", "C1=1"] };

    private static LifeChangeEntry Lost(int owner, int actor, string cause, params string[] stones) =>
        new() { Kind = LifeChangeEntry.Lost, Owner = owner, Actor = actor, At = stones[0], Stones = [.. stones], EyeSpaces = ["A1=1", "C1=1"], Cause = cause };

    private static LifePlayerEntry Player(int player, int alive = 0, int single = 0, int eyes = 0, int eyeCells = 0, int terrainSmall = 0) =>
        new() { Player = player, AliveGroups = alive, SingleStoneAlive = single, EyeSpaces = eyes, EyeCells = eyeCells, TerrainSmallEyeSpaces = terrainSmall };

    /// <summary>
    /// 手算样本（4 局 + 1 局旧日志）：
    ///   局 701：P0 第 2 大回合确立（单子 C3），P1 第 4 大回合确立（两子 E5、E6）；终局 P0 活 1（单子）、眼 1 块 2 格（贴地形小空区 1 块），P1 活 1、眼 2 块 7 格；
    ///           受保护眼格 9 / 可落子 90；P0 获胜。拒绝：暂放禁入 2、预演破坏 5、确认破坏 1。
    ///   局 702：P2 第 3 大回合确立（两子），第 5 大回合所有者自拆失去；终局无人有活形；受保护 0 / 100；P3 获胜。
    ///   局 703：整局无人确立；受保护 0 / 100；截断局（无名次）——计入首次确立 / 终局状态，不计胜率。
    ///   局 704：P1 第 6 大回合确立（两子 G7、G8）；终局 P1 活 1、眼 1 块 2 格；受保护 2 / 100；P1 获胜。
    ///   局 705：旧日志（快照缺活形字段）→ 整局排除并计数。
    /// </summary>
    private static List<MatchLog> Sample()
    {
        MatchLog m701 = SimFixtures.Synthetic(
            701,
            [
                SimFixtures.Turn(1, 2, 0, [10, 5, 5, 5], ["C3:Basic"], life: Life([Established(0, "C3")], forbiddenStaged: 2, breaksRehearsed: 3)),
                SimFixtures.Turn(2, 4, 1, [10, 5, 5, 5], ["E5:Basic"], life: Life([Established(1, "E5", "E6")], breaksRehearsed: 2, breaksRejected: 1)),
                SimFixtures.Turn(3, 5, 2, [10, 5, 5, 5], life: Life(
                    players: [Player(0, 1, 1, 1, 2, 1), Player(1, 1, 0, 2, 7), Player(2), Player(3)], protectedCells: 9, playable: 90)),
            ],
            [],
            SimFixtures.ResultOf(5, [0]));
        MatchLog m702 = SimFixtures.Synthetic(
            702,
            [
                SimFixtures.Turn(1, 3, 2, [5, 5, 10, 5], ["A9:Basic"], life: Life([Established(2, "A8", "B8")])),
                SimFixtures.Turn(2, 5, 2, [5, 5, 10, 5], ["A9:Basic"], life: Life(
                    [Lost(2, 2, LifeChangeEntry.OwnerFill, "A8", "B8")], players: [Player(0), Player(1), Player(2), Player(3)])),
            ],
            [],
            SimFixtures.ResultOf(5, [3]));
        MatchLog m703 = SimFixtures.Synthetic(
            703,
            [SimFixtures.Turn(1, 4, 0, [5, 5, 5, 5], life: Life(players: [Player(0), Player(1), Player(2), Player(3)]))],
            [],
            SimFixtures.TruncatedResult(4));
        MatchLog m704 = SimFixtures.Synthetic(
            704,
            [
                SimFixtures.Turn(1, 6, 1, [5, 10, 5, 5], ["G7:Basic"], life: Life(
                    [Established(1, "G7", "G8")], players: [Player(0), Player(1, 1, 0, 1, 2), Player(2), Player(3)], protectedCells: 2)),
            ],
            [],
            SimFixtures.ResultOf(6, [1]));
        MatchLog legacy = SimFixtures.Synthetic(
            705,
            [SimFixtures.Turn(1, 2, 0, [10, 5, 5, 5], ["C3:Basic"], legacyNoLife: true)],
            [],
            SimFixtures.ResultOf(5, [0]));
        Assert.Null(legacy.Turns[0].Life);
        return [m701, m702, m703, m704, legacy];
    }

    [Fact]
    public void 活形分析输出()
    {
        LifeShapeSection s = BalanceAnalyzer.Analyze(Sample()).LifeShape;

        Assert.Equal(4, s.Matches);
        Assert.Equal(1, s.Skipped);                      // 旧日志整局排除并计数，MUST NOT 当成"这局没人活"

        // 首次活形确立（每局最早一次确立的大回合）：701→2、702→3、704→6；703 整局无确立。平均 (2+3+6)/3 = 11/3。
        Assert.Equal(3, s.MatchesWithLife);
        Assert.Equal(1, s.MatchesWithoutLife);
        Assert.Equal(11.0 / 3, s.MeanFirstEstablishedRound, 10);

        // 按终局名次分组（只取有名次的局 701 / 702 / 704；每名玩家取自己最早一次确立）：
        //   第 1 名：701 的 P0（2）、704 的 P1（6）→ 平均 4，样本 2；第 2 名：701 的 P1（4）、702 的 P2（3）→ 平均 3.5，样本 2。
        Assert.Equal([1, 2], s.FirstRoundByRank.Keys);
        Assert.Equal((4.0, 2), (s.FirstRoundByRank[1].Mean, s.FirstRoundByRank[1].Samples));
        Assert.Equal((3.5, 2), (s.FirstRoundByRank[2].Mean, s.FirstRoundByRank[2].Samples));

        // 终局逐玩家（4 局 × 4 人 = 16 个样本）：活形棋串 (1+1+1)/16，眼空间格 (2+7+2)/16。
        Assert.Equal(3.0 / 16, s.MeanFinalAliveGroups, 10);
        Assert.Equal(11.0 / 16, s.MeanFinalEyeCells, 10);

        // 终局禁入格占比（受保护眼格 ÷ 可落子格，逐局平均）：(9/90 + 0 + 0 + 2/100) / 4 = 0.03。
        Assert.Equal(0.03, s.MeanForbiddenShare, 10);

        // 有活形玩家的胜率：只取有名次的局。701 的 P0（胜）、P1（负）、704 的 P1（胜）→ 2/3；无活形玩家：701 的 P2、P3，702 四人（P3 胜），704 的 P0、P2、P3 → 1/9。
        Assert.Equal((2, 3), (s.WinRateWithLife.Successes, s.WinRateWithLife.Trials));
        Assert.Equal((1, 9), (s.WinRateWithoutLife.Successes, s.WinRateWithoutLife.Trials));

        // 两类拒绝：活棋禁入（暂放 2）、破坏活形（预演 5、确认 1）。
        Assert.Equal((2, 0, 0), (s.ForbiddenStaged, s.ForbiddenRehearsed, s.ForbiddenRejected));
        Assert.Equal((5, 1), (s.BreaksRehearsed, s.BreaksRejected));

        // 失去按原因：所有者自拆 1。他人致失活 0。
        Assert.Equal(1, s.LostByCause[LifeChangeEntry.OwnerFill]);
        Assert.Empty(s.Defects);

        // R8：终局单子活形 1/3；确立事件里单子 1/4（C3 单子，其余三次两子）；终局受保护眼空间里贴地形小空区 1/4（701 的 P0 一块；共 1+2+1 = 4 块）。
        Assert.Equal((1, 3), (s.FinalSingleStoneAlive, s.FinalAliveGroups));
        Assert.Equal((1, 4), (s.EstablishedSingleStone, s.EstablishedTotal));
        Assert.Equal((1, 4), (s.FinalTerrainSmallEyeSpaces, s.FinalEyeSpaces));

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze(Sample()));
        Assert.Contains("## 活形（life-shape）", text, StringComparison.Ordinal);
        Assert.Contains("纳入 4 局，排除缺活形字段的旧日志 1 局", text, StringComparison.Ordinal);
        Assert.Contains("首次活形确立平均第 3.67 大回合（有确立的 3 局；整局无确立 1 局）", text, StringComparison.Ordinal);
        Assert.Contains("第 1 名 4（样本 2）", text, StringComparison.Ordinal);
        Assert.Contains("终局禁入格占可落子格：平均 3.0%", text, StringComparison.Ordinal);
        Assert.Contains("终局有活形玩家的胜率 66.7% (2/3", text, StringComparison.Ordinal);
        Assert.Contains("活棋禁入：暂放被拒 2，预演 0，确认被拒 0", text, StringComparison.Ordinal);
        Assert.Contains("破坏活形：预演 5，确认被拒 1", text, StringComparison.Ordinal);
        Assert.Contains("他人致失活（规则缺陷，应为 0）：0 次", text, StringComparison.Ordinal);
        Assert.Contains("单子活形棋串：终局 1/3（33.3%）；确立事件 1/4（25.0%）", text, StringComparison.Ordinal);
        Assert.Contains("贴地形小空区（≤ 3 格且贴地形墙）形成的眼空间：终局 1/4（25.0%）", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 他人致失活视为缺陷()
    {
        // 一条"非所有者的批次导致活形失去"的记录（P1 的小回合里 P0 的活形失去）→ 报告单列为规则缺陷，给出种子与小回合序号。
        MatchLog broken = SimFixtures.Synthetic(
            706,
            [
                SimFixtures.Turn(1, 2, 0, [10, 5, 5, 5], ["C3:Basic"], life: Life([Established(0, "B1", "D1")])),
                SimFixtures.Turn(2, 2, 1, [10, 5, 5, 5], ["C1:Basic"], life: Life(
                    [Lost(0, 1, LifeChangeEntry.NonOwner, "B1", "D1")], players: [Player(0), Player(1), Player(2), Player(3)])),
            ],
            [],
            SimFixtures.ResultOf(5, [1]));

        LifeShapeSection s = BalanceAnalyzer.Analyze([broken]).LifeShape;

        LifeDefect defect = Assert.Single(s.Defects);
        Assert.Equal((broken.Header.Seed, 2, 2, 0, 1), (defect.Seed, defect.Turn, defect.MajorRound, defect.Owner, defect.Actor));
        string text = ReportWriter.Render(BalanceAnalyzer.Analyze([broken]));
        Assert.Contains("他人致失活（规则缺陷，应为 0）：1 次", text, StringComparison.Ordinal);
        Assert.Contains($"种子 {broken.Header.Seed} 第 2 小回合（第 2 大回合）：玩家 1 的批次使玩家 0 的活形失去", text, StringComparison.Ordinal);

        // 反面：正常样本里是 0 次，且不出明细行。
        Assert.DoesNotContain("的批次使玩家", ReportWriter.Render(BalanceAnalyzer.Analyze(Sample())), StringComparison.Ordinal);
    }
}

/// <summary>部署时只尝试暂放指定格（被拒也不抛），随后什么都不放（Pass）。用于制造"暂放即被拒"的尝试。</summary>
internal sealed class TryStageController(string cell) : ITurnController
{
    public void OrganizeHand(PlayerHandAccess hand, int overflow)
    {
        for (int i = 0; i < overflow; i++)
        {
            hand.Discard(hand.PrivateView().Types.First());
        }
    }

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
    {
    }

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        if (batch.Stage(Coord.Parse(cell), PieceType.Basic) is null)
        {
            throw new InvalidOperationException($"夹具前提不成立：{cell} 应被拒。");
        }
    }

    public bool OnRejected(StagedBatch batch, BatchFailure failure)
    {
        batch.Clear();
        return false;
    }
}

/// <summary>部署时暂放一枚带改造的匠人，暂放被拒即抛（夹具前提）。</summary>
internal sealed class EditController(string cell, TerrainEdit edit) : ITurnController
{
    public void OrganizeHand(PlayerHandAccess hand, int overflow)
    {
    }

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
    {
    }

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        if (batch.Stage(Coord.Parse(cell), PieceType.Artisan, edit) is { } failure)
        {
            throw new InvalidOperationException($"夹具暂放失败：{failure.Message}");
        }
    }

    public bool OnRejected(StagedBatch batch, BatchFailure failure) =>
        throw new InvalidOperationException($"夹具批次被拒：{failure.Message}");
}
