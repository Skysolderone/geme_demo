using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Core.Simul;

/// <summary>
/// 新玩法按观察者投影出的一份载荷（simul-a A3-S1）：现有表现层只认 <see cref="MatchPublicView"/> + <see cref="PublicSupplement"/>（公开世界）、本人私有手牌与本人富预演，
/// 投影把新玩法的状态装进这些既有类型，图形侧照旧用 <c>ViewerWorld.Build(Viewer, View, Supplement, OwnHand, OwnPreview)</c> 组装观察者世界。
/// </summary>
/// <param name="Viewer">观察者。</param>
/// <param name="View">公开快照（与 <paramref name="Supplement"/> 同一时刻）。</param>
/// <param name="Supplement">公开补充载荷。</param>
/// <param name="OwnHand">观察者本人的私有手牌视图。</param>
/// <param name="OwnPreview">观察者本人此刻的暂放预演；只在本人准备中且尚未提交时有，否则为 <c>null</c>。</param>
/// <param name="OwnCheck">与 <paramref name="OwnPreview"/> 同时给出的本人校验结果（失败原因由界面直接显示文字，D4）。</param>
/// <param name="ZoneOwners">出生座位 → 玩家（插旗锁定后才有）。</param>
public sealed record SimulViewerPayload(
    PlayerId Viewer,
    MatchPublicView View,
    PublicSupplement Supplement,
    HandPrivateView OwnHand,
    BatchPreview? OwnPreview,
    SimulBatchCheck? OwnCheck,
    ImmutableSortedDictionary<int, PlayerId> ZoneOwners);

/// <summary>
/// 新玩法的观察者投影（simul-a A3-S1）。只读对局状态、只调用既有唯一实现，盘面一律取副本；零副作用。
/// </summary>
/// <remarks>
/// <para>满足 <c>ViewerWorld.Build</c> 的全部不变量：公开快照与补充载荷同一份盘面副本、同一回合号；手牌与预演只属于观察者；
/// 有预演时公开快照里观察者就是"当前行动玩家"且处于部署阶段（新玩法里全员同时准备，这一项是按观察者投影的）。</para>
/// <para>暂放只进观察者本人的预演，公开快照的盘面是正式盘面——别人的暂放在结构上进不来。</para>
/// <para>势力换成新玩法口径（活形内部格归所有者，<see cref="SimulRules.Power"/>）：<see cref="PlayerPower"/> 的独占 / 计分格与总势力都用修正后的值，名次按修正后的总势力重排。
/// 覆盖表沿用现有算法的那一份，所以盘面层着色与修正后的领地可能不一致（A3 裁决 D9，接受）。</para>
/// </remarks>
public static class SimulProjection
{
    /// <summary>按观察者 <paramref name="viewer"/> 投影 <paramref name="flow"/> 此刻的状态。</summary>
    public static SimulViewerPayload For(SimulFlow flow, PlayerId viewer)
    {
        ArgumentNullException.ThrowIfNull(flow);
        SimulFlow.Seat seat = flow.SeatOf(viewer);

        GameBoard board = flow.Board.Clone();
        string serialized = board.Serialize();
        bool preparing = flow.Phase == SimulPhase.InProgress && flow.Stage == SimulStage.Prep
            && seat.Status == PlayerStatus.Active && !seat.Submitted;

        MatchPhase phase = flow.Phase switch
        {
            SimulPhase.FlagPlanting => MatchPhase.FlagPlanting,
            SimulPhase.InProgress => MatchPhase.InProgress,
            _ => MatchPhase.Ended,
        };

        PowerSnapshot? power = flow.Power is { } p ? Corrected(p) : null;
        ImmutableArray<PlayerFlowState> players = [.. flow.Players.Select(id => StateOf(flow, id))];
        var view = new MatchPublicView(
            board.BaseMap.Id,
            flow.Seed.ToString(),
            phase,
            flow.Round,
            flow.Options.ArtisanWeight,
            flow.Options.ContentSet,
            preparing ? TurnStage.Deploy : TurnStage.Idle,
            preparing ? viewer : null,
            flow.Order,
            players,
            board,
            serialized,
            power,
            RelicForecast: null,
            flow.Hands.PublicViews(),
            PassStreak: 0,
            Result: null,
            LifeShapeReport.Analyze(board),
            CarryInOut: false)
        {
            RoundCap = flow.RoundCap,
            ScoringVersion = flow.Options.ScoringVersion,
            Tier = MapTier.Small,
            RoundCapIsDefault = flow.RoundCap == MatchOptions.DefaultRoundCapFor(MapTier.Small),
        };

        var supplement = new PublicSupplement(serialized, flow.Round, LibertySnapshot.Compute(board), Structures(flow), CurrentOrderReport: null, NextOrderForecast: null);

        SimulBatchCheck? check = preparing ? flow.Check(viewer) : null;
        BatchPreview? preview = preparing ? Preview(flow, viewer, seat, check!) : null;

        ImmutableSortedDictionary<int, PlayerId> zones = flow.Players
            .Where(id => flow.SeatOf(id).BirthZone is not null)
            .GroupBy(id => flow.SeatOf(id).BirthZone!.Value)
            .ToImmutableSortedDictionary(g => g.Key, g => g.Min());

        return new SimulViewerPayload(viewer, view, supplement, flow.Hands.PrivateViewOf(viewer), preview, check, zones);
    }

    /// <summary>把新玩法势力装回既有的势力快照：独占 / 计分格与总势力换成修正后的值，棋串军势原样，名次按修正后的总势力重排（与现有名次同一实现）。</summary>
    internal static PowerSnapshot Corrected(SimulPowerSnapshot power)
    {
        ImmutableArray<PlayerPower> players =
        [
            .. power.Players.Select(p => new PlayerPower(p.Player, p.Status, p.Territory, p.ScoredTerritory, p.Base.Groups, p.Total)),
        ];
        return new PowerSnapshot(power.Base.Coverage, players, PowerCalculator.Rank(players));
    }

    private static PlayerFlowState StateOf(SimulFlow flow, PlayerId player)
    {
        SimulFlow.Seat seat = flow.SeatOf(player);
        BigInteger total = flow.Power?.Of(player).Total ?? BigInteger.Zero;
        int position = flow.Order.IndexOf(player);
        return new PlayerFlowState(
            player,
            seat.Status,
            HasEstablishedPower: total > BigInteger.Zero,
            seat.BirthZone,
            LastRoundPosition: position >= 0 ? position : null,
            EliminationOrder: null,
            EliminatedInMajorRound: null,
            seat.ResignedInRound,
            seat.Status == PlayerStatus.Resigned ? seat.PowerAtResign : null);
    }

    /// <summary>结构参数：展示数 / 选取数 / 类型槽取默认基础值，部署上限恒为 <see cref="SimulRules.DeployLimit"/>；认输者为 <c>null</c>。</summary>
    private static ImmutableArray<PlayerStructure> Structures(SimulFlow flow) =>
    [
        .. flow.Players.Select(p => flow.SeatOf(p).Status != PlayerStatus.Active
            ? new PlayerStructure(p, flow.SeatOf(p).Status, null)
            : new PlayerStructure(p, PlayerStatus.Active, new StructureParameters(
                new StructureParameter(EffectSnapshot.BaseRevealCount, EffectSnapshot.BaseRevealCount),
                new StructureParameter(EffectSnapshot.BaseFreePickCount, EffectSnapshot.BaseFreePickCount),
                new StructureParameter(EffectSnapshot.BaseTypeSlots, EffectSnapshot.BaseTypeSlots),
                new StructureParameter(SimulRules.DeployLimit, SimulRules.DeployLimit)))),
    ];

    /// <summary>
    /// 本人暂放的预演：只装暂放、手牌消耗、部署上限与匠人的可改造目标（按回合开始盘面与改造范围），合法性取新玩法校验；
    /// 不预测提子与势力变化（新玩法要等揭晓才知道冲突结果）。<see cref="BatchPreview.IsPass"/> 恒为 <c>false</c>：0 枚不撤销征召，不能触发现有的"Pass 撤销征召"警示。
    /// </summary>
    private static BatchPreview Preview(SimulFlow flow, PlayerId viewer, SimulFlow.Seat seat, SimulBatchCheck check)
    {
        ImmutableArray<Placement> placements = [.. seat.Staged];
        HandPrivateView hand = flow.Hands.PrivateViewOf(viewer);
        ImmutableArray<HandCost> costs =
        [
            .. placements.GroupBy(p => p.Type).OrderBy(g => g.Key).Select(g => new HandCost(g.Key, g.Count(), hand.CountOf(g.Key))),
        ];

        GameBoard s0 = flow.RoundStartBoard;
        IReadOnlySet<Coord>? scope = null;
        ImmutableArray<EditOutlook>.Builder edits = ImmutableArray.CreateBuilder<EditOutlook>();
        foreach (Placement p in placements.Where(p => p.Type == TerrainEditRules.EditorType))
        {
            scope ??= PublicRules.EditScope(s0, seat.BirthZone!.Value, flow.Round);
            edits.Add(new EditOutlook(p.Coord, p.Edit, TerrainEditRules.LegalTargets(s0.Map, p.Coord, scope)));
        }

        return new BatchPreview(viewer, check.IsValid, IsPass: false, Failure: null, placements, costs, SimulRules.DeployLimit,
            [], [], [], [], edits.ToImmutable());
    }
}
