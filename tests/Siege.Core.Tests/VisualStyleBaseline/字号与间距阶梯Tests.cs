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
    }
}
