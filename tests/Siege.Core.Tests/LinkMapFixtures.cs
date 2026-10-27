using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests;

/// <summary>
/// 测试用链接档地图（board-link-mode 段 A → open-map 建局紧贴）：手写的最小合规数据，不经生成器，便于单独验证校验器、战场导出与改造规则。
/// 画布口径与生成器相同（裁决 J4 / J7）：原点固定，棋盘底边在第 2 行、最西列从第 2 列起，东 / 北边界外留 2 格。
/// </summary>
internal static class LinkMapFixtures
{
    /// <summary>
    /// 五块 6×6 出生棋盘按 open-map D1 的 3+2 布局紧贴：第一行 x = 2 / 8 / 14、y = 2；第二行 x = 2 / 8、y = 8（棋盘 3 北贴棋盘 0、棋盘 4 北贴棋盘 1）。下标即出生区编号。
    /// 围棋记法（列字母跳过 I）：棋盘 0 C3–H8、1 J3–O8、2 P3–U8、3 C9–H14、4 J9–O14；画布 22×16（列 A–W、行 1–16），场景格只剩四周留白。
    /// 棋盘 0 的坐标与改动前（一字排开、间距 3）完全相同，只涉及棋盘 0 的用例不受影响。
    /// 各带一个主题（link-map-terrain 1.1：链接档出生棋盘读入时缺主题被拒，夹具经文件往返的测试因此要带上）；主题只用于生成、校验与呈现，不影响这里的任何几何。
    /// link-map-terrain 第 2 组：边长由 5 改 6（裁决 M9 的下限），出生区改为核心区（左下角起 (1,1)–(3,3)），中间带各放 2 格林地凑足"至少 2 个可改造目标"（裁决 M7）。
    /// </summary>
    internal static readonly ImmutableArray<BoardPlate> Births =
    [
        new(new Coord(2, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Plateau),
        new(new Coord(8, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.River),
        new(new Coord(14, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Forest),
        new(new Coord(2, 8), 6, 6, BoardPlateKind.Birth, BoardTheme.Palisade),
        new(new Coord(8, 8), 6, 6, BoardPlateKind.Birth, BoardTheme.Mixed),
    ];

    /// <summary>22×16 的链接档初始图：只有 5 块紧贴的出生棋盘，各 2 个出生区档信物格（核心区对角）与 2 格林地，其余全部障碍；可落子 180、信物 10；恰 1 个连通分量。</summary>
    internal static MapData Initial() => Build("test-link-5", Births);

    /// <summary>
    /// 出生棋盘 <paramref name="plate"/> 的两格林地：中间带按先行后列的第一格与最后一格（6×6 时是左下角起 (4,1) 与 (4,4)，东列两端、不相邻）。
    /// 没有中间带的尺寸（5×5 等越界夹具）为空。
    /// </summary>
    internal static ImmutableArray<Coord> ForestOf(BoardPlate plate)
    {
        Coord[] middle = [.. plate.Cells().Where(plate.InMiddleBand)];
        return middle.Length < 2 ? [] : [middle[0], middle[^1]];
    }

    /// <summary>
    /// 按棋盘清单拼一张链接档图：画布 = 清单外接矩形的东 / 北边界各外扩 2 格（原点固定），棋盘之外一律障碍，出生区 = 出生棋盘的核心区（<see cref="BoardPlate.CoreCells"/>），
    /// 每块出生棋盘的信物格固定在核心区对角（核心区左下角与右上角：不相邻、不同行不同列），林地见 <see cref="ForestOf"/>。
    /// </summary>
    internal static MapData Build(string id, ImmutableArray<BoardPlate> boards)
    {
        int width = boards.Max(b => b.Origin.X + b.Width) + LinkMapGenerator.Margin;
        int height = boards.Max(b => b.Origin.Y + b.Height) + LinkMapGenerator.Margin;
        ImmutableHashSet<Coord> playable = [.. boards.SelectMany(b => b.Cells())];
        ImmutableDictionary<Coord, RelicCellSpec>.Builder relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        var surfaces = new List<(string, Surface)>();
        foreach (BoardPlate birth in boards.Where(b => b.Kind == BoardPlateKind.Birth))
        {
            Coord core = birth.CoreOrigin;
            relics[core] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
            relics[new Coord(core.X + 2, core.Y + 2)] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
            surfaces.AddRange(ForestOf(birth).Select(c => (c.ToNotation(), Surface.Forest)));
        }

        return new MapData
        {
            Id = id,
            Width = width,
            Height = height,
            MaxPlayers = 4,
            Profile = MapProfile.Link,
            Boards = boards,
            Obstacles = [.. TestMaps.Rect(0, 0, width, height).Where(c => !playable.Contains(c))],   // retire-legacy-maps：Rect 由已删除的 FrontierFixtures 原样迁到 TestMaps
            TerrainData = TestMaps.Terrain(surfaces: [.. surfaces]),
            BirthZones = [.. boards.Where(b => b.Kind == BoardPlateKind.Birth).Select(b => (ImmutableHashSet<Coord>)[.. b.CoreCells()])],
            RelicCells = relics.ToImmutable(),
            ChokePoints = [],
            CentralEntrance = new Coord(boards[0].Origin.X + (boards[0].Width / 2), boards[0].Origin.Y + (boards[0].Height / 2)),
        };
    }

    /// <summary>
    /// 浮空岛生成图补上段 A2 口径的信物格（手写夹具，map-expand-25 段 A3）：生成器已不放静态信物格（relic-redesign R29，2026-10-26 用户裁决），
    /// 本夹具只给仍在测<b>旧信物系统</b>（开局生成、强度预算、权重、揭示与控制）的测试保留覆盖，旧系统拆除时随 relic-redesign 一并删除。
    /// 每块平台核心区对角两格为出生区档（Birth，同 <see cref="Build"/>）；低地上按坐标升序贪心取至多 n+2 格公共区档（Standard）：
    /// 草地、非桥、可落子、距任一平台外接矩形切比雪夫距离 ≥ 2、两两切比雪夫距离 ≥ 5（与原生成器 D5 的候选条件相同，只是不随机）。
    /// 结果仍通过静态校验（信物格合法性规则只查分区与档位）。<see cref="MapData.Id"/> 不变：本图不得经地图标识回放。
    /// </summary>
    internal static MapData WithRelics(MapData map)
    {
        ImmutableDictionary<Coord, RelicCellSpec>.Builder relics = map.RelicCells.ToBuilder();
        foreach (BoardPlate plate in map.Boards)
        {
            Coord core = plate.CoreOrigin;
            relics[core] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
            relics[new Coord(core.X + 2, core.Y + 2)] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
        }

        static int Chebyshev(BoardPlate p, Coord c) =>
            Math.Max(Math.Max(Math.Max(p.Origin.X - c.X, c.X - (p.Origin.X + p.Width - 1)), 0), Math.Max(Math.Max(p.Origin.Y - c.Y, c.Y - (p.Origin.Y + p.Height - 1)), 0));
        var picked = new List<Coord>();
        foreach (Coord c in map.AllCoords().Order())
        {
            if (picked.Count == map.MaxPlayers + 2)
            {
                break;
            }

            if (map.IsPlayable(c) && map.SurfaceAt(c) == Surface.Grass && !map.HasBridge(c)
                && map.Boards.All(p => Chebyshev(p, c) >= 2)
                && picked.All(o => Math.Max(Math.Abs(o.X - c.X), Math.Abs(o.Y - c.Y)) >= 5))
            {
                picked.Add(c);
                relics[c] = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
            }
        }

        return map with { RelicCells = relics.ToImmutable() };
    }
}
