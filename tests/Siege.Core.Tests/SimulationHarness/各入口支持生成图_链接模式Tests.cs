using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.SimulationHarness.LinkSimFixtures;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/simulation-harness —— Requirement: 各入口支持生成图（tasks 3.0 / 3.1 / 3.2）：
/// 终端版开新局、裸 link、每局换图、生成器变了、map 子命令打印（含连通分量 1）；原"批量配对可重复（<c>--pairing-seed</c>）、回放重建链接、模拟链接（<c>--links</c>）"已随 open-map 段 B 删除，改为断言这些选项一律报错。
/// </summary>
[Collection(ConsoleRedirect.Collection)]
public class 各入口支持生成图_链接模式Tests
{
    [Fact]
    public void 终端版开新局()
    {
        // 规格 Scenario「终端版开新局」：play --map link:12345 的插旗提示列出 5 个占位（位置与边长），盲选，不画任何棋盘的内部布局；打印的地图标识为 link:12345。
        // 变异 MC-6：PlayCommand 在链接模式仍画全图（render.Board(zones: true)）→ "号棋盘 · …（左下角 …）" 不出现，本测试红。
        (int code, string output, string err) = RunMain(null, new StringReader("q\n"), "play", "--map", "link:12345", "--seed", "5", "--no-carry");
        Assert.True(code == 0, err);
        Assert.Contains("地图 link:12345（5 块紧贴成一张连通地图的出生棋盘）", output, StringComparison.Ordinal);
        MapData map = MapCatalog.Resolve("link:12345");
        for (int zone = 0; zone < 5; zone++)
        {
            BoundsOf(map, zone, out Coord origin, out int width, out int height);
            // link-map-terrain 5.1：占位行改为"编号 · 主题 · 宽×高（左下角 …）"（match-setup「插旗阶段的可见信息」）。
            Assert.Contains($"  {zone + 1} 号棋盘 · {map.BirthBoard(zone).Theme!.Value.DisplayName()} · {width}×{height}（左下角 {origin.ToNotation()}）", output, StringComparison.Ordinal);
        }

        Assert.Contains("盲选出生棋盘", output, StringComparison.Ordinal);
        Assert.DoesNotContain(" ? ", output, StringComparison.Ordinal);   // 插旗阶段没有画任何信物格（不暴露棋盘内部布局）
    }

    [Fact]
    public void 终端版链接图能开局并按座位渲染()
    {
        // tasks 3.0「play 在 link: 上能建局并跑起来」→ open-map：选棋盘后锁定、开始第 1 大回合并轮到本机玩家；全图全可见（没有"势力不可见"的玩家），
        // 棋盘画整张地图（唯一战场 = 5 块棋盘的包围盒），构筑期提示"只能在自己的出生棋盘落子"。
        (int code, string output, string err) = RunMain(null, new StringReader("2\nq\n"), "play", "--map", "link:12345", "--seed", "5", "--no-carry");
        Assert.True(code == 0, err);
        Assert.Contains("你锁定了 2 号棋盘", output, StringComparison.Ordinal);
        Assert.DoesNotContain("出生区锁定：", output, StringComparison.Ordinal);   // 不列出别人的旗
        Assert.DoesNotContain("其他战场（势力不可见）", output, StringComparison.Ordinal);
        Assert.Contains("只能在自己的出生棋盘落子", output, StringComparison.Ordinal);
        Assert.Contains("════════ 轮到你了 ════════", output, StringComparison.Ordinal);

        // 画整张地图：列标行是 5 块棋盘包围盒的列（第 2 列起到东边留白前），不含画布留白列 A / B。
        MapData map = MapCatalog.Resolve("link:12345");
        int minX = map.Boards.Min(b => b.Origin.X);
        int maxX = map.Boards.Max(b => b.Origin.X + b.Width - 1);
        string columns = "    " + string.Concat(Enumerable.Range(minX, maxX - minX + 1).Select(x => $" {new Coord(x, 0).Column,-2}"));
        Assert.Contains(columns + Environment.NewLine, output, StringComparison.Ordinal);
        Assert.DoesNotContain(" A  B  C ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void 裸link取种子并打印完整标识()
    {
        // 规格：以裸 link 启动时，入口取一个地图种子并向用户打印完整标识；取种子只调用一次来源。
        int calls = 0;
        (int code, string output, string err) = RunMain(() => { calls++; return 777; }, new StringReader("q\n"), "play", "--map", "link", "--seed", "5", "--no-carry");
        Assert.True(code == 0, err);
        Assert.Equal(1, calls);
        Assert.Contains("本次地图为 link:777", output, StringComparison.Ordinal);
        Assert.Contains("地图 link:777（", output, StringComparison.Ordinal);
    }

    [Fact]
    public void 每局换图的批次()
    {
        // 规格 Scenario「每局换图的批次」的缩小版（不跑批量：3 局、各截断 4 个小回合）：起始地图种子 100、每局换图，日志首部地图标识依次为 link:100..link:102。
        // 变异 MC-7：RunConfig.MapIdAt 的链接分支去掉 → 解析成 gen: 报错，本测试红。
        RunConfig config = Config(turnLimit: 4, count: 3) with { MapId = "link:100", MapPerMatch = true };
        List<MatchLog> logs = BatchRunner.Execute(config.Validated(), parallelism: 1);
        Assert.Equal(["link:100", "link:101", "link:102"], logs.Select(l => l.Header.MapId));
        Assert.All(logs, l => Assert.False(l.IsFailed, l.Failure?.Message));
        Assert.All(logs, l => Assert.Equal(MapFile.Digest(MapCatalog.Resolve(l.Header.MapId)), l.Header.MapDigest));
    }

    [Fact]
    public void 生成器变了()
    {
        // 规格 Scenario「生成器变了」：回放时按标识重新生成的初始地图，其内容摘要与日志首部记录的不同 → 在首部报"地图不一致"并停止（不重跑）。
        MatchLog original = Round4Log.Value;
        MatchLog copy = SimFixtures.Clone(original);
        var changed = new MatchLog { Header = copy.Header with { MapDigest = new string('0', 64) }, Turns = copy.Turns, Events = copy.Events, Result = copy.Result };
        ReplayResult replay = Replayer.Replay(changed);
        Assert.False(replay.Identical);
        Assert.Equal(1, replay.FirstDivergentLine);
        Assert.NotNull(replay.MapMismatch);
        Assert.Contains("地图不一致", replay.ToString(), StringComparison.Ordinal);
        Assert.Empty(replay.Replayed.Turns);
    }

    [Fact]
    public void map子命令打印链接初始图()
    {
        // 规格：map 子命令能打印任一 link: 初始地图的文本图与校验报告。
        (int code, string output, string err) = RunMain(null, null, "map", "--map", "link:1");
        Assert.True(code == 0, err);
        MapData initial = MapCatalog.Resolve("link:1");
        Assert.Contains($"地图 link:1  {initial.Width}×{initial.Height}", output, StringComparison.Ordinal);
        Assert.Contains("地图校验通过", output, StringComparison.Ordinal);
        Assert.Contains("连通分量 1", output, StringComparison.Ordinal);   // open-map 裁决 B5：按几何四邻实算（唯一战场）
        Assert.DoesNotContain("链接 #", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("map", "--links", "1-4,2-5")]
    [InlineData("run", "--pairing-seed", "42")]
    [InlineData("run", "--final-countdown", "2")]
    [InlineData("play", "--final-countdown", "0")]
    public void 配对与链接相关选项已删除(string command, string option, string value)
    {
        // 规格 simulation-harness「各入口支持生成图」（open-map）：任何入口 MUST NOT 提供配对或链接相关选项——严格命令行下 --links / --pairing-seed / --final-countdown 一律报错退出，
        // 不建局、不写任何输出目录。
        string outDir = Path.Combine(SimFixtures.TempDir($"retired-{option[2..]}"), "out");
        string[] args = command == "run" ? [command, "--out", outDir, "--seed", "1", option, value] : [command, "--map", "link:1", option, value];
        (int code, string output, string err) = RunMain(null, new StringReader(string.Empty), args);
        Assert.NotEqual(0, code);
        Assert.Contains(option[2..], err + output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }

    [Fact]
    public void map子命令打印每块棋盘的编号主题宽高与公共信物数()
    {
        // link-map-terrain 5.1：map --map link:1 每块出生棋盘一行"编号 · 主题 · 宽×高"，并给左下角与该棋盘内的公共信物数；主题中文名唯一来源 BoardThemeNames.DisplayName。
        // 输出里本来就有"林地 n"与图例"F 林地"，只断言"含林地"恒真——所以逐块拼出整行断言，并另查五个主题名两两不同。
        // 变异（实跑，过滤本类）：MLT-6（map 的棋盘行固定打印"高台"）→ 红 1（本测试）；MLT-7（公共信物数不按棋盘过滤、打全图公共信物数）→ 红 1（本测试）。
        (int code, string output, string err) = RunMain(null, null, "map", "--map", "link:1");
        Assert.True(code == 0, err);
        MapData map = MapCatalog.Resolve("link:1");
        int contested = 0;
        var names = new List<string>();
        var specials = new List<int>();
        for (int zone = 0; zone < 5; zone++)
        {
            BoardPlate plate = map.BirthBoard(zone);
            int publicRelics = map.RelicCells.Count(r => r.Value.Zone == RelicZone.Contested && plate.Contains(r.Key));
            contested += publicRelics;
            string name = plate.Theme!.Value.DisplayName();
            names.Add(name);
            // link-map-surfaces D3：行尾另给该棋盘内的新地表格数（测试内独立计数）。
            int special = plate.Cells().Count(c => map.SurfaceAt(c) is Surface.Desert or Surface.Marsh or Surface.Crag or Surface.Shallows);
            specials.Add(special);
            Assert.Contains($"  {zone + 1} 号棋盘 · {name} · {plate.Width}×{plate.Height}  左下角 {plate.Origin.ToNotation()}  公共信物 {publicRelics}  新地表 {special}\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        }

        // 样本口径：各棋盘新地表格数之和 = 全图新地表格数，且全图数大于任何一块（否则按棋盘过滤与否测不出来）。
        // link-map-surfaces 1.3（F1 / F2）后 link:1 五块都是 2 格，原"各块取值不止一种"的口径失效，改为这条。
        int mapWide = map.AllCoords().Count(c => map.SurfaceAt(c) is Surface.Desert or Surface.Marsh or Surface.Crag or Surface.Shallows);
        Assert.Equal(mapWide, specials.Sum());
        Assert.True(specials.All(n => n < mapWide), $"link:1 新地表 {string.Join("/", specials)}，全图 {mapWide}");

        Assert.Equal(new[] { "林地", "栅栏阵", "河流", "混合", "高台" }, names.Order(StringComparer.Ordinal));
        // 样本口径：各棋盘公共信物数之和等于全图公共区信物数，且 ≥ 2（否则按棋盘过滤与否测不出来）。
        Assert.Equal(map.RelicCells.Count(r => r.Value.Zone == RelicZone.Contested), contested);
        Assert.True(contested >= 2, $"link:1 公共信物 {contested} 个；每块至多 1 个，≥ 2 时全图数才不等于任何一块的数");
    }

    [Theory]
    [InlineData("siege-4p-base-v5")]
    [InlineData("siege-frontier-v2")]
    [InlineData("gen:12345")]
    [InlineData("board:7")]
    [InlineData("maps/siege-4p-base-v5.json")]
    public void map子命令拒绝旧标识(string mapId)
    {
        // retire-legacy-maps 3.4（规格 simulation-harness「map 子命令」）：旧标识族与其他入口一样经目录报错，退出码 1，消息说明只接受 link:<地图种子>。
        // 原「map 子命令仍可打印旧标识」随旧地图删除。
        (int code, string output, string err) = RunMain(null, null, "map", "--map", mapId);
        Assert.Equal(1, code);
        Assert.Contains("不受支持", err, StringComparison.Ordinal);
        Assert.Contains("只接受 link:<地图种子>", err, StringComparison.Ordinal);
        Assert.DoesNotContain("地图校验", output, StringComparison.Ordinal);
    }

    [Fact]
    public void map子命令缺省地图()
    {
        // retire-legacy-maps 3.4：不带 --map 时打印 link:1（原缺省为 v5），且不导出任何文件（内置图的权威文件导出已删除）；未登记的拼写照旧被拒绝。
        string before = Directory.GetCurrentDirectory();
        bool mapsBefore = Directory.Exists(Path.Combine(before, "maps"));
        (int code, string output, string err) = RunMain(null, null, "map");
        Assert.True(code == 0, err);
        MapData initial = MapCatalog.Resolve("link:1");
        Assert.Contains($"地图 link:1  {initial.Width}×{initial.Height}", output, StringComparison.Ordinal);
        Assert.Contains("地图校验通过", output, StringComparison.Ordinal);
        Assert.DoesNotContain("已导出", output, StringComparison.Ordinal);
        Assert.Equal(mapsBefore, Directory.Exists(Path.Combine(before, "maps")));

        (int badCode, _, string badErr) = RunMain(null, null, "map", "--mapp", "link:1");
        Assert.NotEqual(0, badCode);
        Assert.Contains("--mapp", badErr, StringComparison.Ordinal);
    }

    [Fact]
    public void 生成器只经目录调用_裸link只在入口最外层取种子()
    {
        // retire-legacy-maps：由已删除的「各入口支持生成图Tests.生成器只经目录调用_裸gen只在入口最外层取种子」改为链接档版本（守门不降级）。
        // 守门（源码扫描；src/godot 不在解决方案里，反射看不见）：
        //   ① 三个入口与表现层不直接调链接图生成器的建图入口——一律经目录 MapCatalog.Resolve；
        //   ② 识别裸 link 的地方只有：标识的唯一实现、目录（拒绝它）、批量配置校验（每局换图时拒绝它）、批量 / 终端入口（Program）、图形版入口（GameRoot）。
        //      取时钟落种子的只有最后两处（各一次），且都先经同一个折叠函数 LinkMapId.FriendlySeed。
        // 变异记录见 migration.md（在 BatchRunner 直接调 LinkMapGenerator.Generate → 红；PlayCommand 加一处 IsBareRequest → 红）。
        string src = Path.Combine(TestPaths.RepoRoot(), "src");
        (string Path, string Text)[] files =
        [
            .. Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !p.Contains($"{Path.DirectorySeparatorChar}.godot{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(p => (Path.GetRelativePath(src, p).Replace('\\', '/'), File.ReadAllText(p))),
        ];
        Assert.True(files.Length >= 120, $"样本口径：只扫到 {files.Length} 个源文件。");
        Assert.Contains(files, f => f.Path.StartsWith("godot/scripts/", StringComparison.Ordinal));

        string[] generatorCallers = [.. files.Where(f => f.Text.Contains("LinkMapGenerator.Generate(", StringComparison.Ordinal)).Select(f => f.Path).Order(StringComparer.Ordinal)];
        Assert.Equal(["Siege.Core/Board/Maps/MapCatalog.cs"], generatorCallers);   // 反面：目录确实在调

        string[] bareReaders = [.. files.Where(f => f.Text.Contains("IsBareRequest(", StringComparison.Ordinal)).Select(f => f.Path).Order(StringComparer.Ordinal)];
        Assert.Equal(
            ["Siege.Core/Board/Maps/LinkMapId.cs", "Siege.Core/Board/Maps/MapCatalog.cs", "Siege.Sim/Config/RunConfig.cs", "Siege.Sim/Program.cs", "godot/scripts/GameRoot.cs"],
            bareReaders);

        string program = files.Single(f => f.Path == "Siege.Sim/Program.cs").Text;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"Stopwatch\.GetTimestamp\(\)"));          // 缺省来源 ClockMapSeed
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"=> LinkMapId\.FriendlySeed\(\(ulong\)System\.Diagnostics\.Stopwatch\.GetTimestamp\(\)\);"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"LinkMapId\.Format\(mapSeedSource\(\)"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"mapSeedSource\(\)"));   // 只有裸 link 取一次，别无他处
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"=> Execute\(args, ClockMapSeed\)"));
        string gameRoot = files.Single(f => f.Path == "godot/scripts/GameRoot.cs").Text;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(gameRoot, @"LinkMapId\.Format\(LinkMapId\.FriendlySeed\(\(ulong\)Stopwatch\.GetTimestamp\(\)\)"));

        // 规则内核永不读时钟（裸 link 的种子不可能在 Core 里取）。
        Assert.DoesNotContain(files, f => f.Path.StartsWith("Siege.Core/", StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(f.Text, @"Stopwatch\.|DateTime\.(Utc)?Now|Environment\.TickCount|Random\.Shared|new Random\("));
    }

    private static void BoundsOf(MapData map, int zone, out Coord origin, out int width, out int height)
    {
        // link-map-terrain：占位与取景取整块出生棋盘（棋盘清单），不是出生区（核心区）。
        BoardPlate plate = map.BirthBoard(zone);
        origin = plate.Origin;
        width = plate.Width;
        height = plate.Height;
    }
}
