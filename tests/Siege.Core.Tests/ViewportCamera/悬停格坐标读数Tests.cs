using Siege.Core.Board;
using Siege.Presentation.Camera;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>规格：viewport-camera —— Requirement: 悬停格坐标读数（tasks 5.4 的视图模型部分）。</summary>
public class 悬停格坐标读数Tests
{
    [Fact]
    public void 两位数行号_第1列第27行读数为A27()
    {
        Assert.Equal("A27", HoverReadout.Of(new Coord(0, 26)));
    }

    [Fact]
    public void 跳过I_第9列是J()
    {
        Assert.Equal("J1", HoverReadout.Of(new Coord(8, 0)));
        Assert.Equal("Z30", HoverReadout.Of(new Coord(24, 29)));
    }

    [Fact]
    public void 不在格上读数为空()
    {
        Assert.Equal(string.Empty, HoverReadout.Of(null));
    }

    [Fact]
    public void 读数与日志同一份记法_全盘逐格等于Coord的记法()
    {
        int checkedCells = 0;
        for (int x = 0; x < 25; x++)
        {
            for (int y = 0; y < 30; y++)
            {
                var c = new Coord(x, y);
                Assert.Equal(c.ToNotation(), HoverReadout.Of(c));
                Assert.Equal(c, Coord.Parse(HoverReadout.Of(c)));
                checkedCells++;
            }
        }

        Assert.Equal(750, checkedCells);
    }

    [Fact]
    public void Godot侧的悬停读数走视图模型_相机位姿只在一处写入()
    {
        string dir = Path.Combine(RepoRoot(), "src", "godot", "scripts");
        (string Name, string Text)[] scripts =
            [.. Directory.GetFiles(dir, "*.cs").Order().Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];

        // 样本口径下界：路径写错或扫到空目录时，下面的断言会恒真 / 恒假得莫名其妙。
        Assert.True(scripts.Length >= 10, $"只扫到 {scripts.Length} 个脚本");
        Assert.True(scripts.Sum(s => s.Text.Length) >= 50_000);

        // 读数文本取自视图模型（它只转调 Coord.ToNotation）。
        Assert.Contains(scripts, s => s.Text.Contains("HoverReadout.Of(", StringComparison.Ordinal));

        // 相机节点的位姿只在 BoardView 一处写入，且取自视图模型的 Pose——别处再写一份 LookAt / 俯角，缩放就可能带俯角变化。
        string[] lookAt = [.. scripts.Where(s => s.Text.Contains(".LookAt(", StringComparison.Ordinal)).Select(s => s.Name)];
        Assert.Equal(["BoardView.cs"], lookAt);
        string view = scripts.Single(s => s.Name == "BoardView.cs").Text;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(view, @"\.LookAt\("));
        Assert.Contains("pose.Eye", view, StringComparison.Ordinal);
        Assert.Contains("pose.Target", view, StringComparison.Ordinal);
        Assert.DoesNotContain("pitchDegrees", view, StringComparison.Ordinal);
        foreach ((string _, string text) in scripts)
        {
            Assert.DoesNotContain("Camera.Rotation", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Camera.Fov =", text, StringComparison.Ordinal);
        }

        // 只数 `.LookAt(` 挡不住绕过：`Camera.Position += …`、`LookAtFromPosition(`、`Camera.Transform = …`、`Camera.Translate(…)` 都能改位姿。
        // 这里把"对相机节点的写"整类列出来：赋值（含复合赋值）到位姿 / 投影属性，或调用会改位姿的方法。全仓恰两处，都在 ApplyCameraPose 里。
        // （读 `Camera.Position`、调 `UnprojectPosition` / `ProjectRay*` / `IsPositionBehind` 不算写；`new Camera3D { Fov = … }` 是建节点不是改位姿。）
        var writes = new System.Text.RegularExpressions.Regex(
            @"(?<![A-Za-z])[Cc]amera\s*\.\s*(?:(?:Global)?(?:Position|Transform|Basis|Rotation\w*)|Quaternion|Fov|Projection|KeepAspect|HOffset|VOffset|Size|Near|Far)\s*(?:[-+*/]\s*)?=(?!=)"
            + @"|(?<![A-Za-z])[Cc]amera\s*\.\s*(?:LookAt\w*|(?:Global)?Translate\w*|(?:Global)?Rotate\w*|Set[A-Z]\w*|Orthonormalize)\s*\(");
        string[] found = [.. scripts.SelectMany(s => writes.Matches(s.Text).Select(m => $"{s.Name}: {System.Text.RegularExpressions.Regex.Replace(m.Value, @"\s+", string.Empty)}"))];
        Assert.Equal(["BoardView.cs: Camera.Position=", "BoardView.cs: Camera.LookAt("], found);
    }
}
