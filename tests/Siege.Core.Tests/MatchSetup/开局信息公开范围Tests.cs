using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Play;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 开局信息公开范围</summary>
public class 开局信息公开范围Tests
{
    [Fact]
    public void 插旗阶段的可见信息()
    {
        // 设计文档 §4.1：地形、出生区公开。relic-redesign W1：地图没有静态信物格、开局没有信物（第 4 大回合起刷新，第 3 大回合开始预告），
        // 插旗阶段的公开快照里既没有信物也没有预告；原"信物格位置公开、内容隐藏"随旧信物删除。
        MatchFlow match = LinkModeFixtures.PlainCreate();
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);

        MapData map = match.Flags.Map;
        Assert.Equal(5, map.BirthZones.Length);   // retire-legacy-rules 1.5：载体为平坦 8×8 链接档图（五块出生棋盘；原 9×9 夹具 4 个出生区）
        Assert.Equal(Terrain.Playable, map.TerrainAt(TestMaps.At("G7")));
        Assert.False(match.Board.HasRelics);
        Assert.Null(match.Publish().RelicForecast);
        Assert.Empty(match.RelicBatches);
    }

    [Fact]
    public void 占位尺寸取整块棋盘()
    {
        // match-setup Scenario「占位尺寸取整块棋盘」（link-map-terrain 裁决 M3 / D1）：出生棋盘宽 8、高 6 → 占位 8×6，左下角与棋盘外接矩形相同，而不是 3×3 的核心区；占位带主题。
        // 夹具的出生区手写成核心区（与"取出生区"的旧实现区分开）。
        // 变异验证（link-map-terrain 1.4 实跑，只跑本类）：M-P1 SeatPublicView 链接模式的占位改回取出生区外接矩形 → 本测试红 1。
        MapData map = CoreZoneMap();
        MatchFlow match = MatchFlow.CreateUnvalidated(map, TestMaps.Seed, TestMaps.All, LinkModeFixtures.Options);
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);

        SeatPublicView view = match.PublishFor(TestMaps.P0);
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
        MatchFlow match = MatchFlow.CreateUnvalidated(map, TestMaps.Seed, TestMaps.All, LinkModeFixtures.Options);
        match.Flags.Plant(TestMaps.P0, 1);

        SeatPublicView locked = match.PublishFor(TestMaps.P0);
        Assert.Equal(48, locked.Cells.Length);
        Assert.Equal(map.Boards[1].Cells().Order().Select(c => c.ToNotation()), locked.Cells.Select(c => c.Coord));
        Assert.Empty(match.PublishFor(TestMaps.P1).Cells);   // 反面：未锁定者看不到任何格
    }

    [Fact]
    public void 终端插旗占位画整块棋盘()
    {
        // 终端版（play）插旗阶段的占位画整块出生棋盘：8×6 的棋盘 2 共 48 格都标"2"，最南一行（第 3 行）与最北一行（第 8 行）各 8 格——
        // 出生区（核心区）只在第 4–6 行的 3 列；期望由夹具几何手工写出（不经渲染器）。
        // 变异验证（link-map-terrain 1.4 实跑，只跑本类）：M-P3 BoardRenderer 占位格改回 map.BirthZoneOf → 本测试红 1；
        // M-P4 BoardRenderer.Viewport 插旗取景改回出生区之并 → 本测试红 1。
        MapData map = CoreZoneMap();
        MatchFlow match = MatchFlow.CreateUnvalidated(map, TestMaps.Seed, TestMaps.All, LinkModeFixtures.Options);
        var output = new StringWriter();

        new BoardRenderer(output).Board(match.Publish(), TestMaps.P0, zones: true);

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
        Siege.Presentation.Visibility.DefaultBoardView board = match.World(TestMaps.P0).Board();

        Assert.Empty(board.Cells);
        Assert.Equal(4, board.Placeholders.Length);   // seat-gap-25：link:1:4 占位 = 4 个出生座位（map-expand-25 时为 10 块平台，更早为 5 块）
        foreach (Siege.Presentation.Visibility.BoardPlaceholderView p in board.Placeholders)
        {
            Assert.Equal(LinkModeFixtures.Map.BirthBoard(p.Zone).Theme!.Value.DisplayName(), p.ThemeName);
        }

        // 没有高台（测试内字面量，不经 DisplayName 反推）。map-expand-25（裁决 R3）：主题可以重复、高台主题去掉，原"五块两两不同、恰五个主题"改为去重后的集合。
        // seat-gap-25：占位只给 4 个出生座位（河流、栅栏阵 ×3），原"10 块平台四种主题都出现"作废。
        Assert.Equal(new[] { "栅栏阵", "河流" }, board.Placeholders.Select(p => p.ThemeName).Distinct().Order(StringComparer.Ordinal));

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
        MapData map = Siege.Core.Board.Maps.MapCatalog.Resolve("link:1:4");
        var output = new StringWriter();

        int code = PlayCommand.Run(5, null, 1, Siege.Core.Ai.AiDifficulty.Standard, new StringReader("q\n"), output, map);

        Assert.Equal(0, code);
        string text = output.ToString();
        string[] themeNames = new string[map.BirthZones.Length];
        for (int zone = 0; zone < map.BirthZones.Length; zone++)
        {
            BoardPlate plate = map.BirthBoard(zone);
            themeNames[zone] = plate.Theme!.Value.DisplayName();
            Assert.Contains($"  {zone + 1} 号座位 · {themeNames[zone]} · {plate.Width}×{plate.Height}（左下角 {plate.Origin.ToNotation()}）", text, StringComparison.Ordinal);
        }

        // 没有高台、主题可重复（测试内字面量，不经 DisplayName 反推；map-expand-25 裁决 R3，见上一条）。
        // seat-gap-25：插旗只列 4 个出生座位（河流、栅栏阵 ×3；中立平台不列），原"10 块平台四种主题都出现"作废。
        Assert.Equal(4, themeNames.Length);
        Assert.Equal(new[] { "河流", "栅栏阵", "栅栏阵", "栅栏阵" }, themeNames);
        Assert.Contains("盲选出生座位", text, StringComparison.Ordinal);
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
        MapData built = LinkMapFixtures.Build("link:90001:4", boards);
        return built with
        {
            BirthZones = [.. boards.Select(b => (ImmutableHashSet<Coord>)[.. TestMaps.Rect(b.Origin.X + ((b.Width - 3) / 2), b.Origin.Y + ((b.Height - 3) / 2), 3, 3)])],
        };
    }
}
