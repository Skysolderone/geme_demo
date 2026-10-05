using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// 规格：tactical-layers —— Requirement: 棋串军势常驻标注（follow-opponent D7）。<see cref="GroupPowerLabels"/> 只决定"标在哪一格、写什么"，
/// 数值原样取势力层内容里的棋串军势。重心最近用整数比较（坐标 × 枚数 − 坐标和）。
/// tiered-number-show 1.4 增「标注带档位」（design.md D6）：每条标注带该棋串军势的数值档位，字号按分档样式表逐档加大。
/// </summary>
/// <remarks>
/// 变异验证（tiered-number-show 段 A；脚本做法与记录见 SettlementShow/数值档位Tests）：
/// M-L1「标注档位恒为一档」——<c>GroupPowerLabels.Of</c> 里 <c>NumberTier.Of(g.Power.Power)</c> 改为 <c>NumberTier.Lowest</c>
/// → 红 9（本类 标注带档位、数值档位Tests 的 三处呈现同一数值同一档 七条 / 引擎层不取档 的反面命中）。
/// M5（并列取坐标序最大）复跑：红 4（本类 并列取坐标序最小，以及落点共用后的 军势揭示节拍Tests.落点与常驻标注同一格 等三条）。
/// </remarks>
public class 棋串军势常驻标注Tests
{
    private static readonly PlayerId P1 = new(1);
    private static readonly PlayerId P2 = new(2);

    private static GroupScoreView Group(PlayerId owner, BigInteger power, params string[] cells)
    {
        ImmutableArray<Coord> stones = [.. cells.Select(Coord.Parse)];
        var detail = new GroupPower(owner, stones, BaseTotal: stones.Length, LineBonus: 0, SynergyBonus: 0, HighGroundBonus: 0,
            BannerBonus: 0, ChainBonus: 0, SentryBonus: 0, BoundaryBonus: 0, MultiplierCount: 0, Power: power);
        return new GroupScoreView(owner, stones, GroupPowerView.From(detail), 0);
    }

    private static ImmutableArray<GroupPowerLabel> Labels(params GroupScoreView[] groups) => GroupPowerLabels.Of(new PowerLayerContent([.. groups], [], []));

    [Fact]
    public void 单子棋串()
    {
        GroupPowerLabel label = Assert.Single(Labels(Group(P1, 1, "D4")));
        Assert.Equal(("D4", P1, "1"), (label.Coord.ToNotation(), label.Owner, label.Text));
    }

    [Fact]
    public void 标在重心最近的棋子上()
    {
        Assert.Equal("E4", Assert.Single(Labels(Group(P1, 3, "D4", "E4", "F4"))).Coord.ToNotation());

        // 拐角串 D4-D5-E5：重心 (D 偏右三分之一, 4 与 5 之间偏上) → 离 D5 最近；输入顺序打乱结果不变。
        Assert.Equal("D5", Assert.Single(Labels(Group(P1, 3, "E5", "D4", "D5"))).Coord.ToNotation());
    }

    [Fact]
    public void 并列取坐标序最小()
    {
        // M5：并列时改取坐标序最大 → 红。两枚横排取左边、两枚竖排取下边，输入顺序不影响。
        Assert.Equal("D4", Assert.Single(Labels(Group(P1, 2, "E4", "D4"))).Coord.ToNotation());
        Assert.Equal("D4", Assert.Single(Labels(Group(P1, 2, "D5", "D4"))).Coord.ToNotation());
    }

    [Fact]
    public void 数值与势力层一致()
    {
        GroupScoreView[] groups =
        [
            Group(P1, 7, "D4", "E4"),
            Group(P2, 1_234_567, "K9"),
            Group(P2, 12, "A1", "A2", "B2"),
        ];
        ImmutableArray<GroupPowerLabel> labels = Labels(groups);

        // 每条棋串恰一条，文案 = 势力层那份军势的缩写（与势力栏同一规则），归属照抄。
        Assert.Equal(groups.Length, labels.Length);
        Assert.Equal(groups.Select(g => (g.Owner, Siege.Presentation.Text.Labels.CompactPower(g.Power.Power))), labels.Select(l => (l.Owner, l.Text)));
        Assert.Equal(["7", "12"], [labels[0].Text, labels[2].Text]);
        Assert.NotEqual("1234567", labels[1].Text);
    }

    [Fact]
    public void 标注带档位()
    {
        // 三条棋串，军势 1、10、40 → 档位一、三、五；字号按分档样式表依次增大，一档与分档之前相同（88）。
        ImmutableArray<GroupPowerLabel> labels = Labels(Group(P1, 1, "D4"), Group(P2, 10, "K9"), Group(P2, 40, "A1", "A2", "B2"));
        Assert.Equal([1, 3, 5], labels.Select(l => l.Tier));

        int[] fonts = [.. labels.Select(l => NumberTierStyle.For(l.Tier).GroupLabelFontSize)];
        Assert.Equal([88, 106, 132], fonts);
        Assert.True(fonts[0] < fonts[1] && fonts[1] < fonts[2]);

        // 档位只是多带的一项：落点、归属、文案不变；超大军势（10^30）仍是五档。
        Assert.Equal(["D4:1", "K9:10", "A2:40"], labels.Select(l => $"{l.Coord.ToNotation()}:{l.Text}"));
        Assert.Equal(5, Assert.Single(Labels(Group(P1, BigInteger.Pow(10, 30), "D4"))).Tier);
    }
}
