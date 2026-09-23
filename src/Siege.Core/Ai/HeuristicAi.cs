using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Ai;

/// <summary>
/// 正式 AI 的装配入口。这里是唯一接触 <see cref="MatchFlow"/> 的地方：只从中取 <see cref="MatchFlow.Publish"/> 委托与按玩家命名的种子子流，
/// AI 对象本身拿不到对局。
/// </summary>
public static class HeuristicAi
{
    /// <summary>AI 扰动子流名：按玩家分流（<c>ai-P0</c>），与 <c>relic-gen</c> / <c>recruit</c> / <c>setup</c> 互相独立。</summary>
    public static string StreamName(PlayerId player) => $"ai-{player}";

    /// <summary>为某玩家创建正式 AI。</summary>
    public static HeuristicTurnController Create(
        MatchFlow match,
        PlayerId player,
        AiDifficulty difficulty = AiDifficulty.Standard,
        EvaluationWeights? weights = null,
        AiSearchConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        return new HeuristicTurnController(player, match.Publish, match.Seed.Stream(StreamName(player)), difficulty, weights, config);
    }

    /// <summary>
    /// 测试接缝：同 <see cref="Create(MatchFlow, PlayerId, AiDifficulty, EvaluationWeights?, AiSearchConfig?)"/>，另可换上活形查询计数桩、关闭决策内活形缓存（ai-eye D5）。
    /// </summary>
    internal static HeuristicTurnController Create(
        MatchFlow match,
        PlayerId player,
        AiDifficulty difficulty,
        EvaluationWeights? weights,
        AiSearchConfig? config,
        Func<GameBoard, LifeShapeReport>? lifeQuery,
        bool cacheLife)
    {
        ArgumentNullException.ThrowIfNull(match);
        return new HeuristicTurnController(
            player, match.Publish, match.Seed.Stream(StreamName(player)), difficulty, weights, config, relicValue: null, lifeQuery, cacheLife);
    }

    /// <summary>给全部玩家装上同难度、同权重的正式 AI。</summary>
    public static void AttachAll(
        MatchRunner runner,
        AiDifficulty difficulty = AiDifficulty.Standard,
        EvaluationWeights? weights = null,
        AiSearchConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        foreach (PlayerId player in runner.Match.Players)
        {
            runner.SetController(player, Create(runner.Match, player, difficulty, weights, config));
        }
    }
}
