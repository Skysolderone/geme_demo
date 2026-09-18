using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.TerrainEditing;

/// <summary>
/// 规格：terrain-edit —— Requirement: 改造先于提子生效；
/// capture-resolution —— Requirement: 以整批最终状态判定合法性 / 正式结算顺序。
/// tasks 2.4（裁决 T-3）。
/// </summary>
/// <remarks>
/// <para><b>裁决 T-11（段 B 待决 B-1 的结论）</b>：段 B 的旧口径下立栅的边必有一端是匠人落点 A，
/// 而第 5 步算提子时 A 已被己方匠人占据——该边既不是敌串的气（s = A 是己子、c = A 已占），
/// 也切不断敌串内部连接（A 不是敌子），因此<b>任何改造都不可能直接提子</b>（20 局 97 次改造实测 0 次）。
/// 边目标放宽为"至少一端是匠人落点的几何四邻格"后，匠人可以给<b>两枚敌子之间</b>或<b>敌子与其最后一口气之间</b>立栅，
/// 规格场景「立栅导致提子」「立栅切断敌串连接」因此成立并在本文件正面守住；
/// 段 B 的结构性 tripwire <c>立栅不改变任何敌串的提子结果</c> 钉的是旧规则的事实，已随本次放宽删除。</para>
/// </remarks>
public class 改造先于提子Tests
{
    /// <summary>
    /// 9×9，E6 为深水。P1 的棋串 {F5, F6} 的气被 P0 的 F4 / G5 / G6 / F7 堵到只剩 E5
    /// （E6 是未架桥深水，不是气）。匠人落 E5 即可提走该串——除非同一手把 E6 架成桥。
    /// </summary>
    private static GameBoard Cornered()
    {
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("E6", Surface.DeepWater)]), size: 9);
        board.Place(TestMaps.At("F5"), TestMaps.P1, PieceType.Basic);
        board.Place(TestMaps.At("F6"), TestMaps.P1, PieceType.Basic);
        foreach (string cell in new[] { "F4", "G5", "G6", "F7" })
        {
            board.Place(TestMaps.At(cell), TestMaps.P0, PieceType.Basic);
        }

        return board;
    }

    [Fact]
    public void 搭桥先于提子可救活敌串()
    {
        // 顺序敏感的正面算例：改造在第 4 步（提子之前）应用 → 新桥给敌串补上一口气，敌串不被提。
        // 若把改造挪到提子之后，敌串会先被提走 → 本条红（变异 M-B4）。
        GameBoard withoutBridge = Cornered();
        RehearsalResult plain = BatchFixtures.Driver(withoutBridge).Rehearse(
            BatchFixtures.Context(withoutBridge, TestMaps.P0), [BatchFixtures.P("E5", PieceType.Artisan)]);

        Assert.True(plain.IsLegal);
        Assert.Equal(["F5", "F6"], plain.Captures.Select(c => c.Coord).Notations());

        GameBoard withBridge = Cornered();
        SettlementDriver driver = BatchFixtures.Driver(withBridge);
        BatchContext context = BatchFixtures.Context(withBridge, TestMaps.P0);
        Placement[] batch = [BatchFixtures.Artisan("E5", TerrainEdit.Bridge(TestMaps.At("E6")))];

        RehearsalResult bridged = driver.Rehearse(context, batch);
        Assert.True(bridged.IsLegal, bridged.Failure?.Message);
        Assert.Empty(bridged.Captures);

        SettlementOutcome outcome = driver.Confirm(context, batch);
        Assert.True(outcome.Confirmed);
        Assert.Empty(outcome.CaptureRecord!.Captured);
        Assert.NotNull(withBridge[TestMaps.At("F5")].Occupant);
        Assert.True(withBridge.Map.HasBridge(TestMaps.At("E6")));
    }

    [Fact]
    public void 搭桥先于提子可使自杀手变合法()
    {
        // 第二条顺序敏感的正面算例：匠人落点唯一的空邻格是深水，只有"改造先于自杀手判定"才救得回来。
        // A1 的四邻只有 A2（深水）与 B1（敌子）。
        GameBoard Setup()
        {
            GameBoard b = TestMaps.Blank(TestMaps.Terrain(surfaces: [("A2", Surface.DeepWater)]), size: 9);
            b.Place(TestMaps.At("B1"), TestMaps.P1, PieceType.Basic);
            return b;
        }

        GameBoard plain = Setup();
        RehearsalResult suicide = BatchFixtures.Driver(plain).Rehearse(
            BatchFixtures.Context(plain, TestMaps.P0), [BatchFixtures.P("A1", PieceType.Artisan)]);
        Assert.False(suicide.IsLegal);
        Assert.Equal(BatchFailureKind.Suicide, suicide.Failure!.Kind);

        GameBoard bridged = Setup();
        RehearsalResult saved = BatchFixtures.Driver(bridged).Rehearse(
            BatchFixtures.Context(bridged, TestMaps.P0),
            [BatchFixtures.Artisan("A1", TerrainEdit.Bridge(TestMaps.At("A2")))]);

        Assert.True(saved.IsLegal, saved.Failure?.Message);
        Assert.Empty(saved.Captures);
    }

    [Fact]
    public void 改造导致的自杀手()
    {
        // terrain-edit「改造把自己堵死」：匠人立栅后己方棋串无气，且本批未提走任何敌串 → 整批被拒。
        // A1 角落：己子 A2、敌子 B1。匠人落 A1 时与 A2 连成一串（有气），一旦立栅 A1–A2 就被单独切出来且 0 气。
        GameBoard Setup()
        {
            GameBoard b = TestMaps.Blank(size: 9);
            b.Place(TestMaps.At("A2"), TestMaps.P0, PieceType.Basic);
            b.Place(TestMaps.At("B1"), TestMaps.P1, PieceType.Basic);
            return b;
        }

        // 对照：不立栅完全合法——被拒的原因确实是那道栅栏。
        GameBoard plain = Setup();
        Assert.True(BatchFixtures.Driver(plain).Rehearse(
            BatchFixtures.Context(plain, TestMaps.P0), [BatchFixtures.P("A1", PieceType.Artisan)]).IsLegal);

        GameBoard board = Setup();
        SettlementDriver driver = BatchFixtures.Driver(board);
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0);
        Placement[] batch = [BatchFixtures.Artisan("A1", TerrainEdit.Fence(TestMaps.At("A1"), TestMaps.At("A2")))];

        RehearsalResult rehearsal = driver.Rehearse(context, batch);

        Assert.False(rehearsal.IsLegal);
        Assert.Equal(BatchFailureKind.Suicide, rehearsal.Failure!.Kind);
        Assert.Equal(["A1"], rehearsal.Failure.Coords.Notations());

        // 整批被拒 → 正式盘面与地形一个字节都没变（预演零副作用）。
        Assert.False(driver.Confirm(context, batch).Confirmed);
        Assert.Empty(board.TerrainEdits);
        Assert.False(board.Map.HasFence(TestMaps.At("A1"), TestMaps.At("A2")));
    }

    [Fact]
    public void 立栅导致提子()
    {
        // terrain-edit「立栅导致提子」（裁决 T-3，口径由 T-11 放宽后才可实现）：
        // P1 单子 E5 只剩 E5–E6 一条气边。匠人落 E7（**不与 E5 相邻**），给 E5–E6 立栅——
        // E6 是 E7 的几何四邻，因此该边合法（旧口径下这条边非法，立栅永远提不了子）。
        GameBoard plainBoard = Trapped();
        RehearsalResult plain = BatchFixtures.Driver(plainBoard).Rehearse(
            BatchFixtures.Context(plainBoard, TestMaps.P0), [BatchFixtures.P("E7", PieceType.Artisan)]);

        // 对照：同一落点不带栅栏时一个子都提不掉——被提的原因确实是那道栅栏。
        Assert.True(plain.IsLegal);
        Assert.Empty(plain.Captures);

        GameBoard board = Trapped();
        SettlementDriver driver = BatchFixtures.Driver(board);
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0);
        Placement[] batch = [BatchFixtures.Artisan("E7", TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("E6")))];

        RehearsalResult rehearsal = driver.Rehearse(context, batch);
        Assert.True(rehearsal.IsLegal, rehearsal.Failure?.Message);
        Assert.Equal(["E5"], rehearsal.Captures.Select(c => c.Coord).Notations());

        SettlementOutcome outcome = driver.Confirm(context, batch);
        Assert.True(outcome.Confirmed);
        Assert.Equal(["E5"], outcome.CaptureRecord!.Captured.Select(c => c.Coord).Notations());
        Assert.Null(board[TestMaps.At("E5")].Occupant);
        Assert.True(board.Map.HasFence(TestMaps.At("E5"), TestMaps.At("E6")));

        // 该栅栏记为"直接导致提子"（口径：去掉它后提子集合严格变小）。
        AppliedTerrainEdit applied = Assert.Single(outcome.CaptureRecord.Edits);
        Assert.True(applied.CausedCapture);
        Assert.Equal(TestMaps.At("E7"), applied.ArtisanCoord);
    }

    [Fact]
    public void 立栅切断敌串连接后各自算气()
    {
        // terrain-edit「立栅切断敌串连接」：P1 的 E5、E6 本是一串（合起来有气）。
        // 匠人落 E7 给 E5–E6 立栅后二者分属两串：E5 单独 0 气被提，E6 单独仍有 D6 / F6 两口气。
        GameBoard plainBoard = Chained();
        plainBoard.Place(TestMaps.At("E7"), TestMaps.P0, PieceType.Artisan);
        Group whole = plainBoard.GroupAt(TestMaps.At("E5"))!;
        Assert.Equal(["E5", "E6"], whole.Stones.Notations());
        Assert.Equal(["D6", "F6"], plainBoard.LibertiesOf(whole).Notations());

        GameBoard board = Chained();
        SettlementDriver driver = BatchFixtures.Driver(board);
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0);
        Placement[] batch = [BatchFixtures.Artisan("E7", TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("E6")))];

        SettlementOutcome outcome = driver.Confirm(context, batch);

        Assert.True(outcome.Confirmed);
        Assert.Equal(["E5"], outcome.CaptureRecord!.Captured.Select(c => c.Coord).Notations());
        Assert.True(outcome.CaptureRecord.Edits.Single().CausedCapture);

        // E6 独立成串并独立算气：栅栏挡住 E5 方向，但 E5 空出来后从 E6 看仍不是气（中间有栅栏）。
        Group rest = board.GroupAt(TestMaps.At("E6"))!;
        Assert.Equal(["E6"], rest.Stones.Notations());
        Assert.Equal(["D6", "F6"], board.LibertiesOf(rest).Notations());
        Assert.DoesNotContain(TestMaps.At("E5"), board.LibertyNeighbors(TestMaps.At("E6")));
    }

    [Fact]
    public void 外圈立栅把自己堵死也算自杀手()
    {
        // terrain-edit「改造把自己堵死」的外圈变体（T-11）：匠人 E7 给**己方**孤子 E5 与它唯一的气 E6 之间立栅，
        // 本批没有提走任何敌串 → 整批判自杀手。旧口径下这条边不合法，本用例证明放宽后自杀判定同样跟着放宽。
        GameBoard Setup()
        {
            GameBoard b = TestMaps.Blank(size: 9);
            b.Place(TestMaps.At("E5"), TestMaps.P0, PieceType.Basic);
            foreach (string cell in new[] { "E4", "D5", "F5" })
            {
                b.Place(TestMaps.At(cell), TestMaps.P1, PieceType.Basic);
            }

            return b;
        }

        GameBoard plain = Setup();
        Assert.True(BatchFixtures.Driver(plain).Rehearse(
            BatchFixtures.Context(plain, TestMaps.P0), [BatchFixtures.P("E7", PieceType.Artisan)]).IsLegal);

        GameBoard board = Setup();
        SettlementDriver driver = BatchFixtures.Driver(board);
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0);
        Placement[] batch = [BatchFixtures.Artisan("E7", TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("E6")))];

        RehearsalResult rehearsal = driver.Rehearse(context, batch);

        Assert.False(rehearsal.IsLegal);
        Assert.Equal(BatchFailureKind.Suicide, rehearsal.Failure!.Kind);
        Assert.Equal(["E5"], rehearsal.Failure.Coords.Notations());

        Assert.False(driver.Confirm(context, batch).Confirmed);
        Assert.Empty(board.TerrainEdits);
    }

    /// <summary>9×9：P1 单子 E5 被 P0 的 E4 / D5 / F5 围住，只剩 E5–E6 一条气边。</summary>
    private static GameBoard Trapped()
    {
        GameBoard board = TestMaps.Blank(size: 9);
        board.Place(TestMaps.At("E5"), TestMaps.P1, PieceType.Basic);
        board.Place(TestMaps.At("E4"), TestMaps.P0, PieceType.Basic);
        board.Place(TestMaps.At("D5"), TestMaps.P0, PieceType.Basic);
        board.Place(TestMaps.At("F5"), TestMaps.P0, PieceType.Basic);
        return board;
    }

    /// <summary>9×9：<see cref="Trapped"/> 之上 P1 在 E6 再落一子，与 E5 连成一串；该串的气只剩 D6 / F6 / E7。</summary>
    private static GameBoard Chained()
    {
        GameBoard board = Trapped();
        board.Place(TestMaps.At("E6"), TestMaps.P1, PieceType.Basic);
        return board;
    }

    [Fact]
    public void 多个改造同时生效且与顺序无关()
    {
        // terrain-edit「多个改造同时生效」：同一批次内的多个改造同时生效，结果 MUST NOT 依赖应用顺序。
        GameBoard Setup() => TestMaps.Blank(
            TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater), ("F4", Surface.Forest)]), size: 9);

        Placement[] forward =
        [
            BatchFixtures.Artisan("C4", TerrainEdit.Bridge(TestMaps.At("D4"))),
            BatchFixtures.Artisan("E4", TerrainEdit.Burn(TestMaps.At("F4"))),
            BatchFixtures.Artisan("B7", TerrainEdit.Fence(TestMaps.At("B7"), TestMaps.At("B8"))),
        ];
        Placement[] backward = [forward[2], forward[1], forward[0]];

        GameBoard a = Setup();
        SettlementOutcome outA = BatchFixtures.Driver(a).Confirm(BatchFixtures.Context(a, TestMaps.P0), forward);
        GameBoard b = Setup();
        SettlementOutcome outB = BatchFixtures.Driver(b).Confirm(BatchFixtures.Context(b, TestMaps.P0), backward);

        Assert.True(outA.Confirmed);
        Assert.True(outB.Confirmed);

        // 盘面（含改造段）逐字节相等，且每一格的气边与覆盖都相同。
        Assert.Equal(a.Serialize(), b.Serialize());
        foreach (Coord c in a.AllCoords())
        {
            Assert.Equal(a.LibertyNeighbors(c).Notations(), b.LibertyNeighbors(c).Notations());
            Assert.Equal(a.CoverageTargets(c).Notations(), b.CoverageTargets(c).Notations());
        }

        Assert.Equal(3, outA.CaptureRecord!.Edits.Length);
    }

    [Fact]
    public void 两道栅栏合围时两条都记致提子()
    {
        // terrain-edit「多个改造同时生效」：两枚匠人各立一道栅栏，两道栅栏共同使 P1 的 E5 无气。
        // "直接导致提子"的口径：把这一条改造去掉后提子集合**严格变小**才算 true——
        // 两道栅栏各自都是必要的，因此两条都记 true；顺带架的桥记 false。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("B8", Surface.DeepWater)]), size: 9);
        board.Place(TestMaps.At("E5"), TestMaps.P1, PieceType.Basic);
        board.Place(TestMaps.At("D5"), TestMaps.P0, PieceType.Basic);
        board.Place(TestMaps.At("F5"), TestMaps.P0, PieceType.Basic);

        SettlementDriver driver = BatchFixtures.Driver(board);
        Placement[] batch =
        [
            BatchFixtures.Artisan("E3", TerrainEdit.Fence(TestMaps.At("E4"), TestMaps.At("E5"))),
            BatchFixtures.Artisan("E7", TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("E6"))),
            BatchFixtures.Artisan("B7", TerrainEdit.Bridge(TestMaps.At("B8"))),
        ];

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), batch);

        Assert.True(outcome.Confirmed);
        Assert.Equal(["E5"], outcome.CaptureRecord!.Captured.Select(c => c.Coord).Notations());

        ImmutableArray<AppliedTerrainEdit> edits = outcome.CaptureRecord.Edits;
        Assert.Equal(3, edits.Length);
        Assert.Equal(
            ["E3", "E7"],
            edits.Where(e => e.CausedCapture).Select(e => e.ArtisanCoord).Order().Notations());
        Assert.False(edits.Single(e => e.Edit.Kind == TerrainEditKind.Bridge).CausedCapture);

        // 改造方留痕仍然完整（日志要用；公开视图不显示）。
        Assert.Equal(["E3", "B7", "E7"], edits.Select(e => e.ArtisanCoord).Order().Notations());
    }

    [Fact]
    public void 烧林后的揭示与控制()
    {
        // capture-resolution「烧林后的揭示与控制」：第 3 步改地表 → 覆盖关系立刻变化，
        // 第 5 步的揭示与第 6 步的控制判定都基于改造后的覆盖（揭示与控制本身由 relic-control 消费同一份覆盖）。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("F4", Surface.Forest)]), size: 9);
        Coord forest = TestMaps.At("F4");
        var hooks = new RecordingHooks { Board = board };
        var driver = new SettlementDriver(board, new BoardHistory(), hooks);

        Assert.DoesNotContain(forest, board.CoverageTargets(TestMaps.At("E4")));

        SettlementOutcome outcome = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0),
            [BatchFixtures.Artisan("E4", TerrainEdit.Burn(forest))]);

        Assert.True(outcome.Confirmed);
        Assert.Contains(forest, board.CoverageTargets(TestMaps.At("E4")));
        Assert.Equal(Surface.Grass, board.Map.SurfaceAt(forest));

        // 钩子（揭示 → 重算 → 终局检查）看到的盘面已经是改造后的地形。
        Assert.Equal(["DeductHand", "OnRevealRelics", "OnRecalculatePower", "OnCheckEndConditions"], hooks.Steps);
        Assert.All(hooks.Contexts, c => Assert.Equal(Surface.Grass, c.Board.Map.SurfaceAt(forest)));
    }

    [Fact]
    public void 预演不污染正式盘面的地形()
    {
        // capture-resolution「预演不污染正式盘面」：预演在副本上应用改造，正式盘面与地形的序列化结果不变。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater)]), size: 9);
        SettlementDriver driver = BatchFixtures.Driver(board);
        string before = board.Serialize();

        for (int i = 0; i < 5; i++)
        {
            driver.Rehearse(BatchFixtures.Context(board, TestMaps.P0),
                [BatchFixtures.Artisan("C4", TerrainEdit.Bridge(TestMaps.At("D4")))]);
        }

        Assert.Equal(before, board.Serialize());
        Assert.False(board.Map.HasBridge(TestMaps.At("D4")));
        Assert.Empty(board.TerrainEdits);
    }
}
