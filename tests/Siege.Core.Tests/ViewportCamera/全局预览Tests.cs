using Siege.Presentation.Camera;
using static Siege.Core.Tests.ViewportCamera.CameraFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>规格：openspec/changes/frontier-map/specs/viewport-camera「全局预览」。</summary>
public class 全局预览Tests
{
    [Fact]
    public void 切入全局预览_整盘一屏可见且注视点在地图中心()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(6);
        camera.Pan(-1f, 1f, 2f);

        Assert.True(camera.ToggleOverview());

        Assert.True(camera.IsOverview);
        PlaneRect bounds = camera.Bounds;
        Assert.Equal(bounds.CenterX, camera.Pose.FocusX);
        Assert.Equal(bounds.CenterZ, camera.Pose.FocusZ);
        Assert.True(camera.Pose.Distance > BoardCamera.FarthestCap, "全局预览要越过平时的最远上限才看得全边疆图");
        Assert.True(camera.VisibleDepth >= bounds.Depth);
        Assert.True(camera.VisibleWidth >= bounds.Width);
    }

    [Fact]
    public void 再切一次_回到切入前的位姿()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(6);
        camera.Pan(-1f, 1f, 2f);
        CameraPose before = camera.Pose;

        camera.ToggleOverview();
        Assert.True(camera.ToggleOverview());

        Assert.False(camera.IsOverview);
        Assert.Equal(before, camera.Pose);
    }

    [Fact]
    public void 全局预览中_推屏与拉远无效()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.ToggleOverview();
        CameraPose overview = camera.Pose;

        camera.Pan(1f, 1f, 3f);
        camera.Zoom(-3);

        Assert.True(camera.IsOverview);
        Assert.Equal(overview, camera.Pose);
    }

    [Fact]
    public void 全局预览中拉近_退出预览并从最远缩放继续()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.ToggleOverview();

        camera.Zoom(1);

        Assert.False(camera.IsOverview);
        Assert.Equal(camera.Farthest * BoardCamera.ZoomStepFactor, camera.Pose.Distance, 3);
    }

    [Fact]
    public void 全局预览中回家_退出预览_距离回到切入前_注视点到家()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(6);
        float distance = camera.Pose.Distance;
        camera.ToggleOverview();

        PlaneRect bounds = camera.Bounds;
        camera.Home(bounds.CenterX, bounds.CenterZ);

        Assert.False(camera.IsOverview);
        Assert.Equal(distance, camera.Pose.Distance);
        Assert.Equal(bounds.CenterX, camera.Pose.FocusX);
        Assert.Equal(bounds.CenterZ, camera.Pose.FocusZ);
    }

    [Fact]
    public void 全局预览不改俯角_位姿仍只由注视点与距离决定()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        CameraPose normal = camera.Pose;
        camera.ToggleOverview();
        CameraPose overview = camera.Pose;

        // 俯角 = atan2(Eye.Y − Target.Y, Eye.Z − Target.Z)，两种状态下相同。
        static float Pitch(CameraPose p) => MathF.Atan2(p.Eye.Y - p.Target.Y, p.Eye.Z - p.Target.Z);
        Assert.Equal(Pitch(normal), Pitch(overview), 5);
    }

    [Fact]
    public void 窗口变形时_全局预览仍整盘可见()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.ToggleOverview();

        camera.SetAspect(4f / 3f);

        Assert.True(camera.IsOverview);
        Assert.True(camera.VisibleDepth >= camera.Bounds.Depth);
        Assert.True(camera.VisibleWidth >= camera.Bounds.Width);
    }

    [Fact]
    public void 一屏看全的地图上_没有全局预览可切()
    {
        var camera = new BoardCamera(BoundsOf(V4));
        CameraPose before = camera.Pose;

        Assert.False(camera.ToggleOverview());

        Assert.False(camera.IsOverview);
        Assert.Equal(before, camera.Pose);
    }

    [Fact]
    public void 开局对准与自检置位都会退出全局预览()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.ToggleOverview();
        camera.Open(CameraHome.Platform(Frontier.BirthZones[4], CenterOf(Frontier)));
        Assert.False(camera.IsOverview);
        Assert.True(camera.Pose.Distance <= camera.Farthest);

        camera.ToggleOverview();
        camera.Set(new CameraPose(0f, 0f, camera.Nearest));
        Assert.False(camera.IsOverview);
        Assert.Equal(camera.Nearest, camera.Pose.Distance);
    }
}
