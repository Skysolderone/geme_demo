using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Tests.AiDecision;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：openspec/changes/ai-terrain-review/specs/match-telemetry —— Requirement: AI 改造与主题诊断（tasks 1.1–1.4）。
/// </summary>
/// <remarks>
/// <para>改造诊断入口（1.1）的手写局面沿用 <see cref="AI枚举改造目标Tests"/> 的载体（retire-legacy-rules 1.6 起为平坦 8×8 链接载体上四人同锁棋盘 0，原 9×9 坐标平移 (+2, +2)，预筛一条为 (+1, +1)；第 5 大回合），标准难度、权重写死为定值前缺省
/// （<see cref="SimFixtures.PreCalibrationWeights"/>，不跟随缺省表，testing.md）；合法落子范围收窄到一两格，结论只取决于这几格。</para>
/// <para>变异（1.4 实跑，记录见任务 implement.md）：MA-1 诊断消费 AI 扰动子流本体 →「诊断不改对局」红；其余见各测试注释。</para>
/// </remarks>
[Collection(ConsoleRedirect.Collection)]
public class AI改造与主题诊断Tests
{
    private static readonly PlayerId Me = TestMaps.P0;

    private static readonly EvaluationWeights Weights = SimFixtures.PreCalibrationWeights;

    private static readonly IReadOnlyDictionary<PieceType, int> ArtisansOnly = BatchFixtures.Stock((PieceType.Artisan, 5));

    /// <summary>一局真实 AI 链接局（四人四区种子、<b>标准</b>难度——简单难度几乎不落匠人，testing.md「样本口径断言必须有下界」）截到 40 个小回合，经文本往返。</summary>
    private static readonly Lazy<MatchLog> StandardLog = new(() =>
        MatchLog.Parse(MatchSession.Create(LinkSimFixtures.Config(turnLimit: 40, difficulty: AiDifficulty.Standard), LinkSimFixtures.FourZoneSeed.Value).Run().FullText()));

    /// <summary>
    /// P0 在 <paramref name="cells"/> 上做改造诊断（部署上限 1），影子批次走零副作用预演；同时返回同一 AI 对象与同上下文的正式批次，供与正式决策对照。
    /// </summary>
    private static (ArtisanEditReview Review, HeuristicTurnController Ai, StagedBatch Real, Func<RehearsalResult> Rehearse) Diagnose(
        MatchFlow match, IReadOnlyDictionary<PieceType, int> stock, AiSearchConfig? config, params string[] cells)
    {
        GameBoard board = match.Board;
        BatchContext context = BatchFixtures.Context(board, Me, 1, stock, cells.Select(TestMaps.At).ToHashSet());
        var shadow = new StagedBatch(board, context);
        var ai = new HeuristicTurnController(Me, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(Me)), AiDifficulty.Standard, Weights, config ?? AiSearchConfig.Standard);
        ArtisanEditReview review = ai.ReviewArtisanEdits(shadow, () => BatchRehearsal.Rehearse(board, shadow.Context, shadow.Placements, match.History));
        Assert.Empty(shadow.Placements);   // 诊断结束清空影子批次
        Assert.Empty(ai.Decisions);        // 不写决策留痕
        Assert.Null(ai.LastChoice);
        Assert.True(ai.LastPointRanking.IsEmpty);
        var real = new StagedBatch(board, context);
        return (review, ai, real, () => BatchRehearsal.Rehearse(board, real.Context, real.Placements, match.History));
    }

    /// <summary>
    /// 与正式决策同口径：同一 AI 对象随后正式部署，其单点排序（前 N）的第一名就是诊断的第一名；诊断里排在前 N 内的各类最好一手与正式排序同名次、同落点，
    /// 与第一名的分差相同（收益 = 总分 − Pass 总分，差值与 Pass 总分无关）。
    /// </summary>
    private static void AssertSameAsOfficialRanking(ArtisanEditReview review, HeuristicTurnController ai, StagedBatch real, Func<RehearsalResult> rehearse)
    {
        ai.Deploy(real, rehearse);
        ImmutableArray<PointScore> ranking = ai.LastPointRanking;
        ArtisanOption top = Assert.IsType<ArtisanOption>(review.Top);
        Assert.Equal(ranking[0].Placement, top.Placement);
        int checkedKinds = 0;
        foreach (ArtisanKindReview k in review.KindReviews.Where(k => k.Best is { Rank: { } r } && r <= ranking.Length))
        {
            PointScore official = ranking[k.Best!.Rank!.Value - 1];
            Assert.Equal(official.Placement, k.Best.Placement);
            Assert.Equal(ranking[0].Total - official.Total, top.Gain - k.Best.Gain);
            checkedKinds++;
        }

        Assert.True(checkedKinds > 0, "样本口径：至少一类的最好一手排在前 N 内。");
    }

    [Fact]
    public void 只有立栅目标()
    {
        // 平地（平坦 8×8 链接载体的棋盘 0），匠人只能落 G7（盘中部）：四邻都是草地 → 没有深水可搭桥、没有林地可烧；立栅候选是以 G7 四邻为一端的 16 条边（T-11）。
        // 判别力：变异 MA-2"① 不按格累计（只数进入枚举的格）"不影响本条；本条钉"无合法目标"与三层计数的基本口径。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5);
        (ArtisanEditReview review, HeuristicTurnController ai, StagedBatch real, Func<RehearsalResult> rehearse) = Diagnose(match, ArtisansOnly, null, "G7");

        Assert.Equal(1, review.LegalCells);
        Assert.Equal(1, review.CandidateCells);
        Assert.Equal(5, review.ArtisanStock);
        foreach (TerrainEditKind kind in new[] { TerrainEditKind.Bridge, TerrainEditKind.Burn })
        {
            ArtisanKindReview k = review.Of(kind);
            Assert.Equal((0, 0, 0), (k.RangeTargets, k.EnumeratedTargets, k.Candidates));
            Assert.Null(k.Best);
            Assert.Equal(0, k.OutsideCandidates);
            Assert.Equal(ArtisanKindStatus.NoTarget, review.StatusOf(kind));
        }

        ArtisanKindReview fence = review.Of(TerrainEditKind.Fence);
        Assert.Equal(16, fence.RangeTargets);
        Assert.Equal(16, fence.EnumeratedTargets);
        Assert.InRange(fence.Candidates, 1, 16);
        Assert.Equal(TestMaps.At("G7"), fence.Best!.Placement.Coord);
        Assert.Equal(TerrainEditKind.Fence, fence.Best.Placement.Edit!.Value.Kind);
        Assert.Null(fence.OutsideCandidates);   // 立栅不做预筛外打分
        Assert.Equal(new Placement(TestMaps.At("G7"), PieceType.Artisan), review.NoEdit!.Placement);
        Assert.Equal(EvaluationBreakdown.DimensionCount, fence.EditEffect.Length);   // 同格不改造在场：改造本身的逐维效果可算
        Assert.Equal(1 + fence.Candidates, review.Candidates);                       // 只有匠人：不改造 1 + 立栅候选

        AssertSameAsOfficialRanking(review, ai, real, rehearse);
    }

    [Fact]
    public void 有深水可搭桥()
    {
        // F6 / F7 深水，匠人只能落 G6：G6 的四邻里 F6 是深水（F7 是斜向，不是格目标）→ 搭桥合法目标恰 1 个且进入枚举、预演合法；没有林地。
        MatchFlow match = LinkModeFixtures.PlainStarted(TestMaps.Terrain(surfaces: [("F6", Surface.DeepWater), ("F7", Surface.DeepWater)])).AtRound(5);
        (ArtisanEditReview review, HeuristicTurnController ai, StagedBatch real, Func<RehearsalResult> rehearse) = Diagnose(match, ArtisansOnly, null, "G6");

        ArtisanKindReview bridge = review.Of(TerrainEditKind.Bridge);
        Assert.Equal((1, 1, 1), (bridge.RangeTargets, bridge.EnumeratedTargets, bridge.Candidates));
        Assert.Equal(new Placement(TestMaps.At("G6"), PieceType.Artisan, TerrainEdit.Bridge(TestMaps.At("F6"))), bridge.Best!.Placement);
        Assert.Equal(EvaluationBreakdown.DimensionCount, bridge.EditEffect.Length);
        Assert.NotEqual(ArtisanKindStatus.NoTarget, review.StatusOf(TerrainEditKind.Bridge));
        Assert.Equal(ArtisanKindStatus.NoTarget, review.StatusOf(TerrainEditKind.Burn));
        Assert.True(review.Of(TerrainEditKind.Fence).RangeTargets > 0);

        AssertSameAsOfficialRanking(review, ai, real, rehearse);
    }

    [Fact]
    public void 有林地可烧()
    {
        // H6 林地，匠人只能落 G6：烧林合法目标恰 1 个且进入枚举、预演合法；没有深水。
        MatchFlow match = LinkModeFixtures.PlainStarted(TestMaps.Terrain(surfaces: [("H6", Surface.Forest)])).AtRound(5);
        (ArtisanEditReview review, HeuristicTurnController ai, StagedBatch real, Func<RehearsalResult> rehearse) = Diagnose(match, ArtisansOnly, null, "G6");

        ArtisanKindReview burn = review.Of(TerrainEditKind.Burn);
        Assert.Equal((1, 1, 1), (burn.RangeTargets, burn.EnumeratedTargets, burn.Candidates));
        Assert.Equal(new Placement(TestMaps.At("G6"), PieceType.Artisan, TerrainEdit.Burn(TestMaps.At("H6"))), burn.Best!.Placement);
        Assert.Equal(ArtisanKindStatus.NoTarget, review.StatusOf(TerrainEditKind.Bridge));
        Assert.NotEqual(ArtisanKindStatus.NoTarget, review.StatusOf(TerrainEditKind.Burn));

        AssertSameAsOfficialRanking(review, ai, real, rehearse);
    }

    [Fact]
    public void 预筛挡掉搭桥目标即候选生成阶段排除()
    {
        // 候选格上限 K = 1：合法范围 {F5, J8}，手里有普通子与匠人。预筛按代表类型（普通子）、不带改造给每格打分——J8 是 P1 孤子 J9 的最后一口气（提子），
        // F5 只是平地一子，预筛只留 J8；F5 旁的深水 E5 因此是"有规则合法目标、却进不了完整枚举"（候选生成阶段排除）。预筛外打分给出 F5 搭桥这一手。
        // 判别力：变异 MA-2"② 改为按全部合法空格累计（去掉进入枚举的判定）"→ ② = ①、处境不再是预筛排除，本条红。
        MatchFlow match = LinkModeFixtures.PlainStarted(TestMaps.Terrain(surfaces: [("E5", Surface.DeepWater)])).AtRound(5);
        GameBoard board = match.Board;
        board.Place(TestMaps.At("J9"), TestMaps.P1, PieceType.Basic);
        foreach (string cell in new[] { "H9", "K9", "J10" })
        {
            board.Place(TestMaps.At(cell), Me, PieceType.Basic);
        }

        (ArtisanEditReview review, HeuristicTurnController ai, StagedBatch real, Func<RehearsalResult> rehearse) = Diagnose(
            match, BatchFixtures.Stock((PieceType.Basic, 5), (PieceType.Artisan, 5)), AiSearchConfig.Standard with { CandidateCellLimit = 1 }, "F5", "J8");

        Assert.Equal(2, review.LegalCells);
        Assert.Equal(1, review.CandidateCells);
        ArtisanKindReview bridge = review.Of(TerrainEditKind.Bridge);
        Assert.Equal((1, 0, 0), (bridge.RangeTargets, bridge.EnumeratedTargets, bridge.Candidates));
        Assert.Equal(ArtisanKindStatus.ExcludedByPrefilter, review.StatusOf(TerrainEditKind.Bridge));
        Assert.Equal(1, bridge.OutsideCandidates);
        ArtisanOption outside = Assert.IsType<ArtisanOption>(bridge.OutsideBest);
        Assert.Equal(new Placement(TestMaps.At("F5"), PieceType.Artisan, TerrainEdit.Bridge(TestMaps.At("E5"))), outside.Placement);
        Assert.Null(outside.Rank);   // 预筛外的单点不在正式排序里
        ArtisanKindReview fence = review.Of(TerrainEditKind.Fence);
        Assert.True(fence.EnumeratedTargets > 0 && fence.EnumeratedTargets < fence.RangeTargets, $"立栅 ① {fence.RangeTargets} ② {fence.EnumeratedTargets}");

        // 正式决策的预筛确实只留 J8（与诊断同一实现）。
        ai.Deploy(real, rehearse);
        Assert.Equal(new[] { TestMaps.At("J8") }, ai.LastCandidateCells);
    }

    [Fact]
    public void 诊断不改对局()
    {
        // 规格 Scenario「诊断不改对局」：链接图决策序列基线（link:1、种子 3、截到第 12 大回合）在<b>每一次</b>部署前都挂上改造诊断（影子批次 + 同一 AI 对象），
        // 导出仍与钉入的基线逐字节相同；诊断的停手推论（ImpliesPass）与 AI 随后的实际决策逐次一致（实际 = D:pass，或本将 Pass、被决胜一手改落）。
        // 变异 MA-1：ReviewArtisanEdits 消费扰动子流本体（多调一次 Perturb(ranked)）→ 本测试红（基线分歧）。
        var reviews = new Dictionary<PlayerId, List<(ArtisanEditReview Review, int Index)>>();
        var deciders = new List<HeuristicTurnController>();
        var decisive = new List<bool>();
        string actual = LinkDecisionSequence.Export(3, LinkDecisionSequence.BaselineRounds, AiDifficulty.Standard, session => session.ObserveDeploy(batch =>
        {
            if (deciders.Count > 0)
            {
                decisive.Add(deciders[^1].LastDecisive is not null);
            }

            PlayerId player = batch.Context.Player;
            HeuristicTurnController ai = session.AiOf(player)!;
            var shadow = new StagedBatch(batch.Board, batch.Context);
            MatchFlow match = session.Match;
            ArtisanEditReview review = ai.ReviewArtisanEdits(shadow, () => BatchRehearsal.Rehearse(shadow.Board, shadow.Context, shadow.Placements, match.History));
            Assert.Empty(batch.Placements);
            (reviews.TryGetValue(player, out var list) ? list : reviews[player] = []).Add((review, deciders.Count));
            deciders.Add(ai);
        }));
        decisive.Add(deciders[^1].LastDecisive is not null);

        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", "decisions-link1-seed3.txt")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);

        int total = 0;
        foreach ((PlayerId player, List<(ArtisanEditReview Review, int Index)> list) in reviews)
        {
            string[] deploys = [.. expected.Split('\n')
                .Where(l => l.Split('\t') is [var round, var who, _] && int.TryParse(round, out _) && who == player.ToString())
                .SelectMany(l => l.Split('\t')[2].Split(' ').Where(d => d.StartsWith("D:", StringComparison.Ordinal)))];
            Assert.Equal(deploys.Length, list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                bool wouldPass = deploys[i] == "D:pass" || decisive[list[i].Index];
                Assert.True(list[i].Review.ImpliesPass == wouldPass, $"{player} 第 {i + 1} 次部署：诊断 {list[i].Review}，实际 {deploys[i]}");
            }

            total += list.Count;
        }

        // map-expand-25 段 A2：基线截断由 12 改为 LinkDecisionSequence.BaselineRounds（6）个大回合，部署次数上限 24，下界 40 → 20。
        Assert.True(total >= 20, $"样本口径：只诊断了 {total} 次部署。");
        int artisanMoments = reviews.Values.SelectMany(l => l).Count(r => r.Review.ArtisanStock > 0);
        Assert.True(artisanMoments > 0, "样本口径：基线里没有持匠人的部署。");

        // 对局日志同样逐字节不变：同一种子（四人四区、标准难度、截到第 36 个小回合）挂与不挂诊断各跑一次，确定性文本相同。
        // link-map-surfaces 2.2：link:1 投放新地表后走法变化，原 24 个小回合里没有持匠人的部署（样本口径断言红），截断加长到 36（第 9 大回合）以恢复样本；所守性质不变。
        // map-expand-25 段 A2：link:1:4 换成浮空岛后单条 87 秒（孤立跑），截断改回 24 个小回合（第 6 大回合）——新图上 24 个小回合内已有持匠人的部署，样本口径仍成立。
        const int Turns = 24;
        var config = LinkSimFixtures.Config(turnLimit: Turns, difficulty: AiDifficulty.Standard);
        MatchLog plain = MatchSession.Create(config, LinkSimFixtures.FourZoneSeed.Value).Run();
        MatchSession probed = MatchSession.Create(config, LinkSimFixtures.FourZoneSeed.Value);
        int probes = 0, withArtisan = 0;
        probed.ObserveDeploy(batch =>
        {
            var shadow = new StagedBatch(batch.Board, batch.Context);
            MatchFlow match = probed.Match;
            ArtisanEditReview review = probed.AiOf(batch.Context.Player)!.ReviewArtisanEdits(shadow, () => BatchRehearsal.Rehearse(shadow.Board, shadow.Context, shadow.Placements, match.History));
            probes++;
            withArtisan += review.ArtisanStock > 0 ? 1 : 0;
        });
        Assert.Equal(plain.DeterministicText(), probed.Run().DeterministicText());
        Assert.Equal(Turns, probes);
        Assert.True(withArtisan > 0, $"样本口径：{Turns} 个小回合里没有持匠人的部署（{probes} 次部署）。");
    }

    [Fact]
    public void 改造诊断可查()
    {
        // 规格 Scenario「改造诊断可查」：一局标准难度的真实 AI 链接局按日志回放，每次持匠人的 AI 部署都诊断到；与日志逐项对账：
        // ① 日志里每个落了匠人的小回合都在诊断时刻里；② 诊断明细里按类别累计的实际改造 = 日志快照改造表的逐类计数；③ 诊断推论"不会 Pass"的时刻实际都落了子。
        // 反面：脚本控制者跑出的日志回放第 1 小回合即分歧 → 整局记为未能复盘。
        // 变异 MA-4：回放驱动去掉"手里有匠人"的判定（每次 AI 部署都诊断）→ 本测试红。
        MatchLog log = StandardLog.Value;
        ArtisanReviewSection section = ArtisanReviewAnalyzer.Review([log])!;
        Assert.Empty(section.SkippedLogs);
        Assert.NotEmpty(section.Items);   // 样本口径
        Assert.All(section.Items, i => Assert.True(i.Entry.Review.ArtisanStock > 0 && i.Entry.Review.DeployLimit > 0, $"T{i.Entry.Turn}"));
        Assert.All(section.Items, i => Assert.NotNull(i.Theme));

        HashSet<int> reviewed = [.. section.Items.Select(i => i.Entry.Turn)];
        int[] artisanTurns = [.. log.Turns.Where(t => t.Placements.Any(p => p.EndsWith(":Artisan", StringComparison.Ordinal))).Select(t => t.Turn)];
        Assert.NotEmpty(artisanTurns);   // 样本口径：这局确实落过匠人
        Assert.All(artisanTurns, t => Assert.Contains(t, reviewed));

        foreach (TerrainEditKind kind in ArtisanEditReview.Kinds)
        {
            int logged = log.Turns.Where(t => reviewed.Contains(t.Turn)).Sum(t => (t.Edits ?? []).Count(e => e.Action == kind.ToString()));
            int diagnosed = section.Items.Sum(i => i.Entry.Choice.Count(c => c.ToString() == kind.ToString()));
            Assert.Equal(logged, diagnosed);
        }

        Assert.All(section.Items.Where(i => !i.Entry.Review.ImpliesPass), i => Assert.False(i.Entry.Choice.SequenceEqual([ArtisanChoice.Pass]), $"T{i.Entry.Turn}"));

        string report = ReportWriter.Render(BalanceAnalyzer.Analyze([log], new AnalysisOptions { ReviewArtisan = true }));
        Assert.Contains("## AI 改造诊断", report, StringComparison.Ordinal);
        Assert.Contains($"匠人可改造时刻 {section.Items.Count} 次", report, StringComparison.Ordinal);
        Assert.Contains("候选生成阶段排除", report, StringComparison.Ordinal);
        Assert.DoesNotContain("## AI 改造诊断", ReportWriter.Render(BalanceAnalyzer.Analyze([log])), StringComparison.Ordinal);   // 缺省不回放

        MatchLog scripted = LinkSimFixtures.PlayScripted([0, 1, 2, 3]).Log;
        ArtisanReviewSection skipped = ArtisanReviewAnalyzer.Review([scripted])!;
        Assert.Empty(skipped.Items);
        Assert.StartsWith("回放在第 1 小回合分歧", Assert.Single(skipped.SkippedLogs).Key, StringComparison.Ordinal);
    }

    [Fact]
    public void 主题表现可查()
    {
        // 规格 Scenario「主题表现可查」（open-map B6：原"首轮战场胜率"改为整局胜率）：三份样本——A 四人四区、C 3+1（都在第 2 大回合整轮 Pass 结算即终局：各 4 人有名次、1 胜）；
        // T 四人四区截到第 13 个小回合（截断、无名次，只计锁定人次）。合计：锁定 12 人次、有名次 8 人次、胜 2；第 3 大回合结束时势力只有 T 有第 3 大回合快照（4 人）。
        // 判别力：变异"胜场改按截断局也计"→ 有名次人次 / 胜场对不上，本条红。
        MatchLog a = MatchLog.Parse(LinkSimFixtures.PlayScripted([0, 1, 2, 3]).Log.FullText());
        MatchLog c = MatchLog.Parse(LinkSimFixtures.PlayScripted([0, 0, 0, 1]).Log.FullText());
        MatchLog t = MatchLog.Parse(LinkSimFixtures.Round4Log.Value.FullText());
        Assert.True(t.Result!.Truncated);
        Assert.All(new[] { a, c }, l => Assert.Equal(4, l.Result!.Standings.Count));   // 样本口径：A、C 有名次
        Assert.All(new[] { a, c }, l => Assert.Empty(l.Turns.Where(x => x.MajorRound == 3)));   // 样本口径：A、C 第 2 大回合即终局
        ThemeSection section = ThemeAnalyzer.Analyze([a, c, t])!;

        Assert.Equal(12, section.Themes.Sum(s => s.Seats));
        Assert.Equal(8, section.Themes.Sum(s => s.RankedSeats));
        Assert.Equal(2, section.Themes.Sum(s => s.Wins));
        Assert.Equal(2, section.RankedMatches);
        foreach (MatchLog log in new[] { a, c })
        {
            int winner = log.Result!.Winners.Single();
            BoardTheme theme = log.Header.Boards![log.Header.Zones[winner]].Theme!.Value;
            Assert.True(section.Themes.Single(s => s.Theme == theme).Wins >= 1);
        }

        // 第 3 大回合结束时势力：只有 T 有第 3 大回合快照，4 人；合计等于该快照四人势力之和。
        Assert.Equal(4, section.Themes.Sum(s => s.Round3Powers.Count));
        BigInteger expected = t.Turns.Last(x => x.MajorRound == 3).PlayersState.Aggregate(BigInteger.Zero, (sum, p) => sum + p.Total);
        Assert.Equal(expected, section.Themes.SelectMany(s => s.Round3Powers).Aggregate(BigInteger.Zero, (x, y) => x + y));
        Assert.All(section.Themes.SelectMany(s => s.Round3PowerRanks), r => Assert.InRange(r, 1, 4));

        string report = ReportWriter.Render(BalanceAnalyzer.Analyze([a, c, t]));
        int performance = report.IndexOf("## 主题表现", StringComparison.Ordinal);
        Assert.True(performance > report.IndexOf("## 按主题统计", StringComparison.Ordinal));
        Assert.Contains("整局胜", report, StringComparison.Ordinal);
        Assert.DoesNotContain("首轮战场", report, StringComparison.Ordinal);
        Assert.DoesNotContain("等待者评估", report, StringComparison.Ordinal);

        // 旧日志（非链接模式）的报告没有这一段。
        MatchLog[] old = [SimFixtures.Synthetic(1, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(6, [0]))];
        Assert.DoesNotContain("主题表现", ReportWriter.Render(BalanceAnalyzer.Analyze(old)), StringComparison.Ordinal);
    }

    [Fact]
    public void 分析命令的改造诊断开关()
    {
        // tasks 1.2：analyze 缺省不做改造诊断（不回放），主题表现照常输出；--review-artisan 开启，新段只追加在报告末尾（缺省报告是它的前缀）。用法文本列出该开关。
        // 变异 MA-3：开关读取改回恒为 true → 缺省也出改造诊断段，本测试红。
        string dir = SimFixtures.TempDir("analyze-review-artisan");
        StandardLog.Value.WriteTo(dir);
        string plainPath = Path.Combine(dir, "plain.txt");
        string reviewedPath = Path.Combine(dir, "reviewed.txt");

        (int code, string output, string err) = LinkSimFixtures.RunMain(null, null, "analyze", "--dir", dir, "--out", plainPath);
        Assert.True(code == 0, err);
        Assert.DoesNotContain("## AI 改造诊断", output, StringComparison.Ordinal);
        Assert.Contains("## 主题表现", output, StringComparison.Ordinal);

        (int reviewCode, string reviewed, string reviewErr) = LinkSimFixtures.RunMain(null, null, "analyze", "--dir", dir, "--review-artisan", "--out", reviewedPath);
        Assert.True(reviewCode == 0, reviewErr);
        Assert.Contains("## AI 改造诊断", reviewed, StringComparison.Ordinal);
        Assert.StartsWith(File.ReadAllText(plainPath), File.ReadAllText(reviewedPath), StringComparison.Ordinal);

        (_, string usage, _) = LinkSimFixtures.RunMain(null, null);
        Assert.Contains("[--review-artisan]", usage, StringComparison.Ordinal);
    }
}
