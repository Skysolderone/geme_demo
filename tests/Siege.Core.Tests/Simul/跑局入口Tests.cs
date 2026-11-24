using Siege.Core.Tests.SimulationHarness;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 跑局入口（design J5，Sim 部分）。</summary>
[Collection(ConsoleRedirect.Collection)]   // 经 Program.Execute 重定向 Console：与其他重定向控制台的测试类串行（A3-S2 补，A2 漏了）
public sealed class 跑局入口Tests
{
    [Fact]
    public void simul子命令全AI跑指定回合并写日志()
    {
        string file = Path.Combine(Path.GetTempPath(), $"simul-cli-{Guid.NewGuid():N}.log");
        try
        {
            (int code, string output, string error) = LinkSimFixtures.RunMain(null, null,
                "simul", "--players", "2", "--map", "link:1:2", "--seed", "3", "--rounds=2", "--out", file);

            Assert.True(code == 0, error);
            string log = File.ReadAllText(file);
            Assert.Equal(output.ReplaceLineEndings("\n"), log.ReplaceLineEndings("\n"));
            Assert.Equal(2, CountOf(log, "== 第 "));
            Assert.Contains("结算顺位", log, StringComparison.Ordinal);
            Assert.Contains("围杀 孤立自绝", log, StringComparison.Ordinal);
            Assert.Contains("  势力 ", log, StringComparison.Ordinal);
            Assert.Contains("未终局：已跑满 --rounds 2", log, StringComparison.Ordinal);
            Assert.DoesNotContain("棋盘", log, StringComparison.Ordinal);
            Assert.DoesNotContain("棋子", log, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void 参数错误报错退出()
    {
        Assert.Equal(1, LinkSimFixtures.RunMain(null, null, "simul", "--players", "2", "--map", "link:1:2", "--round", "2").Code);
        Assert.Equal(1, LinkSimFixtures.RunMain(null, null, "simul", "--players", "2", "--map", "link:1:2", "--rounds", "-1").Code);
        Assert.Equal(1, LinkSimFixtures.RunMain(null, null, "simul", "--players", "3", "--map", "link:1:2").Code);
    }

    private static int CountOf(string text, string token)
    {
        int n = 0;
        for (int i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + token.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
