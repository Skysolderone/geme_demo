using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Recruit;

/// <summary>
/// 基础棋池权重（设计文档 §9.1）：普通子 40 / 堡垒子 20 / 连珠子 18 / 倍增子 12 / 协同子 10。全部玩家共用同一张表。
/// </summary>
/// <remarks>
/// 徽记调权不在这里重算：<see cref="EffectSnapshot.AdjustedWeight"/> 已把 <c>基础 × (1 + 0.75 × 数量)</c> 写成整数
/// <c>基础 × (4 + 3 × 数量)</c>（分母 4 对全部类型相同，归一化后消去）。归一化在抽取时由
/// <see cref="Determinism.RandomStream.WeightedPick"/> 的整数累积权重完成，不预存归一化概率（design.md D3）。
/// </remarks>
public static class RecruitWeights
{
    /// <summary>权重表的类型顺序，也是加权抽样的下标顺序。</summary>
    public static readonly PieceType[] Order =
    [
        PieceType.Basic,
        PieceType.Fortress,
        PieceType.Line,
        PieceType.Multiplier,
        PieceType.Synergy,
    ];

    private static readonly int[] BaseTable = [40, 20, 18, 12, 10];

    /// <summary>基础权重表，按 <see cref="Order"/> 排列。</summary>
    public static ReadOnlySpan<int> BaseWeights => BaseTable;

    /// <summary>某类型的基础权重。</summary>
    public static int BaseWeightOf(PieceType type) => BaseTable[IndexOf(type)];

    /// <summary>按快照的徽记数量调整后的权重表（按 <see cref="Order"/> 排列），供加权抽样直接使用。</summary>
    public static int[] AdjustedTable(EffectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        int[] table = new int[Order.Length];
        for (int i = 0; i < Order.Length; i++)
        {
            table[i] = snapshot.AdjustedWeight(Order[i], BaseTable[i]);
        }

        return table;
    }

    /// <summary>某类型在快照下的调整后权重。</summary>
    public static int AdjustedWeightOf(EffectSnapshot snapshot, PieceType type)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.AdjustedWeight(type, BaseWeightOf(type));
    }

    /// <summary>类型在 <see cref="Order"/> 中的下标。</summary>
    public static int IndexOf(PieceType type)
    {
        int index = Array.IndexOf(Order, type);
        return index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。");
    }
}
