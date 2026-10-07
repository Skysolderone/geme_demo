using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：map-definition —— Requirement: 人数适配预算。
/// retire-legacy-maps 段 C：标准档（2 / 3 / 4 人 50–65 / 75–90 / 95–110）与边疆档（4 人 300–420）的预算随两档删除，
/// 本类改在棋盘档合规图（<see cref="BoardMapFixtures"/>）上验棋盘档的人数预算：可落子格上界、信物格下界与不支持的人数。
/// 可落子格下界、信物格上界、出生区数 = 人数 + 1 与单区 25–49 见 <c>棋盘档预算与校验Tests</c>。
/// 删除的用例：出生区数量必须等于最大人数（BIRTH_ZONE_COUNT_MISMATCH 只服务标准档）、两人 / 三人预算区间（标准档数值）、
/// 边疆档按自己的区间校验、边疆档出生区数不得等于人数、边疆档出生区数区间端点、边疆档平台过小、边疆档单区下 / 上界端点、
/// 边疆档可落子下 / 上界端点、边疆档信物数区间端点、边疆档两人三人报不支持。
/// </summary>
public class 人数适配预算Tests
{
    [Theory]
    [InlineData(4, 250, 1000)]
    [InlineData(3, 190, 750)]
    [InlineData(2, 125, 500)]
    public void 格数超出预算(int players, int min, int max)
    {
        // 规格 Scenario：可落子格多于该人数预算上限 → 拒绝并报告超出区间（棋盘档 4 人 250–1000、3 人 190–750、2 人 125–500，区间取自规格、测试内独立写出）。
        // 格局：人数 + 1 块 5×5 出生棋盘 + 两块 9×9 公共棋盘（合规），外接放宽到 60×25，再把坐标序前若干个场景格改成可落子，凑到恰为上限 / 上限 + 1。
        // 场景格可落子会另报 SCENERY_CELL_PLAYABLE，这里只看可落子格那一条：上限本身不报、上限 + 1 报出。
        // 变异（段 C 实跑）：MC-S1 2 人上限 500 → 600、MC-S1b 4 人上限 1000 → 1001 → 本测试对应行各红 1。
        ImmutableArray<BoardPlate> boards = [.. BoardMapFixtures.FourBirths.Take(players + 1), BoardMapFixtures.FourPublicA, BoardMapFixtures.FourPublicB];
        ImmutableHashSet<Coord> boardCells = [.. boards.SelectMany(b => b.Cells())];
        Coord[] scenery = [.. TestMaps.Rect(0, 0, 60, 25).Where(c => !boardCells.Contains(c))];
        MapData With(int playable) =>
            BoardMapFixtures.Build($"test-board-over-{players}", players, 60, 25, boards, [.. scenery.Take(playable - boardCells.Count)]);

        MapData atMax = With(max);
        MapData over = With(max + 1);
        Assert.Equal((max, max + 1), (atMax.PlayableCount, over.PlayableCount));

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(over).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
        Assert.Contains($"{players} 人地图的可落子格为 {max + 1}", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"{min}–{max}", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MapValidator.Validate(atMax).Failures, f => f.Code == "PLAYABLE_COUNT_OUT_OF_RANGE");
    }

    [Fact]
    public void 信物格数不足()
    {
        // 规格 Scenario「信物格数不足」：信物格少于该人数下限 → 拒绝并报出方向与区间。
        // retire-legacy-maps 段 C：原是 10×10 标准档 4 人合成图（13–15）；改为 4 人合规棋盘档图只留 5 个出生棋盘信物（棋盘档 4 人 7–25）。
        // 变异 MC-S5（段 C 实跑）：4 人信物下限 7 → 6 → 本测试红（连同 棋盘档预算与校验 两条、地图文件健壮性 一条共红 4）。
        MapData map = BoardMapFixtures.FourPlayerMap();
        MapData stripped = map with { RelicCells = map.RelicCells.Where(kv => kv.Value.Zone == RelicZone.BirthZone).ToImmutableDictionary() };
        Assert.Equal(5, stripped.RelicCells.Count);

        MapValidationResult result = MapValidator.Validate(stripped);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("RELIC_COUNT_OUT_OF_RANGE", failure.Code);
        Assert.Contains("信物格为 5", failure.Message, StringComparison.Ordinal);
        Assert.Contains("7–25", failure.Message, StringComparison.Ordinal);
        Assert.Contains("少于下限 7", failure.Message, StringComparison.Ordinal);
        Assert.True(MapValidator.Validate(map).IsValid);   // 反面：原图（9 个）通过
    }

    [Fact]
    public void 不支持的人数被拒绝()
    {
        MapData map = MapCatalog.Resolve(MapCatalog.DefaultId) with { MaxPlayers = 5 };   // retire-legacy-maps 段 B：原底图 v5

        MapValidationFailure failure = Assert.Single(MapValidator.Validate(map).Failures, f => f.Code == "UNSUPPORTED_PLAYER_COUNT");
        Assert.Contains("不支持的人数 5", failure.Message, StringComparison.Ordinal);
        Assert.Contains("2 / 3 / 4", failure.Message, StringComparison.Ordinal);
    }
}
