using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;

namespace Siege.Core.Ai;

/// <summary>
/// <b>测试专用</b>调试视图（设计文档 §15.3 / design.md D1）：完整权威状态——未揭示信物内容、任意玩家的手牌数量与征募面板、当前暂放批次。
/// 与 <see cref="MatchPublicView"/> 是两个不同的类型；<c>internal</c>：仅 <c>Siege.Sim</c> 与测试程序集可达，面向玩家的 Godot 项目在编译期拿不到。
/// </summary>
internal sealed class MatchDebugView
{
    private readonly MatchFlow _match;

    internal MatchDebugView(MatchFlow match)
    {
        _match = match ?? throw new ArgumentNullException(nameof(match));
    }

    /// <summary>公开快照（与正式 AI 看到的完全相同）。</summary>
    internal MatchPublicView Public() => _match.Publish();

    /// <summary>任意信物格的真实内容，不论是否已揭示。</summary>
    internal RelicContent ContentOf(Coord coord) => _match.Relics.Generation.At(coord).Content;

    /// <summary>任意玩家的私有手牌视图（含数量）。</summary>
    internal HandPrivateView HandOf(PlayerId player) => _match.Hands.Debug.PrivateViewOf(player);

    /// <summary>任意玩家的手牌总枚数。</summary>
    internal int HandCountOf(PlayerId player) => HandOf(player).TotalCount;

    /// <summary>任意玩家的私人征募面板；该玩家不在征募阶段时抛出。</summary>
    internal RecruitPanelView PanelOf(PlayerId player) => _match.Hands.Debug.PanelOf(player);

    /// <summary>当前行动玩家尚未确认的暂放批次；不在部署阶段为 <c>null</c>。</summary>
    internal StagedBatch? CurrentBatch => _match.CurrentBatch;
}
