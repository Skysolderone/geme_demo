namespace Siege.Core.Scoring;

/// <summary>
/// 玩家的参赛状态。权威来源是流程层（add-match-flow），本层只作为显式输入接收，不持有、不推断。
/// </summary>
/// <remarks>
/// 设计文档 §12.2 / openspec design.md D7：已弃赛与已出局玩家的遗留棋子<b>照常</b>参与覆盖与势力计算；
/// 状态过滤只发生在"势力名次"一处（排除已弃赛与已出局），MUST NOT 提前到覆盖或军势计算。
/// </remarks>
public enum PlayerStatus
{
    /// <summary>参赛中。</summary>
    Active,

    /// <summary>已弃赛：势力照常计算与显示并标记，但不参与名次。</summary>
    Resigned,

    /// <summary>已出局：不参与名次。</summary>
    Eliminated,
}
