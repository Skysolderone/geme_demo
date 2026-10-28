using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Recruit;

/// <summary>
/// 基础棋池权重（设计文档 §9.1）：普通子 40 / 堡垒子 20 / 连珠子 18 / 倍增子 12 / 协同子 10 / 匠人 10，
/// 旗手子 / 铁链子 / 哨兵子 / 界碑子各 8（more-pieces-relics，<b>未校准</b>）。全部玩家共用同一张表。
/// </summary>
/// <remarks>
/// <para>征募只按基础权重（relic-redesign D4：流派徽记调权随旧信物删除）。抽样表是基础表的 <see cref="SamplingScale"/> 倍（<see cref="SamplingTable"/>）：
/// 整数倍不改变任何类型的概率，保留这个倍数只是让 <c>recruit</c> 子流的抽样结果与删除徽记之前（无徽记时分子恒为 4）逐位相同。
/// 归一化在抽取时由 <see cref="Determinism.RandomStream.WeightedPick"/> 的整数累积权重完成，不预存归一化概率（design.md D3）。</para>
/// <para>匠人权重是对局配置（artisan-terrain-edit R-2，待扫档校准）：不传 <c>artisanWeight</c> 的重载取
/// <see cref="DefaultArtisanWeight"/>；传入值只替换匠人那一档，其余各档 MUST NOT 随之变化。</para>
/// <para>对局内容集（more-pieces-relics D8）：v1 的棋池只含原六种、表与引入新棋子之前相同；v2 十种。v1 的类型次序与权重表恰是 v2 的前六项
/// （<see cref="ContentSets.PieceTypesOf"/>），所以抽样下标在两个内容集之间通用、<see cref="Order"/> 可直接按下标取类型。
/// 不带内容集的重载取 <see cref="ContentSets.Default"/>（与 <see cref="Match.MatchOptions"/> 的缺省一致）。</para>
/// </remarks>
public static class RecruitWeights
{
    /// <summary>权重表的类型顺序（全部十种 = 内容集 v2），也是加权抽样的下标顺序；v1 取其前六项。</summary>
    public static readonly PieceType[] Order = [.. ContentSets.PieceTypesOf(ContentSet.V2)];

    /// <summary>匠人的默认征募权重（artisan-terrain-edit 裁决 T-1：与协同子同档 10；扫档对照档 5 / 18）。</summary>
    public const int DefaultArtisanWeight = 10;

    /// <summary>
    /// 旗手子、铁链子、哨兵子、界碑子的征募权重：<b>未校准的初值 8</b>（more-pieces-relics 负责人裁决，不扫档）。
    /// 不可配置——对局配置与跑局配置里都没有对应项（testing.md「标着待校准的初值不是基线」）。
    /// </summary>
    public const int UncalibratedNewPieceWeight = 8;

    private static readonly int[] BaseTable =
    [
        40, 20, 18, 12, 10, DefaultArtisanWeight,
        UncalibratedNewPieceWeight, UncalibratedNewPieceWeight, UncalibratedNewPieceWeight, UncalibratedNewPieceWeight,
    ];

    /// <summary>默认配置（内容集 v2）下的基础权重表，按 <see cref="Order"/> 排列。</summary>
    public static ReadOnlySpan<int> BaseWeights => BaseTable;

    /// <summary>某内容集的棋池类型次序：v1 六种、v2 十种（<see cref="ContentSets.PieceTypesOf"/>，v1 是 v2 的前缀）。</summary>
    public static ImmutableArray<PieceType> OrderOf(ContentSet set) => ContentSets.PieceTypesOf(set);

    /// <summary>某内容集在默认匠人权重下的基础权重表，按 <see cref="OrderOf"/> 排列（v1 = 前六项）。</summary>
    public static ReadOnlySpan<int> BaseWeightsOf(ContentSet set) => BaseTable.AsSpan(0, OrderOf(set).Length);

    /// <summary>某类型在默认配置下的基础权重。</summary>
    public static int BaseWeightOf(PieceType type) => BaseTable[IndexOf(type)];

    /// <summary>某类型在指定匠人权重下的基础权重：只有匠人那一档随配置变化。</summary>
    public static int BaseWeightOf(PieceType type, int artisanWeight)
    {
        RequireValidArtisanWeight(artisanWeight);
        return type == PieceType.Artisan ? artisanWeight : BaseTable[IndexOf(type)];
    }

    /// <summary>
    /// 抽样表相对基础权重的整数倍数：4。删除徽记之前抽样表是 <c>基础 × (4 + 3 × 徽记数)</c>，无徽记时恰为基础 × 4；
    /// 保留这个倍数使征募抽样逐位不变（整数倍不改变概率）。
    /// </summary>
    public const int SamplingScale = 4;

    /// <summary>
    /// 征募抽样表（按 <see cref="OrderOf"/> 排列）：基础权重 × <see cref="SamplingScale"/>，匠人取对局配置权重；供加权抽样直接使用。
    /// 表长 = 该内容集的类型数：v1 六档（新四种根本不在池中，而不是权重为 0）。
    /// </summary>
    public static int[] SamplingTable(int artisanWeight, ContentSet contentSet)
    {
        RequireValidArtisanWeight(artisanWeight);
        ImmutableArray<PieceType> order = OrderOf(contentSet);
        int[] table = new int[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            table[i] = checked(BaseWeightOf(order[i], artisanWeight) * SamplingScale);
        }

        return table;
    }

    /// <summary>匠人权重须为非负整数（0 = 不在池中）。</summary>
    public static void RequireValidArtisanWeight(int artisanWeight)
    {
        if (artisanWeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(artisanWeight), artisanWeight, "匠人征募权重不得为负。");
        }
    }

    /// <summary>类型在 <see cref="Order"/> 中的下标。</summary>
    public static int IndexOf(PieceType type)
    {
        int index = Array.IndexOf(Order, type);
        return index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。");
    }
}
