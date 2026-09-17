using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// terrain-model 5.1：盘面层视图模型带出每格高度、地表、桥与栅栏边，供 Godot 渲染——Godot 只消费这些字段，不读地图、不推地形。
/// 钉在真 v3 基准图地形上，字段值直接对照 implement.md 段 B 的文本图。scoring-sites 起 <see cref="FourPlayerBaseMap"/> 是 v4
/// （地形与 v3 逐格相同，只加据点），本类只测地形，断言不变。
/// </summary>
public class 盘面层视图模型带地形Tests
{
    private static DefaultBoardView V3Board()
    {
        MapData map = FourPlayerBaseMap.Create();
        PlayerId[] players = [P0, P1, P2, P3];
        MatchFlow match = MatchFlow.Create(map, new Siege.Core.Determinism.GameSeed(1), players, MatchOptions.Immediate);
        return DefaultBoardView.From(match.PublicWorldOf());
    }

    [Fact]
    public void 每格带高度地表与桥()
    {
        DefaultBoardView board = V3Board();
        Assert.Equal((13, 13), (board.Width, board.Height));

        // 出生区 0（左下高台）全 h=2；缓坡 E2 h=1；岛心 G7 h=0。
        BoardCellView b2 = board.CellAt(TestMaps.At("B2"));
        Assert.Equal((2, Surface.Grass, false, 0), (b2.Height, b2.Surface, b2.HasBridge, b2.BirthZone));
        Assert.Equal(1, board.CellAt(TestMaps.At("E2")).Height);
        Assert.Equal(0, board.CellAt(TestMaps.At("G7")).Height);

        // 深水 D4 未架桥 → 障碍且地表深水；G4 有桥 → 可落子且地表仍是深水。
        BoardCellView d4 = board.CellAt(TestMaps.At("D4"));
        Assert.Equal((Terrain.Obstacle, Surface.DeepWater, false), (d4.Terrain, d4.Surface, d4.HasBridge));
        BoardCellView g4 = board.CellAt(TestMaps.At("G4"));
        Assert.Equal((Terrain.Playable, Surface.DeepWater, true), (g4.Terrain, g4.Surface, g4.HasBridge));

        // 岩石 A13：障碍但地表不是深水——Godot 靠 Surface 区分岩石与水，不再把水画成岩石（段 A 待决 A-2）。
        BoardCellView a13 = board.CellAt(TestMaps.At("A13"));
        Assert.Equal((Terrain.Obstacle, Surface.Grass), (a13.Terrain, a13.Surface));

        // 林地 E5、土路 C8。
        Assert.Equal(Surface.Forest, board.CellAt(TestMaps.At("E5")).Surface);
        Assert.Equal(Surface.Road, board.CellAt(TestMaps.At("C8")).Surface);

        // 全盘高度只有 0 / 1 / 2 三档，且三档都出现。
        Assert.Equal([0, 1, 2], board.Cells.Select(c => c.Height).Distinct().Order());
    }

    [Fact]
    public void 栅栏边随视图模型带出()
    {
        DefaultBoardView board = V3Board();

        // v3 的四段栅栏（C4 轨道）：按 Coord 字典序（先行后列）排列，端点归一化（A 在前）。
        Assert.Equal(["G5-H5", "E6-E7", "J7-J8", "F9-G9"], board.Fences.Select(f => f.ToString()));
        Assert.All(board.Fences, f => Assert.True(f.A.CompareTo(f.B) < 0, f.ToString()));
    }
}
