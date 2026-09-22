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
    public void 来源只有基础值与信物()
    {
        // 规格 Scenario「来源只有基础值与信物」（restore-go-core-rules 裁决 #7 / D7）：读取任一玩家任一结构参数的来源拆分 →
        // 各来源之和等于该参数的当前值，且来源类别只包含"分阶段基础值"与"信物"。
        // 局面一：名次阶梯（P3 最后一名，无信物）；势力独立复算（四邻接）：P0 9、P1 7、P2 5、P3 3。
        // 局面二：P1 经正式结算控制两枚军令（同「显示参数与来源」）。两局覆盖"末名"与"有信物来源"两种情形。
        // 先红：旧实现下局面一 P3 的展示数 6 = 基础 5 + 名次加成 1，信物来源为空 → 基础 + Σ信物 ≠ 当前值；且 StructureParameter 多一个非信物来源成员。
        // 变异验证 M-D2：StructuresOf 在账本副本快照之后给名次 ≥ 3 者展示数 +1（第三类来源）→ 红 12（本测试 + 11 条 TacticalLayers 用例：组装方核对"基础 + Σ信物 ≠ 快照"抛出）。
        MatchFlow ladder = MatchFixtures.Started()
            .AtRound(5, [P3, P0, P1, P2])
            .Stones(P0, "A1", "B1", "C1", "D1")
            .Stones(P1, "G1", "H1", "J1")
            .Stones(P2, "A9", "B9")
            .Stones(P3, "J9");
        Assert.Equal(4, ladder.Scoreboard.Latest!.RankOf(P3));

        MatchFlow command = AiFixtures.Round5(relics: [("H4", RelicFixtures.Command()), ("J4", RelicFixtures.Command())]);
        command.PassTurn();
        command.PlayTurn("H4", "J4");

        int checkedParameters = 0;
        int relicSourced = 0;
        foreach (MatchFlow match in new[] { ladder, command })
        {
            // 规则层：Base 恒为分阶段基础值（部署上限）或默认值（其余三项），其余全部来自信物
            foreach (Siege.Core.Preview.PlayerStructure player in match.PublishSupplement().Structures)
            {
                Siege.Core.Preview.StructureParameters p = player.Parameters!;
                foreach ((Siege.Core.Preview.StructureParameter parameter, int expectedBase) in new[]
                {
                    (p.RevealCount, 5), (p.FreePickCount, 3), (p.TypeSlots, 5), (p.DeployLimit, 4),
                })
                {
                    Assert.Equal(expectedBase, parameter.Base);
                    Assert.Equal(parameter.Value, parameter.Base + parameter.Sources.Sum(s => s.Magnitude));
                    checkedParameters++;
                    relicSourced += parameter.Sources.Length;
                }
            }

            // 表现层：面板上每一项的文案只由基础值与信物拼成
            HandInfoPanelView panel = match.World(P0).HandPanel();
            foreach (StructureView structure in panel.Opponents.Select(o => o.Structure!).Append(panel.Own.Structure!))
            {
                foreach (ParameterView view in new[] { structure.RevealCount, structure.FreePickCount, structure.TypeSlots, structure.DeployLimit })
                {
                    Assert.Equal(view.Value, view.Base + view.Sources.Sum(s => s.Magnitude));
                }
            }
        }

        // 样本口径下界：2 局 × 4 人 × 4 项，且确实出现过信物来源（否则"其余全部来自信物"只在零来源上成立）
        Assert.Equal(32, checkedParameters);
        Assert.Equal(2, relicSourced);

        // 结构上只有两类来源：规则层参数与表现层视图的数值成员只剩 基础值 / 当前值 / 信物来源
        Assert.Equal(["Base", "Sources", "Value"],
            typeof(Siege.Core.Preview.StructureParameter).GetConstructors().Single().GetParameters().Select(x => x.Name!).Order());
        Assert.Equal(["Base", "Label", "Sources", "Text", "Value"],
            typeof(ParameterView).GetConstructors().Single().GetParameters().Select(x => x.Name!).Order());
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
