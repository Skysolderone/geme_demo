using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 开局信息公开范围</summary>
public class 开局信息公开范围Tests
{
    [Fact]
    public void 插旗阶段的可见信息()
    {
        // 设计文档 §4.1：地形、出生区、信物格位置公开；信物具体内容隐藏。
        // 变异验证 M-S1：RelicState.ToPublic 无条件带出 Content → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Create(relics: [("E5", RelicFixtures.Command()), ("B2", RelicFixtures.Vanguard())]);
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);

        MapData map = match.Flags.Map;
        Assert.Equal(4, map.BirthZones.Length);
        Assert.Equal(new[] { "B2", "E5" }, map.RelicCells.Keys.Order().Notations());
        Assert.Equal(Terrain.Playable, map.TerrainAt(TestMaps.At("E5")));

        foreach (RelicPublicState state in match.Relics.PublicStates())
        {
            Assert.False(state.IsRevealed);
            Assert.Null(state.Content);
        }

        // 公开快照里没有任何"信物内容"字段以外的路径：RelicPublicState 是唯一出口，未揭示时 Content 为 null。
        Assert.Equal(2, match.Publish().Relics.Length);
        Assert.All(match.Publish().Relics, r => Assert.Null(r.Content));
    }
}
