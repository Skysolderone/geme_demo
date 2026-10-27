using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Flow = Siege.Core.Match.InitiativeOrder;

namespace Siege.Core.Tests.InitiativeOrder;

/// <summary>规格：initiative-order —— Requirement: 先手值公式</summary>
public class 先手值公式Tests
{
    [Fact]
    public void 四人局基础先手值()
    {
        // 设计文档 §11.2：4 名参赛玩家，势力名次 4 → 4 − 4 = 0（relic-redesign D4：先锋修正随旧信物删除）。
        // 变异验证 M-I3：ValueOf 写成 activeCount − rank + 1 → 红 3（本类三个测试）。
        Assert.Equal(0, Flow.ValueOf(4, 4));
        Assert.Equal(3, Flow.ValueOf(4, 1));

        // 流程层：第 4 名的明细 Value 为 0
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P0, "D4", "G4").Stones(TestMaps.P1, "J4").Stones(TestMaps.P2, "G7");
        match.PlayTurn("D7");
        match.PlayTurn("G9");
        match.PlayTurn("J9");
        match.PlayTurn("K10");
        InitiativeEntry last = match.InitiativeReports.Single().Of(TestMaps.P3);
        Assert.Equal(4, last.Rank);
        Assert.Equal(0, last.Value);
    }

    [Fact]
    public void 参赛人数变化影响公式()
    {
        // 设计文档 §11.2：4 人局 2 人已出局，剩余 2 名参赛玩家，名次第 1 → 2 − 1 = 1。
        // 变异验证 M-I4：EndMajorRound 用 _players.Length 代替参赛人数 → 红 1（本测试流程断言）。
        Assert.Equal(1, Flow.ValueOf(2, 1));

        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P0, "D4", "D3", "J10")
            .Stones(TestMaps.P2, "C3")
            .Stones(TestMaps.P3, "K10");
        match.PlayTurn("C4", "K9");    // P0 一个批次提光 P2（C3）与 P3（K10）→ 同时出局（段 C 改摆法：原为二人盘面与手牌皆空、P0 落 E5；retire-legacy-rules：四角各贴棋盘 0 的角）
        Assert.Equal(2, match.ActiveCount);
        match.PlayTurn("J4");    // P1

        InitiativeReport report = match.InitiativeReports.Single();
        Assert.Equal(2, report.ActiveCount);
        InitiativeEntry top = report.Of(TestMaps.P0);
        Assert.Equal(1, top.Rank);
        Assert.Equal(1, top.Value);
        Assert.Equal(0, report.Of(TestMaps.P1).Value);
    }
}
