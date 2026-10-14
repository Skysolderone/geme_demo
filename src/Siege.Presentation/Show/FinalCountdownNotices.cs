using Siege.Core.Match;

namespace Siege.Presentation.Show;

/// <summary>
/// 决赛倒计时提示条（settlement-show「决赛倒计时提示」，final-countdown 3.1）：本机玩家所在战场进入倒计时时，每个大回合开始出一条不打断操作的提示
/// "对面战场已结算，本战场还剩 N 个大回合"（N = 期限 − 当前大回合 + 1，含本大回合）；本机玩家是等待决赛的玩家时出"等待决赛：对面战场最迟第 X 大回合结束"
/// （X = 其余未结算战场期限的最大值，通常即 R + K）。到期结算沿用既有的「战场结算横幅」，这里不出。
/// 只产出文案；显示复用他场结算提示条的 <see cref="NoticeQueue"/>（无人值守零时长：入队即丢）。按大回合去重：同一个大回合只出一次。
/// 纯表现层状态：不改对局、不进存档与日志。
/// </summary>
public sealed class FinalCountdownNotices
{
    private int _lastRound;

    /// <summary>倒计时提示条文案。</summary>
    public static string CountdownText(int remaining) => $"对面战场已结算，本战场还剩 {remaining} 个大回合";

    /// <summary>等待者提示条文案。</summary>
    public static string WaitingText(int deadline) => $"等待决赛：对面战场最迟第 {deadline} 大回合结束";

    /// <summary>
    /// 第 <paramref name="majorRound"/> 大回合开始时本机玩家该看到的提示（纯函数）：本机在局且所在战场有期限 → 倒计时文案；
    /// 本机是等待者且其余未结算战场有期限 → 等待者文案；都不是为 <c>null</c>。已过期限的数据（不应出现）一律不出。
    /// </summary>
    /// <param name="majorRound">刚开始的大回合。</param>
    /// <param name="active">本机玩家是否仍在局。</param>
    /// <param name="waiting">本机玩家是否是等待决赛的玩家（Core 的 <see cref="MatchFlow.IsWaitingForFinal"/>）。</param>
    /// <param name="myDeadline">本机所在战场的期限（未结算且在倒计时中才有）。</param>
    /// <param name="otherDeadlines">其余未结算战场的期限。</param>
    public static string? At(int majorRound, bool active, bool waiting, int? myDeadline, IEnumerable<int> otherDeadlines)
    {
        ArgumentNullException.ThrowIfNull(otherDeadlines);
        if (!active)
        {
            return null;
        }

        if (myDeadline is { } mine && mine >= majorRound)
        {
            return CountdownText(mine - majorRound + 1);
        }

        if (waiting)
        {
            int[] pending = [.. otherDeadlines.Where(d => d >= majorRound)];
            if (pending.Length > 0)
            {
                return WaitingText(pending.Max());
            }
        }

        return null;
    }

    /// <summary>
    /// 按大回合去重的 <see cref="At"/>：对局不在进行中（<paramref name="inProgress"/> 为假）、或第 <paramref name="majorRound"/> 大回合已经取过（含"没有可出的"）时为 <c>null</c>。
    /// 调用方（图形版会话层）在每次结算接线之后从对局读出这几项输入再调用：大回合推进后的第一次调用即"大回合开始时"。
    /// 输入由调用方读取——本程序集不持有对局本体（守门 <c>UI层不含规则计算Tests</c>）。
    /// </summary>
    public string? Take(int majorRound, bool inProgress, bool active, bool waiting, int? myDeadline, IEnumerable<int> otherDeadlines)
    {
        ArgumentNullException.ThrowIfNull(otherDeadlines);
        if (!inProgress || majorRound <= _lastRound)
        {
            return null;
        }

        _lastRound = majorRound;
        return At(majorRound, active, waiting, myDeadline, otherDeadlines);
    }
}
