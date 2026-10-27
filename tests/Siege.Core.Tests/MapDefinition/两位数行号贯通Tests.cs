using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Logging;
using Siege.Sim.Play;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// frontier-map tasks 1.6：行号 10–32 在地图文件、对局存档、终端渲染、对局日志里贯通（裁决 7：25 列 × 28–32 行的竖长图）。
/// 既有地图最高 13 行，两位数行号只在第 10–13 行出现过；这里用一张 25×32 的合成图把它推到 32。
/// </summary>
public class 两位数行号贯通Tests
{
    private static readonly PlayerId P0 = TestMaps.P0;
    private static readonly PlayerId P1 = TestMaps.P1;

    /// <summary>25×32 合成图（不过校验，只走 Unvalidated 入口）：0 号区 A25–E29（含 A27），1 号区 V1–Z5；地形要素都放在第 27 行以上。</summary>
    private static MapData Tall() =>
        new()
        {
            Id = "test-tall-25x32",
            Width = 25,
            Height = 32,
            MaxPlayers = 2,
            Obstacles = [Coord.Parse("Z30")],
            BirthZones = [[.. TestMaps.Rect(0, 24, 5, 5)], [.. TestMaps.Rect(20, 0, 5, 5)]],   // retire-legacy-maps：Rect 由已删除的 FrontierFixtures 原样迁到 TestMaps
            RelicCells = ImmutableDictionary<Coord, RelicCellSpec>.Empty.Add(Coord.Parse("C28"), new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth)),
            ChokePoints = [Coord.Parse("M27")],
            CentralEntrance = Coord.Parse("N16"),
            TerrainData = TestMaps.Terrain(
                heights: [("B31", 2), ("C31", 1)],
                surfaces: [("M28", Surface.DeepWater), ("N28", Surface.DeepWater), ("O32", Surface.Forest)],
                bridges: ["M28"],
                fences: [("Z31", "Z32")]),
        };

    /// <summary>
    /// 同一张竖长图改为链接模式可用的形态（retire-legacy-rules 1.6）：标识换成 <c>link:</c> 族（链接模式只配链接图）、人数上限 4（链接模式只有 4 人局），棋盘清单取 <paramref name="plates"/>，
    /// 棋盘之外一律障碍（战场 = 可落子格的连通分量）；其余（地形、出生区、信物格）不变。只走 Unvalidated 入口。
    /// </summary>
    private static MapData LinkTall(params BoardPlate[] plates)
    {
        MapData tall = Tall();
        HashSet<Coord> onBoards = [.. plates.SelectMany(b => b.Cells())];
        return tall with
        {
            Id = "link:90032:4",
            MaxPlayers = 4,
            Boards = [.. plates],
            Obstacles = tall.Obstacles.Union(TestMaps.Rect(0, 0, tall.Width, tall.Height).Where(c => !onBoards.Contains(c))),
        };
    }

    [Fact]
    public void 地图文件在两位数行号下往返()
    {
        MapData original = Tall();

        string json = MapFile.ToJson(original);
        MapData restored = MapFile.FromJson(json);

        foreach (string notation in new[] { "\"Z30\"", "\"C28\"", "\"M27\"", "\"N16\"", "\"M28\"", "\"Z31-Z32\"", "\"A27\"" })
        {
            Assert.Contains(notation, json, StringComparison.Ordinal);
        }

        Assert.Equal(original.Obstacles, restored.Obstacles);
        Assert.Equal(0, restored.BirthZoneOf(Coord.Parse("A27")));
        Assert.Equal(original.BirthZones[0].Order(), restored.BirthZones[0].Order());
        Assert.Equal(original.RelicCells.OrderBy(kv => kv.Key), restored.RelicCells.OrderBy(kv => kv.Key));
        Assert.Equal(original.CentralEntrance, restored.CentralEntrance);
        Assert.Equal(2, restored.HeightAt(Coord.Parse("B31")));
        Assert.Equal(1, restored.HeightAt(Coord.Parse("C31")));
        Assert.Equal(0, restored.HeightAt(Coord.Parse("B30")));   // 高度行没有错位一行
        Assert.Equal(Surface.Forest, restored.SurfaceAt(Coord.Parse("O32")));
        Assert.True(restored.HasBridge(Coord.Parse("M28")));
        Assert.False(restored.IsPlayable(Coord.Parse("N28")));
        Assert.True(restored.HasFence(Coord.Parse("Z31"), Coord.Parse("Z32")));
        Assert.Equal(json, MapFile.ToJson(restored));
    }

    [Fact]
    public void 对局存档与终端渲染在两位数行号下正确()
    {
        // retire-legacy-rules 1.6：改以链接模式建局（4 人、整张 25×32 图登记为一块出生棋盘、四人同锁）——链接终端只画本战场的外接矩形，
        // 整图一块棋盘才能把 32 行都画出来；原为旧模式 2 人局、各锁一区。断言不变。
        MapData map = LinkTall(new BoardPlate(new Coord(0, 0), 25, 32, BoardPlateKind.Birth));
        MatchFlow match = LinkModeFixtures.RequireLink(
            MatchFlow.CreateUnvalidated(map, TestMaps.Seed, TestMaps.All, TestMaps.Relics(map), LinkModeFixtures.PlainOptions(MatchOptions.Immediate)));
        match.Debug.SeedHand(P0, (PieceType.Basic, 9));
        match.PlantSequentially(TestMaps.All.Select(p => (p, 0)));
        match.Debug.SetOrder(P0, P1, TestMaps.P2, TestMaps.P3);

        match.PlayTurn("A27", "E29");

        Assert.Equal(P0, match.Board[Coord.Parse("A27")].Occupant?.Owner);
        MatchFlow restored = MatchFlow.RestoreUnvalidated(map, match.Relics.Generation, match.Serialize());
        Assert.Equal(P0, restored.Board[Coord.Parse("A27")].Occupant?.Owner);
        Assert.Equal(P0, restored.Board[Coord.Parse("E29")].Occupant?.Owner);
        Assert.Null(restored.Board[Coord.Parse("A17")].Occupant);

        var output = new StringWriter();
        new BoardRenderer(output).Board(match.Publish(), P0);
        string[] lines = output.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // 32 行棋盘，行标右对齐 3 位；A27 是该行第一格，显示为玩家 1 的普通子；E29 是第 29 行第 5 格。
        for (int row = 1; row <= 32; row++)
        {
            string line = Assert.Single(lines, l => l.StartsWith($"{row,3} ", StringComparison.Ordinal) && l.EndsWith($" {row}", StringComparison.Ordinal));
            Assert.Equal(4 + (25 * 3) + $" {row}".Length, line.Length);
        }

        Assert.StartsWith(" 27 1B ", lines.Single(l => l.StartsWith(" 27 ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal("1B ", lines.Single(l => l.StartsWith(" 29 ", StringComparison.Ordinal)).Substring(4 + (4 * 3), 3));
        Assert.DoesNotContain("1B", lines.Single(l => l.StartsWith(" 17 ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains(" A  B  C ", StringComparison.Ordinal) && l.TrimEnd().EndsWith(" Z", StringComparison.Ordinal));
    }

    [Fact]
    public void 对局日志在两位数行号下记录并可读回()
    {
        // 真实跑局：Easy AI 在竖长图上跑 1 个大回合。0 号区在第 25–29 行，P0 只能落在那里，
        // 所以 P0 的落点必然全是两位数行号——样本口径下界：P0 确实落了子。
        // retire-legacy-rules 1.6：改以链接模式建局——两个出生区各登记为一块 5×5 出生棋盘（两块不相邻，各成一个战场），4 人两两同锁（P0 / P2 锁 0 号、P1 / P3 锁 1 号），
        // P0 的合法范围恒为 0 号棋盘（原为旧模式 2 人局、靠保护期限在出生区）；跑局截断由 2 个小回合改为 4 个（4 人的 1 个大回合）。断言不变。
        MapData map = LinkTall(new BoardPlate(new Coord(0, 24), 5, 5, BoardPlateKind.Birth), new BoardPlate(new Coord(20, 0), 5, 5, BoardPlateKind.Birth));
        // more-pieces-relics 段 A：跑局配置经 PinPreCalibration 写死内容集 v1（样本下界依赖走法），对局配置须与之一致（MatchSession 核对二者）。
        MatchFlow match = LinkModeFixtures.RequireLink(MatchFlow.CreateUnvalidated(
            map, TestMaps.Seed, TestMaps.All, TestMaps.Relics(map), LinkModeFixtures.PlainOptions(MatchOptions.Immediate with { ContentSet = ContentSet.V1 })));
        match.PlantSequentially([(P0, 0), (P1, 1), (TestMaps.P2, 0), (TestMaps.P3, 1)]);
        // 权重与停手阈值写死为 ai-eye 4.5 定值之前的缺省：样本下界"P0 落了子"依赖走法（默认阈值 80 下简单难度第 1 大回合可能一子不落；段 D2 改写）。
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.PinPreCalibration(SimFixtures.Config(turnLimit: 4)));

        MatchLog log = MatchLog.Parse(session.Run().FullText());

        Assert.False(log.IsFailed);
        string[] mine = [.. log.Turns.Where(t => t.Player == 0).SelectMany(t => t.Placements)];
        Assert.NotEmpty(mine);
        foreach (string placement in mine)
        {
            System.Text.RegularExpressions.Match m = Regex.Match(placement, @"^([A-HJ-Z])(\d+)");
            Assert.True(m.Success, placement);
            Coord c = Coord.Parse(m.Value);
            Assert.InRange(c.Row, 25, 29);
            Assert.Equal(0, map.BirthZoneOf(c));
            Assert.Equal(P0, match.Board[c].Occupant?.Owner);   // 日志里的坐标就是盘面上的那一格（1 个大回合内无提子空间：两区相距 20 行）
        }
    }
}
