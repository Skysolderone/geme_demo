using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Sim.Play;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 开局信息公开范围</summary>
public class 开局信息公开范围Tests
{
    [Fact]
    public void 插旗阶段的可见信息()
    {
        // 设计文档 §4.1：地形、出生区、信物格位置公开；信物具体内容隐藏。
        // 变异验证 M-S1：RelicState.ToPublic 无条件带出 Content → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Create(relics: [("E5", RelicFixtures.Command()), ("B2", RelicFixtures.Vanguard())]);
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);

        MapData map = match.Flags.Map;
        Assert.Equal(4, map.BirthZones.Length);
        Assert.Equal(new[] { "B2", "E5" }, map.RelicCells.Keys.Order().Notations());
        Assert.Equal(Terrain.Playable, map.TerrainAt(TestMaps.At("E5")));

        foreach (RelicPublicState state in match.Relics.PublicStates())
        {
            Assert.False(state.IsRevealed);
            Assert.Null(state.Content);
        }

        // 公开快照里没有任何"信物内容"字段以外的路径：RelicPublicState 是唯一出口，未揭示时 Content 为 null。
        Assert.Equal(2, match.Publish().Relics.Length);
        Assert.All(match.Publish().Relics, r => Assert.Null(r.Content));
    }

    [Fact]
    public void 占位尺寸取整块棋盘()
    {
        // match-setup Scenario「占位尺寸取整块棋盘」（link-map-terrain 裁决 M3 / D1）：出生棋盘宽 8、高 6 → 占位 8×6，左下角与棋盘外接矩形相同，而不是 3×3 的核心区；占位带主题。
        // 夹具的出生区手写成核心区（与"取出生区"的旧实现区分开）。
        // 变异验证（link-map-terrain 1.4 实跑，只跑本类）：M-P1 SeatPublicView 链接模式的占位改回取出生区外接矩形 → 本测试红 1。
        MapData map = CoreZoneMap();
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), LinkModeFixtures.Options);
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);

        SeatPublicView view = match.PublishFor(MatchFixtures.P0);
        Assert.Equal(5, view.Placeholders.Length);
        BoardPlaceholder wide = view.Placeholders[1];
        Assert.Equal((1, "L3", 8, 6, (BoardTheme?)BoardTheme.River), (wide.Zone, wide.Origin, wide.Width, wide.Height, wide.Theme));
        Assert.All(view.Placeholders.Where(p => p.Zone != 1), p => Assert.Equal((5, 5), (p.Width, p.Height)));
        Assert.Equal(Themes.Select(t => (BoardTheme?)t), view.Placeholders.Select(p => p.Theme));

        // 样本口径：出生区 2 确是 3×3 核心区（西起第 3–5 列、南起第 2–4 行），不含棋盘左下角。
        Assert.Equal(9, map.BirthZones[1].Count);
        Assert.Equal(new Coord(12, 3), new Coord(map.BirthZones[1].Min(c => c.X), map.BirthZones[1].Min(c => c.Y)));
        Assert.DoesNotContain(map.Boards[1].Origin, map.BirthZones[1]);
    }

    [Fact]
    public void 锁定后只见自己的棋盘()
    {
        // match-setup Scenario「锁定后只见自己的棋盘」：锁定棋盘 2 → 该玩家可见这块棋盘的全部格（整块 8×6），不只是出生区（核心区）。
        // 变异验证（link-map-terrain 1.4 实跑，只跑本类）：M-P2 SeatPublicView 插旗阶段的可见格改回取出生区 → 本测试红 1。
        MapData map = CoreZoneMap();
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), LinkModeFixtures.Options);
        match.Flags.Plant(MatchFixtures.P0, 1);

        SeatPublicView locked = match.PublishFor(MatchFixtures.P0);
        Assert.Equal(48, locked.Cells.Length);
        Assert.Equal(map.Boards[1].Cells().Order().Select(c => c.ToNotation()), locked.Cells.Select(c => c.Coord));
        Assert.Empty(match.PublishFor(MatchFixtures.P1).Cells);   // 反面：未锁定者看不到任何格
    }

    [Fact]
    public void 终端插旗占位画整块棋盘()
    {
        // 终端版（play）插旗阶段的占位画整块出生棋盘：8×6 的棋盘 2 共 48 格都标"2"，最南一行（第 3 行）与最北一行（第 8 行）各 8 格——
        // 出生区（核心区）只在第 4–6 行的 3 列；期望由夹具几何手工写出（不经渲染器）。
        // 变异验证（link-map-terrain 1.4 实跑，只跑本类）：M-P3 BoardRenderer 占位格改回 map.BirthZoneOf → 本测试红 1；
        // M-P4 BoardRenderer.Viewport 插旗取景改回出生区之并 → 本测试红 1。
        MapData map = CoreZoneMap();
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), LinkModeFixtures.Options);
        var output = new StringWriter();

        new BoardRenderer(output).Board(match.Publish(), MatchFixtures.P0, zones: true);

        string[] lines = [.. output.ToString().Split('\n').Select(l => l.TrimEnd('\r'))];
        string[] rows = [.. lines.Where(l => l.Length > 4 && char.IsDigit(l[2]) && l[3] == ' ')];
        int CountIn(string row, string glyph) => Enumerable.Range(0, (row.Length - 4) / 3).Count(i => row.Substring(4 + (i * 3), 3) == glyph);
        Assert.Equal(8, CountIn(rows.Single(l => l.StartsWith("  3 ", StringComparison.Ordinal)), " 2 "));
        Assert.Equal(8, CountIn(rows.Single(l => l.StartsWith("  8 ", StringComparison.Ordinal)), " 2 "));
        Assert.Equal(48, rows.Sum(r => CountIn(r, " 2 ")));
        Assert.Equal(25, rows.Sum(r => CountIn(r, " 1 ")));
    }

    [Fact]
    public void 插旗阶段的可见信息_图形版占位带主题名()
    {
        // match-setup Scenario「插旗阶段的可见信息」（link-map-terrain 6.1 / 裁决 M3）在图形版的落点：默认棋盘视图模型的每个占位给出主题中文名，
        // 唯一来源 BoardThemeNames.DisplayName；Godot 只把它画在占位编号下方（源码守门见 占位轮廓灰度Tests.占位主题名只经视图模型取用_Godot不另写一份）。
        // 插旗阶段看不到任何格，主题名是占位上唯一的布局线索。
        // 变异 MLT-G1（BoardPlaceholderView.ThemeName 恒返回 null，实跑）→ 红 2（本测试、选图视图模型Tests.新局界面背景占位不带主题名_其余与插旗阶段相同 的样本口径）。
        MatchFlow match = LinkModeFixtures.Create();
        Siege.Presentation.Visibility.DefaultBoardView board = match.World(MatchFixtures.P0).Board();

        Assert.Empty(board.Cells);
        Assert.Equal(5, board.Placeholders.Length);
        foreach (Siege.Presentation.Visibility.BoardPlaceholderView p in board.Placeholders)
        {
            Assert.Equal(LinkModeFixtures.Map.BirthBoard(p.Zone).Theme!.Value.DisplayName(), p.ThemeName);
        }

        // 五块主题两两不同，恰是五个主题的中文名（测试内字面量，不经 DisplayName 反推）。
        Assert.Equal(new[] { "林地", "栅栏阵", "河流", "混合", "高台" }, board.Placeholders.Select(p => p.ThemeName).Order(StringComparer.Ordinal));

        // 没有主题的占位（手写夹具）不给主题名：渲染层不画这一行。
        var bare = new Siege.Presentation.Visibility.BoardPlaceholderView(0, new Coord(0, 0), 5, 5, Expanded: false, Own: false, Theme: null);
        Assert.Null(bare.ThemeName);
    }

    [Fact]
    public void 插旗阶段的可见信息_终端占位行显示编号主题宽高()
    {
        // match-setup Scenario「插旗阶段的可见信息」（link-map-terrain 5.1 / 裁决 M3）在终端版的落点：play 插旗阶段每块出生棋盘一行"编号 · 主题 · 宽×高"，
        // 主题中文名唯一来源 BoardThemeNames.DisplayName；仍不画任何棋盘内部布局。期望取自 link:1 的棋盘清单（整块棋盘，不是核心区）。
        // 不经 Program（不碰控制台、不读写带入档案）：直接调 PlayCommand.Run，不给档案即不带入带出。
        // 变异 MLT-5（PlayCommand.ChooseZone 占位行去掉主题）→ 红 2（本测试、各入口支持生成图_链接模式Tests.终端版开新局）。
        MapData map = Siege.Core.Board.Maps.MapCatalog.Resolve("link:1");
        var output = new StringWriter();

        int code = PlayCommand.Run(5, null, 1, Siege.Core.Ai.AiDifficulty.Standard, new StringReader("q\n"), output, map);

        Assert.Equal(0, code);
        string text = output.ToString();
        string[] themeNames = new string[5];
        for (int zone = 0; zone < 5; zone++)
        {
            BoardPlate plate = map.BirthBoard(zone);
            themeNames[zone] = plate.Theme!.Value.DisplayName();
            Assert.Contains($"  {zone + 1} 号棋盘 · {themeNames[zone]} · {plate.Width}×{plate.Height}（左下角 {plate.Origin.ToNotation()}）", text, StringComparison.Ordinal);
        }

        // 五块主题两两不同，恰是五个主题的中文名（测试内字面量，不经 DisplayName 反推）。
        Assert.Equal(new[] { "林地", "栅栏阵", "河流", "混合", "高台" }, themeNames.Order(StringComparer.Ordinal));
        Assert.Contains("盲选出生棋盘", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" ? ", text, StringComparison.Ordinal);   // 不暴露棋盘内部布局（信物格）
    }

    /// <summary>手写各块棋盘的主题（按编号）。</summary>
    private static readonly BoardTheme[] Themes = [BoardTheme.Plateau, BoardTheme.River, BoardTheme.Forest, BoardTheme.Palisade, BoardTheme.Mixed];

    /// <summary>
    /// 链接档手写夹具：5 块出生棋盘一字排开（第 2 块宽 8、高 6，其余 5×5），间距 3；出生区手写成各棋盘的 3×3 核心区
    /// （原点 + (⌊(W−3)/2⌋, ⌊(H−3)/2⌋)，偶数边偏西 / 偏南，link-map-terrain D1），没有信物格。第 1 组的校验器仍要求出生区等于整块棋盘，所以只经 CreateUnvalidated 建局。
    /// </summary>
    private static MapData CoreZoneMap()
    {
        ImmutableArray<BoardPlate> boards =
        [
            new(new Coord(2, 2), 5, 5, BoardPlateKind.Birth, Themes[0]),
            new(new Coord(10, 2), 8, 6, BoardPlateKind.Birth, Themes[1]),
            new(new Coord(21, 2), 5, 5, BoardPlateKind.Birth, Themes[2]),
            new(new Coord(29, 2), 5, 5, BoardPlateKind.Birth, Themes[3]),
            new(new Coord(37, 2), 5, 5, BoardPlateKind.Birth, Themes[4]),
        ];
        // 标识须是 link:<种子>（链接模式只配链接图）；本局不发生链接，不会按该种子重新生成。
        MapData built = LinkMapFixtures.Build("link:90001", boards);
        return built with
        {
            BirthZones = [.. boards.Select(b => (ImmutableHashSet<Coord>)[.. TestMaps.Rect(b.Origin.X + ((b.Width - 3) / 2), b.Origin.Y + ((b.Height - 3) / 2), 3, 3)])],
            RelicCells = ImmutableDictionary<Coord, RelicCellSpec>.Empty,
        };
    }
}
