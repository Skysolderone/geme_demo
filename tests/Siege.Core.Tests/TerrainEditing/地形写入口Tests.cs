using System.Reflection;
using System.Reflection.Emit;
using Siege.Core.Board;

namespace Siege.Core.Tests.TerrainEditing;

/// <summary>
/// 规格：terrain-edit —— Requirement: 改造动作集 / 改造不可逆且设施无归属；
/// terrain —— Requirement: 格属性 / 边属性 / 气边 / 覆盖关系（改造后重算）。
/// tasks 2.1：地形写入口唯一实现。
/// </summary>
public class 地形写入口Tests
{
    private static GameBoard Board() => TestMaps.Blank(
        TestMaps.Terrain(
            surfaces: [("D4", Surface.DeepWater), ("D5", Surface.DeepWater), ("F4", Surface.Forest)]),
        size: 9);

    [Fact]
    public void 架桥后两格同串()
    {
        // terrain「架桥产生气边」：深水格架桥后成为可落子格，与相邻同高可落子格之间产生气边。
        GameBoard board = Board();
        board.Place(TestMaps.At("C4"), TestMaps.P0, PieceType.Basic);
        Assert.Empty(board.LibertyNeighbors(TestMaps.At("D4")));

        board.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4"))]);
        board.Place(TestMaps.At("D4"), TestMaps.P0, PieceType.Basic);

        // 气边、棋串、气全部按新地形重算：C4 与 D4 成为同一条棋串。
        Assert.Contains(TestMaps.At("C4"), board.LibertyNeighbors(TestMaps.At("D4")));
        Group group = board.GroupAt(TestMaps.At("C4"))!;
        Assert.Equal(["C4", "D4"], group.Stones.Notations());
    }

    [Fact]
    public void 立栅后分串()
    {
        // terrain「立栅移除气边」：原本沿气边同属一串的两枚同色棋子，其间立栅后分属两串，各自的气重算。
        GameBoard board = Board();
        board.Place(TestMaps.At("F6"), TestMaps.P0, PieceType.Basic);
        board.Place(TestMaps.At("G6"), TestMaps.P0, PieceType.Basic);
        Assert.Equal(2, board.GroupAt(TestMaps.At("F6"))!.Stones.Length);
        int before = board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!).Length;

        board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("F6"), TestMaps.At("G6"))]);

        Assert.Equal(["F6"], board.GroupAt(TestMaps.At("F6"))!.Stones.Notations());
        Assert.Equal(["G6"], board.GroupAt(TestMaps.At("G6"))!.Stones.Notations());

        // 两串各 3 口气（原来一串 6 口气里，F6-G6 那条边本就不是气）——重点是气确实重算了。
        Assert.Equal(6, before);
        Assert.Equal(3, board.LibertiesOf(board.GroupAt(TestMaps.At("F6"))!).Length);
        Assert.Equal(3, board.LibertiesOf(board.GroupAt(TestMaps.At("G6"))!).Length);
    }

    [Fact]
    public void 烧林后可被覆盖()
    {
        // terrain「烧林后该格可被覆盖」+「烧林不改变气边」：地表变草地，覆盖关系重算，气边一条不变。
        GameBoard board = Board();
        Coord forest = TestMaps.At("F4");
        Coord source = TestMaps.At("E4");
        Assert.DoesNotContain(forest, board.CoverageTargets(source));
        string[] libertiesBefore = board.LibertyNeighbors(forest).Notations();

        board.ApplyTerrainEdits([TerrainEdit.Burn(forest)]);

        Assert.Contains(forest, board.CoverageTargets(source));
        Assert.Equal(Surface.Grass, board.Map.SurfaceAt(forest));
        Assert.Equal(libertiesBefore, board.LibertyNeighbors(forest).Notations());
    }

    [Fact]
    public void 架桥切断隔水覆盖()
    {
        // terrain「架桥切断隔水覆盖」：s 原本隔着一格宽深水覆盖对岸的 t，该深水格架桥后
        // ① s 不再覆盖 t（那格不再是"可被穿过的一格宽深水"）② s 改为覆盖新架桥的那一格。
        // 规格是两半，只断言其中一半会漏掉"架完桥连桥格都覆盖不到"的实现错误。
        GameBoard board = Board();
        Coord source = TestMaps.At("C4");
        Coord water = TestMaps.At("D4");
        Coord across = TestMaps.At("E4");

        Assert.Contains(across, board.CoverageTargets(source));
        Assert.DoesNotContain(water, board.CoverageTargets(source));

        board.ApplyTerrainEdits([TerrainEdit.Bridge(water)]);

        Assert.DoesNotContain(across, board.CoverageTargets(source));
        Assert.Contains(water, board.CoverageTargets(source));
    }

    [Fact]
    public void 立栅不影响覆盖()
    {
        // terrain「立栅不影响覆盖」：覆盖 MUST NOT 被栅栏阻挡——同一条边上气边没了、覆盖一条不变。
        // 与「立栅移除气边」是同一道栅栏的两面，分开断言才挡得住"顺手也把覆盖切了"的实现。
        GameBoard board = Board();
        Coord s = TestMaps.At("F6");
        Coord t = TestMaps.At("G6");
        string[] coverageBefore = board.CoverageTargets(s).Notations();
        Assert.Contains(t, board.CoverageTargets(s));
        Assert.Contains(t, board.LibertyNeighbors(s));

        board.ApplyTerrainEdits([TerrainEdit.Fence(s, t)]);

        Assert.Equal(coverageBefore, board.CoverageTargets(s).Notations());
        Assert.Contains(t, board.CoverageTargets(s));
        Assert.Contains(s, board.CoverageTargets(t));

        // 反面：这道栅栏确实生效了（否则上面的"不变"是因为什么都没发生）。
        Assert.DoesNotContain(t, board.LibertyNeighbors(s));
    }

    [Fact]
    public void 改造只加不减且不动高度与障碍()
    {
        // R-4 + terrain-edit「MUST NOT 提供逆向动作」：写入口只有加桥 / 加栅 / 林地→草地，且不碰高度、障碍与信物。
        GameBoard board = TestMaps.Blank(
            TestMaps.Terrain(
                heights: [("D4", 1), ("F4", 2)],
                surfaces: [("D4", Surface.DeepWater), ("F4", Surface.Forest)]),
            size: 9,
            "B2");
        MapData before = board.Map;

        board.ApplyTerrainEdits(
        [
            TerrainEdit.Bridge(TestMaps.At("D4")),
            TerrainEdit.Burn(TestMaps.At("F4")),
            TerrainEdit.Fence(TestMaps.At("G6"), TestMaps.At("H6")),
        ]);

        // 输入那份快照原样不动（不可变值）；新快照只多了设施、地表只由林地变草地。
        Assert.Equal(1, before.HeightAt(TestMaps.At("D4")));
        Assert.Equal(Surface.DeepWater, before.SurfaceAt(TestMaps.At("D4")));
        Assert.False(before.HasBridge(TestMaps.At("D4")));

        Assert.Equal(1, board.Map.HeightAt(TestMaps.At("D4")));
        Assert.Equal(2, board.Map.HeightAt(TestMaps.At("F4")));
        Assert.Equal(Surface.DeepWater, board.Map.SurfaceAt(TestMaps.At("D4")));
        Assert.True(board.Map.HasBridge(TestMaps.At("D4")));
        Assert.Equal(Surface.Grass, board.Map.SurfaceAt(TestMaps.At("F4")));
        Assert.True(board.Map.HasFence(TestMaps.At("H6"), TestMaps.At("G6")));
        Assert.Equal(before.Obstacles, board.Map.Obstacles);
        Assert.Equal(before.RelicCells, board.Map.RelicCells);
    }

    [Fact]
    public void 对局中架的桥与预置桥完全等价()
    {
        // terrain「对局中架的桥与预置桥等价」/「对局中立的栅栏与预置栅栏等价」：
        // 用"改造出来的地形"与"一开始就这样的地形"逐格比对可落子性、气边与覆盖。
        GameBoard edited = Board();
        edited.ApplyTerrainEdits(
        [
            TerrainEdit.Bridge(TestMaps.At("D4")),
            TerrainEdit.Fence(TestMaps.At("F6"), TestMaps.At("G6")),
            TerrainEdit.Burn(TestMaps.At("F4")),
        ]);
        GameBoard preset = TestMaps.Blank(
            TestMaps.Terrain(
                surfaces: [("D4", Surface.DeepWater), ("D5", Surface.DeepWater)],
                bridges: ["D4"],
                fences: [("F6", "G6")]),
            size: 9);

        foreach (Coord c in preset.AllCoords())
        {
            Assert.Equal(preset.Map.IsPlayable(c), edited.Map.IsPlayable(c));
            Assert.Equal(preset.Map.SurfaceAt(c), edited.Map.SurfaceAt(c));
            Assert.Equal(preset.LibertyNeighbors(c).Notations(), edited.LibertyNeighbors(c).Notations());
            Assert.Equal(preset.CoverageTargets(c).Notations(), edited.CoverageTargets(c).Notations());
        }
    }

    [Fact]
    public void 同一目标不能被写两次()
    {
        // R-5「没有逆向动作」的自然推论：已架桥的深水格、已有栅栏的边、已是草地的格都不是合法目标。
        // 写入口本身也拒绝——它是最后一道门，绕过合法性判定直接调它同样写不进去。
        GameBoard board = Board();
        board.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4"))]);

        Assert.Contains("已架桥", Assert.Throws<SiegeRuleException>(
            () => board.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4"))])).Message);
        Assert.Contains("深水", Assert.Throws<SiegeRuleException>(
            () => board.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("C4"))])).Message);
        Assert.Contains("林地", Assert.Throws<SiegeRuleException>(
            () => board.ApplyTerrainEdits([TerrainEdit.Burn(TestMaps.At("C4"))])).Message);

        board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("F6"), TestMaps.At("G6"))]);
        Assert.Contains("已有栅栏", Assert.Throws<SiegeRuleException>(
            () => board.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("G6"), TestMaps.At("F6"))])).Message);
    }

    [Fact]
    public void 一组改造同时生效且与顺序无关()
    {
        // terrain-edit「多个改造同时生效」：结果 MUST NOT 依赖应用顺序。
        TerrainEdit[] edits =
        [
            TerrainEdit.Bridge(TestMaps.At("D4")),
            TerrainEdit.Burn(TestMaps.At("F4")),
            TerrainEdit.Fence(TestMaps.At("F6"), TestMaps.At("G6")),
        ];

        GameBoard forward = Board();
        forward.ApplyTerrainEdits(edits);
        GameBoard backward = Board();
        backward.ApplyTerrainEdits(edits.Reverse());

        Assert.Equal(forward.Serialize(), backward.Serialize());
        foreach (Coord c in forward.AllCoords())
        {
            Assert.Equal(forward.LibertyNeighbors(c).Notations(), backward.LibertyNeighbors(c).Notations());
            Assert.Equal(forward.CoverageTargets(c).Notations(), backward.CoverageTargets(c).Notations());
        }
    }

    [Fact]
    public void 副本上的改造不影响原盘面()
    {
        // 预演在副本上应用改造（七步预演第 4 步），"预演不改变游戏状态"因此要求副本自带独立的地形快照。
        GameBoard board = Board();
        GameBoard clone = board.Clone();
        clone.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4"))]);

        Assert.True(clone.Map.HasBridge(TestMaps.At("D4")));
        Assert.False(board.Map.HasBridge(TestMaps.At("D4")));
        Assert.Empty(board.TerrainEdits);
        Assert.Single(clone.TerrainEdits);
    }

    [Fact]
    public void 地形写入口之外不得构造改造后的地形()
    {
        // 守门（tasks 2.1）：全仓只有三处允许 new TerrainData(...) ——写入口自身、地图文件读入、地图定义（v4 与 frontier-map 3.1 的边疆图各一个生成器，加 map-generator 的边疆档生成器）。
        // 第四处即"第二份地形写入实现"，改造后的重算与不可逆约束就会各写一份。
        // 变异验证 M-B1：把 TerrainWriter.Apply 的桥分支挪进 GameBoard.ApplyTerrainEdits（直接 new TerrainData）→ 本测试红。
        string[] allowed =
        [
            typeof(TerrainWriter).FullName!,
            typeof(TerrainData).FullName!,            // TerrainData.Flat
            "Siege.Core.Board.MapFile",
            "Siege.Core.Board.Maps.FourPlayerBaseMap",
            "Siege.Core.Board.Maps.FrontierMapV2",
            "Siege.Core.Board.Maps.FrontierMapGenerator",   // map-generator 1.3：生成图与两张内置图同属"地图定义"，只在灌成 MapData 的那一处构造
        ];

        (MethodBase Caller, MemberInfo Target, OpCode OpCode)[] constructions =
        [
            .. PresentationFixtures.IlReferences(typeof(GameBoard).Assembly)
                .Where(r => r.Target is ConstructorInfo ctor && ctor.DeclaringType == typeof(TerrainData)),
        ];

        string[] violations =
        [
            .. constructions
                .Select(r => Outermost(r.Caller.DeclaringType!).FullName!)
                .Where(n => !allowed.Contains(n, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(violations);

        // 反面断言：扫描器确实命中了写入口自身，不是扫了个空集。
        Assert.Contains(constructions, r => Outermost(r.Caller.DeclaringType!) == typeof(TerrainWriter));

        static Type Outermost(Type t)
        {
            while (t.DeclaringType is { } outer)
            {
                t = outer;
            }

            return t;
        }
    }
}
