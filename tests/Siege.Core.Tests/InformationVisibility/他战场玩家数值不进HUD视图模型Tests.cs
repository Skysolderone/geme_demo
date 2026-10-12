using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Hand;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.InformationVisibility;

/// <summary>
/// board-link-mode 负责人裁决 Q1 / D20（information-visibility「他人势力不可见」）：链接模式下不与本机同战场的玩家，在势力排名栏、势力层、顺序层、顺序预测与手牌对比面板里
/// 只显示名字 + 在局 / 出局 / 弃赛 + 所在战场是否已结算，不显示势力数值、势力明细、手牌类型与结构参数；同战场玩家照旧；行动顺序本身全局公开。
/// 判据只读 <see cref="SeatPublicView.Players"/> 的 <c>SameBattlefield</c>（<see cref="ViewerWorld.IsConcealed"/>），引擎层不自己判。Legacy 不变。
/// </summary>
/// <remarks>变异验证（段 D-c 实跑）：M-F1 <c>ViewerWorld.IsConcealed</c> 恒假 → 「他战场玩家的势力行只剩名字与状态」「他战场玩家的顺序行只剩预测位置」「他战场玩家的手牌类型与结构参数不可见」「他战场已结算时状态文案带胜出_出局者按出局显示」红 4。</remarks>
public class 他战场玩家数值不进HUD视图模型Tests
{
    /// <summary>P0、P1 同在棋盘 1，P2 在棋盘 4，P3 在棋盘 5；四人各落一子，势力都大于 0。</summary>
    private static MatchFlow Fought() => Started([0, 0, 3, 4]).PlayRound(P0, P1, P2, P3);

    [Fact]
    public void 他战场玩家的势力行只剩名字与状态()
    {
        ViewerWorld world = Fought().World(P0);
        var power = Assert.IsType<PowerLayerContent>(world.Layer(TacticalLayer.Power));
        Assert.Equal(4, power.Players.Length);   // 行还在：名字与状态全局公开

        PlayerPowerRowView self = power.Players.Single(p => p.Player == P0);
        PlayerPowerRowView mate = power.Players.Single(p => p.Player == P1);
        Assert.False(self.Concealed || mate.Concealed);
        Assert.True(self.Total > BigInteger.Zero && mate.Total > BigInteger.Zero);
        Assert.NotNull(mate.Rank);

        foreach (PlayerId other in new[] { P2, P3 })
        {
            PlayerPowerRowView row = power.Players.Single(p => p.Player == other);
            Assert.True(row.Concealed);
            Assert.Equal((BigInteger.Zero, 0, BigInteger.Zero), (row.Total, row.TerritoryScore, row.GroupScore));
            Assert.Null(row.Rank);
            Assert.Equal(PlayerPowerRowView.ConcealedText, row.CompactText);
            Assert.Equal(PlayerPowerRowView.ConcealedText, row.ExactText);
            Assert.Equal("在局", row.StatusText);
            Assert.True(world.IsConcealed(other));
        }

        Assert.False(world.IsConcealed(P0));
        Assert.False(world.IsConcealed(P1));
        // 反面：全知世界的同一层带着 P2 的数值。
        Assert.True(Assert.IsType<PowerLayerContent>(Siege.Presentation.Layers.TacticalLayers.Build(Fought().PublicWorldOf(), TacticalLayer.Power)).Players.Single(p => p.Player == P2).Total > BigInteger.Zero);
    }

    [Fact]
    public void 他战场玩家的顺序行只剩预测位置()
    {
        ViewerWorld world = Fought().World(P0);
        var order = Assert.IsType<OrderLayerContent>(world.Layer(TacticalLayer.Order));

        Assert.Equal(4, order.CurrentOrder.Length);        // 行动顺序全局公开
        Assert.Equal(4, order.PredictedNextOrder.Length);  // 下一轮顺序（谁在第几位）全局公开
        Assert.Equal(4, order.Rows.Length);
        foreach (OrderRowView row in order.Rows)
        {
            if (row.Player == P2 || row.Player == P3)
            {
                Assert.True(row.Concealed);
                Assert.Equal((0, BigInteger.Zero, 0, 0), (row.Rank, row.Power, row.Bonus, row.Value));
                Assert.Equal(OrderRowView.ConcealedExplanation, row.Explanation);
                Assert.InRange(row.PredictedPosition, 1, 4);
            }
            else
            {
                Assert.False(row.Concealed);
                Assert.True(row.Power > BigInteger.Zero);
                Assert.Contains("先手值", row.Explanation, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void 他战场玩家的手牌类型与结构参数不可见()
    {
        MatchFlow match = Fought();
        HandInfoPanelView panel = match.World(P0).HandPanel();
        Assert.Equal(3, panel.Opponents.Length);

        OpponentHandAreaView mate = panel.Opponents.Single(o => o.Player == P1);
        Assert.False(mate.Concealed);
        Assert.NotEmpty(mate.Types);
        Assert.NotNull(mate.Structure);

        foreach (PlayerId other in new[] { P2, P3 })
        {
            OpponentHandAreaView area = panel.Opponents.Single(o => o.Player == other);
            Assert.True(area.Concealed);
            Assert.Empty(area.Types);
            Assert.Empty(area.TypeNames);
            Assert.Null(area.Structure);
            Assert.Equal("在局", area.StatusText);
        }

        // 反面：对手手牌类型在全知面板里存在（P2 刚落过子，手里有类型）。
        Assert.NotEmpty(HandInfoPanelView.Build(match.World(P2)).Opponents.Single(o => o.Player == P0).Types);
    }

    [Fact]
    public void 他战场已结算时状态文案带胜出_出局者按出局显示()
    {
        // P2、P3 同在棋盘 4（索引 3），本大回合都 Pass → 结算：胜者"所在战场已结算（胜出）"，落败者"出局"；P0 在棋盘 1 看到的只有这些。
        MatchFlow match = Started([0, 1, 3, 3]).PlayRound(P0, P1);
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        PlayerId winner = settled.Winner;
        PlayerId loser = winner == P2 ? P3 : P2;
        ViewerWorld world = match.World(P0);
        var power = Assert.IsType<PowerLayerContent>(world.Layer(TacticalLayer.Power));

        Assert.Equal("在局 · 所在战场已结算（胜出）", power.Players.Single(p => p.Player == winner).StatusText);
        Assert.Equal(Siege.Presentation.Text.Labels.Status(PlayerStatus.Eliminated), power.Players.Single(p => p.Player == loser).StatusText);   // "已出局 · 不再行动"
        Assert.Equal("在局 · 所在战场已结算（胜出）", world.ConcealedStatusText(winner));
    }

    [Fact]
    public void 旧模式与同战场不遮蔽()
    {
        MatchFlow legacy = MatchFixtures.Started();
        ViewerWorld world = legacy.World(P0);
        Assert.All(MatchFixtures.All, p => Assert.False(world.IsConcealed(p)));
        var power = Assert.IsType<PowerLayerContent>(world.Layer(TacticalLayer.Power));
        Assert.All(power.Players, p => Assert.False(p.Concealed));
        Assert.All(world.HandPanel().Opponents, o => Assert.False(o.Concealed));
        Assert.All(Assert.IsType<OrderLayerContent>(world.Layer(TacticalLayer.Order)).Rows, r => Assert.False(r.Concealed));
    }
}
