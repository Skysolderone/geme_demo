using Siege.Core.Board;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// frontier-map tasks 3.4：<c>map</c> 子命令能按地图标识打印边疆图的文本图（高度 / 地表 / 桥 / 栅栏 / 平台编号 / 信物）与校验报告项
/// （各平台到五类目标的距离）；不带选项仍是缺省地图。retire-legacy-maps 段 C 起子命令只在给 <c>--out</c> 时落盘，不再往当前工作目录的 <c>maps/</c> 导出内置图。
/// </summary>
[Collection(ConsoleRedirect.Collection)]
public class 地图子命令Tests
{
    [Fact]
    public void 按标识打印边疆图的文本图与距离报告项()
    {
        // retire-legacy-maps 段 A2：请求的地图由边疆图 v2 改为 2 人内置棋盘图（非缺省图，M-B12 照样打得到）；方法名沿用规格 Scenario 名，段 D 随规格改。
        // 边疆图上的桥 / 林地 / 栅栏三项文本图断言删除：棋盘图上这三种地形不存在（当前所有地图都不产生），文本图图例的写法仍由 map 子命令代码负责。
        // 变异 M-B12：ExportMap 不读 --map → 打出来的是缺省 4 人棋盘图（且 --map 成了未知选项），本测试红。
        (int code, string text, string err) = RunMain("map", "--map", SimFixtures.Board2);
        string[] lines = [.. text.Split('\n').Select(l => l.TrimEnd('\r', ' '))];

        Assert.True(code == 0, err);
        Assert.Contains("地图 siege-2p-board-v1  32×35", lines);
        Assert.Contains(lines, l => l.StartsWith("出生区 3 个，各 36/36/36 个可落子格", StringComparison.Ordinal));
        Assert.Contains("信物格 7（出生区 3，公共区 4）", lines);
        Assert.Contains("地图校验通过。", lines);

        // 报告项：三类目标各一行，每行列出 3 块出生棋盘的距离（棋盘互不连通，一律"不可达"）。
        string[] reports = [.. lines.Where(l => l.StartsWith("[BIRTH_ZONE_DISTANCE_REPORT]", StringComparison.Ordinal))];
        Assert.Equal(3, reports.Length);
        Assert.All(reports, r => Assert.Contains("出生区 3 = ", r, StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("各出生区沿气边最短距离（出生区 1/2/3）", StringComparison.Ordinal));
        Assert.Contains("  中央入口  -/-/-", lines);

        // 文本图：35 行 × 32 列对齐（每格 3 字符）；第 17 行手写期望——左右场景格、公共棋盘与中央入口上的高档信物。
        string[] rows = [.. text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 4 && char.IsDigit(l[2]) && l[3] == ' ')];
        Assert.Equal(35, rows.Length);
        Assert.All(rows, r => Assert.Equal(4 + (32 * 3), r.Length));
        Assert.Contains(" 17 ## ## ## ## ## ## ## ## ## ## ## ## 0  0  0  0  0  0  0  0R 0  0  0  0  0  0  0  ## ## ## ## ##", lines);
        Assert.Contains("    A  B  C  D  E  F  G  H  J  K  L  M  N  O  P  Q  R  S  T  U  V  W  X  Y  Z  AA AB AC AD AE AF AG", lines);   // 列标：Z 之后是双字母 AA–AG
        Assert.Contains(rows, r => r.Contains("0r", StringComparison.Ordinal));          // 出生区信物
        Assert.Contains(rows, r => r.Contains("03", StringComparison.Ordinal));          // 3 号出生区
    }

    [Fact]
    public void 不带选项仍打印缺省地图且未登记的拼写被拒绝()
    {
        (int code, string text, _) = RunMain("map");
        Assert.Equal(0, code);
        Assert.Contains("地图 siege-4p-board-v1  39×41", text, StringComparison.Ordinal);   // builtin-board-maps D3：缺省为 4 人内置棋盘图（board:5，39×41）

        (int badCode, _, string err) = RunMain("map", "--mapp", SimFixtures.Board2);
        Assert.NotEqual(0, badCode);
        Assert.Contains("--mapp", err, StringComparison.Ordinal);
    }

    [Fact]
    public void 给地图文件路径时只打印不导出()
    {
        // 原意：设计师拿内置图的副本改地形（Id 没改）后 `map --map 副本.json` 只想看一眼，不得写出 maps/<内置标识>.json（变异 M-B14）。
        // retire-legacy-maps 段 C：内置图不再有 maps/ 权威文件，map 子命令不给 --out 就一律不落盘——按内置名请求（原先会写 maps/）与按文件路径请求都一样。
        // 变异 MC-M2（段 C 实跑）：ExportMap 恢复"按内置标识请求时另写 maps/<id>.json" → 本测试与 内置棋盘图Tests.导出文件名合法 共红 2。
        string dir = SimFixtures.TempDir("map-subcommand-file");
        string copy = Path.Combine(dir, "my-copy.json");
        MapData edited = Siege.Core.Board.Maps.MapCatalog.Resolve(SimFixtures.Board2);
        File.WriteAllText(copy, MapFile.ToJson(edited));
        string exported = Path.Combine("maps", $"{edited.Id}.json");
        string repoFile = Path.Combine(TestMaps.RepoRoot(), "maps", $"{edited.Id}.json");
        Assert.False(File.Exists(repoFile), $"仓库里不应有 {repoFile}");
        CleanExports();

        foreach (string request in new[] { copy, SimFixtures.Board2 })
        {
            (int code, string text, string err) = RunMain("map", "--map", request);

            Assert.True(code == 0, err);
            Assert.Contains("地图 siege-2p-board-v1  32×35", text, StringComparison.Ordinal);
            Assert.DoesNotContain("已导出", text, StringComparison.Ordinal);
            Assert.False(File.Exists(exported), $"map --map {request} 不给 --out 却导出到了 maps/。");
            Assert.False(File.Exists(repoFile), "导出到了仓库的 maps/。");
        }
    }

    /// <summary>删掉测试进程工作目录里的 maps/（变异下 map 子命令可能又往那里写）。retire-legacy-maps 段 B 起已没有"maps/&lt;标识&gt;.json"文件回落，留着也不会被读回。</summary>
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
