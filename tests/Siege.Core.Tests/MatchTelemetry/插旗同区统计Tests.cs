using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// flag-contest D3：分析报告的"同区对局数 / 占比、同区玩家的平均名次与胜率"（只报告）。
/// 同区取自日志首部锁定的出生区（<see cref="LogHeader.Zones"/>），不解析 FlagsLocked 事件文本。
/// </summary>
public class 插旗同区统计Tests
{
    [Fact]
    public void 同区对局与同区玩家的名次和胜率()
    {
        // 算例（构造日志）：
        //   A 区 [0,0,2,3] 名次 [1,3,2,4] 胜者 P0 → 同区 P0（1，胜）、P1（3）
        //   B 区 [1,1,1,3] 名次 [2,1,3,4] 胜者 P1 → 同区 P0（2）、P1（1，胜）、P2（3）
        //   C 区 [0,1,2,3] 名次 [1,2,3,4] 胜者 P0 → 无同区（被排除的样本 ①：非同区玩家不进名次 / 胜率）
        //   D 区 [2,2,0,1] 截断局 → 计入同区对局数，不计名次 / 胜率（被排除的样本 ②：截断局没有名次与胜者）
        // 期望：同区对局 3/4 = 75%；有名次的同区局 2；同区玩家 5 人次，平均名次 (1+3+2+1+3)/5 = 2，胜率 2/5。
        // 若把非同区玩家算进去 → 名次 20/9、胜率 3/9；若按"同区局的全部玩家"算 → 名次 20/8 = 2.5、胜率 2/8；若截断局的同区玩家进分母 → 胜率 2/7。
        MatchLog a = Log(1, [0, 0, 2, 3], SimFixtures.ResultOf(6, [0], ranks: [1, 3, 2, 4]));
        MatchLog b = Log(2, [1, 1, 1, 3], SimFixtures.ResultOf(6, [1], ranks: [2, 1, 3, 4]));
        MatchLog c = Log(3, [0, 1, 2, 3], SimFixtures.ResultOf(6, [0], ranks: [1, 2, 3, 4]));
        MatchLog d = Log(4, [2, 2, 0, 1], SimFixtures.TruncatedResult(6));

        BalanceReport report = BalanceAnalyzer.Analyze([a, b, c, d]);
        SharedZoneSection s = report.SharedZones;

        Assert.Equal(4, s.Matches);
        Assert.Equal(3, s.SharedMatches);
        Assert.Equal(0.75, s.SharedShare, 9);
        Assert.Equal(2, s.RankedSharedMatches);
        Assert.Equal(2.0, s.MeanSharedRank, 9);
        Assert.Equal(2, s.SharedWinRate.Successes);
        Assert.Equal(5, s.SharedWinRate.Trials);

        string text = ReportWriter.Render(report);
        Assert.Contains("插旗同区", text, StringComparison.Ordinal);
        Assert.Contains("同区对局 3 局，占纳入局 4 局的 75.0%", text, StringComparison.Ordinal);
        Assert.Contains("有名次的同区局 2 局、同区玩家 5 人次：平均名次 2，胜率 40.0% (2/5", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 无同区时如实给出0与无样本()
    {
        // 全部互不同区：同区对局 0 局，名次与胜率无样本（NaN / 空比例），报告照常给出这一段。
        MatchLog c = Log(3, [0, 1, 2, 3], SimFixtures.ResultOf(6, [0], ranks: [1, 2, 3, 4]));
        SharedZoneSection s = BalanceAnalyzer.Analyze([c]).SharedZones;

        Assert.Equal(1, s.Matches);
        Assert.Equal(0, s.SharedMatches);
        Assert.Equal(0.0, s.SharedShare, 9);
        Assert.Equal(0, s.RankedSharedMatches);
        Assert.True(double.IsNaN(s.MeanSharedRank));
        Assert.True(s.SharedWinRate.IsEmpty);
        Assert.Contains("同区对局 0 局，占纳入局 1 局的 0.0%", ReportWriter.Render(BalanceAnalyzer.Analyze([c])), StringComparison.Ordinal);
    }

    [Fact]
    public void 真实跑局的同区经首部进入统计()
    {
        // 端到端（testing.md「真实跑局覆盖不到的写入路径，要补真实盘面端到端测试」）：冒险概率 100 的真实建局 → 首部锁定区全部相同 → 分析计为同区对局。
        // 对照：冒险概率 0 的同种子局互不同区。
        MatchLog always = BatchRunner.Execute(SimFixtures.Config(turnLimit: 4) with { FlagRisk = 100 }, parallelism: 1)[0];
        MatchLog never = BatchRunner.Execute(SimFixtures.Config(turnLimit: 4) with { FlagRisk = 0 }, parallelism: 1)[0];

        Assert.Single(always.Header.Zones.Distinct());
        Assert.Equal(4, never.Header.Zones.Distinct().Count());
        SharedZoneSection s = BalanceAnalyzer.Analyze([always, never]).SharedZones;
        Assert.Equal(2, s.Matches);
        Assert.Equal(1, s.SharedMatches);
    }

    private static MatchLog Log(ulong seed, int[] zones, LogResult result) =>
        SimFixtures.Synthetic(seed, [SimFixtures.Turn(1, 1, 0, [5, 5, 5, 5], ["A1:Basic"])], [], result, zones: zones);
}
