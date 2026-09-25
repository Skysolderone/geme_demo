using Siege.Core.Board;

namespace Siege.Core.Relics;

/// <summary>六类原型信物（设计文档 §8.1）。前五类是结构信物，第六类流派徽记绑定一种棋子类型。</summary>
public enum RelicType
{
    /// <summary>探勘：征募展示数 +1。</summary>
    Prospecting,

    /// <summary>征召：免费选取数 +1。</summary>
    Conscription,

    /// <summary>兵站：手牌类型槽 +1。</summary>
    Depot,

    /// <summary>军令：部署上限 +1。</summary>
    Command,

    /// <summary>先锋：先手修正 +1。只在大回合结束时读取，不进小回合快照。</summary>
    Vanguard,

    /// <summary>流派徽记：提高绑定棋子类型的征募权重。</summary>
    SchoolEmblem,

    // more-pieces-relics D9：以下四类只在末尾追加（存档可能按整数写枚举），都没有 +2 版本（relic-effects「六类原型信物的效果」）。

    /// <summary>连营（计分信物）：持有者每条长度 L ≥ 2 的连珠线额外 +L 位置加值。势力计算时按当前控制读取，不进快照。</summary>
    Encampment,

    /// <summary>犄角（计分信物）：持有者协同子每种类型的加值 +1。势力计算时按当前控制读取，不进快照。</summary>
    Pincer,

    /// <summary>驿站：持有者每控制一枚其他信物，征募展示数 +1（小回合开始的效果快照读取）。</summary>
    Relay,

    /// <summary>工坊：持有者匠人的格改造目标可隔一格（快照读取，不叠加）。</summary>
    Workshop,
}

/// <summary>
/// 一枚信物的内容：类型、强度、以及流派徽记绑定的棋子类型。开局生成后不再变化。
/// </summary>
/// <remarks>
/// <see cref="Magnitude"/> 对结构信物是效果值（+1 / +2）；对流派徽记是<b>数量</b>（普通 1、高阶 2），
/// 直接代入 §9.1 的 <c>基础权重 × (1 + 0.75 × 徽记数量)</c>（design.md D6）。
/// </remarks>
public readonly record struct RelicContent
{
    public RelicContent(RelicType type, int magnitude, PieceType? emblemPiece = null)
    {
        if (magnitude is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(magnitude), magnitude, "信物强度只有 +1 与 +2 两档。");
        }

        if (magnitude == 2 && !HasAdvancedTier(type))
        {
            throw new ArgumentOutOfRangeException(nameof(magnitude), magnitude, $"{type} 没有高阶版，强度只能为 +1（more-pieces-relics D7）。");
        }

        if (type == RelicType.SchoolEmblem && emblemPiece is null)
        {
            throw new ArgumentException("流派徽记 MUST 绑定一种具体棋子类型。", nameof(emblemPiece));
        }

        if (type != RelicType.SchoolEmblem && emblemPiece is not null)
        {
            throw new ArgumentException($"{type} 不是流派徽记，不得绑定棋子类型。", nameof(emblemPiece));
        }

        Type = type;
        Magnitude = magnitude;
        EmblemPiece = emblemPiece;
    }

    /// <summary>
    /// 该类信物是否有高阶版（+2 / 双倍徽记）：原有六类有，连营、犄角、驿站、工坊没有（more-pieces-relics D7）。
    /// 生成器的升级判定与本构造的强度校验都读这里，是"哪些类型可升级"的唯一定义。
    /// </summary>
    public static bool HasAdvancedTier(RelicType type) => type switch
    {
        RelicType.Prospecting or RelicType.Conscription or RelicType.Depot or RelicType.Command or RelicType.Vanguard or RelicType.SchoolEmblem => true,
        RelicType.Encampment or RelicType.Pincer or RelicType.Relay or RelicType.Workshop => false,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知信物类型。"),
    };

    /// <summary>信物类型。</summary>
    public RelicType Type { get; }

    /// <summary>强度：结构信物为效果值，流派徽记为等效数量。</summary>
    public int Magnitude { get; }

    /// <summary>流派徽记绑定的棋子类型；其他类型为 <c>null</c>。</summary>
    public PieceType? EmblemPiece { get; }

    /// <summary>是否高阶（效果 +2 或双倍徽记）。出生区 MUST NOT 出现高阶。</summary>
    public bool IsAdvanced => Magnitude == 2;

    /// <summary>是否结构信物（非流派徽记）。</summary>
    public bool IsStructural => Type != RelicType.SchoolEmblem;

    public override string ToString() =>
        Type == RelicType.SchoolEmblem ? $"{Type}x{Magnitude}({EmblemPiece})" : $"{Type}+{Magnitude}";
}
