using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>地表种类。草地与土路在规则上等价，只作视觉区分；林地不接收覆盖；深水未架桥时不可落子、断气。</summary>
/// <remarks>规格：openspec/changes/terrain-model/specs/terrain —— Requirement: 格属性</remarks>
public enum Surface
{
    /// <summary>草地。</summary>
    Grass,

    /// <summary>土路：规则上与草地等价。</summary>
    Road,

    /// <summary>林地：可落子，但不接收覆盖。</summary>
    Forest,

    /// <summary>深水：未架桥时不可落子、不可控制、不计分；覆盖可穿过一格宽的深水。</summary>
    DeepWater,
}

/// <summary>
/// 栅栏边：两个几何相邻格之间的无序对。构造时按字典序归一化，因此 <c>(a, b)</c> 与 <c>(b, a)</c> 相等。
/// </summary>
/// <remarks>规格：openspec/changes/terrain-model/specs/terrain —— Requirement: 边属性</remarks>
public readonly record struct FenceEdge
{
    public FenceEdge(Coord a, Coord b)
    {
        if (a == b)
        {
            throw new ArgumentException($"栅栏两端不能是同一格：{a.ToNotation()}。", nameof(b));
        }

        (A, B) = a < b ? (a, b) : (b, a);
    }

    /// <summary>字典序较小的一端。</summary>
    public Coord A { get; }

    /// <summary>字典序较大的一端。</summary>
    public Coord B { get; }

    /// <summary>是否连接 <paramref name="x"/> 与 <paramref name="y"/>（不分方向）。</summary>
    public bool Connects(Coord x, Coord y) => (A == x && B == y) || (A == y && B == x);

    public override string ToString() => $"{A.ToNotation()}-{B.ToNotation()}";
}

/// <summary>
/// 地形数据：每格高度（0/1/2）、地表，以及两种设施——预置桥（格）与栅栏（边）。
/// 缺省即"全 h=0、全草地、无桥无栅"。设施与静态地表分开存，为第三轮"对局内改造"留写入口。
/// </summary>
/// <remarks>
/// <para>构造期校验：高度必须在 0–2；桥只能标在深水格上；栅栏两端必须几何相邻。违者抛 <see cref="ArgumentException"/> 并指出坐标。</para>
/// <para>规格：openspec/changes/terrain-model/specs/terrain —— Requirement: 格属性 / 边属性</para>
/// </remarks>
public sealed class TerrainData
{
    /// <summary>最大高度档位。</summary>
    public const int MaxHeight = 2;

    /// <summary>
    /// 崖壁落差（裁决 D2）：相邻两格高度差达到此值即为崖壁——切断气边（<see cref="Adjacency.LibertyNeighbors"/> 要求 |Δh| &lt; CliffDrop），
    /// 覆盖只能从高处落下（<see cref="Adjacency.CoverageTargets"/> 要求 h_t − h_s &lt; CliffDrop）；差 1 为缓坡，两个方向都通。
    /// 表现层解释"被覆盖但不是气"的崖壁原因时也读它。这是全仓唯一一份阈值（terrain-model 裁决 C-8）；
    /// 与 <see cref="MaxHeight"/> 数值相同只是三层高度下的巧合，不是同一语义。
    /// </summary>
    public const int CliffDrop = 2;

    /// <summary>全平地：无任何地形要素。</summary>
    public static readonly TerrainData Flat = new(
        ImmutableDictionary<Coord, int>.Empty,
        ImmutableDictionary<Coord, Surface>.Empty,
        ImmutableHashSet<Coord>.Empty,
        ImmutableHashSet<FenceEdge>.Empty);

    public TerrainData(
        ImmutableDictionary<Coord, int> heights,
        ImmutableDictionary<Coord, Surface> surfaces,
        ImmutableHashSet<Coord> bridges,
        ImmutableHashSet<FenceEdge> fences)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(bridges);
        ArgumentNullException.ThrowIfNull(fences);

        foreach ((Coord c, int h) in heights.OrderBy(kv => kv.Key))
        {
            if (h < 0 || h > MaxHeight)
            {
                throw new ArgumentException($"高度必须在 0–{MaxHeight} 之间：{c.ToNotation()} 为 {h}。", nameof(heights));
            }
        }

        foreach (Coord c in bridges.Order())
        {
            if (!surfaces.TryGetValue(c, out Surface s) || s != Surface.DeepWater)
            {
                throw new ArgumentException($"预置桥只能架在深水格上：{c.ToNotation()} 不是深水。", nameof(bridges));
            }
        }

        foreach (FenceEdge fence in fences.OrderBy(f => f.A).ThenBy(f => f.B))
        {
            if (!Adjacency.AreAdjacent(fence.A, fence.B))
            {
                throw new ArgumentException($"栅栏只能标在几何相邻的格之间：{fence.A.ToNotation()} 与 {fence.B.ToNotation()} 不相邻。", nameof(fences));
            }
        }

        // 显式写成缺省值的项（h=0、草地）归一化掉：查询语义不变，而 Heights / Surfaces 与 MapFile 写出再读入的结果逐字段相等
        // （MapFile 只写非缺省项）。段 B 的 v3 生成器若给每格都填高度，4.3 的"再读入相等"才不会假红。
        Heights = heights.Any(kv => kv.Value == 0) ? heights.Where(kv => kv.Value != 0).ToImmutableDictionary() : heights;
        Surfaces = surfaces.Any(kv => kv.Value == Surface.Grass) ? surfaces.Where(kv => kv.Value != Surface.Grass).ToImmutableDictionary() : surfaces;
        Bridges = bridges;
        Fences = fences;
    }

    /// <summary>显式给定的非零高度；缺省格为 0（显式 0 在构造时被归一化掉）。</summary>
    public ImmutableDictionary<Coord, int> Heights { get; }

    /// <summary>显式给定的非草地地表；缺省格为草地（显式草地在构造时被归一化掉）。</summary>
    public ImmutableDictionary<Coord, Surface> Surfaces { get; }

    /// <summary>预置桥所在的深水格。</summary>
    public ImmutableHashSet<Coord> Bridges { get; }

    /// <summary>栅栏边集合。</summary>
    public ImmutableHashSet<FenceEdge> Fences { get; }

    /// <summary>该格高度，缺省 0。</summary>
    public int HeightAt(Coord c) => Heights.TryGetValue(c, out int h) ? h : 0;

    /// <summary>该格地表，缺省草地。</summary>
    public Surface SurfaceAt(Coord c) => Surfaces.TryGetValue(c, out Surface s) ? s : Surface.Grass;

    /// <summary>该格是否架有预置桥。</summary>
    public bool HasBridge(Coord c) => Bridges.Contains(c);

    /// <summary>该格是否为未架桥的深水。</summary>
    public bool IsUnbridgedDeepWater(Coord c) => SurfaceAt(c) == Surface.DeepWater && !HasBridge(c);

    /// <summary>两格之间是否有栅栏（不分方向）。</summary>
    public bool HasFence(Coord a, Coord b) => a != b && Fences.Contains(new FenceEdge(a, b));
}
