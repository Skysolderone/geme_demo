using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Ai;

/// <summary>
/// 正式 AI 的装配入口。这里是唯一接触 <see cref="MatchFlow"/> 的地方：只从中取观察视图委托（经 <see cref="VisionFor"/>：缺省按座位投影
/// <see cref="MatchFlow.PublishSeatProjection"/> + <see cref="MatchFlow.ProjectRehearsal"/>，显式全知时为 <see cref="MatchFlow.Publish"/>）与按玩家命名的种子子流，
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
    /// 正式 AI 的读取接口（规格 ai-decision「正式对战 AI 的信息边界」；ai-seat-vision 裁决 V1 / D1）：视野<b>只在这里</b>分流。
    /// 缺省（<see cref="AiSearchConfig.SeatVision"/> 为 <c>true</c>，配置为 <c>null</c> 同；四档预设都是）给按座位投影的视图 <see cref="MatchFlow.PublishSeatProjection"/>
    /// （其他战场的棋子、信物内容、势力与手牌类型不可见），并把预演结果投影到同一口径（<see cref="MatchFlow.ProjectRehearsal"/>）——否则"预演前"看不到、
    /// "预演后"看得到的他人棋子会被评价成敌方势力的变化。显式全知（<c>false</c>，只作测试与离线分析的配置项）给全知视图 <see cref="MatchFlow.Publish"/>、预演结果原样使用。
    /// 视图与预演结果的类型都不变，评价代码不感知差别。
    /// </summary>
    internal static AiVisionPorts VisionFor(MatchFlow match, PlayerId player, AiSearchConfig? config)
    {
        ArgumentNullException.ThrowIfNull(match);
        return config?.SeatVision == false
            ? new AiVisionPorts(match.Publish, null)
            : new AiVisionPorts(() => match.PublishSeatProjection(player), result => match.ProjectRehearsal(player, result));
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
