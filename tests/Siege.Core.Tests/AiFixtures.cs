using System.Collections.Immutable;
using System.Reflection;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests;

/// <summary>AI 测试的公共夹具：可达类型闭包（信息边界守门）、局面搭建、决策日志投影、脚本化的人工控制者。</summary>
internal static class AiFixtures
{
    internal static readonly PlayerId P0 = MatchFixtures.P0;
    internal static readonly PlayerId P1 = MatchFixtures.P1;
    internal static readonly PlayerId P2 = MatchFixtures.P2;
    internal static readonly PlayerId P3 = MatchFixtures.P3;

    /// <summary>
    /// 正式 AI 的可达类型闭包中 MUST NOT 出现的类型：账本本体、对局本体、调试接缝、生成记录（含真实内容）、种子（可重算 relic-gen）。
    /// </summary>
    internal static readonly Type[] ForbiddenForOfficialAi =
    [
        typeof(HandLedger),
        typeof(RelicLedger),
        typeof(MatchFlow),
        typeof(MatchRunner),
        typeof(MatchDebugAccess),
        typeof(HandDebugAccess),
        typeof(MatchDebugView),
        typeof(DebugTurnController),
        typeof(RelicGenerationRecord),
        typeof(RelicPlacement),
        typeof(RelicGenerator),
        typeof(GameSeed),
        typeof(HandLedgerState),
        typeof(RelicLedgerState),
    ];

    /// <summary>
    /// 从 <paramref name="root"/> 出发、经<b>类型化 API</b> 可达的 Siege.Core 类型闭包：根类型取全部实例字段（含私有）与全部构造参数；
    /// 其余类型只取公开实例字段 / 属性 / 方法的返回与参数类型。泛型参数、数组元素、可空、委托签名一律展开。
    /// </summary>
    internal static ImmutableHashSet<Type> ReachableTypes(Type root)
    {
        Assembly core = typeof(GameBoard).Assembly;
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>();

        void Visit(Type t)
        {
            if (t.IsByRef || t.IsArray || t.IsPointer)
            {
                Visit(t.GetElementType()!);
                return;
            }

            if (t.IsGenericType)
            {
                foreach (Type arg in t.GetGenericArguments())
                {
                    Visit(arg);
                }
            }

            if (t.IsGenericParameter || t.Assembly != core)
            {
                return;
            }

            if (seen.Add(t))
            {
                queue.Enqueue(t);
            }
        }

        Visit(root);
        bool isRoot = true;
        while (queue.Count > 0)
        {
            Type t = queue.Dequeue();
            BindingFlags fieldFlags = BindingFlags.Instance | BindingFlags.Public | (isRoot ? BindingFlags.NonPublic : BindingFlags.Default);
            foreach (FieldInfo f in t.GetFields(fieldFlags))
            {
                Visit(f.FieldType);
            }

            if (isRoot)
            {
                foreach (ConstructorInfo c in t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    foreach (ParameterInfo p in c.GetParameters())
                    {
                        Visit(p.ParameterType);
                    }
                }
            }

            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                Visit(p.PropertyType);
            }

            foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (m.DeclaringType == typeof(object))
                {
                    continue;
                }

                Visit(m.ReturnType);
                foreach (ParameterInfo p in m.GetParameters())
                {
                    Visit(p.ParameterType);
                }
            }

            isRoot = false;
        }

        return seen.ToImmutableHashSet();
    }

    /// <summary>闭包中出现的违禁类型名（空即通过）。</summary>
    internal static string[] Violations(Type root) =>
        [.. ReachableTypes(root).Intersect(ForbiddenForOfficialAi).Select(t => t.Name).Order()];

    // ---------- 局面 ----------

    /// <summary>开局并摆到第 5 大回合（全图可落子、全员已解除保护）、顺序 P0 → P3。</summary>
    internal static MatchFlow Round5(GameSeed? seed = null, params (string Cell, RelicContent Content)[] relics) =>
        MatchFixtures.Started(seed, relics: relics).AtRound(5, [P0, P1, P2, P3]);

    /// <summary>开始当前玩家的小回合并直接走到部署阶段（征募不选）。</summary>
    internal static StagedBatch OpenDeploy(this MatchFlow match)
    {
        match.BeginTurn();
        match.EnterRecruit();
        return match.EnterDeploy();
    }

    /// <summary>把批次清空、摆上指定落点并预演。</summary>
    internal static RehearsalResult RehearseBatch(this MatchFlow match, StagedBatch batch, params (string Cell, PieceType Type)[] placements)
    {
        batch.Clear();
        foreach ((string cell, PieceType type) in placements)
        {
            if (batch.Stage(Coord.Parse(cell), type) is { } failure)
            {
                throw new InvalidOperationException($"夹具暂放失败：{failure.Message}");
            }
        }

        return match.Rehearse();
    }

    /// <summary>把当前玩家本小回合的部署上限改成 <paramref name="deployLimit"/>（只改快照，不改生成路径）。</summary>
    internal static void SetDeployLimit(this MatchFlow match, int deployLimit) =>
        match.Debug.SetSnapshotTransform(s => new EffectSnapshot(
            s.Player, s.MajorRound, s.RevealCount, s.FreePickCount, s.TypeSlots, deployLimit, s.EmblemCounts, s.HeldTypeCount, s.CatchUp));

    /// <summary>跑完前 <paramref name="majorRounds"/> 个大回合（或直到终局）。</summary>
    internal static void RunMajorRounds(this MatchRunner runner, int majorRounds)
    {
        int guard = 0;
        while (runner.Match.Phase == MatchPhase.InProgress && runner.Match.MajorRound <= majorRounds)
        {
            if (++guard > 10_000)
            {
                throw new InvalidOperationException("跑局超过 10000 个小回合。");
            }

            runner.RunTurn();
        }
    }

    /// <summary>给全部玩家装正式 AI 并返回，便于读取各自的决策日志。</summary>
    internal static ImmutableSortedDictionary<PlayerId, HeuristicTurnController> AttachAi(
        this MatchRunner runner, AiDifficulty difficulty = AiDifficulty.Standard, EvaluationWeights? weights = null, AiSearchConfig? config = null)
    {
        ImmutableSortedDictionary<PlayerId, HeuristicTurnController>.Builder b = ImmutableSortedDictionary.CreateBuilder<PlayerId, HeuristicTurnController>();
        foreach (PlayerId player in runner.Match.Players)
        {
            HeuristicTurnController ai = HeuristicAi.Create(runner.Match, player, difficulty, weights, config);
            runner.SetController(player, ai);
            b.Add(player, ai);
        }

        return b.ToImmutable();
    }

    // ---------- 投影 ----------

    /// <summary>全部 AI 的决策日志投影成一段确定性文本。</summary>
    internal static string DecisionLog(IEnumerable<KeyValuePair<PlayerId, HeuristicTurnController>> ais) =>
        string.Join("\n", ais.Select(kv => $"{kv.Key}: {string.Join(" | ", kv.Value.Decisions)}"));

    /// <summary>势力明细投影成文本（含 ImmutableArray 的 record 不能直接 Assert.Equal）。</summary>
    internal static string PowerText(MatchFlow m) => PowerText(m.Scoreboard.Latest!);

    internal static string PowerText(PowerSnapshot snapshot) =>
        string.Join(";", snapshot.Players.Select(p =>
            $"{p.Player}:{p.Status}:{p.Total}:[{string.Join(",", p.ExclusiveCells.Select(c => c.ToNotation()))}]:" +
            string.Join("|", p.Groups.Select(g => g.ToString()))));

    /// <summary>手牌公开视图 + 流程状态投影成文本。</summary>
    internal static string FlowText(MatchFlow m) =>
        $"{m.Phase}/{m.MajorRound}/{m.Stage}/{m.CurrentPlayer}/{string.Join(">", m.ActionOrder)}/{m.PassStreak}/" +
        string.Join(";", m.PlayerStates.Select(s => s.ToString())) + "/" +
        string.Join(";", m.Hands.PublicViews().Select(h => h.ToString())) + "/" +
        string.Join(";", m.Relics.PublicStates().Select(r => r.ToString()));
}

/// <summary>脚本化的人工控制者：记录每个阶段被调用的次数，部署时摆指定落点（普通子）。</summary>
internal sealed class ScriptedController(params string[] cells) : ITurnController
{
    public int OrganizeCalls { get; private set; }

    public int RecruitCalls { get; private set; }

    public int DeployCalls { get; private set; }

    public void OrganizeHand(PlayerHandAccess hand, int overflow)
    {
        OrganizeCalls++;
        for (int i = 0; i < overflow; i++)
        {
            hand.Discard(hand.PrivateView().Types.First());
        }
    }

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel) => RecruitCalls++;

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        DeployCalls++;
        foreach (string cell in cells)
        {
            batch.Stage(Coord.Parse(cell), PieceType.Basic);
        }

        if (!rehearse().IsLegal)
        {
            batch.Clear();
        }
    }

    public bool OnRejected(StagedBatch batch, BatchFailure failure)
    {
        batch.Clear();
        return false;
    }
}

/// <summary>在整理手牌阶段触发人工接管，然后把本阶段交给原 AI——模拟"设计者在某 AI 玩家的小回合中途按下接管"。</summary>
internal sealed class TakeoverTrigger(MatchRunner runner, PlayerId player, ITurnController ai, ITurnController manual) : ITurnController
{
    public ITurnController Ai => ai;

    public void OrganizeHand(PlayerHandAccess hand, int overflow)
    {
        runner.TakeOver(player, manual);
        ai.OrganizeHand(hand, overflow);
    }

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel) => ai.Recruit(hand, panel);

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse) => ai.Deploy(batch, rehearse);

    public bool OnRejected(StagedBatch batch, BatchFailure failure) => ai.OnRejected(batch, failure);
}
