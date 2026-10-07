using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// terrain-model 5.1：盘面层视图模型带出每格高度、地表、桥与栅栏边，供 Godot 渲染——Godot 只消费这些字段，不读地图、不推地形。
/// 原钉在真 v3 / v4 / v5 基准图地形上（字段值对照 implement.md 段 B 的文本图）；retire-legacy-maps 段 A2 改为合成 9×9 图，
/// 在自定坐标上摆出同类地形（出生区高台 h=2、缓坡 h=1、未架桥深水、桥、岩石、林地、土路、四段栅栏），断言意图不变；坐标不沿用旧基准图。
/// 棋盘图开局全为 h=0 草地、无桥无栅，带不出这些字段。
/// </summary>
public class 盘面层视图模型带地形Tests
{
    private static DefaultBoardView TerrainBoard()
    {
        // 出生区 0（左下 A1–C3）全 h=2；缓坡 D2 h=1；深水 C6（未架桥）与 H4（有桥）；岩石 E9；林地 F3；土路 D7；
        // 四段栅栏故意不按字典序给出、其中一段端点倒写（排序与端点归一化由视图模型负责）。
        (string, int)[] heights = [.. new[] { "A1", "B1", "C1", "A2", "B2", "C2", "A3", "B3", "C3" }.Select(c => (c, 2)), ("D2", 1)];
        TerrainData terrain = TestMaps.Terrain(
            heights: heights,
            surfaces: [("C6", Surface.DeepWater), ("H4", Surface.DeepWater), ("F3", Surface.Forest), ("D7", Surface.Road)],
            bridges: ["H4"],
            fences: [("D8", "E8"), ("J6", "J7"), ("H2", "G2"), ("B4", "B5")]);
        MapData map = MatchFixtures.Map(terrain) with { Obstacles = [TestMaps.At("E9")] };
        PlayerId[] players = [P0, P1, P2, P3];
        MatchFlow match = MatchFlow.CreateUnvalidated(map, new Siege.Core.Determinism.GameSeed(1), players, MatchFixtures.Relics(map), MatchOptions.Immediate);
        return DefaultBoardView.From(match.PublicWorldOf());
    }

    [Fact]
    public void 每格带高度地表与桥()
    {
        DefaultBoardView board = TerrainBoard();
        Assert.Equal((9, 9), (board.Width, board.Height));

        // 出生区 0（左下高台）全 h=2；缓坡 D2 h=1；G7 h=0。
        BoardCellView b2 = board.CellAt(TestMaps.At("B2"));
        Assert.Equal((2, Surface.Grass, false, 0), (b2.Height, b2.Surface, b2.HasBridge, b2.BirthZone));
        Assert.Equal(1, board.CellAt(TestMaps.At("D2")).Height);
        Assert.Equal(0, board.CellAt(TestMaps.At("G7")).Height);

        // 深水 C6 未架桥 → 障碍且地表深水；H4 有桥 → 可落子且地表仍是深水。
        BoardCellView c6 = board.CellAt(TestMaps.At("C6"));
        Assert.Equal((Terrain.Obstacle, Surface.DeepWater, false), (c6.Terrain, c6.Surface, c6.HasBridge));
        BoardCellView h4 = board.CellAt(TestMaps.At("H4"));
        Assert.Equal((Terrain.Playable, Surface.DeepWater, true), (h4.Terrain, h4.Surface, h4.HasBridge));

        // 岩石 E9：障碍但地表不是深水——Godot 靠 Surface 区分岩石与水，不再把水画成岩石（段 A 待决 A-2）。
        BoardCellView e9 = board.CellAt(TestMaps.At("E9"));
        Assert.Equal((Terrain.Obstacle, Surface.Grass), (e9.Terrain, e9.Surface));

        // 林地 F3、土路 D7。
        Assert.Equal(Surface.Forest, board.CellAt(TestMaps.At("F3")).Surface);
        Assert.Equal(Surface.Road, board.CellAt(TestMaps.At("D7")).Surface);

        // 全盘高度只有 0 / 1 / 2 三档，且三档都出现。
        Assert.Equal([0, 1, 2], board.Cells.Select(c => c.Height).Distinct().Order());
    }

    [Fact]
    public void 栅栏边随视图模型带出()
    {
        DefaultBoardView board = TerrainBoard();

        // 四段栅栏（构造时乱序给出、H2–G2 端点倒写）：按 Coord 字典序（先行后列）排列，端点归一化（A 在前）。
        Assert.Equal(["G2-H2", "B4-B5", "J6-J7", "D8-E8"], board.Fences.Select(f => f.ToString()));
        Assert.All(board.Fences, f => Assert.True(f.A.CompareTo(f.B) < 0, f.ToString()));
    }
}
