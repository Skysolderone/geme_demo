using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Play;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/simulation-harness —— Requirement: 各入口支持生成图（tasks 2.1 / 2.2 / 2.6）。
/// retire-legacy-maps 段 B：gen: 生成图（边疆档生成器）删除，只测 gen: 的五条随之删除——目录按 gen: 解析、裸 gen 不进内核与非法标识、
/// 入口把裸 gen 落成完整标识、以裸 gen 批量跑局、终端版玩 gen: 图；裸 board 的同类断言在 棋盘档生成图标识Tests，
/// 已删除标识的报错在 已删除地图明确报错Tests。其余四条（日志首部、map 子命令导出与权威文件守护、生成器只经目录调用）改到 board: 图上。
/// 段 C：内置图不再有 maps/ 权威文件，map 子命令只在给 --out 时落盘；"--out 不得指向权威文件"的守护删除，原测试改为"maps/ 下的同名文件不会顶替内置图"。
/// </summary>
[Collection(ConsoleRedirect.Collection)]
public class 各入口支持生成图Tests
{
    [Fact]
    public void 终端版玩棋盘生成图_插旗提示列出全部出生区_地图标识与对局种子分开显示()
    {
        // retire-legacy-maps 段 B 检查补：原「终端版玩生成图…」在 gen:12345:p8 上钉住 PlayCommand 的"随机生成图"提示行与对局种子分行显示，
        // 随 gen: 删除后该提示的判定改为 BoardMapId.IsBoardMap，却没有任何测试走到它。这里改在 board: 图上钉住同一组行为，
        // 并以内置棋盘图作反面（内置名不是 board: 标识，不带"随机生成"提示）。
        // 变异（检查实跑全量）：PlayCommand 的 IsBoardMap 判定改为恒假 → 只红本测试。
        MapData map = MapCatalog.Resolve("board:12345:n8");
        Assert.Equal(5, map.BirthZones.Length);   // 样本口径：4 人图出生棋盘 = 人数 + 1
        var output = new StringWriter();

        int exit = PlayCommand.Run(7, 4, 1, AiDifficulty.Easy, new StringReader("5\n"), output, map);

        string text = output.ToString();
        Assert.Equal(0, exit);
        Assert.Contains("选择你的出生区（1–5）", text, StringComparison.Ordinal);
        Assert.Contains("出生区锁定：玩家1(你)→5号区", text, StringComparison.Ordinal);
        string mapLine = Assert.Single(text.Split('\n'), l => l.StartsWith($"地图 board:12345:n8（{map.Width}×{map.Height}，5 个出生区）", StringComparison.Ordinal));
        Assert.Contains("随机生成的棋盘图，用 --map board:12345:n8 可再得到同一张图", mapLine, StringComparison.Ordinal);
        Assert.Contains(text.Split('\n'), l => l.StartsWith("种子 7（", StringComparison.Ordinal));   // 对局种子另起一行，不与地图种子合并

        var builtin = new StringWriter();
        Assert.Equal(0, PlayCommand.Run(7, 4, 1, AiDifficulty.Easy, new StringReader("5\n"), builtin, MapCatalog.Resolve(MapCatalog.DefaultId)));
        string builtinLine = Assert.Single(builtin.ToString().Split('\n'), l => l.StartsWith($"地图 {MapCatalog.DefaultId}（", StringComparison.Ordinal));
        Assert.DoesNotContain("随机生成", builtinLine, StringComparison.Ordinal);
    }

    [Fact]
    public void 在生成图上跑局_日志首部的地图标识是完整标识_对局种子另记()
    {
        // retire-legacy-maps 段 B：样本由 gen:12345:p6（规范化为 gen:12345，6 区）改为 board:12345:n7（4 人缺省棋盘数 7，规范化为 board:12345，5 区）。
        MatchLog log = MatchSession.Create(SimFixtures.Config(turnLimit: 4) with { MapId = "board:12345:n7" }, seed: 7).Run();

        Assert.False(log.IsFailed, log.Failure?.Message);
        Assert.Equal("board:12345", log.Header.MapId);                   // 规范化标识
        Assert.Equal("board:12345:n7", log.Header.Config.MapId);         // 配置原样留痕
        Assert.Equal(new Siege.Core.Determinism.GameSeed(7).ToString(), log.Header.Seed);
        Assert.Equal(5, log.Header.ZoneCount);
        Assert.DoesNotContain("12345", log.Header.Seed, StringComparison.Ordinal);
    }

    [Fact]
    public void 地图子命令打印生成图_不导出到权威目录_给out才导出且按路径加载逐项相同()
    {
        // 规格 Scenario「导出再加载」。变异 MB-4：map 子命令对生成图也按 map.Id 往 maps/ 写 → 本测试红（maps/ 下出现文件或抛异常）。
        // retire-legacy-maps 段 B：样本由 gen:12345 改为 board:12345；仓库里的 maps/ 目录随旧图删除，"不往仓库 maps/ 写"改为"仓库里不出现 maps/"。
        string repoMaps = Path.Combine(TestMaps.RepoRoot(), "maps");
        Assert.False(Directory.Exists(repoMaps));
        MapData expected = BoardMapGenerator.Generate("board:12345");
        (int code, string text, string err) = RunMain("map", "--map", "board:12345");

        Assert.True(code == 0, err);
        string[] lines = [.. text.Split('\n').Select(l => l.TrimEnd('\r', ' '))];
        Assert.Contains($"地图 board:12345  {expected.Width}×{expected.Height}", lines);
        Assert.Contains("地图校验通过。", lines);
        Assert.Contains(lines, l => l.StartsWith("出生区 5 个", StringComparison.Ordinal));
        Assert.Equal(expected.Height, lines.Count(l => l.Length > 4 && char.IsDigit(l[2]) && l[3] == ' '));
        Assert.DoesNotContain("已导出", text, StringComparison.Ordinal);
        Assert.False(Directory.Exists("maps") && Directory.GetFiles("maps").Length > 0, "查看生成图不得往 maps/ 写文件。");
        Assert.False(Directory.Exists(repoMaps));

        string dir = SimFixtures.TempDir("map-subcommand-board");
        string exported = Path.Combine(dir, "sub", "my-board.json");
        (int exportCode, string exportText, string exportErr) = RunMain("map", "--map", "board:12345", "--out", exported);

        Assert.True(exportCode == 0, exportErr);
        Assert.Contains($"已导出 {exported}", exportText, StringComparison.Ordinal);
        MapData loaded = MapCatalog.Resolve(exported);
        Assert.Equal("board:12345", loaded.Id);
        Assert.Equal(MapFile.ToJson(expected), MapFile.ToJson(loaded));
        Assert.Equal(MapFile.Digest(MapCatalog.Resolve("board:12345")), MapFile.Digest(loaded));
        GameBoard.Load(loaded);   // 作为普通地图文件过同一套校验

        // 用导出的文件跑的局，回放时按文件重建，摘要对得上。
        MatchLog log = MatchSession.Create(SimFixtures.Config(turnLimit: 4) with { MapId = exported }, seed: 3).Run();
        Assert.Equal("board:12345", log.Header.MapId);
        Assert.True(Replayer.Replay(MatchLog.Parse(log.DeterministicText())).Identical);
    }

    [Fact]
    public void 地图子命令的out可写任意路径_maps目录下的同名文件不顶替内置图()
    {
        // 原「地图子命令的out不得指向内置图的权威文件_同目录下的新文件名允许」（段 B 检查，负责人裁决 5：防手滑覆盖 maps/ 里的内置图导出文件）。
        // retire-legacy-maps 段 C：内置图全部由棋盘生成器产出、内容由导出摘要黄金值守住，不再有 maps/<内置名>.json 权威文件；
        // 段 B 又删除了 MapCatalog 的 maps/<标识>.json 隐式回落——那份文件写成什么都顶替不了内置图，守护失去对象，随之删除。
        // 本测试改为钉住这两点的组合：--out 指向 maps/<内置名>.json 照常导出；工作目录 maps/ 下放一份被改过的同名文件，按内置名解析出的仍是生成器的那张图。
        // 变异 MC-M1（段 C 实跑）：MapCatalog.Resolve 恢复"maps/<标识>.json 存在就读它"的回落（放在内置表之前）→ 本测试红（连同 已删除地图明确报错 一条共红 2）。
        // 只在临时目录与测试工作目录里试，绝不碰仓库根（仓库里没有 maps/，由 各入口按地图标识选图Tests 钉住）。
        string root = SimFixtures.TempDir("map-out-any");
        string target = Path.Combine(root, "maps", SimFixtures.Board2 + ".json");
        (int code, string text, string err) = RunMain("map", "--map", "board:12345", "--out", target);
        Assert.True(code == 0, err);
        Assert.Contains($"已导出 {target}", text, StringComparison.Ordinal);
        Assert.Equal(MapFile.Digest(MapCatalog.Resolve("board:12345")), MapFile.Digest(MapCatalog.Resolve(target)));

        string builtinDigest = MapFile.Digest(MapCatalog.Resolve(SimFixtures.Board2));
        string relative = Path.Combine("maps", SimFixtures.Board2 + ".json");
        bool hadMaps = Directory.Exists("maps");
        Assert.False(File.Exists("siege.sln"), "测试工作目录不应是仓库根。");
        try
        {
            Directory.CreateDirectory("maps");
            File.WriteAllText(relative, File.ReadAllText(target));   // 与内置图同名、内容却是 board:12345
            Assert.NotEqual(builtinDigest, MapFile.Digest(MapCatalog.Resolve(relative)));   // 样本口径：那份文件确实是另一张图

            MapData resolved = MapCatalog.Resolve(SimFixtures.Board2);

            Assert.Equal(SimFixtures.Board2, resolved.Id);
            Assert.Equal(builtinDigest, MapFile.Digest(resolved));
        }
        finally
        {
            if (File.Exists(relative))
            {
                File.Delete(relative);
            }

            if (!hadMaps && Directory.Exists("maps") && !Directory.EnumerateFileSystemEntries("maps").Any())
            {
                Directory.Delete("maps");
            }
        }
    }

    [Fact]
    public void 生成器只经目录调用_裸board只在入口最外层取种子()
    {
        // 守门（源码扫描；src/godot 不在解决方案里，反射看不见）：
        //   ① 三个入口与表现层不直接调生成器——生成图与内置图走同一份"标识 → 地图"解析；
        //   ② 识别裸 board 的地方只有：目录（拒绝它）、批量 / 终端入口（Program）、图形版入口（GameRoot）。
        //      取时钟落种子的只有后两处（各一次）：Program 里是缺省的取种子来源（可注入，测试给固定值），GameRoot 里直接读。
        // 原名「生成器只经目录调用_裸gen只在入口最外层取种子」：retire-legacy-maps 段 B 删除 gen:（边疆档生成器 FrontierMapGenerator、GeneratedMapId），
        // 只针对它们的断言删除，识别裸标识的位置收窄为棋盘图的三处；折叠函数 FriendlySeed 由 GeneratedMapId 迁到 BoardMapId。
        // 变异 MB-14：在 BatchRunner 里直接调 BoardMapGenerator.Generate → 本测试红；MB-15：在 PlayCommand 里加一处 IsBareRequest → 本测试红。
        string src = Path.Combine(TestMaps.RepoRoot(), "src");
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

        string[] boardCallers = [.. files.Where(f => f.Text.Contains("BoardMapGenerator.", StringComparison.Ordinal)).Select(f => f.Path).Order(StringComparer.Ordinal)];
        Assert.All(boardCallers, p => Assert.StartsWith("Siege.Core/Board/Maps/", p, StringComparison.Ordinal));
        Assert.Contains("Siege.Core/Board/Maps/MapCatalog.cs", boardCallers);   // 反面：目录确实在调

        string[] bareReaders = [.. files.Where(f => f.Text.Contains("IsBareRequest(", StringComparison.Ordinal)).Select(f => f.Path).Order(StringComparer.Ordinal)];
        // 标识的唯一实现（定义处）、目录（拒绝它）、批量配置校验（每局换图时拒绝它）、批量 / 终端入口、图形版入口。
        Assert.Equal(
            ["Siege.Core/Board/Maps/BoardMapParameters.cs", "Siege.Core/Board/Maps/MapCatalog.cs", "Siege.Sim/Config/RunConfig.cs", "Siege.Sim/Program.cs", "godot/scripts/GameRoot.cs"],
            bareReaders);
        string[] bareBoardReaders = [.. files.Where(f => f.Text.Contains("BoardMapId.IsBareRequest(", StringComparison.Ordinal)).Select(f => f.Path).Order(StringComparer.Ordinal)];
        Assert.Equal(bareReaders.Skip(1), bareBoardReaders);   // 定义处之外，识别裸标识的只剩棋盘图一族

        string program = files.Single(f => f.Path == "Siege.Sim/Program.cs").Text;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"Stopwatch\.GetTimestamp\(\)"));          // 缺省来源 ClockMapSeed
        // 段 C 检查（裁决 4）：入口取到的时间戳都先经同一个折叠函数折成九位以内的短种子。
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"=> BoardMapId\.FriendlySeed\(\(ulong\)System\.Diagnostics\.Stopwatch\.GetTimestamp\(\)\);"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"BoardMapId\.Format\(mapSeedSource\(\)"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"mapSeedSource\(\)"));   // 只有裸 board 取一次，别无他处
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"=> Execute\(args, ClockMapSeed\)"));
        string gameRoot = files.Single(f => f.Path == "godot/scripts/GameRoot.cs").Text;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(gameRoot, @"BoardMapId\.Format\(BoardMapId\.FriendlySeed\(\(ulong\)Stopwatch\.GetTimestamp\(\)\)"));

        // 已删除的边疆档生成器与 gen: 标识类型在源码里一处都不剩（只许出现在说明删除的注释与报错清单里，不许出现调用）。
        Assert.DoesNotContain(files, f => System.Text.RegularExpressions.Regex.IsMatch(f.Text, @"\b(FrontierMapGenerator|GeneratedMapId|MapGenParameters|FrontierSurfaces|FrontierMapLayout)\."));

        // 规则内核永不读时钟（裸 board 的种子不可能在 Core 里取）。
        Assert.DoesNotContain(files, f => f.Path.StartsWith("Siege.Core/", StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(f.Text, @"Stopwatch\.|DateTime\.(Utc)?Now|Environment\.TickCount|Random\.Shared|new Random\("));
    }

    private static (int Code, string Out, string Error) RunMain(params string[] args) =>
        RunMain(() => throw new InvalidOperationException("本测试不应随机取地图种子。"), args);

    private static (int Code, string Out, string Error) RunMain(Func<ulong> mapSeedSource, params string[] args)
    {
        var output = new StringWriter();
        var err = new StringWriter();
        TextWriter savedOut = Console.Out;
        TextWriter savedErr = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(err);
            return (Siege.Sim.Program.Execute(args, mapSeedSource), output.ToString(), err.ToString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }
}
