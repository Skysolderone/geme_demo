using System.Collections.Immutable;
using System.Numerics;
using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Core.Tests.SettlementShow;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// 规格：tactical-layers —— Requirement: 棋串军势常驻标注（follow-opponent D7）。<see cref="GroupPowerLabels"/> 只决定"标在哪一格、写什么"，
/// 数值原样取势力层内容里的棋串军势。重心最近用整数比较（坐标 × 枚数 − 坐标和）。
/// tiered-number-show 1.4 增「标注带档位」（design.md D6）：每条标注带该棋串军势的数值档位，字号按分档样式表逐档加大。
/// reveal-label-handoff 1.1 增六个场景（design.md D1）：结算演出期间被揭示棋串的常驻标注由遮罩的显现进度表（<see cref="ShowMask.GroupLabelPermille"/>）决定画不画、多透明，
/// 另有一条引擎层源码扫描（2.1，design.md D2）。检查阶段补一条不对应规格场景的守门 <see cref="压缩的揭示节拍下显现进度与结果年龄同源"/>（三条目、压缩节拍）。
/// </summary>
/// <remarks>
/// 变异验证（tiered-number-show 段 A；脚本做法与记录见 SettlementShow/数值档位Tests）：
/// M-L1「标注档位恒为一档」——<c>GroupPowerLabels.Of</c> 里 <c>NumberTier.Of(g.Power.Power)</c> 改为 <c>NumberTier.Lowest</c>
/// → 红 9（本类 标注带档位、数值档位Tests 的 三处呈现同一数值同一档 七条 / 引擎层不取档 的反面命中）。
/// M5（并列取坐标序最大）复跑：红 4（本类 并列取坐标序最小，以及落点共用后的 军势揭示节拍Tests.落点与常驻标注同一格 等三条）。
/// 变异验证（reveal-label-handoff）：见本类里「reveal-label-handoff：演出期间被揭示棋串的常驻标注」一节开头的记录表（22 条）。
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

    // ---------- reveal-label-handoff：演出期间被揭示棋串的常驻标注（design.md D1 / D2） ----------
    //
    // 变异验证。每条由脚本做：二进制读入原文并另存带时间戳的备份 → 断言锚点恰命中 1 次 → 改写 → 跑 TacticalLayers + SettlementShow 两个命名空间
    // （基线 228 通过 / 2 跳过）→ finally 里重新写回原文（mtime 随之刷新）并逐字节比对；全部还原后确认跑 0 红。红数取自测试输出的统计行。
    // 下面的红数是检查阶段补完测试之后 22 条全部复跑的读数（实现方交付时 14 条，基线 227；补的是 压缩的揭示节拍下显现进度与结果年龄同源 一条与引擎层扫描的 ⑤ ⑥）。
    // 呈现层（src/Siege.Presentation/Show/ShowTimeline.cs），「同源」指 压缩的揭示节拍下显现进度与结果年龄同源：
    // M-LH1「未播到揭示节拍时不列出」——Mask 第二段循环里军势揭示分支的 if (!current) 加一个运行时恒假的条件 → 红 3（揭示之前不显示、未被揭示的棋串不受影响、零时长模式）。
    // M-LH2「尚未轮到的条目不隐藏标注」——AddReveals 里尚未轮到分支去掉 HoldLabel(…, 0) → 红 2（揭示之前不显示、同源）。
    // M-LH3「结果停留前半段不隐藏」——LabelPermilleAt 改为 Math.Max(0, 年龄) → 红 3（结果停留前半段仍隐藏、结果淡出时标注淡入、同源）。
    // M-LH4「淡入斜率 ×2 改 ×1」→ 红 2（结果淡出时标注淡入、同源）。
    // M-LH5「结果停留满后仍列出」——停留满的分支在 continue 前补一句 HoldLabel(…, 0) → 红 3（揭示条目消失后完全显示、未被揭示的棋串不受影响、同源）。
    // M-LH6「显现进度不计入遮罩是否为空」——ShowMask.IsEmpty 去掉 GroupLabelPermille.IsEmpty → 红 1（零时长模式）。
    // M-LH7「未到末步的条目不隐藏标注」——AddReveals 末尾的 HoldLabel 只在 atFinal 时调用 → 红 3（揭示之前不显示、未被揭示的棋串不受影响、同源）。
    // M-SC2（既有变异复跑：Mask 的守卫去掉 IsFinished ||）→ 红 7，含本类 零时长模式。
    // 检查阶段另挑的三条，补测试之前都是 0 红，补后各红 1（同源）：
    // M-CK1「尚未轮到的条目只隐藏第一条」——AddReveals 尚未轮到分支的 continue 改回 break（两条目的用例测不出：紧接着的那条照样被隐藏，要三条）。
    // M-CK2「显现进度按未压缩的末步时刻另算一套」——AddReveals 末尾改为按 各步基准时长之和 自己算结果年龄再取 LabelPermilleAt（不压缩的节拍下与原式相等）。
    // M-CK3「揭示节拍进行中整拍条目一律按 0」——Mask 第二段循环的 if (!current) 改为运行时恒真（先揭示的条目在节拍还没播完时就该开始淡入）。
    // 引擎层（src/godot/scripts/BoardView.cs，都只红 引擎层的标注透明度只取自遮罩的显现进度 一条）：
    // M-LE1「透明度写死」——float alpha = shown / 1000f 改为 1f。
    // M-LE2「引擎层自己从揭示条目推进度」——shown 改为 mask.Reveals.Any(r => r.Coord == item.Coord) ? 0 : 满值。
    // M-LE3「描边不乘透明度」——OutlineModulate 去掉 alpha。
    // M-LE4「逐帧重画条件 || 改 &&」——RefreshShow 里的重画条件。
    // M-LE5「完整刷新不带遮罩」——Refresh 里 DrawGroupPower(mask ?? ShowMask.Empty) 改为 DrawGroupPower(ShowMask.Empty)。
    // M-LE6「进度 0 也画」——if (shown <= 0) 改为 if (shown < 0)。
    // 检查阶段另挑的五条（M-CK7 交付时已红，其余四条补 ⑤ ⑥ 之前 0 红）：
    // M-CK4「全局预览下也画常驻标注」——Refresh 里取标注列表去掉 Rig.IsOverview ||。
    // M-CK5「重画前不清标注层」——DrawGroupPower 去掉 Clear(_groupLabelLayer)（演出逐帧重画会一帧帧叠上去）。
    // M-CK6「标注仍挂回叠加层」——_groupLabelLayer.AddChild(label) 改为 _overlay.AddChild(label)（标注层清不掉它）。
    // M-CK7「画过非空表也不记」——_groupLabelsMasked 恒为假（表空之后不再重画，半透明的标注留到演出结束）。
    // M-CK8「完整刷新不更新缓存的标注列表」——已有一份就沿用（结算后标注的落点与数值过期）。

    /// <summary>棋串甲：军势 3（一档，结果停留 1400 ms），算式「2+1 = 3」三步——前两步各 220 ms，末步在揭示节拍开始后 440 ms 开始。</summary>
    private static readonly GroupPower Alpha = ShowFixtures.Group(P1, 2, 1, 0, 3, "D4", "E4", "F4");

    /// <summary>棋串乙：军势 5（二档），只有一步。</summary>
    private static readonly GroupPower Beta = ShowFixtures.Group(P2, 5, 0, 0, 5, "K9");

    /// <summary>军势揭示节拍的开始时刻：落子 1 枚 250 ms + 提子 600 ms。</summary>
    private const int RevealStartMs = 250 + 600;

    /// <summary>棋串甲末步的开始时刻（演出内）。</summary>
    private const int AlphaFinalMs = RevealStartMs + 440;

    /// <summary>一次结算的演出：落子（250 ms）→ 提子（600 ms）→ 军势揭示 → 势力（900 ms）→ 横幅一条（800 ms，让演出在结果停留满之后才结束）。</summary>
    private static ShowTimeline Show(ShowDuration duration, params GroupPower[] revealed) => new(
        [
            new PlacementBeat([new PlacedPiece(Coord.Parse("E4"), PieceType.Basic, P1)]),
            new CaptureBeat([new CapturedPiece(Coord.Parse("H8"), P2, PieceType.Basic)]),
            new PowerRevealBeat([.. revealed.Select(RevealEntry.From)]),
            new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)]),
            new BannerBeat([BannerBeat.MatchEndedText]),
        ],
        duration);

    /// <summary>这条棋串的常驻标注（走标注自己的投影，不取揭示条目的落点）。</summary>
    private static GroupPowerLabel LabelOf(GroupPower group) =>
        Assert.Single(Labels(new GroupScoreView(group.Owner, group.Stones, GroupPowerView.From(group), 0)));

    /// <summary>显现进度表的文本投影："落点:进度,…"（按坐标序）。</summary>
    private static string LabelText(ShowMask mask) =>
        string.Join(",", mask.GroupLabelPermille.OrderBy(k => k.Key).Select(k => $"{k.Key.ToNotation()}:{k.Value}"));

    [Fact]
    public void 揭示之前不显示()
    {
        // 两条棋串都含本次落子：甲先揭示（三步 660 ms），乙在甲的末步结束后才开始。标注落点取自常驻标注自己的投影（甲在重心 E4）。
        Assert.Equal(("E4", "K9"), (LabelOf(Alpha).Coord.ToNotation(), LabelOf(Beta).Coord.ToNotation()));
        ShowTimeline timeline = Show(ShowDuration.Normal, Alpha, Beta);

        // 落子节拍、提子节拍期间：军势揭示节拍尚未播到，它的全部条目都列出，进度 0。
        Assert.IsType<PlacementBeat>(timeline.Current);
        Assert.Equal("E4:0,K9:0", LabelText(timeline.Mask()));
        timeline.Advance(250);
        Assert.IsType<CaptureBeat>(timeline.Current);
        Assert.Equal("E4:0,K9:0", LabelText(timeline.Mask()));

        // 军势揭示节拍开始：甲在第一步、未到末步 → 0；乙尚未轮到（条目本身不列），标注同样先隐藏。
        timeline.Advance(600);
        Assert.IsType<PowerRevealBeat>(timeline.Current);
        ShowMask mask = timeline.Mask();
        RevealDisplay first = Assert.Single(mask.Reveals);
        Assert.Equal(("E4", false), (first.Coord.ToNotation(), first.AtFinal));
        Assert.Equal("E4:0,K9:0", LabelText(mask));

        // 甲末步开始前 1 ms 仍未到末步；末步开始那一刻结果年龄 0，还是 0。
        timeline.Advance(439);
        Assert.False(Assert.Single(timeline.Mask().Reveals).AtFinal);
        Assert.Equal("E4:0,K9:0", LabelText(timeline.Mask()));
        timeline.Advance(1);
        Assert.True(Assert.Single(timeline.Mask().Reveals).AtFinal);
        Assert.Equal("E4:0,K9:0", LabelText(timeline.Mask()));
    }

    [Fact]
    public void 结果停留前半段仍隐藏()
    {
        // 规格算例：一档条目（结果停留 1400 ms）末步开始后 600 ms → 显现进度 0。
        ShowTimeline timeline = Show(ShowDuration.Normal, Alpha);
        timeline.Advance(AlphaFinalMs + 600);
        ShowMask mask = timeline.Mask();
        RevealDisplay reveal = Assert.Single(mask.Reveals);
        Assert.Equal((true, 1, 600 * 1000 / 1400), (reveal.AtFinal, reveal.FinalTier, reveal.ResultAgePermille));
        Assert.Equal("E4:0", LabelText(mask));

        // 停留恰好过半（700 ms，结果年龄 500‰）：max(0, (500 − 500) × 2) = 0，淡入从这里起步。
        timeline.Advance(100);
        Assert.Equal(500, Assert.Single(timeline.Mask().Reveals).ResultAgePermille);
        Assert.Equal("E4:0", LabelText(timeline.Mask()));
    }

    [Fact]
    public void 结果淡出时标注淡入()
    {
        // 规格算例：同一条目末步开始后 1050 ms（结果年龄 750‰）→ 显现进度 500‰。
        ShowTimeline timeline = Show(ShowDuration.Normal, Alpha);
        timeline.Advance(AlphaFinalMs + 1050);
        ShowMask mask = timeline.Mask();
        Assert.Equal(750, Assert.Single(mask.Reveals).ResultAgePermille);
        Assert.Equal("E4:500", LabelText(mask));

        // design.md D1 的算式 max(0, (a − 500) × 2)：1225 ms（年龄 875‰）→ 750‰；条目消失前 1 ms（年龄 999‰）→ 998‰，不到满值。
        timeline.Advance(175);
        Assert.Equal("E4:750", LabelText(timeline.Mask()));
        timeline.Advance(174);
        Assert.Equal(999, Assert.Single(timeline.Mask().Reveals).ResultAgePermille);
        Assert.Equal("E4:998", LabelText(timeline.Mask()));
    }

    [Fact]
    public void 揭示条目消失后完全显示()
    {
        // 规格算例：同一条目末步开始后 1400 ms 且演出尚未结束 → 不再列在显现进度里（不在表里即完全显示）。
        ShowTimeline timeline = Show(ShowDuration.Normal, Alpha);
        timeline.Advance(AlphaFinalMs + 1399);
        Assert.True(timeline.Mask().GroupLabelPermille.ContainsKey(LabelOf(Alpha).Coord));

        timeline.Advance(1);
        Assert.False(timeline.IsFinished);
        ShowMask mask = timeline.Mask();
        Assert.Empty(mask.Reveals);
        Assert.False(mask.GroupLabelPermille.ContainsKey(LabelOf(Alpha).Coord));
        Assert.Empty(mask.GroupLabelPermille);

        // 此后直到演出播完都不再列出；播完遮罩为空。
        while (!timeline.IsFinished)
        {
            Assert.Empty(timeline.Mask().GroupLabelPermille);
            timeline.Advance(1);
        }

        Assert.True(timeline.Mask().IsEmpty);
        Assert.Empty(timeline.Mask().GroupLabelPermille);
    }

    [Fact]
    public void 未被揭示的棋串不受影响()
    {
        // 一次结算只在棋串甲落子：逐毫秒走完整场演出，棋串乙的标注落点从不在显现进度里。
        Coord alpha = LabelOf(Alpha).Coord;
        Coord beta = LabelOf(Beta).Coord;
        ShowTimeline timeline = Show(ShowDuration.Normal, Alpha);
        int held = 0;
        for (int ms = 0; !timeline.IsFinished; ms++)
        {
            ShowMask mask = timeline.Mask();
            Assert.False(mask.GroupLabelPermille.ContainsKey(beta), $"第 {ms} ms：{LabelText(mask)}");
            Assert.All(mask.GroupLabelPermille.Keys, key => Assert.Equal(alpha, key));
            held += mask.GroupLabelPermille.ContainsKey(alpha) ? 1 : 0;
            timeline.Advance(1);
        }

        // 样本口径下界：甲确实一直列着——自演出开始到末步开始后 1400 ms（否则上面的"不在"是对着空表说的）。
        Assert.Equal(AlphaFinalMs + 1400, held);
    }

    [Fact]
    public void 零时长模式()
    {
        // 零时长（无人值守）：创建即播完，显现进度为空、遮罩为空，推进也不出现。
        ShowTimeline zero = Show(ShowDuration.Zero, Alpha, Beta);
        Assert.True(zero.IsFinished);
        Assert.Empty(zero.Mask().GroupLabelPermille);
        Assert.True(zero.Mask().IsEmpty);
        zero.Advance(AlphaFinalMs + 600);
        Assert.Empty(zero.Mask().GroupLabelPermille);
        Assert.True(zero.Mask().IsEmpty);

        // 正常时长下显现进度计入"遮罩是否为空"：一拍没有任何变化的势力节拍在前、军势揭示在后——此刻遮罩里只有这一项中间态。
        var only = new ShowTimeline([new PowerBeat([]), new PowerRevealBeat([RevealEntry.From(Alpha)])], ShowDuration.Normal);
        ShowMask mask = only.Mask();
        Assert.Equal("E4:0", LabelText(mask));
        Assert.True((mask with { GroupLabelPermille = ImmutableDictionary<Coord, int>.Empty }).IsEmpty);
        Assert.False(mask.IsEmpty);

        // 演出播完同样为空。
        only.Advance(only.TotalDurationMs);
        Assert.True(only.IsFinished);
        Assert.Empty(only.Mask().GroupLabelPermille);
        Assert.True(only.Mask().IsEmpty);
    }

    [Fact]
    public void 压缩的揭示节拍下显现进度与结果年龄同源()
    {
        // 检查阶段补（design.md D1「由时间线状态推出」）。算例取 军势揭示节拍Tests「超过上限按比例压缩 / 压缩后遮罩与音效按压缩后的时刻」的同一组：
        // 三条四步棋串（军势 9 / 10 / 12，都是三档，结果停留 1600 ms），各步之和 2640 ms → 整拍 1600 ms，
        // 三条的末步分别开始于节拍内 400、933、1466 ms（未压缩是 660、1540、2420 ms）。
        GroupPower f6 = ShowFixtures.Group(P1, 4, 2, 1, 9, "F6");
        GroupPower c3 = ShowFixtures.Group(P1, 5, 2, 1, 10, "C3");
        GroupPower h9 = ShowFixtures.Group(P1, 6, 2, 1, 12, "H9");
        ShowTimeline timeline = Show(ShowDuration.Normal, f6, c3, h9);
        PowerRevealBeat beat = Assert.IsType<PowerRevealBeat>(timeline.Beats[2]);
        Assert.Equal((2640, 1600), (beat.RawDurationMs, beat.DurationMs));
        Assert.Equal([400, 933, 1466], Enumerable.Range(0, 3).Select(e => beat.StepStartMs(e, 3)));

        // 节拍内 200 ms：第一条在第二步，后两条都尚未轮到——三条的标注都隐藏着（不是只有紧接着的那一条）。
        timeline.Advance(RevealStartMs + 200);
        Assert.Equal("F6", Assert.Single(timeline.Mask().Reveals).Coord.ToNotation());
        Assert.Equal("C3:0,F6:0,H9:0", LabelText(timeline.Mask()));

        // 节拍内 1500 ms（军势揭示仍是当前节拍）：结果年龄按压缩后的末步时刻计——1100、567、34 ms ÷ 1600 → 687、354、21‰；
        // 显现进度 max(0, (年龄 − 500) × 2) → F6 374‰，另两条 0。若按未压缩的 660 ms 起算，F6 会是 (840 × 1000 ÷ 1600 − 500) × 2 = 50‰。
        timeline.Advance(1300);
        Assert.IsType<PowerRevealBeat>(timeline.Current);
        Assert.Equal([687, 354, 21], timeline.Mask().Reveals.Select(r => r.ResultAgePermille));
        Assert.Equal("C3:0,F6:374,H9:0", LabelText(timeline.Mask()));

        // 逐毫秒走完整场：列着的揭示条目，其落点的显现进度恒等于由该条目的结果年龄按 D1 算式（测试内独立写）算出的值；
        // 表里另外的落点只能是尚未轮到的条目（进度 0）——到过末步又不在揭示条目里的（结果停留满）不得再列。
        ShowTimeline full = Show(ShowDuration.Normal, f6, c3, h9);
        HashSet<Coord> reachedFinal = [];
        int fadingMs = 0;
        for (int ms = 0; !full.IsFinished; ms++)
        {
            ShowMask mask = full.Mask();
            foreach (RevealDisplay reveal in mask.Reveals)
            {
                int expected = reveal.AtFinal ? Math.Max(0, (reveal.ResultAgePermille - 500) * 2) : 0;
                Assert.True(
                    mask.GroupLabelPermille.TryGetValue(reveal.Coord, out int shown) && shown == expected,
                    $"第 {ms} ms {reveal.Coord.ToNotation()}：结果年龄 {reveal.ResultAgePermille}‰ 应为 {expected}‰，表里是 {LabelText(mask)}");
                fadingMs += expected > 0 ? 1 : 0;
                if (reveal.AtFinal)
                {
                    reachedFinal.Add(reveal.Coord);
                }
            }

            foreach ((Coord coord, int shown) in mask.GroupLabelPermille.Where(k => mask.Reveals.All(r => r.Coord != k.Key)))
            {
                Assert.True(shown == 0 && !reachedFinal.Contains(coord), $"第 {ms} ms {coord.ToNotation()}：不在揭示条目里却列着 {shown}‰");
            }

            full.Advance(1);
        }

        // 样本口径下界：三条都走完了淡入段——结果年龄 ≥ 501‰ 即末步开始后 802..1599 ms，每条 798 ms。
        Assert.Equal(3, reachedFinal.Count);
        Assert.Equal(3 * 798, fadingMs);
    }

    [Fact]
    public void 引擎层的标注透明度只取自遮罩的显现进度()
    {
        // 规格：呈现层给出"此刻显现多少"，引擎层只照着画（design.md D2）。src/godot 不在 siege.sln 里，用源码文本扫描守门（去掉注释后只看代码）。
        string view = PresentationFixtures.GodotScriptCode("BoardView.cs");
        Assert.True(view.Length >= 50_000, $"只读到 {view.Length} 字符");
        string draw = PresentationFixtures.MethodBody(view, "private void DrawGroupPower(ShowMask mask)");
        Assert.True(draw.Length >= 600, $"方法体只有 {draw.Length} 字符");

        // ① 进度按标注落点查遮罩的表，不在表里取满值；进度 0 不画。
        Assert.Matches(@"int\s+shown\s*=\s*mask\.GroupLabelPermille\.TryGetValue\(\s*item\.Coord\s*,\s*out\s+int\s+permille\s*\)\s*\?\s*permille\s*:\s*PowerInterpolation\.FullPermille\s*;", draw);
        Assert.Matches(@"if\s*\(\s*shown\s*<=\s*0\s*\)\s*\{\s*continue\s*;", draw);

        // ② 透明度就是进度 ÷ 1000，文字与描边都乘它；两个量各只赋值一次（不在别处被改写）。
        Assert.Matches(@"float\s+alpha\s*=\s*shown\s*/\s*1000f\s*;", draw);
        Assert.Matches(@"label\.Modulate\s*=\s*new\s+Color\([^;]*,\s*alpha\s*\)\s*;", draw);
        Assert.Matches(@"label\.OutlineModulate\s*=\s*new\s+Color\([^;]*,\s*alpha\s*\)\s*;", draw);
        Assert.Single(Regex.Matches(draw, @"(?<![A-Za-z_.])shown\s*=(?!=)"));
        Assert.Single(Regex.Matches(draw, @"(?<![A-Za-z_.])alpha\s*=(?!=)"));
        Assert.Single(Regex.Matches(draw, @"\.Modulate\s*="));
        Assert.Single(Regex.Matches(draw, @"\.OutlineModulate\s*="));

        // ③ 不自己算进度：画标注的方法里不读揭示条目、结果年龄与时间线。反面命中：同一判据在画揭示条目的方法里确实命中。
        Regex ownProgress = new(@"(?i)reveal\w*|resultage\w*|timeline\w*|elapsed\w*");
        Assert.Empty(ownProgress.Matches(draw).Select(m => m.Value).Distinct());
        Assert.Matches(ownProgress, PresentationFixtures.MethodBody(view, "private void DrawReveals(ShowMask mask)"));

        // ④ 两条刷新路径都把遮罩原样交给它：完整刷新带当前遮罩（演出开始那一帧就不画被揭示棋串的标注），
        // 演出逐帧刷新在"表非空或上一次照着非空的表画过"时重画，后者由画的那一次记下。
        Assert.Equal(
            ["DrawGroupPower(mask ?? ShowMask.Empty)", "DrawGroupPower(mask)"],
            Regex.Matches(view, @"(?<![A-Za-z])DrawGroupPower\(([^)]*)\)").Select(m => m.Value).Where(v => !v.Contains("ShowMask mask", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        string refreshShow = PresentationFixtures.MethodBody(view, "public void RefreshShow(ViewerWorld world, SceneTreatment treatment, ShowMask mask)");
        Assert.Matches(@"if\s*\(\s*!mask\.GroupLabelPermille\.IsEmpty\s*\|\|\s*_groupLabelsMasked\s*\)\s*\{\s*DrawGroupPower\(mask\)\s*;", refreshShow);
        Assert.Matches(@"_groupLabelsMasked\s*=\s*!mask\.GroupLabelPermille\.IsEmpty\s*;", draw);
        Assert.Single(Regex.Matches(view, @"_groupLabelsMasked\s*=(?!=)"));

        // ⑤ 标注自成一层（检查阶段补）：画之前先清这一层、只往这一层挂；整份脚本里清它与往它挂的都只有画标注这一处，画标注的方法不碰叠加层——
        // 否则演出逐帧重画会一帧帧叠上去，或者清不掉上一帧画的。
        Assert.Matches(@"^private void DrawGroupPower\(ShowMask mask\)\s*\{\s*Clear\(_groupLabelLayer\)\s*;", draw);
        Assert.Single(Regex.Matches(view, @"Clear\(_groupLabelLayer\)"));
        Assert.Matches(@"_groupLabelLayer\.AddChild\(label\)\s*;", draw);
        Assert.Single(Regex.Matches(view, @"_groupLabelLayer\.AddChild\("));
        Assert.Single(Regex.Matches(draw, @"\.AddChild\("));
        Assert.DoesNotContain("_overlay", draw, StringComparison.Ordinal);

        // ⑥ 标注列表只在完整刷新里取（全局预览下为空——规格「全局预览下不显示标注」），取完紧接着照当前遮罩画；别处不改它（另一处是字段声明的初值）。
        string refresh = PresentationFixtures.MethodBody(view, "public void Refresh(\n        ViewerWorld world,");
        Assert.Matches(
            @"_groupLabels\s*=\s*Rig\.IsOverview\s*\|\|\s*world\.Layer\(TacticalLayer\.Power,\s*reading,\s*thresholds\)\s*is\s+not\s+PowerLayerContent\s+power\s*\?\s*\[\]\s*:\s*GroupPowerLabels\.Of\(power\)\s*;"
            + @"\s*DrawGroupPower\(mask\s*\?\?\s*ShowMask\.Empty\)\s*;",
            refresh);
        Assert.Equal(2, Regex.Matches(view, @"(?<![A-Za-z_.])_groupLabels\s*=(?!=)").Count);
    }
}
