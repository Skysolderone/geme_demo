using System.Collections.Immutable;
using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/ai-decision —— Requirement: 正式对战 AI 的信息边界（第一版例外：全知集中一处、可关；候选不越战场）。
/// 实现裁决 C4，tasks 3.3。
/// ai-seat-vision（openspec/changes/ai-seat-vision/specs/ai-decision，裁决 V1 / D1 / D4，tasks 1.3 / 1.4 / 2.1）MODIFIED 同一 Requirement：AI 缺省按座位视野，
/// 全知只作批次配置顶层的显式配置项；新 Scenario「缺省按座位视野」「全知仅作显式配置」「全知配置下与改前相同」。
/// </summary>
public class 正式对战AI的信息边界_链接模式Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 候选不越战场(bool seatVision)
    {
        // 规格 Scenario「候选不越战场」：正式 AI 在双人战场 1 内决策、战场 4 尚未与之链接 → 候选落点全部位于战场 1 内（全知与关闭全知都如此）。
        // 开局 P0、P1 同在棋盘 1（内部 0），P2、P3 在棋盘 4、5；第 3 大回合结束时 4、5 链接，棋盘 1 不参与配对。跑到第 4 大回合，看 P0 / P1 的决策。
        // 变异 MC-19：PublicRules.LegalRange 的链接模式分支改为全图可落子格 → 候选越出战场，本测试红。
        MatchFlow match = Started([0, 0, 3, 4], new ScriptedPairingSource());
        AiSearchConfig config = AiSearchConfig.Easy with { SeatVision = seatVision };
        var runner = new MatchRunner(match);
        // 样本口径（P0、P1 到第 4 大回合仍在局）依赖 AI 实际走法：权重写死，不跟随默认值（testing.md「依赖 AI 实际怎么走的断言要把权重写死」）。
        var ais = match.Players.ToDictionary(p => p, p => HeuristicAi.Create(match, p, AiDifficulty.Easy, SimFixtures.PreCalibrationWeights, config));
        foreach ((PlayerId p, HeuristicTurnController ai) in ais)
        {
            runner.SetController(p, ai);
        }

        while (match.Phase == MatchPhase.InProgress && match.MajorRound < 4)
        {
            runner.RunTurn();
        }

        // 样本口径：棋盘 1 的双人战场仍未结算、两人都在局，且没有与 4、5 的合并战场链接。
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        string why = string.Join(" | ", match.PlayerStates.Select(s => $"{s.Player}:{s.Status}:{s.EliminationReason}:{s.EliminatedInMajorRound}"))
            + " || " + string.Join(" | ", match.Settlements.Select(s => $"{s.MajorRound}:{s.Battlefield}:{s.Reason}:{s.Winner}"));
        Assert.True(match.StateOf(P0).Status == PlayerStatus.Active && match.StateOf(P1).Status == PlayerStatus.Active, why);
        Assert.Single(match.Links);
        Assert.NotEqual(match.BattlefieldOf(P0), match.BattlefieldOf(P2));

        int checkedTurns = 0;
        while (match.Phase == MatchPhase.InProgress && match.MajorRound == 4)
        {
            PlayerId current = match.CurrentPlayer!.Value;
            ImmutableHashSet<Coord> field = [.. match.Board.Battlefields.ById(match.BattlefieldOf(current))!.Cells];
            runner.RunTurn();
            if (current == P0 || current == P1)
            {
                ImmutableArray<Coord> candidates = ais[current].LastCandidateCells;
                Assert.NotEmpty(candidates);   // 样本口径：确实做了部署决策
                Assert.All(candidates, c => Assert.Contains(c, field));
                Assert.All(ais[current].LastChoice?.Placements ?? [], p => Assert.Contains(p.Coord, field));
                checkedTurns++;
            }
        }

        Assert.Equal(2, checkedTurns);
    }

    [Fact]
    public void 全知例外集中可关()
    {
        // 规格 Scenario「全知例外集中可关」：关闭 AI 全知开关 → AI 的读取接口对其他战场返回"未展开"（没有棋子、信物未揭示、势力与手牌类型不可见），
        // 评价代码不需修改即可运行。缺省为全知（第一版例外）。
        // 变异 MC-20：HeuristicAi.VisionFor 恒给 match.Publish → 关闭全知后 AI 仍读到他人战场的棋子，本测试红；
        // MC-21：PublishSeatProjection 不重算势力（沿用全知势力快照）→ 他人势力断言红。
        MatchFlow match = Started([0, 0, 3, 4]);
        Coord mine = BirthCell(Map, 0, 1, 1);
        Coord theirs = BirthCell(Map, 3, 2, 2);
        match.Place(P0, mine).Place(P2, theirs, BirthCell(Map, 3, 2, 3));
        match.Debug.SeedHand(P2, (PieceType.Fortress, 1));

        MatchPublicView full = match.Publish();
        MatchPublicView seat = match.PublishSeatProjection(P0);
        Assert.NotNull(full.Board[theirs].Occupant);   // 样本口径：他人战场上确实有棋子
        Assert.True(full.Power!.Of(P2).Total > 0);

        // 他人战场：没有棋子、势力为 0（只计本战场）、手牌类型不可见、信物未揭示且无人控制。
        Assert.Null(seat.Board[theirs].Occupant);
        Assert.Empty(seat.Board.AllCoords().Where(c => seat.Board[c].Occupant is { } o && o.Owner != P0 && o.Owner != P1));
        Assert.Equal(0, seat.Power!.Of(P2).Total);
        Assert.Empty(seat.Hands.Single(h => h.Player == P2).Types);
        Assert.NotEmpty(full.Hands.Single(h => h.Player == P2).Types);
        ImmutableHashSet<Coord> field = [.. match.Board.Battlefields.ById(match.BattlefieldOf(P0))!.Cells];
        Assert.All(seat.Relics.Where(r => !field.Contains(r.Coord)), r => Assert.False(r.IsRevealed));

        // 本战场：与全知视图逐格相同，自己的势力不变；地形与全局项不过滤。
        Assert.Equal(full.Board[mine].Occupant, seat.Board[mine].Occupant);
        Assert.Equal(full.Power.Of(P0).Total, seat.Power.Of(P0).Total);
        Assert.Equal(full.Hands.Single(h => h.Player == P1).Types, seat.Hands.Single(h => h.Player == P1).Types);
        Assert.Equal(full.ActionOrder, seat.ActionOrder);
        Assert.Equal(full.Players, seat.Players);
        Assert.Equal(full.Board.Map, seat.Board.Map);

        // 分流只在 VisionFor。ai-seat-vision 1.3（裁决 V1）：缺省翻转——缺省（null / 预设）= 按座位投影 + 同口径的预演结果；显式全知（false）= 全知视图、预演结果原样。
        // 原断言"缺省（null / 预设）= 全知、预演口径为 null"随缺省翻转改为下面五行（期望反向，另补显式全知两行）。
        Assert.Null(HeuristicAi.VisionFor(match, P0, null).Observe().Board[theirs].Occupant);
        Assert.NotNull(HeuristicAi.VisionFor(match, P0, null).Rehearsal);
        Assert.Null(HeuristicAi.VisionFor(match, P0, AiSearchConfig.Standard).Observe().Board[theirs].Occupant);
        Assert.NotNull(HeuristicAi.VisionFor(match, P0, AiSearchConfig.Standard with { SeatVision = false }).Observe().Board[theirs].Occupant);
        Assert.Null(HeuristicAi.VisionFor(match, P0, AiSearchConfig.Standard with { SeatVision = false }).Rehearsal);
        AiVisionPorts closed = HeuristicAi.VisionFor(match, P0, AiSearchConfig.Standard with { SeatVision = true });
        Assert.Null(closed.Observe().Board[theirs].Occupant);
        var rehearsal = new Batch.RehearsalResult(true, false, null, [], match.Board.Clone());
        Assert.NotNull(rehearsal.ProjectedBoard![theirs].Occupant);
        Assert.Null(closed.Rehearsal!(rehearsal).ProjectedBoard![theirs].Occupant);
        Assert.Equal(match.Board[mine].Occupant, closed.Rehearsal(rehearsal).ProjectedBoard![mine].Occupant);
        Assert.NotNull(rehearsal.ProjectedBoard[theirs].Occupant);   // 投影在副本上做，不改原预演结果
        // ai-seat-vision 1.3：原断言"四档预设都是全知"随缺省翻转反向——四档预设都按座位视野。
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.True(AiSearchConfig.ForDifficulty(d).SeatVision));

        // 旧模式：投影即全知视图（没有战场之分）。
        MatchFlow legacy = MatchFixtures.Started();
        Assert.Equal(legacy.Publish().BoardSerialized, legacy.PublishSeatProjection(MatchFixtures.P0).BoardSerialized);
    }

    [Fact]
    public void 关闭全知时评价代码照常运行()
    {
        // 关闭全知的 AI 走完整的决策（征募、候选、评价、确认）：四名 AI 都用按座位投影的视图跑一个大回合，不抛错、每人都落了子
        // （他人战场的棋子不会因"预演前看不到、预演后看得到"被评价成敌方势力变化而压到停手阈值以下）。
        // 变异 MC-24：VisionFor 的关闭分支不给预演口径（Rehearsal 为 null）→ 先手之后的 AI 全部 Pass，本测试红。
        MatchFlow match = Started([0, 0, 3, 4]);
        match.Place(P2, BirthCell(Map, 3, 2, 2));
        var runner = new MatchRunner(match);
        AiSearchConfig config = AiSearchConfig.Standard with { SeatVision = true };
        foreach (PlayerId p in match.Players)
        {
            // "每人都落了子"依赖 AI 实际走法：权重写死（同上）。
            runner.SetController(p, HeuristicAi.Create(match, p, AiDifficulty.Standard, SimFixtures.PreCalibrationWeights, config));
        }

        for (int i = 0; i < 4; i++)
        {
            PlayerId current = match.CurrentPlayer!.Value;
            int before = match.Board.AllCoords().Count(c => match.Board[c].Occupant?.Owner == current);
            runner.RunTurn();
            Assert.True(match.Board.AllCoords().Count(c => match.Board[c].Occupant?.Owner == current) > before, $"关闭全知的 AI {current} 一子未落。");
        }

        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 视野开关缺省不写出()
    {
        // ai-seat-vision D1：本条整条重写（原守"搜索配置里的开关缺省不写出、关闭时写出并可往返"，开关已移到批次配置顶层）。
        // 搜索配置里的视野项不进任何 JSON：写不出、读不进（读入恒为缺省 true）——视野只记在批次配置顶层一处；配置文件把它写进 Players[].Search 即报错点名，不静默忽略。
        // 变异 MV-5：AiSearchConfig.SeatVision 去掉 [JsonIgnore] → 搜索配置写出 "SeatVision"、读入 false 被绑定，本测试红。
        JsonSerializerOptions options = RunConfig.JsonOptions;
        Assert.DoesNotContain("SeatVision", JsonSerializer.Serialize(AiSearchConfig.Standard, options), StringComparison.Ordinal);
        Assert.DoesNotContain("SeatVision", JsonSerializer.Serialize(AiSearchConfig.Standard with { SeatVision = false }, options), StringComparison.Ordinal);
        string withVision = JsonSerializer.Serialize(AiSearchConfig.Standard, options).Replace("\"ImmediateOnly\": false", "\"ImmediateOnly\": false,\n  \"SeatVision\": false", StringComparison.Ordinal);
        Assert.Contains("\"SeatVision\": false", withVision, StringComparison.Ordinal);   // 样本口径：确实写进了该项
        Assert.True(JsonSerializer.Deserialize<AiSearchConfig>(withVision, options)!.SeatVision);

        string file = new RunConfig { Players = [new PlayerAiConfig { Search = AiSearchConfig.Standard }, new(), new(), new()] }.ToJson()
            .Replace("\"ImmediateOnly\": false", "\"ImmediateOnly\": false,\n      \"SeatVision\": false", StringComparison.Ordinal);
        Assert.Contains("\"SeatVision\": false", file, StringComparison.Ordinal);
        FormatException error = Assert.Throws<FormatException>(() => RunConfig.FromJson(file));
        Assert.Contains("Players[0].Search.SeatVision", error.Message, StringComparison.Ordinal);
        Assert.Contains("顶层", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 全知例外只在一处分流()
    {
        // 规格："该例外 SHALL 在代码中集中于一处"。源码扫描 src/Siege.Core/Ai：
        // ① 视野开关只在 HeuristicAi.cs 读取（定义在 AiDifficulty.cs）——评价代码不读它；② 正式 AI 的 MatchFlow.Publish 委托只在 VisionFor 一处（调试旁路 DebugTurnController / MatchDebugView 除外）；
        // ③ 投影视图与预演口径只经 VisionFor 到达；预演口径只在 HeuristicTurnController 的部署入口套用。配样本口径下界与反面命中。
        string dir = Path.Combine(Determinism.随机子流隔离Tests.SourceRoot(), "src", "Siege.Core", "Ai");
        (string Name, string Text)[] files = [.. Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];
        Assert.True(files.Length >= 10, $"样本口径：只扫到 {files.Length} 个 AI 源文件。");

        Assert.Equal(["AiDifficulty.cs", "HeuristicAi.cs"], files.Where(f => f.Text.Contains("SeatVision", StringComparison.Ordinal)).Select(f => f.Name).Order());
        Assert.Equal(["HeuristicAi.cs"], files.Where(f => f.Text.Contains("PublishSeatProjection(", StringComparison.Ordinal)).Select(f => f.Name));
        string heuristic = files.Single(f => f.Name == "HeuristicAi.cs").Text;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(heuristic, @"match\.Publish\b(?!SeatProjection)"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(heuristic, @"VisionFor\(match, player, config\)").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(heuristic, @"RehearsalView = vision\.Rehearsal").Count);
        Assert.Equal(["HeuristicAi.cs"], files.Where(f => f.Text.Contains("ProjectRehearsal(", StringComparison.Ordinal)).Select(f => f.Name));
        Assert.Equal(["HeuristicAi.cs", "HeuristicTurnController.cs"], files.Where(f => f.Text.Contains("RehearsalView", StringComparison.Ordinal)).Select(f => f.Name).Order());
        Assert.Equal(["DebugTurnController.cs", "HeuristicAi.cs", "MatchDebugView.cs"],
            files.Where(f => System.Text.RegularExpressions.Regex.IsMatch(f.Text, @"match\.Publish\b(?!SeatProjection)")).Select(f => f.Name).Order());
    }

    [Fact]
    public void 缺省按座位视野()
    {
        // ai-seat-vision 规格 Scenario「缺省按座位视野」：以缺省配置建局，正式 AI 在战场 1（P0、P1 的出生棋盘 0）决策，战场 4（P2 的出生棋盘 3）尚未与之链接
        // → AI 的读取接口对战场 4 的格返回"未展开"（没有棋子、信物未揭示），读不到战场 4 玩家的势力数值。
        // 缺省 = 不给配置 / 四档预设 / 按地图取的配置（终端版与图形版都经 ForMap）/ 跑局层缺省配置（会话落成 true、对全部 AI 生效）。
        // 变异 MV-1：AiSearchConfig.SeatVision 构造缺省改回 false → 本测试红。
        MatchFlow match = Started([0, 0, 3, 4]);
        Coord theirs = BirthCell(Map, 3, 2, 2);
        match.Place(P0, BirthCell(Map, 0, 1, 1)).Place(P2, theirs, BirthCell(Map, 3, 2, 3));
        Assert.NotNull(match.Board[theirs].Occupant);   // 样本口径：战场 4 确有棋子与势力
        Assert.True(match.Publish().Power!.Of(P2).Total > 0);
        ImmutableHashSet<Coord> field = [.. match.Board.Battlefields.ById(match.BattlefieldOf(P0))!.Cells];
        Assert.DoesNotContain(theirs, field);

        List<AiSearchConfig?> defaults = [null, .. Enum.GetValues<AiDifficulty>().Select(AiSearchConfig.ForDifficulty),
            .. Enum.GetValues<AiDifficulty>().Select(d => AiSearchConfig.ForMap(d, Map.PlayableCount))];
        foreach (AiSearchConfig? config in defaults)
        {
            Assert.True(config?.SeatVision ?? true);
            HeuristicTurnController ai = HeuristicAi.Create(match, P0, AiDifficulty.Standard, null, config);
            MatchPublicView seen = HeuristicAi.VisionFor(match, P0, config).Observe();
            Assert.Null(seen.Board[theirs].Occupant);
            Assert.All(seen.Relics.Where(r => !field.Contains(r.Coord)), r => Assert.False(r.IsRevealed));
            Assert.Equal(0, seen.Power!.Of(P2).Total);
            Assert.Equal(0, ai.CreateEvaluator().Before.Of(P2).Total);   // 正式 AI 的评价器同样只读到投影
        }

        // 跑局层：缺省配置的会话视野为 true，落到每名 AI 的配置上（含专家落成的搜索配置）。
        var config4 = new RunConfig { MapId = MapId, Players = [new PlayerAiConfig { Difficulty = AiDifficulty.Expert }, new(), new(), new()], TurnLimit = 1 };
        MatchSession session = MatchSession.Create(config4, 1, Map, recorded: false, carry: null, pairing: new ScriptedPairingSource());
        Assert.True(session.SeatVision);
        Assert.All(session.Match.Players, p => Assert.True(session.AiOf(p)!.Config.SeatVision));
    }

    [Fact]
    public void 全知仅作显式配置()
    {
        // ai-seat-vision 规格 Scenario「全知仅作显式配置」：批量跑局的配置显式打开全知 → AI 按全知视图决策，日志首部记录该配置；图形版与终端版没有打开全知的入口。
        // 变异 MV-2：MatchSession 不把会话视野套到显式的搜索配置上 → 显式 Search 的玩家仍按座位视野，本测试红。
        string file = new RunConfig { MapId = MapId, Players = [new PlayerAiConfig { Search = AiSearchConfig.Standard }, new(), new(), new()], TurnLimit = 6 }.ToJson()
            .Replace("\"MapId\"", "\"SeatVision\": false,\n  \"MapId\"", StringComparison.Ordinal);
        RunConfig omniscient = RunConfig.FromJson(file);
        Assert.False(omniscient.SeatVision);
        MatchSession session = MatchSession.Create(omniscient, 1, Map, recorded: false, carry: null, pairing: new ScriptedPairingSource());
        Assert.False(session.SeatVision);
        Assert.All(session.Match.Players, p => Assert.False(session.AiOf(p)!.Config.SeatVision));   // 含显式给了 Search 的 P0
        MatchLog log = session.Run();
        Assert.False(log.Header.Config.SeatVision);
        Assert.Contains("\"SeatVision\":false", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);

        // 缺省值也写：不写该项的配置在首部落成 true。
        MatchLog seat = MatchSession.Create(new RunConfig { MapId = MapId, TurnLimit = 1 }, 1, Map, recorded: false, carry: null, pairing: new ScriptedPairingSource()).Run();
        Assert.Contains("\"SeatVision\":true", seat.DeterministicText().Split('\n')[0], StringComparison.Ordinal);

        // 经命令行 run --config 打开全知：批次 config.json 与日志首部都记 false（截断 1 个小回合，只验接线）。
        string dir = SimFixtures.TempDir("seat-vision-config");
        string configPath = Path.Combine(dir, "omniscient.json");
        File.WriteAllText(configPath, file);
        (int runCode, _, string runError) = SimulationHarness.LinkSimFixtures.RunMain(null, null, "run", "--out", Path.Combine(dir, "out"), "--config", configPath, "--count", "1", "--turn-limit", "1");
        Assert.True(runCode == 0, runError);
        Assert.False(RunConfig.FromJson(File.ReadAllText(Path.Combine(dir, "out", "config.json"))).SeatVision);
        Assert.False(MatchLog.Read(Assert.Single(Directory.GetFiles(Path.Combine(dir, "out"), "match-*.jsonl"))).Header.Config.SeatVision);

        // 命令行没有打开全知的选项（未知选项一律报错，strict-cli）。
        foreach (string option in new[] { "--seat-vision", "--omniscient" })
        {
            (int code, _, string error) = SimulationHarness.LinkSimFixtures.RunMain(null, null, "run", "--out", Path.Combine(Path.GetTempPath(), "siege-seat-vision-cli"), "--map", MapId, option, "false");
            Assert.NotEqual(0, code);
            Assert.Contains(option[2..], error, StringComparison.Ordinal);
        }

        // 终端版（Sim/Play）与图形版（src/godot）的源码不出现视野开关：它们的 AI 配置只经 ForMap（四档恒按座位视野，见「缺省按座位视野」）。
        string root = Determinism.随机子流隔离Tests.SourceRoot();
        string[] entrySources = [.. Directory.GetFiles(Path.Combine(root, "src", "Siege.Sim", "Play"), "*.cs", SearchOption.AllDirectories),
            .. Directory.GetFiles(Path.Combine(root, "src", "godot"), "*.cs", SearchOption.AllDirectories)];
        Assert.True(entrySources.Length >= 5, $"样本口径：只扫到 {entrySources.Length} 个入口源文件。");
        Assert.Empty(entrySources.Where(f => File.ReadAllText(f).Contains("SeatVision", StringComparison.Ordinal)));
        Assert.Contains(entrySources, f => File.ReadAllText(f).Contains("AiSearchConfig.ForMap(", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1UL, AiDifficulty.Standard, "decisions-link1-seed1-omniscient.txt")]
    [InlineData(2UL, AiDifficulty.Standard, "decisions-link1-seed2-omniscient.txt")]
    [InlineData(3UL, AiDifficulty.Standard, "decisions-link1-seed3-omniscient.txt")]
    [InlineData(1UL, AiDifficulty.Easy, "decisions-link1-easy-seed1-omniscient.txt")]
    [InlineData(1UL, AiDifficulty.Hard, "decisions-link1-hard-seed1-omniscient.txt")]
    public void 全知配置下与改前相同(ulong seed, AiDifficulty difficulty, string fixture)
    {
        // ai-seat-vision 规格 Scenario「全知配置下与改前相同」（裁决 V4 / D4，tasks 1.4）：显式打开全知，以同一种子重跑链接图决策序列基线 → 决策序列与引入本条之前逐字节相同。
        // *-omniscient.txt 是改任何代码之前五条基线（decisions-link1-*.txt）的原样副本；缺省（按座位视野）重钉后的五条见 决策序列基线Tests / 决策序列基线_链接模式Tests。
        // 五条都是标准 / 简单 / 困难，没有专家：专家前瞻名册限同战场（裁决 V2）在全知下也改变专家走法，不在本承诺之内（见实施记录 V7）。
        // 变异 MV-3：HeuristicAi.VisionFor 恒给投影（忽略显式全知）→ 种子 2 / 3 红。
        string path = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", fixture);
        Assert.True(File.Exists(path), $"找不到改前基线：{path}");
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(expected.Split('\n').Count(l => l.Length > 0 && char.IsDigit(l[0])) >= 30, "改前基线小回合不足 30 个");

        Assert.Equal(expected, LinkDecisionSequence.Export(seed, 12, difficulty, attach: null, finalCountdown: null, seatVision: false));
    }

    [Fact]
    public void 旧日志缺视野项按全知回放()
    {
        // ai-seat-vision D1 / tasks 2.1：首部配置缺 SeatVision = 该项出现之前的旧日志（当时 AI 缺省全知）→ 回放按全知重建，不按今天的缺省。
        // 样本：显式全知跑一局，从首部去掉该项模拟旧日志，回放逐字节一致；同一份首部配置按回放口径建会话，视野为 false，按新建口径为 true。
        // 变异 MV-4：MatchSession 构造里缺项一律落成 true（忽略 recorded）→ 本测试红。
        RunConfig config = new() { MapId = MapId, TurnLimit = 12, SeatVision = false, EventRetention = EventRetention.Full, FullEventSamplePermille = 0 };
        MatchLog original = MatchSession.Create(config, 3, Map, recorded: false, carry: null, pairing: new ScriptedPairingSource()).Run();
        string text = original.FullText();
        Assert.Contains("\"SeatVision\":false,", text.Split('\n')[0], StringComparison.Ordinal);
        MatchLog legacy = MatchLog.Parse(text.Replace("\"SeatVision\":false,", string.Empty, StringComparison.Ordinal));
        Assert.Null(legacy.Header.Config.SeatVision);

        Assert.False(MatchSession.Create(legacy.Header.Config, legacy.Seed, Map, recorded: true, carry: null, pairing: new ScriptedPairingSource()).SeatVision);
        Assert.True(MatchSession.Create(legacy.Header.Config, legacy.Seed, Map, recorded: false, carry: null, pairing: new ScriptedPairingSource()).SeatVision);
        ReplayResult replay = Replayer.Replay(legacy, Map);
        Assert.True(replay.Identical, replay.ToString());
    }
}
