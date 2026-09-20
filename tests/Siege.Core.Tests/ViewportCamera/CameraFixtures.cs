using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Presentation.Camera;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>viewport-camera 测试夹具：用<b>测试内独立算式</b>给出地图外接矩形与格心（不调用引擎侧 BoardGeometry——它不在解决方案里）。</summary>
internal static class CameraFixtures
{
    /// <summary>坐标标注外圈的边距（与引擎侧 <c>BoardGeometry.FarLabelMargin</c> 同值；旧固定相机的跨度公式用的就是它）。</summary>
    internal const float Margin = 1.7f;

    internal static MapData V4 => MapCatalog.Resolve("siege-4p-base-v4");

    internal static MapData Frontier => MapCatalog.Resolve("siege-frontier-v1");

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
