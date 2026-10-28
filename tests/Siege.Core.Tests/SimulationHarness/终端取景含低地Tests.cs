using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Play;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// map-expand-25 段 B：终端版（<c>play</c>）画棋盘的取景是整座岛全部可落子格的外接矩形——插旗阶段原取各出生棋盘之并，
/// 浮空岛的平台距岛边 ≥ 2 格，按平台取景会把岛缘的低地整圈切掉；锁定后取本座位战场（= 全部可落子格），同一个矩形。
/// 期望矩形由测试按地图独立算出（可落子格的最小 / 最大行列），不经渲染器；行号与每行格数从输出里解析。
/// </summary>
/// <remarks>
/// 变异验证（段 B 实跑，只跑本类）：MB-6 <c>BoardRenderer.Viewport</c> 插旗取景改回 <c>map.BirthBoards().SelectMany(b =&gt; b.Cells())</c> → 「插旗阶段取景含低地」红 1。
/// </remarks>
public class 终端取景含低地Tests
{
    /// <summary>棋盘行：行首三位行号 + 空格，随后每格 3 个字符，行尾" 行号"。返回 (行号, 该行格数)。</summary>
    private static (int Row, int Cells)[] Rows(string text) =>
    [
        .. text.Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 4 && char.IsDigit(l[2]) && l[3] == ' ')
            .Select(l =>
            {
                int row = int.Parse(l[..3].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                int tail = 1 + row.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
                return (row, (l.Length - 4 - tail) / 3);
            }),
    ];

    /// <summary>全部可落子格的外接矩形（行列从 0 起）。</summary>
    private static (int X0, int Y0, int X1, int Y1) PlayableBounds(MapData map)
    {
        Coord[] playable = [.. map.AllCoords().Where(map.IsPlayable)];
        return (playable.Min(c => c.X), playable.Min(c => c.Y), playable.Max(c => c.X), playable.Max(c => c.Y));
    }

    [Fact]
    public void 插旗阶段取景含低地()
    {
        MatchFlow match = Create();
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);
        (int x0, int y0, int x1, int y1) = PlayableBounds(match.Map);
        // 样本口径：link:1:4 的岛缘低地确实越出各平台之并的外接矩形（否则改回按平台取景测不出来）。
        Coord[] plates = [.. match.Map.Boards.SelectMany(b => b.Cells())];
        Assert.True(plates.Min(c => c.Y) > y0 && plates.Max(c => c.Y) < y1 && plates.Min(c => c.X) > x0 && plates.Max(c => c.X) < x1);

        var output = new StringWriter();
        new BoardRenderer(output).Board(match.Publish(), P0, zones: true);
        (int Row, int Cells)[] rows = Rows(output.ToString());

        Assert.Equal(Enumerable.Range(y0 + 1, y1 - y0 + 1).Reverse(), rows.Select(r => r.Row));
        Assert.All(rows, r => Assert.Equal(x1 - x0 + 1, r.Cells));
    }

    [Fact]
    public void 锁定后取景为整座岛()
    {
        MatchFlow match = Started([0, 1, 3, 2]);
        (int x0, int y0, int x1, int y1) = PlayableBounds(match.Map);

        var output = new StringWriter();
        new BoardRenderer(output).Board(match.Publish(), P0);
        (int Row, int Cells)[] rows = Rows(output.ToString());

        Assert.Equal(Enumerable.Range(y0 + 1, y1 - y0 + 1).Reverse(), rows.Select(r => r.Row));
        Assert.All(rows, r => Assert.Equal(x1 - x0 + 1, r.Cells));
    }
}
