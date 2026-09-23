using System.Text.RegularExpressions;
using Siege.Core.Board;

namespace Siege.Core.Tests.TerrainSpec;

/// <summary>
/// 守门（terrain-surfaces design D1 / tasks 0.4）：四种新地表的规则差别各只进一处唯一实现；
/// 其余代码 MUST NOT 直接比较 <c>Surface.Desert / Marsh / Crag / Shallows</c>，只能调用唯一实现给出的谓词或查询。
/// </summary>
/// <remarks>
/// <para>扫 <c>src/</c> 全树（含不在 siege.sln 的 <c>src/godot/scripts</c>，testing.md「游离工程」），不扫 tests——测试里的枚举值是数据。</para>
/// <para>允许清单只放"地表 ↔ 字符码 / 显示名 / 地砖色"这类映射与各段的唯一落点；每加一项都要在注释里写明它是哪一种。
/// 清单里的每个文件也必须真的引用了新地表（否则清单会烂成摆设）。</para>
/// </remarks>
public class 新地表判断唯一Tests
{
    private static readonly Regex NewSurface = new(@"Surface\.(Desert|Marsh|Crag|Shallows)\b", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>相对 <c>src/</c> 的路径（正斜杠）。</summary>
    private static readonly string[] Allowed =
    [
        "Siege.Core/Board/TerrainData.cs",   // 映射：地表显示名（错误信息与图例共用）
        "Siege.Core/Board/MapFile.cs",       // 映射：地表 ↔ 地图文件字符码
        "Siege.Core/Board/MapValidator.cs",  // 校验：map-definition 第 9 条（出生区内无新地表）
        "Siege.Core/Board/Maps/FrontierSurfaces.cs",  // 生成：map-generation 新地表投放
        "Siege.Core/Scoring/PowerCalculator.cs",      // 规则：段 1 荒漠——独占计分谓词 ScoresTerritory
        "Siege.Sim/Program.cs",              // 映射：终端文本图字符
        "godot/scripts/BoardView.cs",        // 映射：地砖色
    ];

    [Fact]
    public void 地表枚举恰为八种()
    {
        Assert.Equal(8, Enum.GetValues<Surface>().Length);
        Assert.All(Enum.GetValues<Surface>(), s => Assert.False(string.IsNullOrWhiteSpace(s.DisplayName())));
        Assert.Equal(8, Enum.GetValues<Surface>().Select(s => s.DisplayName()).Distinct().Count());
    }

    [Fact]
    public void 新地表只在允许清单里被直接比较()
    {
        // 变异验证 M-S0d：在 Siege.Core/Ai/GroupSafety.Analyze 开头写 `_ = board.Map.SurfaceAt(default) == Surface.Marsh;` → 本测试红 1（实跑）。
        string src = Path.Combine(PresentationFixtures.RepoRoot(), "src");
        char sep = Path.DirectorySeparatorChar;
        string[] files =
        [
            .. Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !p.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
                .Order(),
        ];

        // 样本口径下界：路径写错 / 扫到空目录时下面的"不含"断言会恒真
        Assert.True(files.Length >= 90, $"只扫到 {files.Length} 个源文件");

        // 反面：判据命中典型写法
        Assert.Matches(NewSurface, "if (map.SurfaceAt(c) == Surface.Shallows)");
        Assert.DoesNotMatch(NewSurface, "Surface.DeepWater");

        string Rel(string path) => Path.GetRelativePath(src, path).Replace('\\', '/');

        string[] offenders =
        [
            .. files.Where(f => !Allowed.Contains(Rel(f)))
                .SelectMany(f => File.ReadAllLines(f)
                    .Select((text, i) => (text, line: i + 1))
                    .Where(x => NewSurface.IsMatch(x.text))
                    .Select(x => $"{Rel(f)}:{x.line}: {x.text.Trim()}")),
        ];
        Assert.True(offenders.Length == 0, "新地表的直接比较出现在允许清单之外：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));

        // 正面：清单里每个文件都真的引用了新地表
        foreach (string rel in Allowed)
        {
            string text = File.ReadAllText(Path.Combine(src, rel));
            Assert.True(NewSurface.IsMatch(text), $"允许清单里的 {rel} 并没有引用新地表，清单过期");
        }
    }
}
