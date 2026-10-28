using Siege.Core.Board.Maps;
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
    public void 真实跑局的锁定座位经首部进入统计_座位互斥不计同区()
    {
        // 端到端（testing.md「真实跑局覆盖不到的写入路径，要补真实盘面端到端测试」）：真实建局 → 首部写入各玩家锁定的出生座位 → 分析读首部。
        // seat-gap-25 段 B（原名「真实跑局的同区经首部进入统计」）：座位互斥后真实跑局不再有同区局，测试名与断言改为所守的两件事——
        //   ① 真实跑局首部的锁定座位恰是 0..n−1 的一个排列（每个座位一人），分析计为 0 个同区局；
        //   ② 同区计数的"读首部"路径仍端到端成立：把一局真实日志首部的锁定座位改成有两人同座（只改这一字段，其余是真实写出的文本），经同一解析 → 分析计为 1 个同区局、同区玩家恰是那两人。
        // 同区统计不是死代码：analyze 只读日志、不比对地图摘要，座位互斥之前的旧日志仍可能含同区局，所以保留统计段与两条合成日志用例。
        // 变异验证（段 B 实跑，只跑本类）：
        //   MB2-1 PrototypeZoneAssignment.BlindPicks 改派时不排除已占座位（empty 取全部座位）→ 红 1（本测试①：出现重复座位）；
        //   MB2-2 BalanceAnalyzer.SharedZonePlayers 的同座判定改为"> 99 人"（恒不同区）→ 红 2（本测试②与「同区对局与同区玩家的名次和胜率」）。
        // retire-legacy-maps 1.8：原以冒险概率 100 / 0 造出"全员同区 / 互不同区"两局（v5）；冒险概率随旧地图删除，共用配置改到 link:1（盲选插旗），
        // 改取 link:1 上盲选恰好同区的种子 1（锁定区 3,3,1,0）与四人四区的种子 3（2,0,3,1）——锁定区由 flag-pick 子流决定，与配对来源无关。所守的统计规则不变。
        // map-expand-25 段 A2：link:1:4 浮空岛 10 块平台，同一组抽取按 10 取模——种子 1 变为 8,3,1,5（互不同区），同区样本改取种子 5（1,4,1,9）；种子 3 为 7,0,3,6，仍四人四区。
        // seat-gap-25 E4（裁决 S2）：座位互斥，真实跑局再也造不出同区局——种子 5 / 3 的锁定区都互不相同，端到端只守"首部锁定区写入并经分析计为非同区"；
        // 同区局的统计口径由上面的合成日志用例守（原"种子 5 盲选恰好同区"的样本随改派作废）。段 B 决定保留同区统计（理由见上）。
        MatchLog first = BatchRunner.Execute(SimFixtures.Config(seedStart: 5, turnLimit: 4), parallelism: 1)[0];
        MatchLog second = BatchRunner.Execute(SimFixtures.Config(seedStart: 3, turnLimit: 4), parallelism: 1)[0];

        // ① 每局锁定座位是 0..3 的排列（不只是"互不相同"：越界或写成 -1 也会红），首部区数 = 座位数 4。
        Assert.Equal([0, 1, 2, 3], first.Header.Zones.Order());
        Assert.Equal([0, 1, 2, 3], second.Header.Zones.Order());
        Assert.Equal(4, first.Header.ZoneCount);
        SharedZoneSection s = BalanceAnalyzer.Analyze([first, second]).SharedZones;
        Assert.Equal(2, s.Matches);
        Assert.Equal(0, s.SharedMatches);

        // ② 真实日志文本只改首部锁定座位：第 2 名玩家改坐第 1 名玩家的座位。
        string original = $"\"Zones\":[{string.Join(",", first.Header.Zones)}]";
        string text = first.FullText();
        Assert.Equal(1, text.Split(original).Length - 1);   // 恰命中一次，替换不会误伤别处
        List<int> doubled = [first.Header.Zones[0], first.Header.Zones[0], first.Header.Zones[2], first.Header.Zones[3]];
        MatchLog shared = MatchLog.Parse(text.Replace(original, $"\"Zones\":[{string.Join(",", doubled)}]", StringComparison.Ordinal));
        Assert.Equal(doubled, shared.Header.Zones);
        Assert.Equal([first.Header.Players[0], first.Header.Players[1]], BalanceAnalyzer.SharedZonePlayers(shared));
        SharedZoneSection withShared = BalanceAnalyzer.Analyze([shared, second]).SharedZones;
        Assert.Equal(2, withShared.Matches);
        Assert.Equal(1, withShared.SharedMatches);
    }

    private static MatchLog Log(ulong seed, int[] zones, LogResult result) =>
        SimFixtures.Synthetic(seed, [SimFixtures.Turn(1, 1, 0, [5, 5, 5, 5], ["A1:Basic"])], [], result, zones: zones);
}
