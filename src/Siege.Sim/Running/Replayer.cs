using Siege.Core.Board;
using Siege.Sim.Logging;

namespace Siege.Sim.Running;

/// <summary>回放比对结果：逐行比对确定性文本（去耗时）。</summary>
public sealed record ReplayResult(bool Identical, int LineCount, int? FirstDivergentLine, string? Expected, string? Actual, MatchLog Replayed)
{
    public override string ToString() => Identical
        ? $"回放一致：{LineCount} 行逐字节相同。"
        : $"回放在第 {FirstDivergentLine} 行分歧。\n  原：{Truncate(Expected)}\n  今：{Truncate(Actual)}";

    private static string Truncate(string? s) => s is null ? "<无>" : s.Length > 200 ? s[..200] + "…" : s;
}

/// <summary>
/// 可复现回放（simulation-harness「可复现回放」）：只凭日志首行的种子 + 配置重跑一局，与原日志逐行比对。
/// 失败局同样适用——重跑应在同一小回合抛出同类异常，且此前各行一致。
/// </summary>
public static class Replayer
{
    /// <summary>凭日志重跑并比对。</summary>
    public static ReplayResult Replay(MatchLog original, MapData? map = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        // 原样使用首行配置：失败局自动提升为完整事件流、抽样由 sim-sample 子流决定，二者都只由种子 + 配置决定。
        MatchLog replayed = MatchSession.Create(original.Header.Config, original.Seed, map).Run();
        return Compare(original, replayed);
    }

    /// <summary>逐行比对两份日志的确定性文本。</summary>
    public static ReplayResult Compare(MatchLog expected, MatchLog actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        string[] a = expected.DeterministicText().Split('\n');
        string[] b = actual.DeterministicText().Split('\n');
        int n = Math.Max(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            string? la = i < a.Length ? a[i] : null;
            string? lb = i < b.Length ? b[i] : null;
            if (la != lb)
            {
                return new ReplayResult(false, n, i + 1, la, lb, actual);
            }
        }

        return new ReplayResult(true, n, null, null, null, actual);
    }

    /// <summary>读文件回放。</summary>
    public static ReplayResult ReplayFile(string path, MapData? map = null) => Replay(MatchLog.Read(path), map);
}
