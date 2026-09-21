using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;

namespace Siege.Core.Scoring;

/// <summary>
/// 遥测用：整局出现过的最高倍率及其首次出现的大回合序号（设计文档 §16 / §17）。这是遥测记录，不是分数，不参与任何计算。
/// <see cref="Power"/> 是该串峰值出现时的取整军势（heuristic-ai 裁决 6 的纯增量）；"峰值串被摧毁"由跑局日志层比对相邻快照算出，不进本层。
/// </summary>
public sealed record MultiplierPeak(int MultiplierCount, int MajorRound, PlayerId Player, ImmutableArray<Coord> Stones, BigInteger Power)
{
    /// <summary>峰值倍率的精确表示 <c>1.5^<see cref="MultiplierCount"/></c>。</summary>
    public Multiplier Multiplier => new(MultiplierCount);
}

/// <summary>
/// 公开势力榜：结算顺序第 5 步（<c>ISettlementHooks.OnRecalculatePower</c>）与每次 Pass 后的重算入口。
/// 接线由 add-match-flow 负责，本层只提供可被回调的 <see cref="Recalculate"/>。
/// </summary>
/// <remarks>
/// <para><see cref="Latest"/> 永远是最近一次全量重算的结果，只反映当前盘面；上一份快照被整体替换，不叠加、不累计。</para>
/// <para><see cref="Peak"/> 是唯一跨结算保留的量，且只作遥测用：按倍增子数量取峰值，看玩家"堆了多少"；§16 数值区间失守时靠它定位。</para>
/// </remarks>
public sealed class PowerScoreboard
{
    /// <summary>最近一次重算的结果；尚未重算过时为 <c>null</c>。</summary>
    public PowerSnapshot? Latest { get; private set; }

    /// <summary>已执行的重算次数。Pass 也计入——Pass 同样触发重算。</summary>
    public int Version { get; private set; }

    /// <summary>整局倍率峰值遥测；尚未出现任何倍增子时为 <c>null</c>。</summary>
    public MultiplierPeak? Peak { get; private set; }

    /// <summary>对当前盘面全量重算并替换 <see cref="Latest"/>；<paramref name="siteValues"/> 是对局配置的据点分值；<paramref name="majorRound"/> 只用于倍率峰值遥测的轮次标记。</summary>
    public PowerSnapshot Recalculate(GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus> roster, SiteValues siteValues, int majorRound)
    {
        PowerSnapshot snapshot = PowerCalculator.Compute(board, roster, siteValues);
        Latest = snapshot;
        Version++;
        TrackPeak(snapshot, majorRound);
        return snapshot;
    }

    /// <summary>只在严格更高时更新，因此记录的是峰值<b>首次</b>出现的大回合；同轮内按玩家、棋串的确定性顺序取第一条。</summary>
    private void TrackPeak(PowerSnapshot snapshot, int majorRound)
    {
        foreach (PlayerPower player in snapshot.Players)
        {
            foreach (GroupPower group in player.Groups)
            {
                if (group.MultiplierCount > 0 && (Peak is null || group.MultiplierCount > Peak.MultiplierCount))
                {
                    Peak = new MultiplierPeak(group.MultiplierCount, majorRound, player.Player, group.Stones, group.Power);
                }
            }
        }
    }
}
