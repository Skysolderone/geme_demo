using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// board-isolated-gen D5 / tasks 1.3：人数参数化的连带——棋盘档地图的人数上限来自生成参数，2 / 3 人棋盘图能建局、插旗与选区。
/// </summary>
/// <remarks>
/// 变异 P5（BoardMapGenerator.ToMapData 的人数上限写死 4）→ 本类红 7（全部，除 4 人那一行）：2 / 3 人图的校验预算对不上、生成作废到上限而抛出，
/// 选区与人数上限的断言随之全红。
/// </remarks>
public class 棋盘档人数参数化Tests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void 地图人数上限来自参数(int players)
    {
        string id = players == 4 ? "board:5" : $"board:5:p{players}";
        MapData map = MapCatalog.Resolve(id);

        Assert.Equal(players, map.MaxPlayers);
        Assert.Equal(players + 1, map.BirthZones.Length);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void 二三人棋盘图能建局插旗并走完几个小回合(int players)
    {
        // 跑局入口（MapCatalog → 校验 → 原型插旗 → AI 跑局）在 2 / 3 人棋盘图上走得通：每名玩家各占一个不同的出生区，确实落了子。
        RunConfig config = SimFixtures.Config(players: players, turnLimit: 3 * players) with { MapId = $"board:5:p{players}" };
        MatchSession session = MatchSession.Create(config, seed: 7);
        MatchFlow match = session.Match;

        Assert.Equal(players, match.Players.Length);
        Assert.Equal((players, players + 1), (match.Map.MaxPlayers, match.Map.BirthZones.Length));
        int[] zones = [.. match.Players.Select(p => match.StateOf(p).BirthZone ?? -1)];
        Assert.Equal(players, zones.Distinct().Count());
        Assert.All(zones, z => Assert.InRange(z, 0, players));

        MatchLog log = session.Run();
        Assert.False(log.IsFailed, log.Failure?.Message);
        Assert.NotEmpty(log.Turns.SelectMany(t => t.Placements));   // 样本口径：确实落了子
        Assert.Equal(players + 1, log.Header.ZoneCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void 选区走独立子流(int players)
    {
        // 出生区数（人数 + 1）> 地图人数上限（人数）→ PrototypeZoneAssignment 走 zone-pick 子流在空闲区里均匀抽取，而不是按编号顺排。
        // 判据：种子 1–20 里至少有一局的选区不是 0, 1, …（顺排）；且每局各区互不相同、都在 0..人数 之内。
        // 若人数上限被写死成 4（变异 P5），区数不超过上限 → 顺排 → 20 局全是 0, 1, …，本测试红。
        MapData map = MapCatalog.Resolve($"board:5:p{players}");
        PlayerId[] ids = [.. Enumerable.Range(0, players).Select(i => new PlayerId(i))];
        int[] sequential = [.. Enumerable.Range(0, players)];
        int shuffled = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            int[] zones = [.. PrototypeZoneAssignment.Assign(map, new GameSeed(seed), ids, flagRisk: 0).Select(c => c.Zone)];
            Assert.Equal(players, zones.Distinct().Count());
            Assert.All(zones, z => Assert.InRange(z, 0, players));
            shuffled += zones.SequenceEqual(sequential) ? 0 : 1;
        }

        Assert.True(shuffled > 0, "种子 1–20 的选区全是按编号顺排：没有走独立子流。");
    }

    [Fact]
    public void 人数超过地图上限报错()
    {
        // 对局建立时人数大于地图支持人数照旧报错（MatchFlow 读地图的人数上限）。
        MapData map = MapCatalog.Resolve("board:5:p2");
        PlayerId[] three = [new(0), new(1), new(2)];

        ArgumentException ex = Assert.Throws<ArgumentException>(() => MatchFlow.Create(map, new GameSeed(1), three));
        Assert.Contains("最多支持 2 人", ex.Message, StringComparison.Ordinal);
    }
}
