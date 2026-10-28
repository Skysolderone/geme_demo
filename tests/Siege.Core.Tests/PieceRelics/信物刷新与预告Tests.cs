using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.PieceRelics;
using static Siege.Core.Tests.PieceRelics.PieceRelicFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign RQ2 / AC2（design.md D2，R8–R11、R17）：同种子同盘面同人数两次刷新逐项相同；第 1–3 大回合无刷新；刷新格全在出生区域外、
/// 预告时离棋子 ≥ 2；落位后场上未拾取件数不超过 n + 2（R17 掉落件不受此限）；预告比落位早 1 大回合，落位只少不多（被占格跳过）。
/// 盘面用 <see cref="TestMaps.NineByNine(string[])"/>：四个 3×3 角落出生区、无棋盘清单（出生区域退回到出生区本身）。
/// 变异验证（加法段实跑）：M5 候选格不排除出生区域 → 红 2；M7 刷新改用固定的既有子流 recruit → 红 1（各批互不扰动的反面断言）。
/// </summary>
public class 信物刷新与预告Tests
{
    private static GameBoard Board() => GameBoard.LoadUnvalidated(TestMaps.NineByNine())
        .Place("E5", TestMaps.P0).Place("E6", TestMaps.P0).Place("D4", TestMaps.P1);

    private static IBirthArea Birth(GameBoard board) => new BirthBoardArea(board.Map);

    [Fact]
    public void 子流名按批与玩家派生()
    {
        Assert.Equal("relic-spawn:4", GameSeed.RelicSpawn(4));
        Assert.Equal("relic-drop:2", GameSeed.RelicDrop(2));
    }

    [Fact]
    public void 第一到三大回合无刷新且落位轮为第四六八大回合()
    {
        Assert.Equal([4, 6, 8, 10], Enumerable.Range(1, 10).Where(RelicSpawner.IsLandingRound));
        Assert.Equal([3, 5, 7, 9], Enumerable.Range(1, 10).Where(RelicSpawner.IsForecastRound));

        GameBoard board = Board();
        foreach (int round in new[] { 1, 2, 4, 6 })
        {
            Assert.Null(RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, round, playerCount: 4, unclaimedOnBoard: 0));
        }
    }

    [Fact]
    public void 预告比落位早一个大回合()
    {
        GameBoard board = Board();

        RelicForecast forecast = RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 5, playerCount: 4, unclaimedOnBoard: 0)!;

        Assert.Equal(6, forecast.LandingRound);
        Assert.Equal(5, forecast.ForecastRound);
    }

    [Fact]
    public void 同种子同盘面同人数两次刷新逐项相同()
    {
        // 两块独立构造的盘面、同一种子：每个预告轮的位置与类型逐项相同。换种子则不同（反面：确实由种子驱动）。
        GameBoard a = Board();
        GameBoard b = Board();
        var other = new GameSeed(0xA5A5_0001UL);
        bool anyDifferent = false;
        foreach (int round in new[] { 3, 5, 7, 9, 11 })
        {
            RelicForecast fa = RelicSpawner.Forecast(a, Birth(a), TestMaps.Seed, round, 4, 0)!;
            RelicForecast fb = RelicSpawner.Forecast(b, Birth(b), TestMaps.Seed, round, 4, 0)!;
            Assert.Equal(2, fa.Slots.Length);
            Assert.Equal(fa, fb);
            Assert.Equal(fa.Slots.Select(s => (s.Cell, s.Kind)), fb.Slots.Select(s => (s.Cell, s.Kind)));
            anyDifferent |= !fa.Equals(RelicSpawner.Forecast(a, Birth(a), other, round, 4, 0));
        }

        Assert.True(anyDifferent, "换种子后五批刷新全都相同，刷新没有用种子");
    }

    [Fact]
    public void 各批互不扰动()
    {
        // 每批一条子流：第 7 大回合的预告与之前是否预告过第 3、5 大回合无关。
        GameBoard board = Board();
        RelicForecast direct = RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 7, 4, 0)!;

        _ = RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 3, 4, 0);
        _ = RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 5, 4, 0);

        Assert.Equal(direct, RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 7, 4, 0));

        // 反面：各批用各自的子流，同一盘面上不同批次的抽取结果不全相同（若全用同一条固定子流，五批会逐项相同）。
        RelicForecast[] batches = [.. new[] { 3, 5, 7, 9, 11 }.Select(r => RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, r, 4, 0)!)];
        Assert.True(batches.Select(f => string.Join(";", f.Slots)).Distinct().Count() > 1, "五批刷新的位置与类型逐项相同，各批没有独立子流");
    }

    [Fact]
    public void 刷新格全在出生区域外且离棋子至少两格()
    {
        GameBoard board = Board().Attach("H5", Map(90, RelicKind.WarDrum));
        IBirthArea birth = Birth(board);
        Coord[] stones = [.. board.AllCoords().Where(c => board[c].Occupant is not null)];
        int checkedSlots = 0;
        for (ulong s = 1; s <= 60; s++)
        {
            foreach (int round in new[] { 3, 5, 7 })
            {
                foreach (RelicSpawnSlot slot in RelicSpawner.Forecast(board, birth, new GameSeed(s), round, 4, 0)!.Slots)
                {
                    Assert.Null(board.Map.BirthZoneOf(slot.Cell));
                    Assert.True(board[slot.Cell].IsPlayableEmpty);
                    Assert.Null(board.RelicAt(slot.Cell));
                    Assert.All(stones, stone => Assert.True(Chebyshev(stone, slot.Cell) >= 2, $"{slot} 离 {stone.ToNotation()} 不足 2 格"));
                    checkedSlots++;
                }
            }
        }

        Assert.Equal(60 * 3 * 2, checkedSlots);
    }

    [Fact]
    public void 候选格排除出生区域与棋子周围与已有信物的格()
    {
        // 9×9：四个 3×3 角落出生区 36 格；E5、E6、D4 周围（切比雪夫 ≤ 1）三个 3×3 的并集 17 格，其中 C3 已在左下出生区，另计 16 格；H5 已有信物。
        GameBoard board = Board().Attach("H5", Map(90, RelicKind.WarDrum));

        ImmutableArray<Coord> candidates = RelicSpawner.Candidates(board, Birth(board));

        var near = new HashSet<Coord>();
        foreach (Coord stone in new[] { "E5", "E6", "D4" }.Select(TestMaps.At))
        {
            near.UnionWith(board.AllCoords().Where(c => Chebyshev(c, stone) <= 1));
        }

        Coord[] expected = [.. board.AllCoords().Where(c => board.Map.BirthZoneOf(c) is null && !near.Contains(c) && c != TestMaps.At("H5")).Order()];
        Assert.Equal(81 - 36 - 16 - 1, expected.Length);
        Assert.Equal(expected, candidates);
    }

    [Fact]
    public void 中立平台属于公共区域可刷新信物()
    {
        // seat-gap-25 E6（信物刷新区域核对）：出生区域 = 出生座位（BirthBoardArea 只认棋盘清单里的出生棋盘），中立平台（公共棋盘）不算出生区域——
        // 空盘面上，每块座位的格都不是候选，每块中立平台的可落子格都是候选。变异 M-S6（实跑）：BirthBoardArea 改为"在任一棋盘外接矩形内" → 本测试红 1。
        MapData map = Siege.Core.Board.Maps.LinkMapGenerator.Generate(1, 4);
        GameBoard board = GameBoard.Load(map);
        IBirthArea birth = Birth(board);
        BoardPlate[] neutral = [.. map.Boards.Where(b => b.Kind == BoardPlateKind.Public)];
        Assert.Equal(4, neutral.Length);   // 样本口径：link:1:4 有 4 块中立平台
        HashSet<Coord> candidates = [.. RelicSpawner.Candidates(board, birth)];
        Assert.All(map.BirthBoards().SelectMany(b => b.Cells()), c => Assert.True(birth.IsBirthArea(c) && !candidates.Contains(c), c.ToNotation()));
        Assert.All(neutral.SelectMany(b => b.Cells()).Where(map.IsPlayable), c => Assert.True(!birth.IsBirthArea(c) && candidates.Contains(c), c.ToNotation()));
    }

    [Theory]
    [InlineData(4, 0, 2)]
    [InlineData(3, 0, 2)]
    [InlineData(2, 0, 1)]
    [InlineData(4, 5, 1)]
    [InlineData(4, 6, 0)]
    [InlineData(4, 9, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(2, 4, 0)]
    public void 每批件数为人数一半向上取整且受人数加二上限约束(int players, int unclaimed, int expected)
    {
        Assert.Equal(expected, RelicSpawner.BatchSize(players, unclaimed));
    }

    [Fact]
    public void 上限已满时本批为空()
    {
        GameBoard board = Board();

        RelicForecast forecast = RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 3, playerCount: 4, unclaimedOnBoard: 6)!;

        Assert.True(forecast.IsEmpty);
        Assert.Equal(4, forecast.LandingRound);
    }

    [Fact]
    public void 没有候选格时本批为空()
    {
        GameBoard board = TestMaps.Blank(size: 3).Place("B2", TestMaps.P0);

        Assert.True(RelicSpawner.Forecast(board, new BirthBoardArea(board.Map), TestMaps.Seed, 3, 4, 0)!.IsEmpty);
    }

    [Fact]
    public void 整局刷新落位后场上未拾取件数不超过人数加二()
    {
        // 4 人：每批 2 件、上限 6。第 3–21 大回合无人拾取，逐批预告、落位；每次落位后未拾取件数 ≤ 6，最终恰为 6。
        GameBoard board = Board();
        IBirthArea birth = Birth(board);
        RelicForecast? pending = null;
        int nextId = 0;
        for (int round = 1; round <= 21; round++)
        {
            if (RelicSpawner.IsLandingRound(round))
            {
                Assert.NotNull(pending);
                Assert.Equal(round, pending!.LandingRound);
                foreach (BoardRelic landed in RelicSpawner.Land(board, pending, nextId))
                {
                    board.AttachRelic(landed.Cell, landed.Token);
                    nextId++;
                }

                Assert.True(RelicSpawner.UnclaimedMapRelics(board) <= RelicSpawner.Cap(4));
            }

            if (RelicSpawner.IsForecastRound(round))
            {
                pending = RelicSpawner.Forecast(board, birth, TestMaps.Seed, round, 4, RelicSpawner.UnclaimedMapRelics(board));
            }
        }

        Assert.Equal(6, RelicSpawner.UnclaimedMapRelics(board));
        Assert.Equal(6, nextId);
    }

    [Fact]
    public void 落位只少不多且被占格跳过()
    {
        GameBoard board = Board();
        RelicForecast forecast = RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 3, playerCount: 4, unclaimedOnBoard: 0)!;
        Assert.Equal(2, forecast.Slots.Length);

        // 预告后、落位前：第一件的预告格被落了子。
        board.Place(forecast.Slots[0].Cell, TestMaps.P1, PieceType.Basic);

        ImmutableArray<BoardRelic> landed = RelicSpawner.Land(board, forecast, firstId: 10);

        BoardRelic only = Assert.Single(landed);
        Assert.Equal(forecast.Slots[1].Cell, only.Cell);
        Assert.Equal(RelicToken.Spawned(10, forecast.Slots[1].Kind), only.Token);
    }

    [Fact]
    public void 预告格已有信物时落位跳过该件()
    {
        GameBoard board = Board();
        RelicForecast forecast = RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 3, 4, 0)!;
        board.AttachRelic(forecast.Slots[1].Cell, Carried(50, RelicKind.Command, TestMaps.P2));

        ImmutableArray<BoardRelic> landed = RelicSpawner.Land(board, forecast, firstId: 0);

        Assert.Equal([forecast.Slots[0].Cell], landed.Select(r => r.Cell));
    }

    [Fact]
    public void 种类只出普通四种与稀有军旗且两档都会出现()
    {
        GameBoard board = Board();
        var kinds = new HashSet<RelicKind>();
        for (ulong s = 1; s <= 200; s++)
        {
            foreach (RelicSpawnSlot slot in RelicSpawner.Forecast(board, Birth(board), new GameSeed(s), 3, 4, 0)!.Slots)
            {
                kinds.Add(slot.Kind);
            }
        }

        Assert.Equal(RelicKinds.Order.Order(), kinds.Order());
    }

    [Fact]
    public void 出局者未挂出的非保险件掉落在候选格上且不占上限()
    {
        GameBoard board = Board();
        RelicToken[] items = [Carried(20, RelicKind.WarDrum, TestMaps.P1), Carried(21, RelicKind.Command, TestMaps.P1)];

        ImmutableArray<BoardRelic> dropped = RelicSpawner.Drop(board, Birth(board), TestMaps.Seed, TestMaps.P1, items);

        Assert.Equal([20, 21], dropped.Select(d => d.Token.Id));
        Assert.Equal(2, dropped.Select(d => d.Cell).Distinct().Count());
        ImmutableArray<Coord> candidates = RelicSpawner.Candidates(board, Birth(board));
        Assert.All(dropped, d => Assert.Contains(d.Cell, candidates));
        Assert.Equal(dropped, RelicSpawner.Drop(Board(), Birth(board), TestMaps.Seed, TestMaps.P1, items.Reverse()));

        foreach (BoardRelic d in dropped)
        {
            board.AttachRelic(d.Cell, d.Token);
        }

        Assert.Equal(0, RelicSpawner.UnclaimedMapRelics(board));
        Assert.Equal(2, RelicSpawner.BatchSize(4, RelicSpawner.UnclaimedMapRelics(board)));
    }

    [Fact]
    public void 保险件与别人的带入件不掉落()
    {
        GameBoard board = Board();

        Assert.Throws<ArgumentException>(() => RelicSpawner.Drop(board, Birth(board), TestMaps.Seed, TestMaps.P1, [Carried(1, RelicKind.WarDrum, TestMaps.P1, insured: true)]));
        Assert.Throws<ArgumentException>(() => RelicSpawner.Drop(board, Birth(board), TestMaps.Seed, TestMaps.P1, [Carried(2, RelicKind.WarDrum, TestMaps.P0)]));
    }

    [Fact]
    public void 刷新不消费任何既有子流()
    {
        // 各条既有子流的前若干个值与是否调用过刷新 / 掉落无关（子流按名字从种子派生，本来就独立；这里钉住刷新没有改用既有子流名）。
        string[] existing = [GameSeed.Recruit, GameSeed.Setup, GameSeed.FlagPick, GameSeed.CarryAi];
        ulong[] Head(string name)
        {
            RandomStream stream = TestMaps.Seed.Stream(name);
            return [stream.NextUInt64(), stream.NextUInt64(), stream.NextUInt64()];
        }

        ulong[][] before = [.. existing.Select(Head)];
        GameBoard board = Board();
        _ = RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 3, 4, 0);
        _ = RelicSpawner.Drop(board, Birth(board), TestMaps.Seed, TestMaps.P1, [Carried(1, RelicKind.WarDrum, TestMaps.P1)]);

        Assert.Equal(before, existing.Select(Head));
        Assert.DoesNotContain(GameSeed.RelicSpawn(4), existing);
    }
}
