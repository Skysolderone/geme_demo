using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Presentation.Show;

/// <summary>格坐标的包围盒（整数，供相机节拍把目标交给引擎层换成平面矩形）。</summary>
public readonly record struct CoordRect(int MinX, int MinY, int MaxX, int MaxY)
{
    /// <summary>若干格的包围盒；没有格为 <c>null</c>。</summary>
    public static CoordRect? Of(IEnumerable<Coord> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        bool any = false;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Coord c in cells)
        {
            any = true;
            minX = Math.Min(minX, c.X);
            minY = Math.Min(minY, c.Y);
            maxX = Math.Max(maxX, c.X);
            maxY = Math.Max(maxY, c.Y);
        }

        return any ? new CoordRect(minX, minY, maxX, maxY) : null;
    }

    /// <summary>左下角与右上角两格。</summary>
    public (Coord Low, Coord High) Corners => (new Coord(MinX, MinY), new Coord(MaxX, MaxY));
}

/// <summary>
/// 棋盘拼接节拍 ①（settlement-show「棋盘展开节拍」拼接版，裁决 J5）：被贴方的格从拼接前与本机的相对位置滑到贴合位置。
/// <paramref name="Cells"/> 为滑动的格（拼接后坐标）；<paramref name="FromDx"/> / <paramref name="FromDy"/> 为进度 0 时相对终点的格偏移（进度 1000‰ 时为 0）。
/// 本机是锚点时滑的是对方棋盘；本机是被贴方时滑的是本机自己的棋盘（相机随后的重定界节拍跟过去）。
/// </summary>
public sealed record BoardSlideBeat(ImmutableArray<Coord> Cells, int FromDx, int FromDy) : SettlementBeat
{
    /// <summary>基准时长（毫秒）。</summary>
    public const int Ms = 900;

    public override int DurationMs => Ms;
}

/// <summary>棋盘拼接节拍 ②：相机重定界到合并后战场一屏可见（目标为合并后战场格的包围盒；位姿换算与夹取在引擎侧经相机视图模型完成）。</summary>
public sealed record CameraPullBeat(CoordRect Target) : SettlementBeat
{
    /// <summary>基准时长（毫秒）。</summary>
    public const int Ms = 500;

    public override int DurationMs => Ms;
}

/// <summary>棋盘拼接节拍 ③：对方战场的棋子、设施与已揭示信物显现（这些格此前对本机不可见）。</summary>
public sealed record OpponentRevealBeat(ImmutableArray<Coord> Cells) : SettlementBeat
{
    /// <summary>基准时长（毫秒）。</summary>
    public const int Ms = 600;

    public override int DurationMs => Ms;
}

/// <summary>
/// 链接模式新增的演出节拍生成（settlement-show「棋盘展开节拍」「战场结算横幅」，board-link-mode tasks 4.5）：<b>纯函数</b>，只读结算前后两份按座位的默认棋盘 /
/// 座位视图与全局公开的结算记录，不读时钟、不消费随机、不改对局状态。沿用 <see cref="ShowTimeline"/> 的"按节拍依次播放 / 提速 / 无人值守零时长"三条规则。
/// </summary>
public static class LinkShowBeats
{
    /// <summary>棋盘拼接横幅文案（裁决 J5）。</summary>
    public static string ExpansionText(IEnumerable<PlayerId> opponents)
    {
        ArgumentNullException.ThrowIfNull(opponents);
        string names = string.Join("、", opponents.Select(Labels.Player));
        return names.Length == 0 ? "棋盘拼接" : $"棋盘拼接：与 {names} 交战";
    }

    /// <summary>战场胜出横幅。</summary>
    public const string BattlefieldWonText = "战场胜出";

    /// <summary>战场落败横幅（第 N 名为该战场结算时的名次）。</summary>
    public static string BattlefieldLostText(int rank) => $"战场落败（第 {rank} 名）";

    /// <summary>他场结算的提示条文案（不打断操作）。</summary>
    public static string SettledElsewhereText(int battlefield, PlayerId winner) => $"战场 {BirthZoneLabel.Of(battlefield)} 已结算，胜者 {Labels.Player(winner)}";

    /// <summary>滑入演出起点相对终点的最大偏移（格，裁决 J12-2）。</summary>
    public const int SlideCap = 4;

    /// <summary>四个拼接节拍的基准总时长（毫秒；裁决 J5：滑入约 900 + 重定界约 500 + 显现约 600 + 横幅）。</summary>
    public const int ExpansionTotalMs = BoardSlideBeat.Ms + CameraPullBeat.Ms + OpponentRevealBeat.Ms + BannerBeat.PerBannerMs;

    /// <summary>
    /// 棋盘拼接（board-link-mode F.4 / 裁决 J5）：由本机结算前后两份按座位过滤的默认棋盘与布局换算 <see cref="LinkLayout"/>（版本 <paramref name="fromVersion"/> → 最新）得出。
    /// 结算前本机的格先按平移向量映射到拼接后坐标，再与拼接后的格比：没有新增格（本机不参与的链接，即便它把本机战场收拢平移了）→ 空序列，不把平移误判为新增。
    /// 新增格即对方战场。锚点一方（含最小出生棋盘编号的战场）不动、被贴方滑入：本机是锚点 → 对方棋盘滑入；本机是被贴方 → 本机棋盘滑过去贴合。
    /// 滑动的起点在终点沿贴边反方向 min(拼接前两战场的相对偏移, <see cref="SlideCap"/>) 格处（东贴从东、北贴从北），同一配对事件里的两条链接自然合成一次。
    /// 之后：相机重定界到合并后战场包围盒 → 对方格上的棋子 / 设施 / 已揭示信物显现 → 横幅"棋盘拼接：与 X 交战"。
    /// <paramref name="newOpponents"/> 是链接后新成为同战场的玩家。
    /// </summary>
    public static ImmutableArray<SettlementBeat> Expansion(
        DefaultBoardView before, DefaultBoardView after, IEnumerable<PlayerId> newOpponents, LinkLayout layout, int fromVersion)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(newOpponents);
        ArgumentNullException.ThrowIfNull(layout);
        int to = layout.Versions;
        if (fromVersion >= to || before.Cells.IsEmpty)
        {
            return [];
        }

        ImmutableArray<Coord> mine = [.. before.Cells.Select(c => layout.Translate(c.Coord, fromVersion, to)).Order()];
        HashSet<Coord> had = [.. mine];
        ImmutableArray<Coord> added = [.. after.Cells.Select(c => c.Coord).Where(c => !had.Contains(c)).Order()];
        if (added.IsEmpty)
        {
            return [];
        }

        int myZone = before.Cells.Select(c => layout.ZoneAt(c.Coord, fromVersion)!.Value).Min();
        int theirZone = added.Select(c => layout.ZoneAt(c, to)!.Value).Min();
        (int myDx, int myDy) = Delta(myZone);
        (int theirDx, int theirDy) = Delta(theirZone);
        bool anchor = myZone < theirZone;
        ImmutableArray<Coord> moving = anchor ? added : mine;
        ImmutableArray<Coord> staying = anchor ? mine : added;
        // 演出起点封顶（裁决 J12-2）：被贴方从终点沿贴边反方向偏 min(实际相对偏移, SlideCap) 格的位置滑到终点——东贴从东侧往西滑入、北贴从北侧往南滑入；
        // 本机是被贴方时滑的是本机棋盘，同一几何。真实平移向量照旧在链接记录里，封顶只影响演出。
        int actual = Math.Max(Math.Abs(theirDx - myDx), Math.Abs(theirDy - myDy));
        int reach = Math.Min(actual, SlideCap);
        bool east = moving.Min(c => c.X) > staying.Max(c => c.X);
        BoardSlideBeat slide = east ? new BoardSlideBeat(moving, reach, 0) : new BoardSlideBeat(moving, 0, reach);

        return
        [
            slide,
            new CameraPullBeat(CoordRect.Of(after.Cells.Select(c => c.Coord))!.Value),
            new OpponentRevealBeat(added),
            new BannerBeat([ExpansionText(newOpponents)]),
        ];

        (int Dx, int Dy) Delta(int zone)
        {
            Coord was = layout.BoardAt(zone, fromVersion).Origin;
            Coord now = layout.BoardAt(zone, to).Origin;
            return (now.X - was.X, now.Y - was.Y);
        }
    }

    /// <summary>
    /// 把战场结算横幅接到既有结算节拍上（settlement-show「战场结算横幅」：先"战场落败（第 N 名）"再出局视图）：
    /// 既有序列末尾是横幅节拍（出局 / 终局横幅，<see cref="SettlementBeats"/> 把它排在最后）时并成一条、战场横幅在前；否则直接接在末尾。展开节拍永远在最后。
    /// </summary>
    public static ImmutableArray<SettlementBeat> Splice(ImmutableArray<SettlementBeat> beats, ImmutableArray<SettlementBeat> settled, ImmutableArray<SettlementBeat> expansion)
    {
        if (settled.IsEmpty)
        {
            return [.. beats, .. expansion];
        }

        ImmutableArray<string> fieldBanners = [.. settled.OfType<BannerBeat>().SelectMany(b => b.Banners)];
        if (beats.Length > 0 && beats[^1] is BannerBeat tail)
        {
            return [.. beats[..^1], new BannerBeat([.. fieldBanners, .. tail.Banners]), .. expansion];
        }

        return [.. beats, new BannerBeat(fieldBanners), .. expansion];
    }

    /// <summary>
    /// 战场结算：对结算前后全局公开的结算记录之差，本机所在（曾在）的战场结算 → 横幅"战场胜出" / "战场落败（第 N 名）"（节拍）；
    /// 本机不在的战场结算 → 提示条文案（不打断操作，不是节拍）。对局随之终局（决赛战场结算）时横幅与提示条都不出——沿用既有的终局横幅，不重复。
    /// </summary>
    public static (ImmutableArray<SettlementBeat> Beats, ImmutableArray<string> Notices) Settlements(
        IReadOnlyList<BattlefieldSettlementRecord> before, IReadOnlyList<BattlefieldSettlementRecord> after, PlayerId me, bool matchEnded)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ImmutableArray<string>.Builder banners = ImmutableArray.CreateBuilder<string>();
        ImmutableArray<string>.Builder notices = ImmutableArray.CreateBuilder<string>();
        foreach (BattlefieldSettlementRecord record in after.Skip(before.Count))
        {
            SettlementEntry? mine = record.Entries.FirstOrDefault(e => e.Player == me);
            if (matchEnded)
            {
                // 决赛战场结算即终局：横幅与提示条都不出，只留既有的终局横幅与终局面板（它已列出全部名次）。
                continue;
            }

            if (mine is null)
            {
                // 本机不在该战场的在局名单里（他场，或本机已出局 / 弃赛而原战场随后结算）：提示条，不打断操作。
                notices.Add(SettledElsewhereText(record.Battlefield, record.Winner));
            }
            else
            {
                banners.Add(record.Winner == me ? BattlefieldWonText : BattlefieldLostText(mine.Rank));
            }
        }

        ImmutableArray<SettlementBeat> beats = banners.Count == 0 ? [] : [new BannerBeat(banners.ToImmutable())];
        return (beats, notices.ToImmutable());
    }
}
