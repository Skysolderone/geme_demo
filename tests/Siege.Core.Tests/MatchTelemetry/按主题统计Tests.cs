using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// link-map-terrain 5.2（AC6）：<c>analyze</c> 按主题统计——各主题棋盘（按锁定在该棋盘的玩家）的平均名次与胜场、匠人改造次数（按动作）、公共信物被控制的次数。
/// </summary>
/// <remarks>
/// 小日志集（不跑批量）：<c>link:1</c> 上两局脚本固定配对的小局（<see cref="LinkSimFixtures.PlayScripted"/>）——A 四人四区、B 2+1+1——再加 A 的截断副本 C（被排除样本：
/// 无名次，只计人次）。为让期望值是字面量：首部五块棋盘的主题按编号改写为 高台 / 河流 / 林地 / 栅栏阵 / 混合，结果行的名次与胜者改写为手定值，
/// 清空真实的改造与控制事件后注入已知的几条（坐标按所在版本的布局给出）。期望值逐项写在断言旁。
/// </remarks>
public class 按主题统计Tests
{
    private static readonly BoardTheme[] Themes = [BoardTheme.Plateau, BoardTheme.River, BoardTheme.Forest, BoardTheme.Palisade, BoardTheme.Mixed];

    private static readonly Lazy<(MatchLog A, MatchLog B)> Sample = new(() =>
        (MatchLog.Parse(LinkSimFixtures.PlayScripted([0, 1, 2, 3]).Log.FullText()), MatchLog.Parse(LinkSimFixtures.PlayScripted([0, 0, 1, 2]).Log.FullText())));

    /// <summary>
    /// 克隆一局：主题按 <see cref="Themes"/> 改写、清空改造与控制事件；<paramref name="ranks"/> 为 null 即改成截断局（无名次、无胜者）。
    /// map-expand-25 段 A2：link:1:4 浮空岛有 10 块平台，主题按编号循环改写（第 z 块取 Themes[z mod 5]），每个主题每局 2 块。
    /// </summary>
    private static MatchLog Prepare(MatchLog source, int[]? ranks, int[] winners)
    {
        MatchLog copy = SimFixtures.Clone(source);
        LogResult result = copy.Result!;
        return new MatchLog
        {
            Header = copy.Header with { Boards = [.. copy.Header.Boards!.Select((b, z) => b with { Theme = Themes[z % Themes.Length] })] },
            Turns = [.. copy.Turns.Select(t => t with { Edits = null })],
            Events = [.. copy.Events.Where(e => e.Type != LogEventType.ControlChanged)],
            Result = ranks is null
                ? result with { Reason = LogResult.TurnLimitReason, Standings = [], Winners = [] }
                : result with { Standings = [.. result.Standings.Select(s => s with { Rank = ranks[s.Player] })], Winners = [.. winners] },
        };
    }

    private static (MatchLog A, MatchLog B, MatchLog C) Logs()
    {
        (MatchLog a, MatchLog b) = Sample.Value;
        Assert.Equal(new[] { 0, 1, 2, 3 }, a.Header.Zones);   // 样本口径：锁定棋盘
        Assert.Equal(new[] { 0, 0, 1, 2 }, b.Header.Zones);
        Assert.All(new[] { a, b }, l => Assert.Equal(4, l.Result!.Standings.Count));
        return (Prepare(a, [1, 2, 3, 4], [0]), Prepare(b, [2, 3, 1, 4], [2]), Prepare(a, null, []));
    }

    [Fact]
    public void 各主题的平均名次与胜场按锁定棋盘统计_截断局只计人次()
    {
        // A：P0..P3 锁 0..3，名次 1/2/3/4，胜者 P0；B：P0、P1 锁 0，P2 锁 1，P3 锁 2，名次 2/3/1/4，胜者 P2；C（截断）：锁定同 A，无名次。
        // 高台（棋盘 0）：人次 A·P0、B·P0、B·P1、C·P0 = 4，有名次 3，名次 1、2、3 → 均 2，胜 1（A·P0）。
        // 河流（棋盘 1）：A·P1、B·P2、C·P1 = 3，有名次 2，名次 2、1 → 均 1.5，胜 1（B·P2）。
        // 林地（棋盘 2）：A·P2、B·P3、C·P2 = 3，有名次 2，名次 3、4 → 均 3.5，胜 0。
        // 栅栏阵（棋盘 3）：A·P3、C·P3 = 2，有名次 1，名次 4 → 均 4，胜 0。混合（棋盘 4）：无人锁定，均值无样本。
        // 变异（实跑，过滤本类）：MLT-8（锁定棋盘下标 +1）→ 红 2（本测试、报告列出按主题统计…）；MLT-9（截断局也进名次分母、无名次者记末名 4）→ 红 2（同上）。
        (MatchLog a, MatchLog b, MatchLog c) = Logs();
        ThemeSection s = ThemeAnalyzer.Analyze([a, b, c])!;

        Assert.Equal(3, s.Matches);
        Assert.Equal(2, s.RankedMatches);
        Assert.Equal(Themes.Order(), s.Themes.Select(t => t.Theme));
        Assert.All(s.Themes, t => Assert.Equal(6, t.Boards));   // map-expand-25：3 局 × 每局 2 块（10 块平台按编号循环改写主题）
        Assert.Equal(new[] { 4, 3, 3, 2, 0 }, Themes.Select(t => Stat(s, t).Seats));
        Assert.Equal(new[] { 3, 2, 2, 1, 0 }, Themes.Select(t => Stat(s, t).RankedSeats));
        Assert.Equal(new[] { 1, 1, 0, 0, 0 }, Themes.Select(t => Stat(s, t).Wins));
        Assert.Equal(2.0, Stat(s, BoardTheme.Plateau).MeanRank);
        Assert.Equal(1.5, Stat(s, BoardTheme.River).MeanRank);
        Assert.Equal(3.5, Stat(s, BoardTheme.Forest).MeanRank);
        Assert.Equal(4.0, Stat(s, BoardTheme.Palisade).MeanRank);
        Assert.True(double.IsNaN(Stat(s, BoardTheme.Mixed).MeanRank));
    }

    [Fact]
    public void 报告列出按主题统计_无主题的日志不出这一段()
    {
        // 报告段只在有带主题的链接局时出现；每个主题一行，口径写进段首。旧日志（非链接）与无主题的链接日志都不出这一段（旧报告逐字节不变）。
        // 变异 MLT-14（ReportWriter 不输出本段）→ 红 2（本测试、平衡分析方向Tests.内容集v1的报告除第12项外钉值）。
        (MatchLog a, MatchLog b, MatchLog c) = Logs();
        BalanceReport report = BalanceAnalyzer.Analyze([a, b, c]);
        Assert.NotNull(report.Themes);
        string text = ReportWriter.Render(report);
        Assert.Contains("## 按主题统计（link-map-terrain）", text, StringComparison.Ordinal);
        // map-expand-25 D5：公共信物都在平台之间的低地上，按平台主题统计的公共信物恒为 0 个（原高台棋盘 0 上有 1 个、3 局共 3 个）。
        Assert.Contains("- 高台：棋盘 6 块，锁定 4 人次（有名次 3 人次），平均名次 2，胜 1；匠人改造 0 次；公共信物 0 个，被控制 0 次", text, StringComparison.Ordinal);
        Assert.Contains("- 河流：棋盘 6 块，锁定 3 人次（有名次 2 人次），平均名次 1.5，胜 1；匠人改造 0 次；公共信物 0 个，被控制 0 次", text, StringComparison.Ordinal);
        Assert.Contains("- 混合：棋盘 6 块，锁定 0 人次（有名次 0 人次），平均名次 无样本，胜 0；匠人改造 0 次；公共信物 0 个，被控制 0 次", text, StringComparison.Ordinal);

        MatchLog themeless = SimFixtures.Clone(a);
        themeless = new MatchLog { Header = themeless.Header with { Boards = [.. themeless.Header.Boards!.Select(x => x with { Theme = null })] }, Turns = themeless.Turns, Events = themeless.Events, Result = themeless.Result };
        Assert.Null(ThemeAnalyzer.Analyze([themeless]));
        MatchLog[] old = [SimFixtures.Synthetic(1, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(6, [0]))];
        Assert.Null(BalanceAnalyzer.Analyze(old).Themes);
        Assert.DoesNotContain("按主题统计", ReportWriter.Render(BalanceAnalyzer.Analyze(old)), StringComparison.Ordinal);
    }

    private static ThemeStat Stat(ThemeSection s, BoardTheme theme) => s.Themes.Single(t => t.Theme == theme);
}
