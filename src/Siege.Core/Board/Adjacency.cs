using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// 四邻接是全项目<b>唯一</b>的几何邻接语义，这里是它<b>唯一</b>的实现；
/// 在几何四邻之上导出的三套关系——<b>气边</b>（<see cref="LibertyNeighbors"/>）、<b>眼边</b>（<see cref="EyeNeighbors"/>，fence-eye-rule）与<b>覆盖关系</b>（<see cref="CoverageTargets"/>）——也各自只在此处实现一次。
/// 连接、棋串、气与围杀判定 MUST 全部走气边；活形判定（空区、封闭、眼空间归属、方四）MUST 走眼边；覆盖、空格归属与信物发现 MUST 全部走覆盖关系；
/// MUST NOT 在别处手写邻居遍历或地形过滤。
/// </summary>
/// <remarks>
/// <para>越界方向与障碍格具有相同的封堵语义，<see cref="Neighbors"/> 只负责"棋盘内的上下左右"。</para>
/// <para>三个导出关系每次调用按当前 <see cref="MapData"/> 实时导出，不缓存；全整数，无浮点。</para>
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
    /// 即 <see cref="EyeNeighbors"/> 再去掉与 <paramref name="c"/> 之间有栅栏的格（fence-eye-rule D1：气边 = 眼边 ∧ 无栅栏，两者共用同一份可落子与高差判定）。
    /// <paramref name="c"/> 自身不可落子时返回空。顺序沿用 <see cref="Neighbors"/>（字典序）。
    /// </summary>
    public static ImmutableArray<Coord> LibertyNeighbors(MapData map, Coord c)
    {
        ImmutableArray<Coord> eye = EyeNeighbors(map, c);
        ImmutableArray<Coord>.Builder builder = ImmutableArray.CreateBuilder<Coord>(eye.Length);
        foreach (Coord n in eye)
        {
            if (!map.HasFence(c, n))
            {
                builder.Add(n);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// 眼边导出（fence-eye-rule D1，裁决 F1 / F2 / F3）：<paramref name="c"/> 的几何邻居中，与 <paramref name="c"/> 之间存在眼边者——
    /// 两格都可落子（非障碍、非未架桥深水）∧ |Δh| &lt; <see cref="TerrainData.CliffDrop"/>（即 ≤ 1）。即气边去掉"之间无栅栏"一条：<b>不看栅栏</b>
    /// （预置栅栏与造出的栅栏同样不看）。对称关系。<b>只</b>供活形判定（空区、封闭、眼空间归属、方四）使用；气、棋串、提子一律走 <see cref="LibertyNeighbors"/>。
    /// <paramref name="c"/> 自身不可落子时返回空。顺序沿用 <see cref="Neighbors"/>（字典序）。
    /// </summary>
    public static ImmutableArray<Coord> EyeNeighbors(MapData map, Coord c)
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
            if (map.IsPlayable(n) && Math.Abs(map.HeightAt(n) - h) < TerrainData.CliffDrop)
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
    public static ImmutableArray<Coord> CoverageTargets(MapData map, Coord s) => CoverageTargets(map, s, farReach: false);

    /// <summary>
    /// 同 <see cref="CoverageTargets(MapData, Coord)"/>，<paramref name="farReach"/> 为 <c>true</c> 时<b>不论地表</b>都走岩台远格分支
    /// （relic-redesign D3：挂望台的子"按岩台规则额外覆盖四方向距离 2"）。条件是"站在岩台上 <b>或</b> 挂望台"——岩台上的子再挂望台不再扩大；
    /// 沼泽源仍最先返回空（沼泽优先）。<paramref name="farReach"/> 为 <c>false</c> 时与原重载逐项相同。只由 <see cref="GameBoard.CoverageTargets"/> 按信物层调用。
    /// </summary>
    public static ImmutableArray<Coord> CoverageTargets(MapData map, Coord s, bool farReach)
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

        // terrain-surfaces 岩台远格（design D2 第 3 步）：沿每个方向越过中间格 m 再覆盖一格 u。
        // m 是障碍、盘外或比源高出崖壁阈值时阻挡；深水、林地、有子的格、栅栏都不阻挡。u 与隔水覆盖可能是同一格，按格去重。
        if (farReach || map.SurfaceAt(s) == Surface.Crag)
        {
            foreach (Coord m in Neighbors(map.Width, map.Height, s))
            {
                int ux = (2 * m.X) - s.X;
                int uy = (2 * m.Y) - s.Y;
                if (ux < 0 || uy < 0 || ux >= map.Width || uy >= map.Height
                    || map.Obstacles.Contains(m)
                    || map.HeightAt(m) - h >= TerrainData.CliffDrop)
                {
                    continue;
                }

                var u = new Coord(ux, uy);
                if (Receives(map, u, h) && !builder.Contains(u))
                {
                    builder.Add(u);
                }
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
