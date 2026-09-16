using System.Text.RegularExpressions;
using Siege.Core.Board;

namespace Siege.Core.Tests.TerrainSpec;

/// <summary>
/// 守门（terrain-model 裁决 C-8 / implement 7.5）：崖壁阈值 <see cref="TerrainData.CliffDrop"/> 是全仓唯一一份——
/// 气边的 |Δh| ≤ 1、覆盖关系的 h_t − h_s ≤ 1、表现层差集原因的 h_s − h_t ≥ 2 都是同一个常量的三种写法。
/// </summary>
/// <remarks>
/// <para>只能做<b>源码文本</b>扫描：C# <c>const</c> 编译期内联，IL 里 <c>TerrainData.CliffDrop</c> 与裸字面量 <c>2</c> 都是 <c>ldc.i4.2</c>，
/// 反射 / IL 扫描分不出"引用了常量"和"照抄了一个 2"。</para>
/// <para>扫 <c>src/</c> 全树（含不在 siege.sln 的 <c>src/godot/scripts</c>，testing.md「游离工程」），不扫 tests——测试里 <c>("F7", 2)</c> 是数据不是阈值。</para>
/// </remarks>
public class 崖壁阈值唯一Tests
{
    /// <summary>"含 HeightAt(…) 的差值与裸数字比较"，两个方向：HeightAt 在减号左边 / 右边。运算符不含 ==、!=（MapSymmetry 的 <c>HeightAt(p) != HeightAt(q)</c>、Sim 的 <c>HeightAt(c) == h</c> 是合法比较）。</summary>
    private static readonly Regex[] BareThreshold =
    [
        new(@"HeightAt\([^()]*\)[^;{}]*?-[^;{}]*?(<=|>=|<|>)\s*\d", RegexOptions.None, TimeSpan.FromSeconds(5)),
        new(@"-[^;{}]*?HeightAt\([^()]*\)[^;{}]*?(<=|>=|<|>)\s*\d", RegexOptions.None, TimeSpan.FromSeconds(5)),
    ];

    [Fact]
    public void 崖壁阈值取自裁决D2()
    {
        // 裁决 D2：|Δh| ≤ 1 相邻，Δh ≥ 2 为崖壁；D3 高度三层 0/1/2。
        Assert.Equal(2, TerrainData.CliffDrop);
    }

    [Fact]
    public void 高度差与裸数字比较只允许经CliffDrop()
    {
        // 变异验证 M-D1：LayerContents.ReasonFor 改回 `>= 2` → 本测试红 1；
        // M-D2：Adjacency.LibertyNeighbors 改回 `<= 1` → 本测试红 1（其余 terrain 测试仍绿，因为数值等价——正是 IL 守门抓不到的形状）。
        string root = PresentationFixtures.RepoRoot();
        char sep = Path.DirectorySeparatorChar;
        string[] files =
        [
            .. Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !p.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
                .Order(),
        ];

        // 样本口径下界：路径写错 / 扫到空目录时下面的"不含"断言会恒真
        Assert.True(files.Length >= 90, $"只扫到 {files.Length} 个源文件");
        Assert.True(files.Sum(f => new FileInfo(f).Length) >= 500_000, "源码总量低于下界");

        // 反面：改动前的三行原文必须被判据命中，否则"别处没有"只是扫描器空转
        string[] originals =
        [
            "if (map.IsPlayable(n) && Math.Abs(map.HeightAt(n) - h) <= 1 && !map.HasFence(c, n))",
            "&& map.HeightAt(target) - sourceHeight <= 1;",
            "if (map.HeightAt(source.Stone) - map.HeightAt(target) >= 2)",
        ];
        foreach (string line in originals)
        {
            Assert.True(BareThreshold.Any(r => r.IsMatch(line)), $"判据未命中原始写法：{line}");
        }

        // 正面：唯一常量确实在两处消费点被引用（否则"别处没有裸数字"可能只是别处根本不比高度）
        string adjacency = File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Board", "Adjacency.cs"));
        string layers = File.ReadAllText(Path.Combine(root, "src", "Siege.Presentation", "Layers", "LayerContents.cs"));
        Assert.True(Regex.Matches(adjacency, @"TerrainData\.CliffDrop", RegexOptions.None, TimeSpan.FromSeconds(5)).Count >= 2, "Adjacency 未经 CliffDrop 判气边与覆盖");
        Assert.Contains("TerrainData.CliffDrop", layers, StringComparison.Ordinal);

        string[] offenders =
        [
            .. files.SelectMany(f => File.ReadAllLines(f)
                    .Select((text, i) => (text, line: i + 1))
                    .Where(t => BareThreshold.Any(r => r.IsMatch(t.text)))
                    .Select(t => $"{Path.GetRelativePath(root, f)}:{t.line}: {t.text.Trim()}"))
                .Order(),
        ];

        Assert.Empty(offenders);
    }
}
