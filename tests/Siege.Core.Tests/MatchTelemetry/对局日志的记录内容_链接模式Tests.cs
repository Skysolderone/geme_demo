using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/match-telemetry —— Requirement: 对局日志的记录内容（链接模式写出：第 1 项模式与无冒险概率、第 2 项锁定旗数、
/// 第 8 项配对事件、第 9 项链接事件、第 10 项战场结算、第 11 项出局原因 / 落败势力 / 等待时长 / 结束原因）。实现裁决 C3：这些字段只在链接模式的局写出。tasks 3.1。
/// </summary>
/// <remarks>
/// 样本：真实 <see cref="MatchSession"/> 驱动的链接模式对局（<c>link:1</c>、脚本固定配对），控制者第 1 大回合各落 1 子、此后一律 Pass——
/// 使每个多人战场在确定的大回合结算、落败者势力大于 0（写入端漏写抓得到，testing.md「期望值是 0 / null 的遥测断言抓不到写入端漏写」）。
/// </remarks>
public class 对局日志的记录内容_链接模式Tests
{
    /// <summary>第 <c>round</c> 大回合落一枚匠人并立它的第一道合法栅栏，其余小回合 Pass。</summary>
    private sealed class ArtisanInRound(MatchFlow match, int round) : ITurnController
    {
        public void OrganizeHand(PlayerHandAccess hand, int overflow)
        {
        }

        public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
        {
        }

        public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
        {
            if (match.MajorRound != round)
            {
                return;
            }

            foreach (Coord cell in batch.Context.LegalRange.Order())
            {
                // 只取棋盘内部的栅栏（两端都可落子）：场景格上的栅栏无效果，拼接时可能被丢弃，不是本测试要钉的口径。
                foreach (TerrainEdit edit in TerrainEditRules.LegalTargets(match.Map, cell).Where(e => e.Kind == TerrainEditKind.Fence && match.Map.IsPlayable(e.Edge.A) && match.Map.IsPlayable(e.Edge.B)))
                {
                    if (batch.Stage(cell, PieceType.Artisan, edit) is null)
                    {
                        return;
                    }
                }
            }
        }

        public bool OnRejected(StagedBatch batch, BatchFailure failure) => false;
    }

    [Fact]
    public void 战场结算可查()
    {
        // 规格 Scenario「战场结算可查」：某战场因整轮 Pass 结算 → 日志中有该结算记录，含战场标识、原因、名次与胜者；
        // 出局区分战场落败并记出局时势力（可大于 0）；结束原因为最后战场结算。
        // 变异 MC-16：SettlementLogEntry 的名次势力恒写 0 → 本测试红（落败者势力 > 0）。
        (MatchFlow match, MatchLog log) = Play([0, 1, 2, 3], stoneRounds: 1);

        List<LogEvent> settled = log.Events.Where(e => e.Type == LogEventType.BattlefieldSettled).ToList();
        Assert.Single(settled);   // open-map：唯一战场一次结算即终局，四人都在名单里、三人以战场落败出局
        for (int i = 0; i < settled.Count; i++)
        {
            BattlefieldSettlementRecord record = match.Settlements[i];
            SettlementLogEntry entry = settled[i].Settlement!;
            Assert.Equal((record.Sequence, record.MajorRound, record.Battlefield, record.Reason.ToString(), record.Winner.Value, record.EliminationOrder),
                (entry.Sequence, entry.MajorRound, entry.Battlefield, entry.Reason, entry.Winner, entry.EliminationOrder));
            Assert.Equal(record.Entries.Select(e => (e.Player.Value, e.Rank, e.Power, e.ControlledRelics, e.ExclusiveCells, e.Stones)),
                entry.Entries.Select(e => (e.Player, e.Rank, e.Power, e.ControlledRelics, e.ExclusiveCells, e.Stones)));
            Assert.Equal(nameof(SettlementReason.AllPassed), entry.Reason);
        }

        Assert.All(settled.SelectMany(e => e.Settlement!.Entries), e => Assert.True(e.Power > 0));   // 样本口径：落败者势力非 0

        List<LogEvent> eliminated = log.Events.Where(e => e.Type == LogEventType.PlayerEliminated).ToList();
        Assert.Equal(3, eliminated.Count);
        foreach (LogEvent e in eliminated)
        {
            PlayerFlowState state = match.StateOf(new PlayerId(e.Player!.Value));
            Assert.Equal(nameof(EliminationReason.BattlefieldDefeat), e.Reason);
            Assert.Equal(state.PowerAtDefeat, e.Values!["Power"]);
            Assert.True(e.Values["Power"] > BigInteger.Zero);
        }

        PlayerEntry[] last = [.. log.Turns[^1].PlayersState];
        Assert.Equal(3, last.Count(p => p.EliminationReason == nameof(EliminationReason.BattlefieldDefeat)));
        Assert.All(last.Where(p => p.EliminationReason is not null), p => Assert.Equal(match.StateOf(new PlayerId(p.Player)).PowerAtDefeat, p.PowerAtDefeat));
        Assert.Equal(nameof(EndReason.LastBattlefieldSettled), log.Result!.Reason);
        Assert.Equal(3, log.Result.Standings.Count(s => s.EliminationReason == nameof(EliminationReason.BattlefieldDefeat)));
    }

    [Fact]
    public void 开局棋盘主题经日志往返逐块相同()
    {
        // link-map-terrain 第 1 组裁决②：日志首部的开局棋盘带主题（枚举名），JSON Lines 往返后逐块相同（open-map 段 B：LinkLogLayout 随链接删除，只比首部）。
        // 样本口径：主题确实存在且两两不同（全是 null 时"逐块相同"恒真）。
        // 变异验证（link-map-terrain 第 1 组收尾实跑，只跑本类）：M-L1 MatchSession 写首部时不写 Theme → 本测试红 1；
        (MatchFlow match, MatchLog log) = Play([0, 1, 2, 3], stoneRounds: 1);
        BoardTheme?[] expected = [.. match.Board.BaseMap.Boards.Select(b => b.Theme)];
        Assert.Equal(5, expected.Length);
        Assert.All(expected, t => Assert.NotNull(t));
        Assert.Equal(5, expected.Distinct().Count());

        string text = log.FullText();
        Assert.Contains($"\"Theme\":\"{expected[1]}\"", text, StringComparison.Ordinal);   // 写成枚举名
        MatchLog parsed = MatchLog.Parse(text);
        Assert.Equal(expected, parsed.Header.Boards!.Select(b => b.Theme));
        Assert.Equal(match.Board.BaseMap.Boards.Select(b => (b.Origin.ToNotation(), b.Width, b.Height)), parsed.Header.Boards!.Select(b => (b.Origin, b.Width, b.Height)));
    }

    /// <summary>
    /// 在 <c>link:1</c> 上按 <paramref name="zones"/> 开局，经 <see cref="MatchSession"/> 跑到终局：前 <paramref name="stoneRounds"/> 个大回合每人落 1 子，此后全部 Pass。
    /// </summary>
    private static (MatchFlow Match, MatchLog Log) Play(int[] zones, int stoneRounds)
    {
        MatchFlow match = LinkModeFixtures.Started(zones);
        MatchSession session = MatchSession.ForMatch(match, LinkSimFixtures.Config(turnLimit: 200));
        foreach (PlayerId p in match.Players)
        {
            session.SetController(p, new StoneThenPass(() => match.MajorRound <= stoneRounds));
        }

        MatchLog log = session.Run();
        Assert.False(log.IsFailed, log.Failure?.Message);
        Assert.NotNull(match.Result);
        return (match, log);
    }

    /// <summary>条件成立时落 1 枚普通子（合法范围内按坐标序第一个能暂放的空格），否则 Pass。</summary>
    private sealed class StoneThenPass(Func<bool> placeNow) : ITurnController
    {
        public void OrganizeHand(PlayerHandAccess hand, int overflow)
        {
        }

        public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
        {
        }

        public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
        {
            if (!placeNow())
            {
                return;
            }

            foreach (Coord cell in batch.Context.LegalRange.Order())
            {
                if (batch.Stage(cell, PieceType.Basic) is null)
                {
                    return;
                }
            }
        }

        public bool OnRejected(StagedBatch batch, BatchFailure failure) => false;
    }
}
