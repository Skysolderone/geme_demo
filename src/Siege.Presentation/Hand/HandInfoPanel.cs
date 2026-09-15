using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Presentation.Hand;

/// <summary>手牌信息面板的开关（设计文档 §14.3：点击按钮打开，再次点击或返回关闭）。不引用任何对局对象。</summary>
public sealed class HandPanelState
{
    /// <summary>面板是否打开。</summary>
    public bool IsOpen { get; private set; }

    /// <summary>点击手牌信息按钮。</summary>
    public void ClickButton() => IsOpen = !IsOpen;

    /// <summary>返回。</summary>
    public void Back() => IsOpen = false;
}

/// <summary>结构参数的一个信物来源。</summary>
public sealed record ParameterSourceView(Coord Coord, RelicType Type, int Magnitude, string Text);

/// <summary>一项公开结构参数：当前值、基础值、信物来源与汇总文案，如「部署上限 5（基础 3，+2 来自 军令×2）」。</summary>
public sealed record ParameterView(string Label, int Value, int Base, ImmutableArray<ParameterSourceView> Sources, string Text);

/// <summary>四项公开结构参数（展示数、免费选取数、手牌类型槽、部署上限）。</summary>
public sealed record StructureView(ParameterView RevealCount, ParameterView FreePickCount, ParameterView TypeSlots, ParameterView DeployLimit)
{
    internal static StructureView? From(PlayerStructure? structure)
    {
        if (structure?.Parameters is not { } p)
        {
            return null;
        }

        return new StructureView(
            Parameter("展示数", p.RevealCount),
            Parameter("选取数", p.FreePickCount),
            Parameter("手牌槽", p.TypeSlots),
            Parameter("部署上限", p.DeployLimit));
    }

    private static ParameterView Parameter(string label, StructureParameter parameter)
    {
        ImmutableArray<ParameterSourceView> sources =
        [
            .. parameter.Sources.Select(s => new ParameterSourceView(s.Coord, s.Type, s.Magnitude,
                $"{s.Coord.ToNotation()} {Labels.Relic(s.Type)} +{s.Magnitude}")),
        ];
        string text = sources.IsEmpty
            ? $"{label} {parameter.Value}（基础）"
            : $"{label} {parameter.Value}（基础 {parameter.Base}，+{parameter.Bonus} 来自 {string.Join("、", parameter.Sources.GroupBy(s => s.Type).Select(g => $"{Labels.Relic(g.Key)}×{g.Count()}"))}）";
        return new ParameterView(label, parameter.Value, parameter.Base, sources, text);
    }
}

/// <summary>自己区域的一行：类型、准确数量、其中本轮新征募尚未提交的数量。</summary>
public sealed record OwnHandRowView(PieceType Type, string Name, int Count, int PendingGained, string Text);

/// <summary>
/// 自己的区域（§14.3）：类型、准确数量、槽位占用、本轮新征募未提交数量、公开结构参数。数据来自本人的 <see cref="HandPrivateView"/>。
/// </summary>
/// <param name="TypeSlots">槽位上限：小回合内取本回合快照，回合外取按当前控制的公开参数；两者都没有（非参赛）为 <c>null</c>。</param>
public sealed record OwnHandAreaView(
    PlayerId Player,
    ImmutableArray<OwnHandRowView> Rows,
    int OccupiedSlots,
    int? TypeSlots,
    int? FreeSlots,
    int PendingGained,
    StructureView? Structure,
    PlayerStatus Status,
    string? StatusText);

/// <summary>
/// 敌方区域（§14.3 / §13.2）：<b>只有类型</b>，外加全体公开的结构参数与参赛状态。
/// 结构上不存在任何按类型的数量、征募候选或暂放字段——数据只来自公开世界。
/// </summary>
public sealed record OpponentHandAreaView(
    PlayerId Player,
    ImmutableArray<PieceType> Types,
    ImmutableArray<string> TypeNames,
    StructureView? Structure,
    PlayerStatus Status,
    bool IsActing,
    string? StatusText);

/// <summary>全玩家手牌对比面板。</summary>
public sealed record HandInfoPanelView(OwnHandAreaView Own, ImmutableArray<OpponentHandAreaView> Opponents)
{
    /// <summary>从观察者世界构建：自己区域读本人私有视图，敌方区域只读公开世界。</summary>
    public static HandInfoPanelView Build(ViewerWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new HandInfoPanelView(OwnArea(world), [.. world.Public.View.Players.Where(p => p.Player != world.Viewer).Select(p => Opponent(world.Public, p.Player))]);
    }

    /// <summary>敌方区域：签名只接受 <see cref="PublicWorld"/>，结构上拿不到任何私有信息。</summary>
    public static OpponentHandAreaView Opponent(PublicWorld world, PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(world);
        MatchPublicView view = world.View;
        HandPublicView hand = view.Hands.FirstOrDefault(h => h.Player == player)
            ?? throw new ArgumentException($"公开视图中没有玩家 {player} 的手牌。", nameof(player));
        PlayerFlowState state = view.Players.First(p => p.Player == player);
        return new OpponentHandAreaView(
            player,
            [.. hand.Types],
            [.. hand.Types.Select(Labels.Piece)],
            StructureView.From(world.Supplement.Structures.FirstOrDefault(s => s.Player == player)),
            state.Status,
            hand.IsActing && state.IsActive,
            Labels.Status(state.Status));
    }

    private static OwnHandAreaView OwnArea(ViewerWorld world)
    {
        HandPrivateView hand = world.OwnHand;
        PlayerStructure? structure = world.Public.Supplement.Structures.FirstOrDefault(s => s.Player == world.Viewer);
        int? slots = hand.TypeSlots ?? structure?.Parameters?.TypeSlots.Value;
        PlayerStatus status = world.Public.View.Players.First(p => p.Player == world.Viewer).Status;
        return new OwnHandAreaView(
            world.Viewer,
            [.. hand.Entries.Select(kv => new OwnHandRowView(kv.Key, Labels.Piece(kv.Key), kv.Value.Total, kv.Value.Gained,
                kv.Value.Gained > 0
                    ? $"{Labels.Piece(kv.Key)} ×{kv.Value.Total}（其中 {kv.Value.Gained} 枚为本轮新征募）"
                    : $"{Labels.Piece(kv.Key)} ×{kv.Value.Total}"))],
            hand.OccupiedSlots,
            slots,
            slots is int s ? Math.Max(0, s - hand.OccupiedSlots) : null,
            hand.PendingGained,
            StructureView.From(structure),
            status,
            Labels.Status(status));
    }
}
