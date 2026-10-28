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
/// relic-redesign 段 C1（design.md D5）：信物维按势力增量估值（<see cref="RelicEstimate"/> 三桶：己方 / 参赛敌方持物的军势边际贡献、相邻者对地图信物格 / 预告格的拾取机会折价）
/// 与 AI 挂出带入件的落点规划（<see cref="RelicAttachPlan"/>：挂在本批势力增量最大的落点、零随机）。载体：平坦 8×8 链接载体（<see cref="LinkModeFixtures.PlainStarted()"/>）第 5 大回合。
/// 变异验证（C1 实跑，均已恢复）：MC1-1 <c>PowerCalculator.Evaluate</c> 的 <c>RelicContribution</c> 恒 0 → 红 4（本类 3 条 + 新棋子与新信物的AI适配Tests.拾取信物进入即时势力增量与信物维）；
/// MC1-2 <c>RelicEstimate.Score</c> 对敌方持物不取负 → 红 1（敌方持物棋子的提子估值高于普通提子）；MC1-3 <c>Survey</c> 不纳入挂在棋子上的信物 → 红 1（同上，4 变 3）；
/// MC1-4 预告格与地图格同一折价 → 红 1（预告格折价）；MC1-5 <c>RelicAttachPlan</c> 并列时取后见者 → 红 1（并列打破）。
/// </summary>
public class 信物估值与挂出Tests
{
    private static readonly PlayerId P0 = TestMaps.P0;
    private static readonly PlayerId P1 = TestMaps.P1;

    private static MatchFlow Round5() => LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All);

    /// <summary>对 <paramref name="build"/> 新建的局面评价一手（每次评价都重建局面：一局只能开一次部署）。</summary>
    private static EvaluationBreakdown Evaluate(Func<MatchFlow> build, PlayerId me, string cell, PieceType type = PieceType.Basic)
    {
        MatchFlow match = build();
        StagedBatch batch = match.OpenDeploy();
        BatchEvaluator evaluator = HeuristicAi.Create(match, me).CreateEvaluator();
        RehearsalResult result = match.RehearseBatch(batch, (cell, type));
        Assert.True(result.IsLegal);
        return evaluator.Evaluate(batch.Placements, result, batch.Context);
    }

    [Fact]
    public void 有信物格的落点估值高于无信物格的同形落点()
    {
        // D5「对每个地图信物格估拾取后己方势力增量」：G7 空格上有一件地图战鼓、C3 无；两处都是落一枚孤子，形状相同。
        // 拾取 = 己方持物项出现：孤子 + 战鼓的军势边际贡献 ⌊(1 + 3) × 1⌋ − ⌊1 × 1⌋ = 3（G7 决策前无人相邻，机会项为 0）；C3 为 0。
        // 该量来自唯一势力计算的 GroupPower.RelicContribution，不是 AI 自算的 +3。
        MatchFlow Drum()
        {
            MatchFlow match = Round5();
            match.Board.AttachRelic(TestMaps.At("G7"), RelicToken.Spawned(0, RelicKind.WarDrum));
            match.Debug.Recalculate();
            return match;
        }

        EvaluationBreakdown pick = Evaluate(Drum, P0, "G7");
        EvaluationBreakdown plain = Evaluate(Drum, P0, "C3");

        Assert.Equal(3, pick.RawOf(EvaluationDimension.Relic));
        Assert.Equal(0, plain.RawOf(EvaluationDimension.Relic));
        Assert.True(pick.RawOf(EvaluationDimension.Relic) > plain.RawOf(EvaluationDimension.Relic));

        // 军旗接到倍增串上：D4 / D5 倍增子 + D6 普通子（基础 3、指数 2 → ⌊3 × 9/4⌋ = 6），D7 空格上有军旗，落 D7 后基础 4、指数 3 → ⌊4 × 27/8⌋ = 13，不挂军旗 ⌊4 × 9/4⌋ = 9，边际 4。
        MatchFlow Banner()
        {
            MatchFlow banner = Round5();
            banner.Board.Place(TestMaps.At("D4"), P0, PieceType.Multiplier);
            banner.Board.Place(TestMaps.At("D5"), P0, PieceType.Multiplier);
            banner.Board.Place(TestMaps.At("D6"), P0, PieceType.Basic);
            banner.Board.AttachRelic(TestMaps.At("D7"), RelicToken.Spawned(0, RelicKind.Banner));
            banner.Debug.Recalculate();
            return banner;
        }

        // 决策前己方已与 D7 相邻：机会项 4 / 2 = 2 先在"前"里，拾取后换成持物项 4 → 增量 4 − 2 = 2。
        Assert.Equal(2, Evaluate(Banner, P0, "D7").RawOf(EvaluationDimension.Relic));
    }

    [Fact]
    public void 敌方持物棋子的提子估值高于普通提子()
    {
        // D5「对敌方持物棋子估提走后掉落可被己方拾取的价值」：P1 孤子 F6 只剩一口气 G6，P0 E6 / F5 / F7 围着它。
        // 有信物：F6 上挂战鼓（P1 持有，边际 3）。前 = −3（敌方持物）；G6 提走 F6 后信物留在 F6 成为地图信物，四邻全是 P0 → 机会项 +⌊3 / 2⌋ = 1（P0 落 F6 并入 5 子串的边际仍是 3）。
        // 增量 = 1 − (−3) = 4；无信物的同形提子为 0。
        MatchFlow Plain() => Round5().Stones(P0, "E6", "F5", "F7").Stones(P1, "F6", "C3");
        MatchFlow Holder()
        {
            MatchFlow holder = Plain();
            holder.Board.AttachRelic(TestMaps.At("F6"), RelicToken.Spawned(0, RelicKind.WarDrum));
            holder.Debug.Recalculate();
            return holder;
        }

        EvaluationBreakdown captureHolder = Evaluate(Holder, P0, "G6");
        EvaluationBreakdown capturePlain = Evaluate(Plain, P0, "G6");

        Assert.Equal(4, captureHolder.RawOf(EvaluationDimension.Relic));
        Assert.Equal(0, capturePlain.RawOf(EvaluationDimension.Relic));
        Assert.True(captureHolder.RawOf(EvaluationDimension.Relic) > capturePlain.RawOf(EvaluationDimension.Relic));
        // 提子本身（敌损维）两边相同：差只在信物维。
        Assert.Equal(capturePlain.RawOf(EvaluationDimension.EnemyLoss) + 3, captureHolder.RawOf(EvaluationDimension.EnemyLoss));   // 敌方失去的势力多出战鼓的 3
    }

    [Fact]
    public void 预告格折价低于地图信物格且敌方相邻取负()
    {
        // D5「含预告格的折价」：D4 / D5 倍增子 + D6 普通子（P0），D7 若有军旗、P0 落 D7 的边际 4（见上）。
        //   地图信物（D7 上已有军旗）→ P0 相邻：机会项 ⌊4 / 2⌋ = 2；
        //   预告格（D7 尚无信物，公开预告第 6 大回合落军旗）→ 机会项 ⌊4 / 4⌋ = 1（落位前不可拾取，折得更多）；
        //   同一预告格对 P1：P1 孤子 E7 也与 D7 相邻 → P1 的边际 = 孤子挂军旗 ⌊1 × 3/2⌋ − 1 = 0 → 0；换成 P1 的倍增串相邻则为负。
        // 直接对 RelicEstimate 的 Survey / Score 断言（预告由对局流程在第 3 / 5 大回合给出，这里合成一份）。
        MatchFlow match = Round5();
        match.Board.Place(TestMaps.At("D4"), P0, PieceType.Multiplier);
        match.Board.Place(TestMaps.At("D5"), P0, PieceType.Multiplier);
        match.Board.Place(TestMaps.At("D6"), P0, PieceType.Basic);
        match.Debug.Recalculate();
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = match.Roster;
        ScoringVersion scoring = match.ScoringVersion;
        Coord d7 = TestMaps.At("D7");

        GameBoard onBoard = match.Board.Clone();
        onBoard.AttachRelic(d7, RelicToken.Spawned(0, RelicKind.Banner));
        PowerSnapshot onBoardPower = PowerCalculator.Compute(onBoard, roster, scoring);
        RelicEstimate.RelicSurvey mapSurvey = RelicEstimate.Survey(onBoard, onBoardPower, null, roster, scoring);
        BigInteger mapScore = RelicEstimate.Score(onBoard, onBoardPower, P0, roster, mapSurvey);

        var forecast = new RelicForecast(6, [new RelicSpawnSlot(d7, RelicKind.Banner)]);
        PowerSnapshot power = match.Scoreboard.Latest!;
        RelicEstimate.RelicSurvey forecastSurvey = RelicEstimate.Survey(match.Board, power, forecast, roster, scoring);
        BigInteger forecastScore = RelicEstimate.Score(match.Board, power, P0, roster, forecastSurvey);

        Assert.Equal(2, mapScore);
        Assert.Equal(1, forecastScore);
        Assert.True(forecastScore < mapScore);
        RelicEstimate.RelicOpportunity cell = Assert.Single(forecastSurvey.Cells);
        Assert.True(cell.IsForecast);
        Assert.Equal(4, cell.GainBy[P0]);

        // 敌方相邻取负：P1 的倍增串 C7 / C8 也挨着 D7（C7 与 D7 气边相邻），P1 落 D7 的边际 = 串 (C7, C8, D7) 基础 3、指数 2 挂军旗 ⌊3 × 27/8⌋ − ⌊3 × 9/4⌋ = 10 − 6 = 4 → 对 P0 记 −⌊4 / 4⌋ = −1。
        match.Board.Place(TestMaps.At("C7"), P1, PieceType.Multiplier);
        match.Board.Place(TestMaps.At("C8"), P1, PieceType.Multiplier);
        match.Debug.Recalculate();
        PowerSnapshot contested = match.Scoreboard.Latest!;
        RelicEstimate.RelicSurvey contestedSurvey = RelicEstimate.Survey(match.Board, contested, forecast, roster, scoring);
        Assert.Equal(0, RelicEstimate.Score(match.Board, contested, P0, roster, contestedSurvey));   // +1（己方）−1（敌方）
        Assert.Equal(0, RelicEstimate.Score(match.Board, contested, P1, roster, contestedSurvey));
        Assert.Equal(-2, RelicEstimate.Score(match.Board, contested, TestMaps.P2, roster, contestedSurvey));   // 第三方：两边都是敌方 → −1 −1 = −2
    }

    [Fact]
    public void 无信物无预告时信物维为零且不建表()
    {
        // 盘面没有信物、也没有预告 → Survey 为空表（一次 Clone 都不做）、Score 恒 0；评价的信物维原始值 0，与版本 12 相同。
        MatchFlow Build() => Round5().Stones(P0, "E6").Stones(P1, "F6");
        MatchFlow match = Build();
        RelicEstimate.RelicSurvey survey = RelicEstimate.Survey(match.Board, match.Scoreboard.Latest!, null, match.Roster, match.ScoringVersion);
        Assert.Same(RelicEstimate.RelicSurvey.Empty, survey);
        Assert.Equal(BigInteger.Zero, RelicEstimate.Score(match.Board, match.Scoreboard.Latest!, P0, match.Roster, survey));
        Assert.Equal(0, Evaluate(Build, P0, "G6").RawOf(EvaluationDimension.Relic));
    }

    [Fact]
    public void 挂出规划取势力增量最大的落点()
    {
        // D5「挂在本批势力增量最大的落点」：复摆后盘面有 P0 的倍增串 D4 / D5（指数 2）与本批两枚新子 D6（接到倍增串上）、H8（孤子）；带入 军旗（非保险，下标 0）、战鼓（保险，下标 1）。
        //   战鼓→D6：串 (D4, D5, D6) 基础 3 + 3 = 6，⌊6 × 9/4⌋ = 13，比不挂的 ⌊3 × 9/4⌋ = 6 多 7；战鼓→H8：+3；军旗→D6：⌊3 × 27/8⌋ = 10，多 4；军旗→H8：⌊1 × 3/2⌋ = 1，多 0。
        //   第一轮取最大 7：战鼓→D6；第二轮只剩 H8：军旗→H8（增量 0 也挂，R14 未挂出的不生效）。
        // 势力经唯一势力计算（PowerCalculator.Compute 的己方总势力），规划不自算任何效果。
        MatchFlow match = Round5();
        match.Board.Place(TestMaps.At("D4"), P0, PieceType.Multiplier);
        match.Board.Place(TestMaps.At("D5"), P0, PieceType.Multiplier);
        match.Board.Place(TestMaps.At("D6"), P0, PieceType.Basic);
        match.Board.Place(TestMaps.At("H8"), P0, PieceType.Basic);
        match.Debug.Recalculate();
        var carried = new Dictionary<int, CarriedRelic> { [0] = new(RelicKind.Banner, false), [1] = new(RelicKind.WarDrum, true) };
        BigInteger MyPower(GameBoard b) => PowerCalculator.Compute(b, match.Roster, match.ScoringVersion).Of(P0).Total;

        ImmutableArray<(Coord Cell, int Index)> plan = RelicAttachPlan.Plan(match.Board, P0, [TestMaps.At("H8"), TestMaps.At("D6")], carried, nextId: 0, MyPower);

        Assert.Equal([(TestMaps.At("D6"), 1), (TestMaps.At("H8"), 0)], plan);
        Assert.Equal(plan, RelicAttachPlan.Plan(match.Board, P0, [TestMaps.At("D6"), TestMaps.At("H8")], carried, nextId: 0, MyPower));   // 与落点给出顺序无关
        Assert.False(match.Board.HasRelics);   // 规划只在副本上挂，不碰传入的盘面
    }

    [Fact]
    public void 挂出规划并列打破确定性()
    {
        // 两枚孤子 C3 / C7、两件战鼓（下标 0 保险、下标 1 非保险）：四种组合增量都是 3。并列打破：非保险件优先 → 下标 → 坐标字典序，
        // 所以第一件是下标 1 挂 C3，第二件下标 0 挂 C7；件数多于落点时多出的留到下一批（每子 1 件）。
        MatchFlow match = Round5().Stones(P0, "C3", "C7");
        var carried = new Dictionary<int, CarriedRelic> { [0] = new(RelicKind.WarDrum, true), [1] = new(RelicKind.WarDrum, false), [2] = new(RelicKind.WarDrum, false) };
        BigInteger MyPower(GameBoard b) => PowerCalculator.Compute(b, match.Roster, match.ScoringVersion).Of(P0).Total;

        ImmutableArray<(Coord Cell, int Index)> plan = RelicAttachPlan.Plan(match.Board, P0, [TestMaps.At("C7"), TestMaps.At("C3")], carried, nextId: 5, MyPower);

        Assert.Equal([(TestMaps.At("C3"), 1), (TestMaps.At("C7"), 2)], plan);
        Assert.Empty(RelicAttachPlan.Plan(match.Board, P0, [], carried, nextId: 5, MyPower));
        Assert.Empty(RelicAttachPlan.Plan(match.Board, P0, [TestMaps.At("C3")], new Dictionary<int, CarriedRelic>(), nextId: 5, MyPower));
    }
}
