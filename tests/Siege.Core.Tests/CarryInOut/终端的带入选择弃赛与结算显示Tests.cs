using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Sim;
using Siege.Sim.Cli;
using Siege.Sim.Play;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 终端的带入选择、弃赛与结算显示</summary>
/// <remarks>
/// 脚本化输入 + <see cref="TempProfile"/> 临时档案（tasks 2.4）。依赖走法的脚本写死冒险概率 0 与内容集 v1（与既有终端脚本测试同口径）。
/// 本机玩家在脚本里从不落子（回车不征募 + pass）：从未落子者连续 Pass 不出局，保护期内别家也进不了本机出生区，脚本不依赖 AI 的具体走法。
/// </remarks>
public class 终端的带入选择弃赛与结算显示Tests
{
    private static (int Exit, string Text) Play(string script, CarryProfileStore? profile, ulong seed = 42)
    {
        var output = new StringWriter();
        int exit = PlayCommand.Run(seed, 4, 1, AiDifficulty.Easy, new StringReader(script), output, flagRisk: 0, contentSet: ContentSet.V1, profile: profile);
        return (exit, output.ToString());
    }

    [Fact]
    public void 开局选择()
    {
        // 规格 Scenario：档案有 17 点、换型令 2 件，玩家输入兑换备用子、再选择带入换型令并指定连珠子
        // → 补给点变为 14，换型令库存变为 1，开局后打印"你：换型令 → 连珠子；玩家2：……"。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 17, spare: 0, draft: 0, commission: 2));

        // buy 1 = 兑换备用子；3 = 带入换型令；2 = v1 候选的第 2 项连珠子；1 = 1 号出生区；随后输入耗尽退出。
        (int exit, string text) = Play("buy 1\n3\n2\n1\n", temp.Store());

        Assert.Equal(0, exit);
        Assert.Contains("补给点 17", text, StringComparison.Ordinal);
        Assert.Contains("补给点 14", text, StringComparison.Ordinal);
        Assert.Contains("你：换型令 → 连珠子", text, StringComparison.Ordinal);
        Assert.Matches(new Regex("你：换型令 → 连珠子；玩家2：(备用子|征召签 → \\S+子|换型令 → \\S+子)；玩家3：(备用子|征召签 → \\S+子|换型令 → \\S+子)；玩家4：(备用子|征召签 → \\S+子|换型令 → \\S+子)"), text);
        Assert.True(text.IndexOf("你：换型令 → 连珠子", StringComparison.Ordinal) < text.IndexOf("选择你的出生区", StringComparison.Ordinal), "带入应在插旗前公开");

        CarryProfile after = CarryProfile.Parse(temp.Read());
        Assert.Equal((14, 1, 1), (after.Points, after.StockOf(SupplyKind.SpareStone), after.StockOf(SupplyKind.Commission)));
        Assert.Equal(new CarryIn(SupplyKind.Commission, PieceType.Line), after.InFlight!.Carry);
        Assert.False(string.IsNullOrEmpty(after.InFlight.MatchId));
    }

    [Fact]
    public void 库存为0的补给不在带入选项里()
    {
        // 规格「开局带入与在途记录」Scenario「库存为 0 不能带」的界面一侧：备用子库存为 0 时输入 1 被拒，仍停在补给选择。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 0, spare: 0, draft: 1, commission: 0));

        (_, string text) = Play("1\n\n", temp.Store());

        Assert.Contains("没有备用子库存", text, StringComparison.Ordinal);
        Assert.Contains("带入：全员不带入", text, StringComparison.Ordinal);
        Assert.Equal(1, CarryProfile.Parse(temp.Read()).StockOf(SupplyKind.DraftLot));
    }

    [Fact]
    public void 弃赛命令()
    {
        // 规格 Scenario：第 6 大回合轮到本机玩家时输入 resign 并确认 → 终端打印"弃赛：弃赛名次第 r，带出 p，换型令已返还"，此后 AI 继续直至终局。
        // 名次与点数由测试独立核对：p = 点数表（规格：4 人 24 / 16 / 12 / 10）第 r 名的一半向下取整；第 6 大回合已过保护期。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 0, spare: 0, draft: 0, commission: 1));
        var script = new StringBuilder("3\n1\n1\n");   // 带入换型令、指定 v1 候选第 1 项堡垒子；1 号出生区
        for (int round = 1; round <= 5; round++)
        {
            script.Append("\npass\n");                 // 回车不征募，pass
        }

        script.Append("resign\ny\n");

        (int exit, string text) = Play(script.ToString(), temp.Store());

        Assert.Equal(0, exit);
        Assert.Contains("你：换型令 → 堡垒子", text, StringComparison.Ordinal);
        System.Text.RegularExpressions.Match line = Regex.Match(text, "弃赛：弃赛名次第 (\\d)，带出 (\\d+)，换型令已返还");
        Assert.True(line.Success, "应打印弃赛结算");
        int rank = int.Parse(line.Groups[1].Value);
        int points = int.Parse(line.Groups[2].Value);
        int[] table = [24, 16, 12, 10];
        Assert.Equal(table[rank - 1] / 2, points);

        // 弃赛发生在第 6 大回合：第 5 大回合已结束、第 6 大回合尚未结束。
        int end5 = text.IndexOf("── 第 5 大回合结束 ──", StringComparison.Ordinal);
        Assert.True(end5 >= 0 && end5 < line.Index, "弃赛应在第 5 大回合之后");
        int end6 = text.IndexOf("── 第 6 大回合结束 ──", StringComparison.Ordinal);
        Assert.True(end6 < 0 || end6 > line.Index, "弃赛应在第 6 大回合之内");

        // 此后 AI 继续直至终局，终局照常打印名次与本机结算；结算只写一次。
        Assert.True(text.IndexOf("对局结束", StringComparison.Ordinal) > line.Index, "弃赛后 AI 应继续直至终局");
        Assert.Contains($"结算：弃赛 · 弃赛名次第 {rank} 名 · 带出 {points} · 换型令已返还", text, StringComparison.Ordinal);
        CarryProfile after = CarryProfile.Parse(temp.Read());
        Assert.Equal((points, 1, (CarryInFlight?)null), (after.Points, after.StockOf(SupplyKind.Commission), after.InFlight));
    }

    [Fact]
    public void 弃赛需二次确认()
    {
        // 规格正文：resign 二次确认后执行。回答 n 即取消，不弃赛、不结算，继续读命令。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 0, spare: 1, draft: 0, commission: 0));

        (int exit, string text) = Play("1\n1\nresign\nn\n", temp.Store());

        Assert.Equal(0, exit);
        Assert.Contains("确认弃赛？", text, StringComparison.Ordinal);
        Assert.DoesNotContain("弃赛：弃赛名次", text, StringComparison.Ordinal);
        Assert.DoesNotContain("弃赛（弃赛时势力", text, StringComparison.Ordinal);
        Assert.Contains("已退出。种子 42", text, StringComparison.Ordinal);
        Assert.Equal(CarryFixtures.Spare, CarryProfile.Parse(temp.Read()).InFlight!.Carry);
    }

    [Fact]
    public void 新版本档案时本局关闭带入()
    {
        // 规格「档案缺失、损坏与版本不符的恢复」Scenario「新版本档案不覆盖」的终端一侧：提示档案来自更新的版本，
        // 本局不提供带入、不结算（入口只在带入带出开启时结算，implement 段 A 待决 3），档案文件逐字节不变。
        using var temp = new TempProfile();
        string newer = "{\"version\":2,\"points\":40,\"inventory\":{},\"inFlight\":null}";
        temp.Write(newer);

        (int exit, string text) = Play("1\n", temp.Store());

        Assert.Equal(0, exit);
        Assert.Contains("更新的版本", text, StringComparison.Ordinal);
        Assert.DoesNotContain("补给点", text, StringComparison.Ordinal);
        Assert.DoesNotContain("带入：", text, StringComparison.Ordinal);
        Assert.Contains("选择你的出生区", text, StringComparison.Ordinal);
        Assert.Equal(newer, temp.Read());
        Assert.Equal(["profile.json"], temp.Files());
    }

    [Fact]
    public void 关闭带入()
    {
        // 规格 Scenario：以 --no-carry 开局 → 不显示档案与补给选择，档案文件不被读写，局终不打印带出结算。
        string before = RealProfileDir.State();
        Assert.Null(Program.PlayProfile(new CommandLine(["--no-carry"])));

        (int exit, string text) = Play("1\n", profile: null);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("补给点", text, StringComparison.Ordinal);
        Assert.DoesNotContain("带入：", text, StringComparison.Ordinal);
        Assert.DoesNotContain("结算：", text, StringComparison.Ordinal);
        Assert.Equal(before, RealProfileDir.State());
    }

    [Theory]
    [InlineData("--no-carry", "5")]              // 开关不带值：CommandLine 会把后面的 token 吞成值，必须报错而不是静默当成未给
    [InlineData("--profile", null)]              // --profile 缺路径
    public void 档案选项严格解析(string option, string? value)
    {
        string[] args = value is null ? [option] : [option, value];
        Assert.ThrowsAny<ArgumentException>(() => Program.PlayProfile(new CommandLine(args)));
    }

    [Fact]
    public void 关闭与指定路径不能同时给出()
    {
        Assert.ThrowsAny<ArgumentException>(() => Program.PlayProfile(new CommandLine(["--no-carry", "--profile", "x.json"])));
    }

    [Fact]
    public void 退出提示与下次开局结算中途退出()
    {
        // 规格「中途退出与截断」：q 在本机玩家有带入时先提示"退出将丢失带入的补给；弃赛可返还补给并带出 50%"，由玩家确认；
        // 下次开局前把残留在途记录按中途退出结算：补给丢失、补给点不变，并提示。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 5, spare: 1, draft: 0, commission: 0));

        // 1 = 带入备用子；1 号出生区；q → 提示 → n 取消；q → 提示 → y 确认退出。
        (int exit, string first) = Play("1\n1\nq\nn\nq\ny\n", temp.Store());
        Assert.Equal(0, exit);
        Assert.Equal(2, Regex.Matches(first, "退出将丢失带入的补给；弃赛可返还补给并带出 50%").Count);
        Assert.Contains("已退出。种子 42", first, StringComparison.Ordinal);
        CarryProfile quit = CarryProfile.Parse(temp.Read());
        Assert.Equal(CarryFixtures.Spare, quit.InFlight!.Carry);
        Assert.Equal((5, 0), (quit.Points, quit.StockOf(SupplyKind.SpareStone)));

        (_, string second) = Play("\n", temp.Store(), seed: 43);
        Assert.Contains("上一局中途退出", second, StringComparison.Ordinal);
        Assert.Contains("备用子已丢失", second, StringComparison.Ordinal);
        Assert.True(second.IndexOf("上一局中途退出", StringComparison.Ordinal) < second.IndexOf("补给点 5", StringComparison.Ordinal), "先结算残留在途，再进入补给选择");
        CarryProfile next = CarryProfile.Parse(temp.Read());
        Assert.Equal((5, 0), (next.Points, next.StockOf(SupplyKind.SpareStone)));
        Assert.NotEqual(quit.InFlight.MatchId, next.InFlight!.MatchId);
        Assert.Null(next.InFlight.Carry);
    }

    [Fact]
    public void 未带入时q直接退出()
    {
        // 没有带入就没有可丢的补给：q 不提示、不要求确认（与关闭带入时相同）。
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 5, spare: 0, draft: 0, commission: 0));

        (int exit, string text) = Play("\n1\nq\n", temp.Store());

        Assert.Equal(0, exit);
        Assert.DoesNotContain("退出将丢失", text, StringComparison.Ordinal);
        Assert.Contains("已退出。种子 42", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 脚本化终端对局不传档案时不碰真实档案目录()
    {
        // 守门（design.md Risks「测试碰真实档案」）：PlayCommand.Run 的档案参数缺省为 null = 关闭；不传档案跑一局脚本，
        // 缺省档案所在目录（%APPDATA%\Siege）前后状态不变。
        ParameterInfo profile = typeof(PlayCommand).GetMethod(nameof(PlayCommand.Run))!.GetParameters().Single(p => p.Name == "profile");
        Assert.True(profile.HasDefaultValue);
        Assert.Null(profile.DefaultValue);

        string before = RealProfileDir.State();
        var output = new StringWriter();
        PlayCommand.Run(7, 4, 2, AiDifficulty.Easy, new StringReader("2\n\npass\n"), output);
        Assert.Equal(before, RealProfileDir.State());
    }
}
