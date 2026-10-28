using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.Visibility;
using Siege.Sim.Play;
using static Siege.Core.Tests.SimulationHarness.LinkSimFixtures;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// seat-gap-25 段 B（design E6「入口」）：Sim 与 Godot 只能选出生座位；中立平台可见、与座位区分、不可出生。
/// 期望一律由测试按地图的棋盘清单独立算出（座位 = <see cref="BoardPlateKind.Birth"/>，中立平台 = <see cref="BoardPlateKind.Public"/>），不经被测的渲染 / 打印函数。
/// 载体 <c>link:1:2</c>：2 人长方形岛 45×33（design E1），2 个座位 + 至少 2 块中立平台。
/// </summary>
/// <remarks>
/// 变异验证（段 B 实跑，只跑本类，逐条还原、还原后刷新时间戳）：
/// MB2-4 <c>Program.ExportMap</c> 的中立平台段改为遍历 <c>map.BirthBoards()</c> → 红 1（「map子命令分列出生座位与中立平台」）；
/// MB2-5 <c>Program.Glyph</c> 的中立平台判定改为恒不命中 → 红 1（同上）；
/// MB2-6 <c>PlayCommand.ChooseZone</c> 的上界改为 <c>map.Boards.Length</c> → 红 1（「终端插旗只能选出生座位」：选 3 号被接受、建局时抛"人工选区不合法"）；
/// MB2-7 <c>BoardRenderer.IsNeutralPlate</c> 恒为 false（追加 <c>&amp;&amp; c.X &lt; 0</c>）→ 红 2（「终端插旗取景覆盖长方形岛并区分座位与中立平台」与 map 文本图同用此判定）；
/// MB2-8 <c>BoardRenderer.Viewport</c> 插旗取景改为座位之并 → 红 2（本类取景一条 + 终端取景含低地Tests.插旗阶段取景含低地）；
/// MB2-9 <c>DefaultBoardView.ForSeat</c> 的 NeutralPlates 去掉 Kind 过滤 → 红 1（「选图预览只给座位编号_中立平台只给轮廓」）；
/// MB2-10 <c>MapData.BirthBoardAt</c> 改为遍历全部棋盘清单（中立平台也给编号）→ 红 2（「插旗点选只认出生座位」与终端取景一条）。
/// </remarks>
[Collection(ConsoleRedirect.Collection)]
public class 座位与中立平台的入口呈现Tests
{
    private const string MapId = "link:1:2";

    private static readonly MapData Map = MapCatalog.Resolve(MapId);

    private static BoardPlate[] Seats => [.. Map.Boards.Where(b => b.Kind == BoardPlateKind.Birth)];

    private static BoardPlate[] Neutral => [.. Map.Boards.Where(b => b.Kind == BoardPlateKind.Public)];

    [Fact]
    public void map子命令分列出生座位与中立平台()
    {
        Assert.Equal(2, Seats.Length);
        Assert.True(Neutral.Length >= 2, $"样本口径：{MapId} 须有中立平台（实际 {Neutral.Length} 块）");

        (int code, string output, string err) = RunMain(null, null, "map", "--map", MapId);
        Assert.True(code == 0, err);
        string[] lines = [.. output.ReplaceLineEndings("\n").Split('\n')];

        // 两段标题与各自的行：座位段只列座位、中立平台段只列中立平台，行内是"主题 · 宽×高  左下角 …"。
        int seatHeader = Array.FindIndex(lines, l => l.StartsWith("出生座位 2 个（", StringComparison.Ordinal));
        int neutralHeader = Array.FindIndex(lines, l => l.StartsWith($"中立平台 {Neutral.Length} 块（不可出生", StringComparison.Ordinal));
        Assert.True(seatHeader >= 0 && neutralHeader == seatHeader + Seats.Length + 1, $"座位段 {seatHeader}，中立平台段 {neutralHeader}");
        for (int i = 0; i < Seats.Length; i++)
        {
            BoardPlate p = Seats[i];
            Assert.StartsWith($"  {i + 1} 号座位 · {p.Theme!.Value.DisplayName()} · {p.Width}×{p.Height}  左下角 {p.Origin.ToNotation()}  新地表 ", lines[seatHeader + 1 + i], StringComparison.Ordinal);
        }

        for (int i = 0; i < Neutral.Length; i++)
        {
            BoardPlate p = Neutral[i];
            Assert.StartsWith($"  中立 {i + 1} · {p.Theme!.Value.DisplayName()} · {p.Width}×{p.Height}  左下角 {p.Origin.ToNotation()}  新地表 ", lines[neutralHeader + 1 + i], StringComparison.Ordinal);
        }

        // 中立平台的左下角不出现在任何"号座位"行里（反面）。
        Assert.DoesNotContain(lines.Where(l => l.Contains(" 号座位 · ", StringComparison.Ordinal)), l => Neutral.Any(p => l.Contains($"左下角 {p.Origin.ToNotation()} ", StringComparison.Ordinal)));

        // 文本图：中立平台格（草地、非入口 / 咽喉 / 出生区）次位标 n，座位格同口径无 n。只解析"地图校验"之后的文本图行。
        int start = Array.FindIndex(lines, l => l.Contains("地图校验通过", StringComparison.Ordinal));
        Assert.True(start > neutralHeader);
        Dictionary<int, string> rows = lines.Skip(start)
            .Where(l => l.Length == 4 + (3 * Map.Width) && char.IsDigit(l[2]) && l[3] == ' ')
            .ToDictionary(l => int.Parse(l[..3].Trim(), System.Globalization.CultureInfo.InvariantCulture) - 1, l => l);
        Assert.Equal(Map.Height, rows.Count);
        string GlyphAt(Coord c) => rows[c.Y].Substring(4 + (3 * c.X), 2);
        bool Plain(Coord c) => Map.IsPlayable(c) && Map.SurfaceAt(c) == Surface.Grass && c != Map.CentralEntrance && !Map.ChokePoints.Contains(c) && Map.BirthZoneOf(c) is null;

        Coord[] neutralCells = [.. Neutral.SelectMany(p => p.Cells()).Where(Plain)];
        Coord[] seatCells = [.. Seats.SelectMany(p => p.Cells()).Where(Plain)];
        Assert.True(neutralCells.Length > 20 && seatCells.Length > 20, $"样本口径：中立平台 {neutralCells.Length} 格、座位 {seatCells.Length} 格");
        Assert.All(neutralCells, c => Assert.Equal($"{Map.HeightAt(c)}n", GlyphAt(c)));
        Assert.All(seatCells, c => Assert.Equal($"{Map.HeightAt(c)} ", GlyphAt(c)));
        Assert.Contains("n 中立平台", output, StringComparison.Ordinal);   // 图例
    }

    [Fact]
    public void 终端插旗只能选出生座位()
    {
        // 3 = 座位数 + 1（中立平台总数 ≥ 2，按"全部平台"计 3 号是合法编号）与 0 都被拒、重新提示；1 被接受并锁定 1 号座位。
        Assert.True(Map.Boards.Length >= 3);
        (int code, string output, string err) = RunMain(null, new StringReader("3\n0\n1\nq\n"), "play", "--map", MapId, "--seed", "5", "--no-carry");

        Assert.True(code == 0, err);
        Assert.Equal(3, output.Split("盲选出生座位：以上是 2 个出生座位（1–2），中立平台不可选").Length - 1);
        Assert.Contains("你锁定了 1 号出生座位", output, StringComparison.Ordinal);
        // 插旗清单只列座位：恰 2 行"号座位 ·"，没有中立平台行。
        Assert.Equal(2, output.ReplaceLineEndings("\n").Split('\n').Count(l => l.StartsWith("  ", StringComparison.Ordinal) && l.Contains(" 号座位 · ", StringComparison.Ordinal)));
    }

    [Fact]
    public void 插旗点选只认出生座位()
    {
        // 图形版点选插旗经 MatchSession.BirthZoneAt → MapData.BirthBoardAt：座位格给出座位编号，中立平台格一律为 null（点了不插旗）。
        for (int z = 0; z < Seats.Length; z++)
        {
            Assert.All(Seats[z].Cells(), c => Assert.Equal(z, Map.BirthBoardAt(c)));
        }

        Assert.All(Neutral.SelectMany(p => p.Cells()), c => Assert.Null(Map.BirthBoardAt(c)));

        // Godot 不在解决方案里（testing.md「不在解决方案里的工程对守门测试完全隐身」）：源码级扫描点选链路与中立平台轮廓的画法。
        string scripts = Path.Combine(TestPaths.RepoRoot(), "src", "godot", "scripts");
        string session = File.ReadAllText(Path.Combine(scripts, "MatchSession.cs"));
        string root = File.ReadAllText(Path.Combine(scripts, "GameRoot.cs"));
        string view = File.ReadAllText(Path.Combine(scripts, "BoardView.cs"));
        Assert.True(session.Length > 10_000 && root.Length > 50_000 && view.Length > 50_000, "样本口径：读到的是真实脚本");
        Assert.Contains("public int? BirthZoneAt(Coord coord) => Match.Map.BirthBoardAt(coord);", session, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(root, @"if \(_session\.BirthZoneAt\(coord\) is not int zone\)"));
        Assert.Contains("AddNeutralOutlines(board.NeutralPlates);", view, StringComparison.Ordinal);
        int from = view.IndexOf("private void AddNeutralOutlines(", StringComparison.Ordinal);
        int to = view.IndexOf("private const float WaterDrop", from, StringComparison.Ordinal);
        Assert.True(from > 0 && to > from);
        string outline = view[from..to];
        Assert.Contains("Visuals.PublicBoardFrame", outline, StringComparison.Ordinal);   // 淡色，与座位占位的深色边框区分
        Assert.DoesNotContain("_levels", outline, StringComparison.Ordinal);              // 不登记层高 → 拾取不到
        Assert.DoesNotContain("Label(", outline, StringComparison.Ordinal);               // 不带编号
    }

    [Fact]
    public void 终端插旗取景覆盖长方形岛并区分座位与中立平台()
    {
        MatchFlow match = MatchFlow.Create(Map, TestMaps.Seed, [new PlayerId(0), new PlayerId(1)], LinkModeFixtures.Options);
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);
        Coord[] playable = [.. Map.AllCoords().Where(Map.IsPlayable)];
        (int x0, int y0, int x1, int y1) = (playable.Min(c => c.X), playable.Min(c => c.Y), playable.Max(c => c.X), playable.Max(c => c.Y));
        // 样本口径：长方形岛 45×33（design E1 字面量），宽高不等——正方形取景测不出来。
        Assert.Equal((45, 33), (x1 - x0 + 1, y1 - y0 + 1));

        var output = new StringWriter();
        new BoardRenderer(output).Board(match.Publish(), new PlayerId(0), zones: true);
        string[] rows = [.. output.ToString().ReplaceLineEndings("\n").Split('\n').Where(l => l.Length > 4 && char.IsDigit(l[2]) && l[3] == ' ')];

        Assert.Equal(Enumerable.Range(y0 + 1, y1 - y0 + 1).Reverse(), rows.Select(l => int.Parse(l[..3].Trim(), System.Globalization.CultureInfo.InvariantCulture)));
        Assert.All(rows, l => Assert.Equal(4 + (3 * (x1 - x0 + 1)) + 1 + (l[..3].Trim().Length), l.Length));

        // 逐格：座位格画座位编号，中立平台格画 " = "，其余（低地）留白。期望由棋盘清单独立算出。
        string Cell(Coord c) => rows[y1 - c.Y].Substring(4 + (3 * (c.X - x0)), 3);
        for (int z = 0; z < Seats.Length; z++)
        {
            Assert.All(Seats[z].Cells(), c => Assert.Equal($"{z + 1,2} ", Cell(c)));
        }

        Assert.All(Neutral.SelectMany(p => p.Cells()), c => Assert.Equal(" = ", Cell(c)));
        Coord[] lowland = [.. Map.AllCoords().Where(c => c.X >= x0 && c.X <= x1 && c.Y >= y0 && c.Y <= y1 && !Map.Boards.Any(b => b.Contains(c)))];
        Assert.All(lowland, c => Assert.Equal("   ", Cell(c)));
        Assert.Contains("=中立平台（不可出生", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 选图预览只给座位编号_中立平台只给轮廓()
    {
        // 预览会话（未插旗）按座位过滤：占位只有座位（带编号），中立平台只以轮廓给出（不带编号、不在占位里），格一个都没有；锁定后中立平台进棋盘清单、轮廓清空。
        MatchFlow match = MatchFlow.Create(Map, TestMaps.Seed, [new PlayerId(0), new PlayerId(1)], LinkModeFixtures.Options);
        DefaultBoardView preview = match.World(new PlayerId(0)).Board();

        Assert.Empty(preview.Cells);
        Assert.Equal([0, 1], preview.Placeholders.Select(p => p.Zone));
        Assert.Equal(Seats.Select(p => p.Origin), preview.Placeholders.Select(p => p.Origin));
        Assert.Equal(Neutral, preview.NeutralPlates);
        Assert.DoesNotContain(preview.NeutralPlates, p => Seats.Contains(p));
        Assert.Contains($"{Map.Width}×{Map.Height} 画布，2 个出生座位（边长 8×8）、{Neutral.Length} 块中立平台；", Siege.Presentation.MapSelect.MapPreviewInfo.Of(preview), StringComparison.Ordinal);

        match.PlantPrototype((new PlayerId(0), 0));
        DefaultBoardView locked = match.World(new PlayerId(0)).Board();
        Assert.Empty(locked.NeutralPlates);
        Assert.Equal(Neutral.Length, locked.Boards.Count(b => b.Kind == BoardPlateKind.Public));
        Assert.NotEqual(preview.LayoutKey(), locked.LayoutKey());
    }
}
