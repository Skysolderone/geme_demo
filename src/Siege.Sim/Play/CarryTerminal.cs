using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.PieceRelics;
using Siege.Presentation.Text;

namespace Siege.Sim.Play;

/// <summary>
/// 终端的带入带出界面（relic-redesign 接线段 W2，D8）：插旗前读档、处理残留在途记录、兑换与选择带入清单（选件、勾保险）；
/// 以及结算与档案概览的文本。规则与档案读写都在 Core（<see cref="RelicProfileStore"/>、<c>MatchFlow.SettleCarried</c> / <c>EarlySettlementFor</c>），这里只做输入输出。
/// </summary>
internal static class CarryTerminal
{
    /// <summary>
    /// 插旗之前：读档并提示（新建 / 损坏重置 / 版本更新 / v1 迁移）；有残留在途记录先按中途退出结算（只回保险件）并提示；然后显示档案，允许兑换、选择带入或不带入。
    /// 返回可用的存取与本机玩家的带入清单；档案来自更新的版本、或人数没有点数表时返回 <c>(null, 空清单)</c>，本局按关闭带入带出进行。
    /// 输入 <c>q</c> 或输入耗尽抛 <see cref="PlayQuitException"/>（此时还没有建局，没有可丢的信物）。
    /// </summary>
    internal static (RelicProfileStore? Store, RelicLoadout Loadout) Prepare(
        RelicProfileStore store, int playerCount, TextReader input, TextWriter output, BoardRenderer render)
    {
        output.WriteLine();
        render.Line("══════════ 围杀 Siege · 信物带入 ══════════", ConsoleColor.Yellow);
        RelicProfileLoad load = store.Load();
        render.Line(load.Message, load.Status is RelicProfileLoadStatus.Loaded or RelicProfileLoadStatus.Created ? ConsoleColor.Gray : ConsoleColor.Red);
        if (load.Status == RelicProfileLoadStatus.NewerVersion)
        {
            return (null, RelicLoadout.Empty);
        }

        if (!RelicPoints.Supports(playerCount))
        {
            render.Line($"{playerCount} 人局没有名次补给点表，本局不提供带入、局终不结算。", ConsoleColor.Red);
            return (null, RelicLoadout.Empty);
        }

        if (store.SettleAbandoned() is { } abandoned)
        {
            CarriedRelic[] returned = [.. abandoned.Carried.Where(c => c.Insured)];
            string back = returned.Length == 0 ? "没有保险件" : $"保险件 {string.Join("、", returned.Select(c => c.Kind.DisplayName()))} 已归还";
            render.Line($"上一局中途退出：{back}，其余带入件丢失，补给点不变。", ConsoleColor.Red);
        }

        output.WriteLine($"每局至多带入 {RelicKinds.MaxCarried} 件信物，其中至多 {RelicKinds.MaxInsured} 件勾为保险；部署时挂到新落的棋子上才生效，挂上即公开。");
        output.WriteLine("完赛（任何名次）：带入的与终局挂在你棋子上的信物都带出；出局 / 弃赛 / 中途退出只带回保险件。弃赛带出一半补给点（保护期内 0），出局 0。");
        output.WriteLine($"普通信物 {RelicKinds.UncalibratedExchangePrice} 补给点兑换 1 件；军旗是稀有信物，只能局内带出。");
        foreach (RelicKind kind in RelicKinds.Order)
        {
            output.WriteLine($"  [{Number(kind)}] {kind.DisplayName()}  {CarryTexts.Effect(kind)}");
        }

        while (true)
        {
            output.WriteLine(ProfileText(store.Current));
            output.Write("输入要带入的编号（空格分隔，编号后加 ! 勾为保险，如 1! 3 3 5!），buy 编号 兑换，直接回车不带入 > ");
            string line = ReadLine(input);
            if (line.Length == 0 || line == "0")
            {
                return (store, RelicLoadout.Empty);
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] is "buy" or "兑换")
            {
                if (parts.Length == 2 && TryKind(parts[1], out RelicKind bought))
                {
                    output.WriteLine(store.TryExchange(bought, out string? refusal) ? $"已兑换 1 件{bought.DisplayName()}。" : $"兑换失败：{refusal}。");
                }
                else
                {
                    output.WriteLine($"兑换请输入 buy 1 – buy {RelicKinds.Order.Length}。");
                }

                continue;
            }

            if (ParseLoadout(parts) is not { } loadout)
            {
                output.WriteLine("看不懂这个输入：带入输入编号（加 ! 勾保险），兑换输入 buy 编号，不带入直接回车。");
                continue;
            }

            if (loadout.RefusalAgainst(store.Current) is { } refused)
            {
                output.WriteLine($"不能这样带入：{refused}。");
                continue;
            }

            return (store, loadout);
        }
    }

    /// <summary>本机玩家自己的带入清单（私有，R22：只有本人看得到）。</summary>
    internal static string LoadoutText(RelicLoadout loadout) => CarryTexts.Loadout(loadout) + "（只有你看得到，挂出后公开）";

    /// <summary>中途（弃赛 / 出局）或局终的本机结算行：<c>结算：</c> + <see cref="CarryTexts.Settlement"/>。</summary>
    internal static string SettlementText(RelicPlayerSettlement result, PlayerId me) => "结算：" + CarryTexts.Settlement(result, p => BoardRenderer.Label(p, me));

    /// <summary>局终每件信物的去向（<see cref="CarryTexts.Destinations"/>）。</summary>
    internal static IReadOnlyList<string> DestinationLines(RelicMatchSettlement settlement, PlayerId me) =>
        CarryTexts.Destinations(settlement, p => BoardRenderer.Label(p, me));

    /// <summary>档案概览（<see cref="CarryTexts.Stock"/>）。</summary>
    internal static string ProfileText(RelicProfile profile) => CarryTexts.Stock(profile);

    /// <summary>解析一行带入：每个词是编号（1 起），后缀 <c>!</c> 勾保险。不合形返回 <c>null</c>（件数 / 保险 / 库存的规则校验交给 <see cref="RelicLoadout"/>）。</summary>
    internal static RelicLoadout? ParseLoadout(IEnumerable<string> words)
    {
        ImmutableArray<CarriedRelic>.Builder items = ImmutableArray.CreateBuilder<CarriedRelic>();
        foreach (string word in words)
        {
            bool insured = word.EndsWith('!') || word.EndsWith('！');
            if (!TryKind(insured ? word[..^1] : word, out RelicKind kind))
            {
                return null;
            }

            items.Add(new CarriedRelic(kind, insured));
        }

        return items.Count == 0 ? null : new RelicLoadout(items.ToImmutable());
    }

    private static int Number(RelicKind kind) => RelicKinds.Order.IndexOf(kind) + 1;

    private static bool TryKind(string text, out RelicKind kind)
    {
        bool ok = int.TryParse(text, out int n) && n >= 1 && n <= RelicKinds.Order.Length;
        kind = ok ? RelicKinds.Order[n - 1] : default;
        return ok;
    }

    private static string ReadLine(TextReader input)
    {
        string line = (input.ReadLine() ?? throw new PlayQuitException()).Trim();
        return line is "q" or "quit" or "exit" ? throw new PlayQuitException() : line;
    }
}
