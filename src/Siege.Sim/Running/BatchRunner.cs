using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Config;
using Siege.Sim.Logging;

namespace Siege.Sim.Running;

/// <summary>批量跑局的汇总（随日志与配置一同保存为 <c>summary.json</c>）。全整数。</summary>
public sealed record BatchSummary
{
    public int Count { get; init; }

    public int Completed { get; init; }

    public int Failed { get; init; }

    /// <summary>以「达大回合上限」（规则级 <c>MajorRoundLimit</c>）终局的局数（round-cap D5 不收敛口径）。</summary>
    public int Capped { get; init; }

    public int Excluded { get; init; }

    public SortedDictionary<string, int> Reasons { get; init; } = [];

    public SortedDictionary<int, int> WinsByPlayer { get; init; } = [];

    public long TotalTurns { get; init; }

    public long TotalMajorRounds { get; init; }

    public long TotalMatchMs { get; init; }

    public long WallClockMs { get; init; }

    public int Parallelism { get; init; }

    public List<string> FailedFiles { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, RunConfig.JsonOptions);
}

/// <summary>
/// 按种子批次执行大量对局（simulation-harness「批量跑局」）。每局一个独立 <see cref="MatchSession"/>，局间零共享；
/// 并行只是调度方式，MUST NOT 改变任何单局结果——共享的只有进度计数与结果收集，二者都与结果无关。
/// </summary>
public static class BatchRunner
{
    /// <summary>执行全部对局，按种子顺序返回日志。<paramref name="parallelism"/> ≤ 1 即串行。</summary>
    public static List<MatchLog> Execute(RunConfig config, int parallelism, MapData? map = null, Action<MatchLog>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validated();
        map ??= MapCatalog.Resolve(config.MapId);
        var results = new ConcurrentBag<MatchLog>();
        int[] indices = [.. Enumerable.Range(0, config.Count)];

        void RunOne(int index)
        {
            MatchLog log = MatchSession.Create(config, config.SeedAt(index), map).Run();
            results.Add(log);
            onCompleted?.Invoke(log);
        }

        if (parallelism <= 1)
        {
            foreach (int i in indices)
            {
                RunOne(i);
            }
        }
        else
        {
            Parallel.ForEach(indices, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, RunOne);
        }

        return [.. results.OrderBy(l => l.Seed)];
    }

    /// <summary>执行并把配置、每局日志与汇总写进 <paramref name="outputDir"/>。</summary>
    public static BatchSummary ExecuteToDirectory(RunConfig config, string outputDir, int parallelism, TextWriter? progress = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        // 先解析地图再建输出目录：未知地图标识要在写出任何东西之前报错（strict-cli D4：半份输出比没有输出更糟）。
        MapData map = MapCatalog.Resolve(config.MapId);
        // 候选格上限先按地图落成具体值，config.json 记录的就是实际生效的 K（小图上原样不变）。
        config = config.ResolvedFor(map);
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "config.json"), config.Effective().ToJson());
        var wall = Stopwatch.StartNew();
        int done = 0;
        object gate = new();
        List<MatchLog> logs = Execute(config, parallelism, map, onCompleted: log =>
        {
            log.WriteTo(outputDir, config.Compress);
            int n = Interlocked.Increment(ref done);
            if (progress is not null && (n % 10 == 0 || n == config.Count || log.IsFailed))
            {
                lock (gate)
                {
                    progress.WriteLine($"[{n}/{config.Count}] 种子 {log.Header.Seed} {(log.IsFailed ? "失败" : log.Result!.Reason)} R{log.Result?.MajorRound ?? log.Failure!.MajorRound} {log.Turns.Count} 小回合 {log.Result?.TotalMs ?? log.Failure!.ElapsedMs} ms");
                }
            }
        });
        wall.Stop();
        BatchSummary summary = Summarize(logs, parallelism, wall.ElapsedMilliseconds);
        File.WriteAllText(Path.Combine(outputDir, "summary.json"), summary.ToJson());
        return summary;
    }

    /// <summary>汇总一批日志。</summary>
    public static BatchSummary Summarize(IReadOnlyList<MatchLog> logs, int parallelism, long wallClockMs)
    {
        ArgumentNullException.ThrowIfNull(logs);
        var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var wins = new SortedDictionary<int, int>();
        long turns = 0;
        long rounds = 0;
        long ms = 0;
        int failed = 0;
        int capped = 0;
        int excluded = 0;
        var failedFiles = new List<string>();
        foreach (MatchLog log in logs)
        {
            if (log.Failure is { } f)
            {
                failed++;
                failedFiles.Add(log.FileName);
                ms += f.ElapsedMs ?? 0;
                continue;
            }

            LogResult r = log.Result!;
            reasons[r.Reason] = reasons.TryGetValue(r.Reason, out int n) ? n + 1 : 1;
            if (!r.Converged)
            {
                capped++;
            }

            if (log.IsContaminated)
            {
                excluded++;
            }

            foreach (int w in r.Winners)
            {
                wins[w] = wins.TryGetValue(w, out int m) ? m + 1 : 1;
            }

            turns += r.TurnCount;
            rounds += r.MajorRound;
            ms += r.TotalMs ?? 0;
        }

        return new BatchSummary
        {
            Count = logs.Count,
            Completed = logs.Count - failed,
            Failed = failed,
            Capped = capped,
            Excluded = excluded,
            Reasons = reasons,
            WinsByPlayer = wins,
            TotalTurns = turns,
            TotalMajorRounds = rounds,
            TotalMatchMs = ms,
            WallClockMs = wallClockMs,
            Parallelism = parallelism,
            FailedFiles = failedFiles,
        };
    }
}
