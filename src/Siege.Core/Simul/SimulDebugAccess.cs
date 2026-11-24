using Siege.Core.Board;

namespace Siege.Core.Simul;

/// <summary>新玩法的测试专用接缝（<c>internal</c>，只对测试程序集可达）。</summary>
internal sealed class SimulDebugAccess
{
    private readonly SimulFlow _flow;

    internal SimulDebugAccess(SimulFlow flow) => _flow = flow;

    /// <summary>插旗阶段：直接设定某玩家的手牌（全部计入基数）。</summary>
    internal void SeedHand(PlayerId player, params (PieceType Type, int Count)[] entries) => _flow.Hands.SeedHand(player, entries);

    /// <summary>直接摆单位（绕过规则）；进行中时同步刷新 S0 与势力。</summary>
    internal void Place(PlayerId owner, PieceType type, params Coord[] cells) => _flow.DebugPlace(owner, type, cells);

    /// <summary>直接摆普通单位（围棋记法）。</summary>
    internal void Stones(PlayerId owner, params string[] cells) => _flow.DebugPlace(owner, PieceType.Basic, cells.Select(Coord.Parse));

    /// <summary>按给定座位直接锁定并开始第 1 回合（不改派、不消费 flag-pick）。</summary>
    internal void LockAs(IEnumerable<(PlayerId Player, int Zone)> seats) => _flow.DebugLockAs(seats);

    /// <summary>把当前回合改成指定回合号与顺位（准备阶段）。</summary>
    internal void SetRound(int round, IEnumerable<PlayerId>? order = null) => _flow.DebugSetRound(round, order);

    /// <summary>某玩家的种子兜底名次（锁定时由 setup 子流洗出）。</summary>
    internal int SeedRankOf(PlayerId player) => _flow.SeatOf(player).SeedRank;
}
