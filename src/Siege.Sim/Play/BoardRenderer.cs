using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
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

    public static char Letter(PieceType type) => type switch
    {
        PieceType.Basic => 'B',
        PieceType.Fortress => 'F',
        PieceType.Line => 'L',
        PieceType.Multiplier => 'M',
        PieceType.Synergy => 'S',
        PieceType.Artisan => 'A',
        _ => '?',
    };

    public static string Name(PieceType type) => type switch
    {
        PieceType.Basic => "普通",
        PieceType.Fortress => "堡垒",
        PieceType.Line => "连珠",
        PieceType.Multiplier => "倍增",
        PieceType.Synergy => "协同",
        PieceType.Artisan => "匠人",
        _ => type.ToString(),
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
            default: return false;
        }
    }

    public static string RelicName(RelicType type) => type switch
    {
        RelicType.Prospecting => "探勘(展示+)",
        RelicType.Conscription => "征召(选取+)",
        RelicType.Depot => "兵站(槽位+)",
        RelicType.Command => "军令(部署+)",
        RelicType.Vanguard => "先锋(先手+)",
        RelicType.SchoolEmblem => "徽记",
        _ => type.ToString(),
    };

    private static char RelicLetter(RelicType type) => type switch
    {
        RelicType.Prospecting => 'p',
        RelicType.Conscription => 'c',
        RelicType.Depot => 'd',
        RelicType.Command => 'o',
        RelicType.Vanguard => 'v',
        RelicType.SchoolEmblem => 'e',
        _ => '?',
    };

    /// <summary>画棋盘。<paramref name="batch"/> 非空时叠加暂放棋子并标出合法空格；<paramref name="zones"/> 为插旗阶段显示出生区编号。</summary>
    public void Board(MatchPublicView view, PlayerId me, StagedBatch? batch = null, bool zones = false)
    {
        GameBoard board = view.Board;
        MapData map = board.Map;
        ImmutableDictionary<Coord, RelicPublicState> relics = view.Relics.ToImmutableDictionary(r => r.Coord);
        ImmutableDictionary<Coord, PieceType> staged = batch is null
            ? ImmutableDictionary<Coord, PieceType>.Empty
            : batch.Placements.ToImmutableDictionary(p => p.Coord, p => p.Type);
        IReadOnlySet<Coord>? legal = batch?.Context.LegalRange;

        WriteColumns(map.Width);
        for (int y = map.Height - 1; y >= 0; y--)
        {
            _out.Write($"{y + 1,3} ");
            for (int x = 0; x < map.Width; x++)
            {
                var c = new Coord(x, y);
                Cell cell = board[c];
                if (cell.Terrain == Terrain.Obstacle)
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
                    string glyph = $"{o.Owner.Value + 1}{Letter(o.Type)} ";
                    Ink(glyph, ColorOf(o.Owner), bright: o.Owner == me);
                }
                else if (relics.TryGetValue(c, out RelicPublicState? relic))
                {
                    if (relic.IsRevealed && relic.Content is { } content)
                    {
                        Ink($" {RelicLetter(content.Type)} ", ConsoleColor.DarkYellow);
                    }
                    else
                    {
                        Ink(" ? ", ConsoleColor.DarkYellow);
                    }
                }
                else if (zones && map.BirthZoneOf(c) is { } z)
                {
                    Ink($" {BirthZoneLabel.Number(z)} ", ZoneColor);
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

        WriteColumns(map.Width);
        _out.WriteLine("  图例：1B=玩家1的普通子  B普通 F堡垒 L连珠 M倍增 S协同  *=你暂放  +=可落子  ?=未揭示信物  #=岩石  ~=深水");
        _out.WriteLine("        已揭示信物：p探勘 c征召 d兵站 o军令 v先锋 e徽记");
    }

    /// <summary>对局状态：大回合、行动顺序、各家势力与公开手牌类型、已揭示信物。</summary>
    public void Status(MatchPublicView view, PlayerId me)
    {
        string order = string.Join(" > ", view.ActionOrder.Select(p => Label(p, me)));
        _out.WriteLine($"第 {view.MajorRound} 大回合    行动顺序：{order}    连续 Pass：{view.PassStreak}");
        if (view.MajorRound <= MatchFlow.BuildProtectionRounds)
        {
            _out.WriteLine($"  构筑保护期（第 1–{MatchFlow.BuildProtectionRounds} 大回合）：只能在自己的出生区落子");
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

        var owned = view.Relics.Where(r => r.IsRevealed && r.Content is not null).ToList();
        if (owned.Count > 0)
        {
            _out.WriteLine("  已揭示信物：" + string.Join("  ", owned.Select(r =>
                $"{r.Coord.ToNotation()}{RelicName(r.Content!.Value.Type)}+{r.Content.Value.Magnitude}" +
                (r.Control.Holder is { } h ? $"→{Label(h, me)}" : r.Control.Kind == RelicControlKind.Contested ? "→争夺中" : ""))));
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

    private void WriteColumns(int width)
    {
        _out.Write("    ");
        for (int x = 0; x < width; x++)
        {
            _out.Write($" {Coord.ColumnLetters[x]} ");
        }

        _out.WriteLine();
    }
}
