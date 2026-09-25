using Siege.Core.Ai;
using Siege.Core.Board.Maps;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// frontier-map tasks 3.6 的自动化部分：终端版用脚本化输入在 <c>siege-frontier-v2</c> 上走到第 5 大回合
/// （真正的人工试玩留给负责人）。规格：simulation-harness「选边疆图」+ map-definition「出生区归属与共享」的保护期口径。
/// </summary>
public class 边疆图终端试玩脚本Tests
{
    [Fact]
    public void 脚本走到第五大回合保护期内只能落自家平台之后全图可落()
    {
        // 种子 42、座位 1、三名 Easy AI。人选 5 号台（中西 5×5）；AI 由种子选到 3、1、4 号台，2、6 号台是中立平台。
        // 每个小回合的输入：征募一行（第 2 大回合选两枚普通子补库存，其余回车不征募）→ 若干落点 → ok。
        //   第 1 大回合：R13（6 号台，中立平台）、N15（中央广场，过渡带）被拒 → E13（自家）成功；
        //   第 2 大回合：J8（4 号台，玩家 4 的平台）被拒 → E14；第 3 大回合：N15 仍被拒 → E15；
        //   第 4 大回合：同样的 N15、J8、R13 全部暂放成功并确认；随后进入第 5 大回合，输入耗尽退出。
        string script = string.Join('\n',
            "5",
            "", "R13", "N15", "E13", "ok",
            "1 6", "J8", "E14", "ok",
            "", "N15", "E15", "ok",
            "", "N15", "J8", "R13", "ok",
            "");
        var output = new StringWriter();

        // AI 权重与停手阈值写死为 ai-eye 4.5 定值之前的缺省：脚本里的征募编号与行动顺序依赖 AI 实际走法（默认值下 AI 一子不落，第 2 大回合起失步；段 D2 改写）。
        int exit = PlayCommand.Run(
            42, 4, 1, AiDifficulty.Easy, new StringReader(script), output, MapCatalog.Resolve(FrontierMapV2.Id),
            weights: SimFixtures.PreCalibrationWeights, passThreshold: SimFixtures.PreCalibrationPassThreshold, flagRisk: 0);

        string text = output.ToString();
        string[] lines = [.. text.Split('\n').Select(l => l.TrimEnd('\r'))];
        Assert.Equal(0, exit);
        Assert.Contains("地图 siege-frontier-v2", text, StringComparison.Ordinal);
        Assert.Contains("选择你的出生区（1–6）", text, StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("出生区锁定：玩家1(你)→5号区", StringComparison.Ordinal));

        // 保护期内（第 1–3 大回合）：他人平台、中立平台、过渡带一律被拒，共 4 次；自家平台每回合落 1 枚。
        Assert.Equal(2, Count(text, "落点不在当前合法落子范围内：N15。"));
        Assert.Equal(1, Count(text, "落点不在当前合法落子范围内：R13。"));
        Assert.Equal(1, Count(text, "落点不在当前合法落子范围内：J8。"));
        Assert.Equal(3, Count(text, "构筑保护期（第 1–3 大回合）：只能在自己的出生区落子"));
        Assert.Contains(lines, l => l.Contains("玩家1(你) 落子 E13B", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("玩家1(你) 落子 E14B", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("玩家1(你) 落子 E15B", StringComparison.Ordinal));

        // 第 4 大回合起：同一批落点（他人平台 J8、中立平台 R13、过渡带 N15）合法。拒绝提示只出现在第 4 大回合标题之前。
        int round4 = text.IndexOf("第 4 大回合", StringComparison.Ordinal);   // 段 C：大回合上限删除，标题不再带 "/15"
        Assert.True(round4 > 0);
        Assert.DoesNotContain("落点不在当前合法落子范围内", text[round4..], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("玩家1(你) 落子 J8B R13B N15B", StringComparison.Ordinal));
        Assert.Contains("第 5 大回合", text, StringComparison.Ordinal);
    }

    private static int Count(string text, string needle) => text.Split(needle).Length - 1;
}
