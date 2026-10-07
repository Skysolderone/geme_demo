using System.Diagnostics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Tests.LifeShape;
using Siege.Sim.Config;
using Siege.Sim.Running;
using Xunit.Abstractions;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision（ai-turn-speed）—— Requirement: 决策序列基线与耗时 / Scenario: 大图耗时下降（计时口径，design D3：耗时只进报告、不进断言）。
/// Release、同一进程：<c>board:1</c> 种子 1、4 名标准 AI 的真实对局，先跑到第 8 大回合（兼作预热），再对接下来 ≥ 12 个小回合各计一次
/// 控制者三个阶段（整理手牌 / 征募 / 部署，含被拒补救）的合计耗时，报中位数 / p90 / 最大；随后 4 人内置棋盘图种子 1 的前 24 个小回合，报单个小回合中位数
/// （retire-legacy-maps 段 A2：原为 v5 种子 1 整局，v5 删除）。
/// 决策不随优化改变（<c>决策序列基线Tests</c> 守住），所以改前改后量的是同一局面集。
/// </summary>
/// <remarks>不进默认套件：设 <c>SIEGE_PERF=1</c> 后按 <c>--filter "Category=Perf&amp;FullyQualifiedName~AI小回合耗时"</c> 单独跑（testing.md「慢测试与计时测试」）。</remarks>
[Collection(nameof(AI小回合耗时Tests))]
public class AI小回合耗时Tests(ITestOutputHelper output)
{
    private const int MidgameRound = 8;
    private const int MidgameSamples = 12;

    [PerfTheory]
    [Trait("Category", "Perf")]
    [InlineData("board:1", 1UL)]
    public void 大图中盘与内置棋盘图开局单个小回合耗时(string mapId, ulong seed)
    {
        List<long> midgame = TimeTurns(mapId, seed, session => session.Match.MajorRound >= MidgameRound, MidgameSamples);
        List<long> v5 = TimeTurns(SimFixtures.Board4, seed, _ => true, 24);

        output.WriteLine($"{mapId} 种子 {seed} 第 {MidgameRound} 大回合起 {midgame.Count} 个小回合：中位 {Percentile(midgame, 0.5):F0} ms，p90 {Percentile(midgame, 0.9):F0} ms，最大 {Percentile(midgame, 1.0):F0} ms");
        output.WriteLine($"  样本（ms）：{string.Join(", ", midgame.Select(t => (t * 1000.0 / Stopwatch.Frequency).ToString("F0")))}");
        output.WriteLine($"{SimFixtures.Board4} 种子 {seed} 前 {v5.Count} 个小回合：中位 {Percentile(v5, 0.5):F1} ms，p90 {Percentile(v5, 0.9):F1} ms，最大 {Percentile(v5, 1.0):F1} ms");
        Assert.True(midgame.Count >= MidgameSamples, $"中盘样本只有 {midgame.Count} 个");
    }

    /// <summary>跑一局：<paramref name="from"/> 为真起开始采样，采满 <paramref name="count"/> 个小回合或到终局为止；每个样本是该小回合控制者各阶段的合计 tick。</summary>
    private static List<long> TimeTurns(string mapId, ulong seed, Func<MatchSession, bool> from, int count)
    {
        var config = new RunConfig { MapId = mapId, SeedStart = seed, Count = 1, TurnLimit = 0 };
        MatchSession session = MatchSession.Create(config, seed);
        var samples = new List<long>();
        var timers = new Dictionary<PlayerId, TimingController>();
        foreach (PlayerId player in session.Match.Players)
        {
            var timer = new TimingController(session.AiOf(player)!);
            timers[player] = timer;
            session.SetController(player, timer);
        }

        while (session.Match.Phase == MatchPhase.InProgress && samples.Count < count)
        {
            bool sample = from(session);
            PlayerId player = session.Match.CurrentPlayer!.Value;
            timers[player].Elapsed = 0;
            if (!session.RunTurn())
            {
                break;
            }

            if (sample)
            {
                samples.Add(timers[player].Elapsed);
            }
        }

        return samples;
    }

    private static double Percentile(List<long> ticks, double q)
    {
        long[] sorted = [.. ticks.Order()];
        int i = Math.Min(sorted.Length - 1, (int)Math.Ceiling(q * sorted.Length) - 1);
        return sorted[Math.Max(0, i)] * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>包一层原 AI：累计四个控制者入口的耗时（tick）。</summary>
    private sealed class TimingController(HeuristicTurnController inner) : ITurnController
    {
        public long Elapsed { get; set; }

        public void OrganizeHand(PlayerHandAccess hand, int overflow) => Time(() => inner.OrganizeHand(hand, overflow));

        public void Recruit(PlayerHandAccess hand, RecruitPanelView panel) => Time(() => inner.Recruit(hand, panel));

        public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse) => Time(() => inner.Deploy(batch, rehearse));

        public bool OnRejected(StagedBatch batch, BatchFailure failure)
        {
            bool result = false;
            Time(() => result = inner.OnRejected(batch, failure));
            return result;
        }

        private void Time(Action action)
        {
            long start = Stopwatch.GetTimestamp();
            action();
            Elapsed += Stopwatch.GetTimestamp() - start;
        }
    }
}

/// <summary>计时测试不与其他测试类并行。</summary>
[CollectionDefinition(nameof(AI小回合耗时Tests), DisableParallelization = true)]
public sealed class AI小回合耗时Collection
{
}
