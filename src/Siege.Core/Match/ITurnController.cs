using Siege.Core.Batch;
using Siege.Core.Recruit;

namespace Siege.Core.Match;

/// <summary>
/// 某玩家小回合的决策来源（UI、对战 AI 或调试 AI）。阶段机在每个需要决策的阶段调用对应方法，
/// 控制者通过传入的句柄操作；阶段推进本身由 <see cref="MatchRunner"/> 完成，控制者不能跳过或调换阶段。
/// </summary>
/// <remarks>
/// 人工接管（设计文档 §15.3）= 在任意阶段用 <see cref="MatchRunner.SetController"/> 替换某玩家的控制者；
/// 运行器在<b>每个阶段开始时</b>重新解析控制者，因此替换从下一阶段立即生效。
/// </remarks>
public interface ITurnController
{
    /// <summary>第 2 阶段：整理手牌。可主动整类弃牌；若 <paramref name="overflow"/> &gt; 0 必须弃到合法，否则征募阶段不会开始。</summary>
    void OrganizeHand(PlayerHandAccess hand, int overflow);

    /// <summary>第 3 阶段：私人征募。可按面板选取候选。</summary>
    void Recruit(PlayerHandAccess hand, RecruitPanelView panel);

    /// <summary>第 4 阶段：批次部署。暂放 0 至部署上限枚；留空即 Pass。</summary>
    void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse);

    /// <summary>
    /// 确认被拒绝（自杀手 / 同形等只在整批预演时暴露的原因）时的补救。返回 <c>true</c> 表示已调整暂放、请再次确认；
    /// 返回 <c>false</c> 则清空暂放并 Pass。
    /// </summary>
    bool OnRejected(StagedBatch batch, BatchFailure failure);
}
