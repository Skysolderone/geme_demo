using Siege.Presentation.Camera;
using static Siege.Core.Tests.ViewportCamera.CameraFixtures;

namespace Siege.Core.Tests.ViewportCamera;

/// <summary>
/// map-generator tasks 3.5：生成图上 <c>--pick-check</c> 暴露的两处<b>自检前提</b>问题（不是拾取几何的缺陷）。
/// ①「逐格居中」的论证（俯角 60° 下 h=2 高台向远处只投 0.40 格遮挡）只在格子真的被移到画面中心时成立；
/// 25×30 的图在最远缩放下注视点被夹在中间一段，贴近远边的格居中不了，射线俯角变浅，紧贴 h=2 平台身后的 h=0 格心被台面<b>真实遮住</b>。
/// ② 角位姿在随机图上可能整屏没有可落子格。这里用独立算式钉住 ① 的几何事实，并用源码扫描钉住自检的例外没有被写宽。
/// </summary>
public class 生成图上的拾取自检口径Tests
{
    private const float PlatformTop = 0.70f;   // h=2 台面高出 h=0 地砖的高度（与 CameraPose 注释里的论证同值）

    /// <summary>从相机看向地面点 (0, 0, z) 的射线俯角下，h=2 台面向远处投下的遮挡长度（格）。</summary>
    private static float ShadowBehindPlatform(CameraPose pose, float z)
    {
        System.Numerics.Vector3 eye = pose.Eye;
        float flat = MathF.Sqrt((eye.X * eye.X) + ((eye.Z - z) * (eye.Z - z)));
        return PlatformTop * flat / eye.Y;
    }

    [Fact]
    public void 最远缩放下贴近远边的格居中不了_台面遮挡超过半格_最近缩放下能居中且不遮挡()
    {
        // gen:987654321:p8 的 L28（第 28 行）与 gen:4:p8 的 M25（第 25 行）：紧贴其近侧的是 h=2 平台。
        foreach (int row in new[] { 28, 25 })
        {
            var camera = new BoardCamera(BoundsOf(25, 30));
            float z = -((row - 1) - 14.5f);

            camera.Set(new CameraPose(0f, z, camera.Farthest));
            Assert.Equal(28f, camera.Pose.Distance);
            Assert.True(camera.Pose.FocusZ - z > 4f, $"第 {row} 行在最远缩放下应被夹取到离格心 4 格以上，实际注视点 {camera.Pose.FocusZ}。");
            Assert.True(ShadowBehindPlatform(camera.Pose, z) > 0.5f, $"第 {row} 行：遮挡 {ShadowBehindPlatform(camera.Pose, z)} 格。");

            camera.Set(new CameraPose(0f, z, camera.Nearest));
            Assert.Equal(z, camera.Pose.FocusZ, 3);
            Assert.True(ShadowBehindPlatform(camera.Pose, z) < 0.5f);
        }

        // 对照：真正居中时任何缩放都是 0.40 格（只依赖俯角）。
        Assert.Equal(0.404f, ShadowBehindPlatform(new CameraPose(0f, 0f, 28f), 0.3f - (0.2f / MathF.Tan(60f * MathF.PI / 180f))), 2);
    }

    [Fact]
    public void 拾取自检的例外没有写宽()
    {
        // src/godot 不在解决方案里，只能做源码文本扫描。变异 MC-17：去掉 !centered 条件（居中的格也按遮挡放过）→ 本测试红；
        // 变异 MC-18：去掉"每格至少一次严格往返"→ 本测试红；变异 MC-19：画面内无格对所有位姿都放过 → 本测试红。
        string root = File.ReadAllText(Path.Combine(FrontierFixtures.RepoRoot(), "src", "godot", "scripts", "GameRoot.cs"));

        // 段 C 检查收紧：例外只给"纵深方向被夹取、落在注视点远侧"的格，且只认紧邻的更高更近的格。
        // 横向夹取不在例外内——否则"缩放带 5° 俯角变化"在 v4 上会被当成遮挡放过（实测 B5 → B4，变异 MC-22）。
        Assert.Contains("bool blocked = beyondFocus && adjacent && _board.LevelOf(picked) > cell.Height && Flat(_board.CenterOf(picked) - eye) < Flat(center - eye);", root, StringComparison.Ordinal);
        Assert.Contains("bool beyondFocus = at.FocusZ - z > 1e-3f;", root, StringComparison.Ordinal);
        Assert.Contains("bool adjacent = hit && picked.Y == cell.Coord.Y - 1 && System.Math.Abs(picked.X - cell.Coord.X) <= 1;", root, StringComparison.Ordinal);
        Assert.Contains("ok &= neverStrict.Length == 0;", root, StringComparison.Ordinal);
        Assert.Contains("ok &= failures.Count == 0 && (inView > 0 || !focusedOnCenter);", root, StringComparison.Ordinal);
        Assert.Contains("ok &= missed.Length == 0;", root, StringComparison.Ordinal);   // 角位姿放过"无可验"之后，覆盖面靠这一条兜住
    }
}
