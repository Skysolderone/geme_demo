using Siege.Core.Board;

namespace Siege.Core.Match;

/// <summary>对局顶层阶段。</summary>
public enum MatchPhase
{
    /// <summary>插旗阶段：地形 / 出生区 / 信物格位置公开，信物内容隐藏。</summary>
    FlagPlanting,

    /// <summary>对局进行中：大回合与小回合交替推进。</summary>
    InProgress,

    /// <summary>已结束：<see cref="MatchFlow.Result"/> 可读。</summary>
    Ended,
}

/// <summary>
/// 小回合的五个阶段（设计文档 §5，design.md D6）。做成显式状态而不是函数调用序列：
/// 强制弃牌门需要"停在整理手牌"这个可观察状态，UI 与人工接管都要能在任意阶段介入。
/// </summary>
public enum TurnStage
{
    /// <summary>不在任何小回合内（两个小回合之间、插旗中或已结束）。</summary>
    Idle,

    /// <summary>第 1 阶段：信物快照。读取此刻控制的非先锋信物，生成本小回合参数。</summary>
    RelicSnapshot,

    /// <summary>第 2 阶段：整理手牌。主动整类弃牌；超限必须在此解除。</summary>
    OrganizeHand,

    /// <summary>第 3 阶段：私人征募。</summary>
    Recruit,

    /// <summary>第 4 阶段：批次部署。暂放 0 至部署上限枚棋子。</summary>
    Deploy,

    /// <summary>第 5 阶段：确认结算或 Pass。</summary>
    Settlement,
}

/// <summary>三类终局条件（设计文档 §12.3）。</summary>
public enum EndReason
{
    /// <summary>条件 1：只剩一名参赛玩家，该玩家直接获胜。</summary>
    LastPlayerStanding,

    /// <summary>条件 2：连续 Pass 计数达到当前参赛人数（整轮 Pass）。</summary>
    AllPassed,

    /// <summary>条件 3：盘面不存在任何可落子的空格。</summary>
    BoardFull,
}

/// <summary>流程事件种类。事件<b>只由</b> <see cref="MatchFlow"/> 在阶段切换点发出，其他模块 MUST NOT 自行发出流程事件。</summary>
public enum FlowEventKind
{
    FlagsLocked,
    TurnStarted,
    StageEntered,
    TurnEnded,
    MajorRoundEnded,
    ProtectionLifted,
    PlayerEliminated,
    PlayerResigned,
    MatchEnded,
}

/// <summary>一条流程事件：种类、发生时的大回合、相关玩家（若有）与文字说明。</summary>
public sealed record FlowEvent(int Sequence, FlowEventKind Kind, int MajorRound, PlayerId? Player, string Detail)
{
    public override string ToString() => $"#{Sequence} R{MajorRound} {Kind}{(Player is { } p ? $" {p}" : string.Empty)}: {Detail}";
}
