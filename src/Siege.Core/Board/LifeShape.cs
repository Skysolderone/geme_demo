using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>棋串的活形三态。数值即 design D2 的 <c>life(G)</c>：死 0 / 未定 1 / 活 2。</summary>
public enum LifeState
{
    /// <summary>眼值之和为 0。</summary>
    Dead = 0,

    /// <summary>眼值之和为 1，不受保护。</summary>
    Undetermined = 1,

    /// <summary>眼值之和 ≥ 2：已确定活形，其眼空间对非所有者禁入。</summary>
    Alive = 2,
}

/// <summary>
/// 一块对 <see cref="Owner"/> 封闭的空区，即与它有气边相连的 <see cref="Owner"/> 的<b>每一条</b>棋串的眼空间。
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
/// <para>纯计算、只读盘面：棋子来自 <see cref="GameBoard.AllGroups"/>，邻接只来自 <see cref="GameBoard.LibertyNeighbors"/>——
/// 无气边的方向（障碍、未架桥深水、崖壁、栅栏、棋盘外沿）不产生邻格，天然是墙，不另写判定（design D1）。</para>
/// <para>不接名册：所有者从棋子读出，弃赛 / 出局者遗留的活形照常产生禁入。不缓存、不入存档（design D5）。</para>
/// <para>确定性：内部只用按 <c>y * 宽 + x</c> 编址的数组与按坐标序构造的列表，不遍历无序集合。</para>
/// <para>规格：openspec/changes/life-shape/specs/life-shape</para>
/// </remarks>
public sealed class LifeShapeReport
{
    /// <summary>眼空间上限（格数，含）。design D1 / 裁决 #11，边疆档不放宽（裁决 R1）。</summary>
    public const int EyeSpaceMax = 12;

    private readonly int _width;
    private readonly int[] _groupAt;
    private readonly int[] _eyeSpaceAt;

    private LifeShapeReport(int width, int[] groupAt, int[] eyeSpaceAt, ImmutableArray<GroupLife> groups, ImmutableArray<EyeSpace> eyeSpaces)
    {
        _width = width;
        _groupAt = groupAt;
        _eyeSpaceAt = eyeSpaceAt;
        Groups = groups;
        EyeSpaces = eyeSpaces;
    }

    /// <summary>全盘棋串的活形，顺序与 <see cref="GameBoard.AllGroups"/> 相同（各棋串最小坐标的字典序）。</summary>
    public ImmutableArray<GroupLife> Groups { get; }

    /// <summary>全部眼空间（封闭空区，不论眼值与所属棋串死活），按首格坐标序。</summary>
    public ImmutableArray<EyeSpace> EyeSpaces { get; }

    /// <summary>对盘面做一次全量活形分析。</summary>
    public static LifeShapeReport Analyze(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        int width = board.Width;
        int size = width * board.Height;

        // ① 棋串：一次全盘枚举，按格记下所在棋串下标（空格为 -1）。
        ImmutableArray<Group> groups = board.AllGroups();
        int[] groupAt = new int[size];
        Array.Fill(groupAt, -1);
        for (int g = 0; g < groups.Length; g++)
        {
            foreach (Coord stone in groups[g].Stones)
            {
                groupAt[Index(width, stone)] = g;
            }
        }

        // ② 候选起点：棋串的气。没有贴任何棋子的空区不可能封闭，不必走到。
        bool[] isLiberty = new bool[size];
        foreach (Group group in groups)
        {
            foreach (Coord stone in group.Stones)
            {
                foreach (Coord n in board.LibertyNeighbors(stone))
                {
                    int ni = Index(width, n);
                    isLiberty[ni] = groupAt[ni] < 0;
                }
            }
        }

        // ③ 空区：按坐标序从未走过的气出发，沿气边 flood fill，同一遍收集贴边的棋串。
        // 早停（不改语义）：格数一超过 EyeSpaceMax，或贴到第二名玩家的棋子，该空区不可能封闭，立即放弃；已走过的格留着本次的标记。
        // 之后任何一次 flood 碰到"别的 flood 留下的标记"，说明两者是同一个空区——而完整走完的小空区不可能从外面碰到，
        // 所以碰到的必是一个已放弃（不可能封闭）的空区，本次也立即放弃。每格至多被走一次。
        int[] floodOf = new int[size];
        bool[] touched = new bool[groups.Length];
        var closed = new List<(ImmutableArray<Coord> Cells, PlayerId Owner, ImmutableArray<int> Groups)>();
        var region = new List<Coord>();
        var touching = new List<int>();
        int floodId = 0;

        foreach (Coord start in board.AllCoords())
        {
            int startIndex = Index(width, start);
            if (!isLiberty[startIndex] || floodOf[startIndex] != 0)
            {
                continue;
            }

            floodId++;
            region.Clear();
            touching.Clear();
            floodOf[startIndex] = floodId;
            region.Add(start);
            bool open = false;
            for (int head = 0; head < region.Count && !open; head++)
            {
                foreach (Coord n in board.LibertyNeighbors(region[head]))
                {
                    int ni = Index(width, n);
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
            var space = new EyeSpace(owner, cells, EyeValueOf(board, cells, eyeSpaceAt, width, eyeSpaces.Count), touchingGroups);
            eyeSpaces.Add(space);
            foreach (int g in touchingGroups)
            {
                (eyeSpacesOfGroup[g] ??= []).Add(space);
            }
        }

        // ⑤ 每条棋串：眼值相加得三态。eyeSpaces 按首格坐标序生成，每条棋串的列表因此天然有序。
        ImmutableArray<GroupLife>.Builder lives = ImmutableArray.CreateBuilder<GroupLife>(groups.Length);
        for (int g = 0; g < groups.Length; g++)
        {
            ImmutableArray<EyeSpace> own = eyeSpacesOfGroup[g] is { } list ? [.. list] : [];
            int sum = own.Sum(e => e.EyeValue);
            lives.Add(new GroupLife(groups[g], StateOf(sum), sum, own));
        }

        return new LifeShapeReport(width, groupAt, eyeSpaceAt, lives.MoveToImmutable(), [.. eyeSpaces]);
    }

    /// <summary>该格棋子所在棋串的活形；空格返回 <c>null</c>。预演第 6 步（破坏活形）按棋子在副本上的所在棋串重查用它。</summary>
    public GroupLife? GroupLifeAt(Coord stone)
    {
        int g = _groupAt[IndexChecked(stone)];
        return g >= 0 ? Groups[g] : null;
    }

    /// <summary>该空格所属的眼空间；不在任何封闭空区里返回 <c>null</c>。</summary>
    public EyeSpace? EyeSpaceAt(Coord cell)
    {
        int e = _eyeSpaceAt[IndexChecked(cell)];
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
    /// 贴边棋串由气边 flood fill 收集，无气边的方向天然不贡献。
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
    /// 4 格时按空区内部的气边图判方四——四格连通且每格恰有 2 个区内气边邻格 ⇔ 成 4-环。
    /// </summary>
    private static int EyeValueOf(GameBoard board, ImmutableArray<Coord> cells, int[] eyeSpaceAt, int width, int spaceIndex)
    {
        foreach (Coord c in cells)
        {
            eyeSpaceAt[Index(width, c)] = spaceIndex;
        }

        return cells.Length switch
        {
            1 => 1,
            2 => 0,
            3 => 1,
            4 => IsRing(board, cells, eyeSpaceAt, width, spaceIndex) ? 0 : 2,
            5 => 1,
            _ => 2,
        };
    }

    private static bool IsRing(GameBoard board, ImmutableArray<Coord> cells, int[] eyeSpaceAt, int width, int spaceIndex)
    {
        foreach (Coord c in cells)
        {
            int inside = 0;
            foreach (Coord n in board.LibertyNeighbors(c))
            {
                if (eyeSpaceAt[Index(width, n)] == spaceIndex)
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

    private static LifeState StateOf(int eyeValueSum) => eyeValueSum switch
    {
        >= 2 => LifeState.Alive,
        1 => LifeState.Undetermined,
        _ => LifeState.Dead,
    };

    private static int Index(int width, Coord c) => (c.Y * width) + c.X;

    private int IndexChecked(Coord c)
    {
        int i = Index(_width, c);
        if (c.X >= _width || i >= _groupAt.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(c), c.ToNotation(), "坐标超出棋盘范围。");
        }

        return i;
    }
}
