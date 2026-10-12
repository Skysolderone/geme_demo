using System.Collections.Immutable;
using Siege.Core.Board;
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

/// <summary>棋盘展开节拍 ①：新增的公共棋盘与通道从地面升起（settlement-show「棋盘展开节拍」，design D9 / 裁决 D5）。</summary>
public sealed record BoardRiseBeat(ImmutableArray<Coord> Cells) : SettlementBeat
{
    /// <summary>基准时长（毫秒）。</summary>
    public const int Ms = 700;

    public override int DurationMs => Ms;
}

/// <summary>棋盘展开节拍 ②：相机拉远到合并后战场一屏可见（目标为合并后战场格的包围盒；位姿换算与夹取在引擎侧经相机视图模型完成）。</summary>
public sealed record CameraPullBeat(CoordRect Target) : SettlementBeat
{
    /// <summary>基准时长（毫秒）。</summary>
    public const int Ms = 500;

    public override int DurationMs => Ms;
}

/// <summary>棋盘展开节拍 ③：对方战场的棋子、设施与已揭示信物显现（这些格此前对本机不可见）。</summary>
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
    /// <summary>棋盘展开横幅文案。</summary>
    public static string ExpansionText(IEnumerable<PlayerId> opponents)
    {
        ArgumentNullException.ThrowIfNull(opponents);
        string names = string.Join("、", opponents.Select(Labels.Player));
        return names.Length == 0 ? "棋盘展开" : $"棋盘展开：与 {names} 交战";
    }

    /// <summary>战场胜出横幅。</summary>
    public const string BattlefieldWonText = "战场胜出";

    /// <summary>战场落败横幅（第 N 名为该战场结算时的名次）。</summary>
    public static string BattlefieldLostText(int rank) => $"战场落败（第 {rank} 名）";

    /// <summary>他场结算的提示条文案（不打断操作）。</summary>
    public static string SettledElsewhereText(int battlefield, PlayerId winner) => $"战场 {BirthZoneLabel.Of(battlefield)} 已结算，胜者 {Labels.Player(winner)}";

    /// <summary>四个展开节拍的基准总时长（毫秒；裁决 D5：合计约 2.5 s）。</summary>
    public const int ExpansionTotalMs = BoardRiseBeat.Ms + CameraPullBeat.Ms + OpponentRevealBeat.Ms + BannerBeat.PerBannerMs;

    /// <summary>
    /// 棋盘展开：由本机结算前后两份按座位过滤的默认棋盘得出。本机战场没有新增格（本机不参与的链接）→ 空序列。
    /// 新增格里属于"此前未展开、此刻已展开"的出生棋盘占位的，是对方的出生棋盘（节拍 ③ 显现）；其余新增格是新长出的公共棋盘与通道（节拍 ① 升起）。
    /// <paramref name="newOpponents"/> 是链接后新成为同战场的玩家（横幅文案"与 X 交战"）。
    /// </summary>
    public static ImmutableArray<SettlementBeat> Expansion(DefaultBoardView before, DefaultBoardView after, IEnumerable<PlayerId> newOpponents)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(newOpponents);
        HashSet<Coord> had = [.. before.Cells.Select(c => c.Coord)];
        ImmutableArray<Coord> added = [.. after.Cells.Select(c => c.Coord).Where(c => !had.Contains(c)).Order()];
        if (added.IsEmpty)
        {
            return [];
        }

        HashSet<int> expandedBefore = [.. before.Placeholders.Where(p => p.Expanded).Select(p => p.Zone)];
        HashSet<Coord> opponentBoards = [.. after.Placeholders.Where(p => p.Expanded && !expandedBefore.Contains(p.Zone)).SelectMany(p => p.Cells())];
        ImmutableArray<Coord> rising = [.. added.Where(c => !opponentBoards.Contains(c))];
        ImmutableArray<Coord> revealed = [.. added.Where(opponentBoards.Contains)];

        ImmutableArray<SettlementBeat>.Builder beats = ImmutableArray.CreateBuilder<SettlementBeat>(4);
        if (!rising.IsEmpty)
        {
            beats.Add(new BoardRiseBeat(rising));
        }

        beats.Add(new CameraPullBeat(CoordRect.Of(after.Cells.Select(c => c.Coord))!.Value));
        if (!revealed.IsEmpty)
        {
            beats.Add(new OpponentRevealBeat(revealed));
        }

        beats.Add(new BannerBeat([ExpansionText(newOpponents)]));
        return beats.ToImmutable();
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
