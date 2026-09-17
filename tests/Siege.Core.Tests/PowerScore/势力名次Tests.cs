using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PowerScore;

/// <summary>规格：power-score —— Requirement: 势力名次</summary>
public class 势力名次Tests
{
    /// <summary>堡垒子×4 + 倍增子×1 横排在 B–F 列：基础 17 × 1.5 = 25（scoring-sites 起独占空格不计分；旧口径加 12 独占 = 37，方法名保留）。</summary>
    private static GameBoard Place37(GameBoard board, PlayerId owner, int row)
    {
        foreach (char col in "BCDE")
        {
            board.Place($"{col}{row}", owner, PieceType.Fortress);
        }

        return board.Place($"F{row}", owner, PieceType.Multiplier);
    }

    [Fact]
    public void 排除非参赛玩家()
    {
        // 4 人局中 P2 已出局（无棋子）、P3 已弃赛（有棋子）→ 名次只含 P0、P1，名次 1 与 2。
        // 变异验证 M9：PlayerPower.IsRanked 改为 Status != Eliminated（弃赛者参与名次）→ 红 4，含本测试。
        GameBoard board = TestMaps.Blank(size: 11);
        Place37(board, TestMaps.P0, row: 2);
        board.Place("F6", TestMaps.P1);
        foreach (char col in "BCDEFG")
        {
            board.Place($"{col}9", ScoringFixtures.P3, PieceType.Fortress);
        }

        PowerSnapshot snapshot = PowerCalculator.Compute(board, ScoringFixtures.Roster(
            (TestMaps.P0, PlayerStatus.Active), (TestMaps.P1, PlayerStatus.Active),
            (ScoringFixtures.P2, PlayerStatus.Eliminated), (ScoringFixtures.P3, PlayerStatus.Resigned)), SiteValues.Standard);

        Assert.Equal(4, snapshot.Players.Length);
        Assert.Equal(0, snapshot.Of(ScoringFixtures.P2).Total);
        // scoring-sites 2.7 改写：旧 P3 38（24 + 14 独占）、名次 (1, 37) (2, 5) → 新 P3 24、(1, 25) (2, 1)；名次结构不变。
        Assert.Equal(24, snapshot.Of(ScoringFixtures.P3).Total);
        Assert.Equal([(1, 25L), (2, 1L)], snapshot.Ranking.Select(r => (r.Rank, r.Power)));
        Assert.Equal([TestMaps.P0], snapshot.Ranking[0].Players);
        Assert.Equal([TestMaps.P1], snapshot.Ranking[1].Players);
        Assert.Null(snapshot.RankOf(ScoringFixtures.P2));
        Assert.Null(snapshot.RankOf(ScoringFixtures.P3));
    }

    [Fact]
    public void 并列如实输出()
    {
        // P0 与 P1 均为 25 → 同一名次组、标记并列，不自行打破；P2 势力 1 排在其后，名次跳号为 3。
        // scoring-sites 2.7 改写：旧 37 / 37 / 5（含领地分）→ 25 / 25 / 1。
        // 变异验证 M10：Rank 在同值组内逐人各发一个名次（1、2、3）→ 红 2（本测试、「Pass也触发更新」）。
        GameBoard board = TestMaps.Blank(size: 11);
        Place37(board, TestMaps.P0, row: 2);
        Place37(board, TestMaps.P1, row: 9);
        board.Place("K6", ScoringFixtures.P2);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(25, snapshot.Of(TestMaps.P0).Total);
        Assert.Equal(25, snapshot.Of(TestMaps.P1).Total);
        Assert.Equal(2, snapshot.Ranking.Length);
        RankGroup tied = snapshot.Ranking[0];
        Assert.True(tied.IsTied);
        Assert.Equal((1, 25L), (tied.Rank, tied.Power));
        Assert.Equal([TestMaps.P0, TestMaps.P1], tied.Players);
        RankGroup third = snapshot.Ranking[1];
        Assert.False(third.IsTied);
        Assert.Equal((3, 1L), (third.Rank, third.Power));
        Assert.Equal([ScoringFixtures.P2], third.Players);
        Assert.Equal(1, snapshot.RankOf(TestMaps.P0));
        Assert.Equal(1, snapshot.RankOf(TestMaps.P1));
        Assert.Equal(3, snapshot.RankOf(ScoringFixtures.P2));
    }

    [Fact]
    public void 名单上无棋子的参赛者仍参与名次()
    {
        // 名册列出但盘面上没有棋子的参赛玩家（如刚被提光但尚未判出局）势力为 0，仍占一个名次。
        GameBoard board = TestMaps.Blank(size: 9).Place("D4", TestMaps.P0);

        PowerSnapshot snapshot = PowerCalculator.Compute(
            board, ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active), (TestMaps.P1, PlayerStatus.Active)), SiteValues.Standard);

        Assert.Equal(0, snapshot.Of(TestMaps.P1).Total);
        Assert.Equal(2, snapshot.RankOf(TestMaps.P1));
    }

    [Fact]
    public void 名册外的盘面玩家视为接线错误()
    {
        // 契约边界（implement.md 5.1）：带名册的重载 MUST 对名册未列出、却在盘面上有棋子的玩家抛 SiegeRuleException，
        // 不得静默当作参赛中——那会掩盖流程层漏传弃赛 / 出局状态的接线错误。无名册重载则把盘面玩家一律视为参赛中。
        // 变异验证 C2：ComputeCore 去掉 ContainsKey 检查、回退为 TryGetValue ?? Active → 本测试红。
        GameBoard board = TestMaps.Blank(size: 9).Place("D4", TestMaps.P0).Place("H8", TestMaps.P1);

        SiegeRuleException error = Assert.Throws<SiegeRuleException>(
            () => PowerCalculator.Compute(board, ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active)), SiteValues.Standard));
        Assert.Contains("P1", error.Message, StringComparison.Ordinal);

        PowerSnapshot unlisted = PowerCalculator.Compute(board);
        Assert.All(unlisted.Players, p => Assert.Equal(PlayerStatus.Active, p.Status));
        Assert.Equal(1, unlisted.RankOf(TestMaps.P1));
    }
}
