using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Carry;

/// <summary>
/// 补给种类（carry-in-out「补给种类与开局效果」，design.md D2）。补给只改变持有者的<b>开局手牌</b>，
/// 不改部署上限、保护期落子范围、征募展示数 / 免费选取数、类型槽、先手或任何计分规则。
/// </summary>
/// <remarks>末尾追加式枚举：新种类只能加在末尾，既有值的编号与名字 MUST NOT 改动（存档、日志按名字写出）。</remarks>
public enum SupplyKind
{
    /// <summary>备用子：初始手牌多 1 枚普通子（共 6 枚）。</summary>
    SpareStone,

    /// <summary>征召签：初始 5 枚普通子中 1 枚换成随机候选类型（按基础权重抽取，<see cref="CarryCandidates.Draw"/>）。</summary>
    DraftLot,

    /// <summary>换型令：初始 5 枚普通子中 1 枚换成带入者指定的候选类型。</summary>
    Commission,
}

/// <summary>
/// 一名玩家的带入：补给种类，以及换入的棋子类型（换型令为指定类型；征召签为对局创建时抽得的类型，创建前为 <c>null</c>；备用子恒为 <c>null</c>）。
/// </summary>
public sealed record CarryIn(SupplyKind Kind, PieceType? Type = null);

/// <summary>补给的价格与开局效果（design.md D2）。价格是<b>未校准的初值</b>，不扫档。</summary>
public static class Supplies
{
    /// <summary>补给的固定次序（备用子、征召签、换型令）：AI 等概率抽种类、界面列出补给都按这个次序。</summary>
    public static readonly ImmutableArray<SupplyKind> Order = [SupplyKind.SpareStone, SupplyKind.DraftLot, SupplyKind.Commission];

    /// <summary>备用子的价格：<b>未校准的初值 3</b>。</summary>
    public const int UncalibratedSpareStonePrice = 3;

    /// <summary>征召签的价格：<b>未校准的初值 2</b>。</summary>
    public const int UncalibratedDraftLotPrice = 2;

    /// <summary>换型令的价格：<b>未校准的初值 4</b>。</summary>
    public const int UncalibratedCommissionPrice = 4;

    /// <summary>某补给的价格（补给点）。</summary>
    public static int PriceOf(SupplyKind kind) => kind switch
    {
        SupplyKind.SpareStone => UncalibratedSpareStonePrice,
        SupplyKind.DraftLot => UncalibratedDraftLotPrice,
        SupplyKind.Commission => UncalibratedCommissionPrice,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知补给种类。"),
    };

    /// <summary>
    /// 带入者的开局手牌（类型 → 枚数，按类型次序）：无带入 5 枚普通子；备用子 6 枚普通子；征召签 / 换型令 4 枚普通子 + 1 枚换入类型。
    /// 征召签须已解析出类型（<see cref="CarrySetup.Resolve"/>）。
    /// </summary>
    public static ImmutableArray<(PieceType Type, int Count)> InitialHand(CarryIn? carryIn)
    {
        const int basic = Recruit.HandLedger.InitialBasicCount;
        return carryIn switch
        {
            null => [(PieceType.Basic, basic)],
            { Kind: SupplyKind.SpareStone } => [(PieceType.Basic, basic + 1)],
            { Kind: SupplyKind.DraftLot or SupplyKind.Commission, Type: { } type } => [(PieceType.Basic, basic - 1), (type, 1)],
            { Kind: SupplyKind.DraftLot or SupplyKind.Commission } =>
                throw new ArgumentException($"{carryIn.Kind} 缺少换入类型（征召签须先解析）。", nameof(carryIn)),
            _ => throw new ArgumentOutOfRangeException(nameof(carryIn), carryIn.Kind, "未知补给种类。"),
        };
    }
}
