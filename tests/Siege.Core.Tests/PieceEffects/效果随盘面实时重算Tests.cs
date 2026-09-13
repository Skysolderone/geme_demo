using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceEffects;

/// <summary>
/// 规格：piece-effects —— Requirement: 效果随盘面实时重算。
/// 四邻接下一条棋串不可能只被提走"中间一枚"（围杀整串移除），规格里的三个算例都是假设性盘面变化；
/// 这里用棋盘层的 <see cref="GameBoard.RemoveStones"/> 模拟那次移除——被测的是"分数只由当前盘面决定"，不是提子规则本身。
/// </summary>
public class 效果随盘面实时重算Tests
{
    [Fact]
    public void 连珠断开立即掉分()
    {
        // 设计文档 §9.2：长度 4 的连珠线 C6–F6 = 12；移走中间的 D6、E6 后两侧各为孤立连珠子 → 0。
        // 只移走 D6 时 E6–F6 仍是长度 2 的线 → 2，同样不保留 12。
        // 变异验证：M3（连珠拆子区间）、M24（L×L）、M28（同玩家全部棋子并成一串）都让本测试红。
        GameBoard board = TestMaps.Blank(size: 9);
        foreach (string c in new[] { "C6", "D6", "E6", "F6" })
        {
            board.Place(c, TestMaps.P0, PieceType.Line);
        }

        var scoreboard = new PowerScoreboard();
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active));
        Assert.Equal(12, Assert.Single(scoreboard.Recalculate(board, roster, 1).Of(TestMaps.P0).Groups).LineBonus);

        board.RemoveStones([TestMaps.At("D6")]);
        PlayerPower afterOne = scoreboard.Recalculate(board, roster, 2).Of(TestMaps.P0);
        Assert.Equal(2, afterOne.Groups.Length);
        Assert.Equal(2, afterOne.Groups.Sum(g => g.LineBonus));

        board.RemoveStones([TestMaps.At("E6")]);
        PlayerPower afterTwo = scoreboard.Recalculate(board, roster, 3).Of(TestMaps.P0);
        Assert.Equal(2, afterTwo.Groups.Length);
        Assert.All(afterTwo.Groups, g => Assert.Equal(0, g.LineBonus));
        Assert.Equal(0, afterTwo.Groups.Sum(g => g.LineBonus));
    }

    [Fact]
    public void 倍增子被提走()
    {
        // 含 2 枚倍增子的棋串被提走 1 枚 → 倍率由 2.25 降为 1.5，不保留原倍率。
        GameBoard board = TestMaps.Blank(size: 9).PlaceStandardGroup(TestMaps.P0, row: 2);
        var scoreboard = new PowerScoreboard();
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active));
        GroupPower before = Assert.Single(scoreboard.Recalculate(board, roster, 1).Of(TestMaps.P0).Groups);
        Assert.Equal("2.25", before.Multiplier.ToString());
        Assert.Equal(20, before.Power);

        board.RemoveStones([TestMaps.At("G2")]);

        GroupPower after = Assert.Single(scoreboard.Recalculate(board, roster, 2).Of(TestMaps.P0).Groups);
        Assert.Equal(1, after.MultiplierCount);
        Assert.Equal("1.5", after.Multiplier.ToString());
        Assert.Equal(8, after.BaseTotal);
        Assert.Equal(12, after.Power); // ⌊8 × 1.5⌋
    }

    [Fact]
    public void 棋串分裂重算()
    {
        // 一条棋串 B2..G2（含 2 枚倍增子 F2/G2）从 D2 处断开为 B2-C2 与 E2-F2-G2 两条 →
        // 按两条独立棋串分别重算：左串基础 2、倍率 1；右串基础 4+1+1 = 6、倍率 2.25 → 13。
        // 变异验证 M28：Compute 把同一玩家的全部棋子合并成一条 GroupPower（跳过 AllGroups 的划分）→ 红 8，含本测试。
        GameBoard board = TestMaps.Blank(size: 9).PlaceStandardGroup(TestMaps.P0, row: 2);
        Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        board.RemoveStones([TestMaps.At("D2")]);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);
        Assert.Equal(2, p0.Groups.Length);
        GroupPower left = p0.Groups.Single(g => g.Stones.Contains(TestMaps.At("B2")));
        GroupPower right = p0.Groups.Single(g => g.Stones.Contains(TestMaps.At("G2")));
        Assert.Equal(["B2", "C2"], left.Stones.Notations());
        Assert.Equal((2, 0, 2L), (left.BaseTotal, left.MultiplierCount, left.Power));
        Assert.Equal(["E2", "F2", "G2"], right.Stones.Notations());
        Assert.Equal((6, 2, 13L), (right.BaseTotal, right.MultiplierCount, right.Power));
    }
}
