using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Sim.Running;

/// <summary>回放比对结果：逐行比对确定性文本（去耗时）。</summary>
public sealed record ReplayResult(bool Identical, int LineCount, int? FirstDivergentLine, string? Expected, string? Actual, MatchLog Replayed)
{
    /// <summary>
    /// 地图不一致（map-generator D5）：按日志首部的地图标识重建出的地图，其内容摘要与首部记录的不同。此时分歧报在首部（第 1 行），
    /// <b>没有重跑对局</b>——<see cref="Replayed"/> 只有一行首部（原首部换上重建地图的摘要），不含任何小回合。其余情形为 <c>null</c>。
    /// </summary>
    public string? MapMismatch { get; init; }

    /// <summary>
    /// AI 评价版本不一致（ai-eye 裁决 R10、design Risks「回放兼容」）：日志首部记录的 <see cref="LogHeader.AiEvaluationVersion"/>
    /// 与当前 <see cref="EvaluationBreakdown.Version"/> 不同（缺字段 = 版本 1）。旧版本的 AI 决策不能用新评价逐步重现，所以同样停在首部（第 1 行）、
    /// <b>没有重跑对局</b>——<see cref="Replayed"/> 只有一行首部（原首部换上当前版本号）。其余情形为 <c>null</c>。
    /// </summary>
    public string? AiVersionMismatch { get; init; }

    public override string ToString() => MapMismatch is not null
        ? $"回放在首部（第 1 行）分歧：地图不一致。{MapMismatch}"
        : AiVersionMismatch is not null
        ? $"回放在首部（第 1 行）分歧：AI 评价版本不一致。{AiVersionMismatch}"
        : Identical
        ? $"回放一致：{LineCount} 行逐字节相同。"
        : $"回放在第 {FirstDivergentLine} 行分歧。\n  原：{Truncate(Expected)}\n  今：{Truncate(Actual)}";

    private static string Truncate(string? s) => s is null ? "<无>" : s.Length > 200 ? s[..200] + "…" : s;
}

/// <summary>
/// 可复现回放（simulation-harness「可复现回放」）：只凭日志首行的种子 + 配置重跑一局，与原日志逐行比对。
/// 失败局同样适用——重跑应在同一小回合抛出同类异常，且此前各行一致。
/// 专家的前瞻宽度随首部配置里该玩家的搜索配置（<c>Players[].Search.LookaheadWidth</c>，缺字段按 0）重建；前瞻记录不是回放输入，
/// 重跑时重新产生，随确定性文本逐行核对（expert-lookahead / match-telemetry「专家前瞻的记录」）。
/// </summary>
public static class Replayer
{
    /// <summary>
    /// 凭日志重跑并比对。未传 <paramref name="map"/> 时经 <see cref="MapCatalog.Resolve"/> 按标识重建地图（只认 <c>link:</c>；retire-legacy-maps 3.4：
    /// Legacy 夹具的 resolveMap 过渡口删除）。<c>replay</c> 命令在调用之前已拒绝非 <c>link:</c> 的日志（实现裁决 C3）。
    /// 只回放链接模式的日志（retire-legacy-rules L17 / R10）：首部 <see cref="LogHeader.Mode"/> 缺失或不为 <c>"Link"</c> 即 <see cref="FormatException"/>，
    /// 在重建地图之前报出并点名原因。
    /// </summary>
    public static ReplayResult Replay(MatchLog original, MapData? map = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        LogHeader header = original.Header;
        if (!original.IsLinkMode)
        {
            string actual = header.Mode is null ? "首部缺模式字段（Mode）" : $"首部模式字段（Mode）为 {header.Mode}";
            throw new FormatException(
                $"日志 {header.MapId}（种子 {header.Seed}）{actual}，不是链接模式的日志：旧模式规则已随 retire-legacy-rules 删除，回放只接受 Mode 为 Link 的日志。");
        }

        // 重建地图：每局换图的批次里，首部配置的 MapId 只是批次的起始标识，本局的图是首部的地图标识；
        // 其余情形仍按配置的 MapId（它可能是地图文件路径，而首部的地图标识是文件里写的 Id，未必解析得回去）。
        string mapId = header.Config.MapPerMatch ? header.MapId : header.Config.MapId;
        map ??= MapCatalog.Resolve(mapId);

        // 先比地图内容摘要：不同就停在首部，MUST NOT 带着另一张图继续逐步比对。旧日志没有摘要 → 跳过（不回填）。
        string digest = MapFile.Digest(map);
        if (header.MapDigest is { } recordedDigest && !string.Equals(recordedDigest, digest, StringComparison.Ordinal))
        {
            var stub = new MatchLog { Header = header with { MapDigest = digest } };
            string[] expected = original.DeterministicText().Split('\n');
            return new ReplayResult(false, expected.Length, 1, expected[0], stub.DeterministicText().Split('\n')[0], stub)
            {
                MapMismatch = $"日志首部记录的地图 {header.MapId} 摘要为 {recordedDigest}，现在按标识重建出的地图摘要为 {digest}"
                    + "——生成器或地图数据在这局之后改过，同一标识已不是同一张图；未重跑对局。",
            };
        }

        // AI 评价版本（ai-eye R10）：首部版本与当前不同 → 按首部版本处理：旧版本的 AI 决策无法用今天的评价重现，停在首部、不重跑。
        // 缺字段（ai-eye 之前的旧日志）即版本 1，MUST NOT 当作当前版本。
        if (header.AiEvaluationVersion != EvaluationBreakdown.Version)
        {
            var stub = new MatchLog { Header = header with { AiEvaluationVersion = EvaluationBreakdown.Version } };
            string[] expected = original.DeterministicText().Split('\n');
            string recorded = header.AiEvaluationVersion is { } v ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "缺（= 版本 1，ai-eye 之前）";
            return new ReplayResult(false, expected.Length, 1, expected[0], stub.DeterministicText().Split('\n')[0], stub)
            {
                AiVersionMismatch = $"日志首部记录的 AI 评价版本为 {recorded}，当前为 {EvaluationBreakdown.Version}"
                    + "——旧版本的 AI 决策不能用当前评价逐步重现；未重跑对局。",
            };
        }

        // 原样使用首行配置：失败局自动提升为完整事件流、抽样由 sim-sample 子流决定，二者都只由种子 + 配置决定。
        // 带入按首部记录的各玩家带入重建（simulation-harness「可复现回放」），MUST NOT 按配置的带入数量重抽；首部缺该项的旧日志按无带入回放。
        RecordedCarry? carry = header.CarryInOut is { } carryInOut ? new RecordedCarry(carryInOut, original.CarryIns) : null;
        // 链接模式（board-link-mode D3）：配对事件 MUST 从日志读取、MUST NOT 由种子推导——按记录逐次重放（来源名原样带回），链接随之按记录重建，
        // 每次链接后的地图摘要随确定性文本逐行比对。非链接日志已在入口拒绝（见上）。
        IPairingSource pairing = new RecordedPairingSource(original.RecordedPairings());
        MatchLog replayed = MatchSession.Create(original.Header.Config, original.Seed, map, recorded: true, carry, pairing).Run();
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
