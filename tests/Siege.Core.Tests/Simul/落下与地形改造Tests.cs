using Siege.Core.Board;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 落下与地形改造（v0.1 §8.4；K2）。算例 E11 取自 design.md。</summary>
/// <remarks>变异记录（A1 实跑）：M10 冲突落败的改造不作废 → 红 1（冲突落败的改造作废）。</remarks>
public sealed class 落下与地形改造Tests
{
    private static readonly TerrainEdit BurnG5 = TerrainEdit.Burn(Coord.Parse("G5"));

    [Fact]
    public void 冲突落败的改造作废()
    {
        // E11：P0 的匠人带"烧 G5"投 F5（战力 1）；P1 在 F5 四邻有 F4、E5（普通单位 1 + 2 = 3）。P1 胜，P0 的改造作废，G5 仍是林地。
        SimulFlow flow = Started(2, f => f.Debug.Stones(P1, "F4", "E5"), order: [P0, P1], terrain: Forest("G5"), extra: [(PieceType.Artisan, 1)]);
        flow.Submit(P0, Unit("F5", PieceType.Artisan, BurnG5));
        flow.Submit(P1, "F5");
        SimulRoundReport report = flow.ResolveRound();

        Assert.Equal(P1, Assert.Single(report.Conflicts).Winner);
        SimulEditOutcome edit = Assert.Single(report.Edits);
        Assert.Equal((P0, SimulEditVoidReason.ConflictLost), (edit.Player, edit.VoidReason!.Value));
        Assert.False(edit.Applied);
        Assert.Equal(Surface.Forest, flow.Map.SurfaceAt(Coord.Parse("G5")));
        Assert.Empty(flow.Board.TerrainEdits);
        Assert.Equal(0, flow.Hands.PrivateViewOf(P0).CountOf(PieceType.Artisan));   // 匠人照样扣掉
    }

    [Fact]
    public void 冲突胜出的改造生效()
    {
        SimulFlow flow = Started(2, f => f.Debug.Stones(P0, "F4", "E5"), order: [P0, P1], terrain: Forest("G5"), extra: [(PieceType.Artisan, 1)]);
        flow.Submit(P0, Unit("F5", PieceType.Artisan, BurnG5));
        flow.Submit(P1, "F5");
        SimulRoundReport report = flow.ResolveRound();

        Assert.Equal(P0, Assert.Single(report.Conflicts).Winner);
        Assert.True(Assert.Single(report.Edits).Applied);
        Assert.Equal(Surface.Grass, flow.Map.SurfaceAt(Coord.Parse("G5")));
        Assert.Equal(PieceType.Artisan, flow.Board[Coord.Parse("F5")].Occupant!.Value.Type);
    }

    [Fact]
    public void 同目标改造顺位靠前者生效()
    {
        // 顺位 P1、P0：P0 的匠人 F5、P1 的匠人 H5 都烧 G5，都落下；P1 的改造生效，P0 的作废。
        SimulFlow flow = Started(2, order: [P1, P0], terrain: Forest("G5"), extra: [(PieceType.Artisan, 1)]);
        flow.Submit(P0, Unit("F5", PieceType.Artisan, BurnG5));
        flow.Submit(P1, Unit("H5", PieceType.Artisan, BurnG5));
        SimulRoundReport report = flow.ResolveRound();

        Assert.Empty(report.Conflicts);
        Assert.Equal(2, report.Landed.Length);
        Assert.Equal([(P1, (SimulEditVoidReason?)null), (P0, SimulEditVoidReason.DuplicateTarget)],
            report.Edits.Select(e => (e.Player, e.VoidReason)));
        Assert.Equal(Surface.Grass, flow.Map.SurfaceAt(Coord.Parse("G5")));
        Assert.Equal([BurnG5], flow.Board.TerrainEdits);
        Assert.Equal(P0, flow.OwnerAt("F5"));
        Assert.Equal(P1, flow.OwnerAt("H5"));
    }
}
