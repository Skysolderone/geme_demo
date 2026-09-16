using System.Collections.Immutable;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 4 人原型基准地图 v2：外接 11×11，36 个障碍（占外接 29.8%），85 个可落子格，
/// 4 个出生区各 13 格（区形仍是 15 格阶梯三角，区内各挖 2 格障碍），14 个信物格。
/// </summary>
/// <remarks>
/// <para><b>为什么用 D2 对称。</b>出生区距离容差被定为 1（最严格的一条地图约束）。
/// 让地图在"水平镜像 + 垂直镜像 + 180° 旋转"下完全不变，四个出生区就互为对称像，
/// 到公共信物、中央入口与咽喉的最短距离<b>精确相等</b>（极差 0），容差自然满足。
/// 手工摆格子去凑容差 1 几乎不可能，对称是唯一可靠的办法。</para>
/// <para>因此下面所有元素都用"种子格 + D2 轨道"生成，而不是逐格抄写坐标——
/// 抄写迟早会漏一个镜像像，而漏掉的那个恰好会让容差校验失败且极难定位。</para>
/// <para><b>本图的校验参数与豁免记录：</b>
/// 距离容差 = 1（默认值，未放宽——D2 对称使四区距离精确相等，极差为 0）；
/// 两眼最小格数 = 8（默认值）；
/// 必死口袋豁免：<b>无</b>——全盘 85 个可落子格连通成一块，不存在面积小于 8 的封闭空区。
/// 任何后续改动若需要豁免，必须同时写明理由，否则校验不通过。</para>
/// <para><b>v2 为什么加障碍。</b>v1（109 个可落子格、12 个障碍）下首次提子平均发生在第 6.52 大回合，
/// 而 §16 的目标是第 4–5：实测密度曲线（第 3–7 大回合 26/37/50/60/65%）说明首次提子稳定发生在
/// 盘面占到可落子格 62% 时，109 格把这条线推到了第 6 大回合。把可落子格压到 85，
/// 第 4 大回合结束的 52 枚即达 61%，冲突落回第 5 大回合。围杀要堵掉整条棋串的气，
/// 空旷棋盘上做不到——障碍同时是围杀需要的墙。（denser-map 裁决 1/2）</para>
/// /// <para>规格：openspec/changes/add-board-core/specs/map-definition —— Requirement: 4 人基准地图</para>
/// </remarks>
public static class FourPlayerBaseMap
{
    private const int Size = 11;

    /// <summary>出生区形状：左下角 <c>x + y ≤ 4</c> 的阶梯三角，15 格；区内挖 2 格障碍后可落子 13 格。</summary>
    private const int BirthZoneReach = 4;

    /// <summary>
    /// 公共区与通道上的障碍种子（28 个障碍 = 6 个一般轨道 ×4 + 2 个轴上轨道 ×2）。
    /// <list type="bullet">
    /// <item>C5/C7 与 J5/J7 夹出左右两侧的咽喉，E3/G3 与 E9/G9 夹出上下两侧的咽喉，
    /// A6/L6 与 F1/F11 收窄四条边缘走廊（v1 既有的 12 格）。</item>
    /// <item>D5/H5/D7/H7 与既有的 C5/J5/C7/J7 合成左右两道横墙，只在 B、F、K 三列留门。</item>
    /// <item>D3/H3/D9/H9 与既有的 E3/G3/E9/G9 合成上下两道横墙，只在 A、C、F、J、L 五列留门。</item>
    /// <item>E5/G5/E7/G7 卡住中央入口 F6 的四个斜角，把"抢中央"收成十字窄口。</item>
    /// <item>E2/G2/E10/G10 与既有的 F1/F11 把上下两条边缘走廊再收一档。</item>
    /// </list>
    /// </summary>
    private static readonly string[] ObstacleSeeds = ["C5", "E3", "A6", "F1", "D3", "D5", "E2", "E5"];

    /// <summary>
    /// 出生区内的障碍种子：每个轨道在四个区各落 1 格，两个轨道共让每区从 15 格降到 13 格（裁决 4）。
    /// 两个种子都落在出生区朝向公共区的斜边 <c>x + y = 4</c> 上，把出生区的外沿削成锯齿，
    /// 压缩发生在"出区方向"而不是区内纵深——前三大回合的 9 枚部署仍有 13 格可用。
    /// </summary>
    private static readonly string[] BirthObstacleSeeds = ["E1", "B4"];

    /// <summary>出生区信物种子：每区 2 枚，一枚深处、一枚靠近出口。</summary>
    private static readonly string[] BirthRelicSeeds = ["B2", "C3"];

    /// <summary>公共区标准档信物种子（中环四角）。</summary>
    private static readonly string[] ContestedStandardSeeds = ["D4"];

    /// <summary>公共区高档信物种子：紧邻上下咽喉，风险最高。</summary>
    private static readonly string[] ContestedHighSeeds = ["F4"];

    /// <summary>咽喉种子：C6/J6 是左右两侧障碍之间的唯一通道，F3/F9 是上下两侧的唯一通道。</summary>
    private static readonly string[] ChokeSeeds = ["C6", "F3"];

    /// <summary>构建 4 人基准地图。</summary>
    public static MapData Create()
    {
        ImmutableHashSet<Coord> obstacles = Orbit(ObstacleSeeds).Union(Orbit(BirthObstacleSeeds));

        ImmutableArray<ImmutableHashSet<Coord>> birthZones =
        [
            BirthZone(flipX: false, flipY: false),
            BirthZone(flipX: true, flipY: false),
            BirthZone(flipX: false, flipY: true),
            BirthZone(flipX: true, flipY: true),
        ];

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

        foreach (Coord c in Orbit(ContestedHighSeeds))
        {
            relics[c] = new RelicCellSpec(RelicZone.Contested, BudgetTier.High);
        }

        return new MapData
        {
            Id = "siege-4p-base-v2",
            Width = Size,
            Height = Size,
            MaxPlayers = 4,
            Obstacles = obstacles,
            BirthZones = birthZones,
            RelicCells = relics.ToImmutable(),
            ChokePoints = Orbit(ChokeSeeds),
            CentralEntrance = Coord.Parse("F6"),
            DistanceTolerance = 1,
            MinTwoEyeArea = 8,
        };
    }

    /// <summary>把种子格展开成它在 D2 对称群下的完整轨道（含自身）。</summary>
    private static ImmutableHashSet<Coord> Orbit(IEnumerable<string> seeds)
    {
        ImmutableHashSet<Coord>.Builder builder = ImmutableHashSet.CreateBuilder<Coord>();
        foreach (string seed in seeds)
        {
            Coord c = Coord.Parse(seed);
            int mx = Size - 1 - c.X;
            int my = Size - 1 - c.Y;
            builder.Add(c);
            builder.Add(new Coord(mx, c.Y));
            builder.Add(new Coord(c.X, my));
            builder.Add(new Coord(mx, my));
        }

        return builder.ToImmutable();
    }

    /// <summary>生成一个角落出生区，按需要做水平 / 垂直镜像。</summary>
    private static ImmutableHashSet<Coord> BirthZone(bool flipX, bool flipY)
    {
        ImmutableHashSet<Coord>.Builder builder = ImmutableHashSet.CreateBuilder<Coord>();
        for (int y = 0; y <= BirthZoneReach; y++)
        {
            for (int x = 0; x + y <= BirthZoneReach; x++)
            {
                builder.Add(new Coord(flipX ? Size - 1 - x : x, flipY ? Size - 1 - y : y));
            }
        }

        return builder.ToImmutable();
    }
}
