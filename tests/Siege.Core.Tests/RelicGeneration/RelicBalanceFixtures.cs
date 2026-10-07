using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Tests.RelicGeneration;

/// <summary>
/// 信物生成的同区均衡 / 降权 / 分布测试用的最小合成图（retire-legacy-maps 段 A2，为这些性质专门设计，尺寸与坐标自定）。
/// 内置棋盘图每块出生棋盘只有 1 枚信物，造不出"同一出生区多枚信物"的均衡与同类型降权，故另建此图。
/// </summary>
/// <remarks>
/// 12×8，四个 3×3 出生区：0 = 左下 A1–C3（信物 A1、C2），1 = 右下 K1–M3（信物 L1、M3），2 = 左上 A6–C8（信物 B6、A8），3 = 右上 K6–M8（信物 M6、K8）——每区 2 枚出生区信物，共 8 枚；
/// 公共区 4 枚：E3、H6 标准档，F4、G5 高档（高档取 2 枚，让高档样本量翻倍）。生成器只读信物格、规格与出生区归属，不读地形；只经生成器与
/// <c>LoadUnvalidated</c> 使用，不做地图校验。
/// </remarks>
internal static class RelicBalanceFixtures
{
    internal const string Id = "test-relic-balance-12x8";

    /// <summary>出生区信物格总数（分布类测试的样本数 = 种子数 × 它）。</summary>
    internal const int BirthCells = 8;

    internal static MapData Map()
    {
        static ImmutableHashSet<Coord> Block(int x0, int y0) =>
            [.. Enumerable.Range(0, 9).Select(i => new Coord(x0 + (i % 3), y0 + (i / 3)))];

        var birth = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
        var standard = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        var high = new RelicCellSpec(RelicZone.Contested, BudgetTier.High);
        return new MapData
        {
            Id = Id,
            Width = 12,
            Height = 8,
            MaxPlayers = 4,
            Obstacles = [],
            BirthZones = [Block(0, 0), Block(9, 0), Block(0, 5), Block(9, 5)],
            RelicCells = new Dictionary<Coord, RelicCellSpec>
            {
                [Coord.Parse("A1")] = birth, [Coord.Parse("C2")] = birth,
                [Coord.Parse("L1")] = birth, [Coord.Parse("M3")] = birth,
                [Coord.Parse("B6")] = birth, [Coord.Parse("A8")] = birth,
                [Coord.Parse("M6")] = birth, [Coord.Parse("K8")] = birth,
                [Coord.Parse("E3")] = standard, [Coord.Parse("H6")] = standard,
                [Coord.Parse("F4")] = high, [Coord.Parse("G5")] = high,
            }.ToImmutableDictionary(),
            ChokePoints = [Coord.Parse("F4")],
            CentralEntrance = Coord.Parse("F4"),
        };
    }
}
