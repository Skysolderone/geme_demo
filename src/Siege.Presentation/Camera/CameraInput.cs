using Siege.Core.Board;

namespace Siege.Presentation.Camera;

/// <summary>贴边推屏的意图计算（viewport-camera「推屏与平移」）：指针进入窗口边缘感应带即朝该方向推，角上沿对角。</summary>
public static class EdgePan
{
    /// <summary>感应带宽度（像素）。</summary>
    public const float BandPx = 24f;

    /// <summary>
    /// 指针位置 → 平移意图（右为 +、上为 +，各取 −1 / 0 / 1）。指针不在视口内（含恰在视口之外）一律为零——
    /// "失焦 / 出窗 / 在面板上"三种抑制由引擎侧用窗口与控件的现成信息判定后不调用本函数或丢弃其结果。
    /// </summary>
    public static (float Right, float Up) Intent(float x, float y, float width, float height, float band = BandPx)
    {
        if (!(width > 0f) || !(height > 0f) || x < 0f || y < 0f || x > width || y > height)
        {
            return (0f, 0f);
        }

        float right = x >= width - band ? 1f : x <= band ? -1f : 0f;
        float up = y <= band ? 1f : y >= height - band ? -1f : 0f;
        return (right, up);
    }
}

/// <summary>回家目标（viewport-camera「回到出生平台」）：出生平台中心 = 该出生区格子的外接矩形中心；未选区时为地图中心。</summary>
public static class CameraHome
{
    /// <summary>
    /// <paramref name="zoneCells"/> 为本机玩家出生区的全部格子（未选区传空）；<paramref name="centerOf"/> 是引擎侧
    /// <c>Coord → 世界 (x, z)</c> 的<b>唯一</b>映射，本类不另算格心（coordinates.md「映射唯一」）。
    /// </summary>
    public static (float X, float Z) Target(IEnumerable<Coord> zoneCells, Func<Coord, (float X, float Z)> centerOf, PlaneRect mapBounds) =>
        Platform(zoneCells, centerOf) is { } platform ? (platform.CenterX, platform.CenterZ) : (mapBounds.CenterX, mapBounds.CenterZ);

    /// <summary>
    /// 出生平台<b>格心</b>的外接矩形（开局对准用：相机据此取"平台整个可见"的距离）；未选区为 <c>null</c>。
    /// 行列到世界轴的方向由 <paramref name="centerOf"/> 决定（行向 −Z），这里只取两角格心的最小 / 最大，不假设方向。
    /// </summary>
    public static PlaneRect? Platform(IEnumerable<Coord> zoneCells, Func<Coord, (float X, float Z)> centerOf)
    {
        ArgumentNullException.ThrowIfNull(zoneCells);
        ArgumentNullException.ThrowIfNull(centerOf);

        bool any = false;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Coord cell in zoneCells)
        {
            any = true;
            minX = Math.Min(minX, cell.X);
            minY = Math.Min(minY, cell.Y);
            maxX = Math.Max(maxX, cell.X);
            maxY = Math.Max(maxY, cell.Y);
        }

        if (!any)
        {
            return null;
        }

        (float ax, float az) = centerOf(new Coord(minX, minY));
        (float bx, float bz) = centerOf(new Coord(maxX, maxY));
        return new PlaneRect(MathF.Min(ax, bx), MathF.Min(az, bz), MathF.Max(ax, bx), MathF.Max(az, bz));
    }
}

/// <summary>
/// 悬停格坐标读数（viewport-camera「悬停格坐标读数」）：文本 MUST 取自 <see cref="Coord.ToNotation"/>——与对局日志同一份映射；
/// 指针不在任何格上读数为空。
/// </summary>
public static class HoverReadout
{
    /// <summary>读数文本。</summary>
    public static string Of(Coord? hover) => hover?.ToNotation() ?? string.Empty;
}
