using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;

namespace Siege.Sim.Logging;

/// <summary>一次链接的平移数据（离线解析与跑局层共用的形状）：锚点 X、被贴方 Y 与拼接前每个战场的平移向量。</summary>
public sealed record LinkShiftStep(int X, int Y, ImmutableArray<BattlefieldShift> Shifts)
{
    /// <summary>取自对局的链接记录。</summary>
    public static LinkShiftStep From(LinkRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new LinkShiftStep(record.X, record.Y, record.Shifts);
    }

    /// <summary>取自日志的链接事件。</summary>
    public static LinkShiftStep From(LinkLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new LinkShiftStep(entry.X, entry.Y, [.. entry.Shifts.Select(s => new BattlefieldShift(s.Battlefield, s.Dx, s.Dy))]);
    }
}

/// <summary>
/// "坐标随链接平移"的<b>唯一</b>实现（board-link-mode F.3 / 裁决 J3：链接时坐标允许变化，日志里链接前的坐标按链接前布局解释）。
/// 输入是开局布局（版本 0，每块出生棋盘自成战场，下标 = 出生棋盘编号）与按发生顺序的链接平移记录；第 k 个版本 = 执行完前 k 次链接后的布局。
/// 链接档的可落子格都在出生棋盘上，一格的平移就是它所在出生棋盘的平移：棋盘 z 在版本 v 的位置 = 开局位置 + 它所在战场在第 1…v 次链接里的向量之和
/// （战场标识随合并变化：被贴方 Y 合并后标识变为锚点 X）。
/// </summary>
/// <remarks>
/// 使用方：跑局层把本小回合的落子 / 提子 / 改造从链接前盘面搬到链接后盘面（<c>MatchSession.RecordTurn</c>）、离线分析把开局坐标的信物对到各快照的坐标（<c>BalanceAnalyzer</c>）、
/// 离线重建地形（测试）。日志各行坐标的版本口径见 <see cref="VersionAfterTurn"/> / <see cref="VersionAtEvent"/>。
/// </remarks>
public sealed class LinkLayout
{
    private readonly ImmutableArray<BoardPlate> _opening;
    private readonly (int Dx, int Dy)[][] _offset;

    /// <param name="opening">开局的出生棋盘（下标 = 出生棋盘编号）。</param>
    /// <param name="steps">按发生顺序的链接；每条须含拼接前每个战场的向量，缺即 <see cref="FormatException"/>。</param>
    public LinkLayout(IEnumerable<BoardPlate> opening, IEnumerable<LinkShiftStep> steps)
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(steps);
        _opening = [.. opening];
        LinkShiftStep[] list = [.. steps];
        int zones = _opening.Length;
        _offset = new (int, int)[list.Length + 1][];
        _offset[0] = new (int, int)[zones];
        int[] ids = [.. Enumerable.Range(0, zones)];
        for (int k = 0; k < list.Length; k++)
        {
            LinkShiftStep step = list[k];
            _offset[k + 1] = new (int, int)[zones];
            for (int z = 0; z < zones; z++)
            {
                BattlefieldShift shift = step.Shifts.FirstOrDefault(s => s.Battlefield == ids[z]);
                if (shift.Battlefield != ids[z])
                {
                    throw new FormatException($"第 {k + 1} 次链接（{BirthZoneLabel.Of(step.X)} + {BirthZoneLabel.Of(step.Y)}）的记录缺少战场 {BirthZoneLabel.Of(ids[z])} 的平移向量。");
                }

                _offset[k + 1][z] = (_offset[k][z].Dx + shift.Dx, _offset[k][z].Dy + shift.Dy);
            }

            for (int z = 0; z < zones; z++)
            {
                ids[z] = ids[z] == step.Y ? step.X : ids[z];
            }
        }
    }

    /// <summary>链接次数（最大版本号）。</summary>
    public int Versions => _offset.Length - 1;

    /// <summary>日志的布局：开局棋盘取首部 <see cref="LogHeader.Boards"/>，链接按事件序号。非链接模式或首部缺棋盘即 <see cref="FormatException"/>。</summary>
    public static LinkLayout Of(MatchLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        List<BoardEntry> boards = log.Header.Boards ?? throw new FormatException($"日志 {log.Header.MapId} 的首部没有开局棋盘（Boards），不是链接模式的日志。");
        return new LinkLayout(
            boards.Select((b, z) => new BoardPlate(Coord.Parse(b.Origin), b.Width, b.Height, BoardPlateKind.Birth)),
            log.Events.Where(e => e.Link is not null).OrderBy(e => e.Seq).Select(e => LinkShiftStep.From(e.Link!)));
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

    /// <summary>出生棋盘 <paramref name="zone"/> 在版本 <paramref name="version"/> 的位置。</summary>
    public BoardPlate BoardAt(int zone, int version)
    {
        RequireVersion(version);
        BoardPlate plate = _opening[zone];
        (int dx, int dy) = _offset[version][zone];
        return plate with { Origin = new Coord(plate.Origin.X + dx, plate.Origin.Y + dy) };
    }

    /// <summary>版本 <paramref name="version"/> 上含 <paramref name="c"/> 的出生棋盘编号；场景格为 <c>null</c>。</summary>
    public int? ZoneAt(Coord c, int version)
    {
        RequireVersion(version);
        for (int z = 0; z < _opening.Length; z++)
        {
            if (BoardAt(z, version).Contains(c))
            {
                return z;
            }
        }

        return null;
    }

    /// <summary>版本 <paramref name="from"/> 上的可落子格在版本 <paramref name="to"/>（≥ from）上的坐标。场景格即 <see cref="ArgumentException"/>。</summary>
    public Coord Translate(Coord c, int from, int to)
    {
        int zone = ZoneAt(c, from) ?? throw new ArgumentException($"{c.ToNotation()} 在版本 {from} 上不在任何出生棋盘里，没有平移向量。", nameof(c));
        return Shift(c, zone, from, to);
    }

    /// <summary>
    /// 一条改造随链接迁移（与 <c>GameBoard.ApplySplice</c> 同口径）：搭桥 / 烧林按目标格所在棋盘平移；立栅两端各按所在棋盘平移，一端是场景格时随另一端平移；
    /// 两端都是场景格的不动——但在版本 <paramref name="to"/> 上任一端被棋盘占到、或越出画布（东 / 北边界外留 <see cref="LinkMapGenerator.Margin"/> 格）即丢弃。
    /// 平移后越出画布原点（负坐标）也为 <c>null</c>。
    /// </summary>
    public TerrainEdit? TranslateEdit(TerrainEdit edit, int from, int to)
    {
        if (edit.Kind != TerrainEditKind.Fence)
        {
            if (ZoneAt(edit.Cell, from) is not { } z)
            {
                return edit;
            }

            Coord moved = Shift(edit.Cell, z, from, to);
            return edit.Kind == TerrainEditKind.Bridge ? TerrainEdit.Bridge(moved) : TerrainEdit.Burn(moved);
        }

        Coord a = edit.Edge.A;
        Coord b = edit.Edge.B;
        int? za = ZoneAt(a, from);
        int? zb = ZoneAt(b, from);
        if (za is null && zb is null)
        {
            int width = Enumerable.Range(0, _opening.Length).Max(z => BoardAt(z, to).Origin.X + _opening[z].Width) + LinkMapGenerator.Margin;
            int height = Enumerable.Range(0, _opening.Length).Max(z => BoardAt(z, to).Origin.Y + _opening[z].Height) + LinkMapGenerator.Margin;
            bool inside = a.X < width && b.X < width && a.Y < height && b.Y < height;
            return inside && ZoneAt(a, to) is null && ZoneAt(b, to) is null ? edit : null;
        }

        int ra = za ?? zb!.Value;
        int rb = zb ?? za!.Value;
        (int ax, int ay) = Delta(ra, from, to);
        (int bx, int by) = Delta(rb, from, to);
        int nax = a.X + ax, nay = a.Y + ay, nbx = b.X + bx, nby = b.Y + by;
        return nax < 0 || nay < 0 || nbx < 0 || nby < 0 ? null : TerrainEdit.Fence(new Coord(nax, nay), new Coord(nbx, nby));
    }

    private Coord Shift(Coord c, int zone, int from, int to)
    {
        (int dx, int dy) = Delta(zone, from, to);
        return new Coord(c.X + dx, c.Y + dy);
    }

    private (int Dx, int Dy) Delta(int zone, int from, int to)
    {
        RequireVersion(to);
        if (to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, $"只能从早的版本平移到晚的版本（{from} → {to}）。");
        }

        return (_offset[to][zone].Dx - _offset[from][zone].Dx, _offset[to][zone].Dy - _offset[from][zone].Dy);
    }

    private void RequireVersion(int version)
    {
        if (version < 0 || version > Versions)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, $"版本须在 0–{Versions} 之间。");
        }
    }
}
