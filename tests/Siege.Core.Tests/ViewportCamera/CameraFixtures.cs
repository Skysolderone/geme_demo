using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Presentation.Camera;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>viewport-camera 测试夹具：用<b>测试内独立算式</b>给出地图外接矩形与格心（不调用引擎侧 BoardGeometry——它不在解决方案里）。</summary>
internal static class CameraFixtures
{
    /// <summary>坐标标注外圈的边距（与引擎侧 <c>BoardGeometry.FarLabelMargin</c> 同值；旧固定相机的跨度公式用的就是它）。</summary>
    internal const float Margin = 1.7f;

    /// <summary>
    /// "整盘一屏可见"的小图（retire-legacy-maps 段 A2 自定）：11×11（方形：旧固定相机按 max(宽, 高) 取跨度，只在方形图上与"一屏看全"的距离相同）、四个角落的 3×3 出生区（0 左下、1 右下、2 右上、3 左上）。
    /// 相机只读地图的宽高与出生区格；内置棋盘图都是大图，造不出"最远缩放一屏看全"的情形。
    /// </summary>
    internal static MapData Small { get; } = SmallMap();

    /// <summary>
    /// 相机要推屏 / 夹取的大图（retire-legacy-maps 段 A2 自定）：27×32（竖长：最远缩放时横向看全、纵向看不全），六个矩形出生平台，0 起索引闭区间：
    /// 0 = 9×9 贴左缘（x 0–8、y 22–30）；1 = 8×8（x 18–25、y 1–8）；2 = 7×7 贴右上（x 19–25、y 23–29）；3 = 6×6（x 3–8、y 2–7）；
    /// 4 = 5×5 中部偏左（x 4–8、y 13–17）；5 = 5×5（x 17–21、y 13–17）。各测试的期望值按这张图的尺寸独立推算。
    /// </summary>
    internal static MapData Large { get; } = LargeMap();

    private static MapData Synthetic(string id, int width, int height, ImmutableArray<ImmutableHashSet<Coord>> zones) => new()
    {
        Id = id,
        Width = width,
        Height = height,
        MaxPlayers = zones.Length,
        Obstacles = [],
        BirthZones = zones,
        RelicCells = ImmutableDictionary<Coord, RelicCellSpec>.Empty,
        ChokePoints = [],
        CentralEntrance = new Coord(width / 2, height / 2),
    };

    private static ImmutableHashSet<Coord> Rect(int x0, int x1, int y0, int y1) =>
        [.. Enumerable.Range(x0, x1 - x0 + 1).SelectMany(x => Enumerable.Range(y0, y1 - y0 + 1).Select(y => new Coord(x, y)))];

    private static MapData SmallMap() =>
        Synthetic("test-camera-small-11x11", 11, 11, [Rect(0, 2, 0, 2), Rect(8, 10, 0, 2), Rect(8, 10, 8, 10), Rect(0, 2, 8, 10)]);

    private static MapData LargeMap() =>
        Synthetic("test-camera-large-27x32", 27, 32,
        [
            Rect(0, 8, 22, 30), Rect(18, 25, 1, 8), Rect(19, 25, 23, 29), Rect(3, 8, 2, 7), Rect(4, 8, 13, 17), Rect(17, 21, 13, 17),
        ]);

    /// <summary>格心间距 1、棋盘以原点为中心：外接矩形 = 全部格子 + 四周各 <see cref="Margin"/>。</summary>
    internal static PlaneRect BoundsOf(int width, int height) =>
        new(-(width * 0.5f) - Margin, -(height * 0.5f) - Margin, (width * 0.5f) + Margin, (height * 0.5f) + Margin);

    internal static PlaneRect BoundsOf(MapData map) => BoundsOf(map.Width, map.Height);

    /// <summary>A1 在左下：列向 +X，行向 −Z。</summary>
    internal static Func<Coord, (float X, float Z)> CenterOf(MapData map) =>
        c => (c.X - ((map.Width - 1) * 0.5f), -(c.Y - ((map.Height - 1) * 0.5f)));

    /// <summary>
    /// 世界点是否落在画面内：用<b>真实透视投影</b>（由 Eye / Target / 垂直视场角现算视图与投影矩阵）判定，不用视图模型的线性近似——
    /// "平台整个可见"是 MUST，而线性近似只是夹取用的估算，两者不能互相作证。
    /// </summary>
    internal static bool InFrame(CameraPose pose, float aspect, float x, float y, float z)
    {
        var view = System.Numerics.Matrix4x4.CreateLookAt(pose.Eye, pose.Target, System.Numerics.Vector3.UnitY);
        var projection = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(CameraPose.FovDegrees * MathF.PI / 180f, aspect, 0.05f, 4000f);
        var clip = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(x, y, z, 1f), view * projection);
        return clip.W > 0f && Math.Abs(clip.X) <= clip.W && Math.Abs(clip.Y) <= clip.W;
    }

    /// <summary>
    /// 出生区每一格的四个格角（格心 ± 半格）在地面与 h=2 台面（0.70 高）两个高度上是否全部在画面内；
    /// <paramref name="margin"/> &gt; 0 时另要求地面上向外 <paramref name="margin"/> 格的余量圈也在画面内。
    /// </summary>
    internal static bool ZoneInFrame(CameraPose pose, float aspect, MapData map, IEnumerable<Coord> zone, float margin = 0f)
    {
        Func<Coord, (float X, float Z)> centerOf = CenterOf(map);
        foreach (Coord cell in zone)
        {
            (float cx, float cz) = centerOf(cell);
            foreach ((float y, float reach) in new[] { (0f, 0.5f + margin), (0.7f, 0.5f) })
            {
                if (!InFrame(pose, aspect, cx - reach, y, cz - reach) || !InFrame(pose, aspect, cx + reach, y, cz - reach)
                    || !InFrame(pose, aspect, cx - reach, y, cz + reach) || !InFrame(pose, aspect, cx + reach, y, cz + reach))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>由相机位置与注视目标反推的俯角（度）——不读 <see cref="CameraPose.PitchDegrees"/> 常量。</summary>
    internal static double PitchOf(CameraPose pose)
    {
        System.Numerics.Vector3 back = pose.Eye - pose.Target;
        return Math.Atan2(back.Y, Math.Sqrt((back.X * back.X) + (back.Z * back.Z))) * 180.0 / Math.PI;
    }
}
