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
            .Stones(P0, "E5").Stones(P1, "B8").Stones(P2, "H2").Stones(P3, "H8");
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
            .Stones(P0, "E5").Stones(P1, "J9").Stones(P2, "B2").Stones(P3, "H9");
        match.PlayTurn("J8");
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(P1).Status);
        Assert.Equal(P0, match.CurrentPlayer);

        LookaheadRecord record = Record(DecideExpert(match));

        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.True(record.Entries.Length >= 2, record.ToText());
        Assert.All(record.Entries, e => Assert.Equal(P2, e.Responder));
    }

    /// <summary>顺序 P0（专家）→ P1 → P2 → P3；P1 在盘上只有 J9 一子（曾建立正势力），被 P0 的 H9 逼到只剩一口气 J8。</summary>
    private static MatchFlow CapturablePosition() =>
        AiFixtures.Round5().Stones(P0, "H9").Stones(P1, "J9").Stones(P2, "B2").Stones(P3, "B8");

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

        Assert.Equal((P2, 5), ForCandidate("J8"));
        Assert.Equal((P1, 5), ForCandidate("E5"));
    }

    /// <summary>
    /// 第 3 大回合、顺序 P3 → P1 → P2 → P0（专家末位），P3 已弃赛、P1 与 P2 已 Pass。
    /// P1 在自己的出生区角上有一块两眼活棋（眼 G1、J1）——P2 的禁入格因此非空；P2 势力最高。
    /// </summary>
    private static MatchFlow LastSeatPosition()
    {
        MatchFlow match = MatchFixtures.Started().AtRound(3, [P3, P1, P2, P0])
            .Stones(P0, "A3", "B3", "C3", "C2")
            .Stones(P1, "F1", "H1", "F2", "G2", "H2", "J2")
            .Stones(P2, "A6", "B6", "C6", "D6", "E6", "F6", "G6", "H6", "J6", "A8", "C8", "E8", "G8", "J8")
            .Stones(P3, "E9");
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
        // P2 势力最高而居首 → 下一名对手 P2，按第 4 大回合模拟：分阶段基础部署上限 4，合法落子范围为全图可落子格扣除 P2 的禁入格。
        // 若按本大回合顺序回绕（P3 已弃赛 → P1），会模拟错人。
        // 变异 M-A5c（末位按本大回合顺序回绕）、M-A6d（应答者仍按第 3 大回合的部署上限 / 范围）→ 见段 A 实施记录。
        MatchFlow match = LastSeatPosition();
        RehearsalResult b1 = Rehearse(match, "B2");
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
        Assert.Contains(TestMaps.At("G1"), forbidden);
        Assert.Contains(TestMaps.At("J1"), forbidden);
        Coord[] expected = [.. board.AllCoords().Where(c => board[c].Terrain == Terrain.Playable && !forbidden.Contains(c)).Order()];
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
            .Stones(P0, "A1", "B1", "C1", "D1", "E1", "F1", "G1", "H1", "J1", "A3", "C3", "E3")
            .Stones(P1, "E9")
            .Stones(P2, "J9")
            .Stones(P3, "A9", "B9", "C9");
        match.Resign(P2);
        match.PassTurn();
        match.PassTurn();
        Assert.Equal(P0, match.CurrentPlayer);

        RehearsalResult b1 = Rehearse(match, "G3");
        MatchPublicView view = match.Publish();
        MatchPublicView projected = ExpertLookahead.Project(view, b1.ProjectedBoard!);
        PowerSnapshot power = PowerCalculator.Compute(b1.ProjectedBoard!, match.Roster);
        Assert.True(power.Of(P0).Total > power.Of(P3).Total && power.Of(P3).Total > power.Of(P1).Total, AiFixtures.PowerText(power));

        Assert.Equal((P3, 6), ExpertLookahead.NextOpponent(view, P0, projected));
    }

    /// <summary>
    /// 第 2 大回合、顺序 P0（专家）→ P1 → P2 → P3，P2 / P3 已弃赛（只剩 P0 与 P1）。P1 在盘上只有 B2 一子（曾建立正势力），
    /// 被 P0 的 A2 / C2 / B1 围住、只剩一口气 B3——在 P0 的出生区里。P0 的部署上限放到 9：贪心组批会走完全部候选落点，每个候选都含 B3。
    /// </summary>
    private static MatchFlow LastOpponentPosition()
    {
        MatchFlow match = MatchFixtures.Started().AtRound(2, [P0, P1, P2, P3])
            .Stones(P0, "A2", "C2", "B1").Stones(P1, "B2").Stones(P2, "B8").Stones(P3, "H8");
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
            Assert.Contains(TestMaps.At("B3"), CoordsOf(e.CandidateKey));
            Assert.Null(e.Responder);
            Assert.Null(e.ResponseKey);
            Assert.Equal(e.ScoreBefore, e.ScoreAfter);
        });
        Assert.Equal(0, record.SimulatedRehearsals);
        Assert.Equal(hard.LastChoice!.Key, expert.LastChoice!.Key);
        Assert.Equal(BigInteger.Zero, record.Entries[record.ChosenIndex].ScoreAfter - hard.LastChoice.Total);
    }
}
