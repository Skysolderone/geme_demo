using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Analysis;
using Siege.Sim.Cli;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Sim;

/// <summary>
/// 批量跑局程序（裁决 11）。子命令：<c>map</c>（打印并导出基准地图）、<c>run</c>（批量跑局）、<c>replay</c>（凭种子 + 配置重跑比对）、
/// <c>analyze</c>（只读日志目录出平衡报告）。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            var cli = new CommandLine(args.Skip(1));
            return args[0] switch
            {
                "play" => Play(cli),
                "map" => ExportMap(),
                "run" => Run(cli),
                "replay" => Replay(cli),
                "analyze" => Analyze(cli),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or FileNotFoundException or SiegeRuleException)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知子命令 {command}。");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法：");
        Console.WriteLine("  Siege.Sim play [--seed <种子>] [--players <人数>] [--seat <你的座位>] [--difficulty <Easy|Standard|Hard>] [--max-rounds <大回合上限>]");
        Console.WriteLine("  Siege.Sim map");
        Console.WriteLine("  Siege.Sim run --out <目录> [--config <json>] [--seed <首个种子>] [--count <局数>] [--parallel <并行度|0=核数>]");
        Console.WriteLine("                [--map <地图id或文件>] [--players <人数>] [--difficulty <Easy|Standard|Hard>] [--max-rounds <大回合上限，0=不限>]");
        Console.WriteLine("                [--dominance-start <碾压起始大回合，0=关闭，默认 7>] [--no-catch-up（关闭落后者征募补偿，默认开启）]");
        Console.WriteLine("                [--site-values <营帐/篝火/石碑，默认 5/15/45>]（AI 权重只能经 --config 的 Players[].Weights 指定；同时给 --difficulty / --players 会重建玩家列表、丢弃配置文件里的权重）");
        Console.WriteLine("                [--retention <SnapshotsOnly|Full>] [--sample-permille <千分比>] [--gzip] [--serial]");
        Console.WriteLine("  Siege.Sim replay --file <match-*.jsonl>   或   replay --dir <目录> --seed <十六进制种子>");
        Console.WriteLine("  Siege.Sim analyze --dir <目录> [--include-contaminated] [--out <报告文件>]");
    }

    // ---------- play ----------

    private static int Play(CommandLine cli)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;
        ulong? seed = cli.Has("seed") ? cli.GetUInt64("seed", 0) : null;
        var difficulty = Enum.Parse<Core.Ai.AiDifficulty>(cli.Get("difficulty", "Standard"), ignoreCase: true);
        return Siege.Sim.Play.PlayCommand.Run(
            seed,
            cli.GetInt("players", 4),
            cli.GetInt("seat", 1),
            difficulty,
            cli.GetInt("max-rounds", Core.Match.MatchOptions.DefaultMaxMajorRounds),
            Console.In,
            Console.Out);
    }

    // ---------- map ----------

    /// <summary>地图工具：打印 4 人基准地图（高度 / 地表 / 桥 / 栅栏 / 信物 / 据点 / 出生区与距离表）并导出 maps/&lt;id&gt;.json（权威地图文件）。</summary>
    private static int ExportMap()
    {
        MapData map = FourPlayerBaseMap.Create();
        MapValidationResult result = MapValidator.Validate(map);
        TerrainData terrain = map.TerrainData;
        Coord[] all = [.. map.AllCoords()];

        Console.WriteLine($"地图 {map.Id}  {map.Width}×{map.Height}");
        Console.WriteLine(
            $"可落子格 {map.PlayableCount}   岩石 {map.Obstacles.Count}   深水 {all.Count(c => map.SurfaceAt(c) == Surface.DeepWater)}（其中桥 {terrain.Bridges.Count}）"
            + $"   栅栏 {terrain.Fences.Count}   林地 {all.Count(c => map.SurfaceAt(c) == Surface.Forest)}   土路 {all.Count(c => map.SurfaceAt(c) == Surface.Road)}");
        Console.WriteLine(
            "可落子格按高度 h0/h1/h2 = "
            + string.Join("/", Enumerable.Range(0, TerrainData.MaxHeight + 1).Select(h => all.Count(c => map.IsPlayable(c) && map.HeightAt(c) == h))));
        Console.WriteLine(
            $"出生区 {map.BirthZones.Length} 个，各 "
            + string.Join("/", map.BirthZones.Select(z => z.Count(map.IsPlayable)))
            + " 个可落子格，高度 "
            + string.Join("/", map.BirthZones.Select(z => string.Join(",", z.Select(map.HeightAt).Distinct().Order()))));
        Console.WriteLine($"信物格 {map.RelicCells.Count}（出生区 {map.RelicCells.Count(r => r.Value.Zone == RelicZone.BirthZone)}，公共区 {map.RelicCells.Count(r => r.Value.Zone == RelicZone.Contested)}）");
        Console.WriteLine(
            $"据点 {map.Sites.Count}（营帐 {map.Sites.Count(s => s.Value == SiteTier.Tent)}，篝火 {map.Sites.Count(s => s.Value == SiteTier.Campfire)}，石碑 {map.Sites.Count(s => s.Value == SiteTier.Stele)}）");
        foreach (SiteTier tier in Enum.GetValues<SiteTier>())
        {
            Console.WriteLine($"  {tier switch { SiteTier.Tent => "营帐", SiteTier.Campfire => "篝火", _ => "石碑" }}（{Siege.Sim.Play.BoardRenderer.SiteLetter(tier)}） {string.Join(" ", map.Sites.Where(s => s.Value == tier).Select(s => s.Key).Order())}");
        }

        Console.WriteLine("各出生区沿气边最短距离（出生区 1/2/3/4）：");
        foreach (BirthZoneDistance metric in MapValidator.DistanceTable(map))
        {
            Console.WriteLine($"  {metric.Name}  {string.Join("/", metric.Distances.Select(d => d?.ToString() ?? "-"))}");
        }

        Console.WriteLine($"咽喉 {string.Join(" ", map.ChokePoints.Order())}   中央入口 {map.CentralEntrance}   桥 {string.Join(" ", terrain.Bridges.Order())}");
        Console.WriteLine($"栅栏 {string.Join(" ", terrain.Fences.OrderBy(e => e.A).ThenBy(e => e.B))}");
        Console.WriteLine();
        Console.WriteLine(result);
        Console.WriteLine();

        for (int y = map.Height - 1; y >= 0; y--)
        {
            Console.Write($"{y + 1,3} ");
            for (int x = 0; x < map.Width; x++)
            {
                Coord c = new(x, y);
                Console.Write(Glyph(map, c));
                Console.Write(x < map.Width - 1 && map.HasFence(c, new Coord(x + 1, y)) ? '|' : ' ');
            }

            Console.WriteLine();

            if (y > 0 && Enumerable.Range(0, map.Width).Any(x => map.HasFence(new Coord(x, y), new Coord(x, y - 1))))
            {
                Console.Write("    ");
                for (int x = 0; x < map.Width; x++)
                {
                    Console.Write(map.HasFence(new Coord(x, y), new Coord(x, y - 1)) ? "-- " : "   ");
                }

                Console.WriteLine();
            }
        }

        Console.Write("    ");
        for (int x = 0; x < map.Width; x++)
        {
            Console.Write($"{Coord.ColumnLetters[x]}  ");
        }

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("每格两位：首位是高度 0/1/2，次位是标记。## 岩石  ~~ 深水  = 桥  1-4 出生区  r 出生区信物  o 公共信物  R 公共高档信物  T 营帐  C 篝火  S 石碑");
        Console.WriteLine("@ 中央入口  ^ 咽喉（与信物、据点或桥同格时显示信物 / 据点 / 桥的标记；入口 G7 是高档信物 R、四座桥即咽喉 =；据点不与信物重合）  F 林地  . 土路   格间 | 与行间 -- 为栅栏");

        // 导出地图文件，供设计师脱离代码维护
        Directory.CreateDirectory("maps");
        string path = Path.Combine("maps", $"{map.Id}.json");
        File.WriteAllText(path, MapFile.ToJson(map));
        Console.WriteLine();
        Console.WriteLine($"已导出 {path}");
        return 0;
    }

    /// <summary>文本图的单格两字符：高度数字 + 标记。岩石与未架桥深水不可落子，画成 ## / ~~，桥格按可落子格画并标 =。</summary>
    private static string Glyph(MapData map, Coord c)
    {
        if (map.Obstacles.Contains(c))
        {
            return "##";
        }

        if (map.SurfaceAt(c) == Surface.DeepWater && !map.HasBridge(c))
        {
            return "~~";
        }

        char mark = map.RelicCells.TryGetValue(c, out RelicCellSpec spec)
            ? spec.Budget switch { BudgetTier.Birth => 'r', BudgetTier.High => 'R', _ => 'o' }
            : map.Sites.TryGetValue(c, out SiteTier site) ? Siege.Sim.Play.BoardRenderer.SiteLetter(site)
            : c == map.CentralEntrance ? '@'
            : map.HasBridge(c) ? '='
            : map.ChokePoints.Contains(c) ? '^'
            : map.SurfaceAt(c) == Surface.Forest ? 'F'
            : map.SurfaceAt(c) == Surface.Road ? '.'
            : map.BirthZoneOf(c) is { } z ? (char)('1' + z)
            : ' ';
        return $"{map.HeightAt(c)}{mark}";
    }

    // ---------- run ----------

    private static int Run(CommandLine cli)
    {
        string outDir = cli.GetOrNull("out") ?? throw new ArgumentException("run 需要 --out <目录>。");
        RunConfig config = cli.GetOrNull("config") is { } file ? RunConfig.FromJson(File.ReadAllText(file)) : new RunConfig();

        if (cli.Has("players") || cli.Has("difficulty"))
        {
            int players = cli.GetInt("players", config.PlayerCount);
            var difficulty = Enum.Parse<Core.Ai.AiDifficulty>(cli.Get("difficulty", config.Players[0].Difficulty.ToString()), ignoreCase: true);
            config = config with { Players = [.. Enumerable.Range(0, players).Select(_ => new PlayerAiConfig { Difficulty = difficulty })] };
        }

        config = config with
        {
            MapId = cli.Get("map", config.MapId),
            SeedStart = cli.GetUInt64("seed", config.SeedStart),
            Count = cli.GetInt("count", config.Count),
            Parallelism = cli.GetInt("parallel", config.Parallelism),
            MaxMajorRounds = cli.GetInt("max-rounds", config.MaxMajorRounds),
            DominanceStartRound = cli.GetInt("dominance-start", config.DominanceStartRound),
            CatchUpRecruit = !cli.Flag("no-catch-up") && config.CatchUpRecruit,
            SiteValues = cli.GetOrNull("site-values") is { } siteValues ? RunConfig.ParseSiteValues(siteValues) : config.SiteValues,
            EventRetention = Enum.Parse<EventRetention>(cli.Get("retention", config.EventRetention.ToString()), ignoreCase: true),
            FullEventSamplePermille = cli.GetInt("sample-permille", config.FullEventSamplePermille),
            Compress = cli.Flag("gzip") || config.Compress,
        };
        config.Validated();
        int parallelism = cli.Flag("serial") ? 1 : config.EffectiveParallelism;

        Console.WriteLine($"跑局：{config.Count} 局，种子 {config.SeedStart}..{config.SeedAt(config.Count - 1)}，{config.PlayerCount} 人，并行度 {parallelism}，大回合上限 {config.MaxMajorRounds}，据点分值 {config.SiteValues}，输出 {outDir}");
        BatchSummary summary = BatchRunner.ExecuteToDirectory(config, outDir, parallelism, Console.Out);
        Console.WriteLine();
        Console.WriteLine(summary.ToJson());
        return summary.Failed == 0 ? 0 : 3;
    }

    // ---------- replay ----------

    private static int Replay(CommandLine cli)
    {
        string path;
        if (cli.GetOrNull("file") is { } file)
        {
            path = file;
        }
        else
        {
            string dir = cli.GetOrNull("dir") ?? throw new ArgumentException("replay 需要 --file 或 --dir + --seed。");
            ulong seed = cli.GetUInt64("seed", 0);
            string hex = new Core.Determinism.GameSeed(seed).ToString();
            path = Directory.GetFiles(dir, $"match-{hex}*.jsonl").SingleOrDefault()
                ?? throw new FileNotFoundException($"目录 {dir} 里没有种子 {hex} 的日志。");
        }

        MatchLog original = MatchLog.Read(path);
        Console.WriteLine($"回放 {path}：种子 {original.Header.Seed}，{original.Turns.Count} 小回合，{(original.IsFailed ? "失败局" : original.Result!.Reason)}");
        ReplayResult result = Replayer.Replay(original);
        Console.WriteLine(result);
        if (original.IsFailed)
        {
            Console.WriteLine(result.Replayed.IsFailed
                ? $"重跑同样失败：{result.Replayed.Failure!.ExceptionType} @ 第 {result.Replayed.Failure.Turn} 小回合"
                : "重跑没有失败——失败不可复现，请检查非确定性来源。");
        }

        return result.Identical ? 0 : 4;
    }

    // ---------- analyze ----------

    private static int Analyze(CommandLine cli)
    {
        string dir = cli.GetOrNull("dir") ?? throw new ArgumentException("analyze 需要 --dir <目录>。");
        List<MatchLog> logs = MatchLog.ReadDirectory(dir);
        var options = new AnalysisOptions { IncludeContaminated = cli.Flag("include-contaminated") };
        BalanceReport report = BalanceAnalyzer.Analyze(logs, options);
        string text = ReportWriter.Render(report);
        Console.WriteLine(text);
        string outPath = cli.Get("out", Path.Combine(dir, "report.txt"));
        File.WriteAllText(outPath, text);
        Console.WriteLine($"报告已写入 {outPath}");
        return 0;
    }
}
