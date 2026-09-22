using Siege.Core.Ai;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>终端对局（play 子命令）的冒烟测试：脚本输入能从插旗走到终局，不崩溃、不卡住。</summary>
public class 终端对局Tests
{
    [Fact]
    public void 脚本输入能落子并走到输入耗尽()
    {
        // 选 1 号区；第一回合选 1 号候选，在出生区落 B1（v3 的 A1 是角石），预演后确认；之后 Pass 两个小回合，输入耗尽退出。
        // 段 C：大回合上限删除，脚本不再能"Pass 到上限终局"（AI 不会停手），原"走到终局"改为走到第 3 大回合后输入耗尽干净退出。
        var script = new System.Text.StringBuilder("1\n1\nB1 B\nv\nok\n");
        for (int i = 0; i < 2; i++)
        {
            script.Append("\npass\n");
        }

        var output = new StringWriter();
        int exit = PlayCommand.Run(42, 4, 1, AiDifficulty.Easy, new StringReader(script.ToString()), output);
        string text = output.ToString();

        Assert.Equal(0, exit);
        Assert.Contains("玩家1(你) 落子 B1B", text, StringComparison.Ordinal);
        Assert.Contains("第 3 大回合", text, StringComparison.Ordinal);
        Assert.Contains("已退出。种子 42", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 输入结束时干净退出()
    {
        var output = new StringWriter();
        int exit = PlayCommand.Run(7, 4, 2, AiDifficulty.Easy, new StringReader("2\n"), output);

        Assert.Equal(0, exit);
        Assert.Contains("已退出。种子 7", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("碾压", output.ToString(), StringComparison.Ordinal);   // 段 C：势力碾压已删除，开场说明不再提
        Assert.Contains("终局：只剩一名参赛玩家、棋盘填满或一整轮所有人都 Pass", output.ToString(), StringComparison.Ordinal);
    }
}
