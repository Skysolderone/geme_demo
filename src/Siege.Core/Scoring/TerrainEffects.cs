using Siege.Core.Board;

namespace Siege.Core.Scoring;

/// <summary>
/// 地形军势修正（terrain-bonus 裁决 3–7，唯一实现）：棋子所在格的地表与高度各给一个加法修正，逐枚计入该棋子的基础军势，
/// 与既有限制型地形效果（林地不接收覆盖、沼泽 / 岩石不产生覆盖、空浅滩与空深水不算气等）并存；与"高地压制加值"（位置加值）是两回事，MUST NOT 合并。
/// 名字带 <c>Uncalibrated</c> 的取值都是<b>未校准的初值</b>，不是基线。
/// </summary>
public static class TerrainEffects
{
    /// <summary>地表修正（未校准）：草地 / 土路 / 岩台 0，林地 / 荒漠 / 浅滩 +1，沼泽 / 岩石 +2，未架桥深水 −1（架桥后按 0）。</summary>
    public static int UncalibratedSurfaceModifier(Surface surface) => surface switch
    {
        Surface.Grass or Surface.Road or Surface.Crag => 0,
        Surface.Forest or Surface.Desert or Surface.Shallows => 1,
        Surface.Marsh or Surface.Rock => 2,
        Surface.DeepWater => -1,
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, "未知地表。"),
    };

    /// <summary>高度修正（未校准，裁决 6）：h=0 为 0、h=1 为 +1、h=2 为 +2。</summary>
    public static int UncalibratedHeightModifier(int height) => height switch
    {
        0 => 0,
        1 => 1,
        2 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(height), height, "高度只有 0–2 三档。"),
    };

    /// <summary>某格的地形修正合计：地表修正（桥格不吃深水修正，按 0）+ 高度修正。</summary>
    public static int ModifierAt(MapData map, Coord c)
    {
        ArgumentNullException.ThrowIfNull(map);
        Surface surface = map.SurfaceAt(c);
        int surfaceModifier = surface == Surface.DeepWater && map.HasBridge(c) ? 0 : UncalibratedSurfaceModifier(surface);
        return surfaceModifier + UncalibratedHeightModifier(map.HeightAt(c));
    }

    /// <summary>棋子的有效基础军势：<c>max(0, 类型基础军势 + 该格地形修正)</c>，逐枚不为负。</summary>
    public static int EffectiveBasePower(PieceType type, MapData map, Coord c) =>
        Math.Max(0, PieceEffects.BasePower(type) + ModifierAt(map, c));
}
