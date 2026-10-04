using Siege.Presentation.Camera;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>
/// 规格：viewport-camera —— Requirement: 跟随对手行动（follow-opponent）。跟随状态机 <see cref="CameraFollow"/> 是纯逻辑，这里逐场景钉住；
/// "无人值守不移动"由图形版的开局对准自检钉（自动演示全程零时长演出，位姿一变即退出码 1）。
/// 算例：注视点 (0, 0)、距离 20，可见范围 30 × 16 → 中央区域半宽 30 × 0.6 / 2 = 9、半深 16 × 0.5 / 2 = 4。
/// 变异验证（follow-opponent 1.1）：见各测试注释里的 M 编号，记录在任务实现记录。
/// </summary>
public class 跟随对手行动Tests
{
    private static readonly CameraPose Mine = new(0f, 0f, 20f);
    private const float Width = 30f;
    private const float Depth = 16f;

    private static CameraPose Finish(CameraFollow follow) => follow.Advance(CameraFollow.TravelMs);

    [Fact]
    public void 画面外落子跟过去()
    {
        var follow = new CameraFollow();
        Assert.True(follow.Enabled);

        // 落点 (15, 0) 与 (17, 2)：在中央区域之外 → 移到外接矩形中心 (16, 1)，缩放不变。
        Assert.True(follow.Begin(Mine, Width, Depth, overview: false, [(15f, 0f), (17f, 2f)]));
        Assert.True(follow.IsTravelling);
        Assert.True(follow.HasReturnPose);

        // 移动途中（一半时间，缓入缓出的中点恰为路程一半）仍在移动——引擎侧据此不推进演出。
        CameraPose half = follow.Advance(CameraFollow.TravelMs / 2);
        Assert.True(follow.IsTravelling);
        Assert.Equal(8f, half.FocusX, 3);
        Assert.Equal(0.5f, half.FocusZ, 3);
        Assert.Equal(20f, half.Distance);

        CameraPose arrived = follow.Advance(CameraFollow.TravelMs - (CameraFollow.TravelMs / 2));
        Assert.False(follow.IsTravelling);
        Assert.Equal(new CameraPose(16f, 1f, 20f), arrived);
    }

    [Fact]
    public void 画面中央的落子不动()
    {
        var follow = new CameraFollow();

        // 恰在中央区域边上（半宽 9、半深 4）算在内；M2：把"横纵都在内"改成"横或纵在内"，下面"只有一个方向出界"的断言会红。
        Assert.False(follow.Begin(Mine, Width, Depth, overview: false, [(9f, 4f), (-9f, -4f)]));
        Assert.False(follow.IsTravelling);
        Assert.False(follow.HasReturnPose);

        // 只有纵向出界也要动。
        Assert.True(follow.Begin(Mine, Width, Depth, overview: false, [(0f, 4.5f)]));
    }

    [Fact]
    public void 轮到本机返回()
    {
        var follow = new CameraFollow();
        follow.Begin(Mine, Width, Depth, false, [(15f, 0f)]);
        CameraPose atFirst = Finish(follow);

        // 连续第二名对手：从第一名对手处再跟到 (−20, 6)；返回位姿仍是本机玩家最初的画面（M1：每次跟随都覆盖返回位姿 → 最后回到第一名对手处，红）。
        Assert.True(follow.Begin(atFirst, Width, Depth, false, [(-20f, 6f)]));
        CameraPose atSecond = Finish(follow);
        Assert.Equal(new CameraPose(-20f, 6f, 20f), atSecond);

        Assert.True(follow.OwnTurnStarted(atSecond));
        Assert.True(follow.IsTravelling);
        Assert.Equal(Mine, Finish(follow));
        Assert.False(follow.IsTravelling);

        // 只回一次。
        Assert.False(follow.HasReturnPose);
        Assert.False(follow.OwnTurnStarted(Mine));
    }

    [Fact]
    public void 手动操作后不再抢镜头()
    {
        var follow = new CameraFollow();
        follow.Begin(Mine, Width, Depth, false, [(15f, 0f)]);

        // 对手行动期间本机玩家平移了相机：停止移动、丢掉返回位姿、本轮不再跟随。
        follow.ManualInput(opponentActing: true);
        Assert.False(follow.IsTravelling);
        Assert.False(follow.HasReturnPose);
        Assert.True(follow.Suppressed);
        Assert.False(follow.Begin(new CameraPose(5f, 0f, 20f), Width, Depth, false, [(40f, 0f)]));

        // 轮到本机：不自动返回；解除之后下一轮对手照常跟随。
        Assert.False(follow.OwnTurnStarted(new CameraPose(5f, 0f, 20f)));
        Assert.False(follow.Suppressed);
        Assert.True(follow.Begin(new CameraPose(5f, 0f, 20f), Width, Depth, false, [(40f, 0f)]));
    }

    [Fact]
    public void 自己回合里动相机不影响之后的跟随()
    {
        // M3：ManualInput 不看 opponentActing 一律置"不再跟随" → 这里红。
        var follow = new CameraFollow();
        follow.ManualInput(opponentActing: false);
        Assert.False(follow.Suppressed);
        Assert.True(follow.Begin(Mine, Width, Depth, false, [(15f, 0f)]));
    }

    [Fact]
    public void 关闭后不跟随()
    {
        var follow = new CameraFollow();
        Assert.False(follow.Toggle());
        Assert.False(follow.Begin(Mine, Width, Depth, false, [(15f, 0f)]));
        Assert.False(follow.IsTravelling);

        Assert.True(follow.Toggle());
        Assert.True(follow.Begin(Mine, Width, Depth, false, [(15f, 0f)]));

        // 跟随途中关掉：停在原地，轮到本机也不返回。
        follow.Toggle();
        Assert.False(follow.IsTravelling);
        Assert.False(follow.OwnTurnStarted(new CameraPose(7f, 0f, 20f)));
    }

    [Fact]
    public void Pass不移动()
    {
        var follow = new CameraFollow();
        Assert.False(follow.Begin(Mine, Width, Depth, false, []));
        Assert.False(follow.HasReturnPose);
    }

    [Fact]
    public void 全局预览下不跟随()
    {
        var follow = new CameraFollow();
        Assert.False(follow.Begin(Mine, Width, Depth, overview: true, [(100f, 100f)]));
        Assert.False(follow.IsTravelling);
    }
}
