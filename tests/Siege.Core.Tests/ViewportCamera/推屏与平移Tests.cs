using Siege.Presentation.Camera;
using static Siege.Core.Tests.ViewportCamera.CameraFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>规格：viewport-camera —— Requirement: 推屏与平移（change `frontier-map`，tasks 4.1）。</summary>
public class 推屏与平移Tests
{
    private static BoardCamera FrontierZoomedIn()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(6);
        return camera;
    }

    [Fact]
    public void 平移只改注视点不改距离与俯角()
    {
        BoardCamera camera = FrontierZoomedIn();
        CameraPose before = camera.Pose;

        camera.Pan(1f, 0f, 0.1f);

        Assert.True(camera.Pose.FocusX > before.FocusX);
        Assert.Equal(before.FocusZ, camera.Pose.FocusZ);
        Assert.Equal(before.Distance, camera.Pose.Distance);
        Assert.Equal(60.0, PitchOf(camera.Pose), 3);
    }

    [Fact]
    public void 朝向恒定_相机始终在注视目标的正后上方()
    {
        BoardCamera camera = FrontierZoomedIn();
        camera.Pan(-1f, 1f, 0.5f);

        System.Numerics.Vector3 back = camera.Pose.Eye - camera.Pose.Target;
        Assert.Equal(0f, back.X);
        Assert.True(back.Z > 0f, "相机在注视目标的 +Z 侧，棋盘的上（−Z）才朝屏幕上方");
        Assert.True(back.Y > 0f);
    }

    [Fact]
    public void 屏幕上对应负Z_屏幕右对应正X()
    {
        BoardCamera camera = FrontierZoomedIn();
        CameraPose before = camera.Pose;

        camera.Pan(0f, 1f, 0.1f);

        Assert.True(camera.Pose.FocusZ < before.FocusZ);
        Assert.Equal(before.FocusX, camera.Pose.FocusX);
    }

    [Fact]
    public void 对角推屏_两个方向同时平移()
    {
        BoardCamera camera = FrontierZoomedIn();
        CameraPose before = camera.Pose;

        camera.Pan(-1f, 1f, 0.1f);

        Assert.True(camera.Pose.FocusX < before.FocusX);
        Assert.True(camera.Pose.FocusZ < before.FocusZ);
    }

    [Fact]
    public void 平移速度与距离成正比()
    {
        var near = new BoardCamera(BoundsOf(Frontier));
        near.Zoom(10);
        var far = new BoardCamera(BoundsOf(Frontier));
        far.Zoom(4);
        float ratio = far.Pose.Distance / near.Pose.Distance;
        Assert.True(ratio > 1.5f);

        float nearBefore = near.Pose.FocusZ;
        float farBefore = far.Pose.FocusZ;
        near.Pan(0f, 1f, 0.05f);
        far.Pan(0f, 1f, 0.05f);

        float nearStep = nearBefore - near.Pose.FocusZ;
        float farStep = farBefore - far.Pose.FocusZ;
        Assert.True(nearStep > 0f);
        Assert.Equal(ratio, farStep / nearStep, 3);
    }

    [Fact]
    public void 意图超出正负一按一计_按键与贴边同时推不会加倍()
    {
        BoardCamera one = FrontierZoomedIn();
        BoardCamera two = FrontierZoomedIn();
        one.Pan(1f, 0f, 0.05f);
        two.Pan(2f, 0f, 0.05f);
        Assert.Equal(one.Pose, two.Pose);
    }

    [Fact]
    public void 小地图上等价于固定相机_最远缩放下任意平移意图位姿不变()
    {
        var camera = new BoardCamera(BoundsOf(V4));
        CameraPose initial = camera.Pose;

        foreach ((float right, float up) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f), (1f, 1f), (-1f, -1f) })
        {
            camera.Pan(right, up, 3f);
            Assert.Equal(initial, camera.Pose);
        }

        // 拉近之后才可平移
        camera.Zoom(200);
        camera.Pan(1f, 0f, 0.1f);
        Assert.True(camera.Pose.FocusX > initial.FocusX);
    }

    [Theory]
    [InlineData(800f, 450f, 0f, 0f)]
    [InlineData(1599f, 450f, 1f, 0f)]
    [InlineData(1f, 450f, -1f, 0f)]
    [InlineData(800f, 1f, 0f, 1f)]
    [InlineData(800f, 899f, 0f, -1f)]
    [InlineData(2f, 3f, -1f, 1f)]
    [InlineData(1598f, 897f, 1f, -1f)]
    [InlineData(-5f, 450f, 0f, 0f)]
    [InlineData(800f, 905f, 0f, 0f)]
    public void 贴边感应带_四边四角与窗外(float x, float y, float right, float up)
    {
        Assert.Equal((right, up), EdgePan.Intent(x, y, 1600f, 900f));
    }

    [Fact]
    public void 贴边感应带_刚离开感应带即停()
    {
        Assert.Equal((1f, 0f), EdgePan.Intent(1600f - EdgePan.BandPx, 450f, 1600f, 900f));
        Assert.Equal((0f, 0f), EdgePan.Intent(1600f - EdgePan.BandPx - 1f, 450f, 1600f, 900f));
    }
}
