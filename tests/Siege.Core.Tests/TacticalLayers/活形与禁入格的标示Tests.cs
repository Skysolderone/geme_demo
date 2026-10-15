using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Camera;
using Siege.Presentation.Layers;
using Siege.Presentation.Style;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>规格：tactical-layers —— Requirement: 活形与禁入格的标示（change `life-shape` D6，tasks 3.2）。</summary>
public class 活形与禁入格的标示Tests
{
    /// <summary>
    /// P0 的环形活形：棋子见 <see cref="BatchDeployment.非法批次必须给出可定位的原因Tests.RingF7H7"/>，单格眼 F7、H7，外侧大片空地。
    /// 平坦 8×8 链接载体上四人同锁棋盘 0、第 5 大回合（合法范围即整块棋盘 0），<paramref name="first"/> 先行动，已进入部署阶段。
    /// retire-legacy-rules 1.6：原为 9×9 夹具上的 RingE5G5；本文件坐标由原 9×9 逐用例映射（环形相关为 (+1, +2)）。
    /// </summary>
    private static MatchFlow RingMatch(PlayerId first)
    {
        PlayerId[] order = [first, .. TestMaps.All.Where(p => p != first)];
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, order).Stones(P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingF7H7);
        match.OpenDeploy();
        return match;
    }

    [Fact]
    public void 已活棋串可辨()
    {
        // 规格 Scenario「已活棋串可辨」：同盘一条已确定活形棋串（P0 的环，两个单格眼）与一条只剩 1 口气的棋串（P2 的 C3，C4 被 P3 占住、只剩 D3）。
        // 前者标为"已活"，后者标为危险；二者的标记形状不同（MUST NOT 只依赖颜色）。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5).Stones(P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingF7H7)
            .Stones(P2, "C3").Stones(P3, "C4");
        var content = (LibertyLayerContent)match.World(P1).Layer(TacticalLayer.Board, BoardReading.Groups);

        LibertyGroupView ring = content.Groups.Single(g => g.Stones.Contains(TestMaps.At("F6")));
        LibertyGroupView lone = content.Groups.Single(g => g.Stones.Contains(TestMaps.At("C3")));

        Assert.Equal(LifeState.Alive, ring.Life);
        Assert.Equal(GroupMark.Alive, ring.Mark);
        Assert.Equal("已活", ring.MarkText);
        Assert.Equal(DangerLevel.Safe, ring.Level);

        Assert.Equal(1, lone.LibertyCount);
        Assert.NotEqual(LifeState.Alive, lone.Life);
        Assert.Equal(GroupMark.Danger, lone.Mark);
        Assert.NotEqual("已活", lone.MarkText);

        // 外观不同且不只靠颜色：三种标记各有独立形状。
        Assert.NotEqual(GroupMarks.ShapeOf(GroupMark.Alive), GroupMarks.ShapeOf(GroupMark.Danger));
        Assert.Equal(Enum.GetValues<GroupMark>().Length, Enum.GetValues<GroupMark>().Select(GroupMarks.ShapeOf).Distinct().Count());
    }

    [Fact]
    public void 已活棋串即使气少也标已活()
    {
        // 标记优先级：已活高于危险。角上两眼活形只剩两口气（都是眼），按气数是"危险"，但它受活棋禁入与破坏活形保护、非所有者提不走，
        // 读法 MUST 标"已活"而不是"危险"——否则玩家会被一条提不掉的棋串误导。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5)
            .Stones(P0, "C5", "D5", "E5", "F5", "D4", "F4", "C3", "D3", "E3", "F3")
            .Stones(P1, "C6", "D6", "E6", "F6", "G6", "G5", "G4", "G3");
        var content = (LibertyLayerContent)match.World(P2).Layer(TacticalLayer.Board, BoardReading.Groups);

        LibertyGroupView corner = content.Groups.Single(g => g.Stones.Contains(TestMaps.At("C3")));

        Assert.Equal(2, corner.LibertyCount);
        Assert.Equal(DangerLevel.Danger, corner.Level);
        Assert.Equal(LifeState.Alive, corner.Life);
        Assert.Equal(GroupMark.Alive, corner.Mark);
    }

    [Fact]
    public void 禁入格在默认棋盘上可见()
    {
        // 规格 Scenario「禁入格在默认棋盘上可见」：轮到 P1，P0 有一条已确定活形棋串 → P0 的眼空间格 F7、H7 在默认棋盘上显示为不可落子（活棋禁入），
        // 指向该格可得知"活棋禁入（P0）"。禁入与"地形不可落子""超出合法落子范围"在数据上可区分：前者看 Terrain，后者由契约给出、这里为 None。
        MatchFlow match = RingMatch(P1);
        DefaultBoardView board = match.World(P1).Board();

        BoardCellView[] forbidden = [.. board.Cells.Where(c => c.Block == PlacementBlock.LifeForbidden)];
        Assert.Equal(["F7", "H7"], forbidden.Select(c => c.Coord).Notations());
        Assert.All(forbidden, c => Assert.Equal(P0, c.LifeForbiddenBy));
        Assert.All(forbidden, c => Assert.Equal(Terrain.Playable, c.Terrain));

        // 与合法落子范围契约一致：禁入格不在 P1 的范围里（界面高亮来自契约，本视图只负责说明原因）。
        IReadOnlySet<Coord> range = match.LegalRangeFor(P1);
        Assert.All(forbidden, c => Assert.DoesNotContain(c.Coord, range));

        // 指向禁入格：原因 + 所有者。
        Assert.Equal($"F7 · 活棋禁入（{Labels.Player(P0)}）", HoverReadout.Of(TestMaps.At("F7"), board));
        Assert.Equal("活棋禁入", PlacementBlocks.ReasonText(PlacementBlock.LifeForbidden));

        // 普通空格与棋子格没有禁入标记，指向只给坐标。
        Assert.Equal(PlacementBlock.None, board.CellAt(TestMaps.At("C7")).Block);
        Assert.Null(board.CellAt(TestMaps.At("F6")).LifeForbiddenBy);
        Assert.Equal("C7", HoverReadout.Of(TestMaps.At("C7"), board));
        Assert.Equal(string.Empty, HoverReadout.Of(null, board));
    }

    [Fact]
    public void 禁入格区别于地形不可落子与范围外()
    {
        // 三类"落不下"在视图数据里互斥、在外观上各有独立手法：地形（岩石 / 深水，看地形本身）、范围外（无标记）、活棋禁入（禁入印记）。
        // 第 1 大回合，P1 只能在自己所在的战场落子：P0 的眼 F7 不在 P1 的范围里——它仍 MUST 标活棋禁入（与预演第 1 步"禁入先于范围"同序）；
        // 战场之外的格（棋盘 1 的左下角）只是范围外。岩石 C8 是地形不可落子。
        // retire-legacy-rules 1.6：载体为平坦 8×8 链接档图、四人同锁棋盘 0；环形活形同 RingF7H7（原 RingE5G5 平移 (+1, +2)），岩石 B6 → C8。
        // 原"P2 出生区里的 A9"（保护期里 P1 的范围外）改为本战场之外的格：链接模式的合法范围恒为整个战场，座位视野里看不到战场之外的格，
        // 所以范围外这一格从不带视野过滤的公开棋盘（DefaultBoardView.From(PublicWorld)）上取。
        MapData plain = LinkModeFixtures.PlainMap();
        MapData map = plain with { Obstacles = plain.Obstacles.Add(TestMaps.At("C8")) };
        MatchFlow match = LinkModeFixtures.RequireLink(
            MatchFlow.CreateUnvalidated(map, TestMaps.Seed, TestMaps.All, TestMaps.Relics(map), LinkModeFixtures.PlainOptions(MatchOptions.Immediate)));
        foreach (PlayerId p in TestMaps.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(TestMaps.All.Select(p => (p, 0)));
        match.AtRound(1, [P1, P0, P2, P3]).Stones(P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingF7H7);
        match.OpenDeploy();
        DefaultBoardView board = match.World(P1).Board();
        IReadOnlySet<Coord> range = match.LegalRangeFor(P1);

        Assert.Equal(PlacementBlock.LifeForbidden, board.CellAt(TestMaps.At("F7")).Block);
        Assert.Equal(PlacementBlock.Terrain, board.CellAt(TestMaps.At("C8")).Block);
        BoardCellView outOfRange = DefaultBoardView.From(match.PublicWorldOf()).CellAt(LinkModeFixtures.PlainBoards[1].Origin);
        Assert.DoesNotContain(outOfRange.Coord, range);
        Assert.Equal(PlacementBlock.None, outOfRange.Block);
        Assert.Null(outOfRange.LifeForbiddenBy);

        PlacementMarkStyle[] styles = [PlacementBlocks.StyleOf(PlacementBlock.Terrain), PlacementBlocks.StyleOf(PlacementBlock.LifeForbidden), PlacementBlocks.OutOfRangeStyle];
        Assert.Equal(3, styles.Distinct().Count());
    }

    [Fact]
    public void 所有者看到的是可落子()
    {
        // 规格 Scenario「所有者看到的是可落子」：轮到 P0 → P0 自己的眼空间格 F7、H7 没有禁入标记，且在合法落子范围契约里（合法落点高亮来自契约）。
        MatchFlow match = RingMatch(P0);
        DefaultBoardView board = match.World(P0).Board();
        IReadOnlySet<Coord> range = match.LegalRangeFor(P0);

        foreach (string eye in new[] { "F7", "H7" })
        {
            BoardCellView cell = board.CellAt(TestMaps.At(eye));
            Assert.Null(cell.LifeForbiddenBy);
            Assert.Equal(PlacementBlock.None, cell.Block);
            Assert.Contains(cell.Coord, range);
            Assert.Equal(eye, HoverReadout.Of(cell.Coord, board));
        }

        Assert.DoesNotContain(board.Cells, c => c.Block == PlacementBlock.LifeForbidden);
    }

    [Fact]
    public void 表现层不调用活形分析只读公开视图()
    {
        // 守门（tasks 3.2）：活形状态与禁入格只从公开视图 MatchPublicView.LifeShape 读，表现层 MUST NOT 自己调 LifeShapeReport.Analyze 重算。
        // 反面断言证明扫描器确实看得见表现层对活形报告的读取（否则"没有 Analyze"可能只是没扫到）。
        string[] references =
        [
            .. IlReferences(PresentationAssembly)
                .Where(r => r.Target is System.Reflection.MethodBase)
                .Select(r => $"{r.Target.DeclaringType!.Name}.{r.Target.Name}")
                .Distinct()
                .Order(StringComparer.Ordinal),
        ];

        Assert.DoesNotContain("LifeShapeReport.Analyze", references);
        Assert.Contains("MatchPublicView.get_LifeShape", references);
        Assert.Contains("LifeShapeReport.GroupLifeAt", references);
        Assert.Contains("LifeShapeReport.ForbiddenCellsFor", references);
    }
}
