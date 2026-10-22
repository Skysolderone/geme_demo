// route-value-diagnosis 一次性探针（存档副本，不参与编译）。
// 复现：
//   1. 复制到 tests/Siege.Core.Tests/RouteValueProbeTmp.cs；
//   2. 临时把 src/Siege.Core/Ai/HeuristicTurnController.cs 的 ScorePoints / Rank / ContinueGreedy 由 private 改为 internal；
//   3. RV_DIR=<新跑日志目录> RV_OUT=<research/data> dotnet test tests/Siege.Core.Tests -c Release --filter "FullyQualifiedName~RouteValueProbeTmp"；
//   4. 删除测试文件、git checkout 还原源码并 touch。
// 驱动：LogReplay.Run 按日志首部重建对局、逐小回合与原日志核对（分歧即报），探针只在影子批次上暂放 / 预演，不碰正式批次、不消费随机流。
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests;

public class RouteValueProbeTmp
{
    private const int Radius = 3;
    private const int DistCap = 15;

    private sealed record Reach(int U, int URelic, int B3, int B3Relic, int B3Pub, int DistSum, int DistPub);

    /// <summary>己方全部棋子为源，沿气边（GameBoard.LibertyNeighbors，唯一实现）只经空的可落子格做多源 BFS。</summary>
    private static Reach ReachOf(GameBoard b, PlayerId p)
    {
        var dist = new Dictionary<Coord, int>();
        var queue = new Queue<Coord>();
        foreach (Coord s in b.AllCoords().Where(c => b[c].Occupant is { } o && o.Owner == p).ToList())
        {
            foreach (Coord n in b.LibertyNeighbors(s))
            {
                if (b[n].IsPlayableEmpty && !dist.ContainsKey(n))
                {
                    dist[n] = 1;
                    queue.Enqueue(n);
                }
            }
        }

        while (queue.Count > 0)
        {
            Coord c = queue.Dequeue();
            int d = dist[c];
            foreach (Coord n in b.LibertyNeighbors(c))
            {
                if (b[n].IsPlayableEmpty && !dist.ContainsKey(n))
                {
                    dist[n] = d + 1;
                    queue.Enqueue(n);
                }
            }
        }

        int u = dist.Count, uRelic = 0, b3 = 0, b3Relic = 0, b3Pub = 0, distSum = 0, distPub = 0;
        foreach ((Coord c, int d) in dist)
        {
            bool relic = b[c].IsRelicCell;
            bool pub = relic && b.Map.RelicCells.TryGetValue(c, out RelicCellSpec spec) && spec.Zone == RelicZone.Contested;
            uRelic += relic ? 1 : 0;
            if (d <= Radius)
            {
                b3++;
                b3Relic += relic ? 1 : 0;
                b3Pub += pub ? 1 : 0;
            }
        }

        foreach ((Coord c, RelicCellSpec spec) in b.Map.RelicCells)
        {
            if (!b[c].IsPlayableEmpty)
            {
                continue;
            }

            int d = dist.TryGetValue(c, out int dd) ? Math.Min(dd, DistCap) : DistCap;
            distSum += d;
            distPub += spec.Zone == RelicZone.Contested ? d : 0;
        }

        return new Reach(u, uRelic, b3, b3Relic, b3Pub, distSum, distPub);
    }

    private static string D(Reach a, Reach b) =>
        $"{a.U - b.U},{a.URelic - b.URelic},{a.B3 - b.B3},{a.B3Relic - b.B3Relic},{a.B3Pub - b.B3Pub},{b.DistSum - a.DistSum},{b.DistPub - a.DistPub}";

    /// <summary>隔断线：河流 = 整列 / 整行（不在外圈）全为深水；高台 = 全部高度 ≥ 1。</summary>
    internal static (bool Column, int Index)? BarrierOf(MapData baseMap, BoardPlate plate)
    {
        if (plate.Theme is not (BoardTheme.River or BoardTheme.Plateau))
        {
            return null;
        }

        bool On(Coord c) => plate.Theme == BoardTheme.River ? baseMap.SurfaceAt(c) == Surface.DeepWater : baseMap.HeightAt(c) >= 1;
        for (int x = plate.Origin.X + 1; x < plate.Origin.X + plate.Width - 1; x++)
        {
            if (Enumerable.Range(plate.Origin.Y, plate.Height).All(y => On(new Coord(x, y))))
            {
                return (true, x);
            }
        }

        for (int y = plate.Origin.Y + 1; y < plate.Origin.Y + plate.Height - 1; y++)
        {
            if (Enumerable.Range(plate.Origin.X, plate.Width).All(x => On(new Coord(x, y))))
            {
                return (false, y);
            }
        }

        return null;
    }

    /// <summary>格相对隔断线的一侧：core（含核心区一侧）/ line / far（不含核心区一侧，= 生成器的 beyond）；无隔断线为 none。</summary>
    internal static string SideOf(MapData baseMap, BoardPlate plate, Coord c)
    {
        if (BarrierOf(baseMap, plate) is not { } line)
        {
            return "none";
        }

        int along = line.Column ? c.X : c.Y;
        int core = line.Column ? plate.CoreOrigin.X : plate.CoreOrigin.Y;
        return along == line.Index ? "line" : Math.Sign(along - line.Index) == Math.Sign(core - line.Index) ? "core" : "far";
    }

    [Fact]
    public void Geometry()
    {
        string? outDir = Environment.GetEnvironmentVariable("RV_OUT");
        if (outDir is null)
        {
            return;
        }

        var sb = new StringBuilder("seed,zone,theme,lineCol,lineIndex,cell,side,relicZone\n");
        for (ulong seed = 1; seed <= 20; seed++)
        {
            MapData map = LinkMapGenerator.Generate(seed);
            int zone = 0;
            foreach (BoardPlate plate in map.BirthBoards())
            {
                var line = BarrierOf(map, plate);
                foreach (Coord c in plate.Cells())
                {
                    string relic = map.RelicCells.TryGetValue(c, out RelicCellSpec spec) ? spec.Zone.ToString() : "";
                    sb.Append($"{seed},{zone},{plate.Theme},{(line is { } l ? (l.Column ? "1" : "0") : "")},{(line is { } m ? m.Index.ToString() : "")},{c.ToNotation()},{SideOf(map, plate, c)},{relic}\n");
                }

                zone++;
            }
        }

        File.WriteAllText(Path.Combine(outDir, "geometry.csv"), sb.ToString());
    }

    [Fact]
    public void Probe()
    {
        string? dir = Environment.GetEnvironmentVariable("RV_DIR");
        string? outDir = Environment.GetEnvironmentVariable("RV_OUT");
        if (dir is null || outDir is null)
        {
            return;
        }

        List<MatchLog> logs = MatchLog.ReadDirectory(dir);
        var decisions = new ConcurrentDictionary<int, List<string>>();
        var options = new ConcurrentDictionary<int, List<string>>();
        var failures = new ConcurrentBag<string>();
        Parallel.For(0, logs.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            (List<string> dec, List<string> opt, string? fail) = ProbeOne(logs[i]);
            decisions[i] = dec;
            options[i] = opt;
            failures.Add($"{logs[i].Header.Seed},{fail ?? "ok"}");
        });

        var d = new StringBuilder("seed,turn,round,player,build,artisan,deployLimit,legalCells,bridgeOptions,bridgeLegal,passThreshold,status,topGain,reviewBridgeGain,reviewBridgeRank,outsideBridgeGain,bestBridgeGain,bestBridgeCell,bestBridgeTarget,bestBridgeTheme,bestBridgeOwnBoard,bestBridgeSide,forcedBatchGain,actualChoice,actualTargets,actualGain,editEffect\n");
        var o = new StringBuilder("seed,turn,round,player,build,cell,target,theme,ownBoard,side,legal,gain,gainNoEdit,dU,dURelic,dB3,dB3Relic,dB3Pub,dDist,dDistPub,cU,cURelic,cB3,cB3Relic,cB3Pub,cDist,cDistPub\n");
        foreach (int i in Enumerable.Range(0, logs.Count))
        {
            d.Append(string.Concat(decisions[i]));
            o.Append(string.Concat(options[i]));
        }

        File.WriteAllText(Path.Combine(outDir, "decisions.csv"), d.ToString());
        File.WriteAllText(Path.Combine(outDir, "options.csv"), o.ToString());
        File.WriteAllText(Path.Combine(outDir, "replay.csv"), "seed,replay\n" + string.Join("\n", failures.OrderBy(s => s)) + "\n");
    }

    private static (List<string> Decisions, List<string> Options, string? Failure) ProbeOne(MatchLog log)
    {
        var decisions = new List<string>();
        var options = new List<string>();
        LogHeader header = log.Header;
        string seed = header.Seed;
        Dictionary<int, TurnSnapshot> recorded = log.Turns.GroupBy(t => t.Turn).ToDictionary(g => g.Key, g => g.Last());
        MatchSession? current = null;
        (PlayerId Player, int Row, BigInteger PassTotal)? pending = null;

        // 实际批次收益：该玩家下一次被探针看到之前，其控制者的 LastChoice 仍是本次决策的结果。
        void Harvest()
        {
            if (pending is { } p && current?.AiOf(p.Player) is { } ai)
            {
                BigInteger gain = ai.LastChoice is { } choice ? choice.Total - p.PassTotal : BigInteger.Zero;
                decisions[p.Row] = decisions[p.Row].Replace("{ACTUAL}", gain.ToString());
            }

            pending = null;
        }

        string? failure = LogReplay.Run(log, (session, batch) =>
        {
            current = session;
            Harvest();
            BatchContext context = batch.Context;
            if (context.DeployLimit <= 0 || context.Stock.GetValueOrDefault(TerrainEditRules.EditorType) <= 0 || session.AiOf(context.Player) is not { } ai)
            {
                return null;
            }

            int turn = session.TurnCount + 1;
            if (!recorded.TryGetValue(turn, out TurnSnapshot? snapshot) || snapshot.Player != context.Player.Value)
            {
                return $"turn {turn} missing";
            }

            MatchFlow match = session.Match;
            PlayerId me = context.Player;
            GameBoard board = batch.Board;
            MapData map = board.Map;
            MapData baseMap = board.BaseMap;
            int myZone = header.Zones.Count > me.Value ? header.Zones[me.Value] : -1;
            bool build = context.EditScope is not null;   // 注意：全图期也非 null，分析脚本改按大回合 ≤ 3 分阶段
            var shadow = new StagedBatch(board, context);
            RehearsalResult Raw() => BatchRehearsal.Rehearse(shadow.Board, shadow.Context, shadow.Placements, match.History);
            RehearsalResult Seen()
            {
                RehearsalResult r = Raw();
                return ai.RehearsalView is { } project ? project(r) : r;
            }

            ImmutableArray<Coord> legal = [.. context.LegalRange.Where(c => board[c].IsPlayableEmpty).Order()];
            var bridgeOptions = new List<(Coord Cell, TerrainEdit Edit)>();
            foreach (Coord cell in legal)
            {
                foreach (TerrainEdit e in TerrainEditRules.LegalTargets(map, cell, context.WorkshopActive, context.EditScope))
                {
                    if (e.Kind == TerrainEditKind.Bridge)
                    {
                        bridgeOptions.Add((cell, e));
                    }
                }
            }

            string actualChoice = string.Join("|", ArtisanReplay.ChoiceOf(snapshot));
            string actualTargets = string.Join("|", (snapshot.Edits ?? []).Where(e => e.Action == "Bridge").Select(e => $"{e.Artisan}>{e.Target}"));
            BatchEvaluator evaluator = ai.CreateEvaluator();
            shadow.Clear();
            BigInteger passTotal = evaluator.Evaluate([], Seen(), context).Total;
            string prefix = $"{seed},{turn},{match.MajorRound},{me.Value},{(build ? 1 : 0)}";

            if (bridgeOptions.Count == 0)
            {
                decisions.Add($"{prefix},{context.Stock[TerrainEditRules.EditorType]},{context.DeployLimit},{legal.Length},0,0,,NoTarget,,,,,,,,,,,,{actualChoice},{actualTargets},{{ACTUAL}},\n");
                pending = (me, decisions.Count - 1, passTotal);
                return null;
            }

            ArtisanEditReview review = ai.ReviewArtisanEdits(shadow, Seen);
            ArtisanKindReview kr = review.Of(TerrainEditKind.Bridge);

            // 单点打分（同 ScoreOne：暂放 → 预演（经座位视野投影）→ 活形硬约束 → 九维评价）；可达性用未投影的预演盘面（含提子）。
            (bool Legal, BigInteger Gain, GameBoard Board) Score(Coord cell, TerrainEdit? edit)
            {
                shadow.Clear();
                if (shadow.Stage(cell, TerrainEditRules.EditorType, edit) is not null)
                {
                    return (false, 0, BatchRehearsal.Project(board, me, [new Placement(cell, TerrainEditRules.EditorType, edit)]));
                }

                RehearsalResult raw = Raw();
                RehearsalResult seen = ai.RehearsalView is { } project ? project(raw) : raw;
                GameBoard projected = raw.ProjectedBoard ?? BatchRehearsal.Project(board, me, shadow.Placements);
                EvaluationBreakdown? ev = null;
                bool ok = seen.IsLegal && evaluator.TryEvaluate(shadow.Placements, seen, context, out ev);
                BigInteger gain = ok ? ev!.Total - passTotal : 0;
                return (ok, gain, projected);
            }

            Reach now = ReachOf(board, me);
            var plainCache = new Dictionary<Coord, (bool Legal, BigInteger Gain, Reach Reach)>();
            (Coord Cell, TerrainEdit Edit, BigInteger Gain, string Theme, bool Own, string Side)? best = null;
            int legalCount = 0;
            foreach ((Coord cell, TerrainEdit edit) in bridgeOptions)
            {
                if (!plainCache.TryGetValue(cell, out var plain))
                {
                    var s = Score(cell, null);
                    plain = (s.Legal, s.Gain, ReachOf(s.Board, me));
                    plainCache[cell] = plain;
                }

                var withBridge = Score(cell, edit);
                Reach r = ReachOf(withBridge.Board, me);
                int? zone = map.BirthBoardAt(edit.Cell);
                BoardPlate? plate = zone is int z ? map.BirthBoard(z) : null;
                string theme = plate?.Theme?.ToString() ?? "Public";
                bool own = zone == myZone;
                string side = plate is { } pl ? SideOf(baseMap, pl, edit.Cell) : "none";
                options.Add($"{prefix},{cell.ToNotation()},{edit},{theme},{(own ? 1 : 0)},{side},{(withBridge.Legal ? 1 : 0)},{(withBridge.Legal ? withBridge.Gain.ToString() : "")},{(plain.Legal ? plain.Gain.ToString() : "")},{D(r, plain.Reach)},{D(r, now)}\n");
                legalCount += withBridge.Legal ? 1 : 0;
                if (withBridge.Legal && (best is null || withBridge.Gain > best.Value.Gain))
                {
                    best = (cell, edit, withBridge.Gain, theme, own, side);
                }
            }

            // 含最佳搭桥的批次：以它为起点按正式单点排序前 N 续贪心（ContinueGreedy 同一实现、同一实际停手阈值）。
            string forced = "";
            if (best is { } bb)
            {
                (_, List<PointScore> points) = ai.ScorePoints(shadow, Seen, evaluator);
                ImmutableArray<PointScore> ranking = [.. HeuristicTurnController.Rank(points).Take(ai.Config.CandidatePointCount)];
                shadow.Clear();
                if (shadow.Stage(bb.Cell, TerrainEditRules.EditorType, bb.Edit) is null
                    && Seen() is { IsLegal: true } res && evaluator.TryEvaluate(shadow.Placements, res, context, out EvaluationBreakdown? start))
                {
                    CandidateBatch cb = ai.ContinueGreedy(ranking, shadow, Seen, evaluator, review.PassThreshold, start);
                    forced = (cb.Total - passTotal).ToString();
                }

                shadow.Clear();
            }

            string effect = kr.EditEffect.IsDefaultOrEmpty ? "" : string.Join("|", kr.EditEffect);
            decisions.Add($"{prefix},{context.Stock[TerrainEditRules.EditorType]},{context.DeployLimit},{legal.Length},{bridgeOptions.Count},{legalCount},{review.PassThreshold},{review.StatusOf(TerrainEditKind.Bridge)},{review.Top?.Gain},{kr.Best?.Gain},{kr.Best?.Rank},{kr.OutsideBest?.Gain},{best?.Gain},{best?.Cell.ToNotation()},{best?.Edit},{best?.Theme},{(best is { } b2 ? (b2.Own ? 1 : 0) : "")},{best?.Side},{forced},{actualChoice},{actualTargets},{{ACTUAL}},{effect}\n");
            pending = (me, decisions.Count - 1, passTotal);
            return null;
        });

        Harvest();
        return (decisions, options, failure);
    }
}
