using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Config;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：builtin-board-maps / map-definition —— Requirement: 内置棋盘图。
/// 三张内置棋盘图是负责人 2026-10-06 选定的棋盘档生成图的别名：<c>siege-4p-board-v1</c> = <c>board:5</c>、<c>siege-3p-board-v1</c> = <c>board:55:p3</c>、
/// <c>siege-2p-board-v1</c> = <c>board:23:p2</c>。走批量入口与 <c>map</c> 子命令的两条会改控制台输出与当前目录的 <c>maps/</c>，所以与其他重定向控制台的类串行。
/// </summary>
[Collection(ConsoleRedirect.Collection)]
public class 内置棋盘图Tests
{
    /// <summary>
    /// 三张内置棋盘图的导出摘要黄金值（<see cref="MapFile.Digest"/>，含内置名）。任何一项变了，说明内置图内容变了：
    /// 内置名 MUST 升号（<c>-v2</c>），不得改这里的值了事（boundaries.md「内置图内容一变，标识必须递增」）。
    /// </summary>
    private static readonly (string Id, int Players, string SourceId, string Digest)[] Golden =
    [
        ("siege-4p-board-v1", 4, "board:5", "6392B301C7B1C27D5250A62F9ED2EF5590C50383C26F00FAFA20F04A1229198C"),
        ("siege-3p-board-v1", 3, "board:55:p3", "60F53B80FB8EE0AECD5CE8A5266DCA6F53407802E7C2E92B77728CAAA0D1431A"),
        ("siege-2p-board-v1", 2, "board:23:p2", "7304388E961AA8B717CC24EE3CA7A0AB9DA982A69FC44BCFC2836C0969F4FCA2"),
    ];

    public static IEnumerable<object[]> Ids => Golden.Select(g => new object[] { g.Id });

    [Fact]
    public void 缺省地图()
    {
        // 规格 Scenario：不带地图选项启动批量跑局 → 加载 siege-4p-board-v1，对局日志首部记录的地图标识为 siege-4p-board-v1。
        // 变异 B-C1：MapCatalog.DefaultId 改回 siege-4p-base-v5 → 全量新增红 7（含本测试）。
        Assert.Equal("siege-4p-board-v1", MapCatalog.DefaultId);
        Assert.Equal("siege-4p-board-v1", MapCatalog.Resolve(null).Id);
        Assert.Equal("siege-4p-board-v1", new RunConfig().MapId);

        string outDir = Path.Combine(SimFixtures.TempDir("builtin-board-default"), "out");
        TextWriter saved = Console.Out;
        int code;
        try
        {
            Console.SetOut(new StringWriter());
            code = Siege.Sim.Program.Execute(
                ["run", "--out", outDir, "--count", "1", "--seed", "1", "--turn-limit", "4", "--serial"],
                () => throw new InvalidOperationException("本测试不应随机取地图种子。"));
        }
        finally
        {
            Console.SetOut(saved);
        }

        Assert.Equal(0, code);
        RunConfig recorded = RunConfig.FromJson(File.ReadAllText(Path.Combine(outDir, "config.json")));
        MatchLog log = MatchLog.Parse(File.ReadAllText(Directory.GetFiles(outDir, "match-*.jsonl").Single()));
        Assert.Equal("siege-4p-board-v1", recorded.MapId);
        Assert.Equal("siege-4p-board-v1", log.Header.MapId);
        Assert.Equal(MapFile.Digest(MapCatalog.Resolve("siege-4p-board-v1")), log.Header.MapDigest);
        Assert.Equal(4, log.Header.Players.Count);
        Assert.True(log.Turns.Count >= 4, $"只跑了 {log.Turns.Count} 个小回合");   // 样本口径：对局确实开始了
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void 内置棋盘图通过校验(string id)
    {
        // 规格 Scenario：三张都是棋盘档、都通过棋盘档静态校验，人数上限依次为 4、3、2。
        // 变异 B-C2：别名构造不改写标识（Id 留作生成图标识）→ 本测试与「别名与生成图同内容」「内容不变」各 3 条共红（全量新增红 15）。
        (_, int players, _, _) = Golden.Single(g => g.Id == id);
        Assert.Contains(id, MapCatalog.BuiltinIds);
        MapData map = MapCatalog.Resolve(id);

        Assert.Equal(id, map.Id);
        Assert.Equal(MapProfile.Board, map.Profile);
        Assert.Equal(players, map.MaxPlayers);
        Assert.Equal(players + 1, map.BirthZones.Length);
        Assert.Equal(players + 1, map.Boards.Count(b => b.Kind == BoardPlateKind.Birth));
        MapValidationResult result = MapValidator.Validate(map);
        Assert.True(result.IsValid, result.ToString());
    }

    [Fact]
    public void 内置棋盘图登记的生成图标识与显示名()
    {
        // 别名对应的生成图标识即负责人选定的种子（子任务 prd 段 B），且是规范写法（能原样当 --map 用、与生成图的 Id 相同）。
        // 显示名里的人数与棋盘数与生成图标识一致（显示名是字面量，不得与标识脱节）。
        // 变异 B-C3：显示名把"三人"写成"四人" → 全量只红本测试 1 条。
        Assert.Equal(Golden.Select(g => (g.Id, g.SourceId)), MapCatalog.BuiltinBoards.Select(b => (b.Id, b.SourceId)));
        string[] digits = ["零", "一", "双", "三", "四"];
        foreach (BuiltinBoardAlias alias in MapCatalog.BuiltinBoards)
        {
            Assert.Equal(alias.SourceId, BoardMapId.Normalize(alias.SourceId));
            BoardMapParameters parameters = BoardMapId.Parse(alias.SourceId).Parameters;
            Assert.Equal($"{digits[parameters.Players]}人棋盘图（{parameters.BoardCount} 块）", alias.Title);
            Assert.Equal(alias.Title, Assert.Single(MapCatalog.BuiltinMaps, m => m.Id == alias.Id).Title);
            Assert.Contains($"-{parameters.Players}p-", alias.Id, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void 别名与生成图同内容(string id)
    {
        // 规格 Scenario：除地图标识外，尺寸、棋盘清单、出生区、信物格与全部格子逐项相同。
        // 字段级投影（ImmutableArray / 集合不能直接 Assert.Equal，testing.md）+ 改回同一标识后导出文本逐字节相同，两条腿。
        // 变异 B-C4：4 人别名构造改为生成 :n 多一块的图（board:5:n8）→ 本测试与「内容不变」的 4 人一条红（全量新增红 4）。
        string source = Golden.Single(g => g.Id == id).SourceId;
        MapData alias = MapCatalog.Resolve(id);
        MapData generated = MapCatalog.Resolve(source);

        Assert.Equal(id, alias.Id);
        Assert.Equal(source, generated.Id);
        Assert.Equal((generated.Width, generated.Height, generated.MaxPlayers, generated.Profile), (alias.Width, alias.Height, alias.MaxPlayers, alias.Profile));
        Assert.Equal(Plates(generated), Plates(alias));
        Assert.Equal(Zones(generated), Zones(alias));
        Assert.Equal(Relics(generated), Relics(alias));
        Assert.Equal(Cells(generated), Cells(alias));
        Assert.Equal(generated.CentralEntrance, alias.CentralEntrance);
        Assert.Equal(MapFile.ToJson(generated), MapFile.ToJson(alias with { Id = source }));

        // 标识进摘要：别名与生成图内容相同、摘要不同（日志 / 存档里两者可分辨）。
        Assert.NotEqual(MapFile.Digest(generated), MapFile.Digest(alias));
        Assert.True(alias.Boards.Length >= 4 && alias.PlayableCount > 0, "样本口径：棋盘清单为空或没有可落子格。");

        static string Plates(MapData m) => string.Join(";", m.Boards.Select(b => $"{b.Kind}@{b.Origin}:{b.Width}x{b.Height}"));
        static string Zones(MapData m) => string.Join(";", m.BirthZones.Select(z => string.Join(",", z.Order())));
        static string Relics(MapData m) => string.Join(";", m.RelicCells.OrderBy(r => r.Key).Select(r => $"{r.Key}:{r.Value.Zone}/{r.Value.Budget}"));
        static string Cells(MapData m) => string.Join(";", m.AllCoords().Select(c => $"{c}:{m.IsPlayable(c)}/{m.HeightAt(c)}/{m.SurfaceAt(c)}"));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void 内容不变(string id)
    {
        // 规格 Scenario：导出三张内置棋盘图 → 各自的导出摘要与登记的黄金值相同。黄金值在本 change 段 C 取下（生成器为段 A 之后的版本）。
        // 变异 B-C5：登记表里 board:5 改成 board:6 → 本测试 4 人一条红（全量新增红 5）；生成器任何改动同理：别名内容变了就该升号。
        Assert.Equal(Golden.Single(g => g.Id == id).Digest, MapFile.Digest(MapCatalog.Resolve(id)));
    }

    [Fact]
    public void 内置棋盘图不登记地图专属AI权重()
    {
        // 规格（Requirement 正文）：内置棋盘图 MUST NOT 登记地图专属 AI 权重，在其上运行的 AI 是未校准状态，取默认表。
        // 2 人内置棋盘图与已登记覆盖的 siege-2p-base-v1 人数相同，最容易被顺手照抄一条覆盖。
        // 变异 C-K1（check 实跑）：MapOverrides 加一行 siege-2p-board-v1 → 全量新增红 4（本测试 + 覆盖表钉值的三条：覆盖表被改动、覆盖只作用于登记的地图、两人图覆盖表的校准记录随值一起更新）。
        Assert.Contains(TwoPlayerBaseMap.Id, Siege.Core.Ai.EvaluationWeights.MapOverrides.Keys);   // 反面：登记表本身非空、键确是地图标识
        foreach (BuiltinBoardAlias alias in MapCatalog.BuiltinBoards)
        {
            Assert.False(Siege.Core.Ai.EvaluationWeights.MapOverrides.ContainsKey(alias.Id), $"{alias.Id} 不应登记覆盖。");
            Assert.Same(Siege.Core.Ai.EvaluationWeights.Default, Siege.Core.Ai.EvaluationWeights.ForMapId(alias.Id));
            Assert.Null(Siege.Core.Ai.EvaluationWeights.MapOverrideCalibrationOf(alias.Id));
        }
    }

    [Fact]
    public void 导出文件名合法()
    {
        // 规格 Scenario：以 map --map siege-4p-board-v1 导出 → 文件名为 siege-4p-board-v1.json，不含冒号。
        // board: 标识本身含冒号（Windows 文件名非法），按它请求只打印、不往 maps/ 写；要落盘用内置名或 --out。
        // 变异 B-C6a：Program.ExportMap 的导出文件名改用别名的生成图标识 → 只红本测试；B-C6b：按 board: 标识请求也导出 → 红 2（含本测试）。
        CleanExports();
        try
        {
            (int code, string text) = RunMap("map", "--map", "siege-4p-board-v1");
            Assert.Equal(0, code);
            string expected = Path.Combine("maps", "siege-4p-board-v1.json");
            Assert.Contains($"已导出 {expected}", text, StringComparison.Ordinal);
            Assert.Equal(["siege-4p-board-v1.json"], Directory.GetFiles("maps").Select(Path.GetFileName));
            Assert.Equal(MapFile.ToJson(MapCatalog.Resolve("siege-4p-board-v1")), File.ReadAllText(expected));

            // 反面：按 board: 标识请求同一张图，只打印、不写任何文件（更不会写出带冒号的文件名）。
            CleanExports();
            (int boardCode, string boardText) = RunMap("map", "--map", "board:5");
            Assert.Equal(0, boardCode);
            Assert.Contains("地图 board:5  39×41", boardText, StringComparison.Ordinal);
            Assert.DoesNotContain("已导出", boardText, StringComparison.Ordinal);
            Assert.False(Directory.Exists("maps") && Directory.EnumerateFileSystemEntries("maps").Any(), "按 board: 标识请求时写出了文件。");
        }
        finally
        {
            CleanExports();
        }
    }

    private static (int Code, string Out) RunMap(params string[] args)
    {
        var output = new StringWriter();
        TextWriter saved = Console.Out;
        try
        {
            Console.SetOut(output);
            return (Siege.Sim.Program.Execute(args, () => throw new InvalidOperationException("本测试不应随机取地图种子。")), output.ToString());
        }
        finally
        {
            Console.SetOut(saved);
        }
    }

    /// <summary>只清测试进程工作目录（测试输出目录）里的 maps/；工作目录若是仓库根（有 siege.sln）绝不碰权威文件。同 地图子命令Tests。</summary>
    private static void CleanExports()
    {
        if (Directory.Exists("maps") && !File.Exists("siege.sln"))
        {
            Directory.Delete("maps", recursive: true);
        }
    }
}
