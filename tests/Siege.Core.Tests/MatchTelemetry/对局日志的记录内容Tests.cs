using System.Numerics;
using System.Text.Json;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
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
        // ai-eye 段 B：样本由第 2 局（种子 12）换为第 1 局（种子 11）。停手阈值（缺省 20）之后种子 12 在 16 个小回合内揭示了全部 13 枚信物，
        // "整局未揭示的标为 null"这一支没有样本（下界响亮失败）；种子 11 在改动前、硬约束后、阈值后都恰有 1 枚未揭示。断言与期望均未改，只换样本。
        MatchLog log = MatchLog.Parse(SimFixtures.Sample.Value[0].FullText());
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

    [Fact]
    public void 新来源可查()
    {
        // more-pieces-relics 规格 Scenario：某次结算后 A 的一条棋串含哨兵子、哨兵加值为 4 → 日志中该棋串的来源拆分记录哨兵 4，七项来源之和等于位置加值。
        // P0 哨兵 F5 气边邻接 P1 的 E5、G5；v2 局写出四种新来源（含 0），v1 局不写（null，日志与引入新棋子之前逐字节相同）。
        // 写入 → 文本 → 读回：七项之和 = 军势 − 基础（无倍增子），并与活对象的位置加值一致。变异 MC-L1（写入端漏写哨兵来源）应红。
        MatchFlow match = MatchFixtures.Started().AtRound(5, MatchFixtures.All).Stones(MatchFixtures.P1, "E5", "G5");
        match.Board.Place(Coord.Parse("F5"), MatchFixtures.P0, PieceType.Sentry);
        match.Debug.Recalculate();
        Scoring.GroupPower live = match.Scoreboard.Latest!.GroupContaining(MatchFixtures.P0, "F5");
        Assert.Equal(4, live.SentryBonus);

        GroupEntry written = Assert.Single(MatchSession.PlayerEntries(match.Publish()).Single(p => p.Player == 0).Groups);
        string writtenText = JsonSerializer.Serialize(written, LogJson.Options);
        GroupEntry g = JsonSerializer.Deserialize<GroupEntry>(writtenText, LogJson.Options)!;
        Assert.Equal(4, g.SentryBonus);
        Assert.Equal(((int?)0, (int?)0, (int?)0), (g.BannerBonus, g.ChainBonus, g.BoundaryBonus));
        int seven = g.LineBonus + g.SynergyBonus + g.HighGroundBonus!.Value + g.BannerBonus!.Value + g.ChainBonus!.Value + g.SentryBonus!.Value + g.BoundaryBonus!.Value;
        Assert.Equal(live.PositionBonus, seven);
        Assert.Equal(g.Power - g.Base, seven);

        // v1 局（同一几何、F5 换成普通子）：四种新来源与计分信物子拆分都不出现在日志文本里。
        MatchFlow v1 = MatchFixtures.Started(options: MatchOptions.Immediate with { ContentSet = ContentSet.V1 }).AtRound(5, MatchFixtures.All).Stones(MatchFixtures.P1, "E5", "G5");
        v1.Board.Place(Coord.Parse("F5"), MatchFixtures.P0, PieceType.Basic);
        v1.Debug.Recalculate();
        string v1Text = JsonSerializer.Serialize(MatchSession.PlayerEntries(v1.Publish()), LogJson.Options);
        foreach (string field in new[] { "BannerBonus", "ChainBonus", "SentryBonus", "BoundaryBonus", "EncampmentBonus", "PincerBonus" })
        {
            Assert.DoesNotContain(field, v1Text, StringComparison.Ordinal);
            Assert.Contains(field, writtenText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 旧日志照常解析()
    {
        // more-pieces-relics 规格 Scenario：引入新棋子之前产生的日志——首部配置没有内容集、棋串没有新来源字段——照常解析，
        // 内容集读为 v1，每条棋串的四种新来源读为 0（字段为 null，按 0 读：规格明文允许这四项回填，与领地分等"MUST NOT 回填"的字段不同）。
        const string oldGroupLine = "{\"Stones\":[\"C1\"],\"Base\":3,\"LineBonus\":0,\"SynergyBonus\":0,\"HighGroundBonus\":0,\"MultiplierCount\":0,\"Power\":3}";
        GroupEntry old = JsonSerializer.Deserialize<GroupEntry>(oldGroupLine, LogJson.Options)!;
        Assert.Equal(((int?)null, (int?)null, (int?)null, (int?)null), (old.BannerBonus, old.ChainBonus, old.SentryBonus, old.BoundaryBonus));
        Assert.Equal(0, old.NewSourceBonus);

        const string oldEditLine = "{\"Action\":\"Bridge\",\"Target\":\"B:E4\",\"Player\":0,\"Artisan\":\"E5\",\"CausedCapture\":false}";
        Assert.Null(JsonSerializer.Deserialize<TerrainEditEntry>(oldEditLine, LogJson.Options)!.ViaWorkshop);

        // 首部缺内容集的合成旧日志与一份既有的 v1 样本日志：离线解析后内容集都是 v1，新字段都不存在。
        MatchLog synthetic = MatchLog.Parse(SimFixtures.Synthetic(3, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(1, [0])).FullText());
        Assert.Null(synthetic.Header.Config.ContentSet);
        Assert.Equal(ContentSet.V1, synthetic.ContentSet);
        MatchLog sample = MatchLog.Parse(SimFixtures.Sample.Value[0].FullText());
        Assert.Equal(ContentSet.V1, sample.ContentSet);
        Assert.All(sample.Turns.SelectMany(t => t.PlayersState).SelectMany(p => p.Groups), grp => Assert.Null(grp.SentryBonus));
        Assert.All(sample.Turns, t => Assert.Null(t.RelaySources));
    }

    [Fact]
    public void 驿站来源与工坊标记可查()
    {
        // 规格第 4、5 条：快照记录展示数的驿站来源与工坊是否生效；改造记录"目标是否经工坊扩展（隔一格）"。真实会话端到端（写入函数 → 日志文本 → 读回）：
        // v2 局，P0 占据已揭示的驿站 H5、工坊 H3、先锋 B8 → 驿站计入 2 枚其他信物（+2），展示数 7，工坊生效；
        // P0 的批次：匠人 E5 隔一格搭桥 E7 + 匠人 C5 搭桥四邻的 D5 → 前者经工坊扩展、后者不是。
        // 变异 MC-L2（快照漏写驿站来源）、MC-L3（改造记录的工坊扩展恒否）应红。v1 局同一脚本：三个字段都不出现在日志文本与快照哈希文本里。
        (MatchLog log, MatchFlow match) = WorkshopSession(ContentSet.V2);
        TurnSnapshot first = MatchLog.Parse(log.DeterministicText()).Turns[0];
        Assert.Equal(0, first.Player);
        Assert.Equal(7, first.ShowCount);
        Assert.Equal(["H5=2"], first.RelaySources!.Select(kv => $"{kv.Key}={kv.Value}"));
        Assert.True(first.WorkshopActive);
        Assert.Equal(
            ["B:D5/False", "B:E7/True"],
            first.Edits!.Select(e => $"{e.Target}/{e.ViaWorkshop}").Order(StringComparer.Ordinal));
        Assert.Equal(
            match.TerrainEdits.Select(r => $"{r.Edit}/{r.ViaWorkshop}").Order(StringComparer.Ordinal),
            first.Edits!.Select(e => $"{e.Target}/{e.ViaWorkshop}").Order(StringComparer.Ordinal));

        (MatchLog v1, _) = WorkshopSession(ContentSet.V1);
        string v1Text = v1.DeterministicText() + string.Join('\n', SimFixtures.TurnTexts(v1.Turns));
        foreach (string field in new[] { "RelaySources", "WorkshopActive", "ViaWorkshop" })
        {
            Assert.DoesNotContain(field, v1Text, StringComparison.Ordinal);
        }
    }

    /// <summary>E7 / D5 深水的局（第 5 大回合、P0 先行）：P0 占据并揭示驿站 H5、工坊 H3、先锋 B8，手里只有匠人；P0 的批次是 E5 隔一格搭桥 E7 + C5 搭桥 D5，跑 1 个小回合。</summary>
    private static (MatchLog Log, MatchFlow Match) WorkshopSession(ContentSet set)
    {
        MatchFlow match = MatchFixtures.Started(
                TestMaps.Terrain(surfaces: [("E7", Surface.DeepWater), ("D5", Surface.DeepWater)]),
                options: MatchOptions.Immediate with { ContentSet = set },
                relics: [("H5", RelicFixtures.Relay()), ("H3", RelicFixtures.Workshop()), ("B8", RelicFixtures.Vanguard())])
            .AtRound(5, MatchFixtures.All);
        foreach (string cell in new[] { "H5", "H3", "B8" })
        {
            match.Board.Place(Coord.Parse(cell), MatchFixtures.P0, PieceType.Basic);
        }

        match.Relics.Reveal(match.Board, 5);
        match.Debug.Recalculate();
        match.Debug.SeedHand(MatchFixtures.P0, (PieceType.Artisan, 3));
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config(turnLimit: 1));
        session.SetController(MatchFixtures.P0, new ArtisanScript(
            BatchFixtures.Artisan("E5", TerrainEdit.Bridge(Coord.Parse("E7"))),
            BatchFixtures.Artisan("C5", TerrainEdit.Bridge(Coord.Parse("D5")))));
        return (session.Run(), match);
    }

    /// <summary>按给定暂放（含改造）部署、不征募的脚本控制者。</summary>
    private sealed class ArtisanScript(params Placement[] placements) : ITurnController
    {
        public void OrganizeHand(PlayerHandAccess hand, int overflow)
        {
        }

        public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
        {
        }

        public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
        {
            foreach (Placement p in placements)
            {
                if (batch.Stage(p.Coord, p.Type, p.Edit) is { } failure)
                {
                    throw new InvalidOperationException($"脚本暂放失败：{failure.Message}");
                }
            }
        }

        public bool OnRejected(StagedBatch batch, BatchFailure failure) => throw new InvalidOperationException($"脚本批次被拒：{failure.Message}");
    }
}
