using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PowerScore;

/// <summary>规格：power-score —— Requirement: 势力明细</summary>
public class 势力明细Tests
{
    [Fact]
    public void 明细可复算总势力()
    {
        // 领地分总计 + 全部棋串军势之和 = 总势力，对每名玩家成立；Total 是计算层独立算出的字段，这里用明细反向复算。
        // 变异验证：M20（Total 漏领地）、M8（协同自身计入类型）、M3（连珠拆子区间）、M32（协同并入 LineBonus）都让本测试红。
        GameBoard board = TestMaps.Blank(size: 11, "F6")
            .PlaceStandardGroup(TestMaps.P0, row: 2)
            .Place("B9", TestMaps.P0, PieceType.Line).Place("C9", TestMaps.P0, PieceType.Line).Place("D9", TestMaps.P0, PieceType.Line)
            .Place("D10", TestMaps.P0, PieceType.Synergy)
            .Place("H5", TestMaps.P1, PieceType.Fortress).Place("H6", TestMaps.P1, PieceType.Multiplier).Place("J6", TestMaps.P1, PieceType.Synergy)
            .Place("G3", ScoringFixtures.P2).Place("H3", ScoringFixtures.P2, PieceType.Line);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(3, snapshot.Players.Length);
        foreach (PlayerPower player in snapshot.Players)
        {
            Assert.NotEmpty(player.Groups);
            Assert.Equal(player.ExclusiveCells.Length, player.TerritoryScore);
            long recomputed = player.TerritoryScore;
            foreach (GroupPower g in player.Groups)
            {
                Assert.Equal(g.LineBonus + g.SynergyBonus, g.PositionBonus);
                Assert.Equal(PowerCalculator.GroupPowerOf(g.BaseTotal, g.PositionBonus, g.MultiplierCount), g.Power);
                Assert.Equal(g.Stones.Sum(s => Siege.Core.Scoring.PieceEffects.BasePower(board[s].Occupant!.Value.Type)), g.BaseTotal);
                Assert.Equal(g.Stones.Count(s => board[s].Occupant!.Value.Type == PieceType.Multiplier), g.MultiplierCount);
                recomputed += g.Power;
            }

            Assert.Equal(recomputed, player.Total);
        }

        // 抽查一条：连珠 B9-C9-D9 + 协同 D10（其他类型只有连珠 → 2）→ 基础 4 + 加值 8 = 12
        GroupPower lineGroup = snapshot.GroupContaining(TestMaps.P0, "D10");
        Assert.Equal((4, 6, 2, 0, 12L), (lineGroup.BaseTotal, lineGroup.LineBonus, lineGroup.SynergyBonus, lineGroup.MultiplierCount, lineGroup.Power));
    }

    [Fact]
    public void 位置加值可溯源()
    {
        // 位置加值 14 = 连珠 6（B2-C2-D2 长度 3）+ 协同 8（协同子×2 × 其他类型 {连珠, 普通} 2 种 × 2）
        // 变异验证 M32：Evaluate 把 synergyBonus 并进 LineBonus 字段（SynergyBonus 恒 0）→ 红 5，含本测试；M26（协同每串只计一次）→ 红 2，含本测试。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Line).Place("C2", TestMaps.P0, PieceType.Line).Place("D2", TestMaps.P0, PieceType.Line)
            .Place("E2", TestMaps.P0, PieceType.Synergy).Place("F2", TestMaps.P0, PieceType.Synergy)
            .Place("G2", TestMaps.P0, PieceType.Basic);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(14, group.PositionBonus);
        Assert.Equal(6, group.LineBonus);
        Assert.Equal(8, group.SynergyBonus);
        Assert.Equal(6 + 14, group.Power);
    }

    [Fact]
    public void 明细字段完整()
    {
        // 规格要求的最少字段：领地分总计、独占空格坐标集合、每条棋串的（棋子坐标、基础军势、位置加值、加值来源拆分、倍增子数量、倍率、取整后军势）。
        GameBoard board = TestMaps.Blank(size: 9).PlaceStandardGroup(TestMaps.P0, row: 2);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);
        GroupPower group = Assert.Single(p0.Groups);

        Assert.Equal(14, p0.TerritoryScore);
        Assert.Equal(14, p0.ExclusiveCells.Length);
        Assert.Equal(["B2", "C2", "D2", "E2", "F2", "G2"], group.Stones.Notations());
        Assert.Equal(TestMaps.P0, group.Owner);
        Assert.Equal(9, group.BaseTotal);
        Assert.Equal(0, group.PositionBonus);
        Assert.Equal((0, 0), (group.LineBonus, group.SynergyBonus));
        Assert.Equal(2, group.MultiplierCount);
        Assert.Equal(new Multiplier(2), group.Multiplier);
        Assert.Equal(20, group.Power);
        Assert.Equal(34, p0.Total);
    }

    [Fact]
    public void 明细区分原始与生效倍率()
    {
        // cap-multiplier 规格：某棋串含 9 枚倍增子 → 明细中倍增子数量 9、生效倍率指数 5、倍率 7.59375，三者同时可读；军势 ⌊9 × 243 / 32⌋ = 68。
        // 变异验证 M-M4：GroupPower / MultiplierPeak 的 EffectiveMultiplierCount 改为 => MultiplierCount（生效字段退化成原始字段）→ 红 6，含本测试。
        // 变异验证 M-M3：Multiplier.ToString 改用原始 Count → 红 3，含本测试（25.62890625）。
        GameBoard board = TestMaps.Blank(size: 11);
        for (int x = 1; x <= 9; x++)
        {
            board.Place(new Coord(x, 2), TestMaps.P0, PieceType.Multiplier);
        }

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(9, group.MultiplierCount);
        Assert.Equal(5, group.EffectiveMultiplierCount);
        Assert.Equal("7.59375", group.Multiplier.ToString());
        Assert.Equal((Int128)243, group.Multiplier.Numerator);
        Assert.Equal((Int128)32, group.Multiplier.Denominator);
        Assert.Equal(68, group.Power);
    }

    [Fact]
    public void 遥测峰值保留原始数量并可得生效指数()
    {
        // Requirement 势力明细：遥测倍率峰值 SHALL 保留原始倍增子数量，并同时可得生效倍率指数（implement.md 2.2：8 枚 → 数量 8、生效 5）。
        // 峰值按原始数量比较，因此第 9 枚仍会刷新峰值（生效指数不变仍为 5）——原始数量语义不因封顶而改。
        // 变异验证 M-M4：MultiplierPeak.EffectiveMultiplierCount 改为 => MultiplierCount → 红 6，含本测试；M-M3（ToString 用原始指数）→ 红 3，含本测试。
        GameBoard board = TestMaps.Blank(size: 11);
        for (int x = 1; x <= 8; x++)
        {
            board.Place(new Coord(x, 2), TestMaps.P0, PieceType.Multiplier);
        }

        var scoreboard = new PowerScoreboard();
        var roster = ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active));
        scoreboard.Recalculate(board, roster, majorRound: 1);

        MultiplierPeak peak = scoreboard.Peak!;
        Assert.Equal(8, peak.MultiplierCount);
        Assert.Equal(5, peak.EffectiveMultiplierCount);
        Assert.Equal("7.59375", peak.Multiplier.ToString());
        Assert.Equal(60, peak.Power);

        board.Place(new Coord(9, 2), TestMaps.P0, PieceType.Multiplier);
        scoreboard.Recalculate(board, roster, majorRound: 2);

        peak = scoreboard.Peak!;
        Assert.Equal((9, 5, 2), (peak.MultiplierCount, peak.EffectiveMultiplierCount, peak.MajorRound));
        Assert.Equal(68, peak.Power);
    }
}
