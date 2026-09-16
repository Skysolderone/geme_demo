using Siege.Sim.Analysis;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>规格：match-telemetry —— Requirement: 平衡分析方向 / Scenario: 冲突时的盘面占用率</summary>
public class 冲突占用率口径Tests
{
    /// <summary>生成 <paramref name="count"/> 个互不相同的坐标记法，只用来凑棋子数——分析端只数个数，不解析坐标。</summary>
    private static string[] Stones(int count) =>
        [.. Enumerable.Range(0, count).Select(i => $"{Siege.Core.Board.Coord.ColumnLetters[i % 11]}{(i / 11) + 1}")];

    private static GroupEntry Group(int stones) =>
        new() { Stones = [.. Stones(stones)], Base = stones, Power = stones };

    [Fact]
    public void 冲突时的盘面占用率()
    {
        // 规格 Scenario 的算例：首次提子时盘面有 52 枚棋子、地图可落子格 85 → 该局占用率 61%（52 / 85 = 61.1%，向下取整）。
        // 口径三件事：① 取"首次提子那一小回合"的快照，不是终局快照；② 分母是该局日志首部记的可落子格，
        // 不是分析端按 MapId 重建的地图；③ 无提子的局与旧日志不进分母。
        MatchLog fiftyTwo = SimFixtures.Synthetic(
            901,
            [
                SimFixtures.Turn(1, 3, 0, [10, 5, 5, 5], ["A1:Basic"], groupsOfPlayer: [Group(30)]),
                SimFixtures.Turn(2, 5, 0, [10, 5, 5, 5], ["A2:Basic"], groupsOfPlayer: [Group(52)], captures: ["B2"]),
                SimFixtures.Turn(3, 6, 0, [10, 5, 5, 5], ["A3:Basic"], groupsOfPlayer: [Group(70)], captures: ["B3"]),
            ],
            [],
            SimFixtures.ResultOf(8, [0]),
            playableCells: 85);

        MatchLog thirtyFour = SimFixtures.Synthetic(
            902,
            [SimFixtures.Turn(1, 4, 0, [10, 5, 5, 5], ["A1:Basic"], groupsOfPlayer: [Group(34)], captures: ["C3"])],
            [],
            SimFixtures.ResultOf(8, [0]),
            playableCells: 85);

        // 被排除样本 ①：整局没有提子——分母若写成"全部局"，均值会被这局稀释，算出来就不一样。
        MatchLog noCapture = SimFixtures.Synthetic(
            903,
            [SimFixtures.Turn(1, 4, 0, [10, 5, 5, 5], ["A1:Basic"], groupsOfPlayer: [Group(80)])],
            [],
            SimFixtures.ResultOf(8, [0]),
            playableCells: 85);

        // 被排除样本 ②：denser-map 之前的旧日志，首部没有可落子格 → MUST NOT 按当前基准图的 85 回填。
        MatchLog oldLog = SimFixtures.Synthetic(
            904,
            [SimFixtures.Turn(1, 4, 0, [10, 5, 5, 5], ["A1:Basic"], groupsOfPlayer: [Group(40)], captures: ["D4"])],
            [],
            SimFixtures.ResultOf(8, [0]));
        Assert.Null(oldLog.Header.PlayableCells);

        TargetsSection t = BalanceAnalyzer.Analyze([fiftyTwo, thirtyFour, noCapture, oldLog]).Targets;

        Assert.Equal(1, t.FirstConflictOccupancy[61]);
        Assert.Equal(1, t.FirstConflictOccupancy[40]);
        Assert.Equal(2, t.FirstConflictOccupancy.Count);
        Assert.Equal(2, t.MatchesWithoutOccupancy);

        // 全批次平均：测试内独立算式，不调用被测的均值函数。
        Assert.Equal(((52d / 85) + (34d / 85)) / 2, t.MeanFirstConflictOccupancy, 10);

        // 与冲突大回合并列输出：52 枚那局的首次提子在第 5 大回合，34 枚与旧日志那两局在第 4 大回合。
        // 注意两条口径的分母不同：旧日志有提子，进冲突大回合分布，但不进占用率分布。
        Assert.Equal(1, t.FirstConflictRounds[5]);
        Assert.Equal(2, t.FirstConflictRounds[4]);
        Assert.Equal(1, t.MatchesWithoutConflict);
        Assert.Equal(3, t.FirstConflictRounds.Sum(kv => kv.Value));

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze([fiftyTwo, thirtyFour, noCapture, oldLog]));
        Assert.Contains("冲突时的盘面占用率", text, StringComparison.Ordinal);
        Assert.Contains("50.6%", text, StringComparison.Ordinal);
        Assert.Contains("61×1", text, StringComparison.Ordinal);
        Assert.Contains("未纳入 2 局", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 真实跑局把可落子格写进日志首部()
    {
        // 合成日志绕过写入路径：首部漏写可落子格时，上面那条用的是手填值，照样绿。
        // 这条走真实跑局 → 日志往返 → 报告，并用"非回填值"证伪：v3 基准图是 105，若写入路径漏写就会是 null。
        List<MatchLog> sample = SimFixtures.Sample.Value;
        Assert.All(sample, l => Assert.Equal(105, l.Header.PlayableCells));
        Assert.All(sample, l => Assert.Equal(105, MatchLog.Parse(l.DeterministicText()).Header.PlayableCells));

        // 报告段落在真实批次上也能算出来（可能全批次无提子，那就只断言段落存在与分母自洽）。
        BalanceReport report = BalanceAnalyzer.Analyze(sample);
        int withConflict = sample.Count - report.Targets.MatchesWithoutConflict;
        Assert.Equal(sample.Count - withConflict, report.Targets.MatchesWithoutOccupancy);
        Assert.Equal(withConflict, report.Targets.FirstConflictOccupancy.Sum(kv => kv.Value));
    }
}
