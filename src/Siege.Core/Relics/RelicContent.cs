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
