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
    /// 相机要推屏 / 夹取的大图（retire-legacy-maps 段 B 自定，取代段 A2 那张与边疆图 v2 平台表同形的 27×32）：23×34（竖长），五个正方形出生平台，0 起索引闭区间：
    /// 0 = 8×8 贴左缘（x 0–7、y 24–31，A25–H32）；1 = 6×6 右下（x 15–20、y 2–7）；2 = 7×7 右上、离右缘 1 格（x 15–21、y 25–31，Q26–W32）；
    /// 3 = 5×5 左下（x 2–6、y 3–7）；4 = 5×5 中部偏左（x 4–8、y 14–18，E15–J19）。列标跳过字母 I（围棋记法）：x 8 = J、15 = Q、21 = W。
    /// <para>推导用的几个量（各测试按这张图独立推算）：格心 x = 列 − 11、z = −(行 − 16.5)；外接矩形 = 格子 ± 0.5 再外扩 1.7 → x ±13.2、z ±18.7。
    /// 最远缩放取上限 28（一屏看全要约 43）：16:9 下纵向所见 28 × 12.7 ÷ 14.6 ≈ 24.4 &lt; 37.4（纵向可推），横向 ≈ 43.3 ≥ 26.4（横向锁中线）；4:3 下横向 ≈ 32.5 同样 ≥ 26.4。</para>
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
        Synthetic("test-camera-large-23x34", 23, 34,
        [
            Rect(0, 7, 24, 31), Rect(15, 20, 2, 7), Rect(15, 21, 25, 31), Rect(2, 6, 3, 7), Rect(4, 8, 14, 18),
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
