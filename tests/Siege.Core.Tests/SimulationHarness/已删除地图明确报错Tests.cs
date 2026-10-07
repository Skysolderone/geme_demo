using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// retire-legacy-maps 段 B（design D2）：旧地图（标准档 v1–v5、2 人 / 3 人图、边疆图 v1 / v2）与 <c>gen:</c> 生成图删除后，
/// 请求它们的命令行、旧日志回放、旧存档恢复一律在 <see cref="MapCatalog.Resolve"/> 报"已删除"并列出现有地图；
/// <c>maps/&lt;标识&gt;.json</c> 隐式回落删除，显式文件路径照旧可加载；<c>analyze</c> 不读地图，旧日志照常分析（已知歧义 2、7）。
/// </summary>
[Collection(ConsoleRedirect.Collection)]   // 经 Program.Execute 重定向 Console.Out / Console.Error
public class 已删除地图明确报错Tests
{
    /// <summary>已删除标识的全部写法：九个旧内置名、裸 gen、gen: 生成图（含带参数与带空白的写法）。</summary>
    public static IEnumerable<object[]> RetiredRequests =>
        MapCatalog.RetiredIds.Concat(["gen", "gen:1", "gen:12345:p7", "gen:12345:p7:s1", " gen:42 "]).Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(RetiredRequests))]
    public void 目录对已删除的标识报已删除并列出现有地图(string id)
    {
        // 变异 RB-1：Resolve 去掉"已删除"分支 → 旧内置名落到文件路径报"找不到地图"、gen: 同样落空，本测试全部红（类型不是 RetiredMapException、文案无"已删除"）。
        RetiredMapException ex = Assert.Throws<RetiredMapException>(() => MapCatalog.Resolve(id));
        Assert.IsAssignableFrom<FileNotFoundException>(ex);   // 三个入口与回放现有的 FileNotFoundException 捕获接得住
        Assert.Equal(id.Trim(), ex.MapId);
        Assert.Contains($"地图 {id.Trim()} 已删除（retire-legacy-maps", ex.Message, StringComparison.Ordinal);
        Assert.Contains("现有地图：", ex.Message, StringComparison.Ordinal);
        Assert.All(MapCatalog.BuiltinIds, b => Assert.Contains(b, ex.Message, StringComparison.Ordinal));
        Assert.Contains("board:<地图种子>", ex.Message, StringComparison.Ordinal);
        Assert.True(MapCatalog.IsRetired(id));
    }

    [Fact]
    public void 已删除的标识清单恰为九个旧内置名_现有地图不在其中()
    {
        Assert.Equal(
            ["siege-4p-base-v1", "siege-4p-base-v2", "siege-4p-base-v3", "siege-4p-base-v4", "siege-4p-base-v5",
             "siege-2p-base-v1", "siege-3p-base-v1", "siege-frontier-v1", "siege-frontier-v2"],
            MapCatalog.RetiredIds);
        Assert.Empty(MapCatalog.BuiltinIds.Intersect(MapCatalog.RetiredIds));
        Assert.All(MapCatalog.BuiltinIds, id => Assert.False(MapCatalog.IsRetired(id)));
        // 反面：现有写法不被误判——棋盘图标识、前缀相近的拼写、文件路径。
        foreach (string id in new[] { "board:1", "board", "general", "genx:1", "maps/siege-4p-base-v5.json", "siege-4p-base-v6", "" })
        {
            Assert.False(MapCatalog.IsRetired(id), id);
        }

        Assert.Equal(MapCatalog.DefaultId, MapCatalog.Resolve(MapCatalog.DefaultId).Id);
    }

    [Theory]
    [InlineData("siege-4p-base-v5")]
    [InlineData("gen:1")]
    [InlineData("gen")]
    public void 批量跑局入口报已删除且不写输出(string id)
    {
        string outDir = Path.Combine(SimFixtures.TempDir("retired-run-" + id.Replace(':', '_')), "out");
        (int code, string text, string err) = RunMain("run", "--out", outDir, "--map", id, "--count", "1", "--seed", "1", "--turn-limit", "4", "--serial");

        Assert.Equal(1, code);
        Assert.Contains($"地图 {id} 已删除（retire-legacy-maps", err, StringComparison.Ordinal);
        Assert.Contains(MapCatalog.DefaultId, err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
        Assert.DoesNotContain("随机取了一个地图种子", text, StringComparison.Ordinal);   // 裸 gen 不再被入口补种子
    }

    [Fact]
    public void 每局换图写gen标识开跑前报已删除()
    {
        // 变异 RB-2：RunConfig.Validated 去掉已删除标识的判断 → gen: 报的是"要求棋盘图标识"（仍是 ArgumentException，退出码 1），文案无"已删除"，本测试红。
        string outDir = Path.Combine(SimFixtures.TempDir("retired-run-rotate"), "out");
        (int code, _, string err) = RunMain("run", "--out", outDir, "--map", "gen:100", "--map-per-match", "--count", "2", "--seed", "1", "--turn-limit", "4", "--serial");

        Assert.Equal(1, code);
        Assert.Contains("已删除（retire-legacy-maps", err, StringComparison.Ordinal);
        Assert.Contains("board:<起始地图种子>", err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }

    [Theory]
    [InlineData("siege-4p-base-v5")]
    [InlineData("siege-2p-base-v1")]
    [InlineData("gen:1")]
    public void 终端入口报已删除(string id)
    {
        (int code, string text, string err) = RunMain("play", "--map", id, "--seed", "1");

        Assert.Equal(1, code);
        Assert.Contains($"地图 {id} 已删除（retire-legacy-maps", err, StringComparison.Ordinal);
        Assert.DoesNotContain("围杀 Siege · 终端对局", text, StringComparison.Ordinal);   // 没开局
    }

    [Fact]
    public void 回放引用已删除地图的旧日志报已删除()
    {
        // 入库的旧日志夹具是 v5 上的真实日志（retire-legacy-maps 段 A2 入库）：replay 在按首部重建地图时报"已删除"，退出码 1，不走到"地图不一致"。
        string fixture = LegacyLog();
        Assert.Equal("siege-4p-base-v5", MatchLog.Read(fixture).Header.MapId);   // 样本口径：夹具确是旧图日志

        (int code, _, string err) = RunMain("replay", "--file", fixture);

        Assert.Equal(1, code);
        Assert.Contains("地图 siege-4p-base-v5 已删除（retire-legacy-maps", err, StringComparison.Ordinal);
        Assert.Contains("旧存档与旧日志不再支持加载", err, StringComparison.Ordinal);

        // 反面：同一入口回放一份棋盘图上的新日志照常成功（报错来自地图，不是 replay 本身坏了）。
        string dir = SimFixtures.TempDir("retired-replay-ok");
        string fresh = Path.Combine(dir, "match-fresh.jsonl");
        MatchLog log = Siege.Sim.Running.MatchSession.Create(SimFixtures.Config(turnLimit: 2) with { MapId = "siege-2p-board-v1", Players = [new(), new()] }, seed: 1).Run();
        File.WriteAllText(fresh, log.DeterministicText());
        Assert.Equal(0, RunMain("replay", "--file", fresh).Code);
    }

    [Fact]
    public void 分析不读地图_旧日志照常分析()
    {
        // 已知歧义 7：analyze 只读日志字段、不解析地图，引用已删除地图的旧日志照常出报告。
        string dir = SimFixtures.TempDir("retired-analyze");
        File.Copy(LegacyLog(), Path.Combine(dir, "match-legacy-v5.jsonl.gz"));
        string report = Path.Combine(dir, "report.txt");

        (int code, string text, string err) = RunMain("analyze", "--dir", dir, "--out", report);

        Assert.True(code == 0, err);
        Assert.True(File.Exists(report));
        Assert.Contains("报告已写入", text, StringComparison.Ordinal);
        Assert.Contains("1 局", File.ReadAllText(report), StringComparison.Ordinal);   // 样本口径：那一局确实被读进来了
    }

    [Fact]
    public void 显式文件路径照旧可加载_不再回落到maps目录()
    {
        // 已知歧义 2：删除 maps/<标识>.json 隐式回落（旧标识会被旧文件复活），显式文件路径保留。
        // 变异 RB-3：Resolve 恢复"File.Exists(id) ? id : maps/<id>.json"回落 → 下面按名字请求的那一句不再报错，本测试红。
        string name = $"retire-fallback-probe-{Guid.NewGuid():N}";
        string relative = Path.Combine("maps", name + ".json");
        bool hadMaps = Directory.Exists("maps");
        Directory.CreateDirectory("maps");
        try
        {
            File.WriteAllText(relative, MapFile.ToJson(FrontierFixtures.Map()));

            FileNotFoundException ex = Assert.Throws<FileNotFoundException>(() => MapCatalog.Resolve(name));
            Assert.IsNotType<RetiredMapException>(ex);
            Assert.Contains($"找不到地图 {name}", ex.Message, StringComparison.Ordinal);
            Assert.Contains("现有地图：", ex.Message, StringComparison.Ordinal);

            // 同一份文件给显式路径（相对、绝对）都能加载。
            Assert.Equal("test-frontier-6", MapCatalog.Resolve(relative).Id);
            Assert.Equal("test-frontier-6", MapCatalog.Resolve(Path.GetFullPath(relative)).Id);
        }
        finally
        {
            File.Delete(relative);
            if (!hadMaps && Directory.Exists("maps") && !Directory.EnumerateFileSystemEntries("maps").Any())
            {
                Directory.Delete("maps");
            }
        }

        // 已删除的旧标识即使恰有同名文件也不复活：判定在文件解析之前。
        string retiredFile = Path.Combine(SimFixtures.TempDir("retired-same-name"), "siege-4p-base-v5");
        File.WriteAllText(retiredFile, MapFile.ToJson(FrontierFixtures.Map()));
        Assert.Equal("test-frontier-6", MapCatalog.Resolve(retiredFile).Id);   // 路径不是裸标识：照常加载
        Assert.Throws<RetiredMapException>(() => MapCatalog.Resolve("siege-4p-base-v5"));
    }

    [Fact]
    public void 入口与图形版的地图错误捕获接得住已删除异常()
    {
        // RetiredMapException 派生自 FileNotFoundException：Program 与 GameRoot 的 catch 都按 FileNotFoundException 接（src/godot 不在 sln 里，源码扫描）。
        string root = FrontierFixtures.RepoRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "Siege.Sim", "Program.cs"));
        string gameRoot = File.ReadAllText(Path.Combine(root, "src", "godot", "scripts", "GameRoot.cs"));
        Assert.Matches(@"catch \(Exception ex\) when \(ex is [^)]*FileNotFoundException", program);
        Assert.Matches(@"catch \(System\.Exception ex\) when \(ex is [^)]*System\.IO\.FileNotFoundException", gameRoot);
        Assert.True(typeof(FileNotFoundException).IsAssignableFrom(typeof(RetiredMapException)));
        Assert.Contains("MapCatalog.Resolve(mapId)", gameRoot, StringComparison.Ordinal);
    }

    private static string LegacyLog() => Path.Combine(AppContext.BaseDirectory, "MatchTelemetry", "Fixtures", "legacy-v5-zonecount-seed1.jsonl.gz");

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
            return (Siege.Sim.Program.Execute(args, () => throw new InvalidOperationException("本测试不应随机取地图种子。")), output.ToString(), err.ToString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }
}
