using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>规格：simulation-harness —— Requirement: 人工接管</summary>
public class 人工接管Tests
{
    [Fact]
    public void 任意阶段接管()
    {
        // 设计文档 §15.3：P1 的整理手牌阶段请求接管 → 征募与部署改由人工做出；P1 的 AI 本回合没有征募 / 部署决策；其他玩家不受影响。
        // 变异验证 M-A23：TakeOver 不调用 SetController → 红 2（本测试 + 交还后继续）。
        // 变异验证 M-C3（check）：TakeOver 不写 _suspended → 红 3（本测试 IsTakenOver 为假、交还后继续、接管记录可追溯 两者 HandBack 抛出）。
        MatchFlow match = MatchFixtures.Started();
        match.Debug.SetOrder(AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3);
        var runner = new MatchRunner(match);
        ImmutableSortedDictionary<PlayerId, HeuristicTurnController> ais = runner.AttachAi();
        var manual = new ScriptedController("H2");
        runner.SetController(AiFixtures.P1, new TakeoverTrigger(runner, AiFixtures.P1, ais[AiFixtures.P1], manual));

        runner.RunTurn(); // P0（AI）
        Assert.False(runner.IsTakenOver(AiFixtures.P1));
        runner.RunTurn(); // P1：整理手牌阶段触发接管 → 征募、部署由 manual 完成

        Assert.True(runner.IsTakenOver(AiFixtures.P1));
        Assert.Same(manual, runner.ControllerOf(AiFixtures.P1));
        Assert.Equal(0, manual.OrganizeCalls);
        Assert.Equal(1, manual.RecruitCalls);
        Assert.Equal(1, manual.DeployCalls);
        Assert.Empty(ais[AiFixtures.P1].Decisions);
        Assert.Equal(AiFixtures.P1, match.Board[TestMaps.At("H2")].Occupant!.Value.Owner);
        Assert.Equal(AiFixtures.P2, match.CurrentPlayer);

        TakeoverRecord record = Assert.Single(runner.Annotations.Takeovers);
        Assert.Equal((AiFixtures.P1, 1, TurnStage.OrganizeHand, TakeoverKind.TakenOver), (record.Player, record.MajorRound, record.Stage, record.Kind));

        // 其他玩家仍由各自的 AI 决策
        runner.RunTurn();
        runner.RunTurn();
        Assert.NotEmpty(ais[AiFixtures.P0].Decisions);
        Assert.NotEmpty(ais[AiFixtures.P2].Decisions);
        Assert.NotEmpty(ais[AiFixtures.P3].Decisions);
        Assert.Throws<SiegeRuleException>(() => runner.TakeOver(AiFixtures.P1, manual));
    }

    [Fact]
    public void 交还后继续()
    {
        // 接管一个小回合后交还：后续小回合恢复 AI 决策，对局正常继续到终局；接管前后存档 → 恢复的状态逐字段一致（implement 5.3）。
        // 变异验证 M-A24：HandBack 不恢复原控制者（SetController 留空）→ 红 1（本测试，P1 的 AI 决策数不增长）。
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Command())]);
        match.Debug.SetOrder(AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3);
        var runner = new MatchRunner(match);
        ImmutableSortedDictionary<PlayerId, HeuristicTurnController> ais = runner.AttachAi();
        HeuristicTurnController aiP1 = ais[AiFixtures.P1];

        runner.RunTurn(); // P0
        string saveBefore = match.Serialize();
        runner.TakeOver(AiFixtures.P1, new ScriptedController("G2", "H3"));
        runner.RunTurn(); // P1 人工
        Assert.Equal(AiFixtures.P1, match.Board[TestMaps.At("G2")].Occupant!.Value.Owner);
        Assert.Empty(aiP1.Decisions);
        runner.HandBack(AiFixtures.P1);
        Assert.False(runner.IsTakenOver(AiFixtures.P1));
        Assert.Same(aiP1, runner.ControllerOf(AiFixtures.P1));

        // 接管后的存档可恢复，且逐字段一致（含 ImmutableArray 的 record 投影成文本比）
        string saveAfter = match.Serialize();
        Assert.NotEqual(saveBefore, saveAfter);
        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Relics.Generation, saveAfter);
        Assert.Equal(match.Board.Serialize(), restored.Board.Serialize());
        Assert.Equal(AiFixtures.PowerText(match), AiFixtures.PowerText(restored));
        Assert.Equal(AiFixtures.FlowText(match), AiFixtures.FlowText(restored));
        Assert.Equal(match.Hands.Debug.PrivateViewOf(AiFixtures.P1), restored.Hands.Debug.PrivateViewOf(AiFixtures.P1));
        Assert.Equal(saveAfter, restored.Serialize());

        // 交还后 P1 恢复 AI 决策，对局正常继续（再跑 4 个大回合；不跑到终局，终局动态属于跑局层的实测项）
        runner.RunMajorRounds(4);
        Assert.NotEmpty(aiP1.Decisions);
        Assert.Contains(aiP1.Decisions, d => d.StartsWith("D:", StringComparison.Ordinal));
        Assert.True(match.MajorRound >= 5 || match.Phase == MatchPhase.Ended, $"第 {match.MajorRound} 大回合 / {match.Phase}");
        Assert.Equal(match.CountEvents(FlowEventKind.TurnStarted), match.CountEvents(FlowEventKind.TurnEnded));
        // 恢复传开局地图（Board.BaseMap），不传活地形 match.Map：存档记的是开局地图摘要，地形改造经盘面序列化的改造段往返
        // （与 TerrainEditing/同形与存档纳入设施Tests 同一约定）。restore-go-core-rules 段 D 删除名次征募加成后，这 4 个大回合里出现了地形改造，
        // 原先传 match.Map 的写法才暴露为"地图不一致"。
        Assert.Equal(match.Serialize(), MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Relics.Generation, match.Serialize()).Serialize());
        Assert.Throws<SiegeRuleException>(() => runner.HandBack(AiFixtures.P1));
    }

    [Fact]
    public void 接管记录可追溯()
    {
        // 设计文档 §15.3 / design.md D7：对局记录标注每次接管与交还的玩家、大回合与阶段。
        // 变异验证 M-A25：HandBack 不记录事件 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started();
        match.Debug.SetOrder(AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3);
        var runner = new MatchRunner(match);
        runner.AttachAi();
        Assert.False(runner.Annotations.HasTakeover);

        runner.RunMajorRounds(1);
        Assert.Equal(2, match.MajorRound);
        runner.TakeOver(AiFixtures.P3, new ScriptedController());
        runner.RunMajorRounds(2);
        Assert.Equal(3, match.MajorRound);
        // 第 3 大回合首位玩家（按先手值生成，不一定是 P0）的小回合进行到部署阶段时交还 P3
        match.BeginTurn();
        match.EnterRecruit();
        match.EnterDeploy();
        Assert.Equal(TurnStage.Deploy, match.Stage);
        Assert.NotEqual(AiFixtures.P3, match.CurrentPlayer);
        runner.HandBack(AiFixtures.P3);
        match.Pass();

        Assert.True(runner.Annotations.HasTakeover);
        Assert.Equal(2, runner.Annotations.Takeovers.Count);
        Assert.Equal("#1 R2 Idle P3 TakenOver", runner.Annotations.Takeovers[0].ToString());
        Assert.Equal("#2 R3 Deploy P3 HandedBack", runner.Annotations.Takeovers[1].ToString());
        Assert.False(runner.Annotations.UsedDebugAi);
    }
}
