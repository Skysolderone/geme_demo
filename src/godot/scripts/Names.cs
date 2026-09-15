using Siege.Core.Match;
using Siege.Presentation.Layers;

namespace Siege.Godot;

/// <summary>
/// 界面上几处纯文字映射：小回合阶段名、终局原因、信息层名。
/// 只做「枚举值 → 中文」，不含任何判断。
/// </summary>
/// <remarks>
/// 这几项在 <c>Siege.Presentation.Text.Labels</c> 里暂无对应条目（阶段 A 未覆盖），
/// 因此先落在 Godot 层；一旦 Labels 补齐就应迁走，避免出现第二套文案。见报告的待决项。
/// </remarks>
public static class Names
{
    /// <summary>小回合阶段。</summary>
    public static string Stage(TurnStage stage) => stage switch
    {
        TurnStage.Idle => "等待",
        TurnStage.RelicSnapshot => "信物结算",
        TurnStage.OrganizeHand => "整理手牌",
        TurnStage.Recruit => "征募",
        TurnStage.Deploy => "部署",
        TurnStage.Settlement => "结算",
        _ => stage.ToString(),
    };

    /// <summary>终局原因。</summary>
    public static string End(EndReason reason) => reason switch
    {
        EndReason.LastPlayerStanding => "只剩一名参赛玩家",
        EndReason.AllPassed => "一整轮所有人都 Pass",
        EndReason.BoardFull => "棋盘已无可落子空位",
        EndReason.MajorRoundLimit => "达到大回合上限",
        _ => reason.ToString(),
    };

    /// <summary>信息层。</summary>
    public static string Layer(TacticalLayer layer) => layer switch
    {
        TacticalLayer.Territory => "领地",
        TacticalLayer.Liberties => "气",
        TacticalLayer.Power => "势力",
        TacticalLayer.Relics => "信物",
        TacticalLayer.Order => "顺序",
        _ => layer.ToString(),
    };

    /// <summary>危险等级。</summary>
    public static string Danger(DangerLevel level) => level switch
    {
        DangerLevel.Safe => "安全",
        DangerLevel.Danger => "危险",
        DangerLevel.Urgent => "紧急",
        DangerLevel.NoLiberty => "无气",
        _ => level.ToString(),
    };
}
