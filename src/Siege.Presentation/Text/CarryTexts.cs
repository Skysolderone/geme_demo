using Siege.Core.Board;
using Siege.Core.PieceRelics;

namespace Siege.Presentation.Text;

/// <summary>
/// 带入带出（relic-redesign 接线段 W2）面向玩家的文案：信物名、效果、带入清单、结算与档案概览。终端与图形版共用这一份，
/// 只做"值 → 文字"，不做任何规则判断——结算一律由 Core（<c>MatchFlow.SettleCarried</c> / <c>EarlySettlementFor</c>，唯一实现 <see cref="RelicSettlement"/>）给出。
/// </summary>
public static class CarryTexts
{
    /// <summary>信物名称（Core 的唯一映射 <see cref="RelicKinds.DisplayName"/>）。</summary>
    public static string Relic(RelicKind kind) => kind.DisplayName();

    /// <summary>信物效果（一句话，R12 未校准初值）。</summary>
    public static string Effect(RelicKind kind) => kind switch
    {
        RelicKind.WarDrum => "持物棋子基础军势 +3",
        RelicKind.Command => "所在棋串位置加值 + 棋串棋子数 / 2",
        RelicKind.Watchtower => "持物棋子按岩台覆盖（四方向直线距离 2）",
        RelicKind.Chameleon => "协同计数多算 1 种类型，所在棋串位置加值 +2",
        RelicKind.Banner => "所在棋串倍率指数 +1（×1.5），稀有",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知信物种类。"),
    };

    /// <summary>一件带入：<c>战鼓（保险）</c>。</summary>
    public static string Carried(CarriedRelic item) => item.Kind.DisplayName() + (item.Insured ? "（保险）" : string.Empty);

    /// <summary>一份带入清单：<c>带入：战鼓（保险）、望台</c>；空清单 <c>带入：不带入</c>。清单是私有信息（R22），只给本人看。</summary>
    public static string Loadout(RelicLoadout loadout)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        return "带入：" + (loadout.Count == 0 ? "不带入" : string.Join("、", loadout.Items.Select(Carried)));
    }

    /// <summary>本人未挂出的带入件（部署挂出参数用清单下标，从 1 起显示）：<c>未挂出：[1] 战鼓（保险） [3] 望台</c>；没有时 <c>未挂出：无</c>。</summary>
    public static string Pending(IEnumerable<KeyValuePair<int, CarriedRelic>> pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        string[] items = [.. pending.Select(kv => $"[{kv.Key + 1}] {Carried(kv.Value)}")];
        return "未挂出：" + (items.Length == 0 ? "无" : string.Join(" ", items));
    }

    /// <summary>一件信物：<c>#3 战鼓（玩家2 带入，保险）</c> / <c>#5 军旗（地图）</c>。</summary>
    public static string Token(RelicToken token, Func<PlayerId, string> name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return $"#{token.Id} {token.Kind.DisplayName()}"
            + (token.Origin is { } origin ? $"（{name(origin)} 带入{(token.Insured ? "，保险" : string.Empty)}）" : "（地图）");
    }

    /// <summary>结局正文：<c>完赛 · 第 2 名 · 带出 16 点</c> / <c>弃赛 · 弃赛名次第 2 名 · 带出 8 点</c> / <c>出局 · 带出 0 点</c>。</summary>
    public static string Outcome(RelicPlayerSettlement result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome.Outcome switch
        {
            RelicOutcome.Finished => $"完赛 · 第 {result.Outcome.Rank} 名 · 带出 {result.Points} 点",
            RelicOutcome.Resigned => $"弃赛 · 弃赛名次第 {result.Outcome.Rank} 名 · 带出 {result.Points} 点"
                + (result.Points == 0 ? "（构筑保护期内弃赛不带出补给点）" : string.Empty),
            RelicOutcome.Eliminated => "出局 · 带出 0 点",
            RelicOutcome.Abandoned => "中途退出 · 带出 0 点",
            _ => "未结算",
        };
    }

    /// <summary>本人结算一行：<c>完赛 · 第 1 名 · 带出 24 点 · 带出信物 #3 战鼓（你 带入，保险）、#7 军旗（地图）</c>。</summary>
    public static string Settlement(RelicPlayerSettlement result, Func<PlayerId, string> name)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Outcome(result) + " · " + (result.BroughtOut.IsEmpty ? "未带出信物" : "带出信物 " + string.Join("、", result.BroughtOut.Select(t => Token(t, name))));
    }

    /// <summary>
    /// 整局每件信物的去向（D6）：每名带走了信物的玩家一行 <c>玩家2 带走：#3 战鼓（你 带入）……</c>，消失的一行 <c>消失：……</c>。按玩家编号升序。
    /// </summary>
    public static IReadOnlyList<string> Destinations(RelicMatchSettlement settlement, Func<PlayerId, string> name)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(name);
        List<string> lines = [.. settlement.Players.Values.Where(p => !p.BroughtOut.IsEmpty)
            .Select(p => $"{name(p.Player)} 带走：{string.Join("、", p.BroughtOut.Select(t => Token(t, name)))}")];
        if (!settlement.Vanished.IsEmpty)
        {
            lines.Add($"消失：{string.Join("、", settlement.Vanished.Select(t => Token(t, name)))}");
        }

        if (lines.Count == 0)
        {
            lines.Add("本局没有带出的信物");
        }

        return lines;
    }

    /// <summary>档案概览：<c>补给点 17   库存 战鼓×1  号令×0  望台×2  百变×0  军旗×1</c>。</summary>
    public static string Stock(RelicProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return $"补给点 {profile.Points}   库存 {string.Join("  ", RelicKinds.Order.Select(k => $"{k.DisplayName()}×{profile.StockOf(k)}"))}";
    }
}
