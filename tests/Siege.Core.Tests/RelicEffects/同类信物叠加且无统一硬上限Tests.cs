using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Relics;
using Siege.Presentation.Hand;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 同类信物叠加且无统一硬上限</summary>
public class 同类信物叠加且无统一硬上限Tests
{
    [Fact]
    public void 军令叠加()
    {
        // growth-pass-1 relic-effects 规格：第 2 大回合（基础 3）2 枚普通军令 + 1 枚 +2 军令 → 3 + 1 + 1 + 2 = 7。
        // growth-pass-1 改写：原用第 1 大回合，按规格改为第 2 大回合（同在第一阶段，期望值不变）。
        // 变异验证 M-E4：BuildSnapshot 对 Command 用 `deploy = Base + Magnitude`（覆盖而非累加）→ 红 2，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            ("B2", RelicFixtures.Command()), ("E7", RelicFixtures.Command()), ("H8", RelicFixtures.Command(2)));
        board.Place("B2", TestMaps.P0).Place("E7", TestMaps.P0).Place("H8", TestMaps.P0);
        ledger.Settle(board, 2);

        Assert.Equal(7, ledger.SnapshotFor(TestMaps.P0, board, 0, 2).DeployLimit);
    }

    [Fact]
    public void 无硬上限()
    {
        // 3 枚 +2 军令 → 3 + 6 = 9，系统接受，不截断；同类叠加对探勘 / 征召 / 兵站同样成立。
        // 变异验证 M-E5：BuildSnapshot 末尾加 `deploy = Math.Min(deploy, 8)` → 红 1（本测试）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            ("B2", RelicFixtures.Command(2)), ("E7", RelicFixtures.Command(2)), ("H8", RelicFixtures.Command(2)),
            ("B8", RelicFixtures.Prospecting(2)), ("H2", RelicFixtures.Prospecting(2)), ("E4", RelicFixtures.Prospecting(2)));
        foreach (string cell in new[] { "B2", "E7", "H8", "B8", "H2", "E4" })
        {
            board.Place(cell, TestMaps.P0);
        }

        ledger.Settle(board, 1);
        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);

        Assert.Equal(9, snapshot.DeployLimit);
        Assert.Equal(11, snapshot.RevealCount);
        Assert.Equal(new DeployLimitPeak(9, 1, TestMaps.P0), ledger.DeployLimitPeak);
    }

    [Fact]
    public void 来源可拆分()
    {
        // growth-pass-1 relic-effects 规格：第 7 大回合控制 2 枚普通军令，部署上限 7 → 来源拆分为基础 5（第 7 大回合阶段值）与军令 +1、+1。
        // P1 经正式结算占据 H4、J4（占据即揭示并控制），P0 视角读公开结构参数与手牌面板文案。
        // 变异验证 M-GP4（MatchFlow.StructuresOf 的部署基础改传 BaseDeployLimitFor(1)，并去掉 Parameter 的一致性抛出——
        //   即把 +2 阶段增量算进"军令加成"）→ 全套红 2（本测试 Base 3 / Bonus 4、「显示参数与来源」）。
        MatchFlow match = MatchFixtures.Started(relics: [("H4", RelicFixtures.Command()), ("J4", RelicFixtures.Command())])
            .AtRound(7, MatchFixtures.All);
        match.PassTurn();
        match.PlayTurn("H4", "J4");

        StructureParameter deploy = match.PublishSupplement().Structures.Single(s => s.Player == MatchFixtures.P1).Parameters!.DeployLimit;
        Assert.Equal((7, 5, 2), (deploy.Value, deploy.Base, deploy.Bonus));
        Assert.Equal([(RelicType.Command, 1), (RelicType.Command, 1)], deploy.Sources.Select(s => (s.Type, s.Magnitude)));
        Assert.Equal(["H4", "J4"], deploy.Sources.Select(s => s.Coord).Notations());

        ParameterView view = match.World(MatchFixtures.P0).HandPanel().Opponents.Single(o => o.Player == MatchFixtures.P1).Structure!.DeployLimit;
        Assert.Equal("部署上限 7（基础 5，+2 来自 军令×2）", view.Text);

        // 无军令的玩家：来源为空，基础即当前值 5
        StructureParameter plain = match.PublishSupplement().Structures.Single(s => s.Player == MatchFixtures.P2).Parameters!.DeployLimit;
        Assert.Equal((5, 5), (plain.Value, plain.Base));
        Assert.Empty(plain.Sources);
    }
}
