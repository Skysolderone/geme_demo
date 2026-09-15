using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 视觉方向基准</summary>
public class 视觉方向基准Tests
{
    private static string ReadMe() => File.ReadAllText(Path.Combine(RepoRoot(), "art", "style-exploration", "README.md"));

    [Fact]
    public void 基准图的适用范围()
    {
        // implement 5.1：风格基准文档引用基准图，明确其只用于材质、色彩、轮廓与信息层级，不代表最终地图布局与固定 UI 尺寸；
        // 并逐张列出目录中全部参考图（文件确实存在）。材质观感本身归阶段 B + 人工检查清单。
        // 变异验证 M-VS1：README 删去"不代表最终地图布局与固定 UI 尺寸"一句 → 本测试红 1。
        string readme = ReadMe();
        string dir = Path.Combine(RepoRoot(), "art", "style-exploration");

        Assert.Contains("siege-style-03-low-poly-fantasy.png", readme);
        Assert.Contains("只用于确定材质、色彩、轮廓与信息层级", readme);
        Assert.Contains("不代表最终地图布局与固定 UI 尺寸", readme);
        string[] images = [.. Directory.GetFiles(dir, "*.png").Select(Path.GetFileName).Order()!];
        Assert.Equal(9, images.Length);
        Assert.All(images, image => Assert.Contains($"`{image}`", readme));
    }

    [Fact]
    public void 基准图中的回合数不具规则含义()
    {
        // 设计文档 §20：画面中的 ROUND 7 仅表示当前大回合，不表示总回合数。
        // 变异验证 M-VS2：README 把"仅表示当前大回合"改为"表示总回合数" → 本测试红 1。
        string readme = ReadMe();

        Assert.Contains("`ROUND 7` 标识**仅表示当前大回合**", readme);
        Assert.Contains("不表示总回合数", readme);
    }
}
