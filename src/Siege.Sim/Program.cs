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
    public static int Main(string[] args) => Execute(args, ClockMapSeed);

    /// <summary>
    /// 裸 <c>gen</c> 的地图种子缺省取自时钟——整个批量 / 终端程序里读时钟取地图种子的只有这一处（图形版入口另有一处），规则内核永不读时钟。
    /// 时间戳经 <see cref="GeneratedMapId.FriendlySeed"/> 折成九位以内的短种子，与图形版（裸 <c>gen</c> 与选图界面"换一张"）同一个折叠函数。
    /// </summary>
    private static ulong ClockMapSeed() => GeneratedMapId.FriendlySeed((ulong)System.Diagnostics.Stopwatch.GetTimestamp());

    /// <summary>
    /// 入口本体。<paramref name="mapSeedSource"/> 是"随机取一个地图种子"的来源：只在地图选项是裸 <c>gen</c> 时调用一次，
    /// <c>play</c> / <c>map</c> / <c>run</c> 三个子命令共用。生产入口传时钟；测试注入固定值，不留依赖墙钟的测试。
    /// </summary>
    internal static int Execute(string[] args, Func<ulong> mapSeedSource)
    {
        ArgumentNullException.ThrowIfNull(mapSeedSource);
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
                "play" => Play(cli, mapSeedSource),
                "map" => ExportMap(cli, mapSeedSource),
                "run" => Run(cli, mapSeedSource),
                "replay" => Replay(cli),
                "analyze" => Analyze(cli),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or FileNotFoundException or SiegeRuleException or MapGenerationException)
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
        Console.WriteLine("  Siege.Sim play [--seed <种子>] [--players <人数>] [--seat <你的座位>] [--difficulty <Easy|Standard|Hard>] [--max-rounds <大回合上限>] [--map <地图id或文件>] [--cell-limit <AI 候选格上限，0=不限，缺省按地图大小>]");
        Console.WriteLine("  Siege.Sim map [--map <地图id或文件>] [--out <导出的地图文件>]（生成图只打印；给 --out 才导出，导出的文件可直接当 --map 用）");
        Console.WriteLine("  Siege.Sim run --out <目录> [--config <json>] [--seed <首个种子>] [--count <局数>] [--parallel <并行度|0=核数>]");
        Console.WriteLine("                [--map <地图id或文件>] [--players <人数>] [--difficulty <Easy|Standard|Hard>] [--max-rounds <大回合上限，0=不限>]");
        Console.WriteLine("                [--dominance-start <碾压起始大回合，0=关闭，默认 7>] [--no-catch-up（关闭落后者征募补偿，默认开启）]");
        Console.WriteLine("                [--site-values <营帐/篝火/石碑，默认 5/15/45>] [--artisan-weight <匠人征募权重，默认 10>] [--cell-limit <AI 候选格上限，0=不限，缺省按地图大小>]（AI 权重只能经 --config 的 Players[].Weights 指定；同时给 --difficulty / --players 会重建玩家列表、丢弃配置文件里的权重）");
        Console.WriteLine("                [--map-per-match（每局换一张生成图：--map gen:<起始地图种子>[:p<平台数>]，第 i 局用 起始 + i）]");
        Console.WriteLine("                [--retention <SnapshotsOnly|Full>] [--sample-permille <千分比>] [--gzip] [--serial]");
        Console.WriteLine("  地图标识：内置图 / 地图文件路径 / gen:<地图种子>[:p<平台数 5–8>]（随机生成图）；只写 gen 即随机取一个地图种子并打印完整标识。");
        Console.WriteLine("  Siege.Sim replay --file <match-*.jsonl>   或   replay --dir <目录> --seed <十六进制种子>");
        Console.WriteLine("  Siege.Sim analyze --dir <目录> [--include-contaminated] [--out <报告文件>]");
    }

    // ---------- play ----------

    private static int Play(CommandLine cli, Func<ulong> mapSeedSource)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;
        ulong? seed = cli.Has("seed") ? cli.GetUInt64("seed", 0) : null;
        var difficulty = Enum.Parse<Core.Ai.AiDifficulty>(cli.Get("difficulty", "Standard"), ignoreCase: true);
        int players = cli.GetInt("players", 4);
        int seat = cli.GetInt("seat", 1);
        int maxRounds = cli.GetInt("max-rounds", Core.Match.MatchOptions.DefaultMaxMajorRounds);
        string? mapId = cli.GetOrNull("map");
        int? cellLimit = cli.Has("cell-limit") ? cli.GetInt("cell-limit", 0) : null;
        cli.EnsureRecognized();   // 读完所有选项、开局之前结算（strict-cli 2.4）
        mapId = MaterializeMapRequest(mapId, Console.Out, mapSeedSource);
        MapData map = MapCatalog.Resolve(mapId);   // 未知标识在开局前报错并列出可用标识，不回落到缺省地图（frontier-map D5）
        return Siege.Sim.Play.PlayCommand.Run(seed, players, seat, difficulty, maxRounds, Console.In, Console.Out, map, cellLimit);
    }

    /// <summary>
    /// 裸 <c>gen</c>（"随机取一个地图种子"）：规则内核不读时钟，取种子只在入口最外层做。取到后拼成完整标识、打印给用户，再交给
    /// <see cref="MapCatalog"/>；裸 <c>gen</c> MUST NOT 进入 Core，也 MUST NOT 写进任何记录（配置记录、日志首部）。其余标识原样返回，
    /// 此时 <paramref name="mapSeedSource"/> 不被调用。
    /// </summary>
    internal static string? MaterializeMapRequest(string? mapId, TextWriter output, Func<ulong> mapSeedSource)
    {
        if (!GeneratedMapId.IsBareRequest(mapId))
        {
            return mapId;
        }

        string id = GeneratedMapId.Format(mapSeedSource(), MapGenParameters.Default);
        output.WriteLine($"随机取了一个地图种子：本次地图为 {id}（用 --map {id} 可重开同一张图）");
        return id;
    }

    // ---------- map ----------

    /// <summary>
    /// 地图工具：打印一张地图（高度 / 地表 / 桥 / 栅栏 / 信物 / 据点 / 出生区与距离表、校验结果与报告项）；
    /// 内置图另导出 maps/&lt;id&gt;.json（权威地图文件）。<c>--map</c> 缺省为缺省地图；给地图文件路径时只打印不导出（不回写设计师的文件）。
    /// <c>--out</c> 把这张图另存为指定文件（生成图只有这一条落盘的路）；目标不得是内置图的权威文件（<see cref="RequireNotAuthoritativeMapFile"/>）。
    /// </summary>
    private static int ExportMap(CommandLine cli, Func<ulong> mapSeedSource)
    {
        string? mapId = cli.GetOrNull("map");
        string? outPath = cli.GetOrNull("out");
        cli.EnsureRecognized();   // map 只认 --map / --out；结算在导出之前（strict-cli 2.4）
        RequireNotAuthoritativeMapFile(outPath);   // 先于一切输出：手滑不得覆盖内置图的权威文件
        mapId = MaterializeMapRequest(mapId, Console.Out, mapSeedSource);
        MapData map = MapCatalog.Resolve(mapId);
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

        Console.WriteLine($"各出生区沿气边最短距离（出生区 {string.Join("/", Enumerable.Range(0, map.BirthZones.Length).Select(BirthZoneLabel.Number))}）：");
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
        Console.WriteLine($"每格两位：首位是高度 0/1/2，次位是标记。## 岩石  ~~ 深水  = 桥  1-{BirthZoneLabel.Number(map.BirthZones.Length - 1)} 出生区  r 出生区信物  o 公共信物  R 公共高档信物  T 营帐  C 篝火  S 石碑");
        Console.WriteLine("@ 中央入口  ^ 咽喉（与信物、据点或桥同格时显示信物 / 据点 / 桥的标记：入口若同时是高档信物显示 R，桥若同时是咽喉显示 =；据点不与信物重合）  F 林地  . 土路   格间 | 与行间 -- 为栅栏");

        // --out：把这张图导出到指定文件（map-generator 2.6）。生成图靠它落成普通地图文件——标识里有冒号，做不了 Windows 文件名，
        // 而且 maps/ 是内置图的权威目录，查看生成图不得往里写。导出的文件按路径加载，与按标识生成的地图逐项相同。
        if (outPath is not null)
        {
            if (Path.GetDirectoryName(Path.GetFullPath(outPath)) is { Length: > 0 } outDir)
            {
                Directory.CreateDirectory(outDir);
            }

            File.WriteAllText(outPath, MapFile.ToJson(map));
            Console.WriteLine();
            Console.WriteLine($"已导出 {outPath}（用 --map {outPath} 可加载这张图）");
        }

        // 只导出"按内置标识请求"的图。判据是请求的标识而不是读到的 map.Id：设计师拿一份 v4 的副本改了地形、Id 没改，
        // `map --map 副本.json` 只是想看一眼，不得因此覆盖 maps/ 里的权威文件。
        string requested = string.IsNullOrWhiteSpace(mapId) ? MapCatalog.DefaultId : mapId.Trim();
        if (!MapCatalog.BuiltinIds.Contains(requested))
        {
            return 0;
        }

        // 导出地图文件，供设计师脱离代码维护
        Directory.CreateDirectory("maps");
        string path = Path.Combine("maps", $"{map.Id}.json");
        File.WriteAllText(path, MapFile.ToJson(map));
        Console.WriteLine();
        Console.WriteLine($"已导出 {path}");
        return 0;
    }

    /// <summary>
    /// <c>map --out</c> 的目标 MUST NOT 是内置图的权威文件（任一 <c>maps/</c> 目录下的 <c>&lt;内置标识&gt;.json</c>）：那些文件只由"按内置标识请求"的导出维护，
    /// 拿别的图（尤其是生成图）覆盖上去，地图文件与同名内置图就对不上了。文件此刻存在与否都拒绝；<c>maps/</c> 下的其它文件名允许。
    /// 判据只看路径本身（与当前目录无关），文件名与目录名不分大小写（Windows）。
    /// </summary>
    private static void RequireNotAuthoritativeMapFile(string? outPath)
    {
        if (outPath is null)
        {
            return;
        }

        string full = Path.GetFullPath(outPath);
        string file = Path.GetFileName(full);
        string? directory = Path.GetFileName(Path.GetDirectoryName(full));
        if (string.Equals(directory, "maps", StringComparison.OrdinalIgnoreCase)
            && MapCatalog.BuiltinIds.Any(id => string.Equals(file, id + ".json", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"--out {outPath} 指向内置地图的权威文件（maps/ 下的 {file}），拒绝覆盖。请换一个文件名；要更新权威文件，用 map --map {Path.GetFileNameWithoutExtension(file)}（不带 --out）。");
        }
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
            : map.BirthZoneOf(c) is { } z ? (char)('0' + BirthZoneLabel.Number(z))
            : ' ';
        return $"{map.HeightAt(c)}{mark}";
    }

    // ---------- run ----------

    private static int Run(CommandLine cli, Func<ulong> mapSeedSource)
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
            MapPerMatch = cli.Flag("map-per-match") || config.MapPerMatch,
            SeedStart = cli.GetUInt64("seed", config.SeedStart),
            Count = cli.GetInt("count", config.Count),
            Parallelism = cli.GetInt("parallel", config.Parallelism),
            MaxMajorRounds = cli.GetInt("max-rounds", config.MaxMajorRounds),
            DominanceStartRound = cli.GetInt("dominance-start", config.DominanceStartRound),
            CatchUpRecruit = !cli.Flag("no-catch-up") && config.CatchUpRecruit,
            SiteValues = cli.GetOrNull("site-values") is { } siteValues ? RunConfig.ParseSiteValues(siteValues) : config.SiteValues,
            ArtisanWeight = cli.GetInt("artisan-weight", config.ArtisanWeight),
            CandidateCellLimit = cli.Has("cell-limit") ? cli.GetInt("cell-limit", 0) : config.CandidateCellLimit,
            EventRetention = Enum.Parse<EventRetention>(cli.Get("retention", config.EventRetention.ToString()), ignoreCase: true),
            FullEventSamplePermille = cli.GetInt("sample-permille", config.FullEventSamplePermille),
            Compress = cli.Flag("gzip") || config.Compress,
        };
        bool serial = cli.Flag("serial");
        cli.EnsureRecognized();   // 读完所有选项、创建输出目录与跑局之前结算（strict-cli 2.4；先于 Validated 以便未知选项优先报出）
        // 裸 gen 在这里就落成完整标识：config.json 与日志首部不得出现裸 gen（配置文件里写的裸 gen 同样处理）。
        // 命令行 --map gen --map-per-match：起始地图种子取自 mapSeedSource，落成 gen:<起始> 后再校验，同样打印并记入 config.json。
        // 只有配置文件里同时写"裸 gen + MapPerMatch"会在读入校验（RunConfig.FromJson）时报错——配置文件是要复用的记录，换图必须写明起始地图种子。
        config = config with { MapId = MaterializeMapRequest(config.MapId, Console.Out, mapSeedSource)! };
        config.Validated();
        int parallelism = serial ? 1 : config.EffectiveParallelism;

        Console.WriteLine($"跑局：{config.Count} 局，种子 {config.SeedStart}..{config.SeedAt(config.Count - 1)}，地图 {(config.MapPerMatch ? $"每局换图 {config.MapIdAt(0)}..{config.MapIdAt(config.Count - 1)}" : config.MapId)}，{config.PlayerCount} 人，并行度 {parallelism}，大回合上限 {config.MaxMajorRounds}，据点分值 {config.SiteValues}，匠人权重 {config.ArtisanWeight}，输出 {outDir}");
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
            // --file 与 --dir/--seed 是两种互斥的定位方式；混用必须报错，而不是静默忽略后者——
            // 静默忽略正是 strict-cli 要消灭的失败模式（design D2）。
            if (cli.Has("dir") || cli.Has("seed"))
            {
                throw new ArgumentException("replay 的 --file 与 --dir/--seed 是两种定位方式，不能混用。");
            }

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

        // 两个分支都已把 file / dir / seed 读过（混用在上面就报错了），所以结算不需要任何"声明放行"的白名单。
        cli.EnsureRecognized();
        MatchLog original = MatchLog.Read(path);
        Console.WriteLine($"回放 {path}：地图 {original.Header.MapId}，对局种子 {original.Header.Seed}，{original.Turns.Count} 小回合，{(original.IsFailed ? "失败局" : original.Result!.Reason)}");
        ReplayResult result = Replayer.Replay(original);
        Console.WriteLine(result);
        if (original.IsFailed && result.MapMismatch is null)   // 地图不一致时没有重跑，谈不上失败是否复现
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
        var options = new AnalysisOptions { IncludeContaminated = cli.Flag("include-contaminated") };
        string outPath = cli.Get("out", Path.Combine(dir, "report.txt"));
        cli.EnsureRecognized();   // 读完所有选项、读日志与写报告之前结算（strict-cli 2.4）
        List<MatchLog> logs = MatchLog.ReadDirectory(dir);
        BalanceReport report = BalanceAnalyzer.Analyze(logs, options);
        string text = ReportWriter.Render(report);
        Console.WriteLine(text);
        File.WriteAllText(outPath, text);
        Console.WriteLine($"报告已写入 {outPath}");
        return 0;
    }
}
