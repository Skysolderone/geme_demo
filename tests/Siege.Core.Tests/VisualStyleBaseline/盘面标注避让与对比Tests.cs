using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Core.Tests.SettlementShow;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Show;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>
/// 规格：visual-style-baseline —— Requirement: 盘面标注的避让与对比（board-labels 段 A）。
/// 坐标标注让位走真实的演出时间线（<see cref="ShowTimeline.Mask"/> → <see cref="CoordinateLabelYield.Of"/>），不手拼遮罩；
/// 颜色、字号、描边的期望值是按 design.md D2 / D3 的式子在测试里独立算出的字面量（算法见各条注释）。
/// 「次要文字透明度」在本段只断言常量；引擎层脚本不另写数值的扫描归段 B。
/// </summary>
/// <remarks>
/// 变异验证（段 A；脚本做法：二进制读入原文、另存带时间戳的备份 → 断言锚点恰命中 1 次 → 改写 → 跑 VisualStyleBaseline + TacticalLayers + SettlementShow 三个命名空间
/// → finally 里写回原文并逐字节比对、刷新 mtime；红数取自测试输出的统计行；基线 305 通过 / 2 跳过，全部还原后复跑 0 红）：
/// 让位（<c>CoordinateLabelYield.cs</c>）——M-Y1「远边条件 &gt;= 改 &gt;」红 1（中部不让位）；M-Y2「远边左侧少一列」红 5；M-Y3「左边条件 &lt; 改 &lt;=」红 1（中部）；
/// M-Y4「右边让位记到左表」红 2（右边、中部）；M-Y5「纵向少一行」红 2（右边、中部）；M-Y6「纵向不截在棋盘内」红 2（右边、远边）；
/// M-Y7「多条目不取最小、后写覆盖」红 1（淡出且取最小）；M-Y8「让位到底 150 改 0」红 5；M-Y9「不淡出、立即到底」红 1（淡出）；
/// M-Y10「近边条目也让列字母」红 1（远边）；M-Y11「右边条件 &gt;= 改 &gt;」红 1（中部）。
/// 时间线（<c>ShowTimeline.cs</c>）——M-T1「已显示毫秒数取节拍内时刻、不减条目开始时刻」红 1（淡出且取最小）；
/// M-T2「结果停留满后仍列出」红 5（本类 揭示结束后恢复，及既有的 飘字停留时长 / 常驻标注 四条）。
/// 角位（<c>GroupPowerLabels.cs</c>）——M-G1「最右一列判成倒数第二列」、M-G2「角位按棋串里最右的棋子而非锚格」各红 1（最右一列换角）。
/// 配色与下限（<c>BoardLabelStyle.cs</c>）——M-S1「常驻标注描边改纯黑」、M-S2「提亮 600 改 750」、M-S3「B 通道误用 G」、M-S4「描边下限 12 改 10」、
/// M-S5「描边恒取下限」、M-S12「坐标标注字色改值」各红 1（四个阵营）；M-S6「算式行向白色混 40%」、M-S7「算式字号下限 72 改 64」、M-S8「算式描边下限 10 改 8」、
/// M-S9「算式描边不按字号比例」、M-S11「算式字号比例 60 改 50」各红 1（算式行同色）。
/// M-S10「算式描边去掉 max 下限」红 0——<b>等价变异</b>：现有五档按比例算出的描边 10 / 10 / 10 / 10 / 13 都不低于 10，下限在当前样式表上不起作用；
/// 下限的取值由 M-S8 钉住。常驻标注描边下限 12 同理（比例值 20–30），取值由 M-S4 钉住。
/// 次要文字（<c>VisualBaseline.cs</c>）——M-U1「620 改 600」红 1。
/// 只改测试——M-TT1「红、蓝期望字色对调」红 1（四个阵营）。
/// check 段 A 补测后的变异（同一脚本做法，基线 306 通过）：上面 M-S10 与常驻标注描边下限的等价变异，改由 <c>RevealFormulaOutlineFor</c> / <c>GroupLabelOutlineFor</c>
/// 用假设样式值钉住——C5「算式描边去掉 max」红 1（算式行同色）、C6「常驻描边去掉 max」红 1（四个阵营）。
/// C1「远边只认 y == 高 − 1」红 3；C2「左右让位纵向往下」红 4；C8「远边列不截在宽内」红 2；C9「右边只认最右一列」红 2（窄矮棋盘、中部）；
/// C3「PermilleAt 不把负数夹到 0」红 1、C7「ShownMs 取当前步内时刻」红 1（均为 淡出且取最小 的补充断言；补测前两条都是 0 红）；
/// C4「CornerOf 写成 x ≥ 宽」红 1（最右一列换角，宽 1 断言）。复跑 M-Y7、M-S2 各红 1。
/// </remarks>
public class 盘面标注避让与对比Tests
{
    private static readonly PlayerId P1 = new(1);
    private static readonly PlayerId P2 = new(2);

    /// <summary>规格算例的棋盘：13×13。</summary>
    private const int Size = 13;

    /// <summary>单步条目（只有基础军势）：一步 220 ms，一档结果停留 1400 ms。</summary>
    private static GroupPower Single(PlayerId owner, int power, string cell) => ShowFixtures.Group(owner, power, 0, 0, power, cell);

    /// <summary>演出：军势揭示节拍 → 三条横幅（2400 ms，让演出在一档结果停留满之后才结束）。揭示节拍从 0 ms 开始。</summary>
    private static ShowTimeline Show(ShowDuration duration, params GroupPower[] revealed) => new(
        [
            new PowerRevealBeat([.. revealed.Select(RevealEntry.From)]),
            new BannerBeat(["甲", "乙", "丙"]),
        ],
        duration);

    /// <summary>推进到演出内第 <paramref name="ms"/> 毫秒后的遮罩。</summary>
    private static ShowMask MaskAt(ShowTimeline timeline, int ms)
    {
        if (ms > 0)
        {
            timeline.Advance(ms);
        }

        return timeline.Mask();
    }

    /// <summary>一张让位表的文本投影："下标:进度,…"（按下标升序）。</summary>
    private static string Text(ImmutableDictionary<int, int> table) =>
        string.Join(",", table.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));

    /// <summary>三张表的投影："远[…] 左[…] 右[…]"。</summary>
    private static string Text(CoordinateYieldView view) => $"远[{Text(view.FarColumns)}] 左[{Text(view.LeftRows)}] 右[{Text(view.RightRows)}]";

    /// <summary>13×13 上单个条目在演出第 200 ms（已过 150 ms 淡出）时的让位结果。</summary>
    private static CoordinateYieldView YieldOf(string anchor, int ms = 200) =>
        CoordinateLabelYield.Of(MaskAt(Show(ShowDuration.Normal, Single(P1, 3, anchor)), ms), Size, Size);

    [Fact]
    public void 远边的棋串让出列字母()
    {
        // 规格：13×13，锚格在第 13 行 F 列（x = 5，y = 12 = 远边）。左右各 2 列：D E F G H（x = 3..7）让到 150‰；行数字不让位。
        CoordinateYieldView view = YieldOf("F13");
        Assert.Equal("远[3:150,4:150,5:150,6:150,7:150] 左[] 右[]", Text(view));
        Assert.True(view.FarColumn(5) < CoordinateLabelYield.FullPermille);
        Assert.Equal(CoordinateLabelYield.FullPermille, view.FarColumn(8));
        Assert.Equal(CoordinateLabelYield.FullPermille, view.LeftRow(12));

        // 近边的列字母不让位：让位结果里没有近边那一张表（类型上就没有），近边行上的条目也不让出任何列字母（揭示条目向远边升起，压不到近边）。
        Assert.Equal("远[] 左[] 右[]", Text(YieldOf("F1")));

        // 贴角时截在棋盘内：B13 只让 A..D（x = 0..3），不出现负下标。
        Assert.Equal("远[0:150,1:150,2:150,3:150] 左[12:150] 右[]", Text(YieldOf("B13")));
    }

    [Fact]
    public void 右边的棋串让出行数字()
    {
        // 规格：锚格在最右一列（N，x = 12）第 5 行（y = 4）：右边第 5..8 行（y = 4..7）让位，左边不让。
        Assert.Equal("远[] 左[] 右[4:150,5:150,6:150,7:150]", Text(YieldOf("N5")));

        // 对称：最左一列第 5 行只让左边。
        Assert.Equal("远[] 左[4:150,5:150,6:150,7:150] 右[]", Text(YieldOf("A5")));

        // 贴远角：N12（x = 12，y = 11）右边只到第 13 行（y = 12）为止，同时远边让 L M N（x = 10..12）。
        Assert.Equal("远[10:150,11:150,12:150] 左[] 右[11:150,12:150]", Text(YieldOf("N12")));
    }

    [Fact]
    public void 棋盘中部不让位()
    {
        // 规格：锚格在棋盘中部（G7）→ 没有任何让位。
        Assert.True(YieldOf("G7").IsEmpty);

        // 让位范围的边界（13×13）：远边 3 行（第 11 行 y = 10 起），左右 2 列（B 让、C 不让；M 让、L 不让）。
        Assert.Equal("远[] 左[] 右[]", Text(YieldOf("G10")));
        Assert.Equal("远[4:150,5:150,6:150,7:150,8:150] 左[] 右[]", Text(YieldOf("G11")));
        Assert.Equal("远[] 左[] 右[]", Text(YieldOf("C7")));
        Assert.Equal("远[] 左[6:150,7:150,8:150,9:150] 右[]", Text(YieldOf("B7")));
        Assert.Equal("远[] 左[] 右[]", Text(YieldOf("L7")));
        Assert.Equal("远[] 左[] 右[6:150,7:150,8:150,9:150]", Text(YieldOf("M7")));
    }

    [Fact]
    public void 窄矮棋盘的让位截在棋盘内()
    {
        // 不对应单独的 Scenario（check 段 A 补）：棋盘比让位范围还窄、还矮时，让位只落在棋盘内的下标上，一个锚格可同时让三张表。
        // 3×2：B2（x = 1，y = 1）离远边 0 行、离左右都在 2 列内 → 远边 A..C 全让，左右各让第 2 行（y = 1，往上已出界）。
        Assert.Equal("远[0:150,1:150,2:150] 左[1:150] 右[1:150]", Text(CoordinateLabelYield.Of(MaskAt(Show(ShowDuration.Normal, Single(P1, 3, "B2")), 200), 3, 2)));

        // 3×2：A1（x = 0，y = 0）→ 远边（2 − 3 < 0，任何行都算靠远边）A..C，左边第 1、2 行；右边要 x ≥ 3 − 2 = 1，不让。
        Assert.Equal("远[0:150,1:150,2:150] 左[0:150,1:150] 右[]", Text(CoordinateLabelYield.Of(MaskAt(Show(ShowDuration.Normal, Single(P1, 3, "A1")), 200), 3, 2)));

        // 1×1：唯一一格同时是最左与最右一列。
        Assert.Equal("远[0:150] 左[0:150] 右[0:150]", Text(CoordinateLabelYield.Of(MaskAt(Show(ShowDuration.Normal, Single(P1, 3, "A1")), 200), 1, 1)));
    }

    [Fact]
    public void 揭示结束后恢复()
    {
        // 规格：揭示条目消失之后全部完全显示。单步一档条目自 0 ms 起显示，结果停留 1400 ms：第 1399 ms 仍让位，第 1400 ms 条目消失、全部复原（演出仍在横幅里）。
        ShowTimeline timeline = Show(ShowDuration.Normal, Single(P1, 3, "F13"));
        ShowMask before = MaskAt(timeline, 1399);
        Assert.Single(before.Reveals);
        Assert.Equal("远[3:150,4:150,5:150,6:150,7:150] 左[] 右[]", Text(CoordinateLabelYield.Of(before, Size, Size)));

        ShowMask after = MaskAt(timeline, 1);
        Assert.Empty(after.Reveals);
        Assert.False(timeline.IsFinished);
        Assert.True(CoordinateLabelYield.Of(after, Size, Size).IsEmpty);

        // 演出遮罩为空（演出结束）与零时长模式：全部完全显示。
        Assert.True(CoordinateLabelYield.Of(ShowMask.Empty, Size, Size).IsEmpty);
        Assert.True(CoordinateLabelYield.Of(Show(ShowDuration.Zero, Single(P1, 3, "F13")).Mask(), Size, Size).IsEmpty);
        MaskAt(timeline, 5000);
        Assert.True(timeline.IsFinished);
        Assert.True(CoordinateLabelYield.Of(timeline.Mask(), Size, Size).IsEmpty);
    }

    [Fact]
    public void 让位随条目显示时长淡出且多条目取最小()
    {
        // 不对应单独的 Scenario，钉 design.md D1 的显现进度：条目出现后 150 ms 内从 1000 线性降到 150（1000 − 850 × 已显示 ÷ 150，整除），此后保持。
        Assert.Equal("3:1000,4:1000,5:1000,6:1000,7:1000", Text(YieldOf("F13", 0).FarColumns));
        Assert.Equal("3:575,4:575,5:575,6:575,7:575", Text(YieldOf("F13", 75).FarColumns));
        Assert.Equal("3:150,4:150,5:150,6:150,7:150", Text(YieldOf("F13", 150).FarColumns));

        // 两条单步条目依次播放：甲（F13）自 0 ms、乙（H13）自 220 ms 起显示。第 300 ms：甲已显示 300 ms、乙 80 ms；
        // 遮罩里的「已显示毫秒数」是条目自己的时长，不是节拍时长。
        ShowMask mask = MaskAt(Show(ShowDuration.Normal, Single(P1, 3, "F13"), Single(P2, 5, "H13")), 300);
        Assert.Equal([300, 80], mask.Reveals.Select(r => r.ShownMs));

        // 乙让 F..K（x = 5..9）到 1000 − 850 × 80 ÷ 150 = 547；与甲重叠的 F G H 取较小的 150。
        Assert.Equal("远[3:150,4:150,5:150,6:150,7:150,8:547,9:547] 左[] 右[]", Text(CoordinateLabelYield.Of(mask, Size, Size)));

        // 多步条目（check 段 A 补）：已显示毫秒数从第一步开始算，不是从当前这一步开始算。
        // 规格算例「四步揭示」（基础 5、加值 2、倍增 1、军势 10）每步 220 ms：第 300 ms 在第二步（220 起）里，已显示 300 而不是 80。
        RevealDisplay multi = Assert.Single(MaskAt(Show(ShowDuration.Normal, ShowFixtures.Group(P1, 5, 2, 1, 10, "F13")), 300).Reveals);
        Assert.Equal("5+2", multi.RunningText);
        Assert.Equal(300, multi.ShownMs);

        // 纯函数的边界：负的已显示时长按 0 算（完全显示，不超过 1000）。
        Assert.Equal(CoordinateLabelYield.FullPermille, CoordinateLabelYield.PermilleAt(-50));
    }

    [Fact]
    public void 算式行与结果同色()
    {
        // 规格算例（settlement-show「高档末步定格」）：基础 40、位置加值 4、倍增子 1 枚、军势 66 → 四步，档位 二 三 四 五，末步在 660 ms 开始。
        ShowMask mask = MaskAt(Show(ShowDuration.Normal, ShowFixtures.Group(P1, 40, 4, 1, 66, "D4")), 700);
        RevealDisplay reveal = Assert.Single(mask.Reveals);
        Assert.True(reveal.AtFinal);
        Assert.Equal(5, reveal.StepTier);

        // 算式行与结果同为该步的揭示色（五档 (236, 72, 52)），不向白色混色。
        Assert.Equal(new Rgba(236, 72, 52), BoardLabelStyle.RevealFormulaColorOf(reveal.StepTier));
        Assert.Equal(NumberTierStyle.For(reveal.StepTier).RevealColor, BoardLabelStyle.RevealFormulaColorOf(reveal.StepTier));

        // 下限：描边 10、字号 72（与一档结果同字号）。
        Assert.Equal((10, 72), (BoardLabelStyle.RevealFormulaMinOutline, BoardLabelStyle.RevealFormulaMinFontSize));
        Assert.Equal(72, NumberTierStyle.For(1).RevealFontSize);

        // 各档（字号 72 / 84 / 100 / 124 / 156，描边 10 / 12 / 14 / 18 / 22）：
        // 字号 = max(72, 结果字号 × 60 ÷ 100) → 72 72 72 74 93；描边 = max(10, 结果描边 × 算式字号 ÷ 结果字号) → 10 10 10 10 13。
        int[] tiers = [1, 2, 3, 4, 5];
        Assert.Equal([72, 72, 72, 74, 93], tiers.Select(BoardLabelStyle.RevealFormulaFontSizeOf));
        Assert.Equal([10, 10, 10, 10, 13], tiers.Select(BoardLabelStyle.RevealFormulaOutlineOf));
        Assert.All(tiers, t => Assert.True(BoardLabelStyle.RevealFormulaOutlineOf(t) >= BoardLabelStyle.RevealFormulaMinOutline));

        // 现有五档的比例描边都不低于 10，下限在样式表上不起作用（check 段 A：去掉 max 是等价变异）。用假设的样式值直接钉住规则本身：
        // 比例值 4 × 72 ÷ 72 = 4 → 抬到 10；14 × 74 ÷ 124 = 8 → 抬到 10；比例值 20 × 72 ÷ 72 = 20 高于下限 → 原样。
        Assert.Equal(10, BoardLabelStyle.RevealFormulaOutlineFor(4, 72, 72));
        Assert.Equal(10, BoardLabelStyle.RevealFormulaOutlineFor(14, 74, 124));
        Assert.Equal(20, BoardLabelStyle.RevealFormulaOutlineFor(20, 72, 72));
        Assert.Equal(
            [new Rgba(244, 240, 230), new Rgba(244, 226, 164), new Rgba(240, 196, 72), new Rgba(244, 146, 48), new Rgba(236, 72, 52)],
            tiers.Select(BoardLabelStyle.RevealFormulaColorOf));
    }

    [Fact]
    public void 四个阵营的标注读得清()
    {
        // 坐标标注两色（自 Visuals 搬来，值不变），常驻标注描边与坐标标注描边同色。
        Assert.Equal(new Rgba(206, 202, 190), BoardLabelStyle.CoordinateLabel);
        Assert.Equal(new Rgba(18, 19, 23), BoardLabelStyle.CoordinateLabelOutline);

        // 字色 = 阵营主色各通道 c + (255 − c) × 600 ÷ 1000（整除）：
        // 红 (196, 58, 48) → (231, 176, 172)；蓝 (52, 104, 196) → (173, 194, 231)；金 (214, 170, 52) → (238, 221, 173)；紫 (128, 72, 176) → (204, 181, 223)。
        // 亮度（Rec.601 ×1000 整除）191 / 191 / 220 / 192，描边亮度 19，亮度差 172 / 172 / 201 / 173。
        (Rgba Text, int Gap)[] expected =
        [
            (new Rgba(231, 176, 172), 172),
            (new Rgba(173, 194, 231), 172),
            (new Rgba(238, 221, 173), 201),
            (new Rgba(204, 181, 223), 173),
        ];
        for (int p = 0; p < expected.Length; p++)
        {
            (Rgba text, Rgba outline) = BoardLabelStyle.GroupLabelColors(new PlayerId(p));
            Assert.Equal(expected[p].Text, text);
            Assert.Equal(new Rgba(18, 19, 23), outline);
            Assert.Equal(expected[p].Gap, text.Luma - outline.Luma);
            Assert.True(text.Luma - outline.Luma >= 120, $"P{p} 亮度差 {text.Luma - outline.Luma}");
        }

        // 描边宽度不小于下限 12；各档按字号比例为 20 / 21 / 24 / 26 / 30。
        Assert.Equal(12, BoardLabelStyle.GroupLabelMinOutline);
        Assert.Equal([20, 21, 24, 26, 30], new[] { 1, 2, 3, 4, 5 }.Select(BoardLabelStyle.GroupLabelOutlineOf));

        // 比例值都高于 12，下限在样式表上不起作用（check 段 A：去掉 max 是等价变异）。用假设的比例值直接钉住规则：5 → 12，12 → 12，13 → 13。
        Assert.Equal([12, 12, 13], new[] { 5, 12, 13 }.Select(BoardLabelStyle.GroupLabelOutlineFor));
    }

    [Fact]
    public void 最右一列换角()
    {
        // 13 列：锚格在 N 列（x = 12）→ 左前角；M 列、A 列 → 右前角。角位按锚格判断，不按棋串里最右的棋子：
        // L5-M5-N5 的锚格是 M5（右前角），N4-N5-N6 的锚格是 N5（左前角）。
        var content = new PowerLayerContent(
            [View(P1, "N5"), View(P1, "M5"), View(P2, "A1"), View(P2, "L5", "M5", "N5"), View(P1, "N4", "N5", "N6")], [], []);
        ImmutableArray<GroupPowerLabel> labels = GroupPowerLabels.Of(content, Size);
        Assert.Equal(
            ["N5:FrontLeft", "M5:FrontRight", "A1:FrontRight", "M5:FrontRight", "N5:FrontLeft"],
            labels.Select(l => $"{l.Coord.ToNotation()}:{l.Corner}"));

        // 同一格在更宽的棋盘上不是最右一列 → 右前角。
        Assert.Equal(LabelCorner.FrontRight, Assert.Single(GroupPowerLabels.Of(new PowerLayerContent([View(P1, "N5")], [], []), 19)).Corner);

        // 退化：宽 1 的棋盘上唯一一列就是最右一列 → 左前角（按 x == 宽 − 1，check 段 A 补）。
        Assert.Equal(LabelCorner.FrontLeft, GroupPowerLabels.CornerOf(Coord.Parse("A3"), 1));
    }

    [Fact]
    public void 次要文字透明度()
    {
        // 规格：次要文字的透明度定义在 UiTheme（千分比）。引擎层不另写数值的扫描归段 B。
        Assert.Equal(620, UiTheme.MutedTextAlphaPermille);
    }

    private static GroupScoreView View(PlayerId owner, params string[] cells)
    {
        ImmutableArray<Coord> stones = [.. cells.Select(Coord.Parse)];
        var detail = new GroupPower(owner, stones, BaseTotal: stones.Length, LineBonus: 0, SynergyBonus: 0, HighGroundBonus: 0,
            BannerBonus: 0, ChainBonus: 0, SentryBonus: 0, BoundaryBonus: 0, MultiplierCount: 0, Power: new BigInteger(stones.Length));
        return new GroupScoreView(owner, stones, GroupPowerView.From(detail), 0);
    }
}
