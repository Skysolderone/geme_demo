using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PowerScore;

/// <summary>规格：power-score —— Requirement: 势力明细</summary>
public class 势力明细Tests
{
    [Fact]
    public void 明细可复算总势力()
    {
        // 规格 Scenario「明细可复算总势力」：据点分总计 + 全部棋串军势之和 = 总势力，对每名玩家成立；Total 是计算层独立算出的字段，这里用明细反向复算。
        // scoring-sites 2.7 改写：旧口径 领地分（= 独占空格数）+ 军势 → 新口径 据点分 + 军势。盘面补三个据点：营帐 A2（只被 P0 覆盖 → P0 5）、
        // 石碑 H2（P0 的 G2 与 P2 的 H3 同时覆盖 → 争议）、篝火 J5（只被 P1 的 H5 / J6 覆盖 → P1 15）。据点分用测试内的字面分值表复算，不读 SiteValues。
        // 变异验证：M20（Total 漏据点分）、M8（协同自身计入类型）、M3（连珠拆子区间）、M32（协同并入 LineBonus）都让本测试红。
        GameBoard board = TestMaps.Blank(size: 11, "F6")
            .WithSites(("A2", SiteTier.Tent), ("H2", SiteTier.Stele), ("J5", SiteTier.Campfire))
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
            long recomputed = snapshot.SiteStates.Where(s => s.Controller == player.Player)
                .Sum(s => s.Tier switch { SiteTier.Tent => 5L, SiteTier.Campfire => 15L, SiteTier.Stele => 45L, _ => throw new InvalidOperationException() });
            Assert.Equal(recomputed, player.SiteScore);
            foreach (GroupPower g in player.Groups)
            {
                Assert.Equal(g.LineBonus + g.SynergyBonus + g.HighGroundBonus, g.PositionBonus);
                // multiplier-rebalance check 替换：原用 PowerCalculator.GroupPowerOf 复算（比较被测方法与它的委托目标，恒真）。
                // 改为测试内独立整数式 ⌊基础 × 3^e / 2^e⌋ + 加值，e = min(n, 3)（规格字面封顶 3，不读 Multiplier.MaxExponent）。
                // 变异验证 C-MR1（check）：GroupPowerOf 改回 Apply(baseTotal + positionBonus) → 本测试红（P1 串 15 ≠ 13）。
                int e = Math.Min(g.MultiplierCount, 3);
                long pow3 = 1;
                long pow2 = 1;
                for (int i = 0; i < e; i++)
                {
                    pow3 *= 3;
                    pow2 *= 2;
                }

                Assert.Equal((g.BaseTotal * pow3 / pow2) + g.PositionBonus, g.Power);
                Assert.Equal(g.Stones.Sum(s => Siege.Core.Scoring.PieceEffects.BasePower(board[s].Occupant!.Value.Type)), g.BaseTotal);
                Assert.Equal(g.Stones.Count(s => board[s].Occupant!.Value.Type == PieceType.Multiplier), g.MultiplierCount);
                recomputed += g.Power;
            }

            Assert.Equal(recomputed, player.Total);
        }

        Assert.Equal(5, snapshot.Of(TestMaps.P0).SiteScore);
        Assert.Equal(15, snapshot.Of(TestMaps.P1).SiteScore);
        Assert.Equal(0, snapshot.Of(ScoringFixtures.P2).SiteScore);
        Assert.Equal(SiteControlKind.Contested, snapshot.SiteStates.Single(s => s.Coord == TestMaps.At("H2")).Kind);

        // 抽查一条：连珠 B9-C9-D9 + 协同 D10（其他类型只有连珠 → 2）→ 基础 4 + 加值 8 = 12
        GroupPower lineGroup = snapshot.GroupContaining(TestMaps.P0, "D10");
        Assert.Equal((4, 6, 2, 0, 12L), (lineGroup.BaseTotal, lineGroup.LineBonus, lineGroup.SynergyBonus, lineGroup.MultiplierCount, lineGroup.Power));

        // 抽查含倍增子且含位置加值的一条（check 补）：堡垒 H5 + 倍增 H6 + 协同 J6（其他类型 {堡垒, 倍增} → 4）→ ⌊6 × 1.5⌋ + 4 = 13（旧公式 ⌊10 × 1.5⌋ = 15）
        GroupPower mixedGroup = snapshot.GroupContaining(TestMaps.P1, "H6");
        Assert.Equal((6, 0, 4, 1, 13L), (mixedGroup.BaseTotal, mixedGroup.LineBonus, mixedGroup.SynergyBonus, mixedGroup.MultiplierCount, mixedGroup.Power));
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
    public void 据点分可溯源()
    {
        // 规格 Scenario：据点分总计 65 → 明细列出所控据点，各分值之和为 65，且标明占据或唯一覆盖。
        // 标准局 5 / 15 / 45：P0 占据营帐 C3（5）、唯一覆盖石碑 E4（45，被 E3 覆盖）、唯一覆盖篝火 B4（15，被 B3 覆盖）；另有争议据点 G4 不计。
        // 变异验证 M-S8（段 A2）：SiteControl 把 Occupied 与 UniqueCoverage 映射对调 → 红，含本测试。
        GameBoard board = TestMaps.Blank(size: 9)
            .WithSites(("C3", SiteTier.Tent), ("E4", SiteTier.Stele), ("B4", SiteTier.Campfire), ("G4", SiteTier.Stele))
            .Place("B3", TestMaps.P0).Place("C3", TestMaps.P0).Place("D3", TestMaps.P0).Place("E3", TestMaps.P0)
            .Place("G3", TestMaps.P0).Place("G5", TestMaps.P1);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal(65, p0.SiteScore);
        Assert.Equal(
            [("B4", SiteTier.Campfire, 15, SiteControlKind.UniqueCoverage), ("C3", SiteTier.Tent, 5, SiteControlKind.Occupied), ("E4", SiteTier.Stele, 45, SiteControlKind.UniqueCoverage)],
            p0.Sites.Select(s => (s.Coord.ToNotation(), s.Tier, s.Value, s.Kind)).OrderBy(t => t.Item1));
        Assert.Equal(65, p0.Sites.Sum(s => s.Value));
    }

    [Fact]
    public void 明细字段完整()
    {
        // 规格要求的最少字段：据点分总计与控制据点清单、独占空格坐标集合（只展示）、每条棋串的（棋子坐标、基础军势、位置加值、加值来源拆分、倍增子数量、倍率、取整后军势）。
        // scoring-sites 2.7 改写：旧期望 领地分 14、总势力 34（14 + 20）→ 新期望 无据点、据点分 0、独占空格仍 14 个、总势力 20。
        GameBoard board = TestMaps.Blank(size: 9).PlaceStandardGroup(TestMaps.P0, row: 2);

        PlayerPower p0 = PowerCalculator.Compute(board).Of(TestMaps.P0);
        GroupPower group = Assert.Single(p0.Groups);

        Assert.Equal(0, p0.SiteScore);
        Assert.Empty(p0.Sites);
        Assert.Equal(14, p0.ExclusiveCells.Length);
        Assert.Equal(["B2", "C2", "D2", "E2", "F2", "G2"], group.Stones.Notations());
        Assert.Equal(TestMaps.P0, group.Owner);
        Assert.Equal(9, group.BaseTotal);
        Assert.Equal(0, group.PositionBonus);
        Assert.Equal((0, 0, 0), (group.LineBonus, group.SynergyBonus, group.HighGroundBonus));
        Assert.Equal(2, group.MultiplierCount);
        Assert.Equal(new Multiplier(2), group.Multiplier);
        Assert.Equal(20, group.Power);
        Assert.Equal(20, p0.Total);
    }

    [Fact]
    public void 明细区分原始与生效倍率()
    {
        // cap-multiplier 规格：某棋串含 9 枚倍增子 → 明细中倍增子数量、生效倍率指数、倍率三者同时可读。
        // multiplier-rebalance 改写：封顶 3 → 数量 9、生效指数 3、倍率 3.375、军势 ⌊9 × 27 / 8⌋ = ⌊30.375⌋ = 30（封顶 4 时为 4 / 5.0625 / 81/16 / 45；封顶 5 时为 5 / 7.59375 / 243/32 / 68）。
        // 变异验证 M-M4：GroupPower / MultiplierPeak 的 EffectiveMultiplierCount 改为 => MultiplierCount（生效字段退化成原始字段）→ 红 6，含本测试。
        // 变异验证 M-M3 / M-GP6（growth-pass-1 重跑）：Multiplier.ToString 改用原始 Count → 红 5，含本测试（38.443359375）。
        GameBoard board = TestMaps.Blank(size: 11);
        for (int x = 1; x <= 9; x++)
        {
            board.Place(new Coord(x, 2), TestMaps.P0, PieceType.Multiplier);
        }

        GroupPower group = Assert.Single(PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);

        Assert.Equal(9, group.MultiplierCount);
        Assert.Equal(3, group.EffectiveMultiplierCount);
        Assert.Equal("3.375", group.Multiplier.ToString());
        Assert.Equal((Int128)27, group.Multiplier.Numerator);
        Assert.Equal((Int128)8, group.Multiplier.Denominator);
        Assert.Equal(30, group.Power);
    }

    [Fact]
    public void 遥测峰值保留原始数量并可得生效指数()
    {
        // Requirement 势力明细：遥测倍率峰值 SHALL 保留原始倍增子数量，并同时可得生效倍率指数（implement.md 2.2：8 枚 → 数量 8、生效 4）。
        // 峰值按原始数量比较，因此第 9 枚仍会刷新峰值（生效指数不变仍为 4）——原始数量语义不因封顶而改。
        // growth-pass-1 改写：封顶 4 → 生效 5→4、显示 7.59375→5.0625、军势 60→40（8 枚）与 68→45（9 枚）。
        // multiplier-rebalance 改写：封顶 3 → 生效 4→3、显示 5.0625→3.375、军势 40→27（8 枚，⌊8 × 27 / 8⌋）与 45→30（9 枚，⌊9 × 27 / 8⌋）。
        // 变异验证 M-M4：MultiplierPeak.EffectiveMultiplierCount 改为 => MultiplierCount → 红 6，含本测试；M-M3 / M-GP6（ToString 用原始指数，growth-pass-1 重跑）→ 红 5，含本测试；M-M4 重跑仍红 6。
        GameBoard board = TestMaps.Blank(size: 11);
        for (int x = 1; x <= 8; x++)
        {
            board.Place(new Coord(x, 2), TestMaps.P0, PieceType.Multiplier);
        }

        var scoreboard = new PowerScoreboard();
        var roster = ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active));
        scoreboard.Recalculate(board, roster, SiteValues.Standard, majorRound: 1);

        MultiplierPeak peak = scoreboard.Peak!;
        Assert.Equal(8, peak.MultiplierCount);
        Assert.Equal(3, peak.EffectiveMultiplierCount);
        Assert.Equal("3.375", peak.Multiplier.ToString());
        Assert.Equal(27, peak.Power);

        board.Place(new Coord(9, 2), TestMaps.P0, PieceType.Multiplier);
        scoreboard.Recalculate(board, roster, SiteValues.Standard, majorRound: 2);

        peak = scoreboard.Peak!;
        Assert.Equal((9, 3, 2), (peak.MultiplierCount, peak.EffectiveMultiplierCount, peak.MajorRound));
        Assert.Equal(30, peak.Power);
    }

    [Fact]
    public void 明细可复算棋串军势()
    {
        // multiplier-rebalance power-score 规格：对任一棋串，⌊基础军势总和 × 倍率⌋ + 位置加值 = 取整后军势。
        // 复算完全在测试里用整数做：倍率取明细的精确十进制显示串（如 "3.375" → 3375 / 1000），不调用被测的 Multiplier.Apply / GroupPowerOf
        //（testing.md：比较被测方法与它的委托目标是恒真形状）。这样显示串与计算用的倍率也被一起钉住。
        // 覆盖两类棋串：含位置加值且含倍增子（A、B、D）、不含位置加值（C 标准算例）、含加值不含倍增子（E）；D 同时越过封顶。
        // 每条的期望值手算（旧公式下依次为 40 / 10 / 20 / 27 / 7）：
        //   A 连珠×4 横线 + 倍增×2：⌊6 × 2.25⌋ + 12 = 25        B 协同 + 倍增 + 普通：⌊3 × 1.5⌋ + 协同 2×2 = 4 + 4 = 8
        //   C §10.1 标准算例：⌊9 × 2.25⌋ + 0 = 20              D 倍增×5 + 协同：⌊6 × 3.375⌋ + 2 = 20 + 2 = 22
        //   E 连珠×2 + 协同：3 × 1 + 连珠 2 + 协同 2 = 7
        // 变异验证 M-MR5：把 Multiplier.Apply 改为向上取整 → 本测试在复算循环的断言处红；同时把复算改用 g.Multiplier.Apply(g.BaseTotal) + g.PositionBonus →
        // 复算循环全部通过、只剩末尾手算字面值断言红（复算守门变成恒真，印证不能用被测方法复算）。另 M-MR1 红 5、M-MR2 红 23、M-MR4 红 7 均含本测试。
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
        Assert.Contains(p0.Groups, g => g.MultiplierCount > Multiplier.MaxExponent && g.PositionBonus > 0);
        foreach (GroupPower g in p0.Groups)
        {
            (long numerator, long denominator) = DecimalFraction(g.Multiplier.ToString());
            Assert.Equal(g.BaseTotal * numerator / denominator + g.PositionBonus, g.Power);
        }

        Assert.Equal(
            new[] { ("B2", 6, 12, 2, 25L), ("B4", 3, 4, 1, 8L), ("B6", 9, 0, 2, 20L), ("B8", 6, 2, 5, 22L), ("B10", 3, 4, 0, 7L) },
            p0.Groups.Select(g => (g.Stones[0].ToNotation(), g.BaseTotal, g.PositionBonus, g.MultiplierCount, g.Power)).OrderBy(t => int.Parse(t.Item1[1..])));
    }

    /// <summary>把精确十进制串（"1"、"2.25"、"3.375"）转成分数 (分子, 10^小数位数)；纯整数运算，不经过浮点。</summary>
    private static (long Numerator, long Denominator) DecimalFraction(string text)
    {
        int dot = text.IndexOf('.');
        if (dot < 0)
        {
            return (long.Parse(text), 1);
        }

        long denominator = 1;
        for (int i = dot + 1; i < text.Length; i++)
        {
            denominator *= 10;
        }

        return (long.Parse(text.Remove(dot, 1)), denominator);
    }
}
