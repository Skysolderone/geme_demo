using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>规格：simulation-harness —— Requirement: 随机子流隔离（跑局配置层；子流原语的同名回归在 Determinism/）</summary>
public class 随机子流隔离Tests
{
    [Fact]
    public void 子流互不干扰()
    {
        // 会话 A：默认；会话 B：P0 的一次征募决策改为不选（决策序列变化）；会话 C：多跑一个大回合（recruit 子流消费次数严格更多，钉住前提）。
        // 三者的信物生成结果逐格保持不变；跑局层自己的抽样子流 sim-sample 也不影响对局子流。
        // 变异验证 M-B23：MatchSession.Create 把 MaxMajorRounds 混进种子（seed ^ MaxMajorRounds）→ 红 4（本测试：C 的信物分布与 A 不同；另红 批量执行并汇总、纯AI局可凭种子复现、失败局可复现）。
        // 子流原语层的变异（M-D3：Stream 忽略名字）由 Determinism/随机子流隔离Tests 钉住，这里不重复。
        RunConfig config = SimFixtures.Config(turnLimit: 8);
        MatchSession a = MatchSession.Create(config, 61);
        MatchSession b = MatchSession.Create(config, 61);
        b.SetController(new PlayerId(0), new NoRecruitController(Ai.HeuristicAi.Create(b.Match, new PlayerId(0), Ai.AiDifficulty.Easy)));
        MatchSession c = MatchSession.Create(config with { TurnLimit = 12 }, 61);

        MatchLog logA = a.Run();
        MatchLog logB = b.Run();
        MatchLog logC = c.Run();

        // 前提成立：决策 / 消费次数确实变了
        Assert.NotEqual(logA.Events.Where(e => e.Type == LogEventType.Recruit && e.Player == 0).Select(e => e.Detail), logB.Events.Where(e => e.Type == LogEventType.Recruit && e.Player == 0).Select(e => e.Detail));
        Assert.True(c.Match.Hands.RecruitStreamConsumed > a.Match.Hands.RecruitStreamConsumed);

        // 信物生成逐格不变
        Assert.Equal(a.Match.Relics.Generation, b.Match.Relics.Generation);
        Assert.Equal(a.Match.Relics.Generation, c.Match.Relics.Generation);
        Assert.Equal(a.Match.Relics.Generation.Placements, c.Match.Relics.Generation.Placements);
        Assert.Equal(logA.Header.Relics.Select(r => $"{r.Coord}:{r.Type}:{r.Magnitude}:{r.EmblemPiece}"), logB.Header.Relics.Select(r => $"{r.Coord}:{r.Type}:{r.Magnitude}:{r.EmblemPiece}"));
        Assert.Equal(logA.Header.Relics.Select(r => $"{r.Coord}:{r.Type}:{r.Magnitude}:{r.EmblemPiece}"), logC.Header.Relics.Select(r => $"{r.Coord}:{r.Type}:{r.Magnitude}:{r.EmblemPiece}"));
        Assert.Equal(logA.Header.FirstOrder, logB.Header.FirstOrder);
        Assert.Equal(logA.Header.Zones, logC.Header.Zones);

        // 跑局层抽样子流与对局子流隔离：抽样千分比不同 → 对局内容一致（只有保留策略不同）
        MatchLog d = MatchSession.Create(config with { FullEventSamplePermille = 1000 }, 61).Run();
        Assert.Equal(EventRetention.Full, d.Header.Retention);
        Assert.Equal(SimFixtures.TurnTexts(logA.Turns), SimFixtures.TurnTexts(d.Turns));
        Assert.Equal(logA.Result!.Winners, d.Result!.Winners);

        // 跑局层也只用命名子流：sim-sample 是种子的纯函数
        Assert.Equal(new Siege.Core.Determinism.GameSeed(61).Stream(MatchSession.SampleStream).NextInt(1000), new Siege.Core.Determinism.GameSeed(61).Stream(MatchSession.SampleStream).NextInt(1000));
        Assert.Equal(MatchPhase.InProgress, MatchSession.Create(config, 61).Match.Phase);
    }
}
