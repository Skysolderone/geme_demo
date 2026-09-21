using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PowerScore;

/// <summary>规格：power-score —— Requirement: 势力明细</summary>
public class 势力明细Tests
{
    [Fact]
    public void 明细可复算总势力()
    {
        // restore-go-core-rules power-score 规格「明细可复算总势力」：领地分总计 + 全部棋串军势之和 = 总势力，对每名玩家成立；Total 是计算层独立算出的字段，这里用明细反向复算。
        // 段 A 改写：旧口径改为 领地分（= 独占空格数）+ 军势；盘面只留棋子，摆法不变。
        // 变异验证：M-A4（Total 漏领地分）、M-A1（加值挪到乘法之外）都让本测试红。
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
            Assert.NotEmpty(player.ExclusiveCells);
            Assert.Equal(player.ExclusiveCells.Length, player.TerritoryScore);
            BigInteger recomputed = player.ExclusiveCells.Length;
            foreach (GroupPower g in player.Groups)
            {
                Assert.Equal(g.LineBonus + g.SynergyBonus + g.HighGroundBonus, g.PositionBonus);
                // testing.md「"可复算"守门必须用测试内独立算式」：不调用 GroupPowerOf / Multiplier.Apply，
                // 在测试里写 ⌊(基础 + 加值) × 3^n / 2^n⌋，n = 倍增子数量（不封顶）。
                BigInteger expected = (g.BaseTotal + g.PositionBonus) * BigInteger.Pow(3, g.MultiplierCount) / BigInteger.Pow(2, g.MultiplierCount);
                Assert.Equal(expected, g.Power);
                Assert.Equal(g.Stones.Sum(s => Siege.Core.Scoring.PieceEffects.BasePower(board[s].Occupant!.Value.Type)), g.BaseTotal);
                Assert.Equal(g.Stones.Count(s => board[s].Occupant!.Value.Type == PieceType.Multiplier), g.MultiplierCount);
                recomputed += g.Power;
            }

            Assert.Equal(recomputed, player.Total);
        }

        // 抽查一条：连珠 B9-C9-D9 + 协同 D10（其他类型只有连珠 → 2）→ 基础 4 + 加值 8 = 12（无倍增子，新旧公式同值）
        GroupPower lineGroup = snapshot.GroupContaining(TestMaps.P0, "D10");
        Assert.Equal((4, 6, 2, 0, (BigInteger)12), (lineGroup.BaseTotal, lineGroup.LineBonus, lineGroup.SynergyBonus, lineGroup.MultiplierCount, lineGroup.Power));

        // 抽查含倍增子且含位置加值的一条：堡垒 H5 + 倍增 H6 + 协同 J6（其他类型 {堡垒, 倍增} → 4）
        // 段 A 重算：原期望 13（⌊6 × 1.5⌋ + 4）→ ⌊(6 + 4) × 1.5⌋ = 15。
        GroupPower mixedGroup = snapshot.GroupContaining(TestMaps.P1, "H6");
        Assert.Equal((6, 0, 4, 1, (BigInteger)15), (mixedGroup.BaseTotal, mixedGroup.LineBonus, mixedGroup.SynergyBonus, mixedGroup.MultiplierCount, mixedGroup.Power));
    }

    [Fact]
    public void 领地分可溯源()
    {
        // restore-go-core-rules power-score 规格「领地分可溯源」：领地分总计 12 → 明细列出的独占空格坐标恰为 12 个。
        // 盘面同「总势力 / 领地与棋串相加」：第 2 行棋串 B–G 下方 B1–G1 与左端 A2 是障碍 → 独占 B3–G3 + H2 = 7；
        // 第 6 行棋串 B–E 上方 B7–E7 与左端 A6 是障碍 → 独占 B5–E5 + F6 = 5。坐标按字典序（先行后列）。
        // 变异验证 M-AC6（段 A check 实跑）：PlayerPower.TerritoryScore 改为 ExclusiveCells.Length + 1 → 红 19，含本测试。
        GameBoard board = TestMaps.Blank(size: 11, "A2", "A6", "B1", "C1", "D1", "E1", "F1", "G1", "B7", "C7", "D7", "E7")
            .PlaceStandardGroup(TestMaps.P0, row: 2)
            .Place("B6", TestMaps.P0, PieceType.Fortress).Place("C6", TestMaps.P0).Place("D6", TestMaps.P0).Place("E6", TestMaps.P0);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);
        PlayerPower p0 = snapshot.Of(TestMaps.P0);

        Assert.Equal(12, p0.TerritoryScore);
        Assert.Equal(["H2", "B3", "C3", "D3", "E3", "F3", "G3", "B5", "C5", "D5", "E5", "F6"], p0.ExclusiveCells.Notations());
        Assert.Equal(12, p0.ExclusiveCells.Distinct().Count());
        Assert.All(p0.ExclusiveCells, c => Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, TestMaps.P0), snapshot.Coverage.OwnershipOf(c)));
        Assert.All(p0.ExclusiveCells, c => Assert.Null(board[c].Occupant));
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
    public void 位置加值三来源可溯源()
    {
        // 规格 Scenario「位置加值可溯源」（scoring-sites：三部分）：位置加值 14 = 连珠 6（B2-C2-D2 长度 3）+ 协同 4（协同子 E2 × 其他类型 {连珠, 普通} 2 种 × 2）
        // + 高地 4（第 2 行 h=1，B2 / C2 / D2 / E2 各压制正下方 h=0 的 P1 棋子，F2 / G2 下方无敌子）。军势 = 基础 6 + 14 = 20。
        // 变异验证 M-S7（段 A2）：Evaluate 把 highGroundBonus 并进 LineBonus 字段 → 红，含本测试。
        TerrainData terrain = TestMaps.Terrain(heights: [("B2", 1), ("C2", 1), ("D2", 1), ("E2", 1), ("F2", 1), ("G2", 1)]);
        GameBoard board = TestMaps.Blank(terrain, size: 9)
            .Place("B2", TestMaps.P0, PieceType.Line).Place("C2", TestMaps.P0, PieceType.Line).Place("D2", TestMaps.P0, PieceType.Line)
            .Place("E2", TestMaps.P0, PieceType.Synergy).Place("F2", TestMaps.P0).Place("G2", TestMaps.P0)
            .Place("B3", TestMaps.P1).Place("C3", TestMaps.P1).Place("D3", TestMaps.P1).Place("E3", TestMaps.P1);

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal((6, 4, 4), (group.LineBonus, group.SynergyBonus, group.HighGroundBonus));
        Assert.Equal(14, group.PositionBonus);
        Assert.Equal(6 + 14, group.Power);
    }

    [Fact]
    public void 明细字段完整()
    {
        // 规格要求的最少字段：领地分总计、独占空格坐标集合、每条棋串的（棋子坐标、基础军势、位置加值、加值来源拆分、倍增子数量、倍率、取整后军势）。
        // 段 A 重算：原期望总势力 20（scoring-sites：独占空格不计分）→ 领地分 14、总势力 34 = 14 + 20（与 territory-power 时期同值）。
        GameBoard board = TestMaps.Blank(size: 9).PlaceStandardGroup(TestMaps.P0, row: 2);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);
        GroupPower group = Assert.Single(p0.Groups);

        Assert.Equal(14, p0.TerritoryScore);
        Assert.Equal(14, p0.ExclusiveCells.Length);
        Assert.Equal(["B2", "C2", "D2", "E2", "F2", "G2"], group.Stones.Notations());
        Assert.Equal(TestMaps.P0, group.Owner);
        Assert.Equal(9, group.BaseTotal);
        Assert.Equal(0, group.PositionBonus);
        Assert.Equal((0, 0, 0), (group.LineBonus, group.SynergyBonus, group.HighGroundBonus));
        Assert.Equal(2, group.MultiplierCount);
        Assert.Equal(new Multiplier(2), group.Multiplier);
        Assert.Equal(20, group.Power);
        Assert.Equal(34, p0.Total);
    }

    [Fact]
    public void 遥测峰值保留倍增子数量()
    {
        // Requirement 势力明细：遥测中的倍率峰值记录 SHALL 保留倍增子数量。峰值按数量比较，第 9 枚刷新峰值。
        // 段 A 重算（不封顶、删"生效倍率指数"）：原「遥测峰值保留原始数量并可得生效指数」期望 生效 3、显示 3.375、军势 27（8 枚）/ 30（9 枚）
        // → 显示 15^8 / 10^8 = 25.62890625、军势 ⌊8 × 6561 / 256⌋ = 205（8 枚）与 显示 38.443359375、⌊9 × 19683 / 512⌋ = ⌊345.99…⌋ = 345（9 枚）。
        // 变异验证 M-A2（指数夹到 3）→ 红，含本测试。
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
        Assert.Equal("25.62890625", peak.Multiplier.ToString());
        Assert.Equal(205, peak.Power);

        board.Place(new Coord(9, 2), TestMaps.P0, PieceType.Multiplier);
        scoreboard.Recalculate(board, roster, majorRound: 2);

        peak = scoreboard.Peak!;
        Assert.Equal((9, 2), (peak.MultiplierCount, peak.MajorRound));
        Assert.Equal("38.443359375", peak.Multiplier.ToString());
        Assert.Equal(345, peak.Power);
    }

    [Fact]
    public void 明细可复算棋串军势()
    {
        // restore-go-core-rules power-score 规格：对任一棋串，⌊(基础军势总和 + 位置加值) × 倍率⌋ = 取整后军势。
        // 复算完全在测试里用整数做：倍率取明细的精确十进制显示串（如 "3.375" → 3375 / 1000），不调用被测的 Multiplier.Apply / GroupPowerOf
        //（testing.md：比较被测方法与它的委托目标是恒真形状）。这样显示串与计算用的倍率也被一起钉住。
        // 覆盖：含位置加值且含倍增子（A、B、D）、不含位置加值（C 标准算例）、含加值不含倍增子（E）；D 的倍增子数量 5 超过旧封顶 3。
        // 段 A 重算（旧期望 25 / 8 / 20 / 22 / 7 → 新期望 40 / 10 / 20 / 60 / 7）：
        //   A 连珠×4 横线 + 倍增×2：⌊(6 + 12) × 9 / 4⌋ = ⌊40.5⌋ = 40     B 协同 + 倍增 + 普通：⌊(3 + 4) × 3 / 2⌋ = ⌊10.5⌋ = 10
        //   C §10.1 标准算例：⌊9 × 9 / 4⌋ = 20（不变）                    D 倍增×5 + 协同：⌊(6 + 2) × 243 / 32⌋ = ⌊60.75⌋ = 60
        //   E 连珠×2 + 协同：3 + 连珠 2 + 协同 2 = 7（无倍增子，不变）
        // 变异验证 M-A1（加值挪到乘法之外）→ 本测试在复算循环处红；M-A2（指数夹到 3）→ 红（D 串 27）。
        GameBoard board = TestMaps.Blank(size: 13)
            .Place("B2", TestMaps.P0, PieceType.Line).Place("C2", TestMaps.P0, PieceType.Line).Place("D2", TestMaps.P0, PieceType.Line).Place("E2", TestMaps.P0, PieceType.Line)
            .Place("F2", TestMaps.P0, PieceType.Multiplier).Place("G2", TestMaps.P0, PieceType.Multiplier)
            .Place("B4", TestMaps.P0, PieceType.Synergy).Place("C4", TestMaps.P0, PieceType.Multiplier).Place("D4", TestMaps.P0, PieceType.Basic)
            .PlaceStandardGroup(TestMaps.P0, row: 6)
            .Place("B8", TestMaps.P0, PieceType.Multiplier).Place("C8", TestMaps.P0, PieceType.Multiplier).Place("D8", TestMaps.P0, PieceType.Multiplier)
            .Place("E8", TestMaps.P0, PieceType.Multiplier).Place("F8", TestMaps.P0, PieceType.Multiplier).Place("G8", TestMaps.P0, PieceType.Synergy)
            .Place("B10", TestMaps.P0, PieceType.Line).Place("C10", TestMaps.P0, PieceType.Line).Place("D10", TestMaps.P0, PieceType.Synergy);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);
        PlayerPower p0 = snapshot.Of(TestMaps.P0);

        Assert.Equal(5, p0.Groups.Length);
        Assert.Contains(p0.Groups, g => g.PositionBonus > 0 && g.MultiplierCount > 0);
        Assert.Contains(p0.Groups, g => g.PositionBonus == 0 && g.MultiplierCount > 0);
        Assert.Contains(p0.Groups, g => g.PositionBonus > 0 && g.MultiplierCount == 0);
        Assert.Contains(p0.Groups, g => g.MultiplierCount > 3 && g.PositionBonus > 0);
        foreach (GroupPower g in p0.Groups)
        {
            (BigInteger numerator, BigInteger denominator) = DecimalFraction(g.Multiplier.ToString());
            Assert.Equal((g.BaseTotal + g.PositionBonus) * numerator / denominator, g.Power);
        }

        Assert.Equal(
            new[] { ("B2", 6, 12, 2, (BigInteger)40), ("B4", 3, 4, 1, (BigInteger)10), ("B6", 9, 0, 2, (BigInteger)20), ("B8", 6, 2, 5, (BigInteger)60), ("B10", 3, 4, 0, (BigInteger)7) },
            p0.Groups.Select(g => (g.Stones[0].ToNotation(), g.BaseTotal, g.PositionBonus, g.MultiplierCount, g.Power)).OrderBy(t => int.Parse(t.Item1[1..])));
    }

    /// <summary>把精确十进制串（"1"、"2.25"、"3.375"）转成分数 (分子, 10^小数位数)；纯整数运算，不经过浮点。</summary>
    private static (BigInteger Numerator, BigInteger Denominator) DecimalFraction(string text)
    {
        int dot = text.IndexOf('.');
        if (dot < 0)
        {
            return (BigInteger.Parse(text), BigInteger.One);
        }

        BigInteger denominator = BigInteger.One;
        for (int i = dot + 1; i < text.Length; i++)
        {
            denominator *= 10;
        }

        return (BigInteger.Parse(text.Remove(dot, 1)), denominator);
    }
}
