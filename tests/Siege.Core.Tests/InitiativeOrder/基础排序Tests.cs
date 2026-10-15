using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.InitiativeOrder;

/// <summary>规格：initiative-order —— Requirement: 基础排序</summary>
public class 基础排序Tests
{
    [Fact]
    public void 按势力排名次()
    {
        // 设计文档 §11.1：参赛玩家按势力值从高到低排列得到势力名次（规格算例 80/55/40/12 → 1/2/3/4 只示意"严格递减 → 名次 1..4"；
        // 这里用 4/3/2/1 枚互不相邻的孤子造出严格递减的势力）。
        // 变异验证 M-I1：EndMajorRound 把 rank 写成 _players 下标 + 1 → 红 1（本测试）。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P0, "D4", "G4", "J4")
            .Stones(TestMaps.P1, "G7", "J7")
            .Stones(TestMaps.P2, "G9");
        match.PlayTurn("D7");   // P0 → 4 子
        match.PlayTurn("D9");   // P1 → 3 子
        match.PlayTurn("J9");   // P2 → 2 子
        match.PlayTurn("K10");   // P3 → 1 子

        InitiativeReport report = match.InitiativeReports.Single();
        Assert.Equal(new[] { 1, 2, 3, 4 }, TestMaps.All.Select(p => report.Of(p).Rank));
        BigInteger[] powers = [.. TestMaps.All.Select(p => report.Of(p).Power)];
        Assert.True(powers[0] > powers[1] && powers[1] > powers[2] && powers[2] > powers[3], string.Join(",", powers));
        Assert.Equal(new[] { TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3 }, report.NextOrder);
    }

    [Fact]
    public void 排除非参赛玩家()
    {
        // 设计文档 §11.1：4 人局中 1 人已出局、1 人已弃赛 → 势力名次只包含剩余 2 名参赛玩家，名次为 1 与 2。
        // 变异验证 M-I2：EndMajorRound 不过滤 Status → 抛出（弃赛者无名次）→ 红 1（本测试）。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P0, "G7", "D3")
            .Stones(TestMaps.P2, "C3")          // 段 C：P2 先有子（标记置位），再被提光出局（原为盘面与手牌皆空）
            .Stones(TestMaps.P3, "J9", "K10");
        match.Resign(TestMaps.P3);              // P3 弃赛（势力仍显示）

        match.PlayTurn("C4");                        // P0 提光 P2 → P2 出局（原为落 B2）
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(TestMaps.P2).Status);
        match.PlayTurn("J4");                        // P1
        Assert.Equal(6, match.MajorRound);

        InitiativeReport report = match.InitiativeReports.Single();
        Assert.Equal(2, report.ActiveCount);
        Assert.Equal(new[] { TestMaps.P0, TestMaps.P1 }, report.Entries.Select(e => e.Player).Order());
        Assert.Equal(1, report.Of(TestMaps.P0).Rank);
        Assert.Equal(2, report.Of(TestMaps.P1).Rank);
        Assert.Equal(PlayerStatus.Resigned, match.Scoreboard.Latest!.Of(TestMaps.P3).Status);
        Assert.True(match.Scoreboard.Latest.Of(TestMaps.P3).Total > 0);
    }
}
