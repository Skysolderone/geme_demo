using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using Xunit.Abstractions;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision（expert-lookahead）—— Requirement: 专家前瞻的确定性与耗时</summary>
public class 专家前瞻的确定性与耗时Tests(ITestOutputHelper output)
{
    /// <summary>v5、种子 1、整局：P1 为专家（预设、写死阈值 80），其余三名标准；返回日志确定性文本与 P1 每次部署的前瞻记录文本。</summary>
    internal static (string Text, List<string> Records, List<LookaheadRecord> Raw) RunExpertSeat1(bool cacheLife)
    {
        MatchSession session = MatchSession.Create(
            V5Config(Standard, new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = AiSearchConfig.Expert with { PassThreshold = PassThreshold } }, Standard, Standard), 1);
        HeuristicTurnController inner = HeuristicAi.Create(session.Match, P1, AiDifficulty.Expert, Weights,
            AiSearchConfig.Expert with { PassThreshold = PassThreshold }, lifeQuery: null, cacheLife);
        var recorder = new LookaheadRecorder(inner);
        session.SetController(P1, recorder);
        MatchLog log = session.Run();
        Assert.Null(log.Failure);
        return (log.DeterministicText(), recorder.Records, recorder.Raw);
    }

    private static readonly Lazy<(string Text, List<string> Records, List<LookaheadRecord> Raw)> CachedRun = new(() => RunExpertSeat1(cacheLife: true));

    [Fact]
    public void 同局面同决策()
    {
        // 用相同种子与配置重放一局含专家的对局 → 专家每一步的前瞻集、下一名对手、模拟回应、前瞻后分数与选择逐项相同。
        // 样本口径：至少一次已前瞻、至少一次模拟回应非 Pass。
        (string text, List<string> records, List<LookaheadRecord> raw) = CachedRun.Value;
        (string again, List<string> recordsAgain, _) = RunExpertSeat1(cacheLife: true);

        Assert.True(records.Count >= 5, $"专家只有 {records.Count} 次部署");
        Assert.Contains(raw, r => r.Status == LookaheadStatus.Applied && r.Entries.Any(e => !string.IsNullOrEmpty(e.ResponseKey)));
        Assert.Equal(records, recordsAgain);
        Assert.Equal(text, again);
        Assert.True(text.Split('\n').Length > 100);
    }

    [Fact]
    public void 缓存开关不改变专家决策()
    {
        // 同一配置与种子下，分别开、关决策内活形缓存跑一局含专家的对局 → 两份日志的确定性文本逐行相同，前瞻记录逐条相同。
        // 模拟对手沿用专家的缓存开关（它的评价器同样是决策内新建）。
        (string text, List<string> records, _) = CachedRun.Value;
        (string off, List<string> recordsOff, _) = RunExpertSeat1(cacheLife: false);

        Assert.Equal(text.Split('\n'), off.Split('\n'));
        Assert.Equal(records, recordsOff);
        Assert.Contains(records, r => r.StartsWith(nameof(LookaheadStatus.Applied), StringComparison.Ordinal));
        // expert-strength：专家预设下多样补充与两层加分确实参与了这局（记录文本里有非零的补充 / 两层计数）。
        Assert.Contains(records, r => r.Contains(" two=", StringComparison.Ordinal));
        Assert.Contains(records, r => r.Contains(" sup=", StringComparison.Ordinal));
    }

    [Fact]
    public void 不消费新随机()
    {
        // 同一局面分别由专家与高难做一次部署决策：两者对 ai-<玩家> 子流的消费次数相同（前瞻不消费随机，D5 (a)）；
        // 前瞻组件的类型闭包里没有随机流与种子——对局中不会出现新的子流名，其余子流的位置无从被改动。
        // 局面取 v5 种子 1 上一局 4 名标准 AI 对局的部署局面，每 3 个取 1（与耗时代理同一探针）。
        // 变异 M-A6c（模拟对手用专家的扰动流做完整的标准 M = 8）、M-A9（前瞻里消费一次 ai-<玩家>）→ 见段 A 实施记录。
        // expert-strength：专家取新预设（多样补充上限 8、两层权重 1000‰）——排除重跑与两层扫描同样不消费随机；
        // 前瞻集的第一个逐局面等于高难的选择（「前瞻集第一个仍是高难的选择」的真实局面取样）。样本口径：确有局面做过排除重跑、做过两层扫描。
        // 变异 E-D5 / E-R1（排除重跑用扰动排序 / 两层扫描消费 ai-<玩家>）→ 见 expert-strength 段 A 实施记录。
        int compared = 0;
        int applied = 0;
        int supplemented = 0;
        int scanned = 0;
        ProbePositions(every: 3, (match, batch) =>
        {
            (HeuristicTurnController hard, _, RandomStream hardStream) = Shadow(match, batch.Context, AiDifficulty.Hard, AiSearchConfig.Hard);
            (HeuristicTurnController expert, _, RandomStream expertStream) = Shadow(match, batch.Context, AiDifficulty.Expert, AiSearchConfig.Expert);
            Assert.Equal(hardStream.Consumed, expertStream.Consumed);
            Assert.Equal(hard.LastCandidates.Select(c => c.Key), expert.LastCandidates.Select(c => c.Key));
            LookaheadRecord record = expert.LastLookahead!;
            if (!hard.LastChoice!.IsPass)
            {
                Assert.Equal(hard.LastChoice.Key, record.Entries[0].CandidateKey);
            }

            compared++;
            if (record.Status == LookaheadStatus.Applied && hardStream.Consumed > 0)
            {
                applied++;
            }

            supplemented += record.SupplementRehearsals > 0 ? 1 : 0;
            scanned += record.TwoPlyRehearsals > 0 ? 1 : 0;
        }, 1);

        Assert.True(compared >= 8, $"只比较了 {compared} 个局面");
        Assert.True(applied >= 3, $"只有 {applied} 个局面真的前瞻过且消费过扰动");
        Assert.True(supplemented >= 1, "没有局面做过排除重跑");
        Assert.True(scanned >= 1, "没有局面做过两层扫描");
        ImmutableHashSet<Type> closure = AiFixtures.ReachableTypes(typeof(ExpertLookahead));
        Assert.DoesNotContain(typeof(RandomStream), closure);
        Assert.DoesNotContain(typeof(GameSeed), closure);
    }

    [Fact]
    public void 预演次数代理计入新增部分()
    {
        // D12 / expert-strength D8 确定性代理（默认套件）：v5 种子 1–3、4 名标准 AI 对局的固定局面集（四个座位的全部部署决策每 3 个取 1，见 LookaheadFixtures.ProbePositions 的说明），
        // 统计专家（预设：多样补充上限 8、两层权重 1000‰）与高难每次决策的预演次数，断言总次数之比 ≤ 4，并输出总比值与三部分占比。
        // 专家一方 = 经 rehearse 委托的预演（单点排序、贪心组批、多样补充的排除重跑、候选 B1）+ 模拟对手的预演 + 两层扫描的单点预演。
        // 计时口径见 专家前瞻耗时计时Tests。
        long hardTotal = 0;
        long expertTotal = 0;
        long supplement = 0;
        long simulated = 0;
        long twoPly = 0;
        var rounds = new List<int>();
        int positions = ProbePositions(every: 3, (match, batch) =>
        {
            (_, int hardRehearsals, _) = Shadow(match, batch.Context, AiDifficulty.Hard, AiSearchConfig.Hard);
            (HeuristicTurnController expert, int expertRehearsals, _) = Shadow(match, batch.Context, AiDifficulty.Expert, AiSearchConfig.Expert);
            LookaheadRecord record = expert.LastLookahead!;
            hardTotal += hardRehearsals;
            expertTotal += expertRehearsals + record.SimulatedRehearsals + record.TwoPlyRehearsals;
            supplement += record.SupplementRehearsals;
            simulated += record.SimulatedRehearsals;
            twoPly += record.TwoPlyRehearsals;
            rounds.Add(match.MajorRound);
        });

        double ratio = (double)expertTotal / hardTotal;
        output.WriteLine($"局面 {positions} 个（第 1–3 大回合 {rounds.Count(r => r <= 3)}、第 4 大回合以后 {rounds.Count(r => r >= 4)}）；预演次数 高难 {hardTotal}、专家 {expertTotal}，比值 {ratio:F3}");
        output.WriteLine($"专家新增部分：多样补充 {supplement}（{100.0 * supplement / expertTotal:F1}%）、模拟对手 {simulated}（{100.0 * simulated / expertTotal:F1}%）、两层扫描 {twoPly}（{100.0 * twoPly / expertTotal:F1}%）");
        Assert.True(positions >= 30, $"局面只有 {positions} 个");
        Assert.Contains(rounds, r => r <= 3);
        Assert.Contains(rounds, r => r >= 4);
        Assert.True(supplement > 0, "多样补充的预演未被计入");
        Assert.True(simulated > 0, "模拟对手的预演未被计入");
        Assert.True(twoPly > 0, "两层扫描的预演未被计入");
        Assert.True(expertTotal > hardTotal + simulated + twoPly, "专家的预演次数应多于高难加上模拟对手与两层扫描（多样补充确实经 rehearse 被计入）");
        Assert.True(ratio <= 4.0, $"预演次数比值 {ratio:F3} 超过 4");
    }
}
