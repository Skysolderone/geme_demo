using System.Text.RegularExpressions;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 按钮状态与语义</summary>
/// <remarks>
/// 只覆盖能落到取值上的五条 Scenario；「语义不靠逐处改色」要读 HUD 脚本，归 hud-theme 段 B 的源码扫描守门。
/// 另有三条不对应 Scenario 的补充：Requirement 正文里的"主操作用金色、危险操作用警示色"与"键盘焦点"，以及 design D1 的取值表。
/// 期望值（色值、线宽）与阈值（12 / 160 / 70 / 40）都在测试里独立写，不取被测常量来比。
/// 变异验证（hud-theme 段 A；脚本与口径同 <see cref="面板分级Tests"/>，共 40 条；红数后列红的测试，本类的略去类名）：
/// 悬停与按下——M-B1「悬停底色 (46, 50, 62)，只亮 9」→ 红 2（悬停与按下、按钮取值表）；M-B2「按下底色 (32, 36, 44)，只暗 6」→ 红 2（同上）；
/// M-B3「主操作的悬停底色另取」→ 红 1（悬停与按下 的"四个变体同底色"）。
/// 常态与悬停下读得清——M-B4「默认文字改禁用文字色」→ 红 5（Default 行 + 主操作用金色…、按钮取值表、禁用仍可读、选中不只靠颜色）；
/// M-B6「主操作文字改中性色，亮度 78」→ 红 4（Primary 行 + 主操作用金色…、按钮取值表、禁用仍可读）；
/// M-B5「悬停底色 (62, 68, 82)，亮度 67」→ 红 2（Danger 行、按钮取值表；默认 164 仍够）；
/// M-B9「悬停底色 (66, 72, 88)，亮度 72」→ 红 3（Default 行只在悬停不足、Danger 行、按钮取值表）；M-B36「选中文字改 (100, 100, 100)」→ 红 3（选中行、禁用仍可读、选中不只靠颜色）。
/// 禁用仍可读——M-B10「非默认语义禁用时文字仍取语义色」→ 红 2（禁用仍可读 的"文字更暗"、禁用盖过语义）；M-B8「禁用边框 (90, 94, 104)」→ 红 3（禁用仍可读 的"边框更暗"、禁用盖过语义、选中不只靠颜色）；
/// M-B7「禁用文字 (70, 70, 70)，对禁用底 38」→ 红 4（禁用仍可读 的亮度差、禁用盖过语义、选中不只靠颜色、按钮取值表）。
/// 禁用盖过语义——样本口径五条 NotEqual：M-B37「主操作文字用信息色」→ 红 3、M-B38「危险文字用信息色」→ 红 3、M-B21「危险操作改金色」→ 红 3（各含 禁用盖过语义、主操作用金色…）、
/// M-B20「主操作边框用中性色」→ 红 2、M-B39「危险边框用中性色」→ 红 2；M-B34「禁用底色 alpha 200」→ 红 3（禁用底色字面量 + 按钮取值表、选中不只靠颜色）；M-B27「禁用不填充」→ 红 2（DrawFill + 焦点对照）；
/// 循环三条：M-B10 文字、M-B11「非默认语义禁用时边框仍取语义色」→ 红 2、M-B12「危险禁用时底色取常态」→ 红 1；整条记录相等：M-B41「危险禁用时不填充」→ 红 2（禁用盖过语义 的记录相等、焦点对照）。
/// 选中不只靠颜色——M-B13「底边宽 2 改 1」、M-B14「选中四边都 2」、M-B18「禁用的选中丢掉宽底边」、M-B15「选中文字不变金」、M-B16「底边不是金色」、M-B42「选中三边也金色」、M-B19「守卫恒假」各 → 红 1；
/// M-B17「未选中底边也 2」→ 红 2（+ 主操作用金色…）；M-B43「未选中底边金色 1 像素」→ 红 2（off.Bottom + 主操作用金色…）；M-B44「守卫只挡主操作」→ 红 1（Danger 的 Throws）；
/// 禁用的选中：M-B7 文字、M-B34 底色、M-B8 四边（见上）。
/// 主操作用金色危险操作用警示色——M-B6 主操作文字、M-B20 主操作边框、M-B40「危险文字用金色」→ 红 3、M-B39 危险边框、M-B4 默认文字、M-B35「默认边框 (80, 84, 96)」→ 红 2（+ 选中不只靠颜色）。
/// 键盘焦点只画一圈金边——M-B22「焦点也填充」、M-B23「焦点圈随语义取色」、M-B24「焦点文字固定信息色」、M-B26「悬停不填充」各 → 红 1；M-B25「占位色 alpha 255」→ 红 2（+ 面板 提示条没有边框）。
/// 按钮取值表——M-B33「常态底色 (40, 44, 54)」、M-B28「内边距 12」、M-B30「高度 32」、M-B31「未知状态回落常态」、M-B32「未知语义回落默认」、M-B45「ButtonKind 增第四种」、M-B46「ButtonState 增第六种」、
/// M-B47「未知语义只在禁用态回落」各 → 红 1；M-B29「圆角 3 改 4」→ 红 3（+ 面板两条）；M-X1「Rgba.Luma 改三通道平均」→ 红 2（亮度列 + 面板取值表）。
/// 只改测试：M-T2「悬停期望底色换成按下的」→ 红 1；M-T9「亮度期望对调 148 / 136」→ 红 1；M-T5「禁用边框 / 禁用文字期望对调」→ 红 2；M-T6「金色 / 警示色期望对调」→ 红 3。
/// </remarks>
public class 按钮状态与语义Tests
{
    // design D1 的色值，测试内独立写一份。
    private static readonly Rgba Gold = new(184, 146, 72, 255);
    private static readonly Rgba Warning = new(236, 96, 80, 255);
    private static readonly Rgba Info = new(236, 232, 220, 255);
    private static readonly Rgba Neutral = new(74, 78, 90, 255);
    private static readonly Rgba DisabledBorder = new(52, 54, 60, 255);
    private static readonly Rgba DisabledText = new(132, 130, 124, 255);
    private static readonly Rgba DisabledFill = new(30, 32, 38, 150);

    /// <summary>会填充底色的三种状态（禁用另有一组取值，焦点不填充）。</summary>
    private static readonly ButtonState[] Live = [ButtonState.Normal, ButtonState.Hover, ButtonState.Pressed];

    /// <summary>引擎层要下发的四个按钮变体：三种语义 + 选中。</summary>
    private static readonly (ButtonKind Kind, bool Selected)[] Variants =
    [
        (ButtonKind.Default, false), (ButtonKind.Primary, false), (ButtonKind.Danger, false), (ButtonKind.Default, true),
    ];

    private static UiButtonStyle Of(ButtonKind kind, ButtonState state, bool selected = false) => UiTheme.ButtonStyleOf(kind, state, selected);

    private static UiEdge[] Edges(UiButtonStyle style) => [style.Left, style.Top, style.Right, style.Bottom];

    private static int[] Widths(UiButtonStyle style) => [.. Edges(style).Select(e => e.WidthPx)];

    [Fact]
    public void 悬停与按下()
    {
        // 规格：悬停的底色比常态亮不少于 12，按下的底色比常态暗不少于 12。
        int normal = Of(ButtonKind.Default, ButtonState.Normal).Fill.Luma;
        int hover = Of(ButtonKind.Default, ButtonState.Hover).Fill.Luma;
        int pressed = Of(ButtonKind.Default, ButtonState.Pressed).Fill.Luma;
        Assert.True(hover - normal >= 12, $"悬停 {hover} 对常态 {normal}");
        Assert.True(normal - pressed >= 12, $"按下 {pressed} 对常态 {normal}");

        // design D1：主操作、危险操作、选中的底色"同默认各态"——悬停与按下的反馈对四个变体都成立。
        foreach ((ButtonKind kind, bool selected) in Variants)
        {
            foreach (ButtonState state in Live)
            {
                Assert.Equal(Of(ButtonKind.Default, state).Fill, Of(kind, state, selected).Fill);
            }
        }
    }

    [Theory]
    [InlineData(ButtonKind.Default, false, 160)]
    [InlineData(ButtonKind.Primary, false, 70)]
    [InlineData(ButtonKind.Danger, false, 70)]
    [InlineData(ButtonKind.Default, true, 70)] // 选中：金色文字，按主操作的口径
    public void 常态与悬停下读得清(ButtonKind kind, bool selected, int threshold)
    {
        // 规格：常态与悬停下，默认按钮的文字与底色亮度差不少于 160，主操作与危险操作按钮的不少于 70。
        foreach (ButtonState state in new[] { ButtonState.Normal, ButtonState.Hover })
        {
            UiButtonStyle style = Of(kind, state, selected);
            int contrast = style.Text.Luma - style.Fill.Luma;
            Assert.True(contrast >= threshold, $"{kind}（选中 {selected}）{state}：文字 {style.Text.Luma} − 底色 {style.Fill.Luma} = {contrast}");
        }
    }

    [Fact]
    public void 禁用仍可读()
    {
        // 规格：禁用时文字与边框都比常态暗，且禁用文字与禁用底色的亮度差不少于 40。四个变体逐个对自己的常态比。
        foreach ((ButtonKind kind, bool selected) in Variants)
        {
            UiButtonStyle normal = Of(kind, ButtonState.Normal, selected);
            UiButtonStyle disabled = Of(kind, ButtonState.Disabled, selected);
            Assert.True(disabled.Text.Luma < normal.Text.Luma, $"{kind}（选中 {selected}）文字：禁用 {disabled.Text.Luma} 对常态 {normal.Text.Luma}");
            UiEdge[] normalEdges = Edges(normal), disabledEdges = Edges(disabled);
            for (int i = 0; i < normalEdges.Length; i++)
            {
                Assert.True(
                    disabledEdges[i].Color.Luma < normalEdges[i].Color.Luma,
                    $"{kind}（选中 {selected}）第 {i} 条边：禁用 {disabledEdges[i].Color.Luma} 对常态 {normalEdges[i].Color.Luma}");
            }

            int contrast = disabled.Text.Luma - disabled.Fill.Luma;
            Assert.True(contrast >= 40, $"{kind}（选中 {selected}）禁用文字 {disabled.Text.Luma} − 禁用底色 {disabled.Fill.Luma} = {contrast}");
        }
    }

    [Fact]
    public void 禁用盖过语义()
    {
        // 样本口径：常态下三种语义确实两两不同（否则"禁用后相同"说明不了任何事）。
        UiButtonStyle normalDefault = Of(ButtonKind.Default, ButtonState.Normal);
        UiButtonStyle normalPrimary = Of(ButtonKind.Primary, ButtonState.Normal);
        UiButtonStyle normalDanger = Of(ButtonKind.Danger, ButtonState.Normal);
        Assert.NotEqual(normalDefault.Text, normalPrimary.Text);
        Assert.NotEqual(normalDefault.Text, normalDanger.Text);
        Assert.NotEqual(normalPrimary.Text, normalDanger.Text);
        Assert.NotEqual(normalDefault.Left.Color, normalPrimary.Left.Color);
        Assert.NotEqual(normalDefault.Left.Color, normalDanger.Left.Color);

        // 禁用的默认按钮：design D1"禁用"一行（底色 (30, 32, 38, 150)、边框 (52, 54, 60) 宽 1、文字 (132, 130, 124)）。
        UiButtonStyle disabledDefault = Of(ButtonKind.Default, ButtonState.Disabled);
        Assert.Equal(DisabledFill, disabledDefault.Fill);
        Assert.True(disabledDefault.DrawFill);
        Assert.Equal(DisabledText, disabledDefault.Text);
        Assert.All(Edges(disabledDefault), edge => Assert.Equal(new UiEdge(DisabledBorder, 1), edge));

        // 规格：禁用的主操作按钮与禁用的危险操作按钮，文字、边框、底色与禁用的默认按钮相同。
        foreach (ButtonKind kind in new[] { ButtonKind.Primary, ButtonKind.Danger })
        {
            UiButtonStyle disabled = Of(kind, ButtonState.Disabled);
            Assert.Equal(disabledDefault.Text, disabled.Text);
            Assert.Equal(Edges(disabledDefault), Edges(disabled));
            Assert.Equal(disabledDefault.Fill, disabled.Fill);
            Assert.Equal(disabledDefault, disabled);
        }
    }

    [Fact]
    public void 选中不只靠颜色()
    {
        // 规格：选中的按钮底边宽度为 2、其余三边为 1，未选中的四边都是 1。宽度是形状提示，转成灰度也在；
        // 禁用时颜色全部变灰，宽底边仍在（见下），所以四种会画底色的状态都查。
        foreach (ButtonState state in new[] { ButtonState.Normal, ButtonState.Hover, ButtonState.Pressed, ButtonState.Disabled })
        {
            Assert.Equal(new[] { 1, 1, 1, 2 }, Widths(Of(ButtonKind.Default, state, selected: true)));
            Assert.Equal(new[] { 1, 1, 1, 1 }, Widths(Of(ButtonKind.Default, state)));
        }

        // 规格：选中同时有两种提示——金色文字，以及一条比其余三边更宽的金色底边；其余三边同默认按钮（design D1）。
        foreach (ButtonState state in Live)
        {
            UiButtonStyle on = Of(ButtonKind.Default, state, selected: true);
            UiButtonStyle off = Of(ButtonKind.Default, state);
            Assert.Equal(Gold, on.Text);
            Assert.Equal(new UiEdge(Gold, 2), on.Bottom);
            Assert.Equal(new[] { new UiEdge(Neutral, 1), new UiEdge(Neutral, 1), new UiEdge(Neutral, 1) }, new[] { on.Left, on.Top, on.Right });
            Assert.Equal(Info, off.Text);
            Assert.Equal(new UiEdge(Neutral, 1), off.Bottom);
        }

        // 禁用的选中按钮：颜色一律取禁用那一组（不再是金色），只留宽底边。
        UiButtonStyle disabledOn = Of(ButtonKind.Default, ButtonState.Disabled, selected: true);
        Assert.Equal(DisabledText, disabledOn.Text);
        Assert.Equal(DisabledFill, disabledOn.Fill);
        Assert.Equal(
            new[] { new UiEdge(DisabledBorder, 1), new UiEdge(DisabledBorder, 1), new UiEdge(DisabledBorder, 1), new UiEdge(DisabledBorder, 2) },
            Edges(disabledOn));

        // 选中只对默认语义的可切换按钮有定义：主操作本来就是金色文字，再"选中"就只剩一种提示了——响亮失败。
        Assert.Throws<ArgumentException>(() => Of(ButtonKind.Primary, ButtonState.Normal, selected: true));
        Assert.Throws<ArgumentException>(() => Of(ButtonKind.Danger, ButtonState.Normal, selected: true));
    }

    [Fact]
    public void 主操作用金色危险操作用警示色()
    {
        // Requirement 正文：主操作用金色的边框与文字，危险操作用警示色的边框与文字；默认按钮是信息色文字、中性色边框（design D1）。
        foreach (ButtonState state in Live)
        {
            UiButtonStyle primary = Of(ButtonKind.Primary, state);
            Assert.Equal(Gold, primary.Text);
            Assert.All(Edges(primary), edge => Assert.Equal(new UiEdge(Gold, 1), edge));

            UiButtonStyle danger = Of(ButtonKind.Danger, state);
            Assert.Equal(Warning, danger.Text);
            Assert.All(Edges(danger), edge => Assert.Equal(new UiEdge(Warning, 1), edge));

            UiButtonStyle plain = Of(ButtonKind.Default, state);
            Assert.Equal(Info, plain.Text);
            Assert.All(Edges(plain), edge => Assert.Equal(new UiEdge(Neutral, 1), edge));
        }
    }

    [Fact]
    public void 键盘焦点只画一圈金边()
    {
        // design D1：焦点不填充，只画一圈 1 像素的金色；它叠画在当前状态之上，文字色跟该按钮的常态。
        foreach ((ButtonKind kind, bool selected) in Variants)
        {
            UiButtonStyle focus = Of(kind, ButtonState.Focus, selected);
            Assert.False(focus.DrawFill, $"{kind}（选中 {selected}）焦点不应填充");
            Assert.Equal(0, focus.Fill.A);
            Assert.All(Edges(focus), edge => Assert.Equal(new UiEdge(Gold, 1), edge));
            Assert.Equal(Of(kind, ButtonState.Normal, selected).Text, focus.Text);

            // 对照：其余四种状态都填充（"不填充"不是所有状态共有的缺省值）。
            foreach (ButtonState state in new[] { ButtonState.Normal, ButtonState.Hover, ButtonState.Pressed, ButtonState.Disabled })
            {
                Assert.True(Of(kind, state, selected).DrawFill, $"{kind}（选中 {selected}）{state} 应填充");
            }
        }
    }

    [Fact]
    public void 按钮取值表()
    {
        // hud-theme design D1 按钮表的初值，逐格钉住（负责人调参时这里跟着改；规格阈值由上面几条守）。
        // 常态 (38, 42, 52, 236) 亮度 41；悬停 (56, 61, 75, 240) 亮度 61；按下 (24, 27, 34, 240) 亮度 26；禁用 (30, 32, 38, 150) 亮度 32。
        (ButtonState State, Rgba Fill)[] fills =
        [
            (ButtonState.Normal, new Rgba(38, 42, 52, 236)),
            (ButtonState.Hover, new Rgba(56, 61, 75, 240)),
            (ButtonState.Pressed, new Rgba(24, 27, 34, 240)),
            (ButtonState.Disabled, new Rgba(30, 32, 38, 150)),
        ];
        foreach ((ButtonState state, Rgba fill) in fills)
        {
            Assert.Equal(fill, Of(ButtonKind.Default, state).Fill);
        }

        // D1 表里的亮度（整数亮度算式下的读数）：四种底色 41 / 61 / 26 / 32；文字——信息色 231、金色 148、警示色 136、禁用 129。
        int[] lumas =
        [
            .. fills.Select(f => Of(ButtonKind.Default, f.State).Fill.Luma),
            Of(ButtonKind.Default, ButtonState.Normal).Text.Luma,
            Of(ButtonKind.Primary, ButtonState.Normal).Text.Luma,
            Of(ButtonKind.Danger, ButtonState.Normal).Text.Luma,
            Of(ButtonKind.Default, ButtonState.Disabled).Text.Luma,
        ];
        Assert.Equal(new[] { 41, 61, 26, 32, 231, 148, 136, 129 }, lumas);

        // 圆角 3、左右内边距 10：每个变体的每个状态都一样。规格：恰好三种语义、五种状态。
        Assert.Equal(3, Enum.GetValues<ButtonKind>().Length);
        Assert.Equal(5, Enum.GetValues<ButtonState>().Length);
        foreach ((ButtonKind kind, bool selected) in Variants)
        {
            foreach (ButtonState state in Enum.GetValues<ButtonState>())
            {
                UiButtonStyle style = Of(kind, state, selected);
                Assert.Equal(3, style.CornerRadiusPx);
                Assert.Equal(10, style.PaddingXPx);
            }
        }

        // 规格：按钮高度不变（30）。"仍低于手游式按钮的下限"由 UI风格约束Tests.信息密度 守着，这里不重复。
        int height = UiTheme.ButtonHeightPx;
        Assert.Equal(30, height);

        // 未知语义、未知状态响亮失败，不回落到默认按钮。
        Assert.Throws<ArgumentOutOfRangeException>(() => Of((ButtonKind)99, ButtonState.Normal));
        Assert.Throws<ArgumentOutOfRangeException>(() => Of((ButtonKind)99, ButtonState.Disabled));
        Assert.Throws<ArgumentOutOfRangeException>(() => Of(ButtonKind.Default, (ButtonState)99));
    }

    /// <summary>
    /// 规格「语义不靠逐处改色」：HUD 脚本里没有对按钮设置样式盒覆盖或字号覆盖的调用，主操作、危险操作、选中都经由控件工厂的参数（hud-theme D5 / D7 第二组）。
    /// 回合摘要的阵营色字（<c>SetTurnSummary</c>）是内容不是主题（D5），整个方法体从扫描文本中剔除；其余位置一律不得逐处改字色。
    /// 变异验证（hud-theme 段 B，往脚本里注入违例；口径同 <see cref="面板分级Tests.每个面板都有级别"/>）：B-1「开始按钮补回 AddThemeColorOverride("font_color", …)」、
    /// B-3「演出横幅透明度写回 new Color(1f, 1f, 1f, …)」、B-4「不带入开始去掉 ButtonKind.Primary」、B-6「选图难度按钮改回逐处改字色、去掉 Ui.Select」各 → 红 1（本条）；
    /// B-2「弃赛按钮加 AddThemeStyleboxOverride(」→ 红 2（+ 面板 每个面板都有级别）；B-5「总览页样例加 AddThemeFontSizeOverride(」→ 红 2（+ 样式总览页 与对局界面同源）；
    /// G-6「总览页写 new Color(0.5f, …)」→ 红 2（+ 样式总览页 与对局界面同源）。
    /// 段 B 检查补的绕过形状（补守门前 0 红，补后）：X-3「new Color("#ff3030")」、X-4「Color warn = new(0.9f, 0.2f, 0.2f)」、X-11「关闭按钮 SelfModulate = Ui.PanelBorder」各 → 红 1（本条）；
    /// 补之前就会红的：X-10「Color.FromHtml(…)」→ 红 1（本条）；X-8「按钮直接写 ThemeTypeVariation 指选中变体」→ 红 2（+ 面板 每个面板都有级别）。
    /// </summary>
    [Fact]
    public void 语义不靠逐处改色()
    {
        Dictionary<string, string> code = HudScriptScan.HudAndGallery();
        string summary = PresentationFixtures.MethodBody(code["Hud.cs"], "public void SetTurnSummary(string? text, Color color)");
        Assert.Matches(@"AddThemeColorOverride\(""font_color""", summary); // 反面命中：被剔除的正是那一处内容字色
        Dictionary<string, string> scanned = code.ToDictionary(kv => kv.Key, kv => kv.Key == "Hud.cs" ? kv.Value.Replace(summary, string.Empty, StringComparison.Ordinal) : kv.Value);

        // 不覆盖样式盒、字号与字色（字色覆盖只查 font_ 开头的颜色项：描边色等也算按钮外观）。
        var overrides = new Regex(@"AddThemeStyleboxOverride\(|AddThemeFontSizeOverride\(|AddThemeColorOverride\(\s*""font_");
        Assert.Empty(HudScriptScan.Hits(scanned, overrides));

        // 没有写死的颜色：带数字或字符串的 new Color(、Color8、Color.FromHtml / FromHsv 之类、首参是字面量的目标类型 new(…)（HUD 脚本一律写明类型）。
        // 整体透明度写成 Colors.White with { A = … }（D5），Modulate 只允许这一种写法——按钮不能靠染色表达语义。
        var literalColor = new Regex(@"new\s+Color\s*\(\s*[-\d.""]|Color8\s*\(|FromHtml\s*\(|Color\.From\w+\s*\(|new\s*\(\s*[-\d.""]|Modulate\s*=(?!\s*Colors\.White\s+with\s*\{)");
        Assert.Empty(HudScriptScan.Hits(code, literalColor));
        Assert.Single(HudScriptScan.Hits(code, new Regex(@"Modulate\s*=")));  // 反面命中：放行的正是演出横幅那一处透明度

        // 语义经工厂参数：主操作两处（开始、不带入开始）、危险两处（弃赛、确认框的确认），选图的地图与难度两组经 Ui.Select。
        string hud = string.Concat(HudScriptScan.HudScripts.Select(n => code[n]));
        Assert.True(Regex.Matches(hud, @"ButtonKind\.Primary").Count >= 2, "主操作应经 ButtonKind.Primary");
        Assert.True(Regex.Matches(hud, @"ButtonKind\.Danger").Count >= 2, "危险操作应经 ButtonKind.Danger");
        Assert.Equal(2, Regex.Matches(PresentationFixtures.MethodBody(code["Hud.MapSelect.cs"], "public void ShowMapSelect(MapSelectModel model, string mapInfo)"), @"Ui\.Select\(").Count);

        // 反面命中：控件工厂里确实有字号覆盖（Ui.Text）——正则写错时上面恒真；写死颜色的判据在别的引擎层脚本里确实命中。
        Assert.Matches(overrides, PresentationFixtures.GodotScriptCode(HudScriptScan.Factory));
        Assert.Matches(literalColor, PresentationFixtures.GodotScriptCode("Visuals.cs"));
    }
}
