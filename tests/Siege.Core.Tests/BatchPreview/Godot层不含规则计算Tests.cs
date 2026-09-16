using System.Text.RegularExpressions;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.BatchPreview;

/// <summary>
/// 强制回归（implement 2.2 / 6.3、tactical-ui D2、coordinates.md「映射唯一」）：<b>Godot 表现层</b>的源码守门。
/// </summary>
/// <remarks>
/// <para>裁决 10：<c>src/godot/</c> 用独立解决方案，不进 <c>siege.sln</c>，<c>dotnet test</c> 不依赖 Godot SDK——
/// 所以 <see cref="UI层不含规则计算Tests"/> 的 IL 扫描扫不到它（那条只扫 <c>Siege.Presentation</c>）。
/// 本类改用<b>源码文本</b>扫描补上这个缺口：Godot 侧不得调用规则计算入口、不得读他人私有手牌、
/// 不得在 <c>BoardGeometry</c> 之外写第二份坐标映射。</para>
/// <para>文本扫描是弱手段（能被改写绕过），但它抓的是"随手多写一份"这类真实事故，而这类事故此前<b>零守门</b>：
/// check 阶段实做的变异（把 <c>BoardView.DrawPieces</c> 的 <c>BoardGeometry.Center</c> 换成本地写反 Z 的第二份公式）
/// 在 <c>--build-solutions</c> 与 <c>--auto-demo</c> 下构建零警告、终局输出逐字相同、退出码 0，只有人眼看截图才能发现。</para>
/// </remarks>
public class Godot层不含规则计算Tests
{
    private static (string Name, string Text)[] Scripts()
    {
        string dir = Path.Combine(RepoRoot(), "src", "godot", "scripts");
        Assert.True(Directory.Exists(dir), dir);
        return [.. Directory.GetFiles(dir, "*.cs").Order().Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];
    }

    /// <summary>样本口径下界：路径写错 / 扫到空目录时，下面所有"不含"断言都会恒真。</summary>
    private static (string Name, string Text)[] NonEmptyScripts()
    {
        (string Name, string Text)[] scripts = Scripts();
        Assert.True(scripts.Length >= 10, $"只扫到 {scripts.Length} 个脚本");
        Assert.True(scripts.Sum(s => s.Text.Length) >= 50_000, $"只扫到 {scripts.Sum(s => s.Text.Length)} 字符");
        return scripts;
    }

    [Fact]
    public void Godot层不调用规则计算入口()
    {
        // D2 / 裁决 9：棋串、气、军势、覆盖、提子、先手值一律由 Core 富预演与公开快照给出，Godot 只渲染。
        // 变异验证 M-G1（check 阶段实做）：BoardView.DrawLiberties 里加一行 `_ = Siege.Core.Scoring.CoverageMap.Compute(...)` → 本测试红 1。
        string[] forbidden =
        [
            "PowerCalculator", "CoverageMap", "BatchRehearsal", "BatchPreviewBuilder", "LibertySnapshot", "CatchUpCompensation",
            "CaptureResolver", "FinalStandings", "Adjacency", "MapValidator", "RelicGenerator", "InitiativeOrder",
            ".GroupAt(", ".LibertiesOf(", ".AllGroups(", ".GroupsOf(", ".IsCaptured(", ".RemoveStones(", ".Apply(",
        ];

        string[] violations =
        [
            .. NonEmptyScripts()
                .SelectMany(s => forbidden.Where(f => s.Text.Contains(f, StringComparison.Ordinal)).Select(f => $"{s.Name}: {f}"))
                .Order(),
        ];

        Assert.Empty(violations);
    }

    [Fact]
    public void Godot层不读他人私有信息()
    {
        // §13.2 / D1：Godot 侧只能取本机玩家的私有视图；他人的手牌数量、征募面板、暂放批次一律经公开世界，取不到。
        // 变异验证 M-G2（check 阶段实做）：MatchSession.BuildWorld 里把 `AccessFor(Me)` 改为 `AccessFor(Match.CurrentPlayer!.Value)` → 本测试红 1。
        string[] violations =
        [
            .. NonEmptyScripts().SelectMany(s => Forbidden(s).Select(v => $"{s.Name}: {v}")).Order(),
        ];

        Assert.Empty(violations);

        static IEnumerable<string> Forbidden((string Name, string Text) script)
        {
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(script.Text, @"AccessFor\(([^)]*)\)", RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                if (m.Groups[1].Value != "Me")
                {
                    yield return m.Value;
                }
            }

            foreach (string token in new[] { ".Debug.", "PrivateViewOf(", "Hands.Records", "Relics.Generation", "ExportState(" })
            {
                if (script.Text.Contains(token, StringComparison.Ordinal))
                {
                    yield return token;
                }
            }
        }
    }

    [Fact]
    public void 坐标映射在Godot侧唯一()
    {
        // coordinates.md「映射唯一」：围棋记法坐标 ↔ 3D 世界位置只有 BoardGeometry 一份实现，别处一律调用它。
        // 判据取换算的两个语法特征：居中偏移 `(width - 1)` / `(height - 1)` 与反算的 `RoundToInt`。
        // 变异验证 M-G3（check 阶段实做）：BoardView.DrawPieces 用本地公式 `(cell.Coord.X - ((_width - 1) * 0.5f))` 代替
        //   BoardGeometry.Center（且 Z 符号写反）→ 本测试红 1；同一变异下 Godot 构建与 --auto-demo 全绿，是本测试存在的理由。
        (string Name, string Text)[] scripts = NonEmptyScripts();
        const string centering = @"(?:width|height|_width|_height)\s*-\s*1";
        const string inverse = @"RoundToInt";

        (string Name, string Text) geometry = scripts.Single(s => s.Name == "BoardGeometry.cs");

        // 反面：判据在唯一实现里确实命中，否则下面的"别处没有"只是规则失效
        Assert.Equal(4, Regex.Matches(geometry.Text, centering, RegexOptions.None, TimeSpan.FromSeconds(5)).Count);
        Assert.Equal(2, Regex.Matches(geometry.Text, inverse, RegexOptions.None, TimeSpan.FromSeconds(5)).Count);

        string[] second =
        [
            .. scripts.Where(s => s.Name != "BoardGeometry.cs")
                .Where(s => Regex.IsMatch(s.Text, centering, RegexOptions.None, TimeSpan.FromSeconds(5))
                    || Regex.IsMatch(s.Text, inverse, RegexOptions.None, TimeSpan.FromSeconds(5)))
                .Select(s => s.Name)
                .Order(),
        ];

        Assert.Empty(second);

        // 唯一入口确实在被使用（否则"别处没有换算"可能只是别处根本没画棋盘）
        Assert.True(scripts.Count(s => s.Text.Contains("BoardGeometry.", StringComparison.Ordinal)) >= 2);
    }
}
