using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Style;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>规格：tactical-layers —— Requirement: 五种战术信息层（merge-board-layer 后为四种，盘面层含两种读法）</summary>
/// <remarks>retire-legacy-rules 1.6：局面载体为平坦 8×8 链接档图上四人同锁棋盘 0（<see cref="AiFixtures.Round5"/>），坐标由原 9×9 逐用例映射（保住盘边 / 盘角身份）。</remarks>
public class 五种战术信息层Tests
{
    [Fact]
    public void 归属读法三态可辨()
    {
        // 设计文档 §7.1 / §7.2 / §14.2：P0 占 F5、P1 占 H5 → F5 占据、E5 独占、G5 争议、C10 中立；四态以不同枚举值呈现，
        // 且全盘每格与 Core 覆盖表的归属逐格一致（不重算）。领地层弱化棋子演出（渲染部分归阶段 B）。
        // 变异验证 M-L1：TacticalLayers.Territory 把 Contested 映射为 Neutral → 本测试红 1。
        MatchFlow match = AiFixtures.Round5().Pieces(P0, PieceType.Basic, "F5").Pieces(P1, PieceType.Basic, "H5");

        var layer = (TerritoryLayerContent)match.World(P2).Layer(TacticalLayer.Board, BoardReading.Ownership);

        Assert.Equal((TerritoryState.Occupied, (PlayerId?)P0), State(layer, "F5"));
        Assert.Equal((TerritoryState.Exclusive, (PlayerId?)P0), State(layer, "E5"));
        Assert.Equal((TerritoryState.Contested, (PlayerId?)null), State(layer, "G5"));
        Assert.Equal((TerritoryState.Neutral, (PlayerId?)null), State(layer, "C10"));
        CoverageMap coverage = match.Scoreboard.Latest!.Coverage;
        // retire-legacy-rules 1.6（R8）：原 9×9 的 81 格 → 平坦载体上 64 格（= 棋盘 0 的 8×8）；map-expand-25 段 B：插旗阶段之后座位可见整张图，
        // 平坦载体其余四块 6×6（各自是孤立战场）也在视图里，层内格 = 全图可落子格 64 + 4 × 36 = 208。
        Assert.Equal(64 + (4 * 36), layer.Cells.Length);
        Assert.Equal(match.Map.PlayableCount, layer.Cells.Length);
        Assert.All(layer.Cells, c => Assert.Equal(coverage.OwnershipOf(c.Coord).Kind.ToString(), c.State.ToString()));
        Assert.True(LayerVisuals.For(TacticalLayer.Board, BoardReading.Ownership).PieceEmphasisPercent < 100);
    }

    [Fact]
    public void 棋串读法标示危险棋串()
    {
        // 裁决 2：气 ≤ 2 危险、气 = 1 紧急，阈值可配置。P0 的 C3 被 P1 的 D3 贴住只剩 C4 一口气 → 紧急；
        // P0 的 K3 边角两口气 → 危险；P0 的 G7 四口气 → 安全。把危险阈值调到 4 后 G7 也变危险。
        // 变异验证 M-L2：LibertyThresholds.Classify 的紧急判断改为 `liberties < Urgent` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5().Pieces(P0, PieceType.Basic, "C3", "K3", "G7").Pieces(P1, PieceType.Basic, "D3");

        var layer = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);

        LibertyGroupView a1 = layer.Groups.Single(g => g.Stones[0] == TestMaps.At("C3"));
        Assert.Equal((1, DangerLevel.Urgent), (a1.LibertyCount, a1.Level));
        Assert.Equal(["C4"], a1.Liberties.Notations());
        Assert.Equal(DangerLevel.Danger, layer.Groups.Single(g => g.Stones[0] == TestMaps.At("K3")).Level);
        Assert.Equal(DangerLevel.Safe, layer.Groups.Single(g => g.Stones[0] == TestMaps.At("G7")).Level);

        var loose = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups, new LibertyThresholds(danger: 4, urgent: 1));
        Assert.Equal(DangerLevel.Danger, loose.Groups.Single(g => g.Stones[0] == TestMaps.At("G7")).Level);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LibertyThresholds(danger: 1, urgent: 2));
    }

    [Fact]
    public void 信物层列出地图信物与持物棋子()
    {
        // relic-redesign W1（R22：地图信物、挂出的信物全公开）：信物层是盘面信物层的只读投影。G6 空格上一件地图刷出的战鼓（持有者为空、文案"地图信物"），
        // P1 的 D9 挂着一件号令（持有者 P1、文案"挂在 …的棋子上"）。原"未发现信物显示为未知 / 争议与封锁的失效原因"随旧信物删除（段 C 做正式标记）。
        // 变异 M-L3'：LayerContents.Relics 把持有者恒取 null → 本测试红 1。
        MatchFlow match = AiFixtures.Round5().Pieces(P1, PieceType.Basic, "D9");
        match.Board.AttachRelic(TestMaps.At("G6"), RelicToken.Spawned(0, RelicKind.WarDrum));
        match.Board.AttachRelic(TestMaps.At("D9"), RelicToken.Spawned(1, RelicKind.Command));
        match.Debug.Recalculate();

        var layer = (RelicLayerContent)match.World(P0).Layer(TacticalLayer.Relics);
        RelicCellView onMap = layer.Relics.Single(r => r.Coord == TestMaps.At("G6"));
        RelicCellView held = layer.Relics.Single(r => r.Coord == TestMaps.At("D9"));
        Assert.Equal((RelicKind.WarDrum, (PlayerId?)null, false), (onMap.Kind, onMap.Holder, onMap.IsForecast));
        Assert.Contains("地图信物", onMap.Text, StringComparison.Ordinal);
        Assert.Equal((RelicKind.Command, (PlayerId?)P1), (held.Kind, held.Holder));
        Assert.Contains("挂在", held.Text, StringComparison.Ordinal);
        Assert.Equal(2, layer.Relics.Length);
    }

    [Fact]
    public void 顺序层可解释下一轮排序()
    {
        // 设计文档 §11.2：先手值 = 参赛人数 − 势力名次（relic-redesign D4：先锋先手修正随旧信物删除，原"持先手 +2 的第 4 名与第 2 名竞争"算例不再适用）。
        // P0 C3–C6（势力 9，第 1）、P1 K3–K5（7，第 2）、P2 C10-D10（5，第 3）、P3 K10（3，第 4）。
        // 段 A 重算（retire-legacy-rules 后为平坦 8×8 链接载体的棋盘 0，总势力 = 领地 + 军势）：原 4 / 3 / 2 / 1 → 9 / 7 / 5 / 3，名次、先手值与预测顺序不变：
        //   P0 4 + 领地 5（D3–D6 + C7）；P1 3 + 领地 4（J3–J5 + K6，K 列贴右边）；P2 2 + 领地 3（C9 / D9 / E10）；P3 1 + 领地 2（K9 / J10）。
        // 预测：P0(3) > P1(2) > P2(1) > P3(0)。随后 P0 落一子、P1–P3 Pass 结束大回合，实际生成的明细与（落子后读取的）预测逐项一致。
        // 变异验证 M-L7：TacticalLayers.OrderRow 的 PredictedPosition 改为 `index` → 本测试红 1。
        MatchFlow match = AiFixtures.Round5()
            .Pieces(P0, PieceType.Basic, "C3", "C4", "C5", "C6")
            .Pieces(P1, PieceType.Basic, "K3", "K4", "K5")
            .Pieces(P2, PieceType.Basic, "C10", "D10")
            .Pieces(P3, PieceType.Basic, "K10");

        var layer = (OrderLayerContent)match.World(P1).Layer(TacticalLayer.Order);

        Assert.Equal([P0, P1, P2, P3], layer.PredictedNextOrder);
        Assert.Equal(
            ["P0:1:9:3:1", "P1:2:7:2:2", "P2:3:5:1:3", "P3:4:3:0:4"],
            layer.Rows.Select(r => $"{r.Player}:{r.Rank}:{r.Power}:{r.Value}:{r.PredictedPosition}"));
        Assert.Equal("先手值 2 = 参赛 4 − 势力名次 2；预测第 2 位", layer.Rows[1].Explanation);

        // retire-legacy-rules 1.6：链接规则下本战场全员整轮 Pass 会触发战场结算（唯一战场结算即终局，不再生成下一轮顺序），
        // 所以 P0 改为在棋盘中央 G7 落一子（只抬高 P0 自己的势力，名次与预测顺序不变），预测在此之后读取，P1–P3 Pass 结束大回合。
        match.PlayTurn("G7");
        string forecast = string.Join(";", match.PublishSupplement().NextOrderForecast!.Entries.Select(e => e.ToString()));
        match.PassTurn();
        match.PassTurn();
        match.Debug.SetPassStreak(0);
        match.PassTurn();
        InitiativeReport actual = match.InitiativeReports[^1];
        Assert.Equal(5, actual.CompletedMajorRound);
        Assert.Equal(forecast, string.Join(";", actual.Entries.Select(e => e.ToString())));
        Assert.Equal([P0, P1, P2, P3], match.ActionOrder);
    }

    private static (TerritoryState, PlayerId?) State(TerritoryLayerContent layer, string cell)
    {
        TerritoryCellView view = layer.Cells.Single(c => c.Coord == TestMaps.At(cell));
        return (view.State, view.Owner);
    }
}
