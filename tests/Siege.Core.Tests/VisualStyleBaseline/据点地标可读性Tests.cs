using System.Globalization;
using System.Text.RegularExpressions;
using Siege.Presentation.Style;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>
/// 规格：visual-style-baseline —— Requirement: 据点地标（change `scoring-sites` 裁决 S-16「地标可读性」）。
/// </summary>
/// <remarks>
/// <para>地标尺寸与"看不看得出"只能看图（<c>art/sites-v4/README.md</c>）；能被自动化钉住的是两类数字约束：
/// 据点格底色三档的灰度明度差、石碑石色与岩石的明度差（均从 <c>Visuals.cs</c> 源码读取），以及地标 / 底色不带碰撞体。</para>
/// <para><c>src/godot/</c> 不进 <c>siege.sln</c>，只能做源码文本扫描（testing.md「游离工程」）。明度口径与截图灰度图一致：
/// PIL <c>convert('L')</c> 的 0.299R + 0.587G + 0.114B。</para>
/// </remarks>
public class 据点地标可读性Tests
{
    private static string ScriptsDir() => Path.Combine(RepoRoot(), "src", "godot", "scripts");

    private static double Luma((int R, int G, int B) c) => (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);

    private static (int R, int G, int B) ColorOf(string visuals, string name)
    {
        System.Text.RegularExpressions.Match m = Regex.Match(
            visuals,
            $@"\b{name}\s*=\s*Color\.Color8\((\d+),\s*(\d+),\s*(\d+)\)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        Assert.True(m.Success, $"Visuals.cs 里找不到颜色 {name}");
        return (Parse(m.Groups[1].Value), Parse(m.Groups[2].Value), Parse(m.Groups[3].Value));

        static int Parse(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    }

    private static (int R, int G, int B) Lerp((int R, int G, int B) a, (int R, int G, int B) b, double t) =>
        ((int)Math.Round(a.R + ((b.R - a.R) * t)), (int)Math.Round(a.G + ((b.G - a.G) * t)), (int)Math.Round(a.B + ((b.B - a.B) * t)));

    [Fact]
    public void 据点底色三档灰度可辨且与所在地砖拉开明度()
    {
        string visuals = File.ReadAllText(Path.Combine(ScriptsDir(), "Visuals.cs"));
        double tent = Luma(ColorOf(visuals, "SiteBandTent"));
        double campfire = Luma(ColorOf(visuals, "SiteBandCampfire"));
        double stele = Luma(ColorOf(visuals, "SiteBandStele"));

        // 三档两两相差 ≥ 40（灰度图里靠明度即可区分档位）。
        // 变异验证 M-S16-1：SiteBandCampfire 改成与营帐同色 74,66,58 → 本测试红 1。
        Assert.True(Math.Abs(tent - campfire) >= 40, $"营帐 {tent:F1} / 篝火 {campfire:F1}");
        Assert.True(Math.Abs(campfire - stele) >= 40, $"篝火 {campfire:F1} / 石碑 {stele:F1}");
        Assert.True(Math.Abs(tent - stele) >= 40, $"营帐 {tent:F1} / 石碑 {stele:F1}");

        // 底色带与它所在的地砖拉开 ≥ 30：v4 的营帐全在出生区（插旗前 BirthHint 0.62 混合、锁定后阵营主色 0.34 混合，
        // 混合系数取自 BoardView.Build），篝火与石碑全在草地（布点见裁决 S-13）。
        (int, int, int) grass = ColorOf(visuals, "TilePlayable");
        Assert.True(Math.Abs(campfire - Luma(grass)) >= 30, $"篝火 {campfire:F1} / 草地 {Luma(grass):F1}");
        Assert.True(Math.Abs(stele - Luma(grass)) >= 30, $"石碑 {stele:F1} / 草地 {Luma(grass):F1}");

        List<(string Name, double Luma)> birthTiles = [("插旗前", Luma(Lerp(grass, ColorOf(visuals, "BirthHint"), 0.62)))];
        foreach (FactionStyle faction in FactionTable.All)
        {
            birthTiles.Add((faction.Name, Luma(Lerp(grass, (faction.Primary.R, faction.Primary.G, faction.Primary.B), 0.34))));
        }

        Assert.True(birthTiles.Count >= 5, $"只算了 {birthTiles.Count} 种出生区地砖");
        foreach ((string name, double luma) in birthTiles)
        {
            Assert.True(Math.Abs(tent - luma) >= 30, $"营帐 {tent:F1} / 出生区（{name}）{luma:F1}");
        }
    }

    [Fact]
    public void 石碑石色与岩石明度拉开()
    {
        string visuals = File.ReadAllText(Path.Combine(ScriptsDir(), "Visuals.cs"));
        double rock = Luma(ColorOf(visuals, "Rock"));
        double stone = Luma(ColorOf(visuals, "SteleStone"));
        double carving = Luma(ColorOf(visuals, "SteleCarving"));

        // S-16：灰色小碑易与岩石混淆 → 石面明度至少比岩石高 80，刻痕与石面至少差 30（否则刻痕看不见）。
        // 变异验证 M-S16-2：SteleStone 还原成段 B 的 168,176,190（明度 175，差 55）→ 本测试红 1。
        Assert.True(stone - rock >= 80, $"石碑 {stone:F1} / 岩石 {rock:F1}");
        Assert.True(Math.Abs(stone - carving) >= 30, $"石面 {stone:F1} / 刻痕 {carving:F1}");

        // 石碑不得再直接用岩石材质（基座原先用 Visuals.Rock）
        string lowPoly = File.ReadAllText(Path.Combine(ScriptsDir(), "LowPoly.cs"));
        int start = lowPoly.IndexOf("public static Node3D Stele()", StringComparison.Ordinal);
        Assert.True(start >= 0, "LowPoly.cs 里找不到 Stele()");
        int end = lowPoly.IndexOf("public static Node3D SiteFlag(", start, StringComparison.Ordinal);
        Assert.True(end > start, "LowPoly.cs 里 Stele() 之后找不到 SiteFlag()");
        Assert.DoesNotContain("Visuals.Rock", lowPoly[start..end], StringComparison.Ordinal);
    }

    [Fact]
    public void 地标与底色不带碰撞体()
    {
        // 拾取是 BoardGeometry.TryPick 的数学投影；任何碰撞体都会让"地标 / 底色不参与拾取"失去保证，而 --pick-check 对此不敏感。
        // 变异验证 M-S16-3：BoardView.AddSiteBand 里加一行 `_sites.AddChild(new StaticBody3D());` → 本测试红 1。
        string[] files = [.. Directory.GetFiles(ScriptsDir(), "*.cs").Order()];

        // 样本口径下界：路径写错时"不含"断言恒真
        Assert.True(files.Length >= 10, $"只扫到 {files.Length} 个脚本");
        string board = File.ReadAllText(Path.Combine(ScriptsDir(), "BoardView.cs"));
        Assert.Contains("AddSiteBand(", board, StringComparison.Ordinal);

        string[] forbidden = ["CollisionShape3D", "CollisionPolygon3D", "StaticBody3D", "Area3D", "RigidBody3D", "CharacterBody3D", "CollisionObject3D"];
        string[] violations =
        [
            .. files.SelectMany(f => forbidden.Where(t => File.ReadAllText(f).Contains(t, StringComparison.Ordinal)).Select(t => $"{Path.GetFileName(f)}: {t}")),
        ];
        Assert.Empty(violations);
    }
}
