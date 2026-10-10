using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.SimulationHarness.LinkSimFixtures;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/simulation-harness —— Requirement: 可复现回放（配对事件从日志读取）；实现裁决 C3（replay 命令只回放 link: 日志，
/// 测试内部的回放接口仍可回放旧日志）。tasks 3.1。
/// </summary>
[Collection(ConsoleRedirect.Collection)]
public class 可复现回放_链接模式Tests
{
    [Fact]
    public void 纯AI局可凭种子加配对复现()
    {
        // 规格 Scenario「纯 AI 局可凭种子加配对复现」：用种子、配置与日志中的配对事件重跑，每一步的链接、征募候选、选择、批次与结算结果完全一致；
        // 走 replay 命令（--file）同样一致、退出码 0。
        MatchLog original = TwoLinkLog.Value;
        Assert.NotEmpty(original.EventsOf(LogEventType.PairingApplied));   // 样本口径
        Assert.True(Replayer.Replay(original).Identical);

        string dir = SimFixtures.TempDir("link-replay-cli");
        string path = original.WriteTo(dir);
        (int code, string output, string err) = RunMain(null, null, "replay", "--file", path);
        Assert.True(code == 0, err + output);
        Assert.Contains("回放一致", output, StringComparison.Ordinal);
    }

    [Fact]
    public void 失败局可复现()
    {
        // 规格 Scenario「失败局可复现」：链接之后的小回合注入断言失败；失败日志保留种子、配置、已发生的配对事件与终止前的完整日志，可单独重跑复现。
        MatchLog failed = MatchSession.Create(Config(turnLimit: 20) with { InjectFailureAtTurn = 13 }, FourZoneSeed.Value).Run();
        Assert.True(failed.IsFailed);
        Assert.Equal(13, failed.Failure!.Turn);
        Assert.Single(failed.EventsOf(LogEventType.PairingApplied));
        Assert.Equal(2, failed.EventsOf(LogEventType.BattlefieldsLinked).Count);

        ReplayResult replay = Replayer.Replay(SimFixtures.Clone(failed));
        Assert.True(replay.Identical, replay.ToString());
        Assert.True(replay.Replayed.IsFailed);
    }

    [Fact]
    public void 带入对局可回放()
    {
        // 规格 Scenario「带入对局可回放」的链接模式侧（段 C 质检补缺口）：开启带入（每名 AI 从 carry-ai 子流带入 1 件）的链接图对局，
        // 回放按首部记录的带入重建开局手牌、按日志的配对事件重建链接，逐行一致。Legacy 侧由 可复现回放Tests.带入对局可回放 守。
        MatchLog original = MatchSession.Create(Config(turnLimit: 13) with { CarryIn = 1 }, FourZoneSeed.Value).Run();
        Assert.True(original.CarryInOut);                                        // 样本口径：开了带入
        Assert.Equal(4, original.CarryIns.Count);
        Assert.NotEmpty(original.EventsOf(LogEventType.PairingApplied));      // 样本口径：跑过了首轮配对与链接
        Assert.NotEmpty(original.EventsOf(LogEventType.BattlefieldsLinked));

        ReplayResult replay = Replayer.Replay(SimFixtures.Clone(original));

        Assert.True(replay.Identical, replay.ToString());
        Assert.Equal(original.DeterministicText(), replay.Replayed.DeterministicText());
    }

    [Fact]
    public void replay命令拒绝旧日志()
    {
        // 实现裁决 C3 / match-telemetry「旧日志被拒绝」：replay 命令拒绝地图标识不是 link: 的日志并提示属于已移除的地图模式；
        // 测试内部的回放接口（Replayer）仍能回放旧日志（Legacy 夹具的回归护栏）。
        // 变异 MC-11：Program.Replay 去掉地图标识检查 → 旧日志照常回放、退出码 0，本测试红。
        MatchLog legacy = MatchSession.Create(SimFixtures.Config(turnLimit: 4), 1, resolveMap: MapCatalog.Legacy.Resolve).Run();   // board-link-mode 裁决 D7：Legacy 夹具显式经 MapCatalog.Legacy 解析
        Assert.Equal("siege-4p-base-v5", legacy.Header.MapId);
        string path = legacy.WriteTo(SimFixtures.TempDir("link-replay-legacy"));

        (int code, string output, string err) = RunMain(null, null, "replay", "--file", path);
        Assert.Equal(1, code);
        Assert.Contains("已移除的地图模式", err, StringComparison.Ordinal);
        Assert.DoesNotContain("回放一致", output, StringComparison.Ordinal);

        Assert.True(Replayer.ReplayFile(path, resolveMap: MapCatalog.Legacy.Resolve).Identical);   // board-link-mode 裁决 D7：测试内部回放旧日志显式经 MapCatalog.Legacy
    }

    [Fact]
    public void 配对事件记录来源()
    {
        // match-telemetry 第 8 项：配对事件注明来源。批量跑局为配对种子来源，回放原样带回；脚本来源记 script，真随机来源记 random。
        // 变异 MC-12：RecordedPairingSource 把来源改记 external → 回放与原日志在配对事件一行分歧，本测试红。
        MatchLog original = TwoLinkLog.Value;
        Assert.All(original.EventsOf(LogEventType.PairingApplied), e => Assert.Equal(PairingSourceNames.Seeded, e.Pairing!.Source));
        ReplayResult replay = Replayer.Replay(original);
        Assert.True(replay.Identical, replay.ToString());
        Assert.All(replay.Replayed.EventsOf(LogEventType.PairingApplied), e => Assert.Equal(PairingSourceNames.Seeded, e.Pairing!.Source));

        var request = new PairingRequest(PairingKind.FirstRound, [0, 1, 2, 3], 4);
        Assert.Equal(PairingSourceNames.Random, new RandomPairingSource().Decide(request).Source);
        Assert.Equal(ScriptedPairingSource.ScriptSource, new ScriptedPairingSource().Decide(request).Source);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(3)]
    [InlineData(2)]
    public void Sim的配对来源给出合法结果(int candidates)
    {
        // 三种 Sim 来源共用的抽取：每个候选恰出现一次，奇数时恰一名等待者；决赛只给唯一合法结果。真随机与配对种子来源各抽 50 次。
        var request = new PairingRequest(PairingKind.FirstRound, [.. Enumerable.Range(0, candidates)], 4);
        foreach (IPairingSource source in new IPairingSource[] { new RandomPairingSource(), new SeededPairingSource(9, null) })
        {
            for (int i = 0; i < 50; i++)
            {
                PairingDecision decision = source.Decide(request);
                int[] seen = [.. decision.Pairs.SelectMany(p => new[] { p.X, p.Y }), .. decision.Waiter is { } w ? new[] { w } : []];
                Assert.Equal(Enumerable.Range(0, candidates), seen.Order());
                Assert.Equal(candidates % 2 == 1, decision.Waiter is not null);
            }
        }

        var final = new PairingRequest(PairingKind.Final, [1, 3], 6);
        Assert.Equal(ScriptedPairingSource.Default(final).Canonical, new SeededPairingSource(9, 1).Decide(final).Canonical);
    }

    [Fact]
    public void 回放按记录配对而不是按种子推导()
    {
        // 规格："配对事件 MUST 从日志读取，MUST NOT 由种子推导"：同一局用另一个配对种子跑出的日志（配对不同）照样能逐行回放——回放不读配对种子。
        ulong seed = FourZoneSeed.Value;
        MatchLog a = TwoLinkLog.Value;
        string first = string.Join(";", a.EventsOf(LogEventType.PairingApplied).Select(e => string.Join(",", e.Pairing!.Pairs.Select(p => $"{p[0]}-{p[1]}"))));
        MatchLog? b = Enumerable.Range(1, 20)
            .Select(p => MatchSession.Create(Config(turnLimit: 13) with { PairingSeed = (ulong)p }, seed).Run())
            .FirstOrDefault(l => string.Join(";", l.EventsOf(LogEventType.PairingApplied).Select(e => string.Join(",", e.Pairing!.Pairs.Select(p => $"{p[0]}-{p[1]}")))) != first);
        Assert.NotNull(b);   // 样本口径：存在配对不同的另一局
        Assert.True(Replayer.Replay(b).Identical);

        // 记录用完：日志里删掉配对事件 → 回放在第 3 大回合结束时请求配对即失败，与原日志分歧（不会退回由种子推导）。
        MatchLog stripped = SimFixtures.Clone(b);
        stripped.Events.RemoveAll(e => e.Pairing is not null);
        ReplayResult replay = Replayer.Replay(stripped);
        Assert.False(replay.Identical);
        Assert.True(replay.Replayed.IsFailed);
        Assert.Contains("已没有配对事件", replay.Replayed.Failure!.Message, StringComparison.Ordinal);
    }
}
