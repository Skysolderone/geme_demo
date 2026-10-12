using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/turn-sequence —— Requirement: 构筑保护期的落子限制、合法落子范围的对外契约、第 4 大回合起的战场内落子；
/// battlefield —— Requirement: 等待者的行动（链接模式）。旧模式的同名测试（含「第4大回合的全图解禁」）原样保留、守住旧模式。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：M-B2 <c>PublicRules.LegalRange</c> 忽略 <c>mode</c>（Link 也走旧的保护期 / 全图分支）→ 本类「其他战场不可落子」
/// 「等待者可落子」「等待者无法越界」「范围随链接扩大」红 4，旧模式 <c>第4大回合的全图解禁Tests</c> 2 条仍绿。
/// </remarks>
public class 战场内合法落子范围Tests
{
    [Fact]
    public void 构筑期内越区落子被拒()
    {
        // Scenario：第 2 大回合，玩家尝试在自己战场之外的空格暂放棋子 → 系统拒绝并说明违反当前合法落子范围。
        MatchFlow match = Started([0, 1, 3, 4]).AtRound(2);
        StagedBatch batch = match.ToDeploy();
        BatchFailure? failure = batch.Stage(BirthCell(match.Map, 1, 0, 0), PieceType.Basic);
        Assert.Equal(BatchFailureKind.OutOfLegalRange, failure?.Kind);
        Assert.Null(batch.Stage(BirthCell(match.Map, 0, 0, 0), PieceType.Basic));
    }

    [Fact]
    public void 范围随链接扩大()
    {
        // Scenario：第 3 大回合与首轮链接后第 4 大回合的合法落子范围 → 前者为其出生棋盘的格集合，后者为合并后战场的格集合。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(2);
        Assert.Equal(3, match.MajorRound);
        Assert.Equal(match.Map.BirthZones[0], match.LegalRangeFor(P0));
        match.PassRound();
        match.ApplyPairing([(0, 3), (1, 4)]);

        Assert.Equal(4, match.MajorRound);
        Siege.Core.Board.Battlefield merged = match.Board.Battlefields.ById(0)!;
        Assert.Equal([0, 3], merged.BirthZones);
        Assert.Equal(merged.Cells.ToHashSet(), match.LegalRangeFor(P0));
        Assert.Equal(merged.Cells.ToHashSet(), match.LegalRangeFor(P2));
        Assert.Equal(match.Map.BirthZones[0].Count + match.Map.BirthZones[3].Count, match.LegalRangeFor(P0).Count);   // 拼接不新增格
    }

    [Fact]
    public void 链接后可进入对方棋盘()
    {
        // Scenario：第 4 大回合，玩家所在战场已与另一战场链接，玩家在拼过来的对方出生棋盘（已平移）上暂放棋子 → 接受。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        match.ApplyPairing([(0, 3), (1, 4)]);
        BoardPlate plate = match.Map.Boards[3];
        Assert.Equal(0, match.Board.Battlefields.At(plate.Origin)!.Id);
        match.AtRound(4, [P0, P1, P2, P3]);
        StagedBatch batch = match.ToDeploy();
        Assert.Null(batch.Stage(plate.Origin, PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(P0, match.Board[plate.Origin].Occupant!.Value.Owner);
    }

    [Fact]
    public void 其他战场不可落子()
    {
        // Scenario：第 6 大回合，玩家尝试在未与自己链接的战场上暂放棋子 → 系统拒绝并说明不在合法落子范围内。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        match.ApplyPairing([(0, 3), (1, 4)]);
        match.AtRound(6, [P0, P1, P2, P3]);
        IReadOnlySet<Coord> range = match.LegalRangeFor(P0);
        Assert.DoesNotContain(BirthCell(match.Map, 1, 0, 0), range);
        Assert.DoesNotContain(BirthCell(match.Map, 2, 0, 0), range);   // 无人的出生棋盘
        Assert.Empty(range.Intersect(match.Board.Battlefields.ById(1)!.Cells));

        StagedBatch batch = match.ToDeploy();
        Assert.Equal(BatchFailureKind.OutOfLegalRange, batch.Stage(BirthCell(match.Map, 4, 1, 1), PieceType.Basic)?.Kind);
    }

    [Fact]
    public void 等待者可落子()
    {
        // Scenario：玩家 D 在单人战场 3 等待，轮到其小回合 → D 可征募并在战场 3 内落子，其合法落子范围不含任何其他战场的格。
        MatchFlow match = Started([0, 0, 0, 2]).PlayRounds(3, P0);
        Assert.Equal(LinkStage.Fighting, match.LinkStage);
        Assert.Equal(match.Map.BirthZones[2], match.LegalRangeFor(P3));
        match.AtRound(4, [P3, P0, P1, P2]);
        StagedBatch batch = match.ToDeploy();
        Assert.Null(batch.Stage(BirthCell(match.Map, 2, 1, 1), PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);
    }

    [Fact]
    public void 等待者无法越界()
    {
        // Scenario：等待者尝试在另一战场的空格暂放棋子 → 系统拒绝并说明不在合法落子范围内。
        MatchFlow match = Started([0, 0, 0, 2]).PlayRounds(3, P0).AtRound(5, [P3, P0, P1, P2]);
        StagedBatch batch = match.ToDeploy();
        BatchFailure? failure = batch.Stage(BirthCell(match.Map, 0, 1, 1), PieceType.Basic);
        Assert.Equal(BatchFailureKind.OutOfLegalRange, failure?.Kind);
    }

    [Fact]
    public void 空盘面玩家重新进入()
    {
        // Scenario：第 4 大回合，某玩家盘面无棋子但完成征募后持有手牌 → 可在自己所在战场的任意合法空格重新落子（不只是出生棋盘）。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        match.ApplyPairing([(0, 3), (1, 4)]);
        match.AtRound(4, [P0, P1, P2, P3]);
        match.Debug.SeedHand(P0);
        Assert.Equal(0, match.StoneCount(P0));

        match.BeginTurn();
        RecruitPanelView panel = match.EnterRecruit();
        PlayerHandAccess hand = match.CurrentHand();
        for (int i = 0; i < panel.FreePickCount; i++)
        {
            hand.Pick(hand.Panel().Candidates.First(c => c.IsSelectable).Index);
        }

        StagedBatch batch = match.EnterDeploy();
        PieceType type = hand.PrivateView().Types.First();
        Coord deep = BirthCell(match.Map, 3, 1, 1);            // 链接对方的出生棋盘深处
        Assert.Null(batch.Stage(deep, type));
        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(1, match.StoneCount(P0));
    }

    [Fact]
    public void 禁入格不在范围内()
    {
        // Scenario：玩家 A 有一条已确定活形棋串，B 与 A 同战场 → B 的合法落子范围不含 A 的眼空间，A 的含。
        // 在出生棋盘 0 的上边角做出与旧模式同形的"角上两眼"（眼 (0,0)、(2,0)），P0 与 P1 同区。
        MatchFlow match = Started([0, 0, 3, 4]).AtRound(2);
        MapData map = match.Map;
        Coord eyeA = BirthCell(map, 0, 0, 0);
        Coord eyeB = BirthCell(map, 0, 2, 0);
        match.Place(P0, BirthCell(map, 0, 1, 0), BirthCell(map, 0, 3, 0), BirthCell(map, 0, 0, 1), BirthCell(map, 0, 1, 1), BirthCell(map, 0, 2, 1), BirthCell(map, 0, 3, 1));
        Assert.Equal(new HashSet<Coord> { eyeA, eyeB }, LifeShapeReport.Analyze(match.Board).ForbiddenCellsFor(P1).ToHashSet());

        IReadOnlySet<Coord> p1 = match.LegalRangeFor(P1);
        Assert.DoesNotContain(eyeA, p1);
        Assert.DoesNotContain(eyeB, p1);
        Assert.Equal(map.BirthZones[0].Except([eyeA, eyeB]).ToHashSet(), p1);
        Assert.Contains(eyeA, match.LegalRangeFor(P0));
    }

    [Fact]
    public void 旧模式第4大回合仍全图解禁()
    {
        // 实现裁决 B1：删除"第 4 大回合全图解禁"只作用于链接模式；旧模式原样（与既有「第4大回合的全图解禁Tests」同口径）。
        MatchFlow legacy = MatchFixtures.Started().AtRound(4);
        Assert.Contains(TestMaps.At("H8"), legacy.LegalRangeFor(MatchFixtures.P0));
        Assert.Equal(MatchMode.Legacy, legacy.Mode);
    }
}
