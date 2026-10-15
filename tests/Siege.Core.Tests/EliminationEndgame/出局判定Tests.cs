using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>
/// 规格：elimination-endgame —— Requirement: 出局判定（restore-go-core-rules 裁决 #3 / design D3）。
/// 判据 = 「曾建立正势力」标记已置位 <b>且</b> 当前总势力为 0；手牌不影响，保护期不豁免。
/// retire-legacy-rules 第 1 组：载体为平坦 8×8 链接档图、四人同锁棋盘 0（<c>LinkModeFixtures.PlainStarted</c>），原 9×9 坐标平移 (+2, +2)，
/// 用到东 / 北边角的用例四角各贴棋盘 0 的四角；构筑期的"共享出生区"即同锁一块出生棋盘。
/// </summary>
public class 出局判定Tests
{
    [Fact]
    public void 开局零势力不出局()
    {
        // Scenario：第 1 大回合某玩家尚未落过子，总势力为 0 → 不出局。
        // 退化局面核算（testing.md「带等号的比较式先拿退化局面算一遍」）：开局空盘全员势力 0，
        // 「标记已置位 且 势力 = 0」的左半边为假 → 判据整体为假，一个都不出局。
        MatchFlow match = LinkModeFixtures.PlainStarted();
        Assert.Equal(1, match.MajorRound);
        Assert.All(TestMaps.All, p => Assert.Equal(0, match.Scoreboard.Latest!.Of(p).Total));

        match.PassTurn();

        Assert.All(TestMaps.All, p => Assert.Equal(PlayerStatus.Active, match.StateOf(p).Status));
        Assert.Equal(0, match.CountEvents(FlowEventKind.PlayerEliminated));
    }

    [Fact]
    public void 势力归零立即出局()
    {
        // Scenario：某曾建立正势力的玩家在一次结算后盘面棋子被全部提走，总势力为 0 → 立即出局，
        // 后续大回合的行动序列不再包含他。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P1, TestMaps.P2, TestMaps.P3, TestMaps.P0])
            .Stones(TestMaps.P0, "C3")
            .Stones(TestMaps.P1, "D3");
        Assert.True(match.Scoreboard.Latest!.Of(TestMaps.P0).Total > 0);

        match.PlayTurn("C4");   // P1 提掉 C3

        Assert.Equal(0, match.Scoreboard.Latest!.Of(TestMaps.P0).Total);
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(TestMaps.P0).Status);
        Assert.Equal(5, match.StateOf(TestMaps.P0).EliminatedInMajorRound);
        Assert.Equal(1, match.CountEvents(FlowEventKind.PlayerEliminated));

        match.PlayTurn("G7");   // P2
        match.PlayTurn("K10");   // P3 → 本大回合结束（P0 被跳过）
        Assert.Equal(6, match.MajorRound);
        Assert.DoesNotContain(TestMaps.P0, match.ActionOrder);
    }

    [Fact]
    public void 手牌有子也出局()
    {
        // Scenario：某曾建立正势力的玩家总势力降为 0，但手牌仍有 2 枚棋子 → 该玩家立即出局。
        // 旧判据是「盘面无子 且 手牌为空」，本例手牌非空 → 旧实现不出局；新判据不看手牌。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P1, TestMaps.P0, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P0, "C3")
            .Stones(TestMaps.P1, "D3");
        match.Debug.SeedHand(TestMaps.P0, (PieceType.Basic, 2));
        Assert.False(match.Hands.IsHandEmpty(TestMaps.P0));

        match.PlayTurn("C4");   // P1 提掉 C3

        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(TestMaps.P0).Status);
        Assert.False(match.Hands.IsHandEmpty(TestMaps.P0));
    }

    [Fact]
    public void 保护期内不豁免()
    {
        // Scenario：第 2 大回合，与玩家 A 共享出生区的玩家 B 提走了 A 的全部棋子，A 总势力为 0 → A 立即出局。
        // 旧实现在保护期内暂停出局检查（该 Requirement 已 REMOVED）；新判据不看保护期。
        MatchFlow match = LinkModeFixtures.PlainStarted(zones: [0, 0, 2, 3]).AtRound(2, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P1, "C3");
        Assert.True(match.Scoreboard.Latest!.Of(TestMaps.P1).Total > 0);

        match.PlayTurn("D3", "C4");   // P0 在共享出生棋盘内围杀 C3

        Assert.Equal(0, match.StoneCount(TestMaps.P1));
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(TestMaps.P1).Status);
        Assert.Equal(2, match.StateOf(TestMaps.P1).EliminatedInMajorRound);
        Assert.Equal(TestMaps.P2, match.CurrentPlayer);   // P1 的小回合被跳过
    }

    [Fact]
    public void 从未落子者不因Pass出局()
    {
        // Scenario：某玩家连续 Pass、从未建立过正势力 → 出局检查在每次 Pass 后执行，但该玩家不出局。
        // 退化局面核算：P3 手牌为空、盘面无子、保护早已解除——旧判据（盘面空 且 手牌空）会判他出局，
        // 新判据左半边「曾建立正势力」从未置位 → 不出局。
        // 同一次检查里放一个<b>已置位且被清零</b>的玩家（P0），证明检查确实执行了、不是被整体跳过。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P1, TestMaps.P3, TestMaps.P2, TestMaps.P0])
            .Stones(TestMaps.P0, "C3")
            .Stones(TestMaps.P1, "D3");
        match.Debug.SeedHand(TestMaps.P3);
        Assert.True(match.Hands.IsHandEmpty(TestMaps.P3));
        Assert.Equal(0, match.Scoreboard.Latest!.Of(TestMaps.P3).Total);

        match.PlayTurn("C4");   // P1 提掉 C3：同一次检查里 P0 出局、P3 不出局
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(TestMaps.P0).Status);
        Assert.Equal(PlayerStatus.Active, match.StateOf(TestMaps.P3).Status);

        match.PassTurn();       // P3 Pass，检查照样执行
        Assert.Equal(PlayerStatus.Active, match.StateOf(TestMaps.P3).Status);
        Assert.Equal(1, match.CountEvents(FlowEventKind.PlayerEliminated));

        match.PassTurn();       // P2 Pass：再来一次，标记仍未置位
        Assert.Equal(PlayerStatus.Active, match.StateOf(TestMaps.P3).Status);
    }

    [Fact]
    public void 同时归零同时出局()
    {
        // Scenario：玩家 A 的一个批次同时提走了玩家 B 与玩家 C 的全部棋子 → B 与 C 同时出局，
        // 最终名次中二者共享同一名次。
        // 与「唯一参赛者获胜」共用同一局：P3 随后弃赛 → 只剩 P0 → LastPlayerStanding。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All)
            .Stones(TestMaps.P1, "C3")
            .Stones(TestMaps.P2, "K10")
            .Stones(TestMaps.P0, "D3", "J10");

        match.PlayTurn("C4", "K9");   // P0 一个批次同时提掉 C3 与 K10

        PlayerFlowState b = match.StateOf(TestMaps.P1);
        PlayerFlowState c = match.StateOf(TestMaps.P2);
        Assert.Equal(PlayerStatus.Eliminated, b.Status);
        Assert.Equal(PlayerStatus.Eliminated, c.Status);
        Assert.Equal(b.EliminationOrder, c.EliminationOrder);
        Assert.Equal(2, match.CountEvents(FlowEventKind.PlayerEliminated));

        match.Resign(TestMaps.P3);
        Assert.Equal(EndReason.LastPlayerStanding, match.Result!.Reason);
        Assert.Equal([TestMaps.P0], match.Result.Winners);
        Assert.Equal(match.Result.Of(TestMaps.P1).Rank, match.Result.Of(TestMaps.P2).Rank);
    }
}
