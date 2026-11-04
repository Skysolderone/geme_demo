using Siege.Presentation.Camera;
using static Siege.Core.Tests.ViewportCamera.CameraFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>
/// 按住鼠标左键拖动镜头（viewport-camera「推屏与平移」补充）：抓住的地面点跟着指针走——引擎侧把"抓点 − 指针此刻所指的地面点"
/// 作为世界位移交给 <see cref="BoardCamera.Drag"/>，视图模型只负责移注视点与夹取。
/// </summary>
public class 拖动平移Tests
{
    private static BoardCamera FrontierZoomedIn()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(6);
        return camera;
    }

    [Fact]
    public void 可行矩形内_注视点恰好移动给定的世界位移()
    {
        BoardCamera camera = FrontierZoomedIn();
        CameraPose before = camera.Pose;

        camera.Drag(1.5f, -2f);

        Assert.Equal(before.FocusX + 1.5f, camera.Pose.FocusX, 4);
        Assert.Equal(before.FocusZ - 2f, camera.Pose.FocusZ, 4);
        Assert.Equal(before.Distance, camera.Pose.Distance);
    }

    [Fact]
    public void 拖出地图_注视点夹在可行矩形上()
    {
        BoardCamera camera = FrontierZoomedIn();

        camera.Drag(1000f, 1000f);

        PlaneRect feasible = camera.Feasible;
        Assert.Equal(feasible.MaxX, camera.Pose.FocusX, 4);
        Assert.Equal(feasible.MaxZ, camera.Pose.FocusZ, 4);
    }

    [Fact]
    public void 全局预览中拖动无效_与推屏一致()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        Assert.True(camera.ToggleOverview());
        CameraPose before = camera.Pose;

        camera.Drag(3f, 3f);

        Assert.True(camera.IsOverview);
        Assert.Equal(before, camera.Pose);
    }
}
