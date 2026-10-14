using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;

namespace Siege.Core.Match;

/// <summary>出局原因（elimination-endgame「出局判定」）。序列化名称稳定。只在链接模式下记录；旧模式的出局不带原因（行为与之前逐字节相同）。</summary>
public enum EliminationReason
{
    /// <summary>势力归零：曾建立正势力且当前总势力为 0。</summary>
    PowerZero,

    /// <summary>战场落败：战场结算时势力非最高（出局时势力可以大于 0，记录在案）。</summary>
    BattlefieldDefeat,
}

/// <summary>战场结算的原因（battlefield「战场结算」，match-telemetry 第 10 项）。序列化名称稳定。</summary>
public enum SettlementReason
{
    /// <summary>战场内在局玩家 ≥ 2 且本大回合全部 Pass。</summary>
    AllPassed,

    /// <summary>战场内不存在可落子空格。</summary>
    BoardFull,

    /// <summary>减员：多人战场因弃赛或势力归零只剩 1 名在局玩家（即时标记）。</summary>
    Attrition,

    /// <summary>到期（final-countdown）：战场处于决赛倒计时且到了期限大回合仍未因整轮 Pass、填满或减员结算，按同一比较链强制结算。追加在末位，旧名称不变。</summary>
    Expired,
}

/// <summary>
/// 战场结算时一名玩家的比较量（名次比较链：势力 → 控制信物数 → 独占空格数 → 盘面棋子数，elimination-endgame「终局名次与并列判定」）。
/// <paramref name="Rank"/> 是本次结算内的名次：胜者恒为 1；落败者从 2 起按比较链排、链全同者共享。
/// </summary>
public sealed record SettlementEntry(PlayerId Player, int Rank, BigInteger Power, int ControlledRelics, int ExclusiveCells, int Stones);

/// <summary>
/// 一次战场结算的记录（match-telemetry 第 10 项；存档往返）：序号（从 1 起）、大回合、战场标识、原因、胜者、各玩家名次与势力（胜者在前）。
/// 同一大回合结束时结算的多个战场的落败者共享一个出局序号（<paramref name="EliminationOrder"/>；减员结算没有落败者时为 <c>null</c>）。
/// </summary>
public sealed record BattlefieldSettlementRecord(
    int Sequence, int MajorRound, int Battlefield, SettlementReason Reason, PlayerId Winner, ImmutableArray<SettlementEntry> Entries, int? EliminationOrder)
{
    /// <summary>
    /// 结算时该战场的决赛倒计时期限大回合（final-countdown，match-telemetry「决赛倒计时的记录」）；该战场当时不在倒计时中为 <c>null</c>。
    /// 原因为 <see cref="SettlementReason.Expired"/> 时恒非空且等于 <see cref="MajorRound"/>。
    /// </summary>
    public int? Deadline { get; init; }
}
