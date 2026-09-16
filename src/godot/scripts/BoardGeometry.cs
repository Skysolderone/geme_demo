using Godot;
using Siege.Core.Board;

namespace Siege.Godot;

/// <summary>
/// 围棋记法坐标 ↔ 3D 世界位置的<b>唯一</b>映射（.trellis/spec/core/coordinates.md「映射唯一」）。
/// A1 在左下：列 X 向右增长（+X），行 Y 向上增长（屏幕远处，即 −Z）。别处一律调用本类，不要再写第二份。
/// </summary>
public static class BoardGeometry
{
    /// <summary>格心间距。</summary>
    public const float CellSize = 1.0f;

    /// <summary>地砖边长：比格心间距小，留出的缝即网格边界（§20「方格边界始终清晰」）。</summary>
    public const float TileSize = 0.90f;

    /// <summary>地砖厚度。</summary>
    public const float TileHeight = 0.12f;

    /// <summary>地砖上表面高度。棋子、标记都以它为基准。</summary>
    public const float TopY = TileHeight * 0.5f;

    /// <summary>某格格心的世界坐标（地砖上表面）。</summary>
    public static Vector3 Center(Coord coord, int width, int height) => new(
        (coord.X - ((width - 1) * 0.5f)) * CellSize,
        TopY,
        -(coord.Y - ((height - 1) * 0.5f)) * CellSize);

    /// <summary>坐标标注离棋盘边缘的距离（格心间距的倍数）。</summary>
    public const float LabelMargin = 0.85f;

    /// <summary>
    /// 第 <paramref name="index"/> 列的字母标注锚点。<paramref name="far"/> 为 <c>true</c> 取棋盘远边（−Z），否则取近边。
    /// 位置由 <see cref="Center"/> 推出，不另算一份——标注与格子必须永远对齐（visual-style-baseline「棋盘坐标标注」）。
    /// </summary>
    public static Vector3 ColumnLabelAnchor(int index, int width, int height, bool far)
    {
        Vector3 edge = Center(new Coord(index, far ? height - 1 : 0), width, height);
        return edge with { Z = edge.Z + (far ? -LabelMargin : LabelMargin) };
    }

    /// <summary>
    /// 第 <paramref name="index"/> 行的数字标注锚点。<paramref name="right"/> 为 <c>true</c> 取棋盘右边（+X），否则取左边。
    /// </summary>
    public static Vector3 RowLabelAnchor(int index, int width, int height, bool right)
    {
        Vector3 edge = Center(new Coord(right ? width - 1 : 0, index), width, height);
        return edge with { X = edge.X + (right ? LabelMargin : -LabelMargin) };
    }

    /// <summary>世界坐标（y 任意）落在哪一格；出界为 <c>false</c>。</summary>
    public static bool TryFromWorld(Vector3 point, int width, int height, out Coord coord)
    {
        int x = Mathf.RoundToInt((point.X / CellSize) + ((width - 1) * 0.5f));
        int y = Mathf.RoundToInt((-point.Z / CellSize) + ((height - 1) * 0.5f));
        if (x < 0 || x >= width || y < 0 || y >= height)
        {
            coord = default;
            return false;
        }

        coord = new Coord(x, y);
        return true;
    }

    /// <summary>屏幕像素 → 棋盘格：把摄像机射线交到地砖上表面所在的水平面。</summary>
    public static bool TryPick(Camera3D camera, Vector2 screen, int width, int height, out Coord coord)
    {
        coord = default;
        if (camera is null)
        {
            return false;
        }

        Vector3 origin = camera.ProjectRayOrigin(screen);
        Vector3 dir = camera.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 0.0001f)
        {
            return false;
        }

        float t = (TopY - origin.Y) / dir.Y;
        return t > 0f && TryFromWorld(origin + (dir * t), width, height, out coord);
    }
}
