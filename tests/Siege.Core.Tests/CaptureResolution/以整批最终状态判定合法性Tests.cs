using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.CaptureResolution;

/// <summary>规格：capture-resolution —— Requirement: 以整批最终状态判定合法性</summary>
public class 以整批最终状态判定合法性Tests
{
    /// <summary>P1 用 B4/C3/C5/D4 包住 C4；P0 已有 D3/D5。C4 单独落下无气，整批加 E4 后 D4 被提，C4 得气。</summary>
    private static GameBoard PocketBoard() => TestMaps.Blank(size: 7)
        .Place("B4", TestMaps.P1).Place("C3", TestMaps.P1).Place("C5", TestMaps.P1).Place("D4", TestMaps.P1)
        .Place("D3", TestMaps.P0).Place("D5", TestMaps.P0);

    [Theory]
    [InlineData("C4", "E4")]
    [InlineData("E4", "C4")]
    public void 提子后获得气的合法批次(string first, string second)
    {
        // 设计文档 §6.1：一枚单独看似无气的暂放棋子，只要完整批次完成提子后获得气，便属于合法落子。
        // 两种落子顺序都要合法——把 C4 放在前面的那组能抓住"逐枚结算"的变异。
        // 变异验证：Rehearse 改成每放一枚就做一次提子+自杀检查（逐枚结算）→ ("C4","E4") 这组红 1。
        GameBoard board = PocketBoard();
        SettlementDriver driver = BatchFixtures.Driver(board);
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0);

        // 前提：C4 单独落下确实是自杀手
        Assert.Equal(BatchFailureKind.Suicide, driver.Rehearse(context, [BatchFixtures.P("C4")]).Failure!.Kind);

        RehearsalResult rehearsal = driver.Rehearse(context, [BatchFixtures.P(first), BatchFixtures.P(second)]);

        Assert.True(rehearsal.IsLegal, rehearsal.Failure?.Message);
        Assert.Equal(["D4"], rehearsal.Captures.Select(s => s.Coord).Notations());
        Group own = rehearsal.ProjectedBoard!.GroupAt(TestMaps.At("C4"))!;
        Assert.Equal(["D4"], rehearsal.ProjectedBoard.LibertiesOf(own).Notations());
        Assert.True(driver.Confirm(context, [BatchFixtures.P(first), BatchFixtures.P(second)]).Confirmed);
    }

    [Fact]
    public void 真正的自杀手()
    {
        // 敌方完整包围的单点空位，且批次未造成任何敌串无气 → 第 5 步判自杀手。
        // 变异验证：Rehearse 删掉第 5 步 → 本测试红 1（且 「非法批次保留暂放」等红）。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B4", TestMaps.P1).Place("C3", TestMaps.P1).Place("C5", TestMaps.P1).Place("D4", TestMaps.P1);
        SettlementDriver driver = BatchFixtures.Driver(board);

        RehearsalResult rehearsal = driver.Rehearse(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("C4")]);

        Assert.False(rehearsal.IsLegal);
        Assert.Equal(BatchFailureKind.Suicide, rehearsal.Failure!.Kind);
        Assert.Equal(["C4"], rehearsal.Failure.Coords.Notations());
        Assert.Empty(rehearsal.Captures);
    }

    [Fact]
    public void 敌串同时移除()
    {
        // P1[C3] 与 P2[D3] 相邻，各自的其余邻格都是 P0；批次落 B3、E3 后两条同时无气。
        // 顺序循环会先提 C3、让 D3 得气而漏提（反之亦然）。
        // 变异验证：CaptureResolver.FindCaptured 改成"无气即 board.RemoveStones 再继续遍历" → 本测试红 1，
        // 「提子顺序无关性回归」同时红。trellis-check 复核（在 Clone 上顺序移除）：红 3（本测试、顺序无关性回归、结算的原子性）。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("C3", TestMaps.P1).Place("D3", BatchFixtures.P2)
            .Place("C2", TestMaps.P0).Place("C4", TestMaps.P0).Place("D2", TestMaps.P0).Place("D4", TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);

        RehearsalResult rehearsal = driver.Rehearse(
            BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("B3"), BatchFixtures.P("E3")]);

        Assert.True(rehearsal.IsLegal);
        Assert.Equal(
            [new CapturedStone(TestMaps.At("C3"), TestMaps.P1, PieceType.Basic), new CapturedStone(TestMaps.At("D3"), BatchFixtures.P2, PieceType.Basic)],
            rehearsal.Captures);
        Assert.Null(rehearsal.ProjectedBoard![TestMaps.At("C3")].Occupant);
        Assert.Null(rehearsal.ProjectedBoard[TestMaps.At("D3")].Occupant);
    }

    [Fact]
    public void 提子顺序无关性回归()
    {
        // 强制回归（determinism.md）：种子驱动的随机棋串遍历顺序 100 次，提子集合与结算后盘面完全一致。
        // 盘面含三对互相贴住的不同玩家敌串，任何顺序循环实现都会在某些排列下漏提。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("C3", TestMaps.P1).Place("D3", BatchFixtures.P2)
            .Place("C2", TestMaps.P0).Place("C4", TestMaps.P0).Place("D2", TestMaps.P0).Place("D4", TestMaps.P0)
            .Place("G7", TestMaps.P1).Place("G6", BatchFixtures.P2).Place("H7", new PlayerId(3))
            .Place("F7", TestMaps.P0).Place("G8", TestMaps.P0).Place("F6", TestMaps.P0).Place("G5", TestMaps.P0)
            .Place("H6", TestMaps.P0).Place("H8", TestMaps.P0).Place("J7", TestMaps.P0);
        Placement[] placements = [BatchFixtures.P("B3"), BatchFixtures.P("E3")];
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0);
        BoardHistory history = new();

        RehearsalResult canonical = BatchRehearsal.Rehearse(board, context, placements, history);
        Assert.True(canonical.IsLegal);
        Assert.Equal(["C3", "D3", "G6", "G7", "H7"], canonical.Captures.Select(s => s.Coord).Notations());
        string expectedBoard = canonical.ProjectedBoard!.Serialize();

        var rng = new Random(20260912);
        for (int run = 0; run < 100; run++)
        {
            // 随机棋串遍历顺序直接喂给提子接缝
            GameBoard placed = board.Clone();
            foreach (Placement p in BatchFixtures.Shuffle(placements, rng))
            {
                placed.Place(p.Coord, TestMaps.P0, p.Type);
            }

            ImmutableArray<CapturedStone> captured = CaptureResolver.FindCaptured(
                placed, TestMaps.P0, BatchFixtures.Shuffle(placed.AllGroups(), rng));
            Assert.Equal(canonical.Captures, captured);

            placed.RemoveStones(captured.Select(s => s.Coord));
            Assert.Equal(expectedBoard, placed.Serialize());

            // 随机落子顺序走完整预演，结果同样一致
            RehearsalResult shuffled = BatchRehearsal.Rehearse(board, context, BatchFixtures.Shuffle(placements, rng), history);
            Assert.Equal(canonical.Captures, shuffled.Captures);
            Assert.Equal(expectedBoard, shuffled.ProjectedBoard!.Serialize());
        }
    }

    [Fact]
    public void 自杀手检查全量重算()
    {
        // 裁决记录 2：第 5 步重算当前玩家全部棋串，不收窄到落点所在串。
        // 用例 1：两枚分散落子，先落的那枚是自杀手、后落的合法——只查"最后一枚所在串"会放行。
        // 变异验证：第 5 步只检查 placements[^1] 所在棋串 → 用例 1 红；只检查含落点的棋串 → 用例 2 红。
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B4", TestMaps.P1).Place("C3", TestMaps.P1).Place("C5", TestMaps.P1).Place("D4", TestMaps.P1);
        RehearsalResult scattered = BatchRehearsal.Rehearse(
            board, BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("C4"), BatchFixtures.P("G7")], new BoardHistory());
        Assert.Equal(BatchFailureKind.Suicide, scattered.Failure!.Kind);
        Assert.Equal(["C4"], scattered.Failure.Coords.Notations());

        // 用例 2：盘面上已存在一条无气的己方棋串（只能由绕过规则的写入造成）。全量重算下任何批次都会被拒，
        // 并把那条棋串报出来——"受影响子集"的边界极易漏判，漏判的表现是非法手被放行。
        GameBoard tainted = TestMaps.Blank(size: 7)
            .Place("A1", TestMaps.P0).Place("A2", TestMaps.P1).Place("B1", TestMaps.P1);
        Assert.True(tainted.IsCaptured(tainted.GroupAt(TestMaps.At("A1"))!));
        RehearsalResult far = BatchRehearsal.Rehearse(
            tainted, BatchFixtures.Context(tainted, TestMaps.P0), [BatchFixtures.P("G7")], new BoardHistory());
        Assert.Equal(BatchFailureKind.Suicide, far.Failure!.Kind);
        Assert.Equal(["A1"], far.Failure.Coords.Notations());
    }

    [Fact]
    public void 预演不污染正式盘面()
    {
        // 强制回归：任意预演（通过或失败、在第几步失败）后，正式盘面序列化逐字节不变。
        // 变异验证：Rehearse 第 3 步把 `board.Clone()` 改成 `board` → 本测试红 1（多条其他测试连带红）。
        GameBoard board = PocketBoard().Place("G7", BatchFixtures.P2);
        var hooks = new RecordingHooks { Board = board };
        SettlementDriver driver = BatchFixtures.Driver(board, hooks);
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0, limit: 2);
        string before = board.Serialize();

        // 先制造一条历史，让同形路径可达
        GameBoard twin = board.Clone();
        twin.Place(TestMaps.At("A1"), TestMaps.P0, PieceType.Basic);
        driver.History.Record(twin.Serialize());

        (string Name, Placement[] Batch)[] cases =
        [
            ("合法且提子", [BatchFixtures.P("C4"), BatchFixtures.P("E4")]),
            ("自杀手", [BatchFixtures.P("C4")]),
            ("同形", [BatchFixtures.P("A1")]),
            ("占据", [BatchFixtures.P("G7")]),
            ("超上限", [BatchFixtures.P("A1"), BatchFixtures.P("A2"), BatchFixtures.P("A3")]),
            ("Pass", []),
        ];
        foreach ((string name, Placement[] batch) in cases)
        {
            RehearsalResult result = driver.Rehearse(context, batch);
            Assert.Equal(before, board.Serialize());
            Assert.True(name is "合法且提子" or "Pass" ? result.IsLegal : !result.IsLegal, name);
        }

        Assert.Empty(hooks.Calls);
        Assert.Equal(1, driver.History.Count);
    }
}
