using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>结算驱动器交给下游回调的上下文。<see cref="Board"/> 即正式盘面，回调发生时已完成本次全部盘面变更。</summary>
public sealed record SettlementContext(
    PlayerId Player,
    GameBoard Board,
    bool IsPass,
    int? Sequence,
    ImmutableArray<Placement> Placements,
    ImmutableArray<CapturedStone> Captures);

/// <summary>
/// 正式结算顺序（设计文档 §6.3）中由下游 change 提供的步骤。本层只按次序驱动，MUST NOT 实现其内部计算。
/// </summary>
/// <remarks>
/// 对应 design.md「接口契约」的输出：
/// 第 1 步 <see cref="DeductHand"/> = 手牌扣减请求（add-recruit-hand）；
/// 第 4 步 <see cref="OnRevealRelics"/>（add-relic-system）与第 5 步 <see cref="OnRecalculatePower"/>（add-territory-power）
/// 共同消费"盘面已变更"；第 6 步 <see cref="OnCheckEndConditions"/> = 结算完成（add-match-flow）；
/// <see cref="OnPass"/> = Pass 事件（add-recruit-hand 撤销本轮征募、add-match-flow 整轮 Pass 终局检查）。
/// </remarks>
public interface ISettlementHooks
{
    /// <summary>第 1 步：从手牌扣除已部署棋子。<paramref name="deployed"/> 只含本批次实际部署的类型与数量。</summary>
    void DeductHand(PlayerId player, IReadOnlyDictionary<PieceType, int> deployed);

    /// <summary>第 4 步：更新首次进入覆盖范围的信物并永久公开其内容。此时提子已完成。</summary>
    void OnRevealRelics(SettlementContext context);

    /// <summary>
    /// 第 5 步：重新计算信物控制、空格归属、棋串军势与总势力。
    /// Pass 也触发这一步（power-score 规格「Pass 也触发更新」），此时 <see cref="SettlementContext.IsPass"/> 为 <c>true</c>。
    /// </summary>
    void OnRecalculatePower(SettlementContext context);

    /// <summary>第 6 步：更新公开排名并检查出局与终局条件。Pass 也触发这一步（设计文档 §12.1）。</summary>
    void OnCheckEndConditions(SettlementContext context);

    /// <summary>Pass 事件：玩家确认 0 落子。本层不撤销征募，只发事件（design.md D6）。</summary>
    void OnPass(PlayerId player);
}
