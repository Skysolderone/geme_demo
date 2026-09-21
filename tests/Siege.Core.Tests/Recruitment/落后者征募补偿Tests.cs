using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.Recruitment;

/// <summary>
/// 规格：recruitment —— Requirement: 落后者征募补偿（catch-up-recruit 裁决 1–4）。
/// </summary>
/// <remarks>
/// <para>盘面用「阶梯」局面代替规格里的 60/45/30/20：规则只看名次，故每个用例先把<b>名次</b>断言死（前提用断言钉住），再看补偿。
/// 阶梯盘面（9×9 合成图，四角各一簇）势力可独立复算：势力 = 棋子基础军势 + 独占空格数（restore-go-core-rules 段 A：领地重新计分）。
/// P0 A1-D1 四枚 + 独占 5（A2 B2 C2 D2 E1）→ 9；P1 G1 H1 J1 三枚 + 独占 4（G2 H2 J2 F1）→ 7；P2 A9 B9 两枚 + 独占 3（A8 B8 C9）→ 5；P3 J9 一枚 + 独占 2（H9 J8）→ 3。名次 1 / 2 / 3 / 4。
/// 段 A 重算：原势力 4 / 3 / 2 / 1（scoring-sites：独占空格不计分）→ 9 / 7 / 5 / 3（与 territory-power 时期同值），名次与补偿期望全部不变。</para>
/// <para>变异验证（基线 653 绿；每条都是 cp 备份 → 变异 → 跑全量 → 还原 → 逐字节 cmp）：</para>
/// <list type="table">
/// <item><term>M-CU1</term><description>阈值 <c>(n+1)/2</c> 写成 <c>n/2</c> → 红 4：阈值按人数取上取整（3 人 / 5 人两行）、出局与弃赛者不计入、弃赛者势力可见但不参与</description></item>
/// <item><term>M-CU1b</term><description>阈值比较 <c>rank &gt; threshold</c> 改 <c>&gt;=</c> → 红 8：前半名次无补偿、阈值真值表 4 行、出局与弃赛者不计入、弃赛者势力可见但不参与、真实跑局把补偿写进快照与首部</description></item>
/// <item><term>M-CU2</term><description><c>PowerCalculator.Rank</c> 让并列各占独立名次 → 红 7：全员并列无补偿、并列最后一名都获得补偿、出局与弃赛者不计入，外加 power-score / initiative-order 的既有并列守门</description></item>
/// <item><term>M-CU3</term><description>参赛人数改用名册全员数（弃赛者计入）→ 红 1：出局与弃赛者不计入</description></item>
/// <item><term>M-CU4</term><description><c>MatchFlow.CurrentSnapshot</c> 改成回读账本与此刻名次的活视图 → 红 4：小回合内名次变化不影响本回合、快照读取开始时的名次、快照在回合内不变、超限阻断征募</description></item>
/// <item><term>M-CU5</term><description>最后一名条件去掉 <c>rank &gt; 1</c> → 红 3：全员并列无补偿、弃赛者势力可见但不参与、真实跑局把补偿写进快照与首部</description></item>
/// <item><term>M-CU6</term><description>来源拆分把补偿折进信物来源 → 红 1：显示落后补偿来源</description></item>
/// <item><term>M-CU7</term><description>补偿判定忽略对局配置开关 → 红 1：关闭补偿</description></item>
/// <item><term>M-CU8</term><description><c>Serialize</c> 不写补偿开关 → 红 1：旧存档回填</description></item>
/// <item><term>M-CU9</term><description>小回合快照不写补偿留痕 → 红 1：真实跑局把补偿写进快照与首部</description></item>
/// <item><term>M-CU10</term><description>报告口径不再排除"缺留痕"的日志 → 红 1：报告落后补偿段落含被排除样本</description></item>
/// <item><term>M-CU11</term><description>报告占比分母改成全部局的小回合数 → 红 1：同上</description></item>
/// </list>
/// </remarks>
public class 落后者征募补偿Tests
{
    private static readonly PlayerId P0 = MatchFixtures.P0;
    private static readonly PlayerId P1 = MatchFixtures.P1;
    private static readonly PlayerId P2 = MatchFixtures.P2;
    private static readonly PlayerId P3 = MatchFixtures.P3;

    /// <summary>阶梯局面：势力 9 / 7 / 5 / 3，名次 1 / 2 / 3 / 4。<paramref name="first"/> 是即将开始小回合的玩家。</summary>
    private static MatchFlow Ladder(PlayerId first, MatchOptions? options = null, PlayerId[]? rest = null, params (string Cell, RelicContent Content)[] relics)
    {
        PlayerId[] order = [first, .. rest ?? [.. MatchFixtures.All.Where(p => p != first)]];
        return MatchFixtures.Started(options: options ?? MatchOptions.Immediate, relics: relics)
            .AtRound(5, order)
            .Stones(P0, "A1", "B1", "C1", "D1")
            .Stones(P1, "G1", "H1", "J1")
            .Stones(P2, "A9", "B9")
            .Stones(P3, "J9");
    }

    /// <summary>把已经 <c>BeginTurn</c> 的小回合走完（征募不选、Pass 结算）。</summary>
    private static void FinishStartedTurn(MatchFlow match)
    {
        match.EnterRecruit();
        match.EnterDeploy();
        Assert.True(match.Confirm().Confirmed);
    }

    /// <summary>把名次与势力一次性钉住，避免下游用莫名其妙的数字失败。</summary>
    private static void AssertRanks(MatchFlow match, params (PlayerId Player, long Total, int Rank)[] expected)
    {
        PowerSnapshot power = match.Scoreboard.Latest!;
        Assert.Equal(
            [.. expected.Select(e => $"{e.Player} 势力{e.Total} 名次{e.Rank}")],
            [.. expected.Select(e => $"{e.Player} 势力{power.Of(e.Player).Total} 名次{power.RankOf(e.Player)}")]);
    }

    [Fact]
    public void 四人局第3与第4名()
    {
        // 规格 Scenario「4 人局第 3、4 名」：D 名次 4 > ⌈4÷2⌉=2 → 展示 +1；D 是最后一名 → 选取 +1；面板展示 6、选取 4。
        MatchFlow match = Ladder(P3);
        AssertRanks(match, (P0, 9, 1), (P1, 7, 2), (P2, 5, 3), (P3, 3, 4));

        match.BeginTurn();
        EffectSnapshot snapshot = match.CurrentSnapshot!;

        Assert.Equal(new CatchUpBonus(1, 1), snapshot.CatchUp);
        Assert.Equal((6, 4), (snapshot.RevealCount, snapshot.FreePickCount));
        RecruitPanelView panel = match.EnterRecruit();
        Assert.Equal((6, 4), (panel.ShowCount, panel.FreePickCount));
        Assert.Equal(new CatchUpBonus(1, 1), panel.CatchUp);
    }

    [Fact]
    public void 四人局第3名只加展示()
    {
        // 规格 Scenario「4 人局第 3 名只加展示」：C 名次 3 > 2 → 展示 +1；C 不是最后一名（最大名次 4）→ 选取不加；面板展示 6、选取 3。
        // 本用例是「两档分开」的证伪点：第 3 名只拿展示，不拿选取。
        MatchFlow match = Ladder(P2);
        AssertRanks(match, (P2, 5, 3), (P3, 3, 4));

        match.BeginTurn();
        EffectSnapshot snapshot = match.CurrentSnapshot!;

        Assert.Equal(new CatchUpBonus(1, 0), snapshot.CatchUp);
        Assert.Equal((6, 3), (snapshot.RevealCount, snapshot.FreePickCount));
        Assert.Equal((6, 3), (match.EnterRecruit().ShowCount, match.CurrentHand().Panel().FreePickCount));
    }

    [Fact]
    public void 前半名次无补偿()
    {
        // 规格 Scenario「前半名次无补偿」：B 名次 2，不大于 ⌈4÷2⌉=2 → 无任何补偿，面板展示 5、选取 3。
        // M-CU1b（阈值比较改 ≥）在本用例红：B 会拿到展示 +1。
        MatchFlow match = Ladder(P1);
        AssertRanks(match, (P0, 9, 1), (P1, 7, 2));

        match.BeginTurn();
        EffectSnapshot snapshot = match.CurrentSnapshot!;

        Assert.Equal(CatchUpBonus.None, snapshot.CatchUp);
        Assert.False(snapshot.CatchUp.IsAny);
        Assert.Equal((5, 3), (snapshot.RevealCount, snapshot.FreePickCount));
        Assert.Equal((5, 3), (match.EnterRecruit().ShowCount, match.CurrentHand().Panel().FreePickCount));
    }

    [Fact]
    public void 并列最后一名都获得补偿()
    {
        // 规格 Scenario「并列最后一名都获得补偿」：势力 9 / 7 / 5 / 5（本盘恰为此值）→ C、D 共享名次 3（竞争名次，最大名次是 3 不是 4），二者均展示 +1、选取 +1。
        // P3 补一枚 H9 与 P2 的 A9 B9 镜像对称：2 枚 + 独占 3（J8 H8 G9）= 5（段 A 重算：原 2 → 5）。
        // M-CU2（并列各占独立名次）在本用例红：P3 会被排到名次 4，越过最大名次 3。
        MatchFlow match = MatchFixtures.Started()
            .AtRound(5, [P2, P3, P0, P1])
            .Stones(P0, "A1", "B1", "C1", "D1")
            .Stones(P1, "G1", "H1", "J1")
            .Stones(P2, "A9", "B9")
            .Stones(P3, "J9", "H9");
        AssertRanks(match, (P0, 9, 1), (P1, 7, 2), (P2, 5, 3), (P3, 5, 3));
        Assert.Equal(3, match.Scoreboard.Latest!.Ranking[^1].Rank);

        match.BeginTurn();
        Assert.Equal(new CatchUpBonus(1, 1), match.CurrentSnapshot!.CatchUp);
        Assert.Equal((6, 4), (match.CurrentSnapshot!.RevealCount, match.CurrentSnapshot!.FreePickCount));
        FinishStartedTurn(match);   // 结束 P2 已开始的小回合；Pass 不改盘面，并列前提在下面重新断言

        AssertRanks(match, (P2, 5, 3), (P3, 5, 3));
        match.BeginTurn();
        Assert.Equal(P3, match.CurrentPlayer);
        Assert.Equal(new CatchUpBonus(1, 1), match.CurrentSnapshot!.CatchUp);
        Assert.Equal((6, 4), (match.CurrentSnapshot!.RevealCount, match.CurrentSnapshot!.FreePickCount));
    }

    [Fact]
    public void 全员并列无补偿()
    {
        // 规格 Scenario「全员并列无补偿」：第 1 大回合开始时全员势力 0 → 共享名次 1；名次 1 既不 > ⌈4÷2⌉=2，也不满足「> 1」→ 无人补偿。
        // 这条同时是裁决 3「保护期内照常生效、无需特判」的依据。
        // M-CU5（去掉 `rank > 1`）在本用例红：全员会拿到选取 +1。
        MatchFlow match = MatchFixtures.Started();
        Assert.Equal(1, match.MajorRound);
        RankGroup all = Assert.Single(match.Scoreboard.Latest!.Ranking);
        Assert.Equal((1, 0L), (all.Rank, all.Power));
        Assert.Equal(4, all.Players.Length);

        match.BeginTurn();
        EffectSnapshot snapshot = match.CurrentSnapshot!;
        Assert.Equal(CatchUpBonus.None, snapshot.CatchUp);
        Assert.Equal((5, 3), (snapshot.RevealCount, snapshot.FreePickCount));
        Assert.Equal((5, 3), (match.EnterRecruit().ShowCount, match.CurrentHand().Panel().FreePickCount));
    }

    [Fact]
    public void 二人局()
    {
        // 规格 Scenario「2 人局」：2 人局 ⌈2÷2⌉=1，B 名次 2 > 1 → 展示 +1；B 同时是最后一名 → 选取 +1。
        // 这条钉住阈值的整数写法：若写成 `rank * 2 > participantCount`，3 人局的第 2 名会被误判（见「出局与弃赛者不计入」）。
        MatchFlow match = MatchFixtures.TwoPlayer(order: [P1, P0])
            .Stones(P0, "A1", "B1", "C1", "D1")
            .Stones(P1, "J9");
        AssertRanks(match, (P0, 9, 1), (P1, 3, 2));
        Assert.Equal(2, match.Scoreboard.Latest!.Ranking.Sum(g => g.Players.Length));

        match.BeginTurn();
        Assert.Equal(P1, match.CurrentPlayer);
        Assert.Equal(new CatchUpBonus(1, 1), match.CurrentSnapshot!.CatchUp);
        Assert.Equal((6, 4), (match.CurrentSnapshot!.RevealCount, match.CurrentSnapshot!.FreePickCount));
    }

    [Fact]
    public void 与信物加成相加()
    {
        // 规格 Scenario「与信物加成相加」：D 为最后一名并控制 1 枚探勘 → 展示 5 + 1（探勘）+ 1（补偿）= 7，选取 3 + 1（补偿）= 4。
        // D 的唯一一枚棋子就落在信物格 J9 上（占据即控制），故势力仍为 3（1 子 + 独占 H9 J8）、名次仍为 4。
        MatchFlow match = Ladder(P3, relics: ("J9", RelicFixtures.Prospecting()));
        AssertRanks(match, (P3, 3, 4));
        Assert.Contains(match.Relics.PublicStates(), s => s.Coord == TestMaps.At("J9") && s.Control.GrantsEffectTo(P3));

        match.BeginTurn();
        EffectSnapshot snapshot = match.CurrentSnapshot!;

        Assert.Equal(new CatchUpBonus(1, 1), snapshot.CatchUp);
        Assert.Equal((7, 4), (snapshot.RevealCount, snapshot.FreePickCount));
        Assert.Equal((7, 4), (match.EnterRecruit().ShowCount, match.CurrentHand().Panel().FreePickCount));
    }

    [Fact]
    public void 小回合内名次变化不影响本回合()
    {
        // 规格 Scenario「小回合内名次变化不影响本回合」（裁决 2 / D3）：D 以最后一名开始小回合拿到补偿，本回合内势力反超升到第 1 名，
        // 已生成的快照与面板参数保持补偿后的数值，不回收；下一次判定按届时名次重新算（此处以补充载荷的结构参数证明 D 已不再有补偿）。
        // M-CU4（把 CurrentSnapshot 改成回读此刻名次的活视图）在本用例红：快照会变回 5 / 3。
        MatchFlow match = Ladder(P3);
        AssertRanks(match, (P3, 3, 4));

        match.BeginTurn();
        RecruitPanelView panel = match.EnterRecruit();
        Assert.Equal((6, 4), (panel.ShowCount, panel.FreePickCount));

        // 小回合进行中直接改盘并重算：D 变成第 1 名
        match.Stones(P3, "E5", "E6", "E4", "D5", "F5", "D6", "F6", "D4", "F4");
        // 段 A 重算：P3 原 10 → 24 = 10 子（J9 + D4–F6 九宫）+ 独占 14（J9 的 H9 J8；九宫外圈 D3 E3 F3 / D7 E7 F7 / C4 C5 C6 / G4 G5 G6）；P0 原 4 → 9。
        AssertRanks(match, (P3, 24, 1), (P0, 9, 2));

        Assert.Equal(new CatchUpBonus(1, 1), match.CurrentSnapshot!.CatchUp);
        Assert.Equal((6, 4), (match.CurrentSnapshot!.RevealCount, match.CurrentSnapshot!.FreePickCount));
        Assert.Equal((6, 4), (match.CurrentHand().Panel().ShowCount, match.CurrentHand().Panel().FreePickCount));

        // 重新判定（下一次读名次）：D 已是第 1 名，结构参数里不再有落后补偿
        Siege.Core.Preview.StructureParameters now =
            match.PublishSupplement().Structures.Single(s => s.Player == P3).Parameters!;
        Assert.Equal((5, 0), (now.RevealCount.Value, now.RevealCount.CatchUp));
        Assert.Equal((3, 0), (now.FreePickCount.Value, now.FreePickCount.CatchUp));
    }

    [Fact]
    public void 出局与弃赛者不计入()
    {
        // 规格 Scenario「出局与弃赛者不计入」：D 已弃赛 → 参赛人数 3，⌈3÷2⌉=2；C 名次 3 > 2 且是最后一名 → 展示 +1、选取 +1；D 不获补偿。
        // 同时钉住阈值写法：3 人局的第 2 名（B）不得获得展示 +1——`rank * 2 > participantCount` 会把它算成 4 > 3 而误判。
        // M-CU3（参赛人数改用名册全员数）在本用例红：末段 2 人局会被按 4 人算，阈值从 1 变 2，B 拿不到展示 +1。
        MatchFlow match = Ladder(P3, rest: [P2, P1, P0]);
        match.Resign(P3);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(P3).Status);
        AssertRanks(match, (P0, 9, 1), (P1, 7, 2), (P2, 5, 3));
        Assert.Null(match.Scoreboard.Latest!.RankOf(P3));
        Assert.Equal(3, match.Scoreboard.Latest!.Ranking.Sum(g => g.Players.Length));

        // 弃赛者自己拿不到补偿（判定函数对不在名次中的玩家返回"无"）
        Assert.Equal(CatchUpBonus.None, CatchUpCompensation.For(match.Scoreboard.Latest, P3, enabled: true));

        Assert.Equal(P2, match.CurrentPlayer);
        match.BeginTurn();
        Assert.Equal(new CatchUpBonus(1, 1), match.CurrentSnapshot!.CatchUp);
        Assert.Equal((6, 4), (match.CurrentSnapshot!.RevealCount, match.CurrentSnapshot!.FreePickCount));
        FinishStartedTurn(match);

        // 3 人局的第 2 名不在"后半"：⌈3÷2⌉=2，名次 2 不大于 2
        Assert.Equal(P1, match.CurrentPlayer);
        match.BeginTurn();
        Assert.Equal(CatchUpBonus.None, match.CurrentSnapshot!.CatchUp);
        Assert.Equal((5, 3), (match.CurrentSnapshot!.RevealCount, match.CurrentSnapshot!.FreePickCount));
        FinishStartedTurn(match);

        // 再弃一人 → 参赛人数 2，⌈2÷2⌉=1：同一个 B、同样的名次 2，这次就落进"后半"了。
        // 这一步是「弃赛者不计入人数」的证伪点：若把弃赛者算进人数（仍按 4 人、阈值 2），B 只会拿到选取 +1、展示仍是 5。
        match.Debug.SetPassStreak(0);   // 前两回合都是 Pass；弃赛后参赛人数降到 2，不清零会立刻触发"全员 Pass"终局
        match.Resign(P2);
        Assert.Equal(2, match.Scoreboard.Latest!.Ranking.Sum(g => g.Players.Length));
        AssertRanks(match, (P0, 9, 1), (P1, 7, 2));
        match.Debug.SetOrder(P1, P0);

        match.BeginTurn();
        Assert.Equal(P1, match.CurrentPlayer);
        Assert.Equal(new CatchUpBonus(1, 1), match.CurrentSnapshot!.CatchUp);
        Assert.Equal((6, 4), (match.CurrentSnapshot!.RevealCount, match.CurrentSnapshot!.FreePickCount));
    }

    [Fact]
    public void 关闭补偿()
    {
        // 规格 Scenario「关闭补偿」：对局配置关闭 → 任何名次都不产生补偿，面板只由基础值与信物决定（这里 D 控制 1 枚探勘 → 6 / 3）。
        // M-CU7（判定忽略开关）在本用例红。
        MatchFlow match = Ladder(P3, options: MatchFixtures.CatchUpOff, relics: ("J9", RelicFixtures.Prospecting()));
        Assert.False(match.CatchUpRecruit);
        AssertRanks(match, (P3, 3, 4));

        match.BeginTurn();
        EffectSnapshot snapshot = match.CurrentSnapshot!;

        Assert.Equal(CatchUpBonus.None, snapshot.CatchUp);
        Assert.Equal((6, 3), (snapshot.RevealCount, snapshot.FreePickCount));
        Assert.Equal((6, 3), (match.EnterRecruit().ShowCount, match.CurrentHand().Panel().FreePickCount));
    }

    [Theory]
    [InlineData(4, 2, "0,0|0,0|1,0|1,1")]
    [InlineData(3, 2, "0,0|0,0|1,1")]
    [InlineData(2, 1, "0,0|1,1")]
    [InlineData(5, 3, "0,0|0,0|0,0|1,0|1,1")]
    public void 阈值按人数取上取整(int participants, int threshold, string expected)
    {
        // 判定实现的逐名次真值表（唯一实现 CatchUpCompensation.Evaluate）：⌈n÷2⌉ = 4→2、3→2、2→1、5→3。
        // 独立算式：阈值在测试内用 (int)Math.Ceiling(n / 2.0) 另算一遍，不调用被测的整数写法。
        // M-CU1（`(n+1)/2` 写成 `n/2`）在本用例红 2 行（3 人局与 5 人局）。
        Assert.Equal(threshold, (int)Math.Ceiling(participants / 2.0));
        string actual = string.Join('|', Enumerable.Range(1, participants).Select(rank =>
        {
            CatchUpBonus bonus = CatchUpCompensation.Evaluate(rank, participants, participants);
            return $"{bonus.RevealBonus},{bonus.PickBonus}";
        }));
        Assert.Equal(expected, actual);
    }
}
