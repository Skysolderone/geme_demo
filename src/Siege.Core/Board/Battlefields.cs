using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Siege.Core.Board;

/// <summary>
/// 一个战场（board-link-mode D1）：沿几何四邻相连的可落子格的一个极大连通分量。
/// <see cref="Id"/> 取分量内编号最小的出生棋盘（= 出生区）编号，在整局中稳定；分量里没有任何出生区时为 −1
/// （链接档地图上这是校验失败项 <c>BATTLEFIELD_WITHOUT_BIRTH_BOARD</c>，其余规格档的孤立可落子区允许存在、只是不属于任何战场编号）。
/// </summary>
/// <param name="Id">战场标识：分量内最小出生区编号；无出生区为 −1。</param>
/// <param name="Cells">分量的全部格，按字典序（先行后列）。</param>
/// <param name="BirthZones">分量内的出生区编号，升序。</param>
/// <param name="Boards">分量内的棋盘在 <see cref="MapData.Boards"/> 里的下标，升序（没有棋盘清单的地图为空）。</param>
public sealed record Battlefield(int Id, ImmutableArray<Coord> Cells, ImmutableArray<int> BirthZones, ImmutableArray<int> Boards)
{
    /// <summary>是否含至少一块出生棋盘（出生区）。</summary>
    public bool HasBirthBoard => Id >= 0;

    public override string ToString() => $"战场 {(Id >= 0 ? BirthZoneLabel.Of(Id) : "（无出生棋盘）")}：{Cells.Length} 格、{Boards.Length} 块棋盘";
}

/// <summary>
/// 战场导出（board-link-mode D1，规格 battlefield「战场的定义与规则作用域」）：对一份 <see cref="MapData"/> 按几何四邻把可落子格分成极大连通分量。
/// 它是按需导出的派生量，<b>不另存</b>任何需要与地图同步的状态——地图不可变，结果随地图实例记忆化（<see cref="Of"/>）：
/// 改造、链接换上新 <see cref="MapData"/> 即从头导出，与 <c>GameBoard</c> 的派生表口径一致。
/// </summary>
/// <remarks>
/// <para>邻接用<b>几何四邻</b>（<see cref="Adjacency.Neighbors"/>）而不是气边：规格把战场定义在几何邻接上——匠人立栅、崖壁都不得把一个战场切成两个，
/// 隔开两个战场的只有不可落子的场景格。本类是 <c>四邻接Tests.几何邻居枚举只在允许名单内直接调用</c> 允许名单的第 ⑥ 条。</para>
/// <para>棋串、气、覆盖、活形、禁入格 MUST NOT 读本类：战场之间没有任何邻接，那些规则在既有邻接上自然互不影响（D1）。</para>
/// </remarks>
public sealed class Battlefields
{
    private static readonly ConditionalWeakTable<MapData, Battlefields> Memo = new();

    private readonly int _width;
    private readonly int _height;
    private readonly int[] _indexOf;

    private Battlefields(int width, int height, int[] indexOf, ImmutableArray<Battlefield> all)
    {
        _width = width;
        _height = height;
        _indexOf = indexOf;
        All = all;
    }

    /// <summary>
    /// 全部战场：先按 <see cref="Battlefield.Id"/> 升序（有出生棋盘者在前），无出生棋盘的分量按首格字典序排在最后。
    /// </summary>
    public ImmutableArray<Battlefield> All { get; }

    /// <summary>战场数（含无出生棋盘的分量）。</summary>
    public int Count => All.Length;

    /// <summary>按地图实例取战场导出：同一实例只算一次；不同实例（改造 / 链接换图）各自从头导出。</summary>
    public static Battlefields Of(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Memo.GetValue(map, Compute);
    }

    /// <summary>该格所在的战场；不可落子的格（含越界）为 <c>null</c>。</summary>
    public Battlefield? At(Coord c)
    {
        if (c.X < 0 || c.Y < 0 || c.X >= _width || c.Y >= _height)
        {
            return null;
        }

        int index = _indexOf[(c.Y * _width) + c.X];
        return index >= 0 ? All[index] : null;
    }

    /// <summary>按战场标识取战场；没有该标识的战场为 <c>null</c>。</summary>
    public Battlefield? ById(int id)
    {
        foreach (Battlefield field in All)
        {
            if (field.Id == id)
            {
                return field;
            }
        }

        return null;
    }

    private static Battlefields Compute(MapData map)
    {
        int width = map.Width;
        int height = map.Height;
        var component = new int[width * height];
        Array.Fill(component, -1);
        var fields = new List<(int Id, Coord First, List<Coord> Cells)>();

        // 先行后列的扫描保证分量按首格字典序被发现；分量内部 BFS 顺序无关紧要，输出前排序。
        foreach (Coord start in map.AllCoords())
        {
            int startIndex = (start.Y * width) + start.X;
            if (component[startIndex] >= 0 || !map.IsPlayable(start))
            {
                continue;
            }

            int number = fields.Count;
            var cells = new List<Coord>();
            var queue = new Queue<Coord>();
            component[startIndex] = number;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Coord current = queue.Dequeue();
                cells.Add(current);
                foreach (Coord n in Adjacency.Neighbors(width, height, current))
                {
                    int index = (n.Y * width) + n.X;
                    if (component[index] < 0 && map.IsPlayable(n))
                    {
                        component[index] = number;
                        queue.Enqueue(n);
                    }
                }
            }

            fields.Add((number, start, cells));
        }

        // 每个分量的出生区与棋盘：出生区按"任一格落在分量内"归属；棋盘按左下角格归属（棋盘内每格都可落子且连通，取一格即可）。
        var zonesOf = new List<int>[fields.Count];
        var boardsOf = new List<int>[fields.Count];
        for (int i = 0; i < fields.Count; i++)
        {
            zonesOf[i] = [];
            boardsOf[i] = [];
        }

        for (int z = 0; z < map.BirthZones.Length; z++)
        {
            var seen = new HashSet<int>();
            foreach (Coord c in map.BirthZones[z].Order())
            {
                if (!map.Contains(c))
                {
                    continue;
                }

                int number = component[(c.Y * width) + c.X];
                if (number >= 0 && seen.Add(number))
                {
                    zonesOf[number].Add(z);
                }
            }
        }

        for (int b = 0; b < map.Boards.Length; b++)
        {
            Coord origin = map.Boards[b].Origin;
            if (!map.Contains(origin))
            {
                continue;
            }

            int number = component[(origin.Y * width) + origin.X];
            if (number >= 0)
            {
                boardsOf[number].Add(b);
            }
        }

        // 排序：有出生区的按最小出生区编号，其余按首格字典序排在后面；再把格下标表重映射到排序后的位置。
        var built = new List<(Battlefield Field, int Number)>(fields.Count);
        foreach ((int number, Coord _, List<Coord> cells) in fields)
        {
            int id = zonesOf[number].Count == 0 ? -1 : zonesOf[number].Min();
            built.Add((new Battlefield(id, [.. cells.Order()], [.. zonesOf[number].Order()], [.. boardsOf[number].Order()]), number));
        }

        built.Sort((a, b) =>
        {
            bool aHas = a.Field.Id >= 0;
            bool bHas = b.Field.Id >= 0;
            if (aHas != bHas)
            {
                return aHas ? -1 : 1;
            }

            return aHas ? a.Field.Id.CompareTo(b.Field.Id) : a.Field.Cells[0].CompareTo(b.Field.Cells[0]);
        });

        var remap = new int[fields.Count];
        for (int i = 0; i < built.Count; i++)
        {
            remap[built[i].Number] = i;
        }

        var indexOf = new int[width * height];
        for (int i = 0; i < indexOf.Length; i++)
        {
            indexOf[i] = component[i] >= 0 ? remap[component[i]] : -1;
        }

        return new Battlefields(width, height, indexOf, [.. built.Select(b => b.Field)]);
    }
}
