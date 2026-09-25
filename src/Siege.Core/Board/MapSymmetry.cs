using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// C4 / C2 旋转对称检查：地图绕中心旋转 90° 后，高度、地表、障碍、桥、栅栏、信物格逐格一致，出生区编号轮换
/// （出生区 <c>i</c> 的像是出生区 <c>(i + 1) mod n</c>），咽喉集合不变，中央入口不动。
/// C2（180°，<see cref="Rotation180Defects"/>）同一口径，出生区 <c>i</c> 的像是 <c>(i + n/2) mod n</c>。
/// 不接入 <see cref="MapValidator"/>——3 人图 MUST NOT 被强制套用方形对称；由 4 人 / 2 人基准图的测试直接调用。
/// </summary>
/// <remarks>规格：openspec/changes/terrain-model/specs/map-definition —— Requirement: 4 人基准地图 / Scenario: 旋转对称；
/// openspec/changes/small-maps/specs/map-definition —— Requirement: 2 人基准地图 / Scenario: 2 人图旋转对称</remarks>
public static class MapSymmetry
{
    /// <summary>绕正方形棋盘中心逆时针旋转 90°：<c>(x, y) → (w − 1 − y, x)</c>。</summary>
    public static Coord Rotate90(MapData map, Coord c)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new Coord(map.Width - 1 - c.Y, c.X);
    }

    /// <summary>绕棋盘中心旋转 180°：<c>(x, y) → (w − 1 − x, h − 1 − y)</c>。</summary>
    public static Coord Rotate180(MapData map, Coord c)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new Coord(map.Width - 1 - c.X, map.Height - 1 - c.Y);
    }

    /// <summary>逐格比对旋转 90° 前后的全部属性，返回每一处不一致的说明；空即对称。顺序确定（先行后列）。</summary>
    public static ImmutableArray<string> RotationDefects(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Width != map.Height || map.Width <= 0)
        {
            return [$"外接尺寸 {map.Width}×{map.Height} 不是正方形，无法绕中心旋转 90°。"];
        }

        return Defects(map, Rotate90, zoneShift: 1);
    }

    /// <summary>
    /// 逐格比对旋转 180° 前后的全部属性（C2，small-maps D1：2 人基准图）：出生区 <c>i</c> 的像是出生区 <c>(i + n/2) mod n</c>
    /// （2 个出生区即互换），其余口径同 <see cref="RotationDefects"/>。出生区数为奇数时无法两两互换，直接报出。
    /// </summary>
    public static ImmutableArray<string> Rotation180Defects(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        int zones = map.BirthZones.Length;
        if (zones % 2 != 0)
        {
            return [$"出生区 {zones} 个，不是偶数，绕中心旋转 180° 无法两两互换。"];
        }

        return Defects(map, Rotate180, zoneShift: zones / 2);
    }

    /// <summary>地图是否 C2 对称（绕中心 180° 不变）。</summary>
    public static bool IsC2Symmetric(MapData map) => Rotation180Defects(map).IsEmpty;

    /// <summary>逐格比对的唯一实现：<paramref name="rotate"/> 给出每格的像，出生区编号按 <paramref name="zoneShift"/> 轮换。</summary>
    private static ImmutableArray<string> Defects(MapData map, Func<MapData, Coord, Coord> rotate, int zoneShift)
    {
        ImmutableArray<string>.Builder defects = ImmutableArray.CreateBuilder<string>();

        foreach (Coord p in map.AllCoords())
        {
            Coord q = rotate(map, p);
            string pair = $"{p.ToNotation()} → {q.ToNotation()}";

            if (map.Obstacles.Contains(p) != map.Obstacles.Contains(q))
            {
                defects.Add($"{pair}：障碍不一致。");
            }

            if (map.HeightAt(p) != map.HeightAt(q))
            {
                defects.Add($"{pair}：高度 {map.HeightAt(p)} ≠ {map.HeightAt(q)}。");
            }

            if (map.SurfaceAt(p) != map.SurfaceAt(q))
            {
                defects.Add($"{pair}：地表 {map.SurfaceAt(p)} ≠ {map.SurfaceAt(q)}。");
            }

            if (map.HasBridge(p) != map.HasBridge(q))
            {
                defects.Add($"{pair}：桥不一致。");
            }

            bool relicP = map.RelicCells.TryGetValue(p, out RelicCellSpec specP);
            bool relicQ = map.RelicCells.TryGetValue(q, out RelicCellSpec specQ);
            if (relicP != relicQ || (relicP && specP != specQ))
            {
                defects.Add($"{pair}：信物格不一致。");
            }

            int? zoneP = map.BirthZoneOf(p);
            int? zoneQ = map.BirthZoneOf(q);
            int? expectedQ = zoneP is { } z && map.BirthZones.Length > 0 ? (z + zoneShift) % map.BirthZones.Length : null;
            if (zoneQ != expectedQ)
            {
                defects.Add($"{pair}：出生区应由 {Describe(zoneP)} 轮换为 {Describe(expectedQ)}，实际为 {Describe(zoneQ)}。");
            }
        }

        foreach (FenceEdge fence in map.TerrainData.Fences.OrderBy(f => f.A).ThenBy(f => f.B))
        {
            var image = new FenceEdge(rotate(map, fence.A), rotate(map, fence.B));
            if (!map.TerrainData.Fences.Contains(image))
            {
                defects.Add($"栅栏 {fence} 的像 {image} 不是栅栏。");
            }
        }

        foreach (Coord choke in map.ChokePoints.Order())
        {
            Coord image = rotate(map, choke);
            if (!map.ChokePoints.Contains(image))
            {
                defects.Add($"咽喉 {choke.ToNotation()} 的像 {image.ToNotation()} 不是咽喉。");
            }
        }

        if (rotate(map, map.CentralEntrance) != map.CentralEntrance)
        {
            defects.Add($"中央入口 {map.CentralEntrance.ToNotation()} 不在旋转中心。");
        }

        return defects.ToImmutable();
    }

    /// <summary>地图是否 C4 对称。</summary>
    public static bool IsC4Symmetric(MapData map) => RotationDefects(map).IsEmpty;

    private static string Describe(int? zone) => zone is { } z ? BirthZoneLabel.Of(z) : "非出生区";
}
