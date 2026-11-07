using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.PieceRelics;
using static Siege.Core.Tests.PieceRelics.PieceRelicFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// 规格：openspec/specs/relic-spawn —— Requirement: 大图只落信物遗址（relic-sites 裁决 8）：大图每批只落在空着的遗址格、档位随遗址（稀有遗址只出稀有件、普通只出普通件）、每批稀有至多 1 处，
/// 被占 / 已有信物的遗址不在候选；rare-weight 裁决 1：稀有遗址按权重 3（普通 1）抽；掉落同样只落空遗址、没有空遗址即消失；小图路径逐位不变（刷新与掉落序列按改动前的实际值钉住）。
/// </summary>
public class 信物遗址刷新Tests
{
    private static GameBoard Nine() => GameBoard.LoadUnvalidated(TestMaps.NineByNine())
        .Place("E5", TestMaps.P0).Place("E6", TestMaps.P0).Place("D4", TestMaps.P1);

    private static GameBoard Large(ulong mapSeed = 7) => GameBoard.Load(MapCatalog.Resolve(LinkMapId.Format(mapSeed, 4, MapTier.Large)));

    private static IBirthArea Birth(GameBoard board) => new BirthBoardArea(board.Map);

    private static string Text(RelicForecast forecast) => string.Join(";", forecast.Slots.Select(s => $"{s.Cell.ToNotation()}:{s.Kind}"));

    [Fact]
    public void 小图刷新序列与改前相同()
    {
        // 黄金值：relic-sites 改动前（main 497f7383）的 Release 测试实跑取得——9×9 夹具（TestMaps.Seed）第 3 / 5 / 7 大回合三批、link:1:4 空盘（对局种子 7）三批、link:1:4 上 P1 掉落两件。
        GameBoard nine = Nine();
        Assert.Equal(
            "H5:WarDrum;F9:Watchtower|E1:Chameleon;G4:Watchtower|F3:Command;D9:Banner",
            string.Join("|", new[] { 3, 5, 7 }.Select(r => Text(RelicSpawner.Forecast(nine, Birth(nine), TestMaps.Seed, r, 4, 0)!))));

        GameBoard small = GameBoard.Load(MapCatalog.Resolve("link:1:4"));
        Assert.Equal(MapTier.Small, small.Map.Tier);
        Assert.Equal(
            "Z10:Watchtower;AM5:Command|P16:WarDrum;AQ21:Banner|AS28:Watchtower;AH17:Command",
            string.Join("|", new[] { 3, 5, 7 }.Select(r => Text(RelicSpawner.Forecast(small, Birth(small), new GameSeed(7), r, 4, 0)!))));
        Assert.Equal(
            "M18#1;J46#3",
            string.Join(";", RelicSpawner.Drop(small, Birth(small), new GameSeed(7), TestMaps.P1, [Carried(3, RelicKind.Command, TestMaps.P1), Carried(1, RelicKind.WarDrum, TestMaps.P1)]).Select(r => $"{r.Cell.ToNotation()}#{r.Token.Id}")));
    }

    [Fact]
    public void 大图只落遗址_档位对应_每批稀有至多一处()
    {
        // 地图种子 1–20 × 对局种子 1–3 × 第 3 / 5 / 7 大回合：每件都在遗址上、种类档位 = 遗址档位、每批稀有 ≤ 1、不重复；4 人每批 2 件（空遗址 8 处充足）。
        int batches = 0, rareBatches = 0;
        for (ulong mapSeed = 1; mapSeed <= 20; mapSeed++)
        {
            GameBoard board = Large(mapSeed);
            Assert.Equal(MapTier.Large, board.Map.Tier);
            var tiers = board.Map.RelicSites.ToDictionary(s => s.Cell, s => s.Tier);
            for (ulong gameSeed = 1; gameSeed <= 3; gameSeed++)
            {
                foreach (int round in new[] { 3, 5, 7 })
                {
                    RelicForecast forecast = RelicSpawner.Forecast(board, Birth(board), new GameSeed(gameSeed), round, 4, 0)!;
                    Assert.Equal(round + 1, forecast.LandingRound);
                    Assert.Equal(2, forecast.Slots.Length);
                    Assert.Equal(2, forecast.Slots.Select(s => s.Cell).Distinct().Count());
                    foreach (RelicSpawnSlot slot in forecast.Slots)
                    {
                        Assert.True(tiers.TryGetValue(slot.Cell, out RelicTier tier), $"{board.Map.Id} 种子 {gameSeed} 第 {round} 大回合：{slot.Cell.ToNotation()} 不是遗址。");
                        Assert.Equal(tier, RelicKinds.TierOf(slot.Kind));
                    }

                    int rare = forecast.Slots.Count(s => RelicKinds.TierOf(s.Kind) == RelicTier.Rare);
                    Assert.True(rare <= 1, $"{board.Map.Id} 种子 {gameSeed} 第 {round} 大回合一批出了 {rare} 件稀有。");
                    batches++;
                    rareBatches += rare;
                }
            }
        }

        Assert.Equal(20 * 3 * 3, batches);
        Assert.True(rareBatches > 0, "180 批里没有任何稀有件：稀有遗址从未被抽中");
        Assert.True(rareBatches < batches, "每批都有稀有件：普通遗址从未被抽中");   // 样本口径
        // rare-weight 裁决 1 / 2：稀有遗址权重 3，4 人局遗址全空时每批出稀有的期望约 78%（等权时 46%）。样本带宽取 65%–92%：等权实现落不进来。
        Assert.InRange(rareBatches * 100 / batches, 65, 92);
    }

    [Fact]
    public void 稀有遗址按权重3被抽中()
    {
        // rare-weight 裁决 1：候选只剩 1 处稀有 + 1 处普通、每批 1 件（2 人）时，稀有被抽中的期望 = 3 / (3 + 1) = 75%（等权为 50%）；400 个对局种子取样带宽 67%–83%。
        Assert.Equal(3, RelicKinds.UncalibratedRareSiteWeight);
        GameBoard board = Large();
        RelicSite rare = board.Map.RelicSites.First(s => s.Tier == RelicTier.Rare);
        RelicSite common = board.Map.RelicSites.First(s => s.Tier == RelicTier.Common);
        foreach (RelicSite site in board.Map.RelicSites.Where(s => s != rare && s != common))
        {
            board.Place(site.Cell.ToNotation(), TestMaps.P0);
        }

        Assert.Equal(2, RelicSpawner.SiteCandidates(board, Birth(board)).Length);
        int rareHits = 0;
        for (ulong seed = 1; seed <= 400; seed++)
        {
            RelicSpawnSlot slot = Assert.Single(RelicSpawner.Forecast(board, Birth(board), new GameSeed(seed), 3, 2, 0)!.Slots);
            Assert.True(slot.Cell == rare.Cell || slot.Cell == common.Cell);
            rareHits += slot.Cell == rare.Cell ? 1 : 0;
        }

        Assert.InRange(rareHits * 100 / 400, 67, 83);
    }

    [Fact]
    public void 被占与已有信物的遗址不在候选_同种子可复现()
    {
        GameBoard board = Large();
        ImmutableArray<RelicSite> sites = board.Map.RelicSites;
        Assert.Equal(sites.OrderBy(s => s.Cell), RelicSpawner.SiteCandidates(board, Birth(board)));   // 空盘：全部遗址都是候选，按坐标字典序

        // 一处稀有被棋子占、一处普通已有信物 → 两处都不在候选；其余仍在（large-60：共 4 处，剩 2 处）。
        RelicSite occupied = sites.First(s => s.Tier == RelicTier.Rare);
        RelicSite taken = sites.First(s => s.Tier == RelicTier.Common);
        board.Place(occupied.Cell.ToNotation(), TestMaps.P0).Attach(taken.Cell.ToNotation(), Map(90, RelicKind.WarDrum));
        ImmutableArray<RelicSite> candidates = RelicSpawner.SiteCandidates(board, Birth(board));
        Assert.Equal(sites.Length - 2, candidates.Length);
        Assert.DoesNotContain(occupied, candidates);
        Assert.DoesNotContain(taken, candidates);

        // 同种子两次相同；换种子有不同（反面：确实由种子驱动）。
        RelicForecast a = RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 3, 4, 0)!;
        Assert.Equal(a, RelicSpawner.Forecast(board, Birth(board), TestMaps.Seed, 3, 4, 0));
        Assert.All(a.Slots, s => Assert.Contains(s.Cell, candidates.Select(c => c.Cell)));
        Assert.True(Enumerable.Range(1, 30).Any(s => !a.Equals(RelicSpawner.Forecast(board, Birth(board), new GameSeed((ulong)s), 3, 4, 0))), "30 个种子的第 3 大回合预告全相同");

        // 全部遗址都被占 → 本批为空（候选不足即少落，与小图"没有候选格时本批为空"同一口径）。
        GameBoard full = Large();
        foreach (RelicSite site in sites)
        {
            full.Place(site.Cell.ToNotation(), TestMaps.P1);
        }

        Assert.Empty(RelicSpawner.SiteCandidates(full, Birth(full)));
        Assert.Empty(RelicSpawner.Forecast(full, Birth(full), TestMaps.Seed, 3, 4, 0)!.Slots);
        Assert.Equal(4, RelicSpawner.Forecast(full, Birth(full), TestMaps.Seed, 3, 4, 0)!.LandingRound);
    }

    [Fact]
    public void 大图掉落只落遗址_没有空遗址即消失()
    {
        GameBoard board = Large();
        var cells = board.Map.RelicSites.Select(s => s.Cell).ToHashSet();
        RelicToken[] items = [Carried(1, RelicKind.WarDrum, TestMaps.P1), Carried(2, RelicKind.Banner, TestMaps.P1), Carried(3, RelicKind.Command, TestMaps.P1)];

        ImmutableArray<BoardRelic> dropped = RelicSpawner.Drop(board, Birth(board), TestMaps.Seed, TestMaps.P1, items);
        Assert.Equal(3, dropped.Length);
        Assert.Equal([1, 2, 3], dropped.Select(r => r.Token.Id));   // 按编号升序逐件
        Assert.Equal(3, dropped.Select(r => r.Cell).Distinct().Count());
        Assert.All(dropped, r => Assert.Contains(r.Cell, cells));   // 掉落不分档位：军旗可以掉在普通遗址上，只要求是空遗址
        Assert.Equal(dropped, RelicSpawner.Drop(board, Birth(board), TestMaps.Seed, TestMaps.P1, items));

        // 只剩 1 处空遗址 → 只掉 1 件，其余消失；全占 → 全部消失。
        foreach (RelicSite site in board.Map.RelicSites.Skip(1))
        {
            board.Place(site.Cell.ToNotation(), TestMaps.P0);
        }

        ImmutableArray<BoardRelic> one = RelicSpawner.Drop(board, Birth(board), TestMaps.Seed, TestMaps.P1, items);
        Assert.Single(one);
        Assert.Equal(board.Map.RelicSites[0].Cell, one[0].Cell);
        Assert.Equal(1, one[0].Token.Id);
        board.Place(board.Map.RelicSites[0].Cell.ToNotation(), TestMaps.P0);
        Assert.Empty(RelicSpawner.Drop(board, Birth(board), TestMaps.Seed, TestMaps.P1, items));
    }
}
