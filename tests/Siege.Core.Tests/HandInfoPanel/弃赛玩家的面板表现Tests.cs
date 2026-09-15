using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Hand;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.HandInfoPanel;

/// <summary>规格：hand-info-panel —— Requirement: 弃赛玩家的面板表现</summary>
public class 弃赛玩家的面板表现Tests
{
    [Fact]
    public void 弃赛者标记()
    {
        // 设计文档 §14.3 算例：D（P3）弃赛时持普通子与连珠子 → 面板保留这两种类型，并清楚标记 D 已弃赛、不再行动。
        // 结果对象（面板）与活对象（流程状态）两处都钉住。
        // 变异验证 M-W1：HandInfoPanelView.Opponent 的 StatusText 改为 null → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        match.Debug.SeedHand(P3, (PieceType.Basic, 3), (PieceType.Line, 2));
        match.Resign(P3);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(P3).Status);

        OpponentHandAreaView d = match.World(P0).HandPanel().Opponents.Single(o => o.Player == P3);

        Assert.Equal([PieceType.Basic, PieceType.Line], d.Types);
        Assert.Equal(PlayerStatus.Resigned, d.Status);
        Assert.False(d.IsActing);
        Assert.Equal("已弃赛 · 不再行动", d.StatusText);
        Assert.Null(d.Structure);

        OpponentHandAreaView active = match.World(P0).HandPanel().Opponents.Single(o => o.Player == P1);
        Assert.True(active.IsActing);
        Assert.Null(active.StatusText);
    }
}
