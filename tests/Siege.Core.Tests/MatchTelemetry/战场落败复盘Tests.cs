using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Tests.AiDecision;
using Siege.Core.Tests.CaptureResolution;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：openspec/changes/open-map/specs/match-telemetry —— Requirement: 战场落败复盘与等待者评估（MODIFIED：只剩复盘半边，等待者评估随等待者删除）。
/// open-map 段 B（裁决 B3）：原 <c>战场落败复盘与等待者评估Tests</c> 的 6 条复盘用例搬到本类，「等待者决赛表现可查」随 <c>PassReviewAnalyzer.Waiters</c> 删除。
/// </summary>
/// <remarks>
/// <para>复盘入口（ai-pass-review 1.1）的三例手写局面沿用 <see cref="链接模式下落后者的停手阈值Tests"/> 的口径：P0、P1 同在出生棋盘 0（link-map-terrain 第 2 组起载体由 <c>link:1</c>
/// 改为平坦的手写链接档图 <see cref="LinkModeFixtures.FlatMap"/>），简单难度、权重写死为定值前缺省并把 PowerGain 取 1，停手阈值写死 20；合法落子范围只给一格，结论只取决于这一格。</para>
/// <para>变异（ai-pass-review 1.4 实跑）：MP-1 复盘改为消费 AI 扰动子流本体（<c>ReviewBestMove</c> 里多调一次 <c>Perturb(ranked)</c>）→「复盘不改对局」红；其余见各测试注释。</para>
/// <para>唯一战场上"本战场最高者"= 全部其他在局玩家，「有翻盘手时判本可翻盘」的 P2 只放 1 子（原 MP-3"对手不限本战场"在唯一战场上不再可分）。</para>
/// </remarks>
[Collection(ConsoleRedirect.Collection)]
public class 战场落败复盘Tests
{
    private const int Threshold = 20;

    private static readonly EvaluationWeights Weights = SimFixtures.PreCalibrationWeights with { PowerGain = 1 };

    /// <summary>一局真实 AI 链接局（四人四区种子、简单难度、跑到终局）的日志，经文本往返。</summary>
    // map-expand-25 段 A2 实测（未改）：link:1:4 换成浮空岛后，这局四名 Easy 要跑约 13 分钟（turnLimit 600 / 大回合上限 100 先到者收尾），全局没有一次整轮 Pass 结算，
    // DefeatReplay.TargetsOf 为空，「落败者复盘可查」的样本口径失效（红）；探针 PassThreshold 120–4000 六分钟内都未终局（落后者阈值取 0、决胜一手会接管最后一个 Pass）。
    // 复盘要按首部地图标识回放，不能改用手写图；段 A3 用户裁决：两条使用者转慢测试（见各测试上方注释）。
    private static readonly Lazy<MatchLog> RealLog = new(() =>
        MatchLog.Parse(MatchSession.Create(LinkSimFixtures.Config(turnLimit: 600), LinkSimFixtures.FourZoneSeed.Value).Run().FullText()));

    /// <summary>P0 在 <paramref name="cell"/> 一格上复盘（部署上限 1、库存 5 枚普通子），影子批次走零副作用预演。</summary>
    private static BestMoveReview Review(MatchFlow match, Coord cell, int stock = 5)
    {
        GameBoard board = match.Board;
        IReadOnlyDictionary<PieceType, int> hand = stock == 0 ? new Dictionary<PieceType, int>() : BatchFixtures.Stock((PieceType.Basic, stock));
        var batch = new StagedBatch(board, BatchFixtures.Context(board, P0, 1, hand, new HashSet<Coord> { cell }));
        var ai = new HeuristicTurnController(P0, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(P0)), AiDifficulty.Easy, Weights,
            AiSearchConfig.Easy with { PassThreshold = Threshold });
        BestMoveReview review = ai.ReviewBestMove(batch, () => BatchRehearsal.Rehearse(board, batch.Context, batch.Placements, match.History));
        Assert.Empty(batch.Placements);   // 复盘结束清空影子批次
        Assert.Empty(ai.Decisions);       // 不写决策留痕
        return review;
    }

    [Fact]
    public void 有翻盘手时判本可翻盘()
    {
        // 局面：P0 右上角一枚孤子，P1 左上角两子成串、势力高于 P0；P0 只能落孤子点 (2, 3)。
        // 期望：最好一手 = 这一格；落下后（对手不回应）P0 势力 ≥ 本战场最高者 → 本可翻盘；P0 落后 → 实际阈值取 0（C15），收益 > 0 → 正式决策不会 Pass。
        // open-map：整张地图是唯一战场，"本战场最高者"= 全部其他在局玩家里势力最高者——P2 在棋盘 3 只放 1 子（低于 P0 落下后的势力），最高者仍是 P1。
        Coord cell = BirthCell(FlatMap, 0, 2, 3);
        MatchFlow match = StartedFlat([0, 0, 3, 4]).Place(P0, BirthCell(FlatMap, 0, 4, 0)).Place(P1, BirthCell(FlatMap, 0, 0, 0), BirthCell(FlatMap, 0, 1, 0))
            .Place(P2, BirthCell(FlatMap, 3, 0, 0));
        MatchPublicView view = match.Publish();
        Assert.True(view.Power!.Of(P0).Total < view.Power.Of(P1).Total);   // 样本口径：P0 落后
        Assert.True(view.Power.Of(P2).Total < view.Power.Of(P1).Total);    // 样本口径：最高者是 P1

        BestMoveReview review = Review(match, cell);

        Assert.Equal(0, review.PassThreshold);
        Assert.Equal(view.Power.Of(P0).Total, review.PowerBefore);
        Assert.Equal(view.Power.Of(P1).Total, review.RivalBefore);
        Assert.Equal(NoMoveReason.None, review.NoMove);
        Assert.Equal(1, review.Candidates);
        MoveOutcome best = Assert.IsType<MoveOutcome>(review.Best);
        Assert.Equal(cell, Assert.Single(best.Placements).Coord);
        Assert.True(best.Gain > 0, $"收益 {best.Gain}");
        Assert.Equal(0, best.Captures);
        Assert.True(best.PowerAfter > review.PowerBefore);
        Assert.True(view.Power.Of(P2).Total < best.PowerAfter, $"P2 {view.Power.Of(P2).Total}");   // 样本口径：P2 低于 P0 落下后的势力，不改变"最高者"
        Assert.True(best.ComesBack, $"落下后 {best.PowerAfter} vs {best.RivalAfter}");
        Assert.Equal(1, review.ComebackCandidates);
        Assert.False(review.ImpliesPass);
        Assert.Equal(best.Placements, review.Forced!.Placements);   // 部署上限 1：强制批次即最好一手
    }

    [Fact]
    public void 确无有用手时无合法候选()
    {
        // 局面：P0 唯一可落的格四邻都是 P1 的子（各自另有气）→ 落下即自杀手，预演不合法；没有任何合法候选，正式决策必然 Pass。
        // 另：手里没有棋子 → 无手牌。判别力：变异 MP-4"无落子原因漏判无合法候选（Candidates == 0 一支删去）"→ 本测试红。
        Coord cell = BirthCell(FlatMap, 0, 2, 2);
        MatchFlow match = StartedFlat([0, 0, 3, 4])
            .Place(P0, BirthCell(FlatMap, 0, 4, 4))
            .Place(P1, BirthCell(FlatMap, 0, 1, 2), BirthCell(FlatMap, 0, 3, 2), BirthCell(FlatMap, 0, 2, 1), BirthCell(FlatMap, 0, 2, 3));

        BestMoveReview review = Review(match, cell);

        Assert.Equal(1, review.LegalCells);
        Assert.Equal(0, review.Candidates);
        Assert.Equal(NoMoveReason.NoLegalCandidate, review.NoMove);
        Assert.Null(review.Best);
        Assert.Null(review.Forced);
        Assert.True(review.ImpliesPass);
        Assert.Equal(0, review.ComebackCandidates);
        Assert.Equal(1, review.Rejections[$"预演不合法:{BatchFailureKind.Suicide}"]);

        BestMoveReview empty = Review(match, BirthCell(FlatMap, 0, 0, 4), stock: 0);
        Assert.Equal(NoMoveReason.NoStock, empty.NoMove);
        Assert.True(empty.ImpliesPass);
    }

    [Fact]
    public void 能提子的一手()
    {
        // 局面：P1 一枚子三面被 P0 围住，P0 只能落它的最后一口气 → 提 1 子。最好一手的提子数与"提子候选"都计入。
        Coord stone = BirthCell(FlatMap, 0, 2, 2);
        Coord cell = BirthCell(FlatMap, 0, 2, 3);
        MatchFlow match = StartedFlat([0, 0, 3, 4])
            .Place(P0, BirthCell(FlatMap, 0, 1, 2), BirthCell(FlatMap, 0, 3, 2), BirthCell(FlatMap, 0, 2, 1))
            .Place(P1, stone, BirthCell(FlatMap, 0, 0, 4));
        Assert.Single(match.Board.LibertiesOf(match.Board.GroupAt(stone)!));   // 样本口径：P1 这枚只剩一口气

        BestMoveReview review = Review(match, cell);

        MoveOutcome best = Assert.IsType<MoveOutcome>(review.Best);
        Assert.Equal(1, best.Captures);
        Assert.Equal(1, review.CaptureCandidates);
        Assert.True(best.Gain > 0);
    }

    [Fact]
    public void 复盘不改对局()
    {
        // 规格 Scenario「复盘不改对局」：链接图决策序列基线（link:1、种子 3、截到第 12 大回合）在<b>每一次</b>部署前都挂上复盘（影子批次 + 同一 AI 对象），
        // 导出仍与钉入的基线逐字节相同；并且复盘结论（ImpliesPass）与 AI 随后的实际决策逐次一致——复盘走的确是正式决策的同一条路径。
        // "实际决策"= D:pass，或依停手阈值本将 Pass、被决胜一手改落（LastDecisive 非空）。变异 MP-1：ReviewBestMove 消费扰动子流本体 → 本测试红（基线分歧）。
        var reviews = new Dictionary<PlayerId, List<(BestMoveReview Review, int Index)>>();
        var deciders = new List<HeuristicTurnController>();
        var decisive = new List<bool>();
        string actual = LinkDecisionSequence.Export(3, LinkDecisionSequence.BaselineRounds, AiDifficulty.Standard, session => session.ObserveDeploy(batch =>
        {
            if (deciders.Count > 0)
            {
                decisive.Add(deciders[^1].LastDecisive is not null);   // 上一次部署是否走了决胜一手
            }

            PlayerId player = batch.Context.Player;
            HeuristicTurnController ai = session.AiOf(player)!;
            var shadow = new StagedBatch(batch.Board, batch.Context);
            MatchFlow match = session.Match;
            BestMoveReview review = ai.ReviewBestMove(shadow, () => BatchRehearsal.Rehearse(shadow.Board, shadow.Context, shadow.Placements, match.History));
            Assert.Empty(batch.Placements);
            (reviews.TryGetValue(player, out var list) ? list : reviews[player] = []).Add((review, deciders.Count));
            deciders.Add(ai);
        }));
        decisive.Add(deciders[^1].LastDecisive is not null);

        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", "decisions-link1-seed3.txt")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);

        // 逐玩家对照：第 i 次复盘 ↔ 该玩家第 i 条 D: 决策。
        int total = 0;
        foreach ((PlayerId player, List<(BestMoveReview Review, int Index)> list) in reviews)
        {
            string[] deploys = [.. expected.Split('\n')
                .Where(l => l.Split('\t') is [var round, var who, _] && int.TryParse(round, out _) && who == player.ToString())
                .SelectMany(l => l.Split('\t')[2].Split(' ').Where(d => d.StartsWith("D:", StringComparison.Ordinal)))];
            Assert.Equal(deploys.Length, list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                bool wouldPass = deploys[i] == "D:pass" || decisive[list[i].Index];
                Assert.True(list[i].Review.ImpliesPass == wouldPass, $"{player} 第 {i + 1} 次部署：复盘 {list[i].Review}，实际 {deploys[i]}");
            }

            total += list.Count;
        }

        // map-expand-25 段 A2：基线截断由 12 改为 LinkDecisionSequence.BaselineRounds（6）个大回合，部署次数上限 24，下界 40 → 20。
        Assert.True(total >= 20, $"样本口径：只复盘了 {total} 次部署。");

        // 决胜一手那条腿用手写局面确定地走到（link-map-terrain 裁决 T1 = A）：平坦手写图上 P0 落后、其余三人已 Pass、权重写死使唯一单点评价为负——
        // 同一个 AI 对象先复盘、再正式部署，复盘结论"本将 Pass"、正式决策被决胜一手改落。
        // open-map：唯一战场上"对手"是全部其他在局玩家，P1、P2、P3 都先 Pass 才构成"最后一个 Pass 会输"的局面。
        MatchFlow trailing = StartedFlat([0, 0, 3, 4]).Place(P0, BirthCell(FlatMap, 0, FlatMap.BirthBoard(0).Width - 1, 0)).Place(P1, BirthCell(FlatMap, 0, 0, 0), BirthCell(FlatMap, 0, 1, 0));
        trailing.Debug.SetOrder(P1, P2, P3, P0);
        foreach (PlayerId _ in new[] { P1, P2, P3 })
        {
            trailing.ToDeploy();
            trailing.Pass();
        }

        Assert.Equal(P0, trailing.CurrentPlayer);
        var decider = new HeuristicTurnController(P0, trailing.Publish, trailing.Seed.Stream(HeuristicAi.StreamName(P0)), AiDifficulty.Easy,
            SimFixtures.PreCalibrationWeights with { PowerGain = -1, EnemyLoss = 0 }, AiSearchConfig.Easy with { PassThreshold = Threshold });
        StagedBatch real = trailing.ToDeploy();
        var probe = new StagedBatch(real.Board, real.Context);
        BestMoveReview leg = decider.ReviewBestMove(probe, () => BatchRehearsal.Rehearse(probe.Board, probe.Context, probe.Placements, trailing.History));
        decider.Deploy(real, trailing.Rehearse);
        Assert.True(leg.ImpliesPass, $"复盘 {leg}");
        Assert.NotNull(decider.LastDecisive);
        Assert.False(decider.LastChoice!.IsPass);

        // 对局日志同样逐字节不变：同一种子（四人四区、截到第 13 个小回合）挂与不挂复盘各跑一次，确定性文本相同。
        MatchLog plain = MatchSession.Create(LinkSimFixtures.Config(turnLimit: 13), LinkSimFixtures.FourZoneSeed.Value).Run();
        MatchSession probed = MatchSession.Create(LinkSimFixtures.Config(turnLimit: 13), LinkSimFixtures.FourZoneSeed.Value);
        int probes = 0;
        probed.ObserveDeploy(batch =>
        {
            var shadow = new StagedBatch(batch.Board, batch.Context);
            MatchFlow match = probed.Match;
            _ = probed.AiOf(batch.Context.Player)!.ReviewBestMove(shadow, () => BatchRehearsal.Rehearse(shadow.Board, shadow.Context, shadow.Placements, match.History));
            probes++;
        });
        Assert.Equal(plain.DeterministicText(), probed.Run().DeterministicText());
        Assert.Equal(13, probes);
    }

    // map-expand-25 段 A3（用户裁决 2026-10-26）：归入慢测试（Slow 类别、默认跳过）——浮空岛大图上需要自然终局，单条 7–13 分钟；
    // 落败复盘在 link:1:4 上找不到整轮 Pass 结算样本（DefeatReplay.TargetsOf 为空，SIEGE_SLOW=1 下仍红，待换载体 / 改样本口径）。
    [SlowFact]
    [Trait("Category", "Slow")]
    public void 落败者复盘可查()
    {
        // 规格 Scenario「落败者复盘可查」：一局真实 AI 链接局（四人四区种子、简单难度、跑到终局）按日志回放，每名整轮 Pass 结算落败者都复盘到，
        // open-map：唯一战场整轮 Pass 结算即终局，落败者就是终局时除胜者外的三人（填满 / 减员终局的局没有复盘对象，样本口径下面断言非空）。
        // 复盘结论与其实际 Pass 一致；报告列出逐人明细与"本可翻盘"占比。
        // 反面：脚本控制者跑出的日志（首部写的是 AI 配置）回放第 1 小回合即分歧 → 整局记为未能复盘，不给半截结论。
        // 变异 MP-5：回放核对 SameMove 恒为真 → 脚本日志被"复盘"（Reviewed > 0）即红。
        MatchLog log = RealLog.Value;
        Assert.NotNull(log.Result);
        (IReadOnlyList<DefeatTarget> targets, int missing) = DefeatReplay.TargetsOf(log);
        Assert.NotEmpty(targets);   // 样本口径
        Assert.Equal(0, missing);

        DefeatReviewSection section = PassReviewAnalyzer.ReviewDefeats([log])!;
        Assert.Empty(section.SkippedLogs);
        Assert.Equal(targets.Count, section.Defeats);
        Assert.Equal(targets.Count, section.Reviewed);
        Assert.Equal(section.Reviewed, section.Consistent);
        Assert.All(section.Entries, e => Assert.Equal(targets.Single(t => t.Turn == e.Target.Turn), e.Target));
        Assert.All(section.Decisive, d => Assert.True(d.Entry.Move.Outcome.ComesBack));   // 决胜一手（2.4 收集）都满足追平 / 反超

        string report = ReportWriter.Render(BalanceAnalyzer.Analyze([log], new AnalysisOptions { ReviewDefeats = true }));
        Assert.Contains("## 战场落败复盘", report, StringComparison.Ordinal);
        Assert.Contains($"复盘到 {targets.Count} 人次", report, StringComparison.Ordinal);
        Assert.Contains("本可翻盘", report, StringComparison.Ordinal);
        Assert.DoesNotContain("## 战场落败复盘", ReportWriter.Render(BalanceAnalyzer.Analyze([log])), StringComparison.Ordinal);   // 缺省不回放

        MatchLog scripted = LinkSimFixtures.PlayScripted([0, 1, 2, 3]).Log;
        DefeatReviewSection skipped = PassReviewAnalyzer.ReviewDefeats([scripted])!;
        Assert.Equal(0, skipped.Reviewed);
        Assert.Equal(1, skipped.SkippedLogs.Values.Sum());
        Assert.StartsWith("回放在第 1 小回合分歧", skipped.SkippedLogs.Keys.Single(), StringComparison.Ordinal);
    }

    // map-expand-25 段 A3（用户裁决 2026-10-26）：与「落败者复盘可查」共用 RealLog（一局跑到自然终局的真实 AI 链接局），同归慢测试——
    // 浮空岛大图上需要自然终局，单条 7–13 分钟；落败复盘在 link:1:4 上找不到整轮 Pass 结算样本。
    [SlowFact]
    [Trait("Category", "Slow")]
    public void 分析命令的复盘开关()
    {
        // 裁决 P10（tasks 2.3）：analyze 缺省不做战场落败复盘（不回放）；--review-defeats 开启复盘。用法文本列出该开关。
        // open-map：没有等待者评估段（段 B 删除该分析），这里断言它不出现。
        // 变异 MP-7：开关读取改回恒为 true → 缺省也出复盘段，本测试红。
        string dir = SimFixtures.TempDir("analyze-review-defeats");
        RealLog.Value.WriteTo(dir);

        (int code, string output, string err) = LinkSimFixtures.RunMain(null, null, "analyze", "--dir", dir);
        Assert.True(code == 0, err);
        Assert.DoesNotContain("## 战场落败复盘", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## 等待者评估", output, StringComparison.Ordinal);

        (int reviewCode, string reviewed, string reviewErr) = LinkSimFixtures.RunMain(null, null, "analyze", "--dir", dir, "--review-defeats");
        Assert.True(reviewCode == 0, reviewErr);
        Assert.Contains("## 战场落败复盘", reviewed, StringComparison.Ordinal);
        Assert.Contains("决胜一手", reviewed, StringComparison.Ordinal);
        Assert.DoesNotContain("## 等待者评估", reviewed, StringComparison.Ordinal);

        (_, string usage, _) = LinkSimFixtures.RunMain(null, null);
        Assert.Contains("[--review-defeats]", usage, StringComparison.Ordinal);
    }
}
