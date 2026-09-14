using System.Reflection;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>规格：simulation-harness —— Requirement: 原型调试 AI</summary>
public class 原型调试AITests
{
    [Fact]
    public void 调试AI可读全量状态()
    {
        // 设计文档 §15.3：测试专用模式下可读未揭示信物内容、敌方手牌数量与未确认批次。
        // 变异验证：本类以 M-A21（拒绝入口）与 M-A22（标注）为准；全量读取路径由 M-A5（估计不得使用真实值：调试评价器随真实内容变化）间接钉住。
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Command())]);
        match.Debug.SeedHand(AiFixtures.P1, (PieceType.Basic, 7));
        var runner = new MatchRunner(match);
        DebugTurnController debug = DebugTurnController.Create(runner, AiFixtures.P0, debugMode: true);

        Coord e5 = TestMaps.At("E5");
        Assert.False(match.Relics.IsRevealed(e5));
        Assert.Equal(RelicFixtures.Command(), debug.View.ContentOf(e5));
        Assert.Equal(7, debug.View.HandCountOf(AiFixtures.P1));
        Assert.Equal(7, debug.View.HandOf(AiFixtures.P1).CountOf(PieceType.Basic));

        // 同一时刻的公开视图里这两项都不存在
        MatchPublicView pub = debug.View.Public();
        Assert.Null(pub.Relics.Single(r => r.Coord == e5).Content);
        Assert.DoesNotContain("7", pub.Hands.Single(h => h.Player == AiFixtures.P1).ToString());

        // 对手正在暂放：调试视图可读，公开盘面不可读
        match.Debug.SetOrder(AiFixtures.P1, AiFixtures.P0, AiFixtures.P2, AiFixtures.P3);
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("H2"), PieceType.Basic));
        Assert.Equal(TestMaps.At("H2"), debug.View.CurrentBatch!.Placements.Single().Coord);
        Assert.Null(match.Publish().Board[TestMaps.At("H2")].Occupant);

        // 调试 AI 的估值读的是真实内容
        Assert.Same(debug, runner.ControllerOf(AiFixtures.P0));
        Assert.Equal(RelicEstimate.ValueOf(RelicType.Command), RelicEstimate.ValueOf(debug.View.ContentOf(e5)));
    }

    [Fact]
    public void 面向玩家的对局拒绝调试AI()
    {
        // 裁决 9：构造必须显式 debugMode: true；类型与入口都是 internal、无 public 构造 / 工厂——面向玩家的 Godot 项目在编译期就拿不到。
        // 变异验证 M-A21：Create 去掉 !debugMode 的抛出 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started();
        var runner = new MatchRunner(match);

        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => DebugTurnController.Create(runner, AiFixtures.P0, debugMode: false));
        Assert.Contains("仅供测试", ex.Message);
        Assert.Throws<SiegeRuleException>(() => runner.ControllerOf(AiFixtures.P0));
        Assert.False(runner.Annotations.UsedDebugAi);

        Assert.False(typeof(DebugTurnController).IsPublic);
        Assert.False(typeof(MatchDebugView).IsPublic);
        Assert.Empty(typeof(DebugTurnController).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(MatchDebugView).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(typeof(DebugTurnController).GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.ReturnType == typeof(DebugTurnController));
        Assert.DoesNotContain(typeof(HeuristicAi).GetMethods(), m => m.ReturnType == typeof(DebugTurnController));
    }

    [Fact]
    public void 对局记录标注()
    {
        // 设计文档 §15.3 / design.md D7：用过调试 AI 的局带明确标记，平衡分析据此筛除；纯正式 AI 局不带。
        // 变异验证 M-A22：Create 不调用 MarkDebugAi → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Command())]);
        var runner = new MatchRunner(match);
        runner.AttachAi();
        DebugTurnController.Create(runner, AiFixtures.P2, debugMode: true);
        runner.RunMajorRounds(1);

        Assert.True(runner.Annotations.UsedDebugAi);
        Assert.Equal([AiFixtures.P2], runner.Annotations.DebugAiPlayers);
        Assert.False(runner.Annotations.HasTakeover);
        Assert.Equal(2, match.MajorRound);

        var clean = new MatchRunner(MatchFixtures.Started());
        clean.AttachAi();
        clean.RunMajorRounds(1);
        Assert.False(clean.Annotations.UsedDebugAi);
        Assert.Empty(clean.Annotations.DebugAiPlayers);
    }
}
