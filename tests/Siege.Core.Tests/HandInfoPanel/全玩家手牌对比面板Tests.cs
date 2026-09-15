using System.Reflection;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Hand;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.HandInfoPanel;

/// <summary>规格：hand-info-panel —— Requirement: 全玩家手牌对比面板</summary>
public class 全玩家手牌对比面板Tests
{
    [Fact]
    public void 开关面板()
    {
        // 设计文档 §14.3：点击手牌信息按钮打开，再次点击或返回关闭（implement 4.1：开关状态不影响对局）。
        // 开关状态机结构上不引用任何 Core 类型，且开关前后对局指纹不变。
        // 变异验证 M-HP1：HandPanelState.Back 改为 `IsOpen = !IsOpen` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        string fingerprint = Fingerprint(match);
        var panel = new HandPanelState();
        Assert.False(panel.IsOpen);

        panel.ClickButton();
        Assert.True(panel.IsOpen);
        panel.ClickButton();
        Assert.False(panel.IsOpen);

        panel.ClickButton();
        _ = match.World(P0).HandPanel();
        panel.Back();
        Assert.False(panel.IsOpen);
        panel.Back();
        Assert.False(panel.IsOpen);

        Assert.Equal(fingerprint, Fingerprint(match));
        Assert.DoesNotContain(
            typeof(HandPanelState).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
            f => f.FieldType.Assembly == typeof(GameBoard).Assembly);
    }
}
