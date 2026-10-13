using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Sim.Logging;

/// <summary>
/// 日志与 <see cref="LinkLayout"/> 之间的适配（board-link-mode F.3 / 裁决 J10-1：换算本体已下沉到 Core 的 <see cref="LinkLayout"/>，这里只做"从日志取开局棋盘与链接事件"与版本口径）。
/// 日志各行坐标的版本口径：首部为开局布局；小回合快照为该回合结束时（含本回合内的链接）的布局（<see cref="VersionAfterTurn"/>）；事件为发生时的布局（<see cref="VersionAtEvent"/>）；
/// 末行揭示表为终局布局。
/// </summary>
public static class LinkLogLayout
{
    /// <summary>日志链接事件的平移数据。</summary>
    public static LinkShiftStep StepOf(LinkLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new LinkShiftStep(entry.X, entry.Y, [.. entry.Shifts.Select(s => new BattlefieldShift(s.Battlefield, s.Dx, s.Dy))]);
    }

    /// <summary>日志的布局：开局棋盘取首部 <see cref="LogHeader.Boards"/>，链接按事件序号。非链接模式或首部缺棋盘即 <see cref="FormatException"/>。</summary>
    public static LinkLayout Of(MatchLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        List<BoardEntry> boards = log.Header.Boards ?? throw new FormatException($"日志 {log.Header.MapId} 的首部没有开局棋盘（Boards），不是链接模式的日志。");
        return new LinkLayout(
            boards.Select((b, z) => new BoardPlate(Coord.Parse(b.Origin), b.Width, b.Height, BoardPlateKind.Birth)),
            log.Events.Where(e => e.Link is not null).OrderBy(e => e.Seq).Select(e => StepOf(e.Link!)));
    }

    /// <summary>小回合 <paramref name="turn"/> 快照里落子 / 提子 / 改造 / 信物状态 / 活形的坐标所处的版本：本小回合及之前发生的链接数（快照在小回合结束、该回合内的链接之后）。</summary>
    public static int VersionAfterTurn(MatchLog log, int turn)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.Events.Count(e => e.Link is not null && e.Turn <= turn);
    }

    /// <summary>事件 <paramref name="logEvent"/> 里坐标所处的版本：序号在它之前的链接数（事件按发生时的布局记）。</summary>
    public static int VersionAtEvent(MatchLog log, LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(logEvent);
        return log.Events.Count(e => e.Link is not null && e.Seq < logEvent.Seq);
    }
}
