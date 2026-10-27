using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Sim.Play;

/// <summary>终端棋盘与状态面板。只读公开快照 + 本人的暂放批次，不做任何规则计算。</summary>
internal sealed class BoardRenderer
{
    private static readonly ConsoleColor[] PlayerColors = [ConsoleColor.Cyan, ConsoleColor.Red, ConsoleColor.Green, ConsoleColor.Magenta];

    private readonly TextWriter _out;
    private readonly bool _color;

    public BoardRenderer(TextWriter output)
    {
        _out = output;
        _color = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected;
    }

    /// <summary>已活棋串的子：类型字母后的第三个字符（life-shape 3.3）。</summary>
    internal const char AliveMark = '@';

    /// <summary>禁入格：后跟活形所有者的玩家号（life-shape 3.3）。</summary>
    internal const char ForbiddenMark = 'x';

    /// <summary>
    /// 棋子字母（与盘面类型码同一套，more-pieces-relics D9：旗手 N、铁链 C、哨兵 T、界碑 K）。穷举全部类型，未知类型响亮失败——
    /// 兜底成 '?' 会让新类型在终端里静默显示成"未揭示信物"的符号（段 A 待决 4）。
    /// </summary>
    public static char Letter(PieceType type) => type switch
    {
        PieceType.Basic => 'B',
        PieceType.Fortress => 'F',
        PieceType.Line => 'L',
        PieceType.Multiplier => 'M',
        PieceType.Synergy => 'S',
        PieceType.Artisan => 'A',
        PieceType.Bannerman => 'N',
        PieceType.Chain => 'C',
        PieceType.Sentry => 'T',
        PieceType.Boundary => 'K',
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。"),
    };

    public static string Name(PieceType type) => type switch
    {
        PieceType.Basic => "普通",
        PieceType.Fortress => "堡垒",
        PieceType.Line => "连珠",
        PieceType.Multiplier => "倍增",
        PieceType.Synergy => "协同",
        PieceType.Artisan => "匠人",
        PieceType.Bannerman => "旗手",
        PieceType.Chain => "铁链",
        PieceType.Sentry => "哨兵",
        PieceType.Boundary => "界碑",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。"),
    };

    public static bool TryParseType(string text, out PieceType type)
    {
        type = default;
        switch (text.Trim().ToUpperInvariant())
        {
            case "B" or "普" or "普通": type = PieceType.Basic; return true;
            case "F" or "堡" or "堡垒": type = PieceType.Fortress; return true;
            case "L" or "连" or "连珠": type = PieceType.Line; return true;
            case "M" or "倍" or "倍增": type = PieceType.Multiplier; return true;
            case "S" or "协" or "协同": type = PieceType.Synergy; return true;
            case "A" or "匠" or "匠人": type = PieceType.Artisan; return true;
            case "N" or "旗" or "旗手": type = PieceType.Bannerman; return true;
            case "C" or "链" or "铁链": type = PieceType.Chain; return true;
            case "T" or "哨" or "哨兵": type = PieceType.Sentry; return true;
            case "K" or "碑" or "界碑": type = PieceType.Boundary; return true;
            default: return false;
        }
    }

    /// <summary>
    /// 信物在盘面上的字母（小写，与棋子的大写类型码分开；relic-redesign 接线段 W1 的终端最小版本）：战鼓 d、号令 o、望台 w、百变 v、军旗 g。
    /// 空格上的地图信物与预告格画在格子中间；挂在棋子上的信物在棋串后面单列（见 <see cref="Board"/> 的信物行）。
    /// </summary>
    internal static char RelicLetter(RelicKind kind) => kind switch
    {
        RelicKind.WarDrum => 'd',
        RelicKind.Command => 'o',
        RelicKind.Watchtower => 'w',
        RelicKind.Chameleon => 'v',
        RelicKind.Banner => 'g',
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知信物种类。"),
    };

    /// <summary>改造目标的文案：动作 + 目标格 / 边。</summary>
    internal static string EditText(TerrainEdit edit) =>
        $"{TerrainEdit.DisplayName(edit.Kind)} {(edit.Kind == TerrainEditKind.Fence ? edit.Edge.ToString() : edit.Cell.ToNotation())}";

    /// <summary>画棋盘。<paramref name="batch"/> 非空时叠加暂放棋子并标出合法空格；<paramref name="zones"/> 为插旗阶段显示出生区编号。</summary>
    public void Board(MatchPublicView view, PlayerId me, StagedBatch? batch = null, bool zones = false)
    {
        GameBoard board = view.Board;
        MapData map = board.Map;
        ImmutableDictionary<Coord, RelicToken> relics = board.RelicsOnBoard().Where(r => board[r.Cell].Occupant is null).ToImmutableDictionary(r => r.Cell, r => r.Token);
        ImmutableDictionary<Coord, RelicKind> forecast = view.RelicForecast is { } f ? f.Slots.ToImmutableDictionary(s => s.Cell, s => s.Kind) : ImmutableDictionary<Coord, RelicKind>.Empty;
        ImmutableDictionary<Coord, PieceType> staged = batch is null
            ? ImmutableDictionary<Coord, PieceType>.Empty
            : batch.Placements.ToImmutableDictionary(p => p.Coord, p => p.Type);
        IReadOnlySet<Coord>? legal = batch?.Context.LegalRange;

        // life-shape 3.3：活形状态与禁入格只读公开视图里那一份全量分析（与盘面同一时刻），终端不调 Analyze、不判活形。
        // 禁入格直接取 ForbiddenCellsFor(me)（裁决 R12：读取放开，扣除只在 LegalRangeFor），所有者取该格所在眼空间的所有者；
        // 自己的眼本就不在其中（所有者可自拆）。
        LifeShapeReport life = view.LifeShape;
        ImmutableDictionary<Coord, PlayerId> forbidden = life.ForbiddenCellsFor(me)
            .ToImmutableDictionary(c => c, c => life.EyeSpaceAt(c)!.Owner);

        // 锁定后画本座位战场（open-map 起 = 整张地图）的外接矩形；插旗阶段只画各块出生平台的占位（编号），不画棋盘内部布局（盲选），取景仍是整座岛（含低地）。
        (int x0, int y0, int x1, int y1, IReadOnlySet<Coord>? shown, bool placeholders) = Viewport(view, me, zones);
        WriteColumns(x0, x1);
        for (int y = y1; y >= y0; y--)
        {
            _out.Write($"{y + 1,3} ");
            for (int x = x0; x <= x1; x++)
            {
                var c = new Coord(x, y);
                Cell cell = board[c];
                if (placeholders)
                {
                    // 占位画整块出生棋盘（棋盘清单），不画出生区——链接档的出生区是核心区（link-map-terrain D1）。
                    if (map.BirthBoardAt(c) is { } placeholder)
                    {
                        // 每格恰 3 个字符：浮空岛的平台可达 12 块，两位编号右对齐成"10 "（原" 10 "占 4 格，把整行挤歪）。
                        Ink($"{BirthZoneLabel.Number(placeholder),2} ", ZoneColor);
                    }
                    else
                    {
                        _out.Write("   ");
                    }
                }
                else if (shown is not null && !shown.Contains(c))
                {
                    _out.Write("   ");
                }
                else if (cell.Terrain == Terrain.Obstacle)
                {
                    // 不可落子格有两种：岩石与未架桥深水，只读地表区分（terrain-model 裁决 A-2），不做任何规则计算。
                    if (map.SurfaceAt(c) == Surface.DeepWater)
                    {
                        Ink(" ~ ", ConsoleColor.DarkBlue);
                    }
                    else
                    {
                        Ink(" # ", ConsoleColor.DarkGray);
                    }
                }
                else if (staged.TryGetValue(c, out PieceType st))
                {
                    Ink($"*{Letter(st)} ", ConsoleColor.Yellow);
                }
                else if (cell.Occupant is { } o)
                {
                    char alive = life.GroupLifeAt(c)?.Life == LifeState.Alive ? AliveMark : ' ';
                    string glyph = $"{o.Owner.Value + 1}{Letter(o.Type)}{alive}";
                    Ink(glyph, ColorOf(o.Owner), bright: o.Owner == me);
                }
                else if (relics.TryGetValue(c, out RelicToken relic))
                {
                    // 裁决 R13：信物排在禁入格之前（信物更少见、信息量更大）；被盖住的禁入格在图例下方单列一行。
                    Ink($" {RelicLetter(relic.Kind)} ", ConsoleColor.DarkYellow);
                }
                else if (forecast.TryGetValue(c, out RelicKind coming))
                {
                    Ink($"!{RelicLetter(coming)} ", ConsoleColor.DarkYellow);
                }
                else if (forbidden.TryGetValue(c, out PlayerId lifeOwner))
                {
                    // 禁入格排在区号、合法空格之前：它回答的是"这里能不能落"（与预演第 1 步"禁入先于范围"同序）。
                    Ink($"{ForbiddenMark}{lifeOwner.Value + 1} ", ColorOf(lifeOwner));
                }
                else if (zones && map.BirthZoneOf(c) is { } z)
                {
                    Ink($"{BirthZoneLabel.Number(z),2} ", ZoneColor);
                }
                else if (legal is not null && legal.Contains(c))
                {
                    Ink(" + ", ConsoleColor.DarkGreen);
                }
                else
                {
                    Ink(" . ", ConsoleColor.Gray);
                }
            }

            _out.WriteLine($" {y + 1}");
        }

        WriteColumns(x0, x1);
        _out.WriteLine("  图例：1B=玩家1的普通子  B普通 F堡垒 L连珠 M倍增 S协同  *=你暂放  +=可落子  #=岩石  ~=深水");
        _out.WriteLine($"        {AliveMark}=已活棋串(如 1B{AliveMark})  {ForbiddenMark}N=玩家N活形的眼(你禁入)");

        // 图例按对局内容集（公开、开局固定）：v2 另列新四种棋子。
        ContentSet legacy = ContentSets.Legacy;
        PieceType[] newPieces = [.. ContentSets.PieceTypesOf(view.ContentSet).Except(ContentSets.PieceTypesOf(legacy))];
        if (newPieces.Length > 0)
        {
            _out.WriteLine("        新棋子（只提供位置加值）：" + string.Join(" ", newPieces.Select(t => $"{Letter(t)}{Name(t)}")));
        }

        _out.WriteLine("        信物（空格上即地图信物，落子拾取）：" + string.Join(" ", RelicKinds.Order.Select(k => $"{RelicLetter(k)}{k.DisplayName()}"))
            + "  !x=预告下一批落位");
        Coord[] covered = [.. forbidden.Keys.Where(relics.ContainsKey).Order()];
        if (covered.Length > 0)
        {
            _out.WriteLine("        信物格同为禁入：" + string.Join("  ", covered.Select(c => $"{c.ToNotation()}({ForbiddenMark}{forbidden[c].Value + 1})")));
        }
    }

    /// <summary>
    /// 画哪一块：插旗阶段为整张岛全部可落子格的外接矩形（map-expand-25 段 B：平台之间与岛缘的低地也在取景内，只画占位），此后为本座位所在战场的外接矩形，<c>shown</c> 为该战场的格
    /// （浮空岛的唯一战场即全部可落子格，含低地）。战场取自公开视图里的地形与本座位锁定的出生棋盘（<see cref="PublicRules.BattlefieldOf"/>），不做规则计算。
    /// </summary>
    private static (int X0, int Y0, int X1, int Y1, IReadOnlySet<Coord>? Shown, bool Placeholders) Viewport(MatchPublicView view, PlayerId me, bool zones)
    {
        MapData map = view.Board.Map;
        int? zone = view.Players.FirstOrDefault(p => p.Player == me)?.BirthZone;
        Coord[] cells;
        IReadOnlySet<Coord>? shown;
        if (zones || zone is null)
        {
            // 原为各出生棋盘的格之并：浮空岛的平台距岛边 ≥ 2 格，按平台取景会把岛缘低地切掉。
            cells = [.. map.AllCoords().Where(map.IsPlayable)];
            shown = null;
        }
        else
        {
            Battlefield field = PublicRules.BattlefieldOf(view.Board, zone.Value);
            cells = [.. field.Cells];
            shown = field.Cells.ToHashSet();
        }

        return (cells.Min(c => c.X), cells.Min(c => c.Y), cells.Max(c => c.X), cells.Max(c => c.Y), shown, zones || zone is null);
    }

    /// <summary>对局状态：大回合、行动顺序、各家势力与公开手牌类型、已揭示信物。</summary>
    public void Status(MatchPublicView view, PlayerId me)
    {
        string order = string.Join(" > ", view.ActionOrder.Select(p => Label(p, me)));
        _out.WriteLine($"第 {view.MajorRound} 大回合    行动顺序：{order}    连续 Pass：{view.PassStreak}    {Siege.Presentation.Text.Labels.RoundCapStatus(view.RoundCap, view.MajorRound)}");
        if (view.MajorRound <= MatchFlow.BuildProtectionRounds)
        {
            _out.WriteLine($"  构筑保护期（第 1–{MatchFlow.BuildProtectionRounds} 大回合）：只能在自己的出生棋盘落子");
        }

        foreach (PlayerFlowState state in view.Players)
        {
            PlayerPower? detail = view.Power?.Players.FirstOrDefault(p => p.Player == state.Player);
            int rank = view.Power?.Ranking.FirstOrDefault(g => g.Players.Contains(state.Player))?.Rank ?? 0;
            HandPublicView? hand = view.Hands.FirstOrDefault(h => h.Player == state.Player);
            string types = hand is null || hand.IsEmpty ? "无" : string.Join("", hand.Types.Select(Letter));
            string status = state.Status == PlayerStatus.Active ? "" : $"  [{state.Status}]";
            Ink($"  {Label(state.Player, me),-8}", ColorOf(state.Player), bright: state.Player == me);
            _out.WriteLine($" {PowerText(detail)}  名次 {rank}  手牌类型 {types}{status}");
        }

        // 挂在棋子上的信物（持有者 = 该格棋子的主人）与预告：信物层全公开（relic-redesign R22）。
        List<BoardRelic> held = [.. view.Board.RelicsOnBoard().Where(r => view.Board[r.Cell].Occupant is not null)];
        if (held.Count > 0)
        {
            _out.WriteLine("  持物棋子：" + string.Join("  ", held.Select(r =>
                $"{r.Cell.ToNotation()}{r.Token.Kind.DisplayName()}→{Label(view.Board[r.Cell].Occupant!.Value.Owner, me)}")));
        }

        if (view.RelicForecast is { IsEmpty: false } forecast)
        {
            _out.WriteLine($"  信物预告（第 {forecast.LandingRound} 大回合开始时落位）：" + string.Join("  ", forecast.Slots.Select(s => $"{s.Cell.ToNotation()}{s.Kind.DisplayName()}")));
        }
    }

    /// <summary>
    /// 势力栏：总势力拆成"领地 + 棋串"（restore-go-core-rules 段 E，tasks 5.3），两项都取自 Core 势力明细。
    /// ≥ 10^6 用 <see cref="PowerNotation.Compact"/> 缩写（与图形版同一份），不封顶的军势不会把一行撑到换行；精确值见终局名次与预演。不定宽对齐——定宽遇到大数只会把列撑歪。
    /// </summary>
    internal static string PowerText(PlayerPower? detail) => detail is null
        ? "势力 0（领地 0 + 棋串 0）"
        : $"势力 {PowerNotation.Compact(detail.Total)}（领地 {detail.TerritoryScore} + 棋串 {PowerNotation.Compact(detail.GroupScore)}）";

    public static string Label(PlayerId p, PlayerId me) => p == me ? $"玩家{p.Value + 1}(你)" : $"玩家{p.Value + 1}";

    public void Ink(string text, ConsoleColor color, bool bright = false)
    {
        if (!_color)
        {
            _out.Write(text);
            return;
        }

        ConsoleColor old = Console.ForegroundColor;
        Console.ForegroundColor = bright ? Brighten(color) : color;
        _out.Write(text);
        Console.ForegroundColor = old;
    }

    public void Line(string text, ConsoleColor color)
    {
        Ink(text, color);
        _out.WriteLine();
    }

    internal static ConsoleColor ColorOf(PlayerId p) => PlayerColors[p.Value % PlayerColors.Length];

    /// <summary>
    /// 插旗阶段的区号底色：一律中性色，与区号无关、也不借用玩家色（frontier-map D9）。
    /// 区号只在插旗阶段显示，此时还没有任何平台有主人；出生区可以多于玩家（边疆档 5–8 个），"第 z 区 = 第 z 名玩家的颜色"不成立。
    /// </summary>
    internal const ConsoleColor ZoneColor = ConsoleColor.White;

    private static ConsoleColor Brighten(ConsoleColor c) => c switch
    {
        ConsoleColor.DarkCyan => ConsoleColor.Cyan,
        _ => ConsoleColor.White,
    };

    private void WriteColumns(int x0, int x1)
    {
        _out.Write("    ");
        for (int x = x0; x <= x1; x++)
        {
            // 每列三个字符宽：单字母列标后补一个空格，双字母列标（第 26 列起）正好占满。
            _out.Write($" {new Coord(x, 0).Column,-2}");
        }

        _out.WriteLine();
    }
}
