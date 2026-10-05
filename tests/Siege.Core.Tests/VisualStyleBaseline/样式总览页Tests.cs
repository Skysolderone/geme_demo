using System.Text.RegularExpressions;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 样式总览页</summary>
/// <remarks>
/// 「出图」要起 Godot 截图，不进单元测试（hud-theme 段 B 2.3 以 <c>--ui-gallery=&lt;绝对路径&gt;</c> 实跑：文件存在、日志无异常、退出码 0）。
/// 这里守「与对局界面同源」：总览页脚本不新建样式盒、不写死颜色、不覆盖主题项，样例的样式盒与字色从 <c>Ui.BuildTheme()</c> 的主题里按状态名取。
/// 变异验证（hud-theme 段 B，往脚本里注入违例；口径同 <see cref="面板分级Tests.每个面板都有级别"/>）：
/// G-2「总览页用 Colors.Gold」、G-3「主题改成 new Theme()」、G-4「样例改用写死的状态名 "hover"」、G-5「GameRoot 的选项名拼成 ui-galery」各 → 红 1（本条）；
/// G-1「总览页加 new StyleBoxFlat()」→ 红 2（+ 面板 每个面板都有级别）；G-6「总览页写 new Color(0.5f, …)」、B-5「样例加 AddThemeFontSizeOverride(」→ 各红 2（+ 按钮 语义不靠逐处改色）。
/// </remarks>
public class 样式总览页Tests
{
    [Fact]
    public void 与对局界面同源()
    {
        string gallery = PresentationFixtures.GodotScriptCode(HudScriptScan.Gallery);
        Assert.True(gallery.Length >= 4_000, $"总览页只扫到 {gallery.Length} 字符");

        // 不新建样式盒、不写死颜色（含命名色 Colors.*）、不覆盖任何主题项（样式盒、字号、字色都来自主题与工厂）。
        var forbidden = new Regex(@"new\s+StyleBox\w*|new\s+Color\s*\(|Color8\s*\(|FromHtml\s*\(|Colors\.\w+|new\s+Theme\s*[({]|AddTheme(?:Stylebox|FontSize|Color)Override\(");
        Assert.Empty(forbidden.Matches(gallery).Select(m => m.Value));

        // 主题取自对局界面同一个工厂；样例的样式盒与字色按（类型，状态名）从主题取，状态名经工厂的映射（不另写一份 "hover" 之类的字面量）。
        Assert.Contains("Ui.BuildTheme()", gallery, StringComparison.Ordinal);
        Assert.Matches(@"\.GetStylebox\(\s*Ui\.StyleboxNameOf\(", gallery);
        Assert.Matches(@"\.GetColor\(\s*Ui\.FontColorNameOf\(", gallery);
        Assert.DoesNotMatch(@"""(?:normal|hover|pressed|disabled|focus|font_\w*)""", gallery);

        // 启动参数接入严格解析（拼错退出码 1 的那套），且由它建总览页。
        string root = PresentationFixtures.GodotScriptCode("GameRoot.cs");
        Assert.Matches(@"args\.Text\(\s*""ui-gallery""", root);
        Assert.Contains("new UiGallery(", root, StringComparison.Ordinal);

        // 反面命中：判据在控件工厂与别的引擎层脚本里确实命中（正则写错时上面恒真）。
        Assert.Matches(forbidden, PresentationFixtures.GodotScriptCode(HudScriptScan.Factory));
        Assert.Matches(@"new\s+Color\s*\(", PresentationFixtures.GodotScriptCode("Visuals.cs"));
    }
}
