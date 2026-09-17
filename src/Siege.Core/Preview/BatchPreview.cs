using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Preview;

/// <summary>批次内某棋子类型的手牌消耗：本批用几枚、库存几枚。</summary>
public sealed record HandCost(PieceType Type, int Used, int Stock);

/// <summary>预计被提走的一条敌方棋串：所有者与全部棋子（坐标字典序）。</summary>
public sealed record CapturedGroup(PlayerId Owner, ImmutableArray<CapturedStone> Stones)
{
    /// <summary>棋子坐标（字典序）。</summary>
    public ImmutableArray<Coord> Coords => [.. Stones.Select(s => s.Coord)];

    /// <summary>棋串大小。</summary>
    public int Size => Stones.Length;
}

/// <summary>
/// 结算后（提子后）的一条己方棋串：棋子、气位、是否含本批落点、是否自杀（仍无气），以及合法时的军势明细。
/// </summary>
/// <param name="Owner">所有者（即预演玩家）。</param>
/// <param name="Stones">棋子坐标，字典序。</param>
/// <param name="Liberties">结算后的气位，字典序。</param>
/// <param name="ContainsPlacement">是否含本批落点。</param>
/// <param name="IsSuicideRisk">是否属于自杀手判定出的无气棋串（与 <see cref="BatchFailure.Coords"/> 一致）。</param>
/// <param name="Power">军势明细（基础、位置加值分来源、倍增子数、生效指数、倍率、最终军势），来自 <see cref="PowerCalculator"/>；批次不合法时为 <c>null</c>。</param>
public sealed record GroupOutlook(
    PlayerId Owner,
    ImmutableArray<Coord> Stones,
    ImmutableArray<Coord> Liberties,
    bool ContainsPlacement,
    bool IsSuicideRisk,
    GroupPower? Power)
{
    /// <summary>气数。</summary>
    public int LibertyCount => Liberties.Length;
}

/// <summary>一名玩家的势力与竞争名次在本批结算前后的变化（tactical-ui 裁决 1：含被挤动的他人名次）。名次对非参赛玩家为 <c>null</c>。</summary>
public sealed record PowerChange(PlayerId Player, PlayerStatus Status, long Before, long After, int? RankBefore, int? RankAfter)
{
    /// <summary>势力增减。</summary>
    public long Delta => After - Before;

    /// <summary>名次是否变化。</summary>
    public bool RankChanged => RankBefore != RankAfter;
}

/// <summary>
/// 富预演结果（tactical-ui D2 / D3 / 裁决 9）：一次性带回表现层需要的全部预演信息，表现层据此呈现、不做任何规则计算。
/// </summary>
/// <remarks>
/// <para>六项：① <see cref="Placements"/> + <see cref="HandCosts"/>；② <see cref="DeployLimit"/> / <see cref="DeployUsed"/>；
/// ③ <see cref="Captures"/>；④ <see cref="OwnGroups"/> 的气位与自杀风险；⑤ <see cref="OwnGroups"/> 的军势明细；
/// ⑥ <see cref="PowerChanges"/>。另有 <see cref="Failure"/>（类别 + 坐标）与 <see cref="WillReveal"/>（将揭示格，<b>只有坐标</b>）。</para>
/// <para>填充规则：第 1–2 步失败时没有结算后盘面，③–⑥ 与将揭示均为空；自杀手 / 同形时 ③④ 有值（供高亮），⑤⑥ 与将揭示为空；
/// 合法非 Pass 时全部有值；Pass 时除额度外均为空。</para>
/// </remarks>
public sealed record BatchPreview(
    PlayerId Player,
    bool IsLegal,
    bool IsPass,
    BatchFailure? Failure,
    ImmutableArray<Placement> Placements,
    ImmutableArray<HandCost> HandCosts,
    int DeployLimit,
    ImmutableArray<CapturedGroup> Captures,
    ImmutableArray<GroupOutlook> OwnGroups,
    ImmutableArray<PowerChange> PowerChanges,
    ImmutableArray<Coord> WillReveal)
{
    /// <summary>已用部署额度 = 暂放枚数。</summary>
    public int DeployUsed => Placements.Length;

    /// <summary>自杀手时提子后仍无气的己方棋子（即 <see cref="BatchFailure.Coords"/>）；否则为空。</summary>
    public ImmutableArray<Coord> SuicideStones => Failure is { Kind: BatchFailureKind.Suicide } f ? f.Coords : [];

    /// <summary>预计被提走的全部坐标（字典序）。</summary>
    public ImmutableArray<Coord> CapturedCoords => [.. Captures.SelectMany(g => g.Coords).Order()];
}

/// <summary>
/// 富预演的<b>唯一</b>组装点。全部复用既有唯一实现：合法性与提子走 <see cref="BatchRehearsal.Rehearse"/>，
/// 棋串与气走 <see cref="GameBoard"/> 查询，军势与名次走 <see cref="PowerCalculator"/>，
/// 将揭示走 <see cref="RelicLedger.Reveal"/>（在账本<b>副本</b>上执行）。对正式盘面、历史与账本零副作用。
/// </summary>
public static class BatchPreviewBuilder
{
    /// <summary>组装富预演。<paramref name="roster"/> 与 <paramref name="majorRound"/> 由流程层提供，含义同结算第 4、5 步。</summary>
    public static BatchPreview Build(
        GameBoard board,
        BatchContext context,
        IReadOnlyList<Placement> placements,
        BoardHistory history,
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster,
        SiteValues siteValues,
        RelicLedger relics,
        int majorRound)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(relics);

        RehearsalResult rehearsal = BatchRehearsal.Rehearse(board, context, placements, history);
        ImmutableArray<Placement> ordered = [.. placements];
        ImmutableArray<HandCost> costs =
        [
            .. ordered.GroupBy(p => p.Type).OrderBy(g => g.Key).Select(g => new HandCost(g.Key, g.Count(), context.StockOf(g.Key))),
        ];

        if (rehearsal.IsPass || rehearsal.ProjectedBoard is not { } projected)
        {
            return new BatchPreview(context.Player, rehearsal.IsLegal, rehearsal.IsPass, rehearsal.Failure, ordered, costs,
                context.DeployLimit, [], [], [], []);
        }

        ImmutableArray<CapturedGroup> captures = GroupCaptures(board, rehearsal.Captures);
        PowerSnapshot? after = rehearsal.IsLegal ? PowerCalculator.Compute(projected, roster, siteValues) : null;
        ImmutableArray<GroupOutlook> own = OwnGroups(projected, context.Player, ordered, rehearsal.Failure, after);

        ImmutableArray<PowerChange> changes = [];
        ImmutableArray<Coord> willReveal = [];
        if (after is not null)
        {
            PowerSnapshot before = PowerCalculator.Compute(board, roster, siteValues);
            changes =
            [
                .. after.Players.Select(p =>
                    new PowerChange(p.Player, p.Status, before.Of(p.Player).Total, p.Total, before.RankOf(p.Player), after.RankOf(p.Player))),
            ];

            // D3：将揭示 = 结算第 4 步在结算后盘面上会揭示的格。在账本副本上跑同一个 Reveal，正式账本不动；只取坐标，内容不出本方法。
            RelicLedger copy = RelicLedger.Restore(relics.Generation, relics.ExportState());
            willReveal = [.. copy.Reveal(projected, majorRound).Select(e => e.Coord).Order()];
        }

        return new BatchPreview(context.Player, rehearsal.IsLegal, IsPass: false, rehearsal.Failure, ordered, costs,
            context.DeployLimit, captures, own, changes, willReveal);
    }

    /// <summary>把平铺的提子集合按正式盘面上的棋串分组。放置己方棋子不改变敌方棋串结构，且提子总是整串，故正式盘面上的棋串即被提棋串。</summary>
    private static ImmutableArray<CapturedGroup> GroupCaptures(GameBoard board, ImmutableArray<CapturedStone> captures)
    {
        var remaining = new SortedDictionary<Coord, CapturedStone>();
        foreach (CapturedStone stone in captures)
        {
            remaining[stone.Coord] = stone;
        }

        ImmutableArray<CapturedGroup>.Builder groups = ImmutableArray.CreateBuilder<CapturedGroup>();
        while (remaining.Count > 0)
        {
            Coord first = remaining.Keys.First();
            Group group = board.GroupAt(first)
                ?? throw new SiegeRuleException($"预计提子 {first.ToNotation()} 在正式盘面上为空：预演与盘面不一致。");
            ImmutableArray<CapturedStone>.Builder stones = ImmutableArray.CreateBuilder<CapturedStone>(group.Size);
            foreach (Coord c in group.Stones)
            {
                if (!remaining.Remove(c, out CapturedStone stone))
                {
                    throw new SiegeRuleException($"预计提子不是整串：{group} 中的 {c.ToNotation()} 不在提子集合里。");
                }

                stones.Add(stone);
            }

            groups.Add(new CapturedGroup(group.Owner, stones.MoveToImmutable()));
        }

        return groups.ToImmutable();
    }

    private static ImmutableArray<GroupOutlook> OwnGroups(
        GameBoard projected, PlayerId player, ImmutableArray<Placement> placements, BatchFailure? failure, PowerSnapshot? after)
    {
        var placed = placements.Select(p => p.Coord).ToHashSet();
        HashSet<Coord> dead = failure is { Kind: BatchFailureKind.Suicide } f ? [.. f.Coords] : [];
        ImmutableArray<GroupPower> powers = after?.Of(player).Groups ?? [];

        return
        [
            .. projected.GroupsOf(player).Select(g => new GroupOutlook(
                g.Owner,
                g.Stones,
                projected.LibertiesOf(g),
                g.Stones.Any(placed.Contains),
                dead.Contains(g.Stones[0]),
                after is null ? null : powers.Single(p => p.Stones[0] == g.Stones[0]))),
        ];
    }
}
