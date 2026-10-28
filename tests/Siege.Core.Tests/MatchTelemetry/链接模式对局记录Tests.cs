using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/match-telemetry —— Requirement: 对局日志的记录内容 第 2、8–11 项在 Core 记录结构里的落点
/// （<see cref="MatchFlow.LockedFlagCounts"/>、<see cref="BattlefieldSettlementRecord"/>（配对记录 / 链接记录已随 open-map 段 B 删除）、
/// <see cref="PlayerFlowState"/> 的出局原因 / 落败势力 / 等待大回合数、<see cref="EndReason"/>）。日志文件的写出属段 C（Sim），本段不改 Sim。
/// </summary>
public class 链接模式对局记录Tests
{
    [Fact]
    public void 战场结算可查()
    {
        // Scenario：某战场因整轮 Pass 结算 → 记录含战场标识、原因"整轮 Pass"、名次与胜者；出局区分原因并含出局时势力。
        MatchFlow match = Started([0, 0, 3, 2]).PlayRound(P0).PassRound();
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((2, 0, SettlementReason.AllPassed), (settled.MajorRound, settled.Battlefield, settled.Reason));
        Assert.Equal(P0, settled.Winner);
        Assert.Equal(match.StateOf(P1).PowerAtDefeat, settled.Entries[1].Power);
        Assert.Equal(EliminationReason.BattlefieldDefeat, match.StateOf(P1).EliminationReason);
    }

}
