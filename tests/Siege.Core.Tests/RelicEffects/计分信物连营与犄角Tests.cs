using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>
/// 规格：more-pieces-relics relic-effects —— Requirement: 计分信物：连营与犄角（D3）。
/// 连营 / 犄角在每次势力计算时按当前盘面的控制读取、不经效果快照；额外加值并入连珠 / 协同来源并被倍率放大；
/// 争议、无人控制、弃赛 / 出局者控制的都不生效。输入是"已知信物内容"：正式结算传真实内容（<see cref="RelicLedger.TrueContents"/>）。
/// </summary>
public class 计分信物连营与犄角Tests
{
    private static IReadOnlyDictionary<PlayerId, PlayerStatus> Active => RelicFixtures.AllActive(TestMaps.P0, TestMaps.P1);

    private static PowerSnapshot Truth(GameBoard board, RelicLedger ledger, IReadOnlyDictionary<PlayerId, PlayerStatus>? roster = null) =>
        PowerCalculator.Compute(board, roster ?? Active, ledger.TrueContents());

    [Fact]
    public void 连营加成()
    {
        // 规格 Scenario：A 控制 1 枚连营（G2，直接占据），连珠子构成长度 3（C5–E5）与长度 2（C7–C8）两条线 → 连珠加值 (6 + 3) + (2 + 2) = 13。
        // 对照：同一盘面不传已知信物内容（旧重载）→ 6 + 2 = 8。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("G2", RelicFixtures.Encampment()));
        board.Place("G2", TestMaps.P0)
            .Place("C5", TestMaps.P0, PieceType.Line).Place("D5", TestMaps.P0, PieceType.Line).Place("E5", TestMaps.P0, PieceType.Line)
            .Place("C7", TestMaps.P0, PieceType.Line).Place("C8", TestMaps.P0, PieceType.Line)
            .Place("J9", TestMaps.P1);

        Assert.Equal(13, Truth(board, ledger).Of(TestMaps.P0).Groups.Sum(g => g.LineBonus));
        Assert.Equal(8, PowerCalculator.Compute(board, Active).Of(TestMaps.P0).Groups.Sum(g => g.LineBonus));
    }

    [Fact]
    public void 两枚连营()
    {
        // 规格 Scenario：A 控制 2 枚连营，连珠子构成一条长度 2 的线 → 2 + 2 × 2 = 6。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("G2", RelicFixtures.Encampment()), ("G4", RelicFixtures.Encampment()));
        board.Place("G2", TestMaps.P0).Place("G4", TestMaps.P0)
            .Place("C5", TestMaps.P0, PieceType.Line).Place("D5", TestMaps.P0, PieceType.Line)
            .Place("J9", TestMaps.P1);

        Assert.Equal(6, Truth(board, ledger).GroupContaining(TestMaps.P0, "C5").LineBonus);
    }

    [Fact]
    public void 犄角加成()
    {
        // 规格 Scenario：A 控制 1 枚犄角，一条棋串含协同子 ×1、普通子 ×1、堡垒子 ×1 → 协同加值 1 × 2 × 3 = 6（无犄角时为 4）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("G2", RelicFixtures.Pincer()));
        board.Place("G2", TestMaps.P0)
            .Place("C5", TestMaps.P0, PieceType.Synergy).Place("D5", TestMaps.P0).Place("E5", TestMaps.P0, PieceType.Fortress)
            .Place("J9", TestMaps.P1);

        Assert.Equal(6, Truth(board, ledger).GroupContaining(TestMaps.P0, "C5").SynergyBonus);
        Assert.Equal(4, PowerCalculator.Compute(board, Active).GroupContaining(TestMaps.P0, "C5").SynergyBonus);
    }

    [Fact]
    public void 连营加值被倍率放大()
    {
        // 规格 Scenario：A 控制 1 枚连营，一条棋串由 4 枚连珠子连成横线（B5–E5）、再连上 2 枚倍增子（F5、G5）
        // → 基础军势 6，连珠加值 12 + 4 = 16，棋串军势 ⌊22 × 2.25⌋ = 49（连营的 +4 与原加值一起被倍率放大，不是 ⌊18 × 2.25⌋ + 4 = 44）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("B8", RelicFixtures.Encampment()));
        board.Place("B8", TestMaps.P0)
            .Place("B5", TestMaps.P0, PieceType.Line).Place("C5", TestMaps.P0, PieceType.Line)
            .Place("D5", TestMaps.P0, PieceType.Line).Place("E5", TestMaps.P0, PieceType.Line)
            .Place("F5", TestMaps.P0, PieceType.Multiplier).Place("G5", TestMaps.P0, PieceType.Multiplier)
            .Place("J9", TestMaps.P1);

        GroupPower group = Truth(board, ledger).GroupContaining(TestMaps.P0, "B5");
        Assert.Equal(6, group.BaseTotal);
        Assert.Equal(16, group.LineBonus);
        Assert.Equal(2, group.MultiplierCount);
        Assert.Equal(49, (int)group.Power);
    }

    [Fact]
    public void 失去控制立即失效()
    {
        // 规格 Scenario：玩家 B 的批次结算使玩家 A 失去其唯一的连营 → 该次结算后的势力重算中 A 的连珠加值立即回落，MUST NOT 等到 A 的下一个小回合。
        // 走真实对局流程：A = P0 以 E4 唯一覆盖连营 E5，连珠线 D8–F8（长度 3）；A 先行动一个小回合（生成过 A 的快照），
        // 随后 B = P1 落 E6 使 E5 争议。B 确认后势力榜里 A 的连珠加值由 9 回落为 6，此时仍未轮到 A。
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Encampment())]).AtRound(7, MatchFixtures.All);
        match.Board.Place(TestMaps.At("E4"), TestMaps.P0, PieceType.Basic);
        foreach (string cell in new[] { "D8", "E8", "F8" })
        {
            match.Board.Place(TestMaps.At(cell), TestMaps.P0, PieceType.Line);
        }

        match.Debug.Recalculate();
        Assert.Equal(9, match.Scoreboard.Latest!.GroupContaining(TestMaps.P0, "D8").LineBonus);

        match.PassTurn();                       // A（P0）的小回合：快照在此生成
        Assert.Equal(9, match.Scoreboard.Latest!.GroupContaining(TestMaps.P0, "D8").LineBonus);
        match.PlayTurn("E6");                   // B（P1）的批次：E5 变为争议

        Assert.Equal(RelicControl.Contested, match.Relics.ControlOf(TestMaps.At("E5")));
        Assert.Equal(6, match.Scoreboard.Latest!.GroupContaining(TestMaps.P0, "D8").LineBonus);
    }

    [Fact]
    public void 争议不生效()
    {
        // 规格 Scenario：一枚犄角 E5 同时被 A（E4）与 B（E6）覆盖 → A 与 B 的协同子每种加值都为 2。
        // A：协同子 B2 + 普通子 B3（1 × 1 × 2 = 2）；B：协同子 H8 + 普通子 H7（同为 2）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E5", RelicFixtures.Pincer()));
        board.Place("E4", TestMaps.P0).Place("E6", TestMaps.P1)
            .Place("B2", TestMaps.P0, PieceType.Synergy).Place("B3", TestMaps.P0)
            .Place("H8", TestMaps.P1, PieceType.Synergy).Place("H7", TestMaps.P1);
        ledger.Settle(board, 1);
        Assert.Equal(RelicControl.Contested, ledger.ControlOf(TestMaps.At("E5")));   // 前提

        Assert.Equal(2, Truth(board, ledger).GroupContaining(TestMaps.P0, "B2").SynergyBonus);
        Assert.Equal(2, Truth(board, ledger).GroupContaining(TestMaps.P1, "H8").SynergyBonus);
    }

    [Fact]
    public void 弃赛者不因计分信物得分()
    {
        // 规格 Scenario：已弃赛玩家 D（P3）的遗留棋子唯一覆盖一枚连营 E5，D 的盘面上有一条长度 3 的连珠线 → 该连营显示为由 D 控制（封锁），D 的连珠加值仍为 6。
        // 对照：同一盘面 D 若参赛中，连珠加值为 9——证明是名册而不是盘面让它失效。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E5", RelicFixtures.Encampment()));
        board.Place("E4", RelicFixtures.P3)
            .Place("B7", RelicFixtures.P3, PieceType.Line).Place("C7", RelicFixtures.P3, PieceType.Line).Place("D7", RelicFixtures.P3, PieceType.Line)
            .Place("J9", TestMaps.P0);
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = RelicFixtures.Roster((TestMaps.P0, PlayerStatus.Active), (RelicFixtures.P3, PlayerStatus.Resigned));
        ledger.Settle(board, 3, roster);

        Assert.Equal(new RelicControl(RelicControlKind.Blocked, RelicFixtures.P3), ledger.ControlOf(TestMaps.At("E5")));
        Assert.Equal(6, Truth(board, ledger, roster).GroupContaining(RelicFixtures.P3, "B7").LineBonus);

        IReadOnlyDictionary<PlayerId, PlayerStatus> active = RelicFixtures.AllActive(TestMaps.P0, RelicFixtures.P3);
        Assert.Equal(9, Truth(board, ledger, active).GroupContaining(RelicFixtures.P3, "B7").LineBonus);
    }

    [Fact]
    public void 已结算盘面上真实内容与已揭示内容结果相同()
    {
        // 规格正文（D3）：在任一已结算盘面上，按真实信物内容与按已揭示的公开内容计算的势力 MUST 相同——受控信物必然已被覆盖或占据，因而必然已揭示。
        // 性质测试（tasks 2.5）：v2、标准图、种子 0–149 的信物生成 + 种子驱动的随机盘面（十种棋子类型随机、所有者随机），不跑整局；
        // 每个盘面先做一次"结算"（第 4 步揭示 + 第 5 步重算控制），再比较两种输入下全部玩家的势力明细。
        // 样本口径下界：足够多的盘面上计分信物确实改变了势力（与"不传已知内容"不同），否则两边相等是恒真。
        MapData map = FourPlayerBaseMap.Create();
        PlayerId[] players = [TestMaps.P0, TestMaps.P1, RelicFixtures.P2, RelicFixtures.P3];
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = RelicFixtures.AllActive(players);
        PieceType[] types = [.. ContentSets.PieceTypesOf(ContentSet.V2)];
        int affected = 0;
        for (ulong seed = 0; seed < 150; seed++)
        {
            var ledger = new RelicLedger(RelicGenerator.Generate(map, new GameSeed(seed), ContentSet.V2));
            GameBoard board = GameBoard.Load(map);
            RandomStream rng = new GameSeed(seed).Stream("scoring-relic-property");
            foreach (Coord c in board.AllCoords())
            {
                if (board[c].Terrain == Terrain.Playable && rng.NextInt(3) == 0)
                {
                    board.Place(c, players[rng.NextInt(players.Length)], types[rng.NextInt(types.Length)]);
                }
            }

            ledger.Settle(board, 1, roster);
            ImmutableSortedDictionary<Coord, RelicType> revealed = ledger.PublicStates()
                .Where(s => s.Content is not null)
                .ToImmutableSortedDictionary(s => s.Coord, s => s.Content!.Value.Type);

            PowerSnapshot truth = PowerCalculator.Compute(board, roster, ledger.TrueContents());
            PowerSnapshot open = PowerCalculator.Compute(board, roster, revealed);
            PowerSnapshot none = PowerCalculator.Compute(board, roster);
            foreach (PlayerId player in players)
            {
                Assert.Equal(truth.Of(player).Total, open.Of(player).Total);
                Assert.Equal(truth.Of(player).Groups.Select(g => g.ToString()), open.Of(player).Groups.Select(g => g.ToString()));
            }

            affected += players.Any(p => truth.Of(p).Total != none.Of(p).Total) ? 1 : 0;
        }

        // 实测种子 0–59 中 6 个盘面受影响（约 10%），150 个种子期望约 15 个；下界取 8。
        Assert.True(affected >= 8, $"只有 {affected} 个盘面的势力受计分信物影响，样本不足以证明两种输入等价。");
    }
}
