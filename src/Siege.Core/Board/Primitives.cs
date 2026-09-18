namespace Siege.Core.Board;

/// <summary>
/// 格子的可落子性。<see cref="Obstacle"/> 在气、覆盖与围杀判定中一律视为封堵边界，与棋盘外沿语义相同；
/// 未架桥的深水共享这一语义，因此也归入 <see cref="Obstacle"/>。高度、地表、桥等地形细节见 <see cref="MapData.TerrainData"/>。
/// </summary>
public enum Terrain
{
    /// <summary>可落子格（含桥格与林地）。</summary>
    Playable,

    /// <summary>不可落子：岩石障碍或未架桥的深水；不可控制、不计分。</summary>
    Obstacle,
}

/// <summary>六种原型棋子。基础军势与效果属于计分层，本层只负责类型标识。</summary>
public enum PieceType
{
    /// <summary>普通子，基础军势 1。</summary>
    Basic,

    /// <summary>堡垒子，基础军势 4。</summary>
    Fortress,

    /// <summary>连珠子，成线提供位置加值。</summary>
    Line,

    /// <summary>倍增子，令棋串军势依次乘 1.5。</summary>
    Multiplier,

    /// <summary>协同子，按同串其他类型数提供位置加值。</summary>
    Synergy,

    /// <summary>匠人，基础军势 1；能力只在落子瞬间改造地形（见 <c>terrain-edit</c>），落子后与普通子完全相同。</summary>
    Artisan,
}

/// <summary>信物格所属的强度预算分区。</summary>
public enum RelicZone
{
    /// <summary>出生区：不生成高阶信物。</summary>
    BirthZone,

    /// <summary>公共争夺区。</summary>
    Contested,
}

/// <summary>信物强度预算档位。中央区、交通咽喉与高风险边缘承担更高预算。</summary>
public enum BudgetTier
{
    /// <summary>出生区档位，预算最低。</summary>
    Birth,

    /// <summary>公共区标准档位。</summary>
    Standard,

    /// <summary>公共区高档位：中央、咽喉与高风险边缘。</summary>
    High,
}

/// <summary>玩家标识。</summary>
public readonly record struct PlayerId(int Value) : IComparable<PlayerId>
{
    public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);

    public override string ToString() => $"P{Value}";
}

/// <summary>格子上的棋子：所有者 + 类型。</summary>
public readonly record struct Occupant(PlayerId Owner, PieceType Type)
{
    public override string ToString() => $"{Owner}:{Type}";
}

/// <summary>格子的只读视图。</summary>
public readonly record struct Cell(
    Coord Coord,
    Terrain Terrain,
    int? BirthZone,
    bool IsRelicCell,
    Occupant? Occupant)
{
    /// <summary>该格当前是否为空的可落子格。</summary>
    public bool IsPlayableEmpty => Terrain == Terrain.Playable && Occupant is null;
}

/// <summary>规则层面的非法操作。</summary>
public sealed class SiegeRuleException : InvalidOperationException
{
    public SiegeRuleException(string message) : base(message)
    {
    }
}
