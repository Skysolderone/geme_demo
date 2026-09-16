using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Style;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// 规格：tactical-layers —— Requirement: 盘面层的读法切换（change `merge-board-layer`）与「五种战术信息层」里两种读法的集合关系（change `terrain-model` D-G）。
/// </summary>
public class 盘面层的读法切换Tests
{
    [Fact]
    public void 点击模式下三态循环()
    {
        // 规格 Scenario「点击模式下三态循环」：从尚未切换过读法的初始状态连按三次 → 归属、棋串、默认棋盘。
        var state = new TacticalLayerState(LayerInputMode.ClickToToggle);
        Assert.Null(state.Active);
        Assert.Equal(BoardReading.Ownership, state.Reading);

        state.Click(TacticalLayer.Board);
        Assert.Equal(TacticalLayer.Board, state.Active);
        Assert.Equal(BoardReading.Ownership, state.Reading);

        state.Click(TacticalLayer.Board);
        Assert.Equal(TacticalLayer.Board, state.Active);
        Assert.Equal(BoardReading.Groups, state.Reading);

        state.Click(TacticalLayer.Board);
        Assert.Null(state.Active);
    }

    [Fact]
    public void 第四次按下打开的是关闭时的读法()
    {
        // 规格 Scenario「第四次按下打开的是关闭时的读法」（D6）：关闭不重置读法，所以循环不闭合。
        // 这条同时钉住三态判据不能写成"当前是不是归属读法"——那样上次停在棋串时会退化成两态。
        var state = new TacticalLayerState(LayerInputMode.ClickToToggle);
        state.Click(TacticalLayer.Board);
        state.Click(TacticalLayer.Board);
        state.Click(TacticalLayer.Board);
        Assert.Null(state.Active);
        Assert.Equal(BoardReading.Groups, state.Reading);

        state.Click(TacticalLayer.Board);
        Assert.Equal(TacticalLayer.Board, state.Active);
        Assert.Equal(BoardReading.Groups, state.Reading);

        // 从棋串读法起始，这一轮仍是完整三态：棋串 → 归属 → 关闭。退化成两态的话这里第二次就关了。
        state.Click(TacticalLayer.Board);
        Assert.Equal(TacticalLayer.Board, state.Active);
        Assert.Equal(BoardReading.Ownership, state.Reading);

        state.Click(TacticalLayer.Board);
        Assert.Null(state.Active);
    }

    [Fact]
    public void 按住模式下读法不被层键改变()
    {
        // 规格 Scenario「按住模式下读法不被层键改变」（D3）：按住期间没有"再按一次"，
        // 若把读法推进绑在 Press 上，按住显示的玩家每按一次都换一种读法，等于被锁死在交替里。
        var state = new TacticalLayerState(LayerInputMode.HoldToShow);
        state.CycleReading();
        Assert.Equal(BoardReading.Groups, state.Reading);

        state.Press(TacticalLayer.Board);
        Assert.Equal(TacticalLayer.Board, state.Active);
        Assert.Equal(BoardReading.Groups, state.Reading);

        state.Release(TacticalLayer.Board);
        Assert.Null(state.Active);
        Assert.Equal(BoardReading.Groups, state.Reading);

        state.Press(TacticalLayer.Board);
        Assert.Equal(BoardReading.Groups, state.Reading);
    }

    [Fact]
    public void 读法跨越关闭被记住()
    {
        // 规格 Scenario「读法跨越关闭被记住」（D4）。用 Back 关闭，避开三态循环那条路径。
        var state = new TacticalLayerState(LayerInputMode.ClickToToggle);
        state.Click(TacticalLayer.Board);
        state.CycleReading();
        Assert.Equal(BoardReading.Groups, state.Reading);

        state.Back();
        Assert.Null(state.Active);
        Assert.Equal(BoardReading.Groups, state.Reading);

        state.Click(TacticalLayer.Board);
        Assert.Equal(BoardReading.Groups, state.Reading);

        // 切输入模式也不重置读法——SetMode 只回到默认棋盘。
        state.SetMode(LayerInputMode.HoldToShow);
        Assert.Null(state.Active);
        Assert.Equal(BoardReading.Groups, state.Reading);
    }

    [Fact]
    public void 切换读法不影响其它层()
    {
        // 规格 Scenario「切换读法不影响其它层」：读法是盘面层内部的维度，不参与层之间的互斥。
        var state = new TacticalLayerState(LayerInputMode.ClickToToggle);
        state.Click(TacticalLayer.Power);
        Assert.Equal(TacticalLayer.Power, state.Active);

        state.CycleReading();
        Assert.Equal(TacticalLayer.Power, state.Active);
        Assert.Equal(BoardReading.Groups, state.Reading);

        state.CycleReading();
        Assert.Equal(TacticalLayer.Power, state.Active);
        Assert.Equal(BoardReading.Ownership, state.Reading);
    }

    [Fact]
    public void 两种读法的场景处理不同()
    {
        // 归属读法弱化棋子演出（看格子归属），棋串读法必须保持棋子清晰（棋串轮廓画在棋子上）。
        // 合并成一个值就会让其中一种读法看不清自己要看的东西。
        SceneTreatment ownership = LayerVisuals.For(TacticalLayer.Board, BoardReading.Ownership);
        SceneTreatment groups = LayerVisuals.For(TacticalLayer.Board, BoardReading.Groups);

        Assert.True(ownership.PieceEmphasisPercent < 100, $"归属读法应弱化棋子，实际 {ownership.PieceEmphasisPercent}");
        Assert.Equal(100, groups.PieceEmphasisPercent);
        Assert.NotEqual(ownership, groups);
    }

    [Fact]
    public void 平地上两种读法点亮同一批空格()
    {
        // 规格 Scenario「平地上两种读法点亮同一批空格」（terrain-model D-G 改写 merge-board-layer 的"两种读法点亮同一批空格"）：
        // 没有崖壁、林地、栅栏与深水的区域内，"被某方覆盖的空格"与"某条棋串的气"仍是同一批格子——
        // 覆盖关系与气边在平地上退化成同一条几何邻接。9×9 夹具是全平地，所以全盘成立，且视图模型报出的差集为空。
        // 集合在测试内独立取出后做双向差集，不调用实现自己的任何比较。
        MatchFlow match = AiFixtures.Round5()
            .Pieces(P0, PieceType.Basic, "D4", "D5", "E5")
            .Pieces(P1, PieceType.Basic, "F4", "G4")
            .Pieces(P2, PieceType.Basic, "C8", "D8")
            .Pieces(P3, PieceType.Basic, "H7");

        var ownership = (TerritoryLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Ownership);
        var groups = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);

        HashSet<Coord> covered = [.. ownership.Cells
            .Where(c => c.State is TerritoryState.Exclusive or TerritoryState.Contested)
            .Select(c => c.Coord)];
        HashSet<Coord> liberties = [.. groups.Groups.SelectMany(g => g.Liberties)];

        Assert.NotEmpty(covered);
        Assert.Empty(covered.Except(liberties));
        Assert.Empty(liberties.Except(covered));
        Assert.True(ownership.Diff.IsEmpty, Dump(ownership.Diff));
        Assert.Equal(Dump(ownership.Diff), Dump(groups.Diff));
    }

    [Fact]
    public void 差集可由地形解释()
    {
        // 规格 Scenario「差集可由地形解释」（terrain-model D-G）：同一盘面上被覆盖但不是任何棋串的气的空格，
        // 与提供覆盖的棋子之间存在崖壁、栅栏或一格深水中的至少一种；反向（是气但无人覆盖）只能是林地。
        // 四种地形各摆一处（都放在 9×9 夹具中段，避开四角出生区）：
        //   崖壁：P0 在 h=2 的 D5，D6 为 h=1 缓坡（给它一口气），C5 / E5 / D4 为 h=0 → 三格被居高临下覆盖却不是气；
        //   栅栏：P1 在 F6，F6–G6 有栅栏 → G6 被覆盖（栅栏不挡覆盖）却不是气；
        //   隔岸：P2 在 E2，E3 为未架桥深水 → 覆盖落到对岸 E4，E4 与 E2 不相邻自然不是气；
        //   林地：P3 在 B4，B5 为林地 → B5 是气（林地不挡气）却无人覆盖（B4 与 D5 互不相邻，两处互不干扰）。
        // 变异验证 M-C1：TacticalLayers.ReasonFor 去掉"来源不相邻 → 隔岸"分支 → 本测试红（E4 抛出"既无栅栏也非崖壁"）。
        // 变异验证 M-C2：CoverageMap.Compute 记录来源时把 Adjacent 写死为 true → 本测试红（同上）。
        // 变异验证 M-C4（check 阶段实做）：ReadingDiff 去掉"是气但无人覆盖 → 林地"分支（条件改 false）→ 本测试红（B5 抛"不是林地"）。
        MatchFlow match = TerrainMatch(TestMaps.Terrain(
            heights: [("D5", 2), ("D6", 1)],
            surfaces: [("E3", Surface.DeepWater), ("B5", Surface.Forest)],
            fences: [("F6", "G6")]))
            .Pieces(P0, PieceType.Basic, "D5")
            .Pieces(P1, PieceType.Basic, "F6")
            .Pieces(P2, PieceType.Basic, "E2")
            .Pieces(P3, PieceType.Basic, "B4");

        var ownership = (TerritoryLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Ownership);
        var groups = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);

        // 先在测试内独立算出两个集合的差，再与视图模型报出的差集逐格对上。
        HashSet<Coord> covered = [.. ownership.Cells
            .Where(c => c.State is TerritoryState.Exclusive or TerritoryState.Contested)
            .Select(c => c.Coord)];
        HashSet<Coord> liberties = [.. groups.Groups.SelectMany(g => g.Liberties)];
        // Coord 的字典序先行后列：第 4 行的 D4 / E4 排在第 5 行的 C5 / E5 之前。
        Assert.Equal(["D4", "E4", "C5", "E5", "G6"], covered.Except(liberties).Order().Notations());
        Assert.Equal(["B5"], liberties.Except(covered).Order().Notations());

        BoardReadingDiff diff = ownership.Diff;
        Assert.Equal(Dump(diff), Dump(groups.Diff));
        Assert.Equal(covered.Except(liberties).Order().Notations(), diff.CoveredNotLiberty.Select(d => d.Coord).Notations());
        Assert.Equal(["B5"], diff.LibertyNotCovered.Select(d => d.Coord).Notations());

        // 每一格都给得出原因，且原因与摆出的地形一一对应。
        Dictionary<string, TerrainReason[]> reasons = diff.CoveredNotLiberty.Concat(diff.LibertyNotCovered)
            .ToDictionary(d => d.Coord.ToNotation(), d => d.Reasons.ToArray());
        Assert.All(reasons.Values, r => Assert.NotEmpty(r));
        Assert.Equal([TerrainReason.Cliff], reasons["C5"]);
        Assert.Equal([TerrainReason.Cliff], reasons["D4"]);
        Assert.Equal([TerrainReason.Cliff], reasons["E5"]);
        Assert.Equal([TerrainReason.Fence], reasons["G6"]);
        Assert.Equal([TerrainReason.AcrossWater], reasons["E4"]);
        Assert.Equal([TerrainReason.Forest], reasons["B5"]);

        // 缓坡 D6：Δh = 1，既是气也被覆盖，不在差集里。
        Assert.Contains(TestMaps.At("D6"), covered);
        Assert.Contains(TestMaps.At("D6"), liberties);

        // Core 只读查询 CoverageMap.SourcesOf 的直接断言（段 C 唯一 Core 增量）：来源坐标与"是否几何相邻"一位由 Core 给出，
        // 隔岸 E4 的来源 E2 不相邻，崖壁 C5 / 栅栏 G6 的来源相邻；无人覆盖的林地 B5 没有来源。
        CoverageMap coverage = match.PublicWorldOf().View.Power!.Coverage;
        Assert.Equal(new[] { new CoverageSource(TestMaps.At("E2"), Adjacent: false) }, coverage.SourcesOf(TestMaps.At("E4")).ToArray());
        Assert.Equal(new[] { new CoverageSource(TestMaps.At("D5"), Adjacent: true) }, coverage.SourcesOf(TestMaps.At("C5")).ToArray());
        Assert.Equal(new[] { new CoverageSource(TestMaps.At("F6"), Adjacent: true) }, coverage.SourcesOf(TestMaps.At("G6")).ToArray());
        Assert.Empty(coverage.SourcesOf(TestMaps.At("B5")));
    }

    /// <summary>9×9 流程夹具地图换上指定地形后开到第 5 大回合（全图可落子）。地形要素须避开四角 3×3 出生区。</summary>
    private static MatchFlow TerrainMatch(TerrainData terrain)
    {
        MapData map = MatchFixtures.Map() with { TerrainData = terrain };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), MatchOptions.Immediate);
        foreach (PlayerId p in MatchFixtures.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        return match.AtRound(5, [P0, P1, P2, P3]);
    }

    [Fact]
    public void 归属读法另有棋串读法没有的格()
    {
        // 规格 Scenario「归属读法另有棋串读法没有的格」：占据格与中立格在棋串读法里不被标出。
        // 这条挡的是"合并时顺手把归属读法削成气的子集"——那样合并就变成了删功能。
        MatchFlow match = AiFixtures.Round5()
            .Pieces(P0, PieceType.Basic, "D4", "D5", "E5")
            .Pieces(P1, PieceType.Basic, "F4", "G4");

        var ownership = (TerritoryLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Ownership);
        var groups = (LibertyLayerContent)match.World(P0).Layer(TacticalLayer.Board, BoardReading.Groups);

        HashSet<Coord> liberties = [.. groups.Groups.SelectMany(g => g.Liberties)];
        HashSet<Coord> occupied = [.. ownership.Cells.Where(c => c.State == TerritoryState.Occupied).Select(c => c.Coord)];
        HashSet<Coord> neutral = [.. ownership.Cells.Where(c => c.State == TerritoryState.Neutral).Select(c => c.Coord)];

        Assert.Equal(5, occupied.Count);
        Assert.NotEmpty(neutral);
        Assert.Empty(occupied.Intersect(liberties));
        Assert.Empty(neutral.Intersect(liberties));

        // 棋串读法里的棋子是"轮廓"，与归属读法的占据格是同一批——合并前后都不该丢。
        HashSet<Coord> stones = [.. groups.Groups.SelectMany(g => g.Stones)];
        Assert.True(occupied.SetEquals(stones), $"占据 {occupied.Count} 格，棋串轮廓 {stones.Count} 子");
    }
}
