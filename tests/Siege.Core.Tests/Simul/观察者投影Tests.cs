using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Core.Simul;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 观察者投影（simul-a A3-S1；D1 / D4 / D9）。</summary>
/// <remarks>变异记录（A3-S1 实跑）：M20 投影改用未修正的势力 → 红 1（修正势力与对局报告一致）；M21 已提交仍给预演 → 红 1（本人已提交时没有预演）；M22 公开盘面混入全员暂放 → 红 2（他人视角不含本人暂放、本人未提交时投影满足观察者世界的不变量）。</remarks>
public sealed class 观察者投影Tests
{
    private static ViewerWorld Build(SimulViewerPayload p) =>
        ViewerWorld.Build(p.Viewer, p.View, p.Supplement, p.OwnHand, p.OwnPreview);

    [Fact]
    public void 本人未提交时投影满足观察者世界的不变量()
    {
        SimulFlow flow = Started(2, f => f.Debug.Stones(P1, "J9"), round: 7);
        SimulPrep prep = flow.PrepFor(P0);
        prep.Stage(Unit("D4"));
        prep.Stage(Unit("F6", PieceType.Basic));
        SimulViewerPayload payload = flow.PublishFor(P0);

        Assert.Equal(TurnStage.Deploy, payload.View.Stage);
        Assert.Equal(P0, payload.View.CurrentPlayer);
        Assert.Equal(MatchPhase.InProgress, payload.View.Phase);
        Assert.Equal(7, payload.View.MajorRound);
        Assert.Equal(payload.View.BoardSerialized, payload.Supplement.BoardSerialized);
        Assert.Equal(payload.View.MajorRound, payload.Supplement.MajorRound);
        Assert.Equal(flow.Order, payload.View.ActionOrder);

        ViewerWorld world = Build(payload);
        Assert.True(world.IsViewerTurn);
        Assert.Equal(["D4", "F6"], world.Preview()!.StagedPieces.Select(s => s.Coord.ToNotation()));
        Assert.Equal(SimulRules.DeployLimit, payload.OwnPreview!.DeployLimit);
        Assert.False(payload.OwnPreview.IsPass);   // 0 枚不撤销征召：不能触发现有的 Pass 撤销警示
        Assert.True(payload.OwnCheck!.IsValid);
        Assert.Null(payload.View.Board[Coord.Parse("D4")].Occupant);   // 暂放不进公开盘面

        // 结构参数里的部署上限恒为 3（第 7 回合，现有分阶段表是 5）。
        Assert.All(payload.Supplement.Structures, s => Assert.Equal(SimulRules.DeployLimit, s.Parameters!.DeployLimit.Value));
    }

    [Fact]
    public void 本人已提交时没有预演()
    {
        SimulFlow flow = Started(2);
        flow.Submit(P0, "D4");
        SimulViewerPayload payload = flow.PublishFor(P0);

        Assert.Equal(TurnStage.Idle, payload.View.Stage);
        Assert.Null(payload.View.CurrentPlayer);
        Assert.Null(payload.OwnPreview);
        Assert.Null(payload.OwnCheck);
        ViewerWorld world = Build(payload);
        Assert.False(world.IsViewerTurn);
        Assert.Null(world.Preview());
    }

    [Fact]
    public void 他人视角不含本人暂放()
    {
        SimulFlow flow = Started(2);
        SimulViewerPayload before = flow.PublishFor(P1);

        SimulPrep mine = flow.PrepFor(P0);
        mine.Stage(Unit("D4"));
        mine.Stage(Unit("G7"));
        SimulViewerPayload other = flow.PublishFor(P1);

        Assert.Equal(P1, other.View.CurrentPlayer);
        Assert.Empty(other.OwnPreview!.Placements);
        Assert.Equal(before.View.BoardSerialized, other.View.BoardSerialized);
        Assert.Null(other.View.Board[Coord.Parse("D4")].Occupant);
        Assert.Equal(P1, other.OwnHand.Player);
        Assert.False(Build(other).Preview()!.StagedPieces.Any());

        // 本人提交后，别人看到的仍是正式盘面与"已提交"（座位状态由界面读提交标记，不读暂放）。
        Assert.True(mine.Submit().IsValid);
        Assert.Equal(before.View.BoardSerialized, flow.PublishFor(P1).View.BoardSerialized);
        Assert.True(flow.PublicView().Seats.Single(s => s.Player == P0).Submitted);
    }

    [Fact]
    public void 插旗与终局阶段也能组装观察者世界()
    {
        SimulFlow planting = SimulFlow.CreateUnvalidated(Map(2), Seed, PlayersOf(2));
        SimulViewerPayload early = planting.PublishFor(P0);
        Assert.Equal(MatchPhase.FlagPlanting, early.View.Phase);
        Assert.Null(early.View.Power);
        Assert.Empty(early.ZoneOwners);
        Assert.False(Build(early).IsViewerTurn);

        SimulFlow ended = Started(2);
        Assert.Equal([0], ended.PublishFor(P0).ZoneOwners.Keys);
        ended.Resign(P1);
        SimulViewerPayload last = ended.PublishFor(P0);
        Assert.Equal(MatchPhase.Ended, last.View.Phase);
        Assert.Null(last.OwnPreview);
        Assert.Null(Build(last).Preview());
        Assert.Equal(PlayerStatus.Resigned, last.View.Players.Single(s => s.Player == P1).Status);
        Assert.Null(Build(ended.PublishFor(P1)).Preview());   // 认输者也能看
    }

    [Fact]
    public void 修正势力与对局报告一致()
    {
        // P0 的整体围住 3×3 眼空间（现有算法 20 = 领地 13 + 军势 7，新玩法 24）；P1 四个互不相邻的单子在出生岛 1（20）。
        // 现有名次两人并列；新玩法名次 P0 第 1、P1 第 2——投影里的名次必须按修正后的总势力排。
        SimulFlow flow = Started(2, f =>
        {
            f.Debug.Stones(P0, "F3", "F4", "F5", "F6", "E6", "D6", "C6");
            f.Debug.Stones(P1, "P4", "S4", "P7", "S7");
        }, options: new SimulOptions { ScoringVersion = ScoringVersion.V1 });   // v1：7 子整体不计阵型，军势恰为 7
        SimulPowerSnapshot simul = flow.Power!;
        Assert.Equal([P0, P1], simul.Base.Ranking.Single().Players);

        PowerSnapshot projected = flow.PublishFor(P1).View.Power!;
        foreach (PlayerId p in new[] { P0, P1 })
        {
            Assert.Equal(simul.Of(p).Total, projected.Of(p).Total);
            Assert.Equal(simul.Of(p).Territory, projected.Of(p).ExclusiveCells);
            Assert.Equal(simul.Of(p).ScoredTerritory, projected.Of(p).ScoredCells);
            Assert.Equal(projected.Of(p).TerritoryScore + projected.Of(p).GroupScore, projected.Of(p).Total);
        }

        Assert.Equal(24, (int)projected.Of(P0).Total);
        Assert.Equal(20, (int)projected.Of(P1).Total);
        Assert.Equal(1, projected.RankOf(P0));
        Assert.Equal(2, projected.RankOf(P1));

        // 结算之后：报告里的势力与投影一致。
        flow.Submit(P0, "J9");
        flow.SubmitRestEmpty();
        SimulRoundReport report = flow.ResolveRound();
        PowerSnapshot after = flow.PublishFor(P0).View.Power!;
        Assert.All(flow.Players, p => Assert.Equal(report.Power.Of(p).Total, after.Of(p).Total));
    }
}
