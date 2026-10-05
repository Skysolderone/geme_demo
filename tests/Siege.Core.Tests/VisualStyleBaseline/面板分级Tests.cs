using System.Text.RegularExpressions;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 面板分级</summary>
/// <remarks>
/// 只覆盖能落到取值上的三条 Scenario；「每个面板都有级别」要读 HUD 脚本，归 hud-theme 段 B 的源码扫描守门。
/// 期望值（色值、线宽、圆角）与阈值（8 / 40 / 128–240 / 48）都在测试里独立写，不取被测常量来比。
/// 变异验证（hud-theme 段 A；脚本二进制读写、锚点恰命中 1 次、finally 还原后逐字节比对并刷新 mtime；
/// 口径 <c>dotnet test tests/Siege.Core.Tests -c Release --filter FullyQualifiedName~VisualStyleBaseline</c> 共 40 条，每条红数后列红的测试，本类的略去类名）：
/// M-P1「次面板底色改成与主面板同色」→ 红 2（三级面板灰度下可辨 的底色亮度差、面板取值表）。
/// M-P2「次面板边框提亮到 (150, 120, 60)，亮度差 26」→ 红 2（三级面板灰度下可辨 的边框亮度差、面板取值表）。
/// M-P3「次面板 alpha 255」→ 红 2（三级面板灰度下可辨 的 alpha 范围、面板取值表）。
/// M-P4「提示条底色 (60, 64, 72)，亮度 63」→ 红 2（三级面板灰度下可辨 的亮度上限、面板取值表）。
/// M-P13「PanelTier 增第四级」→ 红 1（三级面板灰度下可辨 的"恰好三级"）。
/// M-P5「提示条边框宽 1」→ 红 1（提示条没有边框）。M-B25「占位色 alpha 255」→ 红 2（提示条没有边框 的 Border.A、按钮 键盘焦点只画一圈金边）。
/// M-P6「提示条圆角 6 改 3」→ 红 2（提示条没有边框 对主面板、面板取值表）。M-P15「次面板圆角取 6」→ 红 2（提示条没有边框 对次面板、面板取值表）。
/// M-P7「主面板边框宽 0」→ 红 2（主面板外观不变、提示条没有边框 的对照）。M-P16「次面板边框宽 0」→ 红 2（提示条没有边框 的对照、面板取值表）。
/// M-P9「主面板改取次面板底色」→ 红 3（主面板外观不变、三级面板灰度下可辨、面板取值表）。M-P14「主面板边框另立字面量 (184, 146, 70)」→ 红 1（主面板外观不变）。
/// M-P8「主面板圆角 3 改 4」→ 红 1（主面板外观不变）。M-B29「共用圆角常量 3 改 4」→ 红 3（主面板外观不变、面板取值表、按钮取值表）。
/// M-P10「主面板底色另立字面量，PanelFill alpha 改 200」→ 红 1（主面板外观不变 的 PanelFill 别名断言，字面量断言仍绿）。
/// M-P17「主面板边框另立字面量 (184, 146, 72)，PanelBorder 改 (184, 150, 72)」→ 红 5（主面板外观不变 的 PanelBorder 别名断言 + 按钮类 4 条）。
/// M-P11「PanelBorder 改 (184, 150, 72)」→ 红 6（主面板外观不变、面板取值表 + 按钮类 4 条）。
/// M-P12「未知级别回落到主面板」→ 红 1（面板取值表 的 Throws）。M-X1「Rgba.Luma 改成三通道平均」→ 红 2（面板取值表 的亮度列、按钮取值表 的亮度列）。
/// 只改测试：M-T1「次面板期望底色换成提示条的」→ 红 1（面板取值表）；M-T7「主面板期望圆角改 4」→ 红 1（主面板外观不变）；M-T8「亮度期望对调 33 / 86」→ 红 1（面板取值表）。
/// </remarks>
public class 面板分级Tests
{
    private static UiPanelStyle Primary => UiTheme.PanelStyleOf(PanelTier.Primary);

    private static UiPanelStyle Secondary => UiTheme.PanelStyleOf(PanelTier.Secondary);

    private static UiPanelStyle Hint => UiTheme.PanelStyleOf(PanelTier.Hint);

    [Fact]
    public void 三级面板灰度下可辨()
    {
        // 规格：主面板与次面板不只靠色相区分——底色亮度相差不少于 8，边框亮度相差不少于 40。
        int fillGap = Math.Abs(Secondary.Fill.Luma - Primary.Fill.Luma);
        int borderGap = Math.Abs(Primary.Border.Luma - Secondary.Border.Luma);
        Assert.True(fillGap >= 8, $"底色亮度差 {fillGap}（主 {Primary.Fill.Luma} / 次 {Secondary.Fill.Luma}）");
        Assert.True(borderGap >= 40, $"边框亮度差 {borderGap}（主 {Primary.Border.Luma} / 次 {Secondary.Border.Luma}）");

        // 规格：恰好三级，三级的底色都满足「UI 风格约束」对面板的要求（半透明 alpha 128–240、亮度不高于 48）。
        PanelTier[] tiers = Enum.GetValues<PanelTier>();
        Assert.Equal(3, tiers.Length);
        foreach (PanelTier tier in tiers)
        {
            Rgba fill = UiTheme.PanelStyleOf(tier).Fill;
            Assert.True(fill.A is >= 128 and <= 240, $"{tier} 底色 alpha {fill.A}");
            Assert.True(fill.Luma <= 48, $"{tier} 底色亮度 {fill.Luma}");
        }
    }

    [Fact]
    public void 提示条没有边框()
    {
        Assert.Equal(0, Hint.BorderWidthPx);
        Assert.Equal(0, Hint.Border.A); // 占位色全透明：即使引擎层照样把它写进样式盒也画不出东西
        Assert.True(Hint.CornerRadiusPx > Primary.CornerRadiusPx, $"提示条圆角 {Hint.CornerRadiusPx} 对主面板 {Primary.CornerRadiusPx}");
        Assert.True(Hint.CornerRadiusPx > Secondary.CornerRadiusPx, $"提示条圆角 {Hint.CornerRadiusPx} 对次面板 {Secondary.CornerRadiusPx}");

        // 对照：另两级确实有边框（"宽度为 0"不是三级共有的缺省值）。
        Assert.True(Primary.BorderWidthPx > 0, $"主面板边框宽 {Primary.BorderWidthPx}");
        Assert.True(Secondary.BorderWidthPx > 0, $"次面板边框宽 {Secondary.BorderWidthPx}");
    }

    [Fact]
    public void 主面板外观不变()
    {
        // 引入分级之前的面板（src/godot/scripts/Ui.cs 的 PanelStyle，提交 ccafe40）：
        // 底色 (18, 20, 26, 208)、边框 (184, 146, 72)、边框宽 1、圆角 3。四项逐项相同。
        Assert.Equal(new Rgba(18, 20, 26, 208), Primary.Fill);
        Assert.Equal(new Rgba(184, 146, 72, 255), Primary.Border);
        Assert.Equal(1, Primary.BorderWidthPx);
        Assert.Equal(3, Primary.CornerRadiusPx);

        // 主面板就是原有的那对取值，没有另立一份（「信息密度」守的 PanelFill / PanelBorder 仍然是画出来的面板）。
        Assert.Equal(UiTheme.PanelFill, Primary.Fill);
        Assert.Equal(UiTheme.PanelBorder, Primary.Border);
    }

    [Fact]
    public void 面板取值表()
    {
        // hud-theme design D1 面板表的初值，逐格钉住（负责人调参时这里跟着改；上面三条守的是规格阈值，调参不得越过）。
        // 次面板：底色 (30, 34, 42, 192) 亮度 33，边框 (96, 86, 62) 亮度 86，边框宽 1，圆角 3。
        Assert.Equal(new Rgba(30, 34, 42, 192), Secondary.Fill);
        Assert.Equal(new Rgba(96, 86, 62, 255), Secondary.Border);
        Assert.Equal(1, Secondary.BorderWidthPx);
        Assert.Equal(3, Secondary.CornerRadiusPx);

        // 提示条：底色 (18, 23, 31, 214) 亮度 22（回合摘要横幅原先内联的 0.07 / 0.09 / 0.12 / 0.84 换成 8 位），圆角 6。
        Assert.Equal(new Rgba(18, 23, 31, 214), Hint.Fill);
        Assert.Equal(6, Hint.CornerRadiusPx);

        // D1 表里的亮度列（整数亮度算式下的读数）：主面板底 20 / 边框 148，次面板底 33 / 边框 86，提示条底 22。
        int[] lumas = [Primary.Fill.Luma, Primary.Border.Luma, Secondary.Fill.Luma, Secondary.Border.Luma, Hint.Fill.Luma];
        Assert.Equal(new[] { 20, 148, 33, 86, 22 }, lumas);

        // 未知级别响亮失败，不回落到某一级。
        Assert.Throws<ArgumentOutOfRangeException>(() => UiTheme.PanelStyleOf((PanelTier)99));
    }

    /// <summary>design D3 的面板归级表：（脚本，创建面板的方法，该方法里依次创建的各面板级别）。</summary>
    private static readonly (string File, string Method, PanelTier[] Tiers)[] PanelTable =
    [
        ("Hud.cs", "private void BuildTurnBanner()", [PanelTier.Primary]),
        ("Hud.cs", "private void BuildOrderBar()", [PanelTier.Secondary]),
        ("Hud.cs", "private void BuildRankPanel()", [PanelTier.Primary]),
        ("Hud.cs", "private void BuildHandBar()", [PanelTier.Primary]),
        ("Hud.cs", "private void BuildLayerButtons()", [PanelTier.Secondary]),
        ("Hud.cs", "private void BuildActionPanel()", [PanelTier.Primary]),
        ("Hud.cs", "private void BuildPreviewPanel()", [PanelTier.Primary]),
        ("Hud.cs", "private void BuildLayerPanel()", [PanelTier.Secondary]),
        ("Hud.cs", "private void BuildCenterPanel()", [PanelTier.Primary]),
        ("Hud.cs", "private void BuildRecruitBar()", [PanelTier.Secondary]),
        ("Hud.MapSelect.cs", "private void BuildMapSelect(MapSelectModel model)", [PanelTier.Primary]),
        ("Hud.Carry.cs", "private void EnsureCarryRoot()", [PanelTier.Primary, PanelTier.Secondary, PanelTier.Primary, PanelTier.Primary]),
    ];

    /// <summary>
    /// 规格「每个面板都有级别」：HUD 脚本里每一处创建面板的调用都经由控件工厂并带着级别，没有自带样式盒的面板（hud-theme D7 第二组）。
    /// 变异验证（hud-theme 段 B，往脚本里注入违例，二进制读写、锚点恰命中 1 次、finally 还原后逐字节比对并刷新 mtime，<c>--filter FullyQualifiedName~VisualStyleBaseline</c> 共 44 条）：
    /// S-1「行动顺序条改成主面板」、S-2「补给面板旁加一行 new StyleBoxFlat()」、S-3「选图面板内边距写 12」、S-4「Build() 里多建一个面板」、S-5「Hud.cs 加一个 new PanelContainer」各 → 红 1（本条）；
    /// 另 B-2「弃赛按钮加 AddThemeStyleboxOverride(」→ 红 2（本条 + 按钮 语义不靠逐处改色）、G-1「总览页加 new StyleBoxFlat()」→ 红 2（本条 + 样式总览页 与对局界面同源）。
    /// 段 B 检查补的绕过形状：X-7「信息层说明面板建好后改 ThemeTypeVariation 换成主面板」补守门前 0 红、补后 → 红 1（本条）；R-1「行动顺序条改主面板」→ 红 1（本条）。
    /// </summary>
    [Fact]
    public void 每个面板都有级别()
    {
        Dictionary<string, string> code = HudScriptScan.Read(HudScriptScan.HudScripts, 50_000);

        // 每一处 Ui.Panel( 都是"级别, 间距阶梯名, 间距阶梯名"的形状：先数原始出现次数，再要求全部命中严格形状，别的写法漏不过去。
        var shape = new Regex(@"Ui\.Panel\(\s*PanelTier\.(\w+)\s*,\s*UiTheme\.Space[0-5]\s*,\s*UiTheme\.Space[0-5]\s*\)");
        int raw = code.Values.Sum(t => Regex.Matches(t, @"Ui\.Panel\(").Count);
        int shaped = code.Values.Sum(t => shape.Matches(t).Count);
        Assert.True(raw >= 15, $"只扫到 {raw} 处 Ui.Panel(");
        Assert.Equal(raw, shaped);

        // D3 归级表逐个方法核对；表里的面板总数等于脚本里的全部面板——表外多出一个面板也红。
        foreach ((string file, string method, PanelTier[] tiers) in PanelTable)
        {
            string body = PresentationFixtures.MethodBody(code[file], method);
            PanelTier[] found = [.. shape.Matches(body).Select(m => Enum.Parse<PanelTier>(m.Groups[1].Value))];
            Assert.True(tiers.SequenceEqual(found), $"{file} {method}：期望 {string.Join("/", tiers)}，实际 {string.Join("/", found)}");
        }

        Assert.Equal(PanelTable.Sum(p => p.Tiers.Length), raw);

        // 提示条：回合摘要横幅经工厂的提示条入口，不自带样式盒。
        Assert.Matches(@"_turnSummary\s*=\s*Ui\.HintBanner\(", code["Hud.cs"]);

        // 没有自带样式盒的面板：HUD 脚本与总览页里不新建样式盒、不覆盖样式盒、不绕过工厂新建面板容器。
        // 主题变体与主题项也只由工厂写：面板建好后改 ThemeTypeVariation 换级、按钮直接指变体、Set("theme_override_…")、theme.SetStylebox 之类都算绕过。
        var selfStyled = new Regex(@"new\s+StyleBox\w*|AddThemeStyleboxOverride\(|new\s+(?:Panel|PanelContainer)\s*[({]|ThemeTypeVariation|theme_override_|\.Set(?:Stylebox|Color|FontSize|Constant|TypeVariation)\(");
        Assert.Empty(HudScriptScan.Hits(HudScriptScan.HudAndGallery(), selfStyled));

        // 反面命中：判据在控件工厂里确实命中（正则写错时上面恒真）。
        Assert.Matches(selfStyled, PresentationFixtures.GodotScriptCode(HudScriptScan.Factory));
    }
}
