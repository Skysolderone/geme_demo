using Siege.Presentation.Camera;

namespace Siege.Core.Tests.ViewportCamera;

public class 跟随对手移植Tests
{
    [Fact]
    public void 对手落点离开中央区域时跟过去并在本机回合返回()
    {
        var follow = new CameraFollow();
        var home = new CameraPose(0f, 0f, 12f);

        Assert.True(follow.Begin(home, 10f, 10f, overview: false, [(8f, 0f)]));
        Assert.True(follow.IsTravelling);
        Assert.Equal(home, follow.Advance(0));
        var away = follow.Advance(CameraFollow.TravelMs);
        Assert.Equal(8f, away.FocusX);
        Assert.True(follow.HasReturnPose);

        Assert.True(follow.OwnTurnStarted(away));
        Assert.Equal(home, follow.Advance(CameraFollow.TravelMs));
        Assert.False(follow.HasReturnPose);
    }

    [Fact]
    public void 手动移动在本轮对手行动中停止自动跟随()
    {
        var follow = new CameraFollow();
        var home = new CameraPose(0f, 0f, 12f);
        Assert.True(follow.Begin(home, 10f, 10f, overview: false, [(8f, 0f)]));

        follow.ManualInput(opponentActing: true);
        Assert.False(follow.IsTravelling);
        Assert.False(follow.HasReturnPose);
        Assert.True(follow.Suppressed);
        Assert.False(follow.Begin(home, 10f, 10f, overview: false, [(8f, 0f)]));

        Assert.False(follow.OwnTurnStarted(home));
        Assert.False(follow.Suppressed);
        Assert.True(follow.Begin(home, 10f, 10f, overview: false, [(8f, 0f)]));
    }
}
