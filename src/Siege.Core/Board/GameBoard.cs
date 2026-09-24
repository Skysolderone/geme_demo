using System.Collections.Immutable;
using System.Text;

namespace Siege.Core.Board;

/// <summary>
/// 棋盘权威状态。只回答"盘面此刻是什么样"——不知道回合、不知道玩家资源、不知道信物内容。
/// </summary>
/// <remarks>
/// <para>棋串、气、覆盖等全部是派生量，每次调用重算，不做增量维护也不缓存。
/// 设计文档 §9.2 / §10.1 要求实时重算；一次提子可能同时改变覆盖、棋串分裂、连珠断线与倍率，
/// 增量维护的组合爆炸不可控。</para>
/// <para>几何四邻、气边与覆盖关系的唯一实现都在 <see cref="Adjacency"/>，本类只是委托：
/// <see cref="Neighbors"/> 是几何邻居；棋串与气走 <see cref="LibertyNeighbors"/>；覆盖走 <see cref="CoverageTargets"/>。
/// MUST NOT 在别处手写邻居偏移或地形过滤。</para>
/// <para>规格：openspec/changes/add-board-core/specs/board-topology</para>
/// </remarks>
public sealed class GameBoard
{
    private readonly Occupant?[] _occupants;
    private readonly List<TerrainEdit> _edits;

    private GameBoard(MapData map)
        : this(map, map, new Occupant?[map.Width * map.Height], [])
    {
    }

    /// <summary>副本专用：直接接管一份已复制好的占用数组与改造列表，不经过 <see cref="Load"/>，不触发校验。</summary>
    private GameBoard(MapData baseMap, MapData map, Occupant?[] occupants, List<TerrainEdit> edits)
    {
        BaseMap = baseMap;
        Map = map;
        _occupants = occupants;
        _edits = edits;
    }

    /// <summary>按地图数据创建棋盘，先执行静态校验；不通过则抛 <see cref="MapValidationException"/>。</summary>
    public static GameBoard Load(MapData map)
    {
        MapValidationResult result = MapValidator.Validate(map);
        if (!result.IsValid)
        {
            throw new MapValidationException(result);
        }

        return new GameBoard(map);
    }

    /// <summary>
    /// 跳过静态校验创建棋盘。<b>仅供单元测试构造小盘面</b>——生产代码必须走 <see cref="Load"/>，
    /// 否则未校验的地图会把必死口袋、距离失衡这类问题带进对局。
    /// </summary>
    internal static GameBoard LoadUnvalidated(MapData map) => new(map);

    /// <summary>
    /// 副本：复制占用状态、共享不可变的 <see cref="MapData"/>，<b>不重跑</b>地图静态校验。
    /// 副本与原盘面之后的修改互不可见。批次结算层的合法性预演在副本上进行，
    /// 这是"预演不污染正式盘面"得以成立的前提。
    /// </summary>
    public GameBoard Clone() => new(BaseMap, Map, (Occupant?[])_occupants.Clone(), [.. _edits]);

    /// <summary>
    /// 地图数据。<b>地形不再是对局内的不变量</b>（artisan-terrain-edit）：每次改造经 <see cref="ApplyTerrainEdits"/> 换上一份新的
    /// <see cref="MapData"/>，气边、覆盖、棋串、气与信物控制都从这里实时导出，因此自动按新地形重算。
    /// 消费方 MUST NOT 把它缓存进字段——要缓存就得自己负责失效，本项目的做法是每次读 <c>Board.Map</c>。
    /// </summary>
    public MapData Map { get; private set; }

    /// <summary>本局开局时的地图（不含对局中的改造）。存档、日志与"改造了什么"的比对读它。</summary>
    public MapData BaseMap { get; }

    /// <summary>本局已完成的改造，按应用顺序。不可逆，只增不减（terrain-edit「改造不可逆」）。</summary>
    public ImmutableArray<TerrainEdit> TerrainEdits => [.. _edits];

    public int Width => Map.Width;

    public int Height => Map.Height;

    /// <summary>该格是否在棋盘范围内。</summary>
    public bool Contains(Coord c) => Map.Contains(c);

    /// <summary>取格子的只读视图。</summary>
    public Cell this[Coord c]
    {
        get
        {
            RequireInBounds(c);
            return new Cell(
                c,
                Map.TerrainAt(c),
                Map.BirthZoneOf(c),
                Map.RelicCells.ContainsKey(c),
                _occupants[Index(c)]);
        }
    }

    /// <summary>按确定性顺序（先行后列，自下而上）枚举全部格子。</summary>
    public IEnumerable<Coord> AllCoords() => Map.AllCoords();

    /// <summary>
    /// 四邻接邻居。委托给 <see cref="Adjacency.Neighbors"/> —— 全项目唯一的邻接实现。
    /// 返回上下左右四个方向中位于棋盘内的格子，按字典序排列；不含斜向。
    /// </summary>
    public ImmutableArray<Coord> Neighbors(Coord c)
    {
        RequireInBounds(c);
        return Adjacency.Neighbors(Width, Height, c);
    }

    /// <summary>
    /// 气边邻居：与 <paramref name="c"/> 之间存在气边的格（两格可落子、|Δh| ≤ 1、无栅栏）。
    /// 委托给 <see cref="Adjacency.LibertyNeighbors"/>，连接、棋串、气与围杀判定全部走这里。
    /// </summary>
    public ImmutableArray<Coord> LibertyNeighbors(Coord c)
    {
        RequireInBounds(c);
        return Adjacency.LibertyNeighbors(Map, c);
    }

    /// <summary>
    /// 覆盖目标：位于 <paramref name="c"/> 的棋子会向哪些格提供覆盖。委托给 <see cref="Adjacency.CoverageTargets"/>。
    /// </summary>
    public ImmutableArray<Coord> CoverageTargets(Coord c)
    {
        RequireInBounds(c);
        return Adjacency.CoverageTargets(Map, c);
    }

    /// <summary>该格所属棋串；空格或障碍返回 <c>null</c>。</summary>
    public Group? GroupAt(Coord c)
    {
        RequireInBounds(c);
        Occupant? occupant = _occupants[Index(c)];
        if (occupant is null)
        {
            return null;
        }

        PlayerId owner = occupant.Value.Owner;
        var visited = new HashSet<Coord> { c };
        var queue = new Queue<Coord>();
        queue.Enqueue(c);

        while (queue.Count > 0)
        {
            Coord current = queue.Dequeue();
            foreach (Coord n in LibertyNeighbors(current))
            {
                if (visited.Contains(n))
                {
                    continue;
                }

                // 棋子类型不影响棋串归属；不同玩家的棋子绝不合并；崖壁 / 栅栏两侧的己子不成串（无气边）。
                if (_occupants[Index(n)] is { } other && other.Owner == owner)
                {
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return new Group(owner, visited.Order().ToImmutableArray());
    }

    /// <summary>
    /// 该格此刻能否作为气（terrain-surfaces design D1 / D3 的唯一落点）：为空的可落子格，且地表不是浅滩。
    /// 浅滩只影响"空格能否作为气"——气边、连串、浅滩上棋子自己的气都不受影响。气、空区与活形都经这里判断，不另写地表比较。
    /// </summary>
    public bool GivesLiberty(Coord c) => this[c].IsPlayableEmpty && Map.SurfaceAt(c) != Surface.Shallows;

    /// <summary>
    /// 棋串的气：与该棋串任一棋子之间存在气边、且 <see cref="GivesLiberty"/> 的格（按格去重）。
    /// 被任何玩家棋子占据的格、空浅滩、障碍格、未架桥深水、越界方向，以及因崖壁或栅栏而无气边的格都不计气。
    /// </summary>
    public ImmutableArray<Coord> LibertiesOf(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var liberties = new HashSet<Coord>();
        foreach (Coord stone in group.Stones)
        {
            foreach (Coord n in LibertyNeighbors(stone))
            {
                if (GivesLiberty(n))
                {
                    liberties.Add(n);
                }
            }
        }

        return liberties.Order().ToImmutableArray();
    }

    /// <summary>
    /// 与棋串有气边相连、为空却不能作为气的格——即贴着它的空浅滩（按格去重、坐标序）。
    /// 供表现层把"浅滩：不算气"与真正的气分开标出，表现层不自己遍历邻接（tactical-layers「新地表的规则标示」）。
    /// </summary>
    public ImmutableArray<Coord> EmptyShallowsBeside(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var cells = new HashSet<Coord>();
        foreach (Coord stone in group.Stones)
        {
            foreach (Coord n in LibertyNeighbors(stone))
            {
                if (this[n].IsPlayableEmpty && !GivesLiberty(n))
                {
                    cells.Add(n);
                }
            }
        }

        return cells.Order().ToImmutableArray();
    }

    /// <summary>棋串是否无气。</summary>
    public bool IsCaptured(Group group) => LibertiesOf(group).IsEmpty;

    /// <summary>枚举指定玩家的全部棋串，按其最小坐标的字典序排列。</summary>
    public ImmutableArray<Group> GroupsOf(PlayerId player) => ScanGroups(o => o.Owner == player);

    /// <summary>
    /// 全盘棋串枚举：不依赖玩家名册，返回盘面上全部棋串，按各棋串最小坐标的字典序排列。
    /// 同时提子（设计文档 §6.1 第 4 步）必须先在全盘范围算出无气棋串的并集再统一移除，
    /// 而不是按名册逐人遍历、边遍历边移除。
    /// </summary>
    public ImmutableArray<Group> AllGroups() => ScanGroups(static _ => true);

    /// <summary>按确定性坐标顺序扫描全盘，把满足条件的占用格所属棋串各收集一次。</summary>
    private ImmutableArray<Group> ScanGroups(Func<Occupant, bool> include)
    {
        var seen = new HashSet<Coord>();
        ImmutableArray<Group>.Builder groups = ImmutableArray.CreateBuilder<Group>();

        foreach (Coord c in AllCoords())
        {
            if (seen.Contains(c) || _occupants[Index(c)] is not { } occupant || !include(occupant))
            {
                continue;
            }

            Group group = GroupAt(c)!;
            foreach (Coord stone in group.Stones)
            {
                seen.Add(stone);
            }

            groups.Add(group);
        }

        return groups.ToImmutable();
    }

    /// <summary>
    /// 棋盘是否仍存在至少一个可落子空格。用于设计文档 §12.3 的终局条件 3。
    /// 只统计地形为可落子且当前无占用的格子。
    /// </summary>
    public bool HasPlayableEmptyCell() => AllCoords().Any(c => this[c].IsPlayableEmpty);

    /// <summary>
    /// 确定性盘面序列化。内容<b>只含</b>每格的占用者与棋子类型，以及对局中完成的地形改造（桥 / 栅栏 / 被烧的地表）；
    /// 不含手牌、征募结果、信物控制、势力值、行动顺序或当前行动者。
    /// 存档、日志、结算核对与弃赛快照都用它；盘面同形禁则比较的是由它投影出的 <see cref="SuperkoKey"/>（不含棋子类型，superko-occupancy）。
    /// </summary>
    /// <remarks>
    /// 改造段是<b>增量</b>：只写对局中新增的改造，地图预置的桥与栅栏不写。理由有三——
    /// ① 预置设施在一局内恒定，写不写对同形比对等价；② 无改造时输出与改造上线之前逐字节相同，旧存档与旧历史天然按"无改造"读入（R-6）；
    /// ③ 改造不可逆且同一目标只能改一次，"已应用集合"与"当前地形"一一对应，排序后即规范形。
    /// 格式：棋子网格后接 <c>|</c> 与按记法字典序排好的改造列表，如 <c>…|B:F7,F:F6-G6</c>；无改造时不写 <c>|</c>。
    /// </remarks>
    public string Serialize()
    {
        var sb = new StringBuilder(Width * Height * 2 + Height);
        for (int y = 0; y < Height; y++)
        {
            if (y > 0)
            {
                sb.Append('/');
            }

            for (int x = 0; x < Width; x++)
            {
                Occupant? occupant = _occupants[Index(new Coord(x, y))];
                if (occupant is null)
                {
                    sb.Append("--");
                }
                else
                {
                    int owner = occupant.Value.Owner.Value;
                    if (owner is < 0 or > 15)
                    {
                        throw new SiegeRuleException($"盘面序列化只支持玩家编号 0–15，实际为 {owner}。");
                    }

                    sb.Append(owner.ToString("X1"));
                    sb.Append(TypeCode(occupant.Value.Type));
                }
            }
        }

        if (_edits.Count > 0)
        {
            sb.Append(TerrainSeparator);
            sb.Append(string.Join(",", _edits.Select(e => e.ToString()).Order(StringComparer.Ordinal)));
        }

        return sb.ToString();
    }

    /// <summary>棋子网格与改造段之间的分隔符。</summary>
    private const char TerrainSeparator = '|';

    /// <summary>
    /// 从 <see cref="Serialize"/> 的输出恢复盘面：先按地图静态校验创建空盘，再逐格写入占用。
    /// 这是类型码 ↔ <see cref="PieceType"/> 映射的反向实现，与 <see cref="TypeCode"/> 同在一处，MUST NOT 在别处再写一份。
    /// 格数、行数或类型码与地图不符即视为存档损坏，抛 <see cref="FormatException"/>。
    /// </summary>
    public static GameBoard Restore(MapData map, string serialized)
    {
        GameBoard board = Load(map);
        board.Fill(serialized);
        return board;
    }

    /// <summary>跳过静态校验的恢复。<b>仅供单元测试</b>在合成小盘面上做存档往返。</summary>
    internal static GameBoard RestoreUnvalidated(MapData map, string serialized)
    {
        GameBoard board = LoadUnvalidated(map);
        board.Fill(serialized);
        return board;
    }

    private void Fill(string serialized)
    {
        ArgumentNullException.ThrowIfNull(serialized);
        string text = serialized.Trim();

        // 改造段先读：气边、覆盖与"该格可不可落子"都依赖地形，棋子必须写进改造后的地形里
        // （否则本局架过桥的格会被 Place 当成未架桥的深水拒绝）。缺该段即"无改造"，旧存档由此天然回填（R-6）。
        int bar = text.IndexOf(TerrainSeparator, StringComparison.Ordinal);
        if (bar >= 0)
        {
            string tail = text[(bar + 1)..];
            text = text[..bar];
            if (tail.Length > 0)
            {
                try
                {
                    ApplyTerrainEdits(tail.Split(',').Select(TerrainEdit.Parse));
                }
                catch (SiegeRuleException ex)
                {
                    throw new FormatException($"盘面序列化的改造段与地图不符：{ex.Message}", ex);
                }
            }
        }

        string[] rows = text.Split('/');
        if (rows.Length != Height)
        {
            throw new FormatException($"盘面序列化行数 {rows.Length} 与地图高度 {Height} 不符。");
        }

        for (int y = 0; y < Height; y++)
        {
            string row = rows[y];
            if (row.Length != Width * 2)
            {
                throw new FormatException($"盘面序列化第 {y + 1} 行长度 {row.Length} 与地图宽度 {Width} 不符。");
            }

            for (int x = 0; x < Width; x++)
            {
                char ownerCode = row[x * 2];
                char typeCode = row[(x * 2) + 1];
                if (ownerCode == '-' && typeCode == '-')
                {
                    continue;
                }

                int owner = Convert.ToInt32(ownerCode.ToString(), 16);
                Place(new Coord(x, y), new PlayerId(owner), TypeFromCode(typeCode));
            }
        }
    }

    /// <summary>
    /// 最小写入原语：把棋子放到指定格。<b>不做任何规则判定</b>——
    /// 合法性预演、同时提子与结算顺序由批次结算层负责。
    /// </summary>
    public void Place(Coord c, PlayerId owner, PieceType type)
    {
        RequireInBounds(c);
        if (!Map.IsPlayable(c))
        {
            string reason = Map.TerrainData.IsUnbridgedDeepWater(c) ? "未架桥的深水格" : "障碍格";
            throw new SiegeRuleException($"目标格不可落子：{c.ToNotation()} 是{reason}。");
        }

        if (_occupants[Index(c)] is not null)
        {
            throw new SiegeRuleException($"目标格已被占据：{c.ToNotation()}。");
        }

        _occupants[Index(c)] = new Occupant(owner, type);
    }

    /// <summary>
    /// 最小写入原语：同时应用一组地形改造。<b>不做任何规则判定</b>——目标合法性（几何四邻、只有匠人能带、批内唯一）
    /// 由 <see cref="TerrainEditRules"/> 与批次结算层负责；本方法只经唯一写入口 <see cref="TerrainWriter.ApplyAll"/> 换上新地形。
    /// 整组改造同时生效、结果与顺序无关；任一目标不满足动作前提即整组抛出，地形不变（原子）。
    /// </summary>
    public void ApplyTerrainEdits(IEnumerable<TerrainEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        TerrainEdit[] list = [.. edits];
        if (list.Length == 0)
        {
            return;
        }

        Map = TerrainWriter.ApplyAll(Map, list);
        _edits.AddRange(list);
    }

    /// <summary>最小写入原语：清除指定格的占用。</summary>
    public void Clear(Coord c)
    {
        RequireInBounds(c);
        _occupants[Index(c)] = null;
    }

    /// <summary>
    /// 批量移除：一次调用移除一组坐标上的棋子。先把输入全部物化并做越界检查，
    /// 全部合法后才写入——不存在"部分已移除"的可观察中间状态。清除空格是无害的空操作。
    /// 何时移除、移除哪些由批次结算层决定，本层不做任何规则判定。
    /// </summary>
    public void RemoveStones(IEnumerable<Coord> coords)
    {
        ArgumentNullException.ThrowIfNull(coords);
        Coord[] targets = [.. coords];
        foreach (Coord c in targets)
        {
            RequireInBounds(c);
        }

        foreach (Coord c in targets)
        {
            _occupants[Index(c)] = null;
        }
    }

    private int Index(Coord c) => (c.Y * Width) + c.X;

    private void RequireInBounds(Coord c)
    {
        if (!Contains(c))
        {
            throw new ArgumentOutOfRangeException(
                nameof(c), c.ToNotation(), $"坐标超出棋盘范围（{Width}×{Height}）。");
        }
    }

    private static PieceType TypeFromCode(char code) => code switch
    {
        'B' => PieceType.Basic,
        'F' => PieceType.Fortress,
        'L' => PieceType.Line,
        'M' => PieceType.Multiplier,
        'S' => PieceType.Synergy,
        'A' => PieceType.Artisan,
        _ => throw new FormatException($"未知棋子类型码：'{code}'。"),
    };

    /// <summary>
    /// 同形比对键（superko-occupancy D1）：把 <see cref="Serialize"/> 的输出投影成"每格占用者 + 设施与地表"，<b>去掉棋子类型</b>。
    /// 棋子网格每格两字符（占用者 + 类型码）只留首字符，行分隔 <c>/</c> 与 <c>|</c> 之后的改造段原样保留。
    /// 盘面同形禁则只经它比较（<see cref="Batch.BoardHistory"/> 是唯一调用方）；序列化本身仍含类型，存档、日志、结算核对照旧。
    /// </summary>
    /// <remarks>
    /// 这是比对键投影的<b>唯一实现</b>，紧挨类型码映射放置：序列化格式一改，这里必须同步。
    /// 只做字符串变换、不解析类型码，因此对任意"每格两字符"的串都是确定性的；行长为奇数即格式损坏，抛 <see cref="FormatException"/>。
    /// </remarks>
    public static string SuperkoKey(string serialized)
    {
        ArgumentNullException.ThrowIfNull(serialized);
        int bar = serialized.IndexOf(TerrainSeparator, StringComparison.Ordinal);
        string grid = bar >= 0 ? serialized[..bar] : serialized;
        var sb = new StringBuilder(serialized.Length);
        int column = 0;
        foreach (char ch in grid)
        {
            if (ch == '/')
            {
                RequireEvenRow(column, serialized);
                column = 0;
                sb.Append(ch);
                continue;
            }

            if (column % 2 == 0)
            {
                sb.Append(ch);
            }

            column++;
        }

        RequireEvenRow(column, serialized);
        if (bar >= 0)
        {
            sb.Append(serialized, bar, serialized.Length - bar);
        }

        return sb.ToString();

        static void RequireEvenRow(int length, string text)
        {
            if (length % 2 != 0)
            {
                throw new FormatException($"盘面序列化的行长度 {length} 不是每格两字符：\"{text}\"。");
            }
        }
    }

    private static char TypeCode(PieceType type) => type switch
    {
        PieceType.Basic => 'B',
        PieceType.Fortress => 'F',
        PieceType.Line => 'L',
        PieceType.Multiplier => 'M',
        PieceType.Synergy => 'S',
        PieceType.Artisan => 'A',
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。"),
    };
}
