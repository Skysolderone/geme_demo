using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Show;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show「决赛倒计时提示」（final-countdown 3.1）：本机所在战场进入倒计时时，每个大回合开始出一条不打断操作的提示条
/// "对面战场已结算，本战场还剩 N 个大回合"；本机是等待者时出"等待决赛：对面战场最迟第 R + K 大回合结束"；无人值守时提示条零时长。
/// 文案与去重在 <see cref="FinalCountdownNotices"/>，显示复用 <see cref="NoticeQueue"/>；输入按图形版会话层同一口径从对局读出（<see cref="Inputs"/>）。
/// </summary>
/// <remarks>
/// 变异验证（final-countdown 3.1 实跑，见 implement.md）：M-FC6 剩余大回合数不含本大回合（<c>mine - majorRound + 1</c> 改为 <c>mine - majorRound</c>）→ 红 2（「倒计时提示」「每个大回合只出一次」）；
/// M-FC7 去掉按大回合去重（<c>majorRound &lt;= _lastRound</c> 改为 <c>majorRound &lt; _lastRound</c>）→ 红 1（「每个大回合只出一次」）。
/// </remarks>
public class 决赛倒计时提示Tests
{
    /// <summary>与 <c>src/godot/scripts/MatchSession.cs</c> 的 <c>CountdownNotice</c> 同一口径读出提示输入并取一次。</summary>
    private static string? Inputs(FinalCountdownNotices notices, MatchFlow match, PlayerId me)
    {
        int myField = match.BattlefieldOf(me);
        int? mine = !match.IsBattlefieldSettled(myField) && match.BattlefieldDeadlines.TryGetValue(myField, out int d) ? d : null;
        return notices.Take(match.MajorRound, match.Phase == MatchPhase.InProgress, match.StateOf(me).Status == PlayerStatus.Active, match.IsWaitingForFinal(me), mine,
            match.BattlefieldDeadlines.Where(kv => kv.Key != myField && !match.IsBattlefieldSettled(kv.Key)).Select(kv => kv.Value));
    }

    /// <summary>四人四区 [0,1,3,4]、K = 2、脚本配对；第 1–5 大回合全员落子，第 6 大回合战场 3（P2、P3）全员 Pass 结算 → 此刻是第 7 大回合开始。</summary>
    private static MatchFlow FieldThreeSettled()
    {
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource(), options: CountdownOptions(2));
        match.PlayRounds(5, All).PlayRound(P0, P1);
        Assert.Equal(7, match.MajorRound);
        Assert.Equal((6, 3), (Assert.Single(match.Settlements).MajorRound, match.Settlements[0].Battlefield));
        Assert.Equal(8, match.DeadlineOf(0));
        return match;
    }

    [Fact]
    public void 倒计时提示()
    {
        // 规格 Scenario：本机玩家在战场 1，战场 4 已结算，K = 2 → 下一大回合开始时出现提示条"对面战场已结算，本战场还剩 2 个大回合"。
        // 这里本机 = P0（战场 0），战场 3 第 6 大回合结算 → 第 7 大回合开始"还剩 2"，第 8 大回合开始"还剩 1"。
        MatchFlow match = FieldThreeSettled();
        var notices = new FinalCountdownNotices();

        Assert.Equal("对面战场已结算，本战场还剩 2 个大回合", Inputs(notices, match, P0));
        match.PlayRound(P0, P1);
        Assert.Equal("对面战场已结算，本战场还剩 1 个大回合", Inputs(notices, match, P0));
    }

    [Fact]
    public void 等待者看到期限()
    {
        // 规格正文：本机玩家是等待者时显示"等待决赛：对面战场最迟第 R + K 大回合结束"（R = 6、K = 2）。
        MatchFlow match = FieldThreeSettled();
        PlayerId waiter = match.Settlements[0].Winner;
        Assert.True(match.IsWaitingForFinal(waiter));

        Assert.Equal("等待决赛：对面战场最迟第 8 大回合结束", Inputs(new FinalCountdownNotices(), match, waiter));
        // 落败出局的那名玩家看不到任何倒计时提示。
        PlayerId loser = match.Settlements[0].Entries[1].Player;
        Assert.Null(Inputs(new FinalCountdownNotices(), match, loser));
    }

    [Fact]
    public void 每个大回合只出一次()
    {
        // 会话层每个小回合结算接线后都会问一次：同一个大回合只出第一次，下一大回合开始再出；没有倒计时的大回合什么都不出（且同样记为已问过）。
        MatchFlow match = FieldThreeSettled();
        var notices = new FinalCountdownNotices();
        Assert.NotNull(Inputs(notices, match, P0));
        Assert.Null(Inputs(notices, match, P0));
        Assert.Null(notices.Take(7, true, true, false, myDeadline: 8, []));
        Assert.Equal(FinalCountdownNotices.CountdownText(1), notices.Take(8, true, true, false, myDeadline: 8, []));
        Assert.Null(notices.Take(9, false, true, false, myDeadline: 8, []));   // 不在进行中（待决赛链接 / 终局）不出
    }

    [Fact]
    public void 没有倒计时不出提示()
    {
        // 纯函数的边界：无期限、已出局、等待者但对面都没有期限、期限已过（不应出现的数据）一律不出。
        Assert.Null(FinalCountdownNotices.At(5, active: true, waiting: false, myDeadline: null, []));
        Assert.Null(FinalCountdownNotices.At(5, active: false, waiting: false, myDeadline: 6, []));
        Assert.Null(FinalCountdownNotices.At(5, active: true, waiting: true, myDeadline: null, []));
        Assert.Null(FinalCountdownNotices.At(9, active: true, waiting: false, myDeadline: 8, []));
        Assert.Equal("等待决赛：对面战场最迟第 9 大回合结束", FinalCountdownNotices.At(7, active: true, waiting: true, myDeadline: null, [8, 9]));

        // 四人四区尚无结算：谁都没有倒计时提示。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource(), options: CountdownOptions(2));
        match.PlayRounds(5, All);
        Assert.All(All, p => Assert.Null(Inputs(new FinalCountdownNotices(), match, p)));
    }

    [Fact]
    public void 无人值守时提示条零时长()
    {
        // 规格正文：无人值守时提示条零时长——与他场结算提示条同一个队列：零时长入队即丢，正常时长立即显示、约 3 s 后消失。
        string text = FinalCountdownNotices.CountdownText(2);
        var unattended = new NoticeQueue(ShowDuration.Zero);
        unattended.Enqueue([text]);
        Assert.True(unattended.IsEmpty);
        Assert.Null(unattended.Current);

        var normal = new NoticeQueue(ShowDuration.Normal);
        normal.Enqueue([text]);
        Assert.Equal(text, normal.Current);
        normal.Advance(NoticeQueue.DwellMs);
        Assert.True(normal.IsEmpty);
    }
}
