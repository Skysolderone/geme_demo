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
        // 规格正文「改造与链接记录 MUST 足以离线重建任意时刻的地形」的链接模式侧（board-link-mode 裁决 J8-9：F.3 恢复）：
        // 只读日志（文本往返后）——按首部标识重建开局地图、核对首部摘要与开局棋盘；按发生顺序把每个链接事件（两个战场 + 贴边）交给链接生成器，
        // 逐次核对链接后的地图摘要与日志里的平移向量；再把每个小回合记录的改造（按该小回合结束时的布局）经 LinkLayout 沿链接记录平移到终局布局，
        // 重放到终局地图上，与这一局真实对局的终局地形逐格相同。反面：漏平移任一次链接即对不上。
        MatchSession session = MatchSession.Create(LinkSimFixtures.Config(turnLimit: 13), LinkSimFixtures.FourZoneSeed.Value);
        MatchLog log = SimFixtures.Clone(session.Run());
        List<LinkLogEntry> links = [.. log.Events.Where(e => e.Link is not null).OrderBy(e => e.Seq).Select(e => e.Link!)];
        Assert.Equal(2, links.Count);   // 样本口径：两次链接

        MapData map = MapCatalog.Resolve(log.Header.MapId);
        Assert.Equal(log.Header.MapDigest, MapFile.Digest(map));
        Assert.Equal(map.Boards.Select(b => b.Origin.ToNotation()), log.Header.Boards!.Select(b => b.Origin));
        foreach (LinkLogEntry link in links)
        {
            LinkResult result = LinkMapGenerator.Link(map, link.X, link.Y, Enum.Parse<LinkSide>(link.Side));
            map = result.Map;
            Assert.Equal(link.MapDigest, MapFile.Digest(map));
            Assert.Equal(result.Shifts.Select(s => (s.Battlefield, s.Dx, s.Dy)), link.Shifts.Select(s => (s.Battlefield, s.Dx, s.Dy)));
        }

        Assert.Equal(MapFile.Digest(session.Match.Board.LinkedMap), MapFile.Digest(map));

        LinkLayout layout = LinkLogLayout.Of(log);
        Assert.Equal(2, layout.Versions);
        (TerrainEdit Edit, int Version)[] recorded = [.. log.Turns.SelectMany(t => (t.Edits ?? []).Select(e => (TerrainEdit.Parse(e.Target), LinkLogLayout.VersionAfterTurn(log, t.Turn))))];
        Assert.NotEmpty(recorded);   // 样本口径：本局确有改造
        MapData actual = session.Match.Board.Map;
        MapData rebuilt = Rebuild(layout);
        Assert.Equal(actual.TerrainData.Bridges, rebuilt.TerrainData.Bridges);
        Assert.Equal(actual.TerrainData.Fences, rebuilt.TerrainData.Fences);
        Assert.Equal(actual.TerrainData.Surfaces.OrderBy(kv => kv.Key), rebuilt.TerrainData.Surfaces.OrderBy(kv => kv.Key));
        foreach (Coord c in actual.AllCoords())
        {
            Assert.Equal(actual.IsPlayable(c), rebuilt.IsPlayable(c));
        }

        // 样本口径：确有改造发生在某次链接之前、且它的棋盘随之平移（坐标真的变了），平移那条腿才被比对。
        Assert.Contains(recorded, r => r.Version < layout.Versions && layout.TranslateEdit(r.Edit, r.Version, layout.Versions) != r.Edit);

        // 反面：漏平移任一次链接（把那次链接的全部向量置零）→ 重建的栅栏与真实终局不同。
        bool anyMismatch = false;
        for (int skip = 0; skip < links.Count; skip++)
        {
            int dropped = skip;
            var broken = new LinkLayout(
                log.Header.Boards!.Select(b => new BoardPlate(Coord.Parse(b.Origin), b.Width, b.Height, BoardPlateKind.Birth)),
                links.Select((l, k) => k == dropped
                    ? new LinkShiftStep(l.X, l.Y, [.. l.Shifts.Select(s => new BattlefieldShift(s.Battlefield, 0, 0))])
                    : LinkLogLayout.StepOf(l)));
            try
            {
                anyMismatch |= !Rebuild(broken).TerrainData.Fences.SetEquals(actual.TerrainData.Fences);
            }
            catch (Exception ex) when (ex is SiegeRuleException or ArgumentException)
            {
                anyMismatch = true;   // 漏平移后的改造落到场景格之间 / 越界，同样是对不上
            }
        }

        Assert.True(anyMismatch, "漏平移一次链接应当重建不出终局地形");

        MapData Rebuild(LinkLayout l) => TerrainWriter.ApplyAll(map,
            recorded.Select(r => l.TranslateEdit(r.Edit, r.Version, l.Versions)).OfType<TerrainEdit>().Distinct());
    }

    [Fact]
    public void 链接那一小回合的改造按链接后布局记()
    {
        // 段 F.3 版本口径：小回合快照里的改造按该小回合结束时（含本回合内的链接）的布局记。第 3 大回合每人落一枚匠人并立一道栅栏，
        // 最后一个小回合的改造发生在链接之前、记录时要经 LinkLayout 平移到链接后——全部日志改造按各自版本平移到终局，与 Core 盘面上已迁移的改造列表逐条相同。
        // 变异 MF3-3：MatchSession 记改造时不平移 → 本测试红。
        MatchFlow match = LinkModeFixtures.Started([0, 1, 3, 4], new ScriptedPairingSource());
        foreach (PlayerId p in match.Players)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 40), (PieceType.Artisan, 10));
        }

        MatchSession session = MatchSession.ForMatch(match, LinkSimFixtures.Config(turnLimit: 13) with { ContentSet = match.ContentSet });
        foreach (PlayerId p in match.Players)
        {
            session.SetController(p, new ArtisanInRound(match, 3));
        }

        MatchLog log = SimFixtures.Clone(session.Run());
        Assert.Equal(2, match.Links.Count);
        LinkLayout layout = LinkLogLayout.Of(log);
        (TerrainEdit Edit, int Version, int Turn)[] recorded = [.. log.Turns.SelectMany(t => (t.Edits ?? []).Select(e => (TerrainEdit.Parse(e.Target), LinkLogLayout.VersionAfterTurn(log, t.Turn), t.Turn)))];
        Assert.Equal(4, recorded.Length);   // 样本口径：第 3 大回合每人一道栅栏

        // 样本口径：最后一道栅栏所在小回合里发生了链接，且该棋盘被平移（版本前后坐标真的不同）。
        (TerrainEdit last, int lastVersion, int lastTurn) = recorded[^1];
        Assert.Equal(2, lastVersion);
        Assert.Equal(0, LinkLogLayout.VersionAfterTurn(log, lastTurn - 1));
        int lastZone = layout.ZoneAt(last.Edge.A, 2) ?? layout.ZoneAt(last.Edge.B, 2)!.Value;
        Assert.NotEqual(layout.BoardAt(lastZone, 0).Origin, layout.BoardAt(lastZone, 2).Origin);   // 该棋盘在本回合的链接里确实平移了
        Assert.Equal(match.Board.TerrainEdits.Select(e => e.ToString()).Order(StringComparer.Ordinal),
            recorded.Select(r => layout.TranslateEdit(r.Edit, r.Version, layout.Versions)!.Value.ToString()).Order(StringComparer.Ordinal));
        Assert.Contains(recorded.Take(3), r => layout.TranslateEdit(r.Edit, 0, 2) != r.Edit);   // 平移确实改变坐标
    }

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
            Assert.Equal((record.Sequence, record.PairingSequence, record.X, record.Y, record.MapVersion),
                (entry.Sequence, entry.PairingSequence, entry.X, entry.Y, entry.MapVersion));
            Assert.Equal(record.MapDigest, entry.MapDigest);
            Assert.Equal(64, entry.MapDigest.Length);
            Assert.Equal(record.Summary, entry.Summary);
            // 段 F.3：贴边、共享边与拼接前每个战场的平移向量（含锚点）逐项进日志。
            Assert.Equal(record.Side.ToString(), entry.Side);
            Assert.Equal(record.SharedEdge, entry.SharedEdge);
            Assert.Equal(record.Shifts.Select(s => (s.Battlefield, s.Dx, s.Dy)), entry.Shifts.Select(s => (s.Battlefield, s.Dx, s.Dy)));
            Assert.True(entry.SharedEdge >= 1);
        }

        Assert.Equal(["East", "East", "North"], links.Select(l => l.Link!.Side));
        Assert.Equal([5, 4, 3], links.Select(l => l.Link!.Shifts.Count));   // 拼接前的战场数
        // 首部的开局出生棋盘（离线换算的起点）：与开局地图的棋盘清单逐块相同。
        Assert.Equal(match.Board.BaseMap.Boards.Select(b => (b.Origin.ToNotation(), b.Width, b.Height)), log.Header.Boards!.Select(b => (b.Origin, b.Width, b.Height)));

        Assert.All(links, l => Assert.Contains("拼接", l.Link!.Summary, StringComparison.Ordinal));
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
        string legacy = MatchSession.Create(SimFixtures.Config(turnLimit: 8), 1, resolveMap: MapCatalog.Legacy.Resolve).Run().DeterministicText();   // board-link-mode 裁决 D7：Legacy 夹具显式经 MapCatalog.Legacy 解析
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
