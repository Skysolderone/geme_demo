using System.Collections.Immutable;
using Siege.Core.PieceRelics;

namespace Siege.Presentation.Hud;

/// <summary>
/// 开局前带入面板的选件状态（relic-redesign R25 / D8，段 C2）：逐件加入、移除、勾 / 取消保险，产出 <see cref="RelicLoadout"/>。
/// 终端用 <c>编号[!]</c> 一行输入同一份清单；图形版用点选，两者都只是输入方式，件数 / 保险 / 库存的规则一律由 Core 回答。
/// </summary>
/// <remarks>
/// <para><b>不重写任何规则</b>：≤ 5 件、≤ 2 件保险、不超库存的判定全部委托 <see cref="RelicLoadout.RefusalAgainst"/> / <see cref="RelicLoadout.ShapeRefusal"/>；
/// 本类只做"试加一件 / 试勾一件之后合不合规"的提问，<see cref="RelicKinds.MaxCarried"/> / <see cref="RelicKinds.MaxInsured"/> 只拿来排文案。
/// 加入与勾选本身<b>不拦</b>——超限的清单照样保存在状态里，由 <see cref="Refusal"/> 给出即时提示、调用方据此禁用"开始"（AC4：带入 6 件或保险 3 件被拒）。</para>
/// <para>本类不持档案、不写文件；兑换走 <c>RelicProfileStore.TryExchange</c>，与本类无关。</para>
/// </remarks>
public sealed class CarryPicker
{
    private readonly List<CarriedRelic> _items = [];

    /// <summary>已选的件（清单顺序 = 加入顺序；部署挂出时的清单下标就是这里的下标）。</summary>
    public ImmutableArray<CarriedRelic> Items => [.. _items];

    /// <summary>件数。</summary>
    public int Count => _items.Count;

    /// <summary>勾为保险的件数。</summary>
    public int InsuredCount => _items.Count(i => i.Insured);

    /// <summary>当前清单（空清单 = 不带入）。</summary>
    public RelicLoadout Loadout => new([.. _items]);

    /// <summary>某种信物已选的件数。</summary>
    public int CountOf(RelicKind kind) => _items.Count(i => i.Kind == kind);

    /// <summary>加入 1 件 <paramref name="kind"/>（不勾保险）。不校验——超限由 <see cref="Refusal"/> 报出。</summary>
    public void Add(RelicKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知信物种类。");
        }

        _items.Add(new CarriedRelic(kind, Insured: false));
    }

    /// <summary>移除第 <paramref name="index"/> 件（0 起）。下标越界返回 <c>false</c>、状态不变。</summary>
    public bool Remove(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        _items.RemoveAt(index);
        return true;
    }

    /// <summary>勾 / 取消第 <paramref name="index"/> 件的保险（0 起）。下标越界返回 <c>false</c>、状态不变。</summary>
    public bool ToggleInsured(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        _items[index] = _items[index] with { Insured = !_items[index].Insured };
        return true;
    }

    /// <summary>清空（回到不带入）。</summary>
    public void Clear() => _items.Clear();

    /// <summary>
    /// 当前清单对照档案 <paramref name="profile"/> 的拒绝原因（Core 的 <see cref="RelicLoadout.RefusalAgainst"/>，原样转发）；合规为 <c>null</c>。
    /// 调用方用它做即时提示，并在非 <c>null</c> 时禁用"开始"。
    /// </summary>
    public string? Refusal(RelicProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Loadout.RefusalAgainst(profile);
    }

    /// <summary>再加 1 件 <paramref name="kind"/> 之后清单是否仍合规（件数与该种库存都够）。用来决定"带入"按钮是否可点。</summary>
    public bool CanAdd(RelicKind kind, RelicProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new RelicLoadout([.. _items, new CarriedRelic(kind, Insured: false)]).RefusalAgainst(profile) is null;
    }

    /// <summary>把第 <paramref name="index"/> 件的保险翻转之后，清单形状是否仍合规（保险件数不超限）。已勾的件总可以取消。</summary>
    public bool CanToggleInsured(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        var toggled = new List<CarriedRelic>(_items)
        {
            [index] = _items[index] with { Insured = !_items[index].Insured },
        };
        return new RelicLoadout([.. toggled]).ShapeRefusal() is null;
    }
}
