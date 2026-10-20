using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Ai;

/// <summary>
/// 正式 AI 的装配入口。这里是唯一接触 <see cref="MatchFlow"/> 的地方：只从中取观察视图委托（经 <see cref="VisionFor"/>：按座位投影
/// <see cref="MatchFlow.PublishSeatProjection"/> + <see cref="MatchFlow.ProjectRehearsal"/>）与按玩家命名的种子子流，AI 对象本身拿不到对局。
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
        AiVisionPorts vision = VisionFor(match, player, config);
        return new HeuristicTurnController(player, vision.Observe, match.Seed.Stream(StreamName(player)), difficulty, weights, config)
        {
            RehearsalView = vision.Rehearsal,
        };
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
        AiVisionPorts vision = VisionFor(match, player, config);
        return new HeuristicTurnController(
            player, vision.Observe, match.Seed.Stream(StreamName(player)), difficulty, weights, config, relicValue: null, lifeQuery, cacheLife)
        {
            RehearsalView = vision.Rehearsal,
        };
    }

    /// <summary>
    /// 正式 AI 的读取接口（规格 ai-decision「正式对战 AI 的信息边界」；ai-seat-vision 裁决 V1 / D1）：AI 只读按座位投影的视图 <see cref="MatchFlow.PublishSeatProjection"/>，
    /// 并把预演结果投影到同一口径（<see cref="MatchFlow.ProjectRehearsal"/>）——否则"预演前"看不到、"预演后"看得到的他人棋子会被评价成敌方势力的变化。
    /// open-map 段 B（裁决 B1）：全知开关 <c>SeatVision</c> 已删除——锁定后整张地图是唯一战场，座位视野即全图，全知与座位视野结果相同（段 A 基线自洽检查实证）。
    /// 视图与预演结果的类型都不变，评价代码不感知差别。
    /// </summary>
    internal static AiVisionPorts VisionFor(MatchFlow match, PlayerId player, AiSearchConfig? config)
    {
        ArgumentNullException.ThrowIfNull(match);
        _ = config;
        return new AiVisionPorts(() => match.PublishSeatProjection(player), result => match.ProjectRehearsal(player, result));
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

/// <summary>正式 AI 的两个读取口：观察视图与预演结果的口径（<c>null</c> = 原样）。由 <see cref="HeuristicAi.VisionFor"/> 唯一给出。</summary>
internal sealed record AiVisionPorts(Func<MatchPublicView> Observe, Func<Batch.RehearsalResult, Batch.RehearsalResult>? Rehearsal);
