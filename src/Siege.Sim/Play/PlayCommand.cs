using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Sim.Play;

/// <summary>终端对局：一名人类玩家对若干启发式 AI。</summary>
internal static class PlayCommand
{
    /// <param name="map">对局地图；<c>null</c> 即缺省地图（<see cref="MapCatalog.DefaultId"/>）。标识 → 地图的解析在入口（<c>Program.Play</c>）经 <see cref="MapCatalog"/> 完成。</param>
    /// <param name="cellLimit">AI 候选格上限 K；<c>null</c> 按地图的可落子格数自动取（<see cref="AiSearchConfig.ForMap"/>），0 = 不限制。</param>
    /// <param name="weights">测试接缝：AI 评价权重；<c>null</c> = 默认权重表。终端入口不传（ai-eye R12：终端对局一律用缺省值，不加选项）。</param>
    /// <param name="passThreshold">测试接缝：AI 停手阈值；<c>null</c> = 难度预设的缺省值。终端入口不传（同上）。
    /// 依赖 AI 实际走法的脚本测试用这两项写死权重与阈值，使脚本不随默认值校准而失步（testing.md「依赖 AI 实际怎么走的断言要把权重写死」）。</param>
    public static int Run(
        ulong? seedArg, int playerCount, int seat, AiDifficulty difficulty, TextReader input, TextWriter output, MapData? map = null, int? cellLimit = null,
        EvaluationWeights? weights = null, int? passThreshold = null)
    {
        map ??= MapCatalog.Resolve(null);
        AiSearchConfig search = AiSearchConfig.ForMap(difficulty, map.PlayableCount, cellLimit);
        search = (passThreshold is int threshold ? search with { PassThreshold = threshold } : search).Validated();
        if (playerCount < 2 || playerCount > map.MaxPlayers)
        {
            throw new ArgumentException($"人数须在 2..{map.MaxPlayers}。");
        }

        if (seat < 1 || seat > playerCount)
        {
            throw new ArgumentException($"座位须在 1..{playerCount}。");
        }

        // 种子只决定这局的随机内容；打印出来，用 --seed 可重开同一局。
        ulong seed = seedArg ?? (ulong)Stopwatch.GetTimestamp();
        PlayerId[] players = [.. Enumerable.Range(0, playerCount).Select(i => new PlayerId(i))];
        PlayerId me = players[seat - 1];
        MatchFlow match = MatchFlow.Create(map, new GameSeed(seed), players, MatchOptions.Immediate);
        var render = new BoardRenderer(output);

        output.WriteLine();
        render.Line("══════════ 围杀 Siege · 终端对局 ══════════", ConsoleColor.Yellow);
        if (map.Id != MapCatalog.DefaultId)
        {
            // 完整地图标识取自公开视图（插旗阶段即公开）。生成图的标识里带地图种子：与下一行的对局种子分开显示，二者互相独立。
            string mapId = match.Publish().MapId;
            output.WriteLine($"地图 {mapId}（{map.Width}×{map.Height}，{map.BirthZones.Length} 个出生区）"
                + (GeneratedMapId.IsGenerated(mapId) ? $"——随机生成图，用 --map {mapId} 可再得到同一张图；地图种子只决定地图，与下面的对局种子无关" : string.Empty));
        }

        output.WriteLine($"种子 {seed}（用 --seed {seed} 可重开这一局）  你是玩家{seat}，对手 {playerCount - 1} 名 {difficulty} AI");
        output.WriteLine("目标：终局时势力最高。势力 = 你独占的空格数 + 你所有棋串的军势。");
        output.WriteLine("终局：只剩一名参赛玩家、棋盘填满或一整轮所有人都 Pass；曾有势力而势力降到 0 即出局。");

        output.WriteLine("围棋式提子：一批棋落下后，对手没有气的棋串被整串提走；你自己的棋串落完仍无气则整批不合法。");
        output.WriteLine();

        try
        {
            int zone = ChooseZone(match, me, map, input, output, render);
            // 其余玩家的选区由 Core 的唯一实现给出（frontier-map D4）：标准图按编号顺排，平台多于人数的图由种子选区。
            ImmutableArray<(PlayerId Player, int Zone)> choices = match.PlantPrototype((me, zone));

            var runner = new MatchRunner(match);
            foreach (PlayerId p in players)
            {
                runner.SetController(p, p == me
                    ? new ConsoleController(me, match.Publish, input, output)
                    : HeuristicAi.Create(match, p, difficulty, weights, search));
            }

            output.WriteLine($"出生区锁定：{string.Join("  ", choices.Select(c => $"{BoardRenderer.Label(c.Player, me)}→{BirthZoneLabel.Number(c.Zone)}号区"))}");
            output.WriteLine($"第 1 大回合顺序随机：{string.Join(" > ", match.ActionOrder.Select(p => BoardRenderer.Label(p, me)))}");

            while (match.Phase == MatchPhase.InProgress)
            {
                PlayerId current = match.CurrentPlayer!.Value;
                int round = match.MajorRound;
                MatchPublicView before = match.Publish();
                if (current != me)
                {
                    output.Write($"{BoardRenderer.Label(current, me)} 思考中… ");
                }

                runner.RunTurn();
                MatchPublicView after = match.Publish();
                Summarize(before, after, current, me, output, render);
                if (match.Phase == MatchPhase.InProgress && match.MajorRound != round)
                {
                    render.Line($"── 第 {round} 大回合结束 ──", ConsoleColor.DarkYellow);
                }
            }

            MatchPublicView final = match.Publish();
            output.WriteLine();
            render.Board(final, me);
            PrintResult(match.Result!, me, output, render);
            return 0;
        }
        catch (PlayQuitException)
        {
            output.WriteLine();
            output.WriteLine($"已退出。种子 {seed}，用 --seed {seed} 可以重开这一局。");
            return 0;
        }
    }

    private static int ChooseZone(MatchFlow match, PlayerId me, MapData map, TextReader input, TextWriter output, BoardRenderer render)
    {
        render.Board(match.Publish(), me, zones: true);
        output.WriteLine();
        while (true)
        {
            output.Write($"选择你的出生区（1–{map.BirthZones.Length}），前 3 个大回合只能在这里落子 > ");
            string? line = input.ReadLine() ?? throw new PlayQuitException();
            line = line.Trim();
            if (line is "q" or "quit")
            {
                throw new PlayQuitException();
            }

            if (int.TryParse(line, out int z) && z >= 1 && z <= map.BirthZones.Length)
            {
                return z - 1;
            }
        }
    }

    /// <summary>对比前后快照，说明这一小回合发生了什么。</summary>
    private static void Summarize(MatchPublicView before, MatchPublicView after, PlayerId actor, PlayerId me, TextWriter output, BoardRenderer render)
    {
        var placed = new List<string>();
        var lost = new Dictionary<PlayerId, int>();
        foreach (Coord c in after.Board.AllCoords())
        {
            Occupant? was = before.Board[c].Occupant;
            Occupant? now = after.Board[c].Occupant;
            if (now is { } n && was is null && n.Owner == actor)
            {
                placed.Add($"{c.ToNotation()}{BoardRenderer.Letter(n.Type)}");
            }

            if (was is { } w && now is null)
            {
                lost[w.Owner] = lost.GetValueOrDefault(w.Owner) + 1;
            }
        }

        string who = BoardRenderer.Label(actor, me);
        string action = placed.Count == 0 ? "Pass" : $"落子 {string.Join(" ", placed)}";
        string captures = lost.Count == 0 ? "" : "，提走 " + string.Join("、", lost.Select(kv => $"{BoardRenderer.Label(kv.Key, me)} {kv.Value} 子"));
        Core.Scoring.PlayerPower? detail = after.Power?.Players.FirstOrDefault(p => p.Player == actor);
        render.Line($"{who} {action}{captures}（{BoardRenderer.PowerText(detail)}）", lost.ContainsKey(me) ? ConsoleColor.Red : ConsoleColor.Gray);

        foreach (PlayerFlowState s in after.Players)
        {
            PlayerFlowState? old = before.Players.FirstOrDefault(p => p.Player == s.Player);
            if (old is not null && old.Status != s.Status)
            {
                render.Line($"  {BoardRenderer.Label(s.Player, me)} → {s.Status}", ConsoleColor.Red);
            }
        }
    }

    private static void PrintResult(MatchResult result, PlayerId me, TextWriter output, BoardRenderer render)
    {
        string reason = result.Reason switch
        {
            EndReason.LastPlayerStanding => "只剩一名玩家",
            EndReason.AllPassed => "一整轮所有人都 Pass",
            EndReason.BoardFull => "棋盘已无空位",
            _ => result.Reason.ToString(),
        };
        output.WriteLine();
        render.Line($"══════════ 对局结束：第 {result.MajorRound} 大回合，{reason} ══════════", ConsoleColor.Yellow);
        foreach (Standing s in result.Standings)
        {
            render.Line($"  第 {s.Rank} 名  {BoardRenderer.Label(s.Player, me),-8} 势力 {s.Input.Power}", s.Player == me ? ConsoleColor.Yellow : ConsoleColor.Gray);
        }

        Standing mine = result.Standings.First(s => s.Player == me);
        output.WriteLine(mine.Rank == 1 ? "你赢了！" : $"你获得第 {mine.Rank} 名。");
    }
}
