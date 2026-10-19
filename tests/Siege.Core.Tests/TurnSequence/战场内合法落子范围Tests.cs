using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>
/// 规格：openspec/changes/open-map/specs/turn-sequence —— Requirement: 构筑保护期的落子限制（自己的出生棋盘）、合法落子范围的对外契约（按大回合两段）、第 4 大回合起的全图落子；
/// 等待者两条随 open-map Skip（段 B 删除）。retire-legacy-rules：旧模式的同名测试（含「第4大回合的全图解禁」）随旧模式删除，
/// 其中仍成立的「构筑期内越区落子被拒」「空盘面玩家重新进入」「禁入格不在范围内」由本类同名条目接替（清单 (j)），「构筑期内规则照常」迁入本类。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：M-B2 <c>PublicRules.LegalRange</c> 忽略 <c>mode</c>（Link 也走旧的保护期 / 全图分支）→ 本类「其他战场不可落子」
/// 「等待者可落子」「等待者无法越界」「范围随链接扩大」红 4，旧模式 <c>第4大回合的全图解禁Tests</c> 2 条仍绿（历史记录；该参数与旧测试已随 retire-legacy-rules 删除）。
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
        Assert.Contains("合法落子范围", failure!.Message);   // retire-legacy-rules 质检：随清单 (j) 合并补回原「保护期内越区落子被拒」的"说明违反合法落子范围"一句
        Assert.Null(batch.Stage(BirthCell(match.Map, 0, 0, 0), PieceType.Basic));
    }

    [Fact]
    public void 范围随大回合扩大()
    {
        // Scenario（open-map「合法落子范围的对外契约」范围随大回合扩大）：第 3 大回合的范围为其出生棋盘的可落子格集合，第 4 大回合起为全图可落子格集合；
        // 同区者共享同一范围。P0 每个大回合落一子、其余 Pass（不触发整轮 Pass 结算）。
        MatchFlow match = Started([0, 0, 3, 4]).PlayRounds(2, P0);
        Assert.Equal(3, match.MajorRound);
        HashSet<Coord> board0 = [.. match.Map.BirthBoard(0).Cells().Where(match.Board.Map.IsPlayable)];
        Assert.Equal(board0, match.LegalRangeFor(P0));   // 范围是整块出生棋盘的可落子格（含靠接缝的外圈），出生区只是核心区
        Assert.Equal(match.LegalRangeFor(P0), match.LegalRangeFor(P1));   // 同区共享
        Assert.True(board0.Count < match.Board.Battlefields.ById(0)!.Cells.Length);

        match.PlayRound(P0);

        Assert.Equal(4, match.MajorRound);
        Siege.Core.Board.Battlefield whole = match.Board.Battlefields.ById(0)!;
        Assert.Equal([0, 1, 2, 3, 4], whole.BirthZones);
        Assert.Equal(whole.Cells.ToHashSet(), match.LegalRangeFor(P0));
        Assert.Equal(whole.Cells.ToHashSet(), match.LegalRangeFor(P2));
        Assert.Equal(match.Map.Boards.Sum(b => b.Cells().Count(match.Board.Map.IsPlayable)), match.LegalRangeFor(P0).Count);   // 全图 = 5 块可落子格之和
    }

    [Fact]
    public void 第4大回合进入对方棋盘()
    {
        // Scenario（open-map「第 4 大回合起的全图落子」）：第 4 大回合，玩家在另一名玩家的出生棋盘上暂放棋子 → 接受；第 2 大回合同一落点被拒。
        MatchFlow match = Started([0, 1, 3, 4]).AtRound(2, [P0, P1, P2, P3]);
        BoardPlate plate = match.Map.Boards[3];   // P2 锁定的棋盘
        Assert.Equal(BatchFailureKind.OutOfLegalRange, match.ToDeploy().Stage(plate.Origin, PieceType.Basic)?.Kind);
        match.Pass();

        match.AtRound(4, [P0, P1, P2, P3]);
        StagedBatch batch = match.ToDeploy();
        Assert.Null(batch.Stage(plate.Origin, PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(P0, match.Board[plate.Origin].Occupant!.Value.Owner);
    }

    [Fact]
    public void 无人棋盘可落子()
    {
        // Scenario（open-map）：第 6 大回合，玩家在无人锁定的出生棋盘（棋盘 2）上暂放棋子 → 接受；范围含其他玩家的出生棋盘。
        MatchFlow match = Started([0, 1, 3, 4]).AtRound(6, [P0, P1, P2, P3]);
        IReadOnlySet<Coord> range = match.LegalRangeFor(P0);
        Assert.Contains(BirthCell(match.Map, 1, 0, 0), range);
        Assert.Contains(BirthCell(match.Map, 2, 0, 0), range);   // 无人的出生棋盘
        Assert.Equal(match.Board.Battlefields.ById(0)!.Cells.ToHashSet(), range);

        StagedBatch batch = match.ToDeploy();
        Assert.Null(batch.Stage(CoreCell(match.Map, 2, 1, 1), PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 等待者可落子()
    {
        // Scenario：玩家 D 在单人战场 3 等待，轮到其小回合 → D 可征募并在战场 3 内落子，其合法落子范围不含任何其他战场的格。
        MatchFlow match = Started([0, 0, 0, 2]).PlayRounds(3, P0);
        Assert.Equal(LinkStage.Fighting, match.LinkStage);
        Assert.Equal(match.Board.Battlefields.ById(2)!.Cells.ToHashSet(), match.LegalRangeFor(P3));
        match.AtRound(4, [P3, P0, P1, P2]);
        StagedBatch batch = match.ToDeploy();
        Assert.Null(batch.Stage(CoreCell(match.Map, 2, 1, 1), PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 等待者无法越界()
    {
        // Scenario：等待者尝试在另一战场的空格暂放棋子 → 系统拒绝并说明不在合法落子范围内。
        MatchFlow match = Started([0, 0, 0, 2]).PlayRounds(3, P0).AtRound(5, [P3, P0, P1, P2]);
        StagedBatch batch = match.ToDeploy();
        BatchFailure? failure = batch.Stage(CoreCell(match.Map, 0, 1, 1), PieceType.Basic);
        Assert.Equal(BatchFailureKind.OutOfLegalRange, failure?.Kind);
    }

    [Fact]
    public void 空盘面玩家重新进入()
    {
        // Scenario：第 4 大回合，某玩家盘面无棋子但完成征募后持有手牌 → 可在全图任意合法空格重新落子（不只是出生棋盘）。
        // open-map：构筑期由 P1 落子、其余 Pass 走到第 4 大回合（没有配对 / 链接）。
        MatchFlow match = Started([0, 1, 3, 4]).PlayRounds(3, P1);
        match.AtRound(4, [P0, P1, P2, P3]);
        match.Debug.SeedHand(P0);
        // retire-legacy-rules 质检：随清单 (j) 合并补回原「第4大回合的全图解禁Tests.空盘面玩家重新进入」的前提——盘面与手牌皆空、征募后持有手牌。
        Assert.True(match.Hands.IsHandEmpty(P0));
        Assert.Equal(0, match.StoneCount(P0));

        match.BeginTurn();
        RecruitPanelView panel = match.EnterRecruit();
        PlayerHandAccess hand = match.CurrentHand();
        for (int i = 0; i < panel.FreePickCount; i++)
        {
            hand.Pick(hand.Panel().Candidates.First(c => c.IsSelectable).Index);
        }

        Assert.False(hand.PrivateView().IsEmpty);
        StagedBatch batch = match.EnterDeploy();
        PieceType type = hand.PrivateView().Types.First();
        Coord deep = CoreCell(match.Map, 3, 1, 1);             // 对方（P2）出生棋盘深处（核心区中心）
        Assert.Null(batch.Stage(deep, type));
        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(1, match.StoneCount(P0));
    }

    [Fact]
    public void 禁入格不在范围内()
    {
        // Scenario：玩家 A 有一条已确定活形棋串，B 与 A 同战场 → B 的合法落子范围不含 A 的眼空间，A 的含。
        // 在出生棋盘 0 的上边角做出与旧模式同形的"角上两眼"（眼 (0,0)、(2,0)），P0 与 P1 同区。
        // link-map-terrain：两眼形摆在棋盘左下角的外圈两行（第 0 行外圈 + 第 1 行）；6×6 棋盘的第 1 行第 1–3 列是核心区，整片高度 0、无栅栏。
        // open-map：眼形要摆在画布留白角上的棋盘（下边与左边都是场景格）——link:1 的 3+2 布局里是出生棋盘 4（下标 3，o0，左下角 (2,2)）；先断言这 8 格平坦（样本口径）。
        MatchFlow match = Started([3, 3, 4, 0]).AtRound(2);
        MapData map = match.Map;
        Coord[] shape = [.. from dy in new[] { 0, 1 } from dx in new[] { 0, 1, 2, 3 } select BirthCell(map, 3, dx, dy)];
        Assert.Equal(new Coord(2, 2), map.BirthBoard(3).Origin);
        Assert.All(shape, c => Assert.True(map.IsPlayable(c) && map.HeightAt(c) == 0 && !shape.Any(n => map.HasFence(c, n)), c.ToNotation()));
        Coord eyeA = BirthCell(map, 3, 0, 0);
        Coord eyeB = BirthCell(map, 3, 2, 0);
        match.Place(P0, BirthCell(map, 3, 1, 0), BirthCell(map, 3, 3, 0), BirthCell(map, 3, 0, 1), BirthCell(map, 3, 1, 1), BirthCell(map, 3, 2, 1), BirthCell(map, 3, 3, 1));
        Assert.Equal(new HashSet<Coord> { eyeA, eyeB }, LifeShapeReport.Analyze(match.Board).ForbiddenCellsFor(P1).ToHashSet());

        IReadOnlySet<Coord> p1 = match.LegalRangeFor(P1);
        Assert.DoesNotContain(eyeA, p1);
        Assert.DoesNotContain(eyeB, p1);
        Assert.Equal(map.BirthBoard(3).Cells().Where(map.IsPlayable).Except([eyeA, eyeB]).ToHashSet(), p1);   // 构筑期：自己的出生棋盘 − 禁入格
        Assert.Contains(eyeA, match.LegalRangeFor(P0));
    }

    [Fact]
    public void 构筑期内规则照常()
    {
        // Scenario（turn-sequence「构筑保护期的落子限制」构筑期内规则照常）：第 3 大回合两名共享出生棋盘的玩家互相围杀 → 提子、覆盖重算、信物揭示与势力更新全部正常执行。
        // retire-legacy-rules 1.5：由「构筑保护期的落子限制Tests.保护期内规则照常」迁来（原文件整删）。载体换为平坦 8×8 链接档图，P0 / P1 同锁棋盘 0，
        // 原 9×9 的 A1 / B1 / A2 / B2 平移 (+2, +2) 为 C3 / D3 / C4 / D4，断言不变。
        // 变异验证 M-T9：OnRevealRelics 在保护期内跳过揭示 → 红 1（本测试）。
        MatchFlow match = PlainStarted(zones: [0, 0, 2, 3], relics: [("D4", RelicFixtures.Depot())])
            .AtRound(3, [P0, P1, P2, P3])
            .Stones(P1, "C3");
        Assert.False(match.Relics.IsRevealed(TestMaps.At("D4")));

        match.PlayTurn("D3", "C4");

        Assert.Null(match.Board[TestMaps.At("C3")].Occupant);                       // 提子
        Assert.True(match.Relics.IsRevealed(TestMaps.At("D4")));                     // 信物揭示
        Assert.Equal(RelicControlKind.Controlled, match.Relics.ControlOf(TestMaps.At("D4")).Kind);
        Assert.Contains(TestMaps.At("C3"), match.Scoreboard.Latest!.Of(P0).ExclusiveCells);   // 覆盖重算
        Assert.Equal(0, match.Scoreboard.Latest.Of(P1).Total);                                // 势力更新
        Assert.True(match.Scoreboard.Latest.Of(P0).Total > 0);
    }
}
