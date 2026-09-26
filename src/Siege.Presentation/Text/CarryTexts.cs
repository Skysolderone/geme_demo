using Siege.Core.Board;
using Siege.Core.Carry;

namespace Siege.Presentation.Text;

/// <summary>
/// 带入带出（carry-in-out）面向玩家的文案：补给名、效果、带入、结算与档案概览。终端与图形版共用这一份（段 B 后裁决 3），
/// 只做"值 → 文字"，不做任何规则判断——结算一律由 Core 的 <see cref="CarryOutSettlement"/> 给出，这里只把结果写成字。
/// </summary>
public static class CarryTexts
{
    /// <summary>补给名称。</summary>
    public static string Supply(SupplyKind kind) => kind switch
    {
        SupplyKind.SpareStone => "备用子",
        SupplyKind.DraftLot => "征召签",
        SupplyKind.Commission => "换型令",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知补给种类。"),
    };

    /// <summary>补给的开局效果（一句话）。</summary>
    public static string Effect(SupplyKind kind) => kind switch
    {
        SupplyKind.SpareStone => "开局多 1 枚普通子（共 6 枚）",
        SupplyKind.DraftLot => "开局 1 枚普通子换成随机类型（按基础权重抽取）",
        SupplyKind.Commission => "开局 1 枚普通子换成指定类型",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知补给种类。"),
    };

    /// <summary>一件带入：<c>换型令 → 连珠子</c>；备用子只写名称。</summary>
    public static string Carry(CarryIn carry)
    {
        ArgumentNullException.ThrowIfNull(carry);
        return carry.Type is { } type ? $"{Supply(carry.Kind)} → {Labels.Piece(type)}" : Supply(carry.Kind);
    }

    /// <summary>
    /// 全部玩家的带入（公开信息）：<c>带入：你：换型令 → 连珠子；玩家2：征召签 → 堡垒子</c>，按玩家编号升序；无人带入时 <c>带入：全员不带入</c>。
    /// 玩家名由调用方给（终端"你 / 玩家2"，图形版阵营名）。
    /// </summary>
    public static string List(IReadOnlyDictionary<PlayerId, CarryIn> carries, Func<PlayerId, string> name)
    {
        ArgumentNullException.ThrowIfNull(carries);
        ArgumentNullException.ThrowIfNull(name);
        return carries.Count == 0
            ? "带入：全员不带入"
            : "带入：" + string.Join("；", carries.OrderBy(kv => kv.Key).Select(kv => $"{name(kv.Key)}：{Carry(kv.Value)}"));
    }

    /// <summary>弃赛当时的结算行：<c>弃赛：弃赛名次第 2，带出 8，换型令已返还</c>；带出 0 时注明保护期。</summary>
    public static string Resign(CarryOutResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return $"弃赛：弃赛名次第 {result.Rank}，带出 {result.Points}，{Item(result)}" + (result.Points == 0 ? "（构筑保护期内弃赛不带出补给点）" : string.Empty);
    }

    /// <summary>出局当时的结算行：<c>出局：带出 0，换型令已丢失</c>。</summary>
    public static string Eliminated(CarryOutResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return $"出局：带出 0，{Item(result)}";
    }

    /// <summary>结算正文：<c>完赛 · 第 2 名 · 带出 16 · 补给已消耗</c>（终端前缀"结算："，图形版结算面板直接显示）。</summary>
    public static string Settlement(CarryOutResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            CarryOutcome.Finished => $"完赛 · 第 {result.Rank} 名 · 带出 {result.Points} · {Item(result)}",
            CarryOutcome.Resigned => $"弃赛 · 弃赛名次第 {result.Rank} 名 · 带出 {result.Points} · {Item(result)}",
            CarryOutcome.Eliminated => $"出局 · 带出 0 · {Item(result)}",
            _ => "未结算",
        };
    }

    /// <summary>档案概览：<c>补给点 17   库存 备用子×1  征召签×0  换型令×2</c>。</summary>
    public static string Stock(CarryProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return $"补给点 {profile.Points}   库存 {string.Join("  ", Supplies.Order.Select(k => $"{Supply(k)}×{profile.StockOf(k)}"))}";
    }

    /// <summary>带入物的去向：未带入 / 已返还 / 已消耗（完赛）/ 已丢失。</summary>
    public static string Item(CarryOutResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.CarryIn is not { } carry
            ? "未带入补给"
            : result.Returned ? $"{Supply(carry.Kind)}已返还"
            : result.Outcome == CarryOutcome.Finished ? "补给已消耗"
            : $"{Supply(carry.Kind)}已丢失";
    }
}
