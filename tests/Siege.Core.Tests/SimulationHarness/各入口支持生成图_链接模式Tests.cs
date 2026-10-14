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
/// 终端版开新局、裸 link、每局换图、批量配对可重复（<c>--pairing-seed</c>）、回放重建链接、生成器变了、map 子命令打印与模拟链接。
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
        Assert.Contains("地图 link:12345（5 块隔绝的出生棋盘）", output, StringComparison.Ordinal);
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
        // tasks 3.0「play 在 link: 上能建局并跑起来」：选棋盘后锁定、开始第 1 大回合并轮到本机玩家；只公开同一块棋盘上的玩家，
        // 他人战场玩家的势力显示为不可见，棋盘只画本座位战场的外接矩形。
        (int code, string output, string err) = RunMain(null, new StringReader("2\nq\n"), "play", "--map", "link:12345", "--seed", "5", "--no-carry");
        Assert.True(code == 0, err);
        Assert.Contains("你锁定了 2 号棋盘", output, StringComparison.Ordinal);
        Assert.DoesNotContain("出生区锁定：", output, StringComparison.Ordinal);   // 不列出别人的旗
        Assert.Contains("其他战场（势力不可见）", output, StringComparison.Ordinal);
        Assert.Contains("════════ 轮到你了 ════════", output, StringComparison.Ordinal);

        // 只画本战场：列标行恰是 2 号棋盘的列，不是整张画布。
        MapData map = MapCatalog.Resolve("link:12345");
        BoundsOf(map, 1, out Coord origin, out int width, out _);
        string columns = "    " + string.Concat(Enumerable.Range(origin.X, width).Select(x => $" {new Coord(x, 0).Column,-2}"));
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
    public void 批量配对可重复()
    {
        // 规格 Scenario「批量配对可重复」：同一对局种子与同一 --pairing-seed 跑两次，配对事件与全部结算结果逐项相同；换一个配对种子则配对可以不同
        // （样本口径：在 1..20 里找到一个与 7 给出不同首轮配对的配对种子），且配对种子只影响配对。
        // 变异 MC-8：SeededPairingSource 忽略配对种子（只用对局种子）→ 找不到不同的配对，本测试红。
        ulong seed = FourZoneSeed.Value;
        RunConfig config = Config(turnLimit: 13) with { PairingSeed = 7 };
        MatchLog a = MatchSession.Create(config, seed).Run();
        MatchLog b = MatchSession.Create(config, seed).Run();
        Assert.Equal(a.DeterministicText(), b.DeterministicText());
        Assert.Equal(2, a.EventsOf(LogEventType.BattlefieldsLinked).Count);   // 样本口径：四人四区 → 两次链接

        string PairsOf(MatchLog log) => string.Join(";", log.EventsOf(LogEventType.PairingApplied).Select(e => string.Join(",", e.Pairing!.Pairs.Select(p => $"{p[0]}-{p[1]}"))));
        string first = PairsOf(a);
        ulong? other = Enumerable.Range(1, 20).Select(i => (ulong)i).Where(p => p != 7)
            .Cast<ulong?>()
            .FirstOrDefault(p => PairsOf(MatchSession.Create(Config(turnLimit: 12) with { PairingSeed = p }, seed).Run()) != first);
        Assert.NotNull(other);
        Assert.Contains("\"PairingSeed\":7", a.DeterministicText().Split('\n')[0], StringComparison.Ordinal);   // 配对种子写进首部配置
    }

    [Fact]
    public void 命令行配对种子写入配置记录()
    {
        // --pairing-seed 被严格命令行认领并写进 config.json 与日志首部；旧地图上给配对种子报错（只用于 link:）。
        string outDir = Path.Combine(SimFixtures.TempDir("link-pairing-seed-cli"), "out");
        (int code, _, string err) = RunMain(null, null, "run", "--out", outDir, "--seed", "1", "--count", "1", "--turn-limit", "2", "--difficulty", "Easy", "--serial", "--pairing-seed", "42");
        Assert.True(code == 0, err);
        Assert.Equal(42UL, RunConfig.FromJson(File.ReadAllText(Path.Combine(outDir, "config.json"))).PairingSeed);
        Assert.Equal(42UL, MatchLog.ReadDirectory(outDir).Single().Header.Config.PairingSeed);

        // retire-legacy-maps 1.8：共用配置改到 link:1 后不再是"旧地图"，这里显式写一个非 link: 标识，断言不变。
        Assert.Throws<ArgumentException>(() => (SimFixtures.Config() with { MapId = "siege-4p-base-v5", PairingSeed = 1 }).Validated());
    }

    [Fact]
    public void 回放重建链接()
    {
        // 规格 Scenario「回放重建链接」：回放一份含两次链接事件的日志，按记录依次执行链接，每次链接后的地图与原局逐格相同
        // （链接事件的地图内容摘要随确定性文本逐行比对）。配对 MUST 从日志读取：把日志里的首轮配对改成另一种合法结果，回放就按改后的配对链接、与原局分歧。
        // 变异 MC-9：Replayer 对链接模式不挂记录来源（回放走 SeededPairingSource）→ 改过配对的日志仍"一致"，本测试红。
        MatchLog original = TwoLinkLog.Value;
        Assert.Equal(2, original.EventsOf(LogEventType.BattlefieldsLinked).Count);   // 样本口径
        ReplayResult replay = Replayer.Replay(original);
        Assert.True(replay.Identical, replay.ToString());
        Assert.Equal(
            original.EventsOf(LogEventType.BattlefieldsLinked).Select(e => e.Link!.MapDigest),
            replay.Replayed.EventsOf(LogEventType.BattlefieldsLinked).Select(e => e.Link!.MapDigest));

        // 改写配对：候选 [a,b,c,d] 原为两对，换成另一种两两配法（仍合法）。
        LogEvent pairing = original.EventsOf(LogEventType.PairingApplied).First();
        int[] c = [.. pairing.Pairing!.Candidates];
        List<List<int>> pairs = pairing.Pairing.Pairs;
        List<List<int>> alternative = pairs.Any(p => p.SequenceEqual(new[] { c[0], c[1] })) ? [[c[0], c[2]], [c[1], c[3]]] : [[c[0], c[1]], [c[2], c[3]]];
        MatchLog tampered = SimFixtures.Clone(original);
        int index = tampered.Events.FindIndex(e => e.Seq == pairing.Seq);
        tampered.Events[index] = pairing with { Pairing = pairing.Pairing with { Pairs = alternative } };
        ReplayResult diverged = Replayer.Replay(tampered);
        Assert.False(diverged.Identical);
        Assert.NotEqual(
            original.EventsOf(LogEventType.BattlefieldsLinked).Select(e => e.Link!.MapDigest),
            diverged.Replayed.EventsOf(LogEventType.BattlefieldsLinked).Select(e => e.Link!.MapDigest));
    }

    [Fact]
    public void 生成器变了()
    {
        // 规格 Scenario「生成器变了」：回放时按标识重新生成的初始地图，其内容摘要与日志首部记录的不同 → 在首部报"地图不一致"并停止（不重跑）。
        MatchLog original = TwoLinkLog.Value;
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
        Assert.DoesNotContain("链接 #", output, StringComparison.Ordinal);
    }

    [Fact]
    public void map子命令模拟链接()
    {
        // 规格 Scenario「map 子命令模拟链接」：map --map link:1 --links 1-4,2-5 输出两次链接后的文本图与校验报告；结果与直接调生成器逐项相同。
        // 变异 MC-10：SimulateLinks 只执行第一项 → 输出没有"链接 #2"、可落子格数不符，本测试红。
        (int code, string output, string err) = RunMain(null, null, "map", "--map", "link:1", "--links", "1-4,2-5");
        Assert.True(code == 0, err);
        Assert.Contains("链接 #1：战场 出生区 1 + 出生区 4", output, StringComparison.Ordinal);
        Assert.Contains("链接 #2：战场 出生区 2 + 出生区 5", output, StringComparison.Ordinal);
        Assert.Contains("地图校验通过", output, StringComparison.Ordinal);

        MapData expected = LinkMapGenerator.Link(LinkMapGenerator.Link(MapCatalog.Resolve("link:1"), 0, 3, LinkSide.East).Map, 1, 4, LinkSide.East).Map;
        Assert.Contains($"可落子格 {expected.PlayableCount} ", output, StringComparison.Ordinal);
        Assert.Contains("已模拟 2 次链接", output, StringComparison.Ordinal);

        // 已合并的战场可用其中任一块棋盘指代：1-4 之后 4-2 即战场 1 与战场 2。
        (int chained, string chainedOut, string chainedErr) = RunMain(null, null, "map", "--map", "link:1", "--links", "1-4,4-2");
        Assert.True(chained == 0, chainedErr);
        Assert.Contains("链接 #2：战场 出生区 1 + 出生区 2", chainedOut, StringComparison.Ordinal);
    }

    [Fact]
    public void map子命令按阶段推断贴边_也接受显式贴边()
    {
        // 裁决 J8-8：不写贴边时，前面互不重叠的单棋盘配对按首轮东贴，连到已合并战场的按决赛北贴；也接受 1-4E,2-5E,1-2N 的显式写法，结果相同；
        // 每次链接打印平移向量，最后打印拼接后的文本图与校验报告。显式 N 可让两个单棋盘战场北贴（3+1 的决赛）。
        (int code, string inferred, string err) = RunMain(null, null, "map", "--map", "link:1", "--links", "1-4,2-5,1-2");
        Assert.True(code == 0, err);
        Assert.Equal(2, CountOf(inferred, "贴东（推断）"));
        Assert.Equal(1, CountOf(inferred, "贴北（推断）"));
        Assert.Equal(3, CountOf(inferred, "  平移向量："));
        Assert.Contains("地图校验通过", inferred, StringComparison.Ordinal);

        MapData expected = MapCatalog.Resolve("link:1");
        foreach ((int x, int y, LinkSide side) in new[] { (0, 3, LinkSide.East), (1, 4, LinkSide.East), (0, 1, LinkSide.North) })
        {
            expected = LinkMapGenerator.Link(expected, x, y, side).Map;
        }

        Assert.Contains($"地图 link:1  {expected.Width}×{expected.Height}", inferred, StringComparison.Ordinal);

        (int code2, string spelled, string err2) = RunMain(null, null, "map", "--map", "link:1", "--links", "1-4E,2-5E,1-2N");
        Assert.True(code2 == 0, err2);
        Assert.Equal(0, CountOf(spelled, "（推断）"));
        Assert.Equal(Tail(inferred), Tail(spelled));   // 拼接后的文本图与校验报告逐行相同

        (int code3, string north, string err3) = RunMain(null, null, "map", "--map", "link:1", "--links", "1-2N");
        Assert.True(code3 == 0, err3);
        Assert.Contains("贴北；", north, StringComparison.Ordinal);
        MapData final = LinkMapGenerator.Link(MapCatalog.Resolve("link:1"), 0, 1, LinkSide.North).Map;
        Assert.Contains($"地图 link:1  {final.Width}×{final.Height}", north, StringComparison.Ordinal);

        static int CountOf(string text, string what) => (text.Length - text.Replace(what, string.Empty, StringComparison.Ordinal).Length) / what.Length;

        static string Tail(string text) => text[text.IndexOf("已模拟", StringComparison.Ordinal)..];
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
        for (int zone = 0; zone < 5; zone++)
        {
            BoardPlate plate = map.BirthBoard(zone);
            int publicRelics = map.RelicCells.Count(r => r.Value.Zone == RelicZone.Contested && plate.Contains(r.Key));
            contested += publicRelics;
            string name = plate.Theme!.Value.DisplayName();
            names.Add(name);
            Assert.Contains($"  {zone + 1} 号棋盘 · {name} · {plate.Width}×{plate.Height}  左下角 {plate.Origin.ToNotation()}  公共信物 {publicRelics}", output, StringComparison.Ordinal);
        }

        Assert.Equal(new[] { "林地", "栅栏阵", "河流", "混合", "高台" }, names.Order(StringComparer.Ordinal));
        // 样本口径：各棋盘公共信物数之和等于全图公共区信物数，且 ≥ 2（否则按棋盘过滤与否测不出来）。
        Assert.Equal(map.RelicCells.Count(r => r.Value.Zone == RelicZone.Contested), contested);
        Assert.True(contested >= 2, $"link:1 公共信物 {contested} 个；每块至多 1 个，≥ 2 时全图数才不等于任何一块的数");
    }

    [Theory]
    [InlineData("link:1", "1-4X", "编号 1–5")]
    [InlineData("siege-4p-base-v5", "1-2", "不受支持")]   // retire-legacy-maps：旧标识在目录先报错（原断言"只用于链接图"）
    [InlineData("link:1", "1-1", "已在同一战场")]
    [InlineData("link:1", "1-4,4-1", "已在同一战场")]
    [InlineData("link:1", "1-6", "编号 1–5")]
    [InlineData("link:1", "1:4", "编号 1–5")]
    public void map子命令的链接参数校验(string mapId, string links, string message)
    {
        (int code, _, string err) = RunMain(null, null, "map", "--map", mapId, "--links", links);
        Assert.Equal(1, code);
        Assert.Contains(message, err, StringComparison.Ordinal);
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
