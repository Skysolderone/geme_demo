using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>
/// openspec/specs/simultaneous-round —— Requirement: 活形锁定与势力（v0.1 §5.2；Q06；design J2-8 的核对）。
/// </summary>
/// <remarks>变异记录（A1 实跑）：M7 去掉"并上本人活形内部格" → 红 1（活形内部空格算所有者领地）；M15 不扣别人的活形内部格 → 红 1（别人的活形内部格不算自己领地）。</remarks>
public sealed class 活形锁定与势力Tests
{
    /// <summary>P0 的一条整体 F3-F4-F5-F6-E6-D6-C6 在出生岛 0 左下角围住 3×3 的眼空间 C3–E5（眼值 2，已确定活形）。</summary>
    private static readonly string[] Ring = ["F3", "F4", "F5", "F6", "E6", "D6", "C6"];

    [Fact]
    public void 活形内部空格算所有者领地()
    {
        SimulFlow flow = Started(2, f => f.Debug.Stones(P0, Ring));
        LifeShapeReport life = LifeShapeReport.Analyze(flow.Board);
        Assert.Equal(LifeState.Alive, life.GroupLifeAt(Coord.Parse("F3"))!.Life);

        SimulPlayerPower p0 = flow.Power!.Of(P0);
        string[] interior = ["C3", "D3", "E3", "C4", "D4", "E4", "C5", "D5", "E5"];
        string[] uncovered = ["C3", "D3", "C4", "D4"];

        // 核对（J2-8）：现有独占空格不含离 P0 单位 ≥ 2 格的 4 个内部格——它们无人覆盖、是中立格。
        foreach (string c in uncovered)
        {
            Assert.DoesNotContain(Coord.Parse(c), p0.Base.ExclusiveCells);
            Assert.Equal(OwnershipKind.Neutral, flow.Power.Base.Coverage.OwnershipOf(Coord.Parse(c)).Kind);
        }

        // 新玩法：9 个内部格全是 P0 的领地，势力比现有算法多 4。
        Assert.Equal(interior.Order(StringComparer.Ordinal), p0.LockedCells.Select(c => c.ToNotation()).Order(StringComparer.Ordinal));
        Assert.All(interior, c => Assert.Contains(Coord.Parse(c), p0.Territory));
        Assert.Equal(p0.Base.ExclusiveCells.Length + 4, p0.Territory.Length);
        Assert.Equal(p0.Base.Total + 4, p0.Total);
        Assert.Equal(new BigInteger(4), p0.Total - p0.Base.Total);
        Assert.Equal(p0.Total, flow.Power.Of(P0).ScoredTerritory.Length + p0.GroupScore);
    }

    [Fact]
    public void 别人的活形内部格不算自己领地()
    {
        // 崖壁：C3–F3 高 2，其余高 0。P1 的 C3-D3-E3 站在崖上（气 F3），向下覆盖 C4 / D4 / E4；崖壁没有眼边，是 P0 眼空间的墙。
        // P0 的整体 F4-F5-F6-F7-E7-D7-C7 围住 3×3 眼空间 C4–E6（眼值 2，已确定活形）。
        // 现有算法：C4、D4 只被 P1 覆盖 → P1 的独占空格；E4 被双方覆盖 → 争议；P0 只独占 E5、E6、D6、C6。
        // 新玩法：9 格全归 P0（+5），P1 失去 C4、D4（−2）。
        var heights = new[] { "C3", "D3", "E3", "F3" }.ToImmutableDictionary(Coord.Parse, _ => 2);
        var terrain = new TerrainData(heights, ImmutableDictionary<Coord, Surface>.Empty, [], []);
        SimulFlow flow = Started(2, f =>
        {
            f.Debug.Stones(P0, "F4", "F5", "F6", "F7", "E7", "D7", "C7");
            f.Debug.Stones(P1, "C3", "D3", "E3");
        }, terrain: terrain);
        LifeShapeReport life = LifeShapeReport.Analyze(flow.Board);
        Assert.Equal(LifeState.Alive, life.GroupLifeAt(Coord.Parse("F4"))!.Life);
        Assert.Equal(P0, life.EyeSpaceAt(Coord.Parse("C4"))!.Owner);

        SimulPlayerPower p0 = flow.Power!.Of(P0);
        SimulPlayerPower p1 = flow.Power.Of(P1);
        Assert.Equal(["C4", "D4"], p1.Base.ExclusiveCells.Select(c => c.ToNotation()).Where(c => c is "C4" or "D4"));
        Assert.DoesNotContain(Coord.Parse("C4"), p1.Territory);
        Assert.DoesNotContain(Coord.Parse("D4"), p1.Territory);
        Assert.Contains(Coord.Parse("C4"), p0.Territory);
        Assert.Contains(Coord.Parse("D4"), p0.Territory);
        Assert.Equal(new BigInteger(5), p0.Total - p0.Base.Total);
        Assert.Equal(new BigInteger(2), p1.Base.Total - p1.Total);
    }

    [Fact]
    public void 没有活形时与现有势力相同()
    {
        SimulFlow flow = Started(2, f =>
        {
            f.Debug.Stones(P0, "D4", "E4", "H8");
            f.Debug.Stones(P1, "F4", "F5");
        });
        Assert.Empty(LifeShapeReport.Analyze(flow.Board).Groups.Where(g => g.Life == LifeState.Alive));
        foreach (PlayerId p in new[] { P0, P1 })
        {
            SimulPlayerPower power = flow.Power!.Of(p);
            Assert.Equal(power.Base.Total, power.Total);
            Assert.Equal(power.Base.ExclusiveCells, power.Territory);
            Assert.Empty(power.LockedCells);
        }
    }
}
