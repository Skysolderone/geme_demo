using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Match;

/// <summary>接管事件的种类。</summary>
public enum TakeoverKind
{
    /// <summary>人工接管了该玩家的控制权。</summary>
    TakenOver,

    /// <summary>控制权交还给原控制者（AI）。</summary>
    HandedBack,
}

/// <summary>一次接管 / 交还：玩家、发生时的大回合与阶段（design.md D7：分析据此筛除接管局）。</summary>
public sealed record TakeoverRecord(int Sequence, PlayerId Player, int MajorRound, TurnStage Stage, TakeoverKind Kind)
{
    public override string ToString() => $"#{Sequence} R{MajorRound} {Stage} {Player} {Kind}";
}

/// <summary>
/// 对局标注（设计文档 §15.3）：本局是否用过调试 AI、以及全部人工接管事件。挂在 <see cref="MatchRunner"/> 上，由跑局日志序列化。
/// 只有 <c>internal</c> 写入口：调试 AI 的构造与运行器的接管方法。
/// </summary>
public sealed class MatchAnnotations
{
    private readonly SortedSet<PlayerId> _debugPlayers = [];
    private readonly List<TakeoverRecord> _takeovers = [];

    /// <summary>本局是否用过调试 AI。</summary>
    public bool UsedDebugAi => _debugPlayers.Count > 0;

    /// <summary>用过调试 AI 的玩家。</summary>
    public ImmutableArray<PlayerId> DebugAiPlayers => [.. _debugPlayers];

    /// <summary>全部接管 / 交还事件，按发生顺序。</summary>
    public IReadOnlyList<TakeoverRecord> Takeovers => _takeovers;

    /// <summary>本局是否发生过人工接管。</summary>
    public bool HasTakeover => _takeovers.Count > 0;

    internal void MarkDebugAi(PlayerId player) => _debugPlayers.Add(player);

    internal void RecordTakeover(PlayerId player, int majorRound, TurnStage stage, TakeoverKind kind) =>
        _takeovers.Add(new TakeoverRecord(_takeovers.Count + 1, player, majorRound, stage, kind));
}
