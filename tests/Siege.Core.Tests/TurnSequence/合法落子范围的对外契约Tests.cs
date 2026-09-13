using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>规格：turn-sequence —— Requirement: 合法落子范围的对外契约</summary>
public class 合法落子范围的对外契约Tests
{
    [Fact]
    public void 范围随大回合切换()
    {
        // design.md D5：第 3 大回合为该玩家的出生区格集合，第 4 大回合为全图可落子格集合；出生区数据本身不带限制。
        // 变异验证 M-T12：`MajorRound <= 3` 改为 `< 3` → 红 1（本测试第 3 大回合断言）。
        MatchFlow match = MatchFixtures.Started(zones: [1, 1, 2, 3]).AtRound(3);
        IReadOnlySet<Coord> zone1 = match.Map.BirthZones[1];
        Assert.Equal(zone1, match.LegalRangeFor(MatchFixtures.P0));
        Assert.Equal(zone1, match.LegalRangeFor(MatchFixtures.P1));
        Assert.Equal(9, zone1.Count);

        match.AtRound(4);
        IReadOnlySet<Coord> full = match.LegalRangeFor(MatchFixtures.P0);
        Assert.Equal(81, full.Count);
        Assert.Equal(match.Map.AllCoords().Where(c => match.Map.TerrainAt(c) == Terrain.Playable).ToHashSet(), full);
        Assert.Equal(full, match.LegalRangeFor(MatchFixtures.P2));
    }
}
