using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>规格：turn-sequence —— Requirement: 小回合的五个阶段</summary>
public class 小回合的五个阶段Tests
{
    [Fact]
    public void 阶段顺序()
    {
        // 设计文档 §5：信物快照 → 整理手牌 → 私人征募 → 批次部署 → 确认结算，不可调换或跳过。
        // 变异验证 M-T1：BeginTurn 不经 RelicSnapshot 直接进入 OrganizeHand → 红 1（本测试的阶段序列）；
        // M-T2：EnterDeploy 的 RequireStage 放宽为 OrganizeHand 或 Recruit → 红 1（本测试的跳阶段断言）。
        MatchFlow match = MatchFixtures.Started();
        int before = match.Events.Count;

        match.BeginTurn();
        Assert.Equal(TurnStage.OrganizeHand, match.Stage);
        Assert.NotNull(match.CurrentSnapshot);
        Assert.Throws<SiegeRuleException>(() => match.EnterDeploy());   // 跳过征募
        Assert.Throws<SiegeRuleException>(() => match.Confirm());       // 跳到确认

        match.EnterRecruit();
        Assert.Equal(TurnStage.Recruit, match.Stage);
        Assert.Throws<SiegeRuleException>(() => match.EnterRecruit());  // 重复进入
        Assert.Throws<SiegeRuleException>(() => match.Confirm());

        match.EnterDeploy();
        Assert.Equal(TurnStage.Deploy, match.Stage);
        Assert.Throws<SiegeRuleException>(() => match.EnterRecruit());  // 倒退

        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(TurnStage.Idle, match.Stage);

        TurnStage[] stages = [.. match.Events.Skip(before).Where(e => e.Kind == FlowEventKind.StageEntered).Select(e => Enum.Parse<TurnStage>(e.Detail))];
        Assert.Equal(
            [TurnStage.RelicSnapshot, TurnStage.OrganizeHand, TurnStage.Recruit, TurnStage.Deploy, TurnStage.Settlement, TurnStage.Idle],
            stages);
    }

    [Fact]
    public void 超限阻断征募()
    {
        // 设计文档 §5.2：持有类型数超过快照类型槽 → 征募阶段在强制整类弃牌完成前不可进入。
        // 原型只有五种棋子、基础槽 5，信物造不出超限，这里用测试接缝把快照的槽位缩到 3（相当于兵站丢失后的缩水）。
        // 变异验证 M-T3：EnterRecruit 捕获账本异常后仍 SetStage(Recruit) → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started();
        PlayerId p = match.CurrentPlayer!.Value;
        match.Debug.SeedHand(p, (PieceType.Basic, 5), (PieceType.Fortress, 1), (PieceType.Line, 1), (PieceType.Multiplier, 1));
        match.Debug.SetSnapshotTransform(s => new EffectSnapshot(s.Player, s.MajorRound, s.RevealCount, s.FreePickCount, 3, s.DeployLimit, s.EmblemCounts, s.HeldTypeCount));

        match.BeginTurn();
        Assert.Equal(1, match.CurrentSnapshot!.OverflowTypeCount);
        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => match.EnterRecruit());
        Assert.Contains("类型槽", ex.Message);
        Assert.Equal(TurnStage.OrganizeHand, match.Stage);

        match.CurrentHand().Discard(PieceType.Multiplier);
        match.EnterRecruit();
        Assert.Equal(TurnStage.Recruit, match.Stage);
    }

    [Fact]
    public void 快照在回合内不变()
    {
        // 设计文档 §5.1：本小回合批次中占领军令信物 → 本小回合部署上限仍为快照值 4；下一小回合才变 5。
        // growth-pass-1 改写：第 5 大回合分阶段基础值为 4（原基础 3），快照值 3→4、下一小回合 4→5，暂放多一枚（C8）才触及上限。
        // 变异验证 M-T4：EnterDeploy 改为现算 Relics.SnapshotFor 而不用 _snapshot → 本测试仍绿（占领在确认时才生效）；
        // 真正钉住"回合内不变"的是快照对象引用不变 + 上限 3：把 CurrentSnapshot 改成每次访问重新生成 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOff, relics: [("E5", RelicFixtures.Command())]).AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);

        match.BeginTurn();
        EffectSnapshot snapshot = match.CurrentSnapshot!;
        Assert.Equal(4, snapshot.DeployLimit);
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("A5"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("B7"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("C8"), PieceType.Basic));
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, batch.Stage(TestMaps.At("D2"), PieceType.Basic)!.Kind);
        Assert.Same(snapshot, match.CurrentSnapshot);
        Assert.Equal(4, batch.Context.DeployLimit);
        Assert.True(match.Confirm().Confirmed);

        // 其余三人 Pass，P0 的下一小回合快照才含军令
        match.PassTurn();
        match.PassTurn();
        match.PassTurn();
        Assert.Equal(MatchFixtures.P0, match.CurrentPlayer);
        match.BeginTurn();
        Assert.Equal(5, match.CurrentSnapshot!.DeployLimit);
    }
}
