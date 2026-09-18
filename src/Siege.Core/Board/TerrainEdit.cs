namespace Siege.Core.Board;

/// <summary>三种地形改造动作（terrain-edit「改造动作集」）。MUST NOT 有逆向动作。</summary>
public enum TerrainEditKind
{
    /// <summary>搭桥：目标为一个未架桥的深水格。</summary>
    Bridge,

    /// <summary>立栅：目标为匠人所在格与某个几何四邻格之间的边。</summary>
    Fence,

    /// <summary>烧林：目标为一个林地格，改造后地表变草地。</summary>
    Burn,
}

/// <summary>
/// 一次地形改造：动作类型 + 目标。格目标（搭桥 / 烧林）用 <see cref="Cell"/>，边目标（立栅）用 <see cref="Edge"/>；
/// 边在 <see cref="FenceEdge"/> 构造时按字典序归一，因此 <c>(a,b)</c> 与 <c>(b,a)</c> 是同一个改造——
/// "同一目标批内唯一"靠值相等判定，归一化是它成立的前提。
/// </summary>
/// <remarks>
/// <para>本类型只是<b>目标的表示</b>：它是否合法由 <see cref="TerrainEditRules"/> 判定（唯一实现），
/// 如何写进地形由 <see cref="TerrainWriter"/> 负责（唯一写入口）。</para>
/// <para>规格：openspec/changes/artisan-terrain-edit/specs/terrain-edit</para>
/// </remarks>
public readonly record struct TerrainEdit
{
    private TerrainEdit(TerrainEditKind kind, Coord cell, FenceEdge edge)
    {
        Kind = kind;
        Cell = cell;
        Edge = edge;
    }

    /// <summary>动作类型。</summary>
    public TerrainEditKind Kind { get; }

    /// <summary>格目标；<see cref="TerrainEditKind.Fence"/> 时无意义（为默认值）。</summary>
    public Coord Cell { get; }

    /// <summary>边目标；非 <see cref="TerrainEditKind.Fence"/> 时无意义（为默认值）。</summary>
    public FenceEdge Edge { get; }

    /// <summary>搭桥：目标深水格。</summary>
    public static TerrainEdit Bridge(Coord cell) => new(TerrainEditKind.Bridge, cell, default);

    /// <summary>烧林：目标林地格。</summary>
    public static TerrainEdit Burn(Coord cell) => new(TerrainEditKind.Burn, cell, default);

    /// <summary>立栅：目标边（按字典序归一）。</summary>
    public static TerrainEdit Fence(FenceEdge edge) => new(TerrainEditKind.Fence, default, edge);

    /// <summary>立栅：目标边由两端给出（不分方向）。</summary>
    public static TerrainEdit Fence(Coord a, Coord b) => Fence(new FenceEdge(a, b));

    /// <summary>该改造牵动的格：搭桥 / 烧林为目标格，立栅为边的两端。</summary>
    public IEnumerable<Coord> Cells => Kind == TerrainEditKind.Fence ? [Edge.A, Edge.B] : [Cell];

    /// <summary>
    /// 规范文本：<c>B:F7</c> / <c>F:F6-G6</c> / <c>X:H3</c>。盘面序列化（同形与存档）与日志共用这一份，
    /// 反向解析在 <see cref="Parse"/>，MUST NOT 在别处再写一份。
    /// </summary>
    public override string ToString() => Kind switch
    {
        TerrainEditKind.Bridge => $"B:{Cell.ToNotation()}",
        TerrainEditKind.Fence => $"F:{Edge}",
        TerrainEditKind.Burn => $"X:{Cell.ToNotation()}",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "未知改造类型。"),
    };

    /// <summary><see cref="ToString"/> 的反向实现。格式不符即抛 <see cref="FormatException"/>。</summary>
    public static TerrainEdit Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 3 || text[1] != ':')
        {
            throw new FormatException($"改造记法格式不符：\"{text}\"。");
        }

        string target = text[2..];
        switch (text[0])
        {
            case 'B':
                return Bridge(Coord.Parse(target));
            case 'X':
                return Burn(Coord.Parse(target));
            case 'F':
                int dash = target.IndexOf('-', StringComparison.Ordinal);
                if (dash <= 0)
                {
                    throw new FormatException($"立栅记法缺少边的两端：\"{text}\"。");
                }

                return Fence(Coord.Parse(target[..dash]), Coord.Parse(target[(dash + 1)..]));
            default:
                throw new FormatException($"未知改造动作码：'{text[0]}'。");
        }
    }

    /// <summary>面向人的动作名称，只用于文案与日志。</summary>
    public static string DisplayName(TerrainEditKind kind) => kind switch
    {
        TerrainEditKind.Bridge => "搭桥",
        TerrainEditKind.Fence => "立栅",
        TerrainEditKind.Burn => "烧林",
        _ => kind.ToString(),
    };
}
