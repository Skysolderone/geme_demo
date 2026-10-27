using Siege.Core.Board.Maps;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
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
        // 权重与停手阈值写死为 ai-eye 4.5 定值之前的缺省：前提"C 多消费征募子流"要求对局走满 3 个大回合（默认阈值 80 下种子 61 第 1 大回合全员 Pass；段 D2 改写）。
        RunConfig config = SimFixtures.PinPreCalibration(SimFixtures.Config(turnLimit: 8));
        MatchSession a = MatchSession.Create(config, 61);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        MatchSession b = MatchSession.Create(config, 61);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        b.SetController(new PlayerId(0), new NoRecruitController(Ai.HeuristicAi.Create(
            b.Match, new PlayerId(0), Ai.AiDifficulty.Easy, SimFixtures.PreCalibrationWeights,
            Ai.AiSearchConfig.Easy with { PassThreshold = SimFixtures.PreCalibrationPassThreshold })));
        MatchSession c = MatchSession.Create(config with { TurnLimit = 12 }, 61);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve

        MatchLog logA = a.Run();
        MatchLog logB = b.Run();
        MatchLog logC = c.Run();

        // 前提成立：决策 / 消费次数确实变了
        Assert.NotEqual(logA.Events.Where(e => e.Type == LogEventType.Recruit && e.Player == 0).Select(e => e.Detail), logB.Events.Where(e => e.Type == LogEventType.Recruit && e.Player == 0).Select(e => e.Detail));
        Assert.True(c.Match.Hands.RecruitStreamConsumed > a.Match.Hands.RecruitStreamConsumed);

        // 信物刷新子流不受影响（relic-redesign W1：开局生成随旧信物删除；刷新是盘面的纯函数，这里取同一张开局盘面上的预告，三局逐格相同）
        Assert.Equal(StartForecast(a.Match), StartForecast(b.Match));
        Assert.Equal(StartForecast(a.Match), StartForecast(c.Match));
        Assert.Equal(logA.Header.FirstOrder, logB.Header.FirstOrder);
        Assert.Equal(logA.Header.Zones, logC.Header.Zones);

        // 跑局层抽样子流与对局子流隔离：抽样千分比不同 → 对局内容一致（只有保留策略不同）
        MatchLog d = MatchSession.Create(config with { FullEventSamplePermille = 1000 }, 61).Run();   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        Assert.Equal(EventRetention.Full, d.Header.Retention);
        Assert.Equal(SimFixtures.TurnTexts(logA.Turns), SimFixtures.TurnTexts(d.Turns));
        Assert.Equal(logA.Result!.Winners, d.Result!.Winners);

        // 跑局层也只用命名子流：sim-sample 是种子的纯函数
        Assert.Equal(new Siege.Core.Determinism.GameSeed(61).Stream(MatchSession.SampleStream).NextInt(1000), new Siege.Core.Determinism.GameSeed(61).Stream(MatchSession.SampleStream).NextInt(1000));
        Assert.Equal(MatchPhase.InProgress, MatchSession.Create(config, 61).Match.Phase);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
    }

    [Fact]
    public void 带入子流不扰动其他子流()
    {
        // 规格 Scenario：同一种子下分别以 --carry-in 0 与 --carry-in 5 创建对局 → 两局的信物刷新预告逐格相同，第 1 大回合行动顺序相同，征募子流未被消费。
        // AI 带入走独立子流 carry-ai（GameSeed.Stream 按名派生，互不消费），所以本条在结构上成立；这里钉的是跑局入口没有绕开子流去动其他随机源。
        // 样本口径下界：--carry-in 5 的局确实每人带了 5 件（relic-redesign W2：带入件不改开局手牌，原"开局手牌不同"的下界随补给删除）。
        RunConfig config = SimFixtures.Config(turnLimit: 4);
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            MatchSession off = MatchSession.Create(config with { CarryIn = 0 }, seed);
            MatchSession on = MatchSession.Create(config with { CarryIn = 5 }, seed);

            Assert.False(off.Match.CarryInOut);
            Assert.Empty(off.Match.CarryIns);
            Assert.True(on.Match.CarryInOut);
            Assert.Equal(4, on.Match.CarryIns.Count);
            Assert.All(on.Match.CarryIns.Values, l => Assert.Equal(5, l.Count));
            Assert.Equal(StartForecast(off.Match), StartForecast(on.Match));
            Assert.Equal(off.Match.ActionOrder.Select(p => p.Value), on.Match.ActionOrder.Select(p => p.Value));
            Assert.Equal(1, off.Match.MajorRound);
            Assert.Equal(1, on.Match.MajorRound);
            Assert.Equal(off.Match.Hands.Export().RecruitConsumed, on.Match.Hands.Export().RecruitConsumed);
        }
    }

    /// <summary>开局盘面（空盘）上第 3 大回合的信物预告：只由对局种子与地图决定（子流 relic-spawn:4）。</summary>
    private static string StartForecast(MatchFlow match) =>
        string.Join(",", RelicSpawner.Forecast(GameBoard.Load(match.Board.BaseMap), new BirthBoardArea(match.Board.BaseMap), match.Seed, 3, match.Players.Length, 0)!.Slots);
}
