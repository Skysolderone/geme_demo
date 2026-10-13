using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Sim.Running;

/// <summary>一名"整轮 Pass"结算落败者的复盘目标：结算那一大回合里本人最后一次 Pass 的小回合。</summary>
/// <param name="Turn">该 Pass 的小回合序号（日志 <see cref="TurnSnapshot.Turn"/>，从 1 起）。</param>
public sealed record DefeatTarget(int MajorRound, int Battlefield, int Player, int Turn);

/// <summary>一名落败者的复盘结果：目标 + 在该时刻按其 AI 配置复盘的最好一手（<see cref="HeuristicTurnController.ReviewBestMove"/>）。</summary>
public sealed record DefeatReviewEntry(string Seed, DefeatTarget Target, BestMoveReview Review);

/// <summary>
/// 一局的复盘结果。<paramref name="Targets"/> 是按日志找到的全部目标（含找不到 Pass 小回合的落败者以外的全部）；
/// <paramref name="MissingPass"/> 是落败者在结算那一大回合找不到本人 Pass 快照的人次；<paramref name="Skipped"/> 非空即整局未能复盘的原因（地图 / AI 版本不一致、回放分歧、控制者不是启发式 AI）。
/// </summary>
public sealed record DefeatReplayResult(string Seed, IReadOnlyList<DefeatTarget> Targets, int MissingPass, IReadOnlyList<DefeatReviewEntry> Entries, string? Skipped);

/// <summary>
/// 战场落败复盘的回放驱动（ai-pass-review 1.2，design D2"分析时回放"）：按日志首部原样重建对局（与 <see cref="Replayer"/> 同一组输入：首部配置、种子、
/// 首部记录的带入、日志里的配对事件），逐小回合推进；到目标小回合的部署阶段、AI 决策之前，在<b>影子批次</b>（同盘面、同批次上下文）上调用复盘。
/// 日志不新增任何字段。复盘不碰正式暂放批次、不消费任何随机流，回放本身与原局逐小回合相同——每推进一个小回合即与原日志的同序号快照核对，
/// 一旦分歧（如脚本控制者跑出的测试日志）整局记为未能复盘，不给出半截结论。
/// </summary>
/// <remarks>本文件不出现浮点（跑局层惯例）。</remarks>
public static class DefeatReplay
{
    /// <summary>
    /// 日志里的复盘目标：每个因"整轮 Pass"结算的战场里，每名落败者在结算那一大回合本人最后一次 Pass 的小回合。按小回合序号升序。
    /// 找不到本人 Pass 快照的落败者计入返回值第二项（不应发生：整轮 Pass 即全员 Pass）。
    /// </summary>
    public static (IReadOnlyList<DefeatTarget> Targets, int MissingPass) TargetsOf(MatchLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var targets = new List<DefeatTarget>();
        int missing = 0;
        foreach (LogEvent e in log.Events.Where(e => e.Settlement is { Reason: nameof(SettlementReason.AllPassed) }).OrderBy(e => e.Seq))
        {
            SettlementLogEntry s = e.Settlement!;
            foreach (SettlementRankEntry loser in s.Entries.Where(x => x.Player != s.Winner))
            {
                TurnSnapshot? pass = log.Turns.LastOrDefault(t => t.MajorRound == s.MajorRound && t.Player == loser.Player && t.Passed);
                if (pass is null)
                {
                    missing++;
                    continue;
                }

                targets.Add(new DefeatTarget(s.MajorRound, s.Battlefield, loser.Player, pass.Turn));
            }
        }

        return ([.. targets.OrderBy(t => t.Turn)], missing);
    }

    /// <summary>回放一局并复盘其全部目标。没有目标时不回放。</summary>
    public static DefeatReplayResult Review(MatchLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        LogHeader header = log.Header;
        (IReadOnlyList<DefeatTarget> targets, int missing) = TargetsOf(log);
        if (targets.Count == 0)
        {
            return new DefeatReplayResult(header.Seed, targets, missing, [], null);
        }

        DefeatReplayResult Skip(string reason) => new(header.Seed, targets, missing, [], reason);

        // 与 Replayer 同一组前置核对：地图内容摘要、AI 评价版本——任一不同，今天的实现重现不了那一局的决策，不复盘。
        string mapId = header.Config.MapPerMatch ? header.MapId : header.Config.MapId;
        MapData map = MapCatalog.Resolve(mapId);
        if (header.MapDigest is { } recorded && !string.Equals(recorded, MapFile.Digest(map), StringComparison.Ordinal))
        {
            return Skip("地图摘要不一致");
        }

        if (header.AiEvaluationVersion != EvaluationBreakdown.Version)
        {
            return Skip("AI 评价版本不一致");
        }

        RecordedCarry? carry = header.CarryInOut is { } carryInOut ? new RecordedCarry(carryInOut, log.CarryIns) : null;
        MatchSession session = MatchSession.Create(header.Config, log.Seed, map, recorded: true, carry, new RecordedPairingSource(log.RecordedPairings()));
        Dictionary<int, DefeatTarget> byTurn = targets.ToDictionary(t => t.Turn);
        Dictionary<int, TurnSnapshot> original = log.Turns.GroupBy(t => t.Turn).ToDictionary(g => g.Key, g => g.Last());
        var entries = new List<DefeatReviewEntry>();
        string? failure = null;

        session.ObserveDeploy(batch =>
        {
            if (failure is not null || !byTurn.TryGetValue(session.TurnCount + 1, out DefeatTarget? target))
            {
                return;
            }

            var player = new PlayerId(target.Player);
            if (batch.Context.Player != player)
            {
                failure = $"第 {target.Turn} 小回合的行动玩家是 {batch.Context.Player}，日志记的是 {player}";
                return;
            }

            if (session.AiOf(player) is not { } ai)
            {
                failure = $"玩家 {player} 的控制者不是启发式 AI";
                return;
            }

            // 影子批次：同一盘面与批次上下文，预演走同一个零副作用入口（与 MatchFlow.Rehearse 同一实现、同一份已提交盘面集合），
            // 正式暂放批次与它的被拒留痕一概不碰。
            MatchFlow match = session.Match;
            var shadow = new StagedBatch(batch.Board, batch.Context);
            BestMoveReview review = ai.ReviewBestMove(shadow, () => BatchRehearsal.Rehearse(shadow.Board, shadow.Context, shadow.Placements, match.History));
            entries.Add(new DefeatReviewEntry(header.Seed, target, review));
        });

        int last = targets[^1].Turn;
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

        if (failure is null && entries.Count != targets.Count)
        {
            failure = $"只到达 {entries.Count}/{targets.Count} 个复盘目标";
        }

        return failure is null ? new DefeatReplayResult(header.Seed, targets, missing, entries, null) : Skip(failure);
    }

    /// <summary>回放与原日志的一个小回合是否相同：行动玩家、大回合、是否 Pass、落子与提子逐项相同（耗时等非确定字段不比）。</summary>
    private static bool SameMove(TurnSnapshot a, TurnSnapshot b) =>
        a.Turn == b.Turn && a.MajorRound == b.MajorRound && a.Player == b.Player && a.Passed == b.Passed
        && a.Placements.SequenceEqual(b.Placements, StringComparer.Ordinal) && a.Captures.SequenceEqual(b.Captures, StringComparer.Ordinal);
}
