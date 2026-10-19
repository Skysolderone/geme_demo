using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/match-telemetry —— Requirement: 对局日志的记录内容 第 2、8–11 项在 Core 记录结构里的落点
/// （<see cref="MatchFlow.LockedFlagCounts"/>、<see cref="PairingRecord"/>、<see cref="LinkRecord"/>、<see cref="BattlefieldSettlementRecord"/>、
/// <see cref="PlayerFlowState"/> 的出局原因 / 落败势力 / 等待大回合数、<see cref="EndReason"/>）。日志文件的写出属段 C（Sim），本段不改 Sim。
/// </summary>
public class 链接模式对局记录Tests
{
    [Fact(Skip = "open-map 段 B 删除")]
    public void 配对与链接可查()
    {
        // Scenario：一局四人四区 → 可查到第 3 大回合结束时的配对事件（两对）、两次链接事件及其地图摘要、决赛前的第三次链接。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(4);

        Assert.Equal(2, match.Pairings.Count);
        PairingRecord first = match.Pairings[0];
        Assert.Equal((PairingKind.FirstRound, 4, ScriptedPairingSource.ScriptSource), (first.Kind, first.MajorRound, first.Source));
        Assert.Equal(2, first.Pairs.Length);
        Assert.Null(first.Waiter);
        Assert.Equal(PairingKind.Final, match.Pairings[1].Kind);

        Assert.Equal(3, match.Links.Count);
        Assert.Equal([1, 1, 2], match.Links.Select(l => l.PairingSequence));
        Assert.Equal(MapFile.Digest(match.Board.LinkedMap), match.Links[^1].MapDigest);
        Assert.Equal(3, match.Board.LinkedMap.MapVersion);
        foreach (LinkRecord link in match.Links)
        {
            Assert.False(string.IsNullOrEmpty(link.MapDigest));
            Assert.True(link.SharedEdge >= 1);
            Assert.True(link.ShiftOf(link.X)!.Value.IsZero);          // 锚点零向量
            Assert.False(link.ShiftOf(link.Y)!.Value.IsZero);         // 被贴方必然移动
        }

        Assert.Equal([LinkSide.East, LinkSide.East, LinkSide.North], match.Links.Select(l => l.Side));

        Assert.Equal(match.Links.Select(l => l.MapDigest).Distinct().Count(), match.Links.Count);
    }

    [Fact]
    public void 战场结算可查()
    {
        // Scenario：某战场因整轮 Pass 结算 → 记录含战场标识、原因"整轮 Pass"、名次与胜者；出局区分原因并含出局时势力。
        MatchFlow match = Started([0, 0, 3, 4]).PlayRound(P0).PassRound();
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((2, 0, SettlementReason.AllPassed), (settled.MajorRound, settled.Battlefield, settled.Reason));
        Assert.Equal(P0, settled.Winner);
        Assert.Equal(match.StateOf(P1).PowerAtDefeat, settled.Entries[1].Power);
        Assert.Equal(EliminationReason.BattlefieldDefeat, match.StateOf(P1).EliminationReason);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 锁定旗数与等待时长可查()
    {
        // 第 2 项：锁定的出生棋盘及锁定时各棋盘的旗数；第 11 项：每名玩家作为等待者度过的大回合数、结束原因。
        MatchFlow match = Started([0, 0, 0, 2], new ScriptedPairingSource()).PlayRounds(5, P0);
        Assert.Equal(new Dictionary<int, int> { [0] = 3, [2] = 1 }, match.LockedFlagCounts);
        Assert.Equal(2, match.StateOf(P3).WaitingRounds);
        Assert.Equal(0, match.StateOf(P1).WaitingRounds);
    }
}
