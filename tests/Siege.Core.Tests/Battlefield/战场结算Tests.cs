using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/battlefield —— Requirement: 战场结算（段 B 后半 2.6）；
/// elimination-endgame「出局判定」的"战场落败"；initiative-order「行动顺序跨战场统一」的"出局者出序"。
/// 战场编号 0 起：规格的"战场 1"即这里的 0。
/// </summary>
/// <remarks>
/// 变异验证（段 B 后半实跑）：
/// M-B10 整轮 Pass 改读全局 <c>_passStreak &gt;= ActiveCount</c>（不按战场逐人记）→ 「他战场有人落子不妨碍本战场整轮Pass结算」红 1；
/// M-B11 "在局 &lt; 2 的战场不检查"放宽为 &lt; 1（单人战场也参与结算）→ 本类红 10（含「等待者Pass不结算」）；
/// M-B12 <c>Resign</c> 不做减员结算 → 「减员结算」红 1。
/// </remarks>
public class 战场结算Tests
{
    [Fact]
    public void 整轮Pass结算()
    {
        // Scenario：战场 1 内 A、B 同一大回合各自 Pass，A 势力高于 B → 该大回合结束时战场 1 结算，A 胜，B 以"战场落败"出局且势力记为结算时的值（> 0）。
        MatchFlow match = Started([0, 0, 3, 4]).PlayRound(P0, P1).PlayRound(P0);
        Assert.Empty(match.Settlements);
        System.Numerics.BigInteger bPower = match.Scoreboard.Latest!.Of(P1).Total;
        Assert.True(bPower > 0);
        Assert.True(match.Scoreboard.Latest!.Of(P0).Total > bPower);

        match.PassRound();

        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, SettlementReason.AllPassed, P0, 3), (settled.Battlefield, settled.Reason, settled.Winner, settled.MajorRound));
        PlayerFlowState b = match.StateOf(P1);
        Assert.Equal(PlayerStatus.Eliminated, b.Status);
        Assert.Equal(EliminationReason.BattlefieldDefeat, b.EliminationReason);
        Assert.Equal(bPower, b.PowerAtDefeat);
        Assert.Equal(PlayerStatus.Active, match.StateOf(P0).Status);
        Assert.Null(match.StateOf(P0).EliminationReason);
        Assert.Equal([P0, P1], settled.Entries.Select(e => e.Player));
        Assert.Equal([1, 2], settled.Entries.Select(e => e.Rank));
        Assert.Equal(1, match.CountEvents(FlowEventKind.BattlefieldSettled));
    }

    [Fact]
    public void 一方落子即不结算()
    {
        // Scenario：战场 1 内 A Pass、B 落子 1 枚 → 本大回合结束时战场 1 不结算。
        MatchFlow match = Started([0, 0, 3, 4]).PlayRound(P1);
        Assert.Empty(match.Settlements);
        Assert.Equal(4, match.ActiveCount);
        Assert.Empty(match.PassedThisRound);   // 大回合结束时清空
    }

    [Fact]
    public void 他战场有人落子不妨碍本战场整轮Pass结算()
    {
        // 整轮 Pass 按战场判定：战场 1 的 A、B 都 Pass，另一战场的 C 落子 → 战场 1 照样结算（不读全局连续 Pass 计数）。
        MatchFlow match = Started([0, 0, 3, 4]).AtRound(1, [P0, P1, P2, P3]).PlayRound(P2);
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, SettlementReason.AllPassed), (settled.Battlefield, settled.Reason));
    }

    [Fact]
    public void 填满结算()
    {
        // Scenario：一次结算后战场 1 内不存在可落子空格 → 该大回合结束时战场 1 结算（原因"填满"，即便有人本大回合落了子）。
        MatchFlow match = Started([0, 0, 3, 4]).AtRound(1, [P0, P1, P2, P3]);
        match.PlaceOne();
        bool toP1 = false;
        foreach (Coord c in match.Board.Battlefields.ById(0)!.Cells.Where(c => match.Board[c].Occupant is null))   // link-map-terrain：填满整个战场（出生区已缩成核心区）
        {
            match.Board.Place(c, toP1 ? P1 : P0, PieceType.Basic);
            toP1 = !toP1;
        }

        match.Debug.Recalculate();
        match.PassRound();

        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal(SettlementReason.BoardFull, settled.Reason);
        Assert.Equal(0, settled.Battlefield);
        Assert.Equal(3, match.ActiveCount);
    }

    [Fact]
    public void 三人战场结算()
    {
        // Scenario：战场 1 内 A、B、C 同一大回合全部 Pass → A（势力最高）胜，B、C 同时以"战场落败"出局（同一出局序号），
        // 二者名次按比较链排序、只有链全同才共享（信物数这一级的比较由「链接模式终局名次Tests」用构造输入钉住）。
        // link-map-terrain：棋盘带主题地形后，"P0 多落一枚就势力最高"不再由几何保证（崖壁 / 林地会改覆盖与领地，三人可能并列）；
        // 断言的是三人战场结算的机制：胜者是结算时势力最高者（并列由比较链决出）、另两人同一出局序号、名次按比较链——不是谁赢。
        MatchFlow match = Started([0, 0, 0, 3]).PlayRound(P0, P1, P2).PlayRound(P0);
        PlayerId[] three = [P0, P1, P2];
        Dictionary<PlayerId, System.Numerics.BigInteger> power = three.ToDictionary(p => p, p => match.Scoreboard.Latest!.Of(p).Total);
        match.PassRound();

        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        PlayerId a = settled.Winner;
        Assert.Contains(a, three);
        Assert.All(three, p => Assert.True(power[p] <= power[a], $"{p} 势力 {power[p]} 高于胜者 {a} 的 {power[a]}"));
        Assert.Equal(3, settled.Entries.Length);
        PlayerId[] losers = [.. three.Where(p => p != a)];
        int order = match.StateOf(losers[0]).EliminationOrder!.Value;
        Assert.Equal(order, match.StateOf(losers[1]).EliminationOrder);
        Assert.Equal(order, settled.EliminationOrder);
        SettlementEntry second = settled.Entries[1];
        SettlementEntry third = settled.Entries[2];
        bool tied = (second.Power, second.ControlledRelics, second.ExclusiveCells, second.Stones) == (third.Power, third.ControlledRelics, third.ExclusiveCells, third.Stones);
        Assert.Equal(tied ? 2 : 3, third.Rank);
        Assert.Equal(2, second.Rank);
    }

    [Fact]
    public void 减员结算()
    {
        // Scenario：双人战场 4 内玩家 D 弃赛 → 战场 4 立即标记为已结算，C 为胜者；转换仍等到大回合结束。
        MatchFlow match = Started([0, 1, 3, 3]).AtRound(2, [P0, P1, P2, P3]);
        match.Resign(P3);

        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((3, SettlementReason.Attrition, P2), (settled.Battlefield, settled.Reason, settled.Winner));
        Assert.Null(settled.EliminationOrder);
        Assert.Equal(match.Scoreboard.Latest!.Of(P2).Total, settled.Entries.Single().Power);
        Assert.True(match.IsBattlefieldSettled(3));
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(P3).Status);
    }

    [Fact]
    public void 势力归零的减员结算()
    {
        // 多人战场因势力归零只剩 1 人 → 同样即时结算；被提光者记"势力归零"。
        MatchFlow match = Started([0, 0, 3, 4]).AtRound(2, [P0, P1, P2, P3]);
        Coord corner = BirthCell(match.Map, 0, 0, 0);
        match.Place(P1, corner);
        Assert.True(match.StateOf(P1).HasEstablishedPower);
        // P0 在角的两个气上落子提走 P1（角上一子只有两口气）。
        StagedBatch batch = match.ToDeploy();
        Assert.Null(batch.Stage(BirthCell(match.Map, 0, 1, 0), PieceType.Basic));
        Assert.Null(batch.Stage(BirthCell(match.Map, 0, 0, 1), PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);

        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(P1).Status);
        Assert.Equal(EliminationReason.PowerZero, match.StateOf(P1).EliminationReason);
        Assert.Null(match.StateOf(P1).PowerAtDefeat);
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, SettlementReason.Attrition, P0), (settled.Battlefield, settled.Reason, settled.Winner));
    }

    [Fact]
    public void 弃赛后按剩余在局者判整轮Pass()
    {
        // 整轮 Pass 按战场内的在局玩家判定：三人战场 1 内 A、B 都 Pass，C 未行动即在小回合边界弃赛 → 本大回合结束时战场 1 按剩余的 A、B 判整轮 Pass 结算；
        // 弃赛者既不需要 Pass，也不进结算名次。retire-legacy-rules 质检补测 Q2：接替已删的旧「出局 / 弃赛改变参赛人数后 Pass 计数重比」两条（链接口径按战场逐人记，不读全局计数）。
        // 变异验证 M-Q2（质检实跑）：JudgeAtRoundEnd 的整轮 Pass 改按战场内全部未出局玩家（含弃赛者）判 → 本条与「主动弃赛Tests.弃赛名次不影响最终名次」红 2。
        MatchFlow match = Started([0, 0, 0, 3]).AtRound(1, [P0, P1, P2, P3]);
        match.PassTurn();   // P0
        match.PassTurn();   // P1
        match.Resign(P2);   // 未 Pass 即弃赛；三人战场剩 2 人，不触发减员结算
        Assert.Equal([P0, P1], match.PassedThisRound.Order());
        Assert.Empty(match.Settlements);
        Assert.Equal(P3, match.CurrentPlayer);

        match.PassTurn();   // P3（单人战场，不结算）→ 第 1 大回合结束

        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, SettlementReason.AllPassed, 1), (settled.Battlefield, settled.Reason, settled.MajorRound));
        Assert.Equal([P0, P1], settled.Entries.Select(e => e.Player).Order());
        PlayerId loser = settled.Entries[1].Player;
        Assert.Equal(EliminationReason.BattlefieldDefeat, match.StateOf(loser).EliminationReason);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(P2).Status);
        Assert.Null(match.StateOf(P2).EliminationReason);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(2, match.ActiveCount);
    }

    [Fact]
    public void 等待者Pass不结算()
    {
        // Scenario：单人战场 3 内的玩家 D 连续三个大回合 Pass → 战场 3 不结算，D 仍在局；等待大回合数逐轮累计（构筑期不计）。
        MatchFlow match = Started([0, 0, 0, 2]).PlayRounds(6, P0);
        Assert.False(match.IsBattlefieldSettled(2));
        Assert.Empty(match.Settlements);
        Assert.Equal(PlayerStatus.Active, match.StateOf(P3).Status);
        Assert.Equal(3, match.StateOf(P3).WaitingRounds);
        Assert.Equal(0, match.StateOf(P0).WaitingRounds);
    }

    [Fact]
    public void 构筑期也结算()
    {
        // Scenario：A、B 锁定同一棋盘，第 1 大回合两人都 Pass → 第 1 大回合结束时该战场结算。
        MatchFlow match = Started([0, 0, 3, 4]).PassRound();
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal(1, settled.MajorRound);
        Assert.Equal(LinkStage.Building, match.LinkStage);
        Assert.Equal(3, match.ActiveCount);
    }

    [Fact]
    public void 出局者出序()
    {
        // initiative-order Scenario：B 在某大回合结束时以战场落败出局 → 下一大回合的行动顺序不含 B。
        MatchFlow match = Started([0, 0, 3, 4]).PlayRound(P0).PassRound();
        PlayerId loser = match.Settlements.Single().Entries[1].Player;
        Assert.DoesNotContain(loser, match.ActionOrder);
        Assert.Equal(3, match.ActionOrder.Length);
    }

    [Fact]
    public void 已结算的战场不再结算()
    {
        MatchFlow match = Started([0, 0, 3, 4]).PassRounds(2);
        Assert.Single(match.Settlements);
        Assert.Throws<SiegeRuleException>(() => match.Debug.SettleBattlefield(0, match.Settlements[0].Winner));
    }

    [Fact]
    public void 全零局面按种子兜底顺序定胜者()
    {
        // 比较链四级全同（都没落子）时，胜者取种子兜底顺序靠前者（裁决 S6 待定）；落败者名次 2。
        MatchFlow match = Started([0, 0, 3, 4]).PassRound();
        BattlefieldSettlementRecord settled = match.Settlements.Single();
        PlayerId expected = new[] { P0, P1 }.OrderBy(p => match.Debug.SeedRankOf(p)).First();
        Assert.Equal(expected, settled.Winner);
        Assert.Equal([1, 2], settled.Entries.Select(e => e.Rank));
    }

    /// <remarks>变异 M-FC1（到期不结算）使本条红（final-countdown 1.3 实跑，check 阶段复现；见 Battlefield.决赛倒计时Tests）。</remarks>
    [Fact]
    public void 倒计时到期强制结算()
    {
        // final-countdown Scenario：战场 4 在第 6 大回合结束时结算、其胜者开始等待决赛，K = 2，战场 1 内 A、B 一直有人落子
        // → 第 8 大回合结束时战场 1 按当前势力强制结算，结算原因记为"到期"，随后进入待决赛链接。
        // 四人四区 [0,1,3,4]、不挂配对来源：首轮配对由外部提交 (0,1)(3,4)，战场 1 / 战场 4 即这里的 0 / 3。比较链复用：胜者 = 结算时势力（及其后各级）最高者。
        MatchFlow match = Started([0, 1, 3, 4], options: CountdownOptions(2));
        match.PlayRounds(3, All);
        Assert.Equal(MatchPhase.AwaitingPairing, match.Phase);
        match.ApplyPairing([(0, 1), (3, 4)]);
        match.PlayRounds(2, All);
        match.PlayRound(P0, P1);
        Assert.Equal((6, 3), (Assert.Single(match.Settlements).MajorRound, match.Settlements[0].Battlefield));
        Assert.Equal(8, match.DeadlineOf(0));

        match.PlayRound(P0, P1);
        Assert.Single(match.Settlements);
        match.PlayRound(P0, P1);

        BattlefieldSettlementRecord expired = match.Settlements[1];
        Assert.Equal((8, 0, SettlementReason.Expired, (int?)8), (expired.MajorRound, expired.Battlefield, expired.Reason, expired.Deadline));
        // 比较链第一级：结算时势力高者胜（样本口径：两人势力不并列，否则要看后续各级）。
        Assert.Equal(2, expired.Entries.Length);
        Assert.NotEqual(expired.Entries[0].Power, expired.Entries[1].Power);
        Assert.Equal(expired.Entries.MaxBy(e => e.Power)!.Player, expired.Winner);
        PlayerId loser = expired.Winner == P0 ? P1 : P0;
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(loser).Status);
        Assert.Equal(expired.Entries[1].Power, match.StateOf(loser).PowerAtDefeat);
        Assert.Equal(MatchPhase.AwaitingFinalLink, match.Phase);
        Assert.Equal(PairingKind.Final, match.PendingPairing!.Kind);
    }
}
