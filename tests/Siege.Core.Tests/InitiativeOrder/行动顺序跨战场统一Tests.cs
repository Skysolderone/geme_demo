using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.InitiativeOrder;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/initiative-order —— Requirement: 行动顺序跨战场统一。
/// 「出局者出序」在 <c>Battlefield/战场结算Tests.出局者出序</c>。战场编号 0 起：规格的"战场 1、4"即这里的 0、3。
/// </summary>
public class 行动顺序跨战场统一Tests
{
    [Fact]
    public void 两个战场共用一轮()
    {
        // Scenario：战场 1 有 A、B，战场 4 有 C、D，进入第 5 大回合 → 该大回合的行动顺序含 A、B、C、D 各一次。
        // A、C 每大回合落子，两个战场都不结算。
        MatchFlow match = Started([0, 0, 3, 3]).PlayRounds(4, P0, P2);
        Assert.Equal(5, match.MajorRound);
        Assert.Empty(match.Settlements);
        Assert.Equal(All.Order(), match.ActionOrder.Order());
        Assert.Equal(4, match.ActionOrder.Length);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 等待者在序()
    {
        // Scenario：D 是单人战场 3 的等待者 → 每个大回合的行动顺序都含 D。A、B、C 同区，A 每大回合落子使战场 1 不结算。
        MatchFlow match = Started([0, 0, 0, 2]).PlayRounds(5, P0);
        Assert.Equal(6, match.MajorRound);
        Assert.True(match.StateOf(P3).WaitingRounds > 0);
        Assert.Equal(5, match.InitiativeReports.Count);
        Assert.All(match.InitiativeReports, r => Assert.Contains(P3, r.NextOrder));
        Assert.Contains(P3, match.ActionOrder);
    }
}
