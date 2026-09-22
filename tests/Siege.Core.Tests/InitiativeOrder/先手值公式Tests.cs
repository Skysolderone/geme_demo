using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Flow = Siege.Core.Match.InitiativeOrder;

namespace Siege.Core.Tests.InitiativeOrder;

/// <summary>规格：initiative-order —— Requirement: 先手值公式</summary>
public class 先手值公式Tests
{
    private static InitiativeEntry Entry(PlayerId p, int active, int rank, int bonus, long power = 0, int? previous = null, int seedRank = 0) =>
        new(p, rank, power, bonus, Flow.ValueOf(active, rank, bonus), previous, seedRank);

    [Fact]
    public void 四人局基础先手值()
    {
        // 设计文档 §11.2：4 名参赛玩家，势力名次 4 且无先锋 → (4 − 4) + 0 = 0。
        // 变异验证 M-I3：ValueOf 写成 activeCount − rank + 1 → 红 3（本类三个测试）。
        Assert.Equal(0, Flow.ValueOf(4, 4, 0));
        Assert.Equal(3, Flow.ValueOf(4, 1, 0));

        // 流程层：第 4 名的明细 Value 为 0
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "B2", "E2").Stones(MatchFixtures.P1, "H2").Stones(MatchFixtures.P2, "E5");
        match.PlayTurn("B5");
        match.PlayTurn("E8");
        match.PlayTurn("H8");
        match.PlayTurn("J9");
        InitiativeEntry last = match.InitiativeReports.Single().Of(MatchFixtures.P3);
        Assert.Equal(4, last.Rank);
        Assert.Equal(0, last.Bonus);
        Assert.Equal(0, last.Value);
    }

    [Fact]
    public void 先锋推动低名次玩家前移()
    {
        // 设计文档 §11.2 算例：第 4 名拥有先手 +2 → 先手值 (4 − 4) + 2 = 2，可与原第 2 名（4 − 2 = 2）竞争行动位置；
        // 同值链第 1 条"先手修正更高者优先"让第 4 名排到第 2 名之前。
        ImmutableArray<InitiativeEntry> entries =
        [
            Entry(MatchFixtures.P0, 4, 1, 0, power: 80),
            Entry(MatchFixtures.P1, 4, 2, 0, power: 55),
            Entry(MatchFixtures.P2, 4, 3, 0, power: 40),
            Entry(MatchFixtures.P3, 4, 4, 2, power: 12),
        ];
        Assert.Equal(2, entries[3].Value);
        Assert.Equal(2, entries[1].Value);

        InitiativeReport report = Flow.Generate(1, entries);
        Assert.Equal(new[] { MatchFixtures.P0, MatchFixtures.P3, MatchFixtures.P1, MatchFixtures.P2 }, report.NextOrder);
    }

    [Fact]
    public void 参赛人数变化影响公式()
    {
        // 设计文档 §11.2：4 人局 2 人已出局，剩余 2 名参赛玩家，名次第 1 无先锋 → (2 − 1) + 0 = 1。
        // 变异验证 M-I4：EndMajorRound 用 _players.Length 代替参赛人数 → 红 1（本测试流程断言）。
        Assert.Equal(1, Flow.ValueOf(2, 1, 0));

        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "B2", "B1", "H9")
            .Stones(MatchFixtures.P2, "A1")
            .Stones(MatchFixtures.P3, "J9");
        match.PlayTurn("A2", "J8");    // P0 一个批次提光 P2（A1）与 P3（J9）→ 同时出局（段 C 改摆法：原为二人盘面与手牌皆空、P0 落 E5）
        Assert.Equal(2, match.ActiveCount);
        match.PlayTurn("H2");    // P1

        InitiativeReport report = match.InitiativeReports.Single();
        Assert.Equal(2, report.ActiveCount);
        InitiativeEntry top = report.Of(MatchFixtures.P0);
        Assert.Equal(1, top.Rank);
        Assert.Equal(1, top.Value);
        Assert.Equal(0, report.Of(MatchFixtures.P1).Value);
    }
}
