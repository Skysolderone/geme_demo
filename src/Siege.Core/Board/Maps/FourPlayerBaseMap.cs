using System.Collections.Immutable;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 4 人原型基准地图 v3：外接 13×13，C4 旋转对称，四家出生在角落的 h=2 高台，中央 h=0 低地被一圈一格宽的护城河环绕。
/// 可落子格 105（岩石 36、深水 32 其中桥 4），4 个出生区各 13 格全部 h=2，信物格 13（出生区 8、公共区 4 + 中心 1）。
/// </summary>
/// <remarks>
/// <para><b>地貌。</b>每个出生区是角落 4×4 减去三格的 13 格高台；朝顺时针方向的下一家经两格 h=1 缓坡（<c>E2 E3</c>）下到
/// 崖脚的三格落脚点（<c>F3 G2 G3</c>）与一条沿邻家崖脚延伸的尾巷（<c>H2 J2</c>，被邻家高台 <c>K2</c> 居高临下覆盖）；
/// 高台其余边缘直接落到 h=0 或被岩石 / 深水包住（崖壁）。四条落脚点各经一座桥（<c>G4 K7 G10 D7</c>，同时是四个咽喉）跨过护城河
/// 进入中央 5×5 岛：岛心 <c>G7</c> 是中央入口兼高档信物，四座桥头各有一枚标准档信物（<c>G5 E7 J7 G9</c>），岛的四角是林地，
/// 岛内四块岩石把它切成十字形的通道；四段栅栏（<c>E6-E7</c> 轨道）各挡在一枚桥头信物的一侧——挡气不挡覆盖。</para>
/// <para><b>为什么用 C4 而不是 D2。</b>terrain-model 裁决 D19：立体地貌做双轴镜像会把山脊与河道走向强行折叠；
/// 绕中心 90° 旋转同样让四家完全对等——到公共信物 / 中央入口 / 咽喉的沿气边最短距离精确相等（极差 0）。
/// 因此全部元素都用"种子格 + C4 轨道"生成而不是逐格抄写：抄写迟早漏一个像，漏掉的那个恰好会让距离校验失败且极难定位。</para>
/// <para><b>为什么公共信物是 5 而不是 6。</b>13×13 绕中心旋转的轨道大小只有 4（一般格）或 1（中心格）：
/// 出生区 8 枚固定，总数要落在 13–15 只剩 8 + 4 + 1 = 13 一解——一个 4 元轨道加中心格。</para>
/// <para><b>本图的校验参数与豁免记录：</b>距离容差 = 1（默认值，未放宽——C4 对称使四区距离精确相等）；
/// 两眼最小格数 = 8（默认值）；必死口袋豁免：<b>无</b>——全盘 105 个可落子格沿气边连通成一块。</para>
/// <para><b>为什么这么密。</b>map-definition 把 4 人可落子区间定为 95–110（裁决 D18）：三层高度与水系需要过渡带来表达地貌，
/// 而 169 格里只留 95–110 可落子，意味着岩石与深水要占掉近四成——密度由可落子格区间把控，不再校验障碍占比（裁决 D-F）。</para>
/// <para>规格：openspec/changes/terrain-model/specs/map-definition —— Requirement: 4 人基准地图</para>
/// </remarks>
public static class FourPlayerBaseMap
{
    private const int Size = 13;

    /// <summary>出生区 0（左下角）：<c>A1–D4</c> 的 4×4 减去角上的岩石 <c>A1</c> 与护城河转角 <c>C4 D4</c>，13 格全部 h=2。</summary>
    private static readonly string[] Zone0Excluded = ["A1", "C4", "D4"];

    /// <summary>
    /// 岩石种子（36 个岩石 = 9 个轨道 ×4）：
    /// <c>A1</c> 角石；<c>E1–J1</c> 底边岩壁；<c>F2</c> 缓坡脚下的碎石、<c>H3</c> 尾巷与护城河之间的隔石；<c>F6</c> 岛内岩石（十字通道）。
    /// </summary>
    private static readonly string[] RockSeeds = ["A1", "E1", "F1", "G1", "H1", "J1", "F2", "H3", "F6"];

    /// <summary>深水种子（32 格）：护城河底边 <c>D4–J4</c>（含桥位 <c>G4</c>）与转角 <c>C4</c>，以及尾巷尽头的 <c>J3</c>。</summary>
    private static readonly string[] WaterSeeds = ["C4", "D4", "E4", "F4", "G4", "H4", "J4", "J3"];

    /// <summary>预置桥：护城河每边正中一座，也是咽喉。</summary>
    private static readonly string[] BridgeSeeds = ["G4"];

    /// <summary>缓坡（h=1）：出生区 0 东侧两格。</summary>
    private static readonly string[] RampSeeds = ["E2", "E3"];

    /// <summary>土路（只作视觉）：缓坡与落脚点到桥头的下山主路。</summary>
    private static readonly string[] RoadSeeds = ["E2", "E3", "F3", "G3"];

    /// <summary>林地：岛的四角，不接收覆盖，只能占据。</summary>
    private static readonly string[] ForestSeeds = ["E5"];

    /// <summary>栅栏：岛西侧桥头信物 <c>E7</c> 与其南邻 <c>E6</c> 之间。</summary>
    private static readonly (string A, string B)[] FenceSeeds = [("E6", "E7")];

    /// <summary>出生区信物种子：每区 2 枚，一枚深处、一枚靠近出口。</summary>
    private static readonly string[] BirthRelicSeeds = ["B2", "C3"];

    /// <summary>公共区标准档信物：四座桥头。</summary>
    private static readonly string[] ContestedStandardSeeds = ["G5"];

    /// <summary>公共区高档信物：岛心，轨道大小为 1；同时是中央入口。</summary>
    private const string Center = "G7";

    /// <summary>咽喉：四座桥。</summary>
    private static readonly string[] ChokeSeeds = ["G4"];

    /// <summary>构建 4 人基准地图。</summary>
    public static MapData Create()
    {
        ImmutableHashSet<Coord> zone0 = CornerBlock().Except(Zone0Excluded.Select(Coord.Parse));
        ImmutableArray<ImmutableHashSet<Coord>>.Builder zones = ImmutableArray.CreateBuilder<ImmutableHashSet<Coord>>(4);
        ImmutableHashSet<Coord> zone = zone0;
        for (int i = 0; i < 4; i++)
        {
            zones.Add(zone);
            zone = zone.Select(Rotate).ToImmutableHashSet();
        }

        ImmutableDictionary<Coord, int>.Builder heights = ImmutableDictionary.CreateBuilder<Coord, int>();
        foreach (Coord c in zones.SelectMany(z => z))
        {
            heights[c] = 2;
        }

        foreach (Coord c in Orbit(RampSeeds))
        {
            heights[c] = 1;
        }

        ImmutableDictionary<Coord, Surface>.Builder surfaces = ImmutableDictionary.CreateBuilder<Coord, Surface>();
        foreach (Coord c in Orbit(RoadSeeds))
        {
            surfaces[c] = Surface.Road;
        }

        foreach (Coord c in Orbit(ForestSeeds))
        {
            surfaces[c] = Surface.Forest;
        }

        foreach (Coord c in Orbit(WaterSeeds))
        {
            surfaces[c] = Surface.DeepWater;
        }

        ImmutableHashSet<FenceEdge> fences = FenceSeeds
            .SelectMany(f => OrbitOfEdge(Coord.Parse(f.A), Coord.Parse(f.B)))
            .ToImmutableHashSet();

        var terrain = new TerrainData(heights.ToImmutable(), surfaces.ToImmutable(), Orbit(BridgeSeeds), fences);

        ImmutableDictionary<Coord, RelicCellSpec>.Builder relics =
            ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();

        foreach (Coord c in Orbit(BirthRelicSeeds))
        {
            relics[c] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
        }

        foreach (Coord c in Orbit(ContestedStandardSeeds))
        {
            relics[c] = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        }

        relics[Coord.Parse(Center)] = new RelicCellSpec(RelicZone.Contested, BudgetTier.High);

        return new MapData
        {
            Id = "siege-4p-base-v3",
            Width = Size,
            Height = Size,
            MaxPlayers = 4,
            Obstacles = Orbit(RockSeeds),
            TerrainData = terrain,
            BirthZones = zones.ToImmutable(),
            RelicCells = relics.ToImmutable(),
            ChokePoints = Orbit(ChokeSeeds),
            CentralEntrance = Coord.Parse(Center),
            DistanceTolerance = 1,
            MinTwoEyeArea = 8,
        };
    }

    /// <summary>绕中心 <c>(6, 6)</c> 逆时针旋转 90°：<c>(x, y) → (12 − y, x)</c>。左下角 → 右下角 → 右上角 → 左上角。</summary>
    private static Coord Rotate(Coord c) => new(Size - 1 - c.Y, c.X);

    /// <summary>把种子格展开成它在 C4 旋转群下的完整轨道（含自身）。</summary>
    private static ImmutableHashSet<Coord> Orbit(IEnumerable<string> seeds)
    {
        ImmutableHashSet<Coord>.Builder builder = ImmutableHashSet.CreateBuilder<Coord>();
        foreach (string seed in seeds)
        {
            Coord c = Coord.Parse(seed);
            for (int i = 0; i < 4; i++)
            {
                builder.Add(c);
                c = Rotate(c);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>栅栏边的 C4 轨道：两端一起旋转，<see cref="FenceEdge"/> 构造时归一化端点顺序。</summary>
    private static IEnumerable<FenceEdge> OrbitOfEdge(Coord a, Coord b)
    {
        for (int i = 0; i < 4; i++)
        {
            yield return new FenceEdge(a, b);
            a = Rotate(a);
            b = Rotate(b);
        }
    }

    /// <summary>左下角 4×4 方块。</summary>
    private static ImmutableHashSet<Coord> CornerBlock()
    {
        ImmutableHashSet<Coord>.Builder builder = ImmutableHashSet.CreateBuilder<Coord>();
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                builder.Add(new Coord(x, y));
            }
        }

        return builder.ToImmutable();
    }
}
