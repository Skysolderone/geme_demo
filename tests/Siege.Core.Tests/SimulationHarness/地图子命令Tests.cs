using Siege.Core.Board;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// frontier-map tasks 3.4：<c>map</c> 子命令能按地图标识打印边疆图的文本图（高度 / 地表 / 桥 / 栅栏 / 平台编号 / 信物）与校验报告项
/// （各平台到五类目标的距离）；不带选项仍是缺省地图。子命令会向当前工作目录的 <c>maps/</c> 导出内置图——测试进程的工作目录是测试输出目录，不碰仓库里的权威文件。
/// </summary>
[Collection(ConsoleRedirect.Collection)]
public class 地图子命令Tests
{
    [Fact]
    public void 按标识打印边疆图的文本图与距离报告项()
    {
        // 变异 M-B12：ExportMap 不读 --map → 打出来的是 v4（且 --map 成了未知选项），本测试红。
        (int code, string text, string err) = RunMain("map", "--map", "siege-frontier-v2");
        string[] lines = [.. text.Split('\n').Select(l => l.TrimEnd('\r', ' '))];

        Assert.True(code == 0, err);
        Assert.Contains("地图 siege-frontier-v2  25×30", lines);
        Assert.Contains(lines, l => l.StartsWith("出生区 6 个，各 81/64/49/36/25/25 个可落子格", StringComparison.Ordinal));
        Assert.Contains("信物格 16（出生区 9，公共区 7）", lines);
        Assert.Contains("地图校验通过。", lines);

        // 报告项：三类目标各一行，每行列出 6 个平台的距离（restore-go-core-rules：五项改三项）。
        string[] reports = [.. lines.Where(l => l.StartsWith("[BIRTH_ZONE_DISTANCE_REPORT]", StringComparison.Ordinal))];
        Assert.Equal(3, reports.Length);
        Assert.All(reports, r => Assert.Contains("出生区 6 = ", r, StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("各出生区沿气边最短距离（出生区 1/2/3/4/5/6）", StringComparison.Ordinal));
        Assert.Contains("  中央入口  10/10/11/11/4/4", lines);

        // 文本图：30 行 × 25 列对齐（每格 3 字符）；第 15 行手写期望——5 号台（整块留白）、东西两处 3 格宽缓坡的中格、广场中心的高档信物、6 号台。
        string[] rows = [.. text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 4 && char.IsDigit(l[2]) && l[3] == ' ')];
        Assert.Equal(30, rows.Length);
        Assert.All(rows, r => Assert.Equal(4 + (25 * 3), r.Length));
        Assert.Contains(" 15 ~~ ## ## ## 25 25 25 25 25 1^ 0  0  0R 0  0  1^ 26 26 26 26 26 ## ## ~~ ~~", lines);
        Assert.Contains("    " + string.Join("  ", Coord.ColumnLetters.Take(25).Select(c => c.ToString())), lines);
        Assert.Contains(rows, r => r.Contains("0=", StringComparison.Ordinal));          // 桥
        Assert.Contains(rows, r => r.Contains("0F", StringComparison.Ordinal));          // 林地
        Assert.Contains(rows, r => r.Contains('|', StringComparison.Ordinal));           // 东西向相邻格之间的栅栏
        Assert.Contains(lines, l => l.Trim() == "--");                                   // 南北向相邻格之间的栅栏
    }

    [Fact]
    public void 不带选项仍打印缺省地图且未登记的拼写被拒绝()
    {
        (int code, string text, _) = RunMain("map");
        Assert.Equal(0, code);
        Assert.Contains("地图 siege-4p-base-v5  13×13", text, StringComparison.Ordinal);

        (int badCode, _, string err) = RunMain("map", "--mapp", "siege-frontier-v2");
        Assert.NotEqual(0, badCode);
        Assert.Contains("--mapp", err, StringComparison.Ordinal);
    }

    [Fact]
    public void 给地图文件路径时只打印不导出()
    {
        // 设计师拿 v4 的副本改地形（Id 没改）后 `map --map 副本.json` 只想看一眼：不得覆盖 maps/ 下的权威文件。
        // 判据必须是"请求的是不是内置标识"，不是读到的 map.Id。变异 M-B14：改回按 map.Id 判 → 当前目录的 maps/ 被写出，本测试红。
        string dir = SimFixtures.TempDir("map-subcommand-file");
        string copy = Path.Combine(dir, "my-copy.json");
        MapData edited = Siege.Core.Board.Maps.FourPlayerBaseMap.Create();
        File.WriteAllText(copy, MapFile.ToJson(edited));
        string exported = Path.Combine("maps", $"{edited.Id}.json");
        string repoFile = Path.Combine(FrontierFixtures.RepoRoot(), "maps", $"{edited.Id}.json");
        byte[] repoBefore = File.ReadAllBytes(repoFile);
        CleanExports();

        (int code, string text, string err) = RunMain("map", "--map", copy);

        Assert.True(code == 0, err);
        Assert.Contains("地图 siege-4p-base-v5  13×13", text, StringComparison.Ordinal);
        Assert.DoesNotContain("已导出", text, StringComparison.Ordinal);
        Assert.False(File.Exists(exported), "按文件路径请求的地图被导出到了 maps/。");
        Assert.Equal(repoBefore, File.ReadAllBytes(repoFile));
    }

    /// <summary>删掉本类在测试进程工作目录里导出的地图文件：留着会让目录的"maps/&lt;标识&gt;.json"文件回落替别的测试把图读回来，掩盖内置表的缺行。</summary>
    private static void CleanExports()
    {
        // 只清测试进程的工作目录（测试输出目录）；万一工作目录就是仓库根，绝不碰权威的 maps/。
        if (Directory.Exists("maps") && !File.Exists("siege.sln"))
        {
            Directory.Delete("maps", recursive: true);
        }
    }

    private static (int Code, string Out, string Error) RunMain(params string[] args)
    {
        var output = new StringWriter();
        var err = new StringWriter();
        TextWriter savedOut = Console.Out;
        TextWriter savedErr = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(err);
            return (Siege.Sim.Program.Main(args), output.ToString(), err.ToString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
            CleanExports();
        }
    }
}
