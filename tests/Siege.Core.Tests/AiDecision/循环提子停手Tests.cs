using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：openspec/changes/cycle-stop/specs/ai-decision —— Requirement: 循环提子停手（负责人裁决 D2 K = 6 / D3 候选条件；tasks 2.2）；
/// stagnation-trace 1.1（负责人裁决 G1）加 Scenario「停滞停手留痕」：停滞成立且最终 Pass 记 <c>D:stall</c>，决胜一手接管照常记落点。
/// </summary>
/// <remarks>
/// <para>载体沿用 <see cref="链接模式下的决胜一手Tests"/> 的平坦手写链接档图（<see cref="FlatMap"/>），简单难度、停手阈值写死 20、权重写死为定值前缺省。
/// 停滞记忆经测试接缝 <see cref="HeuristicTurnController.SeedStagnationMemory"/> 预置（本人各大回合首次决策时采样的公开势力、观察到的被提格），
/// 候选由只含指定格的合法范围限定：K = 3 时需要 4 个采样（本大回合及之前 3 个），"最近 3 个大回合开始时势力 120、118、118 逐一不高于再前一个"写成 122 / 120 / 118 / 118。</para>
/// <para>"会提子"的候选：P1 角上一子只剩一口气，候选格就是那口气（预演提子数 1）；"落在最近被提过的格"的候选：经接缝把该格记为最近观察到被提。</para>
/// <para>变异（2.2 实跑，记录见任务 notes.md）：M-ST1 停滞判定去掉"候选全是提子 / 反提"条件 → 「有非提子候选不停手」红；M-ST2 势力比较 <c>&gt;</c> 改 <c>&gt;=</c>（相等算增长）→ 「连续无净增且候选全是提子则停手」红；
/// M-ST3 停滞成立时跳过决胜一手检查 → 「决胜一手优先」红；M-ST4 K = 0 也进入停滞判定 → 「K 为 0 零变化」红；M-ST5 无子豁免去掉 → 「盘上无子不触发」红。</para>
/// <para>stagnation-trace 1.1 变异（实跑记录见任务 notes.md）：M-TR1 条目恒记 <c>D:pass</c> → 「停滞停手留痕」「连续无净增且候选全是提子则停手」「决胜一手优先」红；
/// M-TR2 去掉 Pass 门（停滞成立即记 <c>D:stall</c>，决胜一手接管也记）→ 「决胜一手优先」红；M-TR3 不看停滞（Pass 一律记 <c>D:stall</c>）→ 「停滞停手留痕」红。</para>
/// </remarks>
[Collection(ConsoleRedirect.Collection)]
public class 循环提子停手Tests
{
    private const int K = 3;
    private const int Threshold = 20;

    private static readonly EvaluationWeights Weights = SimFixtures.PreCalibrationWeights;

    private static AiSearchConfig Config(int k) => AiSearchConfig.Easy with { PassThreshold = Threshold, StagnationRounds = k };

    private static HeuristicTurnController Ai(MatchFlow match, int k = K) =>
        new(P0, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(P0)), AiDifficulty.Easy, Weights, Config(k));

    private static Coord Cell(int dx, int dy) => BirthCell(FlatMap, 0, dx, dy);

    /// <summary>P1 角上一子 (0,0)、P0 在 (1,0) 堵住一口气；候选格 (0,1) 是它最后一口气，落下即提子。P0 另有一枚远处的子，盘上有子（无子豁免不生效）。</summary>
    private static MatchFlow CornerCapture() => StartedFlat([0, 0, 3, 4]).AtRound(5, [P0, P1, P2, P3]).Place(P1, Cell(0, 0)).Place(P0, Cell(1, 0), Cell(4, 3));

    private static Coord CaptureCell => Cell(0, 1);

    private static Coord FarCell => Cell(3, 3);

    /// <summary>预置"连续 K 个大回合无净增"的采样：本大回合及之前 K 个，122 / 120 / 118 / 118（K = 3）。</summary>
    private static IEnumerable<(int Round, BigInteger Power)> Stagnant(int round) =>
        new BigInteger[] { 122, 120, 118, 118 }.Select((p, i) => (round - K + i, p));

    /// <summary>预置"势力仍在增长"的采样：100 / 110 / 118 / 125。</summary>
    private static IEnumerable<(int Round, BigInteger Power)> Growing(int round) =>
        new BigInteger[] { 100, 110, 118, 125 }.Select((p, i) => (round - K + i, p));

    /// <summary>P0 只在 <paramref name="range"/> 里的格、部署上限 1 上决策（影子批次 + 零副作用预演，与正式决策同一路径）。</summary>
    private static HeuristicTurnController Decide(MatchFlow match, HeuristicTurnController ai, params Coord[] range)
    {
        GameBoard board = match.Board;
        var batch = new StagedBatch(board, BatchFixtures.Context(board, P0, 1, BatchFixtures.Stock((PieceType.Basic, 5)), new HashSet<Coord>(range)));
        var driver = new SettlementDriver(board, match.History, new RecordingHooks { Board = board });
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));
        return ai;
    }

    [Fact]
    public void 连续无净增且候选全是提子则停手()
    {
        // Scenario：K = 3，AI 连续 3 个大回合开始时势力为 120、118、118（逐一不高于再前一个），本次候选单点全部会提子 → AI 确认 0 落子并 Pass。
        MatchFlow match = CornerCapture();
        HeuristicTurnController ai = Ai(match);
        ai.SeedStagnationMemory(Stagnant(match.MajorRound), []);
        Decide(match, ai, CaptureCell);

        PointScore only = Assert.Single(ai.LastPointRanking);
        Assert.Equal(1, only.Captures);   // 样本口径：唯一候选确实会提子
        Assert.True(only.Total > Threshold, $"样本口径：提子候选的收益 {only.Total} 本应越过停手阈值");   // 不是停手阈值让它 Pass 的
        StagnationStop stop = Assert.IsType<StagnationStop>(ai.LastStagnation);
        Assert.Equal(K, stop.Rounds);
        Assert.Equal([122, 120, 118, 118], stop.PowerByRound.Select(p => (int)p));
        Assert.Equal([CaptureCell], stop.Candidates);
        Assert.True(ai.LastChoice!.IsPass);
        Assert.Empty(ai.LastCandidates);
        Assert.Null(ai.LastDecisive);
        Assert.Equal(HeuristicTurnController.StallDecision, ai.Decisions[^1]);   // stagnation-trace 1.1：停滞停手留痕
    }

    [Fact]
    public void 势力仍在增长不停手()
    {
        // Scenario：K = 3，AI 连续 3 个大回合开始时势力为 110、118、125 → 停滞判定不成立，AI 按停手阈值正常决策（提子候选收益为正，落下）。
        MatchFlow match = CornerCapture();
        HeuristicTurnController ai = Ai(match);
        ai.SeedStagnationMemory(Growing(match.MajorRound), []);
        Decide(match, ai, CaptureCell);

        Assert.Null(ai.LastStagnation);
        Assert.False(ai.LastChoice!.IsPass);
        Assert.Equal(CaptureCell, Assert.Single(ai.LastChoice.Placements).Coord);
        Assert.StartsWith("D:", ai.Decisions[^1], StringComparison.Ordinal);
        Assert.NotEqual("D:pass", ai.Decisions[^1]);

        // 只差一个大回合有净增也不成立：118 / 118 / 119 / 118。
        HeuristicTurnController blip = Ai(CornerCapture());
        blip.SeedStagnationMemory(new BigInteger[] { 118, 118, 119, 118 }.Select((p, i) => (match.MajorRound - K + i, p)), []);
        Assert.Null(Decide(CornerCapture(), blip, CaptureCell).LastStagnation);
    }

    [Fact]
    public void 有非提子候选不停手()
    {
        // Scenario：K = 3，连续 3 个大回合势力无净增，但候选单点中有一个既不提子、也不在最近被提过的格上 → 停滞判定不成立，AI 按停手阈值正常决策。
        // 反面（判别力）：把那一格记为"最近被提过"后，同一局面即停手。
        MatchFlow match = CornerCapture();
        HeuristicTurnController ai = Ai(match);
        ai.SeedStagnationMemory(Stagnant(match.MajorRound), []);
        Decide(match, ai, CaptureCell, FarCell);

        Assert.Equal(2, ai.LastPointRanking.Length);
        Assert.Contains(ai.LastPointRanking, p => p.Coord == FarCell && p.Captures == 0);   // 样本口径：确有一个不提子的候选
        Assert.Null(ai.LastStagnation);
        Assert.False(ai.LastChoice!.IsPass);

        HeuristicTurnController marked = Ai(CornerCapture());
        marked.SeedStagnationMemory(Stagnant(match.MajorRound), [(match.MajorRound, FarCell)]);
        Decide(CornerCapture(), marked, CaptureCell, FarCell);
        StagnationStop stop = Assert.IsType<StagnationStop>(marked.LastStagnation);
        Assert.Equal([FarCell], stop.RecentlyCaptured);
        Assert.True(marked.LastChoice!.IsPass);

        // 窗口口径：K = 3 看最近 3 个大回合（本大回合及之前 2 个）；提子记录在第 r − 3 大回合就不算最近。
        HeuristicTurnController stale = Ai(CornerCapture());
        stale.SeedStagnationMemory(Stagnant(match.MajorRound), [(match.MajorRound - K, FarCell)]);
        Assert.Null(Decide(CornerCapture(), stale, CaptureCell, FarCell).LastStagnation);
        HeuristicTurnController edge = Ai(CornerCapture());
        edge.SeedStagnationMemory(Stagnant(match.MajorRound), [(match.MajorRound - K + 1, FarCell)]);
        Assert.NotNull(Decide(CornerCapture(), edge, CaptureCell, FarCell).LastStagnation);
    }

    [Fact]
    public void 决胜一手优先()
    {
        // Scenario：停滞判定成立，但本次 Pass 会使战场结算且存在一个追平 / 反超手 → AI 落下决胜一手而不是 Pass。
        // 局面沿用 决胜一手Tests「最后一个 Pass 会输且有反超手」：P0 东南角孤子、P1 两子成串，P1 / P2 / P3 都已 Pass（真实流程），P0 落后；
        // 候选格 (2,3) 既不提子也不在被提格上——经接缝把它记为最近被提，使停滞成立。
        MatchFlow match = StartedFlat([0, 0, 3, 4]).Place(P0, Cell(FlatMap.BirthBoard(0).Width - 1, 0)).Place(P1, Cell(0, 0), Cell(1, 0));
        match.Debug.SetOrder(P1, P2, P3, P0);
        foreach (PlayerId _ in new[] { P1, P2, P3 })
        {
            match.ToDeploy();
            match.Pass();
        }

        Assert.Equal(P0, match.CurrentPlayer);
        Assert.Equal([P1, P2, P3], match.Publish().PassedThisRound.Order());
        Assert.True(HeuristicTurnController.DecisiveTriggered(match.Publish(), P0));   // 样本口径：决胜一手的触发条件成立

        Coord candidate = Cell(2, 3);
        HeuristicTurnController ai = Ai(match);
        ai.SeedStagnationMemory(Stagnant(match.MajorRound), [(match.MajorRound, candidate)]);
        Decide(match, ai, candidate);

        Assert.NotNull(ai.LastStagnation);   // 停滞成立（留痕）
        DecisiveMove decisive = Assert.IsType<DecisiveMove>(ai.LastDecisive);
        Assert.False(ai.LastChoice!.IsPass);
        Assert.Equal(candidate, Assert.Single(ai.LastChoice.Placements).Coord);
        Assert.True(decisive.Outcome.PowerAfter >= decisive.Outcome.RivalAfter);
        // stagnation-trace 1.1：决胜一手接管时照常记落点，不记 D:stall（停滞虽成立，最终没有 Pass）。
        Assert.Equal($"D:{ai.LastChoice.Key}", ai.Decisions[^1]);
        Assert.DoesNotContain(HeuristicTurnController.StallDecision, ai.Decisions);

        // 对照：没有追平手（P1 远远领先）时停滞照常 Pass。
        MatchFlow hopeless = StartedFlat([0, 0, 3, 4]).Place(P0, Cell(FlatMap.BirthBoard(0).Width - 1, 0))
            .Place(P1, Cell(0, 0), Cell(1, 0), Cell(0, 1), Cell(1, 1), Cell(2, 0), Cell(0, 2));
        hopeless.Debug.SetOrder(P1, P2, P3, P0);
        foreach (PlayerId _ in new[] { P1, P2, P3 })
        {
            hopeless.ToDeploy();
            hopeless.Pass();
        }

        HeuristicTurnController passing = Ai(hopeless);
        passing.SeedStagnationMemory(Stagnant(hopeless.MajorRound), [(hopeless.MajorRound, candidate)]);
        Decide(hopeless, passing, candidate);
        Assert.NotNull(passing.LastStagnation);
        Assert.Null(passing.LastDecisive);
        Assert.True(passing.LastChoice!.IsPass);
        Assert.Equal(HeuristicTurnController.StallDecision, passing.Decisions[^1]);   // 停滞成立、没有决胜一手、最终 Pass → D:stall
    }

    [Fact]
    public void 停滞停手留痕()
    {
        // Scenario（stagnation-trace 1.1）：K = 3，停滞判定成立且本次决策最终 Pass → 该小回合的部署决策条目为 D:stall，不是 D:pass；
        // 同一局面上 K = 0 时记 D:pass，两者此后的决策逐步相同。
        // "同一局面"：CornerCapture、唯一候选 = 会提子的那口气、同一停滞采样；两份控制器只差 K。为让 K = 0 也落到 Pass（普通停手），
        // 两份都把停手阈值写死成远高于该候选收益的定值——K = 3 因停滞 Pass、K = 0 因阈值 Pass，决策相同、只有条目文案不同。
        // 简单难度候选批次数为 1：两条路径都不消费扰动子流（Consumed 均为 0），"此后逐步相同"由第二次决策逐条目相同与子流消费数相同共同钉住。
        const int HighThreshold = 1_000_000;
        MatchFlow stallMatch = CornerCapture();
        MatchFlow passMatch = CornerCapture();
        Siege.Core.Determinism.RandomStream stallStream = stallMatch.Seed.Stream(HeuristicAi.StreamName(P0));
        Siege.Core.Determinism.RandomStream passStream = passMatch.Seed.Stream(HeuristicAi.StreamName(P0));
        var stall = new HeuristicTurnController(P0, stallMatch.Publish, stallStream, AiDifficulty.Easy, Weights, Config(K) with { PassThreshold = HighThreshold });
        var pass = new HeuristicTurnController(P0, passMatch.Publish, passStream, AiDifficulty.Easy, Weights, Config(0) with { PassThreshold = HighThreshold });
        stall.SeedStagnationMemory(Stagnant(stallMatch.MajorRound), []);
        pass.SeedStagnationMemory(Stagnant(passMatch.MajorRound), []);

        Decide(stallMatch, stall, CaptureCell);
        Decide(passMatch, pass, CaptureCell);

        PointScore only = Assert.Single(stall.LastPointRanking);
        Assert.Equal(1, only.Captures);   // 样本口径：唯一候选会提子（停滞的候选条件成立）
        Assert.True(only.Total <= HighThreshold, $"样本口径：候选收益 {only.Total} 本应不越过写死的阈值，K = 0 才会普通 Pass");
        Assert.NotNull(stall.LastStagnation);
        Assert.Null(pass.LastStagnation);
        Assert.True(stall.LastChoice!.IsPass);
        Assert.True(pass.LastChoice!.IsPass);
        Assert.Null(stall.LastDecisive);
        Assert.Equal(HeuristicTurnController.StallDecision, stall.Decisions[^1]);
        Assert.Equal("D:pass", pass.Decisions[^1]);
        PointScore same = Assert.Single(pass.LastPointRanking);
        Assert.Equal((only.Coord, only.Type, only.Captures, only.Total), (same.Coord, same.Type, same.Captures, same.Total));   // 同一候选、同一评价

        // 此后逐步相同：同一大回合再决策一次（候选换成不提子、也不在被提格上的远处格，停滞不成立），两份的条目、选择与子流消费都相同。
        Decide(stallMatch, stall, FarCell);
        Decide(passMatch, pass, FarCell);
        Assert.Null(stall.LastStagnation);
        Assert.Equal(pass.Decisions[^1], stall.Decisions[^1]);
        Assert.Equal(pass.LastChoice!.Key, stall.LastChoice!.Key);
        Assert.Equal(
            pass.Decisions.Select(d => d == HeuristicTurnController.StallDecision ? "D:pass" : d),
            stall.Decisions.Select(d => d == HeuristicTurnController.StallDecision ? "D:pass" : d));
        Assert.Equal(1, stall.Decisions.Count(d => d == HeuristicTurnController.StallDecision));
        Assert.Equal(passStream.Consumed, stallStream.Consumed);

        // 普通 Pass 仍记 D:pass：K = 3、势力仍在增长（停滞不成立）、同一高阈值 → 因阈值 Pass，条目为 D:pass。
        MatchFlow growing = CornerCapture();
        var normal = new HeuristicTurnController(P0, growing.Publish, growing.Seed.Stream(HeuristicAi.StreamName(P0)), AiDifficulty.Easy, Weights, Config(K) with { PassThreshold = HighThreshold });
        normal.SeedStagnationMemory(Growing(growing.MajorRound), []);
        Decide(growing, normal, CaptureCell);
        Assert.Null(normal.LastStagnation);
        Assert.True(normal.LastChoice!.IsPass);
        Assert.Equal("D:pass", normal.Decisions[^1]);
    }

    [Fact]
    public void 盘上无子不触发()
    {
        // Requirement 首句"若该 AI 盘上有己方棋子"：无子豁免沿用——P0 盘上无子时即便采样全是无净增、候选落在被提格上也不停手。
        MatchFlow match = StartedFlat([0, 0, 3, 4]).AtRound(5, [P0, P1, P2, P3]).Place(P1, Cell(0, 0));
        Assert.True(match.Board.GroupsOf(P0).IsEmpty);
        HeuristicTurnController ai = Ai(match);
        ai.SeedStagnationMemory(Stagnant(match.MajorRound), [(match.MajorRound, FarCell)]);
        Decide(match, ai, FarCell);
        Assert.Null(ai.LastStagnation);
        Assert.False(ai.LastChoice!.IsPass);
    }

    [Fact]
    public void 采样与被提格由公开量推导()
    {
        // 判定 MUST 只读公开信息：势力采样取本人每个大回合首次决策时公开快照的总势力；被提格由相邻两次观察的占用差分推导（有子 → 空 / 换主人）。
        // 走真实流程：P0 第 5 大回合决策一次（采样 + 记占用），随后 P1 的子被提走，P0 第 6 大回合再决策时该格进入"最近被提"。
        MatchFlow match = CornerCapture();
        HeuristicTurnController ai = Ai(match);
        match.Debug.SetOrder(P0, P1, P2, P3);
        Decide(match, ai, FarCell);   // 第 5 大回合的决策：采样势力、记占用
        Assert.Null(ai.LastStagnation);
        Assert.Equal(match.Publish().Power!.Of(P0).Total, ai.LastSampledPower);
        Assert.Equal(5, ai.LastSampledRound);

        // P0 真实落在远处格（本大回合有人落子，不会整轮 Pass 结算），其余三人 Pass → 第 6 大回合；再提走 P1 角上的子（绕过本人决策，直接改权威盘面）。
        match.PlayTurn(FarCell.ToNotation());
        match.PassRound();
        match.Board.Place(CaptureCell, P2, PieceType.Basic);   // (0,1) 被 P2 占 → P1 (0,0) 无气，直接移除模拟被提
        match.Board.RemoveStones([Cell(0, 0)]);
        match.Debug.Recalculate();
        Assert.Equal(6, match.MajorRound);
        Assert.Null(match.Board[Cell(0, 0)].Occupant);
        match.Debug.SetOrder(P0, P1, P2, P3);

        Decide(match, ai, Cell(4, 4));   // 第 6 大回合的决策
        Assert.Equal(6, ai.LastSampledRound);
        Assert.Contains(Cell(0, 0), ai.RecentlyCapturedCells(match.MajorRound));
        Assert.DoesNotContain(CaptureCell, ai.RecentlyCapturedCells(match.MajorRound));   // 空 → 有子不算被提
    }

    [Fact]
    public void 复盘不改停滞记忆()
    {
        // 停手复盘（ReviewBestMove，--review-defeats）按同一判定给出停滞结论，但 MUST NOT 改 AI 的停滞记忆（ai-pass-review「复盘不改对局」）：
        // 第 5 大回合决策一次（记占用），提走 P1 角上的子、进入第 6 大回合；复盘之后采样与被提格都不变，随后的正式决策照常差分出被提格。
        // 变异 M-RV2（复盘后不还原上次观察的占用）→ 本条红（正式决策差分为空）；不还原采样 → 本条红（采样大回合变 6）。
        MatchFlow match = CornerCapture();
        HeuristicTurnController ai = Ai(match);
        Decide(match, ai, FarCell);
        Assert.Equal(5, ai.LastSampledRound);

        match.Board.RemoveStones([Cell(0, 0)]);
        match.Debug.Recalculate();
        match.AtRound(6, [P0, P1, P2, P3]);
        GameBoard board = match.Board;
        var shadow = new StagedBatch(board, BatchFixtures.Context(board, P0, 1, BatchFixtures.Stock((PieceType.Basic, 5)), new HashSet<Coord> { Cell(4, 4) }));
        BestMoveReview review = ai.ReviewBestMove(shadow, () => BatchRehearsal.Rehearse(shadow.Board, shadow.Context, shadow.Placements, match.History));
        Assert.Null(review.Stagnation);   // 采样不足 K + 1 个，停滞不成立
        Assert.Equal(5, ai.LastSampledRound);
        Assert.Empty(ai.RecentlyCapturedCells(6));

        Decide(match, ai, Cell(4, 4));
        Assert.Equal(6, ai.LastSampledRound);
        Assert.Equal([Cell(0, 0)], ai.RecentlyCapturedCells(6));
    }

    [Fact]
    public void K为0零变化()
    {
        // Scenario：K 配置为 0，其余配置不变，用相同种子对照引入本条之前的实现各跑一局 → 两局 AI 的每一步决策逐步相同。
        // 引入之前的走法 = 钉在 cycle-stop 之前的 5 份决策序列基线（本段实测 K = 6 也逐字节不变，见 notes.md）；这里取 link:1 种子 1 标准 × 4，
        // 跑局配置显式 StagnationRounds = 0，导出与基线逐字节相同；同一停滞局面下 K = 0 不停手（结构守门：K = 0 时整段判定不进入）。
        string actual = LinkDecisionSequence.Export(1, LinkDecisionSequence.BaselineRounds, AiDifficulty.Standard, attach: null, configure: c => c with { StagnationRounds = 0 });
        string path = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", "decisions-link1-seed1.txt");
        Assert.True(File.Exists(path), $"找不到决策序列基线：{path}");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), actual);

        MatchFlow match = CornerCapture();
        HeuristicTurnController ai = Ai(match, k: 0);
        ai.SeedStagnationMemory(Stagnant(match.MajorRound), [(match.MajorRound, FarCell)]);
        Decide(match, ai, CaptureCell);
        Assert.Null(ai.LastStagnation);
        Assert.False(ai.LastChoice!.IsPass);

        // K = 0：入口不采样、不记占用（未预置的控制器决策之后记忆仍为空）；K > 0 的对照确实采样。
        HeuristicTurnController fresh = Decide(CornerCapture(), Ai(CornerCapture(), k: 0), CaptureCell);
        Assert.Null(fresh.LastSampledRound);
        Assert.Equal(match.MajorRound, Decide(CornerCapture(), Ai(CornerCapture()), CaptureCell).LastSampledRound);
    }

    [Fact]
    public void 配置进入记录()
    {
        // Scenario：读取任意一局的对局日志首部 → 其中记录了各 AI 玩家实际生效的停滞大回合数。缺省（四档共用 6）落成到首部配置；显式 Search 的玩家写在 Players[].Search；
        // 按缺省配置建的会话每名 AI 实际生效 6；按首部重建（回放）时缺该项按 0；负数在跑局之前报错；命令行 --stagnation-rounds / --round-cap 进 config.json。
        RunConfig config = SimFixtures.Config(turnLimit: 4, difficulty: AiDifficulty.Standard);
        Assert.Null(config.StagnationRounds);
        MatchSession session = MatchSession.Create(config, 7);
        Assert.Equal(AiSearchConfig.DefaultStagnationRounds, session.StagnationRounds);
        Assert.All(session.Match.Players, p => Assert.Equal(AiSearchConfig.DefaultStagnationRounds, session.AiOf(p)!.Config.StagnationRounds));
        MatchLog log = session.Run();
        Assert.Equal(AiSearchConfig.DefaultStagnationRounds, log.Header.Config.StagnationRounds);
        Assert.Contains("\"StagnationRounds\":6", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);

        // 四档共用：四档预设与按地图取的配置都带 6；专家未显式给 Search 时落成的 Search 也带 6。
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Equal(AiSearchConfig.DefaultStagnationRounds, AiSearchConfig.ForMap(d, 300).StagnationRounds));
        RunConfig expert = (SimFixtures.Config(turnLimit: 1) with { Players = [new PlayerAiConfig { Difficulty = AiDifficulty.Expert }, new(), new(), new()] })
            .ResolvedFor(Siege.Core.Board.Maps.MapCatalog.Resolve("link:1:4"));
        Assert.Equal(AiSearchConfig.DefaultStagnationRounds, expert.Players[0].Search!.StagnationRounds);

        // 显式 Search：原样生效、原样记录（K = 2）；旧记录缺该项按 0 读。
        RunConfig explicitK = config with { Players = [.. config.Players.Select(p => p with { Search = AiSearchConfig.Standard with { StagnationRounds = 2 } })] };
        MatchSession explicitSession = MatchSession.Create(explicitK, 7);
        Assert.All(explicitSession.Match.Players, p => Assert.Equal(2, explicitSession.AiOf(p)!.Config.StagnationRounds));
        JsonObject header = JsonNode.Parse(explicitSession.Run().DeterministicText().Split('\n')[0])!.AsObject();
        Assert.Equal(2, header["Config"]!["Players"]![0]!["Search"]!["StagnationRounds"]!.GetValue<int>());

        // 按首部重建：缺该项 = 该项出现之前的旧日志，按 0 重建（MUST NOT 按今天的缺省 6）。
        Assert.Equal(0, MatchSession.Create(config, 7, map: null, recorded: true).StagnationRounds);
        Assert.Equal(0, MatchSession.Create(config, 7, map: null, recorded: true).AiOf(P0)!.Config.StagnationRounds);
        Assert.Equal(5, MatchSession.Create(config with { StagnationRounds = 5 }, 7, map: null, recorded: true).StagnationRounds);
        Assert.Throws<ArgumentException>(() => (config with { StagnationRounds = -1 }).Validated());
        Assert.Throws<ArgumentOutOfRangeException>(() => (AiSearchConfig.Standard with { StagnationRounds = -1 }).Validated());

        // 命令行：--stagnation-rounds / --round-cap 进 config.json。
        string dir = SimFixtures.TempDir("stagnation-cli");
        (int code, _, string err) = LinkSimFixtures.RunMain(null, null, "run", "--out", dir, "--map", "link:1:4", "--count", "1", "--turn-limit", "1", "--stagnation-rounds", "2", "--round-cap", "50");
        Assert.True(code == 0, err);
        RunConfig saved = RunConfig.FromJson(File.ReadAllText(Path.Combine(dir, "config.json")));
        Assert.Equal(2, saved.StagnationRounds);
        Assert.Equal(50, saved.RoundCap);
    }
}
