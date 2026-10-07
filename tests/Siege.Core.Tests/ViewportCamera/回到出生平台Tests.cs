using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Presentation.Camera;
using static Siege.Core.Tests.ViewportCamera.CameraFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>规格：viewport-camera —— Requirement: 回到出生平台（tasks 4.4）。</summary>
public class 回到出生平台Tests
{
    /// <summary>大图 3 号平台（内部索引 2）：外接矩形 U24–AA30，贴着地图右上角（retire-legacy-maps 段 A2 起为自定合成图，原为边疆图 R22–X28）。</summary>
    private static IEnumerable<Coord> Zone3 => Large.BirthZones[2];

    /// <summary>大图 5 号平台（内部索引 4）：外接矩形 E14–J18，在地图中部偏左。</summary>
    private static IEnumerable<Coord> Zone5 => Large.BirthZones[4];

    [Fact]
    public void 出生平台中心是出生区格子的外接矩形中心()
    {
        // 样本自证：3 号平台的外接矩形确实是 U24–AA30（列 U..AA = 索引 19..25，行 24..30 = 索引 23..29）。
        Assert.Equal("U24", new Coord(Zone3.Min(c => c.X), Zone3.Min(c => c.Y)).ToNotation());
        Assert.Equal("AA30", new Coord(Zone3.Max(c => c.X), Zone3.Max(c => c.Y)).ToNotation());

        (float x, float z) = CameraHome.Target(Zone3, CenterOf(Large), BoundsOf(Large));

        // 27 列：列中线索引 13；32 行：行中线索引 15.5。外接矩形中心 = 列 22、行 26 → x = +9，z = −10.5。
        Assert.Equal(9f, x);
        Assert.Equal(-10.5f, z);

        // 5 号平台 E14–J18（列索引 4..8、行索引 13..17）→ 列 6、行 15 → x = −7，z = +0.5。
        Assert.Equal("E14", new Coord(Zone5.Min(c => c.X), Zone5.Min(c => c.Y)).ToNotation());
        Assert.Equal("J18", new Coord(Zone5.Max(c => c.X), Zone5.Max(c => c.Y)).ToNotation());
        Assert.Equal((-7f, 0.5f), CameraHome.Target(Zone5, CenterOf(Large), BoundsOf(Large)));
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
        (float x, float z) = CameraHome.Target([], CenterOf(Large), new PlaneRect(-10f, -20f, 30f, 40f));

        Assert.Equal(10f, x);
        Assert.Equal(10f, z);
    }

    [Fact]
    public void 空格回家_只改注视点不改距离()
    {
        var camera = new BoardCamera(BoundsOf(Large));
        camera.Zoom(10);
        for (int i = 0; i < 600; i++)
        {
            camera.Pan(-1f, -1f, 1f / 60f);
        }

        float distance = camera.Pose.Distance;
        Assert.True(camera.Pose.FocusX < -7f && camera.Pose.FocusZ > 0.5f, "确实已推到地图另一端（左下）");
        (float x, float z) = CameraHome.Target(Zone5, CenterOf(Large), BoundsOf(Large));

        camera.Home(x, z);

        Assert.Equal(distance, camera.Pose.Distance);
        Assert.Equal(-7f, camera.Pose.FocusX);
        Assert.Equal(0.5f, camera.Pose.FocusZ);
    }

    [Fact]
    public void 回家结果同样经过夹取()
    {
        var camera = new BoardCamera(BoundsOf(Large));
        (float x, float z) = CameraHome.Target(Zone3, CenterOf(Large), BoundsOf(Large));

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
        PlaneRect zone3 = CameraHome.Platform(Zone3, CenterOf(Large))!.Value;
        Assert.Equal(new PlaneRect(6f, -13.5f, 12f, -7.5f), zone3);   // 格心：列 19..25 → x 6..12，行 23..29 → z −7.5..−13.5
        Assert.Equal((zone3.CenterX, zone3.CenterZ), CameraHome.Target(Zone3, CenterOf(Large), BoundsOf(Large)));

        Assert.Null(CameraHome.Platform([], CenterOf(Large)));
    }

    [Fact]
    public void 开局对准自家_3号平台整个可见且四周留约2格()
    {
        var camera = new BoardCamera(BoundsOf(Large));
        camera.Open(CameraHome.Platform(Zone3, CenterOf(Large)));

        // 7×7 平台：纵向要显示 7 + 2 × 2 = 11 格 → 距离 = 14.6 × 11 ÷ 12.7 ≈ 12.65（16:9 下纵向是瓶颈）。
        Assert.Equal(14.6f * 11f / 12.7f, camera.Pose.Distance, 3);
        Assert.InRange(camera.Pose.Distance, camera.Nearest + 1f, camera.Farthest - 1f);
        Assert.Equal(11f, camera.VisibleDepth, 3);

        // 16:9 下横向所见 19.6 格，3 号平台（中心 x = 9）离右缘（13.5 + 1.7 = 15.2）只有 6.2：注视点被夹到可行矩形右缘（偏离中心是允许的），纵向精确居中。
        PlaneRect feasible = camera.Feasible;
        Assert.Equal(feasible.MaxX, camera.Pose.FocusX);
        Assert.InRange(camera.Pose.FocusX, 4f, 9f);
        Assert.True(camera.Pose.FocusX < 9f, "确实被夹取（偏离平台中心）");
        Assert.Equal(-10.5f, camera.Pose.FocusZ, 3);
        Assert.InRange(camera.Pose.FocusX, feasible.MinX, feasible.MaxX);
        Assert.InRange(camera.Pose.FocusZ, feasible.MinZ, feasible.MaxZ);

        // 真实透视投影下：7 行 7 列（含 h=2 台面高度）连同四周 2 格余量全部在画面内；余量不是越大越好——再多 2.5 格就出画面了。
        Assert.Equal(7, Zone3.Select(c => c.X).Distinct().Count());
        Assert.Equal(7, Zone3.Select(c => c.Y).Distinct().Count());
        Assert.True(ZoneInFrame(camera.Pose, 16f / 9f, Large, Zone3, margin: 2f));
        Assert.False(ZoneInFrame(camera.Pose, 16f / 9f, Large, Zone3, margin: 4.5f));
    }

    [Fact]
    public void 贴边的大平台整个可见_1号平台9行9列全部在画面内_注视点允许偏离平台中心()
    {
        IEnumerable<Coord> zone1 = Large.BirthZones[0];
        Assert.Equal("A23", new Coord(zone1.Min(c => c.X), zone1.Min(c => c.Y)).ToNotation());
        Assert.Equal("J31", new Coord(zone1.Max(c => c.X), zone1.Max(c => c.Y)).ToNotation());
        Assert.Equal(9, zone1.Select(c => c.X).Distinct().Count());
        Assert.Equal(9, zone1.Select(c => c.Y).Distinct().Count());

        var camera = new BoardCamera(BoundsOf(Large));
        camera.Open(CameraHome.Platform(zone1, CenterOf(Large)));

        // 9×9：纵向 9 + 4 = 13 格 → 14.6 × 13 ÷ 12.7 ≈ 14.94，远于最近限值（旧做法停在最近限值 8.05，纵向只看得到约 7 行）。
        Assert.Equal(14.6f * 13f / 12.7f, camera.Pose.Distance, 3);
        Assert.True(camera.VisibleDepth >= 9f + 4f - 1e-3f);

        // 贴左缘：注视点被夹到可行矩形左缘，偏离平台中心（x = 4 − 13 = −9）——允许；纵向不受夹取影响（中心 z = −(26 − 15.5) = −10.5）。
        Assert.Equal(camera.Feasible.MinX, camera.Pose.FocusX);
        Assert.True(camera.Pose.FocusX > -9f + 1f, $"注视点 x = {camera.Pose.FocusX}");
        Assert.Equal(-10.5f, camera.Pose.FocusZ, 3);

        // 线性近似下：平台（格边）整个落在所见范围内：x 从 −13.5，z 从 −15 到 −6。
        Assert.True(camera.Pose.FocusX - (camera.VisibleWidth * 0.5f) <= -13.5f);
        Assert.True(camera.Pose.FocusZ - (camera.VisibleDepth * 0.5f) <= -15f && camera.Pose.FocusZ + (camera.VisibleDepth * 0.5f) >= -6f);

        // 真实透视投影下：全部格子整个在画面内。贴着地图边的一侧余量只到标注外圈（1.7 格），且透视下画面近处比线性近似略窄，
        // 所以靠边一侧的近角余量约 1 格——"约 2 格"只对不贴边的方向成立（纵向）。
        Assert.True(ZoneInFrame(camera.Pose, 16f / 9f, Large, zone1, margin: 1f));
    }

    [Theory]
    [InlineData(16f / 9f)]
    [InlineData(4f / 3f)]
    [InlineData(21f / 9f)]
    [InlineData(0.75f)]
    public void 开局对准自家_边疆图六个平台开局时都整个可见(float aspect)   // 方法名沿用规格 Scenario 名；retire-legacy-maps 段 A2 起为自定 6 平台大图
    {
        Assert.Equal(6, Large.BirthZones.Length);
        for (int zone = 0; zone < Large.BirthZones.Length; zone++)
        {
            var camera = new BoardCamera(BoundsOf(Large), aspect);
            camera.Open(CameraHome.Platform(Large.BirthZones[zone], CenterOf(Large)));

            Assert.True(ZoneInFrame(camera.Pose, aspect, Large, Large.BirthZones[zone]), $"{zone + 1} 号平台开局没有整个落在画面内（宽高比 {aspect}）");
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
        var camera = new BoardCamera(BoundsOf(Large));
        camera.Zoom(200);
        Assert.Equal(camera.Nearest, camera.Pose.Distance);

        camera.Open(CameraHome.Platform(Zone3, CenterOf(Large)));

        Assert.Equal(14.6f * 11f / 12.7f, camera.Pose.Distance, 3);
    }

    [Fact]
    public void 开局对准自家_竖长窗口下横向成为瓶颈()
    {
        var camera = new BoardCamera(BoundsOf(Large), 0.75f);
        camera.Open(CameraHome.Platform(Zone3, CenterOf(Large)));

        // 横向要显示 11 格 → 纵向等效 11 ÷ 0.75。
        Assert.Equal(14.6f * (11f / 0.75f) / 12.7f, camera.Pose.Distance, 3);
    }

    [Fact]
    public void 小图开局不动相机_一屏看全的地图上锁定前后位姿相同()
    {
        var camera = new BoardCamera(BoundsOf(Small));
        CameraPose initial = camera.Pose;
        PlaneRect? platform = CameraHome.Platform(Small.BirthZones[0], CenterOf(Small));
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
        var camera = new BoardCamera(BoundsOf(Large));
        camera.Zoom(8);
        camera.Pan(1f, 1f, 0.5f);
        float distance = camera.Pose.Distance;

        camera.Open(null);

        Assert.Equal(new CameraPose(0f, 0f, distance), camera.Pose);
    }

    [Fact]
    public void 轻震不改相机状态()
    {
        // tiered-number-show（design.md D8）：轻震只是画面的临时偏移，MUST NOT 改变相机的注视点与缩放距离，结束后画面回到轻震前的位置。
        // 相机状态（注视点、距离）只在视图模型 BoardCamera 里，它没有任何轻震入口；轻震全在引擎层的 BoardView——
        // src/godot 不在 siege.sln 里，IL 守门与行为测试都够不着，用源码文本扫描钉住接线（配样本下界与反面命中）：
        // ① 偏移只存在 _shake 一个字段里，只有 SetShake 给它赋值；② 写相机节点只有 WriteCamera 一处，偏移只在那里叠加（眼位与注视点各一次）；
        // ③ 轻震的三个方法（SetShake / ShakeOffsetOf / WriteCamera）都不碰视图模型（Rig），也不改"已写入的位姿"。
        //
        // 变异验证（tiered-number-show 段 B 检查，脚本：二进制读写、锚点恰命中 1 次、finally 还原并逐字节比对、刷新 mtime；加这条之前下面两条都是 0 红）：
        // MC-1「偏移写回视图模型」——BoardView.SetShake 的 _shake = offset; 之后加一行 Rig.Set(new CameraPose(Rig.Pose.FocusX + offset.X, …)) → 红 1（本条）。
        // MC-5「WriteCamera 之外再写一次相机节点」——同处加一行 Camera.Position += offset; → 红 1（本条）。
        string view = PresentationFixtures.GodotScriptCode("BoardView.cs");
        Assert.True(view.Length >= 40_000, $"只读到 {view.Length} 字符");

        string setShake = PresentationFixtures.MethodBody(view, "public void SetShake(int? permille)");
        string offsetOf = PresentationFixtures.MethodBody(view, "public static Vector3 ShakeOffsetOf(int permille)");
        string write = PresentationFixtures.MethodBody(view, "private void WriteCamera(CameraPose pose)");
        string apply = PresentationFixtures.MethodBody(view, "public bool ApplyCameraPose()");

        Regex assignsShake = new(@"\b_shake\s*[-+*/]?=(?!=)");
        Assert.Equal(2, assignsShake.Matches(view).Count);   // 字段初值 + SetShake 里的一次
        Assert.Single(assignsShake.Matches(setShake));

        Regex writesCameraNode = new(@"\bCamera\s*\.\s*(?:(?:Global)?(?:Position|Transform|Rotation\w*|Basis)\s*[-+*/]?=(?!=)|(?:LookAt\w*|Translate\w*|Rotate\w*|Set\w+)\s*\()");
        Assert.Equal(2, writesCameraNode.Matches(view).Count);
        Assert.Equal(2, writesCameraNode.Matches(write).Count);
        Assert.Equal(2, Regex.Matches(write, @"\+\s*_shake\b").Count);
        Assert.Equal(2, Regex.Matches(view, @"\+\s*_shake\b").Count);

        // 主场景只把遮罩给的进度交给 SetShake，不自己算偏移、不直接写相机节点。
        foreach (string script in new[] { "GameRoot.cs", "GameRoot.Reveal.cs" })
        {
            string root = PresentationFixtures.GodotScriptCode(script);
            Assert.DoesNotMatch(writesCameraNode, root);
            Assert.DoesNotMatch(@"ShakeOffsetOf\s*\(", root);
        }

        Regex touchesViewModel = new(@"\bRig\b|\b_appliedPose\s*=(?!=)");
        Assert.DoesNotMatch(touchesViewModel, setShake);
        Assert.DoesNotMatch(touchesViewModel, offsetOf);
        Assert.DoesNotMatch(touchesViewModel, write);

        // 反面命中：同一组判据在"确实读写视图模型 / 相机节点"的写法上扫得到。
        Assert.Matches(touchesViewModel, apply);
        Assert.Matches(touchesViewModel, "Rig.Set(new CameraPose(Rig.Pose.FocusX + offset.X, Rig.Pose.FocusZ + offset.Z, Rig.Pose.Distance));");
        Assert.Matches(writesCameraNode, "Camera.Position += offset;");
        Assert.Matches(writesCameraNode, "Camera.GlobalPosition = eye;");
        Assert.Matches(writesCameraNode, "Camera.Translate(offset);");
        Assert.Matches(assignsShake, "_shake += drift;");
    }
}
