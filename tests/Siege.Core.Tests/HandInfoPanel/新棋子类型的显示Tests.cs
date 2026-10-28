using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Hand;
using Siege.Presentation.Text;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.HandInfoPanel;

/// <summary>规格：hand-info-panel —— Requirement: 新棋子类型的显示（more-pieces-relics ADDED；驿站来源随旧信物删除，R47 改名）</summary>
public class 新棋子类型的显示Tests
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

    private static MatchFlow B控制驿站探勘军令() =>
        LinkModeFixtures.PlainStarted()
            .AtRound(7, TestMaps.All)
            .Pieces(P1, PieceType.Basic, "H6", "J6", "K6");
}
