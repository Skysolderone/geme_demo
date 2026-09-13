using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 首回合顺序随机</summary>
public class 首回合顺序随机Tests
{
    [Fact]
    public void 首回合随机顺序()
    {
        // 设计文档 §11.1：第一大回合随机排序，由对局种子（setup 子流）驱动。
        // 变异验证 M-S6：Shuffle 直接返回按编号排序的玩家 → 红 1（本测试：20 个种子只出现 1 种顺序）。
        var orders = new HashSet<string>();
        for (ulong v = 1; v <= 20; v++)
        {
            MatchFlow match = MatchFixtures.Started(new GameSeed(v));
            Assert.Equal(1, match.MajorRound);
            Assert.Equal(MatchFixtures.All.Order(), match.ActionOrder.Order());
            orders.Add(string.Join(">", match.ActionOrder));
        }

        Assert.True(orders.Count >= 3, $"20 个种子只出现 {orders.Count} 种首回合顺序，不像随机。");
    }

    [Fact]
    public void 顺序可复现()
    {
        // 同一对局种子与同一插旗结果重放 → 第一大回合顺序完全一致；且不受 recruit 子流消费影响（子流隔离）。
        // 变异验证 M-S7：Shuffle 改用 Random.Shared → 红 1（本测试，概率性但 4! = 24 种顺序下连续 3 局一致的概率极低）。
        MatchFlow a = MatchFixtures.Started(new GameSeed(77), zones: [0, 1, 2, 3]);
        MatchFlow b = MatchFixtures.Started(new GameSeed(77), zones: [0, 1, 2, 3]);
        MatchFlow c = MatchFixtures.Started(new GameSeed(77), zones: [3, 2, 1, 0]);
        a.PlayTurn();
        Assert.Equal(a.ActionOrder, b.ActionOrder);
        Assert.Equal(a.ActionOrder, c.ActionOrder);
        Assert.Equal(a.PlayerStates.Select(s => s.LastRoundPosition), b.PlayerStates.Select(s => s.LastRoundPosition));
    }
}
