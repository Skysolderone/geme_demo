using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Siege.Core.Board;

/// <summary>
/// <see cref="GameBoard"/> 的地图派生表部分（board-link-mode 实现裁决 P7：把 <see cref="MapTables"/> 从 <c>GameBoard.cs</c> 抽出，零行为改动）。
/// 本文件只有嵌套类 <see cref="MapTables"/>；盘面的字段、写入原语与查询仍在 <c>GameBoard.cs</c>。
/// </summary>
public sealed partial class GameBoard
{
    /// <summary>
    /// 一份 <see cref="MapData"/> 的逐格派生表（ai-turn-speed D2「数据结构」）：可落子 / 可作气的地表 / 出生区 / 信物格、气边邻居、眼边邻居、覆盖目标。
    /// 每一项都在第一次读取时由唯一实现算出并记住（<see cref="MapData.IsPlayable"/> / <see cref="MapData.SurfaceAt"/> / <see cref="MapData.BirthZoneOf"/> /
    /// <see cref="MapData.RelicCells"/> / <see cref="Adjacency.LibertyNeighbors"/> / <see cref="Adjacency.EyeNeighbors"/> / <see cref="Adjacency.CoverageTargets"/>），本类不含任何地形判定；
    /// 地图不可变，表随地图实例存亡——<see cref="GameBoard.ApplyTerrainEdits"/> 换地图即换表。
    /// </summary>
    /// <remarks>
    /// 段 C2（board-link-mode 裁决 C18 / P2）：改造换图时新表由旧表<b>派生</b>（<see cref="Derive"/>）——旧表里已算好的逐格项原样抄过来，
    /// 只把改造目标周围 <see cref="EditReach"/> 格（切比雪夫距离）内的格作废、留待按新地图重算。派生表的每一项与在新地图上从零建表逐项相同
    /// （<c>盘面派生量提速对照Tests.改造派生表与从零建表逐项相同</c> 穷举守门）：改造只改目标格的地表 / 桥或一条边的栅栏，不碰高度、障碍、出生区与信物格
    /// （<see cref="TerrainWriter"/> 的每类改造只改目标格或目标边——若将来新增会改动邻格地形的改造，<see cref="Invalidate"/> 的中心必须一并包含那些格）；
    /// 一格的可落子与地表标志只看本格，气边 / 眼边邻居只看本格与四邻（气边另看其间的栅栏，距离 1），覆盖目标最远经隔水 / 岩台远格到达距离 2 的格。
    /// AI 每小回合对匠人的每个改造选项各预演一次，从零建表要对 73×73 画布逐格查一遍地图。
    /// </remarks>
    private sealed class MapTables
    {
        /// <summary>改造使派生表作废的范围：改造目标格（栅栏取两端）周围切比雪夫距离不超过此值的格。覆盖目标的最远距离是 2。</summary>
        internal const int EditReach = 2;

        private const byte Computed = 1;
        private const byte Playable = 2;
        private const byte LibertyWhenEmpty = 4;
        private const byte Relic = 8;

        private readonly MapData map;
        private readonly byte[] _flags;
        private readonly int[] _zone;
        private readonly ImmutableArray<Coord>[] _liberty;
        private readonly ImmutableArray<Coord>[] _eye;
        private readonly ImmutableArray<Coord>[] _coverage;

        private ImmutableArray<int> _playable;
        private ImmutableArray<int> _ordinal;

        public MapTables(MapData map)
        {
            this.map = map;
            int size = map.Width * map.Height;
            _flags = new byte[size];
            _zone = new int[size];
            _liberty = new ImmutableArray<Coord>[size];
            _eye = new ImmutableArray<Coord>[size];
            _coverage = new ImmutableArray<Coord>[size];
        }

        private MapTables(MapData map, MapTables parent)
        {
            this.map = map;
            _flags = (byte[])parent._flags.Clone();
            _zone = (int[])parent._zone.Clone();
            _liberty = (ImmutableArray<Coord>[])parent._liberty.Clone();
            _eye = (ImmutableArray<Coord>[])parent._eye.Clone();
            _coverage = (ImmutableArray<Coord>[])parent._coverage.Clone();
        }

        /// <summary>
        /// 改造换图时的新表：<paramref name="edited"/> 必须是 <see cref="TerrainWriter.ApplyAll"/> 对 <paramref name="parent"/> 所属地图应用 <paramref name="edits"/> 的结果。
        /// 画布、障碍、出生区与信物格不是同一份（同一实例）时不派生，从零建表。
        /// </summary>
        public static MapTables Derive(MapTables parent, MapData edited, IReadOnlyList<TerrainEdit> edits)
        {
            MapData old = parent.map;
            if (old.Width != edited.Width || old.Height != edited.Height
                || !ReferenceEquals(old.Obstacles, edited.Obstacles)
                || old.BirthZones != edited.BirthZones
                || !ReferenceEquals(old.RelicCells, edited.RelicCells))
            {
                return new MapTables(edited);
            }

            var tables = new MapTables(edited, parent);
            foreach (TerrainEdit edit in edits)
            {
                if (edit.Kind == TerrainEditKind.Fence)
                {
                    tables.Invalidate(edit.Edge.A);
                    tables.Invalidate(edit.Edge.B);
                }
                else
                {
                    tables.Invalidate(edit.Cell);
                }
            }

            return tables;
        }

        /// <summary>作废 <paramref name="center"/> 周围 <see cref="EditReach"/> 格内（含越界裁剪）的全部逐格项。</summary>
        private void Invalidate(Coord center)
        {
            int width = map.Width;
            int height = map.Height;
            for (int y = Math.Max(0, center.Y - EditReach); y <= Math.Min(height - 1, center.Y + EditReach); y++)
            {
                for (int x = Math.Max(0, center.X - EditReach); x <= Math.Min(width - 1, center.X + EditReach); x++)
                {
                    int i = (y * width) + x;
                    _flags[i] = 0;
                    _liberty[i] = default;
                    _eye[i] = default;
                    _coverage[i] = default;
                }
            }
        }

        public bool IsPlayable(int i, Coord c) => (Flags(i, c) & Playable) != 0;

        /// <summary>全部可落子格的格下标，升序（即 <see cref="MapData.AllCoords"/> 的次序）；按本表逐格的可落子标志一次算出并记忆。</summary>
        public ImmutableArray<int> PlayableIndices()
        {
            EnsurePlayable();
            return _playable;
        }

        /// <summary>格下标 → 它在 <see cref="PlayableIndices"/> 里的序号；不可落子格为 −1。与前者同一次算出。</summary>
        public ImmutableArray<int> PlayableOrdinals()
        {
            EnsurePlayable();
            return _ordinal;
        }

        private void EnsurePlayable()
        {
            if (!_playable.IsDefault)
            {
                return;
            }

            int width = map.Width;
            var ordinal = new int[_flags.Length];
            ImmutableArray<int>.Builder playable = ImmutableArray.CreateBuilder<int>();
            for (int i = 0; i < _flags.Length; i++)
            {
                if (IsPlayable(i, CoordAt(width, i)))
                {
                    ordinal[i] = playable.Count;
                    playable.Add(i);
                }
                else
                {
                    ordinal[i] = -1;
                }
            }

            _ordinal = ImmutableCollectionsMarshal.AsImmutableArray(ordinal);
            _playable = playable.ToImmutable();
        }

        public Terrain TerrainAt(int i, Coord c) => IsPlayable(i, c) ? Terrain.Playable : Terrain.Obstacle;

        /// <summary>该格为空时能否作为气：可落子且地表不是浅滩（"空格能否作为气"的唯一判定，见 <see cref="GameBoard.GivesLiberty"/>）。</summary>
        public bool GivesLibertyWhenEmpty(int i, Coord c) => (Flags(i, c) & LibertyWhenEmpty) != 0;

        public bool IsRelicCell(int i, Coord c) => (Flags(i, c) & Relic) != 0;

        public int? BirthZoneOf(int i, Coord c)
        {
            _ = Flags(i, c);
            return _zone[i] >= 0 ? _zone[i] : null;
        }

        public ImmutableArray<Coord> LibertyNeighbors(int i, Coord c)
        {
            if (_liberty[i].IsDefault)
            {
                _liberty[i] = Adjacency.LibertyNeighbors(map, c);
            }

            return _liberty[i];
        }

        public ImmutableArray<Coord> EyeNeighbors(int i, Coord c)
        {
            if (_eye[i].IsDefault)
            {
                _eye[i] = Adjacency.EyeNeighbors(map, c);
            }

            return _eye[i];
        }

        public ImmutableArray<Coord> CoverageTargets(int i, Coord c)
        {
            if (_coverage[i].IsDefault)
            {
                _coverage[i] = Adjacency.CoverageTargets(map, c);
            }

            return _coverage[i];
        }

        private byte Flags(int i, Coord c)
        {
            byte flags = _flags[i];
            if ((flags & Computed) == 0)
            {
                bool playable = map.IsPlayable(c);
                flags = Computed;
                if (playable)
                {
                    flags |= Playable;
                    if (map.SurfaceAt(c) != Surface.Shallows)
                    {
                        flags |= LibertyWhenEmpty;
                    }
                }

                if (map.RelicCells.ContainsKey(c))
                {
                    flags |= Relic;
                }

                _zone[i] = map.BirthZoneOf(c) ?? -1;
                _flags[i] = flags;
            }

            return flags;
        }
    }
}
