using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Style;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>规格：tactical-layers —— Requirement: 五种战术信息层（merge-board-layer 后为四种，盘面层含两种读法）</summary>
public class 五种战术信息层Tests
{
    [Fact]
    public void 归属读法三态可辨()
    {
        // 设计文档 §7.1 / §7.2 / §14.2：P0 占 D4、P1 占 F4 → D4 占据、C4 独占、E4 争议、A9 中立；四态以不同枚举值呈现，
        // 且全盘每格与 Core 覆盖表的归属逐格一致（不重算）。领地层弱化棋子演出（渲染部分归阶段 B）。
        // 变异验证 M-L1：TacticalLayers.Territory 把 Contested 映射为 Neutral → 本测试红 1。
        MatchFlow match = AiFixtures.Round5().Pieces(P0, PieceType.Basic, "D4").Pieces(P1, PieceType.Basic, "F4");

        var layer = (TerritoryLayerContent)match.World(P2).Layer(TacticalLayer.Board, BoardReading.Ownership);

        Assert.Equal((TerritoryState.Occupied, (PlayerId?)P0), State(layer, "D4"));
        Assert.Equal((TerritoryState.Exclusive, (PlayerId?)P0), State(layer, "C4"));
        Assert.Equal((TerritoryState.Contested, (PlayerId?)null), State(layer, "E4"));
        Assert.Equal((TerritoryState.Neutral, (PlayerId?)null), State(layer, "A9"));
        CoverageMap coverage = match.Scoreboard.Latest!.Coverage;
        Assert.Equal(81, layer.Cells.Length);
        Assert.All(layer.Cells, c => Assert.Equal(coverage.OwnershipOf(c.Coord).Kind.ToString(), c.State.ToString()));
        Assert.True(LayerVisuals.For(TacticalLayer.Board, BoardReading.Ownership).PieceEmphasisPercent < 100);
    }

    [Fact]
    public void 棋串读法标示危险棋串()
    {
        // 裁决 2：气 ≤ 2 危险、气 = 1 紧急，阈值可配置。P0 的 A1 被 P1 的 B1 贴住只剩 A2 一口气 → 紧急；
        // P0 的 J1 边角两口气 → 危险；P0 的 E5 四口气 → 安全。把危险阈值调到 4 后 E5 也变危险。
        // 变异验证 M-L2：LibertyThresholds.Classify 的紧急判断改为 `liberties < Urgent` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5().Pieces(P0, PieceType.Basic, "A1", "J1", "E5").Pieces(P1, PieceType.Basic, "B1");

        var layer = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);

        LibertyGroupView a1 = layer.Groups.Single(g => g.Stones[0] == TestMaps.At("A1"));
        Assert.Equal((1, DangerLevel.Urgent), (a1.LibertyCount, a1.Level));
        Assert.Equal(["A2"], a1.Liberties.Notations());
        Assert.Equal(DangerLevel.Danger, layer.Groups.Single(g => g.Stones[0] == TestMaps.At("J1")).Level);
        Assert.Equal(DangerLevel.Safe, layer.Groups.Single(g => g.Stones[0] == TestMaps.At("E5")).Level);

        var loose = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups, new LibertyThresholds(danger: 4, urgent: 1));
        Assert.Equal(DangerLevel.Danger, loose.Groups.Single(g => g.Stones[0] == TestMaps.At("E5")).Level);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LibertyThresholds(danger: 1, urgent: 2));
    }

    [Fact]
    public void 信物层不泄漏未知内容()
    {
        // 设计文档 §14.2：信物层突出内容，但未发现的信物仍显示为未知。E5 未揭示、B8 已被 P1 占据揭示。
        // 同种子两局只改 E5 真实内容 → 信物层投影逐字相同。
        // 变异验证 M-L3：TacticalLayers.RelicCell 的 contentText 在未揭示时改为 `r.Spec.Zone.ToString()` → 本测试红 1（"未知信物"断言）。
        string Layer(RelicContent hidden)
        {
            MatchFlow match = AiFixtures.Round5(relics: [("E5", hidden), ("B8", RelicFixtures.Depot())]).Pieces(P1, PieceType.Basic, "B8");
            return Dump(match.World(P0).Layer(TacticalLayer.Relics));
        }

        Assert.Equal(Layer(RelicFixtures.Command()), Layer(RelicFixtures.Vanguard(2)));

        MatchFlow match = AiFixtures.Round5(relics: [("E5", RelicFixtures.Command()), ("B8", RelicFixtures.Depot())]).Pieces(P1, PieceType.Basic, "B8");
        var layer = (RelicLayerContent)match.World(P0).Layer(TacticalLayer.Relics);
        RelicCellView hidden = layer.Relics.Single(r => r.Coord == TestMaps.At("E5"));
        Assert.Equal((false, (RelicContent?)null, "未知信物"), (hidden.IsRevealed, hidden.Content, hidden.ContentText));
        RelicCellView shown = layer.Relics.Single(r => r.Coord == TestMaps.At("B8"));
        Assert.Equal((true, "兵站 +1"), (shown.IsRevealed, shown.ContentText));
    }

    [Fact]
    public void 信物层说明失效原因()
    {
        // 设计文档 §7.3 / §14.2：已揭示信物 E5 被 P0（E4）与 P1（E6）同时覆盖 → 争议，标示并说明不向任何玩家提供效果。
        // 变异验证 M-L4：RelicCell 的争议 reason 改为 null → 本测试红 1。
        MatchFlow match = AiFixtures.Round5(relics: ("E5", RelicFixtures.Command()))
            .Pieces(P0, PieceType.Basic, "E4").Pieces(P1, PieceType.Basic, "E6");

        RelicCellView relic = ((RelicLayerContent)match.World(P2).Layer(TacticalLayer.Relics)).Relics.Single();

        Assert.True(relic.IsRevealed);
        Assert.Equal(RelicControlKind.Contested, relic.Control);
        Assert.Equal("争议", relic.ControlText);
        Assert.Contains("争议", relic.IneffectiveReason);
        Assert.Contains("不向任何玩家提供效果", relic.IneffectiveReason);
        Assert.Equal(RelicControlKind.Contested, match.Relics.ControlOf(TestMaps.At("E5")).Kind);
    }

    [Fact]
    public void 弃赛者封锁的信物可辨()
    {
        // 裁决（relic-system 裁决 5）/ §7.3：G8 仅被弃赛玩家 P3 的 H8 覆盖 → "由已弃赛玩家控制（封锁）"；E5 争议；A9 无人控制——三者控制类别与文案两两不同。
        // 变异验证 M-L5：BlockedText 的控制文案改为"无人控制" → 本测试红 1。
        MatchFlow match = AiFixtures.Round5(relics: [("G8", RelicFixtures.Command()), ("E5", RelicFixtures.Depot()), ("A9", RelicFixtures.Prospecting())])
            .Pieces(P3, PieceType.Basic, "H8").Pieces(P0, PieceType.Basic, "E4").Pieces(P1, PieceType.Basic, "E6");
        match.Resign(P3);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(P3).Status);

        var relics = ((RelicLayerContent)match.World(P0).Layer(TacticalLayer.Relics)).Relics;
        RelicCellView blocked = relics.Single(r => r.Coord == TestMaps.At("G8"));
        RelicCellView contested = relics.Single(r => r.Coord == TestMaps.At("E5"));
        RelicCellView none = relics.Single(r => r.Coord == TestMaps.At("A9"));

        Assert.Equal((RelicControlKind.Blocked, (PlayerId?)P3), (blocked.Control, blocked.Holder));
        Assert.Contains("由已弃赛玩家", blocked.ControlText);
        Assert.EndsWith("控制（封锁）", blocked.ControlText);
        Assert.Equal(3, new[] { blocked.Control, contested.Control, none.Control }.Distinct().Count());
        Assert.Equal(3, new[] { blocked.ControlText, contested.ControlText, none.ControlText }.Distinct().Count());
        Assert.Equal(3, new[] { blocked.IneffectiveReason, contested.IneffectiveReason, none.IneffectiveReason }.Distinct().Count());
    }

    [Fact]
    public void 顺序层可解释下一轮排序()
    {
        // 设计文档 §11.2 算例：4 人局第 4 名基础先手值 0，持"先手 +2"后为 2，与原第 2 名竞争位置（同值时先手修正高者先）。
        // P0 A1–A4（势力 9，第 1）、P1 J1–J3（7，第 2）、P2 A9-B9（5，第 3）、P3 J9 + 先锋 +2 于 H9（3，第 4）。
        // 段 A 重算（9×9 图，总势力 = 领地 + 军势）：原 4 / 3 / 2 / 1 → 9 / 7 / 5 / 3，名次、先手值与预测顺序不变：
        //   P0 4 + 领地 5（B1–B4 + A5）；P1 3 + 领地 4（H1–H3 + J4，J 列贴右边）；P2 2 + 领地 3（A8 / B8 / C9）；P3 1 + 领地 2（J8 + 空的信物格 H9）。
        // 预测：P0(3) > P3(2, 修正 2) > P1(2, 修正 0) > P2(1)。随后 P0–P2 Pass、P3 Pass 结束大回合，实际生成的明细与预测逐项一致。
        // 变异验证 M-L6：MatchFlow.ForecastInitiative 的先手修正读取改为恒 0 → 本测试红 1。
        // 变异验证 M-L7：TacticalLayers.OrderRow 的 PredictedPosition 改为 `index` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5(relics: ("H9", RelicFixtures.Vanguard(2)))
            .Pieces(P0, PieceType.Basic, "A1", "A2", "A3", "A4")
            .Pieces(P1, PieceType.Basic, "J1", "J2", "J3")
            .Pieces(P2, PieceType.Basic, "A9", "B9")
            .Pieces(P3, PieceType.Basic, "J9");

        var layer = (OrderLayerContent)match.World(P1).Layer(TacticalLayer.Order);

        Assert.Equal([P0, P3, P1, P2], layer.PredictedNextOrder);
        Assert.Equal(
            ["P0:1:9:0:3:1", "P3:4:3:2:2:2", "P1:2:7:0:2:3", "P2:3:5:0:1:4"],
            layer.Rows.Select(r => $"{r.Player}:{r.Rank}:{r.Power}:{r.Bonus}:{r.Value}:{r.PredictedPosition}"));
        Assert.Equal("先手值 2 =（参赛 4 − 势力名次 4）+ 先手修正 2；预测第 2 位", layer.Rows[1].Explanation);

        string forecast = string.Join(";", match.PublishSupplement().NextOrderForecast!.Entries.Select(e => e.ToString()));
        match.PassTurn();
        match.PassTurn();
        match.PassTurn();
        match.Debug.SetPassStreak(0);
        match.PassTurn();
        InitiativeReport actual = match.InitiativeReports[^1];
        Assert.Equal(5, actual.CompletedMajorRound);
        Assert.Equal(forecast, string.Join(";", actual.Entries.Select(e => e.ToString())));
        Assert.Equal([P0, P3, P1, P2], match.ActionOrder);
    }

    private static (TerritoryState, PlayerId?) State(TerritoryLayerContent layer, string cell)
    {
        TerritoryCellView view = layer.Cells.Single(c => c.Coord == TestMaps.At(cell));
        return (view.State, view.Owner);
    }
}
