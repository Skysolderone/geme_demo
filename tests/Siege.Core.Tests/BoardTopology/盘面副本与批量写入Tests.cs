using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>规格：board-topology —— Requirement: 盘面副本与批量写入原语</summary>
public class 盘面副本与批量写入Tests
{
    private static readonly PlayerId P2 = new(2);

    [Fact]
    public void 副本与原盘面互不影响()
    {
        GameBoard original = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("E4", TestMaps.P1, PieceType.Fortress);
        string before = original.Serialize();

        GameBoard clone = original.Clone();
        clone.Place(TestMaps.At("D5"), TestMaps.P0, PieceType.Multiplier);
        clone.RemoveStones([TestMaps.At("E4")]);

        // 原盘面逐字节不变；副本反映了所做修改
        Assert.Equal(before, original.Serialize());
        Assert.NotEqual(before, clone.Serialize());
        Assert.Equal(new Occupant(TestMaps.P0, PieceType.Multiplier), clone[TestMaps.At("D5")].Occupant);
        Assert.Null(clone[TestMaps.At("E4")].Occupant);
        Assert.Equal(new Occupant(TestMaps.P1, PieceType.Fortress), original[TestMaps.At("E4")].Occupant);
        Assert.Null(original[TestMaps.At("D5")].Occupant);

        // 反向：之后修改原盘面，副本也不受影响
        string cloneSnapshot = clone.Serialize();
        original.Clear(TestMaps.At("D4"));
        Assert.Equal(cloneSnapshot, clone.Serialize());
        Assert.NotNull(clone[TestMaps.At("D4")].Occupant);
    }

    [Fact]
    public void 副本立即等价()
    {
        GameBoard original = TestMaps.Blank(size: 7, "C3")
            .Place("D4", TestMaps.P0)
            .Place("D5", TestMaps.P0, PieceType.Line)
            .Place("E4", TestMaps.P1)
            .Place("B6", P2, PieceType.Synergy);

        GameBoard clone = original.Clone();

        Assert.Equal(original.Serialize(), clone.Serialize());
        Assert.Same(original.Map, clone.Map);
        foreach (Coord c in original.AllCoords())
        {
            Assert.Equal(original[c], clone[c]);
            Group? g = original.GroupAt(c);
            Group? gc = clone.GroupAt(c);
            Assert.Equal(g?.ToString(), gc?.ToString());
            if (g is not null)
            {
                Assert.Equal(original.LibertiesOf(g), clone.LibertiesOf(gc!));
            }
        }

        Assert.Equal(
            original.AllGroups().Select(g => g.ToString()),
            clone.AllGroups().Select(g => g.ToString()));
    }

    [Fact]
    public void 副本不重跑校验()
    {
        // 诱饵：把咽喉标注清空，这张图 MUST 过不了静态校验。
        // 若 Clone() 偷偷走了 Load，就会在这里抛 MapValidationException。
        MapData bait = FourPlayerBaseMap.Create() with { ChokePoints = ImmutableHashSet<Coord>.Empty };
        Assert.Contains(MapValidator.Validate(bait).Failures, f => f.Code == "CHOKE_NOT_ANNOTATED");
        Assert.Throws<MapValidationException>(() => GameBoard.Load(bait));

        GameBoard board = GameBoard.LoadUnvalidated(bait);
        board.Place(TestMaps.At("F6"), TestMaps.P0, PieceType.Basic);

        GameBoard clone = board.Clone();

        Assert.Same(bait, clone.Map);
        Assert.Equal(board.Serialize(), clone.Serialize());
    }

    [Fact]
    public void 全盘棋串枚举不依赖名册()
    {
        // 三名玩家、五条棋串，调用方不提供任何玩家列表
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B2", TestMaps.P0)
            .Place("B3", TestMaps.P0, PieceType.Fortress)
            .Place("F6", TestMaps.P0)
            .Place("D4", TestMaps.P1)
            .Place("E4", TestMaps.P1, PieceType.Line)
            .Place("G1", P2)
            .Place("A7", P2, PieceType.Synergy);

        ImmutableArray<Group> all = board.AllGroups();

        Assert.Equal(5, all.Length);
        Assert.Equal(3, all.Select(g => g.Owner).Distinct().Count());

        // 按各棋串最小坐标的字典序排列（行优先、自下而上）
        string[] expectedOrder = ["P2[G1]", "P0[B2,B3]", "P1[D4,E4]", "P0[F6]", "P2[A7]"];
        Assert.Equal(expectedOrder, all.Select(g => g.ToString()));

        // 与分别按每名玩家枚举再合并的结果一致
        IEnumerable<string> merged = new[] { TestMaps.P0, TestMaps.P1, P2 }
            .SelectMany(p => board.GroupsOf(p))
            .OrderBy(g => g.Stones[0])
            .Select(g => g.ToString());
        Assert.Equal(merged, all.Select(g => g.ToString()));
    }

    [Fact]
    public void 批量移除后气立即重算()
    {
        // P0 的棋串 D4-D5 被 P1 的 E4、E5、C4 贴住，只剩 C5、D3、D6 三口气
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("D5", TestMaps.P0, PieceType.Fortress)
            .Place("E4", TestMaps.P1)
            .Place("E5", TestMaps.P1)
            .Place("C4", TestMaps.P1);
        Group own = board.GroupAt(TestMaps.At("D4"))!;
        Assert.Equal(["D3", "C5", "D6"], board.LibertiesOf(own).Notations());

        board.RemoveStones([TestMaps.At("E4"), TestMaps.At("E5")]);

        // 调用返回后，被腾出的 E4、E5 已在气集合中；C4 仍被占据
        Assert.Equal(["D3", "E4", "C5", "E5", "D6"], board.LibertiesOf(own).Notations());
        Assert.Null(board[TestMaps.At("E4")].Occupant);
        Assert.Null(board[TestMaps.At("E5")].Occupant);
        Assert.NotNull(board[TestMaps.At("C4")].Occupant);
    }

    [Fact]
    public void 批量移除无部分完成的中间态()
    {
        GameBoard board = TestMaps.Blank(size: 5)
            .Place("B2", TestMaps.P0)
            .Place("C3", TestMaps.P1);
        string before = board.Serialize();

        // 合法坐标在前、越界坐标在后：整批拒绝，前面的合法坐标也不能被清掉
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            board.RemoveStones([TestMaps.At("B2"), TestMaps.At("C3"), new Coord(5, 0)]));

        Assert.Equal(before, board.Serialize());
    }

    [Fact]
    public void 批量移除输入枚举中途抛出时不写入()
    {
        // 越界只是失败路径之一：调用方传入的惰性枚举本身也可能在半途抛出
        // （例如枚举时读取了已失效的棋串）。输入 MUST 先整体物化再写入，
        // 否则已枚举出的前几个坐标会在异常抛出前被清掉。
        // 变异验证：把 RemoveStones 改成"foreach (c in coords) { RequireInBounds; 写 null }"
        // （去掉物化与两段循环）→ 本测试与「批量移除无部分完成的中间态」精准红 2。
        GameBoard board = TestMaps.Blank(size: 5)
            .Place("B2", TestMaps.P0)
            .Place("C3", TestMaps.P1);
        string before = board.Serialize();

        static IEnumerable<Coord> ThrowsMidway()
        {
            yield return TestMaps.At("B2");
            throw new InvalidOperationException("枚举中途失效");
        }

        Assert.Throws<InvalidOperationException>(() => board.RemoveStones(ThrowsMidway()));

        Assert.Equal(before, board.Serialize());
        Assert.NotNull(board[TestMaps.At("B2")].Occupant);
    }

    [Fact]
    public void 批量移除空格是无害空操作()
    {
        GameBoard board = TestMaps.Blank(size: 5, "A1").Place("B2", TestMaps.P0);
        string before = board.Serialize();

        // 空的可落子格、障碍格、空集合都不抛
        board.RemoveStones([TestMaps.At("C3"), TestMaps.At("A1")]);
        board.RemoveStones([]);

        Assert.Equal(before, board.Serialize());
    }
}
