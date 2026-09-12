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
/// <para>本类是全项目<b>唯一</b>的四邻接遍历实现。连接、气、覆盖与围杀判定 MUST 全部复用
/// <see cref="Neighbors"/>。</para>
/// <para>规格：openspec/changes/add-board-core/specs/board-topology</para>
/// </remarks>
public sealed class GameBoard
{
    private readonly Occupant?[] _occupants;

    private GameBoard(MapData map)
    {
        Map = map;
        _occupants = new Occupant?[map.Width * map.Height];
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

    /// <summary>地图静态数据。</summary>
    public MapData Map { get; }

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
            foreach (Coord n in Neighbors(current))
            {
                if (visited.Contains(n))
                {
                    continue;
                }

                // 棋子类型不影响棋串归属；不同玩家的棋子绝不合并。
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
    /// 棋串的气：与该棋串任一棋子四邻接、且当前为空的可落子格（按格去重）。
    /// 被任何玩家棋子占据的格、障碍格与越界方向都不计气。
    /// </summary>
    public ImmutableArray<Coord> LibertiesOf(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var liberties = new HashSet<Coord>();
        foreach (Coord stone in group.Stones)
        {
            foreach (Coord n in Neighbors(stone))
            {
                if (this[n].IsPlayableEmpty)
                {
                    liberties.Add(n);
                }
            }
        }

        return liberties.Order().ToImmutableArray();
    }

    /// <summary>棋串是否无气。</summary>
    public bool IsCaptured(Group group) => LibertiesOf(group).IsEmpty;

    /// <summary>枚举指定玩家的全部棋串，按其最小坐标的字典序排列。</summary>
    public ImmutableArray<Group> GroupsOf(PlayerId player)
    {
        var seen = new HashSet<Coord>();
        ImmutableArray<Group>.Builder groups = ImmutableArray.CreateBuilder<Group>();

        foreach (Coord c in AllCoords())
        {
            if (seen.Contains(c) || _occupants[Index(c)] is not { } occupant || occupant.Owner != player)
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
    /// 确定性盘面序列化。内容<b>只含</b>每格的占用者与棋子类型，
    /// 不含手牌、征募结果、信物控制、势力值、行动顺序或当前行动者。
    /// 直接服务于批次结算的盘面同形禁则（设计文档 §6.2）。
    /// </summary>
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
                    sb.Append(occupant.Value.Owner.Value.ToString("X1"));
                    sb.Append(TypeCode(occupant.Value.Type));
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 最小写入原语：把棋子放到指定格。<b>不做任何规则判定</b>——
    /// 合法性预演、同时提子与结算顺序由批次结算层负责。
    /// </summary>
    public void Place(Coord c, PlayerId owner, PieceType type)
    {
        RequireInBounds(c);
        if (Map.TerrainAt(c) == Terrain.Obstacle)
        {
            throw new SiegeRuleException($"目标格不可落子：{c.ToNotation()} 是障碍格。");
        }

        if (_occupants[Index(c)] is not null)
        {
            throw new SiegeRuleException($"目标格已被占据：{c.ToNotation()}。");
        }

        _occupants[Index(c)] = new Occupant(owner, type);
    }

    /// <summary>最小写入原语：清除指定格的占用。</summary>
    public void Clear(Coord c)
    {
        RequireInBounds(c);
        _occupants[Index(c)] = null;
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

    private static char TypeCode(PieceType type) => type switch
    {
        PieceType.Basic => 'B',
        PieceType.Fortress => 'F',
        PieceType.Line => 'L',
        PieceType.Multiplier => 'M',
        PieceType.Synergy => 'S',
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知棋子类型。"),
    };
}
