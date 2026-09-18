using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>确认批次的结果。拒绝时 <see cref="Failure"/> 非空、暂放状态保留；接受的非 Pass 批次附 <see cref="CaptureRecord"/>。</summary>
public sealed record SettlementOutcome(
    bool Confirmed,
    bool IsPass,
    BatchFailure? Failure,
    CaptureRecord? CaptureRecord);

/// <summary>
/// 正式结算顺序（设计文档 §6.3 + artisan-terrain-edit「正式结算顺序」七步）的<b>唯一</b>实现：
/// 扣手牌 → 放置 → <b>应用改造</b> → 提子 → 信物揭示 → 控制与势力重算 → 排名与终局。
/// 下游 change 只作为被驱动的步骤，MUST NOT 各自"就近"实现结算。
/// </summary>
/// <remarks>
/// <para>确认时 MUST 重跑一次完整预演，不信任任何缓存的预演结果（裁决记录 1）；预演副本不会被提升为正式盘面，
/// 正式盘面由同一组放置与移除操作重新写入，并与副本逐字节核对（design.md D1）。</para>
/// <para>围杀不产生通用奖励：本类不持有、不修改任何资源计数，对外只有一次手牌扣减请求。</para>
/// </remarks>
public sealed class SettlementDriver
{
    private readonly ISettlementHooks _hooks;

    public SettlementDriver(GameBoard board, BoardHistory history, ISettlementHooks hooks)
    {
        Board = board ?? throw new ArgumentNullException(nameof(board));
        History = history ?? throw new ArgumentNullException(nameof(history));
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
    }

    /// <summary>正式盘面。</summary>
    public GameBoard Board { get; }

    /// <summary>已提交盘面集合。随对局状态持久化。</summary>
    public BoardHistory History { get; }

    /// <summary>合法性预演：对正式盘面零副作用，可在每次暂放变更后调用。</summary>
    public RehearsalResult Rehearse(BatchContext context, IReadOnlyList<Placement> placements) =>
        BatchRehearsal.Rehearse(Board, context, placements, History);

    /// <summary>确认暂放批次。接受后清空暂放；拒绝则保留全部暂放供玩家继续调整。</summary>
    public SettlementOutcome Confirm(StagedBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!ReferenceEquals(batch.Board, Board))
        {
            throw new ArgumentException("暂放批次校验的盘面与结算驱动器的正式盘面不是同一个。", nameof(batch));
        }

        SettlementOutcome outcome = Confirm(batch.Context, batch.Placements);
        if (outcome.Confirmed)
        {
            batch.Clear();
        }

        return outcome;
    }

    /// <summary>确认一个批次。整批生效或整批不生效；0 落子即 Pass。</summary>
    public SettlementOutcome Confirm(BatchContext context, IReadOnlyList<Placement> placements)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(placements);

        // 裁决记录 1：确认时重跑完整预演，不接受调用方传入的预演结果。
        RehearsalResult rehearsal = BatchRehearsal.Rehearse(Board, context, placements, History);
        if (!rehearsal.IsLegal)
        {
            return new SettlementOutcome(Confirmed: false, IsPass: false, rehearsal.Failure, CaptureRecord: null);
        }

        if (rehearsal.IsPass)
        {
            // Pass：不扣手牌、不改盘面、不记入已提交盘面集合、不揭示信物（盘面未变，不可能有新格进入覆盖）。
            // 但仍要走第 5、6 步：power-score 规格「Pass 也触发更新」要求 Pass 后执行一次势力重算与排名更新，
            // 设计文档 §12.1 要求"每次合法批次结算或 Pass 完成后"都做出局检查。
            var passContext = new SettlementContext(context.Player, Board, IsPass: true, Sequence: null, [], []);
            _hooks.OnPass(context.Player);
            _hooks.OnRecalculatePower(passContext);
            _hooks.OnCheckEndConditions(passContext);
            return new SettlementOutcome(Confirmed: true, IsPass: true, Failure: null, CaptureRecord: null);
        }

        ImmutableArray<Placement> ordered = [.. placements];
        ImmutableArray<CapturedStone> captures = rehearsal.Captures;

        // 改造是否"直接导致提子"：在改盘之前、按本批开始前的地形算好（口径见 AppliedTerrainEdit.CausedCapture）。
        ImmutableArray<AppliedTerrainEdit> edits = AttributeEdits(context, ordered, captures);

        // 1. 从手牌扣除已部署棋子
        _hooks.DeductHand(context.Player, CountByType(ordered));

        // 2. 将整个批次加入正式盘面
        foreach (Placement placement in ordered)
        {
            Board.Place(placement.Coord, context.Player, placement.Type);
        }

        // 3. 同时应用本批次的全部改造（§6.3 + terrain-edit「改造先于提子生效」）。
        // 与第 4 步调换顺序会让"立栅致提子"失效——预演副本与正式盘面的 Serialize 比对会当场抓住。
        Board.ApplyTerrainEdits(BatchRehearsal.EditsOf(ordered));

        // 4. 同时移除所有无气的敌方棋串（集合在预演中一次性算出）
        Board.RemoveStones(captures.Select(s => s.Coord));

        string settled = Board.Serialize();
        if (!string.Equals(settled, rehearsal.ProjectedBoard!.Serialize(), StringComparison.Ordinal))
        {
            throw new SiegeRuleException("正式结算结果与预演副本不一致：盘面在预演与确认之间被修改。");
        }

        int sequence = History.Record(settled);
        var record = new CaptureRecord(sequence, context.Player, ordered, captures, edits);
        var settlementContext = new SettlementContext(context.Player, Board, IsPass: false, sequence, ordered, captures);

        // 5. 更新首次进入覆盖范围的信物并永久公开其内容
        _hooks.OnRevealRelics(settlementContext);

        // 6. 重新计算信物控制、空格归属、棋串军势与总势力
        _hooks.OnRecalculatePower(settlementContext);

        // 7. 更新公开排名并检查出局与终局条件
        _hooks.OnCheckEndConditions(settlementContext);

        return new SettlementOutcome(Confirmed: true, IsPass: false, Failure: null, record);
    }

    /// <summary>
    /// 给本批次的每次改造标注"是否直接导致提子"：把该条改造单独去掉后重跑放置 + 提子，无气敌串集合<b>严格变小</b>即为 <c>true</c>。
    /// 只在正式确认路径上算（每批至多一次，且只在真有改造时才做），MUST NOT 放进 <see cref="Rehearse"/> ——
    /// AI 每小回合调预演成千上万次。
    /// </summary>
    private ImmutableArray<AppliedTerrainEdit> AttributeEdits(
        BatchContext context, ImmutableArray<Placement> placements, ImmutableArray<CapturedStone> captures)
    {
        ImmutableArray<Placement> withEdits = [.. placements.Where(p => p.Edit is not null)];
        if (withEdits.IsEmpty)
        {
            return [];
        }

        ImmutableArray<AppliedTerrainEdit>.Builder builder = ImmutableArray.CreateBuilder<AppliedTerrainEdit>(withEdits.Length);
        foreach (Placement one in withEdits)
        {
            GameBoard probe = Board.Clone();
            foreach (Placement placement in placements)
            {
                probe.Place(placement.Coord, context.Player, placement.Type);
            }

            probe.ApplyTerrainEdits(placements.Where(p => p.Edit is not null && p.Coord != one.Coord).Select(p => p.Edit!.Value));
            int without = CaptureResolver.FindCaptured(probe, context.Player).Length;
            builder.Add(new AppliedTerrainEdit(one.Edit!.Value, one.Coord, without < captures.Length));
        }

        return builder.MoveToImmutable();
    }

    private static IReadOnlyDictionary<PieceType, int> CountByType(ImmutableArray<Placement> placements)
    {
        var counts = new SortedDictionary<PieceType, int>();
        foreach (Placement placement in placements)
        {
            counts[placement.Type] = counts.TryGetValue(placement.Type, out int n) ? n + 1 : 1;
        }

        return counts;
    }
}
