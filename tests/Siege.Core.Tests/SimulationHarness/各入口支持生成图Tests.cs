using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Play;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/simulation-harness —— Requirement: 各入口支持生成图（tasks 2.1 / 2.2 / 2.6）；
/// map-generation —— Requirement: 生成图的地图标识（裸 gen 不入记录、非法标识）。
/// </summary>
[Collection(ConsoleRedirect.Collection)]
public class 各入口支持生成图Tests
{
    [Fact]
    public void 目录按生成图标识解析出的地图与直接调生成器逐项相同()
    {
        // 变异 MB-1：MapCatalog.Resolve 去掉 gen: 分支 → 落到文件路径分支报"找不到地图"，本测试红。
        Assert.Equal(MapFile.ToJson(FrontierMapGenerator.Generate(12345)), MapFile.ToJson(MapCatalog.Resolve("gen:12345")));
        Assert.Equal(
            MapFile.ToJson(FrontierMapGenerator.Generate(987654321, new MapGenParameters { PlatformCount = 7 })),
            MapFile.ToJson(MapCatalog.Resolve(" gen:987654321:p7 ")));

        // 标识规范化：gen:42:p6 解析出的地图标识是 gen:42；内置图不受影响，也不出现在"内置标识"清单里。
        Assert.Equal("gen:42", MapCatalog.Resolve("gen:42:p6").Id);
        Assert.Equal("gen:987654321:p7", MapCatalog.Resolve("gen:987654321:p7").Id);
        Assert.DoesNotContain(MapCatalog.BuiltinIds, GeneratedMapId.IsGenerated);
    }

    [Fact]
    public void 裸gen不进规则内核_非法标识报错并说明格式与范围()
    {
        // 规则内核不读时钟：裸 gen 到了目录这里直接报错（取种子是入口最外层的事）。
        // 变异 MB-2：MapCatalog 遇到裸 gen 时自己取一个固定种子生成 → 本测试红。
        FormatException bare = Assert.Throws<FormatException>(() => MapCatalog.Resolve("gen"));
        Assert.Contains("gen:12345", bare.Message, StringComparison.Ordinal);

        FormatException notNumber = Assert.Throws<FormatException>(() => MapCatalog.Resolve("gen:abc"));
        Assert.Contains("gen:<地图种子>[:p<平台数>]", notNumber.Message, StringComparison.Ordinal);
        FormatException outOfRange = Assert.Throws<FormatException>(() => MapCatalog.Resolve("gen:1:p99"));
        Assert.Contains("5–8", outOfRange.Message, StringComparison.Ordinal);

        // 入口：报错退出、不回落到缺省地图，错误信息说明格式与范围。
        (int code, _, string err) = RunMain("map", "--map", "gen:1:p99");
        Assert.Equal(1, code);
        Assert.Contains("5–8", err, StringComparison.Ordinal);
        (int playCode, _, string playErr) = RunMain("play", "--map", "gen:abc");
        Assert.Equal(1, playCode);
        Assert.Contains("gen:<地图种子>[:p<平台数>]", playErr, StringComparison.Ordinal);

        // 未知标识的可用清单里说明 gen:<种子> 这一写法。
        FileNotFoundException unknown = Assert.Throws<FileNotFoundException>(() => MapCatalog.Resolve("no-such-map"));
        Assert.Contains("gen:<地图种子>", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 入口把裸gen落成带种子的完整标识并打印_其余标识原样()
    {
        // 变异 MB-3：MaterializeMapRequest 原样返回裸 gen → 本测试红（run 的配置记录里也会出现裸 gen，见下一条）。
        // 取种子的来源是注入的（段 B 检查，裁决 6）：测试给固定值，不依赖墙钟。变异 CB-4：忽略注入的来源、自己读时钟 → 本测试红。
        var output = new StringWriter();
        string id = Siege.Sim.Program.MaterializeMapRequest(" gen ", output, () => 987654321UL)!;

        Assert.Equal("gen:987654321", id);
        Assert.Contains("--map gen:987654321", output.ToString(), StringComparison.Ordinal);

        // 其余标识原样返回、不打印，也不去取种子。
        var silent = new StringWriter();
        static ulong Never() => throw new InvalidOperationException("不是裸 gen，不应取地图种子。");
        Assert.Equal("gen:12345:p7", Siege.Sim.Program.MaterializeMapRequest("gen:12345:p7", silent, Never));
        Assert.Equal("siege-frontier-v1", Siege.Sim.Program.MaterializeMapRequest("siege-frontier-v1", silent, Never));
        Assert.Null(Siege.Sim.Program.MaterializeMapRequest(null, silent, Never));
        Assert.Equal(string.Empty, silent.ToString());

        // map 子命令走同一处：注入的种子出现在打印的完整标识里，随即按它出图。
        (int code, string text, string err) = RunMain(() => 12345UL, "map", "--map", "gen");
        Assert.True(code == 0, err);
        Assert.Contains("本次地图为 gen:12345（", text, StringComparison.Ordinal);
        Assert.Contains("地图 gen:12345  25×30", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 以裸gen批量跑局_配置记录与日志首部都是带种子的完整标识()
    {
        // 规格 Scenario「裸 gen 不入记录」。走真实入口 run --map gen；"随机取的地图种子"由测试注入固定值（不依赖墙钟）。
        string dir = SimFixtures.TempDir("run-bare-gen");
        (int code, string text, string err) = RunMain(() => 12345UL, "run", "--out", dir, "--map", "gen", "--count", "1", "--difficulty", "Easy", "--max-rounds", "1", "--serial", "--sample-permille", "0");

        Assert.True(code == 0, err);
        AssertRecordsOnly(dir, "gen:12345", perMatch: false);
        Assert.Contains("--map gen:12345", text, StringComparison.Ordinal);   // 打印给用户

        // 反例一：裸 gen 写在配置文件里（不经 --map）→ 同样在入口落成完整标识，落盘的 config.json 不是原样抄回去的。
        string configFile = Path.Combine(SimFixtures.TempDir("run-bare-gen-config"), "in.json");
        File.WriteAllText(configFile, (SimFixtures.Config(count: 1, maxRounds: 1) with { MapId = "gen" }).ToJson());
        Assert.Contains("\"gen\"", File.ReadAllText(configFile), StringComparison.Ordinal);
        string fromFile = Path.Combine(Path.GetDirectoryName(configFile)!, "out");
        (int fileCode, _, string fileErr) = RunMain(() => 12345UL, "run", "--out", fromFile, "--config", configFile, "--serial");
        Assert.True(fileCode == 0, fileErr);
        AssertRecordsOnly(fromFile, "gen:12345", perMatch: false);

        // 反例二：裸 gen + 每局换图（命令行）→ 起始地图种子取自同一来源，批次配置记 gen:<起始>，各局首部依次递增。
        string rotating = SimFixtures.TempDir("run-bare-gen-rotate");
        (int rotateCode, string rotateText, string rotateErr) = RunMain(() => 100UL, "run", "--out", rotating, "--map", "gen", "--map-per-match", "--count", "2", "--difficulty", "Easy", "--max-rounds", "1", "--serial", "--sample-permille", "0");
        Assert.True(rotateCode == 0, rotateErr);
        AssertRecordsOnly(rotating, "gen:100", perMatch: true);
        Assert.Contains("每局换图 gen:100..gen:101", rotateText, StringComparison.Ordinal);

        // 反例三：绕过入口、把裸 gen 直接交给批量 / 会话 → 规则内核拒绝，且不留半份输出。
        string direct = Path.Combine(SimFixtures.TempDir("run-bare-gen-direct"), "out");
        Assert.Throws<FormatException>(() => BatchRunner.ExecuteToDirectory(SimFixtures.Config(count: 1, maxRounds: 1) with { MapId = "gen" }, direct, parallelism: 1));
        Assert.False(Directory.Exists(direct));
        Assert.Throws<FormatException>(() => MatchSession.Create(SimFixtures.Config(maxRounds: 1) with { MapId = " gen " }, seed: 1));
    }

    /// <summary>输出目录里的全部记录（config.json、每局日志首部及其内嵌配置）只出现带种子的完整标识，不出现裸 gen。</summary>
    private static void AssertRecordsOnly(string dir, string startId, bool perMatch)
    {
        string configText = File.ReadAllText(Path.Combine(dir, "config.json"));
        Assert.DoesNotContain("\"gen\"", configText, StringComparison.Ordinal);
        Assert.Equal(startId, RunConfig.FromJson(configText).MapId);

        List<MatchLog> logs = MatchLog.ReadDirectory(dir);
        Assert.NotEmpty(logs);
        (ulong start, MapGenParameters parameters) = GeneratedMapId.Parse(startId);
        for (int i = 0; i < logs.Count; i++)
        {
            Assert.Equal(perMatch ? GeneratedMapId.Format(start + (ulong)i, parameters) : startId, logs[i].Header.MapId);
            Assert.Equal(startId, logs[i].Header.Config.MapId);
            Assert.DoesNotContain("\"gen\"", logs[i].DeterministicText().Split('\n')[0], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 终端版玩生成图_插旗提示列出全部平台_地图标识与对局种子分开显示()
    {
        // 规格 Scenario「终端版玩生成图」：gen:12345 有 6 个平台；gen:12345:p8 有 8 个。脚本只选区，随后输入耗尽退出。
        MapData map = MapCatalog.Resolve("gen:12345:p8");
        var output = new StringWriter();

        int exit = PlayCommand.Run(7, 4, 1, AiDifficulty.Easy, maxRounds: 3, new StringReader("8\n"), output, map);

        string text = output.ToString();
        Assert.Equal(0, exit);
        Assert.Contains("选择你的出生区（1–8）", text, StringComparison.Ordinal);
        Assert.Contains("出生区锁定：玩家1(你)→8号区", text, StringComparison.Ordinal);
        string mapLine = Assert.Single(text.Split('\n'), l => l.StartsWith("地图 gen:12345:p8（25×30，8 个出生区）", StringComparison.Ordinal));
        Assert.Contains("--map gen:12345:p8", mapLine, StringComparison.Ordinal);
        Assert.Contains(text.Split('\n'), l => l.StartsWith("种子 7（", StringComparison.Ordinal));   // 对局种子另起一行，不与地图种子合并
    }

    [Fact]
    public void 在生成图上跑局_日志首部的地图标识是完整标识_对局种子另记()
    {
        MatchLog log = MatchSession.Create(SimFixtures.Config(maxRounds: 1) with { MapId = "gen:12345:p6" }, seed: 7).Run();

        Assert.False(log.IsFailed, log.Failure?.Message);
        Assert.Equal("gen:12345", log.Header.MapId);                   // 规范化标识
        Assert.Equal("gen:12345:p6", log.Header.Config.MapId);         // 配置原样留痕
        Assert.Equal(new Siege.Core.Determinism.GameSeed(7).ToString(), log.Header.Seed);
        Assert.Equal(6, log.Header.ZoneCount);
        Assert.DoesNotContain("12345", log.Header.Seed, StringComparison.Ordinal);
    }

    [Fact]
    public void 地图子命令打印生成图_不导出到权威目录_给out才导出且按路径加载逐项相同()
    {
        // 规格 Scenario「导出再加载」。变异 MB-4：map 子命令对生成图也按 map.Id 往 maps/ 写 → 本测试红（maps/ 下出现文件或抛异常）。
        string repoMaps = Path.Combine(FrontierFixtures.RepoRoot(), "maps");
        string[] repoBefore = [.. Directory.GetFiles(repoMaps).Order()];
        (int code, string text, string err) = RunMain("map", "--map", "gen:12345");

        Assert.True(code == 0, err);
        string[] lines = [.. text.Split('\n').Select(l => l.TrimEnd('\r', ' '))];
        Assert.Contains("地图 gen:12345  25×30", lines);
        Assert.Contains("地图校验通过。", lines);
        Assert.Contains(lines, l => l.StartsWith("出生区 6 个", StringComparison.Ordinal));
        Assert.Equal(30, lines.Count(l => l.Length > 4 && char.IsDigit(l[2]) && l[3] == ' '));
        Assert.DoesNotContain("已导出", text, StringComparison.Ordinal);
        Assert.False(Directory.Exists("maps") && Directory.GetFiles("maps").Length > 0, "查看生成图不得往 maps/ 写文件。");
        Assert.Equal(repoBefore, Directory.GetFiles(repoMaps).Order());

        string dir = SimFixtures.TempDir("map-subcommand-gen");
        string exported = Path.Combine(dir, "sub", "my-gen.json");
        (int exportCode, string exportText, string exportErr) = RunMain("map", "--map", "gen:12345", "--out", exported);

        Assert.True(exportCode == 0, exportErr);
        Assert.Contains($"已导出 {exported}", exportText, StringComparison.Ordinal);
        MapData loaded = MapCatalog.Resolve(exported);
        Assert.Equal("gen:12345", loaded.Id);
        Assert.Equal(MapFile.ToJson(FrontierMapGenerator.Generate("gen:12345")), MapFile.ToJson(loaded));
        Assert.Equal(MapFile.Digest(MapCatalog.Resolve("gen:12345")), MapFile.Digest(loaded));
        GameBoard.Load(loaded);   // 作为普通地图文件过同一套校验

        // 用导出的文件跑的局，回放时按文件重建，摘要对得上。
        MatchLog log = MatchSession.Create(SimFixtures.Config(maxRounds: 1) with { MapId = exported }, seed: 3).Run();
        Assert.Equal("gen:12345", log.Header.MapId);
        Assert.True(Replayer.Replay(MatchLog.Parse(log.DeterministicText())).Identical);
    }

    [Fact]
    public void 地图子命令的out不得指向内置图的权威文件_同目录下的新文件名允许()
    {
        // 段 B 检查（负责人裁决 5）：防手滑 `map --map gen:12345 --out maps/siege-frontier-v1.json` 覆盖权威文件。
        // 只在临时目录与测试工作目录里试，绝不碰仓库的 maps/（变异下也不会写坏它）。变异 CB-5：去掉这道检查 → 本测试红。
        string root = SimFixtures.TempDir("map-out-guard");
        string mapsDir = Path.Combine(root, "maps");
        foreach (string builtin in MapCatalog.BuiltinIds)
        {
            string target = Path.Combine(mapsDir, builtin + ".json");
            (int code, string text, string err) = RunMain("map", "--map", "gen:12345", "--out", target);

            Assert.Equal(1, code);
            Assert.Contains("权威文件", err, StringComparison.Ordinal);
            Assert.Contains(builtin, err, StringComparison.Ordinal);
            Assert.Equal(string.Empty, text);               // 先于一切输出
            Assert.False(Directory.Exists(mapsDir));        // 什么都没写，连目录都没建
        }

        // 文件已存在、大小写不同（Windows 上是同一个文件）、相对路径：一律拒绝，原文件逐字节不变。
        Directory.CreateDirectory(Path.Combine(root, "Maps"));
        string existing = Path.Combine(root, "Maps", FrontierMapV1.Id.ToUpperInvariant() + ".JSON");
        File.WriteAllText(existing, "原样");
        // （请求的是生成图而不是内置图：内置图的导出另会往工作目录的 maps/ 写，变异下会留垃圾。）
        Assert.Equal(1, RunMain("map", "--map", "gen:12345", "--out", existing).Code);
        Assert.Equal("原样", File.ReadAllText(existing));

        string relative = Path.Combine("maps", FourPlayerBaseMap.Id + ".json");
        bool hadMaps = Directory.Exists("maps");
        try
        {
            Assert.Equal(1, RunMain("map", "--map", "gen:12345", "--out", relative).Code);
            Assert.False(File.Exists(relative));
        }
        finally
        {
            if (File.Exists(relative))
            {
                File.Delete(relative);
            }

            if (!hadMaps && Directory.Exists("maps"))
            {
                Directory.Delete("maps");
            }
        }

        // maps/ 下的新文件名允许；不在 maps/ 目录里的同名文件也允许（那不是权威文件）。
        string fresh = Path.Combine(mapsDir, "my-gen-12345.json");
        (int freshCode, _, string freshErr) = RunMain("map", "--map", "gen:12345", "--out", fresh);
        Assert.True(freshCode == 0, freshErr);
        Assert.Equal(MapFile.Digest(MapCatalog.Resolve("gen:12345")), MapFile.Digest(MapCatalog.Resolve(fresh)));
        string elsewhere = Path.Combine(root, "scratch", FrontierMapV1.Id + ".json");
        Assert.Equal(0, RunMain("map", "--map", "gen:12345", "--out", elsewhere).Code);
        Assert.True(File.Exists(elsewhere));
    }

    [Fact]
    public void 生成器只经目录调用_裸gen只在入口最外层取种子()
    {
        // 守门（源码扫描；src/godot 不在解决方案里，反射看不见）：
        //   ① 三个入口与表现层不直接调生成器——生成图与内置图走同一份"标识 → 地图"解析；
        //   ② 识别裸 gen 的地方只有：标识的唯一实现、目录（拒绝它）、批量配置校验（每局换图时拒绝它）、批量 / 终端入口（Program）、图形版入口（GameRoot）。
        //      取时钟落种子的只有最后两处（各一次）：Program 里是缺省的取种子来源（可注入，测试给固定值），GameRoot 里直接读。
        // 变异 MB-14：在 BatchRunner 里直接调 FrontierMapGenerator.Generate → 本测试红；MB-15：在 PlayCommand 里加一处 IsBareRequest → 本测试红。
        string src = Path.Combine(FrontierFixtures.RepoRoot(), "src");
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

        string[] generatorCallers = [.. files.Where(f => f.Text.Contains("FrontierMapGenerator.", StringComparison.Ordinal)).Select(f => f.Path).Order(StringComparer.Ordinal)];
        Assert.All(generatorCallers, p => Assert.StartsWith("Siege.Core/Board/Maps/", p, StringComparison.Ordinal));
        Assert.Contains("Siege.Core/Board/Maps/MapCatalog.cs", generatorCallers);   // 反面：目录确实在调

        string[] bareReaders = [.. files.Where(f => f.Text.Contains("IsBareRequest(", StringComparison.Ordinal)).Select(f => f.Path).Order(StringComparer.Ordinal)];
        Assert.Equal(
            ["Siege.Core/Board/Maps/MapCatalog.cs", "Siege.Core/Board/Maps/MapGenParameters.cs", "Siege.Sim/Config/RunConfig.cs", "Siege.Sim/Program.cs", "godot/scripts/GameRoot.cs"],
            bareReaders);
        string program = files.Single(f => f.Path == "Siege.Sim/Program.cs").Text;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"Stopwatch\.GetTimestamp\(\)"));          // 缺省来源 ClockMapSeed
        // 段 C 检查（裁决 4）：三个入口取到的时间戳都先经同一个折叠函数折成九位以内的短种子（本断言与下面图形版那条随之更新）。
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"=> GeneratedMapId\.FriendlySeed\(\(ulong\)System\.Diagnostics\.Stopwatch\.GetTimestamp\(\)\);"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"GeneratedMapId\.Format\(mapSeedSource\(\)"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"=> Execute\(args, ClockMapSeed\)"));
        string gameRoot = files.Single(f => f.Path == "godot/scripts/GameRoot.cs").Text;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(gameRoot, @"GeneratedMapId\.Format\(GeneratedMapId\.FriendlySeed\(\(ulong\)Stopwatch\.GetTimestamp\(\)\)"));

        // 规则内核永不读时钟（裸 gen 的种子不可能在 Core 里取）。
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
