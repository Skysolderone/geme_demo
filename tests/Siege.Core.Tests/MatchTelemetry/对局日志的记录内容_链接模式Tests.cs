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
    [Fact]
    public void 地形可离线重建()
    {
        // 规格正文「改造与链接记录 MUST 足以离线重建任意时刻的地形」的链接模式侧（段 C 质检补缺口；Legacy 侧见 地形改造日志与分析Tests.地形可离线重建）：
        // 只读日志（文本往返后）——按首部标识重建开局地图、核对首部摘要；按发生顺序把每个链接事件的两个战场交给链接生成器，逐次核对链接后的地图摘要；
        // 最后把全部改造重放上去，与这一局真实对局的终局地形逐格相同。
        MatchSession session = MatchSession.Create(LinkSimFixtures.Config(turnLimit: 13), LinkSimFixtures.FourZoneSeed.Value);
        MatchLog log = SimFixtures.Clone(session.Run());
        List<LinkLogEntry> links = [.. log.Events.Where(e => e.Link is not null).OrderBy(e => e.Seq).Select(e => e.Link!)];
        Assert.Equal(2, links.Count);   // 样本口径：两次链接

        MapData map = MapCatalog.Resolve(log.Header.MapId);
        Assert.Equal(log.Header.MapDigest, MapFile.Digest(map));
        var digests = new List<string>();
        foreach (LinkLogEntry link in links)
        {
            map = LinkMapGenerator.Link(map, link.X, link.Y).Map;
            Assert.Equal(link.MapDigest, MapFile.Digest(map));
            digests.Add(MapFile.Digest(map));
        }

        Assert.Equal(MapFile.Digest(session.Match.Board.LinkedMap), digests[^1]);
        Assert.NotEqual(digests[0], digests[^1]);   // 反面：少重放一次链接就对不上

        TerrainEdit[] edits = [.. log.Turns.SelectMany(t => t.Edits ?? []).Select(e => TerrainEdit.Parse(e.Target))];
        Assert.NotEmpty(edits);   // 样本口径：本局确有改造（实测 2 次），改造那条腿才真的被比对
        MapData rebuilt = TerrainWriter.ApplyAll(map, edits);
        MapData actual = session.Match.Board.Map;
        Assert.Equal(actual.TerrainData.Bridges, rebuilt.TerrainData.Bridges);
        Assert.Equal(actual.TerrainData.Fences, rebuilt.TerrainData.Fences);
        foreach (Coord c in actual.AllCoords())
        {
            Assert.Equal(actual.SurfaceAt(c), rebuilt.SurfaceAt(c));
            Assert.Equal(actual.IsPlayable(c), rebuilt.IsPlayable(c));
        }
    }

    [Fact]
    public void 配对与链接可查()
    {
        // 规格 Scenario「配对与链接可查」：四人四区的日志里可查到第 3 大回合结束时的配对事件（两对）、两次链接事件及其地图摘要、决赛前的第三次链接。
        // 变异 MC-14：链接事件不写 MapDigest（写空串）→ 摘要比对红；MC-15：CopyFlowEvents 漏掉 PairingApplied → 配对事件数红。
        (MatchFlow match, MatchLog log) = Play([0, 1, 2, 3], stoneRounds: 1);

        List<LogEvent> pairings = log.Events.Where(e => e.Type == LogEventType.PairingApplied).ToList();
        Assert.Equal(2, pairings.Count);
        PairingLogEntry first = pairings[0].Pairing!;
        Assert.Equal(nameof(PairingKind.FirstRound), first.Kind);
        Assert.Equal(4, first.MajorRound);
        Assert.Equal([0, 1, 2, 3], first.Candidates);
        Assert.Equal([[0, 1], [2, 3]], first.Pairs);
        Assert.Null(first.Waiter);
        Assert.Equal(ScriptedPairingSource.ScriptSource, first.Source);
        Assert.Equal(nameof(PairingKind.Final), pairings[1].Pairing!.Kind);
        Assert.Equal(12, pairings[0].Turn);   // 发生在第 3 大回合的最后一个小回合（第 12 个）里：此时大回合已推进到 4

        List<LogEvent> links = log.Events.Where(e => e.Type == LogEventType.BattlefieldsLinked).ToList();
        Assert.Equal(3, links.Count);
        for (int i = 0; i < links.Count; i++)
        {
            LinkRecord record = match.Links[i];
            LinkLogEntry entry = links[i].Link!;
            Assert.Equal((record.Sequence, record.PairingSequence, record.X, record.Y, record.MapVersion, record.Degraded),
                (entry.Sequence, entry.PairingSequence, entry.X, entry.Y, entry.MapVersion, entry.Degraded));
            Assert.Equal(record.MapDigest, entry.MapDigest);
            Assert.Equal(64, entry.MapDigest.Length);
            Assert.Equal(record.CorridorCells, entry.CorridorCells);
            Assert.True(entry.CorridorCells > 0);
            Assert.Equal(record.PublicBoard is { } plate ? $"{plate.Origin.ToNotation()} {plate.Width}×{plate.Height}" : null, entry.PublicBoard);
            Assert.Equal(record.NewRelics.Select(r => r.Coord.ToNotation()), entry.NewRelics.Select(r => r.Coord));
            Assert.Equal(record.NewRelics.Select(r => r.Content.Type.ToString()), entry.NewRelics.Select(r => r.Type));
        }

        Assert.Contains(links, l => l.Link!.NewRelics.Count > 0);   // 样本口径：公共棋盘带来了新信物
        Assert.Equal([1, 2, 3], links.Select(l => l.Link!.MapVersion));

        // 首部第 1、2 项：模式、锁定时各棋盘旗数，没有冒险概率。
        Assert.Equal("Link", log.Header.Mode);
        Assert.Equal([1, 1, 1, 1, 0], log.Header.LockedFlagCounts);
        Assert.DoesNotContain("FlagRisk", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);
    }

    [Fact]
    public void 战场结算可查()
    {
        // 规格 Scenario「战场结算可查」：某战场因整轮 Pass 结算 → 日志中有该结算记录，含战场标识、原因、名次与胜者；
        // 出局区分战场落败并记出局时势力（可大于 0）；结束原因为最后战场结算。
        // 变异 MC-16：SettlementLogEntry 的名次势力恒写 0 → 本测试红（落败者势力 > 0）。
        (MatchFlow match, MatchLog log) = Play([0, 1, 2, 3], stoneRounds: 1);

        List<LogEvent> settled = log.Events.Where(e => e.Type == LogEventType.BattlefieldSettled).ToList();
        Assert.Equal(3, settled.Count);
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
    public void 等待时长可查()
    {
        // match-telemetry 第 11 项：每名玩家作为等待者度过的大回合数。2+1+1：同区的两人在第 2 大回合结算、胜者独处一战场；
        // 第 4 大回合结束时它仍是单人战场 → 等待 1 个大回合（其余玩家 0）。
        // 变异 MC-17：PlayerEntries 的 WaitingRounds 恒写 0 → 本测试红。
        (MatchFlow match, MatchLog log) = Play([0, 0, 1, 2], stoneRounds: 1);
        PlayerEntry[] last = [.. log.Turns[^1].PlayersState];
        Assert.Equal(match.PlayerStates.Select(s => (int?)s.WaitingRounds), last.Select(p => p.WaitingRounds));
        Assert.Contains(last, p => p.WaitingRounds > 0);   // 样本口径
        Assert.Equal([2, 1, 1, 0, 0], log.Header.LockedFlagCounts);
        Assert.Equal(2, log.Events.Count(e => e.Type == LogEventType.PairingApplied));   // 首轮一对 + 决赛
    }

    [Fact]
    public void 日志往返且配对可从日志重建()
    {
        // 新增的结构化字段经 JSON Lines 往返不丢（testing.md「写出时漏字段要用往返测试抓」）；回放输入（配对事件）可从日志逐项重建出原局的配对结果。
        (MatchFlow match, MatchLog log) = Play([0, 1, 2, 3], stoneRounds: 1);
        MatchLog parsed = MatchLog.Parse(log.FullText());
        Assert.Equal(log.DeterministicText(), parsed.DeterministicText());
        Assert.True(parsed.IsLinkMode);

        List<RecordedPairing> recorded = parsed.RecordedPairings();
        Assert.Equal(match.Pairings.Count, recorded.Count);
        for (int i = 0; i < recorded.Count; i++)
        {
            PairingRecord original = match.Pairings[i];
            Assert.Equal(original.Kind, recorded[i].Kind);
            Assert.Equal(original.Candidates, recorded[i].Candidates);
            Assert.Equal(original.Pairs, recorded[i].Decision.Canonical);
            Assert.Equal(original.Waiter, recorded[i].Decision.Waiter);
            Assert.Equal(original.Source, recorded[i].Decision.Source);
        }
    }

    [Fact]
    public void 旧模式日志不写链接模式字段()
    {
        // 实现裁决 C3：Legacy 对局的日志写出逐字节不变——新字段一律只在链接模式写出（逐字节由既有黄金哈希守住，这里守"键不出现"）。
        // 反面：同样的键在链接模式的日志里确实出现。
        // 变异 MC-18：首部 Mode 对旧模式也写 "Legacy" → 本测试红（也会让既有首部黄金哈希红）。
        string legacy = MatchSession.Create(SimFixtures.Config(turnLimit: 8), 1).Run().DeterministicText();
        string link = Play([0, 1, 2, 3], stoneRounds: 1).Log.DeterministicText();
        foreach (string key in new[] { "\"Mode\":", "\"LockedFlagCounts\":", "\"WaitingRounds\":", "\"EliminationReason\":", "\"Pairing\":", "\"Link\":", "\"Settlement\":", "\"PairingSeed\":" })
        {
            Assert.DoesNotContain(key, legacy, StringComparison.Ordinal);
            if (key != "\"PairingSeed\":")
            {
                Assert.Contains(key, link, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// 在 <c>link:1</c> 上按 <paramref name="zones"/> 开局（脚本固定配对），经 <see cref="MatchSession"/> 跑到终局：前 <paramref name="stoneRounds"/> 个大回合每人落 1 子，此后全部 Pass。
    /// </summary>
    private static (MatchFlow Match, MatchLog Log) Play(int[] zones, int stoneRounds)
    {
        MatchFlow match = LinkModeFixtures.Started(zones, new ScriptedPairingSource());
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
