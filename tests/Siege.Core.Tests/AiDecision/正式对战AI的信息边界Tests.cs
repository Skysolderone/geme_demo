using System.Collections.Immutable;
using System.Reflection;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 正式对战 AI 的信息边界</summary>
public class 正式对战AI的信息边界Tests
{
    [Fact]
    public void 正式AI拿不到对局本体与账本()
    {
        // 设计文档 §15.1 / 裁决 9：正式 AI 的输入 = MatchPublicView + 本人句柄；AI 的类型化可达闭包里不存在 HandLedger / MatchFlow / 种子等能读到私有信息的类型。
        // relic-redesign W1：原"未知信物不可读"随旧信物删除——挂在棋子上的信物与预告全公开（R22），公开视图经盘面信物层与 RelicForecast 给出。
        // 变异验证 M-A1：给 HeuristicTurnController 加 `public HandLedger? Leak { get; init; }` → 红 3（本测试、敌方手牌数量不可读、高难度不越权）。
        //（未用字段的写法会先撞 CS0169 警告即错误，编译期就被拦下。）
        // 变异验证 M-C1（check）：给非根类型 BatchEvaluator 加 `public HandLedger? Leak` 属性（经 CreateEvaluator 返回类型可达）→ 红 3（同上三条），证明闭包确实展开了非根类型的公开成员。
        MatchFlow match = LinkModeFixtures.PlainStarted();
        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0);

        Assert.Empty(AiFixtures.Violations(typeof(HeuristicTurnController)));
        ImmutableHashSet<Type> reachable = AiFixtures.ReachableTypes(typeof(HeuristicTurnController));
        Assert.Contains(typeof(MatchPublicView), reachable);
        Assert.Contains(typeof(Siege.Core.PieceRelics.RelicForecast), reachable);
        Assert.Equal(AiFixtures.P0, ai.CreateEvaluator().Player);
    }

    [Fact]
    public void 敌方手牌数量不可读()
    {
        // 设计文档 §13.1 / §13.2：对手手牌只返回类型集合。公开视图是独立类型，结构上没有数量字段；
        // AI 闭包里唯一的 HandPrivateView 来自本人的 PlayerHandAccess（Deploy/Recruit 递入），不经 HandLedger。
        // 变异验证：M-A1（见上）→ 本测试红；M-A3（见下）钉住公开快照闭包。
        MatchFlow match = LinkModeFixtures.PlainStarted();
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
        MatchFlow match = LinkModeFixtures.PlainStarted();
        match.Debug.SetOrder(AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3);
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("D4"), PieceType.Basic));
        Assert.Equal(1, batch.Count);

        MatchPublicView seenByEnemy = match.Publish();
        Assert.Null(seenByEnemy.Board[TestMaps.At("D4")].Occupant);
        Assert.Equal(TurnStage.Deploy, seenByEnemy.Stage);

        ImmutableHashSet<Type> fromPublicView = AiFixtures.ReachableTypes(typeof(MatchPublicView));
        Assert.DoesNotContain(typeof(StagedBatch), fromPublicView);
        Assert.DoesNotContain(typeof(Placement), fromPublicView);
        Assert.DoesNotContain(typeof(BatchContext), fromPublicView);
    }
}
