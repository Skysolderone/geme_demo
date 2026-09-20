using Siege.Presentation.Camera;
using static Siege.Core.Tests.ViewportCamera.CameraFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>规格：viewport-camera —— Requirement: 边界夹取（tasks 4.2）。</summary>
public class 边界夹取Tests
{
    [Fact]
    public void v4在最远缩放下两个方向都锁中线()
    {
        var camera = new BoardCamera(BoundsOf(V4));
        PlaneRect feasible = camera.Feasible;

        Assert.Equal(0f, feasible.MinX);
        Assert.Equal(0f, feasible.MaxX);
        Assert.Equal(0f, feasible.MinZ);
        Assert.Equal(0f, feasible.MaxZ);
        Assert.Equal(0f, camera.Pose.FocusX);
        Assert.Equal(0f, camera.Pose.FocusZ);
    }

    [Fact]
    public void 可行矩形等于外接矩形向内收缩所见范围之半()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(8);
        PlaneRect bounds = BoundsOf(Frontier);
        PlaneRect feasible = camera.Feasible;

        Assert.Equal(bounds.MinX + (camera.VisibleWidth * 0.5f), feasible.MinX, 3);
        Assert.Equal(bounds.MaxX - (camera.VisibleWidth * 0.5f), feasible.MaxX, 3);
        Assert.Equal(bounds.MinZ + (camera.VisibleDepth * 0.5f), feasible.MinZ, 3);
        Assert.Equal(bounds.MaxZ - (camera.VisibleDepth * 0.5f), feasible.MaxZ, 3);
        Assert.True(feasible.MinX < feasible.MaxX && feasible.MinZ < feasible.MaxZ);
    }

    [Fact]
    public void 推到尽头_持续向左推注视点停在可行矩形左缘()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(8);
        float left = camera.Feasible.MinX;
        float z = camera.Pose.FocusZ;

        for (int i = 0; i < 600; i++)
        {
            camera.Pan(-1f, 0f, 1f / 60f);
        }

        Assert.Equal(left, camera.Pose.FocusX);
        Assert.Equal(z, camera.Pose.FocusZ);

        // 画面中心仍落在地图上
        PlaneRect bounds = BoundsOf(Frontier);
        Assert.InRange(camera.Pose.FocusX, bounds.MinX, bounds.MaxX);
    }

    [Fact]
    public void 某方向所见不小于地图跨度时该方向锁中线_另一方向仍可推()
    {
        // 边疆图 25 × 30 竖长：最远缩放（上限 28）下横向所见 ≥ 地图宽，纵向不够。
        var camera = new BoardCamera(BoundsOf(Frontier));
        Assert.True(camera.VisibleWidth >= BoundsOf(Frontier).Width);
        Assert.True(camera.VisibleDepth < BoundsOf(Frontier).Depth);

        camera.Pan(1f, 1f, 0.5f);

        Assert.Equal(0f, camera.Pose.FocusX);
        Assert.True(camera.Pose.FocusZ < 0f);
    }

    [Fact]
    public void 拉远后重新夹取_在一角拉到最远注视点被拉回()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(200);
        for (int i = 0; i < 1200; i++)
        {
            camera.Pan(1f, 1f, 1f / 60f);
        }

        CameraPose corner = camera.Pose;
        Assert.Equal(camera.Feasible.MaxX, corner.FocusX);
        Assert.Equal(camera.Feasible.MinZ, corner.FocusZ);

        camera.Zoom(-400);

        PlaneRect feasible = camera.Feasible;
        Assert.Equal(camera.Farthest, camera.Pose.Distance);
        Assert.InRange(camera.Pose.FocusX, feasible.MinX, feasible.MaxX);
        Assert.InRange(camera.Pose.FocusZ, feasible.MinZ, feasible.MaxZ);
        Assert.True(camera.Pose.FocusX < corner.FocusX, "横向被拉回");
        Assert.True(camera.Pose.FocusZ > corner.FocusZ, "纵向被拉回");
    }

    [Fact]
    public void 直接给出界位姿也会被夹取()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Set(new CameraPose(999f, -999f, 0.01f));

        Assert.Equal(camera.Nearest, camera.Pose.Distance);
        Assert.Equal(camera.Feasible.MaxX, camera.Pose.FocusX);
        Assert.Equal(camera.Feasible.MinZ, camera.Pose.FocusZ);
    }
}
