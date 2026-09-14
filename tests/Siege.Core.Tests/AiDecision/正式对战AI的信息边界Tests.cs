using System.Collections.Immutable;
using System.Reflection;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 正式对战 AI 的信息边界</summary>
public class 正式对战AI的信息边界Tests
{
    [Fact]
    public void 未知信物不可读()
    {
        // 设计文档 §15.1 / 裁决 9：正式 AI 的输入 = MatchPublicView + 本人句柄；未揭示信物在公开状态里 Content 为 null，
        // 且 AI 的类型化可达闭包里不存在 RelicLedger / RelicGenerationRecord / MatchFlow 等能读到真实内容的类型。
        // 变异验证 M-A1：给 HeuristicTurnController 加 `public RelicLedger? Leak { get; init; }` → 红 3（本测试、敌方手牌数量不可读、高难度不越权）。
        //（未用字段的写法会先撞 CS0169 警告即错误，编译期就被拦下。）
        // 变异验证 M-C1（check）：给非根类型 BatchEvaluator 加 `public RelicGenerationRecord? Leak` 属性（经 CreateEvaluator 返回类型可达）→ 红 3（同上三条），证明闭包确实展开了非根类型的公开成员。
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Command())]);
        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0);

        Assert.Empty(AiFixtures.Violations(typeof(HeuristicTurnController)));
        ImmutableHashSet<Type> reachable = AiFixtures.ReachableTypes(typeof(HeuristicTurnController));
        Assert.Contains(typeof(MatchPublicView), reachable);
        Assert.Contains(typeof(RelicPublicState), reachable);

        // 行为：AI 能看到的信物状态里没有类型与强度；评价器只能对它做先验估计
        RelicPublicState state = match.Publish().Relics.Single(r => r.Coord == TestMaps.At("E5"));
        Assert.Equal(AiFixtures.P0, ai.CreateEvaluator().Player);
        Assert.Equal(RelicEstimate.ExpectedValueScaled(RelicZone.Contested) / RelicEstimate.PercentScale, RelicEstimate.Estimate(state));
        Assert.False(state.IsRevealed);
        Assert.Null(state.Content);
        Assert.Equal(RelicZone.Contested, state.Spec.Zone);
        Assert.DoesNotContain("Command", state.ToString());
    }

    [Fact]
    public void 敌方手牌数量不可读()
    {
        // 设计文档 §13.1 / §13.2：对手手牌只返回类型集合。公开视图是独立类型，结构上没有数量字段；
        // AI 闭包里唯一的 HandPrivateView 来自本人的 PlayerHandAccess（Deploy/Recruit 递入），不经 HandLedger。
        // 变异验证：M-A1（见上）→ 本测试红；M-A3（见下）钉住公开快照闭包。
        MatchFlow match = MatchFixtures.Started();
        match.Debug.SeedHand(AiFixtures.P1, (PieceType.Basic, 7), (PieceType.Fortress, 2));
        match.Debug.Recalculate();

        MatchPublicView view = match.Publish();
        HandPublicView enemy = view.Hands.Single(h => h.Player == AiFixtures.P1);
        Assert.Equal([PieceType.Basic, PieceType.Fortress], enemy.Types);
        Assert.DoesNotContain("7", enemy.ToString());
        Assert.DoesNotContain("2", enemy.ToString());

        foreach (PropertyInfo prop in typeof(HandPublicView).GetProperties())
        {
            Assert.False(prop.PropertyType == typeof(int) || prop.PropertyType == typeof(long) || prop.PropertyType == typeof(HandEntry),
                $"公开视图含数量字段 {prop.Name}");
        }

        ImmutableHashSet<Type> fromPublicView = AiFixtures.ReachableTypes(typeof(MatchPublicView));
        Assert.DoesNotContain(typeof(HandPrivateView), fromPublicView);
        Assert.DoesNotContain(typeof(HandEntry), fromPublicView);
        Assert.DoesNotContain(typeof(RecruitPanelView), fromPublicView);
        Assert.Empty(AiFixtures.Violations(typeof(HeuristicTurnController)));
    }

    [Fact]
    public void 敌方未确认批次不可读()
    {
        // 设计文档 §13.2：对手正在暂放时，正式 AI 的任何读取路径都拿不到暂放信息——公开快照的盘面是正式盘面的副本，
        // 闭包里没有 StagedBatch / Placement；AI 唯一能拿到的 StagedBatch 是 Deploy 递入的本人批次。
        // 变异验证 M-A3：MatchPublicView 加 `StagedBatch? CurrentBatch` 字段 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started();
        match.Debug.SetOrder(AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3);
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("B2"), PieceType.Basic));
        Assert.Equal(1, batch.Count);

        MatchPublicView seenByEnemy = match.Publish();
        Assert.Null(seenByEnemy.Board[TestMaps.At("B2")].Occupant);
        Assert.Equal(TurnStage.Deploy, seenByEnemy.Stage);

        ImmutableHashSet<Type> fromPublicView = AiFixtures.ReachableTypes(typeof(MatchPublicView));
        Assert.DoesNotContain(typeof(StagedBatch), fromPublicView);
        Assert.DoesNotContain(typeof(Placement), fromPublicView);
        Assert.DoesNotContain(typeof(BatchContext), fromPublicView);
    }
}
