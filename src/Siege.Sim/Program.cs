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
    /// 批量与终端入口的缺省地图（board-link-mode Q4 / 规格 map-selection「批量入口缺省」、simulation-harness「各入口按地图标识选图」）：未给地图选项时加载 <c>link:1</c>。
    /// 与 Core 的 <see cref="MapCatalog.DefaultId"/> 同为 <c>link:1</c>；<c>map</c> 调试子命令与批次配置的内部缺省同样是 <c>link:1</c>
    /// （retire-legacy-maps：原为 Legacy 夹具的 v5，实现裁决 C10，随旧地图删除）。
    /// </summary>
    internal const string DefaultMapId = "link:1";

    /// <summary>
    /// 玩家入口的地图门（board-link-mode 实现裁决 C1）：<c>run</c> / <c>play</c> 只接受链接图 <c>link:&lt;地图种子&gt;</c>。旧标识族（内置图、<c>gen:</c>、<c>board:</c>、
    /// 地图文件路径）与解析不了的标识一律报错并说明，MUST NOT 静默回落到缺省地图。判定只看标识前缀（<see cref="LinkMapId.IsLinkMap"/>），
    /// 格式错误的 <c>link:</c> 标识由随后的解析报出。段 D（实现裁决 D7）起 <see cref="MapCatalog.Resolve"/> 自身也只接受 <c>link:</c>，本门只是让入口先于解析给出更贴切的文案。
    /// </summary>
    internal static string RequireLinkMap(string mapId, string entry)
    {
        if (!LinkMapId.IsLinkMap(mapId))
        {
            throw new ArgumentException(
                $"{entry} 的地图标识 {mapId} 不受支持：只接受 {LinkMapId.Prefix}:<地图种子>（例如 {DefaultMapId}、{LinkMapId.Prefix}:12345；只写 {LinkMapId.Prefix} 即随机取一个地图种子）。"
                + "内置图、gen:、board: 与地图文件属于已移除的地图模式（retire-legacy-maps 已删除）。");
        }

        return mapId;
    }

    /// <summary>
    /// 裸 <c>gen</c> 的地图种子缺省取自时钟——整个批量 / 终端程序里读时钟取地图种子的只有这一处（图形版入口另有一处），规则内核永不读时钟。
    /// 时间戳经 <see cref="LinkMapId.FriendlySeed"/> 折成九位以内的短种子，与图形版（裸 <c>link</c> 与选图界面"换一张"）同一个折叠函数。
    /// </summary>
    private static ulong ClockMapSeed() => LinkMapId.FriendlySeed((ulong)System.Diagnostics.Stopwatch.GetTimestamp());

    /// <summary>
    /// 入口本体。<paramref name="mapSeedSource"/> 是"随机取一个地图种子"的来源：只在地图选项是裸 <c>gen</c> 时调用一次，
    /// <c>play</c> / <c>map</c> / <c>run</c> 三个子命令共用。生产入口传时钟；测试注入固定值，不留依赖墙钟的测试。
    /// <paramref name="playInput"/> / <paramref name="playOutput"/> 是 <c>play</c> 的测试接缝（<c>null</c> 即控制台）：<c>play</c> 开头设置控制台编码时，
    /// 运行时会丢弃经 <c>Console.SetIn</c> 换上的输入，脚本化的终端测试只能从这里注入。
    /// </summary>
    internal static int Execute(string[] args, Func<ulong> mapSeedSource, TextReader? playInput = null, TextWriter? playOutput = null)
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
                "play" => Play(cli, mapSeedSource, playInput ?? Console.In, playOutput ?? Console.Out),
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
        Console.WriteLine($"  Siege.Sim play [--seed <种子>] [--players <人数，链接模式恒为 4>] [--seat <你的座位>] [--difficulty <{Core.Ai.AiDifficultyNames.Usage}>] [--map <link:地图种子，缺省 {DefaultMapId}>] [--cell-limit <AI 候选格上限，0=不限，缺省按地图大小>]");
        Console.WriteLine("                [--profile <档案路径，缺省 %APPDATA%\\Siege\\profile.json>] [--no-carry（关闭带入带出，不读写档案）]");
        Console.WriteLine("  Siege.Sim map [--map <地图id或文件>] [--links <出生棋盘对，如 1-4,2-5 或 1-4E,2-5E,1-2N（E 东贴 / N 北贴，不写按阶段推断），只用于 link:>] [--out <导出的地图文件>]（生成图只打印；给 --out 才导出；旧标识族仍可打印，夹具用途）");
        Console.WriteLine("  Siege.Sim run --out <目录> [--config <json>] [--seed <首个种子>] [--count <局数>] [--parallel <并行度|0=核数>]");
        Console.WriteLine($"                [--map <link:地图种子，缺省 {DefaultMapId}>] [--players <人数，链接模式恒为 4>] [--difficulty <{Core.Ai.AiDifficultyNames.Usage}>] [--turn-limit <小回合数截断，默认 600，0=不截断>]");
        Console.WriteLine($"                [--artisan-weight <匠人征募权重，默认 10>] [--cell-limit <AI 候选格上限，0=不限，缺省按地图大小>] [--pass-threshold <AI 停手阈值，非负整数，缺省 {Core.Ai.AiSearchConfig.DefaultPassThreshold}（v2-recalibration 在内容集 V2 上复核的校准值）>]（AI 权重只能经 --config 的 Players[].Weights 指定，整表生效；优先级：配置文件的权重 > 地图专属覆盖（按地图标识登记，AI 配置而非规则） > 缺省表；同时给 --difficulty / --players 会重建玩家列表、丢弃配置文件里的权重，改按地图覆盖、再按缺省表取值）");
        Console.WriteLine("                [--pairing-seed <配对种子，非负整数；缺省由对局种子派生，同一批次可重复对比>]");
        Console.WriteLine("                [--carry-in <每名 AI 的带入数量 0|1，缺省 0 = 关闭带入带出；1 = 每名 AI 从 carry-ai 子流随机带入 1 件补给，结算只写日志、不读写档案>]");
        Console.WriteLine("                [--map-per-match（每局换一张图：--map link:<起始地图种子>，第 i 局用 起始 + i）]");
        Console.WriteLine("                [--retention <SnapshotsOnly|Full>] [--sample-permille <千分比>] [--gzip] [--serial]");
        Console.WriteLine($"  地图标识：play / run / replay 只接受 link:<地图种子>（缺省 {DefaultMapId}）；只写 link 即随机取一个地图种子并打印完整标识。");
        Console.WriteLine("            map 子命令另可打印旧标识族（内置图 / 地图文件 / gen: / board:，测试夹具用途）。");
        Console.WriteLine("  Siege.Sim replay --file <match-*.jsonl>   或   replay --dir <目录> --seed <十六进制种子>（只回放 link: 对局的日志）");
        Console.WriteLine("  Siege.Sim analyze --dir <目录> [--include-contaminated] [--review-defeats] [--out <报告文件>]");
        Console.WriteLine("    --review-defeats：按日志回放每局，复盘整轮 Pass 结算的落败者与决胜一手（耗时约等于跑局本身；缺省不做）");
    }

    // ---------- play ----------

    private static int Play(CommandLine cli, Func<ulong> mapSeedSource, TextReader input, TextWriter output)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;
        ulong? seed = cli.Has("seed") ? cli.GetUInt64("seed", 0) : null;
        // 难度名称经 Core 的唯一解析（expert-lookahead D10）：数字、未知名称在开局之前报错并列出可用名称，不回落到标准。
        var difficulty = Core.Ai.AiDifficultyNames.Parse(cli.Get("difficulty", nameof(Core.Ai.AiDifficulty.Standard)));
        int? players = cli.Has("players") ? cli.GetInt("players", 0) : null;   // 不给人数 = 4；给非 4 的值按 S1 报错（retire-legacy-maps 4b.3）
        int seat = cli.GetInt("seat", 1);
        string? mapId = cli.GetOrNull("map");
        int? cellLimit = cli.Has("cell-limit") ? cli.GetInt("cell-limit", 0) : null;
        Core.Carry.CarryProfileStore? profile = PlayProfile(cli);   // 只解析、不读写档案；读档在选图之后、插旗之前（PlayCommand）
        cli.EnsureRecognized();   // 读完所有选项、开局之前结算（strict-cli 2.4）
        // board-link-mode C1：不给地图选项即 link:1；只接受 link:，旧标识族报错（在任何取种子与解析之前）。
        mapId = RequireLinkMap(string.IsNullOrWhiteSpace(mapId) ? DefaultMapId : mapId.Trim(), "play");
        mapId = MaterializeMapRequest(mapId, output, mapSeedSource);
        MapData map = MapCatalog.Resolve(mapId);   // 未知标识在开局前报错并列出可用标识，不回落到缺省地图（frontier-map D5）
        return Siege.Sim.Play.PlayCommand.Run(seed, players, seat, difficulty, input, output, map, cellLimit, profile: profile);
    }

    /// <summary>
    /// play 的档案选项（carry-in-out D10）：缺省开启带入带出、读写缺省档案（<see cref="Core.Carry.CarryProfileStore.DefaultPath"/>）；
    /// <c>--profile &lt;路径&gt;</c> 改用其他文件；<c>--no-carry</c> 关闭带入带出、不读写任何档案（返回 <c>null</c>）。严格解析：
    /// <c>--no-carry</c> 不带值（后面跟了非选项的词即报错，不静默当成未给），<c>--profile</c> 必须给路径，两者不能同时给出。
    /// 只构造存取、不做任何 I/O。
    /// </summary>
    internal static Core.Carry.CarryProfileStore? PlayProfile(CommandLine cli)
    {
        ArgumentNullException.ThrowIfNull(cli);
        string? noCarry = cli.GetOrNull("no-carry");
        string? path = cli.GetOrNull("profile");
        if (noCarry is not null && noCarry != "true")
        {
            throw new ArgumentException($"--no-carry 是开关，不带值（给出了 {noCarry}）。");
        }

        if (path == "true")
        {
            throw new ArgumentException("--profile 需要档案路径：--profile <路径>。");
        }

        if (noCarry is not null && path is not null)
        {
            throw new ArgumentException("--no-carry 关闭带入带出、不读写档案，不能与 --profile 同时给出。");
        }

        // 档案损坏时备份文件名要带本地时间：Core 不读时钟，由入口注入。这是 Sim 里唯一一处取墙钟，只进备份文件名，不参与任何对局、日志或配置记录。
        return noCarry is not null ? null : new Core.Carry.CarryProfileStore(path ?? Core.Carry.CarryProfileStore.DefaultPath(), TimeProvider.System.GetLocalNow);
    }

    /// <summary>
    /// 裸 <c>link</c>（"随机取一个地图种子"）：规则内核不读时钟，取种子只在入口最外层做。取到后拼成完整标识、打印给用户，再交给
    /// <see cref="MapCatalog"/>；裸 <c>link</c> MUST NOT 进入 Core，也 MUST NOT 写进任何记录（配置记录、日志首部）。其余标识原样返回，
    /// 此时 <paramref name="mapSeedSource"/> 不被调用。retire-legacy-maps：裸 <c>gen</c> / 裸 <c>board</c> 的取种子分支随旧生成器删除（旧标识由地图门 / 目录报错）。
    /// </summary>
    internal static string? MaterializeMapRequest(string? mapId, TextWriter output, Func<ulong> mapSeedSource)
    {
        if (LinkMapId.IsBareRequest(mapId))
        {
            // 裸 link（board-link-mode / 规格 simulation-harness「各入口支持生成图」）：同一个取种子来源，拼成链接图的完整标识。
            string linkId = LinkMapId.Format(mapSeedSource());
            output.WriteLine($"随机取了一个地图种子：本次地图为 {linkId}（用 --map {linkId} 可重开同一张图）");
            return linkId;
        }

        return mapId;
    }

    // ---------- map ----------

    /// <summary>
    /// 地图工具：打印一张链接图（高度 / 地表 / 桥 / 栅栏 / 信物 / 出生区与距离表、校验结果与报告项）。<c>--map</c> 只接受 <c>link:</c>、缺省 <c>link:1</c>；
    /// <c>--links</c> 模拟链接序列；<c>--out</c> 把这张图另存为指定文件。retire-legacy-maps：旧标识族（内置图、<c>gen:</c>、<c>board:</c>、地图文件路径）与内置图的
    /// 权威文件导出（<c>maps/</c>）随旧地图删除，旧标识与其他入口一样经目录报错。
    /// </summary>
    private static int ExportMap(CommandLine cli, Func<ulong> mapSeedSource)
    {
        string? mapId = cli.GetOrNull("map");
        string? outPath = cli.GetOrNull("out");
        string? links = cli.GetOrNull("links");
        cli.EnsureRecognized();   // map 只认 --map / --links / --out；结算在导出之前（strict-cli 2.4）
        mapId = MaterializeMapRequest(mapId, Console.Out, mapSeedSource);
        MapData map = MapCatalog.Resolve(mapId);   // 只认 link:，缺省 link:1；旧标识族在这里报错（retire-legacy-maps）
        if (links is not null)
        {
            map = SimulateLinks(map, links);
        }

        MapValidationResult result = MapValidator.Validate(map);
        TerrainData terrain = map.TerrainData;
        Coord[] all = [.. map.AllCoords()];

        Console.WriteLine($"地图 {map.Id}  {map.Width}×{map.Height}");
        Console.WriteLine(
            $"可落子格 {map.PlayableCount}   岩石 {map.Obstacles.Count}   深水 {all.Count(c => map.SurfaceAt(c) == Surface.DeepWater)}（其中桥 {terrain.Bridges.Count}）"
            + $"   栅栏 {terrain.Fences.Count}   林地 {all.Count(c => map.SurfaceAt(c) == Surface.Forest)}   土路 {all.Count(c => map.SurfaceAt(c) == Surface.Road)}"
            + string.Concat(new[] { Surface.Desert, Surface.Marsh, Surface.Crag, Surface.Shallows }
                .Select(s => (s, n: all.Count(c => map.SurfaceAt(c) == s)))
                .Where(x => x.n > 0)
                .Select(x => $"   {x.s.DisplayName()} {x.n}")));
        Console.WriteLine(
            "可落子格按高度 h0/h1/h2 = "
            + string.Join("/", Enumerable.Range(0, TerrainData.MaxHeight + 1).Select(h => all.Count(c => map.IsPlayable(c) && map.HeightAt(c) == h))));
        Console.WriteLine(
            $"出生区 {map.BirthZones.Length} 个，各 "
            + string.Join("/", map.BirthZones.Select(z => z.Count(map.IsPlayable)))
            + " 个可落子格，高度 "
            + string.Join("/", map.BirthZones.Select(z => string.Join(",", z.Select(map.HeightAt).Distinct().Order()))));
        Console.WriteLine($"信物格 {map.RelicCells.Count}（出生区 {map.RelicCells.Count(r => r.Value.Zone == RelicZone.BirthZone)}，公共区 {map.RelicCells.Count(r => r.Value.Zone == RelicZone.Contested)}）");
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
            // 每列三个字符宽：双字母列标（第 26 列起）只是少补一个空格。
            Console.Write($"{new Coord(x, 0).Column,-3}");
        }

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"每格两位：首位是高度 0/1/2，次位是标记。## 岩石  ~~ 深水  = 桥  1-{BirthZoneLabel.Number(map.BirthZones.Length - 1)} 出生区  r 出生区信物  o 公共信物  R 公共高档信物");
        Console.WriteLine("@ 中央入口  ^ 咽喉（与信物或桥同格时显示信物 / 桥的标记：入口若同时是高档信物显示 R，桥若同时是咽喉显示 =）  F 林地  . 土路  D 荒漠  M 沼泽  P 岩台  S 浅滩   格间 | 与行间 -- 为栅栏");

        // --out：把这张图导出到指定文件（map-generator 2.6）。生成图靠它落成普通地图文件——标识里有冒号，做不了 Windows 文件名。
        if (outPath is not null)
        {
            if (Path.GetDirectoryName(Path.GetFullPath(outPath)) is { Length: > 0 } outDir)
            {
                Directory.CreateDirectory(outDir);
            }

            File.WriteAllText(outPath, MapFile.ToJson(map));
            Console.WriteLine();
            Console.WriteLine($"已导出 {outPath}");
        }

        return 0;
    }

    /// <summary>
    /// <c>map --links 1-4,2-5</c>（规格 simulation-harness「map 子命令模拟链接」，段 F.3 / 裁决 J8-8）：在链接图的初始状态上依次执行给定的拼接，打印每一步（含平移向量），返回最终地图。
    /// 每一项是两块出生棋盘的编号（面向人 1–5）；执行时换算成各自所在战场的标识（= 战场内最小出生棋盘编号），所以已合并的战场可用其中任一块棋盘指代。
    /// 贴边：可显式写在项尾（<c>1-4E</c> 东贴、<c>1-2N</c> 北贴）；不写时按配对阶段推断——同一串里前面互不重叠的单棋盘配对视为首轮（东贴），
    /// 一旦某项连到已合并的战场即进入决赛（北贴），之后不写贴边的项都按北贴。
    /// 只用于链接图；两块棋盘已在同一战场、编号越界、格式不对都报错。拼接的几何与校验全部由 <see cref="LinkMapGenerator.Link(MapData, int, int, LinkSide)"/> 给出，这里不另算。
    /// </summary>
    internal static MapData SimulateLinks(MapData map, string links)
    {
        if (!LinkMapId.IsLinkMap(map.Id))
        {
            throw new ArgumentException($"--links 只用于链接图 {LinkMapId.Prefix}:<地图种子>，当前地图为 {map.Id}。");
        }

        string[] items = links.Split(',', StringSplitOptions.TrimEntries);
        int step = 0;
        bool final = false;
        foreach (string item in items)
        {
            LinkSide? explicitSide = item.EndsWith('E') ? LinkSide.East : item.EndsWith('N') ? LinkSide.North : null;
            string pair = explicitSide is null ? item : item[..^1];
            string[] ends = pair.Split('-', StringSplitOptions.TrimEntries);
            if (ends.Length != 2
                || !int.TryParse(ends[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int a)
                || !int.TryParse(ends[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int b)
                || a < 1 || b < 1 || a > map.BirthZones.Length || b > map.BirthZones.Length)
            {
                throw new ArgumentException(
                    $"--links 的每一项须为两块出生棋盘的编号 1–{map.BirthZones.Length}，写作 1-4，可在项尾加 E（东贴）/ N（北贴），多项用逗号分隔（如 1-4,2-5 或 1-4E,2-5E,1-2N）；无法解析 \"{item}\"。");
            }

            Battlefields fields = Battlefields.Of(map);
            int x = fields.All.Single(f => f.BirthZones.Contains(a - 1)).Id;   // 输入解析：面向人的 1 起编号 → 内部 0 起
            int y = fields.All.Single(f => f.BirthZones.Contains(b - 1)).Id;
            if (x == y)
            {
                throw new ArgumentException($"--links 第 {step + 1} 项 {item}：出生棋盘 {a} 与 {b} 已在同一战场 {BirthZoneLabel.Of(x)}，不能再链接。");
            }

            // 阶段推断：连到已合并战场即进入决赛，此后都按北贴（显式贴边优先）。
            final |= fields.ById(x)!.BirthZones.Length > 1 || fields.ById(y)!.BirthZones.Length > 1;
            LinkSide side = explicitSide ?? (final ? LinkSide.North : LinkSide.East);
            LinkResult linked = LinkMapGenerator.Link(map, x, y, side);
            map = linked.Map;
            step++;
            Console.WriteLine($"链接 #{step}：战场 {BirthZoneLabel.Of(linked.Anchor)} + {BirthZoneLabel.Of(linked.Mover)}，贴{(side == LinkSide.East ? "东" : "北")}{(explicitSide is null ? "（推断）" : string.Empty)}；地图版本 {map.MapVersion}；{linked.Summary}");
            Console.WriteLine($"  平移向量：{string.Join("  ", linked.Shifts.Select(s => $"{BirthZoneLabel.Of(s.Battlefield)} ({s.Dx},{s.Dy})"))}");
        }

        Console.WriteLine($"已模拟 {step} 次链接。棋盘清单：{string.Join("  ", map.Boards.Select(p => $"{p.Kind}@{p.Origin.ToNotation()} {p.Width}×{p.Height}"))}");
        Console.WriteLine();
        return map;
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
            : c == map.CentralEntrance ? '@'
            : map.HasBridge(c) ? '='
            : map.ChokePoints.Contains(c) ? '^'
            : SurfaceMark(map.SurfaceAt(c)) is char s ? s
            : map.BirthZoneOf(c) is { } z ? (char)('0' + BirthZoneLabel.Number(z))
            : ' ';
        return $"{map.HeightAt(c)}{mark}";
    }

    /// <summary>文本图的地表标记（8 种地表全覆盖；草地无标记，深水在上面已画成 ~~ / 桥）。</summary>
    private static char? SurfaceMark(Surface surface) => surface switch
    {
        Surface.Grass or Surface.DeepWater => null,
        Surface.Road => '.',
        Surface.Forest => 'F',
        Surface.Desert => 'D',
        Surface.Marsh => 'M',
        Surface.Crag => 'P',
        Surface.Shallows => 'S',
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, "未知地表。"),
    };

    // ---------- run ----------

    private static int Run(CommandLine cli, Func<ulong> mapSeedSource)
    {
        string outDir = cli.GetOrNull("out") ?? throw new ArgumentException("run 需要 --out <目录>。");
        string? configText = cli.GetOrNull("config") is { } file ? File.ReadAllText(file) : null;
        RunConfig config = configText is not null ? RunConfig.FromJson(configText) : new RunConfig();

        config = RebuildPlayers(
            config,
            cli.Has("players") ? cli.GetInt("players", config.PlayerCount) : null,
            cli.Has("difficulty") ? Core.Ai.AiDifficultyNames.Parse(cli.Get("difficulty", config.Players[0].Difficulty.ToString())) : null);   // 同一处严格解析（D10）

        config = config with
        {
            // board-link-mode C1：命令行与配置文件都没给地图 → link:1（retire-legacy-maps：RunConfig 自身的缺省也已是 link:1）。
            MapId = cli.GetOrNull("map") ?? (configText is not null && DeclaresKey(configText, nameof(RunConfig.MapId)) ? config.MapId : DefaultMapId),
            MapPerMatch = cli.Flag("map-per-match") || config.MapPerMatch,
            SeedStart = cli.GetUInt64("seed", config.SeedStart),
            Count = cli.GetInt("count", config.Count),
            Parallelism = cli.GetInt("parallel", config.Parallelism),
            TurnLimit = cli.GetInt("turn-limit", config.TurnLimit),
            ArtisanWeight = cli.GetInt("artisan-weight", config.ArtisanWeight),
            CandidateCellLimit = cli.Has("cell-limit") ? cli.GetInt("cell-limit", 0) : config.CandidateCellLimit,
            PassThreshold = cli.Has("pass-threshold") ? cli.GetInt("pass-threshold", 0) : config.PassThreshold,
            PairingSeed = cli.Has("pairing-seed") ? cli.GetUInt64("pairing-seed", 0) : config.PairingSeed,
            CarryIn = cli.Has("carry-in") ? cli.GetInt("carry-in", 0) : config.CarryIn,
            EventRetention = Enum.Parse<EventRetention>(cli.Get("retention", config.EventRetention.ToString()), ignoreCase: true),
            FullEventSamplePermille = cli.GetInt("sample-permille", config.FullEventSamplePermille),
            Compress = cli.Flag("gzip") || config.Compress,
        };
        bool serial = cli.Flag("serial");
        cli.EnsureRecognized();   // 读完所有选项、创建输出目录与跑局之前结算（strict-cli 2.4；先于 Validated 以便未知选项优先报出）
        // 裸 link 在这里就落成完整标识：config.json 与日志首部不得出现裸 link（配置文件里写的裸 link 同样处理）。
        // 命令行 --map link --map-per-match：起始地图种子取自 mapSeedSource，落成 link:<起始> 后再校验，同样打印并记入 config.json。
        // （retire-legacy-maps：原文针对已删除的裸 gen，口径同上。）
        config = config with { MapId = MaterializeMapRequest(RequireLinkMap(config.MapId.Trim(), "run"), Console.Out, mapSeedSource)! };
        config.Validated();
        // retire-legacy-maps 4b.3（L20）：原"命令行与配置文件都没给人数时取地图人数上限"删除——链接图恒为 4 人，与缺省的 4 名玩家相同；给非 4 的人数在建局时按 S1 报错。

        int parallelism = serial ? 1 : config.EffectiveParallelism;

        Console.WriteLine($"跑局：{config.Count} 局，种子 {config.SeedStart}..{config.SeedAt(config.Count - 1)}，地图 {(config.MapPerMatch ? $"每局换图 {config.MapIdAt(0)}..{config.MapIdAt(config.Count - 1)}" : config.MapId)}，{config.PlayerCount} 人，并行度 {parallelism}，小回合数截断 {config.TurnLimit}，匠人权重 {config.ArtisanWeight}，输出 {outDir}");
        BatchSummary summary = BatchRunner.ExecuteToDirectory(config, outDir, parallelism, Console.Out);
        Console.WriteLine();
        Console.WriteLine(summary.ToJson());
        return summary.Failed == 0 ? 0 : 3;
    }

    /// <summary>
    /// 命令行给了人数或难度（design D5）：按给定人数与难度重建玩家列表，丢弃配置文件里的逐玩家设置（含权重）；未给的一项沿用配置（人数 = 现有人数，难度 = 首名玩家的难度）。
    /// 两项都没给时原样返回。从 <c>run</c> 抽出的纯函数（board-link-mode 裁决 C7：旧地图的同一口径改由测试直接调用，不再经命令行）。
    /// </summary>
    internal static RunConfig RebuildPlayers(RunConfig config, int? players, Core.Ai.AiDifficulty? difficulty)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (players is null && difficulty is null)
        {
            return config;
        }

        Core.Ai.AiDifficulty chosen = difficulty ?? config.Players[0].Difficulty;
        return config with { Players = [.. Enumerable.Range(0, players ?? config.PlayerCount).Select(_ => new PlayerAiConfig { Difficulty = chosen })] };
    }

    /// <summary>配置文件的顶层是否写了某键（与 <see cref="RunConfig.FromJson"/> 同口径：键名区分大小写）。</summary>
    private static bool DeclaresKey(string json, string key)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
            && doc.RootElement.TryGetProperty(key, out _);
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
        if (!LinkMapId.IsLinkMap(original.Header.MapId))
        {
            // board-link-mode 实现裁决 C3：replay 命令只回放链接图的日志；测试内部的回放接口（Replayer）仍能回放旧日志（Legacy 夹具的回归护栏）。
            throw new ArgumentException(
                $"日志 {path} 的地图标识为 {original.Header.MapId}，属于已移除的地图模式（只接受 {LinkMapId.Prefix}:<地图种子> 对局的日志），replay 不再回放它。");
        }

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
        // ai-pass-review 2.3（裁决 P10）：战场落败复盘要按日志回放每局，改为显式开关 --review-defeats，缺省不做；等待者评估照常输出。
        var options = new AnalysisOptions { IncludeContaminated = cli.Flag("include-contaminated"), ReviewDefeats = cli.Flag("review-defeats") };
        string outPath = cli.Get("out", Path.Combine(dir, "report.txt"));
        cli.EnsureRecognized();   // 读完所有选项、读日志与写报告之前结算（strict-cli 2.4）
        List<MatchLog> logs = MatchLog.ReadDirectory(dir);
        if (logs.FirstOrDefault(l => !LinkMapId.IsLinkMap(l.Header.MapId)) is { } retired)
        {
            // match-telemetry「旧日志被拒绝」（与 replay 同口径，实现裁决 C3）：analyze 命令只分析链接图的日志；
            // 内部分析接口（BalanceAnalyzer）仍能读旧日志（Legacy 夹具的分析回归测试）。
            throw new ArgumentException(
                $"目录 {dir} 里种子 {retired.Header.Seed} 的日志地图标识为 {retired.Header.MapId}，属于已移除的地图模式（只接受 {LinkMapId.Prefix}:<地图种子> 对局的日志），analyze 不再分析它。");
        }

        BalanceReport report = BalanceAnalyzer.Analyze(logs, options);
        string text = ReportWriter.Render(report);
        Console.WriteLine(text);
        File.WriteAllText(outPath, text);
        Console.WriteLine($"报告已写入 {outPath}");
        return 0;
    }
}
