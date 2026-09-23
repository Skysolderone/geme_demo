using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// 四邻接是全项目<b>唯一</b>的几何邻接语义，这里是它<b>唯一</b>的实现；
/// 在几何四邻之上导出的两套关系——<b>气边</b>（<see cref="LibertyNeighbors"/>）与<b>覆盖关系</b>（<see cref="CoverageTargets"/>）——也各自只在此处实现一次。
/// 连接、棋串、气与围杀判定 MUST 全部走气边；覆盖、空格归属与信物发现 MUST 全部走覆盖关系；
/// MUST NOT 在别处手写邻居遍历或地形过滤。
/// </summary>
/// <remarks>
/// <para>越界方向与障碍格具有相同的封堵语义，<see cref="Neighbors"/> 只负责"棋盘内的上下左右"。</para>
/// <para>两个导出关系每次调用按当前 <see cref="MapData"/> 实时导出，不缓存；全整数，无浮点。</para>
/// <para>规格：openspec/changes/terrain-model/specs/board-topology —— Requirement: 四邻接是唯一邻接语义；
/// specs/terrain —— Requirement: 气边 / 覆盖关系</para>
/// </remarks>
public static class Adjacency
{
    /// <summary>
    /// 返回 <paramref name="c"/> 的四邻接邻居中位于 <paramref name="width"/>×<paramref name="height"/>
    /// 棋盘内的格子，按字典序（先行后列）排列：下、左、右、上。不含斜向。
    /// </summary>
    public static ImmutableArray<Coord> Neighbors(int width, int height, Coord c)
    {
        ImmutableArray<Coord>.Builder builder = ImmutableArray.CreateBuilder<Coord>(4);

        if (c.Y > 0)
        {
            builder.Add(new Coord(c.X, c.Y - 1));
        }

        if (c.X > 0)
        {
            builder.Add(new Coord(c.X - 1, c.Y));
        }

        if (c.X < width - 1)
        {
            builder.Add(new Coord(c.X + 1, c.Y));
        }

        if (c.Y < height - 1)
        {
            builder.Add(new Coord(c.X, c.Y + 1));
        }

        return builder.ToImmutable();
    }

    /// <summary>两格是否几何相邻（上下左右之一，不含斜向）。不看棋盘范围——供栅栏边这类数据合法性校验使用。</summary>
    public static bool AreAdjacent(Coord a, Coord b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

    /// <summary>
    /// 气边导出：<paramref name="c"/> 的几何邻居中，与 <paramref name="c"/> 之间存在气边者——
    /// 两格都可落子（非障碍、非未架桥深水）∧ |Δh| &lt; <see cref="TerrainData.CliffDrop"/>（即 ≤ 1）∧ 之间无栅栏。对称关系。
    /// <paramref name="c"/> 自身不可落子时返回空。顺序沿用 <see cref="Neighbors"/>（字典序）。
    /// </summary>
    public static ImmutableArray<Coord> LibertyNeighbors(MapData map, Coord c)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.IsPlayable(c))
        {
            return ImmutableArray<Coord>.Empty;
        }

        int h = map.HeightAt(c);
        ImmutableArray<Coord>.Builder builder = ImmutableArray.CreateBuilder<Coord>(4);
        foreach (Coord n in Neighbors(map.Width, map.Height, c))
        {
            if (map.IsPlayable(n) && Math.Abs(map.HeightAt(n) - h) < TerrainData.CliffDrop && !map.HasFence(c, n))
            {
                builder.Add(n);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// 覆盖关系导出：格 <paramref name="s"/> 上的棋子向哪些格提供覆盖。按 terrain 规格三步：
    /// ① 几何邻居 t 可落子、非林地且 h_t − h_s ≤ 1 → 覆盖 t；
    /// ② t 是未架桥深水 → 看沿同方向的下一格 u：在盘内、可落子、非林地且 h_u − h_s ≤ 1 → 覆盖 u，不覆盖 t（只穿一格水）；
    /// ③ 其余不覆盖。栅栏不影响。可不对称：高处覆盖低处，低处不跨崖覆盖高处。
    /// <paramref name="s"/> 自身不可落子时返回空。结果按字典序排列。
    /// <para>terrain-surfaces：<paramref name="s"/> 是沼泽时返回空（沼泽源，design D2 第 1 步）——沼泽上的棋子不产生覆盖，
    /// 空格归属、信物发现与高地压制都经本方法自然继承，不另设地表例外。</para>
    /// </summary>
    public static ImmutableArray<Coord> CoverageTargets(MapData map, Coord s)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.IsPlayable(s) || map.SurfaceAt(s) == Surface.Marsh)
        {
            return ImmutableArray<Coord>.Empty;
        }

        int h = map.HeightAt(s);
        ImmutableArray<Coord>.Builder builder = ImmutableArray.CreateBuilder<Coord>(4);
        foreach (Coord t in Neighbors(map.Width, map.Height, s))
        {
            if (map.Obstacles.Contains(t))
            {
                continue;
            }

            if (map.TerrainData.IsUnbridgedDeepWater(t))
            {
                // 沿 s → t 的方向再走一格。先用整数算，越界就不构造 Coord（Coord 对负索引抛出）。
                int ux = (2 * t.X) - s.X;
                int uy = (2 * t.Y) - s.Y;
                if (ux < 0 || uy < 0 || ux >= map.Width || uy >= map.Height)
                {
                    continue;
                }

                var u = new Coord(ux, uy);
                if (Receives(map, u, h))
                {
                    builder.Add(u);
                }

                continue;
            }

            if (Receives(map, t, h))
            {
                builder.Add(t);
            }
        }

        builder.Sort();
        return builder.ToImmutable();
    }

    /// <summary>目标格是否接收来自高度 <paramref name="sourceHeight"/> 的覆盖：可落子、非林地、不比来源高 <see cref="TerrainData.CliffDrop"/>。</summary>
    private static bool Receives(MapData map, Coord target, int sourceHeight) =>
        map.IsPlayable(target)
        && map.SurfaceAt(target) != Surface.Forest
        && map.HeightAt(target) - sourceHeight < TerrainData.CliffDrop;
}
