using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Hand;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.HandInfoPanel;

/// <summary>规格：hand-info-panel —— Requirement: 公开结构参数与信物来源（relic-redesign D4：信物来源随旧信物删除，只剩基础值）</summary>
public class 公开结构参数与信物来源Tests
{
    [Fact]
    public void 显示参数为基础值()
    {
        // 设计文档 §14.3：面板显示对手的四项结构参数。relic-redesign D4：旧信物（军令等）删除后结构参数没有信物来源，当前值即分阶段基础值——
        // 第 5 大回合部署上限 4、文案"（基础）"。原算例"+2 来自两枚军令"随旧信物删除。
        // 变异验证 M-S2（呈现）：StructureView.Parameter 的文案改用 `parameter.Base` 以外的值 → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        match.PassTurn();
        match.PlayTurn("J6", "K6");

        StructureView structure = match.World(P0).HandPanel().Opponents.Single(o => o.Player == P1).Structure!;

        Assert.Equal((4, 4), (structure.DeployLimit.Value, structure.DeployLimit.Base));
        Assert.Equal("部署上限 4（基础）", structure.DeployLimit.Text);
        Assert.Equal("展示数 5（基础）", structure.RevealCount.Text);
    }

    [Fact]
    public void 参数只有基础值与当前值()
    {
        // 规格 Scenario「来源只有基础值与信物」（restore-go-core-rules 裁决 #7 / D7）在 relic-redesign D4 之后：信物来源随旧信物删除，任一玩家任一结构参数的当前值 = 基础值
        // （部署上限为分阶段基础值，其余三项为默认值）；名次不进任何参数。局面：名次阶梯（P3 最后一名）。
        // 变异验证 M-D2：StructuresOf 给名次 ≥ 3 者展示数 +1（第三类来源）→ 本测试红。
        MatchFlow ladder = LinkModeFixtures.PlainStarted()
            .AtRound(5, [P3, P0, P1, P2])
            .Stones(P0, "C3", "D3", "E3", "F3")
            .Stones(P1, "H3", "J3", "K3")
            .Stones(P2, "C10", "D10")
            .Stones(P3, "K10");
        Assert.Equal(4, ladder.Scoreboard.Latest!.RankOf(P3));

        int checkedParameters = 0;
        foreach (Siege.Core.Preview.PlayerStructure player in ladder.PublishSupplement().Structures)
        {
            Siege.Core.Preview.StructureParameters p = player.Parameters!;
            foreach ((Siege.Core.Preview.StructureParameter parameter, int expectedBase) in new[]
            {
                (p.RevealCount, 5), (p.FreePickCount, 3), (p.TypeSlots, 5), (p.DeployLimit, 4),
            })
            {
                Assert.Equal((expectedBase, expectedBase), (parameter.Base, parameter.Value));
                checkedParameters++;
            }
        }

        HandInfoPanelView panel = ladder.World(P0).HandPanel();
        foreach (StructureView structure in panel.Opponents.Select(o => o.Structure!).Append(panel.Own.Structure!))
        {
            foreach (ParameterView view in new[] { structure.RevealCount, structure.FreePickCount, structure.TypeSlots, structure.DeployLimit })
            {
                Assert.Equal(view.Value, view.Base);
                Assert.EndsWith("（基础）", view.Text, StringComparison.Ordinal);
            }
        }

        Assert.Equal(16, checkedParameters);
        Assert.Equal(["Base", "Value"],
            typeof(Siege.Core.Preview.StructureParameter).GetConstructors().Single().GetParameters().Select(x => x.Name!).Order());
        Assert.Equal(["Base", "Label", "Text", "Value"],
            typeof(ParameterView).GetConstructors().Single().GetParameters().Select(x => x.Name!).Order());
    }

    [Fact]
    public void 参数对全体公开()
    {
        // 设计文档 §14.3：任意玩家打开面板 → 全部玩家的四项结构参数均可见，且不同观察者看到的同一玩家参数逐字相同。
        // 变异验证 M-S3：HandInfoPanelView.OwnArea 的 Structure 改为 null → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        match.PlayTurn("D6");

        var seen = new Dictionary<PlayerId, List<string>>();
        foreach (PlayerId viewer in TestMaps.All)
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
        Assert.Contains("展示数 5（基础）", seen[P0][0]);   // relic-redesign W1：旧探勘 +2 随旧信物删除，展示数只有基础值
    }
}
