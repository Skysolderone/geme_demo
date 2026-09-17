using System.Collections.Immutable;
using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Sim.Analysis;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：match-telemetry —— Requirement: 对局日志的记录内容（据点）/ 平衡分析方向 第 10 项；simulation-harness —— Requirement: 批量跑局（据点分值与权重可追溯）。scoring-sites 3.1–3.3。
/// </summary>
public class 据点遥测Tests
{
    private static readonly PlayerId P0 = MatchFixtures.P0;
    private static readonly PlayerId P1 = MatchFixtures.P1;
    private static readonly PlayerId P2 = MatchFixtures.P2;
    private static readonly PlayerId P3 = MatchFixtures.P3;

    [Fact]
    public void 据点控制变化可查()
    {
        // 规格 Scenario：某石碑在第 6 大回合由 A 唯一覆盖变为争议 → 日志可查到该次变化的大回合、小回合、据点坐标、旧状态与新状态。
        // 真实盘面 → MatchSession 写入 → 文件往返 → 字段断言（testing.md：写入路径要有真实盘面端到端）。同局顺带钉住：每名玩家据点分、
        // 位置加值来源含高地（P2 在 h=1 的 H5 压制 h=0 的 P3 H4）、首部据点分值与据点表。
        // 另放一个被 P0 占据的营帐 E4，使据点状态的控制者、玩家据点分、终局名次据点数都是非零 / 非 null 值（M-C1 教训：只用零值钉不住写入端漏写）。
        // 变异验证 M-T1（段 A2）：MatchSession.RecordTurn 不写 SiteControlChanged 事件 → 红 1（本测试）；
        //           M-T2：PlayerEntries 的 HighGroundBonus 不写 → 红 1（本测试）；M-T3：去掉配置与对局分值一致性检查 → 见「跑局配置与对局据点分值不一致即拒绝」；
        //           M-T10：PlayerEntries 的 SiteScore 恒写 0 → 红 1；M-T11：快照 Sites 的 Holder 恒写 null → 红 1；M-T12：Finish 的 StandingEntry.ControlledSites 恒写 0 → 红 1。
        MapData map = MatchFixtures.Map() with
        {
            Sites = ImmutableDictionary<Coord, SiteTier>.Empty.Add(Coord.Parse("E5"), SiteTier.Stele).Add(Coord.Parse("E4"), SiteTier.Tent),
            TerrainData = TestMaps.Terrain(heights: [("H5", 1)]),
        };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), MatchOptions.Immediate with { MaxMajorRounds = 6 });
        foreach (PlayerId p in MatchFixtures.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        match.AtRound(6, [P1, P0, P2, P3]).Stones(P0, "E4").Stones(P2, "H5").Stones(P3, "H4");
        Assert.Equal(SiteControlKind.UniqueCoverage, match.Scoreboard.Latest!.StateAt("E5").Kind);   // 前提

        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config(maxRounds: 6));
        session.SetController(P1, new BlindController("E6"));
        MatchLog live = session.Run();
        Assert.False(live.IsFailed, live.Failure?.Message);
        MatchLog log = MatchLog.Read(live.WriteTo(SimFixtures.TempDir("site-telemetry")));

        LogEvent change = log.Events.First(e => e.Type == LogEventType.SiteControlChanged);
        Assert.Equal((1, 6, (int?)null), (change.Turn, change.MajorRound, change.Player));
        Assert.Equal(["E5"], change.Coords);
        Assert.Equal("E5 Stele: UniqueCoverage:P0 -> Contested", change.Detail);

        TurnSnapshot first = log.Turns[0];
        Assert.Equal(1, first.Player);
        Assert.Equal(
            [("E4", nameof(SiteControlKind.Occupied), (int?)0), ("E5", nameof(SiteControlKind.Contested), (int?)null)],
            first.Sites!.Select(s => (s.Coord, s.Control, s.Holder)));
        Assert.Equal([5L, 0L, 0L, 0L], first.PlayersState.OrderBy(p => p.Player).Select(p => p.SiteScore!.Value));
        GroupEntry high = Assert.Single(first.PlayersState.Single(p => p.Player == 2).Groups);
        Assert.Equal((1, 2L), (high.HighGroundBonus, high.Power));
        Assert.All(log.Turns, t => Assert.NotNull(t.Sites));

        Assert.Equal((5, 15, 45), (log.Header.SiteValues!.Tent, log.Header.SiteValues.Campfire, log.Header.SiteValues.Stele));
        Assert.Equal(
            [("E4", nameof(SiteTier.Tent), (int?)null), ("E5", nameof(SiteTier.Stele), (int?)null)],
            log.Header.Sites!.Select(s => (s.Coord, s.Tier, s.HomeZone)));

        // 终局名次的据点数：P0 的营帐 E4 在对局以大回合上限终局时仍被占据（前提用活对象钉住）。
        Assert.Equal(EndReason.MajorRoundLimit, match.Result!.Reason);
        int p0Sites = match.Result.Of(P0).Input.ControlledSites;
        Assert.True(p0Sites >= 1, $"P0 终局据点数 {p0Sites}");
        Assert.Equal(p0Sites, log.Result!.Standings.Single(s => s.Player == 0).ControlledSites);

        // 活对象与文件解析逐字段一致（据点字段不漏写 / 漏读）
        Assert.Equal(SimFixtures.TurnTexts(live.Turns), SimFixtures.TurnTexts(log.Turns));
        Assert.Equal(
            live.Events.Where(x => x.Type == LogEventType.SiteControlChanged).Select(x => (x.Turn, x.MajorRound, x.Detail)),
            log.Events.Where(x => x.Type == LogEventType.SiteControlChanged).Select(x => (x.Turn, x.MajorRound, x.Detail)));
    }

    [Fact]
    public void 控制者不变而控制方式变化也记事件()
    {
        // 「据点控制变化可查」的补充：旧状态与新状态只要有一项不同就记事件。P1 的 D6 唯一覆盖营帐 E6，第 1 个小回合 P1 落子占据 E6
        // → 控制者仍是 P1、控制方式由唯一覆盖变为占据，日志 MUST 记一条 "E6 Tent: UniqueCoverage:P1 -> Occupied:P1"。
        // 变异验证 N-1（段 A2 检查）：MatchSession 的变化判定 `wasKind != now.Kind || wasHolder != now.Controller` 把 `||` 改成 `&&`
        // → 补本测试前全绿 797（缺口：既有用例的变化都是两项同时变），补后红 1（本测试）。
        MapData map = MatchFixtures.Map() with { Sites = ImmutableDictionary<Coord, SiteTier>.Empty.Add(Coord.Parse("E6"), SiteTier.Tent) };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), MatchOptions.Immediate with { MaxMajorRounds = 6 });
        foreach (PlayerId p in MatchFixtures.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        match.AtRound(6, [P1, P0, P2, P3]).Stones(P1, "D6");
        Assert.Equal(new SiteState(Coord.Parse("E6"), SiteTier.Tent, SiteControlKind.UniqueCoverage, P1), match.Scoreboard.Latest!.StateAt("E6"));   // 前提

        MatchSession session = MatchSession.ForMatch(match, SimFixtures.Config(maxRounds: 6));
        session.SetController(P1, new BlindController("E6"));
        MatchLog live = session.Run();
        Assert.False(live.IsFailed, live.Failure?.Message);
        MatchLog log = MatchLog.Read(live.WriteTo(SimFixtures.TempDir("site-telemetry-kind")));

        Assert.Equal(1, log.Turns[0].Player);
        var site = Assert.Single(log.Turns[0].Sites!);
        Assert.Equal(("E6", nameof(SiteControlKind.Occupied), (int?)1), (site.Coord, site.Control, site.Holder));
        LogEvent change = Assert.Single(log.Events, e => e.Type == LogEventType.SiteControlChanged && e.Turn == 1);
        Assert.Equal((6, (int?)1), (change.MajorRound, change.Player));
        Assert.Equal(["E6"], change.Coords);
        Assert.Equal("E6 Tent: UniqueCoverage:P1 -> Occupied:P1", change.Detail);
    }

    [Fact]
    public void 跑局配置与对局据点分值不一致即拒绝()
    {
        // 与大回合上限同理（round-cap D3）：跑局配置与对局配置只能有一份据点分值，否则日志首部与报告会和规则层分叉。
        MatchFlow match = SiteFixtures.Started(MatchOptions.Immediate, ("E5", SiteTier.Stele));
        RunConfig config = SimFixtures.Config(maxRounds: 15) with { SiteValues = new SiteValues(3, 8, 24) };

        SiegeRuleException error = Assert.Throws<SiegeRuleException>(() => MatchSession.ForMatch(match, config));
        Assert.Contains("据点分值", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 扫档配置可追溯()
    {
        // 规格 Scenario（simulation-harness）：以据点分值 3 / 8 / 24、全部玩家 Safety = 7 执行一批对局 → 配置记录写明 3 / 8 / 24 与四名玩家的完整权重。
        // 读 config.json 原文（不经 RunConfig 反序列化，避免缺省值把漏写掩盖掉）；日志首部的分值取自对局本身。
        // 变异验证 M-T4（段 A2）：BatchRunner 改回写 config.ToJson()（未配置权重的玩家不写 Weights）→ 另一条「批量执行并汇总」红；
        //           M-T5：MatchSession.Create 不把 RunConfig.SiteValues 传入对局 → 红，含本测试（首部 5/15/45）。
        EvaluationWeights safety7 = EvaluationWeights.Default with { Safety = 7 };
        RunConfig config = SimFixtures.Config(count: 1, maxRounds: 1) with
        {
            SiteValues = new SiteValues(3, 8, 24),
            Players = [.. Enumerable.Range(0, 4).Select(_ => new PlayerAiConfig { Difficulty = AiDifficulty.Easy, Weights = safety7 })],
        };
        string dir = SimFixtures.TempDir("sweep-config");

        BatchRunner.ExecuteToDirectory(config, dir, parallelism: 1);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "config.json")));
        JsonElement sites = json.RootElement.GetProperty("SiteValues");
        Assert.Equal((3, 8, 24), (sites.GetProperty("Tent").GetInt32(), sites.GetProperty("Campfire").GetInt32(), sites.GetProperty("Stele").GetInt32()));
        JsonElement[] players = [.. json.RootElement.GetProperty("Players").EnumerateArray()];
        Assert.Equal(4, players.Length);
        Assert.All(players, p =>
        {
            JsonElement w = p.GetProperty("Weights");
            Assert.Equal(
                (10, 8, 6, 7, 4, 20, 2),
                (w.GetProperty("PowerGain").GetInt32(), w.GetProperty("EnemyLoss").GetInt32(), w.GetProperty("Relic").GetInt32(), w.GetProperty("Safety").GetInt32(),
                 w.GetProperty("Growth").GetInt32(), w.GetProperty("Initiative").GetInt32(), w.GetProperty("Supply").GetInt32()));
        });

        MatchLog log = Assert.Single(MatchLog.ReadDirectory(dir));
        Assert.Equal((3, 8, 24), (log.Header.SiteValues!.Tent, log.Header.SiteValues.Campfire, log.Header.SiteValues.Stele));
        Assert.Equal(12, log.Header.Sites!.Count);
    }

    [Theory]
    [InlineData("3/8/24", 3, 8, 24)]
    [InlineData(" 10 / 30 / 90 ", 10, 30, 90)]
    public void 命令行据点分值(string text, int tent, int campfire, int stele)
    {
        Assert.Equal(new SiteValues(tent, campfire, stele), RunConfig.ParseSiteValues(text));
        RunConfig roundTrip = RunConfig.FromJson((new RunConfig() with { SiteValues = new SiteValues(tent, campfire, stele) }).ToJson());
        Assert.Equal(new SiteValues(tent, campfire, stele), roundTrip.SiteValues);
    }

    [Theory]
    [InlineData("3/8", "营帐/篝火/石碑")]
    [InlineData("5/0/45", "篝火")]
    public void 命令行据点分值非法被拒(string text, string fragment)
    {
        ArgumentException error = Assert.ThrowsAny<ArgumentException>(() => RunConfig.ParseSiteValues(text));
        Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 据点主人按地图推导()
    {
        // 分析第 10 项的"篝火主人"与"石碑桥头那家"由 SiteAttribution 按地图推导（不写死坐标表）。v4 上（出生区编号 0 起，对玩家显示 +1）：
        // 营帐按所在出生区；篝火 J2→0、M9→1、E12→2、B5→3（所在河外低地的主人）；
        // 石碑 H5→0（隔栅栏相邻的桥头信物 G5 → 桥 G4 → 南岸 G3 属出生区 0 的河外低地）、J8→1、F9→2、E6→3。
        // 变异验证 M-T6（段 A2）：ReachableWithoutBridges 不排除桥格（区域经桥连通到岛上）→ 红 1（本测试：石碑主人不唯一变 null）。
        MapData v4 = FourPlayerBaseMap.Create();

        ImmutableSortedDictionary<Coord, int?> home = SiteAttribution.HomeZones(v4);

        Assert.Equal(12, home.Count);
        var expected = new Dictionary<string, int>
        {
            ["B3"] = 0, ["L2"] = 1, ["M11"] = 2, ["C12"] = 3,
            ["J2"] = 0, ["M9"] = 1, ["E12"] = 2, ["B5"] = 3,
            ["H5"] = 0, ["J8"] = 1, ["F9"] = 2, ["E6"] = 3,
        };
        Assert.Equal(
            expected.OrderBy(kv => Coord.Parse(kv.Key)).Select(kv => (kv.Key, (int?)kv.Value)),
            home.Select(kv => (kv.Key.ToNotation(), kv.Value)));

        // 合成图上没有桥头信物 → 石碑主人推不出（null），不猜。
        MapData synthetic = MatchFixtures.Map() with { Sites = ImmutableDictionary<Coord, SiteTier>.Empty.Add(Coord.Parse("E5"), SiteTier.Stele) };
        Assert.Null(SiteAttribution.HomeZones(synthetic)[Coord.Parse("E5")]);
    }

    [Fact]
    public void 据点分析分档输出()
    {
        // 规格 Scenario「据点分析分档输出」+「据点分占比口径」（终局四名参赛玩家总势力 600、据点分 180 → 30%）。手算样本（4 个小回合快照）：
        //   营帐 A1（主人 0）：t1 占据 P0、t2 占据 P0、t3 无人、t4 占据 P0 → 被控制 3/4、争议 0/4、首次第 1 大回合、控制者 {P0}（非胜者）→ 胜率 0/1。
        //   篝火 B1（主人 1）：t1 唯一覆盖 P2、t2 争议、t3 唯一覆盖 P1、t4 唯一覆盖 P1 → 被控制 3/4、争议 1/4、首次第 1 大回合、控制者 {P1, P2}（P2 胜）→ 1/2；
        //     主人 P1 控制 2/4（占被控制 2/3），主人首次第 2 大回合。
        //   石碑 C1（主人 2）：t1 无人、t2 唯一覆盖 P2、t3 唯一覆盖 P2、t4 争议；石碑 D1（主人推不出）：t1 争议、其余无人
        //     → 被控制 2/8、争议 2/8、首次第 1 大回合（D1 从未被控制 1 个）、控制者 {P2} → 1/1；桥头那家 P2 控制 2/4（D1 不计，推不出主人 1 个），占被控制 2/2。
        //   终局快照：参赛 P0 300（据点 120）、P1 200（60）、P2 100（0）= 600 / 180 → 30%；已弃赛 P3 1000（500）不计。
        //   高地：参赛棋串 连珠 4 + 协同 2 + 高地 2，另一串高地 1 → 3 / 9；弃赛者棋串高地 10 不计。
        // 被排除样本（testing.md 口径测试必须放一个）：一局 scoring-sites 之前的旧日志（首部与快照都无据点字段）→ 整局排除，计 1。
        // 变异验证 M-T7（段 A2）：Sites 不排除旧日志（把缺字段当"无据点"纳入）→ 红，含本测试（纳入局 2）；
        //           M-T8：终局据点分占比把已弃赛玩家计入 → 红，含本测试；M-T9：主人口径把推不出主人的据点计入分母 → 红，含本测试。
        string[] coords = ["A1", "B1", "C1", "D1"];
        (string Control, int? Holder)[][] states =
        [
            [("Occupied", 0), ("UniqueCoverage", 2), ("Unclaimed", null), ("Contested", null)],
            [("Occupied", 0), ("Contested", null), ("UniqueCoverage", 2), ("Unclaimed", null)],
            [("Unclaimed", null), ("UniqueCoverage", 1), ("UniqueCoverage", 2), ("Unclaimed", null)],
            [("Occupied", 0), ("UniqueCoverage", 1), ("Contested", null), ("Unclaimed", null)],
        ];
        int[] rounds = [1, 1, 2, 3];
        List<TurnSnapshot> turns = [];
        for (int i = 0; i < 4; i++)
        {
            TurnSnapshot t = SimFixtures.Turn(i + 1, rounds[i], i % 4, [300, 200, 100, 1000]);
            List<SiteStateEntry> siteStates = [.. coords.Select((c, k) => new SiteStateEntry { Coord = c, Control = states[i][k].Control, Holder = states[i][k].Holder })];
            long[] siteScores = [120, 60, 0, 500];
            List<PlayerEntry> players = [.. t.PlayersState.Select(p => p with
            {
                SiteScore = siteScores[p.Player],
                Status = p.Player == 3 ? nameof(PlayerStatus.Resigned) : nameof(PlayerStatus.Active),
                Groups = p.Player switch
                {
                    0 => [new GroupEntry { Base = 5, LineBonus = 4, SynergyBonus = 2, HighGroundBonus = 2, Power = 13 }],
                    1 => [new GroupEntry { Base = 1, HighGroundBonus = 1, Power = 2 }],
                    3 => [new GroupEntry { Base = 1, HighGroundBonus = 10, Power = 11 }],
                    _ => [],
                },
            })];
            turns.Add(t with { Sites = siteStates, PlayersState = players });
        }

        MatchLog withSites = SimFixtures.Synthetic(1, turns, [], SimFixtures.ResultOf(3, [2]));
        withSites = new MatchLog
        {
            Header = withSites.Header with
            {
                Sites =
                [
                    new SiteEntry { Coord = "A1", Tier = nameof(SiteTier.Tent), HomeZone = 0 },
                    new SiteEntry { Coord = "B1", Tier = nameof(SiteTier.Campfire), HomeZone = 1 },
                    new SiteEntry { Coord = "C1", Tier = nameof(SiteTier.Stele), HomeZone = 2 },
                    new SiteEntry { Coord = "D1", Tier = nameof(SiteTier.Stele), HomeZone = null },
                ],
                SiteValues = new SiteValuesEntry { Tent = 5, Campfire = 15, Stele = 45 },
            },
            Turns = withSites.Turns,
            Events = withSites.Events,
            Result = withSites.Result,
        };
        MatchLog legacy = SimFixtures.Synthetic(2, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(1, [0]));
        Assert.Null(legacy.Header.Sites);

        BalanceReport report = BalanceAnalyzer.Analyze([withSites, legacy]);
        SiteSection s = report.Sites;

        Assert.Equal((1, 1), (s.Matches, s.Skipped));
        SiteTierStat tent = s.Tiers.Single(t => t.Tier == nameof(SiteTier.Tent));
        Assert.Equal((1, 4L, 3L, 0L, 1.0, 0), (tent.SiteInstances, tent.SiteTurns, tent.ControlledTurns, tent.ContestedTurns, tent.MeanFirstControlledRound, tent.NeverControlled));
        Assert.Equal((0, 1), (tent.WinRateOfControllers.Successes, tent.WinRateOfControllers.Trials));
        SiteTierStat campfire = s.Tiers.Single(t => t.Tier == nameof(SiteTier.Campfire));
        Assert.Equal((4L, 3L, 1L, 0.75, 0.25), (campfire.SiteTurns, campfire.ControlledTurns, campfire.ContestedTurns, campfire.ControlledShare, campfire.ContestedShare));
        Assert.Equal((1, 2), (campfire.WinRateOfControllers.Successes, campfire.WinRateOfControllers.Trials));
        SiteTierStat stele = s.Tiers.Single(t => t.Tier == nameof(SiteTier.Stele));
        Assert.Equal((2, 8L, 2L, 2L, 1.0, 1), (stele.SiteInstances, stele.SiteTurns, stele.ControlledTurns, stele.ContestedTurns, stele.MeanFirstControlledRound, stele.NeverControlled));
        Assert.Equal((1, 1), (stele.WinRateOfControllers.Successes, stele.WinRateOfControllers.Trials));

        Assert.Equal((4L, 3L, 2L, 0.5, 2.0, 0, 0), (s.Campfire.SiteTurns, s.Campfire.ControlledTurns, s.Campfire.OwnerTurns, s.Campfire.OwnerShare, s.Campfire.MeanOwnerFirstControlRound, s.Campfire.OwnerNeverControlled, s.Campfire.UnknownOwner));
        Assert.Equal(2.0 / 3, s.Campfire.OwnerShareOfControlled, 9);
        Assert.Equal((4L, 2L, 2L, 0.5, 1.0, 1.0, 1), (s.SteleBridgehead.SiteTurns, s.SteleBridgehead.ControlledTurns, s.SteleBridgehead.OwnerTurns, s.SteleBridgehead.OwnerShare, s.SteleBridgehead.OwnerShareOfControlled, s.SteleBridgehead.MeanOwnerFirstControlRound, s.SteleBridgehead.UnknownOwner));

        Assert.Equal((0.3, 1), (s.MeanFinalSiteShare, s.FinalShareSamples));
        Assert.Equal((3L, 9L), (s.FinalHighGroundBonus, s.FinalPositionBonus));
        Assert.Equal(1.0 / 3, s.HighGroundShare, 9);

        string text = ReportWriter.Render(report);
        Assert.Contains("### 10. 据点", text);
        Assert.Contains("排除无据点字段的旧日志 1 局", text);
        Assert.Contains("- 营帐（1 个·局）：被控制 75.0%（3/4）", text);
        Assert.Contains("- 篝火由所在低地主人控制：占全部据点小回合 50.0%（2/4），占被控制小回合 66.7%（2/3）；主人首次控制平均第 2 大回合", text);
        Assert.Contains("- 石碑由相邻桥头那家控制：占全部据点小回合 50.0%（2/4）", text);
        Assert.Contains("推不出主人 1 个", text);
        Assert.Contains("- 终局据点分占参赛玩家总势力：平均 30.0%", text);
        Assert.Contains("- 终局高地加值占全部位置加值：33.3%（3/9）", text);
    }
}
