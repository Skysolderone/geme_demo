using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>校验输出项的严重度（frontier-map D2）：拒绝项让地图无法加载；报告项只供人工判断（如边疆档各平台的距离），不影响加载。</summary>
public enum MapFindingSeverity
{
    /// <summary>拒绝加载。缺省值——既有的每一条校验失败都是拒绝项。</summary>
    Reject = 0,

    /// <summary>只报告，不拒绝。</summary>
    Report = 1,
}

/// <summary>一条地图校验输出：拒绝项（缺省）或报告项，见 <see cref="Severity"/>。</summary>
public readonly record struct MapValidationFailure(
    string Code,
    string Message,
    ImmutableArray<Coord> Coords)
{
    /// <summary>严重度，缺省为拒绝。</summary>
    public MapFindingSeverity Severity { get; init; } = MapFindingSeverity.Reject;

    public override string ToString() =>
        Coords.IsDefaultOrEmpty
            ? $"[{Code}] {Message}"
            : $"[{Code}] {Message}（{string.Join(", ", Coords.Select(c => c.ToNotation()))}）";
}

/// <summary>规则第 1 条的一项距离均衡目标：目标名、目标格、各出生区（按编号）到最近目标格的沿气边距离，<c>null</c> 为不可达或无目标。</summary>
public readonly record struct BirthZoneDistance(string Name, ImmutableArray<Coord> Targets, ImmutableArray<int?> Distances);

/// <summary>地图校验结果。</summary>
public sealed class MapValidationResult
{
    internal MapValidationResult(ImmutableArray<MapValidationFailure> findings)
    {
        Failures = [.. findings.Where(f => f.Severity == MapFindingSeverity.Reject)];
        Reports = [.. findings.Where(f => f.Severity == MapFindingSeverity.Report)];
    }

    /// <summary>拒绝项：任何一条都让地图无法加载。</summary>
    public ImmutableArray<MapValidationFailure> Failures { get; }

    /// <summary>报告项：与拒绝项出自同一次校验，但不影响 <see cref="IsValid"/>。标准档地图没有报告项。</summary>
    public ImmutableArray<MapValidationFailure> Reports { get; }

    public bool IsValid => Failures.IsEmpty;

    public override string ToString()
    {
        string verdict = IsValid ? "地图校验通过。" : string.Join(Environment.NewLine, Failures);
        return Reports.IsEmpty
            ? verdict
            : verdict + Environment.NewLine + "报告项（不影响加载）：" + Environment.NewLine + string.Join(Environment.NewLine, Reports);
    }
}

/// <summary>地图校验失败时抛出。</summary>
public sealed class MapValidationException : Exception
{
    public MapValidationException(MapValidationResult result)
        : base($"地图校验未通过：{Environment.NewLine}{result}") => Result = result;

    public MapValidationResult Result { get; }
}

/// <summary>
/// 地图静态校验。把必死口袋、距离失衡、出生区被崖壁封死这类问题挡在加载期——
/// 漏到对局中会表现为"某玩家莫名其妙被迫 Pass"，极难归因。
/// 距离与连通区一律沿气边（<see cref="Adjacency.LibertyNeighbors"/>）：崖壁、栅栏、未架桥深水挡住的路不算路。
/// "障碍占外接区域 25%–35%"在 terrain-model 取消：深水与崖壁同样在压缩空间，只数障碍已无意义，密度由可落子格区间把控。
/// </summary>
/// <remarks>规格：openspec/changes/terrain-model/specs/map-definition —— Requirement: 地图静态校验规则</remarks>
public static class MapValidator
{
    /// <summary>
    /// 规格档声明表（frontier-map D2）：每个规格档的人数预算与各条分流规则的处理方式，<b>全部</b>写在这一张表里。
    /// 校验器里对规格档的分支只允许出现在这里——各条规则只读 <see cref="ProfileRules"/> 的字段，不得再写"如果是边疆档"的散落分支
    /// （守门 <c>规格档分流守门Tests</c>：本文件里规格档枚举的字面量只在本表内、地图的规格档属性只在 <see cref="RulesOf"/> 读一次）。
    /// <para>
    /// 人数预算：可落子格区间、信物格区间、出生区数区间、单个出生区可落子格区间。
    /// 标准档出生区数 = 人数；单区区间为 <c>null</c> 表示规格未给该人数的区间
    /// （2 / 3 人图尚未定稿），此项不校验。边疆档只定 4 人（300–420 / 5–8 区且多于人数 / 单区 20–225 / 信物 14–24，验证版估值）。
    /// 与规格档无关的"容得下 9 枚基础部署"下界对所有地图一律生效。
    /// </para>
    /// 棋盘档（board-map D1 / D8、board-isolated-gen D4）定 2 / 3 / 4 人（4 人 250–1000 / 出生区恰 5 个 / 信物 7–25；3 人 190–750 / 4 个 / 5–20；
    /// 2 人 125–500 / 3 个 / 4–15；单区一律 25–49；估值）：出生区数 = 人数 + 1。
    /// 外接尺寸区间与棋盘清单的处理方式同样是声明行的字段：标准档与边疆档宽度至多 25 列（列标只用单个字母，frontier-map 裁决 7）、行数不限、清单必须为空；
    /// 棋盘档列数与行数各 15–60（尺寸按摆放结果裁出）、清单不得为空，并按清单规则逐块校验棋盘、棋盘之外不得有可落子格。
    /// 可达性三项也是声明行的字段（board-isolated-gen D4：棋盘档的棋盘互不连通，不可达是常态）：
    /// 目标不可达是否拒绝、出生区到中央入口必须有通路、必须标注咽喉——标准档与边疆档三项都要求，棋盘档三项都豁免（距离只报告，不可达记为"不可达"）。
    /// 各档共用、不在表里的规则：必死口袋、桥在深水、栅栏在邻格、信物格合法、保护期容量。
    /// 旋转对称不在校验器里（原 <c>MapSymmetry</c> 只由标准档基准图的测试调用，已随旧图于 retire-legacy-maps 删除）。
    /// </summary>
    private static readonly ImmutableDictionary<MapProfile, ProfileRules> Rules =
        new Dictionary<MapProfile, ProfileRules>
        {
            [MapProfile.Standard] = new(
                "只提供 2 / 3 / 4 人的预算表",
                new Dictionary<int, Budget>
                {
                    [2] = new(50, 65, 7, 9, (2, 2), null),
                    [3] = new(75, 90, 10, 12, (3, 3), null),
                    [4] = new(95, 110, 13, 15, (4, 4), (12, 14)),
                }.ToImmutableDictionary(),
                ZonesMustExceedPlayers: false,
                DistanceHandling.RejectOnImbalance,
                ColumnRange: (0, 25),
                RowRange: null,
                PlateList: null,
                Reach: new(true, true, true)),
            [MapProfile.Frontier] = new(
                "边疆档验证版只提供 4 人的预算表，2 / 3 人边疆图尚未设计",
                new Dictionary<int, Budget>
                {
                    [4] = new(300, 420, 14, 24, (5, 8), (20, 225)),
                }.ToImmutableDictionary(),
                ZonesMustExceedPlayers: true,
                DistanceHandling.AlwaysReport,
                ColumnRange: (0, 25),
                RowRange: null,
                PlateList: null,
                Reach: new(true, true, true)),
            [MapProfile.Board] = new(
                "棋盘档只提供 2 / 3 / 4 人的预算表",
                new Dictionary<int, Budget>
                {
                    [2] = new(125, 500, 4, 15, (3, 3), (25, 49)),
                    [3] = new(190, 750, 5, 20, (4, 4), (25, 49)),
                    [4] = new(250, 1000, 7, 25, (5, 5), (25, 49)),
                }.ToImmutableDictionary(),
                ZonesMustExceedPlayers: true,
                DistanceHandling.AlwaysReport,
                ColumnRange: (15, 60),
                RowRange: (15, 60),
                PlateList: new((7, 15), (5, 7), MinGap: 2),
                Reach: new(false, false, false)),
        }.ToImmutableDictionary();

    /// <summary>距离均衡（规则第 1 条）的极差处理方式。目标不可达不在此列——由声明行的可达性字段决定（标准 / 边疆拒绝，棋盘档只报告）。</summary>
    private enum DistanceHandling
    {
        /// <summary>极差超容差即拒绝；不超则无输出。</summary>
        RejectOnImbalance,

        /// <summary>无论极差多少都把各出生区的距离作为报告项给出，不因极差拒绝。</summary>
        AlwaysReport,
    }

    /// <summary>
    /// 一个规格档的全部分流声明。外接宽度区间的下限为 0 表示不设下限（尺寸为正由结构校验负责）；行数区间为 <c>null</c> 表示行数不限；
    /// 棋盘清单规则为 <c>null</c> 表示该档的棋盘清单必须为空。
    /// </summary>
    private sealed record ProfileRules(
        string SupportedPlayers,
        ImmutableDictionary<int, Budget> Budgets,
        bool ZonesMustExceedPlayers,
        DistanceHandling Distance,
        (int Min, int Max) ColumnRange,
        (int Min, int Max)? RowRange,
        PlateRules? PlateList,
        Reachability Reach);

    /// <summary>
    /// 可达性三项的处理（board-isolated-gen D4）：目标不可达即拒绝（否则报告项里记"不可达"）、
    /// 每个出生区必须有沿气边到中央入口的通路（规则第 7 条）、必须显式标注咽喉。
    /// </summary>
    private readonly record struct Reachability(bool RejectUnreachableTargets, bool RequireEntrancePath, bool RequireChokes);

    /// <summary>
    /// 棋盘清单规则（「棋盘档预算与校验」第 1–4 条的数值）：公共棋盘边长区间、出生棋盘边长区间、棋盘两两的最小间隔。
    /// </summary>
    private sealed record PlateRules(
        (int Min, int Max) PublicSide,
        (int Min, int Max) BirthSide,
        int MinGap);

    /// <summary>一档人数的规模预算。</summary>
    private readonly record struct Budget(
        int MinPlayable,
        int MaxPlayable,
        int MinRelics,
        int MaxRelics,
        (int Min, int Max) BirthZones,
        (int Min, int Max)? BirthZoneCells);

    /// <summary>全文件唯一读取规格档的地方。未定义的规格档值（只可能来自手工构造）没有声明行，返回 <c>null</c>。</summary>
    private static ProfileRules? RulesOf(MapData map) => Rules.GetValueOrDefault(map.Profile);

    /// <summary>前三大回合单玩家最多 9 枚基础部署，出生区必须容得下。</summary>
    private const int ProtectionPhaseDeployments = 9;

    public static MapValidationResult Validate(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        ImmutableArray<MapValidationFailure>.Builder f = ImmutableArray.CreateBuilder<MapValidationFailure>();

        ValidateStructure(map, f);
        if (RulesOf(map) is not { } rules)
        {
            // 没有声明行的只有未定义的取值（只可能来自手工构造）：拒绝加载并说明原因，不往下跑任何一条规则。
            f.Add(new MapValidationFailure(
                "MAP_PROFILE_UNKNOWN",
                "该规格档没有校验声明表：只有标准档、边疆档与棋盘档可以加载。",
                ImmutableArray<Coord>.Empty));
            return new MapValidationResult(f.ToImmutable());
        }

        // 外接尺寸不合该档的区间、或棋盘清单与该档不符：规模已经不对，到此为止——后面的规则只会报出一堆派生失败。
        if (!ValidateExtent(map, rules.ColumnRange, rules.RowRange, f) || !ValidatePlateList(map, rules.PlateList, f))
        {
            return new MapValidationResult(f.ToImmutable());
        }

        Budget? budget = rules.Budgets.TryGetValue(map.MaxPlayers, out Budget found) ? found : null;

        ValidateTerrainBounds(map, f);
        ValidatePlayableCount(map, budget, f);
        ValidateBudgets(map, rules, budget, f);
        ValidateBirthZones(map, budget, f);
        ValidateRelicCells(map, f);
        Reachability reach = rules.Reach;
        ValidateLandmarks(map, reach.RequireChokes, f);
        ValidateTolerance(map, f);
        ValidatePockets(map, f);
        if (reach.RequireEntrancePath)
        {
            ValidateBirthZoneConnectivity(map, f);
        }

        ValidateDistanceBalance(map, rules.Distance, reach.RejectUnreachableTargets, f);

        return new MapValidationResult(f.ToImmutable());
    }

    /// <summary>
    /// 外接宽度必须落在该档声明的区间内；该档声明了行数区间时行数也必须落在其中（<paramref name="rows"/> 为 <c>null</c> 即行数不限）。
    /// 不通过返回 <c>false</c>。
    /// </summary>
    private static bool ValidateExtent(
        MapData map, (int Min, int Max) columns, (int Min, int Max)? rows, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (map.Width > columns.Max)
        {
            string rowNote = rows is null ? "；行数不限" : string.Empty;
            f.Add(new MapValidationFailure(
                "MAP_TOO_WIDE",
                $"地图宽 {map.Width} 列，超出该规格档的 {columns.Max} 列上限{rowNote}。",
                ImmutableArray<Coord>.Empty));
            return false;
        }

        if (map.Width < columns.Min)
        {
            f.Add(new MapValidationFailure(
                "MAP_TOO_NARROW",
                $"地图宽 {map.Width} 列，不足该规格档的 {columns.Min} 列下限。",
                ImmutableArray<Coord>.Empty));
            return false;
        }

        if (rows is not { } range)
        {
            return true;
        }

        if (map.Height > range.Max)
        {
            f.Add(new MapValidationFailure(
                "MAP_TOO_TALL",
                $"地图高 {map.Height} 行，超出该规格档的 {range.Max} 行上限。",
                ImmutableArray<Coord>.Empty));
            return false;
        }

        if (map.Height < range.Min)
        {
            f.Add(new MapValidationFailure(
                "MAP_TOO_SHORT",
                $"地图高 {map.Height} 行，不足该规格档的 {range.Min} 行下限。",
                ImmutableArray<Coord>.Empty));
            return false;
        }

        return true;
    }

    /// <summary>
    /// 棋盘清单（board-map「棋盘清单」「棋盘档预算与校验」第 1–5 条）。<paramref name="declared"/> 为 <c>null</c> 的档清单必须为空；
    /// 否则清单不得为空，并逐块校验棋盘、检查棋盘之外没有可落子格。返回 <c>false</c> 表示清单与该档根本不符，后面的规则不必再跑。
    /// </summary>
    private static bool ValidatePlateList(MapData map, PlateRules? declared, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (declared is not { } plates)
        {
            if (map.Boards.IsDefaultOrEmpty)
            {
                return true;
            }

            f.Add(new MapValidationFailure(
                "BOARDS_NOT_ALLOWED",
                $"标准档与边疆档地图的棋盘清单必须为空，实际有 {map.Boards.Length} 项。",
                [.. map.Boards.Select(b => b.Origin)]));
            return false;
        }

        if (map.Boards.IsDefaultOrEmpty)
        {
            f.Add(new MapValidationFailure(
                "BOARDS_REQUIRED", "棋盘档地图的棋盘清单不得为空。", ImmutableArray<Coord>.Empty));
            return false;
        }

        int[,] owner = ValidatePlateCells(map, plates, f);
        ValidatePlateGaps(map, plates, f);
        ValidateBirthPlates(map, f);
        ValidateBirthPlateSizes(map, f);
        ValidateFlatScenery(map, owner, f);
        ValidateSceneryCells(map, owner, f);
        return true;
    }

    /// <summary>棋盘的称呼：类别、外接矩形的两个对角与尺寸，如"公共棋盘 J9–S17（9×9）"。</summary>
    private static string Describe(BoardPlate plate)
    {
        string kind = plate.Kind == BoardPlateKind.Birth ? "出生棋盘" : "公共棋盘";
        string corner = plate.Width > 0 && plate.Height > 0
            ? "–" + new Coord(plate.Origin.X + plate.Width - 1, plate.Origin.Y + plate.Height - 1).ToNotation()
            : string.Empty;
        return $"{kind} {plate.Origin.ToNotation()}{corner}（{plate.Width}×{plate.Height}）";
    }

    /// <summary>
    /// 第 1、2 条：棋盘在盘内、边长在区间内（公共 / 出生各自的区间），外接矩形内每一格都是 h=0 草地的可落子格。
    /// 返回"格 → 所属棋盘下标"的表（不属于任何棋盘为 −1；重叠时取清单里靠前的一块，重叠本身由间隔规则报出）。
    /// </summary>
    private static int[,] ValidatePlateCells(MapData map, PlateRules plates, ImmutableArray<MapValidationFailure>.Builder f)
    {
        var owner = new int[Math.Max(0, map.Width), Math.Max(0, map.Height)];
        foreach (Coord c in map.AllCoords())
        {
            owner[c.X, c.Y] = -1;
        }

        for (int i = 0; i < map.Boards.Length; i++)
        {
            BoardPlate plate = map.Boards[i];
            bool birth = plate.Kind == BoardPlateKind.Birth;
            (int Min, int Max) side = birth ? plates.BirthSide : plates.PublicSide;
            if (plate.Width < side.Min || plate.Width > side.Max || plate.Height < side.Min || plate.Height > side.Max)
            {
                f.Add(new MapValidationFailure(
                    "BOARD_SIZE_OUT_OF_RANGE",
                    $"{Describe(plate)} 的宽与高必须各在{(birth ? "出生" : "公共")}棋盘的合法边长 {side.Min}–{side.Max} 之内。",
                    [plate.Origin]));
            }

            if (plate.Width <= 0 || plate.Height <= 0
                || plate.Origin.X + plate.Width > map.Width || plate.Origin.Y + plate.Height > map.Height)
            {
                f.Add(new MapValidationFailure(
                    "BOARD_OUT_OF_BOUNDS", $"{Describe(plate)} 超出了地图的外接范围 {map.Width}×{map.Height}。", [plate.Origin]));
                continue;
            }

            var blocked = new List<Coord>();
            var uneven = new List<Coord>();
            foreach (Coord c in plate.Cells())
            {
                if (owner[c.X, c.Y] < 0)
                {
                    owner[c.X, c.Y] = i;
                }

                if (!map.IsPlayable(c))
                {
                    blocked.Add(c);
                }
                else if (map.HeightAt(c) != 0 || map.SurfaceAt(c) != Surface.Grass)
                {
                    uneven.Add(c);
                }
            }

            if (blocked.Count > 0)
            {
                f.Add(new MapValidationFailure(
                    "BOARD_CELL_NOT_PLAYABLE",
                    $"{Describe(plate)} 的外接矩形内有不可落子的格：棋盘内不得有障碍与深水。",
                    [.. blocked]));
            }

            if (uneven.Count > 0)
            {
                f.Add(new MapValidationFailure(
                    "BOARD_CELL_NOT_FLAT_GRASS",
                    $"{Describe(plate)} 的外接矩形内每一格都必须是高度 0 的草地。",
                    [.. uneven]));
            }
        }

        // 栅栏（含棋盘边界上的）：任一端点落在棋盘内即不合规。
        ImmutableArray<Coord> fenced = map.TerrainData.Fences
            .SelectMany(fence => new[] { fence.A, fence.B })
            .Where(c => map.Contains(c) && owner[c.X, c.Y] >= 0)
            .Distinct()
            .Order()
            .ToImmutableArray();
        if (!fenced.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "BOARD_MAP_FENCE", "棋盘内与棋盘边界上不得有栅栏。", fenced));
        }

        return owner;
    }

    /// <summary>第 3 条：棋盘外接矩形两两不重叠，且至少间隔若干格（两个方向上间隔的较大者；投影重叠的方向记为负）。</summary>
    private static void ValidatePlateGaps(MapData map, PlateRules plates, ImmutableArray<MapValidationFailure>.Builder f)
    {
        for (int i = 0; i < map.Boards.Length; i++)
        {
            for (int j = i + 1; j < map.Boards.Length; j++)
            {
                BoardPlate a = map.Boards[i];
                BoardPlate b = map.Boards[j];
                int dx = Math.Max(a.Origin.X - (b.Origin.X + b.Width), b.Origin.X - (a.Origin.X + a.Width));
                int dy = Math.Max(a.Origin.Y - (b.Origin.Y + b.Height), b.Origin.Y - (a.Origin.Y + a.Height));
                int gap = Math.Max(dx, dy);
                if (gap < plates.MinGap)
                {
                    string how = gap < 0 ? "重叠" : $"只隔 {gap} 格";
                    f.Add(new MapValidationFailure(
                        "BOARD_TOO_CLOSE",
                        $"{Describe(a)} 与 {Describe(b)} {how}：棋盘两两至少间隔 {plates.MinGap} 格。",
                        [a.Origin, b.Origin]));
                }
            }
        }
    }

    /// <summary>「棋盘清单」：出生棋盘与出生区一一对应，第 i 块出生棋盘的格子集合恰等于出生区 i。</summary>
    private static void ValidateBirthPlates(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        BoardPlate[] births = [.. map.Boards.Where(b => b.Kind == BoardPlateKind.Birth)];
        int zones = map.BirthZones.IsDefault ? 0 : map.BirthZones.Length;
        if (births.Length != zones)
        {
            f.Add(new MapValidationFailure(
                "BIRTH_BOARD_MISMATCH",
                $"出生棋盘 {births.Length} 块、出生区 {zones} 个：两者必须一一对应。",
                [.. births.Select(b => b.Origin)]));
            return;
        }

        for (int i = 0; i < births.Length; i++)
        {
            if (!map.BirthZones[i].SetEquals(births[i].Cells()))
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_BOARD_MISMATCH",
                    $"第 {i + 1} 块{Describe(births[i])} 的格子集合与{BirthZoneLabel.Of(i)} 不相等。",
                    [births[i].Origin]));
            }
        }
    }

    /// <summary>
    /// 第 1 条后半（builtin-board-maps D1）：全部出生棋盘的 {宽, 高} 无序对相同——开局空间对等，转 90° 摆放算同尺寸。
    /// 违例时按尺寸分组列出每块出生棋盘（以第一块出生棋盘的尺寸为准，坐标给出与它不同的那些）。
    /// </summary>
    private static void ValidateBirthPlateSizes(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        BoardPlate[] births = [.. map.Boards.Where(b => b.Kind == BoardPlateKind.Birth)];
        if (births.Length == 0)
        {
            return;
        }

        static (int Short, int Long) SizeOf(BoardPlate b) => (Math.Min(b.Width, b.Height), Math.Max(b.Width, b.Height));

        (int Short, int Long) reference = SizeOf(births[0]);
        BoardPlate[] odd = [.. births.Where(b => SizeOf(b) != reference)];
        if (odd.Length == 0)
        {
            return;
        }

        f.Add(new MapValidationFailure(
            "BIRTH_BOARD_SIZE_MISMATCH",
            $"出生棋盘尺寸不一：{string.Join("、", births.Select(Describe))}；全部出生棋盘的宽与高必须相同（可转 90°），"
                + $"与第一块（{reference.Short}×{reference.Long}）不同的有 {odd.Length} 块。",
            [.. odd.Select(b => b.Origin)]));
    }

    /// <summary>第 4、5 条：全图不得有深水；信物格必须在棋盘内。</summary>
    private static void ValidateFlatScenery(MapData map, int[,] owner, ImmutableArray<MapValidationFailure>.Builder f)
    {
        ImmutableArray<Coord> water = [.. map.AllCoords().Where(c => map.SurfaceAt(c) == Surface.DeepWater)];
        if (!water.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "BOARD_MAP_DEEP_WATER",
                "棋盘档地图不得含深水格（含架了桥的）：棋盘之外的格子必须全部是障碍格。",
                water));
        }

        ImmutableArray<Coord> strayRelics = map.RelicCells.Keys
            .Where(c => map.IsPlayable(c) && owner[c.X, c.Y] < 0)
            .Order()
            .ToImmutableArray();
        if (!strayRelics.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "RELIC_OUTSIDE_BOARD", "信物格必须位于棋盘内。", strayRelics));
        }
    }

    /// <summary>
    /// 第 4 条：不属于任何棋盘的格子必须全部是障碍格（board-isolated-gen：取消通道，棋盘之间只隔场景）。
    /// 棋盘外的可落子格一律拒绝，一条拒绝项列出全部这样的格（坐标序）——由此不同棋盘的格子之间不存在四邻接。
    /// </summary>
    private static void ValidateSceneryCells(MapData map, int[,] owner, ImmutableArray<MapValidationFailure>.Builder f)
    {
        ImmutableArray<Coord> loose = [.. map.AllCoords().Where(c => owner[c.X, c.Y] < 0 && map.IsPlayable(c)).Order()];
        if (!loose.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "SCENERY_CELL_PLAYABLE",
                "棋盘之外有可落子格：不属于任何棋盘的格子必须全部是障碍格，棋盘之间不得相连。",
                loose));
        }
    }

    /// <summary>
    /// 结构性前置校验。这些问题若不先报出来，后面的规则只会报出症状
    /// （"可落子格为 0"），把根因藏在一堆派生失败里。
    /// </summary>
    private static void ValidateStructure(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (map.Width <= 0 || map.Height <= 0)
        {
            f.Add(new MapValidationFailure(
                "MAP_DIMENSION_INVALID",
                $"地图外接尺寸必须为正，实际为 {map.Width}×{map.Height}。",
                ImmutableArray<Coord>.Empty));
        }

        if (string.IsNullOrWhiteSpace(map.Id))
        {
            f.Add(new MapValidationFailure(
                "MAP_ID_MISSING",
                "地图必须有标识：对局日志按地图标识归档（设计文档 §17）。",
                ImmutableArray<Coord>.Empty));
        }

        // 越界障碍不参与任何校验，等于被静默丢弃——占比、口袋、距离全都算不到它。
        ImmutableArray<Coord> outside = map.Obstacles.Where(c => !map.Contains(c)).Order().ToImmutableArray();
        if (!outside.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "OBSTACLE_OUT_OF_BOUNDS", "障碍格越界，不会参与任何校验。", outside));
        }
    }

    /// <summary>
    /// 地形坐标必须在盘内：高度 / 地表 / 桥 / 栅栏两端。越界的地形项不参与任何查询，等于被静默丢弃。
    /// "桥必须在深水格上、栅栏必须在相邻格之间"（规则第 6 条）已由 <see cref="TerrainData"/> 构造期强制，
    /// 到不了这里（terrain-model 裁决 A-1），此处不重复。
    /// </summary>
    private static void ValidateTerrainBounds(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        TerrainData t = map.TerrainData;
        ImmutableArray<Coord> outside = t.Heights.Keys
            .Concat(t.Surfaces.Keys)
            .Concat(t.Bridges)
            .Concat(t.Fences.SelectMany(fence => new[] { fence.A, fence.B }))
            .Where(c => !map.Contains(c))
            .Distinct()
            .Order()
            .ToImmutableArray();
        if (!outside.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "TERRAIN_OUT_OF_BOUNDS", "地形数据（高度 / 地表 / 桥 / 栅栏端点）含越界格，不会参与任何查询。", outside));
        }
    }

    /// <summary>
    /// 校验规则第 5 条：可落子格总数必须落在该人数的预算区间内（4 人 95–110，terrain-model 裁决 D18）。
    /// 单列成一步而不是混在信物 / 出生区预算里——改图时越界是最容易发生、又最难在对局中归因的一类错误
    /// （表现为"密度不对、冲突时点漂移"，而不是任何一条规则报错）。denser-map 裁决 5。
    /// </summary>
    private static void ValidatePlayableCount(MapData map, Budget? known, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (known is not { } budget)
        {
            return;
        }

        int playable = map.PlayableCount;
        if (playable < budget.MinPlayable || playable > budget.MaxPlayable)
        {
            f.Add(new MapValidationFailure(
                "PLAYABLE_COUNT_OUT_OF_RANGE",
                $"{map.MaxPlayers} 人地图的可落子格为 {playable}，超出 {budget.MinPlayable}–{budget.MaxPlayable} 区间。",
                ImmutableArray<Coord>.Empty));
        }
    }

    private static void ValidateBudgets(MapData map, ProfileRules rules, Budget? known, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (known is not { } budget)
        {
            f.Add(new MapValidationFailure(
                "UNSUPPORTED_PLAYER_COUNT",
                $"不支持的人数 {map.MaxPlayers}：{rules.SupportedPlayers}。",
                ImmutableArray<Coord>.Empty));
            return;
        }

        int relics = map.RelicCells.Count;
        if (relics < budget.MinRelics || relics > budget.MaxRelics)
        {
            // 规格的 Scenario 要求报出方向（"信物格少于 7"），不只是"超出区间"。
            string direction = relics < budget.MinRelics
                ? $"少于下限 {budget.MinRelics}"
                : $"多于上限 {budget.MaxRelics}";
            f.Add(new MapValidationFailure(
                "RELIC_COUNT_OUT_OF_RANGE",
                $"{map.MaxPlayers} 人地图的信物格为 {relics}，{direction}，超出 {budget.MinRelics}–{budget.MaxRelics} 区间。",
                ImmutableArray<Coord>.Empty));
        }

        // 规则第 4 条。区间退化成"恰等于人数"（标准档）时沿用原来的报文；否则报区间与方向（棋盘档恰 5 个也走这里）。
        int zones = map.BirthZones.Length;
        (int minZones, int maxZones) = budget.BirthZones;
        bool outOfRange = zones < minZones || zones > maxZones;
        if (outOfRange && minZones == maxZones && minZones == map.MaxPlayers)
        {
            f.Add(new MapValidationFailure(
                "BIRTH_ZONE_COUNT_MISMATCH",
                $"出生区数量 {zones} 必须等于该地图支持的最大人数 {map.MaxPlayers}。",
                ImmutableArray<Coord>.Empty));
        }
        else if (outOfRange)
        {
            string direction = zones < minZones ? $"低于下限 {minZones}" : $"高于上限 {maxZones}";
            f.Add(new MapValidationFailure(
                "BIRTH_ZONE_COUNT_OUT_OF_RANGE",
                $"{map.MaxPlayers} 人地图的出生区为 {zones} 个，{direction}，超出 {minZones}–{maxZones} 区间。",
                ImmutableArray<Coord>.Empty));
        }

        if (rules.ZonesMustExceedPlayers && zones <= map.MaxPlayers)
        {
            f.Add(new MapValidationFailure(
                "BIRTH_ZONE_COUNT_NOT_ABOVE_PLAYERS",
                $"出生区数量 {zones} 必须多于该地图支持的最大人数 {map.MaxPlayers}：没人选的平台是中立争夺区。",
                ImmutableArray<Coord>.Empty));
        }
    }

    private static void ValidateBirthZones(MapData map, Budget? budget, ImmutableArray<MapValidationFailure>.Builder f)
    {
        for (int i = 0; i < map.BirthZones.Length; i++)
        {
            ImmutableHashSet<Coord> zone = map.BirthZones[i];
            ImmutableArray<Coord> outside = zone.Where(c => !map.Contains(c)).Order().ToImmutableArray();
            if (!outside.IsEmpty)
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_OUT_OF_BOUNDS", $"{BirthZoneLabel.Of(i)} 含越界格。", outside));
            }

            int playable = zone.Count(map.IsPlayable);
            if (playable < ProtectionPhaseDeployments)
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_TOO_SMALL",
                    $"{BirthZoneLabel.Of(i)} 只有 {playable} 个可落子格，容不下前三大回合最多 {ProtectionPhaseDeployments} 枚基础部署。",
                    ImmutableArray<Coord>.Empty));
            }

            // 出生区内可以有障碍或深水，但不得把该区压到区间之外（denser-map：4 人图每区 12–14 格）。
            if (budget?.BirthZoneCells is { } range
                && (playable < range.Min || playable > range.Max))
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_SIZE_OUT_OF_RANGE",
                    $"{BirthZoneLabel.Of(i)} 有 {playable} 个可落子格，超出 {map.MaxPlayers} 人地图的 {range.Min}–{range.Max} 区间。",
                    ImmutableArray<Coord>.Empty));
            }

            // 第 9 条（terrain-surfaces）：出生区是起手阵地，新地表只出现在公共区域，避免起手条件因地表而不对等。
            foreach (Coord c in zone.Where(map.Contains).Order())
            {
                Surface surface = map.SurfaceAt(c);
                if (surface is Surface.Desert or Surface.Marsh or Surface.Crag or Surface.Shallows)
                {
                    f.Add(new MapValidationFailure(
                        "BIRTH_ZONE_SPECIAL_SURFACE",
                        $"{BirthZoneLabel.Of(i)} 的 {c.ToNotation()} 是{surface.DisplayName()}：出生区内不得有荒漠、沼泽、岩台或浅滩。",
                        [c]));
                }
            }

            for (int j = i + 1; j < map.BirthZones.Length; j++)
            {
                ImmutableArray<Coord> overlap = zone.Intersect(map.BirthZones[j]).Order().ToImmutableArray();
                if (!overlap.IsEmpty)
                {
                    f.Add(new MapValidationFailure(
                        "BIRTH_ZONE_OVERLAP", $"{BirthZoneLabel.Of(i)} 与 {BirthZoneLabel.Number(j)} 重叠。", overlap));
                }
            }
        }
    }

    private static void ValidateRelicCells(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        foreach ((Coord c, RelicCellSpec spec) in map.RelicCells.OrderBy(kv => kv.Key))
        {
            if (map.TerrainAt(c) != Terrain.Playable)
            {
                f.Add(new MapValidationFailure(
                    "RELIC_ON_NON_PLAYABLE", "信物格必须位于可落子格上。", [c]));
                continue;
            }

            int? zone = map.BirthZoneOf(c);
            bool inBirthZone = zone is not null;

            if (inBirthZone && spec.Zone != RelicZone.BirthZone)
            {
                f.Add(new MapValidationFailure(
                    "RELIC_ZONE_MISMATCH",
                    $"位于{BirthZoneLabel.Of(zone!.Value)} 的信物格被标注为 {spec.Zone}。", [c]));
            }
            else if (!inBirthZone && spec.Zone != RelicZone.Contested)
            {
                f.Add(new MapValidationFailure(
                    "RELIC_ZONE_MISMATCH",
                    $"位于公共区的信物格被标注为 {spec.Zone}。", [c]));
            }

            // 出生区不生成高阶信物；公共区的预算档位必须严格高于出生区。
            if (spec.Zone == RelicZone.BirthZone && spec.Budget != BudgetTier.Birth)
            {
                f.Add(new MapValidationFailure(
                    "RELIC_BUDGET_MISMATCH",
                    $"出生区信物格的预算档位必须是 {BudgetTier.Birth}，实际为 {spec.Budget}。", [c]));
            }

            if (spec.Zone == RelicZone.Contested && spec.Budget <= BudgetTier.Birth)
            {
                f.Add(new MapValidationFailure(
                    "RELIC_BUDGET_MISMATCH",
                    $"公共争夺区信物格的预算档位必须严格高于出生区，实际为 {spec.Budget}。", [c]));
            }
        }
    }

    /// <summary>中央入口与咽喉格必须可落子；<paramref name="requireChokes"/> 为真时还必须显式标注咽喉（棋盘档豁免）。</summary>
    private static void ValidateLandmarks(MapData map, bool requireChokes, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (map.TerrainAt(map.CentralEntrance) != Terrain.Playable)
        {
            f.Add(new MapValidationFailure(
                "CENTRAL_ENTRANCE_NOT_PLAYABLE", "中央入口必须位于可落子格上。", [map.CentralEntrance]));
        }

        ImmutableArray<Coord> badChokes = map.ChokePoints
            .Where(c => map.TerrainAt(c) != Terrain.Playable).Order().ToImmutableArray();
        if (!badChokes.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "CHOKE_NOT_PLAYABLE", "标注的咽喉格必须位于可落子格上。", badChokes));
        }

        if (requireChokes && map.ChokePoints.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "CHOKE_NOT_ANNOTATED",
                "地图必须显式标注主要咽喉格——不做自动识别，自动识别的错误会静默污染距离均衡校验。",
                ImmutableArray<Coord>.Empty));
        }
    }

    private static void ValidateTolerance(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        const int defaultTolerance = 1;
        if (map.DistanceTolerance < 0)
        {
            f.Add(new MapValidationFailure(
                "TOLERANCE_NEGATIVE", "距离容差不得为负。", ImmutableArray<Coord>.Empty));
        }

        if (map.DistanceTolerance > defaultTolerance && string.IsNullOrWhiteSpace(map.ToleranceRelaxReason))
        {
            f.Add(new MapValidationFailure(
                "TOLERANCE_RELAX_WITHOUT_REASON",
                $"距离容差放宽到 {map.DistanceTolerance}（默认 {defaultTolerance}）必须同时写明理由。",
                ImmutableArray<Coord>.Empty));
        }

        foreach (Coord c in map.PocketExemptions.Order())
        {
            if (!map.PocketExemptionReasons.ContainsKey(c))
            {
                f.Add(new MapValidationFailure(
                    "POCKET_EXEMPTION_WITHOUT_REASON", "每条必死口袋豁免都必须写明理由。", [c]));
            }
        }
    }

    /// <summary>
    /// 必死口袋：出生区内被障碍、崖壁、深水、栅栏或边界围成的沿气边连通空区，面积小于
    /// <see cref="MapData.MinTwoEyeArea"/> 即判失败，除非有显式豁免。
    /// 这是保守近似——不做完整死活判定，宁可误报由设计师确认，也不放过对局中卡死的风险。
    /// </summary>
    private static void ValidatePockets(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        var visited = new HashSet<Coord>();
        foreach (Coord start in map.AllCoords())
        {
            if (visited.Contains(start) || !map.IsPlayable(start))
            {
                continue;
            }

            ImmutableArray<Coord> component = FloodFill(map, start, visited);
            bool touchesBirthZone = component.Any(c => map.BirthZoneOf(c) is not null);
            if (!touchesBirthZone || component.Length >= map.MinTwoEyeArea)
            {
                continue;
            }

            if (component.Any(map.PocketExemptions.Contains))
            {
                continue;
            }

            f.Add(new MapValidationFailure(
                "DEAD_POCKET",
                $"出生区内存在面积 {component.Length} 的必死口袋，小于形成两眼所需的 {map.MinTwoEyeArea} 格。",
                component));
        }
    }

    /// <summary>
    /// 校验规则第 7 条：每个出生区至少有一条沿气边到中央入口的通路。高台出生区若四周全是崖壁 / 深水 / 栅栏，
    /// 对局根本无法开始。与第 1 条的"中央入口不可达"是同一根因的两条规则，各自报出（后者带的是距离口径）。
    /// </summary>
    private static void ValidateBirthZoneConnectivity(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        for (int z = 0; z < map.BirthZones.Length; z++)
        {
            Dictionary<Coord, int> dist = MultiSourceDistances(map, map.BirthZones[z]);
            if (dist.ContainsKey(map.CentralEntrance))
            {
                continue;
            }

            f.Add(new MapValidationFailure(
                "BIRTH_ZONE_ISOLATED",
                $"{BirthZoneLabel.Of(z)} 没有任何一条沿气边到中央入口 {map.CentralEntrance.ToNotation()} 的通路：它的边缘全是崖壁、深水、栅栏或障碍。",
                map.BirthZones[z].Where(map.IsPlayable).Order().ToImmutableArray()));
        }
    }

    /// <summary>
    /// 各出生区到每个距离均衡目标的最短落子距离（沿气边），规则第 1 条的唯一口径。
    /// 目标依次为：最近公共信物、中央入口、最近咽喉，共三项（restore-go-core-rules 起由五项改三项）。
    /// 距离为 <c>null</c> 表示该出生区到不了任一目标格；目标集合为空时整项距离全为 <c>null</c>。
    /// 校验器与 <c>Siege.Sim map</c> 的距离表共用这一份计算。
    /// </summary>
    public static ImmutableArray<BirthZoneDistance> DistanceTable(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);

        ImmutableArray<Coord> publicRelics = map.RelicCells
            .Where(kv => kv.Value.Zone == RelicZone.Contested)
            .Select(kv => kv.Key).Order().ToImmutableArray();

        var metrics = new (string Name, ImmutableArray<Coord> Targets)[]
        {
            ("最近公共信物", publicRelics),
            ("中央入口", [map.CentralEntrance]),
            ("最近咽喉", map.ChokePoints.Order().ToImmutableArray()),
        };

        Dictionary<Coord, int>[] zoneDistances = map.BirthZones.IsDefault
            ? []
            : [.. map.BirthZones.Select(z => MultiSourceDistances(map, z))];

        ImmutableArray<BirthZoneDistance>.Builder table = ImmutableArray.CreateBuilder<BirthZoneDistance>(metrics.Length);
        foreach ((string name, ImmutableArray<Coord> targets) in metrics)
        {
            ImmutableArray<int?>.Builder ds = ImmutableArray.CreateBuilder<int?>(zoneDistances.Length);
            foreach (Dictionary<Coord, int> dist in zoneDistances)
            {
                int? best = null;
                foreach (Coord t in targets)
                {
                    if (dist.TryGetValue(t, out int d) && (best is null || d < best))
                    {
                        best = d;
                    }
                }

                ds.Add(best);
            }

            table.Add(new BirthZoneDistance(name, targets, ds.ToImmutable()));
        }

        return table.ToImmutable();
    }

    /// <summary>
    /// 距离均衡：各出生区到最近公共信物格、中央入口与主要咽喉的最短落子距离
    /// （沿气边——崖壁、栅栏、未架桥深水挡住的路不算路），两两差值不得超过容差。
    /// 任一出生区到任一目标不可达：<paramref name="rejectUnreachable"/> 为真即拒绝；为假（棋盘档：棋盘互不连通）时三类目标一律作为报告项给出，
    /// 不可达记为"不可达"（本图没有的目标同样记"不可达"），不计极差。极差：按 <paramref name="handling"/>——超容差即拒绝，
    /// 或不论极差多少都把逐区距离作为报告项给出（边疆档平台大小与远近本就不等，见规格档声明表）。
    /// </summary>
    private static void ValidateDistanceBalance(
        MapData map, DistanceHandling handling, bool rejectUnreachable, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (map.BirthZones.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (BirthZoneDistance metric in DistanceTable(map))
        {
            bool noTargets = metric.Targets.IsDefaultOrEmpty;
            if (noTargets && rejectUnreachable)
            {
                continue;
            }

            bool complete = true;
            for (int z = 0; z < metric.Distances.Length; z++)
            {
                if (metric.Distances[z] is null)
                {
                    complete = false;
                    if (rejectUnreachable)
                    {
                        f.Add(new MapValidationFailure(
                            "LANDMARK_UNREACHABLE",
                            $"{BirthZoneLabel.Of(z)} 无法到达{metric.Name}。", metric.Targets));
                    }
                }
            }

            if (!complete && !rejectUnreachable)
            {
                string partial = string.Join("，", metric.Distances.Select((d, z) => $"{BirthZoneLabel.Of(z)} = {(d is { } v ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "不可达")}"));
                string none = noTargets ? $"（本图没有{metric.Name}）" : string.Empty;
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_DISTANCE_REPORT",
                    $"各出生区到{metric.Name}的最短落子距离：{partial}{none}；有出生区不可达，不计极差（只报告，不因不可达拒绝）。",
                    ImmutableArray<Coord>.Empty) { Severity = MapFindingSeverity.Report });
                continue;
            }

            if (!complete)
            {
                continue;
            }

            int[] ds = [.. metric.Distances.Select(d => d!.Value)];
            int min = ds.Min();
            int max = ds.Max();
            string detail = string.Join("，", ds.Select((d, z) => $"{BirthZoneLabel.Of(z)} = {d}"));
            if (handling == DistanceHandling.AlwaysReport)
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_DISTANCE_REPORT",
                    $"各出生区到{metric.Name}的最短落子距离：{detail}；极差 {max - min}（只报告，不因极差拒绝）。",
                    ImmutableArray<Coord>.Empty) { Severity = MapFindingSeverity.Report });
            }
            else if (max - min > map.DistanceTolerance)
            {
                f.Add(new MapValidationFailure(
                    "DISTANCE_IMBALANCE",
                    $"各出生区到{metric.Name}的最短落子距离失衡（{detail}），"
                    + $"极差 {max - min} 超出容差 {map.DistanceTolerance}。",
                    ImmutableArray<Coord>.Empty));
            }
        }
    }

    private static ImmutableArray<Coord> FloodFill(MapData map, Coord start, HashSet<Coord> visited)
    {
        var component = new List<Coord>();
        var queue = new Queue<Coord>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            Coord current = queue.Dequeue();
            component.Add(current);
            // 连通区沿气边（terrain-model D-E）：崖壁、栅栏、深水隔开的两片各成一区。平地上与几何邻居等价。
            foreach (Coord n in Adjacency.LibertyNeighbors(map, current))
            {
                if (!visited.Contains(n))
                {
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return component.Order().ToImmutableArray();
    }

    private static Dictionary<Coord, int> MultiSourceDistances(MapData map, IEnumerable<Coord> sources)
    {
        var dist = new Dictionary<Coord, int>();
        var queue = new Queue<Coord>();
        foreach (Coord s in sources.Order())
        {
            if (!map.IsPlayable(s))
            {
                continue;
            }

            dist[s] = 0;
            queue.Enqueue(s);
        }

        while (queue.Count > 0)
        {
            Coord current = queue.Dequeue();
            // 最短落子距离沿气边（terrain-model D-E）：崖壁挡住的路不算路。平地上与几何邻居等价。
            foreach (Coord n in Adjacency.LibertyNeighbors(map, current))
            {
                if (!dist.ContainsKey(n))
                {
                    dist[n] = dist[current] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        return dist;
    }

}
