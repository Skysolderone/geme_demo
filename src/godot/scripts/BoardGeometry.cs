using System;
using Godot;
using Siege.Core.Board;

namespace Siege.Godot;

/// <summary>
/// 围棋记法坐标 ↔ 3D 世界位置的<b>唯一</b>映射（.trellis/spec/core/coordinates.md「映射唯一」）。
/// A1 在左下：列 X 向右增长（+X），行 Y 向上增长（屏幕远处，即 −Z）；高度（terrain-model）沿 +Y 按层抬升。别处一律调用本类，不要再写第二份。
/// </summary>
public static class BoardGeometry
{
    /// <summary>格心间距。</summary>
    public const float CellSize = 1.0f;

    /// <summary>地砖边长：比格心间距小，留出的缝即网格边界（§20「方格边界始终清晰」）。</summary>
    public const float TileSize = 0.90f;

    /// <summary>地砖厚度。</summary>
    public const float TileHeight = 0.12f;

    /// <summary>h=0 地砖上表面高度。棋子、标记都以它为基准。</summary>
    public const float TopY = TileHeight * 0.5f;

    /// <summary>
    /// 每一层高度（terrain-model 高度 0 / 1 / 2）抬升的世界高度。取格宽的 0.36，
    /// 让高台侧面在全局视角下更明显；棋子可见性与拾取需配合实际相机视角检查。
    /// </summary>
    public const float LayerHeight = 0.36f;

    /// <summary>最高层（与 <c>TerrainData.MaxHeight</c> 一致，拾取按 0..MaxLevel 逐层求交）。</summary>
    public const int MaxLevel = 2;

    /// <summary>第 <paramref name="level"/> 层地砖上表面的高度。</summary>
    public static float TopYOf(int level) => TopY + (level * LayerHeight);

    /// <summary>某格格心的世界坐标（h=0 地砖上表面）。标注锚点与棋盘外圈用它；盘内的格一律用带层数的重载。</summary>
    public static Vector3 Center(Coord coord, int width, int height) => Center(coord, width, height, 0);

    /// <summary>某格格心的世界坐标（第 <paramref name="level"/> 层地砖上表面）。层数来自视图模型的每格高度，本类不推。</summary>
    public static Vector3 Center(Coord coord, int width, int height, int level) => new(
        (coord.X - ((width - 1) * 0.5f)) * CellSize,
        TopYOf(level),
        -(coord.Y - ((height - 1) * 0.5f)) * CellSize);

    /// <summary>
    /// 近边与左右两边的坐标标注离边缘格格心的距离（格心间距的倍数）。标注平铺在棋盘外圈的 h=0 平面上，边缘格可能是 h=2 的高台：
    /// 近边与左右的遮挡只有侧向分量（约 0.3 格），1.2 足够。
    /// </summary>
    public const float LabelMargin = 1.2f;

    /// <summary>
    /// 远边（−Z）列标注的边距。相机俯角 60° 只对注视点成立，到远边第 13 行的射线仰角只有约 46°：
    /// 0.76 高的顶行地砖向远处投下 0.45 + 0.76 / tan 46° ≈ 1.2 格的遮挡区，标注中心再加半个字高，取 1.7。
    /// </summary>
    public const float FarLabelMargin = 1.7f;

    /// <summary>
    /// 第 <paramref name="index"/> 列的字母标注锚点。<paramref name="far"/> 为 <c>true</c> 取棋盘远边（−Z），否则取近边。
    /// 位置由 <see cref="Center(Coord, int, int)"/>（h=0 平面）推出，不另算一份——标注与格子必须永远对齐（visual-style-baseline「棋盘坐标标注」）。
    /// </summary>
    public static Vector3 ColumnLabelAnchor(int index, int width, int height, bool far) =>
        ColumnLabelAnchor(index, far ? height - 1 : 0, width, height, far);

    /// <summary>
    /// 第 <paramref name="index"/> 列的字母标注锚点，沿画幅边缘行 <paramref name="edgeRow"/>（当前战场 ∪ 占位的包围盒，board-link-mode 裁决 D26）：
    /// 该行格心再向外加既有边距。旧地图的画幅就是整张图，边缘行为 0 / 末行，与上一个重载逐值相同。
    /// </summary>
    public static Vector3 ColumnLabelAnchor(int index, int edgeRow, int width, int height, bool far)
    {
        Vector3 edge = Center(new Coord(index, edgeRow), width, height);
        return edge with { Z = edge.Z + (far ? -FarLabelMargin : LabelMargin) };
    }

    /// <summary>
    /// 第 <paramref name="index"/> 行的数字标注锚点。<paramref name="right"/> 为 <c>true</c> 取棋盘右边（+X），否则取左边。
    /// </summary>
    public static Vector3 RowLabelAnchor(int index, int width, int height, bool right) =>
        RowLabelAnchor(index, right ? width - 1 : 0, width, height, right);

    /// <summary>第 <paramref name="index"/> 行的数字标注锚点，沿画幅边缘列 <paramref name="edgeColumn"/>（同上，列标 / 行号的换算只在这里）。</summary>
    public static Vector3 RowLabelAnchor(int index, int edgeColumn, int width, int height, bool right)
    {
        Vector3 edge = Center(new Coord(edgeColumn, index), width, height);
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

    /// <summary>线段沿棋盘平面的候选格范围；返回的矩形右边和上边不包含在范围内。</summary>
    public static Rect2I CellsAlongSegment(Vector3 first, Vector3 last, int width, int height)
    {
        Vector2 a = CellPosition(first, width, height);
        Vector2 b = CellPosition(last, width, height);
        int minX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, b.X)) - 1, 0, width);
        int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, b.X)) + 2, 0, width);
        int minY = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, b.Y)) - 1, 0, height);
        int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, b.Y)) + 2, 0, height);
        return new Rect2I(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    /// <summary>棋盘局部包围盒覆盖的格范围；用于把跨格的可见物登记到拾取索引。</summary>
    public static Rect2I CellsOverlapping(Aabb bounds, int width, int height)
    {
        Vector2 a = CellPosition(bounds.Position, width, height);
        Vector2 b = CellPosition(bounds.End, width, height);
        int minX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, b.X) + 0.5f), 0, width);
        int maxX = Math.Clamp((int)MathF.Floor(MathF.Max(a.X, b.X) + 0.5f) + 1, 0, width);
        int minY = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, b.Y) + 0.5f), 0, height);
        int maxY = Math.Clamp((int)MathF.Floor(MathF.Max(a.Y, b.Y) + 0.5f) + 1, 0, height);
        return new Rect2I(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    // 以唯一的格心映射作为原点，避免重新实现棋盘居中与行方向换算。
    private static Vector2 CellPosition(Vector3 point, int width, int height)
    {
        Vector3 first = Center(default, width, height);
        return new Vector2((point.X - first.X) / CellSize, (first.Z - point.Z) / CellSize);
    }

    /// <summary>
    /// 屏幕像素 → 棋盘格（分层拾取，design D-H）：把摄像机射线依次交到 h=2、h=1、h=0 三层地砖上表面所在的水平面，
    /// 取<b>最近的命中</b>——命中点所在格的层数恰等于该层、且该格可落子（<paramref name="levelOf"/> 非 <c>null</c>）。
    /// 高层平面的命中点落在盘外或落在更低的格上都不算命中，继续试下一层；否则高台边缘的射线会先"穿过"高台落到身后的低地格。
    /// <paramref name="levelOf"/> 由视图模型给出每格高度，本类不读地图。
    /// </summary>
    public static bool TryPick(Camera3D camera, Vector2 screen, int width, int height, Func<Coord, int?> levelOf, out Coord coord)
    {
        coord = default;
        if (camera is null || levelOf is null)
        {
            return false;
        }

        Vector3 origin = camera.ProjectRayOrigin(screen);
        Vector3 dir = camera.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 0.0001f)
        {
            return false;
        }

        // 相机在棋盘上方，射线向下：越高的平面越先被击中，所以从最高层往下试就是"最近命中优先"。
        for (int level = MaxLevel; level >= 0; level--)
        {
            float t = (TopYOf(level) - origin.Y) / dir.Y;
            if (t <= 0f || !TryFromWorld(origin + (dir * t), width, height, out Coord candidate))
            {
                continue;
            }

            if (levelOf(candidate) == level)
            {
                coord = candidate;
                return true;
            }
        }

        return false;
    }
}
