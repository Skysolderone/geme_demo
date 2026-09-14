using System.Diagnostics;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Sim.Play;

/// <summary>终端对局：一名人类玩家对若干启发式 AI。</summary>
internal static class PlayCommand
{
    public static int Run(ulong? seedArg, int playerCount, int seat, AiDifficulty difficulty, int maxRounds, TextReader input, TextWriter output)
    {
        MapData map = FourPlayerBaseMap.Create();
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
        MatchFlow match = MatchFlow.Create(map, new GameSeed(seed), players, MatchOptions.Immediate with { MaxMajorRounds = maxRounds });
        var render = new BoardRenderer(output);

        output.WriteLine();
        render.Line("══════════ 围杀 Siege · 终端对局 ══════════", ConsoleColor.Yellow);
        output.WriteLine($"种子 {seed}（用 --seed {seed} 可重开这一局）  你是玩家{seat}，对手 {playerCount - 1} 名 {difficulty} AI，大回合上限 {(maxRounds == 0 ? "不限" : maxRounds)}");
        output.WriteLine("目标：大回合结束时势力最高。势力 = 你独占的空格数 + 你所有棋串的军势。");
        output.WriteLine("围棋式提子：一批棋落下后，对手没有气的棋串被整串提走；你自己的棋串落完仍无气则整批不合法。");
        output.WriteLine();

        try
        {
            int zone = ChooseZone(match, me, map, input, output, render);
            int next = 0;
            var choices = new List<(PlayerId, int)>();
            foreach (PlayerId p in players)
            {
                if (p == me)
                {
                    choices.Add((p, zone));
                    continue;
                }

                if (next == zone)
                {
                    next++;
                }

                choices.Add((p, next % map.BirthZones.Length));
                next++;
            }

            match.PlantSequentially(choices);

            var runner = new MatchRunner(match);
            foreach (PlayerId p in players)
            {
                runner.SetController(p, p == me
                    ? new ConsoleController(me, match.Publish, input, output)
                    : HeuristicAi.Create(match, p, difficulty));
            }

            output.WriteLine($"出生区锁定：{string.Join("  ", choices.Select(c => $"{BoardRenderer.Label(c.Item1, me)}→{c.Item2 + 1}号区"))}");
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
        long power = after.Power?.Players.FirstOrDefault(p => p.Player == actor)?.Total ?? 0;
        render.Line($"{who} {action}{captures}（势力 {power}）", lost.ContainsKey(me) ? ConsoleColor.Red : ConsoleColor.Gray);

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
            EndReason.MajorRoundLimit => "达到大回合上限",
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
