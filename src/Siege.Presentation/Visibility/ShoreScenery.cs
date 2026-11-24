using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Presentation.Visibility;

/// <summary>
/// 岛外浮石上的场景装饰件（shore-islets）：沿用原场景格的七种造型。渲染层按它挑部件，不自己判。
/// </summary>
public enum SceneryKind
{
    /// <summary>巨石。</summary>
    Boulder,

    /// <summary>松树丛。</summary>
    Pines,

    /// <summary>断柱遗迹。</summary>
    Ruins,

    /// <summary>水晶。</summary>
    Crystal,

    /// <summary>火盆。</summary>
    Brazier,

    /// <summary>拱门。</summary>
    Arch,

    /// <summary>宝箱。</summary>
    Chest,
}

/// <summary>
/// 一块岛外浮石（shore-islets 裁决：方案 A）：放在紧贴岛缘的虚空格上，垫一块小浮石，上面放一件场景装饰。
/// </summary>
/// <param name="Cell">所在的虚空格。</param>
/// <param name="Kind">浮石上的装饰件。</param>
/// <param name="OutX">背离岛面的方向（格坐标 x 分量，−1 / 0 / 1）：渲染层把浮石往这边让一点，避开岛缘外凸的岩沿。</param>
/// <param name="OutY">背离岛面的方向（格坐标 y 分量，y 向北）。</param>
/// <param name="Variant">浮石与装饰件的档号散列（非负；渲染层按各部件档数取模）。</param>
public readonly record struct SceneryIslet(Coord Cell, SceneryKind Kind, int OutX, int OutY, int Variant);

/// <summary>
/// 岛面判定与岛外浮石装饰（visual-style-baseline「棋盘台面的可视表现」：场景装饰落在岛外浮石上）。只读视图里的格，不判规则、不读地图。
/// </summary>
/// <remarks>
/// terrain-bonus 段 B 把岛内岩石障碍改成可落子的岩石地表后，障碍格只剩岛外虚空，原"岛内障碍格放场景装饰"的画法没有格可放。
/// 这里把装饰挪到紧贴岛缘的一圈虚空格上：哪一格放、放哪件只由坐标散列决定。视图里没有地图种子；岛形本身由种子生成，所以同一张图（同种子同人数）的结果恒定。
/// </remarks>
public static class ShoreScenery
{
    /// <summary>候选格的抽取比例（百分比）：散列 &lt; 此值且八邻还没有浮石的候选格才放。</summary>
    public const int Density = 45;

    private static readonly (int Dx, int Dy)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// 岛面格：从画布四边的非地面格向内泛洪（四邻），渗不到的格即岛面。地面 = 可落子格或深水格；不在视图里的格按非地面算。
    /// 只返回视图里的格（原 Godot <c>BoardView.IslandCells</c>，逐位同口径，3D 场景与小地图共用这一份）。
    /// </summary>
    public static HashSet<Coord> IslandCells(DefaultBoardView board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var ground = new HashSet<Coord>(board.Cells.Where(c => c.Terrain == Terrain.Playable || c.Surface == Surface.DeepWater).Select(c => c.Coord));
        var hollow = new HashSet<Coord>();
        var frontier = new Queue<Coord>();
        void Seep(int x, int y)
        {
            // 先判界再建 Coord：Coord 的构造会拒绝负下标，画布外不必建格。
            if (x < 0 || y < 0 || x >= board.Width || y >= board.Height)
            {
                return;
            }

            var at = new Coord(x, y);
            if (!ground.Contains(at) && hollow.Add(at))
            {
                frontier.Enqueue(at);
            }
        }

        // 种子 = 画布四边上的非地面格（等价于从画布外那一圈留白往里渗）。
        for (int y = 0; y < board.Height; y++)
        {
            for (int x = 0; x < board.Width; x++)
            {
                if (x == 0 || y == 0 || x + 1 == board.Width || y + 1 == board.Height)
                {
                    Seep(x, y);
                }
            }
        }

        while (frontier.Count > 0)
        {
            Coord c = frontier.Dequeue();
            Seep(c.X + 1, c.Y);
            Seep(c.X - 1, c.Y);
            Seep(c.X, c.Y + 1);
            Seep(c.X, c.Y - 1);
        }

        return [.. board.Cells.Select(c => c.Coord).Where(c => !hollow.Contains(c))];
    }

    /// <summary>
    /// 岛外浮石清单（按行、行内按列的次序）。候选格 = 视图里的障碍格（虚空）且不在岛面上，同时满足：
    /// 四邻里至少一格是岛面；四邻里的岛面格都不是深水（瀑布口正前方不放）；四邻里的岛面格不在相对两侧（夹在两段岸线之间的窄湾不放）。
    /// 候选格按坐标散列抽 <see cref="Density"/>%，已放浮石的格的八邻不再放。
    /// 视图里还有没展开的座位占位或中立平台轮廓（插旗阶段、选图预览）时岛形不完整，返回空。
    /// </summary>
    public static ImmutableArray<SceneryIslet> Islets(DefaultBoardView board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (board.Placeholders.Any(p => !p.Expanded) || !board.NeutralPlates.IsDefaultOrEmpty)
        {
            return [];
        }

        HashSet<Coord> island = IslandCells(board);
        Dictionary<Coord, Surface> surfaceAt = board.Cells.ToDictionary(c => c.Coord, c => c.Surface);
        bool IslandAt(int x, int y) => x >= 0 && y >= 0 && x < board.Width && y < board.Height && island.Contains(new Coord(x, y));

        var picked = new HashSet<Coord>();
        var islets = ImmutableArray.CreateBuilder<SceneryIslet>();
        foreach (BoardCellView cell in board.Cells.Where(c => c.Terrain == Terrain.Obstacle && !island.Contains(c.Coord))
                     .OrderBy(c => c.Coord.Y).ThenBy(c => c.Coord.X))
        {
            Coord at = cell.Coord;
            int sumX = 0, sumY = 0, shore = 0;
            bool waterfall = false;
            foreach ((int dx, int dy) in Sides)
            {
                if (!IslandAt(at.X + dx, at.Y + dy))
                {
                    continue;
                }

                shore++;
                sumX += dx;
                sumY += dy;
                waterfall |= surfaceAt.TryGetValue(new Coord(at.X + dx, at.Y + dy), out Surface s) && s == Surface.DeepWater;
            }

            // 相对两侧都有岛面时方向和会抵消、比岛面格数短（两侧对夹的窄湾、三面围住的湾底都在此列）。
            bool pinched = shore >= 2 && (Math.Abs(sumX) + Math.Abs(sumY)) < shore;
            if (shore == 0 || waterfall || pinched || Hash(at, 1) % 100 >= Density || HasPickedNeighbour(picked, at))
            {
                continue;
            }

            picked.Add(at);
            islets.Add(new SceneryIslet(at, KindOf((int)(Hash(at, 2) % 100)), -Math.Sign(sumX), -Math.Sign(sumY), (int)(Hash(at, 3) & 0x7FFF_FFFF)));
        }

        return islets.ToImmutable();
    }

    /// <summary>装饰件的阈值表（沿用原场景格的分布：巨石 30、松树 28、遗迹 12、水晶 10、火盆 8、拱门 6、宝箱 6）。</summary>
    public static SceneryKind KindOf(int pick) =>
        pick < 30 ? SceneryKind.Boulder : pick < 58 ? SceneryKind.Pines : pick < 70 ? SceneryKind.Ruins
            : pick < 80 ? SceneryKind.Crystal : pick < 88 ? SceneryKind.Brazier : pick < 94 ? SceneryKind.Arch : SceneryKind.Chest;

    private static bool HasPickedNeighbour(HashSet<Coord> picked, Coord at)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = at.X + dx, y = at.Y + dy;
                if ((dx != 0 || dy != 0) && x >= 0 && y >= 0 && picked.Contains(new Coord(x, y)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>坐标散列：只用整数算术（跨进程、跨平台稳定；不用 <c>string.GetHashCode</c> / <c>HashCode</c>，它们按进程随机）。</summary>
    private static uint Hash(Coord at, int salt)
    {
        unchecked
        {
            uint h = ((uint)at.X * 0x9E3779B1u) ^ ((uint)at.Y * 0x85EBCA77u) ^ ((uint)salt * 0xC2B2AE3Du);
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
    }
}
