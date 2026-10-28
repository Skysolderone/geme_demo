using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.InformationVisibility;

/// <summary>规格：information-visibility —— Requirement: 始终公开的信息</summary>
public class 始终公开的信息Tests
{
    [Fact]
    public void 棋子类型公开()
    {
        // 设计文档 §13.1：A 落下倍增子 → 所有玩家的默认棋盘都显示该格是 A 的倍增子。
        // 变异验证 M-V1：DefaultBoardView.From 的 Occupant 改为 `cell.Occupant is { } o ? o with { Type = PieceType.Basic } : null` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        match.Debug.SeedHand(P0, (PieceType.Multiplier, 1), (PieceType.Basic, 5));
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("G7"), PieceType.Multiplier));
        Assert.True(match.Confirm().Confirmed);

        foreach (PlayerId viewer in TestMaps.All)
        {
            BoardCellView cell = match.World(viewer).Board().CellAt(TestMaps.At("G7"));
            Assert.Equal(new Occupant(P0, PieceType.Multiplier), cell.Occupant);
        }
    }

    [Fact]
    public void 势力明细公开()
    {
        // 设计文档 §13.1 / §10.1 算例：A（P0）的棋串普通子×3 + 堡垒子×1 + 倍增子×2 → 基础 9、倍率 2.25、军势 20；
        // B（P1）打开势力层可见 A 的领地分与该棋串的基础军势、位置加值、倍率与最终军势，且与 Core 势力明细逐项一致
        // （information-visibility「领地分公开」）。
        MatchFlow match = AiFixtures.Round5()
            .Pieces(P0, PieceType.Basic, "D4", "E4", "F4")
            .Pieces(P0, PieceType.Fortress, "G4")
            .Pieces(P0, PieceType.Multiplier, "H4", "J4");

        var layer = (PowerLayerContent)match.World(P1).Layer(TacticalLayer.Power);

        GroupScoreView group = Assert.Single(layer.Groups, g => g.Owner == P0);
        Assert.Equal((9, 0, "2.25", 20L), (group.Power.BaseTotal, group.Power.PositionBonus, group.Power.MultiplierText, group.Power.Power));
        Scoring.PlayerPower truth = match.Scoreboard.Latest!.Of(P0);
        PlayerPowerRowView row = Assert.Single(layer.Players, p => p.Player == P0);
        Assert.Equal((truth.Total, truth.TerritoryScore), (row.Total, row.TerritoryScore));
    }

    [Fact]
    public void 领地分公开()
    {
        // 规格 Scenario（information-visibility「始终公开的信息」第 5 条）：任意玩家查询对手的势力明细 → 可读到对手的领地分总计与独占空格坐标集合。
        // P0 占 D7–H7 一排 5 子 → 独占 12 格（第 6 / 8 行 D–H 各 5 格 + C7、J7；retire-legacy-rules：平坦 8×8 链接载体，原 9×9 的 C5–G5 列 +1、行 +2）。三名对手各自从自己的公开世界读取，逐格与权威明细一致；
        // 势力层的独占格着色与领地分也对每名对手给出同一份（不是只对本人可见）。
        // 变异验证 M-E15：势力层的独占格集合漏掉 P0（只投影其余玩家）→ 实跑红 2（本测试、势力层显示领地分）。
        // 公开视图（MatchPublicView.Power）对所有观察者是同一个对象，不存在按观察者裁剪的路径，所以变异落在表现层投影上。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5).Stones(P0, "D7", "E7", "F7", "G7", "H7");
        Scoring.PlayerPower truth = match.Scoreboard.Latest!.Of(P0);
        string[] expected = ["C7", "D6", "D8", "E6", "E8", "F6", "F8", "G6", "G8", "H6", "H8", "J7"];
        Assert.Equal(expected, truth.ExclusiveCells.Select(c => c.ToNotation()).Order(StringComparer.Ordinal));

        foreach (PlayerId viewer in new[] { P1, TestMaps.P2, TestMaps.P3 })
        {
            ViewerWorld world = match.World(viewer);
            Scoring.PlayerPower seen = world.Public.View.Power!.Of(P0);
            Assert.Equal(12, seen.TerritoryScore);
            Assert.Equal(expected, seen.ExclusiveCells.Select(c => c.ToNotation()).Order(StringComparer.Ordinal));

            var layer = (PowerLayerContent)world.Layer(TacticalLayer.Power);
            Assert.Equal(12, layer.Players.Single(r => r.Player == P0).TerritoryScore);
            Assert.Equal(expected, layer.Territory.Where(c => c.Owner == P0).Select(c => c.Coord.ToNotation()).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void 气可推导()
    {
        // 设计文档 §13.1：任意棋串的气数与气位可从公开信息推导。拐角串 F6-F7-G7（testing.md：共享气 G6 被 F6 与 G7 同时邻接；retire-legacy-rules：原 9×9 的 D4-D5-E5 平移 (+2, +2)），
        // 手工推导气位 F5、E6、G6、E7、H7、F8、G8 共 7 口；任何观察者的气层都给出同一结果。
        // 变异验证 M-V3：TacticalLayers.Liberties 的 LibertyCount 改为 `g.Stones.Length` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5().Pieces(P2, PieceType.Basic, "F6", "F7", "G7");

        foreach (PlayerId viewer in TestMaps.All)
        {
            var layer = (LibertyLayerContent)match.World(viewer).Layer(TacticalLayer.Board, BoardReading.Groups);
            LibertyGroupView group = Assert.Single(layer.Groups, g => g.Owner == P2);
            Assert.Equal(["F6", "F7", "G7"], group.Stones.Notations());
            Assert.Equal(["F5", "E6", "G6", "E7", "H7", "F8", "G8"], group.Liberties.Notations());
            Assert.Equal(7, group.LibertyCount);
        }
    }

    [Fact]
    public void 活形状态公开()
    {
        // life-shape D6 / 始终公开的信息第 6 条：P0 两眼活形（眼 F7、H7）、P2 一眼未定（角上 C10，C9 / D10）、P3 孤子 K3（无眼，死）——retire-legacy-rules：平坦 8×8 链接载体，环列 +1、行 +2，三角各贴棋盘 0 的角。
        // 四名观察者从各自的公开世界读到同一份：每条棋串的三态、已确定活形的眼空间、每名玩家的禁入格；且与公开盘面上独立重算的结果逐项相同。
        MatchFlow match = AiFixtures.Round5()
            .Stones(P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingF7H7)
            .Stones(TestMaps.P2, "C9", "D10")
            .Stones(TestMaps.P3, "K3");

        foreach (PlayerId viewer in TestMaps.All)
        {
            MatchPublicView view = match.World(viewer).Public.View;
            Siege.Core.Board.LifeShapeReport life = view.LifeShape;

            Assert.Equal(Siege.Core.Board.LifeState.Alive, life.LifeOf("F6"));
            Assert.Equal(["F7", "H7"], life.EyeSpacesOf("F6"));
            Assert.Equal(Siege.Core.Board.LifeState.Undetermined, life.LifeOf("C9"));
            Assert.Equal(Siege.Core.Board.LifeState.Undetermined, life.LifeOf("D10"));
            Assert.Equal(Siege.Core.Board.LifeState.Dead, life.LifeOf("K3"));
            Assert.Empty(life.Forbidden(P0));
            Assert.Equal(["F7", "H7"], life.Forbidden(P1));
            Assert.Equal(["F7", "H7"], life.Forbidden(TestMaps.P2));
            Assert.Equal(["F7", "H7"], life.Forbidden(TestMaps.P3));
            Assert.Equal(
                LifeShapeFixtures.Describe(Siege.Core.Board.LifeShapeReport.Analyze(view.Board)),
                LifeShapeFixtures.Describe(life));
        }
    }
}
