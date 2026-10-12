using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Core.Scoring;
using Siege.Presentation.Hand;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Text;

namespace Siege.Presentation.Visibility;

/// <summary>
/// 任何观察者都能看到的世界（设计文档 §13.1）：同一时刻的 <see cref="MatchPublicView"/> 与 <see cref="PublicSupplement"/>。
/// </summary>
/// <remarks>
/// <para>公开部分直接用 Core 的 <see cref="MatchPublicView"/>——与正式对战 AI 读的是同一个类型（implement 1.4），不另造一份。</para>
/// <para>结构上不含任何私有信息：没有手牌数量、征募面板、暂放批次、未揭示信物内容（§13.2，由信息边界闭包守门）。
/// 信息层、默认棋盘与敌方手牌区只从本类构建。</para>
/// </remarks>
public sealed class PublicWorld
{
    private PublicWorld(MatchPublicView view, PublicSupplement supplement)
    {
        View = view;
        Supplement = supplement;
    }

    /// <summary>公开快照。</summary>
    public MatchPublicView View { get; }

    /// <summary>公开补充载荷（气、结构参数、顺序明细与预测）。</summary>
    public PublicSupplement Supplement { get; }

    /// <summary>组装。两份输入 MUST 取自同一时刻（盘面序列化一致），否则抛出——混用新旧快照会让各层互相矛盾。</summary>
    public static PublicWorld From(MatchPublicView view, PublicSupplement supplement)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(supplement);
        if (!string.Equals(view.BoardSerialized, supplement.BoardSerialized, StringComparison.Ordinal)
            || view.MajorRound != supplement.MajorRound)
        {
            throw new ArgumentException("公开快照与补充载荷不是同一时刻发布的。", nameof(supplement));
        }

        return new PublicWorld(view, supplement);
    }
}

/// <summary>
/// 某个观察者"能看到的世界"：<see cref="PublicWorld"/> + 本人的私有手牌视图 + 本人正在部署时的富预演。
/// </summary>
/// <remarks>
/// 私有部分只接受<b>本人</b>的数据：手牌视图或预演属于他人即抛出。他人行动期间不存在本人预演，
/// 因此他人的暂放在结构上不可能进入本对象（§13.2「对手尚未确认的批次部署」）。
/// </remarks>
public sealed class ViewerWorld
{
    private ViewerWorld(PlayerId viewer, PublicWorld world, HandPrivateView ownHand, BatchPreview? ownPreview, ForbiddenMoveReport? ownForbidden, SeatPublicView? seat)
    {
        Viewer = viewer;
        Public = world;
        OwnHand = ownHand;
        OwnPreview = ownPreview;
        OwnForbidden = ownForbidden;
        Seat = seat;
    }

    /// <summary>
    /// 本座位的按座位公开视图（board-link-mode 实现裁决 B4，<c>MatchFlow.PublishFor</c>）：链接模式下决定默认棋盘与信息层里哪些格可见（<see cref="Board"/> / <see cref="Layer"/>），
    /// 并提供占位、同战场玩家等只对本座位成立的信息。未给出（旧模式、既有调用方）即全图可见。
    /// </summary>
    public SeatPublicView? Seat { get; }

    /// <summary>本座位锁定的出生棋盘编号（0 起）；尚未锁定为 <c>null</c>。取自公开快照里本座位自己的状态。</summary>
    public int? OwnZone => Public.View.Players.FirstOrDefault(s => s.Player == Viewer)?.BirthZone;

    /// <summary>链接模式下与本座位同一战场的其他玩家（match-setup「匿名同时插旗」：锁定后只公开"与自己同一块棋盘的玩家有谁"）；旧模式或插旗阶段为空。</summary>
    public ImmutableArray<PlayerId> SameBattlefieldPlayers =>
        Seat is { Mode: MatchMode.Link } seat ? [.. seat.Players.Where(p => p.SameBattlefield && p.Player != Viewer).Select(p => p.Player)] : [];

    /// <summary>
    /// 某玩家的数值是否对本座位遮蔽（board-link-mode Q1 / 裁决 D20，information-visibility「他人势力不可见」）：链接模式下不与本座位同战场的其他玩家。
    /// 判据只读按座位视图的 <see cref="SeatPlayerView.SameBattlefield"/>——引擎层不得自己判"同不同战场"。旧模式、本人、同战场玩家恒为假。
    /// </summary>
    public bool IsConcealed(PlayerId player) =>
        Seat is { Mode: MatchMode.Link } seat && player != Viewer && seat.Players.FirstOrDefault(p => p.Player == player) is { SameBattlefield: false };

    /// <summary>
    /// 遮蔽行的状态文案（裁决 D20 允许显示的全部）：在局 / 出局 / 弃赛，外加"所在战场已结算（胜出）"——该玩家是某个已结算战场的胜者时。
    /// 其所在战场是否已结算只能从全局公开的结算记录推出（结算只记胜者；落败者已是出局状态）。
    /// </summary>
    public string ConcealedStatusText(PlayerId player)
    {
        PlayerStatus status = Public.View.Players.FirstOrDefault(p => p.Player == player)?.Status ?? PlayerStatus.Active;
        string text = Labels.Status(status) ?? "在局";
        return Seat is { } seat && seat.Settlements.Any(s => s.Winner == player) ? $"{text} · 所在战场已结算（胜出）" : text;
    }

    /// <summary>观察者。</summary>
    public PlayerId Viewer { get; }

    /// <summary>公开世界。</summary>
    public PublicWorld Public { get; }

    /// <summary>本人的私有手牌视图。</summary>
    public HandPrivateView OwnHand { get; }

    /// <summary>本人部署阶段的富预演；不在本人部署阶段时为 <c>null</c>。</summary>
    public BatchPreview? OwnPreview { get; }

    /// <summary>
    /// 本人的单子禁手（forbidden-marks D6）：只在轮到本人行动时有，否则为 <c>null</c>。它按本人的暂放批次算出，与 <see cref="OwnPreview"/>
    /// 同属本人的私有部分，不进公开世界。
    /// </summary>
    public ForbiddenMoveReport? OwnForbidden { get; }

    /// <summary>是否轮到本人行动。</summary>
    public bool IsViewerTurn => Public.View.CurrentPlayer == Viewer && Public.View.Stage != TurnStage.Idle;

    /// <summary>
    /// 本人看到的默认棋盘：公开的默认棋盘（正式盘面 + 统一的未知信物标记），轮到本人行动时叠上本人的单子禁手。
    /// 不轮到本人时与 <see cref="DefaultBoardView.From"/> 的结果相同——别人的回合不显示单子禁手。
    /// </summary>
    public DefaultBoardView Board()
    {
        DefaultBoardView board = DefaultBoardView.From(Public);
        if (Seat is { } seat)
        {
            // 链接模式：只留本座位所在战场的格（information-visibility「其他战场不可见」在表现层的镜像）；旧模式 ForSeat 原样返回。
            board = board.ForSeat(seat, OwnZone);
        }

        return OwnForbidden is null ? board : board.WithForbidden(OwnForbidden);
    }

    /// <summary>本人部署阶段的预演呈现；不在本人部署阶段为 <c>null</c>。</summary>
    public PreviewPresentation? Preview(LibertyThresholds? thresholds = null) =>
        OwnPreview is null ? null : PreviewPresentation.Build(OwnPreview, OwnHand, thresholds ?? LibertyThresholds.Default);

    /// <summary>全玩家手牌对比面板。链接模式下他战场玩家的区域被遮蔽（裁决 D20）：没有手牌类型与结构参数。</summary>
    public HandInfoPanelView HandPanel()
    {
        HandInfoPanelView panel = HandInfoPanelView.Build(this);
        return Seat is { Mode: MatchMode.Link }
            ? panel with { Opponents = [.. panel.Opponents.Select(o => IsConcealed(o.Player) ? o.Conceal(ConcealedStatusText(o.Player)) : o)] }
            : panel;
    }

    /// <summary>某信息层的内容。只读公开世界。<paramref name="reading"/> 只对盘面层有意义。</summary>
    public LayerContent Layer(
        TacticalLayer layer, BoardReading reading = BoardReading.Ownership, LibertyThresholds? thresholds = null)
    {
        LayerContent content = TacticalLayers.Build(Public, layer, reading, thresholds);
        if (Seat is not { Mode: MatchMode.Link } seat)
        {
            return content;
        }

        // 链接模式（board-link-mode D-b）：信息层里逐格 / 逐棋串的内容只保留本座位可见的格——其他战场的格根本不进视图模型。
        // 势力层 / 顺序层里按玩家列出的行（他战场玩家的势力数值）本段不过滤，见 D-b 报告 D20。
        HashSet<Coord> visible = [.. seat.Cells.Select(c => Coord.Parse(c.Coord))];
        // 裁决 D20：按玩家列出的行（势力层玩家汇总、顺序层先手值明细）对他战场玩家只留名字 + 状态（势力层）/ 预测位置（顺序层），数值归零。
        return content switch
        {
            TerritoryLayerContent territory => territory with { Cells = [.. territory.Cells.Where(c => visible.Contains(c.Coord))] },
            LibertyLayerContent liberties => liberties with { Groups = [.. liberties.Groups.Where(g => g.Stones.All(visible.Contains))] },
            PowerLayerContent power => power with
            {
                Groups = [.. power.Groups.Where(g => g.Stones.All(visible.Contains))],
                Players = [.. power.Players.Select(p => IsConcealed(p.Player) ? p.Conceal(ConcealedStatusText(p.Player)) : p)],
                Territory = [.. power.Territory.Where(c => visible.Contains(c.Coord))],
            },
            RelicLayerContent relics => relics with { Relics = [.. relics.Relics.Where(r => visible.Contains(r.Coord))] },
            OrderLayerContent order => order with { Rows = [.. order.Rows.Select(r => IsConcealed(r.Player) ? r.Conceal() : r)] },
            _ => content,
        };
    }

    /// <summary>
    /// 组装。<paramref name="ownPreview"/> 只在本人处于部署阶段时传入；<paramref name="ownForbidden"/> 只在轮到本人行动时传入
    /// （属于他人、或当前行动玩家不是本人即抛出：别人的禁手依赖别人的暂放，结构上不得进入观察者视图）。
    /// </summary>
    /// <param name="seat">本座位的按座位公开视图（<c>MatchFlow.PublishFor(viewer)</c>，与 <paramref name="view"/> 同一时刻）；属于他人即抛出。未给出即全图可见（旧模式 / 既有调用方）。</param>
    public static ViewerWorld Build(
        PlayerId viewer, MatchPublicView view, PublicSupplement supplement, HandPrivateView ownHand, BatchPreview? ownPreview,
        ForbiddenMoveReport? ownForbidden = null, SeatPublicView? seat = null)
    {
        ArgumentNullException.ThrowIfNull(ownHand);
        PublicWorld world = PublicWorld.From(view, supplement);
        if (seat is not null && seat.Seat != viewer)
        {
            throw new ArgumentException($"按座位视图属于 {seat.Seat}，观察者是 {viewer}：别人的座位视图不得进入观察者世界。", nameof(seat));
        }

        if (ownHand.Player != viewer)
        {
            throw new ArgumentException($"手牌私有视图属于 {ownHand.Player}，观察者是 {viewer}：私有视图只能交给本人。", nameof(ownHand));
        }

        if (ownPreview is not null)
        {
            if (ownPreview.Player != viewer)
            {
                throw new ArgumentException($"预演属于 {ownPreview.Player}，观察者是 {viewer}：他人的暂放批次不得进入观察者视图。", nameof(ownPreview));
            }

            if (view.CurrentPlayer != viewer || view.Stage != TurnStage.Deploy)
            {
                throw new ArgumentException("只有本人处于部署阶段时才有预演。", nameof(ownPreview));
            }
        }

        if (ownForbidden is not null)
        {
            if (ownForbidden.Player != viewer)
            {
                throw new ArgumentException($"单子禁手属于 {ownForbidden.Player}，观察者是 {viewer}：他人的禁手不得进入观察者视图。", nameof(ownForbidden));
            }

            if (view.CurrentPlayer != viewer)
            {
                throw new ArgumentException("只有轮到本人行动时才有单子禁手。", nameof(ownForbidden));
            }
        }

        if (!view.Players.Any(p => p.Player == viewer))
        {
            throw new ArgumentException($"观察者 {viewer} 不在本局名单中。", nameof(viewer));
        }

        return new ViewerWorld(viewer, world, ownHand, ownPreview, ownForbidden, seat);
    }
}
