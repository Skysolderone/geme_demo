using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Style;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>规格：tactical-layers —— Requirement: 盘面层的读法切换（change `merge-board-layer`）</summary>
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
    public void 两种读法点亮同一批空格()
    {
        // 规格 Scenario「两种读法点亮同一批空格」——本次合并的全部依据。
        // 覆盖 = 棋子的四邻接相邻格，气 = 棋串的空邻格，所以"被某方覆盖的空格"就是"某条棋串的气"。
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
