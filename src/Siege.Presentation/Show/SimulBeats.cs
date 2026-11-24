using System.Collections.Immutable;
using Siege.Core.Match;
using Siege.Core.Simul;
using Siege.Presentation.Text;

namespace Siege.Presentation.Show;

/// <summary>
/// 新玩法一回合的演出节拍（simul-a D3 / J5）：先是冲突节拍（每格一条横幅 + 格上飘字，次序同 Core 报告里的冲突次序），
/// 再接现有的节拍生成（<see cref="SettlementBeats.Generate(MatchPublicView, MatchPublicView, Core.Batch.CaptureRecord?, bool)"/>：按结算前后两份公开快照的盘面之差得出落子、提子、势力与横幅）。
/// 纯函数：不读时钟、不消费随机、不调用任何规则计算入口；战力与骰点原样转录。
/// </summary>
public static class SimulBeats
{
    /// <summary>
    /// 由回合开始与结算后的两份投影快照（<c>SimulFlow.PublishFor</c> 的 <c>View</c>）与本回合报告生成节拍序列。没有冲突时与现有节拍生成的结果完全相同。
    /// </summary>
    public static ImmutableArray<SettlementBeat> Generate(MatchPublicView before, MatchPublicView after, SimulRoundReport report)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(report);
        ImmutableArray<SettlementBeat> settlement = SettlementBeats.Generate(before, after, record: null);
        if (report.Conflicts.IsEmpty)
        {
            return settlement;
        }

        var conflicts = new ConflictBeat(
        [
            .. report.Conflicts.Select(c => new ConflictCallout(c.Cell, Labels.SimulConflictBanner(c), Labels.SimulConflictCallout(c))),
        ]);
        return [conflicts, .. settlement];
    }
}
