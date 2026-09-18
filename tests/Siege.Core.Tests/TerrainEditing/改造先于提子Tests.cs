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
/// <para><b>待决 B-1</b>：规格场景「立栅导致提子」在现行规则下<b>不可实现</b>。
/// 立栅的边必有一端是匠人落点 A，而第 5 步算提子时 A 已被己方匠人占据；敌串的气是"(敌子 s, 空格 c) 的气边"，
/// s = A 不成立（A 是己子）、c = A 不成立（A 已占），栅栏也切不断敌串内部连接（A 不是敌子）。
/// 搭桥只新增空可落子格（只加气），烧林不改气边。因此<b>任何改造都不可能直接导致提子</b>。
/// 本文件因此把「立栅导致提子」换成结构性守门 <see cref="立栅不改变任何敌串的提子结果"/>：
/// 它红了就说明边目标口径被放宽，届时应改回规格场景。顺序敏感性由两条搭桥用例正面守住。</para>
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
    public void 立栅不改变任何敌串的提子结果()
    {
        // 待决 B-1 的结构性守门（替代不可实现的规格场景「立栅导致提子」）：
        // 立栅的边必有一端是匠人落点，而提子判定时该格已被己方棋子占据，因此栅栏碰不到敌串的气。
        // 穷举：三个局面 × 每个合法落点 × 该落点的每条合法栅栏，带 / 不带栅栏的提子集合必须完全相同。
        // 本条红 = 边目标口径被放宽（例如放宽到"至少一端是匠人的四邻格"），届时应改回规格场景。
        GameBoard[] boards = [Cornered(), Trapped(), Surrounded()];
        int fencesChecked = 0;
        foreach (GameBoard board in boards)
        {
            foreach (Coord cell in board.AllCoords().Where(c => board[c].IsPlayableEmpty))
            {
                foreach (TerrainEdit fence in TerrainEditRules.LegalTargets(board.Map, cell)
                             .Where(e => e.Kind == TerrainEditKind.Fence))
                {
                    GameBoard plain = board.Clone();
                    plain.Place(cell, TestMaps.P0, PieceType.Artisan);
                    GameBoard fenced = board.Clone();
                    fenced.Place(cell, TestMaps.P0, PieceType.Artisan);
                    fenced.ApplyTerrainEdits([fence]);

                    Assert.Equal(
                        CaptureResolver.FindCaptured(plain, TestMaps.P0).Select(s => s.Coord).Notations(),
                        CaptureResolver.FindCaptured(fenced, TestMaps.P0).Select(s => s.Coord).Notations());
                    fencesChecked++;
                }
            }
        }

        // 样本口径下界：确实检了大量栅栏，也确实有局面本来就会提子（不是"两边恒为空集"的假绿）。
        Assert.True(fencesChecked > 100, $"只检了 {fencesChecked} 条栅栏，样本太小。");
        Assert.NotEmpty(CaptureResolver.FindCaptured(Placed(Cornered(), "E5"), TestMaps.P0));

        static GameBoard Placed(GameBoard board, string cell)
        {
            board.Place(TestMaps.At(cell), TestMaps.P0, PieceType.Artisan);
            return board;
        }
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

    /// <summary>9×9 带林地与深水：三种动作都有合法目标，供穷举用。</summary>
    private static GameBoard Surrounded() => TestMaps.Blank(
        TestMaps.Terrain(
            surfaces: [("D4", Surface.DeepWater), ("D5", Surface.DeepWater), ("F4", Surface.Forest)],
            fences: [("G6", "H6")]),
        size: 9);

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
    public void 本批改造一律不记致提子()
    {
        // "直接导致提子"的口径：把这一条改造去掉后提子集合**严格变小**才算 true。
        // 待决 B-1：现行规则下没有任何改造能做到这一点，因此本条钉的是"恒为 false"——
        // 规则一旦放宽（立栅能敲敌串的气），本条会红，那时它就是提醒改口径的信号。
        GameBoard board = Cornered();
        SettlementDriver driver = BatchFixtures.Driver(board);
        Placement[] batch =
        [
            BatchFixtures.Artisan("E5", TerrainEdit.Bridge(TestMaps.At("E6"))),
            BatchFixtures.Artisan("B7", TerrainEdit.Fence(TestMaps.At("B7"), TestMaps.At("B8"))),
        ];

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), batch);

        Assert.True(outcome.Confirmed);
        ImmutableArray<AppliedTerrainEdit> edits = outcome.CaptureRecord!.Edits;
        Assert.Equal(2, edits.Length);
        Assert.All(edits, e => Assert.False(e.CausedCapture));

        // 改造方留痕仍然完整（日志要用；公开视图不显示）。
        Assert.Equal(["E5", "B7"], edits.Select(e => e.ArtisanCoord).Order().Notations());
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
