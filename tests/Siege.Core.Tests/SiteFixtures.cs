using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests;

/// <summary>scoring-sites 测试夹具：带据点的 9×9 合成对局（地图同 <see cref="MatchFixtures.Map"/>，只多据点）。</summary>
internal static class SiteFixtures
{
    /// <summary>
    /// 与 <see cref="MatchFixtures.Started"/> 相同（四人各占角落出生区、各 50 枚普通子、进入第 1 大回合），地图额外带 <paramref name="sites"/>。
    /// <paramref name="options"/> 缺省为 <see cref="MatchOptions.Immediate"/> 且关闭势力碾压（避免计分用例意外以碾压终局）。
    /// </summary>
    internal static MatchFlow Started(MatchOptions? options, params (string Cell, SiteTier Tier)[] sites)
    {
        MapData map = MatchFixtures.Map() with { Sites = sites.ToImmutableDictionary(s => Coord.Parse(s.Cell), s => s.Tier) };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), options ?? MatchFixtures.DominanceOff);
        foreach (PlayerId p in MatchFixtures.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        return match;
    }

    internal static SiteState StateAt(this PowerSnapshot snapshot, string cell) =>
        snapshot.SiteStates.Single(s => s.Coord == Coord.Parse(cell));
}
