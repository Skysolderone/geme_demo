using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Carry;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Sim.Play;

/// <summary>终端对局：一名人类玩家对若干启发式 AI。</summary>
internal static class PlayCommand
{
    /// <summary>链接模式的参赛人数（裁决 3b / S1：只有 4 人局）。</summary>
    private const int LinkPlayers = 4;

    /// <param name="playerCountArg">参赛人数；<c>null</c> = 4。链接模式恒为 4 人局（S1），给别的值即报错（retire-legacy-maps 4b.3 / L20：原"缺省取地图人数上限"随 2 / 3 人图删除）。</param>
    /// <param name="map">对局地图；<c>null</c> 即缺省地图（<see cref="MapCatalog.DefaultId"/>）。标识 → 地图的解析在入口（<c>Program.Play</c>）经 <see cref="MapCatalog"/> 完成。</param>
    /// <param name="cellLimit">AI 候选格上限 K；<c>null</c> 按地图的可落子格数自动取（<see cref="AiSearchConfig.ForMap"/>），0 = 不限制。</param>
    /// <param name="weights">测试接缝：AI 评价权重（整表）；<c>null</c> = 默认权重表 <see cref="EvaluationWeights.Default"/>（retire-legacy-maps 3.3：地图专属覆盖已删除）。终端入口不传（ai-eye R12：终端对局一律用缺省值，不加选项）。</param>
    /// <param name="passThreshold">测试接缝：AI 停手阈值；<c>null</c> = 难度预设的缺省值。终端入口不传（同上）。
    /// 依赖 AI 实际走法的脚本测试用这两项写死权重与阈值，使脚本不随默认值校准而失步（testing.md「依赖 AI 实际怎么走的断言要把权重写死」）。</param>
    /// <param name="contentSet">测试接缝：对局内容集；<c>null</c> = 对局配置缺省值（<see cref="ContentSets.Default"/>，v2）。终端入口不传（more-pieces-relics D8）。
    /// 依赖走法的脚本测试写死 v1（征募棋池随内容集变），使脚本不随缺省内容集变化而失步。</param>
    /// <param name="profile">带入带出的档案存取（carry-in-out D10）；<c>null</c> = 关闭带入带出、不读写任何档案（缺省）。
    /// 入口 <c>Program.Play</c> 缺省传缺省档案、<c>--no-carry</c> 传 <c>null</c>；脚本化测试不传即关闭，不碰真实用户目录，只在需要时注入临时档案。</param>
    /// <param name="onAi">测试接缝：每名 AI 玩家的控制者建好后回调一次（终端不写对局日志，脚本测试经它核对 AI 的难度、搜索配置与前瞻记录）。终端入口不传。</param>
    /// <param name="pairingSource">测试接缝：链接图对局的配对来源；<c>null</c> = 真随机 <see cref="Running.RandomPairingSource"/>（规格 simulation-harness：有人值守对局 MUST 用真随机源）。
    /// 终端入口不传；脚本测试注入固定配对使转录可复现。旧地图（Legacy 夹具）的对局不读它。</param>
    public static int Run(
        ulong? seedArg, int? playerCountArg, int seat, AiDifficulty difficulty, TextReader input, TextWriter output, MapData? map = null, int? cellLimit = null,
        EvaluationWeights? weights = null, int? passThreshold = null, ContentSet? contentSet = null,
        CarryProfileStore? profile = null, Action<PlayerId, HeuristicTurnController>? onAi = null,
        IPairingSource? pairingSource = null)
    {
        map ??= MapCatalog.Resolve(null);
        int playerCount = playerCountArg ?? LinkPlayers;
        AiSearchConfig search = AiSearchConfig.ForMap(difficulty, map.PlayableCount, cellLimit);
        search = (passThreshold is int threshold ? search with { PassThreshold = threshold } : search).Validated();
        if (playerCount != LinkPlayers)
        {
            // 裁决 3b / S1：与 Core 建局同一口径的报文；在入口先报，免得非正人数先在建玩家列表时崩掉。
            throw new ArgumentException($"链接模式只有 {LinkPlayers} 人局，实际 {playerCount} 人。");
        }

        if (seat < 1 || seat > playerCount)
        {
            throw new ArgumentException($"座位须在 1..{playerCount}。");
        }

        // 种子只决定这局的随机内容；打印出来，用 --seed 可重开同一局。
        ulong seed = seedArg ?? (ulong)Stopwatch.GetTimestamp();
        PlayerId[] players = [.. Enumerable.Range(0, playerCount).Select(i => new PlayerId(i))];
        PlayerId me = players[seat - 1];
        // board-link-mode 实现裁决 C1：链接图一律以链接模式建局（盲选插旗，冒险概率不读，C2）；其余地图是 Legacy 夹具，原样。
        // retire-legacy-maps 3.6：建局分叉删除——公开 MatchFlow.Create 恒以链接模式建局（3.5），冒险概率测试接缝已删（3.1）。
        MatchOptions options = MatchOptions.Immediate;
        options = contentSet is { } set ? options with { ContentSet = set } : options;
        var render = new BoardRenderer(output);

        // 带入带出（carry-in-out）：开启时在建局之前读档、处理残留在途记录、兑换与选择带入；AI 按本机玩家的带入数从 carry-ai 子流抽取。
        // 档案来自更新的版本或人数没有点数表时，本局按关闭进行（store 为 null）。关闭时对局选项不变，与引入带入带出之前逐步相同。
        CarryProfileStore? store = null;
        if (profile is not null)
        {
            CarryIn? mine;
            try
            {
                (store, mine) = CarryTerminal.Prepare(profile, playerCount, options.ContentSet, input, output, render);
            }
            catch (PlayQuitException)
            {
                output.WriteLine();
                output.WriteLine($"已退出。种子 {seed}，用 --seed {seed} 可以重开这一局。");
                return 0;
            }

            if (store is not null)
            {
                options = options with { CarryInOut = true, CarryIns = CarryAi.ForHumanMatch(new GameSeed(seed), players, me, mine, options.ContentSet) };
            }
        }

        MatchFlow match = MatchFlow.Create(map, new GameSeed(seed), players, options);
        match.PairingSource = pairingSource ?? new Running.RandomPairingSource();

        // 本机玩家读到的公开快照：按座位投影（其他战场的棋子、信物、势力与手牌类型不可见，information-visibility）。
        Func<MatchPublicView> observe = () => match.PublishSeatProjection(me);
        // 对局标识：只用于在途记录的"同一局只结算一次"。种子 + 计时器读数（不参与任何对局随机）。
        string matchId = $"{seed:X16}-{Stopwatch.GetTimestamp():X}";
        CarryIn? myCarry = match.CarryIns.GetValueOrDefault(me);   // 征召签在建局时已解析出类型
        store?.Begin(matchId, myCarry);
        string? quitWarning = store is not null && myCarry is not null ? "退出将丢失带入的补给；弃赛可返还补给并带出 50%。" : null;
        CarryOutResult? settled = null;

        output.WriteLine();
        render.Line("══════════ 围杀 Siege · 终端对局 ══════════", ConsoleColor.Yellow);
        // 地图那一行：一律打印完整标识（retire-legacy-maps：旧地图族的缺省图 v5 随旧地图删除，"缺省图不多打一行"的例外随之删除）。
        // 完整地图标识取自公开视图（插旗阶段即公开）。生成图的标识里带地图种子：与下一行的对局种子分开显示，二者互相独立。
        string shownMapId = match.Publish().MapId;
        output.WriteLine($"地图 {shownMapId}（{map.BirthZones.Length} 块隔绝的出生棋盘）——用 --map {shownMapId} 可再得到同一张图；地图种子只决定地图，与下面的对局种子无关");

        // 专家（前瞻宽度 > 0）另写明实际生效的前瞻宽度；其余三档这一行与引入专家之前逐字相同。
        string lookahead = search.LookaheadWidth > 0 ? $"（前瞻宽度 {search.LookaheadWidth}）" : string.Empty;
        output.WriteLine($"种子 {seed}（用 --seed {seed} 可重开这一局）  你是玩家{seat}，对手 {playerCount - 1} 名 {difficulty} AI{lookahead}");
        output.WriteLine("目标：终局时势力最高。势力 = 你独占的空格数 + 你所有棋串的军势。");
        output.WriteLine("战场：前 3 个大回合各自在出生棋盘上构筑；第 3 大回合结束时独占一块棋盘的玩家被随机配对、棋盘之间长出公共棋盘连通；"
            + "一个战场里全员在一个大回合内都 Pass、或战场填满即结算，势力最高者胜、其余出局；两对都分出胜负后胜者链接打决赛。曾有势力而势力降到 0 同样出局。");

        output.WriteLine("围棋式提子：一批棋落下后，对手没有气的棋串被整串提走；你自己的棋串落完仍无气则整批不合法。");
        if (store is not null)
        {
            // 所有玩家的带入从插旗阶段起公开（information-visibility），取自公开视图。
            output.WriteLine(CarryTerminal.CarryListText(match.Publish().CarryIns, me));
            output.WriteLine("对局中输入 resign（或 弃赛）可弃赛：返还带入的补给，并按弃赛时的势力名次带出一半补给点（保护期内为 0）。");
        }

        output.WriteLine();

        try
        {
            int zone = ChooseZone(match, me, map, input, output, quitWarning);
            // 其余玩家的选区由 Core 的唯一实现给出（frontier-map D4 / board-link-mode B2）：盲选。
            ImmutableArray<(PlayerId Player, int Zone)> choices = match.PlantPrototype((me, zone));

            var runner = new MatchRunner(match);
            // retire-legacy-maps 3.3：地图专属评价权重覆盖已删除，入口未给权重即缺省表。
            EvaluationWeights aiWeights = weights ?? EvaluationWeights.Default;
            foreach (PlayerId p in players)
            {
                if (p == me)
                {
                    runner.SetController(p, new ConsoleController(me, observe, match.PublishSupplement, match.PreviewCurrentBatch, input, output, quitWarning));
                    continue;
                }

                HeuristicTurnController ai = HeuristicAi.Create(match, p, difficulty, aiWeights, search);
                onAi?.Invoke(p, ai);
                runner.SetController(p, ai);
            }

            // 盲选（match-setup「匿名同时插旗」）：锁定后只公开与自己同一块棋盘的玩家，不公开其他棋盘上有谁。
            PlayerId[] mates = [.. choices.Where(c => c.Zone == zone && c.Player != me).Select(c => c.Player)];
            output.WriteLine($"你锁定了 {BirthZoneLabel.Number(zone)} 号棋盘；" + (mates.Length == 0
                ? "这块棋盘上只有你。"
                : $"同一块棋盘上还有 {string.Join("、", mates.Select(p => BoardRenderer.Label(p, me)))}，你们就地开打。"));

            output.WriteLine($"第 1 大回合顺序随机：{string.Join(" > ", match.ActionOrder.Select(p => BoardRenderer.Label(p, me)))}");

            int pairings = 0;
            int links = 0;
            int settlements = 0;
            while (match.Phase == MatchPhase.InProgress)
            {
                PlayerId current = match.CurrentPlayer!.Value;
                int round = match.MajorRound;
                MatchPublicView before = observe();
                int linksBefore = match.Links.Count;   // 本小回合末尾可能发生拼接：Summarize 按它把拼接前的格平移到新画布（retire-legacy-maps 4b.1 / L15）
                // 行动者是否在本座位的战场里：在行动之前判定（本小回合结束时可能发生链接，之后再判会把刚展开的棋子算成这一手的落子）。
                bool visibleActor = match.BattlefieldOf(current) == match.BattlefieldOf(me);
                if (current != me)
                {
                    output.Write($"{BoardRenderer.Label(current, me)} 思考中… ");
                }

                bool resigned = false;
                try
                {
                    runner.RunTurn();
                }
                catch (PlayResignException)
                {
                    // 本机玩家在自己的小回合里弃赛：此时仍在该小回合内（阶段非 Idle），走 Resign 的"当前行动玩家任意阶段弃赛"分支。
                    match.Resign(me);
                    ResignationSnapshot snapshot = match.Resignations.Last(r => r.Player == me);
                    render.Line($"{BoardRenderer.Label(me, me)} 弃赛（弃赛时势力 {snapshot.Power}）。此后由 AI 继续把对局下完。", ConsoleColor.Red);
                    if (store is not null)
                    {
                        // 弃赛立即结算并写档（design.md D5）：弃赛时势力名次的点数一半；保护期内为 0；补给返还。之后的对局事件不再改变它。
                        settled = CarryOutSettlement.Resigned(players.Length, snapshot.RankAtResign!.Value, snapshot.MajorRound, myCarry);
                        store.Settle(matchId, settled);
                        render.Line(CarryTerminal.ResignText(settled), ConsoleColor.Yellow);
                    }

                    resigned = true;
                }

                MatchPublicView after = observe();
                if (!resigned)
                {
                    if (!visibleActor)
                    {
                        // 其他战场的行动不可见（information-visibility）：只报"已行动"。
                        render.Line($"{BoardRenderer.Label(current, me)} 在其他战场行动完毕。", ConsoleColor.DarkGray);
                    }
                    else
                    {
                        Summarize(before, after, current, me, output, render, MovedSince(match, linksBefore));
                    }
                }

                // 配对、链接与战场结算对所有玩家公开（battlefield / information-visibility：链接与结算是全局项）。
                for (; pairings < match.Pairings.Count; pairings++)
                {
                    PairingRecord p = match.Pairings[pairings];
                    render.Line($"── {(p.Kind == PairingKind.FirstRound ? "首轮配对" : "决赛链接")}：{string.Join("、", p.Pairs.Select(x => $"{BirthZoneLabel.Number(x.X)} 号与 {BirthZoneLabel.Number(x.Y)} 号战场"))}"
                        + (p.Waiter is { } w ? $"；{BirthZoneLabel.Number(w)} 号战场等待决赛" : string.Empty) + " ──", ConsoleColor.Yellow);
                }

                for (; links < match.Links.Count; links++)
                {
                    LinkRecord l = match.Links[links];
                    // 段 F 最小适配（F.3 再按平移向量改写）：拼接取代公共棋盘 / 通道。
                    render.Line($"   棋盘拼接：{BirthZoneLabel.Number(l.Y)} 号战场平移贴到 {BirthZoneLabel.Number(l.X)} 号战场{(l.Side == Siege.Core.Board.Maps.LinkSide.East ? "东" : "北")}侧，两个战场连通。", ConsoleColor.Yellow);
                }

                for (; settlements < match.Settlements.Count; settlements++)
                {
                    BattlefieldSettlementRecord s = match.Settlements[settlements];
                    render.Line($"── {BirthZoneLabel.Number(s.Battlefield)} 号战场结算：{BoardRenderer.Label(s.Winner, me)} 胜出"
                        + (s.Entries.Length > 1 ? $"，{string.Join("、", s.Entries.Skip(1).Select(e => BoardRenderer.Label(e.Player, me)))} 战场落败出局" : string.Empty) + " ──",
                        s.Entries.Any(e => e.Player == me) ? ConsoleColor.Red : ConsoleColor.Yellow);
                }

                if (store is not null && settled is null && match.StateOf(me).Status == Core.Scoring.PlayerStatus.Eliminated)
                {
                    // 出局立即结算（carry-in-out「出局结算」）：补给丢失、带出 0，之后的对局事件不再改变它。
                    settled = CarryOutSettlement.Eliminated(myCarry);
                    store.Settle(matchId, settled);
                    render.Line(CarryTerminal.EliminatedText(settled), ConsoleColor.Red);
                }

                if (match.Phase == MatchPhase.InProgress && match.MajorRound != round)
                {
                    render.Line($"── 第 {round} 大回合结束 ──", ConsoleColor.DarkYellow);
                }
            }

            MatchPublicView final = observe();
            output.WriteLine();
            render.Board(final, me);
            PrintResult(match.Result!, me, output, render);
            if (store is not null)
            {
                // 局终结算（只在带入带出开启时——implement 段 A 待决「入口只在开关开启时结算」）：弃赛 / 出局已在当时结算过，这里只补完赛者；结算只写一次。
                if (settled is null)
                {
                    settled = CarryOutSettlement.Settle(match.Result, match.Resignations, match.CarryIns, match.Players)[me];
                    store.Settle(matchId, settled);
                }

                render.Line(CarryTerminal.SettlementText(settled), ConsoleColor.Yellow);
                output.WriteLine(CarryTerminal.ProfileText(store.Current));
            }

            return 0;
        }
        catch (PlayQuitException)
        {
            output.WriteLine();
            output.WriteLine($"已退出。种子 {seed}，用 --seed {seed} 可以重开这一局。");
            return 0;
        }
    }

    /// <summary>retire-legacy-maps 3.6：终端只剩链接模式，旧模式的整图选区分支（画全图 + "选择你的出生区"）删除。</summary>
    private static int ChooseZone(MatchFlow match, PlayerId me, MapData map, TextReader input, TextWriter output, string? quitWarning)
    {
        // 盲选（match-setup「开局信息公开范围」）：只列 5 块出生棋盘的占位（位置与边长，取自按座位视图），不画棋盘内部布局。
        foreach (BoardPlaceholder p in match.PublishFor(me).Placeholders)
        {
            output.WriteLine($"  {BirthZoneLabel.Number(p.Zone)} 号棋盘：左下角 {p.Origin}，{p.Width}×{p.Height}");
        }

        output.WriteLine();
        while (true)
        {
            output.Write($"盲选出生棋盘：以上是 {map.BirthZones.Length} 块棋盘的占位（1–{map.BirthZones.Length}），看不到别人的旗，选中即锁定；前 3 个大回合只能在这块棋盘上落子 > ");
            string? line = input.ReadLine() ?? throw new PlayQuitException();
            line = line.Trim();
            if (line is "q" or "quit")
            {
                if (quitWarning is null || Confirm(quitWarning + "确认退出？(y/N) ", input, output))
                {
                    throw new PlayQuitException();
                }

                continue;
            }

            if (int.TryParse(line, out int z) && z >= 1 && z <= map.BirthZones.Length)
            {
                return z - 1;
            }
        }
    }

    /// <summary>二次确认（退出 / 弃赛）：只有 y / yes / 是 算确认；输入耗尽即退出。</summary>
    internal static bool Confirm(string prompt, TextReader input, TextWriter output)
    {
        output.Write(prompt);
        string answer = input.ReadLine() ?? throw new PlayQuitException();
        return answer.Trim() is "y" or "Y" or "yes" or "是";
    }

    /// <summary>
    /// 自 <paramref name="linksBefore"/> 次链接以来的坐标映射（拼接前盘面的格 → 拼接后坐标），经 Core 的唯一换算 <see cref="LinkLayout"/>；期间没有链接为 <c>null</c>（恒等）。
    /// 与跑局层 <c>MatchSession.RecordTurn</c>、图形版 <c>MatchSession.MovedSince</c> 同口径。
    /// </summary>
    private static Func<Coord, Coord>? MovedSince(MatchFlow match, int linksBefore)
    {
        if (match.Links.Count == linksBefore)
        {
            return null;
        }

        var layout = new LinkLayout(match.Board.BaseMap.Boards, match.Links.Select(l => l.ShiftStep));
        int to = layout.Versions;
        return c => layout.Translate(c, linksBefore, to);
    }

    /// <summary>
    /// 对比前后快照，说明这一小回合发生了什么。本小回合末尾发生了拼接时（<paramref name="moved"/> 非空），逐格比对的是拼接前的可落子格与它平移后的坐标——
    /// retire-legacy-maps 4b.1（L15）：原先拿拼接后画布的坐标去索引拼接前的盘面，画布变大即越界崩溃，未越界时也会把平移的棋子误报成落子 / 提子。
    /// </summary>
    private static void Summarize(MatchPublicView before, MatchPublicView after, PlayerId actor, PlayerId me, TextWriter output, BoardRenderer render, Func<Coord, Coord>? moved = null)
    {
        var placed = new List<string>();
        var lost = new Dictionary<PlayerId, int>();
        IEnumerable<Coord> cells = moved is null ? after.Board.AllCoords() : before.Board.AllCoords().Where(before.Board.Map.IsPlayable);
        foreach (Coord from in cells)
        {
            Coord c = moved?.Invoke(from) ?? from;
            Occupant? was = before.Board[from].Occupant;
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
            EndReason.LastBattlefieldSettled => "最后一个战场结算",
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
