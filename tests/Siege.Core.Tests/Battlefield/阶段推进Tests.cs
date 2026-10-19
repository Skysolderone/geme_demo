using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/battlefield —— Requirement: 阶段推进（段 B 前半：阶段机与首轮 / 决赛的转换时机）。
/// 2.6 起战场结算是真实判定（整轮 Pass / 填满 / 减员）：要让多人战场保持未结算，就让其中一人每大回合落子（<c>PlayRound(mover)</c>）；
/// 要指定胜者时仍可用测试接缝 <c>Debug.SettleBattlefield</c>。
/// 出生棋盘编号用 0 起的内部索引：规格写的"棋盘 2、4、5"即这里的 1、3、4。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：M-B7 <c>FirstRoundCandidates</c> 的"锁定时旗数恰为 1"改成"当前战场只剩 1 名在局者" → 「提前打完的同区棋盘不进配对」「构筑期结算的两个同区战场在第3大回合结束进入决赛」红 2。
/// <para>open-map（openspec/changes/open-map/specs/battlefield「阶段推进」MODIFIED）：阶段只剩 插旗 → 构筑 → 交战 → 终局，原配对 / 决赛用例整文件 Skip（段 B 删除）；
/// 新流程的三条 Scenario「第 4 大回合不发生任何事件」「构筑期内整轮 Pass 即终局」「四人同区」由下面三条（段 A 质检补；「四人同区_开放地图」与已 Skip 的配对版同名用例区分）守，并顺带钉 map-definition「地图整局不变」。</para>
/// </remarks>
public class 阶段推进Tests
{
    [Fact]
    public void 第4大回合不发生任何事件()
    {
        // Scenario（open-map）：四人四区，第 3 大回合结束 → 对局直接进入第 4 大回合，没有配对、链接或等待者事件；行动顺序照常按势力生成；地图与开局逐格相同、版本号 0。
        // P0 每个大回合落 1 子、其余 Pass（不触发整轮 Pass 结算）。
        MatchFlow match = Started([0, 1, 3, 4]).PlayRounds(3, P0);

        Assert.Equal(4, match.MajorRound);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Empty(match.Pairings);
        Assert.Empty(match.Links);
        Assert.Null(match.PendingPairing);
        Assert.Empty(match.Settlements);
        Assert.NotNull(match.CurrentPlayer);
        Assert.Equal(4, match.ActionOrder.Length);
        Assert.Equal(P0, match.ActionOrder[0]);   // 只有 P0 有势力 → 势力名次第一、先手
        Assert.Equal(0, match.Map.MapVersion);
        Assert.Same(Map, match.Board.BaseMap);
        Assert.Equal(MapFile.ToJson(Map), MapFile.ToJson(match.Map));   // 地图整局不变（没有改造时逐格相同）
        Assert.Equal(1, match.Board.Battlefields.Count);

        // 第 4 大回合照常进行：P0 落子、其余 Pass → 第 5 大回合，仍没有任何事件。
        match.PlayRound(P0);
        Assert.Equal(5, match.MajorRound);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Empty(match.Pairings);
        Assert.Empty(match.Links);
    }

    [Fact]
    public void 构筑期内整轮Pass即终局()
    {
        // Scenario（open-map）：第 2 大回合 4 名玩家全部 Pass → 第 2 大回合结束时唯一战场结算，对局终局，名次按势力与判定链（P0 第 1 大回合落了 1 子 → 胜者）。
        MatchFlow match = Started([0, 1, 3, 4]).PlayRound(P0);
        Assert.Equal(2, match.MajorRound);
        match.PassRound();

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Equal(2, match.Result.MajorRound);
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((2, 0, SettlementReason.AllPassed, P0), (settled.MajorRound, settled.Battlefield, settled.Reason, settled.Winner));
        Assert.Equal(4, settled.Entries.Length);
        Assert.Equal(P0, match.Result.Standings.Single(s => s.Rank == 1).Player);
        Assert.Equal(3, match.Result.Standings.Count(s => s.Input.Reason == EliminationReason.BattlefieldDefeat));
    }

    [Fact]
    public void 四人同区_开放地图()
    {
        // Scenario（open-map「四人同区」；原配对版同名用例已 Skip）：4 名玩家都锁定棋盘 1（内部索引 0）→ 构筑期内四人共享棋盘 1 的合法落子范围；第 4 大回合起全图可落；终局条件与四人四区相同。
        MatchFlow match = Started([0, 0, 0, 0]);
        HashSet<Coord> board0 = [.. match.Map.BirthBoard(0).Cells().Where(match.Board.Map.IsPlayable)];
        Assert.Equal(board0, match.LegalRangeFor(P0));
        Assert.All(All, p => Assert.Equal(match.LegalRangeFor(P0), match.LegalRangeFor(p)));   // 同区共享
        Assert.Empty(match.Pairings);
        Assert.Empty(match.Links);

        match.PlayRounds(3, P0);
        Assert.Equal(4, match.MajorRound);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        IReadOnlySet<Coord> unlocked = match.LegalRangeFor(P1);
        Assert.True(unlocked.Count > board0.Count);
        Assert.Contains(BirthCell(match.Map, 3, 0, 0), unlocked);   // 无人锁定的棋盘 4 也可落
        Assert.Empty(match.Settlements);

        // 第 4 大回合整轮 Pass → 唯一战场结算即终局：胜者第 1，其余三人以战场落败出局（与四人四区相同的判定）。
        match.PassRound();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Single(match.Settlements);
        Assert.Equal(P0, match.Result.Standings.Single(s => s.Rank == 1).Player);
        Assert.Equal(3, match.PlayerStates.Count(s => s.Status == Scoring.PlayerStatus.Eliminated));
        Assert.Equal(3, match.Result.Standings.Count(s => s.Input.Reason == EliminationReason.BattlefieldDefeat));
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 构筑期三个大回合不转换()
    {
        MatchFlow match = Started([0, 1, 2, 3]);
        Assert.Equal(LinkStage.Building, match.LinkStage);
        match.PassRounds(2);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(3, match.MajorRound);
        Assert.Equal(LinkStage.Building, match.LinkStage);
        Assert.Null(match.PendingPairing);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 四人四区()
    {
        // Scenario：4 名玩家锁定 4 块不同的出生棋盘，第 3 大回合结束 → 候选为 4 个战场，进入待首轮配对；配对后形成 2 个双人战场。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        Assert.Equal(MatchPhase.AwaitingPairing, match.Phase);
        Assert.Equal(4, match.MajorRound);
        Assert.Null(match.CurrentPlayer);
        Assert.Equal([0, 1, 3, 4], match.PendingPairing!.Candidates);
        Assert.Equal(PairingKind.FirstRound, match.PendingPairing.Kind);
        Assert.Throws<SiegeRuleException>(match.BeginTurn);   // 待配对阻塞小回合

        match.ApplyPairing([(0, 3), (1, 4)]);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(LinkStage.Fighting, match.LinkStage);
        Assert.NotNull(match.CurrentPlayer);
        Assert.Equal(match.BattlefieldOf(P0), match.BattlefieldOf(P2));
        Assert.Equal(match.BattlefieldOf(P1), match.BattlefieldOf(P3));
        Assert.NotEqual(match.BattlefieldOf(P0), match.BattlefieldOf(P1));
        Assert.Equal([0, 1], match.OccupiedBattlefields());
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 两人同区加两人独占()
    {
        // Scenario：A、B 锁定棋盘 2，C 锁定 4，D 锁定 5，第 3 大回合结束 → 候选只有战场 4 与 5，配对后形成战场 2（A、B）与战场 4（C、D）。
        MatchFlow match = Started([1, 1, 3, 4]).PassRounds(3);
        Assert.Equal([3, 4], match.PendingPairing!.Candidates);
        match.ApplyPairing([(3, 4)]);
        Assert.Equal(1, match.BattlefieldOf(P0));
        Assert.Equal(1, match.BattlefieldOf(P1));
        Assert.Equal(3, match.BattlefieldOf(P2));
        Assert.Equal(3, match.BattlefieldOf(P3));
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 提前打完的同区棋盘不进配对()
    {
        // Scenario：A、B 锁定棋盘 2 并在第 2 大回合结算、A 胜；C 锁定 4、D 锁定 5；第 3 大回合结束 → 候选仍只有战场 4 与 5，A 在战场 2 等待决赛。
        // 第 1 大回合 A 落子（战场 2 不结算），第 2 大回合两人都 Pass → 第 2 大回合结束时战场 2 真实结算、A（有势力）胜。
        MatchFlow match = Started([1, 1, 3, 4]).PlayRound(P0).PassRound();
        Assert.Equal(3, match.MajorRound);
        BattlefieldSettlementRecord early = Assert.Single(match.Settlements);
        Assert.Equal((1, SettlementReason.AllPassed, P0, 2), (early.Battlefield, early.Reason, early.Winner, early.MajorRound));
        match.PassRound();

        Assert.Equal(MatchPhase.AwaitingPairing, match.Phase);
        Assert.Equal([3, 4], match.PendingPairing!.Candidates);
        match.ApplyPairing([(3, 4)]);
        Assert.Equal(1, match.BattlefieldOf(P0));
        Assert.Equal([P0], match.ActivePlayersIn(1));
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 三人同区加一人独占()
    {
        // Scenario：A、B、C 锁定棋盘 1，D 锁定棋盘 3，第 3 大回合结束 → 候选只有战场 3，不配对，D 原地等待；
        // 战场 1 结算出胜者后与战场 3 链接打决赛。
        MatchFlow match = Started([0, 0, 0, 2]).PlayRounds(3, P0);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(LinkStage.Fighting, match.LinkStage);
        Assert.Empty(match.Pairings);
        Assert.Equal([2], match.FirstRoundCandidates());

        match.PlayRound(P0);
        Assert.Equal(MatchPhase.InProgress, match.Phase);    // 战场 1 未结算：不触发决赛
        match.Debug.SettleBattlefield(0, P1);
        Assert.Equal(MatchPhase.InProgress, match.Phase);    // 转换等到大回合结束
        match.PassRound();
        Assert.Equal(MatchPhase.AwaitingFinalLink, match.Phase);
        Assert.Equal([0, 2], match.PendingPairing!.Candidates);
        match.ApplyPairing([(0, 2)]);
        Assert.Equal(LinkStage.Final, match.LinkStage);
        Assert.Equal(match.BattlefieldOf(P1), match.BattlefieldOf(P3));
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 减员触发的决赛等到大回合结束()
    {
        // Scenario：第 6 大回合中途，最后一个未结算的多人战场因弃赛只剩 1 人 → 该战场立即标记为已结算，待决赛链接在第 6 大回合结束时进入。
        // 局面：A、B 同区（棋盘 1），C、D 同区（棋盘 2）；C、D 第 1 大回合都 Pass → 战场 2 结算；A 每大回合落子 → 战场 1 一直未结算。
        MatchFlow match = Started([0, 0, 1, 1]).PlayRounds(5, P0);
        Assert.Equal(6, match.MajorRound);
        Assert.True(match.IsBattlefieldSettled(1));
        Assert.False(match.IsBattlefieldSettled(0));
        Assert.Equal(MatchPhase.InProgress, match.Phase);

        match.PlaceOne();           // 第 6 大回合中途：先有人行动
        match.Resign(P1);           // 战场 1 只剩 A → 减员结算，即时标记
        Assert.True(match.IsBattlefieldSettled(0));
        Assert.Equal(SettlementReason.Attrition, match.Settlements[^1].Reason);
        Assert.Equal(MatchPhase.InProgress, match.Phase);   // 转换等到大回合结束
        Assert.Null(match.PendingPairing);

        match.PassRound();
        Assert.Equal(7, match.MajorRound);
        Assert.Equal(MatchPhase.AwaitingFinalLink, match.Phase);
        Assert.Equal([0, 1], match.PendingPairing!.Candidates);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 四人同区()
    {
        // Scenario：4 名玩家都锁定棋盘 1 → 没有配对与决赛，战场 1 结算即为全局终局，胜者第 1、其余三人出局。
        MatchFlow match = Started([0, 0, 0, 0]).PlayRounds(4, P0);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Empty(match.Pairings);
        Assert.Empty(match.Links);

        match.Debug.SettleBattlefield(0, P2);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Empty(match.Pairings);
        Assert.Equal(P2, match.Result!.Standings.Single(s => s.Rank == 1).Player);
        Assert.Equal(3, match.PlayerStates.Count(s => s.Status == Scoring.PlayerStatus.Eliminated));
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 决赛链接()
    {
        // Scenario：两个双人战场都已结算、各剩一名胜者 → 进入待决赛链接，链接后两名胜者处于同一战场。
        MatchFlow match = Started([0, 1, 2, 3]).PassRounds(3);
        match.ApplyPairing([(0, 2), (1, 3)]);
        match.Debug.SettleBattlefield(0, P2);
        match.PlayRound(P1);
        Assert.Equal(MatchPhase.InProgress, match.Phase);    // 只结算了一个多人战场（战场 2 有人落子，不结算）
        match.Debug.SettleBattlefield(1, P1);
        match.PassRound();

        Assert.Equal(MatchPhase.AwaitingFinalLink, match.Phase);
        Assert.Equal(PairingKind.Final, match.PendingPairing!.Kind);
        Assert.Equal([0, 1], match.PendingPairing.Candidates);
        match.ApplyPairing([(0, 1)]);
        Assert.Equal(match.BattlefieldOf(P1), match.BattlefieldOf(P2));
        Assert.Equal(LinkStage.Final, match.LinkStage);
        Assert.Equal(3, match.Links.Count);
        Assert.Empty(match.SettledBattlefields);   // 合并后的新战场视为未结算
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 构筑期结算的两个同区战场在第3大回合结束进入决赛()
    {
        // 2+2：两块双人棋盘都在第 1 大回合整轮 Pass 后结算；构筑期内不转换，第 3 大回合结束时首轮无候选，随即决赛触发。
        MatchFlow match = Started([0, 0, 4, 4]).PassRound();
        Assert.Equal([0, 4], match.SettledBattlefields);
        Assert.Equal(2, match.ActiveCount);
        match.PassRound();
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(3, match.MajorRound);
        match.PassRound();
        Assert.Equal(MatchPhase.AwaitingFinalLink, match.Phase);
        Assert.Equal([0, 4], match.PendingPairing!.Candidates);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 挂接配对来源时当场配对()
    {
        // 实现裁决 B3：MatchFlow 在进入待配对时调用挂接的来源；脚本缺省按编号升序两两相配。
        var source = new ScriptedPairingSource();
        MatchFlow match = Started([0, 1, 2, 3], source).PassRounds(3);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(4, match.MajorRound);
        Assert.Single(source.Requests);
        Assert.Equal([0, 1, 2, 3], source.Requests[0].Candidates);
        Assert.Equal([(0, 1), (2, 3)], match.Pairings.Single().Pairs);
        Assert.Equal(2, match.Links.Count);

        // 决赛同样经来源（唯一合法结果）。
        match.Debug.SettleBattlefield(0, P0);
        match.Debug.SettleBattlefield(2, P3);
        match.PassRound();
        Assert.Equal(2, source.Requests.Count);
        Assert.Equal(PairingKind.Final, source.Requests[1].Kind);
        Assert.Equal(LinkStage.Final, match.LinkStage);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }
}
