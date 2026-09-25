namespace Siege.Core.Scoring;

/// <summary>
/// 一名玩家在本次势力计算中<b>控制</b>的计分信物枚数（more-pieces-relics D3，relic-effects「计分信物：连营与犄角」）。
/// 由 <see cref="PowerCalculator"/> 按"已知信物内容 + 同一次计算的覆盖表 + 名册"逐次算出，不进效果快照、不跨计算保留。
/// </summary>
/// <param name="Encampments">连营枚数 k：每条长度 L ≥ 2 的连珠线额外 <c>+k × L</c>（并入连珠加值）。</param>
/// <param name="Pincers">犄角枚数 k：协同子每种类型的加值由 2 变为 <c>2 + k</c>（并入协同加值）。</param>
public readonly record struct ScoringRelicCounts(int Encampments, int Pincers)
{
    /// <summary>不控制任何计分信物（旧签名、v1 对局、未传已知内容时）。</summary>
    public static readonly ScoringRelicCounts None = default;
}
