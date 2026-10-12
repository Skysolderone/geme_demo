using Siege.Presentation.Camera;
using static Siege.Core.Tests.ViewportCamera.CameraFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/viewport-camera「边界夹取」「回到出生平台」「全局预览」（tasks 4.4，裁决 D22 / D26）。
/// 相机的可行矩形取"当前战场 ∪ 5 个占位"的包围盒（<see cref="BoardCamera.Bounds"/>），全局预览与"一屏看全"取当前战场的包围盒（<see cref="BoardCamera.Focus"/>）；
/// 链接后包围盒扩大时<b>保位姿重定界</b>（<see cref="BoardCamera.Rebound"/>）。旧地图两个矩形相同，行为与引入之前逐位相同。
/// </summary>
/// <remarks>变异验证（段 D-c 实跑）：M-F2 <c>Rebound</c> 改为复位到中心 / 最远 → 「链接后重定界保位姿」红 1；M-F3 <c>OverviewAvailable</c> 改回 <c>!FitsOneScreen</c> → 「单块棋盘没有全局预览_占位再远也不算」红 1。</remarks>
public class 战场范围的相机Tests
{
    /// <summary>73×73 画布上：战场 = 左下角 7×7 出生棋盘（含标注外圈），可行矩形 = 整张画布（5 个占位散在四角与中央）。</summary>
    private static readonly PlaneRect Canvas = BoundsOf(73, 73);
    private static readonly PlaneRect SingleBoard = new(Canvas.MinX, Canvas.MaxZ - 7f - (2f * Margin), Canvas.MinX + 7f + (2f * Margin), Canvas.MaxZ);
    private static readonly PlaneRect MergedField = new(Canvas.MinX, Canvas.MaxZ - 9f - (2f * Margin), Canvas.MinX + 60f, Canvas.MaxZ);

    [Fact]
    public void 单块棋盘没有全局预览_占位再远也不算()
    {
        // Scenario「单块棋盘没有全局预览」：构筑期战场只有一块 7×7，按 M 不动相机；但可行矩形是整张画布，能推到占位、能拉到最远上限。
        var camera = new BoardCamera(Canvas, focus: SingleBoard);
        Assert.False(camera.OverviewAvailable);
        Assert.False(camera.FitsOneScreen);   // 画布一屏看不全：可以推屏
        Assert.Equal(BoardCamera.FarthestCap, camera.Farthest);
        CameraPose before = camera.Pose;

        Assert.False(camera.ToggleOverview());
        Assert.False(camera.IsOverview);
        Assert.Equal(before, camera.Pose);

        camera.Pan(-1f, -1f, 30f);
        Assert.Equal(camera.Feasible.MinX, camera.Pose.FocusX, 3);   // 推到画布一角（占位所在），不是战场一角
    }

    [Fact]
    public void 链接后全局预览以当前战场为准()
    {
        // Scenario「一键看全战场再回来」「预览不显示他人战场」：合并后战场 60 格宽一屏看不全 → 预览距离按战场、注视点锁战场中心，而不是画布中心。
        var camera = new BoardCamera(Canvas, focus: MergedField);
        Assert.True(camera.OverviewAvailable);
        camera.Zoom(5);
        camera.Pan(1f, 0f, 2f);
        CameraPose before = camera.Pose;

        Assert.True(camera.ToggleOverview());
        Assert.True(camera.IsOverview);
        Assert.Equal(MergedField.CenterX, camera.Pose.FocusX, 3);
        Assert.Equal(MergedField.CenterZ, camera.Pose.FocusZ, 3);
        Assert.Equal(camera.FullViewDistance, camera.Pose.Distance);
        Assert.True(camera.VisibleWidth >= MergedField.Width && camera.VisibleDepth >= MergedField.Depth);
        Assert.True(camera.VisibleWidth < Canvas.Width, "预览只盖当前战场，不盖整张画布");

        Assert.True(camera.ToggleOverview());
        Assert.Equal(before, camera.Pose);
    }

    [Fact]
    public void 链接后重定界保位姿()
    {
        // Scenario「链接后范围扩大」：战场由单块扩成合并战场，可行矩形随之更新；相机位姿不复位（注视点与距离原样，只按新矩形重夹）。
        var camera = new BoardCamera(Canvas, focus: SingleBoard);
        camera.Zoom(4);
        camera.Pan(-1f, -1f, 1f);
        CameraPose before = camera.Pose;

        camera.Rebound(Canvas, MergedField);

        Assert.Equal(before, camera.Pose);
        Assert.Equal(MergedField, camera.Focus);
        Assert.True(camera.OverviewAvailable);

        // 可行矩形扩大的情形：画布中央一块 7×7 自成战场时相机只能在它上面动；重定界到整张画布后位姿原样、此前推不到的区域现在可以推到。
        PlaneRect inner = new(-5.2f, -5.2f, 5.2f, 5.2f);
        var narrow = new BoardCamera(inner);
        narrow.Pan(0f, 1f, 30f);
        CameraPose pose = narrow.Pose;
        PlaneRect narrowFeasible = narrow.Feasible;
        Assert.Equal(narrowFeasible.MinZ, pose.FocusZ, 3);   // 已推到小战场可行矩形的边

        narrow.Rebound(Canvas, inner);

        Assert.Equal(pose, narrow.Pose);   // 保位姿：注视点与距离原样（它在新可行矩形之内）
        narrow.Pan(0f, 1f, 30f);
        Assert.True(narrow.Pose.FocusZ < narrowFeasible.MinZ - 1f, "此前推不到的区域现在可以推到");

        // 相同矩形重定界什么都不变；非法矩形拒绝。
        camera.Rebound(Canvas, MergedField);
        Assert.Equal(before, camera.Pose);
        Assert.Throws<ArgumentException>(() => camera.Rebound(new PlaneRect(0f, 0f, 0f, 1f)));
    }

    [Fact]
    public void 旧地图两个矩形相同_行为逐位不变()
    {
        // Legacy：不给 focus 时等于 bounds，FitsOneScreen / OverviewAvailable / 预览距离与注视点与引入之前相同。
        var v4 = new BoardCamera(BoundsOf(V4));
        Assert.Equal(v4.Bounds, v4.Focus);
        Assert.True(v4.FitsOneScreen);
        Assert.False(v4.OverviewAvailable);
        Assert.False(v4.ToggleOverview());

        var frontier = new BoardCamera(BoundsOf(Frontier));
        Assert.Equal(frontier.Bounds, frontier.Focus);
        Assert.False(frontier.FitsOneScreen);
        Assert.True(frontier.OverviewAvailable);
        Assert.Equal(frontier.FullViewDistance, frontier.BoundsViewDistance);
        Assert.True(frontier.ToggleOverview());
        Assert.Equal((frontier.Bounds.CenterX, frontier.Bounds.CenterZ), (frontier.Pose.FocusX, frontier.Pose.FocusZ));
    }

    [Fact]
    public void 预览中重定界_预览随新战场重算()
    {
        var camera = new BoardCamera(Canvas, focus: MergedField);
        Assert.True(camera.ToggleOverview());
        float distance = camera.Pose.Distance;
        PlaneRect wider = MergedField with { MaxX = MergedField.MaxX + 10f };

        camera.Rebound(Canvas, wider);

        Assert.True(camera.IsOverview);
        Assert.True(camera.Pose.Distance > distance);
        Assert.Equal(wider.CenterX, camera.Pose.FocusX, 3);
    }
}
