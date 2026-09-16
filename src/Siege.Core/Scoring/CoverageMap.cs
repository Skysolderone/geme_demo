using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Scoring;

/// <summary>一格的覆盖情况：不同覆盖者的数量，以及数量恰为 1 时的那名玩家（design.md D1）。</summary>
/// <remarks>只记录"有几个不同的玩家在覆盖"，不记录每名玩家用了几枚棋子——同一玩家 1 枚还是 4 枚棋子覆盖同一格，结果相同。</remarks>
public readonly record struct CellCoverage(int CovererCount, PlayerId? SoleCoverer)
{
    /// <summary>无任何覆盖。</summary>
    public static readonly CellCoverage None = new(0, null);

    /// <summary>是否被恰好一名玩家覆盖。</summary>
    public bool IsUnique => CovererCount == 1;
}

/// <summary>格子归属的四种结果加障碍。</summary>
public enum OwnershipKind
{
    /// <summary>障碍格：不属于任何玩家，不参与计分。</summary>
    Obstacle,

    /// <summary>被棋子占据：由棋子所有者直接控制（设计文档 §7.1 占据优先）。</summary>
    Occupied,

    /// <summary>空格且只受一名玩家覆盖：该玩家的独占领地。</summary>
    Exclusive,

    /// <summary>空格且同时受多名玩家覆盖：争议格，不属于任何玩家。</summary>
    Contested,

    /// <summary>空格且无任何覆盖：中立。</summary>
    Neutral,
}

/// <summary>格子归属。<see cref="Owner"/> 只在 <see cref="OwnershipKind.Occupied"/> 与 <see cref="OwnershipKind.Exclusive"/> 时非空。</summary>
public readonly record struct CellOwnership(OwnershipKind Kind, PlayerId? Owner)
{
    /// <summary>该格是否由 <paramref name="player"/> 控制（占据或独占）。信物控制判定（add-relic-system）直接用它。</summary>
    public bool IsControlledBy(PlayerId player) =>
        Owner == player && Kind is OwnershipKind.Occupied or OwnershipKind.Exclusive;
}

/// <summary>
/// 覆盖表：全项目<b>唯一</b>的覆盖关系实现。空格归属（领地层）与唯一覆盖查询（信物控制）都从这一份数据读取，
/// MUST NOT 在别处再算一遍覆盖——"领地层说独占、信物层判争议"就是第二套覆盖语义的典型症状。
/// </summary>
/// <remarks>
/// <para>棋子按 terrain 规格的覆盖关系提供所有者的覆盖：通常是几何相邻格，遇一格宽深水落到对岸，不向林地 / 障碍 / 未架桥深水、
/// 不向比自身高 2 的格覆盖，栅栏不挡。覆盖目标由 <see cref="GameBoard.CoverageTargets"/> 给出（唯一实现在 <see cref="Adjacency.CoverageTargets"/>），
/// 不在此处手写偏移或地形过滤。</para>
/// <para>按格存"覆盖者数量 + 唯一覆盖者"（design.md D1），三态判定与唯一覆盖查询都是 O(1) 读取。
/// 每次调用 <see cref="Compute"/> 全量重算，不做增量、不缓存（D2）。</para>
/// <para>规格：openspec/changes/add-territory-power/specs/coverage-territory</para>
/// </remarks>
public sealed class CoverageMap
{
    private readonly int _width;
    private readonly int _height;
    private readonly CellCoverage[] _coverage;
    private readonly CellOwnership[] _ownership;

    private CoverageMap(int width, int height, CellCoverage[] coverage, CellOwnership[] ownership)
    {
        _width = width;
        _height = height;
        _coverage = coverage;
        _ownership = ownership;
    }

    /// <summary>对当前盘面全量计算覆盖表与格子归属。不过滤任何玩家状态：弃赛者的遗留棋子照常覆盖（D7）。</summary>
    public static CoverageMap Compute(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        int width = board.Width;
        int height = board.Height;
        var coverers = new HashSet<PlayerId>?[width * height];

        foreach (Coord c in board.AllCoords())
        {
            if (board[c].Occupant is not { } occupant)
            {
                continue;
            }

            foreach (Coord target in board.CoverageTargets(c))
            {
                // CoverageTargets 已排除越界、障碍、未架桥深水、林地与崖上格；覆盖到此为止，不再向更远的格传递。
                (coverers[Index(width, target)] ??= []).Add(occupant.Owner);
            }
        }

        var coverage = new CellCoverage[width * height];
        var ownership = new CellOwnership[width * height];
        foreach (Coord c in board.AllCoords())
        {
            int index = Index(width, c);
            HashSet<PlayerId>? set = coverers[index];
            CellCoverage cell = set is null
                ? CellCoverage.None
                : new CellCoverage(set.Count, set.Count == 1 ? set.Single() : null);
            coverage[index] = cell;
            ownership[index] = Resolve(board[c], cell);
        }

        return new CoverageMap(width, height, coverage, ownership);
    }

    /// <summary>覆盖查询：该格有几名不同的覆盖者，以及唯一覆盖者是谁。被占据的格同样记录覆盖，占据优先由 <see cref="OwnershipOf"/> 处理。</summary>
    public CellCoverage CoverageOf(Coord c) => _coverage[RequireIndex(c)];

    /// <summary>
    /// 唯一覆盖查询：被恰好一名玩家覆盖时返回该玩家，否则 <c>null</c>。<b>不考虑占据</b>——被 A 占据、被 B 唯一覆盖的格返回 B。
    /// 信物控制是"直接占据或唯一覆盖"（设计文档 §7.3），add-relic-system MUST 读 <c>OwnershipOf(c).IsControlledBy(p)</c>，
    /// 它与本查询共用同一份覆盖表，且已把占据优先压在覆盖之上。
    /// </summary>
    public PlayerId? UniqueCoverer(Coord c) => CoverageOf(c).SoleCoverer;

    /// <summary>格子归属：障碍 / 占据 / 独占 / 争议 / 中立。与 <see cref="UniqueCoverer"/> 读取同一份覆盖数据。</summary>
    public CellOwnership OwnershipOf(Coord c) => _ownership[RequireIndex(c)];

    /// <summary>某玩家的独占空格集合，按字典序排列。棋子所在格不在其中——那些格只通过棋子的基础军势计分。</summary>
    public ImmutableArray<Coord> ExclusiveCellsOf(PlayerId player)
    {
        ImmutableArray<Coord>.Builder cells = ImmutableArray.CreateBuilder<Coord>();
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                var c = new Coord(x, y);
                CellOwnership ownership = _ownership[Index(_width, c)];
                if (ownership.Kind == OwnershipKind.Exclusive && ownership.Owner == player)
                {
                    cells.Add(c);
                }
            }
        }

        return cells.ToImmutable();
    }

    /// <summary>三态判定（设计文档 §7.1 / §7.2）：不可落子（障碍 / 未架桥深水）→ 占据优先 → 按覆盖者数量分独占 / 争议 / 中立。</summary>
    private static CellOwnership Resolve(Cell cell, CellCoverage coverage)
    {
        if (cell.Terrain == Terrain.Obstacle)
        {
            return new CellOwnership(OwnershipKind.Obstacle, null);
        }

        if (cell.Occupant is { } occupant)
        {
            return new CellOwnership(OwnershipKind.Occupied, occupant.Owner);
        }

        return coverage.CovererCount switch
        {
            0 => new CellOwnership(OwnershipKind.Neutral, null),
            1 => new CellOwnership(OwnershipKind.Exclusive, coverage.SoleCoverer),
            _ => new CellOwnership(OwnershipKind.Contested, null),
        };
    }

    private static int Index(int width, Coord c) => (c.Y * width) + c.X;

    private int RequireIndex(Coord c)
    {
        if (c.X >= _width || c.Y >= _height)
        {
            throw new ArgumentOutOfRangeException(nameof(c), c.ToNotation(), $"坐标超出棋盘范围（{_width}×{_height}）。");
        }

        return Index(_width, c);
    }
}
