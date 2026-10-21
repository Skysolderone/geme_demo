using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision（expert-lookahead）—— Requirement: 前瞻中的下一名对手</summary>
public class 前瞻中的下一名对手Tests
{
    [Fact]
    public void 跳过已弃赛者()
    {
        // 本大回合顺序 P0（专家）→ P1 → P2 → P3，P1 已弃赛 → 每个候选的下一名对手都是 P2，行动大回合为当前大回合。
        // 变异 M-A5a（名册不去掉已弃赛者）→ 见段 A 实施记录。
        MatchFlow match = AiFixtures.Round5()
            .Stones(P0, "F6").Stones(P1, "D9").Stones(P2, "J4").Stones(P3, "J9");
        match.Resign(P1);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(P1).Status);
        Assert.Equal(P0, match.CurrentPlayer);

        LookaheadRecord record = Record(DecideExpert(match));

        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.True(record.Entries.Length >= 2, record.ToText());
        Assert.All(record.Entries, e =>
        {
            Assert.Equal(P2, e.Responder);
            Assert.Equal(5, e.ResponderRound);
        });
    }

    [Fact]
    public void 跳过已出局者()
    {
        // 顺序 P3 → P0（专家）→ P1 → P2；P3 先在 J8 提走 P1 在盘上唯一的子 J9（P1 曾建立正势力）→ P1 出局 → 每个候选的下一名对手都是 P2。
        MatchFlow match = AiFixtures.Round5()
            .AtRound(5, [P3, P0, P1, P2])
            .Stones(P0, "F6").Stones(P1, "K10").Stones(P2, "D4").Stones(P3, "J10");
        match.PlayTurn("K9");
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(P1).Status);
        Assert.Equal(P0, match.CurrentPlayer);

        LookaheadRecord record = Record(DecideExpert(match));

        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.True(record.Entries.Length >= 2, record.ToText());
        Assert.All(record.Entries, e => Assert.Equal(P2, e.Responder));
    }

    /// <summary>顺序 P0（专家）→ P1 → P2 → P3；P1 在盘上只有 J9 一子（曾建立正势力），被 P0 的 H9 逼到只剩一口气 J8。</summary>
    private static MatchFlow CapturablePosition() =>
        AiFixtures.Round5().Stones(P0, "J10").Stones(P1, "K10").Stones(P2, "D4").Stones(P3, "D9");

    [Fact]
    public void 候选使下一名对手出局()
    {
        // 候选 A（J8）提走 P1 在盘上的全部棋子 → 对 A，下一名对手是 P2；候选 B（E5）不提 P1 的子 → 对 B，下一名对手是 P1。
        // 名册按候选在 B1 上判定，不按决策起点判定。变异 M-A5b（按决策起点判定出局）→ 见段 A 实施记录。
        (PlayerId, int)? ForCandidate(string cell)
        {
            MatchFlow match = CapturablePosition();
            RehearsalResult b1 = Rehearse(match, cell);
            Assert.True(b1.IsLegal, b1.Failure?.Message);
            MatchPublicView view = match.Publish();
            return ExpertLookahead.NextOpponent(view, P0, ExpertLookahead.Project(view, b1.ProjectedBoard!));
        }

        Assert.Equal((P2, 5), ForCandidate("K9"));
        Assert.Equal((P1, 5), ForCandidate("F6"));
    }

    /// <summary>
    /// 第 3 大回合、顺序 P3 → P1 → P2 → P0（专家末位），P3 已弃赛、P1 与 P2 已 Pass。
    /// P1 在自己的出生区角上有一块两眼活棋（眼 G1、J1）——P2 的禁入格因此非空；P2 势力最高。
    /// </summary>
    private static MatchFlow LastSeatPosition()
    {
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(3, [P3, P1, P2, P0])
            .Stones(P0, "C5", "D5", "E5", "E4")
            .Stones(P1, "G3", "J3", "G4", "H4", "J4", "K4")
            .Stones(P2, "C7", "D7", "E7", "F7", "G7", "H7", "J7", "K7", "C9", "E9", "F9", "H9", "K9")
            .Stones(P3, "F10");
        match.Resign(P3);
        match.PassTurn();
        match.PassTurn();
        Assert.Equal(P0, match.CurrentPlayer);
        Assert.Equal(3, match.MajorRound);
        return match;
    }

    [Fact]
    public void 专家是本大回合末位()
    {
        // 第 3 大回合专家末位行动：本大回合专家之后没有名册玩家 → 用 InitiativeOrder 的唯一实现、按 B1 上的公开输入预测第 4 大回合顺序。
        // P2 势力最高而居首 → 下一名对手 P2，按第 4 大回合模拟：分阶段基础部署上限 4，合法落子范围为对手所在战场（四人同锁的棋盘 0）的可落子格扣除 P2 的禁入格
        // （retire-legacy-rules 1.4：规格 ai-decision「前瞻中的下一名对手」Scenario「专家是本大回合末位」由"全图可落子格"改为对手所在战场）。
        // 若按本大回合顺序回绕（P3 已弃赛 → P1），会模拟错人。
        // 变异 M-A5c（末位按本大回合顺序回绕）、M-A6d（应答者仍按第 3 大回合的部署上限 / 范围）→ 见段 A 实施记录。
        MatchFlow match = LastSeatPosition();
        RehearsalResult b1 = Rehearse(match, "D4");
        Assert.True(b1.IsLegal, b1.Failure?.Message);
        MatchPublicView view = match.Publish();
        MatchPublicView projected = ExpertLookahead.Project(view, b1.ProjectedBoard!);

        // 前提：B1 上 P2 势力最高（因果声明用断言钉住）——预测顺序因此以 P2 居首，而本大回合顺序回绕会得到 P1。
        PowerSnapshot power = PowerCalculator.Compute(b1.ProjectedBoard!, match.Roster);
        Assert.True(power.Of(P2).Total > power.Of(P0).Total && power.Of(P2).Total > power.Of(P1).Total, AiFixtures.PowerText(power));

        Assert.Equal((P2, 4), ExpertLookahead.NextOpponent(view, P0, projected));

        BatchContext context = ExpertLookahead.SimulatedContext(projected, P2, 4);
        GameBoard board = b1.ProjectedBoard!;
        System.Collections.Immutable.ImmutableArray<Coord> forbidden = LifeShapeReport.Analyze(board).ForbiddenCellsFor(P2);
        Assert.Contains(TestMaps.At("H3"), forbidden);
        Assert.Contains(TestMaps.At("K3"), forbidden);
        Coord[] expected = [.. match.Map.Boards[0].Cells().Where(c => board[c].Terrain == Terrain.Playable && !forbidden.Contains(c)).Order()];   // 本战场（棋盘 0）的可落子格
        Assert.Equal(expected, context.LegalRange.Order());
        Assert.Equal(4, context.DeployLimit);
        Assert.Equal(P2, context.Player);
        Assert.Equal(4, context.StockOf(PieceType.Basic));
        Assert.Single(context.Stock);
    }

    [Fact]
    public void 专家在预测顺序中居首()
    {
        // 专家是本大回合末位，按 B1 预测的下一大回合顺序为 专家 → P3 → P1 → 下一名对手为 P3（一层前瞻只模拟对手，不模拟自己的下一手）。
        // 变异 M-A5d（居首时返回专家本人）→ 见段 A 实施记录。
        MatchFlow match = AiFixtures.Round5()
            .AtRound(5, [P2, P1, P3, P0])
            .Stones(P0, "C3", "D3", "E3", "F3", "G3", "H3", "J3", "K3", "C5", "E5", "G5")
            .Stones(P1, "G10")
            .Stones(P2, "K10")
            .Stones(P3, "C10", "D10", "E10");
        match.Resign(P2);
        match.PassTurn();
        match.PassTurn();
        Assert.Equal(P0, match.CurrentPlayer);

        RehearsalResult b1 = Rehearse(match, "J5");
        MatchPublicView view = match.Publish();
        MatchPublicView projected = ExpertLookahead.Project(view, b1.ProjectedBoard!);
        PowerSnapshot power = PowerCalculator.Compute(b1.ProjectedBoard!, match.Roster);
        Assert.True(power.Of(P0).Total > power.Of(P3).Total && power.Of(P3).Total > power.Of(P1).Total, AiFixtures.PowerText(power));

        Assert.Equal((P3, 6), ExpertLookahead.NextOpponent(view, P0, projected));
    }

    /// <summary>
    /// 第 5 大回合、顺序 P0（专家）→ P1 → P2 → P3，P2 / P3 已弃赛（只剩 P0 与 P1）。P1 在盘上只有 B2 一子（曾建立正势力），
    /// 被 P0 的 A2 / C2 / B1 围住、只剩一口气 B3——在 P0 的出生区里。P0 的部署上限放到 9：贪心组批会走完全部候选落点，每个候选都含 B3。
    /// expert-tiebreak 1.5：原为第 2 大回合——构筑期决策不前瞻，局面移到全图阶段（平坦 8×8 载体的五块棋盘互不连通、四人同锁棋盘 0，第 2 与第 5 大回合的合法范围同为棋盘 0，几何上中性）。
    /// </summary>
    internal static MatchFlow LastOpponentPosition()
    {
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [P0, P1, P2, P3])
            .Stones(P0, "C4", "E4", "D3").Stones(P1, "D4").Stones(P2, "D10").Stones(P3, "K10");
        match.Resign(P2);
        match.Resign(P3);
        match.SetDeployLimit(9);
        return match;
    }

    [Fact]
    public void 只剩自己时退化()
    {
        // 每个候选都提走 P1 在盘上的全部棋子 → 每个候选都没有下一名对手、不做模拟，前瞻后分数等于自身总分，选择与同一局面上的高难相同。
        HeuristicTurnController hard = DecideHard(LastOpponentPosition());
        HeuristicTurnController expert = DecideExpert(LastOpponentPosition());
        LookaheadRecord record = Record(expert);

        Assert.True(record.Entries.Length >= 2, record.ToText());
        Assert.All(record.Entries, e =>
        {
            Assert.Contains(TestMaps.At("D5"), CoordsOf(e.CandidateKey));
            Assert.Null(e.Responder);
            Assert.Null(e.ResponseKey);
            Assert.Equal(e.ScoreBefore, e.ScoreAfter);
        });
        Assert.Equal(0, record.SimulatedRehearsals);
        Assert.Equal(hard.LastChoice!.Key, expert.LastChoice!.Key);
        Assert.Equal(BigInteger.Zero, record.Entries[record.ChosenIndex].ScoreAfter - hard.LastChoice.Total);
    }

    [Fact]
    public void 构筑期不判定下一名对手()
    {
        // 规格（expert-tiebreak）：第 2 大回合专家 P0 之后还有在局玩家（P1–P3）→ 构筑期决策，前瞻记录中没有下一名对手与模拟回应（不调用下一名对手的判定）。
        // 变异 T-B5（去掉构筑期判定）→ 本测试红。
        LookaheadRecord record = Record(DecideExpert(专家难度的一层前瞻Tests.BuildContact(2, [P0, P1, P2, P3])));
        Assert.Equal(LookaheadStatus.NotApplied, record.Status);
        LookaheadEntry only = Assert.Single(record.Entries);
        Assert.Null(only.Responder);
        Assert.Null(only.ResponderRound);
        Assert.Null(only.SimulatedDeployLimit);
        Assert.Null(only.ResponseKey);
        Assert.Equal(0, record.SimulatedRehearsals);
    }

    // ---------- ai-seat-vision（裁决 V2 / D2，tasks 1.2）：链接模式下名册限同战场，修 C12 ----------

    [Fact]
    public void 不同出生棋盘的玩家进名册()
    {
        // 规格 Scenario「不同出生棋盘的玩家进名册」（open-map；编号映射：专家 P0、同棋盘 P1 在出生棋盘 0；P2、P3 在出生棋盘 3、4，整局只有一个战场）：
        // 第 5 大回合本大回合顺序 P0、P2、P1、P3 → 每个候选的下一名对手都是 P2，模拟回应的合法落子范围为全图可落子格扣除 P2 的禁入格；全知视图与按座位投影视图（= 全图）都如此。
        // 专家末位时（顺序 P1、P2、P3、P0）跨大回合预测在全部名册玩家之间比较 → 下一名对手是预测顺序里的第一名他人，行动大回合 + 1。
        // 原「他战场玩家不进名册」随 open-map 反向（名册 = 全部其他在局玩家）。
        // expert-tiebreak 1.5：原为第 2 大回合、断言"构筑期内模拟范围 = 其出生棋盘（棋盘 4）"——构筑期决策不前瞻、不模拟，被模拟的下一名对手总在全图阶段行动，
        // 局面改到第 5 大回合（第 1 大回合四人各落 1 子后把大回合摆到 5），范围断言改为全图口径。
        MatchFlow match = LinkModeFixtures.Started([0, 0, 3, 4]);
        match.PlayRounds(1, TestMaps.All);
        match.Debug.SetMajorRound(5);
        int round = match.MajorRound;
        Assert.Equal(5, round);
        Assert.True(match.Publish().Power!.Of(P2).Total > 0, "样本口径：别的棋盘上的玩家确有势力");
        Assert.All(TestMaps.All, p => Assert.True(match.StateOf(p).HasEstablishedPower, $"样本口径：{p} 曾建立正势力"));
        Assert.All(TestMaps.All, p => Assert.Equal(match.BattlefieldOf(P0), match.BattlefieldOf(p)));

        MatchPublicView seatView = match.PublishSeatProjection(P0);
        Assert.Equal(match.Publish().Power!.Of(P2).Total, seatView.Power!.Of(P2).Total);   // 按座位投影含全图：P2 的势力可读
        HashSet<Coord> board3 = [.. match.Map.BirthBoard(3).Cells()];

        foreach (MatchPublicView start in new[] { match.Publish(), seatView })
        {
            MatchPublicView view = start with { ActionOrder = [P0, P2, P1, P3] };
            MatchPublicView projected = ExpertLookahead.Project(view, view.Board, P0);
            Assert.All(new[] { P1, P2, P3 }, p => Assert.Equal(PlayerStatus.Active, projected.Players.Single(s => s.Player == p).Status));
            Assert.Equal((P2, round), ExpertLookahead.NextOpponent(view, P0, projected));
            // 全图阶段模拟对手的合法落子范围 = 全图可落子格 − P2 的禁入格（含其出生棋盘之外的格）；改造范围为全图。
            Batch.BatchContext simulated = ExpertLookahead.SimulatedContext(projected, P2, round);
            Coord[] expected = [.. projected.Board.AllCoords().Where(c => projected.Board.Map.IsPlayable(c)).Except(projected.LifeShape.ForbiddenCellsFor(P2)).Order()];
            Assert.Equal(expected, simulated.LegalRange.Order());
            Assert.Contains(simulated.LegalRange, c => !board3.Contains(c));
            Assert.NotNull(simulated.EditScope);
            Assert.Equal(projected.Board.AllCoords().Count(), simulated.EditScope!.Count);

            MatchPublicView last = start with { ActionOrder = [P1, P2, P3, P0] };
            (PlayerId Responder, int MajorRound)? next = ExpertLookahead.NextOpponent(last, P0, ExpertLookahead.Project(last, projected.Board, P0));
            Assert.NotNull(next);
            Assert.NotEqual(P0, next!.Value.Responder);
            Assert.Equal(round + 1, next.Value.MajorRound);
        }
    }

}
