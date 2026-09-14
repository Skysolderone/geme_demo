using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 一名玩家的流程状态（对下游的"玩家状态"契约）。
/// </summary>
/// <param name="Player">玩家。</param>
/// <param name="Status">参赛 / 已弃赛 / 已出局。</param>
/// <param name="HasOpeningProtection">开局出局保护是否仍然有效。<b>每玩家一个布尔</b>，只在该玩家完成第 4 大回合的小回合后单独解除（design.md D1）。</param>
/// <param name="BirthZone">锁定的出生区编号；插旗未锁定时为 <c>null</c>。</param>
/// <param name="LastRoundPosition">上一大回合的行动位置（0 基）；首回合的随机顺序不计入，故第 1 大回合结束时为 <c>null</c>。</param>
/// <param name="EliminationOrder">出局序号（第几个出局，从 1 起）；未出局为 <c>null</c>。</param>
/// <param name="EliminatedInMajorRound">出局所在的大回合。</param>
/// <param name="ResignedInMajorRound">弃赛所在的大回合。</param>
/// <param name="PowerAtResign">弃赛时的势力值，供终局名次的弃赛者组内排序。</param>
public sealed record PlayerFlowState(
    Board.PlayerId Player,
    PlayerStatus Status,
    bool HasOpeningProtection,
    int? BirthZone,
    int? LastRoundPosition,
    int? EliminationOrder,
    int? EliminatedInMajorRound,
    int? ResignedInMajorRound,
    long? PowerAtResign)
{
    /// <summary>是否参赛中。</summary>
    public bool IsActive => Status == PlayerStatus.Active;
}
