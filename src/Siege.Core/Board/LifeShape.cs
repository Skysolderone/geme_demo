using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>棋串的活形三态。数值即 design D2 的 <c>life(G)</c>：死 0 / 未定 1 / 活 2。</summary>
public enum LifeState
{
    /// <summary>眼值之和为 0。</summary>
    Dead = 0,

    /// <summary>眼值之和为 1，或单子（1 枚子的棋串）眼值之和 ≥ 2；不受保护。</summary>
    Undetermined = 1,

    /// <summary>眼值之和 ≥ 2 且棋串不止 1 枚子：已确定活形，其眼空间对非所有者禁入。单子上限为未定（life-single-stone D1）。</summary>
    Alive = 2,
}

/// <summary>
/// 一块对 <see cref="Owner"/> 封闭的空区，即与它有眼边相连的 <see cref="Owner"/> 的<b>每一条</b>棋串的眼空间
/// （fence-eye-rule D2：只隔栅栏贴着它的棋串也算，即便这块空区不是那条棋串的气）。
/// </summary>
public sealed class EyeSpace
{
    internal EyeSpace(PlayerId owner, ImmutableArray<Coord> cells, int eyeValue, ImmutableArray<int> groupIndices)
    {
        Owner = owner;
        Cells = cells;
        EyeValue = eyeValue;
        GroupIndices = groupIndices;
    }

    /// <summary>围住它的唯一玩家。</summary>
    public PlayerId Owner { get; }

    /// <summary>空区格，按坐标序。</summary>
    public ImmutableArray<Coord> Cells { get; }

    /// <summary>眼值（design D2 表）：1 格 1、2 格 0、3 格 1、方四 0、其余 4 格 2、5 格 1、6–12 格 2。</summary>
    public int EyeValue { get; }

    /// <summary>以它为眼空间的棋串在 <see cref="LifeShapeReport.Groups"/> 中的下标，升序。</summary>
    public ImmutableArray<int> GroupIndices { get; }
}

/// <summary>一条棋串的活形分析结果。</summary>
public sealed class GroupLife
{
    internal GroupLife(Group group, LifeState life, int eyeValueSum, ImmutableArray<EyeSpace> eyeSpaces)
    {
        Group = group;
        Life = life;
        EyeValueSum = eyeValueSum;
        EyeSpaces = eyeSpaces;
    }

    public Group Group { get; }

    public LifeState Life { get; }

    /// <summary>全部眼空间眼值之和（相加口径，design Open Question 3 / 裁决 R1）。</summary>
    public int EyeValueSum { get; }

    /// <summary>该棋串的全部眼空间，按各自首格的坐标序。</summary>
    public ImmutableArray<EyeSpace> EyeSpaces { get; }
}

/// <summary>
/// 活形查询：对任意 <see cref="GameBoard"/>（正式盘面或批次预演副本）一次全量算出
/// 每条棋串的活形状态、眼空间与眼值，以及每名玩家的禁入格。活形 / 禁入<b>只有这一处实现</b>，
/// 预演（活棋禁入、破坏活形）、合法落子范围契约、公开视图与 AI 共用它。
/// </summary>
/// <remarks>
/// <para>纯计算、只读盘面：棋子（棋串，按气边连通）来自 <see cref="GameBoard.AllGroups"/>，空区与封闭的邻接只来自 <see cref="GameBoard.EyeNeighbors"/>
/// （fence-eye-rule D1：眼边 = 两格可落子且 |Δh| ≤ 1，不看栅栏）——无眼边的方向（障碍、未架桥深水、崖壁、棋盘外沿）不产生邻格，天然是墙，不另写判定；
/// <b>栅栏不是墙</b>：它既不切分空区，也不挡住对面的棋子参与封闭判定（裁决 F1 / F3）。</para>
/// <para>不接名册：所有者从棋子读出，弃赛 / 出局者遗留的活形照常产生禁入。不入存档（design D5）；不做增量——唯一的记忆化是
/// <see cref="GameBoard.LifeMemo"/>（ai-turn-speed D2）：同一盘面实例在没有任何写入原语之间再次分析时返回同一份全量结果，写入之后整份重算。</para>
/// <para>确定性：内部只用按 <c>y * 宽 + x</c> 编址（段 C2 起按可落子格序号编址，序号升序即坐标序）的数组与按坐标序构造的列表，不遍历无序集合。</para>
/// <para>规格：openspec/specs/life-shape；单子不成活见 life-single-stone（「活形三态」MODIFIED），栅栏不参与活形判定见 fence-eye-rule（两条 Requirement MODIFIED）。</para>
/// </remarks>
public sealed class LifeShapeReport
{
    /// <summary>眼空间上限（格数，含）。design D1 / 裁决 #11，边疆档不放宽（裁决 R1）。</summary>
    public const int EyeSpaceMax = 12;

    private readonly int _width;

    /// <summary>格下标 → 可落子格序号（不可落子为 −1），取自盘面的地图派生表（段 C2）；下面两张表只为可落子格存放。</summary>
    private readonly ImmutableArray<int> _slot;
    private readonly int[] _groupAt;
    private readonly int[] _eyeSpaceAt;

    private LifeShapeReport(
        int width, ImmutableArray<int> slot, int[] groupAt, int[] eyeSpaceAt, ImmutableArray<GroupLife> groups, ImmutableArray<EyeSpace> eyeSpaces)
    {
        _width = width;
        _slot = slot;
        _groupAt = groupAt;
        _eyeSpaceAt = eyeSpaceAt;
        Groups = groups;
        EyeSpaces = eyeSpaces;
    }

    /// <summary>全盘棋串的活形，顺序与 <see cref="GameBoard.AllGroups"/> 相同（各棋串最小坐标的字典序）。</summary>
    public ImmutableArray<GroupLife> Groups { get; }

    /// <summary>全部眼空间（封闭空区，不论眼值与所属棋串死活），按首格坐标序。</summary>
    public ImmutableArray<EyeSpace> EyeSpaces { get; }

    /// <summary>对盘面做一次全量活形分析（盘面自上次分析以来未变即返回同一份结果，见 <see cref="GameBoard.LifeMemo"/>）。</summary>
    public static LifeShapeReport Analyze(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (board.LifeMemo is { } memo)
        {
            return memo;
        }

        LifeShapeReport report = AnalyzeCore(board);
        board.LifeMemo = report;
        return report;
    }

    private static LifeShapeReport AnalyzeCore(GameBoard board)
    {
        // 段 C2（board-link-mode 裁决 C18 / P2，决策不变的数据结构改写）：逐格表只为可落子格存放、按可落子格序号编址
        // （序号升序即坐标序）——棋子、气与空区都只在可落子格上（气边、眼边两端都可落子），73×73 画布上省掉九成的逐格分配与扫描。
        int width = board.Width;
        ImmutableArray<int> playable = board.PlayableIndices;
        ImmutableArray<int> slot = board.PlayableOrdinals;
        int size = playable.Length;

        // ① 棋串：一次全盘枚举，按格记下所在棋串下标（空格为 -1）。
        ImmutableArray<Group> groups = board.AllGroups();
        int[] groupAt = new int[size];
        Array.Fill(groupAt, -1);
        for (int g = 0; g < groups.Length; g++)
        {
            foreach (Coord stone in groups[g].Stones)
            {
                groupAt[SlotOf(slot, width, stone)] = g;
            }
        }

        // ② 候选起点：沿眼边贴着棋子的空格（能作气的地表）。没有贴任何棋子的空区不可能封闭，不必走到。
        // fence-eye-rule D2：只隔栅栏贴着棋子的空格不是该棋串的气，却同样是起点——空区归属按眼边，不按气。
        bool[] isStart = new bool[size];
        foreach (Group group in groups)
        {
            foreach (Coord stone in group.Stones)
            {
                foreach (Coord n in board.EyeNeighbors(stone))
                {
                    int ni = SlotOf(slot, width, n);
                    isStart[ni] = groupAt[ni] < 0 && board.GivesLiberty(n);
                }
            }
        }

        // ③ 空区：按坐标序从未走过的起点出发，沿眼边 flood fill，同一遍收集贴边（有眼边相连）的棋串。
        // 早停（不改语义）：格数一超过 EyeSpaceMax，或贴到第二名玩家的棋子，该空区不可能封闭，立即放弃；已走过的格留着本次的标记。
        // 之后任何一次 flood 碰到"别的 flood 留下的标记"，说明两者是同一个空区——而完整走完的小空区不可能从外面碰到，
        // 所以碰到的必是一个已放弃（不可能封闭）的空区，本次也立即放弃。每格至多被走一次。
        int[] floodOf = new int[size];
        bool[] touched = new bool[groups.Length];
        var closed = new List<(ImmutableArray<Coord> Cells, PlayerId Owner, ImmutableArray<int> Groups)>();
        var region = new List<Coord>();
        var touching = new List<int>();
        int floodId = 0;

        // 段 C2：按可落子格序号升序找起点（即 AllCoords 的坐标序），不经坐标枚举器逐格构造坐标。
        for (int startIndex = 0; startIndex < size; startIndex++)
        {
            if (!isStart[startIndex] || floodOf[startIndex] != 0)
            {
                continue;
            }

            Coord start = board.CoordOfIndex(playable[startIndex]);
            floodId++;
            region.Clear();
            touching.Clear();
            floodOf[startIndex] = floodId;
            region.Add(start);
            bool open = false;
            for (int head = 0; head < region.Count && !open; head++)
            {
                foreach (Coord n in board.EyeNeighbors(region[head]))
                {
                    int ni = SlotOf(slot, width, n);
                    int g = groupAt[ni];
                    if (g >= 0)
                    {
                        if (!touched[g])
                        {
                            touched[g] = true;
                            touching.Add(g);
                            open |= groups[g].Owner != groups[touching[0]].Owner;
                        }
                    }
                    else if (!board.GivesLiberty(n))
                    {
                        // 空浅滩（terrain-surfaces）：不属于任何空区；它有眼边、谁都能落进去，所以贴着它的空区不封闭——不是墙。
                        open = true;
                    }
                    else if (floodOf[ni] == 0)
                    {
                        floodOf[ni] = floodId;
                        region.Add(n);
                        open |= region.Count > EyeSpaceMax;
                    }
                    else if (floodOf[ni] != floodId)
                    {
                        open = true;
                    }

                    if (open)
                    {
                        break;
                    }
                }
            }

            foreach (int g in touching)
            {
                touched[g] = false;
            }

            if (open || !IsClosed(groups, touching, region.Count, out PlayerId owner))
            {
                continue;
            }

            touching.Sort();
            region.Sort();
            closed.Add(([.. region], owner, [.. touching]));
        }

        // ④ 眼空间按首格坐标序编号（空区互不相交，首格即可定序），再给眼值。
        closed.Sort((a, b) => a.Cells[0].CompareTo(b.Cells[0]));
        int[] eyeSpaceAt = new int[size];
        Array.Fill(eyeSpaceAt, -1);
        var eyeSpaces = new List<EyeSpace>(closed.Count);
        var eyeSpacesOfGroup = new List<EyeSpace>?[groups.Length];
        foreach ((ImmutableArray<Coord> cells, PlayerId owner, ImmutableArray<int> touchingGroups) in closed)
        {
            var space = new EyeSpace(owner, cells, EyeValueOf(board, cells, eyeSpaceAt, slot, width, eyeSpaces.Count), touchingGroups);
            eyeSpaces.Add(space);
            foreach (int g in touchingGroups)
            {
                (eyeSpacesOfGroup[g] ??= []).Add(space);
            }
        }

        // ⑤ 每条棋串：眼值相加得三态（单子上限为未定，life-single-stone D1）。eyeSpaces 按首格坐标序生成，每条棋串的列表因此天然有序。
        ImmutableArray<GroupLife>.Builder lives = ImmutableArray.CreateBuilder<GroupLife>(groups.Length);
        for (int g = 0; g < groups.Length; g++)
        {
            ImmutableArray<EyeSpace> own = eyeSpacesOfGroup[g] is { } list ? [.. list] : [];
            int sum = own.Sum(e => e.EyeValue);
            lives.Add(new GroupLife(groups[g], StateOf(sum, groups[g].Size), sum, own));
        }

        return new LifeShapeReport(width, slot, groupAt, eyeSpaceAt, lives.MoveToImmutable(), [.. eyeSpaces]);
    }

    /// <summary>该格棋子所在棋串的活形；空格返回 <c>null</c>。预演第 6 步（破坏活形）按棋子在副本上的所在棋串重查用它。</summary>
    public GroupLife? GroupLifeAt(Coord stone)
    {
        int i = IndexChecked(stone);
        int g = i >= 0 ? _groupAt[i] : -1;
        return g >= 0 ? Groups[g] : null;
    }

    /// <summary>该空格所属的眼空间；不在任何封闭空区里返回 <c>null</c>。</summary>
    public EyeSpace? EyeSpaceAt(Coord cell)
    {
        int i = IndexChecked(cell);
        int e = i >= 0 ? _eyeSpaceAt[i] : -1;
        return e >= 0 ? EyeSpaces[e] : null;
    }

    /// <summary>以该眼空间为眼的全部棋串，按 <see cref="Groups"/> 顺序。</summary>
    public ImmutableArray<GroupLife> GroupsOf(EyeSpace space)
    {
        ArgumentNullException.ThrowIfNull(space);
        return [.. space.GroupIndices.Select(g => Groups[g])];
    }

    /// <summary>该眼空间是否受保护：以它为眼的棋串中至少一条为已确定活形。</summary>
    public bool IsProtected(EyeSpace space) => GroupsOf(space).Any(g => g.Life == LifeState.Alive);

    /// <summary>按所有者分组：<paramref name="owner"/> 的全部已确定活形棋串的眼空间格之并，坐标序。</summary>
    public ImmutableArray<Coord> ProtectedCellsOf(PlayerId owner) => CollectProtected(space => space.Owner == owner);

    /// <summary>
    /// 玩家 <paramref name="player"/> 的禁入格：所有者不是他的已确定活形棋串的全部眼空间格之并，坐标序。
    /// 所有者自己的眼空间不在其中；盘上没有棋子的玩家同样可查。
    /// </summary>
    public ImmutableArray<Coord> ForbiddenCellsFor(PlayerId player) => CollectProtected(space => space.Owner != player);

    /// <summary>该格对 <paramref name="player"/> 是否禁入。</summary>
    public bool IsForbiddenFor(PlayerId player, Coord cell) =>
        EyeSpaceAt(cell) is { } space && space.Owner != player && IsProtected(space);

    private ImmutableArray<Coord> CollectProtected(Func<EyeSpace, bool> include)
    {
        var cells = new List<Coord>();
        foreach (EyeSpace space in EyeSpaces)
        {
            if (include(space) && IsProtected(space))
            {
                cells.AddRange(space.Cells);
            }
        }

        cells.Sort();
        return [.. cells];
    }

    /// <summary>
    /// 封闭判定（design D1）：贴边棋串同属一名玩家 ∧ 至少贴一条棋串 ∧ 格数 ≤ <see cref="EyeSpaceMax"/>。
    /// 贴边棋串由眼边 flood fill 收集，无眼边的方向（障碍、未架桥深水、崖壁、棋盘外沿）天然不贡献；栅栏不挡眼边（fence-eye-rule F1）。
    /// </summary>
    private static bool IsClosed(ImmutableArray<Group> groups, List<int> touching, int cellCount, out PlayerId owner)
    {
        owner = default;
        if (touching.Count == 0 || cellCount > EyeSpaceMax)
        {
            return false;
        }

        owner = groups[touching[0]].Owner;
        foreach (int g in touching)
        {
            if (groups[g].Owner != owner)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 眼值（design D2）：先把本空区格登记进 <paramref name="eyeSpaceAt"/>，再按格数查表；
    /// 4 格时按空区内部的眼边图判方四——四格连通且每格恰有 2 个区内眼边邻格 ⇔ 成 4-环（内部的栅栏不影响形状，fence-eye-rule F4）。
    /// </summary>
    private static int EyeValueOf(GameBoard board, ImmutableArray<Coord> cells, int[] eyeSpaceAt, ImmutableArray<int> slot, int width, int spaceIndex)
    {
        foreach (Coord c in cells)
        {
            eyeSpaceAt[SlotOf(slot, width, c)] = spaceIndex;
        }

        return cells.Length switch
        {
            1 => 1,
            2 => 0,
            3 => 1,
            4 => IsRing(board, cells, eyeSpaceAt, slot, width, spaceIndex) ? 0 : 2,
            5 => 1,
            _ => 2,
        };
    }

    private static bool IsRing(GameBoard board, ImmutableArray<Coord> cells, int[] eyeSpaceAt, ImmutableArray<int> slot, int width, int spaceIndex)
    {
        foreach (Coord c in cells)
        {
            int inside = 0;
            foreach (Coord n in board.EyeNeighbors(c))
            {
                if (eyeSpaceAt[SlotOf(slot, width, n)] == spaceIndex)
                {
                    inside++;
                }
            }

            if (inside != 2)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 三态判定：眼值之和 ≥ 2 → 活，= 1 → 未定，= 0 → 死；<b>只有 1 枚子的棋串上限为未定</b>（life-single-stone D1）——
    /// 眼空间与眼值照常计算、对外可查，只是不成活。共享眼空间经由同一玩家的另一条多子活形棋串照常受保护（D2），
    /// 破坏活形检查按原棋子重查本结果，无需另改（D3）。这是"活"的唯一判定处，预演、合法落子范围、公开视图、表现层与 AI 自动跟随。
    /// </summary>
    private static LifeState StateOf(int eyeValueSum, int stoneCount) => eyeValueSum switch
    {
        >= 2 when stoneCount > 1 => LifeState.Alive,
        >= 1 => LifeState.Undetermined,
        _ => LifeState.Dead,
    };

    private static int Index(int width, Coord c) => (c.Y * width) + c.X;

    /// <summary>坐标 → 逐格表的序号；越界抛出，不可落子格为 −1（那里没有棋子，也不在任何空区里）。</summary>
    private int IndexChecked(Coord c)
    {
        int i = Index(_width, c);
        if (c.X >= _width || i >= _slot.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(c), c.ToNotation(), "坐标超出棋盘范围。");
        }

        return _slot[i];
    }

    /// <summary>
    /// 分析途中的坐标 → 序号：棋子与眼边两端都只可能在可落子格上（落子与眼边都要求可落子，改造与链接只增不减可落子格），
    /// 不可落子即盘面状态自相矛盾，响亮失败而不是静默当作空格。
    /// </summary>
    private static int SlotOf(ImmutableArray<int> slot, int width, Coord c)
    {
        int o = slot[Index(width, c)];
        if (o < 0)
        {
            throw new SiegeRuleException($"活形分析遇到不可落子格上的棋子或眼边：{c.ToNotation()}。");
        }

        return o;
    }
}
