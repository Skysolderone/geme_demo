using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>规格：openspec/changes/board-map/specs/map-generation —— Requirement: 棋盘档生成图标识</summary>
/// <remarks>
/// 变异验证（board-map 段 B 实跑，每条只跑本类）：
/// M-B15 BoardMapId.Format 恒省略 :n 段 → 红 4：非缺省棋盘数保留；
/// M-B16 MapCatalog 不再拒绝裸 board → 红 1：裸 board 不进规则内核；
/// M-B17 Program.MaterializeMapRequest 把裸 board 原样返回 → 红 2：裸 board 不入记录、裸 board 不进规则内核。
/// </remarks>
[Collection(SimulationHarness.ConsoleRedirect.Collection)]
public class 棋盘档生成图标识Tests
{
    [Fact]
    public void 标识往返()
    {
        // Scenario：把 board:42:n7 规范化 → board:42；用它生成的地图的标识为 board:42。
        Assert.Equal("board:42", BoardMapId.Normalize("board:42:n7"));
        Assert.Equal("board:42", BoardMapId.Normalize(" board:42 "));
        Assert.Equal("board:42", BoardMapId.Format(42, BoardMapParameters.Default));
        Assert.Equal((42UL, 7), (BoardMapId.Parse("board:42").MapSeed, BoardMapId.Parse("board:42").Parameters.BoardCount));

        Assert.Equal("board:42", BoardMapGenerator.Generate("board:42:n7").Id);
        Assert.Equal("board:42", MapCatalog.Resolve("board:42:n7").Id);
        Assert.Equal(MapFile.ToJson(BoardMapGenerator.Generate(42)), MapFile.ToJson(MapCatalog.Resolve(" board:42:n7 ")));

        // 种子的整个取值范围都能往返。
        Assert.Equal("board:18446744073709551615", BoardMapId.Normalize("board:18446744073709551615"));
        Assert.Equal("board:0", BoardMapId.Normalize("board:0"));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void 非缺省棋盘数保留(int boards)
    {
        // Scenario：把 board:42:n9 规范化 → board:42:n9。
        string id = $"board:42:n{boards}";

        Assert.Equal(id, BoardMapId.Normalize(id));
        Assert.Equal(boards, BoardMapId.Parse(id).Parameters.BoardCount);
        Assert.Equal(id, BoardMapId.Format(42, new BoardMapParameters { BoardCount = boards }));
        MapData map = MapCatalog.Resolve(id);
        Assert.Equal((id, boards), (map.Id, map.Boards.Length));
    }

    [Theory]
    [InlineData("board:abc")]
    [InlineData("board:42:n11")]
    [InlineData("board:42:n6")]
    [InlineData("board:42:n5")]
    [InlineData("board:")]
    [InlineData("board:42:")]
    [InlineData("board:42:n")]
    [InlineData("board:42:p7")]
    [InlineData("board:42:n7:n7")]
    [InlineData("board:42:n7:s1")]
    [InlineData("board:-1")]
    [InlineData("board:+1")]
    [InlineData("board:4 2")]
    [InlineData("board:42:n+7")]
    [InlineData("board:18446744073709551616")]
    [InlineData("Board:42")]
    public void 非法标识(string id)
    {
        // Scenario：请求地图 board:abc 或 board:42:n11 → 报错并给出标识格式与棋盘数范围 7–10。
        FormatException parse = Assert.Throws<FormatException>(() => BoardMapId.Parse(id));
        Assert.Contains("board:<地图种子>[:n<棋盘数>]", parse.Message, StringComparison.Ordinal);
        Assert.Contains("7–10", parse.Message, StringComparison.Ordinal);

        if (BoardMapId.IsBoardMap(id))
        {
            FormatException resolve = Assert.Throws<FormatException>(() => MapCatalog.Resolve(id));
            Assert.Contains("board:<地图种子>[:n<棋盘数>]", resolve.Message, StringComparison.Ordinal);
            Assert.Contains("7–10", resolve.Message, StringComparison.Ordinal);

            // 入口：报错退出，不回落到缺省地图。
            (int code, _, string err) = RunMain("map", "--map", id);
            Assert.Equal(1, code);
            Assert.Contains("7–10", err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 裸board不进规则内核_入口把它落成带种子的完整标识()
    {
        // 「裸 board 表示随机取一个地图种子，MUST 只在入口最外层处理」。规则内核不读时钟：裸 board 到了目录这里直接报错。
        Assert.True(BoardMapId.IsBareRequest(" board "));
        Assert.False(BoardMapId.IsBareRequest("board:1"));
        Assert.False(BoardMapId.IsBareRequest("gen"));
        Assert.False(GeneratedMapId.IsBareRequest("board"));
        Assert.False(GeneratedMapId.IsGenerated("board:1"));
        Assert.False(BoardMapId.IsBoardMap("gen:1"));
        FormatException bare = Assert.Throws<FormatException>(() => MapCatalog.Resolve("board"));
        Assert.Contains("board:12345", bare.Message, StringComparison.Ordinal);
        Assert.Contains("随机取种子由入口完成", bare.Message, StringComparison.Ordinal);   // 是目录自己拒绝的，不是落到标识解析才报错

        // 取种子的来源是注入的：测试给固定值，不依赖墙钟。
        var output = new StringWriter();
        string id = Siege.Sim.Program.MaterializeMapRequest(" board ", output, () => 987654321UL)!;
        Assert.Equal("board:987654321", id);
        Assert.Contains("--map board:987654321", output.ToString(), StringComparison.Ordinal);

        // 完整标识原样返回、不打印，也不去取种子；裸 gen 的行为不变。
        var silent = new StringWriter();
        static ulong Never() => throw new InvalidOperationException("不是裸标识，不应取地图种子。");
        Assert.Equal("board:12345:n9", Siege.Sim.Program.MaterializeMapRequest("board:12345:n9", silent, Never));
        Assert.Equal(string.Empty, silent.ToString());
        Assert.Equal("gen:5:s1", Siege.Sim.Program.MaterializeMapRequest("gen", new StringWriter(), () => 5UL));

        // map 子命令走同一处：注入的种子出现在打印的完整标识里，随即按它出图并通过校验。
        (int code, string text, string err) = RunMain(() => 12345UL, "map", "--map", "board");
        Assert.True(code == 0, err);
        Assert.Contains("本次地图为 board:12345（", text, StringComparison.Ordinal);
        MapData map = MapCatalog.Resolve("board:12345");
        Assert.Contains($"地图 board:12345  {map.Width}×{map.Height}", text, StringComparison.Ordinal);
        Assert.Contains("地图校验通过。", text, StringComparison.Ordinal);
        Assert.DoesNotContain("已导出", text, StringComparison.Ordinal);   // 生成图只打印，不往权威目录写
    }

    [Fact]
    public void 裸board不入记录()
    {
        // Scenario：以 --map=board 启动一局并查看对局日志首部 → 记录的地图标识是带种子的完整标识。
        // 走真实入口 run --map board，只跑 1 局、截断在 4 个小回合；"随机取的地图种子"由测试注入固定值。
        string dir = SimFixtures.TempDir("run-bare-board");
        (int code, string text, string err) = RunMain(
            () => 12345UL, "run", "--out", dir, "--map", "board", "--count", "1", "--difficulty", "Easy", "--turn-limit", "4", "--serial", "--sample-permille", "0");

        Assert.True(code == 0, err);
        Assert.Contains("--map board:12345", text, StringComparison.Ordinal);
        string configText = File.ReadAllText(Path.Combine(dir, "config.json"));
        Assert.DoesNotContain("\"board\"", configText, StringComparison.Ordinal);
        Assert.Equal("board:12345", RunConfig.FromJson(configText).MapId);

        MatchLog log = Assert.Single(MatchLog.ReadDirectory(dir));
        Assert.False(log.IsFailed, log.Failure?.Message);
        Assert.Equal("board:12345", log.Header.MapId);
        Assert.Equal("board:12345", log.Header.Config.MapId);
        Assert.Equal(5, log.Header.ZoneCount);
        Assert.DoesNotContain("\"board\"", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);
        Assert.Equal(MapFile.Digest(MapCatalog.Resolve("board:12345")), log.Header.MapDigest);

        // 回放按首部的标识重建同一张图。
        Assert.True(Replayer.Replay(MatchLog.Parse(log.DeterministicText())).Identical);

        // 绕过入口、把裸 board 直接交给会话 → 规则内核拒绝。
        Assert.Throws<FormatException>(() => MatchSession.Create(SimFixtures.Config(turnLimit: 4) with { MapId = " board " }, seed: 1));
    }

    [Fact]
    public void 未知标识的可用清单里说明棋盘图写法()
    {
        FileNotFoundException unknown = Assert.Throws<FileNotFoundException>(() => MapCatalog.Resolve("no-such-map"));
        Assert.Contains("board:<地图种子>", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("gen:<地图种子>", unknown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MapCatalog.BuiltinIds, BoardMapId.IsBoardMap);
        Assert.Equal(FourPlayerBaseMap.Id, MapCatalog.DefaultId);   // 缺省地图不变（design D9）
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
