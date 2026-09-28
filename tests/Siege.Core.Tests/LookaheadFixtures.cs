using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Sim.Config;
using Siege.Sim.Running;

namespace Siege.Core.Tests;

/// <summary>
/// expert-lookahead 的公共夹具：写死的权重与搜索配置、单次决策、v5 真实对局上的局面探针。
/// 依赖 AI 实际走法的断言一律写死权重（testing.md「依赖 AI 实际怎么走的断言要把权重写死」），不读 <see cref="EvaluationWeights.Default"/>。
/// </summary>
internal static class LookaheadFixtures
{
    internal static readonly PlayerId P0 = MatchFixtures.P0;
    internal static readonly PlayerId P1 = MatchFixtures.P1;
    internal static readonly PlayerId P2 = MatchFixtures.P2;
    internal static readonly PlayerId P3 = MatchFixtures.P3;

    /// <summary>写死的评价权重：取段 A 开工时（HEAD 03d45f6）的缺省值。</summary>
    internal static readonly EvaluationWeights Weights =
        new(PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 35, Growth: 4, Initiative: 20, Supply: 2, Eye: 200, Threat: 25);

    /// <summary>写死的停手阈值（= expert-lookahead 段 A 开工时的 <see cref="AiSearchConfig.DefaultPassThreshold"/>；v2-recalibration 段 A 把缺省改为 20 之后仍写死 80，依赖它的黄金值一字不改）。</summary>
    internal const int PassThreshold = 80;

    /// <summary>专家配置：高难的候选生成 + 前瞻宽度（不经 <see cref="AiSearchConfig.Expert"/> 预设，预设另有测试钉住）。</summary>
    internal static AiSearchConfig ExpertConfig(int passThreshold = 0, int width = 4) =>
        AiSearchConfig.Hard with { PassThreshold = passThreshold, LookaheadWidth = width };

    /// <summary>高难配置。</summary>
    internal static AiSearchConfig HardConfig(int passThreshold = 0) => AiSearchConfig.Hard with { PassThreshold = passThreshold };

    /// <summary>当前玩家开到部署阶段，以给定难度与配置在对局自己的批次上做一次部署决策（决策后批次里是被选批次）。</summary>
    internal static HeuristicTurnController Decide(MatchFlow match, AiDifficulty difficulty, AiSearchConfig config)
    {
        StagedBatch batch = match.OpenDeploy();
        HeuristicTurnController ai = HeuristicAi.Create(match, match.CurrentPlayer!.Value, difficulty, Weights, config);
        ai.Deploy(batch, match.Rehearse);
        return ai;
    }

    internal static HeuristicTurnController DecideExpert(MatchFlow match, int passThreshold = 0, int width = 4) =>
        Decide(match, AiDifficulty.Expert, ExpertConfig(passThreshold, width));

    internal static HeuristicTurnController DecideHard(MatchFlow match, int passThreshold = 0) =>
        Decide(match, AiDifficulty.Hard, HardConfig(passThreshold));

    /// <summary>专家的前瞻记录（必须存在）。</summary>
    internal static LookaheadRecord Record(HeuristicTurnController ai) =>
        ai.LastLookahead ?? throw new InvalidOperationException("该控制者没有前瞻记录。");

    /// <summary>批次键里的落点坐标（键形如 <c>E5:Basic,F5:Basic</c>）。</summary>
    internal static Coord[] CoordsOf(string key) =>
        key.Length == 0 ? [] : [.. key.Split(',').Select(p => Coord.Parse(p.Split(':')[0]))];

    /// <summary>当前玩家开到部署阶段、摆上指定普通子并预演，返回预演结果（批次保留在对局里）。</summary>
    internal static RehearsalResult Rehearse(MatchFlow match, params string[] cells)
    {
        StagedBatch batch = match.OpenDeploy();
        return match.RehearseBatch(batch, [.. cells.Select(c => (c, PieceType.Basic))]);
    }

    // ---------- v5 真实对局 ----------

    /// <summary>v5 上的一局：玩家配置逐名给出，写死权重 / 阈值 / 冒险概率 / 内容集，不截断。</summary>
    internal static RunConfig V5Config(params PlayerAiConfig[] players) => new()
    {
        Players = [.. players.Select(p => p with { Weights = Weights })],
        SeedStart = 1,
        Count = 1,
        TurnLimit = 600,
        PassThreshold = PassThreshold,
        FlagRisk = 0,
        ContentSet = ContentSet.V2,
        EventRetention = EventRetention.Full,
        FullEventSamplePermille = 0,
    };

    internal static PlayerAiConfig Standard => new() { Difficulty = AiDifficulty.Standard };

    /// <summary>
    /// 固定局面集（expert-lookahead D12）：<c>siege-4p-base-v5</c>、种子 1–3（或 <paramref name="seeds"/>）、4 名标准 AI 的真实对局中的部署决策，按全局决策序号每 <paramref name="every"/> 个取一个。
    /// 在每个入选局面上调用 <paramref name="probe"/>（对局此刻停在部署阶段），随后由原 AI 照常决策——对局走法与不探针时相同。
    /// </summary>
    /// <remarks>
    /// 规格写的是"玩家 1 的每次部署决策（不少于 30 个）"，但 v5 种子 1–3 上 4 名标准 AI 的对局只有 7 + 7 + 9 = 23 次玩家 1 的部署（段 A 实测），
    /// 达不到 30；这里改取四个座位的全部部署决策（共 92 次）按固定间隔抽样（记入段 A 实施记录的待决项）。
    /// </remarks>
    internal static int ProbePositions(int every, Action<MatchFlow, StagedBatch> probe, params ulong[] seeds)
    {
        int decision = 0;
        int probed = 0;
        foreach (ulong seed in seeds.Length == 0 ? [1, 2, 3] : seeds)
        {
            MatchSession session = MatchSession.Create(V5Config(Standard, Standard, Standard, Standard), seed);
            foreach (PlayerId player in session.Match.Players)
            {
                HeuristicTurnController inner = session.AiOf(player)!;
                session.SetController(player, new ProbeController(inner, batch =>
                {
                    if (decision++ % every == 0)
                    {
                        probe(session.Match, batch);
                        probed++;
                    }
                }));
            }

            session.Run();
        }

        return probed;
    }

    /// <summary>
    /// 在对局的部署局面上另起一个批次（不动对局自己的批次：暂放留痕不随清空而清空），以给定配置做一次独立决策。
    /// 每次决策都从种子重新派生 <c>ai-&lt;玩家&gt;</c> 子流（<see cref="Siege.Core.Determinism.GameSeed.Stream"/> 每次从头开始），高难与专家的扰动序列因而对得上。
    /// </summary>
    internal static (HeuristicTurnController Ai, int Rehearsals, Siege.Core.Determinism.RandomStream Stream) Shadow(
        MatchFlow match, BatchContext context, AiDifficulty difficulty, AiSearchConfig config)
    {
        var batch = new StagedBatch(match.Board, context);
        int rehearsals = 0;
        Siege.Core.Determinism.RandomStream stream = match.Seed.Stream(HeuristicAi.StreamName(context.Player));
        var ai = new HeuristicTurnController(context.Player, match.Publish, stream, difficulty, Weights, config);
        ai.Deploy(batch, () =>
        {
            rehearsals++;
            return BatchRehearsal.Rehearse(match.Board, context, batch.Placements, match.History);
        });
        return (ai, rehearsals, stream);
    }
}

/// <summary>包一层原 AI：部署前先调用探针，再交给原 AI；其余阶段直接转交。</summary>
internal sealed class ProbeController(HeuristicTurnController inner, Action<StagedBatch> beforeDeploy) : ITurnController
{
    public HeuristicTurnController Inner => inner;

    public void OrganizeHand(PlayerHandAccess hand, int overflow) => inner.OrganizeHand(hand, overflow);

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel) => inner.Recruit(hand, panel);

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        beforeDeploy(batch);
        inner.Deploy(batch, rehearse);
    }

    public bool OnRejected(StagedBatch batch, BatchFailure failure) => inner.OnRejected(batch, failure);
}

/// <summary>包一层原 AI：每次部署后记下它的前瞻记录文本（无记录记为 "-"）。</summary>
internal sealed class LookaheadRecorder(HeuristicTurnController inner) : ITurnController
{
    public List<string> Records { get; } = [];

    public List<LookaheadRecord> Raw { get; } = [];

    public void OrganizeHand(PlayerHandAccess hand, int overflow) => inner.OrganizeHand(hand, overflow);

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel) => inner.Recruit(hand, panel);

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        inner.Deploy(batch, rehearse);
        Records.Add(inner.LastLookahead?.ToText() ?? "-");
        if (inner.LastLookahead is { } record)
        {
            Raw.Add(record);
        }
    }

    public bool OnRejected(StagedBatch batch, BatchFailure failure) => inner.OnRejected(batch, failure);

    public IReadOnlyList<string> Decisions => inner.Decisions;
}

/// <summary>合成候选（前瞻集 / 选择规则的单元测试用）：给定落点与九维原始值。</summary>
internal static class SyntheticCandidates
{
    internal static CandidateBatch Of(string cells, long powerGain) =>
        new([.. LookaheadFixtures.CoordsOf(cells).Select(c => new Placement(c, PieceType.Basic))],
            new EvaluationBreakdown([(BigInteger)powerGain, 0, 0, 0, 0, 0, 0, 0, 0], LookaheadFixtures.Weights));

    internal static CandidateBatch Pass(long supply) =>
        new([], new EvaluationBreakdown([0, 0, 0, 0, 0, 0, (BigInteger)supply, 0, 0], LookaheadFixtures.Weights));

    internal static ImmutableArray<CandidateBatch> List(params CandidateBatch[] items) => [.. items];
}
