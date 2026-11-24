using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Presentation.Text;

/// <summary>
/// 新玩法（simul-a，同时决策）图形版的界面文案：按钮、按键提示、座位状态、提示语。只做"值 → 文字"的映射，不做规则判断；
/// 文案用"单位 / 整体 / 岛 / 部署"，不写"棋盘 / 棋子"。冲突、终局原因等与规则记录相关的文案在 <see cref="Labels"/>。
/// </summary>
public static class SimulTexts
{
    /// <summary>提交按钮。</summary>
    public const string Submit = "提交 [Enter]";

    /// <summary>本回合不部署（提交 0 个单位）。</summary>
    public const string Skip = "本回合不部署 [P]";

    /// <summary>撤回提交。</summary>
    public const string Withdraw = "撤回提交 [Backspace]";

    /// <summary>清空暂放。</summary>
    public const string Clear = "清空暂放 [C]";

    /// <summary>认输。</summary>
    public const string Resign = "认输";

    /// <summary>打开征召面板。</summary>
    public const string OpenRecruit = "征召";

    /// <summary>收起征召面板。</summary>
    public const string CloseRecruit = "完成征召";

    /// <summary>征召面板标题。</summary>
    public const string RecruitTitle = "征召（候选在回合开始时已定）";

    /// <summary>准备阶段的按键提示。</summary>
    public const string KeysHint = "点格暂放 · 右键撤回一枚 · E 换改造目标 · Enter 提交 · P 本回合不部署 · C 清空暂放 · Backspace 撤回提交";

    /// <summary>本人已提交、等待其他玩家时的提示。</summary>
    public const string WaitingOthers = "已提交，等待其他玩家";

    /// <summary>本人已认输时的提示。</summary>
    public const string Resigned = "你已认输：单位留在场上，不再部署";

    /// <summary>终局面板标题。</summary>
    public const string ResultTitle = "对局结束";

    /// <summary>选座阶段的提示（D6：复用 3D 座位点选）。</summary>
    public const string PickSeat = "点一个带编号的出生座位开始新玩法（选中即锁定）";

    /// <summary>手牌里某类的一行：名称与数量。</summary>
    public static string HandRow(string name, int count) => $"{name} ×{count}";

    /// <summary>一名玩家本回合的状态行（对手只显示提交状态，Q09）。</summary>
    public static string SeatLine(PlayerId player, PlayerStatus status, bool submitted) =>
        status == PlayerStatus.Active ? $"{Labels.Player(player)}：{Labels.SimulSubmitted(submitted)}" : $"{Labels.Player(player)}：已认输";

    /// <summary>某回合结算后的提示：冲突数、落下与移除的单位数。</summary>
    public static string RoundResolved(int round, int conflicts, int landed, int removed) =>
        $"第 {round} 回合揭晓：冲突 {conflicts} 处，落下 {landed} 个单位，移除 {removed} 个单位";

    /// <summary>终局面板的一行名次。</summary>
    public static string PlacingRow(int place, PlayerId player, bool winner) => $"第 {place} 名　{Labels.Player(player)}{(winner ? "（胜者）" : string.Empty)}";

    /// <summary>终局面板的原因行。</summary>
    public static string ResultReason(Core.Simul.SimulEndReason reason, int round) => $"第 {round} 回合终局：{Labels.SimulEndReason(reason)}";
}
