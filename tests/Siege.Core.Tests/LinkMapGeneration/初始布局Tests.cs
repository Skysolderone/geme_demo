using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>规格：openspec/changes/board-link-mode/specs/link-map-generation —— Requirement: 初始布局</summary>
/// <remarks>
/// 画布：规格写"列、行各不超过 50"，实现为 73×73（4×4 槽位，槽距 22 = 出生棋盘最大边长 7 + 预留间距 15）；
/// 50 列里一轴最多排 3 个带，而 3×3 槽位的全部 5 槽图案都存在接不上的棋盘对（见 LinkMapGenerator 注释与任务报告的偏离项）。
/// </remarks>
public class 初始布局Tests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 20).Select(s => new object[] { (ulong)s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 初始图只有出生棋盘(ulong seed)
    {
        // Scenario：link:1–link:20 每张恰 5 块出生棋盘、清单里没有公共棋盘、棋盘之外没有可落子格。
        MapData map = LinkMapGenerator.Generate(seed);

        Assert.Equal(MapProfile.Link, map.Profile);
        Assert.Equal(0, map.MapVersion);
        Assert.Equal(4, map.MaxPlayers);
        Assert.Equal(5, map.Boards.Length);
        Assert.All(map.Boards, b => Assert.Equal(BoardPlateKind.Birth, b.Kind));
        Assert.Equal(5, map.BirthZones.Length);

        // 棋盘之外没有可落子格；棋盘内每格可落子、h=0 草地、无栅栏、无深水与桥。
        HashSet<Coord> boardCells = [.. map.Boards.SelectMany(b => b.Cells())];
        Assert.All(map.AllCoords(), c => Assert.Equal(boardCells.Contains(c), map.IsPlayable(c)));
        Assert.Equal(boardCells.Count, map.PlayableCount);
        Assert.All(boardCells, c => Assert.True(map.HeightAt(c) == 0 && map.SurfaceAt(c) == Surface.Grass));
        Assert.Empty(map.TerrainData.Fences);
        Assert.Empty(map.TerrainData.Bridges);
        Assert.DoesNotContain(Surface.DeepWater, map.TerrainData.Surfaces.Values);

        // 边长 5–7；第 i 块出生棋盘 = 出生区 i；每块恰 1 个信物格且不在最外一圈。
        for (int i = 0; i < 5; i++)
        {
            BoardPlate b = map.Boards[i];
            Assert.InRange(b.Width, LinkMapGenerator.BirthMinSide, LinkMapGenerator.BirthMaxSide);
            Assert.InRange(b.Height, LinkMapGenerator.BirthMinSide, LinkMapGenerator.BirthMaxSide);
            Assert.True(map.BirthZones[i].SetEquals(b.Cells()));
            Coord[] relics = [.. map.RelicCells.Keys.Where(b.Contains)];
            Coord relic = Assert.Single(relics);
            Assert.Equal(new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth), map.RelicCells[relic]);
            Assert.True(relic.X > b.Origin.X && relic.X < b.Origin.X + b.Width - 1 && relic.Y > b.Origin.Y && relic.Y < b.Origin.Y + b.Height - 1, $"{relic} 在最外一圈");
        }

        Assert.Equal(5, map.RelicCells.Count);
        Assert.True(MapValidator.Validate(map).IsValid, MapValidator.Validate(map).ToString());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 出生棋盘间距(ulong seed)
    {
        // Scenario：任意两块出生棋盘的外接矩形之间至少能放下一块 11×11 的矩形与两条 3 格宽的直通道——
        // 按校验器同口径的间隔（两个方向上空格数的较大者）≥ 11 + 2 × 2 = 15。
        MapData map = LinkMapGenerator.Generate(seed);
        for (int i = 0; i < map.Boards.Length; i++)
        {
            for (int j = i + 1; j < map.Boards.Length; j++)
            {
                Assert.True(Gap(map.Boards[i], map.Boards[j]) >= LinkMapGenerator.ReserveGap, $"{seed}: 棋盘 {i} 与 {j} 只隔 {Gap(map.Boards[i], map.Boards[j])}");
            }
        }

        Assert.Equal(15, LinkMapGenerator.ReserveGap);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 画布固定且不超过校验器上限(ulong seed)
    {
        // A2 / 裁决 R1：画布由 4×4 槽位决定、固定 73×73、整局不变（规格「初始布局」同口径）；这里钉住常量，画布一变本测试先红。
        MapData map = LinkMapGenerator.Generate(seed);
        Assert.Equal(LinkMapGenerator.CanvasSide, map.Width);
        Assert.Equal(LinkMapGenerator.CanvasSide, map.Height);
        Assert.Equal(73, LinkMapGenerator.CanvasSide);
        Assert.InRange(map.Width, 20, 80);
    }

    [Fact]
    public void 槽位图案每一对棋盘都能接上()
    {
        // 逐对可接的图案表：4×4 槽位里 5 个槽位的组合中"每对都能用一块公共棋盘 + 两条直通道接上"的那些；每个图案恰 5 槽、互不相同、按字典序。
        var feasible = LinkMapGenerator.AllSlotFeasiblePatterns;
        Assert.Equal(536, feasible.Length);
        Assert.All(feasible, p => Assert.Equal(5, p.Length));
        Assert.Equal(feasible.Length, feasible.Select(p => string.Join(";", p)).Distinct().Count());
        // 3 槽同行相邻（(0,0),(1,0),(2,0)）两端接不上：这样的图案不得出现。
        Assert.DoesNotContain(feasible, p => p.Contains((0, 0)) && p.Contains((1, 0)) && p.Contains((2, 0)));

        // 生成白名单是它的子集（32 个），阶梯图案在内。
        var patterns = LinkMapGenerator.SlotPatterns;
        Assert.Equal(32, patterns.Length);
        Assert.All(patterns, p => Assert.Contains(feasible, f => f.SequenceEqual(p)));
        Assert.Contains(patterns, p => p.SequenceEqual([(0, 0), (0, 1), (1, 1), (2, 2), (3, 3)]));
    }

    internal static int Gap(BoardPlate a, BoardPlate b)
    {
        int dx = Math.Max(a.Origin.X - (b.Origin.X + b.Width), b.Origin.X - (a.Origin.X + a.Width));
        int dy = Math.Max(a.Origin.Y - (b.Origin.Y + b.Height), b.Origin.Y - (a.Origin.Y + a.Height));
        return Math.Max(dx, dy);
    }
}
