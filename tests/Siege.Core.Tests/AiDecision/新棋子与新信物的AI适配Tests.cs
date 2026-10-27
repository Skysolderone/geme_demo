using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：more-pieces-relics ai-decision —— Requirement: 新棋子与新信物的 AI 适配（D10）。
/// AI 只经唯一的势力计算感知新棋子与挂在棋子上的信物；"组合成长"维口径不变。
/// relic-redesign 接线段 W1：旧十类信物的估值（类型价值、分区先验期望）、工坊扩展目标与"未揭示计分信物"三组用例随旧信物删除；
/// 信物维改为按盘面信物层的最简估计（<see cref="RelicEstimate"/>，段 C 重写），由 <see cref="拾取信物进入即时势力增量与信物维"/> 钉住。
/// </summary>
public class 新棋子与新信物的AI适配Tests
{
    private static readonly PlayerId Me = TestMaps.P0;

    [Fact]
    public void 哨兵加值进入即时势力增量()
    {
        // 规格 Scenario：AI 手中只有哨兵子，两个候选落点其余条件相同——H7 经气边邻接 P1 的 G7、J7 两枚敌子，C3（角）不邻接敌子；
        // 两处都恰好新增 2 格独占空格（H7：H6 / H8；C3：C4 / D3），棋串都只有这一枚哨兵子、没有倍率 → 即时势力增量之差 = 2 × 2 = 4。
        // 差值来自唯一的势力计算：落 H7 后该棋串的哨兵加值就是 4。
        // 组合成长维口径不变（哨兵不是倍增 / 连珠 / 协同）：两处的成长原始值都是 0——变异 MC-G1（成长维计入新来源）应红。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All).Stones(TestMaps.P1, "G7", "J7");
        match.Debug.SeedHand(Me, (PieceType.Sentry, 3));
        StagedBatch batch = match.OpenDeploy();
        BatchEvaluator evaluator = HeuristicAi.Create(match, Me).CreateEvaluator();

        RehearsalResult near = match.RehearseBatch(batch, ("H7", PieceType.Sentry));
        EvaluationBreakdown nearScore = evaluator.Evaluate(batch.Placements, near, batch.Context);
        GroupPower sentry = Assert.Single(PowerCalculator.Compute(near.ProjectedBoard!, match.Roster).Of(Me).Groups);
        RehearsalResult far = match.RehearseBatch(batch, ("C3", PieceType.Sentry));
        EvaluationBreakdown farScore = evaluator.Evaluate(batch.Placements, far, batch.Context);

        Assert.Equal(4, sentry.SentryBonus);
        Assert.Equal(7, nearScore.RawOf(EvaluationDimension.PowerGain));   // 领地 2 + 基础 1 + 哨兵 4
        Assert.Equal(3, farScore.RawOf(EvaluationDimension.PowerGain));    // 领地 2 + 基础 1
        Assert.Equal(sentry.SentryBonus, nearScore.RawOf(EvaluationDimension.PowerGain) - farScore.RawOf(EvaluationDimension.PowerGain));
        Assert.Equal(0, nearScore.RawOf(EvaluationDimension.Growth));
        Assert.Equal(0, farScore.RawOf(EvaluationDimension.Growth));
    }

    [Fact]
    public void 组合成长不计新来源与信物()
    {
        // Requirement 正文："组合成长"维 MUST NOT 计入四种新来源与信物（与势力增量双计）。relic-redesign W1：原"犄角"改为挂在棋子上的百变（D3：协同额外 +1 种、位置 +2）。
        // P0 棋串 D4 协同 + D5 普通（D5 挂百变）；候选在 D6 落铁链子。
        // 成长维（旧口径，不看信物）：协同加值由 1 种 × 2 = 2 变为 2 种 × 2 = 4，铁链的 +2（棋串 3 子 − 1）不计 → 原始值 2。
        // 即时势力增量经唯一势力计算感知百变与铁链：协同 1 × (1 + 1) × 2 = 4 → 1 × (2 + 1) × 2 = 6，铁链 +2——等于独立算出的结算后势力之差。
        // 变异 MC-G1（成长维加上铁链 / 哨兵 / 旗手 / 界碑加值 → 4）、MC-G2（成长维的协同按百变多算一种 → 4）应红。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All);
        match.Board.Place(TestMaps.At("D4"), Me, PieceType.Synergy);
        match.Board.Place(TestMaps.At("D5"), Me, PieceType.Basic);
        match.Board.AttachRelic(TestMaps.At("D5"), RelicToken.Spawned(0, RelicKind.Chameleon));
        match.Debug.Recalculate();
        match.Debug.SeedHand(Me, (PieceType.Chain, 3));
        Assert.Equal(4, match.Scoreboard.Latest!.GroupContaining(Me, "D4").SynergyBonus);   // 前提：百变已进入协同

        StagedBatch batch = match.OpenDeploy();
        BatchEvaluator evaluator = HeuristicAi.Create(match, Me).CreateEvaluator();
        RehearsalResult result = match.RehearseBatch(batch, ("D6", PieceType.Chain));
        EvaluationBreakdown score = evaluator.Evaluate(batch.Placements, result, batch.Context);

        Assert.Equal(2, score.RawOf(EvaluationDimension.Growth));

        PowerSnapshot after = PowerCalculator.Compute(result.ProjectedBoard!, match.Roster);
        GroupPower grown = after.GroupContaining(Me, "D4");
        Assert.Equal((6, 2), (grown.SynergyBonus, grown.ChainBonus));
        Assert.Equal(after.Of(Me).Total - match.Scoreboard.Latest!.Of(Me).Total, score.RawOf(EvaluationDimension.PowerGain));
    }

    [Fact]
    public void 拾取信物进入即时势力增量与信物维()
    {
        // relic-redesign W1：落子到地图信物上即拾取（R4）。G7 空格上有一件地图刷出的战鼓（D3：持物棋子基础军势 +3）。
        // 候选 G7 普通子：即时势力增量 = 结算后势力 − 结算前势力（预演副本带信物层，由唯一势力计算给出），其中该子基础军势 1 + 3 = 4；
        // 信物维 = 挂在己方棋子上的件数 × 未校准值 5（RelicEstimate 最简估计，段 C 重写）= 5。对照候选 C3（无信物）信物维 0。
        // 变异 MC-R1（Clone 不复制信物层）→ 预演看不到拾取，基础军势与信物维都红；MC-R2（RelicEstimate 返回 0）→ 信物维红。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All);
        match.Board.AttachRelic(TestMaps.At("G7"), RelicToken.Spawned(0, RelicKind.WarDrum));
        match.Debug.Recalculate();
        StagedBatch batch = match.OpenDeploy();
        BatchEvaluator evaluator = HeuristicAi.Create(match, Me).CreateEvaluator();

        RehearsalResult pick = match.RehearseBatch(batch, ("G7", PieceType.Basic));
        EvaluationBreakdown pickScore = evaluator.Evaluate(batch.Placements, pick, batch.Context);
        PowerSnapshot after = PowerCalculator.Compute(pick.ProjectedBoard!, match.Roster);
        Assert.Equal(4, after.GroupContaining(Me, "G7").BaseTotal);
        Assert.Equal(after.Of(Me).Total - match.Scoreboard.Latest!.Of(Me).Total, pickScore.RawOf(EvaluationDimension.PowerGain));
        Assert.Equal(RelicEstimate.UncalibratedFlatValue, pickScore.RawOf(EvaluationDimension.Relic));

        RehearsalResult plain = match.RehearseBatch(batch, ("C3", PieceType.Basic));
        Assert.Equal(0, evaluator.Evaluate(batch.Placements, plain, batch.Context).RawOf(EvaluationDimension.Relic));
    }

    [Fact]
    public void AI源码不另写新加值公式()
    {
        // Requirement 正文：AI MUST NOT 另写一份新棋子或信物的加值公式——只经唯一的势力计算（PowerCalculator）感知。
        // 源码扫描 src/Siege.Core/Ai（去掉 // 注释）：不得直接调用四项新加值、不得出现其系数常量、不得自数挂在棋子上的信物效果（RelicEffects / RelicTally）。
        // 反面命中：判据在唯一实现 Scoring/PieceEffects.cs 与 Scoring/PowerCalculator.cs 里确实出现；口径下界：文件数 ≥ 10。
        // 变异 MC-T2（BatchEvaluator 里加一行 PieceEffects.SentryBonus(…) 调用）应红。
        string root = PresentationFixtures.RepoRoot();
        string[] files = Directory.GetFiles(Path.Combine(root, "src", "Siege.Core", "Ai"), "*.cs", SearchOption.AllDirectories);
        Assert.True(files.Length >= 10, $"只扫到 {files.Length} 个文件");
        string[] tokens =
        [
            "BannerBonus(", "ChainBonus(", "SentryBonus(", "BoundaryBonus(",
            "BannerPerRelic", "SentryPerForeignStone", "BoundaryPerExclusiveCell", "RelicEffects.", "RelicTally",
        ];

        var violations = new List<string>();
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            string code = LifeShape.空区与封闭眼空间Tests.StripComments(File.ReadAllText(file));
            violations.AddRange(tokens.Where(t => code.Contains(t, StringComparison.Ordinal)).Select(t => $"{Path.GetFileName(file)}: {t}"));
        }

        Assert.Empty(violations);
        string effects = File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Scoring", "PieceEffects.cs"));
        string calculator = File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Scoring", "PowerCalculator.cs"));
        Assert.All(tokens.Take(7), t => Assert.Contains(t, effects, StringComparison.Ordinal));
        Assert.Contains("RelicEffects.Tally", calculator, StringComparison.Ordinal);
    }
}
