using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Sim.Play;
using static Siege.Core.Tests.PieceRelics.CarryWiringFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign 接线段 W2-5：终端的带入选择（选件、勾保险、兑换）、部署挂出参数、中途退出与结算显示（D8）。
/// 脚本化输入 + <see cref="TempRelicProfile"/> 临时档案（档案测试一律用临时目录，MUST NOT 读写真实用户目录）。
/// </summary>
/// <remarks>
/// 变异验证（W2 实跑，均已恢复）：T1 <c>CarryTerminal.ParseLoadout</c> 忽略 <c>!</c>（不勾保险）→ 红 2（开局选择兑换并带入后中途退出只回保险件、超量与库存不足的选择被拒后可重选）；
/// T2 部署命令丢弃 <c>@N</c>（解析出的挂出下标置空）→ 红 1（部署挂出参数）。
/// </remarks>
public class 终端带入选择挂出与结算Tests
{
    private static (int Exit, string Text) Play(string script, RelicProfileStore? profile, ulong seed = 42)
    {
        var output = new StringWriter();
        int exit = PlayCommand.Run(seed, 4, 1, AiDifficulty.Easy, new StringReader(script), output,
            map: MapCatalog.Resolve("link:1:4"), contentSet: ContentSet.V1, profile: profile);
        return (exit, output.ToString());
    }

    [Fact]
    public void 开局选择兑换并带入后中途退出只回保险件()
    {
        // 新档案（3 件普通：战鼓 / 号令 / 望台各 1）加 17 点：兑换 1 件百变（−8 点），带入"战鼓（保险）、号令、百变"，插旗后退出（有带入，退出要确认）。
        // → 开局从库存扣除三件并写在途记录；只有本人看得到自己的清单。下次启动：残留在途记录按中途退出结算——只回保险件战鼓，补给点不变。
        using var temp = new TempRelicProfile();
        temp.Write(TempRelicProfile.JsonV2(17, drum: 1, command: 1, watchtower: 1));

        (int exit, string text) = Play("buy 4\n1! 2 4\n1\nq\ny\n", temp.Store());

        Assert.Equal(0, exit);
        Assert.Contains("已兑换 1 件百变", text, StringComparison.Ordinal);
        Assert.Contains("带入：战鼓（保险）、号令、百变（只有你看得到", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("带入：战鼓（保险）", StringComparison.Ordinal) < text.IndexOf("盲选出生座位", StringComparison.Ordinal), "带入应在插旗前选定");
        RelicProfile during = RelicProfile.Parse(temp.Read());
        Assert.Equal((9, 0, 0, 1, 0), (during.Points, during.StockOf(RelicKind.WarDrum), during.StockOf(RelicKind.Command), during.StockOf(RelicKind.Watchtower), during.StockOf(RelicKind.Chameleon)));
        Assert.Equal("战鼓（保险）、号令、百变", new RelicLoadout(during.InFlight!.Carried).ToString());

        (int again, string next) = Play("q\n", temp.Store());

        Assert.Equal(0, again);
        Assert.Contains("上一局中途退出：保险件 战鼓 已归还", next, StringComparison.Ordinal);
        RelicProfile after = RelicProfile.Parse(temp.Read());
        Assert.Equal((9, 1, 0, 0), (after.Points, after.StockOf(RelicKind.WarDrum), after.StockOf(RelicKind.Command), after.StockOf(RelicKind.Chameleon)));
        Assert.Null(after.InFlight);
    }

    [Fact]
    public void 超量与库存不足的选择被拒后可重选()
    {
        // AC4（终端口径）：选 6 件、勾 3 件保险、超出库存都被拒并说明原因，不开局；随后直接回车不带入，在途记录的清单为空。
        using var temp = new TempRelicProfile();
        temp.Write(TempRelicProfile.JsonV2(0, drum: 6));

        (int exit, string text) = Play("1 1 1 1 1 1\n1! 1! 1!\n2\n\n1\nq\n", temp.Store());

        Assert.Equal(0, exit);
        Assert.Contains("每局至多带入 5 件信物（选了 6 件）", text, StringComparison.Ordinal);
        Assert.Contains("至多 2 件可勾为保险（勾了 3 件）", text, StringComparison.Ordinal);
        Assert.Contains("号令库存不足", text, StringComparison.Ordinal);
        Assert.Contains("带入：不带入", text, StringComparison.Ordinal);
        RelicProfile during = RelicProfile.Parse(temp.Read());
        Assert.Equal(6, during.StockOf(RelicKind.WarDrum));
        Assert.Empty(during.InFlight!.Carried);
    }

    [Fact]
    public void 部署挂出参数()
    {
        // 部署命令 "E5 B @1" = 在 E5 暂放一枚普通子并挂上带入清单第 1 件；"F5 B" 后再 "F5 @2" = 给已暂放的格改挂第 2 件；"@9" 不存在 → 拒绝并提示。
        MatchFlow match = LinkModeFixtures.PlainStarted(options: On((0, Loadout((RelicKind.WarDrum, true), (RelicKind.Banner, false))))).AtRound(1, TestMaps.All);
        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        var output = new StringWriter();
        var console = new ConsoleController(TestMaps.P0, () => match.PublishSeatProjection(TestMaps.P0), match.PublishSupplement, match.PreviewCurrentBatch,
            new StringReader("E5 B @1\nF5 B\nF5 @2\nG5 B @9\nok\n"), output);

        console.Deploy(batch, match.Rehearse);

        Assert.Equal(["E5:Basic@0", "F5:Basic@1"], batch.Placements.Select(p => p.ToString()));
        string text = output.ToString();
        Assert.Contains("未挂出：[1] 战鼓（保险） [2] 军旗", text, StringComparison.Ordinal);
        Assert.Contains("带入清单第 9 件不存在或已挂出", text, StringComparison.Ordinal);
        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(RelicKind.Banner, match.Board.RelicAt(TestMaps.At("F5"))!.Value.Kind);
    }
}
