using Siege.Core.Ai;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>终端对局（play 子命令）的冒烟测试：脚本输入能从插旗走到终局，不崩溃、不卡住。</summary>
public class 终端对局Tests
{
    [Fact]
    public void 脚本输入能落子并走到终局()
    {
        // 选 1 号区；第一回合选 1 号候选，在出生区落 A1，预演后确认；之后一直 Pass 到大回合上限。
        var script = new System.Text.StringBuilder("1\n1\nA1 B\nv\nok\n");
        for (int i = 0; i < 20; i++)
        {
            script.Append("\npass\n");
        }

        var output = new StringWriter();
        int exit = PlayCommand.Run(42, 4, 1, AiDifficulty.Easy, maxRounds: 3, new StringReader(script.ToString()), output);
        string text = output.ToString();

        Assert.Equal(0, exit);
        Assert.Contains("玩家1(你) 落子 A1B", text, StringComparison.Ordinal);
        Assert.Contains("对局结束：第 3 大回合", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 输入结束时干净退出()
    {
        var output = new StringWriter();
        int exit = PlayCommand.Run(7, 4, 2, AiDifficulty.Easy, maxRounds: 15, new StringReader("2\n"), output);

        Assert.Equal(0, exit);
        Assert.Contains("已退出。种子 7", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("或势力碾压（第 7 大回合起", output.ToString(), StringComparison.Ordinal);   // dominance-victory：开场说明提到碾压且用标准局起始值
    }
}
