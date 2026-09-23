using System.Collections.Immutable;
using System.Text;
using Siege.Core.Board;

namespace Siege.Core.Tests;

/// <summary>
/// 活形测试用的文本盘面夹具。文本<b>第一行是最高行号</b>（与 <see cref="Coord"/> 的"行号自下而上"一致，最后一行是第 1 行），
/// 每行从 <c>A</c> 列起。格字符：
/// <c>.</c> 空草地、<c>#</c> 岩石障碍、<c>~</c> 未架桥深水、<c>T</c> 空林地、<c>0</c>–<c>3</c> 对应玩家的普通子；
/// terrain-surfaces：<c>s</c> 空浅滩、<c>d</c> 空荒漠、<c>m</c> 空沼泽、<c>p</c> 空岩台。
/// 高度、栅栏、桥与"棋子底下的地表"（<c>under</c>）是另给的参数（栅栏是边，画不进格字符）。只用 <see cref="GameBoard.LoadUnvalidated"/> 构造。
/// </summary>
internal static class LifeShapeFixtures
{
    internal static readonly PlayerId A = new(0);
    internal static readonly PlayerId B = new(1);
    internal static readonly PlayerId C = new(2);
    internal static readonly PlayerId D = new(3);
    internal static readonly PlayerId[] All = [A, B, C, D];

    internal static GameBoard Grid(
        string[] rows,
        (string Cell, int Height)[]? heights = null,
        (string A, string B)[]? fences = null,
        string[]? bridges = null,
        (string Cell, Surface Surface)[]? under = null)
    {
        int height = rows.Length;
        int width = rows[0].Length;
        var obstacles = new List<Coord>();
        var surfaces = new List<(string Cell, Surface Surface)>();
        var stones = new List<(Coord Cell, PlayerId Owner)>();
        for (int r = 0; r < height; r++)
        {
            Assert.Equal(width, rows[r].Length);
            int y = height - 1 - r;
            for (int x = 0; x < width; x++)
            {
                var c = new Coord(x, y);
                char ch = rows[r][x];
                switch (ch)
                {
                    case '.':
                        break;
                    case '#':
                        obstacles.Add(c);
                        break;
                    case '~':
                        surfaces.Add((c.ToNotation(), Surface.DeepWater));
                        break;
                    case 'T':
                        surfaces.Add((c.ToNotation(), Surface.Forest));
                        break;
                    case 's':
                        surfaces.Add((c.ToNotation(), Surface.Shallows));
                        break;
                    case 'd':
                        surfaces.Add((c.ToNotation(), Surface.Desert));
                        break;
                    case 'm':
                        surfaces.Add((c.ToNotation(), Surface.Marsh));
                        break;
                    case 'p':
                        surfaces.Add((c.ToNotation(), Surface.Crag));
                        break;
                    case >= '0' and <= '3':
                        stones.Add((c, new PlayerId(ch - '0')));
                        break;
                    default:
                        throw new ArgumentException($"未知盘面字符 '{ch}'（第 {r + 1} 行文本）。");
                }
            }
        }

        TerrainData terrain = TestMaps.Terrain(heights, [.. surfaces, .. under ?? []], bridges, fences);
        GameBoard board = GameBoard.LoadUnvalidated(new MapData
        {
            Id = "test-life-shape",
            Width = width,
            Height = height,
            MaxPlayers = 4,
            Obstacles = [.. obstacles],
            BirthZones = [],
            RelicCells = ImmutableDictionary<Coord, RelicCellSpec>.Empty,
            ChokePoints = ImmutableHashSet<Coord>.Empty,
            CentralEntrance = new Coord(width / 2, height / 2),
            TerrainData = terrain,
        });
        foreach ((Coord cell, PlayerId owner) in stones)
        {
            board.Place(cell, owner, PieceType.Basic);
        }

        return board;
    }

    /// <summary>眼空间格的记法数组（坐标序）。</summary>
    internal static string[] Cells(this EyeSpace space) => space.Cells.Notations();

    /// <summary>某枚棋子所在棋串的全部眼空间，每个眼空间写成"格,格,…"。</summary>
    internal static string[] EyeSpacesOf(this LifeShapeReport report, string stone) =>
        [.. report.GroupLifeAt(TestMaps.At(stone))!.EyeSpaces.Select(e => string.Join(",", e.Cells()))];

    internal static LifeState LifeOf(this LifeShapeReport report, string stone) =>
        report.GroupLifeAt(TestMaps.At(stone))!.Life;

    internal static string[] Forbidden(this LifeShapeReport report, PlayerId player) =>
        report.ForbiddenCellsFor(player).Notations();

    /// <summary>
    /// 报告的逐项文本：每条棋串（棋子、所有者、活形、眼值和、各眼空间及眼值）+ 全部眼空间 + 四名玩家的禁入格。
    /// 测试内独立拼写，不调用被测类型的任何格式化方法。
    /// </summary>
    internal static string Describe(LifeShapeReport report)
    {
        var sb = new StringBuilder();
        foreach (GroupLife g in report.Groups)
        {
            sb.Append(g.Group.Owner).Append('[').Append(string.Join(",", g.Group.Stones.Notations())).Append("] ")
                .Append(g.Life).Append(' ').Append(g.EyeValueSum).Append(" :");
            foreach (EyeSpace e in g.EyeSpaces)
            {
                sb.Append(" {").Append(string.Join(",", e.Cells())).Append('=').Append(e.EyeValue).Append('}');
            }

            sb.Append('\n');
        }

        foreach (EyeSpace e in report.EyeSpaces)
        {
            sb.Append("eye ").Append(e.Owner).Append(' ').Append(string.Join(",", e.Cells()))
                .Append(" v").Append(e.EyeValue).Append(" g").Append(string.Join(",", e.GroupIndices)).Append('\n');
        }

        foreach (PlayerId p in All)
        {
            sb.Append("forbid ").Append(p).Append(' ').Append(string.Join(",", report.Forbidden(p))).Append('\n');
        }

        return sb.ToString();
    }
}
