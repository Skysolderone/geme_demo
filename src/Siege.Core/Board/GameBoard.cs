using System.Buffers;
using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// 棋盘权威状态。只回答"盘面此刻是什么样"——不知道回合、不知道玩家资源、不知道信物内容。
/// </summary>
/// <remarks>
/// <para>棋串、气、覆盖等全部是派生量，每次调用重算，不做增量维护。
/// 设计文档 §9.2 / §10.1 要求实时重算；一次提子可能同时改变覆盖、棋串分裂、连珠断线与倍率，
/// 增量维护的组合爆炸不可控。</para>
/// <para>唯一的例外是<b>盘面未变时的记忆化</b>（ai-turn-speed D2；board-link-mode 段 C2 加上 <see cref="Serialize"/>）：全盘棋串（<see cref="AllGroups"/>）、活形报告（<see cref="LifeShapeReport.Analyze"/>）
/// 与盘面序列化按突变版本号记忆——每次写入原语（<see cref="Place"/> / <see cref="Clear"/> / <see cref="RemoveStones"/> / <see cref="ApplyTerrainEdits"/>）都让版本号 +1
/// （空集合的 <see cref="RemoveStones"/> 什么都没写，不算写入），版本号不同即整份重算，副本从零起。它不是增量维护：命中时返回的是同一盘面上同一份全量结果，任何一次写入之后的第一次查询都重算。</para>
/// <para>地图派生量（每格可落子 / 出生区 / 信物格、气边邻居、覆盖目标、可落子格清单）按<b>当前 <see cref="MapData"/> 实例</b>逐格惰性记忆：
/// 改造换上新地图即换表（<see cref="ApplyTerrainEdits"/>：段 C2 起新表由旧表派生，只作废改造目标周围 2 格，与从零建表逐项相同），
/// 副本与原盘面共用同一地图时共用同一张表。表里的每一项都由唯一实现（<see cref="MapData"/> 的查询与 <see cref="Adjacency"/>）算出，本类不另写地形判定。</para>
/// <para>几何四邻、气边与覆盖关系的唯一实现都在 <see cref="Adjacency"/>，本类只是委托：
/// <see cref="Neighbors"/> 是几何邻居；棋串与气走 <see cref="LibertyNeighbors"/>；覆盖走 <see cref="CoverageTargets"/>。
/// MUST NOT 在别处手写邻居偏移或地形过滤。</para>
/// <para>规格：openspec/changes/add-board-core/specs/board-topology</para>
/// </remarks>
public sealed partial class GameBoard
{
    private Occupant?[] _occupants;
    private readonly List<TerrainEdit> _edits;
    private MapTables _tables;

    /// <summary>突变版本号：每次写入原语 +1。棋串与活形的记忆化只在版本号相同时命中。</summary>
    private int _version;
    private int _groupsVersion = -1;
    private ImmutableArray<Group> _groups;
    private int _lifeVersion = -1;
    private LifeShapeReport? _life;
    private int _serialVersion = -1;
    private string? _serial;

    private GameBoard(MapData map)
        : this(map, map, new Occupant?[map.Width * map.Height], [], new MapTables(map))
    {
    }

    /// <summary>副本专用：直接接管一份已复制好的占用数组与改造列表，不经过 <see cref="Load"/>，不触发校验。</summary>
    private GameBoard(MapData baseMap, MapData map, Occupant?[] occupants, List<TerrainEdit> edits, MapTables tables)
    {
        BaseMap = baseMap;
        Map = map;
        _occupants = occupants;
        _edits = edits;
        _tables = tables;
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
    /// 副本：复制占用状态、共享不可变的 <see cref="MapData"/>（及其派生表），<b>不重跑</b>地图静态校验。
    /// 副本与原盘面之后的修改互不可见；副本的棋串 / 活形记忆从零起。批次结算层的合法性预演在副本上进行，
    /// 这是"预演不污染正式盘面"得以成立的前提。
    /// </summary>
    public GameBoard Clone() => new(BaseMap, Map, (Occupant?[])_occupants.Clone(), [.. _edits], _tables);

    /// <summary>
    /// 地图数据。<b>地形不再是对局内的不变量</b>（artisan-terrain-edit）：每次改造经 <see cref="ApplyTerrainEdits"/> 换上一份新的
    /// <see cref="MapData"/>，气边、覆盖、棋串、气与信物控制都从这里实时导出，因此自动按新地形重算。
    /// 消费方 MUST NOT 把它缓存进字段——要缓存就得自己负责失效，本项目的做法是每次读 <c>Board.Map</c>。
    /// </summary>
    public MapData Map { get; private set; }

    /// <summary>本局开局时的地图（不含对局中的改造；open-map 起地图整局不变，<see cref="Map"/> = 它 + 全部改造）。存档、日志首部的地图摘要与"改造了什么"的比对读它。</summary>
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
            int i = Index(c);
            return new Cell(c, _tables.TerrainAt(i, c), _tables.BirthZoneOf(i, c), _tables.IsRelicCell(i, c), _occupants[i]);
        }
    }

    /// <summary>该格的占用者（无棋子为 <c>null</c>）。与 <c>this[c].Occupant</c> 相同，只是不构造整格视图，供热路径逐格读取。</summary>
    internal Occupant? OccupantAt(Coord c)
    {
        RequireInBounds(c);
        return _occupants[Index(c)];
    }

    /// <summary>
    /// 全部格的占用者，按格下标（<c>y * 宽 + x</c>，即 <see cref="AllCoords"/> 的次序）只读暴露，供热路径整盘扫描（段 C2：不逐格经坐标枚举器与越界检查）。
    /// 只读视图，与 <see cref="OccupantAt"/> 逐格相同；盘面之后的写入会反映在同一视图上，调用方不得跨写入持有。
    /// </summary>
    internal ReadOnlySpan<Occupant?> Occupants => _occupants;

    /// <summary>
    /// 当前地图全部可落子格的格下标，升序（即 <see cref="AllCoords"/> 里可落子的那些，次序不变）。按当前 <see cref="MapData"/> 实例的派生表记忆（与逐格可落子标志同一份），
    /// 改造 / 链接换图即换表重算。供整盘扫描只走可落子格（段 C2）；不可落子格的结论由调用方按"不可落子"的既有口径给出。
    /// </summary>
    internal ImmutableArray<int> PlayableIndices => _tables.PlayableIndices();

    /// <summary>格下标 → 该格在 <see cref="PlayableIndices"/> 里的序号，不可落子格为 −1（同一张派生表、同一次算出）。供按可落子格紧凑存放的派生表（段 C2：覆盖表）换算下标。</summary>
    internal ImmutableArray<int> PlayableOrdinals => _tables.PlayableOrdinals();

    /// <summary>
    /// 活形报告的记忆槽（ai-turn-speed D2）：<see cref="LifeShapeReport.Analyze"/> 是唯一读写方——盘面版本号未变即返回上一次的全量结果，
    /// 任何写入原语之后读到 <c>null</c>、必须重算。本类不解释报告内容。
    /// </summary>
    internal LifeShapeReport? LifeMemo
    {
        get => _lifeVersion == _version ? _life : null;
        set
        {
            _life = value;
            _lifeVersion = _version;
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
    /// 委托给 <see cref="Adjacency.LibertyNeighbors"/>，连接、棋串、气与围杀判定全部走这里（结果按当前地图逐格记忆，改造后整表重算）。
    /// </summary>
    public ImmutableArray<Coord> LibertyNeighbors(Coord c)
    {
        RequireInBounds(c);
        return _tables.LibertyNeighbors(Index(c), c);
    }

    /// <summary>
    /// 眼边邻居：与 <paramref name="c"/> 之间存在眼边的格（两格可落子、|Δh| ≤ 1，<b>不看栅栏</b>；fence-eye-rule D1）。
    /// 委托给 <see cref="Adjacency.EyeNeighbors"/>，<b>只</b>供活形判定（<see cref="LifeShapeReport"/> 的空区、封闭、眼空间归属与方四）使用；
    /// 气、棋串、提子一律走 <see cref="LibertyNeighbors"/>（结果按当前地图逐格记忆，改造后整表重算）。
    /// </summary>
    public ImmutableArray<Coord> EyeNeighbors(Coord c)
    {
        RequireInBounds(c);
        return _tables.EyeNeighbors(Index(c), c);
    }

    /// <summary>
    /// 覆盖目标：位于 <paramref name="c"/> 的棋子会向哪些格提供覆盖。委托给 <see cref="Adjacency.CoverageTargets"/>（结果按当前地图逐格记忆，改造后整表重算）。
    /// </summary>
    public ImmutableArray<Coord> CoverageTargets(Coord c)
    {
        RequireInBounds(c);
        return _tables.CoverageTargets(Index(c), c);
    }

    /// <summary>该格所属棋串；空格或障碍返回 <c>null</c>。</summary>
    public Group? GroupAt(Coord c)
    {
        RequireInBounds(c);
        int start = Index(c);
        return _occupants[start] is { } occupant ? CollectGroup(start, occupant.Owner, new bool[_occupants.Length]) : null;
    }

    /// <summary>
    /// 沿气边从 <paramref name="start"/> 收集同一所有者的棋串：棋子类型不影响棋串归属；不同玩家的棋子绝不合并；
    /// 崖壁 / 栅栏两侧的己子不成串（无气边）。<paramref name="visited"/> 标记本串全部棋子（供全盘扫描跳过已收集的串）。
    /// 棋子按坐标序（先行后列）返回——格下标 <c>y * 宽 + x</c> 的升序即该序。
    /// </summary>
    private Group CollectGroup(int start, PlayerId owner, bool[] visited)
    {
        var stones = new List<int> { start };
        visited[start] = true;
        for (int head = 0; head < stones.Count; head++)
        {
            int current = stones[head];
            foreach (Coord n in _tables.LibertyNeighbors(current, CoordAt(current)))
            {
                int ni = Index(n);
                if (!visited[ni] && _occupants[ni] is { } other && other.Owner == owner)
                {
                    visited[ni] = true;
                    stones.Add(ni);
                }
            }
        }

        stones.Sort();
        ImmutableArray<Coord>.Builder coords = ImmutableArray.CreateBuilder<Coord>(stones.Count);
        foreach (int i in stones)
        {
            coords.Add(CoordAt(i));
        }

        return new Group(owner, coords.MoveToImmutable());
    }

    /// <summary>
    /// 该格此刻能否作为气（terrain-surfaces design D1 / D3 的唯一落点）：为空的可落子格，且地表不是浅滩。
    /// 浅滩只影响"空格能否作为气"——气边、连串、浅滩上棋子自己的气都不受影响。气、空区与活形都经这里判断，不另写地表比较。
    /// </summary>
    public bool GivesLiberty(Coord c)
    {
        RequireInBounds(c);
        int i = Index(c);
        return _occupants[i] is null && _tables.GivesLibertyWhenEmpty(i, c);
    }

    /// <summary>
    /// 棋串的气：与该棋串任一棋子之间存在气边、且 <see cref="GivesLiberty"/> 的格（按格去重）。
    /// 被任何玩家棋子占据的格、空浅滩、障碍格、未架桥深水、越界方向，以及因崖壁或栅栏而无气边的格都不计气。
    /// </summary>
    public ImmutableArray<Coord> LibertiesOf(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // 段 C2：不再为每次查询分配整张画布大小的去重表（73×73 画布上每条棋串一次 5 KB）——先收集全部候选格下标，
        // 排序后去掉相邻重复，结果与"按格去重再排序"逐项相同（升序、无重复）。
        int capacity = group.Stones.Length * 4;
        int[] buffer = ArrayPool<int>.Shared.Rent(Math.Max(1, capacity));
        try
        {
            int count = 0;
            foreach (Coord stone in group.Stones)
            {
                foreach (Coord n in LibertyNeighbors(stone))
                {
                    int ni = Index(n);
                    if (_occupants[ni] is null && _tables.GivesLibertyWhenEmpty(ni, n))
                    {
                        if (count == buffer.Length)
                        {
                            int[] larger = ArrayPool<int>.Shared.Rent(buffer.Length * 2);
                            buffer.AsSpan(0, count).CopyTo(larger);
                            ArrayPool<int>.Shared.Return(buffer);
                            buffer = larger;
                        }

                        buffer[count++] = ni;
                    }
                }
            }

            Span<int> found = buffer.AsSpan(0, count);
            found.Sort();
            int unique = 0;
            for (int k = 0; k < found.Length; k++)
            {
                if (k == 0 || found[k] != found[k - 1])
                {
                    found[unique++] = found[k];
                }
            }

            ImmutableArray<Coord>.Builder coords = ImmutableArray.CreateBuilder<Coord>(unique);
            for (int k = 0; k < unique; k++)
            {
                coords.Add(CoordAt(found[k]));
            }

            return coords.MoveToImmutable();
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer);
        }
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

    /// <summary>枚举指定玩家的全部棋串，按其最小坐标的字典序排列（即 <see cref="AllGroups"/> 里该玩家的那些，顺序不变）。</summary>
    public ImmutableArray<Group> GroupsOf(PlayerId player)
    {
        ImmutableArray<Group> all = AllGroups();
        ImmutableArray<Group>.Builder groups = ImmutableArray.CreateBuilder<Group>();
        foreach (Group group in all)
        {
            if (group.Owner == player)
            {
                groups.Add(group);
            }
        }

        return groups.ToImmutable();
    }

    /// <summary>
    /// 全盘棋串枚举：不依赖玩家名册，返回盘面上全部棋串，按各棋串最小坐标的字典序排列。
    /// 同时提子（设计文档 §6.1 第 4 步）必须先在全盘范围算出无气棋串的并集再统一移除，
    /// 而不是按名册逐人遍历、边遍历边移除。盘面未变时返回同一份结果（按突变版本号记忆，见类注释）。
    /// </summary>
    public ImmutableArray<Group> AllGroups()
    {
        if (_groupsVersion != _version)
        {
            _groups = ScanGroups();
            _groupsVersion = _version;
        }

        return _groups;
    }

    /// <summary>按确定性坐标顺序扫描全盘，把每个占用格所属棋串各收集一次。</summary>
    private ImmutableArray<Group> ScanGroups()
    {
        var visited = new bool[_occupants.Length];
        ImmutableArray<Group>.Builder groups = ImmutableArray.CreateBuilder<Group>();
        for (int i = 0; i < _occupants.Length; i++)
        {
            if (!visited[i] && _occupants[i] is { } occupant)
            {
                groups.Add(CollectGroup(i, occupant.Owner, visited));
            }
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
        // board-link-mode 段 C2（裁决 C18 / P2）：按突变版本号记忆，与 LifeMemo 同一口径——任何写入原语之后的第一次调用重算，副本从零起。
        // 预演第 8 步、结算核对与日志会对同一盘面多次序列化，73×73 画布上每次约 1 万字符。
        if (_serialVersion == _version && _serial is not null)
        {
            return _serial;
        }

        string serialized = SerializeCore();
        _serial = serialized;
        _serialVersion = _version;
        return serialized;
    }

    /// <summary>
    /// <see cref="Serialize"/> 的本体：一次算出总长、直接写进结果串（不经 StringBuilder 扩容、不逐格分配占用者编号串）。
    /// 输出与逐格 <c>Append</c> 的写法逐字符相同：每格两字符（<c>--</c> 或"占用者一位大写十六进制 + 类型码"），行间 <c>/</c>，有改造时接 <c>|</c> 与按序数字典序排好的改造列表。
    /// </summary>
    private string SerializeCore()
    {
        int width = Width;
        int height = Height;
        string? tail = _edits.Count > 0
            ? string.Join(",", _edits.Select(e => e.ToString()).Order(StringComparer.Ordinal))
            : null;
        int grid = height > 0 ? (width * height * 2) + (height - 1) : 0;
        int length = grid + (tail is null ? 0 : tail.Length + 1);
        return string.Create(length, (Board: this, Tail: tail), static (span, state) =>
        {
            GameBoard board = state.Board;
            int w = board.Width;
            int h = board.Height;
            int k = 0;
            for (int y = 0; y < h; y++)
            {
                if (y > 0)
                {
                    span[k++] = '/';
                }

                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    Occupant? occupant = board._occupants[row + x];
                    if (occupant is null)
                    {
                        span[k++] = '-';
                        span[k++] = '-';
                    }
                    else
                    {
                        int owner = occupant.Value.Owner.Value;
                        if (owner is < 0 or > 15)
                        {
                            throw new SiegeRuleException($"盘面序列化只支持玩家编号 0–15，实际为 {owner}。");
                        }

                        span[k++] = HexDigits[owner];
                        span[k++] = TypeCode(occupant.Value.Type);
                    }
                }
            }

            if (state.Tail is { } edits)
            {
                span[k++] = TerrainSeparator;
                edits.AsSpan().CopyTo(span[k..]);
            }
        });
    }

    /// <summary>占用者编号的一位大写十六进制（与 <c>owner.ToString("X1")</c> 对 0–15 逐字符相同）。</summary>
    private const string HexDigits = "0123456789ABCDEF";

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

    /// <summary>
    /// 存档恢复：在空盘上写入盘面序列化（先改造、后棋子）。只能对没有棋子、没有改造的盘面调用。
    /// </summary>
    internal void LoadState(string serialized)
    {
        if (_edits.Count > 0 || AllCoords().Any(c => this[c].Occupant is not null))
        {
            throw new SiegeRuleException("只能在空盘上写入存档盘面。");
        }

        Fill(serialized);
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
        _version++;
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

        MapData edited = TerrainWriter.ApplyAll(Map, list);
        ReplaceMap(edited, MapTables.Derive(_tables, edited, list));
        _edits.AddRange(list);
    }

    /// <summary>
    /// 战场导出（board-link-mode D1）：当前地图的可落子格连通分量，按地图实例记忆化（<see cref="Battlefields.Of"/>）。
    /// 改造换上新地图即自动换一份；棋串 / 气 / 覆盖 / 活形不读它。
    /// </summary>
    public Battlefields Battlefields => Battlefields.Of(Map);

    /// <summary>
    /// 换地图的唯一路径（改造，实现裁决 A1）：换上新 <see cref="MapData"/>、换派生表、推进突变版本号令棋串 / 活形 / 序列化记忆失效。
    /// <paramref name="tables"/> 缺省即从零建表；改造路径传入由旧表派生的新表（<see cref="MapTables.Derive"/>，段 C2）。
    /// </summary>
    private void ReplaceMap(MapData map, MapTables? tables = null)
    {
        Map = map;
        _tables = tables ?? new MapTables(map);
        _version++;
    }

    /// <summary>最小写入原语：清除指定格的占用。</summary>
    public void Clear(Coord c)
    {
        RequireInBounds(c);
        _occupants[Index(c)] = null;
        _version++;
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

        if (targets.Length == 0)
        {
            // 段 C2：空集合即什么都没写（预演第 5 步对无提子的批次也会调用），盘面未变，记忆（棋串 / 活形 / 序列化）照旧有效，不推进版本号。
            return;
        }

        foreach (Coord c in targets)
        {
            _occupants[Index(c)] = null;
        }

        _version++;
    }

    private int Index(Coord c) => (c.Y * Width) + c.X;

    private Coord CoordAt(int index) => CoordAt(Width, index);

    /// <summary>格下标 → 坐标的<b>唯一</b>算式（<see cref="Index"/> 的逆）：画布宽 <paramref name="width"/> 下 <c>index = y * 宽 + x</c>。派生表与覆盖表的格下标换算都走这里（实现裁决 P7）。</summary>
    private static Coord CoordAt(int width, int index) => new(index % width, index / width);

    /// <summary>格下标（<c>y * 宽 + x</c>）→ 坐标，与 <see cref="Occupants"/> / <see cref="PlayableIndices"/> 的下标同一口径（段 C2：供整盘扫描按下标找起点后换回坐标）。</summary>
    internal Coord CoordOfIndex(int index) => CoordAt(Width, index);

    /// <summary>同 <see cref="CoordOfIndex(int)"/>，供只记住了画布宽的派生结构（<see cref="Scoring.CoverageMap"/>）换算，不另写一份 <c>i % w, i / w</c>（实现裁决 P7）。</summary>
    internal static Coord CoordOfIndex(int width, int index) => CoordAt(width, index);

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
        'N' => PieceType.Bannerman,
        'C' => PieceType.Chain,
        'T' => PieceType.Sentry,
        'K' => PieceType.Boundary,
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
        ReadOnlySpan<char> grid = bar >= 0 ? serialized.AsSpan(0, bar) : serialized.AsSpan();

        // 段 C2：写进租用缓冲区再一次成串（不经 StringBuilder），逐字符结果与按格 Append 相同。
        char[] buffer = ArrayPool<char>.Shared.Rent(Math.Max(1, serialized.Length));
        try
        {
            int k = 0;
            int column = 0;
            foreach (char ch in grid)
            {
                if (ch == '/')
                {
                    RequireEvenRow(column, serialized);
                    column = 0;
                    buffer[k++] = ch;
                    continue;
                }

                if (column % 2 == 0)
                {
                    buffer[k++] = ch;
                }

                column++;
            }

            RequireEvenRow(column, serialized);
            if (bar >= 0)
            {
                serialized.AsSpan(bar).CopyTo(buffer.AsSpan(k));
                k += serialized.Length - bar;
            }

            return new string(buffer, 0, k);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

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
        PieceType.Bannerman => 'N',
        PieceType.Chain => 'C',
        PieceType.Sentry => 'T',
        PieceType.Boundary => 'K',
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。"),
    };
}
