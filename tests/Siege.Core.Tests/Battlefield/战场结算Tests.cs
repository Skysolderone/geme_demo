using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 规格：openspec/changes/open-map/specs/battlefield —— Requirement: 战场结算（唯一战场：整轮 Pass / 填满 / 减员即结算、即终局）；
/// elimination-endgame「出局判定」的"战场落败"。open-map 起整张地图是唯一战场（标识 0），任何结算都是最后战场结算，对局随之终局；
/// 多战场 / 等待者 / 倒计时的用例整体 Skip（段 B 删除），其余按唯一战场重导期望（结算名单 = 全部在局玩家）。
/// cycle-stop（openspec/changes/cycle-stop/specs/battlefield）：判定链第 (c) 条"到达大回合上限"——「上限结算」「上限与整轮 Pass 同时满足记整轮 Pass」「上限为 0 不启用」。
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
        // open-map：唯一战场，结算名单是全部 4 名在局玩家（P2、P3 未落子势力为 0），结算即终局。
        MatchFlow match = Started([0, 0, 3, 2]).PlayRound(P0, P1).PlayRound(P0);
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
        Assert.Equal([P0, P1], settled.Entries.Take(2).Select(e => e.Player));
        Assert.Equal(4, settled.Entries.Length);
        Assert.Equal([1, 2], settled.Entries.Take(2).Select(e => e.Rank));
        Assert.Equal(1, match.CountEvents(FlowEventKind.BattlefieldSettled));
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
    }

    [Fact]
    public void 一方落子即不结算()
    {
        // Scenario：战场 1 内 A Pass、B 落子 1 枚 → 本大回合结束时战场 1 不结算。
        MatchFlow match = Started([0, 0, 3, 2]).PlayRound(P1);
        Assert.Empty(match.Settlements);
        Assert.Equal(4, match.ActiveCount);
        Assert.Empty(match.PassedThisRound);   // 大回合结束时清空
    }

    [Fact]
    public void 填满结算()
    {
        // Scenario：一次结算后战场 1 内不存在可落子空格 → 该大回合结束时战场 1 结算（原因"填满"，即便有人本大回合落了子）。
        MatchFlow match = Started([0, 0, 3, 2]).AtRound(1, [P0, P1, P2, P3]);
        match.PlaceOne();
        bool toP1 = false;
        foreach (Coord c in match.Board.Battlefields.ById(0)!.Cells.Where(c => match.Board[c].Occupant is null))   // open-map：填满整张地图（唯一战场）
        {
            match.Board.Place(c, toP1 ? P1 : P0, PieceType.Basic);
            toP1 = !toP1;
        }

        match.Debug.Recalculate();
        match.PassRound();

        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal(SettlementReason.BoardFull, settled.Reason);
        Assert.Equal(0, settled.Battlefield);
        Assert.Equal(1, match.ActiveCount);   // open-map：唯一战场结算即终局，落败三人出局
        Assert.Equal(MatchPhase.Ended, match.Phase);
    }

    [Fact]
    public void 三人战场结算()
    {
        // Scenario：战场 1 内 A、B、C 同一大回合全部 Pass → A（势力最高）胜，B、C 同时以"战场落败"出局（同一出局序号），
        // 二者名次按比较链排序、只有链全同才共享（信物数这一级的比较由「链接模式终局名次Tests」用构造输入钉住）。
        // link-map-terrain：棋盘带主题地形后，"P0 多落一枚就势力最高"不再由几何保证（崖壁 / 林地会改覆盖与领地，三人可能并列）；
        // 断言的是三人战场结算的机制：胜者是结算时势力最高者（并列由比较链决出）、另两人同一出局序号、名次按比较链——不是谁赢。
        // open-map：唯一战场，四人都在名单里（P3 未落子势力 0，排最后）；机制断言不变。
        MatchFlow match = Started([0, 0, 0, 3]).PlayRound(P0, P1, P2).PlayRound(P0);
        PlayerId[] three = [P0, P1, P2];
        Dictionary<PlayerId, System.Numerics.BigInteger> power = three.ToDictionary(p => p, p => match.Scoreboard.Latest!.Of(p).Total);
        match.PassRound();

        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        PlayerId a = settled.Winner;
        Assert.Contains(a, three);
        Assert.All(three, p => Assert.True(power[p] <= power[a], $"{p} 势力 {power[p]} 高于胜者 {a} 的 {power[a]}"));
        Assert.Equal(4, settled.Entries.Length);
        Assert.Equal(P3, settled.Entries[^1].Player);
        PlayerId[] losers = [.. three.Where(p => p != a)];
        int order = match.StateOf(losers[0]).EliminationOrder!.Value;
        Assert.Equal(order, match.StateOf(losers[1]).EliminationOrder);
        Assert.Equal(order, match.StateOf(P3).EliminationOrder);
        Assert.Equal(order, settled.EliminationOrder);
        SettlementEntry second = settled.Entries[1];
        SettlementEntry third = settled.Entries[2];
        bool tied = (second.Power, second.HeldRelics, second.ExclusiveCells, second.Stones) == (third.Power, third.HeldRelics, third.ExclusiveCells, third.Stones);
        Assert.Equal(tied ? 2 : 3, third.Rank);
        Assert.Equal(2, second.Rank);
    }

    [Fact]
    public void 减员结算()
    {
        // Scenario（open-map）：在局玩家只剩 C、D 时 D 弃赛 → 立即标记为已结算，C 为胜者，对局终局（原因"只剩一名参赛玩家"优先）。
        MatchFlow match = Started([0, 1, 3, 3]).AtRound(2, [P0, P1, P2, P3]);
        match.Resign(P0);
        match.Resign(P1);
        Assert.Empty(match.Settlements);
        Assert.Equal(2, match.ActiveCount);

        match.Resign(P3);

        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, SettlementReason.Attrition, P2), (settled.Battlefield, settled.Reason, settled.Winner));
        Assert.Null(settled.EliminationOrder);
        Assert.Equal(match.Scoreboard.Latest!.Of(P2).Total, settled.Entries.Single().Power);
        Assert.True(match.IsBattlefieldSettled(0));
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastPlayerStanding, match.Result!.Reason);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(P3).Status);
    }

    [Fact]
    public void 势力归零的减员结算()
    {
        // 势力归零出局：被提光者记"势力归零"；open-map 下唯一战场仍有 3 人在局，不触发减员结算。
        // 载体：左下角在画布留白角上的出生棋盘，其左下角格只有两口气。
        // terrain-barriers 3.1（地图变了）：link:1 左下角那块是河流棋盘，河道横穿第 4 行，角格 C3 只剩一口气——载体改为手写平坦图 FlatMap（出生棋盘 0 = C3–H8，角格两口气 D3 / C4）。
        MatchFlow match = StartedFlat([0, 0, 1, 2]).AtRound(2, [P0, P1, P2, P3]);
        Coord corner = BirthCell(match.Map, 0, 0, 0);
        Assert.Equal(new Coord(2, 2), corner);
        Assert.Equal(2, Adjacency.LibertyNeighbors(match.Map, corner).Length);   // 样本口径：角上一子恰两口气
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
        Assert.Empty(match.Settlements);
        Assert.Equal(3, match.ActiveCount);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
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

        match.PassTurn();   // P3 → 剩余三人都已 Pass，第 1 大回合结束即结算（open-map：唯一战场，结算即终局）

        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, SettlementReason.AllPassed, 1), (settled.Battlefield, settled.Reason, settled.MajorRound));
        Assert.Equal([P0, P1, P3], settled.Entries.Select(e => e.Player).Order());
        Assert.All(settled.Entries.Skip(1), e => Assert.Equal(EliminationReason.BattlefieldDefeat, match.StateOf(e.Player).EliminationReason));
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(P2).Status);
        Assert.Null(match.StateOf(P2).EliminationReason);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(1, match.ActiveCount);
    }

    [Fact]
    public void 构筑期也结算()
    {
        // Scenario（open-map「构筑期也结算」）：第 1 大回合 4 名玩家都 Pass → 第 1 大回合结束时唯一战场结算、终局。
        MatchFlow match = Started([0, 0, 3, 2]).PassRound();
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal(1, settled.MajorRound);
        Assert.Equal(1, match.ActiveCount);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
    }

    [Fact]
    public void 已结算的战场不再结算()
    {
        MatchFlow match = Started([0, 0, 3, 2]).PassRounds(2);
        Assert.Single(match.Settlements);
        Assert.Throws<SiegeRuleException>(() => match.Debug.SettleBattlefield(0, match.Settlements[0].Winner));
    }

    [Fact]
    public void 全零局面按种子兜底顺序定胜者()
    {
        // 比较链四级全同（都没落子）时，胜者取种子兜底顺序靠前者（裁决 S6 待定）；落败者名次 2。
        MatchFlow match = Started([0, 0, 3, 2]).PassRound();
        BattlefieldSettlementRecord settled = match.Settlements.Single();
        PlayerId expected = All.OrderBy(p => match.Debug.SeedRankOf(p)).First();   // open-map：唯一战场，四人全零
        Assert.Equal(expected, settled.Winner);
        Assert.Equal([1, 2, 2, 2], settled.Entries.Select(e => e.Rank));   // 落败者比较链四级全同 → 共享名次 2
        Assert.Equal(All.OrderBy(p => match.Debug.SeedRankOf(p)), settled.Entries.Select(e => e.Player));
    }

    /// <summary>上限 <paramref name="roundCap"/>、P3 弃赛（三人在局）、P0 / P1 / P2 各落过子（势力都 &gt; 0）且 P0 多落 6 子领先，摆到第 <paramref name="round"/> 大回合开始。</summary>
    private static MatchFlow ThreeUnderCap(int roundCap, int round)
    {
        // 四人四块棋盘：P0 多落的子都在自己的出生棋盘核心区（PlaceOne 先落核心区），不会提走别人的子、不会让谁势力归零。
        MatchFlow match = Started([0, 1, 3, 2], options: Options with { RoundCap = roundCap }).PlayRound(P0, P1, P2).PlayRounds(6, P0);
        match.Resign(P3);
        Assert.Equal(3, match.ActiveCount);
        Assert.Empty(match.Settlements);
        return match.AtRound(round, [P0, P1, P2]);
    }

    [Fact]
    public void 上限结算()
    {
        // Scenario（cycle-stop）：大回合上限 60，第 60 大回合结束时 A、B、C 在局且 B 本大回合落子，势力 A > B > C → 战场按势力结算，A 胜、B / C 以"战场落败"出局，原因"上限"；
        // 终局原因仍是"最后战场结算"（elimination-endgame「上限结算即终局」）。反面：第 59 大回合有人落子不结算（未到上限）。
        // 变异 M-RC1：JudgeAtRoundEnd 去掉第 (c) 条 → 本条、「上限结算即终局」「上限结算可查」红；M-RC2：`>=` 改 `>` → 本条红（第 60 大回合不结算）。
        MatchFlow match = ThreeUnderCap(60, 59);
        // 对照（质检补）：第 59 大回合"假设此刻结束"未到上限 → 先手预测照常给出下一轮，三名在局者都在。
        Assert.Equal([P0, P1, P2], match.PublishSupplement().NextOrderForecast!.NextOrder.Order());
        match.PlayRound(P1);
        Assert.Empty(match.Settlements);
        Assert.Equal(60, match.MajorRound);
        System.Numerics.BigInteger[] power = [.. new[] { P0, P1, P2 }.Select(p => match.Scoreboard.Latest!.Of(p).Total)];
        Assert.True(power[0] > power[1] + 5 && power[0] > power[2] + 5 && power[2] > 0, $"样本口径：A 领先 B、C 超过一枚孤子的势力，实际 {string.Join(" / ", power)}");

        // 先手预测复用同一判定（D2；规格「战场结算」"先手预测复用同一判定，上限大回合同样剔除将落败者"的守门）：第 60 大回合"假设此刻结束"会以上限结算
        // 剔除 B、C，唯一战场随之结算终局 → 没有下一轮顺序（唯一战场上"剔除将落败者"唯一可观察的形式）。
        Assert.Null(match.PublishSupplement().NextOrderForecast);

        match.PlayRound(P1);   // B 落子：不是整轮 Pass、也没填满，只因上限结算

        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, SettlementReason.RoundCap, P0, 60), (settled.Battlefield, settled.Reason, settled.Winner, settled.MajorRound));
        Assert.Equal([P1, P2], settled.Entries.Skip(1).Select(e => e.Player).Order());   // 落败者按比较链排（势力相同才共享名次）
        Assert.Equal(1, settled.Entries[0].Rank);
        Assert.Equal(settled.Entries[1].Power == settled.Entries[2].Power ? [2, 2] : [2, 3], settled.Entries.Skip(1).Select(e => e.Rank));
        Assert.True(settled.Entries[1].Power >= settled.Entries[2].Power);
        Assert.All(new[] { P1, P2 }, p => Assert.Equal(EliminationReason.BattlefieldDefeat, match.StateOf(p).EliminationReason));
        Assert.Equal(settled.Entries.Single(e => e.Player == P1).Power, match.StateOf(P1).PowerAtDefeat);
        Assert.True(match.StateOf(P1).PowerAtDefeat > 0);
        Assert.Equal(PlayerStatus.Active, match.StateOf(P0).Status);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Equal(60, match.Result.MajorRound);
        Assert.Equal(60, match.MajorRound);   // 终局后不再推进（快照与活对象都钉住）
    }

    [Fact]
    public void 上限与整轮Pass同时满足记整轮Pass()
    {
        // Scenario（cycle-stop）：上限 60，第 60 大回合全部在局玩家都 Pass → 结算原因记"整轮 Pass"（判定顺序 (a)、(b)、(c)，同时满足记靠前者）。
        // 变异 M-RC3：第 (c) 条挪到整轮 Pass 之前 → 本条红。
        MatchFlow match = ThreeUnderCap(60, 60).PassRound();
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((SettlementReason.AllPassed, 60), (settled.Reason, settled.MajorRound));
        Assert.Equal(MatchPhase.Ended, match.Phase);
    }

    [Fact]
    public void 上限为0不启用()
    {
        // Scenario（cycle-stop）：对局配置大回合上限为 0，第 200 大回合仍有人落子 → 不因上限结算，对局继续。
        // 变异 M-RC4：第 (c) 条去掉 `RoundCap > 0` → 本条红（0 ≤ 200 即结算）。
        MatchFlow match = ThreeUnderCap(0, 200).PlayRound(P1);
        Assert.Empty(match.Settlements);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(201, match.MajorRound);
        Assert.NotNull(match.PublishSupplement().NextOrderForecast);
    }
}
