using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Tests.AiDecision;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：openspec/changes/ai-pass-review/specs/match-telemetry —— Requirement: 战场落败复盘与等待者评估（tasks 1.1–1.4）。
/// </summary>
/// <remarks>
/// <para>复盘入口（1.1）的三例手写局面沿用 <see cref="链接模式下落后者的停手阈值Tests"/> 的口径：<c>link:1</c>，P0、P1 同在出生棋盘 0（双人战场），
/// 简单难度、权重写死为定值前缺省并把 PowerGain 取 1，停手阈值写死 20；合法落子范围只给一格，结论只取决于这一格。</para>
/// <para>变异（1.4 实跑，记录见任务 implement.md）：MP-1 复盘改为消费 AI 扰动子流本体（<c>ReviewBestMove</c> 里多调一次 <c>Perturb(ranked)</c>）→
/// 「复盘不改对局」红；其余见各测试注释。</para>
/// </remarks>
[Collection(ConsoleRedirect.Collection)]
public class 战场落败复盘与等待者评估Tests
{
    private const int Threshold = 20;

    private static readonly EvaluationWeights Weights = SimFixtures.PreCalibrationWeights with { PowerGain = 1 };

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
        // 期望：最好一手 = 这一格；落下后（对手不回应）P0 势力 ≥ P1 → 本可翻盘；P0 落后 → 实际阈值取 0（C15），收益 > 0 → 正式决策不会 Pass。
        // 另：P2 在别的战场（棋盘 3）势力远高于 P0——"本战场最高者"只看同战场，P2 不算对手。
        // 判别力：变异 MP-3"对手不限本战场（取全部其他在局玩家）"→ RivalBefore 变成 P2 的势力、翻盘判否，本测试红。
        Coord cell = BirthCell(Map, 0, 2, 3);
        MatchFlow match = Started([0, 0, 3, 4]).Place(P0, BirthCell(Map, 0, 4, 0)).Place(P1, BirthCell(Map, 0, 0, 0), BirthCell(Map, 0, 1, 0))
            .Place(P2, BirthCell(Map, 3, 0, 0), BirthCell(Map, 3, 1, 0), BirthCell(Map, 3, 2, 0), BirthCell(Map, 3, 0, 1), BirthCell(Map, 3, 1, 1), BirthCell(Map, 3, 2, 1));
        MatchPublicView view = match.Publish();
        Assert.True(view.Power!.Of(P0).Total < view.Power.Of(P1).Total);   // 样本口径：P0 落后

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
        Assert.True(view.Power.Of(P2).Total > best.PowerAfter, $"P2 {view.Power.Of(P2).Total}");   // 样本口径：战场外的 P2 高于 P0 落下后的势力
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
        Coord cell = BirthCell(Map, 0, 2, 2);
        MatchFlow match = Started([0, 0, 3, 4])
            .Place(P0, BirthCell(Map, 0, 4, 4))
            .Place(P1, BirthCell(Map, 0, 1, 2), BirthCell(Map, 0, 3, 2), BirthCell(Map, 0, 2, 1), BirthCell(Map, 0, 2, 3));

        BestMoveReview review = Review(match, cell);

        Assert.Equal(1, review.LegalCells);
        Assert.Equal(0, review.Candidates);
        Assert.Equal(NoMoveReason.NoLegalCandidate, review.NoMove);
        Assert.Null(review.Best);
        Assert.Null(review.Forced);
        Assert.True(review.ImpliesPass);
        Assert.Equal(0, review.ComebackCandidates);
        Assert.Equal(1, review.Rejections[$"预演不合法:{BatchFailureKind.Suicide}"]);

        BestMoveReview empty = Review(match, BirthCell(Map, 0, 0, 4), stock: 0);
        Assert.Equal(NoMoveReason.NoStock, empty.NoMove);
        Assert.True(empty.ImpliesPass);
    }

    [Fact]
    public void 能提子的一手()
    {
        // 局面：P1 一枚子三面被 P0 围住，P0 只能落它的最后一口气 → 提 1 子。最好一手的提子数与"提子候选"都计入。
        Coord stone = BirthCell(Map, 0, 2, 2);
        Coord cell = BirthCell(Map, 0, 2, 3);
        MatchFlow match = Started([0, 0, 3, 4])
            .Place(P0, BirthCell(Map, 0, 1, 2), BirthCell(Map, 0, 3, 2), BirthCell(Map, 0, 2, 1))
            .Place(P1, stone, BirthCell(Map, 0, 0, 4));
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
        // 导出仍与钉入的基线逐字节相同；并且复盘结论（ImpliesPass）与 AI 随后的实际决策（D:pass 与否）逐次一致——复盘走的确是正式决策的同一条路径。
        // 变异 MP-1：ReviewBestMove 消费扰动子流本体 → 本测试红（基线分歧）。
        var reviews = new Dictionary<PlayerId, List<BestMoveReview>>();
        string actual = LinkDecisionSequence.Export(3, 12, AiDifficulty.Standard, session => session.ObserveDeploy(batch =>
        {
            PlayerId player = batch.Context.Player;
            HeuristicTurnController ai = session.AiOf(player)!;
            var shadow = new StagedBatch(batch.Board, batch.Context);
            MatchFlow match = session.Match;
            BestMoveReview review = ai.ReviewBestMove(shadow, () => BatchRehearsal.Rehearse(shadow.Board, shadow.Context, shadow.Placements, match.History));
            Assert.Empty(batch.Placements);
            (reviews.TryGetValue(player, out List<BestMoveReview>? list) ? list : reviews[player] = []).Add(review);
        }));

        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", "decisions-link1-seed3.txt")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);

        // 逐玩家对照：第 i 次复盘 ↔ 该玩家第 i 条 D: 决策。
        int total = 0;
        foreach ((PlayerId player, List<BestMoveReview> list) in reviews)
        {
            string[] deploys = [.. expected.Split('\n')
                .Where(l => l.Split('\t') is [var round, var who, _] && int.TryParse(round, out _) && who == player.ToString())
                .SelectMany(l => l.Split('\t')[2].Split(' ').Where(d => d.StartsWith("D:", StringComparison.Ordinal)))];
            Assert.Equal(deploys.Length, list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                Assert.True(list[i].ImpliesPass == (deploys[i] == "D:pass"), $"{player} 第 {i + 1} 次部署：复盘 {list[i]}，实际 {deploys[i]}");
            }

            total += list.Count;
        }

        Assert.True(total >= 40, $"样本口径：只复盘了 {total} 次部署。");

        // 对局日志同样逐字节不变：同一种子（四人四区、截到第 13 个小回合，含两次链接）挂与不挂复盘各跑一次，确定性文本相同。
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

    [Fact]
    public void 落败者复盘可查()
    {
        // 规格 Scenario「落败者复盘可查」：一局真实 AI 链接局（四人四区种子、简单难度、跑到终局）按日志回放，每名整轮 Pass 结算落败者都复盘到，
        // 复盘结论与其实际 Pass 一致；报告列出逐人明细与"本可翻盘"占比。
        // 反面：脚本控制者跑出的日志（首部写的是 AI 配置）回放第 1 小回合即分歧 → 整局记为未能复盘，不给半截结论。
        // 变异 MP-5：回放核对 SameMove 恒为真 → 脚本日志被"复盘"（Reviewed > 0）即红。
        MatchLog log = MatchLog.Parse(MatchSession.Create(LinkSimFixtures.Config(turnLimit: 600), LinkSimFixtures.FourZoneSeed.Value).Run().FullText());
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

    [Fact]
    public void 等待者决赛表现可查()
    {
        // 规格 Scenario「等待者决赛表现可查」：脚本小样本（见 链接模式平衡分析Tests）——A 四人四区：首轮 (1,2)(3,4) 都在第 4 大回合结算 → 两名首轮刚结算的胜者；
        // B 2+1+1：棋盘 1 的两人在第 2 大回合（构筑期）结算，胜者从第 4 大回合起独处 → 等待 1 个大回合（来由：同区提前结算），另一名决赛者是首轮刚结算的胜者。
        // 变异 MP-6：等待者判据由"等待 ≥ 1"改为"等待 ≥ 2"→ 本测试红。
        MatchLog a = MatchLog.Parse(LinkSimFixtures.PlayScripted([0, 1, 2, 3]).Log.FullText());
        MatchLog b = MatchLog.Parse(LinkSimFixtures.PlayScripted([0, 0, 1, 2]).Log.FullText());
        WaiterSection w = PassReviewAnalyzer.Waiters([a, b])!;

        Assert.Equal(2, w.Finals);
        Assert.Equal(4, w.Participants.Count);
        FinalistEntry waiter = Assert.Single(w.Participants, p => p.Group == FinalistGroup.Waiter);
        Assert.Equal(1, waiter.WaitingRounds);
        Assert.Equal(WaiterOrigin.SharedSettledEarly, waiter.Origin);
        Assert.Equal(3, w.Participants.Count(p => p.Group == FinalistGroup.FreshWinner && p.WaitingRounds == 0));

        // 逐局（两份样本同一对局种子，分开分析）：A 两名都是首轮胜者；B 一名等待者、一名首轮胜者；胜者与日志一致；两人决赛的势力差互为相反数。
        WaiterSection wa = PassReviewAnalyzer.Waiters([a])!;
        WaiterSection wb = PassReviewAnalyzer.Waiters([b])!;
        Assert.All(wa.Participants, p => Assert.Equal(FinalistGroup.FreshWinner, p.Group));
        Assert.Equal(waiter, Assert.Single(wb.Participants, p => p.Group == FinalistGroup.Waiter));
        Assert.Equal(b.Header.Zones[waiter.Player], b.Header.Zones[b.Events.First(e => e.Settlement is { MajorRound: 2 }).Settlement!.Winner]);
        foreach ((MatchLog log, WaiterSection section) in new[] { (a, wa), (b, wb) })
        {
            Assert.Equal(2, section.Participants.Count);
            Assert.Equal(log.Result!.Winners.Single(), Assert.Single(section.Participants, p => p.Won).Player);
            Assert.Equal(-section.Participants[0].PowerGap, section.Participants[1].PowerGap);
        }

        string report = ReportWriter.Render(BalanceAnalyzer.Analyze([a, b]));
        Assert.Contains("## 等待者评估", report, StringComparison.Ordinal);
        Assert.Contains("- 等待者：1 人次", report, StringComparison.Ordinal);
        Assert.Contains("等待大回合数分布：1×1", report, StringComparison.Ordinal);

        // 旧日志（非链接模式）的报告没有这两段。
        MatchLog[] old = [SimFixtures.Synthetic(1, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(6, [0]))];
        BalanceReport oldReport = BalanceAnalyzer.Analyze(old, new AnalysisOptions { ReviewDefeats = true });
        Assert.Null(oldReport.Waiters);
        Assert.Null(oldReport.DefeatReview);
        Assert.DoesNotContain("等待者评估", ReportWriter.Render(oldReport), StringComparison.Ordinal);
    }
}
