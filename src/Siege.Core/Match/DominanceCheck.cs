using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>碾压式的一名玩家输入：玩家、判定时刻的<b>权威</b>参赛状态、当前势力值。</summary>
/// <param name="Player">玩家。</param>
/// <param name="Status">参赛状态。只有 <see cref="PlayerStatus.Active"/> 参与判定（dominance-victory 裁决 2）。</param>
/// <param name="Power">当前势力值。</param>
public sealed record DominanceEntry(PlayerId Player, PlayerStatus Status, long Power);

/// <summary>碾压候选的公开状态（dominance-victory 裁决 7）：候选玩家与待回应名单，始终公开。</summary>
/// <param name="Candidate">碾压候选。</param>
/// <param name="Pending">待回应名单，按玩家编号升序。</param>
public sealed record DominanceState(PlayerId Candidate, ImmutableArray<PlayerId> Pending);

/// <summary>
/// 碾压式 <c>本人势力 ≥ 其余参赛玩家势力之和</c>（设计文档 §12.3，dominance-victory D1 / D2 / 裁决 1 / 2）的<b>唯一</b>实现，纯函数。
/// </summary>
/// <remarks>
/// <para>取等号即满足。只统计参赛玩家：已弃赛与已出局玩家的势力 MUST NOT 计入"其余之和"，其本人也 MUST NOT 满足。
/// 传入的状态必须取自流程层的权威名册，而不是势力快照里的状态——快照在同一次结算里先于出局检查生成，那里的状态是旧的。</para>
/// <para>候选制（裁决 7）需要区分"恰有一人满足"与"多人同时满足"，因此本函数返回<b>全部</b>满足者而不是只挑最高者。
/// 多人同时满足只可能是"最高两人势力相同且其余为 0"（含全员 0）。</para>
/// </remarks>
public static class DominanceCheck
{
    /// <summary>全部满足碾压式的参赛玩家，按玩家编号升序；零人参赛时为空。</summary>
    public static ImmutableArray<PlayerId> Satisfying(IEnumerable<DominanceEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        DominanceEntry[] active = [.. entries.Where(e => e.Status == PlayerStatus.Active).OrderBy(e => e.Player)];
        long total = active.Sum(e => e.Power);
        return [.. active.Where(e => e.Power >= total - e.Power).Select(e => e.Player)];
    }
}
