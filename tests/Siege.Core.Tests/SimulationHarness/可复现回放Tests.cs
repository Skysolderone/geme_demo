using Siege.Core.Board.Maps;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>规格：simulation-harness —— Requirement: 可复现回放</summary>
[Collection(ConsoleRedirect.Collection)]   // cycle-stop 1.1：「旧拼接日志被拒」经 LinkSimFixtures.RunMain 重定向控制台，与其他重定向类并行时互换 stderr（全量两次抓到），同样串行
public class 可复现回放Tests
{
    [Fact]
    public void 纯AI局可凭种子复现()
    {
        // 用某局的种子与配置重跑 → 每一步的征募候选、选择、批次与结算结果完全一致：日志逐行一致，AI 的决策序列与候选集合也一致。
        // 标准难度带种子扰动子流（ai-P{n}），一并覆盖。
        // 变异验证：M-B16 / M-B23（种子被扰动）均使本测试红；本类专属变异见 失败局可复现 的 M-B21。
        RunConfig config = SimFixtures.Config(turnLimit: 8, difficulty: AiDifficulty.Standard);
        MatchSession a = MatchSession.Create(config, 41);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        MatchLog logA = a.Run();
        string dir = SimFixtures.TempDir("replay");
        string path = logA.WriteTo(dir);

        ReplayResult replay = Replayer.ReplayFile(path);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve

        Assert.True(replay.Identical, replay.ToString());
        Assert.Equal(logA.DeterministicText(), replay.Replayed.DeterministicText());
        // 比对的文本必须覆盖快照与事件（M-C5：DeterministicText 只出 header 时上面两条恒真）
        Assert.True(replay.LineCount > logA.Turns.Count + logA.Events.Count, $"回放只比了 {replay.LineCount} 行");
        Assert.Equal(SimFixtures.TurnTexts(logA.Turns), SimFixtures.TurnTexts(replay.Replayed.Turns));
        Assert.False(replay.Replayed.IsFailed);
        Assert.Equal(AiDifficulty.Standard, replay.Replayed.Header.Config.Players[0].Difficulty);

        MatchSession b = MatchSession.Create(config, 41);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        b.Run();
        foreach (PlayerId p in a.Match.Players)
        {
            HeuristicTurnController aiA = a.AiOf(p)!;
            HeuristicTurnController aiB = b.AiOf(p)!;
            Assert.NotEmpty(aiA.Decisions);
            Assert.Equal(aiA.Decisions, aiB.Decisions);
            Assert.Equal(aiA.LastCandidates.Select(c => c.ToString()), aiB.LastCandidates.Select(c => c.ToString()));
        }

        Assert.Equal(a.Match.Serialize(), b.Match.Serialize());
        Assert.Equal(a.Match.Hands.Records, b.Match.Hands.Records);

        // 不同种子不会被误判为一致
        MatchLog other = MatchSession.Create(config, 42).Run();   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        ReplayResult mismatch = Replayer.Compare(logA, other);
        Assert.False(mismatch.Identical);
        Assert.Equal(1, mismatch.FirstDivergentLine);
    }

    [Fact]
    public void 带入对局可回放()
    {
        // 规格 Scenario：回放一局本机玩家带入换型令（连珠子）、三名 AI 各带入 1 件的对局日志 → 开局手牌与原局相同，每一步的征募候选、批次与结算结果逐项一致。
        // 带入按人机对局的口径给出（CarryAi.ForHumanMatch：P0 的换型令不可由种子推出），所以回放必须读首部记录的带入——按配置或种子重抽都会分歧。
        // 变异 M-T1：Replayer 不传首部带入（按配置重建）→ 本测试红。
        const ulong seed = 23;
        var game = new Siege.Core.Determinism.GameSeed(seed);
        System.Collections.Immutable.ImmutableSortedDictionary<PlayerId, Siege.Core.Carry.CarryIn> carries = Siege.Core.Carry.CarryAi.ForHumanMatch(
            game, TestMaps.All, TestMaps.P0, new Siege.Core.Carry.CarryIn(Siege.Core.Carry.SupplyKind.Commission, PieceType.Line), ContentSet.V2);
        Assert.Equal(4, carries.Count);                                   // 前提：三名 AI 各带 1 件
        MatchSession original = MatchTelemetry.对局日志的记录内容Tests.CarriedSession(seed, carries.ToDictionary(kv => kv.Key.Value, kv => kv.Value), turnLimit: 12);
        string opening = string.Join(" | ", original.Match.Players.Select(p => CarryInOut.CarryFixtures.HandText(original.Match, p)));
        MatchLog log = original.Run();
        string path = log.WriteTo(SimFixtures.TempDir("replay-carry"));

        ReplayResult replay = Replayer.ReplayFile(path);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve

        Assert.True(replay.Identical, replay.ToString());
        Assert.Equal(log.DeterministicText(), replay.Replayed.DeterministicText());
        Assert.True(replay.LineCount > log.Turns.Count + log.Events.Count, $"回放只比了 {replay.LineCount} 行");
        Assert.Equal(SimFixtures.TurnTexts(log.Turns), SimFixtures.TurnTexts(replay.Replayed.Turns));
        Assert.Equal(CarryInOut.CarryFixtures.CarryText(log.CarryIns), CarryInOut.CarryFixtures.CarryText(replay.Replayed.CarryIns));
        Assert.Contains("P0:Commission>Line", CarryInOut.CarryFixtures.CarryText(replay.Replayed.CarryIns), StringComparison.Ordinal);
        Assert.Contains("Line×1+0", opening, StringComparison.Ordinal);   // 开局手牌确实含换入的连珠子
    }

    [Fact]
    public void 旧日志按无带入回放()
    {
        // 规格正文（可复现回放）：缺带入字段的旧日志按无带入回放。旧日志样本：一局新写出的关闭带入的日志删掉首部三项（开关、带入、配置的带入数量），
        // 引入带入带出之前的首部就是如此。回放 MUST 逐行一致——重建出的首部也不得多出这三项（否则第 1 行即分歧）。
        // 变异 M-T7：按缺字段的旧日志回放时仍写首部带入两项 → 本测试红。
        MatchLog log = MatchSession.Create(SimFixtures.Config(turnLimit: 8), 17).Run();   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        string[] lines = log.FullText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = System.Text.Json.Nodes.JsonNode.Parse(lines[0])!.AsObject();
        Assert.True(header.Remove("CarryInOut") && header.Remove("CarryIns") && header["Config"]!.AsObject().Remove("CarryIn"), "新首部应写出三项");
        lines[0] = header.ToJsonString();
        string dir = SimFixtures.TempDir("replay-legacy-carry");
        string path = Path.Combine(dir, "match-legacy.jsonl");
        File.WriteAllText(path, string.Join('\n', lines) + "\n");

        ReplayResult replay = Replayer.ReplayFile(path);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve

        Assert.True(replay.Identical, replay.ToString());
        Assert.Null(replay.Replayed.Header.CarryInOut);
        Assert.Null(replay.Replayed.Header.Config.CarryIn);
        Assert.True(replay.LineCount > log.Turns.Count, $"回放只比了 {replay.LineCount} 行");
    }

    [Fact]
    public void 旧日志缺停滞停手字段按未停手回放()
    {
        // stagnation-trace 质检：快照 Stalled（S1）只在停手小回合写出；引入之前跑出、含停手的旧日志没有该字段。回放 MUST 逐行一致（缺字段按未停手，
        // 与离线分析"缺字段计 0"同口径）——落子 / Pass / 提子不变，该字段只是留痕。反向（原日志记了停手、回放文本里没有）仍 MUST 报分歧。
        // 载体与「停滞停手次数可查」同：link:1 简单、停滞大回合数 1（只为短局触发停手）、权重写死为定值前缺省。
        // 变异 M-RP1（check.md）：Replayer.Compare 去掉 WithoutUnrecordedStalls（直接比 actual）→ 本测试红（首个停手小回合分歧）。
        // map-expand-25 段 A2：link:1:4 换成浮空岛后短局里不再停滞，载体改为手写平坦图（LinkSimFixtures.FlatStallSession）。手写图不能按标识重建，
        // "回放"改为同一会话确定性重跑一次、再经 Replayer.Compare（Replayer.Replay 重建之后调用的同一比对，M-RP1 变异点就在其中）与旧式日志比对；
        // 按标识重建地图的那一半由本类其余用例与 每局换图Tests 守。
        MatchLog current = LinkSimFixtures.FlatStallSession(80, 9).Run();   // 种子 9：平坦图上 80 小回合内停手 4 次（relic-redesign W1 重探，原种子 6；探针见 循环提子诊断Tests.停滞停手次数可查）
        MatchLog rerun = LinkSimFixtures.FlatStallSession(80, 9).Run();
        Assert.Contains(current.Turns, t => t.Stalled == true);   // 样本口径：这局确有停手
        MatchLog legacy = MatchLog.Parse(current.FullText());
        for (int i = 0; i < legacy.Turns.Count; i++)
        {
            legacy.Turns[i] = legacy.Turns[i] with { Stalled = null };
        }

        Assert.DoesNotContain("\"Stalled\"", legacy.FullText(), StringComparison.Ordinal);   // 样本口径：旧式日志不含该字段

        ReplayResult replay = Replayer.Compare(legacy, rerun);
        Assert.True(replay.Identical, replay.ToString());
        Assert.True(replay.LineCount > legacy.Turns.Count, $"回放只比了 {replay.LineCount} 行");
        Assert.Contains(replay.Replayed.Turns, t => t.Stalled == true);   // 返回的回放日志不被改写
        Assert.True(Replayer.Compare(current, rerun).Identical);   // 新日志照常逐行一致

        ReplayResult reverse = Replayer.Compare(current, legacy);
        Assert.False(reverse.Identical);
        Assert.Contains("\"Stalled\"", reverse.Expected, StringComparison.Ordinal);
    }

    [Fact]
    public void 失败局可复现()
    {
        // 批量跑局中某局因断言失败而终止 → 保留种子、配置与终止前完整日志（文件名带 failed），可单独重跑复现同一失败。
        // 用配置里的测试专用注入点在第 5 个小回合触发断言失败；批量里其他局不受影响。
        // 变异验证 M-B21：MatchSession.Fail 丢弃已记录的事件（Events = []）→ 红 1（本测试）。
        // 权重与停手阈值写死为 ai-eye 4.5 定值之前的缺省：三局都要走到第 5 个小回合才触发注入（默认阈值 80 下简单难度可能第 1 大回合全员 Pass 终局；段 D2 改写）。
        RunConfig config = SimFixtures.PinPreCalibration(
            SimFixtures.Config(count: 3, seedStart: 51, turnLimit: 12, retention: EventRetention.SnapshotsOnly, injectFailureAtTurn: 5));
        string dir = SimFixtures.TempDir("failed");

        BatchSummary summary = BatchRunner.ExecuteToDirectory(config, dir, parallelism: 3);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve

        Assert.Equal((3, 0, 3), (summary.Count, summary.Completed, summary.Failed));
        Assert.Equal(3, summary.FailedFiles.Count);
        Assert.All(summary.FailedFiles, f => Assert.EndsWith("-failed.jsonl", f));
        Assert.Empty(Directory.GetFiles(dir, "match-*.jsonl").Where(f => !f.EndsWith("-failed.jsonl", StringComparison.Ordinal)));

        MatchLog failed = MatchLog.Read(Path.Combine(dir, "match-0000000000000033-failed.jsonl"));
        Assert.True(failed.IsFailed);
        Assert.Null(failed.Result);
        Assert.Equal(51UL, failed.Seed);
        Assert.Equal(5, failed.Failure!.Turn);
        Assert.Equal(typeof(SimAssertionException).FullName, failed.Failure.ExceptionType);
        Assert.Contains("第 5 个小回合", failed.Failure.Message);
        Assert.Equal(5, failed.Turns.Count);
        // 首部配置 = 跑局配置（阈值已显式写死；"未配置的阈值落成缺省值写入首部"由 停手阈值Tests.阈值进入记录 钉住）。
        // carry-in-out 段 C：未配置的带入数量落成 0 写进首部（写明关闭），期望随之补上这一项。
        // retire-legacy-maps 1.8：共用配置改到 link:1（157 格 > 150），未配置的候选格上限按地图落成 24 写进首部，期望随之补上这一项。
        // ai-seat-vision D1：未配置的 AI 视野落成 true（按座位视野）写进首部（缺省值也写），期望随之补上这一项。
        // cycle-stop 2.1 / 2.2：未配置的大回合上限与停滞大回合数落成 100 / 6 写进首部，期望随之补上这两项。
        // ai-whole-batch 1.3：未配置的整批候选开关落成 1 写进首部，期望随之补上这一项。
        // ai-bridge-candidate 1.3：未配置的搭桥候选开关同样落成 1 写进首部。
        Assert.Equal((config with { CarryIn = 0, CandidateCellLimit = Siege.Core.Ai.AiSearchConfig.LargeMapCellLimit, RoundCap = Siege.Core.Match.MatchOptions.DefaultRoundCap, StagnationRounds = Siege.Core.Ai.AiSearchConfig.DefaultStagnationRounds, WholeBatch = Siege.Core.Ai.AiSearchConfig.DefaultWholeBatch, EditSeed = Siege.Core.Ai.AiSearchConfig.DefaultEditSeed }).ToJson(), failed.Header.Config.ToJson());
        // 失败局强制保留完整事件流（含细粒度事件），不受 SnapshotsOnly 影响
        Assert.Equal(EventRetention.Full, failed.Header.Retention);
        Assert.Contains(failed.Events, e => e.Type == LogEventType.Candidates);
        Assert.Contains(failed.Events, e => e.Type == LogEventType.MajorRoundEnded && e.MajorRound == 1);

        ReplayResult replay = Replayer.Replay(failed);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        Assert.True(replay.Identical, replay.ToString());
        Assert.True(replay.Replayed.IsFailed);
        Assert.Equal(failed.Failure.ExceptionType, replay.Replayed.Failure!.ExceptionType);
        Assert.Equal(5, replay.Replayed.Failure.Turn);

        // 去掉注入点重跑同一种子 → 正常完成，前 5 个小回合与失败局逐条一致
        MatchLog healthy = MatchSession.Create(config with { InjectFailureAtTurn = null, EventRetention = EventRetention.Full }, 51).Run();   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        Assert.False(healthy.IsFailed);
        Assert.Equal(SimFixtures.TurnTexts(failed.Turns), SimFixtures.TurnTexts(healthy.Turns.Take(5)));
    }

    [Theory]
    [InlineData("header", "\"FinalCountdownRounds\":2,", "FinalCountdownRounds")]
    [InlineData("header", "\"PairingSeed\":7,", "PairingSeed")]
    [InlineData("header", "\"SeatVision\":false,", "SeatVision")]
    [InlineData("event", "{\"Kind\":\"event\",\"Seq\":999,\"Turn\":12,\"MajorRound\":4,\"Type\":\"PairingApplied\",\"Pairing\":{\"Sequence\":1,\"Kind\":\"FirstRound\",\"MajorRound\":4,\"Candidates\":[0,1,2,3],\"Pairs\":[[0,1],[2,3]],\"Source\":\"script\"}}", "PairingApplied")]
    [InlineData("event", "{\"Kind\":\"event\",\"Seq\":999,\"Turn\":12,\"MajorRound\":4,\"Type\":\"BattlefieldsLinked\",\"Link\":{\"Sequence\":1,\"X\":0,\"Y\":1,\"Side\":\"East\",\"MapDigest\":\"00\",\"Summary\":\"x\"}}", "BattlefieldsLinked")]
    [InlineData("event", "{\"Kind\":\"event\",\"Seq\":999,\"Turn\":12,\"MajorRound\":4,\"Type\":\"FinalCountdown\",\"Countdown\":{\"Sequence\":1,\"Battlefield\":0,\"Start\":7,\"Deadline\":9,\"AssignedInMajorRound\":7}}", "FinalCountdown")]
    [InlineData("turn", "\"WaitingRounds\":1,", "WaitingRounds")]
    public void 旧拼接日志被拒(string where, string fragment, string field)
    {
        // 规格 simulation-harness「旧拼接日志被拒」/ match-telemetry「旧拼接日志被拒绝」（open-map 裁决 B2）：含配对 / 链接事件或决赛倒计时字段的改动前日志
        // 在解析时（回放、analyze、replay 命令共用 MatchLog.Parse）MUST 明确拒绝并提示属于已移除的拼接规则——先于地图摘要比对。
        // 样本：真跑一局（截断 8 个小回合）的日志文本，按行注入旧字段 / 旧事件：首部配置里加 K / 配对种子 / 视野，事件流里加配对 / 链接 / 倒计时事件，快照玩家项里加等待大回合数。
        // 反面：未注入的同一份照常解析并回放一致。
        MatchLog original = MatchSession.Create(SimFixtures.Config(turnLimit: 8), 41).Run();
        string text = original.FullText();
        string[] lines = text.Split('\n');
        string tampered = where switch
        {
            "header" => text.Replace("\"MapId\":", fragment + "\"MapId\":", StringComparison.Ordinal),
            "event" => string.Join('\n', lines.Take(2).Append(fragment).Concat(lines.Skip(2))),
            _ => text.Replace("\"PowerAtDefeat\":", fragment + "\"PowerAtDefeat\":", StringComparison.Ordinal) is var t && t != text ? t
                : text.Replace("\"HasEstablishedPower\":", fragment + "\"HasEstablishedPower\":", StringComparison.Ordinal),
        };
        Assert.NotEqual(text, tampered);   // 样本口径：确实注入了
        Assert.Contains(field, tampered, StringComparison.Ordinal);

        FormatException ex = Assert.Throws<FormatException>(() => MatchLog.Parse(tampered));
        Assert.StartsWith(MatchLog.RetiredLinkMessage, ex.Message, StringComparison.Ordinal);
        Assert.Contains("拼接规则已移除", ex.Message, StringComparison.Ordinal);
        Assert.Contains(field, ex.Message, StringComparison.Ordinal);

        // replay 命令与 analyze 命令都经同一解析入口拒绝（退出码非 0、提示同一文案）。
        string dir = SimFixtures.TempDir($"retired-log-{field}");
        string path = Path.Combine(dir, "match-0000000000000029.jsonl");
        File.WriteAllText(path, tampered);
        (int replayCode, string replayOut, string replayErr) = LinkSimFixtures.RunMain(null, null, "replay", "--file", path);
        Assert.NotEqual(0, replayCode);
        Assert.Contains("拼接规则已移除", replayErr + replayOut, StringComparison.Ordinal);
        (int analyzeCode, string analyzeOut, string analyzeErr) = LinkSimFixtures.RunMain(null, null, "analyze", "--dir", dir);
        Assert.NotEqual(0, analyzeCode);
        Assert.Contains("拼接规则已移除", analyzeErr + analyzeOut, StringComparison.Ordinal);

        // 反面：未注入的日志照常解析、回放一致。
        Assert.True(Replayer.Replay(MatchLog.Parse(text)).Identical);
    }

    [Theory]
    [InlineData(null, "首部缺模式字段（Mode）")]
    [InlineData("Legacy", "首部模式字段（Mode）为 Legacy")]
    public void 回放接口拒绝非链接日志(string? mode, string expected)
    {
        // retire-legacy-rules L17（design D5 / R10）：Replayer 只回放链接模式的日志——首部 Mode 缺失或不为 "Link" 即 FormatException 并点名原因，不重跑。
        // open-map 段 B：原 可复现回放_链接模式Tests 的同名用例随文件删除，直调 Replayer.Replay 的这条守门搬到这里（载体换成本类的真跑局日志）。
        // 变异：去掉 Replayer 入口的模式检查 → 两行都不抛，本测试红。
        MatchLog link = SimFixtures.Clone(MatchSession.Create(SimFixtures.Config(turnLimit: 8), 41).Run());
        Assert.True(link.IsLinkMode);
        MatchLog tampered = new() { Header = link.Header with { Mode = mode }, Turns = link.Turns, Events = link.Events, Result = link.Result };
        FormatException ex = Assert.Throws<FormatException>(() => Replayer.Replay(tampered));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.Contains("不是链接模式的日志", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void replay命令拒绝旧日志()
    {
        // 实现裁决 C3 / match-telemetry「旧日志被拒绝」：replay 命令拒绝地图标识不是 link: 的日志并提示属于已移除的地图模式（命令级，与 analyze 同口径）。
        // open-map 段 B 质检（R-B4）：原 可复现回放_链接模式Tests 的同名用例随文件删除、没有同名替代，原样搬到这里（载体为合成日志，与配对无关）。
        // 变异 MC-11：Program.Replay 去掉地图标识检查 → 旧日志照常回放、退出码 0，本测试红。
        MatchLog synthetic = SimFixtures.Synthetic(1, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(6, [0]));
        MatchLog legacy = new() { Header = synthetic.Header with { MapId = "siege-4p-base-v5" }, Turns = synthetic.Turns, Events = synthetic.Events, Result = synthetic.Result };
        Assert.Equal("siege-4p-base-v5", legacy.Header.MapId);
        string path = legacy.WriteTo(SimFixtures.TempDir("link-replay-legacy"));

        (int code, string output, string err) = LinkSimFixtures.RunMain(null, null, "replay", "--file", path);
        Assert.Equal(1, code);
        Assert.Contains("已移除的地图模式", err, StringComparison.Ordinal);
        Assert.DoesNotContain("回放一致", output, StringComparison.Ordinal);
    }
}
