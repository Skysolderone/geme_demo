using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>终端对局（play 子命令）的冒烟测试：脚本输入能从插旗走到终局，不崩溃、不卡住。</summary>
public class 终端对局Tests
{
    [Fact]
    public void 脚本输入能落子并走到输入耗尽()
    {
        // 选 1 号区；第一回合选 1 号候选，在出生区落 B1（v3 的 A1 是角石），预演后确认；之后 Pass 一个小回合，输入耗尽退出。
        // 段 C：大回合上限删除，脚本不再能"Pass 到上限终局"（AI 不会停手），原"走到终局"改为走到第 3 大回合后输入耗尽干净退出。
        // life-shape 段 B：原脚本 Pass 两次、在第 4 大回合的提示处耗尽；活棋禁入 / 破坏活形生效后 AI 第 4 大回合走法变了（与改动前逐行比对：前 3 个大回合相同），
        // 玩家2 在轮到你之前提走了你唯一的 B1，你出局、对局自动跑到终局，永远走不到"输入耗尽"。改为只 Pass 一次、在第 3 大回合的提示处耗尽——
        // 保护期内别家不能进你的出生区，这一步不再依赖 AI 的走法；断言未改。
        var script = new System.Text.StringBuilder("1\n1\nB1 B\nv\nok\n");
        for (int i = 0; i < 1; i++)
        {
            script.Append("\npass\n");
        }

        var output = new StringWriter();
        int exit = PlayCommand.Run(42, 4, 1, AiDifficulty.Easy, new StringReader(script.ToString()), output, flagRisk: 0);   // flag-contest：脚本依赖出生区与走法，写死冒险概率 0
        string text = output.ToString();

        Assert.Equal(0, exit);
        Assert.Contains("玩家1(你) 落子 B1B", text, StringComparison.Ordinal);
        Assert.Contains("第 3 大回合", text, StringComparison.Ordinal);
        Assert.Contains("已退出。种子 42", text, StringComparison.Ordinal);

        // restore-go-core-rules 段 E（tasks 5.3）：势力栏显示"领地 + 棋串"，且两项之和等于总势力（测试内独立加和）；样本里确有非零领地分。
        // 变异验证 M-E16：BoardRenderer.Status 只打总势力（去掉领地 / 棋串拆分）→ 实跑红 1，只红 状态栏大数势力缩写不撑破一行——
        // 本测试这条正则也会命中每步播报（PlayCommand 同用 PowerText），所以状态栏本身由下一条测试钉住。
        System.Text.RegularExpressions.MatchCollection rows = System.Text.RegularExpressions.Regex.Matches(text, "势力 (\\d+)（领地 (\\d+) \\+ 棋串 (\\d+)）");
        Assert.True(rows.Count >= 8, $"只找到 {rows.Count} 条势力栏");
        Assert.All(rows, m => Assert.Equal(long.Parse(m.Groups[1].Value), long.Parse(m.Groups[2].Value) + long.Parse(m.Groups[3].Value)));
        Assert.Contains(rows, m => m.Groups[2].Value != "0");
    }

    [Fact]
    public void 状态栏大数势力缩写不撑破一行()
    {
        // tasks 5.3「大数显示不换行溢出」：120 枚倍增子的一条棋串军势 ⌊120 × 3^120 / 2^120⌋ 有 23 位；势力栏用 ≥ 10^6 的缩写（与图形版同一份 PowerNotation），
        // 整行不超过 100 列、不出现完整的 23 位数字。精确值由终局名次 / 预演给出，不在概览栏。
        MapData map = MatchFixtures.Map() with { Id = "test-match-12x12", Width = 12, Height = 12 };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), MatchOptions.Immediate);
        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 12; x++)
            {
                match.Board.Place(new Coord(x, y), MatchFixtures.P0, PieceType.Multiplier);
            }
        }

        match.Debug.Recalculate();
        Scoring.PlayerPower p0 = match.Scoreboard.Latest!.Of(MatchFixtures.P0);
        var output = new StringWriter();
        new BoardRenderer(output).Status(match.Publish(), MatchFixtures.P0);

        string line = output.ToString().Split('\n').Single(l => l.Contains("玩家1(你)", StringComparison.Ordinal) && l.Contains("势力 ", StringComparison.Ordinal));
        Assert.Contains($"势力 {Scoring.PowerNotation.Compact(p0.Total)}（领地 12 + 棋串 {Scoring.PowerNotation.Compact(p0.GroupScore)}）", line, StringComparison.Ordinal);
        Assert.DoesNotContain(p0.GroupScore.ToString(System.Globalization.CultureInfo.InvariantCulture), line, StringComparison.Ordinal);
        Assert.True(line.TrimEnd().Length <= 100, $"势力栏 {line.TrimEnd().Length} 列");
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
