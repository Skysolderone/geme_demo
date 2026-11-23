using System.Globalization;
using System.Text.RegularExpressions;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>
/// 规格：visual-style-baseline —— Requirement: 新地表的灰度可辨（terrain-bonus 段 B 追加裁决 Q2：岩石与相邻地表的灰度差）。
/// 色板只在 Godot 工程里（<c>src/godot/scripts/TerrainPalette.cs</c>，不在 sln，testing.md「游离工程」），这里按源码扫描取色板 A 的取值。
/// </summary>
/// <remarks>
/// 规格的口径是<b>渲染后</b>灰度差 ≥ 17（地表色块图 art/terrain-bonus-b/palette-rock.png 实测：岩石 68.3 / 沼泽 102.4 / 土路 171.6 / 草地 193.2–202.3；
/// h=2 岩石 119.7 / 沼泽 145.2 / 土路 194.0 / 草地 209.6–216.4）。渲染要起 Godot，自动守门退一步守<b>反照率</b>（Rec.601）灰度：
/// 同一张色块图上七种地表的反照率灰度与渲染灰度近似线性（土路 121 → 171.6、沼泽 68 → 102.4，斜率约 1.32），渲染差 17 约合反照率差 13，守门取 13。
/// 渲染差的复核仍按色块图（人工检查清单），本测试只挡"把岩石调回与土路 / 草地同档"这类回退。
/// 变异验证（段 B 阶段 2 Q2 实跑）：色板 A 的岩石改回 (120,136,110)（Q2 之前的值）→ 本条红 1；只改测试把门槛 13 改成 25 → 本条红 1（h=2 岩石与沼泽差 18.8）。
/// </remarks>
public class 新地表的灰度可辨Tests
{
    /// <summary>反照率灰度差门槛（≈ 渲染灰度差 17，见类注释）。</summary>
    private const double MinAlbedoGap = 13.0;

    [Fact]
    public void 岩石与相邻地表灰度可辨()
    {
        string source = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "godot", "scripts", "TerrainPalette.cs"));
        int start = source.IndexOf("public static readonly TerrainPalette A = new()", StringComparison.Ordinal);
        Assert.True(start >= 0, "找不到色板 A 的定义");
        string block = source[start..source.IndexOf("};", start, StringComparison.Ordinal)];

        (double R, double G, double B) Of(string name)
        {
            System.Text.RegularExpressions.Match m = Regex.Match(block, $@"\b{name} = Color\.Color8\((\d+), (\d+), (\d+)\)");
            Assert.True(m.Success, $"色板 A 里没有 {name}");
            return (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
        }

        System.Text.RegularExpressions.Match wash = Regex.Match(source, @"HighWashHigh \{ get; init; \} = ([0-9.]+)f;");
        Assert.True(wash.Success, "找不到 HighWashHigh 的缺省值");
        double t = double.Parse(wash.Groups[1].Value, CultureInfo.InvariantCulture);
        (double R, double G, double B) high = Of("HighWash");
        static double Gray((double R, double G, double B) c) => (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
        (double R, double G, double B) H2((double R, double G, double B) c) => (c.R + ((high.R - c.R) * t), c.G + ((high.G - c.G) * t), c.B + ((high.B - c.B) * t));

        string[] neighbours = ["Road", "GrassPatchA", "GrassPatchB", "GrassPatchC", "Marsh"];
        (double R, double G, double B) rock = Of("Rock");
        foreach (string name in neighbours)
        {
            (double R, double G, double B) other = Of(name);
            double flat = Math.Abs(Gray(rock) - Gray(other));
            double plate = Math.Abs(Gray(H2(rock)) - Gray(H2(other)));
            Assert.True(flat >= MinAlbedoGap, $"岩石与 {name} 的反照率灰度差 {flat:0.0} < {MinAlbedoGap}");
            Assert.True(plate >= MinAlbedoGap, $"h=2 岩石与 h=2 {name} 的反照率灰度差 {plate:0.0} < {MinAlbedoGap}");
        }
    }
}
