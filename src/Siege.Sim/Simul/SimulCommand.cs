using System.Diagnostics;
using System.Text;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Simul;
using Siege.Sim.Cli;

namespace Siege.Sim.Simul;

/// <summary>
/// 新玩法（同时决策，simul-a J5）的全 AI 跑局入口：<c>Siege.Sim simul</c>。每名玩家都用 <see cref="SimulGreedyAi"/>，
/// 日志逐回合写结算顺位、各家部署、冲突（战力构成与骰点）、围杀分步、本回合势力，最后写终局原因与名次。
/// </summary>
internal static class SimulCommand
{
    internal const string Usage =
        "  Siege.Sim simul [--seed <对局种子，十进制或 0x 十六进制，缺省 1>] [--players <人数 2–4>] [--map <link:地图种子:人数，只开小图，缺省 link:1:<人数>>]"
        + " [--rounds <最多跑几个回合，0 = 跑到终局，缺省 0；也接受 --rounds=N>] [--out <日志文件，缺省只写控制台>]";

    /// <summary>入口。<paramref name="args"/> 是子命令之后的原始参数（<c>--key=value</c> 先拆成 <c>--key value</c>，只在本子命令里这样做）。</summary>
    internal static int Run(IEnumerable<string> args, Func<ulong> mapSeedSource, TextWriter console)
    {
        var cli = new CommandLine(SplitEquals(args));
        ulong seed = cli.GetUInt64("seed", 1);
        int? players = cli.Has("players") ? cli.GetInt("players", 0) : null;
        string? mapId = cli.GetOrNull("map");
        int rounds = cli.GetInt("rounds", 0);
        string? outPath = cli.GetOrNull("out");
        cli.EnsureRecognized();
        if (rounds < 0)
        {
            throw new ArgumentException($"--rounds 须为非负整数（0 = 跑到终局），实际为 {rounds}。");
        }

        (string resolvedId, int count) = Program.ResolveLinkRequest(mapId, players, "simul", console, mapSeedSource);
        MapData map = MapCatalog.Resolve(resolvedId);
        var log = new StringBuilder();
        Play(map, new GameSeed(seed), count, rounds, line =>
        {
            console.WriteLine(line);
            log.AppendLine(line);
        });

        if (outPath is not null)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(outPath, log.ToString(), new UTF8Encoding(false));
        }

        return 0;
    }

    /// <summary>跑一局（最多 <paramref name="rounds"/> 个回合，0 = 跑到终局），逐行写日志，返回对局。</summary>
    internal static SimulFlow Play(MapData map, GameSeed seed, int players, int rounds, Action<string> write)
    {
        SimulFlow flow = SimulFlow.Create(map, seed, Enumerable.Range(0, players).Select(i => new PlayerId(i)));
        flow.PlantPrototype();
        write($"新玩法 地图 {map.Id} 种子 {seed} 人数 {players} 大回合上限 {flow.RoundCap}" + (rounds > 0 ? $" 本次最多跑 {rounds} 回合" : string.Empty));
        write("出生岛 " + string.Join(" ", flow.Players.Select(p => $"{p}:{BirthZoneLabel.Of(flow.BirthZoneOf(p)!.Value)}")));

        while (flow.Phase == SimulPhase.InProgress && (rounds == 0 || flow.Round <= rounds))
        {
            int round = flow.Round;
            write($"== 第 {round} 回合  结算顺位 {string.Join(" > ", flow.Order)}");
            foreach (PlayerId p in flow.Order)
            {
                var clock = Stopwatch.StartNew();
                SimulAiDecision decision = SimulGreedyAi.Play(flow.PublicView(), flow.PrepFor(p));
                clock.Stop();
                write($"  {p} 部署 [{string.Join(", ", decision.Placements.Select(Describe))}]"
                    + $" 征召 [{string.Join(",", decision.Recruited)}] 评价 {decision.Evaluated} 次 预期 +{decision.ExpectedGain} AI 耗时 {clock.ElapsedMilliseconds} ms");
            }

            SimulRoundReport report = flow.ResolveRound();
            foreach (SimulConflict c in report.Conflicts)
            {
                string parts = string.Join("；", c.Entrants.Select(e => $"{e.Player} {e.Type} 基础 {e.BasePower} + 支援 {e.Support} = {e.Power}"));
                write($"  冲突 {c.Describe()}（{parts}）");
            }

            foreach (SimulEditOutcome e in report.Edits)
            {
                write($"  改造 {e.Player}@{e.ArtisanCell.ToNotation()} {e.Edit}：{(e.Applied ? "生效" : $"作废（{e.VoidReason}）")}");
            }

            foreach (SimulCaptureStep step in report.Captures)
            {
                string who = step.By is { } by ? $"{by} 的顺位" : "孤立自绝";
                write($"  围杀 {who}：" + (step.Removed.IsEmpty ? "无" : $"移除 {step.Removed.Length} 个单位 [{string.Join(", ", step.Removed)}]"));
            }

            write("  势力 " + string.Join(" ", report.Power.Players
                .OrderByDescending(p => p.Total).ThenBy(p => p.Player)
                .Select(p => $"{p.Player}={p.Total}（领地 {p.ScoredTerritory.Length} + 军势 {p.GroupScore}，单位 {flow.Board.GroupsOf(p.Player).Sum(g => g.Size)}）")));
            int captured = report.Captures.Sum(s => s.Removed.Length);
            string top = string.Join(" ", report.Power.Players.OrderByDescending(p => p.Total).ThenBy(p => p.Player).Take(3).Select(p => $"{p.Player}={p.Total}"));
            write($"  摘要 回合 {round}：部署 {report.SubmittedCount} 落下 {report.Landed.Length} 冲突 {report.Conflicts.Length} 掷骰 {report.DiceRolled} 围杀移除 {captured} 势力前三 {top}");
        }

        if (flow.Result is { } result)
        {
            write($"终局：原因 {result.Reason}，第 {result.Round} 回合；名次 {string.Join(" ", result.Standings.Select(s => $"{s.Rank}.{s.Player}"))}；胜者 {string.Join("、", result.Winners)}");
        }
        else
        {
            write($"未终局：已跑满 --rounds {rounds}，停在第 {flow.Round} 回合的准备阶段。");
        }

        return flow;
    }

    private static string Describe(Placement p) => p.ToString();

    /// <summary>把 <c>--key=value</c> 拆成 <c>--key value</c>（只在本子命令里），其余原样。</summary>
    private static IEnumerable<string> SplitEquals(IEnumerable<string> args)
    {
        foreach (string arg in args)
        {
            int eq = arg.IndexOf('=', StringComparison.Ordinal);
            if (arg.StartsWith("--", StringComparison.Ordinal) && eq > 2)
            {
                yield return arg[..eq];
                yield return arg[(eq + 1)..];
            }
            else
            {
                yield return arg;
            }
        }
    }
}
