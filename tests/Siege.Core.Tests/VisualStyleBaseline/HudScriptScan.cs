using System.Text.RegularExpressions;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>
/// hud-theme 段 B 的引擎层源码扫描（design D7 第二组）共用的读取：<c>src/godot</c> 不在 sln 里，单元测试碰不到它，HUD 脚本的写法只能读源码守。
/// 一律经 <see cref="PresentationFixtures.GodotScriptCode"/> 去注释后扫；扫描正则不以词边界开头（复合标识符的教训，testing.md）。
/// </summary>
internal static class HudScriptScan
{
    /// <summary>
    /// HUD 脚本：<c>src/godot/scripts</c> 下全部 <c>Hud*.cs</c>（<c>Hud</c> 是 partial 类，新起一个 partial 文件也在扫描范围内——
    /// 原先写死三个文件名，把违例挪进 <c>Hud.Panels.cs</c> 就整个逃出全部 HUD 守门，hud-panels 段 B 检查方补）。
    /// 已知的三个文件必须都在（口径下界）。
    /// </summary>
    internal static string[] HudScripts
    {
        get
        {
            string[] names =
            [
                .. Directory.GetFiles(Path.Combine(PresentationFixtures.RepoRoot(), "src", "godot", "scripts"), "Hud*.cs")
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Order(StringComparer.Ordinal),
            ];
            foreach (string known in new[] { "Hud.cs", "Hud.MapSelect.cs", "Hud.Carry.cs" })
            {
                Assert.Contains(known, names);
            }

            return names;
        }
    }

    /// <summary>样式总览页脚本。</summary>
    internal const string Gallery = "UiGallery.cs";

    /// <summary>引擎层的控件工厂（唯一把取值翻成 Godot 对象的地方；反面命中用）。</summary>
    internal const string Factory = "Ui.cs";

    /// <summary>读一组脚本（去注释），并钉住样本口径：字符数不低于 <paramref name="minChars"/>。</summary>
    internal static Dictionary<string, string> Read(IEnumerable<string> names, int minChars)
    {
        Dictionary<string, string> code = names.ToDictionary(n => n, PresentationFixtures.GodotScriptCode);
        int total = code.Values.Sum(t => t.Length);
        Assert.True(total >= minChars, $"只扫到 {total} 字符（{string.Join("、", code.Keys)}）");
        return code;
    }

    /// <summary>HUD 三个脚本 + 总览页。</summary>
    internal static Dictionary<string, string> HudAndGallery() => Read([.. HudScripts, Gallery], 60_000);

    /// <summary><c>src/godot/scripts</c> 下全部 C# 脚本。</summary>
    internal static Dictionary<string, string> AllScripts()
    {
        string[] names =
        [
            .. Directory.GetFiles(Path.Combine(PresentationFixtures.RepoRoot(), "src", "godot", "scripts"), "*.cs")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal),
        ];
        Assert.True(names.Length >= 24, $"只扫到 {names.Length} 个脚本");
        Assert.Contains("Hud.cs", names);
        Assert.Contains(Gallery, names);
        return Read(names, 300_000);
    }

    /// <summary>某个正则在各脚本里的全部命中，格式化成"文件: 命中"。</summary>
    internal static string[] Hits(Dictionary<string, string> code, Regex pattern) =>
        [.. code.SelectMany(kv => pattern.Matches(kv.Value).Select(m => $"{kv.Key}: {m.Value}"))];
}
