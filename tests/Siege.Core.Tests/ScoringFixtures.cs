using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests;

/// <summary>计分层测试的公共夹具：名册工厂、把势力榜接到结算驱动器第 5 步的钩子。</summary>
internal static class ScoringFixtures
{
    internal static readonly PlayerId P2 = new(2);
    internal static readonly PlayerId P3 = new(3);

    internal static IReadOnlyDictionary<PlayerId, PlayerStatus> Roster(params (PlayerId Player, PlayerStatus Status)[] entries) =>
        entries.ToDictionary(e => e.Player, e => e.Status);

    /// <summary>设计文档 §10.1 标准算例棋串：普通子×3、堡垒子×1、倍增子×2 横排在 <paramref name="row"/> 行的 B–G 列。基础 9、倍率 2.25、军势 20。</summary>
    internal static GameBoard PlaceStandardGroup(this GameBoard board, PlayerId owner, int row)
    {
        board.Place($"B{row}", owner, PieceType.Basic).Place($"C{row}", owner, PieceType.Basic).Place($"D{row}", owner, PieceType.Basic)
             .Place($"E{row}", owner, PieceType.Fortress)
             .Place($"F{row}", owner, PieceType.Multiplier).Place($"G{row}", owner, PieceType.Multiplier);
        return board;
    }

    internal static GroupPower GroupContaining(this PowerSnapshot snapshot, PlayerId player, string notation) =>
        snapshot.Of(player).Groups.Single(g => g.Stones.Contains(TestMaps.At(notation)));

    internal static long GroupPowerSum(this PlayerPower player) => player.Groups.Sum(g => g.Power);
}

/// <summary>
/// 把 <see cref="PowerScoreboard"/> 接到结算顺序第 5 步。正式接线属于 add-match-flow；这里只演示"合法批次与 Pass 都会到达重算入口"。
/// </summary>
internal sealed class ScoreboardHooks : ISettlementHooks
{
    private readonly IReadOnlyDictionary<PlayerId, PlayerStatus> _roster;

    internal ScoreboardHooks(IReadOnlyDictionary<PlayerId, PlayerStatus> roster) => _roster = roster;

    internal PowerScoreboard Scoreboard { get; } = new();

    internal int MajorRound { get; set; } = 1;

    internal List<string> Steps { get; } = [];

    public void DeductHand(PlayerId player, IReadOnlyDictionary<PieceType, int> deployed) => Steps.Add("DeductHand");

    public void OnRevealRelics(SettlementContext context) => Steps.Add("OnRevealRelics");

    public void OnRecalculatePower(SettlementContext context)
    {
        Steps.Add("OnRecalculatePower");
        Scoreboard.Recalculate(context.Board, _roster, SiteValues.Standard, MajorRound);
    }

    public void OnCheckEndConditions(SettlementContext context) => Steps.Add("OnCheckEndConditions");

    public void OnPass(PlayerId player) => Steps.Add("OnPass");
}
