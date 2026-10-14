using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Sim.Running;

/// <summary>
/// 分析复盘的回放驱动（ai-pass-review 1.2 的 <see cref="DefeatReplay"/> 原样抽出，ai-terrain-review 1.2 的 <see cref="ArtisanReplay"/> 共用）：
/// 按日志首部原样重建对局（与 <see cref="Replayer"/> 同一组输入：首部配置、种子、首部记录的带入、日志里的配对事件），逐小回合推进到日志最后一个小回合，
/// 每次部署前、AI 决策之前调用一次探针；每推进一个小回合即与原日志的同序号快照核对，一旦分歧整局记为未能复盘，不给出半截结论。日志不新增任何字段。
/// </summary>
/// <remarks>本文件不出现浮点（跑局层惯例）。</remarks>
public static class LogReplay
{
    /// <summary>
    /// 回放 <paramref name="log"/>。<paramref name="probe"/> 在每次部署前调用（参数：回放会话、正式暂放批次）——探针 MUST NOT 暂放到正式批次上（用同上下文的影子批次），
    /// 返回非 <c>null</c> 即以该文本为原因中止回放，此后不再调用探针。返回未能复盘的原因；回放完整且与原日志逐小回合一致时为 <c>null</c>。
    /// 调用方应先确认日志有小回合。
    /// </summary>
    public static string? Run(MatchLog log, Func<MatchSession, StagedBatch, string?> probe)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(probe);
        LogHeader header = log.Header;

        // 与 Replayer 同一组前置核对：地图内容摘要、AI 评价版本——任一不同，今天的实现重现不了那一局的决策，不复盘。
        string mapId = header.Config.MapPerMatch ? header.MapId : header.Config.MapId;
        MapData map = MapCatalog.Resolve(mapId);
        if (header.MapDigest is { } recorded && !string.Equals(recorded, MapFile.Digest(map), StringComparison.Ordinal))
        {
            return "地图摘要不一致";
        }

        if (header.AiEvaluationVersion != EvaluationBreakdown.Version)
        {
            return "AI 评价版本不一致";
        }

        RecordedCarry? carry = header.CarryInOut is { } carryInOut ? new RecordedCarry(carryInOut, log.CarryIns) : null;
        MatchSession session = MatchSession.Create(header.Config, log.Seed, map, recorded: true, carry, new RecordedPairingSource(log.RecordedPairings()));
        Dictionary<int, TurnSnapshot> original = log.Turns.GroupBy(t => t.Turn).ToDictionary(g => g.Key, g => g.Last());
        string? failure = null;
        session.ObserveDeploy(batch =>
        {
            if (failure is null)
            {
                failure = probe(session, batch);
            }
        });

        int last = log.Turns[^1].Turn;
        try
        {
            while (failure is null && session.TurnCount < last)
            {
                int before = session.TurnCount;
                bool more = session.RunTurn();
                if (session.TurnCount > before)
                {
                    TurnSnapshot replayed = session.RecordedTurns[^1];
                    if (!original.TryGetValue(replayed.Turn, out TurnSnapshot? expected) || !SameMove(expected, replayed))
                    {
                        failure = $"回放在第 {replayed.Turn} 小回合分歧";
                    }
                }

                if (!more)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is SiegeRuleException or SimAssertionException or InvalidOperationException)
        {
            failure = $"回放抛出 {ex.GetType().Name}";
        }

        return failure;
    }

    /// <summary>回放与原日志的一个小回合是否相同：行动玩家、大回合、是否 Pass、落子与提子逐项相同（耗时等非确定字段不比）。</summary>
    private static bool SameMove(TurnSnapshot a, TurnSnapshot b) =>
        a.Turn == b.Turn && a.MajorRound == b.MajorRound && a.Player == b.Player && a.Passed == b.Passed
        && a.Placements.SequenceEqual(b.Placements, StringComparer.Ordinal) && a.Captures.SequenceEqual(b.Captures, StringComparer.Ordinal);
}
