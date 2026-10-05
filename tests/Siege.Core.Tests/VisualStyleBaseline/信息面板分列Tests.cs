using System.Collections.Immutable;
using System.Numerics;
using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Hand;
using Siege.Presentation.Hud;
using Siege.Presentation.Layers;
using Siege.Presentation.Show;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 信息面板的分列排版</summary>
/// <remarks>
/// 覆盖能落到呈现层（<see cref="HudPanelRows"/>）的八条 Scenario；「插旗提示不被裁」是截图验证（hud-panels 段 B，<c>art/hud-panels/</c>）。
/// 段 B 另加 D9-5 / D9-6 两条与引擎层 <c>Hud.cs</c> 的源码守门（<c>src/godot</c> 不在 sln 里，只能读源码）。
/// 另有不对应 Scenario 的补充：design D1 的演出中间态明细规则（未开始 / 领地段 / 军势段 / 定格 / 名次升降 / 名次不变 / 增量正负与零增量 / 有状态又在演出中）。
/// 期望文字都在测试里写字面量；「信息不减少」用测试内独立的正则从重排前的 <c>CompactText</c> 抽数字，不调用被测函数的格式化。
/// 变异验证（hud-panels 段 A，D9-1 补改后全部重跑；脚本二进制读写、锚点恰命中 1 次、finally 还原后 SHA-256 与变异前一致并刷新 mtime；
/// 口径 <c>dotnet test tests/Siege.Core.Tests -c Release --filter FullyQualifiedName~信息面板分列Tests</c> 共 16 条，红数后列红的测试）：
/// 排名——M-H1「名次写成裸数字」→ 红 3（排名按列给出、演出未开始…、领地段…）；M-H2「无名次写成空串」→ 红 2（出局玩家的明细、有状态又在演出中…）；
/// M-H3「明细丢掉领地」→ 红 4（信息不减少、出局玩家的明细、大数缩写、排名按列给出）；M-H4「明细的棋串不缩写」→ 红 2（信息不减少、大数缩写）；M-H5「势力列不缩写」→ 红 2（同上）；
/// M-H6「阵营名取颜色枚举名」→ 红 2（出局玩家的明细、排名按列给出）；M-H7「有状态不置次要」→ 红 2、M-H8「丢掉状态文字」→ 红 2（出局玩家的明细、有状态又在演出中…）；
/// M-H9「有状态时丢掉拆分（D9-1 之前的写法）」→ 红 2（信息不减少、出局玩家的明细）；M-H21「状态与拆分顺序颠倒」→ 红 1（出局玩家的明细）；M-H10「本机标记取反」→ 红 2（本机玩家有标记、名次下降与增量正负…）。
/// 演出明细——M-H11「势力恒取总势力」→ 红 3（演出未开始…、领地段…、军势段…）；M-H12「名次恒取新名次」→ 红 1（演出未开始…）；
/// M-H13「定格也取段文字（为 null）」→ 红 3（定格…、名次下降…、有状态又在演出中…）；M-H14「段内取总增量文字」→ 红 3（领地段…、军势段…、名次下降…）；
/// M-H15「名次升降箭头反了」→ 红 4（领地段…、军势段…、定格…、名次下降…）；M-H16「名次提示不看是否滚动」→ 红 1（演出未开始…）；
/// M-H17「增量正负恒取总增量」→ 红 1（名次下降与增量正负按当前段取）；M-H18「明细是否增量恒假」→ 红 4（领地段…、军势段…、定格…、有状态又在演出中…）；
/// M-H19「演出中仍回落到拆分」→ 红 6（演出未开始…、领地段…、军势段…、定格…、名次下降…、有状态又在演出中…）；M-H20「分隔符改半角空格」→ 红 6（领地段…、军势段…、定格…、名次下降…、有状态又在演出中…、出局玩家的明细）。
/// 顺序条——M-O1「当前行动者取反」、M-O2「顺序倒排」、M-O4「顺序已定也给未定」各 → 红 1（当前行动者）；M-O3「顺序未定时不给未定」→ 红 2（行动顺序未定 两组数据）；
/// M-O5「提示文字改回 [5] 展开顺序层」→ 红 3（当前行动者 + 行动顺序未定 两组）；M-O6「只判 IsEmpty 不判 default」→ 红 1（行动顺序未定 的 default 一组）。
/// 手牌——M-D1「新征募数不标」、M-D2「无新征募也标（新 0）」、M-D3「弃牌阶段语义取默认」、M-D4「弃牌阶段不加前缀」、M-D5「名称列取整行旧文字」各 → 红 1（手牌分列）。
/// 主题——M-T1「ViewerMarkWidthPx 3 改 4」→ 红 1（自家标记宽度）。只改测试：M-X1「排名按列给出的领地 / 棋串期望对调」→ 红 1。
/// 以上红数是 16 条口径。check 阶段发现 C2 / C3 / C5 三条 0 红或只靠空名次偶然红，补了「名次不变时不给名次提示且零增量不算负」与「名次下降…」里的演出未开始一例，
/// 口径变为 17 条后重跑（同一脚本）：C1「当前行动者改为比较顺序第一个」→ 红 1（当前行动者）；C2「名次不变也输出名次提示」→ 红 2（名次不变…、有状态又在演出中…，补测试前红 1 且只因空名次）；
/// C3「DeltaIsNegative 去掉 isDelta 门」→ 红 1（名次下降…，补测试前红 0）；C4「弃牌阶段名称列取整行旧文字」→ 红 1（手牌分列）；C5「增量为 0 也算负（Sign ≤ 0）」→ 红 1（名次不变…，补测试前红 0）；
/// C6「演出中名次恒取旧名次」→ 红 1（领地段…）；重跑 M-D2 → 红 1、M-H4 → 红 2、M-H15 → 红 4，与上面一致。
/// 段 B（D9-5 / D9-6 与 D6 第二条源码守门，同一套脚本约定，口径 20 条）：
/// 引擎层 Hud.cs——G1「顺序条按钮整行拼文字带 ◀」、G2「手牌名称列取 row.Text」、G3「排名自己拼 第 N 名」、G4「CompactText 挪进 Hud 的私有辅助方法」、
/// G5「手牌选中加 ▶ 前缀」、G6「明细其余部分画整段 DetailText（D9-6 回退）」、G8「自家标记恒为空占位」、G9「手牌子 Label 字色恒为正文色」、
/// G10「阵营列取 faction.Name」、G12「手牌直接写按钮文字、不铺两列」、G13「顺序未定写字面量 未定」、G14「明细自己拼 领地 + 棋串」、G15「排名网格改成一列」
/// 各 → 红 1（引擎层只摆放不拼文字）；G7「顺序条删掉标题（D9-5 回退）」、G7b「标题挪到条目之后」各 → 红 1（顺序条保留标题）。
/// 呈现层——H1「状态不单列（DetailStatusText 恒 null）」、H2「其余部分含状态」、H5「其余为空时仍接全角空格」各 → 红 1（明细拆出状态部分）；
/// H3「标题带冒号」→ 红 1（顺序条保留标题）；H4「整段明细状态在后」→ 红 3（出局玩家的明细、明细拆出状态部分、有状态又在演出中…）。
/// 只改测试：X1「状态部分与其余部分的期望对调」→ 红 1。
/// 主会话裁决（演出中本段增量与势力列同字号）之后补：F1「本段增量字号恒取注释字号」、F2「其余部分标签不用 restSize」、F3「定格也放大」、
/// F4「状态部分也随放大」各 → 红 1（引擎层只摆放不拼文字）；G6 / G14 按新写法改锚点后重跑，仍各红 1。
/// 名次提示单列（主会话裁决：不随段首放大）之后重跑，H1 / H2 / H4 按新写法改锚点（红数同上：1 / 1 / 3），原 H5 随写法消失；新增：
/// F5「名次提示标签改用 restSize」→ 红 1（引擎层只摆放不拼文字）；H6「名次提示混进增量部分」、H7「名次提示字段丢失（整段明细仍含）」各 → 红 1（明细拆出状态部分）；
/// H8「名次提示整个丢失」→ 红 5（领地段… / 军势段… / 定格… / 名次下降… / 明细拆出状态部分）。F1–F4、H3、X1 重跑仍各红 1。
/// </remarks>
public class 信息面板分列Tests
{
    private static readonly PlayerId P0 = MatchFixtures.P0;
    private static readonly PlayerId P1 = MatchFixtures.P1;
    private static readonly PlayerId P2 = MatchFixtures.P2;
    private static readonly PlayerId P3 = MatchFixtures.P3;

    private static PlayerPowerRowView Row(PlayerId player, BigInteger total, int territory, BigInteger groups, int? rank, PlayerStatus status = PlayerStatus.Active, string? statusText = null) =>
        new(player, status, total, territory, groups, rank, statusText);

    private static string[] Columns(RankRowView view) => [view.RankText, view.NameText, view.PowerText, view.DetailText];

    // ---------- Scenario ----------

    [Fact]
    public void 排名按列给出()
    {
        RankRowView view = HudPanelRows.RankRow(Row(P2, 26, 8, 18, 2), null, P0);

        Assert.Equal(P2, view.Player);
        Assert.Equal("第 2 名", view.RankText);
        Assert.Equal("金方", view.NameText);
        Assert.Equal(new BigInteger(26), view.PowerValue);
        Assert.Equal("26", view.PowerText);
        Assert.Equal("领地 8 + 棋串 18", view.DetailText);
        Assert.False(view.DetailIsDelta);
        Assert.False(view.IsMuted);
    }

    [Fact]
    public void 出局玩家的明细()
    {
        RankRowView view = HudPanelRows.RankRow(Row(P3, 0, 0, 0, null, PlayerStatus.Eliminated, "已出局 · 不再行动"), null, P0);

        Assert.Equal("—", view.RankText);
        Assert.Equal("紫方", view.NameText);
        Assert.Equal("已出局 · 不再行动　领地 0 + 棋串 0", view.DetailText);
        Assert.True(view.IsMuted);
        Assert.False(view.DetailIsDelta);

        // D9-1：弃赛者领地、棋串不为 0 时，拆分照样在状态后面。
        RankRowView resigned = HudPanelRows.RankRow(Row(P1, 40, 15, 25, null, PlayerStatus.Resigned, "已弃赛 · 不再行动"), null, P0);
        Assert.Equal("已弃赛 · 不再行动　领地 15 + 棋串 25", resigned.DetailText);
        Assert.Equal("40", resigned.PowerText);
        Assert.True(resigned.IsMuted);
    }

    [Fact]
    public void 大数缩写()
    {
        PlayerPowerRowView row = Row(P0, 1_234_567, 0, 1_234_567, 1);
        RankRowView view = HudPanelRows.RankRow(row, null, P0);

        Assert.Equal("1.23M", view.PowerText);
        Assert.Equal(new BigInteger(1_234_567), view.PowerValue);
        Assert.Equal("领地 0 + 棋串 1.23M", view.DetailText);
        // 与重排之前排名栏的整行文字用的缩写相同。
        Assert.Equal("势力 1.23M（领地 0 + 棋串 1.23M）", row.CompactText);
    }

    [Fact]
    public void 本机玩家有标记()
    {
        PlayerPowerRowView[] rows = [Row(P0, 30, 10, 20, 1), Row(P1, 26, 8, 18, 2), Row(P2, 12, 2, 10, 3), Row(P3, 0, 0, 0, null, PlayerStatus.Eliminated, "已出局 · 不再行动")];

        RankRowView[] views = [.. rows.Select(r => HudPanelRows.RankRow(r, null, P1))];

        Assert.Equal([false, true, false, false], views.Select(v => v.IsViewer));
    }

    [Fact]
    public void 当前行动者()
    {
        MatchPublicView view = MatchFixtures.Started().Publish() with { ActionOrder = [P1, P2, P3, P0], CurrentPlayer = P3 };

        OrderBarView bar = HudPanelRows.OrderBar(view);

        Assert.Equal(["蓝方", "金方", "紫方", "红方"], bar.Entries.Select(e => e.NameText));
        Assert.Equal([P1, P2, P3, P0], bar.Entries.Select(e => e.Player));
        Assert.Equal([false, false, true, false], bar.Entries.Select(e => e.IsCurrent));
        Assert.Null(bar.EmptyText);
        Assert.Equal("[5] 顺序层", bar.HintText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 行动顺序未定(bool defaultArray)
    {
        MatchPublicView view = MatchFixtures.Started().Publish() with
        {
            ActionOrder = defaultArray ? default : ImmutableArray<PlayerId>.Empty,
            CurrentPlayer = null,
        };

        OrderBarView bar = HudPanelRows.OrderBar(view);

        Assert.Empty(bar.Entries);
        Assert.DoesNotContain(bar.Entries, e => e.IsCurrent);
        Assert.Equal("未定", bar.EmptyText);
        Assert.Equal("[5] 顺序层", bar.HintText);
    }

    [Fact]
    public void 手牌分列()
    {
        var gained = new OwnHandRowView(PieceType.Basic, "普通子", 5, 1, "普通子 ×5（其中 1 枚为本轮新征募）");
        var plain = new OwnHandRowView(PieceType.Fortress, "堡垒子", 2, 0, "堡垒子 ×2");

        HandRowView row = HudPanelRows.HandRow(gained, discard: false);
        Assert.Equal(PieceType.Basic, row.Type);
        Assert.Equal("普通子", row.NameText);
        Assert.Equal("×5（新 1）", row.CountText);
        Assert.Equal(ButtonKind.Default, row.Kind);

        HandRowView noGain = HudPanelRows.HandRow(plain, discard: false);
        Assert.Equal("堡垒子", noGain.NameText);
        Assert.Equal("×2", noGain.CountText);

        // 弃牌阶段：危险语义，数量列不变。
        HandRowView discard = HudPanelRows.HandRow(gained, discard: true);
        Assert.Equal("弃掉整类：普通子", discard.NameText);
        Assert.Equal("×5（新 1）", discard.CountText);
        Assert.Equal(ButtonKind.Danger, discard.Kind);
    }

    [Fact]
    public void 信息不减少()
    {
        // 独立算式：把重排前排名栏的整行文字（"第 N 名" + CompactText + 状态文字，照旧 Hud.cs 的整行拼法）里的数字全部抽出来，每个都必须出现在重排后某一列抽出的数字里。
        // 正则写在测试里，覆盖整数、带小数点的缩写（1.23M）与指数记数（1.00e20）。
        var number = new Regex(@"\d+(?:\.\d+)?(?:e\d+|[MBT])?");
        PlayerPowerRowView[] rows =
        [
            Row(P0, 2_500_003, 13, 2_499_990, 1),   // 势力 2.50M、棋串 2.49M：两列缩写不同
            Row(P1, 26, 8, 18, 2),
            Row(P2, BigInteger.Pow(10, 20) + 7, 7, BigInteger.Pow(10, 20), 3),
            Row(P3, 9, 9, 0, 4),
            Row(P1, 40, 15, 25, null, PlayerStatus.Resigned, "已弃赛 · 不再行动"),   // 有状态的行（D9-1），领地、棋串都不为 0
        ];

        int checkedNumbers = 0;
        foreach (PlayerPowerRowView row in rows)
        {
            string before = $"第 {row.Rank} 名　{row.CompactText}{row.StatusText}";
            RankRowView view = HudPanelRows.RankRow(row, null, P0);
            HashSet<string> after = [.. Columns(view).SelectMany(c => number.Matches(c).Select(m => m.Value))];
            foreach (System.Text.RegularExpressions.Match m in number.Matches(before))
            {
                Assert.True(after.Contains(m.Value), $"{before} 里的 {m.Value} 在重排后各列里找不到：{string.Join(" | ", Columns(view))}");
                checkedNumbers++;
            }
        }

        // 样本口径下界：前四行各 名次 + 势力 + 领地 + 棋串 四个数，弃赛行没有名次、三个数。
        Assert.Equal(19, checkedNumbers);
    }

    // ---------- design D1：演出中间态的明细 ----------

    // P2 势力 20 → 26（领地 +2、军势 +4），名次 3 → 2。
    private static readonly PowerChange Up = new(P2, 20, 26, 3, 2, 2, 4);

    [Fact]
    public void 演出未开始显示旧值旧名次且明细为空()
    {
        var display = new PowerDisplay(20, Up, 0, Rolling: false, PowerStage.Pending, 0, null);

        RankRowView view = HudPanelRows.RankRow(Row(P2, 26, 8, 18, 2), display, P0);

        Assert.Equal("第 3 名", view.RankText);
        Assert.Equal(new BigInteger(20), view.PowerValue);
        Assert.Equal("20", view.PowerText);
        Assert.Equal(string.Empty, view.DetailText);
        Assert.False(view.DetailIsDelta);
    }

    [Fact]
    public void 领地段明细为本段增量与名次提示()
    {
        var display = new PowerDisplay(21, Up, 200, Rolling: true, PowerStage.Territory, 500, "领地 +2");

        RankRowView view = HudPanelRows.RankRow(Row(P2, 26, 8, 18, 2), display, P0);

        Assert.Equal("第 2 名", view.RankText);
        Assert.Equal(new BigInteger(21), view.PowerValue);
        Assert.Equal("21", view.PowerText);
        Assert.Equal("领地 +2　名次 3 → 2 ↑", view.DetailText);
        Assert.True(view.DetailIsDelta);
        Assert.False(view.DeltaIsNegative);
    }

    [Fact]
    public void 军势段明细为本段增量()
    {
        var display = new PowerDisplay(24, Up, 600, Rolling: true, PowerStage.Group, 500, "军势 +4");

        RankRowView view = HudPanelRows.RankRow(Row(P2, 26, 8, 18, 2), display, P0);

        Assert.Equal("军势 +4　名次 3 → 2 ↑", view.DetailText);
        Assert.Equal("24", view.PowerText);
        Assert.True(view.DetailIsDelta);
    }

    [Fact]
    public void 定格明细为总增量()
    {
        var display = new PowerDisplay(26, Up, 900, Rolling: true, PowerStage.Hold, 0, null);

        RankRowView view = HudPanelRows.RankRow(Row(P2, 26, 8, 18, 2), display, P0);

        Assert.Equal("+6　名次 3 → 2 ↑", view.DetailText);
        Assert.True(view.DetailIsDelta);
        Assert.False(view.DeltaIsNegative);
    }

    [Fact]
    public void 名次下降与增量正负按当前段取()
    {
        // P0 势力 30 → 27：领地 +3、军势 −6，名次 1 → 2。领地段是正增量、定格是负增量。
        var down = new PowerChange(P0, 30, 27, 1, 2, 3, -6);
        PlayerPowerRowView row = Row(P0, 27, 13, 14, 2);

        RankRowView territory = HudPanelRows.RankRow(row, new PowerDisplay(33, down, 200, true, PowerStage.Territory, 1000, "领地 +3"), P0);
        RankRowView group = HudPanelRows.RankRow(row, new PowerDisplay(27, down, 600, true, PowerStage.Group, 1000, "军势 −6"), P0);
        RankRowView hold = HudPanelRows.RankRow(row, new PowerDisplay(27, down, 900, true, PowerStage.Hold, 0, null), P0);

        Assert.Equal("领地 +3　名次 1 → 2 ↓", territory.DetailText);
        Assert.False(territory.DeltaIsNegative);
        Assert.True(group.DeltaIsNegative);
        Assert.Equal("−3　名次 1 → 2 ↓", hold.DetailText);
        Assert.True(hold.DeltaIsNegative);
        Assert.True(hold.IsViewer);

        // 演出未开始：明细为空、不是增量，即使总增量为负也不标负（DeltaIsNegative 只随 DetailIsDelta 成立）。
        RankRowView pending = HudPanelRows.RankRow(row, new PowerDisplay(30, down, 0, false, PowerStage.Pending, 0, null), P0);
        Assert.Equal(string.Empty, pending.DetailText);
        Assert.False(pending.DetailIsDelta);
        Assert.False(pending.DeltaIsNegative);
    }

    [Fact]
    public void 名次不变时不给名次提示且零增量不算负()
    {
        // P1 势力 10 → 10：领地 +3、军势 −3，名次 1 → 1。定格总增量 ±0：不标负（与旧 Hud.cs 的 shown.Sign < 0 一致），也没有名次提示。
        var flat = new PowerChange(P1, 10, 10, 1, 1, 3, -3);

        RankRowView hold = HudPanelRows.RankRow(Row(P1, 10, 4, 6, 1), new PowerDisplay(10, flat, 900, Rolling: true, PowerStage.Hold, 0, null), P0);

        Assert.Equal("±0", hold.DetailText);
        Assert.True(hold.DetailIsDelta);
        Assert.False(hold.DeltaIsNegative);
        Assert.Equal("第 1 名", hold.RankText);
    }

    [Fact]
    public void 有状态又在演出中时状态在前增量在后()
    {
        var change = new PowerChange(P3, 5, 0, null, null, -2, -3);
        var display = new PowerDisplay(0, change, 900, Rolling: true, PowerStage.Hold, 0, null);

        RankRowView view = HudPanelRows.RankRow(Row(P3, 0, 0, 0, null, PlayerStatus.Eliminated, "已出局 · 不再行动"), display, P0);

        Assert.Equal("—", view.RankText);
        Assert.Equal("已出局 · 不再行动　−5", view.DetailText);
        Assert.True(view.IsMuted);
        Assert.True(view.DetailIsDelta);
        Assert.True(view.DeltaIsNegative);
    }

    [Fact]
    public void 自家标记宽度()
    {
        Assert.Equal(3, UiTheme.ViewerMarkWidthPx);
    }

    // ---------- design D9-5 / D9-6（段 B） ----------

    [Fact]
    public void 明细拆出状态部分()
    {
        // D9-6：状态部分单列（引擎层恒用次要色），其余部分按是否增量取色；DetailText 仍是二者用全角空格连接的结果。
        var change = new PowerChange(P3, 5, 0, null, null, -2, -3);
        PlayerPowerRowView eliminated = Row(P3, 0, 0, 0, null, PlayerStatus.Eliminated, "已出局 · 不再行动");

        RankRowView still = HudPanelRows.RankRow(eliminated, null, P0);
        Assert.Equal("已出局 · 不再行动", still.DetailStatusText);
        Assert.Equal("领地 0 + 棋串 0", still.DetailRestText);
        Assert.False(still.DetailIsDelta);

        RankRowView hold = HudPanelRows.RankRow(eliminated, new PowerDisplay(0, change, 900, Rolling: true, PowerStage.Hold, 0, null), P0);
        Assert.Equal("已出局 · 不再行动", hold.DetailStatusText);
        Assert.Equal("−5", hold.DetailRestText);
        Assert.True(hold.DetailIsDelta);

        // 演出未开始：其余部分为空，明细只剩状态。
        RankRowView pending = HudPanelRows.RankRow(eliminated, new PowerDisplay(5, change, 0, Rolling: false, PowerStage.Pending, 0, null), P0);
        Assert.Equal("已出局 · 不再行动", pending.DetailStatusText);
        Assert.Equal(string.Empty, pending.DetailRestText);
        Assert.Equal("已出局 · 不再行动", pending.DetailText);

        // 没有状态：状态部分为 null，其余部分就是整个明细。
        RankRowView plain = HudPanelRows.RankRow(Row(P2, 26, 8, 18, 2), null, P0);
        Assert.Null(plain.DetailStatusText);
        Assert.Equal("领地 8 + 棋串 18", plain.DetailRestText);

        Assert.Null(still.DetailRankHintText);
        Assert.Null(hold.DetailRankHintText);   // 没有名次的玩家不提示
        Assert.Null(plain.DetailRankHintText);

        // 名次提示单列（不随段首放大）：增量部分不含它。
        RankRowView territory = HudPanelRows.RankRow(Row(P2, 26, 8, 18, 2), new PowerDisplay(21, Up, 200, Rolling: true, PowerStage.Territory, 500, "领地 +2"), P0);
        Assert.Null(territory.DetailStatusText);
        Assert.Equal("领地 +2", territory.DetailRestText);
        Assert.Equal("名次 3 → 2 ↑", territory.DetailRankHintText);

        // 三段齐全：状态、总增量、名次提示。
        RankRowView all = HudPanelRows.RankRow(Row(P2, 26, 8, 18, 2, PlayerStatus.Resigned, "已弃赛 · 不再行动"), new PowerDisplay(26, Up, 900, Rolling: true, PowerStage.Hold, 0, null), P0);
        Assert.Equal("已弃赛 · 不再行动", all.DetailStatusText);
        Assert.Equal("+6", all.DetailRestText);
        Assert.Equal("名次 3 → 2 ↑", all.DetailRankHintText);
        Assert.Equal("已弃赛 · 不再行动　+6　名次 3 → 2 ↑", all.DetailText);

        // 三段与整段明细一致：测试内独立拼接（状态、增量、名次提示里非空者依次用全角空格连接）。
        foreach (RankRowView v in new[] { still, hold, pending, plain, territory, all })
        {
            var segments = new List<string>();
            foreach (string? s in new[] { v.DetailStatusText, v.DetailRestText, v.DetailRankHintText })
            {
                if (!string.IsNullOrEmpty(s))
                {
                    segments.Add(s);
                }
            }

            Assert.Equal(string.Join("　", segments), v.DetailText);
        }
    }

    [Fact]
    public void 顺序条保留标题()
    {
        // D9-5：重排前按钮文字以"行动顺序："开头，重排后标题保留，引擎层放在条目之前（摆放顺序由下面的源码守门钉住）。
        Assert.Equal("行动顺序", HudPanelRows.OrderTitleText);
        string order = PresentationFixtures.MethodBody(PresentationFixtures.GodotScriptCode("Hud.cs"), "private void RefreshOrderBar(ViewerWorld world)");
        int title = order.IndexOf("HudPanelRows.OrderTitleText", StringComparison.Ordinal);
        int entries = order.IndexOf("bar.Entries", StringComparison.Ordinal);
        Assert.True(title >= 0, "RefreshOrderBar 没有放顺序条标题");
        Assert.True(entries > title, "顺序条标题应放在条目之前");
    }

    // ---------- design D6 第二条：引擎层源码守门（src/godot 不在 sln 里，只能读源码） ----------

    /// <summary>
    /// <c>RefreshRank</c> / <c>RefreshOrderBar</c> / <c>RefreshHand</c> 只摆放：文字整份取自 <see cref="HudPanelRows"/>，不再拼 <c>CompactText</c>、
    /// 整行旧文字 <c>row.Text</c>、<c>"▶ "</c>、<c>"◀"</c>，也不自己取阵营名、写控件文字或拼名次 / 拆分 / 数量。
    /// 变异记录见 <see cref="信息面板分列Tests"/> 的类注释（段 B）。
    /// </summary>
    [Fact]
    public void 引擎层只摆放不拼文字()
    {
        string hud = PresentationFixtures.GodotScriptCode("Hud.cs");
        string rank = PresentationFixtures.MethodBody(hud, "public void RefreshRank(ViewerWorld world, ShowMask mask)");
        string order = PresentationFixtures.MethodBody(hud, "private void RefreshOrderBar(ViewerWorld world)");
        string hand = PresentationFixtures.MethodBody(hud, "private void RefreshHand(MatchSession session, ViewerWorld world)");

        // 样本口径下界：三个方法体都扫到了实质内容（防止签名改了、方法体被掏空还绿）。
        Assert.True(hud.Length >= 30_000, $"Hud.cs 只有 {hud.Length} 字符");
        Assert.True(rank.Length >= 1_500, $"RefreshRank 只有 {rank.Length} 字符");
        Assert.True(order.Length >= 800, $"RefreshOrderBar 只有 {order.Length} 字符");
        Assert.True(hand.Length >= 800, $"RefreshHand 只有 {hand.Length} 字符");

        // 三者都经 HudPanelRows。
        Assert.Matches(@"HudPanelRows\.RankRow\(", rank);
        Assert.Matches(@"HudPanelRows\.OrderBar\(", order);
        Assert.Matches(@"HudPanelRows\.HandRow\(", hand);

        // 旧写法整份 Hud.cs 都不得出现（挪进私有辅助方法也算）：概览栏整行 CompactText、▶ 选中前缀、◀ 当前行动者符号。
        var legacy = new Regex(@"CompactText|▶|◀");
        Assert.Empty(legacy.Matches(hud).Select(m => m.Value));

        // 三个方法体里不读整行旧文字 / 不直接写控件文字（除工厂 Ui.Text 外的任何 .Text）、不取阵营名、不 string.Join。
        var compose = new Regex(@"(?<!Ui)\.Text(?!\w)|\.Name(?!\w)|string\.Join\(");
        foreach ((string name, string body) in new[] { ("RefreshRank", rank), ("RefreshOrderBar", order), ("RefreshHand", hand) })
        {
            Assert.True(!compose.IsMatch(body), $"{name} 自己拼文字：{string.Join("、", compose.Matches(body).Select(m => m.Value))}");
        }

        // 各面板特有的文字片段不得出现在引擎层：名次 / 拆分 / 名次箭头；顺序未定与快捷键提示；弃牌前缀与数量。
        Assert.DoesNotMatch(@"\$""|""第|领地|棋串|↑|↓", rank);
        Assert.DoesNotMatch(@"\$""|未定|顺序层|"" > """, order);
        Assert.DoesNotMatch(@"弃掉整类|×|（新", hand);

        // D2 / D9-6：六列网格、本机标记取自 IsViewer 并用主题宽度；明细分状态与其余两段取色，不整段用 DetailText。
        Assert.Matches(@"new GridContainer \{ Columns = 6", rank);
        Assert.Matches(@"view\.IsViewer", rank);
        Assert.Matches(@"UiTheme\.ViewerMarkWidthPx", rank);
        Assert.Matches(@"view\.DetailStatusText", rank);
        Assert.Matches(@"view\.DetailRestText", rank);
        Assert.DoesNotMatch(@"\.DetailText(?!\w)", rank);
        // 演出中领地段 / 军势段，明细其余部分（本段增量）与势力列同字号（主会话裁决：保留 tiered-number-show 的"段首数字与本段增量一起弹"）；状态部分恒为注释字号。
        Assert.Matches(@"int restSize = display is \{ Stage: PowerStage\.Territory or PowerStage\.Group \} \? RankFontPx\(display\) : UiTheme\.Caption;", rank);
        Assert.Matches(@"Ui\.Text\(view\.DetailRestText, restColor, restSize\)", rank);
        Assert.Matches(@"Ui\.Text\(status, Ui\.MutedText, UiTheme\.Caption\)", rank);
        // 名次提示单独一个标签、恒为正文字号（不随段首放大），颜色与增量相同。
        Assert.Matches(@"view\.DetailRankHintText is \{ \} rankHint", rank);
        Assert.Matches(@"Ui\.Text\(rankHint, restColor, UiTheme\.Body\)", rank);
        // D3：当前行动者取自 IsCurrent。D4：语义取自 HandRow、按钮里铺两列、子 Label 字色经工厂按语义取。
        Assert.Matches(@"entry\.IsCurrent", order);
        Assert.Matches(@"view\.Kind", hand);
        Assert.Matches(@"Ui\.FillButton\(", hand);
        Assert.Matches(@"Ui\.ButtonTextColor\(view\.Kind", hand);

        // 反面命中：判据对改前的写法确实命中，对工厂 Ui.Text 不命中（正则写错时上面恒真）。
        Assert.Matches(legacy, "_orderButton.Text = $\"行动顺序：{string.Join(\" > \", names)}◀\";");
        Assert.Matches(compose, "button.Text = $\"弃掉整类：{row.Text}\";");
        Assert.Matches(compose, "Ui.Text($\"{rank}　{faction.Name}\")");
        Assert.DoesNotMatch(compose, "grid.AddChild(Ui.Text(view.NameText, textColor));");
    }

    /// <summary>
    /// 上一条按"不得出现的片段"扫，挡不住把拼法挪出方法体：私有辅助方法、另起一个 <c>Hud.*.cs</c> partial 文件、
    /// <c>+</c> / <c>string.Concat</c> 拼常量、换用 <c>Labels.*</c> 取名字（段 B 检查方 B1–B4 / B7 / B8 实测 0 红）。
    /// 这里改成白名单：三个方法体里交给 <c>Ui.Text</c> / <c>Ui.Toggle</c> / <c>Ui.Action</c> / <c>Ui.Heading</c> 的文字实参只能是呈现层视图字段
    /// （或由它们解构出的局部量、顺序条的分隔符 <c>›</c>、空串），且不得用 <c>Text =</c> 直接给控件写字。
    /// 另钉住取色：手牌子 <c>Label</c> 只用按语义取的 <c>color</c>（B5），当前行动者金字 + 金色底边（B9 / B10），本机竖条用主题宽度（B6）。
    /// 旧写法（<c>CompactText</c> / ▶ / ◀）扫全部 <c>Hud*.cs</c>，新起的 partial 文件同样在内。
    /// 变异（检查方脚本，二进制读写、锚点恰命中 1 次、finally 还原后 SHA-256 一致并刷新 mtime）：B1–B10 补测试前全部红 0，补后各红 1（本条）。
    /// </summary>
    [Fact]
    public void 引擎层面板文字只取自呈现层()
    {
        string hud = PresentationFixtures.GodotScriptCode("Hud.cs");
        string rank = PresentationFixtures.MethodBody(hud, "public void RefreshRank(ViewerWorld world, ShowMask mask)");
        string order = PresentationFixtures.MethodBody(hud, "private void RefreshOrderBar(ViewerWorld world)");
        string handBody = PresentationFixtures.MethodBody(hud, "private void RefreshHand(MatchSession session, ViewerWorld world)");
        int loop = handBody.IndexOf("foreach (OwnHandRowView row", StringComparison.Ordinal);
        Assert.True(loop > 0, "RefreshHand 里找不到逐类循环");
        string hand = handBody[loop..];   // 循环之前是类型槽摘要与"手牌为空"，不在本 change 范围

        string[] rankArgs = TextArguments(rank);
        string[] orderArgs = TextArguments(order);
        string[] handArgs = TextArguments(hand);
        // 样本口径下界：每处都确实扫到了实参（防止工厂改名后扫了个空还绿）。
        Assert.True(rankArgs.Length >= 6, $"RefreshRank 只扫到 {rankArgs.Length} 处文字");
        Assert.True(orderArgs.Length >= 5, $"RefreshOrderBar 只扫到 {orderArgs.Length} 处文字");
        Assert.True(handArgs.Length >= 4, $"RefreshHand 只扫到 {handArgs.Length} 处文字");

        // "尚未产生势力快照"是排名为空时的整面板占位（重排前就有，不属于按列文字）。
        AssertOnly("RefreshRank", rankArgs, ["\"尚未产生势力快照\"", "view.RankText", "view.NameText", "view.PowerText", "status", "view.DetailRestText", "rankHint"]);
        AssertOnly("RefreshOrderBar", orderArgs, ["HudPanelRows.OrderTitleText", "empty", "\"›\"", "entry.NameText", "bar.HintText"]);
        AssertOnly("RefreshHand", handArgs, ["view.NameText", "view.CountText", "string.Empty"]);

        // 解构出的局部量确实来自视图字段。
        Assert.Matches(@"view\.DetailStatusText is \{ \} status\b", rank);
        Assert.Matches(@"view\.DetailRankHintText is \{ \} rankHint\b", rank);
        Assert.Matches(@"bar\.EmptyText is \{ \} empty\b", order);

        // 不绕过工厂直接给控件写字（对象初始化器或赋值）。
        var directText = new Regex(@"(?<![\w.])Text\s*=(?!=)");
        foreach ((string name, string body) in new[] { ("RefreshRank", rank), ("RefreshOrderBar", order), ("RefreshHand", hand) })
        {
            Assert.False(directText.IsMatch(body), $"{name} 直接写控件文字");
        }

        // 取色：手牌子 Label 一律用按语义取的 color；当前行动者金字 + 金色底边；本机竖条用主题宽度。
        Assert.Matches(@"Color color = Ui\.ButtonTextColor\(view\.Kind, selected\);", hand);
        Assert.Equal(2, Regex.Matches(hand, @"Ui\.Text\(view\.\w+, color, UiTheme\.Small\)").Count);
        Assert.Matches(@"Ui\.Text\(entry\.NameText, entry\.IsCurrent \? Ui\.PanelBorder : Ui\.InfoText,", order);
        Assert.Matches(@"entry\.IsCurrent \? Ui\.Bar\(Ui\.PanelBorder, 0f, UiTheme\.SelectedEdgeWidthPx\) : Ui\.Spacer\(0f, UiTheme\.SelectedEdgeWidthPx\)", order);
        Assert.Matches(@"view\.IsViewer\s*\?\s*Ui\.Bar\(Ui\.PanelBorder, UiTheme\.ViewerMarkWidthPx,", rank);

        // 旧写法扫全部 Hud*.cs（含新起的 partial 文件）。
        Dictionary<string, string> scripts = HudScriptScan.Read(HudScriptScan.HudScripts, 50_000);
        Assert.Empty(HudScriptScan.Hits(scripts, new Regex(@"CompactText|▶|◀")));

        // 反面命中：白名单之外的实参确实被扫出来（正则写错时上面恒真）。
        Assert.Equal(["RankOf(row)", "string.Concat(\"第 \", r, \" 名\")", "Labels.Player(entry.Player)"],
            TextArguments("Ui.Text(RankOf(row), textColor); Ui.Text(string.Concat(\"第 \", r, \" 名\"), c); Ui.Toggle(Labels.Player(entry.Player), selected)"));
        Assert.Matches(directText, "new Label { Text = row.Name }");
        Assert.DoesNotMatch(directText, "_orderButton.Text = x;");   // 成员访问由上一条的 compose 规则管

        static void AssertOnly(string name, string[] args, string[] allowed)
        {
            string[] bad = [.. args.Where(a => !allowed.Contains(a))];
            Assert.True(bad.Length == 0, $"{name} 的文字不是呈现层给的：{string.Join("、", bad)}");
        }
    }

    /// <summary>取 <c>Ui.Text(</c> / <c>Ui.Toggle(</c> / <c>Ui.Action(</c> / <c>Ui.Heading(</c> 的第一个实参（按括号与引号配平到顶层逗号或右括号）。</summary>
    private static string[] TextArguments(string code)
    {
        var list = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in Regex.Matches(code,@"Ui\.(?:Text|Toggle|Action|Heading)\("))
        {
            int depth = 0;
            bool inString = false;
            int i = m.Index + m.Length;
            int start = i;
            for (; i < code.Length; i++)
            {
                char c = code[i];
                if (inString)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c is '(' or '[' or '{')
                {
                    depth++;
                }
                else if (c is ')' or ']' or '}')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    break;
                }
            }

            list.Add(code[start..i].Trim());
        }

        return [.. list];
    }
}
