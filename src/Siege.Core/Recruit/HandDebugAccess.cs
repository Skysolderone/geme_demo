using Siege.Core.Board;

namespace Siege.Core.Recruit;

/// <summary>
/// <b>测试专用</b>的全量读取旁路（设计文档 §15.3）：调试 AI 与回归测试可以读取任意玩家的私有手牌与征募面板。
/// 类型为 <c>internal</c>，仅 <c>InternalsVisibleTo</c> 的测试程序集可达；面向玩家的对局在编译期就拿不到它。
/// </summary>
internal sealed class HandDebugAccess
{
    private readonly HandLedger _ledger;

    internal HandDebugAccess(HandLedger ledger)
    {
        _ledger = ledger;
    }

    /// <summary>任意玩家的私有视图。</summary>
    internal HandPrivateView PrivateViewOf(PlayerId player) => _ledger.PrivateViewOf(player);

    /// <summary>任意玩家当前的征募面板。</summary>
    internal RecruitPanelView PanelOf(PlayerId player) => _ledger.PanelOf(player);

    /// <summary>测试夹具：在小回合之外直接设定某玩家的手牌（全部计入回合前基数）。</summary>
    internal void SeedHand(PlayerId player, params (PieceType Type, int Count)[] entries) => _ledger.SeedHand(player, entries);
}
