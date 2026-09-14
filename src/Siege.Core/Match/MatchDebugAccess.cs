using Siege.Core.Board;

namespace Siege.Core.Match;

/// <summary>
/// <b>测试专用</b>接缝（设计文档 §15.3）：把对局直接摆到某个大回合 / 某种保护状态，以便单独触发一条规则。
/// <c>internal</c>：仅测试程序集可达。它只改流程层自己的状态，不绕过任何下层规则。
/// </summary>
internal sealed class MatchDebugAccess
{
    private readonly MatchFlow _match;

    internal MatchDebugAccess(MatchFlow match)
    {
        _match = match;
    }

    /// <summary>直接设定当前大回合序号。只允许在小回合边界。</summary>
    internal void SetMajorRound(int majorRound) => _match.DebugSetMajorRound(majorRound);

    /// <summary>直接设定本大回合的行动顺序并把指针指向第一位。只允许在小回合边界。</summary>
    internal void SetOrder(params PlayerId[] order) => _match.DebugSetOrder(order);

    /// <summary>直接设定某玩家的开局出局保护状态。</summary>
    internal void SetProtection(PlayerId player, bool protectedNow) => _match.DebugSetProtection(player, protectedNow);

    /// <summary>直接设定连续 Pass 计数。</summary>
    internal void SetPassStreak(int streak) => _match.DebugSetPassStreak(streak);

    /// <summary>在小回合之外直接设定某玩家的手牌（空数组即清空手牌）。</summary>
    internal void SeedHand(PlayerId player, params (PieceType Type, int Count)[] entries) => _match.Hands.Debug.SeedHand(player, entries);

    /// <summary>改写小回合开始时生成的效果快照（如把类型槽缩到 3 以触发强制弃牌门）。传 <c>null</c> 取消。</summary>
    internal void SetSnapshotTransform(Func<Relics.EffectSnapshot, Relics.EffectSnapshot>? transform) => _match.DebugSetSnapshotTransform(transform);

    /// <summary>盘面被测试直接改动后，重算势力与信物控制，让公开快照与盘面一致。</summary>
    internal void Recalculate() => _match.DebugRecalculate();
}
