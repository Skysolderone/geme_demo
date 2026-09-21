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
            // terrain-model：几何邻居 / 气边 / 覆盖关系三个导出入口，Godot 层不得自算邻接或地形过滤
            ".Neighbors(", ".LibertyNeighbors(", ".CoverageTargets(",
            // 高地加值的唯一实现入口。只禁调用形态——GroupPowerView.HighGroundBonus 属性是合法读数。
            // 变异验证（scoring-sites 段 B）M-B8：BoardView 里改调 `PieceEffects.HighGroundBonus(null!, null!)` → 本测试红 1。
            "PieceEffects", ".HighGroundBonus(",
            // artisan-terrain-edit 4.3：改造合法性与地形写入口的入口名。Godot 侧的可改造目标一律来自
            // PreviewPresentation.ArtisanEdits（Core 富预演 → 表现层），地形改动一律由规则层结算后经默认棋盘视图带过来。
            // 只禁带点的调用形态：TerrainEdit / TerrainEditKind 是值类型与枚举，Godot 读它们合法，且都不含下列子串。
            // 变异验证 M-SC5（实做，4.3 点名的那一条）：BoardView.DrawPreview 加一行
            //   `if (_width < 0) { _ = Siege.Core.Board.TerrainEditRules.LegalTargets(null!, default); }` → 本测试红 1。
            "TerrainEditRules.", "TerrainWriter.", "ApplyTerrainEdits",
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
    public void 落后补偿判定不在表现层重写一份()
    {
        // catch-up-recruit 裁决 1 / design.md D3：补偿判定的唯一实现是 Core 的 CatchUpCompensation，表现层只读来源拆分、不另算。
        // 把 "CatchUpCompensation" 列进上面的违禁 token 只挡得住"调用唯一实现"，挡不住"随手照抄一份阈值算式"——
        // 变异验证 M-CU12（check 阶段实做）：在 Hud.AddStructure 里写 `rank > (participants + 1) / 2 ? 1 : 0` 并拼进结构参数文案，
        //   Godot层不调用规则计算入口、UI层不含规则计算Tests 的 IL 扫描全绿（0 红）；补上本测试后该变异红 1。
        // 判据取阈值 ⌈n ÷ 2⌉ 的整数写法 `(… + 1) / 2`：全 src/ 树里只有唯一实现命中，且恰好 1 次。
        const string threshold = @"\+\s*1\s*\)\s*/\s*2";

        // 反面：判据在唯一实现里确实命中，否则下面的"别处没有"只是规则失效
        string unique = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Siege.Core", "Scoring", "CatchUpCompensation.cs"));
        Assert.Single(Regex.Matches(unique, threshold, RegexOptions.None, TimeSpan.FromSeconds(5)));

        string[] files =
        [
            .. new[] { Path.Combine(RepoRoot(), "src", "godot", "scripts"), Path.Combine(RepoRoot(), "src", "Siege.Presentation") }
                .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Order(),
        ];

        // 样本口径下界：路径写错时下面的"不含"断言会恒真
        Assert.True(files.Length >= 20, $"只扫到 {files.Length} 个表现层源文件");

        string[] second =
        [
            .. files.Where(f => Regex.IsMatch(File.ReadAllText(f), threshold, RegexOptions.None, TimeSpan.FromSeconds(5)))
                .Select(Path.GetFileName)
                .Order()!,
        ];

        Assert.Empty(second);
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

        // 反面：判据在唯一实现里确实命中，否则下面的"别处没有"只是规则失效。
        // 6 处的构成：Center 的 x / z 各一处，TryFromWorld 反算的 x / y 各一处，
        // 以及 board-coordinates 新增的两个标注锚点各取一次边界行列（ColumnLabelAnchor 的 height - 1、RowLabelAnchor 的 width - 1）。
        // 这个数字是精确值而非下界：BoardGeometry 每多一处换算都该有人复审一次，确认它不是第二份映射。
        Assert.Equal(6, Regex.Matches(geometry.Text, centering, RegexOptions.None, TimeSpan.FromSeconds(5)).Count);
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
