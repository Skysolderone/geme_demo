using System.Text.RegularExpressions;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 字号与间距阶梯</summary>
/// <remarks>
/// 只覆盖能落到取值上的两条 Scenario；「脚本里没有临时拼的字号与间距」要读 HUD 脚本，归 hud-theme 段 B 的源码扫描守门。
/// 每条里先断言规格的界（严格递增、正文 ≤ 16、最大 ≤ 20），再断言取值：调参越界时先红的是界，报的是哪一条界。
/// 期望值在测试里独立写。
/// 变异验证（hud-theme 段 A；脚本与口径同 <see cref="面板分级Tests"/>，共 40 条）：
/// 字号阶梯——M-L5「FontLadder 漏掉 Title」→ 红 1（五级）；M-L1「Small 改 15」、M-L6「FontLadder 次序颠倒」→ 各红 1（严格递增）；
/// M-L2「Body 改 17、Title 改 18」→ 红 2（正文 ≤ 16、UI风格约束Tests.信息密度）；M-L3「Banner 改 21」→ 红 1（最大 ≤ 20）；M-L4「Title 改 16」→ 红 1（取值）；
/// M-L8「FontLadder 首项写死 13、Caption 改 12」→ 红 1（名字与阶梯同数）；M-L7「BodyFontPx = Body + 1」→ 红 1（别名）。
/// 间距阶梯——M-L11「SpaceLadder 漏掉 Space3」、M-L9「Space1 改 0」、M-L10「Space5 改 16」、M-L12「SpaceLadder 末项写死 12、Space5 改 10」→ 各红 1。
/// 只改测试：M-T3「字号期望对调 17 / 19」、M-T4「间距期望对调 6 / 8」→ 各红 1。
/// 段 B 补的 D8 常量（口径 44 条）：D-1「ButtonPaddingYPx 改 5」、D-2「HintPaddingXPx 改 16」→ 各红 1（间距阶梯）；D-3「ShowBannerPx 改 48」、D-4「ShowBannerPx = Banner」→ 各红 1（字号阶梯）；
/// 只改测试 T-1「内边距期望对调成 12 / 4 / 4」→ 红 1（间距阶梯）。
/// </remarks>
public class 字号与间距阶梯Tests
{
    [Fact]
    public void 字号阶梯()
    {
        int[] ladder = [.. UiTheme.FontLadder];

        // 规格：五级，严格递增，正文不大于 16，最大一级不大于 20。
        Assert.Equal(5, ladder.Length);
        for (int i = 1; i < ladder.Length; i++)
        {
            Assert.True(ladder[i] > ladder[i - 1], $"第 {i + 1} 级 {ladder[i]} 对第 {i} 级 {ladder[i - 1]}");
        }

        Assert.True(ladder[2] <= 16, $"正文 {ladder[2]}");
        Assert.True(ladder.Max() <= 20, $"最大一级 {ladder.Max()}");

        // 规格：依次是 13（注释）、14（按钮与次要正文）、15（正文）、17（标题）、19（横幅）。
        int[] expected = [13, 14, 15, 17, 19];
        Assert.Equal(expected, ladder);

        // 阶梯里的每一级与它的名字是同一个数（HUD 脚本传的是名字）。
        int[] named = [UiTheme.Caption, UiTheme.Small, UiTheme.Body, UiTheme.Title, UiTheme.Banner];
        Assert.Equal(expected, named);

        // 原有的正文字号是正文一级的别名，值不变（「信息密度」与引擎层脚本仍在用它）。
        int bodyAlias = UiTheme.BodyFontPx;
        Assert.Equal(15, bodyAlias);

        // hud-theme D8-4：演出横幅的字号是呈现层常量 45（原先引擎层写 BodyFontPx * 3）。它是演出字号，不在阶梯里（规格写明不受阶梯限制），
        // 也不随正文字号联动——正文一级改动时它不跟着变。
        int showBanner = UiTheme.ShowBannerPx;
        Assert.Equal(45, showBanner);
        Assert.DoesNotContain(showBanner, ladder);
    }

    [Fact]
    public void 间距阶梯()
    {
        int[] ladder = [.. UiTheme.SpaceLadder];

        // 规格：六级，严格递增。
        Assert.Equal(6, ladder.Length);
        for (int i = 1; i < ladder.Length; i++)
        {
            Assert.True(ladder[i] > ladder[i - 1], $"第 {i + 1} 级 {ladder[i]} 对第 {i} 级 {ladder[i - 1]}");
        }

        // 规格：依次是 0、2、4、6、8、12。
        int[] expected = [0, 2, 4, 6, 8, 12];
        Assert.Equal(expected, ladder);

        // 阶梯里的每一级与它的名字是同一个数。
        int[] named = [UiTheme.Space0, UiTheme.Space1, UiTheme.Space2, UiTheme.Space3, UiTheme.Space4, UiTheme.Space5];
        Assert.Equal(expected, named);

        // hud-theme D8-2 / D8-3：按钮上下内边距与提示条内边距取阶梯上的值——按钮上下 4，提示条左右 12、上下 4。
        int[] paddings = [UiTheme.ButtonPaddingYPx, UiTheme.HintPaddingXPx, UiTheme.HintPaddingYPx];
        Assert.All(paddings, p => Assert.Contains(p, ladder));
        Assert.Equal(new[] { 4, 12, 4 }, paddings);
    }

    /// <summary>
    /// 规格「脚本里没有临时拼的字号与间距」（hud-theme D7 第二组 + D8-4）：
    /// <c>src/godot/scripts</c> 全部脚本里，正文字号或阶梯名后面跟加减乘除的写法只允许出现在 <c>Hud.RankFontPx</c> 的方法体里（tiered-number-show 的按档放大，演出字号）；
    /// HUD 脚本与总览页设置容器间距的调用，第二个参数只取 <c>UiTheme.Space0</c>–<c>Space5</c>。
    /// 变异验证（hud-theme 段 B，往脚本里注入违例；口径同 <see cref="面板分级Tests.每个面板都有级别"/>）：F-1「回合副标题改回 UiTheme.BodyFontPx - 2」、F-2「BoardView.cs 加一行 UiTheme.BodyFontPx + 1」、
    /// F-3「前缀复合名 LabelBodyFontPx - 1」、F-4「Hud.cs 一处间距改回 8」、F-5「总览页 h_separation 写 8」、F-6「阶梯名做加法 UiTheme.Title + 2」各 → 红 1（本条）。
    /// 段 B 检查补的绕过形状（补守门前 0 红，补后）：X-2「项名写成局部常量 Sep、值写 1」、X-5「Ui.Text 字号写 16」、X-6「数字在前 1 + UiTheme.BodyFontPx」各 → 红 1（本条）；
    /// X-1「separation 传局部变量 gap = 1」、R-4「演出横幅改回 BodyFontPx * 3」→ 各红 1（本条）。
    /// </summary>
    [Fact]
    public void 脚本里没有临时拼的字号与间距()
    {
        // 字号：全部引擎层脚本。正则不以词边界开头，前缀复合名（xxxBodyFontPx）也算。
        Dictionary<string, string> all = HudScriptScan.AllScripts();
        // 运算符在前在后都算（1 + UiTheme.BodyFontPx 与 UiTheme.BodyFontPx + 1 是同一种写法）。
        const string font = @"(?:BodyFontPx|UiTheme\.(?:Caption|Small|Body|Title|Banner|ShowBannerPx))";
        var arithmetic = new Regex($@"{font}\s*[-+*/]|[-+*/]\s*{font}");
        string rank = PresentationFixtures.MethodBody(all["Hud.cs"], "public static int RankFontPx(PowerDisplay display)");
        Assert.Matches(arithmetic, rank); // 反面命中：豁免的正是那一处按档放大的算式
        all["Hud.cs"] = all["Hud.cs"].Replace(rank, string.Empty, StringComparison.Ordinal);
        Assert.Empty(HudScriptScan.Hits(all, arithmetic));

        // 间距：HUD 脚本与总览页。先数原始调用次数，再要求全部是阶梯名。
        Dictionary<string, string> hud = HudScriptScan.HudAndGallery();
        var separation = new Regex(@"AddThemeConstantOverride\(\s*""(?:[hv]_)?separation""\s*,\s*([^)]*)\)");
        string[] args = [.. hud.Values.SelectMany(t => separation.Matches(t).Select(m => m.Groups[1].Value.Trim()))];
        Assert.True(args.Length >= 15, $"只扫到 {args.Length} 处间距设置");
        Assert.Equal(hud.Values.Sum(t => Regex.Matches(t, @"AddThemeConstantOverride\(\s*""(?:[hv]_)?separation""").Count), args.Length);
        Assert.All(args, a => Assert.Matches(@"^UiTheme\.Space[0-5]$", a));

        // 常量覆盖只用来设间距：项名写成常量或别的常量项（描边宽等）都不放过——每一处 AddThemeConstantOverride( 都得是上面那个形状。
        Assert.Equal(hud.Values.Sum(t => Regex.Matches(t, @"AddThemeConstantOverride\(").Count), args.Length);

        // 字号不写数字：Ui.Text 的字号参数（第三个位置参数或 size:）不得是数字字面量，只传阶梯名或呈现层给的演出字号。
        var literalSize = new Regex(@"Ui\.Text\([^;]*?,\s*(?:size\s*:\s*)?\d+\s*(?:,\s*(?:wrap\s*:\s*)?(?:true|false)\s*)?\)");
        Assert.Empty(HudScriptScan.Hits(hud, literalSize));
        Assert.Matches(literalSize, "Ui.Text(\"补给点\", Ui.InfoText, 16)"); // 反面命中：正则写错时上面恒真
    }
}
