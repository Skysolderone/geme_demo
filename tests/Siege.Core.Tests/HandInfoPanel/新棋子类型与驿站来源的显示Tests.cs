using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Presentation.Hand;
using Siege.Presentation.Text;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.HandInfoPanel;

/// <summary>规格：hand-info-panel —— Requirement: 新棋子类型与驿站来源的显示（more-pieces-relics ADDED）</summary>
public class 新棋子类型与驿站来源的显示Tests
{
    [Fact]
    public void 新类型在自己区域显示()
    {
        // Scenario「新类型在自己区域显示」：当前玩家持有旗手子×2、界碑子×1 → 自己区域以"旗手""界碑"的名称与对应标识显示这两种类型及准确数量。
        // 名称口径（段 D 定，段 C 待决 10）：面板沿用 Labels.Piece 的全名（"旗手子"，同既有的"普通子"），终端沿用短名（"旗手"，同"普通"）——
        // 两者都含规格点名的"旗手""界碑"，不在表现层另起第二张名称表。标识 = 行上的 PieceType（Godot 按它取轮廓）。
        // 敌方区域同样按类型给出名称（只有类型、没有数量）。
        // 骨架态即绿（段 A 已补 Labels.Piece 四个名称）；变异验证见 implement.md 段 D（MD-H1：Labels.Piece 旗手子 → "未知"）。
        MatchFlow match = AiFixtures.Round5();
        match.Debug.SeedHand(P0, (PieceType.Bannerman, 2), (PieceType.Boundary, 1));

        OwnHandAreaView own = match.World(P0).HandPanel().Own;

        Assert.Equal([(PieceType.Bannerman, 2), (PieceType.Boundary, 1)], own.Rows.Select(r => (r.Type, r.Count)));
        Assert.Equal(["旗手子", "界碑子"], own.Rows.Select(r => r.Name));
        Assert.Equal(["旗手子 ×2", "界碑子 ×1"], own.Rows.Select(r => r.Text));
        Assert.Contains("旗手", own.Rows[0].Name, StringComparison.Ordinal);
        Assert.Contains("界碑", own.Rows[1].Name, StringComparison.Ordinal);
        Assert.Equal(own.Rows.Select(r => Labels.Piece(r.Type)), own.Rows.Select(r => r.Name));

        OpponentHandAreaView seen = match.World(P1).HandPanel().Opponents.Single(o => o.Player == P0);
        Assert.Equal([PieceType.Bannerman, PieceType.Boundary], seen.Types.Order());
        Assert.Equal(["旗手子", "界碑子"], seen.Types.Zip(seen.TypeNames).OrderBy(t => t.First).Select(t => t.Second));
    }

    [Fact]
    public void 驿站来源逐枚列出()
    {
        // Scenario「驿站来源逐枚列出」：玩家 B 的展示数为 8，来源为基础 5、探勘 +1、驿站 +2（计入 2 枚其他信物）
        // → 面板显示展示数 8，并分别列出探勘 +1 与"驿站 +2（控制 2 枚其他信物）"，各来源之和为 8。
        // 局面（同 私人征募面板Tests.驿站生效）：B = P1 占据驿站 G4、探勘 H4、军令 J4——驿站计除自身外 2 枚受控信物 → +2；探勘 +1；军令只进部署上限。
        // 数值来自 Core 公开补充载荷（MatchFlow 结构参数来源），面板只拼文案。Godot 手牌面板显示的是 ParameterView.Text，所以驿站明细必须在 Text 里。
        // 先红：骨架态文案按类型合并为"驿站×1"，来源文案不注"控制 N 枚其他信物"。
        MatchFlow match = B控制驿站探勘军令();

        foreach (ParameterView reveal in new[]
        {
            match.World(P0).HandPanel().Opponents.Single(o => o.Player == P1).Structure!.RevealCount,
            match.World(P1).HandPanel().Own.Structure!.RevealCount,
        })
        {
            Assert.Equal((8, 5), (reveal.Value, reveal.Base));
            Assert.Equal(["H4 探勘 +1", "G4 驿站 +2（控制 2 枚其他信物）"], reveal.Sources.Select(s => s.Text).OrderBy(t => t.Contains("驿站", StringComparison.Ordinal)));
            Assert.Equal(8, reveal.Base + reveal.Sources.Sum(s => s.Magnitude));
            Assert.Equal("展示数 8（基础 5，+3 来自 探勘×1、驿站 G4 +2（控制 2 枚其他信物））", reveal.Text);
        }
    }

    [Fact]
    public void 零加成的驿站不列出()
    {
        // 段 C 待决 5 / 段 B 待决 4 的图形面板口径（段 D 定）：只控制驿站本身时它的加成为 +0，面板与终端一样不列这一条——
        // 两处共用 Core 的 StructureParameter.ListedSources（略去 0 加成来源），不各写过滤；略去的项为 0，各来源之和仍等于当前值。
        // 规则层的来源拆分照旧逐枚保留 +0（遥测照记）。先红：骨架态 ListedSources 不过滤。
        MatchFlow match = MatchFixtures.Started(relics: [("G4", RelicFixtures.Relay())])
            .AtRound(7, MatchFixtures.All)
            .Pieces(P1, PieceType.Basic, "G4");

        Siege.Core.Preview.StructureParameter core = match.PublishSupplement().Structures.Single(s => s.Player == P1).Parameters!.RevealCount;
        Siege.Core.Preview.ParameterSource relay = Assert.Single(core.Sources);
        Assert.Equal((RelicType.Relay, 0), (relay.Type, relay.Magnitude));
        Assert.Empty(core.ListedSources);

        ParameterView reveal = match.World(P0).HandPanel().Opponents.Single(o => o.Player == P1).Structure!.RevealCount;
        Assert.Equal((5, 5), (reveal.Value, reveal.Base));
        Assert.Empty(reveal.Sources);
        Assert.Equal("展示数 5（基础）", reveal.Text);
        Assert.DoesNotContain("驿站", reveal.Text, StringComparison.Ordinal);
    }

    private static MatchFlow B控制驿站探勘军令() =>
        MatchFixtures.Started(relics: [("G4", RelicFixtures.Relay()), ("H4", RelicFixtures.Prospecting()), ("J4", RelicFixtures.Command())])
            .AtRound(7, MatchFixtures.All)
            .Pieces(P1, PieceType.Basic, "G4", "H4", "J4");
}
