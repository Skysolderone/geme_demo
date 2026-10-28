using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Play;

// retire-legacy-maps：仓库根目录改取 TestPaths.RepoRoot()（原 FrontierFixtures.RepoRoot，随旧地图夹具删除而原样迁出）。
namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// frontier-map tasks 2.4 / design D9：面向人的出生区编号与终端区号底色不再借用玩家色、支持到 8 个区。
/// 规格：simulation-harness「各入口按地图标识选图」——面向人的出生区编号显示 MUST 支持到该地图的出生区数，MUST NOT 假定不超过 4。
/// </summary>
public class 出生区编号显示Tests
{
    [Fact]
    public void 六区图的插旗棋盘快照()
    {
        // 期望行由夹具的平台摆放手工写出（不经渲染器）：
        //   第 1 行（y=0）：1 号台 A–E、台外 F–P、2 号台 Q–U；
        //   第 14 行（y=13）：3 号台 A–E、台外 F–H、6 号台 J–N（夹具的平台内已无营帐，map-generator 裁决 18）、台外 O、4 号台 P–T、台外 U。
        // retire-legacy-rules 1.6（R8）：改以链接模式建局（标识 link:90006，六个平台即六块 5×5 出生棋盘）。链接模式插旗阶段终端只画出生棋盘的外接矩形（y 0–17，
        // 最上两行岩石在外）、每块棋盘整块画区号占位（台内信物格不另画），台外留空——所以台外由"."改为空白、行数 20 → 18、每区格数 24 → 25。
        // retire-legacy-maps 1.8：夹具原为 FrontierFixtures.Map()（边疆档、经公开 MatchFlow.Create 校验建局）；该夹具随边疆档删除，
        // 同一张 20×20 六平台图原样搬进本类（SixZoneMap，去掉边疆档规格档），按手写夹具的入口 CreateUnvalidated 建局（信物按同一种子生成）。断言不变。
        MapData map = SixZoneMap();
        MatchFlow match = LinkModeFixtures.RequireLink(MatchFlow.CreateUnvalidated(
            map, new GameSeed(3), TestMaps.All, LinkModeFixtures.PlainOptions(MatchOptions.Immediate)));
        var output = new StringWriter();

        new BoardRenderer(output).Board(match.Publish(), TestMaps.P0, zones: true);

        string[] lines = [.. output.ToString().Split('\n').Select(l => l.TrimEnd('\r'))];
        Assert.Equal("  1  1  1  1  1  1                                2  2  2  2  2  1", lines.Single(l => l.StartsWith("  1 ", StringComparison.Ordinal)));
        Assert.Equal(" 14  3  3  3  3  3           6  6  6  6  6     4  4  4  4  4     14", lines.Single(l => l.StartsWith(" 14 ", StringComparison.Ordinal)));

        // 六个区号都出现，且格数 = 平台 25 格（占位整块画区号）；没有 7 以上的区号。
        string[] boardRows = [.. lines.Where(l => l.Length > 4 && char.IsDigit(l[2]) && l[3] == ' ')];
        Assert.Equal(18, boardRows.Length);
        string cells = string.Concat(boardRows.Select(l => l[4..(4 + (20 * 3))]));
        for (int zone = 1; zone <= 6; zone++)
        {
            Assert.Equal(25, Enumerable.Range(0, cells.Length / 3).Count(i => cells.Substring(i * 3, 3) == $" {zone} "));
        }

        Assert.DoesNotContain(" 7 ", cells, StringComparison.Ordinal);
    }

    [Fact]
    public void 区号底色是中性色不借用玩家色()
    {
        // D9：未被选的平台用中性色。区号只在插旗阶段显示，此时没有任何平台有主人；6 个区时"第 z 区 = 第 z 名玩家的颜色"会把 5、6 号台染成玩家 1、2 的颜色。
        // 变异 M-A16：BoardRenderer 改回 Ink(…, ColorOf(new PlayerId(z))) → 本测试红（源码扫描那条）。
        for (int p = 0; p < 8; p++)
        {
            Assert.NotEqual(BoardRenderer.ZoneColor, BoardRenderer.ColorOf(new PlayerId(p)));
        }

        string source = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "src", "Siege.Sim", "Play", "BoardRenderer.cs"));
        Assert.True(source.Length > 8_000, "样本口径：BoardRenderer.cs 过短。");
        Assert.DoesNotContain("new PlayerId(z)", source, StringComparison.Ordinal);
        Assert.Contains("BirthZoneLabel.Number(z),2} \", ZoneColor)", source, StringComparison.Ordinal);   // 反面：区号那一格确实用中性色上色（map-expand-25 段 B：两位编号右对齐成 3 个字符）
    }

    [Fact]
    public void 各区胜率段在六区下输出六行()
    {
        // 合成 60 局 4 人日志，选区轮转覆盖 6 个区：第 i 局四名玩家占 i、i+1、i+2、i+3（模 6），胜者恒为 0 号玩家。
        // 每个区被选 40 次，其中作为 0 号玩家的区 10 次 → 各区胜率 10/40 = 25% = 基线 1/4，不显著。
        // 基线若按 1/区数 = 1/6 ≈ 16.7% 取（改动前 Math.Max(zones, players) 的算法），25% 的 Wilson 区间下界 ≈ 14.2% 仍含它，
        // 所以另放一条直接断言基线数值。变异 M-A17：基线改回 1.0 / Math.Max(zones, players) → Baseline 断言红。
        MatchLog[] logs = [.. Enumerable.Range(0, 60).Select(i => SimFixtures.Synthetic(
            (ulong)i + 1,
            [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])],
            [],
            SimFixtures.ResultOf(6, [0]),
            zones: [i % 6, (i + 1) % 6, (i + 2) % 6, (i + 3) % 6]))];

        BirthZoneSection section = BalanceAnalyzer.Analyze(logs).BirthZones;

        Assert.Equal(0.25, section.Baseline);
        Assert.Equal([0, 1, 2, 3, 4, 5], section.All.Select(z => z.Zone));
        Assert.All(section.All, z => Assert.Equal((10, 40), (z.WinRate.Successes, z.WinRate.Trials)));
        Assert.All(section.All, z => Assert.False(z.Significant));

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze(logs));
        string[] zoneLines = [.. text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("  - 出生区 ", StringComparison.Ordinal))];
        Assert.True(zoneLines.Length == 6, $"各区胜率段有 {zoneLines.Length} 行。");   // relic-redesign W1：原"收敛局 / 未收敛局"两段随旧信物生成删除，只剩「全部局」一段
        for (int n = 1; n <= 6; n++)
        {
            Assert.StartsWith($"  - 出生区 {n}：胜率 25.0% (10/40", zoneLines[n - 1], StringComparison.Ordinal);
        }

        Assert.Contains("基线 25.0%", text, StringComparison.Ordinal);
    }

    /// <summary>六个 5×5 平台的左下角（出生区 0–5 依次）。</summary>
    private static readonly Coord[] SixZoneOrigins = [new(0, 0), new(15, 0), new(0, 13), new(14, 12), new(7, 1), new(8, 11)];

    /// <summary>
    /// 20×20 六平台手写图（retire-legacy-maps：自已删除的 FrontierFixtures.Map 原样搬来，只去掉边疆档规格档）：最上两行岩石，6 个 5×5 平台故意不对称，
    /// 每平台 1 个出生区信物 + 10 个公共信物。
    /// retire-legacy-rules 1.6：标识改为 link:90006、规格档链接、六个平台登记为六块出生棋盘（链接模式只配 link: 标识，插旗阶段终端按出生棋盘画占位），其余不变。
    /// </summary>
    private static MapData SixZoneMap() =>
        new()
        {
            Id = "link:90006:4",
            Width = 20,
            Height = 20,
            MaxPlayers = 4,
            Obstacles = [.. TestMaps.Rect(0, 18, 20, 2)],
            Profile = MapProfile.Link,
            Boards = [.. SixZoneOrigins.Select(o => new BoardPlate(o, 5, 5, BoardPlateKind.Birth))],
            BirthZones =
            [
                [.. TestMaps.Rect(0, 0, 5, 5)],
                [.. TestMaps.Rect(15, 0, 5, 5)],
                [.. TestMaps.Rect(0, 13, 5, 5)],
                [.. TestMaps.Rect(14, 12, 5, 5)],
                [.. TestMaps.Rect(7, 1, 5, 5)],
                [.. TestMaps.Rect(8, 11, 5, 5)],
            ],
            ChokePoints = [new Coord(7, 9), new Coord(13, 9)],
            CentralEntrance = new Coord(10, 9),
        };

    [Theory]
    [InlineData(0, 1, "出生区 1")]
    [InlineData(5, 6, "出生区 6")]
    [InlineData(7, 8, "出生区 8")]
    public void 对人编号支持到八(int index, int number, string label)
    {
        Assert.Equal(number, BirthZoneLabel.Number(index));
        Assert.Equal(label, BirthZoneLabel.Of(index));
    }
}
