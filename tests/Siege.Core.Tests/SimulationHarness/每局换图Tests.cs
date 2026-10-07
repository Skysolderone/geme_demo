using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Analysis;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// map-generator tasks 2.5（design D6）：批量"每局换图"——第 i 局用地图种子 起始 + i；批次配置记录写明；
/// 分析报告在该模式下按平台边长分组。规格：simulation-harness「各入口支持生成图」—— Scenario: 每局换图的批次。
/// retire-legacy-maps 段 B（design D2 / 已知歧义 3）：每局换图由 <c>gen:&lt;起始种子&gt;[:p&lt;平台数&gt;]</c> 改为
/// <c>board:&lt;起始种子&gt;[:p&lt;人数&gt;][:n&lt;棋盘数&gt;]</c>（人数与棋盘数不变，种子逐局加一）；原 gen: 写法开跑前报"已删除"。
/// "平台边长"即出生棋盘边长（5–7）。真实批次只走 CLI，这里用小局数验证机制。
/// </summary>
public class 每局换图Tests
{
    private static RunConfig Rotating(int count, string mapId = "board:100") =>
        SimFixtures.Config(count: count, seedStart: 1, turnLimit: 4) with { MapId = mapId, MapPerMatch = true };

    [Fact]
    public void 第i局的地图标识是起始种子加i_人数与棋盘数不变()
    {
        // 原名「第i局的地图标识是起始种子加i_平台数不变」（gen: 写法）。变异 RB-4：MapIdAt 每局都返回起始标识 → 本测试红。
        RunConfig four = Rotating(20);
        Assert.Equal(Enumerable.Range(100, 20).Select(n => $"board:{n}"), Enumerable.Range(0, 20).Select(four.MapIdAt));

        RunConfig three = Rotating(3, "board:18446744073709551613:p3:n7");
        Assert.Equal(
            ["board:18446744073709551613:p3:n7", "board:18446744073709551614:p3:n7", "board:18446744073709551615:p3:n7"],
            Enumerable.Range(0, 3).Select(three.MapIdAt));

        // 规范化：缺省段省略（4 人缺省棋盘数 7）。
        Assert.Equal("board:101", Rotating(2, "board:100:p4:n7").MapIdAt(1));

        // 不换图：恒为配置的地图标识。
        Assert.Equal("siege-2p-board-v1", (SimFixtures.Config(count: 3) with { MapId = "siege-2p-board-v1" }).MapIdAt(2));
    }

    [Fact]
    public void 每局换图要求带起始种子的棋盘图标识_开跑之前报错()
    {
        // 原名「每局换图要求带起始种子的生成图标识_开跑之前报错」。
        ArgumentException builtin = Assert.Throws<ArgumentException>(() => Rotating(2, "siege-2p-board-v1").Validated());
        Assert.Contains("board:<起始地图种子>", builtin.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Rotating(2, "board").Validated());
        Assert.Throws<FormatException>(() => Rotating(2, "board:1:p9").Validated());
        Assert.Throws<FormatException>(() => Rotating(2, "board:1:n99").Validated());
        Assert.Throws<ArgumentException>(() => Rotating(2, "board:18446744073709551615").Validated());

        // 原 gen: 写法（含裸 gen）与已删除的旧内置图：报"已删除"，并指向 board: 写法。
        foreach (string retired in new[] { "gen:100", "gen:1:p7", "gen", "siege-4p-base-v5" })
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() => Rotating(2, retired).Validated());
            Assert.Contains("已删除（retire-legacy-maps", ex.Message, StringComparison.Ordinal);
            Assert.Contains("board:<起始地图种子>", ex.Message, StringComparison.Ordinal);
        }

        Rotating(2).Validated();   // 反面：合法的起始标识通过

        // 会话自己解析不了"本局是第几局"：不给地图就拒绝，而不是悄悄用起始那张图。
        Assert.Throws<ArgumentException>(() => MatchSession.Create(Rotating(2), seed: 1));

        // 半份输出比没有输出更糟：非法配置不建输出目录。
        string dir = Path.Combine(SimFixtures.TempDir("rotate-invalid"), "out");
        Assert.Throws<ArgumentException>(() => BatchRunner.ExecuteToDirectory(Rotating(2, "gen:100"), dir, parallelism: 1));
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void 小批次每局换图_首部标识依次递增_配置记录写明_全部可回放()
    {
        // 变异 MB-10：BatchRunner 每局都用第 0 局的图 → 本测试红（首部标识全是 board:100:n8）。
        // retire-legacy-maps 段 B：起始标识由 gen:100:p7（7 个平台）改为 board:100:n8（4 人、8 块棋盘，出生区恒为 5）。
        string dir = SimFixtures.TempDir("rotate-batch");
        RunConfig config = Rotating(3, "board:100:n8");

        BatchSummary summary = BatchRunner.ExecuteToDirectory(config, dir, parallelism: 2);

        Assert.Equal((3, 0), (summary.Completed, summary.Failed));
        List<MatchLog> logs = MatchLog.ReadDirectory(dir);
        Assert.Equal(["board:100:n8", "board:101:n8", "board:102:n8"], logs.Select(l => l.Header.MapId));
        Assert.Equal(3, logs.Select(l => l.Header.MapDigest).Distinct().Count());
        Assert.All(logs, l => Assert.Equal(5, l.Header.ZoneCount));
        Assert.All(logs, l => Assert.Equal(MapFile.Digest(MapCatalog.Resolve(l.Header.MapId)), l.Header.MapDigest));

        // 批次配置记录：起始地图种子、人数与棋盘数（都在起始标识里）+ "每局换图"这一事实；每局首部的配置同样带着。
        string saved = File.ReadAllText(Path.Combine(dir, "config.json"));
        Assert.Contains("\"MapId\": \"board:100:n8\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"MapPerMatch\": true", saved, StringComparison.Ordinal);
        RunConfig reread = RunConfig.FromJson(saved);
        Assert.True(reread.MapPerMatch);
        Assert.Equal("board:102:n8", reread.MapIdAt(2));
        Assert.All(logs, l => Assert.True(l.Header.Config.MapPerMatch));
        Assert.All(logs, l => Assert.Equal("board:100:n8", l.Header.Config.MapId));

        // 回放：只凭日志文件。本局的图取首部的地图标识（不是配置里的起始标识）。变异 MB-11：Replayer 一律按配置的 MapId 解析 → 第 2、3 局报地图不一致。
        foreach (MatchLog log in logs)
        {
            ReplayResult replay = Replayer.Replay(log);
            Assert.True(replay.Identical, $"{log.Header.MapId}: {replay}");
        }

        // 串行与并行逐字节相同（换图不引入调度相关性）。
        List<MatchLog> serial = BatchRunner.Execute(config, parallelism: 1);
        Assert.Equal(logs.Select(l => l.DeterministicText()), serial.Select(l => l.DeterministicText()));
    }

    [Fact]
    public void 首部的平台边长取自对局本身_与生成器给出的各平台方块一致()
    {
        // 首部 ZoneSides = 各出生区格子外接矩形的较长边；棋盘图的出生区即出生棋盘，应等于地图棋盘清单里对应出生棋盘的长边（测试内独立来源）。
        // 变异 MB-12：SideOf 少加 1（把"跨度"当边长）→ 本测试红。
        // retire-legacy-maps 段 B：原以 gen:1:p8 起换 4 局、对照生成器报告的平台方块（MapGenFixtures，随 gen: 删除）；改为 board:1 起换 4 局、对照棋盘清单。
        List<MatchLog> logs = BatchRunner.Execute(Rotating(4, "board:1"), parallelism: 1);

        for (int i = 0; i < logs.Count; i++)
        {
            MapData map = MapCatalog.Resolve($"board:{1 + i}");
            int[] expected =
            [
                .. map.BirthZones.Select(zone => map.Boards
                    .Where(b => b.Kind == BoardPlateKind.Birth && b.Contains(zone.Order().First()))
                    .Select(b => Math.Max(b.Width, b.Height))
                    .Single()),
            ];
            Assert.Equal(expected, logs[i].Header.ZoneSides);
            Assert.All(logs[i].Header.ZoneSides!, side => Assert.InRange(side, 5, 7));
            Assert.Equal(logs[i].Header.ZoneSides, MatchLog.Parse(logs[i].DeterministicText()).Header.ZoneSides);
        }

        // 不换图的批次（哪怕是生成图）不写这一项。
        MatchLog single = MatchSession.Create(SimFixtures.Config(turnLimit: 4) with { MapId = "board:1" }, seed: 1).Run();
        Assert.Null(single.Header.ZoneSides);
    }

    [Fact]
    public void 每局换图的批次_报告按平台边长分组给出被选次数与胜率()
    {
        // 合成 10 局 4 人日志：每局 6 个平台，边长 [9,8,7,6,5,5]；第 i 局四人占 i、i+1、i+2、i+3（模 6）号台，胜者恒为 0 号玩家（占 i 号台）。
        // 手算：
        //   出现：边长 9/8/7/6 各 10 个，边长 5 共 20 个。
        //   被选：区 z 被选的局 = i ∈ {z, z-1, z-2, z-3}（模 6）→ 10 局里 i=0..9：
        //     区0: i∈{0,5,4,3}+{6,9}→ i=0,3,4,5,6,9 → 6；区1: i∈{1,0,5,4}+{6,7}→ 0,1,4,5,6,7 → 6；区2: i∈{2,1,0,5}+{6,7,8} → 0,1,2,5,6,7,8 → 7；
        //     区3: i∈{3,2,1,0}+{6,7,8,9} → 0,1,2,3,6,7,8,9 → 8；区4: i∈{4,3,2,1}+{7,8,9} → 1,2,3,4,7,8,9 → 7；区5: i∈{5,4,3,2}+{8,9} → 2,3,4,5,8,9 → 6。合计 40 ✓
        //   胜：胜者占 i mod 6 号台 → 区0: i=0,6 → 2；区1: 1,7 → 2；区2: 2,8 → 2；区3: 3,9 → 2；区4: 4 → 1；区5: 5 → 1。
        //   按边长：9 → 被选 6 胜 2；8 → 6/2；7 → 7/2；6 → 8/2；5（区 4、5）→ 13/2。
        // 变异 MB-13：分析端把边长分组的被选次数误填成出现次数 → 本测试红。
        int[] sides = [9, 8, 7, 6, 5, 5];
        MatchLog[] logs = [.. Enumerable.Range(0, 10).Select(i => WithSides(
            SimFixtures.Synthetic(
                (ulong)i + 1,
                [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])],
                [],
                SimFixtures.ResultOf(6, [0]),
                zones: [i % 6, (i + 1) % 6, (i + 2) % 6, (i + 3) % 6],
                zoneCount: 6),
            sides))];

        BalanceReport report = BalanceAnalyzer.Analyze(logs);
        PlatformSideSection bySide = Assert.IsType<PlatformSideSection>(report.BirthZones.BySide);

        Assert.Equal((10, 0), (bySide.Matches, bySide.Skipped));
        Assert.Equal(0.25, bySide.Baseline);
        Assert.Equal([5, 6, 7, 8, 9], bySide.Sides.Select(s => s.Side));
        Assert.Equal([20, 10, 10, 10, 10], bySide.Sides.Select(s => s.Offered));
        Assert.Equal([13, 8, 7, 6, 6], bySide.Sides.Select(s => s.Picks));
        Assert.Equal([2, 2, 2, 2, 2], bySide.Sides.Select(s => s.WinRate.Successes));
        Assert.Equal(40, bySide.Sides.Sum(s => s.Picks));

        string[] lines = [.. ReportWriter.Render(report).Split('\n').Select(l => l.TrimEnd('\r'))];
        Assert.Contains(lines, l => l.Contains("改按平台边长分组（纳入 10 局", StringComparison.Ordinal));
        string five = Assert.Single(lines, l => l.StartsWith("  - 边长 5：", StringComparison.Ordinal));
        Assert.StartsWith("  - 边长 5：胜率 15.4% (2/13", five, StringComparison.Ordinal);
        Assert.EndsWith("；被选 13 次（共出现 20 个）", five, StringComparison.Ordinal);
        Assert.Equal(5, lines.Count(l => l.StartsWith("  - 边长 ", StringComparison.Ordinal)));
        Assert.DoesNotContain(lines, l => l.StartsWith("  - 出生区 ", StringComparison.Ordinal));   // 按编号的胜率在换图批次里没有意义，不再列

        // 没出现过的边长同样占一行；首部没有平台边长的局（混进来的别的批次）整局排除并计数，不按标识重建地图去补。
        MatchLog[] mixed = [.. logs[..2], SimFixtures.Synthetic(99, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(6, [0]), zoneCount: 6)];
        PlatformSideSection fromMixed = BalanceAnalyzer.Analyze(mixed).BirthZones.BySide!;
        Assert.Equal((2, 1), (fromMixed.Matches, fromMixed.Skipped));
        Assert.Equal(8, fromMixed.Sides.Sum(s => s.Picks));
    }

    [Fact]
    public void 不换图的批次_报告仍按平台编号_没有边长一节()
    {
        BalanceReport report = BalanceAnalyzer.Analyze(SimFixtures.Sample.Value);

        Assert.Null(report.BirthZones.BySide);
        string text = ReportWriter.Render(report);
        Assert.Contains("  - 出生区 1：", text, StringComparison.Ordinal);
        Assert.DoesNotContain("边长", text, StringComparison.Ordinal);
    }

    private static MatchLog WithSides(MatchLog log, int[] sides) => new()
    {
        Header = log.Header with { ZoneSides = [.. sides], Config = log.Header.Config with { MapId = "board:100", MapPerMatch = true } },
        Turns = log.Turns,
        Events = log.Events,
        Result = log.Result,
    };
}
