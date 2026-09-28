// engagement-diagnosis 诊断跑局器（只诊断，不改 src）。
// 用法：dotnet <Diag.dll> --config <cfg.json> --out <dir> [--parallel N]
// 行为：按 RunConfig 与 BatchRunner 同口径逐局跑（MatchSession.Create → Run），官方日志照常写到 <dir>；
//       每名 AI 外包一层 DiagController：在内层 Deploy 之前，用同一公开接口只读复算"单点枚举 + k=0 贪心"，
//       记下合法范围、禁入格、活形硬约束淘汰数、停手阈值撤回数等，写到 <dir>/diag-<seed>.jsonl。
// 复算只调 MatchFlow.Rehearse（零副作用、不经日志装饰器计数），不消费任何随机流；
// 内层 AI 的排序 LastPointRanking 与复算前 N 逐项比对，结果记在 rank_match 字段（忠实性检查）。
// 已知副作用：SetController 换装饰器后 LoggingController.Heuristic 为 null，官方日志的 Settled 事件缺九维分值、缺 Candidates 事件；其余字段不变。
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? cfgPath = null, outDir = null, verifyDir = null;
        int parallel = Environment.ProcessorCount, verifyMax = int.MaxValue;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config": cfgPath = args[++i]; break;
                case "--out": outDir = args[++i]; break;
                case "--parallel": parallel = int.Parse(args[++i]); break;
                case "--verify": verifyDir = args[++i]; break;
                case "--verify-max": verifyMax = int.Parse(args[++i]); break;
                default: Console.Error.WriteLine($"未知参数 {args[i]}"); return 2;
            }
        }

        if (verifyDir is not null)
        {
            // 忠实性检查：用官方 Replayer（标准控制者，不带诊断装饰器）按日志首部重跑，逐小回合比对落子 / 提子 / Pass 与终局。
            // Settled 事件的九维分值因装饰器缺失而不同，不比对事件行。
            string[] files = [.. Directory.GetFiles(verifyDir, "match-*.jsonl").Order().Take(verifyMax)];
            int ok = 0;
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = parallel }, f =>
            {
                MatchLog original = MatchLog.Read(f);
                MatchLog replayed = Replayer.Replay(original).Replayed;
                static string Sig(MatchLog l) => string.Join(";", l.Turns.Select(t => $"{t.Player}|{t.Passed}|{string.Join(",", t.Placements)}|{string.Join(",", t.Captures)}"))
                    + $"#{l.Result?.Reason}#{l.Result?.MajorRound}";
                bool same = Sig(original) == Sig(replayed);
                if (same) Interlocked.Increment(ref ok);
                Console.WriteLine($"{Path.GetFileName(f)} {(same ? "IDENTICAL" : "DIFFERENT")}");
            });
            Console.WriteLine($"verify {ok}/{files.Length} identical");
            return ok == files.Length ? 0 : 4;
        }

        if (cfgPath is null || outDir is null)
        {
            Console.Error.WriteLine("需要 --config 与 --out");
            return 2;
        }

        RunConfig config = RunConfig.FromJson(File.ReadAllText(cfgPath));
        if (config.CarryIn is not (null or 0))
        {
            Console.Error.WriteLine("诊断跑局只允许关闭带入带出（CarryIn 0）。");
            return 2;
        }

        MapData map = MapCatalog.Resolve(config.MapId);
        config = config.ResolvedFor(map);
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "config.json"), config.Effective().ToJson());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var summary = new ConcurrentBag<string>();
        Parallel.ForEach(Enumerable.Range(0, config.Count), new ParallelOptions { MaxDegreeOfParallelism = parallel }, index =>
        {
            ulong seed = config.SeedAt(index);
            MatchSession session = MatchSession.Create(config, seed, map);
            var lines = new List<string>();
            foreach (PlayerId p in session.Match.Players)
            {
                HeuristicTurnController ai = session.AiOf(p) ?? throw new InvalidOperationException("诊断只支持启发式 AI。");
                session.SetController(p, new DiagController(ai, session.Match, seed, lines));
            }

            MatchLog log = session.Run();
            log.WriteTo(outDir, config.Compress);
            File.WriteAllLines(Path.Combine(outDir, $"diag-{seed:X16}.jsonl"), lines, new UTF8Encoding(false));
            string line = $"seed {seed} {(log.IsFailed ? "FAILED " + log.Failure!.Message : log.Result!.Reason)} R{log.Result?.MajorRound ?? log.Failure!.MajorRound} turns {log.Turns.Count}";
            summary.Add(line);
            Console.WriteLine(line);
        });
        Console.WriteLine($"done {config.Count} matches in {sw.ElapsedMilliseconds} ms");
        return 0;
    }
}

/// <summary>旁观装饰器：先只读复算，再原样转发给内层 AI。</summary>
internal sealed class DiagController(HeuristicTurnController inner, MatchFlow match, ulong seed, List<string> sink) : ITurnController
{
    private int _decision;

    public void OrganizeHand(PlayerHandAccess hand, int overflow) => inner.OrganizeHand(hand, overflow);

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel) => inner.Recruit(hand, panel);

    public bool OnRejected(StagedBatch batch, BatchFailure failure) => inner.OnRejected(batch, failure);

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        PlayerId me = inner.Player;
        MatchPublicView view = match.Publish();
        GameBoard board = view.Board;
        BatchContext context = batch.Context;
        MapData map = board.Map;
        int threshold = board.GroupsOf(me).IsEmpty ? 0 : inner.Config.PassThreshold;
        var evaluator = new BatchEvaluator(me, view, inner.Weights, inner.Config.ImmediateOnly);
        Func<RehearsalResult> raw = match.Rehearse;   // 不经日志装饰器计数

        // ---------- 盘面量 ----------
        int? myZone = view.Players.First(s => s.Player == me).BirthZone;
        var zoneOwners = new Dictionary<int, List<int>>();
        foreach (PlayerFlowState s in view.Players)
        {
            if (s.BirthZone is { } z && s.IsActive)
            {
                (zoneOwners.TryGetValue(z, out var l) ? l : zoneOwners[z] = []).Add(s.Player.Value);
            }
        }

        string Label(Coord c)
        {
            int zone = -1;
            for (int z = 0; z < map.BirthZones.Length; z++)
            {
                if (map.BirthZones[z].Contains(c)) { zone = z; break; }
            }

            if (zone < 0) return "neutral";
            if (zone == myZone) return "own";
            return zoneOwners.ContainsKey(zone) ? "opp" : "emptyzone";
        }

        int playable = board.AllCoords().Count(c => board[c].Terrain == Terrain.Playable);
        int emptyPlayable = board.AllCoords().Count(c => board[c].IsPlayableEmpty);
        int stones = board.AllCoords().Count(c => board[c].Occupant is not null);
        LifeShapeReport life = LifeShapeReport.Analyze(board);
        int forbiddenMe = life.ForbiddenCellsFor(me).Count(c => board[c].IsPlayableEmpty);
        int protectedAll = life.EyeSpaces.Where(life.IsProtected).Sum(e => e.Cells.Length);
        int protectedOwn = life.ProtectedCellsOf(me).Length;

        // 接触：己方棋子与敌方棋子在气边上相邻
        bool contact = false;
        foreach (Group g in board.GroupsOf(me))
        {
            foreach (Coord s in g.Stones)
            {
                foreach (Coord n in board.LibertyNeighbors(s))
                {
                    if (board[n].Occupant is { } o && o.Owner != me) { contact = true; break; }
                }
                if (contact) break;
            }
            if (contact) break;
        }

        // 可被提的目标：非已确定活形的敌方棋串（活形棋串非所有者提不动）
        int enemyWeakStones = 0, enemyAliveStones = 0, enemyWeakLe2 = 0, myWeakStones = 0, myAliveStones = 0;
        foreach (GroupLife gl in life.Groups)
        {
            bool alive = gl.Life == LifeState.Alive;
            if (gl.Group.Owner == me)
            {
                if (alive) myAliveStones += gl.Group.Size; else myWeakStones += gl.Group.Size;
            }
            else if (view.Players.Any(s => s.Player == gl.Group.Owner && s.IsActive))
            {
                if (alive) enemyAliveStones += gl.Group.Size;
                else
                {
                    enemyWeakStones += gl.Group.Size;
                    if (board.LibertiesOf(gl.Group).Length <= 2) enemyWeakLe2++;
                }
            }
        }

        int capPoints = 0;

        // ---------- 单点枚举复算（与 RankPoints 同口径；候选格上限 K>0 时不复算预筛，标记出来） ----------
        ImmutableArray<PieceType> types = [.. context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order()];
        ImmutableArray<Coord> cells = [.. context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order()];
        bool prefiltered = inner.Config.CandidateCellLimit > 0 && cells.Length > inner.Config.CandidateCellLimit && !types.IsEmpty;
        batch.Clear();
        EvaluationBreakdown baseEval = evaluator.Evaluate([], raw(), context);
        int tried = 0, illegal = 0, lifeElim = 0, evaluated = 0, overThr = 0, positive = 0;
        var lifeElimByLabel = new Dictionary<string, int>();
        var overByLabel = new Dictionary<string, int>();
        BigInteger? maxGain = null;
        string? maxGainLabel = null;
        var points = new List<PointScore>();
        if (!prefiltered)
        {
            foreach (Coord cell in cells)
            {
                foreach (PieceType type in types)
                {
                    IEnumerable<TerrainEdit?> edits = type == TerrainEditRules.EditorType
                        ? new TerrainEdit?[] { null }.Concat(TerrainEditRules.LegalTargets(map, cell, context.WorkshopActive).Select(e => (TerrainEdit?)e))
                        : [null];
                    foreach (TerrainEdit? edit in edits)
                    {
                        batch.Clear();
                        if (batch.Stage(cell, type, edit) is not null) continue;
                        tried++;
                        RehearsalResult r = raw();
                        if (!r.IsLegal) { illegal++; continue; }
                        if (!r.Captures.IsDefaultOrEmpty) capPoints++;
                        if (!evaluator.TryEvaluate(batch.Placements, r, context, out EvaluationBreakdown? ev))
                        {
                            lifeElim++;
                            string lb = Label(cell);
                            lifeElimByLabel[lb] = lifeElimByLabel.GetValueOrDefault(lb) + 1;
                            continue;
                        }

                        evaluated++;
                        BigInteger gain = ev.Total - baseEval.Total;
                        if (gain > 0) positive++;
                        if (gain > threshold)
                        {
                            overThr++;
                            string lb = Label(cell);
                            overByLabel[lb] = overByLabel.GetValueOrDefault(lb) + 1;
                        }

                        if (maxGain is null || gain > maxGain) { maxGain = gain; maxGainLabel = Label(cell); }
                        points.Add(new PointScore(cell, type, edit, ev));
                    }
                }
            }
        }

        batch.Clear();
        ImmutableArray<PointScore> ranking = [.. points
            .OrderByDescending(p => p.Total)
            .ThenBy(p => p.Coord)
            .ThenBy(p => p.Type)
            .ThenBy(p => p.Edit?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .Take(inner.Config.CandidatePointCount)];

        // ---------- k=0 贪心复算：撤回原因分类 ----------
        int kept = 0, wdIllegal = 0, wdLife = 0, wdThr = 0, wdThrPositive = 0;
        var keptLabels = new List<string>();
        if (!prefiltered && !ranking.IsEmpty && context.DeployLimit > 0)
        {
            batch.Clear();
            EvaluationBreakdown current = evaluator.Evaluate([], raw(), context);
            foreach (PointScore point in ranking)
            {
                if (batch.Count >= context.DeployLimit) break;
                if (batch.Placements.Any(p => p.Coord == point.Coord) || batch.Stage(point.Coord, point.Type, point.Edit) is not null) continue;
                RehearsalResult r = raw();
                if (!r.IsLegal) { batch.Unstage(point.Coord); wdIllegal++; continue; }
                if (!evaluator.TryEvaluate(batch.Placements, r, context, out EvaluationBreakdown? next)) { batch.Unstage(point.Coord); wdLife++; continue; }
                BigInteger delta = next.Total - current.Total;
                if (delta > threshold) { current = next; kept++; keptLabels.Add(Label(point.Coord)); }
                else { batch.Unstage(point.Coord); wdThr++; if (delta > 0) wdThrPositive++; }
            }
        }

        batch.Clear();

        // ---------- 转发给内层 AI ----------
        inner.Deploy(batch, rehearse);

        bool? rankMatch = prefiltered ? null : inner.LastPointRanking.Length == ranking.Length
            && inner.LastPointRanking.Zip(ranking).All(t => t.First.Coord == t.Second.Coord && t.First.Type == t.Second.Type
                && Equals(t.First.Edit, t.Second.Edit) && t.First.Total == t.Second.Total);
        CandidateBatch? choice = inner.LastChoice;
        var chosen = choice is null || choice.IsPass ? [] : choice.Placements.Select(p => p.Coord).ToList();
        int chosenTouchEnemy = chosen.Count(c => board.LibertyNeighbors(c).Any(n => board[n].Occupant is { } o && o.Owner != me));

        var powers = view.Players.Select(s => view.Power is { } pw ? pw.Of(s.Player).Total.ToString() : "0").ToList();
        int? rank = view.Power?.RankOf(me);
        var rec = new Dictionary<string, object?>
        {
            ["seed"] = seed,
            ["d"] = _decision++,
            ["round"] = view.MajorRound,
            ["player"] = me.Value,
            ["zone"] = myZone,
            ["rank"] = rank,
            ["powers"] = powers,
            ["deploy_limit"] = context.DeployLimit,
            ["legal_range"] = context.LegalRange.Count,
            ["legal_empty"] = cells.Length,
            ["legal_by"] = cells.GroupBy(Label).ToDictionary(g => g.Key, g => g.Count()),
            ["forbidden_by"] = life.ForbiddenCellsFor(me).Where(c => board[c].IsPlayableEmpty).GroupBy(Label).ToDictionary(g => g.Key, g => g.Count()),
            ["forbidden_me"] = forbiddenMe,
            ["protected_all"] = protectedAll,
            ["protected_own"] = protectedOwn,
            ["playable"] = playable,
            ["empty_playable"] = emptyPlayable,
            ["stones"] = stones,
            ["contact"] = contact,
            ["enemy_weak_stones"] = enemyWeakStones,
            ["enemy_alive_stones"] = enemyAliveStones,
            ["enemy_weak_le2"] = enemyWeakLe2,
            ["my_weak_stones"] = myWeakStones,
            ["my_alive_stones"] = myAliveStones,
            ["cap_points"] = capPoints,
            ["threshold"] = threshold,
            ["types"] = types.Length,
            ["prefiltered"] = prefiltered,
            ["tried"] = tried,
            ["illegal"] = illegal,
            ["life_elim"] = lifeElim,
            ["life_elim_by"] = lifeElimByLabel,
            ["evaluated"] = evaluated,
            ["positive"] = positive,
            ["over_thr"] = overThr,
            ["over_by"] = overByLabel,
            ["max_gain"] = maxGain?.ToString(),
            ["max_gain_at"] = maxGainLabel,
            ["g_kept"] = kept,
            ["g_wd_illegal"] = wdIllegal,
            ["g_wd_life"] = wdLife,
            ["g_wd_thr"] = wdThr,
            ["g_wd_thr_pos"] = wdThrPositive,
            ["g_kept_at"] = keptLabels,
            ["rank_match"] = rankMatch,
            ["chosen"] = chosen.Select(c => c.ToNotation()).ToList(),
            ["chosen_at"] = chosen.Select(Label).ToList(),
            ["chosen_touch_enemy"] = chosenTouchEnemy,
        };
        sink.Add(JsonSerializer.Serialize(rec));
    }
}
