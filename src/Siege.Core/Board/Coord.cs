namespace Siege.Core.Board;

/// <summary>
/// 格子坐标。权威记法为围棋记法：列标 + 行数字。
/// 列标自左向右，按围棋惯例跳过 <c>I</c>；行数字自下而上，从 1 起。左下角为 <c>A1</c>。
/// 前 25 列的列标是单个字母；第 26 列起是两个字母（<c>AA</c>、<c>AB</c>……<c>AZ</c>、<c>BA</c>……），两位取自同一张字母表（board-map D6）。
/// </summary>
/// <remarks>
/// 这里是围棋记法与内部数值索引之间的<b>唯一</b>映射实现。
/// 任何其他地方手写列字母表都是缺陷（最常见的错误是忘记跳过 <c>I</c>）。
/// 规格：openspec/changes/add-board-core/specs/board-topology —— Requirement: 坐标记法；
/// 双字母列标见 openspec/changes/board-map/specs/board-topology
/// </remarks>
public readonly struct Coord : IEquatable<Coord>, IComparable<Coord>
{
    /// <summary>围棋列字母表，跳过 <c>I</c>。</summary>
    public const string ColumnLetters = "ABCDEFGHJKLMNOPQRSTUVWXYZ";

    /// <summary>列数上限：单字母 25 列 + 双字母 25×25 列。</summary>
    public static readonly int MaxColumns = ColumnLetters.Length + (ColumnLetters.Length * ColumnLetters.Length);

    /// <summary>列索引，0 基，自左向右。</summary>
    public int X { get; }

    /// <summary>行索引，0 基，自下而上。</summary>
    public int Y { get; }

    public Coord(int x, int y)
    {
        if (x < 0 || x >= MaxColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, $"列索引必须在 0..{MaxColumns - 1} 之间。");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "行索引不得为负。");
        }

        X = x;
        Y = y;
    }

    /// <summary>列标，如 <c>A</c>、<c>Z</c>、<c>AA</c>。前 25 列为单个字母，其后为两个字母。</summary>
    public string Column
    {
        get
        {
            int n = ColumnLetters.Length;
            if (X < n)
            {
                return ColumnLetters[X].ToString();
            }

            int rest = X - n;
            return string.Concat(ColumnLetters[rest / n].ToString(), ColumnLetters[rest % n].ToString());
        }
    }

    /// <summary>行号，1 基，自下而上。</summary>
    public int Row => Y + 1;

    /// <summary>返回围棋记法，如 <c>A1</c>、<c>F6</c>、<c>L11</c>、<c>AB12</c>。</summary>
    public string ToNotation() => string.Concat(Column, Row.ToString());

    /// <summary>解析围棋记法。大小写不敏感；<c>I</c> 不是合法列字母；列标取最长的字母前缀（一或两个字母）。</summary>
    public static Coord Parse(string? notation)
    {
        if (!TryParse(notation, out Coord coord))
        {
            throw new FormatException(
                $"无法解析为围棋记法坐标：{(notation is null ? "<null>" : $"\"{notation}\"")}。列字母跳过 I，行号自下而上从 1 起。");
        }

        return coord;
    }

    /// <summary>尝试解析围棋记法。</summary>
    public static bool TryParse(string? notation, out Coord coord)
    {
        coord = default;
        if (string.IsNullOrWhiteSpace(notation))
        {
            return false;
        }

        string text = notation.Trim().ToUpperInvariant();
        int letters = 0;
        while (letters < text.Length && char.IsAsciiLetterUpper(text[letters]))
        {
            letters++;
        }

        if (letters is < 1 or > 2)
        {
            return false;
        }

        int first = ColumnLetters.IndexOf(text[0]);
        int second = letters == 2 ? ColumnLetters.IndexOf(text[1]) : 0;
        if (first < 0 || second < 0)
        {
            return false;
        }

        int x = letters == 1 ? first : ColumnLetters.Length + (first * ColumnLetters.Length) + second;
        if (!int.TryParse(text.AsSpan(letters), out int row) || row < 1)
        {
            return false;
        }

        coord = new Coord(x, row - 1);
        return true;
    }

    public bool Equals(Coord other) => X == other.X && Y == other.Y;

    public override bool Equals(object? obj) => obj is Coord other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>稳定的字典序：先行后列，用于确定性遍历与并列打破。</summary>
    public int CompareTo(Coord other)
    {
        int byRow = Y.CompareTo(other.Y);
        return byRow != 0 ? byRow : X.CompareTo(other.X);
    }

    public override string ToString() => ToNotation();

    public static bool operator ==(Coord left, Coord right) => left.Equals(right);

    public static bool operator !=(Coord left, Coord right) => !left.Equals(right);

    public static bool operator <(Coord left, Coord right) => left.CompareTo(right) < 0;

    public static bool operator >(Coord left, Coord right) => left.CompareTo(right) > 0;

    public static bool operator <=(Coord left, Coord right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Coord left, Coord right) => left.CompareTo(right) >= 0;
}
