using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.PieceRelics;

namespace Siege.Core.Match;

/// <summary>
/// 大图（<see cref="MapTier.Large"/>）自由插旗的纯函数（big-map 裁决 2 / 4 / 5，design D3 / D4）：合规旗格、锁定时的间距改派、锁定后的落座图。
/// 三者都只读地图与旗格、不消费随机、不持有状态；<see cref="FlagPlanting"/>（正式插旗）、<see cref="PrototypeZoneAssignment"/>（AI 盲选）与存档 / 回放的落座重建共用这一份，MUST NOT 各写一遍。
/// 小图（出生座位）不经过这里。
/// </summary>
/// <remarks>
/// <para><b>合规旗格</b>（D3，段 A 追加裁决 19）：可落子、高度 0、不是桥格、不在任何平台内；旗心 13×13（锁定后物化的出生平台）整块在画布内、且与任何中立平台的外接矩形至少隔 <see cref="PlateClearance"/> 格
/// （不重叠落座图才过得了"出生棋盘不与平台重叠"的校验、构筑期的落子范围才不切进高台；隔 1 格是让中立平台坡道朝外的那格仍是"低地"而不是另一块棋盘的格——校验器 <c>PLATE_HEIGHT</c> 的口径）；
/// 旗心与任一普通信物遗址切比雪夫距离 ≥ <see cref="SiteClearance"/>（relic-sites 裁决 9：13×13 出生平台不盖遗址），与任一稀有遗址<b>区域</b>（<see cref="RelicSites.RareZoneOf"/>）同样 ≥ <see cref="SiteClearance"/>（relic-zones 裁决 3）；
/// 旗心 9×9（出生圈）内可落子格 ≥ <see cref="MinRingPlayable"/>（避免把出生圈压在海湾尖角上）。不合规的原因由 <see cref="FlagCellProblem"/> 给出文案，界面与终端据此提示。</para>
/// <para><b>改派</b>（D3）：按插旗先后逐个确认——与已确认的每一面旗切比雪夫距离 ≥ <see cref="FlagGap"/> 即保留；否则改派到"与全部已确认旗间距 ≥ FlagGap 的合规旗格里离原旗最近者"
/// （切比雪夫距离，并列按先行后列取第一个）；一个都没有时改派到"到已确认旗的最小间距最大"的合规旗格（并列同样先行后列）。全程确定性、不消费随机。</para>
/// <para><b>落座图</b>（D4，歧义 18）：出生区 z = 旗 z 心 9×9 ∩ 可落子，棋盘清单 = 每人一块 13×13 的出生棋盘（原点 = 旗 − (6, 6)、不带主题，按出生区编号）+ 既有中立平台；
/// 地形、障碍不动，<see cref="MapData.MapVersion"/> 置 1 区分锁定前后。锁定后大图与小图对下游是同一种东西：<see cref="MapData.BirthBoard"/>（13×13）给构筑期范围，<see cref="MapData.BirthZones"/>（9×9）给归属色与座位投影。</para>
/// </remarks>
public static class FlagRules
{
    /// <summary>任意两旗的最小切比雪夫距离（裁决 2：开局互不感知）。</summary>
    public const int FlagGap = LinkMapGenerator.LargeFlagGap;

    /// <summary>锁定时物化的出生平台边长（裁决 5：旗心半径 6 的方域 13×13）。</summary>
    public const int BirthPlateSide = LinkMapGenerator.LargeBirthPlateSide;

    /// <summary>出生圈边长（裁决 4：旗心 9×9）。</summary>
    public const int BirthRingSide = LinkMapGenerator.LargeBirthRingSide;

    /// <summary>出生圈 9×9 内可落子格的下限（D3）。</summary>
    public const int MinRingPlayable = 40;

    /// <summary>出生平台外接矩形与任何中立平台外接矩形之间至少隔开的格数（追加裁决 19；校验器大图行的出生棋盘间隔同值）。</summary>
    public const int PlateClearance = 1;

    /// <summary>旗心与任一信物遗址的最小切比雪夫距离（relic-sites 裁决 9，= <see cref="RelicSites.FlagClearance"/> 7：13×13 出生平台盖不住遗址）。</summary>
    public const int SiteClearance = RelicSites.FlagClearance;

    /// <summary>旗心到出生平台边的格数（6）与到出生圈边的格数（4）。</summary>
    public const int PlateReach = (BirthPlateSide - 1) / 2;
    public const int RingReach = (BirthRingSide - 1) / 2;

    /// <summary>切比雪夫距离（横纵相隔格数取大者）：旗间距的唯一口径。</summary>
    public static int Distance(Coord a, Coord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>旗 <paramref name="flag"/> 的出生平台外接矩形（13×13，原点 = 旗 − (6, 6)，不带主题）。</summary>
    public static BoardPlate BirthPlateOf(Coord flag) => new(new Coord(flag.X - PlateReach, flag.Y - PlateReach), BirthPlateSide, BirthPlateSide, BoardPlateKind.Birth);

    /// <summary>旗 <paramref name="flag"/> 的出生圈 9×9 ∩ 可落子格。</summary>
    public static ImmutableHashSet<Coord> BirthRingOf(MapData map, Coord flag)
    {
        ArgumentNullException.ThrowIfNull(map);
        var cells = ImmutableHashSet.CreateBuilder<Coord>();
        for (int y = Math.Max(0, flag.Y - RingReach); y <= Math.Min(map.Height - 1, flag.Y + RingReach); y++)
        {
            for (int x = Math.Max(0, flag.X - RingReach); x <= Math.Min(map.Width - 1, flag.X + RingReach); x++)
            {
                var c = new Coord(x, y);
                if (map.IsPlayable(c))
                {
                    cells.Add(c);
                }
            }
        }

        return cells.ToImmutable();
    }

    /// <summary>
    /// 全部合规旗格，先行后列（自下而上、自西向东）。大图基图上调用；小图或落座图（已有出生区）返回空。
    /// 判定见类注释；用可落子格的二维前缀和数 9×9 内的可落子格，整张图一次遍历。
    /// </summary>
    public static ImmutableArray<Coord> LegalFlagCells(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Tier != MapTier.Large || !map.BirthZones.IsDefaultOrEmpty)
        {
            return [];
        }

        int w = map.Width, h = map.Height;
        int[] prefix = PlayablePrefix(map);
        bool[] blocked = BlockedByPlatesOrSites(map);
        var legal = ImmutableArray.CreateBuilder<Coord>();
        for (int y = PlateReach; y < h - PlateReach; y++)
        {
            for (int x = PlateReach; x < w - PlateReach; x++)
            {
                var c = new Coord(x, y);
                if (!blocked[(y * w) + x] && IsFlagGround(map, c) && RingPlayable(prefix, w, c) >= MinRingPlayable)
                {
                    legal.Add(c);
                }
            }
        }

        return legal.ToImmutable();
    }

    /// <summary>单格判定（与 <see cref="LegalFlagCells"/> 同一口径，供界面与 <see cref="FlagPlanting.PlantAt"/> 逐格查）：<see cref="FlagCellProblem"/> 为 <c>null</c> 即合规。</summary>
    public static bool IsLegalFlagCell(MapData map, Coord c) => FlagCellProblem(map, c) is null;

    /// <summary>
    /// 单格不合规的原因（relic-sites 裁决 9 起界面要能区分"压到遗址"与其他原因）：合规返回 <c>null</c>，否则一句中文说明（第一条不满足的判据）。
    /// 与 <see cref="LegalFlagCells"/> 同一口径，判据次序：档位 / 基图 → 在画布内 → 旗格本身（可落子、不在平台内、高度 0、非桥）→ 13×13 在画布内 → 不压中立平台 → 与稀有遗址区域 ≥ 7 → 与普通遗址 ≥ 7 → 9×9 可落子 ≥ 40。
    /// </summary>
    public static string? FlagCellProblem(MapData map, Coord c)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Tier != MapTier.Large || !map.BirthZones.IsDefaultOrEmpty)
        {
            return $"地图 {map.Id} 不是大图基图（小图用出生座位，锁定后不再插旗）。";
        }

        if (!map.Contains(c))
        {
            return $"{c.ToNotation()} 不在画布内。";
        }

        if (!map.IsPlayable(c))
        {
            return $"{c.ToNotation()} 不可落子。";
        }

        if (map.Boards.Any(p => p.Contains(c)))
        {
            return $"{c.ToNotation()} 在平台内，旗不能插在平台上。";
        }

        if (map.HeightAt(c) != 0)
        {
            return $"{c.ToNotation()} 不是低地（高度 {map.HeightAt(c)}），旗只能插在高度 0 的格。";
        }

        if (map.HasBridge(c))
        {
            return $"{c.ToNotation()} 是桥格，旗不能插在桥上。";
        }

        // 旗心 13×13 整块在画布内（先判，再构造平台——坐标类型不接受负值）。
        if (c.X < PlateReach || c.Y < PlateReach || c.X + PlateReach >= map.Width || c.Y + PlateReach >= map.Height)
        {
            return $"{c.ToNotation()} 离画布边不足 {PlateReach} 格，旗心 {BirthPlateSide}×{BirthPlateSide} 出生平台放不进画布。";
        }

        BoardPlate plate = BirthPlateOf(c);
        if (map.Boards.Any(p => !MapValidator.PlatesApart(p, plate, PlateClearance)))
        {
            return $"{c.ToNotation()} 的 {BirthPlateSide}×{BirthPlateSide} 出生平台压到或贴着中立平台（须至少隔 {PlateClearance} 格）。";
        }

        // relic-zones 裁决 3：稀有遗址只公开区域，旗心离区域须 ≥ 7（区域外扩 6 圈整块禁插）——遗址的 13×13 禁插方块因此整个包在里面，悬停逐格探红框也找不出遗址；文案只提区域、不带遗址坐标。
        foreach (RareSiteZone zone in RelicSites.RareZones(map))
        {
            int d = zone.DistanceTo(c);
            if (d == 0)
            {
                return $"{c.ToNotation()} 在稀有信物遗址区域内（遗址具体位置需进图探索），旗不能插在区域里。";
            }

            if (d < SiteClearance)
            {
                return $"{c.ToNotation()} 离稀有信物遗址区域只有 {d} 格，旗心须离区域 ≥ {SiteClearance}（{BirthPlateSide}×{BirthPlateSide} 出生平台不得伸进区域）。";
            }
        }

        foreach (RelicSite site in map.RelicSites.Where(s => s.Tier == RelicTier.Common))
        {
            int d = Distance(c, site.Cell);
            if (d < SiteClearance)
            {
                return $"{c.ToNotation()} 与信物遗址 {site.Cell.ToNotation()} 相距 {d}，旗心与任一遗址须相距 ≥ {SiteClearance}（{BirthPlateSide}×{BirthPlateSide} 出生平台不得盖住遗址）。";
            }
        }

        int ring = BirthRingOf(map, c).Count;
        return ring >= MinRingPlayable ? null : $"{c.ToNotation()} 的 {BirthRingSide}×{BirthRingSide} 出生圈内只有 {ring} 个可落子格（须 ≥ {MinRingPlayable}）。";
    }

    /// <summary>
    /// 锁定时的改派（D3）：<paramref name="planted"/> 按插旗先后；返回同序的确认旗格。每面旗与此前已确认的旗间距 ≥ <see cref="FlagGap"/> 即保留，否则在 <paramref name="legal"/> 里改派（见类注释）。
    /// 不消费随机。<paramref name="legal"/> 须按先行后列排好（<see cref="LegalFlagCells"/> 的次序）。
    /// </summary>
    public static ImmutableArray<Coord> Reassign(IReadOnlyList<Coord> legal, IReadOnlyList<Coord> planted)
    {
        ArgumentNullException.ThrowIfNull(legal);
        ArgumentNullException.ThrowIfNull(planted);
        var confirmed = new List<Coord>();
        foreach (Coord flag in planted)
        {
            confirmed.Add(confirmed.All(q => Distance(flag, q) >= FlagGap) ? flag : Fallback(legal, confirmed, flag));
        }

        return [.. confirmed];
    }

    /// <summary>与 <paramref name="confirmed"/> 全部间距 ≥ <see cref="FlagGap"/> 的合规格里离 <paramref name="flag"/> 最近者；没有时取"最小间距最大"者。并列按 <paramref name="legal"/> 的次序取第一个。</summary>
    private static Coord Fallback(IReadOnlyList<Coord> legal, List<Coord> confirmed, Coord flag)
    {
        if (legal.Count == 0)
        {
            throw new SiegeRuleException("地图上没有任何合规旗格，无法改派。");
        }

        Coord? nearest = null;
        int nearestDistance = int.MaxValue;
        Coord widest = legal[0];
        int widestGap = -1;
        foreach (Coord c in legal)
        {
            int gap = confirmed.Count == 0 ? int.MaxValue : confirmed.Min(q => Distance(c, q));
            if (gap > widestGap)
            {
                widestGap = gap;
                widest = c;
            }

            if (gap >= FlagGap)
            {
                int d = Distance(c, flag);
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearest = c;
                }
            }
        }

        return nearest ?? widest;
    }

    /// <summary>
    /// 落座图（D4）：按出生区编号给出各旗 <paramref name="flagsByZone"/>，返回带出生区（9×9 ∩ 可落子）与出生棋盘（13×13，排在中立平台之前）的地图，<see cref="MapData.MapVersion"/> 为 1。
    /// 只能对大图基图（没有出生区）调用；旗格必须合规，否则抛 <see cref="SiegeRuleException"/>。确定性：同一基图与同一旗格得到同一张落座图。
    /// </summary>
    public static MapData Seat(MapData baseMap, IReadOnlyList<Coord> flagsByZone)
    {
        ArgumentNullException.ThrowIfNull(baseMap);
        ArgumentNullException.ThrowIfNull(flagsByZone);
        if (baseMap.Tier != MapTier.Large || !baseMap.BirthZones.IsDefaultOrEmpty)
        {
            throw new SiegeRuleException($"地图 {baseMap.Id} 不是大图基图（档位 {baseMap.Tier}、出生区 {baseMap.BirthZones.Length} 个），不能落座。");
        }

        foreach (Coord flag in flagsByZone)
        {
            if (!IsLegalFlagCell(baseMap, flag))
            {
                throw new SiegeRuleException($"旗格 {flag.ToNotation()} 不是地图 {baseMap.Id} 的合规旗格，不能落座。");
            }
        }

        return baseMap with
        {
            BirthZones = [.. flagsByZone.Select(flag => BirthRingOf(baseMap, flag))],
            Boards = [.. flagsByZone.Select(BirthPlateOf), .. baseMap.Boards],
            MapVersion = 1,
        };
    }

    /// <summary>旗格本身的条件：可落子、高度 0、不是桥格、不在任何平台的外接矩形内。</summary>
    private static bool IsFlagGround(MapData map, Coord c) =>
        map.IsPlayable(c) && map.HeightAt(c) == 0 && !map.HasBridge(c) && !map.Boards.Any(p => p.Contains(c));

    /// <summary>
    /// 旗心 13×13 会与某块平台重叠或贴着它的格（按平台外接矩形外扩 6 + <see cref="PlateClearance"/> 格标记），以及与某处普通信物遗址切比雪夫距离 &lt; <see cref="SiteClearance"/> 的格
    /// （遗址周围 13×13，relic-sites 裁决 9）、与某个稀有遗址区域距离 &lt; <see cref="SiteClearance"/> 的格（区域外扩 6 圈，relic-zones 裁决 3），下标 = y × 宽 + x。与 <see cref="FlagCellProblem"/> 的 <c>PlatesApart</c> / 遗址距离同一口径。
    /// </summary>
    private static bool[] BlockedByPlatesOrSites(MapData map)
    {
        int w = map.Width, h = map.Height;
        int reach = PlateReach + PlateClearance;
        var blocked = new bool[w * h];
        foreach (BoardPlate p in map.Boards)
        {
            for (int y = Math.Max(0, p.Origin.Y - reach); y < Math.Min(h, p.Origin.Y + p.Height + reach); y++)
            {
                for (int x = Math.Max(0, p.Origin.X - reach); x < Math.Min(w, p.Origin.X + p.Width + reach); x++)
                {
                    blocked[(y * w) + x] = true;
                }
            }
        }

        int siteReach = SiteClearance - 1;
        foreach (RareSiteZone zone in RelicSites.RareZones(map))
        {
            for (int y = Math.Max(0, zone.Origin.Y - siteReach); y <= Math.Min(h - 1, zone.Origin.Y + zone.Height - 1 + siteReach); y++)
            {
                for (int x = Math.Max(0, zone.Origin.X - siteReach); x <= Math.Min(w - 1, zone.Origin.X + zone.Width - 1 + siteReach); x++)
                {
                    blocked[(y * w) + x] = true;
                }
            }
        }

        foreach (RelicSite site in map.RelicSites.Where(s => s.Tier == RelicTier.Common))
        {
            for (int y = Math.Max(0, site.Cell.Y - siteReach); y <= Math.Min(h - 1, site.Cell.Y + siteReach); y++)
            {
                for (int x = Math.Max(0, site.Cell.X - siteReach); x <= Math.Min(w - 1, site.Cell.X + siteReach); x++)
                {
                    blocked[(y * w) + x] = true;
                }
            }
        }

        return blocked;
    }

    /// <summary>可落子格的二维前缀和：<c>prefix[(y + 1) × (宽 + 1) + (x + 1)]</c> = 左下角到 (x, y) 的可落子格数。</summary>
    private static int[] PlayablePrefix(MapData map)
    {
        int w = map.Width, h = map.Height;
        int stride = w + 1;
        var prefix = new int[(h + 1) * stride];
        for (int y = 0; y < h; y++)
        {
            int row = 0;
            for (int x = 0; x < w; x++)
            {
                row += map.IsPlayable(new Coord(x, y)) ? 1 : 0;
                prefix[((y + 1) * stride) + x + 1] = prefix[(y * stride) + x + 1] + row;
            }
        }

        return prefix;
    }

    /// <summary>旗心 9×9（裁到画布内）里的可落子格数。</summary>
    private static int RingPlayable(int[] prefix, int width, Coord c)
    {
        int stride = width + 1;
        int height = (prefix.Length / stride) - 1;
        int x0 = Math.Max(0, c.X - RingReach), x1 = Math.Min(width - 1, c.X + RingReach);
        int y0 = Math.Max(0, c.Y - RingReach), y1 = Math.Min(height - 1, c.Y + RingReach);
        return prefix[((y1 + 1) * stride) + x1 + 1] - prefix[(y0 * stride) + x1 + 1] - prefix[((y1 + 1) * stride) + x0] + prefix[(y0 * stride) + x0];
    }
}
