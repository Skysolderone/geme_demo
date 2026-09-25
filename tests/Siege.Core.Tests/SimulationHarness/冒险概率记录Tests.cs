using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// flag-contest D2：冒险概率作为批量配置字段的记录、回放与命令行。
/// 规格：match-setup —— Requirement: 原型插旗替代路径（"p 是对局配置，SHALL 写入对局日志首部；p = 0 时锁定结果与此前逐项相同"）。
/// </summary>
public class 冒险概率记录Tests
{
    [Fact]
    public void 跑局配置的冒险概率文本往返()
    {
        // 未配置不写出（旧配置文件逐字节不变）；0 与非缺省值 37 都写出并读回（0 是回放回填值、15 是新建缺省值，两者都不能证明写入路径）。
        RunConfig unset = SimFixtures.Config();
        Assert.DoesNotContain("FlagRisk", unset.ToJson(), StringComparison.Ordinal);
        Assert.Null(RunConfig.FromJson(unset.ToJson()).FlagRisk);

        foreach (int p in new[] { 0, 37, 100 })
        {
            RunConfig set = unset with { FlagRisk = p };
            Assert.Contains($"\"FlagRisk\": {p}", set.ToJson(), StringComparison.Ordinal);
            Assert.Equal(p, RunConfig.FromJson(set.ToJson()).FlagRisk);
        }

        // 取值范围 0–100：越界在读入 / 开跑前响亮失败。
        Assert.Throws<ArgumentException>(() => (unset with { FlagRisk = -1 }).Validated());
        Assert.Throws<ArgumentException>(() => (unset with { FlagRisk = 101 }).Validated());
        Assert.Throws<ArgumentException>(() => RunConfig.FromJson((unset with { FlagRisk = 101 }).ToJson()));

        // 新建的局把缺省值落成具体数值（ResolvedFor），不落成就无法与"首部缺该项 = 旧日志 = 0"区分。
        Assert.Equal(MatchOptions.DefaultFlagRisk, unset.ResolvedFor(FourPlayerBaseMap.Create()).FlagRisk);
        Assert.Equal(37, (unset with { FlagRisk = 37 }).ResolvedFor(FourPlayerBaseMap.Create()).FlagRisk);
    }

    [Fact]
    public void 首部缺冒险概率的旧日志按0回放()
    {
        // 该项出现之前的日志首部没有 FlagRisk：当时的原型选区没有冒险（= p 0）。回放 MUST 按 0 重建，重建的首部也不得多出一项。
        // 样本：p = 0 跑出的局去掉这一项。样本种子取"缺省 p 下锁定结果与 p = 0 不同"的第一颗（样本口径，不是凑期望值）——
        // 否则"按缺省 p 重建"与"按 0 重建"得到同一局，回放守门是空证。
        MapData map = FourPlayerBaseMap.Create();
        PlayerId[] ids = [.. Enumerable.Range(0, 4).Select(i => new PlayerId(i))];
        ulong seed = Enumerable.Range(1, 200).Select(i => (ulong)i).First(s =>
            !PrototypeZoneAssignment.Assign(map, new GameSeed(s), ids, MatchOptions.DefaultFlagRisk)
                .SequenceEqual(PrototypeZoneAssignment.Assign(map, new GameSeed(s), ids, 0)));

        RunConfig config = SimFixtures.PinPreCalibration(SimFixtures.Config(seedStart: seed, turnLimit: 8)) with { FlagRisk = null };
        MatchLog zero = BatchRunner.Execute(config with { FlagRisk = 0 }, parallelism: 1)[0];
        string text = zero.DeterministicText();
        Assert.Contains("\"FlagRisk\":0,", text, StringComparison.Ordinal);
        MatchLog old = MatchLog.Parse(text.Replace("\"FlagRisk\":0,", string.Empty, StringComparison.Ordinal));
        Assert.Null(old.Header.Config.FlagRisk);

        ReplayResult replay = Replayer.Replay(old);

        Assert.True(replay.Identical, replay.ToString());
        Assert.True(replay.LineCount >= 9, $"比对行数 {replay.LineCount}");
        Assert.Null(replay.Replayed.Header.Config.FlagRisk);

        // 会话层：新建的局取缺省值并写进首部配置；按首部重建的局缺字段取 0，有字段取记录值。
        Assert.Equal(MatchOptions.DefaultFlagRisk, MatchSession.Create(config, seed).Match.Options.FlagRisk);
        Assert.Equal(MatchOptions.DefaultFlagRisk, MatchSession.Create(config, seed).Config.FlagRisk);
        Assert.Equal(0, MatchSession.Create(config, seed, map: null, recorded: true).Match.Options.FlagRisk);
        Assert.Equal(37, MatchSession.Create(config with { FlagRisk = 37 }, seed, map: null, recorded: true).Match.Options.FlagRisk);

        // 样本口径：这颗种子在缺省 p 下的真实建局确实与 p = 0 锁定结果不同。
        Assert.NotEqual(
            MatchSession.Create(config with { FlagRisk = 0 }, seed).Match.PlayerStates.Select(s => s.BirthZone),
            MatchSession.Create(config, seed).Match.PlayerStates.Select(s => s.BirthZone));
    }

    [Fact]
    public void 命令行冒险概率写入配置记录()
    {
        // 严格 CLI：--flag-risk 被认领并写进 config.json 与日志首部；越界 / 非整数在跑局之前报错、不写任何输出。
        string outDir = Path.Combine(SimFixtures.TempDir("flag-risk-cli"), "out");
        int code = Siege.Sim.Program.Main(
            ["run", "--out", outDir, "--seed", "1", "--count", "1", "--turn-limit", "4", "--difficulty", "Easy", "--serial", "--flag-risk", "37"]);

        Assert.Equal(0, code);
        Assert.Contains("\"FlagRisk\": 37", File.ReadAllText(Path.Combine(outDir, "config.json")), StringComparison.Ordinal);
        Assert.Contains("\"FlagRisk\":37,", File.ReadLines(Directory.GetFiles(outDir, "match-*.jsonl").Single()).First(), StringComparison.Ordinal);

        foreach (string bad in new[] { "-1", "101", "abc" })
        {
            string badDir = Path.Combine(SimFixtures.TempDir($"flag-risk-cli-bad-{bad}"), "out");
            TextWriter saved = Console.Error;
            using var err = new StringWriter();
            int result;
            try
            {
                Console.SetError(err);
                result = Siege.Sim.Program.Main(["run", "--out", badDir, "--seed", "1", "--count", "1", "--serial", "--flag-risk", bad]);
            }
            finally
            {
                Console.SetError(saved);
            }

            Assert.NotEqual(0, result);
            Assert.False(Directory.Exists(badDir), $"--flag-risk {bad} 仍写出了输出目录。");
            Assert.StartsWith("错误：", err.ToString(), StringComparison.Ordinal);
            if (bad != "abc")
            {
                Assert.Contains("冒险概率", err.ToString(), StringComparison.Ordinal);   // 越界报出的是本项，不是别的校验碰巧失败
            }
        }
    }
}
