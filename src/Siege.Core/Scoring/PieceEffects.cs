using System.Numerics;
using Siege.Core.Board;

namespace Siege.Core.Scoring;

/// <summary>
/// 倍增子倍率 <c>1.5^n</c> 的精确表示：分子 <c>3^n</c>、分母 <c>2^n</c>，<c>n = 倍增子数量</c>，不设任何封顶（restore-go-core-rules D1）。
/// 计分路径上 MUST NOT 出现二进制浮点——取整对边界值极其敏感，整数运算才能保证跨平台、跨架构结果一致。
/// </summary>
/// <remarks>
/// <para><c>n</c> 的上界是地图可落子格数（边疆档可达数百），<c>3^n</c> 远超 64 位乃至 128 位整数，
/// 因此分子、分母与 <see cref="Apply"/> 的结果一律用任意精度整数 <see cref="BigInteger"/>：精确、不溢出、不截断、不饱和。
/// 饱和到某个上限等于变相封顶，已被裁决否决。</para>
/// <para><see cref="ToString"/> 给精确十进制显示串，同样只走整数。</para>
/// </remarks>
public readonly record struct Multiplier
{
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

    /// <summary>倍增子数量 n，即倍率指数。</summary>
    public int Count { get; }

    /// <summary>分子 <c>3^n</c>。</summary>
    public BigInteger Numerator => BigInteger.Pow(3, Count);

    /// <summary>分母 <c>2^n</c>。</summary>
    public BigInteger Denominator => BigInteger.Pow(2, Count);

    /// <summary>
    /// 对非负整数值施加倍率并向下取整：<c>value × 3^n / 2^n</c>，非负整数除法即向下取整。
    /// 这是全项目唯一的"乘倍率并取整"实现；势力组装把"基础军势总和 + 位置加值"整体传进来（restore-go-core-rules D1），每条棋串各取整一次。
    /// </summary>
    public BigInteger Apply(BigInteger value)
    {
        if (value.Sign < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "军势不得为负。");
        }

        return value * Numerator / Denominator;
    }

    /// <summary>精确十进制表示（如 <c>1</c>、<c>1.5</c>、<c>2.25</c>、<c>3.375</c>、<c>5.0625</c>），供 UI 与遥测显示；不经过浮点，也不参与计算。</summary>
    public override string ToString()
    {
        int n = Count;
        if (n == 0)
        {
            return "1";
        }

        // 3^n / 2^n = 15^n / 10^n：15^n 的十进制串，小数点左移 n 位（15^n > 10^n，整数部分至少一位）。
        string digits = BigInteger.Pow(15, n).ToString();
        return digits[..^n] + "." + digits[^n..];
    }
}

/// <summary>
/// 六种原型棋子的效果（设计文档 §9.2）：基础军势、连珠线位置加值、协同位置加值。
/// 只回答"这条棋串上的棋子产生多少数值"；棋串归属、气与围杀完全由棋盘层决定，类型效果 MUST NOT 提供额外气、免死或复活。
/// </summary>
/// <remarks>规格：openspec/changes/add-territory-power/specs/piece-effects</remarks>
public static class PieceEffects
{
    /// <summary>基础军势：普通子 1、堡垒子 4、连珠子 1、倍增子 1、协同子 1、匠人 1（artisan-terrain-edit 裁决 T-1）。</summary>
    public static int BasePower(PieceType type) => type switch
    {
        PieceType.Basic => 1,
        PieceType.Fortress => 4,
        PieceType.Line => 1,
        PieceType.Multiplier => 1,
        PieceType.Synergy => 1,
        PieceType.Artisan => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。"),
    };

    /// <summary>
    /// 连珠子位置加值：对棋串内每条由同一玩家连珠子构成、长度 <c>L ≥ 2</c> 的极长横向或纵向连续线计 <c>L × (L − 1)</c>。
    /// 横、纵各扫一遍，每条线只从其起点出发计一次，MUST NOT 拆成子区间重复加分；十字交点自然在两次扫描中各计一次。
    /// </summary>
    /// <remarks>
    /// 裁决记录 1：线的构成是"同玩家 + 连续的连珠子"，MUST NOT 穿越普通子、堡垒子等其他类型的己方棋子。
    /// "连续"指相邻两枚之间存在<b>气边</b>（terrain-model 裁决 A-6）：隔着崖壁、未架桥深水或栅栏的两枚连珠子不成线。
    /// 经气边相邻的同玩家棋子必然同串，因此只需检查所有者与类型，无需再做棋串判定。
    /// 方向步进从 <see cref="GameBoard.LibertyNeighbors"/> 的结果中挑选，不在此处手写邻居偏移或地形过滤。
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

    /// <summary>
    /// 高地压制加值（scoring-sites D-E，唯一实现）：棋串中每枚棋子 s，若 s 的覆盖目标里存在"非 s 所有者的棋子且其格高度严格低于 s"，
    /// 则 s 提供 1 点，每枚至多 1 点；按棋串求和，计入位置加值，不被倍率放大。
    /// </summary>
    /// <remarks>
    /// 覆盖目标只经 <see cref="GameBoard.CoverageTargets"/> 取得（含跨崖居高临下、隔一格深水到对岸；林地不是覆盖目标），
    /// MUST NOT 改走几何邻居或气边。弃赛者的遗留棋子同样判定（不看玩家状态）。
    /// 规格：openspec/changes/scoring-sites/specs/piece-effects —— Requirement: 高地压制加值
    /// </remarks>
    public static int HighGroundBonus(GameBoard board, Group group)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(group);

        int bonus = 0;
        foreach (Coord stone in group.Stones)
        {
            int height = board.Map.HeightAt(stone);
            foreach (Coord target in board.CoverageTargets(stone))
            {
                if (board[target].Occupant is { } enemy && enemy.Owner != group.Owner && board.Map.HeightAt(target) < height)
                {
                    bonus++;
                    break;
                }
            }
        }

        return bonus;
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

    /// <summary>在气边邻居中挑出位于 (dx, dy) 方向的那一格；越界或无气边（崖壁 / 深水 / 栅栏）则为 <c>null</c>。</summary>
    private static Coord? Step(GameBoard board, Coord c, int dx, int dy)
    {
        foreach (Coord n in board.LibertyNeighbors(c))
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
