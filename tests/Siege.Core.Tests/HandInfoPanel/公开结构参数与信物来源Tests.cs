using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Presentation.Hand;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.HandInfoPanel;

/// <summary>规格：hand-info-panel —— Requirement: 公开结构参数与信物来源</summary>
public class 公开结构参数与信物来源Tests
{
    [Fact]
    public void 显示参数与来源()
    {
        // 设计文档 §14.3 算例：B（P1）部署上限含 +2 来自两枚军令信物 → 面板显示总值并标注 2 点来自军令。
        // growth-pass-1 改写：第 5 大回合分阶段基础值 4（原 3），总值 5→6、基础 3→4；+2 军令来源不变。
        // P1 经正式结算占据 H4、J4 两枚军令（占据即揭示并控制）；数值取自账本副本上的效果快照，来源取同副本的控制状态。
        // 变异验证 M-S1（结构）：MatchFlow.StructuresOf 的部署上限来源改传 RelicType.Depot → 抛出"来源与效果快照不一致"，本测试红 1。
        // 变异验证 M-S2（呈现）：StructureView.Parameter 的文案改用 `parameter.Base` 代替 `parameter.Value` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5(relics: [("H4", RelicFixtures.Command()), ("J4", RelicFixtures.Command())]);
        match.PassTurn();
        match.PlayTurn("H4", "J4");

        ParameterView deploy = match.World(P0).HandPanel().Opponents.Single(o => o.Player == P1).Structure!.DeployLimit;

        Assert.Equal((6, 4), (deploy.Value, deploy.Base));
        Assert.Equal(["H4 军令 +1", "J4 军令 +1"], deploy.Sources.Select(s => s.Text));
        Assert.All(deploy.Sources, s => Assert.Equal((RelicType.Command, 1), (s.Type, s.Magnitude)));
        Assert.Equal("部署上限 6（基础 4，+2 来自 军令×2）", deploy.Text);

        // 其他三项无来源
        StructureView structure = match.World(P0).HandPanel().Opponents.Single(o => o.Player == P1).Structure!;
        Assert.Equal("展示数 5（基础）", structure.RevealCount.Text);
        Assert.Empty(structure.TypeSlots.Sources);
    }

    [Fact]
    public void 显示落后补偿来源()
    {
        // 规格 Scenario「显示落后补偿来源」（catch-up-recruit 裁决 5）：D 本小回合以最后一名开始 → 展示数 6、选取数 4，
        // 面板各标注其中 1 点来自「落后补偿」；落后补偿是<b>非信物</b>来源，不进 Sources。
        // 势力独立复算（四邻接）：P0 A1-D1 → 4 + 5 = 9；P1 G1 H1 J1 → 3 + 4 = 7；P2 A9 B9 → 2 + 3 = 5；P3 J9 → 1 + 2 = 3。
        // 变异验证 M-CU6：MatchFlow.Parameter 把 catchUp 折进 sources（当成一枚探勘信物来源）→ 红 1（本测试：Sources 非空且文案变成 探勘×1）。
        // 变异验证 M-CU6b：StructureView.Parameter 的文案丢掉落后补偿项（只拼信物）→ 本测试红：文案变成"展示数 6（基础）"。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOff)
            .AtRound(5, [P3, P0, P1, P2])
            .Stones(P0, "A1", "B1", "C1", "D1")
            .Stones(P1, "G1", "H1", "J1")
            .Stones(P2, "A9", "B9")
            .Stones(P3, "J9");
        Assert.Equal(4, match.Scoreboard.Latest!.RankOf(P3));

        StructureView structure = match.World(P0).HandPanel().Opponents.Single(o => o.Player == P3).Structure!;

        Assert.Equal((6, 5, 1), (structure.RevealCount.Value, structure.RevealCount.Base, structure.RevealCount.CatchUp));
        Assert.Empty(structure.RevealCount.Sources);
        Assert.Equal("展示数 6（基础 5，+1 来自 落后补偿 +1）", structure.RevealCount.Text);
        Assert.Equal((4, 3, 1), (structure.FreePickCount.Value, structure.FreePickCount.Base, structure.FreePickCount.CatchUp));
        Assert.Empty(structure.FreePickCount.Sources);
        Assert.Equal("选取数 4（基础 3，+1 来自 落后补偿 +1）", structure.FreePickCount.Text);

        // 第 1 名不获补偿，且补偿不外溢到其它两项
        StructureView leader = match.World(P0).HandPanel().Own.Structure!;
        Assert.Equal((5, 0), (leader.RevealCount.Value, leader.RevealCount.CatchUp));
        Assert.Equal("展示数 5（基础）", leader.RevealCount.Text);
        Assert.Equal((0, 0), (structure.TypeSlots.CatchUp, structure.DeployLimit.CatchUp));
    }

    [Fact]
    public void 参数对全体公开()
    {
        // 设计文档 §14.3：任意玩家打开面板 → 全部玩家的四项结构参数均可见，且不同观察者看到的同一玩家参数逐字相同。
        // 变异验证 M-S3：HandInfoPanelView.OwnArea 的 Structure 改为 null → 本测试红 1。
        MatchFlow match = AiFixtures.Round5(relics: [("B4", RelicFixtures.Prospecting(2))]);
        match.PlayTurn("B4");

        var seen = new Dictionary<PlayerId, List<string>>();
        foreach (PlayerId viewer in MatchFixtures.All)
        {
            HandInfoPanelView panel = match.World(viewer).HandPanel();
            Assert.Equal(3, panel.Opponents.Length);
            foreach ((PlayerId player, StructureView? structure) in panel.Opponents.Select(o => (o.Player, o.Structure)).Append((panel.Own.Player, panel.Own.Structure)))
            {
                Assert.NotNull(structure);
                (seen.TryGetValue(player, out List<string>? list) ? list : seen[player] = []).Add(Dump(structure));
            }
        }

        Assert.Equal(4, seen.Count);
        Assert.All(seen.Values, texts => Assert.Single(texts.Distinct()));
        Assert.Contains("展示数 7（基础 5，+2 来自 探勘×1）", seen[P0][0]);
    }
}
