using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>规格：openspec/changes/board-link-mode/specs/link-map-generation —— Requirement: 初始布局（段 F 拼接版，裁决 J2 / J4）</summary>
/// <remarks>
/// 变异验证（段 F 实跑，只跑本类）：
/// M-F1 <c>LinkMapGenerator.Layout</c> 的第二个信物格候选不排除相邻（<c>apart</c> 改为全部内圈格）→ 「初始图只有出生棋盘」种子 1–20 里多条红（两格相邻）；
/// M-F2 <c>Layout</c> 的横向间距 <c>Gap</c> 改为 2 → 「出生棋盘互不相邻」全红；
/// M-F3 <c>Assemble</c> 的东侧留白改为 1 → 「画布按布局裁出」全红。
/// </remarks>
public class 初始布局Tests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 20).Select(s => new object[] { (ulong)s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 初始图只有出生棋盘(ulong seed)
    {
        // Scenario：link:1–link:20 每张恰 5 块出生棋盘、每块 2 个信物格，清单里没有其他棋盘，棋盘之外没有可落子格。
        MapData map = LinkMapGenerator.Generate(seed);

        Assert.Equal(MapProfile.Link, map.Profile);
        Assert.Equal(0, map.MapVersion);
        Assert.Equal(4, map.MaxPlayers);
        Assert.Equal(LinkMapGenerator.BirthBoards, map.Boards.Length);
        Assert.All(map.Boards, b => Assert.Equal(BoardPlateKind.Birth, b.Kind));
        Assert.Equal(5, map.BirthZones.Length);

        // 棋盘之外没有可落子格；棋盘内每格可落子、h=0 草地、无栅栏、无深水与桥。
        HashSet<Coord> boardCells = [.. map.Boards.SelectMany(b => b.Cells())];
        Assert.All(map.AllCoords(), c => Assert.Equal(boardCells.Contains(c), map.IsPlayable(c)));
        Assert.Equal(boardCells.Count, map.PlayableCount);
        Assert.InRange(map.PlayableCount, 125, 245);
        Assert.All(boardCells, c => Assert.True(map.HeightAt(c) == 0 && map.SurfaceAt(c) == Surface.Grass));
        Assert.Empty(map.TerrainData.Fences);
        Assert.Empty(map.TerrainData.Bridges);
        Assert.DoesNotContain(Surface.DeepWater, map.TerrainData.Surfaces.Values);

        // 边长 5–7；第 i 块出生棋盘 = 出生区 i；每块恰 2 个信物格、都在最外一圈以内、互不相邻。
        for (int i = 0; i < 5; i++)
        {
            BoardPlate b = map.Boards[i];
            Assert.InRange(b.Width, LinkMapGenerator.BirthMinSide, LinkMapGenerator.BirthMaxSide);
            Assert.InRange(b.Height, LinkMapGenerator.BirthMinSide, LinkMapGenerator.BirthMaxSide);
            Assert.True(map.BirthZones[i].SetEquals(b.Cells()));
            Coord[] relics = [.. map.RelicCells.Keys.Where(b.Contains).Order()];
            Assert.Equal(LinkMapGenerator.RelicsPerBirthBoard, relics.Length);
            Assert.All(relics, r => Assert.Equal(new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth), map.RelicCells[r]));
            Assert.All(relics, r => Assert.True(r.X > b.Origin.X && r.X < b.Origin.X + b.Width - 1 && r.Y > b.Origin.Y && r.Y < b.Origin.Y + b.Height - 1, $"{r} 在最外一圈"));
            Assert.False(Adjacency.AreAdjacent(relics[0], relics[1]), $"link:{seed} 棋盘 {i} 的两个信物格 {relics[0]} / {relics[1]} 相邻");
        }

        Assert.Equal(10, map.RelicCells.Count);
        Assert.True(MapValidator.Validate(map).IsValid, MapValidator.Validate(map).ToString());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 出生棋盘互不相邻(ulong seed)
    {
        // Scenario：任意两块出生棋盘的外接矩形之间至少隔 3 格，沿几何邻接不连通（5 个战场）。
        MapData map = LinkMapGenerator.Generate(seed);
        for (int i = 0; i < map.Boards.Length; i++)
        {
            for (int j = i + 1; j < map.Boards.Length; j++)
            {
                Assert.True(LinkMapFixtures.Gap(map.Boards[i], map.Boards[j]) >= LinkMapGenerator.Gap, $"{seed}: 棋盘 {i} 与 {j} 只隔 {LinkMapFixtures.Gap(map.Boards[i], map.Boards[j])}");
            }
        }

        Assert.Equal(5, Battlefields.Of(map).Count);
        Assert.Equal(3, LinkMapGenerator.Gap);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void 画布按布局裁出(ulong seed)
    {
        // 裁决 J4 / J7：5 块棋盘底边对齐在第 2 行、自第 2 列起一字排开、相邻两块恰隔 3 格；画布四周各留 2 格裁出一次（之后原点不再变）。
        MapData map = LinkMapGenerator.Generate(seed);
        BoardPlate[] byX = [.. map.Boards.OrderBy(b => b.Origin.X)];
        Assert.Equal(LinkMapGenerator.Margin, byX[0].Origin.X);
        Assert.All(byX, b => Assert.Equal(LinkMapGenerator.Margin, b.Origin.Y));
        for (int k = 1; k < byX.Length; k++)
        {
            Assert.Equal(byX[k - 1].Origin.X + byX[k - 1].Width + LinkMapGenerator.Gap, byX[k].Origin.X);
        }

        Assert.Equal(byX[^1].Origin.X + byX[^1].Width + LinkMapGenerator.Margin, map.Width);
        Assert.Equal(map.Boards.Max(b => b.Origin.Y + b.Height) + LinkMapGenerator.Margin, map.Height);
        Assert.InRange(map.Width, 41, 51);   // 5×5 + 4×3 + 4 … 5×7 + 4×3 + 4
        Assert.InRange(map.Height, 9, 11);

        // 次序随种子变化（样本口径：20 个种子里不止一种排列）。
        Assert.True(Seeds().Select(s => string.Join(",", LinkMapGenerator.Generate((ulong)s[0]).Boards.OrderBy(b => b.Origin.X).Select(b => Array.IndexOf([.. LinkMapGenerator.Generate((ulong)s[0]).Boards], b)))).Distinct().Count() > 1);
    }
}
