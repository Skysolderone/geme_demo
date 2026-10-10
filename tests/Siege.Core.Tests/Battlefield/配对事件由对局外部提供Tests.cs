using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/battlefield —— Requirement: 配对事件由对局外部提供（段 B 前半：校验、规范次序、逐对链接、内存事件；
/// 日志与存档序列化、"回放按记录配对"属 2.9）。战场编号 0 起：规格的"战场 1、2、4、5"即这里的 0、1、3、4。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：M-B5 <c>MatchFlow.Validate</c> 删去"漏了候选"检查 → 「非法配对被拒」的漏候选一行红；
/// M-B8 <c>ResolvePairing</c> 按事件列出顺序而不是规范次序链接 → 「列出顺序不影响结果」红 1。
/// </remarks>
public class 配对事件由对局外部提供Tests
{
    [Fact]
    public void 合法配对被接受()
    {
        // Scenario：候选为战场 1、2、4、5，外部提交 {(1,4),(2,5)} → 系统依次执行两次链接，进入交战阶段。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        match.ApplyPairing(Pairs(null, (0, 3), (1, 4)));

        Assert.Equal(MatchPhase.InProgress, match.Phase);
        PairingRecord record = Assert.Single(match.Pairings);
        Assert.Equal(PairingKind.FirstRound, record.Kind);
        Assert.Equal([(0, 3), (1, 4)], record.Pairs);
        Assert.Null(record.Waiter);
        Assert.Equal(4, record.MajorRound);
        Assert.Equal([(0, 3), (1, 4)], match.Links.Select(l => (l.X, l.Y)));
        Assert.Equal([1, 1], match.Links.Select(l => l.PairingSequence));
        Assert.Equal(2, match.Map.MapVersion);
        Assert.Equal(1, match.CountEvents(FlowEventKind.PairingApplied));
        Assert.Equal(2, match.CountEvents(FlowEventKind.BattlefieldsLinked));
        Assert.Null(match.PendingPairing);
    }

    [Fact]
    public void 奇数候选需要等待者()
    {
        // Scenario：候选为战场 2、4、5，外部提交 {(2,5)} 且指定 4 为等待者 → 执行一次链接，战场 4 的玩家原地等待。
        // 构造奇数候选：1+1+1+1 中 P0 在构筑期弃赛。
        MatchFlow match = Started([0, 1, 3, 4]);
        match.Resign(P0);
        match.PassRounds(3);
        Assert.Equal([1, 3, 4], match.PendingPairing!.Candidates);

        Assert.Throws<SiegeRuleException>(() => match.ApplyPairing([(1, 4)]));          // 奇数缺等待者
        Assert.Equal(MatchPhase.AwaitingPairing, match.Phase);
        match.ApplyPairing([(1, 4)], waiter: 3);

        Assert.Single(match.Links);
        Assert.Equal(3, match.Pairings.Single().Waiter);
        Assert.Equal([P2], match.ActivePlayersIn(3));
        Assert.Equal(1, match.BattlefieldOf(P3));
    }

    [Theory]
    [InlineData("非候选", new[] { 0, 2, 1, 4 }, -1)]          // 规格 Scenario：候选 1、2、4、5，提交 {(1,3),(2,5)}
    [InlineData("同一候选两次", new[] { 0, 3, 0, 4 }, -1)]
    [InlineData("一对两方相同", new[] { 0, 0, 1, 4 }, -1)]
    [InlineData("偶数却指定等待者", new[] { 0, 3, 1, 4 }, 1)]
    [InlineData("漏了候选", new[] { 0, 3 }, -1)]
    public void 非法配对被拒(string 情形, int[] flat, int waiter)
    {
        // Scenario：非法配对被拒 → 系统拒绝该事件，对局仍处于待配对状态，地图与记录都不变。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        (int, int)[] pairs = [.. Enumerable.Range(0, flat.Length / 2).Select(i => (flat[2 * i], flat[(2 * i) + 1]))];
        MapData before = match.Map;

        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => match.ApplyPairing(pairs, waiter < 0 ? null : waiter));
        Assert.False(string.IsNullOrEmpty(ex.Message), 情形);
        Assert.Equal(MatchPhase.AwaitingPairing, match.Phase);
        Assert.NotNull(match.PendingPairing);
        Assert.Same(before, match.Map);
        Assert.Empty(match.Pairings);
        Assert.Empty(match.Links);

        // 被拒之后仍可提交合法事件。
        match.ApplyPairing([(0, 3), (1, 4)]);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 等待者须是候选且不在配对里()
    {
        MatchFlow match = Started([0, 1, 3, 4]);
        match.Resign(P0);
        match.PassRounds(3);
        Assert.Throws<SiegeRuleException>(() => match.ApplyPairing([(1, 4)], waiter: 0));   // 0 不是候选（P0 已弃赛）
        Assert.Throws<SiegeRuleException>(() => match.ApplyPairing([(1, 4)], waiter: 4));   // 等待者同时在配对里
        Assert.Equal(MatchPhase.AwaitingPairing, match.Phase);
    }

    [Fact]
    public void 决赛只接受唯一合法结果()
    {
        MatchFlow match = Started([0, 0, 2, 2]).PassRounds(1);
        match.Debug.SettleBattlefield(0, P1);
        match.Debug.SettleBattlefield(2, P2);
        match.PassRounds(2);
        Assert.Equal(MatchPhase.AwaitingFinalLink, match.Phase);

        Assert.Throws<SiegeRuleException>(() => match.ApplyPairing([(0, 2)], waiter: 2));
        Assert.Throws<SiegeRuleException>(() => match.ApplyPairing([(0, 3)]));
        Assert.Throws<SiegeRuleException>(() => match.ApplyPairing(Array.Empty<(int, int)>()));
        Assert.Equal(MatchPhase.AwaitingFinalLink, match.Phase);
        match.ApplyPairing([(2, 0)]);      // 对内顺序无关紧要
        Assert.Equal(PairingKind.Final, match.Pairings.Single().Kind);
        Assert.Equal(LinkStage.Final, match.LinkStage);
    }

    [Fact]
    public void 列出顺序不影响结果()
    {
        // 裁决 R2：同一配对事件内按"含最小出生棋盘编号的一对先"逐对链接，与事件中列出的顺序和对内先后无关 → 同一配对集合得到同一地图。
        MatchFlow listed = Started([0, 1, 3, 4]).PassRounds(3);
        listed.ApplyPairing([(4, 1), (3, 0)]);
        MatchFlow canonical = Started([0, 1, 3, 4]).PassRounds(3);
        canonical.ApplyPairing([(0, 3), (1, 4)]);

        Assert.Equal([(0, 3), (1, 4)], listed.Links.Select(l => (l.X, l.Y)));
        Assert.Equal(MapFile.ToJson(canonical.Map), MapFile.ToJson(listed.Map));
        Assert.Equal(canonical.Board.Serialize(), listed.Board.Serialize());
    }

    [Fact]
    public void 不在待配对时拒绝配对事件()
    {
        MatchFlow match = Started([0, 1, 3, 4]);
        Assert.Throws<SiegeRuleException>(() => match.ApplyPairing([(0, 3), (1, 4)]));
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Empty(match.Links);
    }

    [Fact]
    public void 脚本固定配对的确定性缺省()
    {
        // 实现裁决 B3：按编号升序两两相配，奇数时编号最大者等待；决赛给唯一合法结果。Core 不抽随机数。
        PairingDecision even = ScriptedPairingSource.Default(new PairingRequest(PairingKind.FirstRound, [4, 0, 3, 1], 4));
        Assert.Equal([(0, 1), (3, 4)], even.Canonical);
        Assert.Null(even.Waiter);
        PairingDecision odd = ScriptedPairingSource.Default(new PairingRequest(PairingKind.FirstRound, [1, 3, 4], 4));
        Assert.Equal([(1, 3)], odd.Canonical);
        Assert.Equal(4, odd.Waiter);
        PairingDecision final = ScriptedPairingSource.Default(new PairingRequest(PairingKind.Final, [3, 0], 6));
        Assert.Equal([(0, 3)], final.Canonical);

        // 脚本优先：先给脚本里的结果，用完回落缺省。
        var source = new ScriptedPairingSource(Pairs(null, (0, 3), (1, 4)));
        MatchFlow match = Started([0, 1, 3, 4], source).PassRounds(3);
        Assert.Equal([(0, 3), (1, 4)], match.Pairings.Single().Pairs);
    }

    [Fact]
    public void 来源给出非法结果时停在待配对()
    {
        var source = new ScriptedPairingSource(Pairs(null, (0, 2), (1, 4)));
        MatchFlow match = Started([0, 1, 3, 4], source).PassRounds(2);
        Assert.Throws<SiegeRuleException>(() => match.PassRound());
        Assert.Equal(MatchPhase.AwaitingPairing, match.Phase);
        Assert.Empty(match.Links);
        match.ApplyPairing([(0, 3), (1, 4)]);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 无来源时跑到底报待配对()
    {
        MatchFlow match = Started([0, 1, 3, 4]);
        var runner = new MatchRunner(match);
        foreach (PlayerId p in All)
        {
            runner.SetController(p, new PassController());
        }

        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => runner.RunToEnd());
        Assert.Contains("AwaitingPairing", ex.Message, StringComparison.Ordinal);
    }

    private sealed class PassController : ITurnController
    {
        public void OrganizeHand(PlayerHandAccess hand, int overflow)
        {
        }

        public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
        {
        }

        public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
        {
        }

        public bool OnRejected(StagedBatch batch, BatchFailure failure) => false;
    }
}
