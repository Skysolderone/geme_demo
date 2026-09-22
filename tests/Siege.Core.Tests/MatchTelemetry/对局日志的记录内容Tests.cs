using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>规格：match-telemetry —— Requirement: 对局日志的记录内容</summary>
public class 对局日志的记录内容Tests
{
    [Fact]
    public void 日志覆盖八类记录()
    {
        // match-telemetry 八类记录逐类对应到字段（映射表见 MatchLog 类型注释；第 4 类地形改造由 地形改造日志与分析Tests 逐字段钉住）。写文件 → 只读文件解析 → 逐类断言字段存在且可解析。
        // restore-go-core-rules 段 E：原名「日志覆盖七类记录」，规格早已是八类；本段补第 6 类的领地分与第 7 类的结束原因。
        // 变异验证 M-E5：MatchSession.PlayerEntries 不写领地分（TerritoryScore 恒 null）→ 实跑红 4（本测试、领地分可查、大数不失真、候选格上限黄金哈希）。
        // 持久化两条腿：活对象字段 vs 解析结果逐字段比 + 文本往返逐字节比。
        // 变异验证 M-B1：MatchSession.RecordTurn 不写 Recruit 事件 → 红 2（本测试、SimulationHarness.子流互不干扰）。
        MatchLog live = SimFixtures.Sample.Value[0];
        string dir = SimFixtures.TempDir("seven-kinds");
        string path = live.WriteTo(dir);
        MatchLog log = MatchLog.Read(path);

        // 1. 地图、种子、完整信物分布及揭示时间
        Assert.Equal("siege-4p-base-v5", log.Header.MapId); // scoring-sites：RunConfig 默认地图切到 v4
        Assert.Equal(live.Header.Seed, log.Header.Seed);
        Assert.NotEmpty(log.Header.Relics);
        Assert.All(log.Header.Relics, r => Assert.True(Coord.TryParse(r.Coord, out _) && Enum.TryParse<Relics.RelicType>(r.Type, out _)));
        Assert.Equal(log.Header.Relics.Count, log.Result!.RelicReveals.Count);

        // 2. 每轮征募候选、玩家选择、被 Pass 撤销的征募数
        List<LogEvent> recruits = [.. log.Events.Where(e => e.Type == LogEventType.Recruit)];
        Assert.Equal(log.Turns.Count, recruits.Count);
        Assert.All(recruits, e =>
        {
            Assert.NotNull(e.Player);
            Assert.Contains("candidates=", e.Detail);
            Assert.True(e.Values!.ContainsKey("Recruited") && e.Values.ContainsKey("Revoked") && e.Values.ContainsKey("Deployed"));
        });

        // 3. 每次批次落子、合法性结果、提子数量与同形检查
        List<LogEvent> settled = [.. log.Events.Where(e => e.Type == LogEventType.Settled)];
        Assert.NotEmpty(settled);
        Assert.All(settled, e =>
        {
            Assert.NotEmpty(e.Coords!);
            Assert.Equal(1, e.Values!["SuperkoPassed"]);
            Assert.True(e.Values.ContainsKey("Captures"));
        });
        Assert.Equal(settled.Count, log.Turns.Count(t => !t.Passed));

        // 4. 信物控制变化、结构参数、行动顺序
        Assert.Contains(log.Events, e => e.Type == LogEventType.ControlChanged && e.Coords!.Count == 1);
        Assert.All(log.Turns, t =>
        {
            Assert.True(t.ShowCount >= 5 && t.FreePickCount >= 3 && t.TypeSlots >= 5 && t.DeployLimit >= 3, t.ToString());
            Assert.Equal(4, t.ActionOrder.Count);
        });
        LogEvent roundEnd = log.Events.First(e => e.Type == LogEventType.MajorRoundEnded);
        Assert.True(roundEnd.Values!.ContainsKey("P0.Bonus") && roundEnd.Values.ContainsKey("P0.Rank"));

        // 5. 每个棋串的基础军势、位置加值（来源拆分）、倍率、最终军势
        GroupEntry group = log.Turns[^1].PlayersState.SelectMany(p => p.Groups).First();
        Assert.NotEmpty(group.Stones);
        Assert.True(group.Base > 0 && group.Power > 0);
        Assert.True(group.LineBonus >= 0 && group.SynergyBonus >= 0 && group.MultiplierCount >= 0);

        // 5（续）每名玩家的领地分（独占空格数）：每条快照每名玩家都写出（非 null）；总势力 = 领地分 + Σ棋串军势（测试内独立加和），
        // 且样本里确有非零领地分（testing.md「期望值是 0 / null 的遥测断言抓不到写入端漏写」）。
        Assert.All(log.Turns.SelectMany(t => t.PlayersState), p =>
        {
            Assert.NotNull(p.TerritoryScore);
            Assert.Equal(p.Total, p.TerritoryScore!.Value + p.Groups.Aggregate(BigInteger.Zero, (sum, g) => sum + g.Power));
        });
        Assert.Contains(log.Turns.SelectMany(t => t.PlayersState), p => p.TerritoryScore > 0);

        // 6. 势力排名变化、Pass、出局、弃赛、最终结果
        Assert.Contains(log.Events, e => e.Type == LogEventType.RankChanged);
        Assert.All(log.Turns, t => Assert.Equal(t.Placements.Count == 0, t.Passed));
        // 段 C：规则层已无大回合上限，4 大回合样本由跑局层的小回合数截断（D5）收住——结束原因 turn_limit，MUST NOT 产生名次与胜者；
        // turn_limit 不是 EndReason 的成员（规则层没有这种终局）。规则级终局的名次记录见本测试末段的第二局。
        Assert.Equal(LogResult.TurnLimitReason, log.Result.Reason);
        Assert.True(log.Result.Truncated);
        Assert.False(log.Result.Converged);
        Assert.DoesNotContain(log.Result.Reason, Enum.GetNames<EndReason>());
        Assert.Empty(log.Result.Standings);
        Assert.Empty(log.Result.Winners);
        Assert.Equal(16, log.Result.TurnCount);
        Assert.Equal(4, log.Result.MajorRound);

        // 7. 小回合、大回合与整局耗时
        Assert.All(log.Turns, t => Assert.NotNull(t.ElapsedMs));
        Assert.NotNull(log.Result.TotalMs);
        Assert.Equal(log.Result.MajorRound, log.Result.MajorRoundMs!.Count);

        // 活对象 vs 解析结果逐字段；再文本往返逐字节
        Assert.Equal(live.Turns.Count, log.Turns.Count);
        Assert.Equal(live.Events.Count, log.Events.Count);
        Assert.Equal(
            live.Turns.Select(t => t.PlayersState.Aggregate(BigInteger.Zero, (sum, p) => sum + p.Total)),
            log.Turns.Select(t => t.PlayersState.Aggregate(BigInteger.Zero, (sum, p) => sum + p.Total)));
        Assert.Equal(live.Result!.Winners, log.Result.Winners);
        Assert.Equal(live.Result.Reason, log.Result.Reason);
        Assert.Equal(live.Header.Relics.Select(r => $"{r.Coord}:{r.Type}{r.Magnitude}"), log.Header.Relics.Select(r => $"{r.Coord}:{r.Type}{r.Magnitude}"));
        Assert.Equal(live.FullText(), log.FullText());
        Assert.Equal(live.DeterministicText(), MatchLog.Parse(live.FullText().Replace("\n", "\r\n")).DeterministicText());

        // 6（续）规则级终局的最终结果：P1、P2 先弃赛，P0 由 AI 走一个小回合后 P3 弃赛 → 只剩一名参赛玩家，名次取自规则层。
        MatchFlow match = MatchFixtures.Started().AtRound(5, MatchFixtures.All);
        match.Resign(MatchFixtures.P1);
        match.Resign(MatchFixtures.P2);
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config());
        Assert.True(session.RunTurn());
        match.Resign(MatchFixtures.P3);
        MatchLog ended = MatchLog.Parse(session.Run().FullText());
        Assert.Equal(nameof(EndReason.LastPlayerStanding), ended.Result!.Reason);
        Assert.True(ended.Result.Converged);
        Assert.Equal(4, ended.Result.Standings.Count);
        Assert.Equal([0], ended.Result.Winners);
        Assert.Equal(match.Result!.Standings.Select(s => (s.Player.Value, s.Rank)), ended.Result.Standings.Select(s => (s.Player, s.Rank)));
    }

    [Fact]
    public void 领地分可查()
    {
        // 规格 Scenario：某次结算后玩家 A 的独占空格由 14 变为 9 → 日志中可查到该次结算后 A 的领地分为 9。
        // 走真实盘面 → 日志写入函数（MatchSession.PlayerEntries，RecordTurn 用的同一个）→ 文本往返（testing.md「真实跑局覆盖不到的写入路径」）。
        // 9×9 平地：P0 占 C5–H5 一排 6 子 → 覆盖第 4 / 6 行各 6 格 + 两端 B5、J5 = 14 个独占空格。
        // 随后 P1 落 D6、H4：D6 与 H4 本身被占（−2），D6 使 C6 / E6 变争议（−2），H4 使 G4 变争议（−1）→ 14 − 5 = 9。
        MatchFlow match = MatchFixtures.Started().AtRound(5).Stones(MatchFixtures.P0, "C5", "D5", "E5", "F5", "G5", "H5");
        List<PlayerEntry> before = MatchSession.PlayerEntries(match.Publish());
        match.Stones(MatchFixtures.P1, "D6", "H4");
        List<PlayerEntry> after = MatchSession.PlayerEntries(match.Publish());

        MatchLog written = SimFixtures.Synthetic(
            1,
            [SimFixtures.Turn(1, 5, 0, [0, 0, 0, 0]) with { PlayersState = before }, SimFixtures.Turn(2, 5, 1, [0, 0, 0, 0]) with { PlayersState = after }],
            [],
            SimFixtures.ResultOf(5, [0]));
        MatchLog log = MatchLog.Parse(written.FullText());

        Assert.Equal(14, log.Turns[0].PlayersState.Single(p => p.Player == 0).TerritoryScore);
        Assert.Equal(9, log.Turns[1].PlayersState.Single(p => p.Player == 0).TerritoryScore);
        // 与权威势力明细逐人一致（P1 的领地分也非零：D7 / J4 / H3 三格）
        Assert.All(log.Turns[1].PlayersState, p => Assert.Equal(match.Scoreboard.Latest!.Of(new PlayerId(p.Player)).TerritoryScore, p.TerritoryScore));
        Assert.Equal(3, log.Turns[1].PlayersState.Single(p => p.Player == 1).TerritoryScore);
    }

    [Fact]
    public void 大数不失真()
    {
        // 规格 Scenario：某棋串军势超过 2^63 → 日志中记录的是精确整数，离线解析后与对局内的值逐位一致。
        // 真实盘面：12×12 平地上 P0 用 120 枚倍增子铺满第 1–10 行（一条棋串）→ 军势 ⌊120 × 3^120 / 2^120⌋（测试内独立整数式），
        // 远超 2^63；第 11 行 12 格为 P0 独占（领地分 12，非零）。经日志写入函数 → 文本 → 解析，逐位与对局内的势力明细比。
        // 写出端形状见 军势精确整数遥测Tests（JSON 数字、不加引号、无指数）。
        MapData map = MatchFixtures.Map() with { Id = "test-match-12x12", Width = 12, Height = 12 };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), MatchOptions.Immediate);
        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 12; x++)
            {
                match.Board.Place(new Coord(x, y), MatchFixtures.P0, PieceType.Multiplier);
            }
        }

        match.Debug.Recalculate();
        BigInteger expected = 120 * BigInteger.Pow(3, 120) / BigInteger.Pow(2, 120);
        Assert.True(expected > BigInteger.Pow(2, 63));
        Scoring.PlayerPower truth = match.Scoreboard.Latest!.Of(MatchFixtures.P0);
        Assert.Equal(expected, Assert.Single(truth.Groups).Power);
        Assert.Equal(12, truth.TerritoryScore);

        MatchLog written = SimFixtures.Synthetic(
            1, [SimFixtures.Turn(1, 5, 0, [0, 0, 0, 0]) with { PlayersState = MatchSession.PlayerEntries(match.Publish()) }], [], SimFixtures.ResultOf(5, [0]));
        string text = written.FullText();
        Assert.Contains($"\"Power\":{expected}", text, StringComparison.Ordinal);
        Assert.Contains($"\"Total\":{expected + 12}", text, StringComparison.Ordinal);

        PlayerEntry p0 = MatchLog.Parse(text).Turns[0].PlayersState.Single(p => p.Player == 0);
        Assert.Equal(expected, Assert.Single(p0.Groups).Power);
        Assert.Equal(truth.Total, p0.Total);
        Assert.Equal((12, 120), (p0.TerritoryScore, p0.Groups[0].MultiplierCount));
    }

    [Fact]
    public void 揭示时间可查()
    {
        // 每枚信物在 Result.RelicReveals 里都有一条：揭示的记大回合序号，且与过程中的 Reveal 事件一致；整局未揭示的标为 null。
        // 变异验证 M-C4（check）：MatchSession.Finish 把 RelicReveals 置空 → 红 2（本测试、日志覆盖七类记录）。
        MatchLog log = MatchLog.Parse(SimFixtures.Sample.Value[1].FullText());
        Assert.Equal(log.Header.Relics.Select(r => r.Coord).Order(), log.Result!.RelicReveals.Select(r => r.Coord).Order());
        List<LogEvent> reveals = [.. log.Events.Where(e => e.Type == LogEventType.Reveal)];
        Assert.NotEmpty(reveals);
        foreach (RelicRevealEntry entry in log.Result.RelicReveals)
        {
            LogEvent? reveal = reveals.SingleOrDefault(e => e.Coords![0] == entry.Coord);
            if (entry.RevealedInMajorRound is { } round)
            {
                Assert.NotNull(reveal);
                Assert.Equal(round, reveal.MajorRound);
                Assert.InRange(round, 1, log.Result.MajorRound);
            }
            else
            {
                Assert.Null(reveal);
            }
        }

        Assert.Contains(log.Result.RelicReveals, r => r.RevealedInMajorRound is null);
    }

    [Fact]
    public void 非法批次也记录()
    {
        // 玩家提交自杀手批次：P1 先占 A2、B1，P0 提交 A1（无气、不提子）→ 确认被拒 → 日志记录该次尝试、失败类别 Suicide 与相关坐标 A1。
        // 变异验证 M-B4：LoggingController.OnRejected 不记录 → 红 1（本测试）。
        // round-cap 3.3：原靠跑局层 maxRounds: 1 硬停；现由对局配置上限 1 以规则原因 MajorRoundLimit 终局。
        MatchFlow match = MatchFixtures.Started(options: MatchOptions.Immediate).Stones(MatchFixtures.P1, "A2", "B1");
        match.Debug.SetOrder(MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3);
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config(turnLimit: 4));
        session.SetController(MatchFixtures.P0, new BlindController("A1"));

        Assert.True(session.RunTurn());
        MatchLog log = session.Run();

        Assert.False(log.IsFailed);
        TurnSnapshot first = log.Turns[0];
        Assert.Equal(0, first.Player);
        Assert.Equal(1, first.Rejections);
        Assert.True(first.Passed);
        LogEvent rejected = Assert.Single(log.Events, e => e.Type == LogEventType.Rejected);
        Assert.Equal(1, rejected.Turn);
        Assert.Equal(0, rejected.Player);
        Assert.Equal(nameof(Batch.BatchFailureKind.Suicide), rejected.FailureKind);
        Assert.Equal(["A1"], rejected.Coords);
        Assert.Contains("A1", rejected.Detail);
        Assert.Null(match.Board[Coord.Parse("A1")].Occupant);
    }
}
