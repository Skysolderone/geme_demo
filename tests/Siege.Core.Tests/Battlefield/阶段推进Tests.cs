using Siege.Core.Board;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/battlefield —— Requirement: 阶段推进（段 B 前半：阶段机与首轮 / 决赛的转换时机）。
/// 战场结算的判定（整轮 Pass / 填满 / 减员）属 2.6，这里用测试桩 <c>Debug.SettleBattlefield</c>"手动标记战场已结算"推进。
/// 出生棋盘编号用 0 起的内部索引：规格写的"棋盘 2、4、5"即这里的 1、3、4。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：M-B7 <c>FirstRoundCandidates</c> 的"锁定时旗数恰为 1"改成"当前战场只剩 1 名在局者" → 「提前打完的同区棋盘不进配对」「构筑期结算的两个同区战场在第3大回合结束进入决赛」红 2。
/// </remarks>
public class 阶段推进Tests
{
    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void 提前打完的同区棋盘不进配对()
    {
        // Scenario：A、B 锁定棋盘 2 并在第 2 大回合结算、A 胜；C 锁定 4、D 锁定 5；第 3 大回合结束 → 候选仍只有战场 4 与 5，A 在战场 2 等待决赛。
        MatchFlow match = Started([1, 1, 3, 4]).PassRounds(1);
        Assert.Equal(2, match.MajorRound);
        match.Debug.SettleBattlefield(1, P0);
        match.PassRounds(2);

        Assert.Equal(MatchPhase.AwaitingPairing, match.Phase);
        Assert.Equal([3, 4], match.PendingPairing!.Candidates);
        match.ApplyPairing([(3, 4)]);
        Assert.Equal(1, match.BattlefieldOf(P0));
        Assert.Equal([P0], match.ActivePlayersIn(1));
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 三人同区加一人独占()
    {
        // Scenario：A、B、C 锁定棋盘 1，D 锁定棋盘 3，第 3 大回合结束 → 候选只有战场 3，不配对，D 原地等待；
        // 战场 1 结算出胜者后与战场 3 链接打决赛。
        MatchFlow match = Started([0, 0, 0, 2]).PassRounds(3);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(LinkStage.Fighting, match.LinkStage);
        Assert.Empty(match.Pairings);
        Assert.Equal([2], match.FirstRoundCandidates());

        match.PassRound();
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

    [Fact]
    public void 四人同区()
    {
        // Scenario：4 名玩家都锁定棋盘 1 → 没有配对与决赛，战场 1 结算即为全局终局，胜者第 1、其余三人出局。
        MatchFlow match = Started([0, 0, 0, 0]).PassRounds(4);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Empty(match.Pairings);
        Assert.Empty(match.Links);

        match.Debug.SettleBattlefield(0, P2);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Empty(match.Pairings);
        Assert.Equal(P2, match.Result!.Standings.Single(s => s.Rank == 1).Player);
        Assert.Equal(3, match.PlayerStates.Count(s => s.Status == Scoring.PlayerStatus.Eliminated));
    }

    [Fact]
    public void 决赛链接()
    {
        // Scenario：两个双人战场都已结算、各剩一名胜者 → 进入待决赛链接，链接后两名胜者处于同一战场。
        MatchFlow match = Started([0, 1, 2, 3]).PassRounds(3);
        match.ApplyPairing([(0, 2), (1, 3)]);
        match.Debug.SettleBattlefield(0, P2);
        match.PassRound();
        Assert.Equal(MatchPhase.InProgress, match.Phase);    // 只结算了一个多人战场
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

    [Fact]
    public void 构筑期结算的两个同区战场在第3大回合结束进入决赛()
    {
        // 2+2：两块双人棋盘都在第 2 大回合结算；构筑期内不转换，第 3 大回合结束时首轮无候选，随即决赛触发。
        MatchFlow match = Started([0, 0, 4, 4]).PassRounds(1);
        match.Debug.SettleBattlefield(0, P0);
        match.Debug.SettleBattlefield(4, P3);
        match.PassRound();
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(3, match.MajorRound);
        match.PassRound();
        Assert.Equal(MatchPhase.AwaitingFinalLink, match.Phase);
        Assert.Equal([0, 4], match.PendingPairing!.Candidates);
    }

    [Fact]
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

    [Fact]
    public void 旧模式没有阶段转换()
    {
        // 实现裁决 B1：旧模式第 3 大回合结束不进入待配对。
        MatchFlow match = MatchFixtures.Started();
        for (int i = 0; i < 12 && match.Phase == MatchPhase.InProgress; i++)
        {
            match.PassTurn();
        }

        Assert.Null(match.LinkStage);
        Assert.NotEqual(MatchPhase.AwaitingPairing, match.Phase);
        Assert.Throws<SiegeRuleException>(() => match.FirstRoundCandidates());
    }
}
