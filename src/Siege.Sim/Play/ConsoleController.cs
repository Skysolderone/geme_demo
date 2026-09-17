using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Sim.Play;

/// <summary>玩家主动退出对局。</summary>
internal sealed class PlayQuitException : Exception
{
    public PlayQuitException()
        : base("玩家退出。")
    {
    }
}

/// <summary>
/// 终端里的人类控制者：每个阶段打印需要的信息并读命令。只经 <see cref="ITurnController"/> 递入的本人句柄操作，
/// 看到的是公开快照 + 自己的手牌，与正式 AI 的信息边界相同。
/// </summary>
internal sealed class ConsoleController : ITurnController
{
    private readonly PlayerId _me;
    private readonly Func<MatchPublicView> _observe;
    private readonly TextReader _in;
    private readonly TextWriter _out;
    private readonly BoardRenderer _render;

    public ConsoleController(PlayerId me, Func<MatchPublicView> observe, TextReader input, TextWriter output)
    {
        _me = me;
        _observe = observe;
        _in = input;
        _out = output;
        _render = new BoardRenderer(output);
    }

    // ---------- 第 2 阶段 ----------

    public void OrganizeHand(PlayerHandAccess hand, int overflow)
    {
        MatchPublicView view = _observe();
        _out.WriteLine();
        _render.Line($"════════ 轮到你了 ════════", ConsoleColor.Yellow);
        _render.Status(view, _me);
        _out.WriteLine();
        _render.Board(view, _me);
        PrintHand(hand.PrivateView());

        while (hand.PrivateView().Overflow > 0)
        {
            HandPrivateView mine = hand.PrivateView();
            _out.WriteLine($"手牌类型超出槽位 {mine.Overflow} 种（槽位 {mine.TypeSlots}），必须整类弃掉。输入要弃的类型字母（如 B）：");
            string line = Read();
            if (BoardRenderer.TryParseType(line, out PieceType type) && mine.CountOf(type) > 0)
            {
                Try(() => hand.Discard(type));
            }
            else
            {
                _out.WriteLine("没有这个类型。");
            }
        }
    }

    // ---------- 第 3 阶段 ----------

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
    {
        while (true)
        {
            RecruitPanelView current = hand.Panel();
            if (current.PicksRemaining <= 0)
            {
                _out.WriteLine("本回合免费选取次数已用完。");
                break;
            }

            _out.WriteLine();
            _render.Line($"── 征募：还能免费选 {current.PicksRemaining} 枚 ──", ConsoleColor.Yellow);
            foreach (RecruitCandidateView c in current.Candidates)
            {
                string mark = c.IsPicked ? "（已选）" : c.IsSelectable ? "" : $"（不可选：{c.Reason}）";
                _out.WriteLine($"  [{c.Index + 1}] {BoardRenderer.Letter(c.Type)} {BoardRenderer.Name(c.Type)}{mark}");
            }

            _out.WriteLine("一次输入全部要选的编号（如 1 3），直接回车表示不征募：");
            string line = Read();
            if (line.Length == 0)
            {
                break;
            }

            bool picked = false;
            foreach (string token in line.Split(' ', ',', '，').Where(t => t.Length > 0))
            {
                if (int.TryParse(token, out int n) && n >= 1 && n <= current.Candidates.Length)
                {
                    int before = hand.Panel().PicksMade;
                    Try(() => hand.Pick(n - 1));
                    picked |= hand.Panel().PicksMade > before;
                }
                else
                {
                    _out.WriteLine($"无效编号：{token}");
                }
            }

            if (picked)
            {
                break;
            }
        }

        PrintHand(hand.PrivateView());
    }

    // ---------- 第 4 阶段 ----------

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        _out.WriteLine();
        _render.Board(_observe(), _me, batch);
        DeployLoop(batch, rehearse);
    }

    // ---------- 第 5 阶段 ----------

    public bool OnRejected(StagedBatch batch, BatchFailure failure)
    {
        _render.Line($"确认失败：{failure.Message}", ConsoleColor.Red);
        return DeployLoop(batch, rehearse: null);
    }

    /// <summary>部署命令循环。返回 <c>true</c> 表示带着暂放棋子确认，<c>false</c> 表示 Pass。</summary>
    private bool DeployLoop(StagedBatch batch, Func<RehearsalResult>? rehearse)
    {
        PrintDeployHelp(batch);
        while (true)
        {
            string staged = batch.Count == 0 ? "无" : string.Join(" ", batch.Placements.Select(p => $"{p.Coord.ToNotation()}:{BoardRenderer.Letter(p.Type)}"));
            _out.Write($"部署 [{batch.Count}/{batch.Context.DeployLimit}] 暂放 {staged} > ");
            string line = Read();
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            string cmd = parts[0].ToLowerInvariant();
            switch (cmd)
            {
                case "?" or "h" or "help":
                    PrintDeployHelp(batch);
                    break;
                case "b" or "board":
                    _render.Board(_observe(), _me, batch);
                    break;
                case "s" or "status":
                    _render.Status(_observe(), _me);
                    break;
                case "c" or "clear":
                    batch.Clear();
                    break;
                case "v" or "preview":
                    if (rehearse is not null)
                    {
                        Preview(rehearse());
                    }
                    else
                    {
                        _out.WriteLine("（补救阶段不支持预演，直接 ok 再试）");
                    }

                    break;
                case "ok" or "y" or "confirm":
                    if (batch.Count == 0)
                    {
                        _out.WriteLine("没有暂放棋子。要 Pass 请输入 pass。");
                        break;
                    }

                    if (rehearse is not null)
                    {
                        RehearsalResult r = rehearse();
                        if (!r.IsLegal)
                        {
                            _render.Line($"这批棋不合法：{r.Failure?.Message}", ConsoleColor.Red);
                            break;
                        }
                    }

                    return true;
                case "pass":
                    if (batch.Count > 0)
                    {
                        batch.Clear();
                    }

                    _out.WriteLine("你 Pass 了（本回合征募所得会被撤销）。");
                    return false;
                default:
                    if (cmd.StartsWith('-'))
                    {
                        if (Coord.TryParse(cmd[1..].ToUpperInvariant(), out Coord remove) && batch.Unstage(remove))
                        {
                            _out.WriteLine($"已撤回 {remove.ToNotation()}");
                        }
                        else
                        {
                            _out.WriteLine("该格没有你暂放的棋子。");
                        }
                    }
                    else if (Coord.TryParse(parts[0].ToUpperInvariant(), out Coord coord))
                    {
                        Stage(batch, coord, parts.Length > 1 ? parts[1] : null);
                    }
                    else
                    {
                        _out.WriteLine("看不懂这个命令，输入 ? 看帮助。");
                    }

                    break;
            }
        }
    }

    private void Stage(StagedBatch batch, Coord coord, string? typeText)
    {
        if (!batch.Board.Map.Contains(coord))
        {
            _out.WriteLine($"棋盘上没有 {coord.ToNotation()}（列 A–{Coord.ColumnLetters[batch.Board.Map.Width - 1]}，行 1–{batch.Board.Map.Height}）。");
            return;
        }

        PieceType type;
        if (typeText is null)
        {
            var available = batch.Context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
            if (available.Count != 1)
            {
                _out.WriteLine($"请指定棋子类型，如：{coord.ToNotation()} M");
                return;
            }

            type = available[0];
        }
        else if (!BoardRenderer.TryParseType(typeText, out type))
        {
            _out.WriteLine($"不认识的类型 {typeText}（B 普通 / F 堡垒 / L 连珠 / M 倍增 / S 协同）");
            return;
        }

        int used = batch.Placements.Count(p => p.Type == type);
        if (used >= batch.Context.StockOf(type))
        {
            _out.WriteLine($"{BoardRenderer.Name(type)}子库存不够（有 {batch.Context.StockOf(type)} 枚）。");
            return;
        }

        if (batch.Stage(coord, type) is { } failure)
        {
            _render.Line(failure.Message, ConsoleColor.Red);
        }
    }

    private void Preview(RehearsalResult r)
    {
        if (!r.IsLegal)
        {
            _render.Line($"预演：不合法——{r.Failure?.Message}", ConsoleColor.Red);
            return;
        }

        if (r.IsPass)
        {
            _out.WriteLine("预演：空批次（等于 Pass）。");
            return;
        }

        MatchPublicView view = _observe();
        long before = view.Power?.Players.FirstOrDefault(p => p.Player == _me)?.Total ?? 0;
        long after = r.ProjectedBoard is { } projected
            ? PowerCalculator.Compute(projected, view.Players.ToDictionary(p => p.Player, p => p.Status), view.SiteValues)
                .Players.FirstOrDefault(p => p.Player == _me)?.Total ?? 0
            : before;
        string captures = r.Captures.IsDefaultOrEmpty ? "不提子" : $"提走对手 {r.Captures.Length} 子";
        _render.Line($"预演：合法，{captures}，你的势力 {before} → {after}", ConsoleColor.Green);
    }

    private void PrintDeployHelp(StagedBatch batch)
    {
        string stock = string.Join("  ", batch.Context.Stock.Where(kv => kv.Value > 0)
            .Select(kv => $"{BoardRenderer.Letter(kv.Key)}{BoardRenderer.Name(kv.Key)}×{kv.Value}"));
        _out.WriteLine();
        _render.Line($"── 部署：本回合最多落 {batch.Context.DeployLimit} 枚  库存 {(stock.Length == 0 ? "空" : stock)} ──", ConsoleColor.Yellow);
        _out.WriteLine("  D4 M   在 D4 暂放一枚倍增子（只有一种棋子时可省略类型）");
        _out.WriteLine("  -D4    撤回 D4      c 清空      v 预演（看提子与势力变化）");
        _out.WriteLine("  ok     确认落子     pass 本回合不落子（撤销本回合征募）");
        _out.WriteLine("  b 看盘  s 看各家状态  q 退出游戏");
    }

    private void PrintHand(HandPrivateView hand)
    {
        string entries = hand.IsEmpty ? "空" : string.Join("  ", hand.Entries.Select(kv =>
            $"{BoardRenderer.Letter(kv.Key)}{BoardRenderer.Name(kv.Key)}×{kv.Value.Total}" + (kv.Value.Gained > 0 ? $"(本轮新得{kv.Value.Gained})" : "")));
        _out.WriteLine($"你的手牌：{entries}    类型槽 {hand.OccupiedSlots}/{hand.TypeSlots?.ToString() ?? "-"}");
    }

    private string Read()
    {
        string? line = _in.ReadLine();
        if (line is null)
        {
            throw new PlayQuitException();
        }

        line = line.Trim();
        if (line is "q" or "quit" or "exit")
        {
            throw new PlayQuitException();
        }

        return line;
    }

    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (SiegeRuleException ex)
        {
            _render.Line(ex.Message, ConsoleColor.Red);
        }
    }
}
