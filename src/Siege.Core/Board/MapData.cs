using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>信物格的分区与预算档位。</summary>
public readonly record struct RelicCellSpec(RelicZone Zone, BudgetTier Budget);

/// <summary>
/// 地图静态数据。地形、障碍、出生区与信物格位置由设计师固定，MUST NOT 参与随机；
/// 对局内唯一的随机来源是信物的具体内容，由信物系统按对局种子生成。
/// </summary>
/// <remarks>规格：openspec/changes/add-board-core/specs/map-definition；地形字段见 openspec/changes/terrain-model/specs/terrain</remarks>
public sealed record MapData
{
    /// <summary>地图标识，写入对局日志。</summary>
    public required string Id { get; init; }

    /// <summary>外接宽度（列数）。</summary>
    public required int Width { get; init; }

    /// <summary>外接高度（行数）。</summary>
    public required int Height { get; init; }

    /// <summary>该地图支持的最大人数。出生区数量 MUST 与之相等。</summary>
    public required int MaxPlayers { get; init; }

    /// <summary>障碍格：岩石等不可通行地块。</summary>
    public required ImmutableHashSet<Coord> Obstacles { get; init; }

    /// <summary>
    /// 地形：每格高度 / 地表、预置桥与栅栏边。缺省为全平地（h=0、草地、无桥无栅），
    /// 因此旧地图与既有测试不必显式给出。桥在深水上、栅栏在相邻格间由 <see cref="Board.TerrainData"/> 构造期校验。
    /// </summary>
    public TerrainData TerrainData { get; init; } = TerrainData.Flat;

    /// <summary>出生区，下标即出生区编号。</summary>
    public required ImmutableArray<ImmutableHashSet<Coord>> BirthZones { get; init; }

    /// <summary>信物格及其分区与预算档位。</summary>
    public required ImmutableDictionary<Coord, RelicCellSpec> RelicCells { get; init; }

    /// <summary>
    /// 据点：坐标 → 档位（scoring-sites D-A）。缺省为空，旧地图与既有测试不必显式给出；
    /// 数量区间、可落子、不与信物重合、档位必填由 <see cref="MapValidator"/> 规则 8 校验。
    /// </summary>
    public ImmutableDictionary<Coord, SiteTier> Sites { get; init; } = ImmutableDictionary<Coord, SiteTier>.Empty;

    /// <summary>设计师显式标注的主要咽喉格。不做自动识别——自动识别的错误会静默污染距离均衡校验。</summary>
    public required ImmutableHashSet<Coord> ChokePoints { get; init; }

    /// <summary>中央入口。</summary>
    public required Coord CentralEntrance { get; init; }

    /// <summary>出生区距离均衡容差，默认 1。逐图放宽 MUST 同时给出 <see cref="ToleranceRelaxReason"/>。</summary>
    public int DistanceTolerance { get; init; } = 1;

    /// <summary>放宽距离容差的理由。容差大于默认值时必填。</summary>
    public string? ToleranceRelaxReason { get; init; }

    /// <summary>形成两眼所需的最小格数，默认 8（保守取值）。</summary>
    public int MinTwoEyeArea { get; init; } = 8;

    /// <summary>必死口袋校验的显式豁免格。</summary>
    public ImmutableHashSet<Coord> PocketExemptions { get; init; } = ImmutableHashSet<Coord>.Empty;

    /// <summary>每条豁免的理由，按豁免格记录。</summary>
    public ImmutableDictionary<Coord, string> PocketExemptionReasons { get; init; }
        = ImmutableDictionary<Coord, string>.Empty;

    /// <summary>该格是否在棋盘范围内。</summary>
    public bool Contains(Coord c) => c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;

    /// <summary>该格高度（0/1/2），缺省 0。</summary>
    public int HeightAt(Coord c) => TerrainData.HeightAt(c);

    /// <summary>该格地表，缺省草地。</summary>
    public Surface SurfaceAt(Coord c) => TerrainData.SurfaceAt(c);

    /// <summary>该格是否架有预置桥。</summary>
    public bool HasBridge(Coord c) => TerrainData.HasBridge(c);

    /// <summary>两格之间是否有栅栏（不分方向）。</summary>
    public bool HasFence(Coord a, Coord b) => TerrainData.HasFence(a, b);

    /// <summary>该格是否可落子：在盘内、非障碍、非未架桥的深水。桥格与林地都是可落子格。</summary>
    public bool IsPlayable(Coord c) => Contains(c) && !Obstacles.Contains(c) && !TerrainData.IsUnbridgedDeepWater(c);

    /// <summary>
    /// 该格的可落子性。越界、障碍与未架桥的深水一律为 <see cref="Terrain.Obstacle"/>（三者共享"不可落子、不可控制、不计分、封堵"的语义）；
    /// 要区分岩石与深水读 <see cref="SurfaceAt"/>。
    /// </summary>
    public Terrain TerrainAt(Coord c) => IsPlayable(c) ? Terrain.Playable : Terrain.Obstacle;

    /// <summary>该格所属出生区编号；不属于任何出生区时为 <c>null</c>。</summary>
    public int? BirthZoneOf(Coord c)
    {
        for (int i = 0; i < BirthZones.Length; i++)
        {
            if (BirthZones[i].Contains(c))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>按确定性顺序（先行后列，自下而上）枚举全部格子。</summary>
    public IEnumerable<Coord> AllCoords()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                yield return new Coord(x, y);
            }
        }
    }

    /// <summary>可落子格总数（不含障碍与未架桥的深水，含桥格）。</summary>
    public int PlayableCount => AllCoords().Count(IsPlayable);
}
