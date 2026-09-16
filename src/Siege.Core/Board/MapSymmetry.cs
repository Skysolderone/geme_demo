using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// C4 旋转对称检查：地图绕中心旋转 90° 后，高度、地表、障碍、桥、栅栏、信物格逐格一致，出生区编号轮换
/// （出生区 <c>i</c> 的像是出生区 <c>(i + 1) mod n</c>），咽喉集合不变，中央入口不动。
/// 不接入 <see cref="MapValidator"/>——3 人图 MUST NOT 被强制套用方形对称；由 4 人基准图的测试直接调用。
/// </summary>
/// <remarks>规格：openspec/changes/terrain-model/specs/map-definition —— Requirement: 4 人基准地图 / Scenario: 旋转对称</remarks>
public static class MapSymmetry
{
    /// <summary>绕正方形棋盘中心逆时针旋转 90°：<c>(x, y) → (w − 1 − y, x)</c>。</summary>
    public static Coord Rotate90(MapData map, Coord c)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new Coord(map.Width - 1 - c.Y, c.X);
    }

    /// <summary>逐格比对旋转前后的全部属性，返回每一处不一致的说明；空即对称。顺序确定（先行后列）。</summary>
    public static ImmutableArray<string> RotationDefects(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        ImmutableArray<string>.Builder defects = ImmutableArray.CreateBuilder<string>();

        if (map.Width != map.Height || map.Width <= 0)
        {
            defects.Add($"外接尺寸 {map.Width}×{map.Height} 不是正方形，无法绕中心旋转 90°。");
            return defects.ToImmutable();
        }

        foreach (Coord p in map.AllCoords())
        {
            Coord q = Rotate90(map, p);
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
            int? expectedQ = zoneP is { } z && map.BirthZones.Length > 0 ? (z + 1) % map.BirthZones.Length : null;
            if (zoneQ != expectedQ)
            {
                defects.Add($"{pair}：出生区应由 {Describe(zoneP)} 轮换为 {Describe(expectedQ)}，实际为 {Describe(zoneQ)}。");
            }
        }

        foreach (FenceEdge fence in map.TerrainData.Fences.OrderBy(f => f.A).ThenBy(f => f.B))
        {
            var image = new FenceEdge(Rotate90(map, fence.A), Rotate90(map, fence.B));
            if (!map.TerrainData.Fences.Contains(image))
            {
                defects.Add($"栅栏 {fence} 的像 {image} 不是栅栏。");
            }
        }

        foreach (Coord choke in map.ChokePoints.Order())
        {
            Coord image = Rotate90(map, choke);
            if (!map.ChokePoints.Contains(image))
            {
                defects.Add($"咽喉 {choke.ToNotation()} 的像 {image.ToNotation()} 不是咽喉。");
            }
        }

        if (Rotate90(map, map.CentralEntrance) != map.CentralEntrance)
        {
            defects.Add($"中央入口 {map.CentralEntrance.ToNotation()} 不在旋转中心。");
        }

        return defects.ToImmutable();
    }

    /// <summary>地图是否 C4 对称。</summary>
    public static bool IsC4Symmetric(MapData map) => RotationDefects(map).IsEmpty;

    private static string Describe(int? zone) => zone is { } z ? $"出生区 {z}" : "非出生区";
}
