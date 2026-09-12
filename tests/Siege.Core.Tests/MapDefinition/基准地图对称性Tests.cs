using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 基准地图的独立验证：这里的每一项都**自己算**，不调用 <see cref="MapValidator"/>。
/// </summary>
/// <remarks>
/// 距离容差被定为 1（裁决记录第 1 条），基准图靠 D2 对称精确满足它。
/// 如果对称被后续改动破坏，MapValidator 会报 DISTANCE_IMBALANCE——但那是它自己的 BFS 说的。
/// 这里用独立实现的 BFS 复算一遍，避免"校验器和被校验对象一起错"。
/// </remarks>
public class 基准地图对称性Tests
{
    private static readonly MapData Map = FourPlayerBaseMap.Create();
    private const int Size = 11;

    /// <summary>测试内独立实现的四方向偏移，不复用 <see cref="Adjacency"/>。</summary>
    private static readonly (int Dx, int Dy)[] Directions = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    private static Coord MirrorX(Coord c) => new(Size - 1 - c.X, c.Y);

    private static Coord MirrorY(Coord c) => new(c.X, Size - 1 - c.Y);

    private static Coord Rotate180(Coord c) => new(Size - 1 - c.X, Size - 1 - c.Y);

    private static Func<Coord, Coord> TransformOf(string name) => name switch
    {
        "mirrorX" => MirrorX,
        "mirrorY" => MirrorY,
        "rotate180" => Rotate180,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [InlineData("mirrorX")]
    [InlineData("mirrorY")]
    [InlineData("rotate180")]
    public void 障碍集合在D2变换下不变(string transform)
    {
        Func<Coord, Coord> t = TransformOf(transform);

        Assert.Equal(Map.Obstacles.OrderBy(c => c), Map.Obstacles.Select(t).OrderBy(c => c));
    }

    [Theory]
    [InlineData("mirrorX")]
    [InlineData("mirrorY")]
    [InlineData("rotate180")]
    public void 信物格与分区在D2变换下不变(string transform)
    {
        Func<Coord, Coord> t = TransformOf(transform);

        foreach ((Coord c, RelicCellSpec spec) in Map.RelicCells)
        {
            Coord image = t(c);
            Assert.True(Map.RelicCells.ContainsKey(image), $"{c} 的像 {image} 不是信物格。");
            Assert.Equal(spec, Map.RelicCells[image]);
        }
    }

    [Theory]
    [InlineData("mirrorX")]
    [InlineData("mirrorY")]
    [InlineData("rotate180")]
    public void 咽喉与中央入口在D2变换下不变(string transform)
    {
        Func<Coord, Coord> t = TransformOf(transform);

        Assert.Equal(Map.ChokePoints.OrderBy(c => c), Map.ChokePoints.Select(t).OrderBy(c => c));
        Assert.Equal(Map.CentralEntrance, t(Map.CentralEntrance));
    }

    [Theory]
    [InlineData("mirrorX")]
    [InlineData("mirrorY")]
    [InlineData("rotate180")]
    public void 出生区在D2变换下整体互换(string transform)
    {
        Func<Coord, Coord> t = TransformOf(transform);
        var zones = Map.BirthZones.Select(z => z.OrderBy(c => c).ToImmutableArray()).ToList();

        foreach (var zone in zones)
        {
            var image = zone.Select(t).OrderBy(c => c).ToImmutableArray();
            Assert.Contains(zones, z => z.SequenceEqual(image));
        }
    }

    [Fact]
    public void 四个出生区到三类地标的距离精确相等()
    {
        ImmutableArray<Coord> contestedRelics = Map.RelicCells
            .Where(kv => kv.Value.Zone == RelicZone.Contested)
            .Select(kv => kv.Key).ToImmutableArray();

        var metrics = new (string Name, ImmutableArray<Coord> Targets)[]
        {
            ("最近公共信物", contestedRelics),
            ("中央入口", [Map.CentralEntrance]),
            ("最近咽喉", Map.ChokePoints.ToImmutableArray()),
        };

        foreach ((string name, ImmutableArray<Coord> targets) in metrics)
        {
            int[] perZone = Map.BirthZones
                .Select(zone =>
                {
                    Dictionary<Coord, int> dist = Bfs(zone);
                    return targets.Where(dist.ContainsKey).Select(t => dist[t]).Min();
                })
                .ToArray();

            Assert.Equal(perZone.Min(), perZone.Max());
            Assert.True(perZone.Max() - perZone.Min() <= Map.DistanceTolerance,
                $"到{name}的距离极差 {perZone.Max() - perZone.Min()} 超出容差 {Map.DistanceTolerance}。");
        }
    }

    [Fact]
    public void 全盘可落子格连通且无小口袋()
    {
        var playable = Map.AllCoords()
            .Where(c => Map.TerrainAt(c) == Terrain.Playable).ToImmutableHashSet();
        Dictionary<Coord, int> reached = Bfs([playable.OrderBy(c => c).First()]);

        Assert.Equal(playable.Count, reached.Count);
        Assert.Equal(109, playable.Count);
    }

    [Fact]
    public void 校验输出跨次运行确定()
    {
        // ImmutableHashSet / ImmutableDictionary 的枚举顺序不保证稳定，
        // 任何直接枚举而未先 Order() 的地方都会在这里暴露。
        string first = Describe(MapValidator.Validate(Map with { ChokePoints = [Coord.Parse("C6")] }));

        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(first, Describe(MapValidator.Validate(Map with { ChokePoints = [Coord.Parse("C6")] })));
        }
    }

    private static string Describe(MapValidationResult result) =>
        string.Join("|", result.Failures.Select(f => f.ToString()));

    /// <summary>测试内独立实现的多源 BFS，绕开障碍，只走四邻接。</summary>
    private static Dictionary<Coord, int> Bfs(IEnumerable<Coord> sources)
    {
        var dist = new Dictionary<Coord, int>();
        var queue = new Queue<Coord>();
        foreach (Coord s in sources.OrderBy(c => c))
        {
            if (Map.TerrainAt(s) == Terrain.Playable)
            {
                dist[s] = 0;
                queue.Enqueue(s);
            }
        }

        while (queue.Count > 0)
        {
            Coord current = queue.Dequeue();
            foreach ((int dx, int dy) in Directions)
            {
                int nx = current.X + dx;
                int ny = current.Y + dy;
                if (nx < 0 || nx >= Size || ny < 0 || ny >= Size)
                {
                    continue;
                }

                Coord n = new(nx, ny);
                if (Map.TerrainAt(n) == Terrain.Playable && !dist.ContainsKey(n))
                {
                    dist[n] = dist[current] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        return dist;
    }
}
