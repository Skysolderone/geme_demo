using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Presentation.Hand;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;

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
    private ViewerWorld(PlayerId viewer, PublicWorld world, HandPrivateView ownHand, BatchPreview? ownPreview)
    {
        Viewer = viewer;
        Public = world;
        OwnHand = ownHand;
        OwnPreview = ownPreview;
    }

    /// <summary>观察者。</summary>
    public PlayerId Viewer { get; }

    /// <summary>公开世界。</summary>
    public PublicWorld Public { get; }

    /// <summary>本人的私有手牌视图。</summary>
    public HandPrivateView OwnHand { get; }

    /// <summary>本人部署阶段的富预演；不在本人部署阶段时为 <c>null</c>。</summary>
    public BatchPreview? OwnPreview { get; }

    /// <summary>是否轮到本人行动。</summary>
    public bool IsViewerTurn => Public.View.CurrentPlayer == Viewer && Public.View.Stage != TurnStage.Idle;

    /// <summary>默认棋盘（正式盘面 + 统一的未知信物标记）。</summary>
    public DefaultBoardView Board() => DefaultBoardView.From(Public);

    /// <summary>本人部署阶段的预演呈现；不在本人部署阶段为 <c>null</c>。</summary>
    public PreviewPresentation? Preview(LibertyThresholds? thresholds = null) =>
        OwnPreview is null ? null : PreviewPresentation.Build(OwnPreview, OwnHand, thresholds ?? LibertyThresholds.Default);

    /// <summary>全玩家手牌对比面板。</summary>
    public HandInfoPanelView HandPanel() => HandInfoPanelView.Build(this);

    /// <summary>某信息层的内容。只读公开世界。<paramref name="reading"/> 只对盘面层有意义。</summary>
    public LayerContent Layer(
        TacticalLayer layer, BoardReading reading = BoardReading.Ownership, LibertyThresholds? thresholds = null) =>
        TacticalLayers.Build(Public, layer, reading, thresholds);

    /// <summary>组装。<paramref name="ownPreview"/> 只在本人处于部署阶段时传入。</summary>
    public static ViewerWorld Build(
        PlayerId viewer, MatchPublicView view, PublicSupplement supplement, HandPrivateView ownHand, BatchPreview? ownPreview)
    {
        ArgumentNullException.ThrowIfNull(ownHand);
        PublicWorld world = PublicWorld.From(view, supplement);
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

        if (!view.Players.Any(p => p.Player == viewer))
        {
            throw new ArgumentException($"观察者 {viewer} 不在本局名单中。", nameof(viewer));
        }

        return new ViewerWorld(viewer, world, ownHand, ownPreview);
    }
}
