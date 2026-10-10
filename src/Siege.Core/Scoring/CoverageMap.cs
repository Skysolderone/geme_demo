using System.Buffers;
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

/// <summary>
/// 一格的一个覆盖来源：提供覆盖的棋子所在格，以及它与被覆盖格是否几何相邻（不相邻只可能是隔着一格深水，terrain「覆盖关系」第三步）。
/// 供表现层解释两种读法的差集（tactical-layers「差集可由地形解释」）——表现层只读这一位，不自己算邻接。
/// </summary>
public readonly record struct CoverageSource(Coord Stone, bool Adjacent);

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
/// 每次调用 <see cref="Compute"/> 全量重算，不做增量、不缓存（D2）。不为每格分配集合（ai-turn-speed D2「数据结构」）：
/// 段 C2 起只为可落子格紧凑存放，覆盖者由每格的来源（至多十余个）数出，结果与逐格建集合逐项相同。</para>
/// <para>规格：openspec/changes/add-territory-power/specs/coverage-territory</para>
/// </remarks>
public sealed class CoverageMap
{
    private readonly int _width;
    private readonly int _height;

    /// <summary>计算时盘面的可落子格下标（升序）与"格下标 → 序号"（不可落子为 −1），取自盘面的地图派生表（段 C2）。</summary>
    private readonly ImmutableArray<int> _playable;
    private readonly ImmutableArray<int> _ordinal;

    /// <summary>按可落子格序号紧凑存放的覆盖与归属（段 C2）：不可落子格不占位，查询时按"无覆盖、障碍、无来源"作答。</summary>
    private readonly CellCoverage[] _coverage;
    private readonly CellOwnership[] _ownership;

    /// <summary>覆盖来源的扁平存放（段 C2）：序号 o 的来源是 <c>_sources[_sourceStart[o] .. _sourceStart[o + 1])</c>，按 <see cref="Compute"/> 的遍历顺序。</summary>
    private readonly int[] _sourceStart;
    private readonly CoverageSource[] _sources;

    private CoverageMap(
        int width,
        int height,
        ImmutableArray<int> playable,
        ImmutableArray<int> ordinal,
        CellCoverage[] coverage,
        CellOwnership[] ownership,
        int[] sourceStart,
        CoverageSource[] sources)
    {
        _width = width;
        _height = height;
        _playable = playable;
        _ordinal = ordinal;
        _coverage = coverage;
        _ownership = ownership;
        _sourceStart = sourceStart;
        _sources = sources;
    }

    /// <summary>对当前盘面全量计算覆盖表与格子归属。不过滤任何玩家状态：弃赛者的遗留棋子照常覆盖（D7）。</summary>
    /// <remarks>
    /// 段 C2（board-link-mode 裁决 C18 / P2，决策不变的数据结构改写）：棋子按格下标升序遍历（即坐标字典序，与逐格枚举同序）；
    /// 来源按同一遍历顺序写进扁平数组，覆盖者数量与唯一覆盖者由每格的来源数出（不为每格建集合）；
    /// 结果只为可落子格存放（<see cref="GameBoard.PlayableIndices"/>，与 <c>board[c].Terrain</c> 同一份逐格标志）——覆盖目标恒为可落子格
    /// （<see cref="Adjacency.CoverageTargets"/> 的接收条件含可落子，违反即抛出），不可落子格的归属即三态判定的第一步"障碍"。
    /// 与逐格建表的写法逐项相同（<c>盘面派生量优化对照Tests</c> 的参考实现守住）。
    /// </remarks>
    public static CoverageMap Compute(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        int width = board.Width;
        int height = board.Height;
        ReadOnlySpan<Occupant?> occupants = board.Occupants;
        ImmutableArray<int> playable = board.PlayableIndices;
        ImmutableArray<int> ordinal = board.PlayableOrdinals;
        int count = playable.Length;
        var sourceStart = new int[count + 1];

        // 第一遍：每格来源数。
        int total = 0;
        for (int s = 0; s < occupants.Length; s++)
        {
            if (occupants[s] is null)
            {
                continue;
            }

            Coord c = board.CoordOfIndex(s);
            foreach (Coord target in board.CoverageTargets(c))
            {
                // CoverageTargets 已排除越界、障碍、未架桥深水、林地与崖上格；覆盖到此为止，不再向更远的格传递。
                sourceStart[OrdinalOfTarget(ordinal, width, c, target) + 1]++;
                total++;
            }
        }

        // 第二遍：来源按同一遍历顺序写进扁平数组。
        for (int o = 0; o < count; o++)
        {
            sourceStart[o + 1] += sourceStart[o];
        }

        CoverageSource[] sources = total == 0 ? [] : new CoverageSource[total];
        if (total > 0)
        {
            int[] cursor = ArrayPool<int>.Shared.Rent(Math.Max(1, count));
            try
            {
                Array.Copy(sourceStart, cursor, count);
                for (int s = 0; s < occupants.Length; s++)
                {
                    if (occupants[s] is null)
                    {
                        continue;
                    }

                    Coord c = board.CoordOfIndex(s);
                    foreach (Coord target in board.CoverageTargets(c))
                    {
                        sources[cursor[ordinal[Index(width, target)]]++] = new CoverageSource(c, Adjacency.AreAdjacent(c, target));
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(cursor);
            }
        }

        // 覆盖者计数：按格读它的来源（来源次序即遍历次序，首个来源的所有者即"第一名覆盖者"），数不同的所有者——同一玩家多枚棋子只计一名。
        // 归属：可落子格按占据优先、覆盖者数量判定（不可落子格不存放，查询即障碍）。
        var coverage = new CellCoverage[count];
        var ownership = new CellOwnership[count];
        Span<PlayerId> distinct = stackalloc PlayerId[16];
        for (int o = 0; o < count; o++)
        {
            int start = sourceStart[o];
            int end = sourceStart[o + 1];
            CellCoverage cell = CellCoverage.None;
            if (end > start)
            {
                Span<PlayerId> seen = end - start <= distinct.Length ? distinct : new PlayerId[end - start];
                int found = 0;
                for (int k = start; k < end; k++)
                {
                    Coord stone = sources[k].Stone;
                    PlayerId owner = occupants[Index(width, stone)]!.Value.Owner;
                    if (!seen[..found].Contains(owner))
                    {
                        seen[found++] = owner;
                    }
                }

                cell = found == 1 ? new CellCoverage(1, seen[0]) : new CellCoverage(found, null);
            }

            coverage[o] = cell;
            ownership[o] = ResolvePlayable(occupants[playable[o]], cell);
        }

        return new CoverageMap(width, height, playable, ordinal, coverage, ownership, sourceStart, sources);
    }

    /// <summary>覆盖查询：该格有几名不同的覆盖者，以及唯一覆盖者是谁。被占据的格同样记录覆盖，占据优先由 <see cref="OwnershipOf"/> 处理。</summary>
    public CellCoverage CoverageOf(Coord c)
    {
        int o = _ordinal[RequireIndex(c)];
        return o >= 0 ? _coverage[o] : CellCoverage.None;
    }

    /// <summary>
    /// 唯一覆盖查询：被恰好一名玩家覆盖时返回该玩家，否则 <c>null</c>。<b>不考虑占据</b>——被 A 占据、被 B 唯一覆盖的格返回 B。
    /// 信物控制是"直接占据或唯一覆盖"（设计文档 §7.3），add-relic-system MUST 读 <c>OwnershipOf(c).IsControlledBy(p)</c>，
    /// 它与本查询共用同一份覆盖表，且已把占据优先压在覆盖之上。
    /// </summary>
    public PlayerId? UniqueCoverer(Coord c) => CoverageOf(c).SoleCoverer;

    /// <summary>格子归属：障碍 / 占据 / 独占 / 争议 / 中立。与 <see cref="UniqueCoverer"/> 读取同一份覆盖数据。</summary>
    public CellOwnership OwnershipOf(Coord c)
    {
        int o = _ordinal[RequireIndex(c)];
        return o >= 0 ? _ownership[o] : new CellOwnership(OwnershipKind.Obstacle, null);
    }

    /// <summary>
    /// 该格的全部覆盖来源（按 <see cref="Compute"/> 的遍历顺序，即棋子坐标字典序），无覆盖为空。
    /// 只读查询：来源在 <see cref="Compute"/> 遍历覆盖目标时顺手记录，不另算一遍覆盖关系。
    /// </summary>
    public ImmutableArray<CoverageSource> SourcesOf(Coord c)
    {
        int o = _ordinal[RequireIndex(c)];
        if (o < 0)
        {
            return ImmutableArray<CoverageSource>.Empty;
        }

        int start = _sourceStart[o];
        int count = _sourceStart[o + 1] - start;
        return count == 0 ? ImmutableArray<CoverageSource>.Empty : ImmutableArray.Create(_sources, start, count);
    }

    /// <summary>某玩家的独占空格集合，按字典序排列。棋子所在格不在其中——那些格只通过棋子的基础军势计分。</summary>
    /// <remarks>段 C2：只扫可落子格（序号升序即字典序）——不可落子格的归属恒为障碍，不可能是独占。格下标换回坐标走 <see cref="GameBoard.CoordOfIndex(int, int)"/>（实现裁决 P7）。</remarks>
    public ImmutableArray<Coord> ExclusiveCellsOf(PlayerId player)
    {
        ImmutableArray<Coord>.Builder cells = ImmutableArray.CreateBuilder<Coord>();
        for (int o = 0; o < _ownership.Length; o++)
        {
            CellOwnership ownership = _ownership[o];
            if (ownership.Kind == OwnershipKind.Exclusive && ownership.Owner == player)
            {
                int i = _playable[o];
                cells.Add(GameBoard.CoordOfIndex(_width, i));
            }
        }

        return cells.ToImmutable();
    }

    /// <summary>
    /// 三态判定（设计文档 §7.1 / §7.2）：不可落子（障碍 / 未架桥深水）→ 占据优先 → 按覆盖者数量分独占 / 争议 / 中立。
    /// 只对可落子格调用（不可落子格的归属即第一步"障碍"，见 <see cref="OwnershipOf"/>）。
    /// </summary>
    private static CellOwnership ResolvePlayable(Occupant? occupant, CellCoverage coverage)
    {
        if (occupant is { } stone)
        {
            return new CellOwnership(OwnershipKind.Occupied, stone.Owner);
        }

        return coverage.CovererCount switch
        {
            0 => new CellOwnership(OwnershipKind.Neutral, null),
            1 => new CellOwnership(OwnershipKind.Exclusive, coverage.SoleCoverer),
            _ => new CellOwnership(OwnershipKind.Contested, null),
        };
    }

    /// <summary>覆盖目标在紧凑表里的序号。目标不是可落子格即与 <see cref="Adjacency.CoverageTargets"/> 的接收条件矛盾，响亮失败。</summary>
    private static int OrdinalOfTarget(ImmutableArray<int> ordinal, int width, Coord source, Coord target)
    {
        int o = ordinal[Index(width, target)];
        if (o < 0)
        {
            throw new SiegeRuleException($"覆盖目标 {target.ToNotation()}（来自 {source.ToNotation()}）不是可落子格：与覆盖关系的接收条件矛盾。");
        }

        return o;
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
