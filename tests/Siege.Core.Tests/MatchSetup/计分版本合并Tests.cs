using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchSetup;

public class 计分版本合并Tests
{
    [Theory]
    [InlineData(2, 0, 2)]
    [InlineData(3, 1, 4)]
    [InlineData(4, 1, 6)]
    [InlineData(5, 2, 11)]
    [InlineData(7, 2, 15)]
    [InlineData(8, 3, 27)]
    [InlineData(11, 3, 37)]
    [InlineData(12, 4, 60)]
    public void 阵型门槛影响整串倍率(int count, int tier, int expected)
    {
        GameBoard board = TestMaps.Blank(size: 16);
        for (int i = 0; i < count; i++)
        {
            board.Place(new Coord(i + 1, 2), TestMaps.P0, PieceType.Basic);
        }

        var roster = TestMaps.All.ToDictionary(p => p, _ => PlayerStatus.Active);
        var known = ImmutableDictionary<Coord, RelicType>.Empty;
        GroupPower old = Assert.Single(PowerCalculator.Compute(board, roster, known, ScoringVersion.V1).Of(TestMaps.P0).Groups);
        GroupPower current = Assert.Single(PowerCalculator.Compute(board, roster, known, ScoringVersion.V2).Of(TestMaps.P0).Groups);

        Assert.Equal(0, old.FormationTier);
        Assert.Equal(count, old.Power);
        Assert.Equal(tier, current.FormationTier);
        Assert.Equal(tier, current.MultiplierExponent);
        Assert.Equal(expected, current.Power);
    }

    [Fact]
    public void 新局默认V2而旧存档回填V1()
    {
        var map = MapCatalog.Resolve("link:1");
        MatchFlow match = MatchFlow.Create(map, new GameSeed(5), TestMaps.All);
        Assert.Equal(ScoringVersion.V2, match.ScoringVersion);
        Assert.Equal(ScoringVersion.V2, match.Publish().ScoringVersion);

        string saved = match.Serialize();
        Assert.Equal("V2", JsonNode.Parse(saved)!["ScoringVersion"]!.GetValue<string>());
        MatchFlow roundtrip = MatchFlow.Restore(map, saved);
        Assert.Equal(ScoringVersion.V2, roundtrip.ScoringVersion);
        Assert.False(roundtrip.ScoringVersionBackfilled);

        JsonObject legacy = JsonNode.Parse(saved)!.AsObject();
        legacy.Remove("ScoringVersion");
        MatchFlow restored = MatchFlow.Restore(map, legacy.ToJsonString());
        Assert.Equal(ScoringVersion.V1, restored.ScoringVersion);
        Assert.True(restored.ScoringVersionBackfilled);
        Assert.Equal(ScoringVersion.V1, restored.Publish().ScoringVersion);
    }

    [Fact]
    public void 新跑局记录V2且旧日志缺字段回放V1()
    {
        RunConfig input = SimFixtures.Config(turnLimit: 1) with { ScoringVersion = null };
        RunConfig resolved = input.ResolvedFor(MapCatalog.Resolve(input.MapId));
        Assert.Equal(ScoringVersion.V2, resolved.ScoringVersion);
        Assert.Equal(ScoringVersion.V2, MatchSession.Create(input, 1).Match.ScoringVersion);
        Assert.Equal(ScoringVersion.V1, MatchSession.Create(input, 1, MapCatalog.Resolve(input.MapId), recorded: true).Match.ScoringVersion);

        MatchLog log = MatchSession.Create(input, 2).Run();
        Assert.Equal(ScoringVersion.V2, log.ScoringVersion);
        Assert.Contains("\"ScoringVersion\":\"V2\"", log.FullText(), StringComparison.Ordinal);
    }

    [Fact]
    public void 并行度与行尾不影响跨机器配置摘要()
    {
        RunConfig config = SimFixtures.Config() with { Parallelism = 0 };
        string json = config.ToJson();
        Assert.DoesNotContain("EffectiveParallelism", json, StringComparison.Ordinal);
        string lf = json.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(SimFixtures.Sha256Lf(lf), SimFixtures.Sha256Lf(lf.Replace("\n", "\r\n", StringComparison.Ordinal)));

        JsonObject old = JsonNode.Parse(json)!.AsObject();
        old["EffectiveParallelism"] = 28;
        RunConfig recorded = JsonSerializer.Deserialize<RunConfig>(old.ToJsonString(), RunConfig.JsonOptions)!;
        Assert.Equal(28, recorded.RecordedParallelism);
        Assert.DoesNotContain("EffectiveParallelism", RunConfig.FromJson(old.ToJsonString()).ToJson(), StringComparison.Ordinal);
    }
}
