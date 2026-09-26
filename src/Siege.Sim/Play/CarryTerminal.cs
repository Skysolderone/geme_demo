using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Carry;

namespace Siege.Sim.Play;

/// <summary>
/// 终端的带入带出界面（carry-in-out「终端的带入选择、弃赛与结算显示」）：插旗前读档、处理残留在途记录、兑换与选择带入；
/// 以及带入列表、弃赛 / 出局 / 局终结算与档案概览的文本。规则与档案读写都在 Core（<see cref="CarryProfileStore"/>、<see cref="CarryOutSettlement"/>），这里只做输入输出。
/// </summary>
internal static class CarryTerminal
{
    /// <summary>
    /// 插旗之前：读档并提示（新建 / 损坏重置 / 版本更新）；有残留在途记录先按中途退出结算并提示；然后显示档案，允许兑换、带入或不带入。
    /// 返回可用的存取与本机玩家的带入；档案来自更新的版本、或人数没有点数表时返回 <c>(null, null)</c>，本局按关闭带入带出进行。
    /// 输入 <c>q</c> 或输入耗尽抛 <see cref="PlayQuitException"/>（此时还没有建局，没有可丢的补给）。
    /// </summary>
    internal static (CarryProfileStore? Store, CarryIn? Carry) Prepare(
        CarryProfileStore store, int playerCount, ContentSet contentSet, TextReader input, TextWriter output, BoardRenderer render)
    {
        output.WriteLine();
        render.Line("══════════ 围杀 Siege · 补给 ══════════", ConsoleColor.Yellow);
        CarryProfileLoad load = store.Load();
        render.Line(load.Message, load.Status is CarryProfileLoadStatus.Loaded or CarryProfileLoadStatus.Created ? ConsoleColor.Gray : ConsoleColor.Red);
        if (load.Status == CarryProfileLoadStatus.NewerVersion)
        {
            return (null, null);
        }

        if (!CarryPoints.Supports(playerCount))
        {
            render.Line($"{playerCount} 人局没有名次补给点表，本局不提供带入、局终不结算。", ConsoleColor.Red);
            return (null, null);
        }

        if (store.SettleAbandoned() is { } abandoned)
        {
            string lost = abandoned.Carry is { } c ? $"{Name(c.Kind)}已丢失" : "上一局未带入补给";
            render.Line($"上一局中途退出：{lost}，补给点不变。", ConsoleColor.Red);
        }

        output.WriteLine("补给只改开局手牌，每局至多带入 1 件；局终按名次带出补给点（4 人局 24 / 16 / 12 / 10），弃赛带出一半并返还补给，出局补给丢失。");
        foreach (SupplyKind kind in Supplies.Order)
        {
            output.WriteLine($"  [{Number(kind)}] {Name(kind)}  价 {Supplies.PriceOf(kind)}  {Effect(kind)}");
        }

        while (true)
        {
            output.WriteLine(ProfileText(store.Current));
            ImmutableArray<SupplyKind> carriable = store.Current.Carriable;
            string options = carriable.IsEmpty ? "无（库存为空）" : string.Join(" ", carriable.Select(k => $"{Number(k)} {Name(k)}"));
            output.Write($"可带入：{options}。输入编号带入，buy 编号 兑换，直接回车不带入 > ");
            string line = ReadLine(input);
            if (line.Length == 0 || line == "0")
            {
                return (store, null);
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] is "buy" or "兑换")
            {
                if (parts.Length == 2 && TryKind(parts[1], out SupplyKind bought))
                {
                    output.WriteLine(store.TryExchange(bought, out string? refusal) ? $"已兑换 1 件{Name(bought)}。" : $"兑换失败：{refusal}。");
                }
                else
                {
                    output.WriteLine("兑换请输入 buy 1 / buy 2 / buy 3。");
                }

                continue;
            }

            if (parts.Length != 1 || !TryKind(parts[0], out SupplyKind kind))
            {
                output.WriteLine("看不懂这个输入：带入输入编号，兑换输入 buy 编号，不带入直接回车。");
                continue;
            }

            if (store.Current.StockOf(kind) < 1)
            {
                output.WriteLine($"没有{Name(kind)}库存，先用 buy {Number(kind)} 兑换。");
                continue;
            }

            if (kind != SupplyKind.Commission)
            {
                return (store, new CarryIn(kind));
            }

            if (ChooseType(contentSet, input, output) is { } type)
            {
                return (store, new CarryIn(kind, type));
            }
        }
    }

    /// <summary>全部玩家的带入（公开）：<c>带入：你：换型令 → 连珠子；玩家2：征召签 → 堡垒子；……</c>，无人带入时 <c>带入：全员不带入</c>。</summary>
    internal static string CarryListText(IReadOnlyDictionary<PlayerId, CarryIn> carries, PlayerId me) =>
        carries.Count == 0
            ? "带入：全员不带入"
            : "带入：" + string.Join("；", carries.OrderBy(kv => kv.Key).Select(kv => $"{(kv.Key == me ? "你" : $"玩家{kv.Key.Value + 1}")}：{CarryText(kv.Value)}"));

    /// <summary>弃赛当时的结算行：<c>弃赛：弃赛名次第 2，带出 8，换型令已返还</c>。</summary>
    internal static string ResignText(CarryOutResult result) =>
        $"弃赛：弃赛名次第 {result.Rank}，带出 {result.Points}，{ItemText(result)}" + (result.Points == 0 ? "（构筑保护期内弃赛不带出补给点）" : string.Empty);

    /// <summary>出局当时的结算行：<c>出局：带出 0，换型令已丢失</c>。</summary>
    internal static string EliminatedText(CarryOutResult result) => $"出局：带出 0，{ItemText(result)}";

    /// <summary>局终打印的本机结算：结局类别 · 所用名次 · 带出补给点 · 补给是否返还。</summary>
    internal static string SettlementText(CarryOutResult result) => "结算：" + result.Outcome switch
    {
        CarryOutcome.Finished => $"完赛 · 第 {result.Rank} 名 · 带出 {result.Points} · {ItemText(result)}",
        CarryOutcome.Resigned => $"弃赛 · 弃赛名次第 {result.Rank} 名 · 带出 {result.Points} · {ItemText(result)}",
        CarryOutcome.Eliminated => $"出局 · 带出 0 · {ItemText(result)}",
        _ => "未结算",
    };

    /// <summary>档案概览：<c>补给点 17   库存 备用子×1  征召签×0  换型令×2</c>。</summary>
    internal static string ProfileText(CarryProfile profile) =>
        $"补给点 {profile.Points}   库存 {string.Join("  ", Supplies.Order.Select(k => $"{Name(k)}×{profile.StockOf(k)}"))}";

    internal static string Name(SupplyKind kind) => kind switch
    {
        SupplyKind.SpareStone => "备用子",
        SupplyKind.DraftLot => "征召签",
        SupplyKind.Commission => "换型令",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知补给种类。"),
    };

    private static string Effect(SupplyKind kind) => kind switch
    {
        SupplyKind.SpareStone => "开局多 1 枚普通子（共 6 枚）",
        SupplyKind.DraftLot => "开局 1 枚普通子换成随机类型（按基础权重抽取）",
        SupplyKind.Commission => "开局 1 枚普通子换成指定类型",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知补给种类。"),
    };

    private static string CarryText(CarryIn carry) =>
        carry.Type is { } type ? $"{Name(carry.Kind)} → {BoardRenderer.Name(type)}子" : Name(carry.Kind);

    private static string ItemText(CarryOutResult result) => result.CarryIn is not { } carry
        ? "未带入补给"
        : result.Returned ? $"{Name(carry.Kind)}已返还"
        : result.Outcome == CarryOutcome.Finished ? "补给已消耗"
        : $"{Name(carry.Kind)}已丢失";

    private static PieceType? ChooseType(ContentSet contentSet, TextReader input, TextWriter output)
    {
        ImmutableArray<CarryCandidate> candidates = CarryCandidates.Of(contentSet);
        while (true)
        {
            output.Write($"换型令指定类型：{string.Join(" ", candidates.Select((c, i) => $"[{i + 1}] {BoardRenderer.Name(c.Type)}子"))}，直接回车取消 > ");
            string line = ReadLine(input);
            if (line.Length == 0)
            {
                return null;
            }

            if (int.TryParse(line, out int n) && n >= 1 && n <= candidates.Length)
            {
                return candidates[n - 1].Type;
            }
        }
    }

    private static int Number(SupplyKind kind) => Supplies.Order.IndexOf(kind) + 1;

    private static bool TryKind(string text, out SupplyKind kind)
    {
        bool ok = int.TryParse(text, out int n) && n >= 1 && n <= Supplies.Order.Length;
        kind = ok ? Supplies.Order[n - 1] : default;
        return ok;
    }

    private static string ReadLine(TextReader input)
    {
        string line = (input.ReadLine() ?? throw new PlayQuitException()).Trim();
        return line is "q" or "quit" or "exit" ? throw new PlayQuitException() : line;
    }
}
