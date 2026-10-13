using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.Show;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/settlement-show —— Requirement: 棋盘展开节拍（段 F 拼接版：对方棋盘滑入贴合 → 相机重定界 → 对方盘面显现 → 横幅"棋盘拼接"，裁决 J5）。
/// 节拍由 <see cref="LinkShowBeats.Expansion"/> 从本机结算前后两份按座位过滤的默认棋盘与布局换算 <see cref="LinkLayout"/> 纯函数生成；时间线与遮罩沿用 <see cref="ShowTimeline"/>。
/// </summary>
/// <remarks>
/// board-link-mode 裁决 J8-9：F.4 恢复「首轮链接演出_四个节拍依次」「他人链接不演出」两条（原 Skip）。
/// 变异验证（段 F.4 实跑）：MF4-1 <c>Expansion</c> 不经布局换算、直接按坐标差分 → 红 3（含「他人链接不演出」：本机棋盘被收拢平移，平移被误判为新增）；
/// MF4-2 <c>Expansion</c> 锚点 / 被贴方判反（滑本机而非对方）→ 「首轮链接演出_四个节拍依次」「本机是被贴方_本机棋盘滑过去贴合」红 2；
/// MF4-3 <c>SettlementBeats</c> 不用拼接映射 → 「拼接那一回合的结算节拍不把平移当成落子与提子」红 1。
/// </remarks>
public class 棋盘展开节拍Tests
{
    /// <summary>
    /// 四人四区、脚本配对 (0,1)(3,4)：第 3 大回合结束拼接。返回链接前后 <paramref name="viewer"/> 的默认棋盘、新同战场玩家与布局换算（版本 0 → 2）。
    /// </summary>
    private static (DefaultBoardView Before, DefaultBoardView After, ImmutableArray<PlayerId> Opponents, MatchFlow Match, LinkLayout Layout) LinkedFor(PlayerId viewer, int[]? zones = null)
    {
        MatchFlow match = Started(zones ?? [0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(2);
        DefaultBoardView before = match.World(viewer).Board();
        ImmutableArray<PlayerId> matesBefore = match.World(viewer).SameBattlefieldPlayers;
        match.PassRound();
        DefaultBoardView after = match.World(viewer).Board();
        ImmutableArray<PlayerId> opponents = [.. match.World(viewer).SameBattlefieldPlayers.Where(p => !matesBefore.Contains(p))];
        return (before, after, opponents, match, new LinkLayout(match.Board.BaseMap.Boards, match.Links.Select(l => l.ShiftStep)));
    }

    /// <summary>演出起点封顶（裁决 J12-2）：东贴沿 +x、北贴沿 +y，偏 min(拼接前两战场的相对偏移, 4) 格。</summary>
    private static (int Dx, int Dy) Capped(int relDx, int relDy, bool east)
    {
        int reach = Math.Min(Math.Max(Math.Abs(relDx), Math.Abs(relDy)), LinkShowBeats.SlideCap);
        return east ? (reach, 0) : (0, reach);
    }

    /// <summary>手搭的两块 5×5 出生棋盘与一条链接（0 + 1，1 号被贴方平移 <paramref name="moverShift"/>）的布局换算，以及 <paramref name="viewer"/> 号视角的拼接前后默认棋盘（只有格坐标有意义）。</summary>
    private static (DefaultBoardView Before, DefaultBoardView After, LinkLayout Layout) Synthetic(Coord moverOrigin, (int Dx, int Dy) moverShift, int viewer)
    {
        BoardPlate[] plates = [new(new Coord(2, 2), 5, 5, BoardPlateKind.Birth), new(moverOrigin, 5, 5, BoardPlateKind.Birth)];
        var layout = new LinkLayout(plates, [new LinkShiftStep(0, 1, [new BattlefieldShift(0, 0, 0), new BattlefieldShift(1, moverShift.Dx, moverShift.Dy)])]);
        DefaultBoardView View(IEnumerable<Coord> coords) =>
            new(64, 64, [.. coords.Order().Select(c => new BoardCellView(c, default, 0, default, false, null, null, default, null, null))], [], [], []);
        DefaultBoardView before = View(plates[viewer].Cells());
        DefaultBoardView after = View(plates[0].Cells().Concat(layout.BoardAt(1, 1).Cells()));
        return (before, after, layout);
    }

    private static (int Dx, int Dy) Delta(LinkLayout layout, int zone)
    {
        Coord was = layout.BoardAt(zone, 0).Origin;
        Coord now = layout.BoardAt(zone, layout.Versions).Origin;
        return (now.X - was.X, now.Y - was.Y);
    }

    [Fact]
    public void 首轮链接演出_四个节拍依次()
    {
        // Scenario「首轮链接演出」：本机 P0（出生棋盘 0，锚点）与 P1（出生棋盘 1）拼接 → 对方棋盘滑入贴合 → 相机重定界 → 对方盘面显现 → 横幅"棋盘拼接：与 X 交战"。
        (DefaultBoardView before, DefaultBoardView after, ImmutableArray<PlayerId> opponents, MatchFlow match, LinkLayout layout) = LinkedFor(P0);
        Assert.Equal(2, match.Links.Count);
        Assert.Equal([P1], opponents);

        ImmutableArray<SettlementBeat> beats = LinkShowBeats.Expansion(before, after, opponents, layout, 0);

        Assert.Equal(4, beats.Length);
        var slide = Assert.IsType<BoardSlideBeat>(beats[0]);
        var pull = Assert.IsType<CameraPullBeat>(beats[1]);
        var reveal = Assert.IsType<OpponentRevealBeat>(beats[2]);
        var banner = Assert.IsType<BannerBeat>(beats[3]);

        // 滑的是对方那块（出生棋盘 1，拼接后坐标）。
        // board-link-mode F.6：原断言"起点 = 拼接前相对本机的位置（本机位移 − 对方位移）"改为裁决 J12-2 的封顶口径——首轮东贴，从东侧偏 min(相对偏移, 4) 格滑入。
        HashSet<Coord> theirs = [.. match.Map.BirthZones[1]];
        Assert.Equal(theirs.Order(), slide.Cells.Order());
        (int myDx, int myDy) = Delta(layout, 0);
        (int theirDx, int theirDy) = Delta(layout, 1);
        Assert.Equal(Capped(myDx - theirDx, myDy - theirDy, east: true), (slide.FromDx, slide.FromDy));
        Assert.NotEqual((0, 0), (slide.FromDx, slide.FromDy));

        Assert.Equal(theirs.Order(), reveal.Cells.Order());                                       // 显现的是对方那块出生棋盘
        Assert.Equal(before.Cells.Length + theirs.Count, after.Cells.Length);                    // 拼接不增不减格：两块之和
        Assert.Equal(CoordRect.Of(after.Cells.Select(c => c.Coord))!.Value, pull.Target);        // 相机目标 = 合并后战场包围盒
        Assert.Equal(["棋盘拼接：与 蓝方(P1) 交战"], banner.Banners);

        int total = beats.Sum(b => b.DurationMs);
        Assert.Equal(LinkShowBeats.ExpansionTotalMs, total);
        Assert.Equal(900 + 500 + 600 + BannerBeat.PerBannerMs, total);                           // 裁决 J5：滑入约 900 ms、重定界约 500 ms、显现约 600 ms
        Assert.Equal(total, new ShowTimeline(beats, ShowDuration.Normal).TotalDurationMs);
    }

    [Fact]
    public void 本机是被贴方_本机棋盘滑过去贴合()
    {
        // 裁决 J5：本机自己是被贴方时，演出为"本机棋盘滑过去贴合"（相机随后的重定界跟过去）；对方（锚点）盘面照样显现。
        (DefaultBoardView before, DefaultBoardView after, ImmutableArray<PlayerId> opponents, MatchFlow match, LinkLayout layout) = LinkedFor(P1);
        ImmutableArray<SettlementBeat> beats = LinkShowBeats.Expansion(before, after, opponents, layout, 0);
        var slide = Assert.IsType<BoardSlideBeat>(beats[0]);
        var reveal = Assert.IsType<OpponentRevealBeat>(beats[2]);
        Assert.Equal(match.Map.BirthZones[1].Order(), slide.Cells.Order());
        Assert.Equal(match.Map.BirthZones[0].Order(), reveal.Cells.Order());
        (int myDx, int myDy) = Delta(layout, 1);
        (int theirDx, int theirDy) = Delta(layout, 0);
        // board-link-mode F.6：原断言"起点 = 对方位移 − 本机位移"改为裁决 J12-2 的封顶口径——本机棋盘从东侧偏 min(相对偏移, 4) 格滑过去。
        Assert.Equal(Capped(theirDx - myDx, theirDy - myDy, east: true), (slide.FromDx, slide.FromDy));
        Assert.Equal([P0], opponents);
    }

    [Theory]
    [InlineData(38, 2, -31, 0, true, 4)]    // 东贴、相对偏移 31 → 演出起点只差 4 格
    [InlineData(10, 2, -3, 0, true, 3)]     // 东贴、相对偏移 3 → 按 3
    [InlineData(2, 40, 0, -33, false, 4)]   // 北贴、相对偏移 33 → 从北侧 4 格往南滑入
    [InlineData(2, 9, 0, -2, false, 2)]     // 北贴、相对偏移 2 → 按 2
    public void 滑入起点封顶四格_真实平移照旧(int moverX, int moverY, int shiftX, int shiftY, bool east, int reach)
    {
        // 裁决 J12-2：滑动方从"终点沿贴边反方向偏 min(实际偏移, 4) 格"的位置滑到终点；真实平移向量照旧在布局换算（链接记录）里，封顶只影响演出。
        foreach (int viewer in new[] { 0, 1 })
        {
            (DefaultBoardView before, DefaultBoardView after, LinkLayout layout) = Synthetic(new Coord(moverX, moverY), (shiftX, shiftY), viewer);
            var slide = Assert.IsType<BoardSlideBeat>(LinkShowBeats.Expansion(before, after, [P1], layout, 0)[0]);
            Assert.Equal(layout.BoardAt(1, 1).Cells().Order(), slide.Cells.Order());                // 锚点 0 视角滑对方、被贴方 1 视角滑本机：都是 1 号那块
            Assert.Equal(east ? (reach, 0) : (0, reach), (slide.FromDx, slide.FromDy));
            Assert.Equal(new Coord(moverX + shiftX, moverY + shiftY), layout.BoardAt(1, 1).Origin);   // 真实平移不封顶
        }
    }

    [Fact]
    public void 他人链接不演出()
    {
        // Scenario「他人链接不演出」：P0、P1 同在出生棋盘 0（不进首轮配对），P2、P3 的出生棋盘 3 + 4 拼接 → P0 的战场没有新增格，空序列；
        // 画面按新布局正确重搭：本机棋盘可能随收拢平移（J7 / J8-1），结算前的格经布局换算后与结算后的格逐格相同，本机占位原点即换算后的位置。
        (DefaultBoardView before, DefaultBoardView after, _, MatchFlow match, LinkLayout layout) = LinkedFor(P0, [0, 0, 3, 4]);
        Assert.Single(match.Links);

        Assert.Empty(LinkShowBeats.Expansion(before, after, [], layout, 0));
        Assert.Equal(before.Cells.Select(c => layout.Translate(c.Coord, 0, 1)).Order(), after.Cells.Select(c => c.Coord).Order());
        Assert.Equal(layout.BoardAt(0, 1).Origin, after.Placeholders.Single(p => p.Own).Origin);
        Assert.Equal(before.Cells.Select(c => c.Occupant), after.Cells.Select(c => c.Occupant));   // 逐格内容随平移不变（两边都按坐标序）

        // 样本口径：本机棋盘确实随这次他人链接平移了——否则按坐标直接差分也会是空序列，这条测试就守不住"平移不算新增"。
        Assert.NotEqual(layout.BoardAt(0, 0).Origin, layout.BoardAt(0, 1).Origin);
        // 反面：P2 的战场确实长出了格（拼入 4 号）。
        Assert.True(match.World(P2).Board().Cells.Length > Map.BirthZones[3].Count);
    }

    [Fact]
    public void 他人链接平移本机棋盘_布局指纹变_引擎层据此重搭()
    {
        // 裁决 J12-1 根因（board-link-mode F.6）：他人链接把本机战场收拢平移后，本机看得见的格的逐格属性序列（可落子 / 高度 / 地表 / 桥）不变，
        // 引擎层原先的地形指纹只看这串序列 → 不重搭、画面停在旧坐标、开局对准自检只见 23 / 25。布局指纹须带坐标、画布与占位。
        (DefaultBoardView before, DefaultBoardView after, _, _, LinkLayout layout) = LinkedFor(P0, [0, 0, 3, 4]);
        Assert.NotEqual(layout.BoardAt(0, 0).Origin, layout.BoardAt(0, 1).Origin);

        // 原失败局面：旧指纹（逐格属性 + 栅栏）前后相同。
        static string PropertiesOnly(DefaultBoardView v) =>
            string.Concat(v.Cells.Select(c => $"{(int)c.Terrain}{c.Height}{(int)c.Surface}{(c.HasBridge ? 1 : 0)};")) + "|" + string.Concat(v.Fences.Select(f => $"{f};"));
        Assert.Equal(PropertiesOnly(before), PropertiesOnly(after));

        Assert.NotEqual(before.LayoutKey(), after.LayoutKey());
        // 单看格坐标也要分得出（画布、棋盘清单、占位都不动，只把格整体东移 1 格）。
        DefaultBoardView shifted = before with { Cells = [.. before.Cells.Select(c => c with { Coord = new Coord(c.Coord.X + 1, c.Coord.Y) })] };
        Assert.NotEqual(before.LayoutKey(), shifted.LayoutKey());

        // 反面：没有链接的大回合（只 Pass）指纹不变——不会每回合重搭。
        MatchFlow quiet = Started([0, 0, 3, 4], new ScriptedPairingSource());
        string opening = quiet.World(P0).Board().LayoutKey();
        quiet.PassRound();
        Assert.Empty(quiet.Links);
        Assert.Equal(opening, quiet.World(P0).Board().LayoutKey());
    }

    [Fact]
    public void 遮罩_滑入前停在起点_对方格先隐藏后渐显_相机目标带进度()
    {
        (DefaultBoardView before, DefaultBoardView after, ImmutableArray<PlayerId> opponents, _, LinkLayout layout) = LinkedFor(P0);
        ImmutableArray<SettlementBeat> beats = LinkShowBeats.Expansion(before, after, opponents, layout, 0);
        var slide = (BoardSlideBeat)beats[0];
        var pull = (CameraPullBeat)beats[1];
        var reveal = (OpponentRevealBeat)beats[2];
        var timeline = new ShowTimeline(beats, ShowDuration.Normal);
        Coord one = slide.Cells[0];

        // 0 ms：滑动格停在起点（偏移 = 起点偏移 × 1000‰）、对方格棋子全部隐藏、相机未动；不在滑动里的格没有偏移。
        ShowMask mask = timeline.Mask();
        Assert.Equal((slide.FromDx * 1000, slide.FromDy * 1000), mask.SlideOffsetOf(one));
        Assert.Equal((0, 0), mask.SlideOffsetOf(before.Cells[0].Coord));
        Assert.All(reveal.Cells, c => Assert.Contains(c, mask.Hidden));
        Assert.Null(mask.CameraPull);
        Assert.False(mask.IsEmpty);

        // 滑入过半：偏移约剩一半。
        timeline.Advance(BoardSlideBeat.Ms / 2);
        mask = timeline.Mask();
        (int dx, int dy) = mask.SlideOffsetOf(one);
        Assert.InRange(dx, Math.Min(slide.FromDx * 400, slide.FromDx * 600), Math.Max(slide.FromDx * 400, slide.FromDx * 600));
        Assert.InRange(dy, Math.Min(slide.FromDy * 400, slide.FromDy * 600), Math.Max(slide.FromDy * 400, slide.FromDy * 600));
        Assert.All(reveal.Cells, c => Assert.Contains(c, mask.Hidden));

        // 进入相机节拍：滑入已到位（不再列入）、相机目标带进度、对方格仍隐藏。
        timeline.Advance(BoardSlideBeat.Ms / 2 + CameraPullBeat.Ms / 2);
        mask = timeline.Mask();
        Assert.Null(mask.Slide);
        Assert.Equal((0, 0), mask.SlideOffsetOf(one));
        Assert.Equal(pull.Target, mask.CameraPull!.Value.Target);
        Assert.InRange(mask.CameraPull.Value.Permille, 400, 600);
        Assert.All(reveal.Cells, c => Assert.Contains(c, mask.Hidden));

        // 显现过半：对方格按进度出现。
        timeline.Advance(CameraPullBeat.Ms / 2 + OpponentRevealBeat.Ms / 2);
        mask = timeline.Mask();
        Assert.Null(mask.CameraPull);
        Assert.All(reveal.Cells, c => Assert.InRange(mask.Appearing[c], 400, 600));

        // 横幅。
        timeline.Advance(OpponentRevealBeat.Ms / 2 + BannerBeat.PerBannerMs / 2);
        mask = timeline.Mask();
        Assert.Equal(LinkShowBeats.ExpansionText(opponents), mask.Banner!.Text);
        Assert.Empty(mask.Hidden);
        Assert.Empty(mask.Appearing);

        timeline.Advance(BannerBeat.PerBannerMs);
        Assert.True(timeline.IsFinished);
        Assert.True(timeline.Mask().IsEmpty);
    }

    [Fact]
    public void 本机为被贴方时滑入期间相机跟随本机棋盘()
    {
        // board-link-mode F.4b / 裁决 J11-5：本机是被贴方，滑入期间注视点 = 滑入开始位姿 + 本机棋盘此刻的平面偏移（与滑入同一插值）；距离不变；
        // 本机是锚点（滑的是对方）时不跟随；滑入播完不再跟随。格心映射取一个线性的测试映射（x 向东 = +X、y 向北 = −Z，与引擎同口径）。
        // 变异 MF4b-1：FollowSlide 不加偏移（恒返回起点）→ 本测试红。
        (DefaultBoardView before, DefaultBoardView after, ImmutableArray<PlayerId> opponents, MatchFlow match, LinkLayout layout) = LinkedFor(P1);
        ImmutableArray<SettlementBeat> beats = LinkShowBeats.Expansion(before, after, opponents, layout, 0);
        var slide = (BoardSlideBeat)beats[0];
        HashSet<Coord> own = [.. match.Map.BirthZones[1]];
        static (float X, float Z) Plane(Coord c) => (c.X * 1f, -c.Y * 1f);
        var start = new Siege.Presentation.Camera.CameraPose(3f, -2f, 12f);
        var timeline = new ShowTimeline(beats, ShowDuration.Normal);

        Siege.Presentation.Camera.CameraPose atStart = Siege.Presentation.Camera.CameraPose.FollowSlide(start, timeline.Mask(), own, Plane)!.Value;
        Assert.Equal(start.FocusX + slide.FromDx, atStart.FocusX, 3);
        Assert.Equal(start.FocusZ - slide.FromDy, atStart.FocusZ, 3);
        Assert.Equal(start.Distance, atStart.Distance, 3);

        timeline.Advance(BoardSlideBeat.Ms / 2);
        Siege.Presentation.Camera.CameraPose atHalf = Siege.Presentation.Camera.CameraPose.FollowSlide(start, timeline.Mask(), own, Plane)!.Value;
        Assert.InRange(atHalf.FocusX - start.FocusX, Math.Min(slide.FromDx * 0.4f, slide.FromDx * 0.6f), Math.Max(slide.FromDx * 0.4f, slide.FromDx * 0.6f));
        Assert.NotEqual(0, slide.FromDx);

        timeline.Advance(BoardSlideBeat.Ms / 2 + 10);
        Assert.Null(Siege.Presentation.Camera.CameraPose.FollowSlide(start, timeline.Mask(), own, Plane));

        // 本机是锚点（P0 视角，滑的是对方）：不跟随。
        (DefaultBoardView b0, DefaultBoardView a0, ImmutableArray<PlayerId> o0, MatchFlow m0, LinkLayout l0) = LinkedFor(P0);
        ShowMask anchorMask = new ShowTimeline(LinkShowBeats.Expansion(b0, a0, o0, l0, 0), ShowDuration.Normal).Mask();
        Assert.Null(Siege.Presentation.Camera.CameraPose.FollowSlide(start, anchorMask, new HashSet<Coord>(m0.Map.BirthZones[0]), Plane));
    }

    [Fact]
    public void 无人值守零时长()
    {
        // Scenario「无人值守零时长」：节拍照常生成，时间线零时长创建即播完、遮罩为空。
        (DefaultBoardView before, DefaultBoardView after, ImmutableArray<PlayerId> opponents, _, LinkLayout layout) = LinkedFor(P0);
        ImmutableArray<SettlementBeat> beats = LinkShowBeats.Expansion(before, after, opponents, layout, 0);
        Assert.Equal(4, beats.Length);
        var timeline = new ShowTimeline(beats, ShowDuration.Zero);
        Assert.True(timeline.IsFinished);
        Assert.Equal(0, timeline.TotalDurationMs);
        Assert.True(timeline.Mask().IsEmpty);
    }

    [Fact]
    public void 提速照旧()
    {
        (DefaultBoardView before, DefaultBoardView after, ImmutableArray<PlayerId> opponents, _, LinkLayout layout) = LinkedFor(P0);
        var timeline = new ShowTimeline(LinkShowBeats.Expansion(before, after, opponents, layout, 0), ShowDuration.Normal);
        timeline.Advance(LinkShowBeats.ExpansionTotalMs / ShowTimeline.SpeedUpFactor, fast: true);
        Assert.True(timeline.IsFinished);
    }

    [Fact]
    public void 拼接那一回合的结算节拍不把平移当成落子与提子()
    {
        // 段 F.4：拼接发生在某小回合末尾（第 3 大回合最后一手之后），该小回合的结算节拍由前后两份全知快照之差得出；
        // 结算前的格先经布局换算到拼接后坐标再比——被平移的棋子不算"落子"也不算"被提"。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(2);
        match.Place(P1, BirthCell(match.Map, 1, 1, 1), BirthCell(match.Map, 1, 2, 2));
        match.Place(P3, BirthCell(match.Map, 4, 0, 0));
        while (match.ActionOrder.IndexOf(match.CurrentPlayer!.Value) < match.ActionOrder.Length - 1)
        {
            match.PassTurn();
        }

        MatchPublicView before = match.Publish();
        match.PassTurn();
        Assert.Equal(2, match.Links.Count);
        MatchPublicView after = match.Publish();
        var layout = new LinkLayout(match.Board.BaseMap.Boards, match.Links.Select(l => l.ShiftStep));

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, record: null, moved: c => layout.Translate(c, 0, 2));
        Assert.DoesNotContain(beats, b => b is PlacementBeat or CaptureBeat);

        // 反面：不给映射时，被平移的棋子会被误判（或越出结算前的画布而抛出）。
        bool misjudged;
        try
        {
            misjudged = SettlementBeats.Generate(before, after, record: null).Any(b => b is PlacementBeat or CaptureBeat);
        }
        catch (ArgumentOutOfRangeException)
        {
            misjudged = true;
        }

        Assert.True(misjudged);
    }
}
