using Siege.Presentation.Camera;
using static Siege.Core.Tests.ViewportCamera.CameraFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>规格：viewport-camera —— Requirement: 缩放（tasks 4.1 / 4.3）。</summary>
public class 缩放Tests
{
    [Fact]
    public void 缩放夹取_持续拉近停在最近限值_持续拉远停在最远限值()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));

        camera.Zoom(200);
        Assert.Equal(camera.Nearest, camera.Pose.Distance);
        camera.Zoom(1);
        Assert.Equal(camera.Nearest, camera.Pose.Distance);

        camera.Zoom(-400);
        Assert.Equal(camera.Farthest, camera.Pose.Distance);
        Assert.True(camera.Nearest < camera.Farthest);
    }

    [Fact]
    public void 缩放不改俯角_任意距离下反推的俯角都是60度()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        var seen = new HashSet<float>();
        for (int i = 0; i < 14; i++)
        {
            seen.Add(camera.Pose.Distance);
            Assert.Equal(60.0, PitchOf(camera.Pose), 3);
            Assert.Equal(camera.Pose.Distance, (camera.Pose.Eye - camera.Pose.Target).Length(), 3);
            camera.Zoom(1);
        }

        Assert.True(seen.Count >= 10, "样本下界：确实走过了多个不同的缩放距离");
    }

    [Fact]
    public void 缩放不改注视点()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(12);
        camera.Pan(0f, 1f, 0.2f);
        CameraPose before = camera.Pose;

        camera.Zoom(1);

        Assert.Equal(before.FocusX, camera.Pose.FocusX);
        Assert.Equal(before.FocusZ, camera.Pose.FocusZ);
        Assert.True(camera.Pose.Distance <= before.Distance);
    }

    [Fact]
    public void 最远限值取一屏看全所需距离与上限中的较小者()
    {
        var v4 = new BoardCamera(BoundsOf(V4));
        Assert.True(v4.FitsOneScreen);
        Assert.Equal(v4.FullViewDistance, v4.Farthest);
        Assert.True(v4.Farthest < BoardCamera.FarthestCap);

        var frontier = new BoardCamera(BoundsOf(Frontier));
        Assert.False(frontier.FitsOneScreen);
        Assert.True(frontier.FullViewDistance > BoardCamera.FarthestCap);
        Assert.Equal(BoardCamera.FarthestCap, frontier.Farthest);
    }

    [Fact]
    public void 最近限值下画面完整显示一个5x5平台()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(200);

        Assert.True(camera.VisibleDepth >= 5f + 1f, $"纵向所见 {camera.VisibleDepth}");
        Assert.True(camera.VisibleWidth >= 5f + 1f, $"横向所见 {camera.VisibleWidth}");
        Assert.True(camera.VisibleDepth < 9f, "最近限值不应宽到失去拉近的意义");
    }

    [Fact]
    public void v4的初始位姿与旧固定相机逐位相等()
    {
        // 旧 BoardView 固定相机的算式，原样抄在测试里（独立算式，不调用视图模型的任何常量）：
        //   span = Max(w, h) + 2 × FarLabelMargin；distance = 14.6 × span ÷ 12.7；target = (0, 0.2, 0.3)；
        //   position = target + (0, distance × sin 60°, distance × cos 60°)
        float span = Math.Max(V4.Width, V4.Height) + (2f * 1.7f);
        float distance = 14.6f * span / 12.7f;
        float radians = 60f * 0.0174532924f;
        var target = new System.Numerics.Vector3(0f, 0.2f, 0.3f);
        System.Numerics.Vector3 position = target + new System.Numerics.Vector3(0f, distance * MathF.Sin(radians), distance * MathF.Cos(radians));

        var camera = new BoardCamera(BoundsOf(V4));

        Assert.Equal(distance, camera.Pose.Distance);
        Assert.Equal(distance, camera.Farthest);
        Assert.Equal(target, camera.Pose.Target);
        Assert.Equal(position, camera.Pose.Eye);
        Assert.Equal(54f, CameraPose.FovDegrees);
    }

    [Theory]
    [InlineData(4f / 3f)]
    [InlineData(16f / 9f)]
    [InlineData(21f / 9f)]
    public void v4的最远距离不随横向窗口宽高比变化(float aspect)
    {
        float expected = 14.6f * (13f + (2f * 1.7f)) / 12.7f;
        Assert.Equal(expected, new BoardCamera(BoundsOf(V4), aspect).Farthest);
    }

    [Fact]
    public void 停在最远缩放时窗口变形后仍停在最远缩放()
    {
        var camera = new BoardCamera(BoundsOf(V4), 16f / 9f);
        camera.SetAspect(0.5f);

        Assert.Equal(camera.Farthest, camera.Pose.Distance);
        Assert.True(camera.Farthest > 14.6f * 16.4f / 12.7f, "竖长窗口下横向成了瓶颈，要退得更远才看得全");
    }
}
