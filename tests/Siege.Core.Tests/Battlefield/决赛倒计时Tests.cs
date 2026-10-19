using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Core.Tests.AiDecision;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 规格：battlefield「决赛倒计时」（final-countdown，裁决 W1–W3、design D1–D4）。出生棋盘 / 战场编号用 0 起的内部索引：规格里的"战场 1（A、B）/ 战场 4（C、D）"
/// 在这里是四人四区 [0,1,3,4] 经脚本缺省配对 (0,1)(3,4) 后的战场 0 与战场 3。
/// </summary>
/// <remarks>
/// 变异验证（final-countdown 1.3 实跑，脚本：备份 → 改 → 构建 → 只跑倒计时相关类 → finally 写回原字节并刷新时间戳；见 implement.md）：
/// M-FC1 到期不结算（<c>CountdownExpired</c> 的 <c>MajorRound &gt;= deadline</c> 改为 <c>MajorRound &lt; 0</c>）→ 红 9（本类 4、<c>战场结算Tests.倒计时到期强制结算</c>、
/// 先手预测「期限大回合的预测剔除将到期落败者」、存档往返、日志两条）；
/// M-FC2 起点 R 错一回合（<c>_countdownStart = completed</c> 改为 <c>completed + 1</c>）→ 红 13；
/// M-FC3 外部提交配对后按 <c>MajorRound</c> 而非 <c>MajorRound - 1</c> 计 → 红 1（「外部提交首轮配对后同样赋期限」）；
/// M-FC10 等待者判定的 <c>已结算 || 首轮配对步骤已完成</c> 改为 <c>&amp;&amp;</c> → 红 3（「首轮配对的等待者也触发倒计时」「之后出现的战场至少给满一个大回合」「外部提交首轮配对后同样赋期限」；W7 之前的实跑，前者现已改为「构筑期结算按第3大回合起算」）；
/// M-FC11 链接时不清两个旧标识的期限 → 红 1（「决赛战场不倒计时」）。
/// M-FC13（裁决 W7，tasks 1.5）去掉 R 的第 3 大回合下限（删 <c>UpdateFinalCountdown</c> 里的 <c>WaitingStarted</c> 早退）→ 红 2（「构筑期结算按第3大回合起算」「外部提交首轮配对后同样赋期限」）。
/// </remarks>
public class 决赛倒计时Tests
{
    /// <summary>四人四区 [0,1,3,4]，挂脚本缺省配对（首轮 (0,1)(3,4)，决赛唯一结果），决赛倒计时 K = <paramref name="k"/>。</summary>
    private static MatchFlow FourZones(int k, IPairingSource? source = null) =>
        Started([0, 1, 3, 4], source ?? new ScriptedPairingSource(), options: CountdownOptions(k));

    /// <summary>第 1–5 大回合四人都落子（首轮第 3 大回合结束时配对拼接），第 6 大回合战场 0 的 P0、P1 落子、战场 3 的 P2、P3 全部 Pass → 战场 3 第 6 大回合结束时整轮 Pass 结算。</summary>
    private static BattlefieldSettlementRecord SettleFieldThreeAtRoundSix(MatchFlow match)
    {
        match.PlayRounds(5, All);
        Assert.Empty(match.Settlements);
        Assert.Equal((0, 0, 3, 3), (match.BattlefieldOf(P0), match.BattlefieldOf(P1), match.BattlefieldOf(P2), match.BattlefieldOf(P3)));
        match.PlayRound(P0, P1);
        Assert.Equal(7, match.MajorRound);
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((6, 3, SettlementReason.AllPassed), (settled.MajorRound, settled.Battlefield, settled.Reason));
        Assert.Null(settled.Deadline);   // 战场 3 自己结算时还没有任何等待者，不在倒计时中
        return settled;
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 首轮一侧先结算即开始倒计时()
    {
        // 规格 Scenario：四人四区，战场 1（A、B）与战场 4（C、D）首轮交战；战场 4 在第 6 大回合结束时结算、C 胜，K = 2
        // → 战场 1 的期限为第 8 大回合结束；若届时未结算则按势力强制结算，两名胜者进入决赛。
        MatchFlow match = FourZones(k: 2);
        BattlefieldSettlementRecord first = SettleFieldThreeAtRoundSix(match);

        Assert.True(match.IsWaitingForFinal(first.Winner));
        Assert.Equal(6, match.FinalCountdownStart);
        Assert.Equal(8, match.DeadlineOf(0));
        Assert.Null(match.DeadlineOf(3));   // 已结算的战场不进倒计时
        FinalCountdownRecord record = Assert.Single(match.FinalCountdowns);
        Assert.Equal((1, 0, 6, 8, 6), (record.Sequence, record.Battlefield, record.Start, record.Deadline, record.AssignedInMajorRound));
        Assert.Contains(match.Events, e => e.Kind == FlowEventKind.FinalCountdown && e.MajorRound == 7);

        // 第 7 大回合：A、B 照常落子 → 不结算（R + K − 1）。
        match.PlayRound(P0, P1);
        Assert.Single(match.Settlements);
        Assert.Equal(PlayerStatus.Active, match.StateOf(P0).Status);
        Assert.Equal(PlayerStatus.Active, match.StateOf(P1).Status);

        // 第 8 大回合（R + K）：A、B 仍在落子 → 到期强制结算，两名胜者随即决赛链接（挂了脚本配对）。
        match.PlayRound(P0, P1);
        Assert.Equal(2, match.Settlements.Count);
        BattlefieldSettlementRecord expired = match.Settlements[1];
        Assert.Equal((8, 0, SettlementReason.Expired, (int?)8), (expired.MajorRound, expired.Battlefield, expired.Reason, expired.Deadline));
        Assert.True(expired.Entries[0].Power >= expired.Entries[1].Power);   // 按当前势力排：胜者势力不低于落败者
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(expired.Entries[1].Player).Status);
        Assert.Equal(EliminationReason.BattlefieldDefeat, match.StateOf(expired.Entries[1].Player).EliminationReason);
        Assert.Equal(PairingKind.Final, match.Pairings[^1].Kind);
        Assert.Equal(Siege.Core.Match.LinkStage.Final, match.LinkStage);
        Assert.Equal(new[] { first.Winner, expired.Winner }.Order(), match.Players.Where(p => match.StateOf(p).Status == PlayerStatus.Active).Order());
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 首轮配对的等待者也触发倒计时()
    {
        // 规格 Scenario：3 人同区、1 人独占，第 3 大回合结束时独占者成为首轮等待者，K = 2 → 三人战场的期限为第 5 大回合结束。
        MatchFlow match = Started([0, 0, 0, 3], new ScriptedPairingSource(), options: CountdownOptions(2));
        match.PlayRounds(2, P0, P1, P2);
        Assert.Null(match.FinalCountdownStart);   // 构筑期内独占者还要参加首轮配对步骤，不算等待者
        Assert.False(match.IsWaitingForFinal(P3));

        match.PlayRound(P0, P1, P2);
        Assert.Empty(match.Pairings);   // 候选只有 1 个：不配对，直接进入交战
        Assert.True(match.IsWaitingForFinal(P3));
        Assert.Equal(3, match.FinalCountdownStart);
        Assert.Equal(5, match.DeadlineOf(0));

        match.PlayRound(P0, P1, P2);
        Assert.Empty(match.Settlements);
        // 样本实况（夹具种子、按坐标序各落 1 子）：P1 第 2 大回合被提光、势力归零出局（不是本条要验的），三人战场此后在局 P0、P2 两人——仍是多人战场，
        // 期限照赋；到期时战场里仍在局的全部进入结算名单。
        Assert.Equal((PlayerStatus.Eliminated, EliminationReason.PowerZero, (int?)2), (match.StateOf(P1).Status, match.StateOf(P1).EliminationReason, match.StateOf(P1).EliminatedInMajorRound));
        int fighters = match.ActivePlayersIn(0).Length;
        Assert.Equal(2, fighters);
        match.PlayRound(P0, P1, P2);
        BattlefieldSettlementRecord expired = Assert.Single(match.Settlements);
        Assert.Equal((5, 0, SettlementReason.Expired), (expired.MajorRound, expired.Battlefield, expired.Reason));
        Assert.Equal(fighters, expired.Entries.Length);
        Assert.Equal(PairingKind.Final, Assert.Single(match.Pairings).Kind);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void K为0不启用()
    {
        // 规格 Scenario：对局配置 K = 0 → 任何战场都不会因到期结算，对局与引入本条之前逐步相同。同一局面下战场 3 第 6 大回合结算、胜者开始等待，
        // 战场 0 打到第 10 大回合也没有期限、不结算；不设起点、不发倒计时事件。等待者照实认定（与 K 无关）。
        MatchFlow match = FourZones(k: 0);
        BattlefieldSettlementRecord first = SettleFieldThreeAtRoundSix(match);
        match.PlayRounds(4, P0, P1);

        Assert.Equal(11, match.MajorRound);
        Assert.Single(match.Settlements);
        Assert.True(match.IsWaitingForFinal(first.Winner));
        Assert.Null(match.FinalCountdownStart);
        Assert.Empty(match.BattlefieldDeadlines);
        Assert.Empty(match.FinalCountdowns);
        Assert.DoesNotContain(match.Events, e => e.Kind == FlowEventKind.FinalCountdown);
    }

    /// <summary>
    /// 规格 Scenario「K 为 0 不启用」的回归面（design D3 / D4）：链接图五条决策序列基线以 K = 0 导出，与引入倒计时之前钉下的基线（<c>*-k0.txt</c>，改前原样另存）逐字节相同。
    /// 缺省 K = 2 的重钉见 <see cref="决策序列基线Tests"/>。
    /// </summary>
    [Theory(Skip = "open-map 段 B 删除")]
    [InlineData(1UL, AiDifficulty.Standard, "decisions-link1-seed1-k0.txt")]
    [InlineData(2UL, AiDifficulty.Standard, "decisions-link1-seed2-k0.txt")]
    [InlineData(3UL, AiDifficulty.Standard, "decisions-link1-seed3-k0.txt")]
    [InlineData(1UL, AiDifficulty.Easy, "decisions-link1-easy-seed1-k0.txt")]
    [InlineData(1UL, AiDifficulty.Hard, "decisions-link1-hard-seed1-k0.txt")]
    public void K为0时链接图决策序列与改前基线逐字节相同(ulong seed, AiDifficulty difficulty, string fixture)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", fixture);
        Assert.True(File.Exists(path), $"找不到改前基线：{path}");
        string expected = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = expected.Split('\n');
        Assert.True(lines.Count(l => l.Length > 0 && char.IsDigit(l[0])) >= 30, "改前基线小回合不足 30 个");   // 覆盖范围下界
        Assert.DoesNotContain(lines, l => l.StartsWith("C\t", StringComparison.Ordinal));

        // ai-seat-vision 1.4：*-k0.txt 是改前（AI 缺省全知）的产物，本测试守的是倒计时、不是视野——显式全知（改前缺省），期望不改。
        string actual = LinkDecisionSequence.Export(seed, 12, difficulty, attach: null, finalCountdown: 0, seatVision: false);

        Assert.Equal(expected, actual);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 无人等待者不倒计时()
    {
        // 规格 Scenario：四人四区首轮两个战场都在交战、尚无任何战场结算 → 两个战场都没有期限。
        MatchFlow match = FourZones(k: 2);
        match.PlayRounds(6, All);

        Assert.Empty(match.Settlements);
        Assert.All(All, p => Assert.False(match.IsWaitingForFinal(p)));
        Assert.Null(match.FinalCountdownStart);
        Assert.Null(match.DeadlineOf(0));
        Assert.Null(match.DeadlineOf(3));
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 构筑期结算按第3大回合起算()
    {
        // 规格 Scenario「构筑期结算按第 3 大回合起算」（裁决 W7）：2+1+1 布局，同区战场在第 2 大回合整轮 Pass 结算，K = 2
        // → R = 3（不是 2），第 3 大回合结束后拼出的首轮战场期限为第 5 大回合结束，至少可打满 K 个大回合。
        // 两人同区 [0,0,3,4]：第 1 大回合四人都落子；第 2 大回合 P0、P1 Pass（P2、P3 落子）→ 同区战场第 2 大回合结算。
        MatchFlow match = Started([0, 0, 3, 4], new ScriptedPairingSource(), options: CountdownOptions(2));
        match.PlayRound(All);
        Assert.Empty(match.Settlements);
        match.PlayRound(P2, P3);
        BattlefieldSettlementRecord early = Assert.Single(match.Settlements);
        Assert.Equal((2, 0), (early.MajorRound, early.Battlefield));
        Assert.True(match.IsWaitingForFinal(early.Winner));   // 等待者照实认定……
        Assert.Null(match.FinalCountdownStart);                // ……但构筑期内不起算
        Assert.Equal(0, match.StateOf(early.Winner).WaitingRounds);   // 与遥测口径一致：构筑期不计等待大回合

        match.PlayRound(P2, P3);
        Assert.Equal(3, match.BattlefieldOf(P2));
        Assert.Equal(3, match.FinalCountdownStart);
        Assert.Equal(5, match.DeadlineOf(3));
        FinalCountdownRecord record = Assert.Single(match.FinalCountdowns);
        Assert.Equal((3, 5, 3), (record.Start, record.Deadline, record.AssignedInMajorRound));

        match.PlayRound(P2, P3);
        Assert.Single(match.Settlements);
        Assert.Equal(1, match.StateOf(early.Winner).WaitingRounds);   // 第 4 大回合起计
        match.PlayRound(P2, P3);
        BattlefieldSettlementRecord expired = match.Settlements[^1];
        Assert.Equal((5, 3, SettlementReason.Expired, (int?)5), (expired.MajorRound, expired.Battlefield, expired.Reason, expired.Deadline));
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 外部提交首轮配对后同样赋期限()
    {
        // 不挂配对来源：第 3 大回合结束时停在待配对，独占者此刻还要被配对、不算等待者，也不给期限；外部提交配对执行拼接后，
        // 新战场按刚结束的第 3 大回合计期限（与挂接来源时当场配对同一结果）。
        MatchFlow match = Started([0, 0, 3, 4], options: CountdownOptions(2));
        match.PlayRound(P2, P3);
        match.PlayRounds(2, P2, P3);
        Assert.Equal(MatchPhase.AwaitingPairing, match.Phase);
        Assert.Equal(4, match.MajorRound);
        Assert.False(match.IsWaitingForFinal(P2));
        Assert.Empty(match.BattlefieldDeadlines);

        match.ApplyPairing([(3, 4)]);

        // 裁决 W7：同区胜者第 1 大回合就已结算，R 仍取 3 → 期限 3 + 2 = 5。
        Assert.Equal(3, match.FinalCountdownStart);
        Assert.Equal(5, match.DeadlineOf(3));
        Assert.Equal(3, Assert.Single(match.FinalCountdowns).AssignedInMajorRound);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 决赛战场不倒计时()
    {
        // 规格正文：决赛战场本身 MUST NOT 进入倒计时。接「首轮一侧先结算」：第 8 大回合到期结算后决赛链接，决赛两人此后一直落子，
        // 打满 4 个大回合也没有期限、不结算。
        MatchFlow match = FourZones(k: 2);
        SettleFieldThreeAtRoundSix(match);
        match.PlayRounds(2, P0, P1);
        Assert.Equal(Siege.Core.Match.LinkStage.Final, match.LinkStage);
        PlayerId[] finalists = [.. match.Players.Where(p => match.StateOf(p).Status == PlayerStatus.Active)];
        int finalField = match.BattlefieldOf(finalists[0]);
        Assert.Equal(finalField, match.BattlefieldOf(finalists[1]));

        match.PlayRounds(4, finalists);

        Assert.Equal(13, match.MajorRound);
        Assert.Equal(2, match.Settlements.Count);
        Assert.Null(match.DeadlineOf(finalField));
        Assert.All(finalists, p => Assert.False(match.IsWaitingForFinal(p)));
        Assert.Single(match.FinalCountdowns);
    }
}
