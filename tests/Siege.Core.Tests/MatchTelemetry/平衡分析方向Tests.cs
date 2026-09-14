using Siege.Core.Board;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>规格：match-telemetry —— Requirement: 平衡分析方向</summary>
public class 平衡分析方向Tests
{
    [Fact]
    public void 棋子选择率与胜率()
    {
        // 五种棋子各自的选择率（选取 / 展示）与选过它的玩家的关联胜率都被列出。合成：P0（胜者）选 Basic、Line；P1 选 Fortress。
        // 变异验证：本类以 M-B10 为准（见 最小落子拖延检测）。
        MatchLog log = SimFixtures.Synthetic(
            1,
            [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"]), SimFixtures.Turn(2, 1, 1, [1, 1, 1, 1], ["H1:Basic"])],
            [
                SimFixtures.Recruit(1, 0, "Basic,Line,Basic,Fortress,Synergy", "Basic,Line"),
                SimFixtures.Recruit(2, 1, "Fortress,Multiplier,Basic,Basic,Line", "Fortress"),
            ],
            SimFixtures.ResultOf(5, [0]));

        SelectionSection s = BalanceAnalyzer.Analyze([log]).Selection;

        Assert.Equal(Enum.GetNames<PieceType>().Order(), s.Pieces.Select(p => p.Type).Order());
        PieceStat basic = s.Pieces.Single(p => p.Type == "Basic");
        Assert.Equal((4, 1), (basic.Offered, basic.Picked));
        Assert.Equal(0.25, basic.SelectionRate.Value);
        Assert.Equal((1, 1), (basic.WinRateOfPickers.Successes, basic.WinRateOfPickers.Trials));
        PieceStat fortress = s.Pieces.Single(p => p.Type == "Fortress");
        Assert.Equal((2, 1), (fortress.Offered, fortress.Picked));
        Assert.Equal((0, 1), (fortress.WinRateOfPickers.Successes, fortress.WinRateOfPickers.Trials));
        PieceStat multiplier = s.Pieces.Single(p => p.Type == "Multiplier");
        Assert.Equal((1, 0), (multiplier.Offered, multiplier.Picked));
        Assert.True(multiplier.WinRateOfPickers.IsEmpty);

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze(SimFixtures.Sample.Value));
        Assert.All(Enum.GetNames<PieceType>(), type => Assert.Contains($"- 棋子 {type}：", text));
        Assert.All(Enum.GetNames<Relics.RelicType>(), type => Assert.Contains($"- 信物 {type}：", text));
    }

    [Fact]
    public void 出生区公平性()
    {
        // 各出生区胜率 + 显著性（基线 1/4 落在 Wilson 区间之外即显著）；裁决 8：收敛局 / 未收敛局分别统计。
        // 合成 40 局：出生区 0 的玩家全胜（前 20 局信物收敛，后 20 局不收敛）。
        // 出生区映射用 zones: [2, 1, 3, 0] 故意错位，zone 与玩家下标不同，抓得住"取 p 而非 Zones[p]"的错误。
        MatchLog[] logs = [.. Enumerable.Range(0, 40).Select(i => SimFixtures.Synthetic(
            (ulong)i + 1,
            [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])],
            [],
            SimFixtures.ResultOf(6, [3]),
            zones: [2, 1, 3, 0],
            relicsConverged: i < 20))];

        BirthZoneSection z = BalanceAnalyzer.Analyze(logs).BirthZones;

        Assert.Equal(0.25, z.Baseline);
        Assert.Equal((20, 20), (z.ConvergedMatches, z.NotConvergedMatches));
        ZoneStat zone0 = z.All.Single(s => s.Zone == 0);
        Assert.Equal((40, 40), (zone0.WinRate.Successes, zone0.WinRate.Trials));
        Assert.True(zone0.Significant);
        ZoneStat zone2 = z.All.Single(s => s.Zone == 2);
        Assert.Equal((0, 40), (zone2.WinRate.Successes, zone2.WinRate.Trials));
        Assert.True(zone2.Significant);
        Assert.Equal((20, 20), (z.RelicsConverged.Single(s => s.Zone == 0).WinRate.Successes, z.RelicsNotConverged.Single(s => s.Zone == 0).WinRate.Successes));

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze(logs));
        Assert.Contains("出生区 0：胜率 100.0% (40/40", text);
        Assert.Contains("，显著", text);
        Assert.Contains("信物生成未收敛局（20）", text);

        // 均衡样本不显著：4 局各区各胜一次
        MatchLog[] even = [.. Enumerable.Range(0, 4).Select(i => SimFixtures.Synthetic(
            (ulong)i + 100, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(6, [i])))];
        Assert.All(BalanceAnalyzer.Analyze(even).BirthZones.All, s => Assert.False(s.Significant));
    }

    [Fact]
    public void 最小落子拖延检测()
    {
        // 信号 = 该小回合落子数为 1 且该玩家势力与上一快照相比无变化；报告给出占全部小回合的比例。
        // 合成 4 个小回合：#2 落 1 枚势力不变（信号）、#3 落 1 枚势力上升（否）、#4 落 2 枚势力不变（否）→ 1/4。
        // 变异验证 M-B10：Stalling 把 Placements.Count != 1 改成 < 1 → 红 1（本测试：#4 被误计，比例 2/4）。
        MatchLog log = SimFixtures.Synthetic(
            1,
            [
                SimFixtures.Turn(1, 1, 0, [5, 0, 0, 0], ["A1:Basic"]),
                SimFixtures.Turn(2, 1, 0, [5, 0, 0, 0], ["A2:Basic"]),
                SimFixtures.Turn(3, 1, 0, [7, 0, 0, 0], ["A3:Basic"]),
                SimFixtures.Turn(4, 1, 0, [7, 0, 0, 0], ["B1:Basic", "B2:Basic"]),
            ],
            [],
            SimFixtures.ResultOf(1, [0]));

        StallingSection s = BalanceAnalyzer.Analyze([log]).Stalling;

        Assert.Equal((1, 4), (s.SignalTurns, s.TotalTurns));
        Assert.Equal(0.25, s.Ratio.Value);
        Assert.Contains("信号出现 1 次，占全部 4 个小回合的 25.0% (1/4", ReportWriter.Render(BalanceAnalyzer.Analyze([log])));
    }

    [Fact]
    public void 成长轴顺序分析()
    {
        // 四条成长轴（供给 / 部署 / 槽位 / 倍率）在胜局中的获取顺序分布。合成胜者 P0：第 1 大回合出现倍增串、第 2 大回合部署上限 4、第 3 大回合槽位 6，供给未获取。
        // 部署轴阈值若误为 >= BaseDeployLimit，顺序会变成 部署>倍率>槽位，本测试的期望值钉住 > 。
        GroupEntry[] multiplierGroup = [new() { Stones = ["A1", "A2"], Base = 2, MultiplierCount = 1, Power = 3 }];
        MatchLog log = SimFixtures.Synthetic(
            1,
            [
                SimFixtures.Turn(1, 1, 0, [3, 0, 0, 0], ["A2:Multiplier"], groupsOfPlayer: multiplierGroup),
                SimFixtures.Turn(2, 2, 0, [3, 0, 0, 0], ["A3:Basic"], deployLimit: 4, groupsOfPlayer: multiplierGroup),
                SimFixtures.Turn(3, 3, 0, [3, 0, 0, 0], ["A4:Basic"], deployLimit: 4, typeSlots: 6, groupsOfPlayer: multiplierGroup),
                SimFixtures.Turn(4, 3, 1, [3, 1, 0, 0], ["H1:Basic"], deployLimit: 5, showCount: 6),
            ],
            [],
            SimFixtures.ResultOf(3, [0]));

        int?[] rounds = BalanceAnalyzer.AxisAcquisitionRounds(log, 0);
        Assert.Equal([null, 2, 3, 1], rounds);
        Assert.Equal("倍率>部署>槽位", BalanceAnalyzer.AxisSequence(rounds));
        Assert.Equal("供给=部署", BalanceAnalyzer.AxisSequence(BalanceAnalyzer.AxisAcquisitionRounds(log, 1)));
        Assert.Equal("无", BalanceAnalyzer.AxisSequence(BalanceAnalyzer.AxisAcquisitionRounds(log, 2)));

        GrowthAxisSection g = BalanceAnalyzer.Analyze([log]).GrowthAxes;
        Assert.Equal(1, g.WinnerSamples);
        Assert.Equal(1, g.SequenceCounts["倍率>部署>槽位"]);
        Assert.Equal(1, g.FirstAxisCounts["倍率"]);
        Assert.Equal("倍率>部署>槽位", g.DominantSequence);
        Assert.Contains("顺序 倍率>部署>槽位：1 次", ReportWriter.Render(BalanceAnalyzer.Analyze([log])));
    }
}
