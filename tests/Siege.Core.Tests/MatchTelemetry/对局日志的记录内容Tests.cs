using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Core.PieceRelics;
using Siege.Core.Tests.PieceRelics;
using Siege.Core.Recruit;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>规格：match-telemetry —— Requirement: 对局日志的记录内容</summary>
/// <remarks>retire-legacy-rules 1.6：手写局面的载体为平坦 8×8 链接档图上四人同锁棋盘 0（LinkModeFixtures.PlainStarted），原 9×9 坐标平移 (+2, +2)（领地分一条 (+1, +1)、带出结算一条按四角映射）。</remarks>
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

        // 1. 地图、种子（relic-redesign W1：旧静态信物分布与揭示时间随旧信物删除；新信物的刷新 / 拾取遥测留给段 C）
        Assert.Equal("link:1:4", log.Header.MapId); // retire-legacy-maps 1.8：共用样本由 v5 改到 link:1（原注：scoring-sites：RunConfig 默认地图切到 v4）
        Assert.Equal(live.Header.Seed, log.Header.Seed);

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

        // 4. 结构参数、行动顺序（relic-redesign W1：信物控制变化事件随旧信物删除）
        Assert.All(log.Turns, t =>
        {
            Assert.True(t.ShowCount >= 5 && t.FreePickCount >= 3 && t.TypeSlots >= 5 && t.DeployLimit >= 3, t.ToString());
            Assert.Equal(4, t.ActionOrder.Count);
        });
        LogEvent roundEnd = log.Events.First(e => e.Type == LogEventType.MajorRoundEnded);
        Assert.True(roundEnd.Values!.ContainsKey("P0.Rank") && !roundEnd.Values.ContainsKey("P0.Bonus"));   // 先锋修正随旧信物删除：先手只按名次

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
        Assert.Equal(LogResult.TurnLimitReason, log.Result!.Reason);
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
        Assert.Equal(live.FullText(), log.FullText());
        Assert.Equal(live.DeterministicText(), MatchLog.Parse(live.FullText().Replace("\n", "\r\n")).DeterministicText());

        // 6（续）规则级终局的最终结果：P1、P2 先弃赛，P0 由 AI 走一个小回合后 P3 弃赛 → 只剩一名参赛玩家，名次取自规则层。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All);
        match.Resign(TestMaps.P1);
        match.Resign(TestMaps.P2);
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config());
        Assert.True(session.RunTurn());
        match.Resign(TestMaps.P3);
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
        // 平地（retire-legacy-rules 1.6 起为平坦 8×8 链接载体的棋盘 0，原 9×9 坐标平移 (+1, +1)）：P0 占 D6–J6 一排 6 子 → 覆盖第 5 / 7 行各 6 格 + 两端 C6、K6 = 14 个独占空格。
        // 随后 P1 落 E7、J5：E7 与 J5 本身被占（−2），E7 使 D7 / F7 变争议（−2），J5 使 H5 变争议（−1）→ 14 − 5 = 9。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5).Stones(TestMaps.P0, "D6", "E6", "F6", "G6", "H6", "J6");
        List<PlayerEntry> before = MatchSession.PlayerEntries(match.Publish());
        match.Stones(TestMaps.P1, "E7", "J5");
        List<PlayerEntry> after = MatchSession.PlayerEntries(match.Publish());

        MatchLog written = SimFixtures.Synthetic(
            1,
            [SimFixtures.Turn(1, 5, 0, [0, 0, 0, 0]) with { PlayersState = before }, SimFixtures.Turn(2, 5, 1, [0, 0, 0, 0]) with { PlayersState = after }],
            [],
            SimFixtures.ResultOf(5, [0]));
        MatchLog log = MatchLog.Parse(written.FullText());

        Assert.Equal(14, log.Turns[0].PlayersState.Single(p => p.Player == 0).TerritoryScore);
        Assert.Equal(9, log.Turns[1].PlayersState.Single(p => p.Player == 0).TerritoryScore);
        // 与权威势力明细逐人一致（P1 的领地分也非零：E8 / K5 / J4 三格）
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
        // retire-legacy-rules 1.6：同 终端对局Tests.状态栏大数势力缩写不撑破一行——12×12 全平地改以链接模式建局（link:90012，整图一块出生棋盘、四人同锁）。
        MapData map = TestMaps.NineByNine() with
        {
            Id = "link:90012:4",
            Width = 12,
            Height = 12,
            Profile = MapProfile.Link,
            Boards = [new BoardPlate(new Coord(0, 0), 12, 12, BoardPlateKind.Birth)],
            BirthZones = [[.. TestMaps.Rect(0, 0, 12, 12)]],
        };
        MatchFlow match = LinkModeFixtures.RequireLink(
            MatchFlow.CreateUnvalidated(map, TestMaps.Seed, TestMaps.All, LinkModeFixtures.PlainOptions(MatchOptions.Immediate)));
        match.PlantSequentially(TestMaps.All.Select(p => (p, 0)));
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 12; x++)
            {
                match.Board.Place(new Coord(x, y), TestMaps.P0, PieceType.Multiplier);
            }
        }

        match.Debug.Recalculate();
        BigInteger expected = 120 * BigInteger.Pow(3, 120) / BigInteger.Pow(2, 120);
        Assert.True(expected > BigInteger.Pow(2, 63));
        Scoring.PlayerPower truth = match.Scoreboard.Latest!.Of(TestMaps.P0);
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
    public void 非法批次也记录()
    {
        // 玩家提交自杀手批次：P1 先占 C4、D3，P0 提交 C3（无气、不提子）→ 确认被拒 → 日志记录该次尝试、失败类别 Suicide 与相关坐标 C3。
        // 变异验证 M-B4：LoggingController.OnRejected 不记录 → 红 1（本测试）。
        // round-cap 3.3：原靠跑局层 maxRounds: 1 硬停；现由对局配置上限 1 以规则原因 MajorRoundLimit 终局。
        MatchFlow match = LinkModeFixtures.PlainStarted(options: MatchOptions.Immediate).Stones(TestMaps.P1, "C4", "D3");
        match.Debug.SetOrder(TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3);
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config(turnLimit: 4));
        session.SetController(TestMaps.P0, new BlindController("C3"));

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
        Assert.Equal(["C3"], rejected.Coords);
        Assert.Contains("C3", rejected.Detail);
        Assert.Null(match.Board[Coord.Parse("C3")].Occupant);
    }

    [Fact]
    public void 新来源可查()
    {
        // more-pieces-relics 规格 Scenario：某次结算后 A 的一条棋串含哨兵子、哨兵加值为 4 → 日志中该棋串的来源拆分记录哨兵 4，七项来源之和等于位置加值。
        // P0 哨兵 H7 气边邻接 P1 的 G7、J7；v2 局写出四种新来源（含 0），v1 局不写（null，日志与引入新棋子之前逐字节相同）。
        // 写入 → 文本 → 读回：七项之和 = 军势 − 基础（无倍增子），并与活对象的位置加值一致。变异 MC-L1（写入端漏写哨兵来源）应红。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All).Stones(TestMaps.P1, "G7", "J7");
        match.Board.Place(Coord.Parse("H7"), TestMaps.P0, PieceType.Sentry);
        match.Debug.Recalculate();
        Scoring.GroupPower live = match.Scoreboard.Latest!.GroupContaining(TestMaps.P0, "H7");
        Assert.Equal(4, live.SentryBonus);

        GroupEntry written = Assert.Single(MatchSession.PlayerEntries(match.Publish()).Single(p => p.Player == 0).Groups);
        string writtenText = JsonSerializer.Serialize(written, LogJson.Options);
        GroupEntry g = JsonSerializer.Deserialize<GroupEntry>(writtenText, LogJson.Options)!;
        Assert.Equal(4, g.SentryBonus);
        Assert.Equal(((int?)0, (int?)0, (int?)0), (g.BannerBonus, g.ChainBonus, g.BoundaryBonus));
        int seven = g.LineBonus + g.SynergyBonus + g.HighGroundBonus!.Value + g.BannerBonus!.Value + g.ChainBonus!.Value + g.SentryBonus!.Value + g.BoundaryBonus!.Value;
        Assert.Equal(live.PositionBonus, seven);
        Assert.Equal(g.Power - g.Base, seven);

        // v1 局（同一几何、H7 换成普通子）：四种新来源与计分信物子拆分都不出现在日志文本里。
        MatchFlow v1 = LinkModeFixtures.PlainStarted(options: MatchOptions.Immediate with { ContentSet = ContentSet.V1 }).AtRound(5, TestMaps.All).Stones(TestMaps.P1, "G7", "J7");
        v1.Board.Place(Coord.Parse("H7"), TestMaps.P0, PieceType.Basic);
        v1.Debug.Recalculate();
        string v1Text = JsonSerializer.Serialize(MatchSession.PlayerEntries(v1.Publish()), LogJson.Options);
        foreach (string field in new[] { "BannerBonus", "ChainBonus", "SentryBonus", "BoundaryBonus" })
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


        // 首部缺内容集的合成旧日志与一份既有的 v1 样本日志：离线解析后内容集都是 v1，新字段都不存在。
        MatchLog synthetic = MatchLog.Parse(SimFixtures.Synthetic(3, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(1, [0])).FullText());
        Assert.Null(synthetic.Header.Config.ContentSet);
        Assert.Equal(ContentSet.V1, synthetic.ContentSet);
        MatchLog sample = MatchLog.Parse(SimFixtures.Sample.Value[0].FullText());
        Assert.Equal(ContentSet.V1, sample.ContentSet);
        Assert.All(sample.Turns.SelectMany(t => t.PlayersState).SelectMany(p => p.Groups), grp => Assert.Null(grp.SentryBonus));
    }

    // ---------- 带入带出（relic-redesign W2）：首部带入清单、带出结算、旧日志 ----------

    [Fact]
    public void 首部记录带入()
    {
        // 规格 Scenario（接线段 W2 改写：带入的是信物清单）：玩家 1 带入战鼓（保险）与军旗、玩家 2 带入号令、玩家 3 带入望台与百变（均保险）、玩家 4 不带入
        // → 日志首部逐名记录这三份清单与开关状态（不带入的玩家不列）。两条腿：写出文本 → 解析 → 逐项比对活对象（对局配置里的带入）；首部字面值由测试写死。
        var carries = new Dictionary<int, RelicLoadout>
        {
            [0] = CarryWiringFixtures.Loadout((RelicKind.WarDrum, true), (RelicKind.Banner, false)),
            [1] = CarryWiringFixtures.Loadout((RelicKind.Command, false)),
            [2] = CarryWiringFixtures.Loadout((RelicKind.Watchtower, true), (RelicKind.Chameleon, true)),
        };
        MatchSession session = CarriedSession(5, carries, turnLimit: 4);
        MatchLog live = session.Run();
        MatchLog log = MatchLog.Parse(live.FullText());

        Assert.Equal(true, log.Header.CarryInOut);
        Assert.Equal(["0:WarDrum!,Banner", "1:Command", "2:Watchtower!,Chameleon!"],
            log.Header.CarryIns!.Select(e => $"{e.Player}:{string.Join(",", e.Items!.Select(i => i.Kind + (i.Insured ? "!" : string.Empty)))}"));
        Assert.True(log.CarryInOut);
        Assert.Equal(session.Match.CarryIns, log.CarryIns);
        Assert.Equal(live.DeterministicText(), log.DeterministicText());

        // 反面：首部带入解析不了即响亮失败，不静默当成无带入。
        MatchLog broken = MatchLog.Parse(live.FullText().Replace("\"Kind\":\"Command\"", "\"Kind\":\"Lottery\"", StringComparison.Ordinal));
        Assert.Throws<FormatException>(() => broken.CarryIns);
    }

    [Fact]
    public void 带出结算可查()
    {
        // 规格 Scenario（接线段 W2 改写，D6 + R19）：开启带入带出的一局中玩家 4 在第 6 大回合以弃赛时势力名次第 2 弃赛 → 日志中可查到玩家 4 的结算：弃赛、名次 2、带出 8 点、只带回保险件。
        // 局面同 主动弃赛Tests.弃赛时势力名次：C（P2）已出局；A（P0）严格高于 D（P3），B（P1）与 D 相等 → D 第 2。随后 B 也弃赛，A 成为唯一参赛者，规则终局。
        // 带入：A 战鼓（非保险，未挂出）；C 号令（保险）；D 望台（保险）+ 军旗（非保险）。B 不带入。
        // 编号：AtRound 不经大回合开始、没有刷新，游标从 0 起——D 弃赛时军旗按 R17 掉落取 #0（无处可落即消失）；终局时仍未挂出的件按（玩家、清单下标）临时编号 #1 起：A 战鼓 #1、C 号令 #2、D 望台 #3。
        // 归属（D6）：A 完赛 → 未挂出的战鼓归 A（第 ③ 条）；C 出局 → 保险号令归 C（第 ① 条）、0 点；D 弃赛 → 保险望台归 D，掉在地上的军旗无人持有、原主未完赛 → 消失。
        MatchFlow match = LinkModeFixtures.PlainStarted(options: CarryWiringFixtures.On(
                (0, CarryWiringFixtures.Loadout((RelicKind.WarDrum, false))),
                (2, CarryWiringFixtures.Loadout((RelicKind.Command, true))),
                (3, CarryWiringFixtures.Loadout((RelicKind.Watchtower, true), (RelicKind.Banner, false)))))
            .AtRound(6, TestMaps.All)
            .Stones(TestMaps.P2, "C10")
            .Stones(TestMaps.P1, "K3")
            .Stones(TestMaps.P3, "K10");
        match.PlayTurn("C9", "D10");
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(TestMaps.P2).Status);   // 前提：C 已出局
        match.Resign(TestMaps.P3);
        Assert.Equal(2, match.Resignations.Single().RankAtResign);                        // 前提：D 的弃赛名次第 2
        // 前提：军旗按 R17 掉落、取编号 #0。平坦夹具的可落子格全在出生平台上（刷新 / 掉落候选为空），所以它无处可落、当场消失（RelicSpawner.Drop 的口径）。
        Assert.Contains(match.Events, e => e.Kind == FlowEventKind.CarriedRelicsDropped && e.Player == TestMaps.P3 && e.Detail.Contains("1 件无处可落", StringComparison.Ordinal));
        Assert.Empty(match.Board.RelicsOnBoard());
        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config());
        match.Resign(TestMaps.P1);
        Assert.Equal(2, match.Resignations[1].RankAtResign);                              // 前提：B 的弃赛名次也是第 2

        MatchLog log = MatchLog.Parse(session.Run().FullText());

        Assert.Equal(nameof(EndReason.LastPlayerStanding), log.Result!.Reason);
        Assert.Equal(
            ["0:Finished:1:24:#1:WarDrum", "1:Resigned:2:8:", "2:Eliminated:-:0:#2:Command", "3:Resigned:2:8:#3:Watchtower"],
            log.Result.CarryOut!.Select(e => $"{e.Player}:{e.Outcome}:{e.Rank?.ToString() ?? "-"}:{e.Points}:{string.Join(",", e.BroughtOut)}"));

        // 弃赛事件带弃赛时势力名次（match-telemetry 第 7 条"弃赛（含弃赛时势力名次）"）：与弃赛快照逐条相同。
        LogEvent resigned = log.Events.Single(e => e.Type == LogEventType.PlayerResigned && e.Player == 3);
        Assert.Equal(2, resigned.Values!["RankAtResign"]);
    }

    [Fact]
    public void 旧日志按无带入解析()
    {
        // 规格 Scenario：离线解析一份引入带入带出之前产生的日志 → 解析成功，开关读为关闭，每名玩家的带入读为无，没有带出结算。
        // 样本两份：合成旧日志；一份新写出的带入日志删掉全部带入字段（首部两项、配置的带入数量、结果的带出结算——引入之前的日志结构就是如此）。
        MatchLog synthetic = MatchLog.Parse(SimFixtures.Synthetic(3, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(1, [0])).FullText());
        Assert.Null(synthetic.Header.CarryInOut);
        Assert.Null(synthetic.Header.CarryIns);
        Assert.Null(synthetic.Header.Config.CarryIn);
        Assert.False(synthetic.CarryInOut);
        Assert.Empty(synthetic.CarryIns);
        Assert.Null(synthetic.Result!.CarryOut);

        MatchLog carried = CarriedSession(7, new Dictionary<int, RelicLoadout> { [1] = CarryWiringFixtures.Loadout((RelicKind.WarDrum, false)) }, turnLimit: 4).Run();
        Assert.True(carried.CarryInOut);                         // 反面：删字段之前确实是带入局
        Assert.NotNull(carried.Result!.CarryOut);
        string[] lines = carried.FullText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = System.Text.Json.Nodes.JsonNode.Parse(lines[0])!.AsObject();
        Assert.True(header.Remove("CarryInOut") && header.Remove("CarryIns") && header["Config"]!.AsObject().Remove("CarryIn"), "新首部应写出三项");
        var result = System.Text.Json.Nodes.JsonNode.Parse(lines[^1])!.AsObject();
        Assert.True(result.Remove("CarryOut"), "新结果行应写出带出结算");
        lines[0] = header.ToJsonString();
        lines[^1] = result.ToJsonString();

        MatchLog legacy = MatchLog.Parse(string.Join('\n', lines));

        Assert.False(legacy.CarryInOut);
        Assert.Empty(legacy.CarryIns);
        Assert.Null(legacy.Result!.CarryOut);
        Assert.Equal(carried.Turns.Count, legacy.Turns.Count);
    }

    /// <summary>
    /// 一局开启带入带出、各玩家带入由测试指定的真实会话（<c>link:1</c>、内容集 v2、Easy）：与 <c>Siege.Sim.Running.MatchSession.Create</c> 同样先落成配置再建局插旗，
    /// 只是带入不从种子抽，而是像人机对局那样由外部给出——这样的带入不可由种子推出，回放必须读首部。
    /// retire-legacy-maps 1.8：原为缺省图 v5 的 Legacy 建局（含冒险概率）；改为 link:1 的链接模式建局（种子派生配对，与 MatchSession.Create 对 link: 的处理相同）。
    /// </summary>
    internal static MatchSession CarriedSession(ulong seed, IReadOnlyDictionary<int, RelicLoadout> carries, int turnLimit)
    {
        MapData map = MapCatalog.Resolve("link:1:4");
        RunConfig config = SimFixtures.Config(turnLimit: turnLimit).ResolvedFor(map);
        MatchFlow match = MatchFlow.Create(map, new GameSeed(seed), TestMaps.All, MatchOptions.Immediate with
        {
            ArtisanWeight = config.ArtisanWeight,
            ContentSet = config.ContentSet!.Value,
            CarryInOut = true,
            CarryIns = carries.ToImmutableSortedDictionary(kv => new PlayerId(kv.Key), kv => kv.Value),
        });
        match.PlantPrototype();
        return MatchSession.ForMatch(match, config);
    }
}
