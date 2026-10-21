using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.LinkModeFixtures;

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
    public void 上限结算可查()
    {
        // 规格 Scenario「上限结算可查」（cycle-stop）：第 6 大回合因到达大回合上限结算 → 日志中有该结算记录、原因为"上限"，首部记录大回合上限 6；
        // 离线分析能统计上限结算次数（链接模式一段）。样本：四名控制者每小回合都落 1 子（永不 Pass、填不满），上限 6 → 只可能因上限结算。
        // 旧日志：首部缺该项按 100 读、不拒绝（D2）。变异 M-RC5：MatchSession 建局不把 RoundCap 写进对局配置 → 样本跑到 turn_limit 截断，本条红。
        // terrain-barriers 3.1（地图变了）：link:1 上按坐标序落子的样本里有一人被提光出局（结算只剩 3 人），"四人都落过子、势力非 0"的样本口径落空——
        // 载体改为手写平坦图 FlatMap（link:90003，不随生成器变），本条守的是上限结算的记录与统计，与地形无关。
        MatchFlow match = StartedFlat([0, 1, 2, 3], options: Options with { RoundCap = 6 });
        MatchSession session = MatchSession.ForMatch(match, LinkSimFixtures.Config(turnLimit: 200) with { ContentSet = match.ContentSet, RoundCap = match.RoundCap });
        foreach (PlayerId p in match.Players)
        {
            session.SetController(p, new LinkSimFixtures.StoneThenPass(() => true));
        }

        MatchLog log = session.Run();
        Assert.False(log.IsFailed, log.Failure?.Message);
        Assert.Equal(nameof(EndReason.LastBattlefieldSettled), log.Result!.Reason);
        Assert.Equal(6, log.Result.MajorRound);
        Assert.Equal(6, log.Header.Config.RoundCap);
        Assert.Equal(6, log.RoundCap);
        Assert.Contains("\"RoundCap\":6", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);

        LogEvent settled = Assert.Single(log.Events.Where(e => e.Type == LogEventType.BattlefieldSettled));
        Assert.Equal(nameof(SettlementReason.RoundCap), settled.Settlement!.Reason);
        Assert.Equal(6, settled.Settlement.MajorRound);
        Assert.Equal(4, settled.Settlement.Entries.Count);
        Assert.True(settled.Settlement.Entries.All(e => e.Power > 0));   // 样本口径：四人都落过子、势力非 0

        // 离线统计：链接模式一段按结算原因计数，上限结算 1 次。
        MatchLog parsed = MatchLog.Parse(log.FullText());
        LinkModeSection link = BalanceAnalyzer.Analyze([parsed]).LinkMode ?? throw new InvalidOperationException("没有链接模式统计。");
        Assert.Equal(1, link.SettlementReasons.GetValueOrDefault(nameof(SettlementReason.RoundCap)));
        Assert.Contains("上限结算 1 次", ReportWriter.Render(BalanceAnalyzer.Analyze([parsed])), StringComparison.Ordinal);

        // 旧日志：从首部配置里删掉 RoundCap → 按 100 读、不拒绝。
        string[] lines = log.FullText().Split('\n');
        System.Text.Json.Nodes.JsonObject header = System.Text.Json.Nodes.JsonNode.Parse(lines[0])!.AsObject();
        Assert.True(header["Config"]!.AsObject().Remove("RoundCap"));
        lines[0] = header.ToJsonString();
        MatchLog legacy = MatchLog.Parse(string.Join('\n', lines));
        Assert.Null(legacy.Header.Config.RoundCap);
        Assert.Equal(100, legacy.RoundCap);
        Assert.NotNull(BalanceAnalyzer.Analyze([legacy]).LinkMode);
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
