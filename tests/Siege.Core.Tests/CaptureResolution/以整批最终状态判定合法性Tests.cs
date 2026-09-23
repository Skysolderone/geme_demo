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

    // ---------- life-shape：活棋禁入（第 1 步）与破坏活形（第 6 步） ----------

    private static readonly PlayerId A = LifeShapeFixtures.A;
    private static readonly PlayerId B = LifeShapeFixtures.B;

    private static RehearsalResult Rehearse(GameBoard board, PlayerId player, params Placement[] placements) =>
        BatchRehearsal.Rehearse(board, BatchFixtures.Context(board, player, limit: 5), placements, new BoardHistory());

    private static Placement Fence(string artisan, string a, string b) =>
        BatchFixtures.Artisan(artisan, TerrainEdit.Fence(TestMaps.At(a), TestMaps.At(b)));

    /// <summary>
    /// 角上两眼活形：A 串 A3–D3、B2、D2、A1–D1，单格眼 A2、C2；外气全被 B（A4–E4、E3、E2、E1）填满，A 只剩两眼这两口气。
    /// </summary>
    internal static GameBoard CornerTwoEyes() => LifeShapeFixtures.Grid(
    [
        ".......",
        "11111..",
        "00001..",
        ".0.01..",
        "00001..",
    ]);

    /// <summary>
    /// 一字两眼：A 串 A2–E2，单格眼 A1（B1 岩石）与 E1（D1 岩石），每个眼只有一条气边通向 A 子；
    /// A2–E2 与第 3 行之间全是既有栅栏，第 3 行是空的、可供 B 的匠人落脚（几何上贴着 A 串，但不是 A 的气）。
    /// </summary>
    internal static GameBoard FencedLineTwoEyes() => LifeShapeFixtures.Grid(
        [
            ".....",
            "00000",
            ".###.",
        ],
        fences: [("A2", "A3"), ("B2", "B3"), ("C2", "C3"), ("D2", "D3"), ("E2", "E3")]);

    [Fact]
    public void 同时填两眼被禁入拦下()
    {
        // life-shape D3：B 把两枚子分别放进 A 活形的两个单格眼。没有禁入时这是合法的提子批次（A 两口气同时被填、整串被提，
        // B 的两子随即得气）——前提断言钉住 A 只剩这两口气，拦下它的只能是第 1 步。
        GameBoard board = CornerTwoEyes();
        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(board).LifeOf("A1"));
        Assert.Equal(["A2", "C2"], board.LibertiesOf(board.GroupAt(TestMaps.At("A1"))!).Notations());
        string before = board.Serialize();

        RehearsalResult result = Rehearse(board, B, BatchFixtures.P("A2"), BatchFixtures.P("C2"));

        Assert.False(result.IsLegal);
        Assert.Equal(BatchFailureKind.LifeForbidden, result.Failure!.Kind);
        Assert.Equal(["A2"], result.Failure.Coords.Notations());
        Assert.Null(result.ProjectedBoard);
        Assert.Empty(result.Captures);
        Assert.Equal(before, board.Serialize());
        Assert.NotNull(board.GroupAt(TestMaps.At("A1")));
    }

    [Fact]
    public void 未定棋串的眼可以进()
    {
        // A 串只有单格眼 A2（C2 是 A 子），另一口外气 E2。B 一批落 E2 + A2：A2 不是禁入格（A 为未定），第 5 步 A 无气被提，批次合法。
        GameBoard board = LifeShapeFixtures.Grid(
        [
            ".......",
            "11111..",
            "00001..",
            ".000...",
            "00001..",
        ]);
        LifeShapeReport life = LifeShapeReport.Analyze(board);
        Assert.Equal(LifeState.Undetermined, life.LifeOf("A1"));
        Assert.False(life.IsForbiddenFor(B, TestMaps.At("A2")));

        RehearsalResult result = Rehearse(board, B, BatchFixtures.P("E2"), BatchFixtures.P("A2"));

        Assert.True(result.IsLegal, result.Failure?.Message);
        Assert.Equal(
            ["A1", "B1", "C1", "D1", "B2", "C2", "D2", "A3", "B3", "C3", "D3"],
            result.Captures.Select(s => s.Coord).Notations());
    }

    [Fact]
    public void 立栅切开活形被拒()
    {
        // B 的匠人落 C3，在 C2–D2 立栅：A 串被切成 A2–C2（眼 A1）与 D2–E2（眼 E1），两条都只剩一个眼 → 第 6 步破坏活形。
        GameBoard board = FencedLineTwoEyes();
        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(board).LifeOf("A2"));

        RehearsalResult result = Rehearse(board, B, Fence("C3", "C2", "D2"));

        Assert.False(result.IsLegal);
        Assert.Equal(BatchFailureKind.BreaksLife, result.Failure!.Kind);
        Assert.Equal(["A2", "B2", "C2", "D2", "E2"], result.Failure.Coords.Notations());
        LifeShapeReport after = LifeShapeReport.Analyze(result.ProjectedBoard!);
        Assert.Equal(LifeState.Undetermined, after.LifeOf("A2"));
        Assert.Equal(LifeState.Undetermined, after.LifeOf("E2"));
    }

    [Fact]
    public void 立栅切出单子被拒()
    {
        // life-single-stone「切出单子即不再是活形」的非所有者一半（所有者自切在 LifeShape/活形三态Tests）：
        // B 的匠人落 C2、在 B1–C1 立栅，A 的两子活形串切成两枚单子。两枚单子各自仍有眼值 2 的直四（A1–A4 / D1–G1），
        // 没有单子上限时二者仍"活"、批次合法；有了上限二者都是未定 → 第 6 步破坏活形（design D3，第 6 步本身不改）。
        // 实现前红（IsLegal 为 True）。变异 M1（去掉单子上限）→ 红 7，M2（上限误用于 2 子）→ 红 7，本测试均在其中；明细见 implement 记录。
        GameBoard board = LifeShapeFixtures.SingleStoneCut();
        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(board).LifeOf("B1"));
        Assert.False(LifeShapeReport.Analyze(board).IsForbiddenFor(B, TestMaps.At("C2")));

        RehearsalResult result = Rehearse(board, B, Fence("C2", "B1", "C1"));

        Assert.False(result.IsLegal);
        Assert.Equal(BatchFailureKind.BreaksLife, result.Failure!.Kind);
        Assert.Equal(["B1", "C1"], result.Failure.Coords.Notations());
        Assert.Empty(result.Captures);
        LifeShapeReport after = LifeShapeReport.Analyze(result.ProjectedBoard!);
        Assert.Equal(2, after.GroupLifeAt(TestMaps.At("B1"))!.EyeValueSum);
        Assert.Equal(2, after.GroupLifeAt(TestMaps.At("C1"))!.EyeValueSum);
        Assert.Equal(LifeState.Undetermined, after.LifeOf("B1"));
        Assert.Equal(LifeState.Undetermined, after.LifeOf("C1"));
    }

    [Fact]
    public void 立栅隔开眼与棋子被拒()
    {
        // B 的匠人落 A3，在 A1–A2 立栅：眼 A1 不再贴任何棋串（B1 岩石），A 串只剩眼 E1 → 未定 → 第 6 步破坏活形。
        GameBoard board = FencedLineTwoEyes();

        RehearsalResult result = Rehearse(board, B, Fence("A3", "A1", "A2"));

        Assert.False(result.IsLegal);
        Assert.Equal(BatchFailureKind.BreaksLife, result.Failure!.Kind);
        LifeShapeReport after = LifeShapeReport.Analyze(result.ProjectedBoard!);
        Assert.Equal(LifeState.Undetermined, after.LifeOf("A2"));
        Assert.Null(after.EyeSpaceAt(TestMaps.At("A1")));
    }

    [Fact]
    public void 搭桥漏眼被拒()
    {
        // A 的环围出 6 格眼空间 B2–D3，右侧 E3 是未架桥深水（墙）。B 的匠人落 F3 给 E3 搭桥：空区连到 E3、贴到 F3 的 B 子，不再封闭 → A 无眼。
        GameBoard board = LifeShapeFixtures.Grid(
        [
            ".......",
            ".......",
            "00000..",
            "0...~..",
            "0...0..",
            "00000..",
        ]);
        LifeShapeReport before = LifeShapeReport.Analyze(board);
        Assert.Equal(LifeState.Alive, before.LifeOf("A1"));
        Assert.Equal(["B2,C2,D2,B3,C3,D3"], before.EyeSpacesOf("A1"));

        RehearsalResult result = Rehearse(board, B, BatchFixtures.Artisan("F3", TerrainEdit.Bridge(TestMaps.At("E3"))));

        Assert.False(result.IsLegal);
        Assert.Equal(BatchFailureKind.BreaksLife, result.Failure!.Kind);
        Assert.Equal(LifeState.Dead, LifeShapeReport.Analyze(result.ProjectedBoard!).LifeOf("A1"));
    }

    [Fact]
    public void 围死活形被拒()
    {
        // design D4：A 一字两眼，外气已被 B（B3–D3）填满，A3 / E3 与 A 串之间有既有栅栏。B 一批落两枚匠人：A3 立 A1–A2、E3 立 E1–E2，
        // 隔断 A 串与两个眼之间的全部气边 → 第 5 步 A 无气被提 → 第 6 步发现原棋子已不在副本上 → 破坏活形。
        // 没有第 6 步时这是合法批次（B 的五子连成一串，A 被提后得气）。
        GameBoard board = LifeShapeFixtures.Grid(
            [
                "2....",
                ".111.",
                "00000",
                ".###.",
            ],
            fences: [("A2", "A3"), ("E2", "E3")]);
        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(board).LifeOf("A2"));
        Assert.Equal(["A1", "E1"], board.LibertiesOf(board.GroupAt(TestMaps.At("A2"))!).Notations());
        SettlementDriver driver = BatchFixtures.Driver(board);
        Placement[] batch = [Fence("A3", "A1", "A2"), Fence("E3", "E1", "E2")];

        RehearsalResult result = driver.Rehearse(BatchFixtures.Context(board, B), batch);

        Assert.False(result.IsLegal);
        Assert.Equal(BatchFailureKind.BreaksLife, result.Failure!.Kind);
        Assert.Equal(["A2", "B2", "C2", "D2", "E2"], result.Captures.Select(s => s.Coord).Notations());
        Assert.Equal(["A2", "B2", "C2", "D2", "E2"], result.Failure.Coords.Notations());
        Assert.False(driver.Confirm(BatchFixtures.Context(board, B), batch).Confirmed);
        Assert.Equal(5, board.GroupAt(TestMaps.At("A2"))!.Size);
    }

    [Fact]
    public void 不影响活形的改造合法()
    {
        // B 的匠人落 C3，在 C3–D3 立栅（A 串旁，不碰 A 的气边）：结算后 A 仍为已确定活形，第 6 步通过。
        GameBoard board = FencedLineTwoEyes();

        RehearsalResult result = Rehearse(board, B, Fence("C3", "C3", "D3"));

        Assert.True(result.IsLegal, result.Failure?.Message);
        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(result.ProjectedBoard!).LifeOf("A2"));
    }

    [Fact]
    public void 所有者可以拆自己的眼()
    {
        // A 把一子落进自己的单格眼 C2：第 1 步不拦（所有者不受禁入），第 6 步不拦（只复查非己方活形）；
        // 结算后 A 只剩眼 A2，按新盘面重算为未定。
        GameBoard board = CornerTwoEyes();

        RehearsalResult result = Rehearse(board, A, BatchFixtures.P("C2"));

        Assert.True(result.IsLegal, result.Failure?.Message);
        Assert.Equal(LifeState.Undetermined, LifeShapeReport.Analyze(result.ProjectedBoard!).LifeOf("A1"));
    }

    [Fact]
    public void 提子后恢复活形的批次合法()
    {
        // 规格顺序：第 5 步（提子）在第 6 步（破坏活形）之前，第 6 步看的是提子后的盘面。
        // A 串 A3–E3，眼 A2、E2（各只有一条气边）；C 的孤子 C2 两侧岩石，唯一的气是 C1（C1–D1 是 C 的两格空区，眼值 0）。
        // B 一批两枚匠人：A4 立 A2–A3（A 失去眼 A2），C1 立 C1–C2（C2 无气）。放置 + 改造后、提子前 A 只剩眼 E2 → 未定；
        // 第 5 步提走 C2 后，C2 只贴 A 的 C3 → A 新得单格眼 C2 → 回到已确定活形 → 批次合法。
        GameBoard board = LifeShapeFixtures.Grid(
            [
                ".....",
                "00000",
                ".#2#.",
                "##..#",
            ],
            fences: [("A3", "A4"), ("B3", "B4"), ("C3", "C4"), ("D3", "D4"), ("E3", "E4")]);
        Placement[] batch = [Fence("A4", "A2", "A3"), Fence("C1", "C1", "C2")];
        Assert.Equal(LifeState.Alive, LifeShapeReport.Analyze(board).LifeOf("A3"));

        // 夹具几何上确实经过"提子前未定"：手工复现第 3–4 步
        GameBoard placed = board.Clone();
        foreach (Placement p in batch)
        {
            placed.Place(p.Coord, B, p.Type);
        }

        placed.ApplyTerrainEdits(batch.Select(p => p.Edit!.Value));
        Assert.Equal(LifeState.Undetermined, LifeShapeReport.Analyze(placed).LifeOf("A3"));

        RehearsalResult result = Rehearse(board, B, batch);

        Assert.True(result.IsLegal, result.Failure?.Message);
        Assert.Equal(["C2"], result.Captures.Select(s => s.Coord).Notations());
        LifeShapeReport after = LifeShapeReport.Analyze(result.ProjectedBoard!);
        Assert.Equal(LifeState.Alive, after.LifeOf("A3"));
        Assert.Equal(["C2", "E2"], after.EyeSpacesOf("A3"));
    }

    [Fact]
    public void 只挨空浅滩的落子是自杀手()
    {
        // terrain-surfaces tasks 4.2：C4 的四个气边邻格全是空浅滩、批次不提任何子 → 无气，按既有自杀规则判非法并给出可定位的原因。
        // 对照：同样位置四邻是空草地时合法。
        TerrainData shallows = TestMaps.Terrain(surfaces: [("B4", Surface.Shallows), ("D4", Surface.Shallows), ("C3", Surface.Shallows), ("C5", Surface.Shallows)]);
        GameBoard board = TestMaps.Blank(shallows, size: 7);
        SettlementDriver driver = BatchFixtures.Driver(board);

        RehearsalResult rehearsal = driver.Rehearse(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("C4")]);

        Assert.False(rehearsal.IsLegal);
        Assert.Equal(BatchFailureKind.Suicide, rehearsal.Failure!.Kind);
        Assert.Equal(["C4"], rehearsal.Failure.Coords.Notations());

        GameBoard grass = TestMaps.Blank(size: 7);
        Assert.True(BatchFixtures.Driver(grass).Rehearse(BatchFixtures.Context(grass, TestMaps.P0), [BatchFixtures.P("C4")]).IsLegal);
    }
}
