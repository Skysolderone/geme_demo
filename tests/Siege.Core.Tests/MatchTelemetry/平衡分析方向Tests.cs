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
        Assert.Contains("出生区 1：胜率 100.0% (40/40", text);
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
    public void 成长轴部署阈值按分阶段基础值()
    {
        // growth-pass-1：部署轴的"获取"= 部署上限高于该小回合所在大回合的分阶段基础值（3 / 4 / 5），而不是固定的 3——
        // 否则第 4 大回合起人人都会被误记为"获取了部署轴"。合成 P0：第 4 大回合 4、第 6 大回合 4、第 7 大回合 5 都只是基础值；第 8 大回合 6 才是军令带来的获取。
        // 变异验证 M-GP9：BalanceAnalyzer.AxisAcquisitionRounds 的阈值改为 BaseDeployLimitFor(1) → 全套红 1（本测试，部署轴记为第 4 大回合）。
        MatchLog log = SimFixtures.Synthetic(
            1,
            [
                SimFixtures.Turn(1, 4, 0, [3, 0, 0, 0], ["A3:Basic"], deployLimit: 4),
                SimFixtures.Turn(2, 6, 0, [3, 0, 0, 0], ["A4:Basic"], deployLimit: 4),
                SimFixtures.Turn(3, 7, 0, [3, 0, 0, 0], ["A5:Basic"], deployLimit: 5),
                SimFixtures.Turn(4, 8, 0, [3, 0, 0, 0], ["A6:Basic"], deployLimit: 6),
            ],
            [],
            SimFixtures.ResultOf(8, [0]));

        Assert.Equal([null, 8, null, null], BalanceAnalyzer.AxisAcquisitionRounds(log, 0));
    }

    [Fact]
    public void 成长轴顺序分析()
    {
        // 四条成长轴（供给 / 部署 / 槽位 / 倍率）在胜局中的获取顺序分布。合成胜者 P0：第 1 大回合出现倍增串、第 2 大回合部署上限 4、第 3 大回合槽位 6，供给未获取。
        // 部署轴阈值若误为 >= BaseDeployLimitFor(大回合)，顺序会变成 部署>倍率>槽位，本测试的期望值钉住 > 。
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

    [Fact]
    public void 碾压胜统计()
    {
        // 规格算例：一批对局中有 40 局以势力碾压终局 → 报告给出碾压胜占比、这些局碾压成立的平均大回合、触发时获胜者与第 2 名势力之比。
        // testing.md「统计口径测试必须放一个被排除的样本」：另放 1 局调试 AI 碾压局（污染）与 1 局失败局，分母只能是纳入的 100 局。
        // 变异验证 M-DV10：Analyze 把 Dominance(included) 改为全部未失败局（含污染局）→ 红 1（本测试：41/101）。
        static LogResult Dominance(int round, long winner, long second) =>
            SimFixtures.ResultOf(round, [0]) with
            {
                Reason = nameof(Siege.Core.Match.EndReason.PowerDominance),
                Standings = [.. new[] { winner, second, 0L, 0L }.Select((power, i) => new StandingEntry
                {
                    Rank = i + 1,
                    Player = i,
                    Group = "Finisher",
                    Status = "Active",
                    Power = power,
                })],
            };

        TurnSnapshot[] turns = [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])];
        var logs = new List<MatchLog>();
        for (int i = 0; i < 100; i++)
        {
            LogResult result = i switch
            {
                < 20 => Dominance(6, 60, 20),    // 比值 3
                < 39 => Dominance(8, 50, 25),    // 比值 2
                39 => Dominance(8, 30, 0),       // 第 2 名势力 0：比值无定义
                _ => SimFixtures.ResultOf(12, [i % 4]),
            };
            logs.Add(SimFixtures.Synthetic(900UL + (ulong)i, turns, [], result));
        }

        logs.Add(SimFixtures.Synthetic(1100, turns, [], Dominance(4, 99, 1) with { UsedDebugAi = true }));
        MatchLog failed = SimFixtures.Synthetic(1101, turns, [], SimFixtures.ResultOf(1, [0]));
        failed.Result = null;
        failed.Failure = new LogFailure { ExceptionType = "SimAssertionException", Message = "测试注入" };
        logs.Add(failed);

        BalanceReport report = BalanceAnalyzer.Analyze(logs);
        Assert.Equal((102, 100, 1, 1), (report.TotalLogs, report.Included, report.ExcludedContaminated, report.ExcludedFailed));
        DominanceSection d = report.Dominance;
        Assert.Equal((100, 40), (d.Matches, d.DominanceWins));
        Assert.Equal((40, 100), (d.Rate.Successes, d.Rate.Trials));
        Assert.Equal(0.40, d.Rate.Value);
        Assert.Equal(7.0, d.MeanTriggerRound, 10);                 // (20×6 + 20×8) / 40
        Assert.Equal((39, 1), (d.RatioSamples, d.RatioUndefined));
        Assert.Equal(98.0 / 39, d.MeanPowerRatio, 10);             // (20×3 + 19×2) / 39

        // 碾压计为正常终局：终局原因分布含碾压，不计入不收敛。
        Assert.Equal(40, report.Convergence.Reasons[nameof(Siege.Core.Match.EndReason.PowerDominance)]);
        Assert.Equal((100, 0), (report.Convergence.Converged, report.Convergence.Capped));
        Assert.All(logs.Take(40), l => Assert.True(l.Result!.Converged));

        string text = ReportWriter.Render(report);
        Assert.Contains("## 势力碾压", text);
        Assert.Contains("非达上限终局（含势力碾压）100 局", text);
        Assert.Contains("碾压胜 40 / 100 局，占比 40.0% (40/100", text);
        Assert.Contains("碾压局平均成立大回合 7", text);
        Assert.Contains("平均 2.51（样本 39；第 2 名势力为 0、比值无定义 1）", text);
    }
}
