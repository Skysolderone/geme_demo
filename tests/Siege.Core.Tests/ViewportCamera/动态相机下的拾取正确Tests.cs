using Siege.Presentation.Camera;
using static Siege.Core.Tests.ViewportCamera.CameraFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>
/// 规格：viewport-camera —— Requirement: 动态相机下的拾取正确（tasks 5.3 的视图模型部分：自检位姿表）。
/// 拾取本身在引擎侧，由 <c>--auto-demo --pick-check</c> 逐格验证；这里只钉"自检到底在哪 7 个位姿下跑"。
/// </summary>
public class 动态相机下的拾取正确Tests
{
    [Fact]
    public void 自检位姿共7个_中心_四角夹取位_最近_最远()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        IReadOnlyList<(string Name, CameraPose Pose)> poses = camera.CheckPoses();

        Assert.Equal(["中心", "左上角", "右上角", "左下角", "右下角", "最近", "最远"], poses.Select(p => p.Name));

        CameraPose Of(string name) => poses.Single(p => p.Name == name).Pose;
        Assert.Equal(camera.Nearest, Of("最近").Distance);
        Assert.Equal(camera.Farthest, Of("最远").Distance);
        Assert.InRange(Of("中心").Distance, camera.Nearest + 1f, camera.Farthest - 1f);
        Assert.Equal(0f, Of("中心").FocusX);
        Assert.Equal(0f, Of("中心").FocusZ);

        // 四角互不相同，且确实是夹取位置：落在该缩放下可行矩形的四个角上。
        camera.Zoom(200);
        PlaneRect feasible = camera.Feasible;
        Assert.Equal((feasible.MinX, feasible.MinZ), (Of("左上角").FocusX, Of("左上角").FocusZ));
        Assert.Equal((feasible.MaxX, feasible.MinZ), (Of("右上角").FocusX, Of("右上角").FocusZ));
        Assert.Equal((feasible.MinX, feasible.MaxZ), (Of("左下角").FocusX, Of("左下角").FocusZ));
        Assert.Equal((feasible.MaxX, feasible.MaxZ), (Of("右下角").FocusX, Of("右下角").FocusZ));
        Assert.Equal(7, poses.Select(p => p.Pose).Distinct().Count());
    }

    [Fact]
    public void 取自检位姿不改变相机当前状态()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(7);
        camera.Pan(1f, -1f, 0.3f);
        CameraPose before = camera.Pose;

        _ = camera.CheckPoses();

        Assert.Equal(before, camera.Pose);
    }

    [Fact]
    public void 一屏看全的地图上最远位姿就是旧固定相机()
    {
        var camera = new BoardCamera(BoundsOf(V4));
        CameraPose farthest = camera.CheckPoses().Single(p => p.Name == "最远").Pose;

        Assert.Equal(new CameraPose(0f, 0f, 14.6f * (13f + (2f * 1.7f)) / 12.7f), farthest);
    }

    [Fact]
    public void 全部自检位姿俯角相同()
    {
        foreach (MapDataPose item in new[] { V4, Frontier }.SelectMany(m => new BoardCamera(BoundsOf(m)).CheckPoses().Select(p => new MapDataPose(m.Id, p.Pose))))
        {
            Assert.Equal(60.0, PitchOf(item.Pose), 3);
        }
    }

    private sealed record MapDataPose(string Map, CameraPose Pose);
}
