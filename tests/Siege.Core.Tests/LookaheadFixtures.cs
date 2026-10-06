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
/// expert-lookahead 的公共夹具：写死的权重与搜索配置、单次决策、4 人棋盘图真实对局上的局面探针（retire-legacy-maps 段 A 之前为 v5）。
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

    /// <summary>
    /// 一层配置（expert-strength D6 / G1）：专家预设关掉多样候选与两层加分（多样补充上限 0、两层权重 0），其余与专家预设相同。
    /// 专家预设退回一层（负责人裁决 2026-09-28，段 B 后）之后它与预设逐字段相同（阈值除外）；仍显式清零两项，使 G1 不随预设改动。
    /// 凡钉一层前瞻行为的既有测试改用它，断言与期望值一字不改；它的走法由 G1 黄金值（<c>专家难度的一层前瞻Tests.一层配置与改动前的专家逐步相同</c>）钉住。
    /// </summary>
    internal static AiSearchConfig OneLayerConfig(int passThreshold = PassThreshold) =>
        AiSearchConfig.Expert with { PassThreshold = passThreshold, DiverseSupplementLimit = 0, TwoPlyWeightPermille = 0 };

    /// <summary>段 B 扩样用的两层权重（千分数）：λ = 1000‰。专家预设已退回一层（负责人裁决 2026-09-28，段 B 后），打开两层加分的测试一律在 <c>Search</c> 里显式给出。</summary>
    internal const int StrengthPermille = 1000;

    /// <summary>段 B 扩样用的多样补充上限：S = 8。专家预设已退回 0，打开多样候选的测试一律显式给出。</summary>
    internal const int StrengthSupplement = 8;

    /// <summary>专家配置另开多样候选与两层加分（其余同 <see cref="ExpertConfig"/>：高难的候选生成 + 前瞻宽度 4，停手阈值显式给出）。</summary>
    internal static AiSearchConfig StrengthConfig(int passThreshold = 0, int permille = StrengthPermille, int supplement = StrengthSupplement, int width = 4) =>
        ExpertConfig(passThreshold, width) with { TwoPlyWeightPermille = permille, DiverseSupplementLimit = supplement };

    /// <summary>
    /// 专家预设显式打开多样候选（S = 8）与两层加分（λ = 1000‰）：段 B 扩样的专家配置。预设退回一层之后，
    /// 原先"跑专家预设、依赖预设带补充 / 两层"的测试改用它，断言不改。
    /// </summary>
    internal static AiSearchConfig ExpandedExpert =>
        AiSearchConfig.Expert with { DiverseSupplementLimit = StrengthSupplement, TwoPlyWeightPermille = StrengthPermille };

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

    /// <summary>
    /// 当前玩家开到部署阶段，按决策起点的公开快照直接建一个前瞻引擎（不做决策）：返回引擎、快照、本人批次上下文与只含决策起点盘面的同形历史。
    /// 两层加分的构造口径（下一手的上下文、单点扫描）在这里逐项取样。
    /// </summary>
    internal static (ExpertLookahead Engine, MatchPublicView View, BatchContext Mine, BoardHistory History) Engine(
        MatchFlow match, AiSearchConfig config, EvaluationWeights? weights = null)
    {
        StagedBatch batch = match.OpenDeploy();
        MatchPublicView view = match.Publish();
        PlayerId me = match.CurrentPlayer!.Value;
        EvaluationWeights w = weights ?? Weights;
        var history = new BoardHistory();
        history.Record(view.BoardSerialized);
        var evaluator = new BatchEvaluator(me, view, w, config.ImmediateOnly);
        return (new ExpertLookahead(me, view, evaluator, w, config, lifeQuery: null, cacheLife: true), view, batch.Context, history);
    }

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

    // ---------- 4 人棋盘图真实对局 ----------

    /// <summary>
    /// 4 人棋盘图上本夹具的小回合截断。v5 上一局标准 AI 整局约 30–40 小回合、0.7 s；
    /// 4 人棋盘图整局 180–200 小回合、约 13 s（retire-legacy-maps 段 A 实测，种子 1–3），整局跑不进单元测试。
    /// 取 24（= 6 个大回合）：每局约 2 s，4 名标准 AI 共 24 次部署决策，与 v5 整局的决策数同一量级。各测试按样本需要另给更短的截断（逐处注明）。
    /// </summary>
    internal const int BoardTurnLimit = 24;

    /// <summary>
    /// 4 人内置棋盘图（<see cref="SimFixtures.Board4"/>）上的一局：玩家配置逐名给出，写死权重 / 阈值 / 冒险概率 / 内容集 / 计分规则，
    /// 截断于 <see cref="BoardTurnLimit"/> 小回合（retire-legacy-maps D1：此前为 v5 整局，不截断）。
    /// 计分规则钉 v1（formation-tiers D2）。本夹具上的黄金值与局面集在 retire-legacy-maps 段 A 重钉。
    /// </summary>
    internal static RunConfig BoardConfig(params PlayerAiConfig[] players) => new()
    {
        MapId = SimFixtures.Board4,
        Players = [.. players.Select(p => p with { Weights = Weights, Search = WithMapCellLimit(p.Search, BoardPlayable.Value) })],
        SeedStart = 1,
        Count = 1,
        TurnLimit = BoardTurnLimit,
        PassThreshold = PassThreshold,
        FlagRisk = 0,
        ContentSet = ContentSet.V2,
        ScoringVersion = Siege.Core.Scoring.ScoringVersion.V1,
        EventRetention = EventRetention.Full,
        FullEventSamplePermille = 0,
    };

    internal static PlayerAiConfig Standard => new() { Difficulty = AiDifficulty.Standard };

    /// <summary><c>难度分级Tests.三档旧难度逐步不变</c> 与 <see cref="FourStandardSeed1"/> 的小回合截断（16，四个大回合；高难每局约 3 s）。</summary>
    internal const int DifficultyTurnLimit = 16;

    /// <summary>
    /// 4 名标准 AI、种子 1 的一局（<see cref="BoardConfig"/>，截断 <see cref="DifficultyTurnLimit"/>）：<c>难度分级Tests.三档旧难度逐步不变</c> 的标准档与 <c>专家前瞻的记录Tests.非专家没有前瞻记录</c>
    /// 钉的是同一局同一个黄金值，共用一次运行（retire-legacy-maps 段 A：4 人棋盘图上一局约 2 s）。返回会话（已跑完）与日志。
    /// </summary>
    internal static readonly Lazy<(MatchSession Session, Siege.Sim.Logging.MatchLog Log)> FourStandardSeed1 = new(() =>
    {
        MatchSession session = MatchSession.Create(BoardConfig(Standard, Standard, Standard, Standard) with { TurnLimit = DifficultyTurnLimit }, 1);
        return (session, session.Run());
    });

    /// <summary>
    /// 段 A 之前的 v5 整局配置（不截断）。retire-legacy-maps 段 A1 只留给"在棋盘图上结论改变、待主会话裁决"的测试，段 B 删 v5 时一并处理。
    /// </summary>
    internal static RunConfig LegacyV5Config(params PlayerAiConfig[] players) =>
        BoardConfig(players) with { MapId = Siege.Core.Board.Maps.FourPlayerBaseMap.Id, TurnLimit = 600, Players = [.. players.Select(p => p with { Weights = Weights })] };

    /// <summary>4 人棋盘图的可落子格数（只算一次）。</summary>
    private static readonly Lazy<int> BoardPlayable = new(() => Siege.Core.Board.Maps.MapCatalog.Resolve(SimFixtures.Board4).PlayableCount);

    /// <summary>4 人棋盘图的可落子格数。</summary>
    internal static int BoardPlayableCells => BoardPlayable.Value;

    /// <summary>
    /// 显式给出的搜索配置若未设候选格上限（0），补上该图的缺省上限（<see cref="AiSearchConfig.DefaultCellLimitFor"/>：可落子格 &gt; 150 取 24）。
    /// retire-legacy-maps 段 A：v5 只有 105 格、缺省上限就是 0，显式配置与未配置的玩家同口径；换到 465 格的棋盘图后，未配置的玩家经
    /// <see cref="AiSearchConfig.ForMap"/> 取 24，显式配置的却全盘枚举——两者不再同口径（"缺省预设的专家与一层配置逐步相同"随之失真），且专家单步耗时放大一个量级。
    /// 补上之后显式与未配置的玩家仍按同一上限生成候选，与三个入口对<b>未显式配置</b>搜索参数的玩家在该图上的实际生效值一致。
    /// 注意这是测试侧的口径归一，<b>不是</b>产品行为：产品里显式给出的 <c>Search</c> 原样生效（<c>MatchSession.AttachConfigured</c>），
    /// <c>CandidateCellLimit</c> 缺省 0 = 不限制，跑局级的 <c>CandidateCellLimit</c> / <c>--cell-limit</c> 也不作用于它——
    /// 用 <c>run --config</c> 给 <c>Players[].Search</c> 而不写候选格上限时，大图上该玩家全盘枚举（retire-legacy-maps 段 A1 检查记录，待主会话裁决）。
    /// </summary>
    internal static AiSearchConfig? WithMapCellLimit(AiSearchConfig? search, int playableCells) =>
        search is { CandidateCellLimit: 0 } s ? s with { CandidateCellLimit = AiSearchConfig.DefaultCellLimitFor(playableCells) } : search;

    /// <summary>
    /// 固定局面集（expert-lookahead D12）：4 人棋盘图（<see cref="BoardConfig"/>，截断 <see cref="BoardTurnLimit"/>）、种子 1–3（或 <paramref name="seeds"/>）、4 名标准 AI 的真实对局中的部署决策，按全局决策序号每 <paramref name="every"/> 个取一个。
    /// 在每个入选局面上调用 <paramref name="probe"/>（对局此刻停在部署阶段），随后由原 AI 照常决策——对局走法与不探针时相同。
    /// </summary>
    /// <remarks>
    /// 规格写的是"玩家 1 的每次部署决策（不少于 30 个）"，但 v5 种子 1–3 上 4 名标准 AI 的对局只有 7 + 7 + 9 = 23 次玩家 1 的部署（段 A 实测），
    /// 达不到 30；这里改取四个座位的全部部署决策按固定间隔抽样（记入段 A 实施记录的待决项）。retire-legacy-maps 段 A 改到 4 人棋盘图、
    /// 截断 24 小回合后，种子 1–3 共 72 次部署决策（每局 24 个小回合各一次）。
    /// </remarks>
    internal static int ProbePositions(int every, Action<MatchFlow, StagedBatch> probe, params ulong[] seeds) =>
        ProbePositionsOn(BoardConfig(Standard, Standard, Standard, Standard), every, probe, seeds);

    /// <summary>
    /// 同 <see cref="ProbePositions"/>，但局面取自给定配置的对局。只给"棋盘图上结论变了、待主会话裁决"的测试暂时钉回 v5 用
    /// （<see cref="LegacyV5Config"/>，retire-legacy-maps 段 A1 报告逐条列出）。
    /// </summary>
    internal static int ProbePositionsOn(RunConfig config, int every, Action<MatchFlow, StagedBatch> probe, params ulong[] seeds)
    {
        int decision = 0;
        int probed = 0;
        foreach (ulong seed in seeds.Length == 0 ? [1, 2, 3] : seeds)
        {
            MatchSession session = MatchSession.Create(config, seed);
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
        // 与对局里的 AI 同口径：显式配置未设候选格上限时补上该图的缺省上限（见 WithMapCellLimit）。
        config = WithMapCellLimit(config, match.Map.PlayableCount)!;
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

    /// <summary>带类型的合成候选：<paramref name="placements"/> 形如 <c>C1:Basic,C2:Fortress</c>，次序即批次内落点次序（贪心接受的次序）。</summary>
    internal static CandidateBatch Typed(string placements, long powerGain) =>
        new([.. placements.Split(',').Select(p => new Placement(Coord.Parse(p.Split(':')[0]), Enum.Parse<PieceType>(p.Split(':')[1])))],
            new EvaluationBreakdown([(BigInteger)powerGain, 0, 0, 0, 0, 0, 0, 0, 0], LookaheadFixtures.Weights));

    internal static CandidateBatch Pass(long supply) =>
        new([], new EvaluationBreakdown([0, 0, 0, 0, 0, 0, (BigInteger)supply, 0, 0], LookaheadFixtures.Weights));

    internal static ImmutableArray<CandidateBatch> List(params CandidateBatch[] items) => [.. items];
}
