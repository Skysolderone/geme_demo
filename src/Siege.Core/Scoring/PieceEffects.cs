using System.Numerics;
using Siege.Core.Board;

namespace Siege.Core.Scoring;

/// <summary>
/// 倍增子倍率 <c>1.5^e</c> 的精确表示：分子 <c>3^e</c>、分母 <c>2^e</c>，<c>e = min(倍增子数量, <see cref="MaxExponent"/>)</c>。
/// 计分路径上 MUST NOT 出现二进制浮点——取整对边界值极其敏感，整数运算才能保证跨平台、跨架构结果一致。
/// </summary>
/// <remarks>
/// <para>倍率指数封顶为 <see cref="MaxExponent"/>（cap-multiplier D1/D2/D4，推翻 territory-power 裁决 3）：封顶只在这一处做，
/// 调用方继续传原始倍增子数量，<see cref="Count"/> 保留原始数量、<see cref="Exponent"/> 才是生效指数。</para>
/// <para>计算用 <see cref="Int128"/> 的 checked 整数运算：零分配（批量跑局每次结算都要对全盘每条棋串调用 <see cref="Apply"/>）。
/// 封顶后 <c>3^5 = 243</c>，中间值 <c>value × 243</c> 不可能溢出 <see cref="Int128"/>；checked 作防御保留——
/// 结果装不进 <see cref="long"/> 时仍抛 <see cref="OverflowException"/>，响亮失败而非静默回绕。</para>
/// <para>只有 <see cref="ToString"/> 走 <see cref="BigInteger"/>：它是显示路径，不参与任何计算。</para>
/// </remarks>
public readonly record struct Multiplier
{
    /// <summary>倍率指数上限：第 6 枚起的倍增子不再让倍率乘 1.5，倍率上限 <c>3^5 / 2^5 = 7.59375</c>。全项目只在此定义一次。</summary>
    public const int MaxExponent = 5;

    /// <summary>不含倍增子时的倍率 1。</summary>
    public static readonly Multiplier One = new(0);

    public Multiplier(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "倍增子数量不得为负。");
        }

        Count = count;
    }

    /// <summary>倍增子的原始数量 n（未封顶）。</summary>
    public int Count { get; }

    /// <summary>生效倍率指数 <c>e = min(n, <see cref="MaxExponent"/>)</c>；分子、分母与显示都按它算。</summary>
    public int Exponent => Math.Min(Count, MaxExponent);

    /// <summary>分子 <c>3^e</c>。</summary>
    public Int128 Numerator => Pow(3, Exponent);

    /// <summary>分母 <c>2^e</c>。</summary>
    public Int128 Denominator => Pow(2, Exponent);

    /// <summary>
    /// 对非负整数值施加倍率并向下取整：<c>value × 3^e / 2^e</c>，非负整数除法即向下取整。
    /// 这是全项目唯一的"乘倍率并取整"实现。结果超出 <see cref="long"/> 时抛 <see cref="OverflowException"/>。
    /// </summary>
    public long Apply(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "军势不得为负。");
        }

        Int128 scaled = checked((Int128)value * Numerator);
        return checked((long)(scaled / Denominator));
    }

    /// <summary>精确十进制表示（如 <c>1</c>、<c>1.5</c>、<c>2.25</c>、<c>3.375</c>，最大 <c>7.59375</c>），按生效指数生成，供 UI 与遥测显示；不经过浮点，也不参与计算。</summary>
    public override string ToString()
    {
        int e = Exponent;
        if (e == 0)
        {
            return "1";
        }

        // 3^e / 2^e = 15^e / 10^e：15^e 的十进制串，小数点左移 e 位。
        string digits = BigInteger.Pow(15, e).ToString();
        return digits[..^e] + "." + digits[^e..];
    }

    /// <summary>checked 整数幂；溢出 <see cref="Int128"/> 时抛 <see cref="OverflowException"/>。</summary>
    private static Int128 Pow(Int128 @base, int exponent)
    {
        Int128 result = Int128.One;
        for (int i = 0; i < exponent; i++)
        {
            result = checked(result * @base);
        }

        return result;
    }
}

/// <summary>
/// 五种原型棋子的效果（设计文档 §9.2）：基础军势、连珠线位置加值、协同位置加值。
/// 只回答"这条棋串上的棋子产生多少数值"；棋串归属、气与围杀完全由棋盘层决定，类型效果 MUST NOT 提供额外气、免死或复活。
/// </summary>
/// <remarks>规格：openspec/changes/add-territory-power/specs/piece-effects</remarks>
public static class PieceEffects
{
    /// <summary>基础军势：普通子 1、堡垒子 4、连珠子 1、倍增子 1、协同子 1。</summary>
    public static int BasePower(PieceType type) => type switch
    {
        PieceType.Basic => 1,
        PieceType.Fortress => 4,
        PieceType.Line => 1,
        PieceType.Multiplier => 1,
        PieceType.Synergy => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。"),
    };

    /// <summary>
    /// 连珠子位置加值：对棋串内每条由同一玩家连珠子构成、长度 <c>L ≥ 2</c> 的极长横向或纵向连续线计 <c>L × (L − 1)</c>。
    /// 横、纵各扫一遍，每条线只从其起点出发计一次，MUST NOT 拆成子区间重复加分；十字交点自然在两次扫描中各计一次。
    /// </summary>
    /// <remarks>
    /// 裁决记录 1：线的构成是"同玩家 + 四邻接连续的连珠子"，MUST NOT 穿越普通子、堡垒子等其他类型的己方棋子。
    /// 四邻接连续的同玩家棋子必然同串，因此只需检查所有者与类型，无需再做棋串判定。
    /// 方向步进从 <see cref="GameBoard.Neighbors"/> 的结果中挑选，不在此处手写邻居偏移。
    /// </remarks>
    public static int LineBonus(GameBoard board, Group group)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(group);

        int bonus = 0;
        foreach (Coord stone in group.Stones)
        {
            if (!IsLineStoneOf(board, stone, group.Owner))
            {
                continue;
            }

            bonus += RunBonus(board, stone, group.Owner, dx: 1, dy: 0);
            bonus += RunBonus(board, stone, group.Owner, dx: 0, dy: 1);
        }

        return bonus;
    }

    /// <summary>
    /// 协同子位置加值：棋串中每枚协同子按"该棋串中除协同子以外的棋子类型种数"每种 2 点，
    /// 即 <c>协同子数量 × 其他类型数 × 2</c>。纯协同子棋串为 0（裁决记录 2）。
    /// </summary>
    public static int SynergyBonus(GameBoard board, Group group)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(group);

        int synergyCount = 0;
        var otherTypes = new HashSet<PieceType>();
        foreach (Coord stone in group.Stones)
        {
            PieceType type = TypeAt(board, stone);
            if (type == PieceType.Synergy)
            {
                synergyCount++;
            }
            else
            {
                otherTypes.Add(type);
            }
        }

        return synergyCount * otherTypes.Count * 2;
    }

    /// <summary>棋串中倍增子的原始数量；封顶为生效指数由 <see cref="Multiplier"/> 负责。</summary>
    public static int MultiplierCount(GameBoard board, Group group)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(group);
        return group.Stones.Count(stone => TypeAt(board, stone) == PieceType.Multiplier);
    }

    /// <summary>棋串的基础军势总和。</summary>
    public static int BaseTotal(GameBoard board, Group group)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(group);
        return group.Stones.Sum(stone => BasePower(TypeAt(board, stone)));
    }

    /// <summary>从 <paramref name="start"/> 沿 (dx, dy) 方向的极长连珠线；若 <paramref name="start"/> 不是该线起点则返回 0，避免子区间重复计分。</summary>
    private static int RunBonus(GameBoard board, Coord start, PlayerId owner, int dx, int dy)
    {
        if (Step(board, start, -dx, -dy) is { } previous && IsLineStoneOf(board, previous, owner))
        {
            return 0;
        }

        int length = 1;
        Coord current = start;
        while (Step(board, current, dx, dy) is { } next && IsLineStoneOf(board, next, owner))
        {
            length++;
            current = next;
        }

        return length >= 2 ? length * (length - 1) : 0;
    }

    /// <summary>在四邻接邻居中挑出位于 (dx, dy) 方向的那一格；越界则为 <c>null</c>。</summary>
    private static Coord? Step(GameBoard board, Coord c, int dx, int dy)
    {
        foreach (Coord n in board.Neighbors(c))
        {
            if (n.X - c.X == dx && n.Y - c.Y == dy)
            {
                return n;
            }
        }

        return null;
    }

    private static bool IsLineStoneOf(GameBoard board, Coord c, PlayerId owner) =>
        board[c].Occupant is { } occupant && occupant.Owner == owner && occupant.Type == PieceType.Line;

    private static PieceType TypeAt(GameBoard board, Coord stone) =>
        (board[stone].Occupant ?? throw new SiegeRuleException($"棋串与盘面不一致：{stone.ToNotation()} 已为空。")).Type;
}
