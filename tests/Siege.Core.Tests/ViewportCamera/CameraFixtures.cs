using Siege.Core.Board;
using System.Collections.Immutable;
using Siege.Presentation.Camera;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>viewport-camera 测试夹具：用<b>测试内独立算式</b>给出地图外接矩形与格心（不调用引擎侧 BoardGeometry——它不在解决方案里）。</summary>
internal static class CameraFixtures
{
    /// <summary>坐标标注外圈的边距（与引擎侧 <c>BoardGeometry.FarLabelMargin</c> 同值；旧固定相机的跨度公式用的就是它）。</summary>
    internal const float Margin = 1.7f;

    /// <summary>
    /// 一屏看全的单块棋盘（retire-legacy-maps 1.7：原为 v5 内置图）：13×13，四个出生区与 v5 逐格相同。
    /// 相机测试只读外接尺寸、出生区格子与标识，所以期望值一字不改。
    /// </summary>
    internal static MapData V4 { get; } = new()
    {
        Id = "test-camera-13x13",
        Width = 13,
        Height = 13,
        MaxPlayers = 4,
        Obstacles = [],
        BirthZones =
        [
            Cells("B1", "C1", "D1", "A2", "B2", "C2", "D2", "A3", "B3", "C3", "D3", "A4", "B4"),
            Cells("K1", "L1", "M1", "K2", "L2", "M2", "N2", "L3", "M3", "N3", "L4", "M4", "N4"),
            Cells("M10", "N10", "K11", "L11", "M11", "N11", "K12", "L12", "M12", "N12", "K13", "L13", "M13"),
            Cells("A10", "B10", "C10", "A11", "B11", "C11", "A12", "B12", "C12", "D12", "B13", "C13", "D13"),
        ],
        RelicCells = ImmutableDictionary<Coord, RelicCellSpec>.Empty,
        ChokePoints = [],
        CentralEntrance = Coord.Parse("G7"),
    };

    /// <summary>
    /// 竖长的高台图（retire-legacy-maps 1.7：原为 siege-frontier-v2）：25×30，六个正方形出生平台（9 / 8 / 7 / 6 / 5 / 5 格边长）与边疆图逐格相同、全部 h=2。
    /// 遮挡分类与"每格至少一种缩放下严格选中自身"由 Core 相机测试在含高差的手写夹具上守护（裁决 L13），图形版 --pick-check 只验链接图。
    /// </summary>
    internal static MapData Frontier { get; } = FrontierLike();

    private static MapData FrontierLike()
    {
        ImmutableArray<ImmutableHashSet<Coord>> zones =
        [
            [.. TestMaps.Rect(0, 20, 9, 9)],
            [.. TestMaps.Rect(16, 1, 8, 8)],
            [.. TestMaps.Rect(16, 21, 7, 7)],
            [.. TestMaps.Rect(3, 2, 6, 6)],
            [.. TestMaps.Rect(4, 12, 5, 5)],
            [.. TestMaps.Rect(16, 12, 5, 5)],
        ];
        return new MapData
        {
            Id = "test-camera-25x30",
            Width = 25,
            Height = 30,
            MaxPlayers = 4,
            Obstacles = [],
            BirthZones = zones,
            RelicCells = ImmutableDictionary<Coord, RelicCellSpec>.Empty,
            ChokePoints = [],
            CentralEntrance = Coord.Parse("M15"),
            TerrainData = TestMaps.Terrain(heights: [.. zones.SelectMany(z => z).Select(c => (c.ToNotation(), 2))]),
        };
    }

    private static ImmutableHashSet<Coord> Cells(params string[] notations) => [.. notations.Select(Coord.Parse)];

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
