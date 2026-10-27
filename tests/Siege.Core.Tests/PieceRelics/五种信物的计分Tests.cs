using Siege.Core.Board;
using Siege.Core.PieceRelics;
using Siege.Core.Scoring;
using static Siege.Core.Tests.PieceRelics.PieceRelicFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign RQ3 / AC3（不含旗手子与同分比较，它们在接线段改）：五种信物各自进计分（design.md D3）——
/// 战鼓：持物棋子基础军势 +3；号令：棋串位置加值 + ⌊棋串棋子数 / 2⌋；百变：位置加值 +2、协同子的其他类型种数 +1；
/// 军旗：倍率指数 +1；都在 <c>(基础 + 位置) × 1.5^n</c> 取整之前。数值是未校准初值，期望值按 prd.md R12 表手算。
/// 变异验证（加法段实跑）：M6 百变不给协同子加类型（ExtraSynergyTypes 恒 0）→ 红 3。
/// </summary>
public class 五种信物的计分Tests
{
    [Fact]
    public void 战鼓让持物棋子基础军势加三()
    {
        GameBoard board = TestMaps.Blank(size: 9).Place("B2", TestMaps.P0).Place("C2", TestMaps.P0).Attach("B2", Map(0, RelicKind.WarDrum));

        GroupPower group = OnlyGroup(board, TestMaps.P0);

        Assert.Equal(1 + 3 + 1, group.BaseTotal);
        Assert.Equal(0, group.RelicPositionBonus);
        Assert.Equal(5, group.Power);
    }

    [Fact]
    public void 战鼓与倍增子叠加时先加再乘()
    {
        // (倍增子 1 + 战鼓 3 + 普通子 1) × 1.5 = 7.5 → 7
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Multiplier).Place("C2", TestMaps.P0).Attach("B2", Map(0, RelicKind.WarDrum));

        Assert.Equal(7, OnlyGroup(board, TestMaps.P0).Power);
    }

    [Fact]
    public void 号令按棋串棋子数的一半取整计位置加值()
    {
        // 5 枚普通子成串，号令挂在 B2：⌊5 / 2⌋ = 2 → 5 + 2 = 7
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0).Place("C2", TestMaps.P0).Place("D2", TestMaps.P0).Place("E2", TestMaps.P0).Place("F2", TestMaps.P0)
            .Attach("B2", Map(0, RelicKind.Command));

        GroupPower group = OnlyGroup(board, TestMaps.P0);

        Assert.Equal(2, group.RelicPositionBonus);
        Assert.Equal(group.LineBonus + group.SynergyBonus + group.HighGroundBonus + group.BannerBonus + group.ChainBonus + group.SentryBonus + group.BoundaryBonus + 2, group.PositionBonus);
        Assert.Equal(7, group.Power);
    }

    [Fact]
    public void 两件号令叠加()
    {
        // 4 枚成串、两件号令：2 × ⌊4 / 2⌋ = 4 → 4 + 4 = 8
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0).Place("C2", TestMaps.P0).Place("D2", TestMaps.P0).Place("E2", TestMaps.P0)
            .Attach("B2", Map(0, RelicKind.Command)).Attach("E2", Map(1, RelicKind.Command));

        Assert.Equal(8, OnlyGroup(board, TestMaps.P0).Power);
    }

    [Fact]
    public void 棋串分裂后号令按新棋串重算()
    {
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0).Place("C2", TestMaps.P0).Place("D2", TestMaps.P0).Place("E2", TestMaps.P0).Place("F2", TestMaps.P0)
            .Attach("B2", Map(0, RelicKind.Command));
        Assert.Equal(7, OnlyGroup(board, TestMaps.P0).Power);

        board.RemoveStones([TestMaps.At("D2")]);

        // B2–C2 两枚：2 + ⌊2 / 2⌋ = 3；E2–F2 两枚无信物：2。
        PlayerPower player = PowerCalculator.Compute(board).Of(TestMaps.P0);
        Assert.Equal([3, 2], player.Groups.Select(g => (int)g.Power));
        Assert.Equal(1, player.Groups[0].RelicPositionBonus);
    }

    [Fact]
    public void 百变给位置加值二并让协同子多算一种类型()
    {
        // 协同子 B2 + 普通子 C2（百变挂在 C2）：其他类型 1 + 1 = 2 → 协同 1 × 2 × 2 = 4；百变 +2 → 2 + 4 + 2 = 8
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Synergy).Place("C2", TestMaps.P0).Attach("C2", Map(0, RelicKind.Chameleon));

        GroupPower group = OnlyGroup(board, TestMaps.P0);

        Assert.Equal(4, group.SynergyBonus);
        Assert.Equal(2, group.RelicPositionBonus);
        Assert.Equal(8, group.Power);
    }

    [Fact]
    public void 百变挂在协同子自己身上同样多算一种()
    {
        GameBoard onSynergy = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Synergy).Place("C2", TestMaps.P0).Attach("B2", Map(0, RelicKind.Chameleon));

        Assert.Equal(4, OnlyGroup(onSynergy, TestMaps.P0).SynergyBonus);
        Assert.Equal(8, OnlyGroup(onSynergy, TestMaps.P0).Power);
    }

    [Fact]
    public void 纯协同子棋串挂百变后也有协同加值()
    {
        // 两枚协同子：其他类型 0 + 1 = 1 → 协同 2 × 1 × 2 = 4；百变 +2 → 2 + 4 + 2 = 8
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Synergy).Place("C2", TestMaps.P0, PieceType.Synergy).Attach("B2", Map(0, RelicKind.Chameleon));

        Assert.Equal(4, OnlyGroup(board, TestMaps.P0).SynergyBonus);
        Assert.Equal(8, OnlyGroup(board, TestMaps.P0).Power);
    }

    [Fact]
    public void 没有协同子时百变只给位置加值()
    {
        GameBoard board = TestMaps.Blank(size: 9).Place("B2", TestMaps.P0).Place("C2", TestMaps.P0).Attach("B2", Map(0, RelicKind.Chameleon));

        GroupPower group = OnlyGroup(board, TestMaps.P0);

        Assert.Equal(0, group.SynergyBonus);
        Assert.Equal(2 + 2, group.Power);
    }

    [Fact]
    public void 军旗让倍率指数加一()
    {
        // 两枚普通子、一件军旗：2 × 1.5 = 3
        GameBoard board = TestMaps.Blank(size: 9).Place("B2", TestMaps.P0).Place("C2", TestMaps.P0).Attach("C2", Map(0, RelicKind.Banner));

        GroupPower group = OnlyGroup(board, TestMaps.P0);

        Assert.Equal(1, group.RelicExponent);
        Assert.Equal(1, group.MultiplierExponent);
        Assert.Equal(3, group.Power);
    }

    [Fact]
    public void 军旗可叠加并与倍增子相乘()
    {
        // 倍增子 B2 + 普通子 C2、D2，军旗挂在 C2、D2：指数 1 + 2 = 3 → 3 × 3.375 = 10.125 → 10
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Multiplier).Place("C2", TestMaps.P0).Place("D2", TestMaps.P0)
            .Attach("C2", Map(0, RelicKind.Banner)).Attach("D2", Map(1, RelicKind.Banner));

        GroupPower group = OnlyGroup(board, TestMaps.P0);

        Assert.Equal(3, group.MultiplierExponent);
        Assert.Equal(10, group.Power);
    }

    [Fact]
    public void 军旗与阵型倍率叠加()
    {
        // 计分规则 v2：3 枚成串为阵型 1 阶；军旗 1 件 → 指数 2 → 3 × 2.25 = 6.75 → 6
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0).Place("C2", TestMaps.P0).Place("D2", TestMaps.P0).Attach("B2", Map(0, RelicKind.Banner));

        GroupPower group = OnlyGroupV2(board, TestMaps.P0);

        Assert.Equal(1, group.FormationTier);
        Assert.Equal(1, group.RelicExponent);
        Assert.Equal(2, group.MultiplierExponent);
        Assert.Equal(6, group.Power);
    }

    [Fact]
    public void 位置加值与基础军势一起被倍率放大()
    {
        // 倍增子 B2 + 普通子 C2，号令挂在 C2：(2 + ⌊2/2⌋) × 1.5 = 4.5 → 4
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B2", TestMaps.P0, PieceType.Multiplier).Place("C2", TestMaps.P0).Attach("C2", Map(0, RelicKind.Command));

        Assert.Equal(4, OnlyGroup(board, TestMaps.P0).Power);
    }

    [Fact]
    public void 信物只作用于所在棋串()
    {
        // B2 挂战鼓；D2 是另一条棋串（C2 空），不受影响。
        GameBoard board = TestMaps.Blank(size: 9).Place("B2", TestMaps.P0).Place("D2", TestMaps.P0).Attach("B2", Map(0, RelicKind.WarDrum));

        PlayerPower player = PowerCalculator.Compute(board).Of(TestMaps.P0);

        Assert.Equal([4, 1], player.Groups.Select(g => (int)g.Power));
    }

    [Fact]
    public void 空格上的地图信物不产生效果()
    {
        GameBoard plain = TestMaps.Blank(size: 9).Place("B2", TestMaps.P0).Place("C2", TestMaps.P0);
        GameBoard withMapRelic = TestMaps.Blank(size: 9).Place("B2", TestMaps.P0).Place("C2", TestMaps.P0).Attach("D2", Map(0, RelicKind.Banner));

        Assert.Equal(PowerCalculator.Compute(plain).Of(TestMaps.P0).Total, PowerCalculator.Compute(withMapRelic).Of(TestMaps.P0).Total);
    }

    [Fact]
    public void 未挂出的带入信物不产生效果()
    {
        // AC4：带入清单里的信物在挂出之前不在盘面上，只经盘面信物层读取的计分看不到它们。
        GameBoard board = TestMaps.Blank(size: 9).Place("B2", TestMaps.P0).Place("C2", TestMaps.P0);
        var loadout = new RelicLoadout([new(RelicKind.WarDrum, true), new(RelicKind.Banner, false), new(RelicKind.Command, false)]);
        System.Collections.Immutable.ImmutableArray<RelicToken> tokens = loadout.ToTokens(TestMaps.P0, firstId: 0);
        System.Numerics.BigInteger before = OnlyGroup(board, TestMaps.P0).Power;

        Assert.Equal(2, before);
        Assert.False(board.HasRelics);

        board.AttachRelic(TestMaps.At("B2"), tokens[0]);
        Assert.Equal(5, OnlyGroup(board, TestMaps.P0).Power);
    }

    [Fact]
    public void 无信物时明细的信物项为零且文字不变()
    {
        GameBoard board = TestMaps.Blank(size: 9).Place("B2", TestMaps.P0).Place("C2", TestMaps.P0);

        GroupPower group = OnlyGroup(board, TestMaps.P0);

        Assert.Equal(0, group.RelicPositionBonus);
        Assert.Equal(0, group.RelicExponent);
        Assert.DoesNotContain("信物", group.ToString());
        Assert.DoesNotContain("军旗", group.ToString());
    }
}
