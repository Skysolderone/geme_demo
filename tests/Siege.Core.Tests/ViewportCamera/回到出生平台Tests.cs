using Siege.Core.Board;
using Siege.Presentation.Camera;
using static Siege.Core.Tests.ViewportCamera.CameraFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>规格：viewport-camera —— Requirement: 回到出生平台（tasks 4.4）。</summary>
public class 回到出生平台Tests
{
    /// <summary>边疆图 3 号平台（内部索引 2）：外接矩形 R22–X28，贴着地图右上角。</summary>
    private static IEnumerable<Coord> Zone3 => Frontier.BirthZones[2];

    /// <summary>边疆图 5 号平台（内部索引 4）：外接矩形 E13–J17，在地图中部偏左。</summary>
    private static IEnumerable<Coord> Zone5 => Frontier.BirthZones[4];

    [Fact]
    public void 出生平台中心是出生区格子的外接矩形中心()
    {
        // 样本自证：3 号平台的外接矩形确实是 R22–X28（列 R..X = 索引 16..22，行 22..28 = 索引 21..27）。
        Assert.Equal("R22", new Coord(Zone3.Min(c => c.X), Zone3.Min(c => c.Y)).ToNotation());
        Assert.Equal("X28", new Coord(Zone3.Max(c => c.X), Zone3.Max(c => c.Y)).ToNotation());

        (float x, float z) = CameraHome.Target(Zone3, CenterOf(Frontier), BoundsOf(Frontier));

        // 25 列：列中线索引 12；30 行：行中线索引 14.5。外接矩形中心 = 列 19、行 24 → x = +7，z = −9.5。
        Assert.Equal(7f, x);
        Assert.Equal(-9.5f, z);

        // 5 号平台 E13–J17（列索引 4..8、行索引 12..16）→ 列 6、行 14 → x = −6，z = +0.5。
        Assert.Equal("E13", new Coord(Zone5.Min(c => c.X), Zone5.Min(c => c.Y)).ToNotation());
        Assert.Equal("J17", new Coord(Zone5.Max(c => c.X), Zone5.Max(c => c.Y)).ToNotation());
        Assert.Equal((-6f, 0.5f), CameraHome.Target(Zone5, CenterOf(Frontier), BoundsOf(Frontier)));
    }

    [Fact]
    public void 不规则出生区取外接矩形中心而不是格子重心()
    {
        // L 形：A1 B1 C1 A2 A3 → 外接矩形 A1–C3，中心 B2；重心会偏向 A1。
        Coord[] cells = [new(0, 0), new(1, 0), new(2, 0), new(0, 1), new(0, 2)];
        (float x, float z) = CameraHome.Target(cells, c => (c.X, -c.Y), new PlaneRect(-9f, -9f, 9f, 9f));

        Assert.Equal(1f, x);
        Assert.Equal(-1f, z);
    }

    [Fact]
    public void 未选区时回家目标是地图中心()
    {
        (float x, float z) = CameraHome.Target([], CenterOf(Frontier), new PlaneRect(-10f, -20f, 30f, 40f));

        Assert.Equal(10f, x);
        Assert.Equal(10f, z);
    }

    [Fact]
    public void 空格回家_只改注视点不改距离()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(10);
        for (int i = 0; i < 600; i++)
        {
            camera.Pan(-1f, -1f, 1f / 60f);
        }

        float distance = camera.Pose.Distance;
        Assert.True(camera.Pose.FocusX < -6f && camera.Pose.FocusZ > 0.5f, "确实已推到地图另一端（左下）");
        (float x, float z) = CameraHome.Target(Zone5, CenterOf(Frontier), BoundsOf(Frontier));

        camera.Home(x, z);

        Assert.Equal(distance, camera.Pose.Distance);
        Assert.Equal(-6f, camera.Pose.FocusX);
        Assert.Equal(0.5f, camera.Pose.FocusZ);
    }

    [Fact]
    public void 回家结果同样经过夹取()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        (float x, float z) = CameraHome.Target(Zone3, CenterOf(Frontier), BoundsOf(Frontier));

        // 最远缩放：横向锁中线，纵向可行范围很窄
        camera.Home(x, z);

        Assert.Equal(0f, camera.Pose.FocusX);
        Assert.Equal(camera.Feasible.MinZ, camera.Pose.FocusZ);
        Assert.True(camera.Pose.FocusZ > z);
    }

    [Fact]
    public void 平台外接矩形取格心的最小最大_与行列方向无关()
    {
        // 行向 −Z：centerOf(最小行) 的 z 反而更大，矩形仍须 Min ≤ Max。
        PlaneRect zone3 = CameraHome.Platform(Zone3, CenterOf(Frontier))!.Value;
        Assert.Equal(new PlaneRect(4f, -12.5f, 10f, -6.5f), zone3);
        Assert.Equal((zone3.CenterX, zone3.CenterZ), CameraHome.Target(Zone3, CenterOf(Frontier), BoundsOf(Frontier)));

        Assert.Null(CameraHome.Platform([], CenterOf(Frontier)));
    }

    [Fact]
    public void 开局对准自家_3号平台整个可见且四周留约2格()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Open(CameraHome.Platform(Zone3, CenterOf(Frontier)));

        // 7×7 平台：纵向要显示 7 + 2 × 2 = 11 格 → 距离 = 14.6 × 11 ÷ 12.7 ≈ 12.65（16:9 下纵向是瓶颈）。
        Assert.Equal(14.6f * 11f / 12.7f, camera.Pose.Distance, 3);
        Assert.InRange(camera.Pose.Distance, camera.Nearest + 1f, camera.Farthest - 1f);
        Assert.Equal(11f, camera.VisibleDepth, 3);

        // 16:9 下横向所见 19.6 格，3 号平台（中心 x = 7）离右缘只有 7.2：注视点被夹到可行矩形右缘（偏离中心是允许的），纵向精确居中。
        PlaneRect feasible = camera.Feasible;
        Assert.Equal(feasible.MaxX, camera.Pose.FocusX);
        Assert.InRange(camera.Pose.FocusX, 4f, 7f);
        Assert.Equal(-9.5f, camera.Pose.FocusZ, 3);
        Assert.InRange(camera.Pose.FocusX, feasible.MinX, feasible.MaxX);
        Assert.InRange(camera.Pose.FocusZ, feasible.MinZ, feasible.MaxZ);

        // 真实透视投影下：7 行 7 列（含 h=2 台面高度）连同四周 2 格余量全部在画面内；余量不是越大越好——再多 2.5 格就出画面了。
        Assert.Equal(7, Zone3.Select(c => c.X).Distinct().Count());
        Assert.Equal(7, Zone3.Select(c => c.Y).Distinct().Count());
        Assert.True(ZoneInFrame(camera.Pose, 16f / 9f, Frontier, Zone3, margin: 2f));
        Assert.False(ZoneInFrame(camera.Pose, 16f / 9f, Frontier, Zone3, margin: 4.5f));
    }

    [Fact]
    public void 贴边的大平台整个可见_1号平台9行9列全部在画面内_注视点允许偏离平台中心()
    {
        IEnumerable<Coord> zone1 = Frontier.BirthZones[0];
        Assert.Equal("A21", new Coord(zone1.Min(c => c.X), zone1.Min(c => c.Y)).ToNotation());
        Assert.Equal("J29", new Coord(zone1.Max(c => c.X), zone1.Max(c => c.Y)).ToNotation());
        Assert.Equal(9, zone1.Select(c => c.X).Distinct().Count());
        Assert.Equal(9, zone1.Select(c => c.Y).Distinct().Count());

        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Open(CameraHome.Platform(zone1, CenterOf(Frontier)));

        // 9×9：纵向 9 + 4 = 13 格 → 14.6 × 13 ÷ 12.7 ≈ 14.94，远于最近限值（旧做法停在最近限值 8.05，纵向只看得到约 7 行）。
        Assert.Equal(14.6f * 13f / 12.7f, camera.Pose.Distance, 3);
        Assert.True(camera.VisibleDepth >= 9f + 4f - 1e-3f);

        // 贴左缘：注视点被夹到可行矩形左缘，偏离平台中心（x = −8）——允许；纵向不受夹取影响。
        Assert.Equal(camera.Feasible.MinX, camera.Pose.FocusX);
        Assert.True(camera.Pose.FocusX > -8f + 1f, $"注视点 x = {camera.Pose.FocusX}");
        Assert.Equal(-9.5f, camera.Pose.FocusZ, 3);

        // 线性近似下：平台（格边）整个落在所见范围内。
        Assert.True(camera.Pose.FocusX - (camera.VisibleWidth * 0.5f) <= -12.5f);
        Assert.True(camera.Pose.FocusZ - (camera.VisibleDepth * 0.5f) <= -14f && camera.Pose.FocusZ + (camera.VisibleDepth * 0.5f) >= -5f);

        // 真实透视投影下：全部格子整个在画面内。贴着地图边的一侧余量只到标注外圈（1.7 格），且透视下画面近处比线性近似略窄，
        // 所以靠边一侧的近角余量约 1 格——"约 2 格"只对不贴边的方向成立（纵向）。
        Assert.True(ZoneInFrame(camera.Pose, 16f / 9f, Frontier, zone1, margin: 1f));
    }

    [Theory]
    [InlineData(16f / 9f)]
    [InlineData(4f / 3f)]
    [InlineData(21f / 9f)]
    [InlineData(0.75f)]
    public void 开局对准自家_边疆图六个平台开局时都整个可见(float aspect)
    {
        Assert.Equal(6, Frontier.BirthZones.Length);
        for (int zone = 0; zone < Frontier.BirthZones.Length; zone++)
        {
            var camera = new BoardCamera(BoundsOf(Frontier), aspect);
            camera.Open(CameraHome.Platform(Frontier.BirthZones[zone], CenterOf(Frontier)));

            Assert.True(ZoneInFrame(camera.Pose, aspect, Frontier, Frontier.BirthZones[zone]), $"{zone + 1} 号平台开局没有整个落在画面内（宽高比 {aspect}）");
            Assert.InRange(camera.Pose.Distance, camera.Nearest, camera.Farthest);
            PlaneRect feasible = camera.Feasible;
            Assert.InRange(camera.Pose.FocusX, feasible.MinX, feasible.MaxX);
            Assert.InRange(camera.Pose.FocusZ, feasible.MinZ, feasible.MaxZ);
            if (aspect > 1f)
            {
                Assert.True(camera.Pose.Distance < camera.Farthest - 1f, $"{zone + 1} 号平台开局没有拉近");
            }
        }
    }

    [Fact]
    public void 开局对准自家_距离双向设定_已拉到最近的相机会被拉远到平台整个可见()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(200);
        Assert.Equal(camera.Nearest, camera.Pose.Distance);

        camera.Open(CameraHome.Platform(Zone3, CenterOf(Frontier)));

        Assert.Equal(14.6f * 11f / 12.7f, camera.Pose.Distance, 3);
    }

    [Fact]
    public void 开局对准自家_竖长窗口下横向成为瓶颈()
    {
        var camera = new BoardCamera(BoundsOf(Frontier), 0.75f);
        camera.Open(CameraHome.Platform(Zone3, CenterOf(Frontier)));

        // 横向要显示 11 格 → 纵向等效 11 ÷ 0.75。
        Assert.Equal(14.6f * (11f / 0.75f) / 12.7f, camera.Pose.Distance, 3);
    }

    [Fact]
    public void 小图开局不动相机_一屏看全的地图上锁定前后位姿相同()
    {
        var camera = new BoardCamera(BoundsOf(V4));
        CameraPose initial = camera.Pose;
        PlaneRect? platform = CameraHome.Platform(V4.BirthZones[0], CenterOf(V4));
        Assert.NotEqual(0f, platform!.Value.CenterX);

        camera.Open(platform);
        Assert.Equal(initial, camera.Pose);

        // 插旗阶段拉近、推过屏：锁定不动相机，玩家的画面原样保留（不跳回整盘视图）。
        camera.Zoom(5);
        camera.Pan(1f, 1f, 0.5f);
        CameraPose moved = camera.Pose;
        Assert.NotEqual(initial, moved);
        camera.Open(platform);
        Assert.Equal(moved, camera.Pose);
    }

    [Fact]
    public void 开局对准自家_未选区时等同回到地图中心_距离不变()
    {
        var camera = new BoardCamera(BoundsOf(Frontier));
        camera.Zoom(8);
        camera.Pan(1f, 1f, 0.5f);
        float distance = camera.Pose.Distance;

        camera.Open(null);

        Assert.Equal(new CameraPose(0f, 0f, distance), camera.Pose);
    }
}
