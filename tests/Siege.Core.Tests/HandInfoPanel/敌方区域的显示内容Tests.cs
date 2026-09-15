using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Presentation.Hand;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.HandInfoPanel;

/// <summary>规格：hand-info-panel —— Requirement: 敌方区域的显示内容</summary>
public class 敌方区域的显示内容Tests
{
    [Fact]
    public void 敌方只显示类型()
    {
        // 设计文档 §14.3 算例：A（P0）持普通子×6、堡垒子×1 → 其他玩家只看到普通子与堡垒子两种类型。
        // 结构上：敌方区域的构建入口只接受 PublicWorld；区域类型闭包里没有私有手牌视图与两段账。
        // 变异验证 M-E1：HandInfoPanelView.Opponent 的 Types 改为 `[.. hand.Types.Take(1)]` → 本测试红 1。
        // 变异验证 M-E2（非根类型）：给 ParameterView 加 `public HandEntry? Leak { get; init; }` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        match.Debug.SeedHand(P0, (PieceType.Basic, 6), (PieceType.Fortress, 1));

        foreach (PlayerId viewer in new[] { P1, P2, P3 })
        {
            OpponentHandAreaView area = match.World(viewer).HandPanel().Opponents.Single(o => o.Player == P0);
            Assert.Equal([PieceType.Basic, PieceType.Fortress], area.Types);
            Assert.Equal(["普通子", "堡垒子"], area.TypeNames);
            Assert.True(area.IsActing);
        }

        Assert.Empty(PrivateLeaks(typeof(OpponentHandAreaView)));
        Assert.All(typeof(HandInfoPanelView).GetMethod(nameof(HandInfoPanelView.Opponent))!.GetParameters(),
            p => Assert.True(p.ParameterType == typeof(PublicWorld) || p.ParameterType == typeof(PlayerId), p.Name));
    }

    [Fact]
    public void 不显示敌方征募与暂放()
    {
        // 设计文档 §14.3 / §13.2：A（P0）正在征募、随后暂放 → 面板的 A 区域不出现任何征募候选或暂放信息。
        // A 只持普通子、只选普通子候选，类型集合不变，因此 A 区域在征募前、征募中、暂放中三个时刻逐字相同。
        // 变异验证 M-E3（非根类型）：给 StructureView 加 `public Siege.Core.Batch.StagedBatch? Pending { get; init; }` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        match.Debug.SeedHand(P0, (PieceType.Basic, 5));
        string idle = Area(match);

        match.BeginTurn();
        RecruitPanelView panel = match.EnterRecruit();
        string recruiting = Area(match);
        foreach (int index in panel.Candidates.Where(c => c.Type == PieceType.Basic).Take(1).Select(c => c.Index))
        {
            match.CurrentHand().Pick(index);
        }

        StagedBatch batch = match.EnterDeploy();
        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Basic));
        string staging = Area(match);

        Assert.Equal(idle, recruiting);
        Assert.Equal(idle, staging);
        string[] leaks = PrivateLeaks(typeof(OpponentHandAreaView));
        Assert.DoesNotContain(nameof(RecruitPanelView), leaks);
        Assert.DoesNotContain(nameof(StagedBatch), leaks);
        Assert.DoesNotContain(nameof(Placement), leaks);
        Assert.Empty(leaks);
    }

    private static string Area(MatchFlow match) => Dump(match.World(P1).HandPanel().Opponents.Single(o => o.Player == P0));
}
