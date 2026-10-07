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

    /// <summary>该地图支持的最大人数。棋盘档出生区（出生棋盘）数 MUST 恰为它 + 1。</summary>
    public required int MaxPlayers { get; init; }

    /// <summary>
    /// 规格档，缺省 <see cref="MapProfile.Standard"/>（已删除的标准档：经 Unvalidated 入口构造的合成盘面不必显式给出，经校验加载会被拒绝）。
    /// 只供 <see cref="MapValidator"/> 选预算表与校验处理方式，对局规则不读它（frontier-map D1）。
    /// </summary>
    public MapProfile Profile { get; init; } = MapProfile.Standard;

    /// <summary>障碍格：岩石等不可通行地块。</summary>
    public required ImmutableHashSet<Coord> Obstacles { get; init; }

    /// <summary>
    /// 地形：每格高度 / 地表、预置桥与栅栏边。缺省为全平地（h=0、草地、无桥无栅），
    /// 因此旧地图与既有测试不必显式给出。桥在深水上、栅栏在相邻格间由 <see cref="Board.TerrainData"/> 构造期校验。
    /// </summary>
    public TerrainData TerrainData { get; init; } = TerrainData.Flat;

    /// <summary>出生区，下标即出生区编号。</summary>
    public required ImmutableArray<ImmutableHashSet<Coord>> BirthZones { get; init; }

    /// <summary>
    /// 棋盘清单（board-map D2），缺省为空（旧地图与既有测试不必显式给出）。出生棋盘与出生区一一对应、次序一致。
    /// 只用于静态校验与呈现，对局规则不读它；棋盘档地图的清单不得为空。
    /// </summary>
    public ImmutableArray<BoardPlate> Boards { get; init; } = [];

    /// <summary>信物格及其分区与预算档位。</summary>
    public required ImmutableDictionary<Coord, RelicCellSpec> RelicCells { get; init; }

    /// <summary>
    /// 显式标注的主要咽喉格（只进距离报告的"最近咽喉"一项，标了就必须可落子）。棋盘档生成器恒写空；
    /// 标准档 / 边疆档"必须标注咽喉"的规则已于 retire-legacy-maps 删除，字段保留以免改变内置棋盘图的导出摘要。
    /// </summary>
    public required ImmutableHashSet<Coord> ChokePoints { get; init; }

    /// <summary>中央入口。</summary>
    public required Coord CentralEntrance { get; init; }

    /// <summary>
    /// 出生区距离均衡容差，默认 1。retire-legacy-maps 段 C 起校验器不再读它（距离只报告），字段与导出格式保留以免改变内置棋盘图的导出摘要。
    /// </summary>
    public int DistanceTolerance { get; init; } = 1;

    /// <summary>放宽距离容差的理由（校验器不再读，见 <see cref="DistanceTolerance"/>）。</summary>
    public string? ToleranceRelaxReason { get; init; }

    /// <summary>
    /// 形成两眼所需的最小格数，默认 8。原用于必死口袋校验，该规则只服务标准档 / 边疆档的不规则出生区，已于 retire-legacy-maps 段 C 删除；
    /// 字段与导出格式保留以免改变内置棋盘图的导出摘要。
    /// </summary>
    public int MinTwoEyeArea { get; init; } = 8;

    /// <summary>必死口袋校验的显式豁免格（校验器不再读，见 <see cref="MinTwoEyeArea"/>）。</summary>
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
