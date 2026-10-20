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
public sealed record DefeatReplayResult(string Seed, IReadOnlyList<DefeatTarget> Targets, int MissingPass, IReadOnlyList<DefeatReviewEntry> Entries, string? Skipped)
{
    /// <summary>回放中出现的决胜一手（ai-pass-review 2.4：回放到日志最后一个小回合，逐次读 AI 的 <see cref="HeuristicTurnController.LastDecisive"/>）。未能复盘时为空。</summary>
    public IReadOnlyList<DecisiveEntry> Decisive { get; init; } = [];
}

/// <summary>一次决胜一手：所在小回合、大回合、玩家与 AI 留痕（<see cref="DecisiveMove"/>）。</summary>
public sealed record DecisiveEntry(string Seed, int Turn, int MajorRound, int Player, DecisiveMove Move);

/// <summary>
/// 战场落败复盘的回放驱动（ai-pass-review 1.2，design D2"分析时回放"；2.4 起回放到日志最后一个小回合并收集决胜一手）：按日志首部原样重建对局（与 <see cref="Replayer"/> 同一组输入：首部配置、种子、
/// 首部记录的带入），逐小回合推进；到目标小回合的部署阶段、AI 决策之前，在<b>影子批次</b>（同盘面、同批次上下文）上调用复盘。
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

    /// <summary>
    /// 回放一局（到日志最后一个小回合），复盘其全部目标并收集决胜一手。日志没有小回合时不回放。
    /// </summary>
    public static DefeatReplayResult Review(MatchLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        LogHeader header = log.Header;
        (IReadOnlyList<DefeatTarget> targets, int missing) = TargetsOf(log);
        if (log.Turns.Count == 0)
        {
            return new DefeatReplayResult(header.Seed, targets, missing, [], null);
        }

        DefeatReplayResult Skip(string reason) => new(header.Seed, targets, missing, [], reason);
        Dictionary<int, DefeatTarget> byTurn = targets.ToDictionary(t => t.Turn);
        var entries = new List<DefeatReviewEntry>();
        var decisive = new List<DecisiveEntry>();

        // 决胜一手的留痕在 AI 决策之后才有：每次部署前收上一次部署者的 LastDecisive，回放结束再收最后一次。
        (HeuristicTurnController Ai, int Turn, int Round)? previous = null;
        void Harvest()
        {
            if (previous is { } p && p.Ai.LastDecisive is { } move)
            {
                decisive.Add(new DecisiveEntry(header.Seed, p.Turn, p.Round, p.Ai.Player.Value, move));
            }

            previous = null;
        }

        // 回放驱动（前置核对、逐小回合核对、异常即未能复盘）与改造诊断共用 LogReplay（ai-terrain-review 1.2 原样抽出）。
        string? failure = LogReplay.Run(log, (session, batch) =>
        {
            Harvest();
            if (session.AiOf(batch.Context.Player) is { } deciding)
            {
                previous = (deciding, session.TurnCount + 1, session.Match.MajorRound);
            }

            if (!byTurn.TryGetValue(session.TurnCount + 1, out DefeatTarget? target))
            {
                return null;
            }

            var player = new PlayerId(target.Player);
            if (batch.Context.Player != player)
            {
                return $"第 {target.Turn} 小回合的行动玩家是 {batch.Context.Player}，日志记的是 {player}";
            }

            if (session.AiOf(player) is not { } ai)
            {
                return $"玩家 {player} 的控制者不是启发式 AI";
            }

            // 影子批次：同一盘面与批次上下文，预演走同一个零副作用入口（与 MatchFlow.Rehearse 同一实现、同一份已提交盘面集合），
            // 正式暂放批次与它的被拒留痕一概不碰。
            MatchFlow match = session.Match;
            var shadow = new StagedBatch(batch.Board, batch.Context);
            BestMoveReview review = ai.ReviewBestMove(shadow, () => BatchRehearsal.Rehearse(shadow.Board, shadow.Context, shadow.Placements, match.History));
            entries.Add(new DefeatReviewEntry(header.Seed, target, review));
            return null;
        });

        Harvest();
        if (failure is null && entries.Count != targets.Count)
        {
            failure = $"只到达 {entries.Count}/{targets.Count} 个复盘目标";
        }

        return failure is null ? new DefeatReplayResult(header.Seed, targets, missing, entries, null) { Decisive = decisive } : Skip(failure);
    }
}
