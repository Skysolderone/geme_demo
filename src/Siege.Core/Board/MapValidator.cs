using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>校验输出项的严重度（frontier-map D2）：拒绝项让地图无法加载；报告项只供人工判断（如各出生棋盘到三类目标的距离），不影响加载。</summary>
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

/// <summary>距离报告的一项目标：目标名、目标格、各出生区（按编号）到最近目标格的沿气边距离，<c>null</c> 为不可达或无目标。</summary>
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

    /// <summary>报告项：与拒绝项出自同一次校验，但不影响 <see cref="IsValid"/>。</summary>
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
/// 地图静态校验。把规模越界、出生棋盘不对等、棋盘外有可落子格这类问题挡在加载期——
/// 漏到对局中会表现为"某玩家莫名其妙被迫 Pass"或"冲突时点漂移"，极难归因。
/// 距离一律沿气边（<see cref="Adjacency.LibertyNeighbors"/>）：崖壁、栅栏、未架桥深水挡住的路不算路。
/// retire-legacy-maps 段 C 起只剩棋盘档：标准档 / 边疆档的声明行与只服务它们的规则（距离均衡拒绝、必须标注咽喉、出生区到中央入口的通路、
/// 目标不可达拒绝、距离容差、必死口袋、出生区新地表禁令、"出生区数 = 人数"）一并删除。
/// </summary>
/// <remarks>规格：openspec/changes/board-map、board-isolated-gen、retire-legacy-maps/specs/map-definition —— Requirement: 地图静态校验规则、棋盘档预算与校验</remarks>
public static class MapValidator
{
    /// <summary>
    /// 已删除的规格档（retire-legacy-maps D3）：枚举值仍在（标准档是地图数据规格档属性的缺省值，
    /// 缺规格档字段的旧地图文件按它读入），校验器对它报"已删除"并拒绝，不往下跑任何一条规则。
    /// 与 <see cref="Rules"/> 同属规格档声明区：规格档枚举的字面量只允许出现在这两张表里。
    /// </summary>
    private static readonly ImmutableDictionary<MapProfile, string> RetiredProfiles =
        new Dictionary<MapProfile, string>
        {
            [MapProfile.Standard] = "标准档",
        }.ToImmutableDictionary();

    /// <summary>
    /// 规格档声明表（frontier-map D2）：每个规格档的人数预算与各条分流规则的处理方式，<b>全部</b>写在这一张表里。
    /// 校验器里对规格档的分支只允许出现在这里与 <see cref="RetiredProfiles"/>——各条规则只读 <see cref="ProfileRules"/> 的字段，不得再写"如果是某档"的散落分支
    /// （守门 <c>规格档分流守门Tests</c>：本文件里规格档枚举的字面量只在两张声明表内、地图的规格档属性只在 <see cref="RulesOf"/> 读一次）。
    /// <para>
    /// retire-legacy-maps 段 C 起只剩棋盘档一行（board-map D1 / D8、board-isolated-gen D4）：定 2 / 3 / 4 人
    /// （4 人 250–1000 / 出生区恰 5 个 / 信物 7–25；3 人 190–750 / 4 个 / 5–20；2 人 125–500 / 3 个 / 4–15；单区一律 25–49；估值），出生区数 = 人数 + 1。
    /// 列数与行数各 15–60（尺寸按摆放结果裁出）；棋盘清单不得为空，并按清单规则逐块校验棋盘、棋盘之外不得有可落子格。
    /// 棋盘互不连通，距离一律只作报告项（不可达记为"不可达"），不因极差或不可达拒绝。
    /// </para>
    /// 不在表里的规则：结构、地形坐标在盘内、可落子格预算、出生区在盘内 / 不重叠 / 容得下 9 枚基础部署、信物格合法、中央入口与咽喉格可落子。
    /// 旋转对称不在校验器里（原 <c>MapSymmetry</c> 已随旧图于 retire-legacy-maps 删除）。
    /// </summary>
    private static readonly ImmutableDictionary<MapProfile, ProfileRules> Rules =
        new Dictionary<MapProfile, ProfileRules>
        {
            [MapProfile.Board] = new(
                "棋盘档只提供 2 / 3 / 4 人的预算表",
                new Dictionary<int, Budget>
                {
                    [2] = new(125, 500, 4, 15, (3, 3), (25, 49)),
                    [3] = new(190, 750, 5, 20, (4, 4), (25, 49)),
                    [4] = new(250, 1000, 7, 25, (5, 5), (25, 49)),
                }.ToImmutableDictionary(),
                ZonesMustExceedPlayers: true,
                ColumnRange: (15, 60),
                RowRange: (15, 60),
                PlateList: new((7, 15), (5, 7), MinGap: 2)),
        }.ToImmutableDictionary();

    /// <summary>一个规格档的全部分流声明：支持人数的说明、人数预算、出生区数是否必须多于人数、外接列数 / 行数区间、棋盘清单规则。</summary>
    private sealed record ProfileRules(
        string SupportedPlayers,
        ImmutableDictionary<int, Budget> Budgets,
        bool ZonesMustExceedPlayers,
        (int Min, int Max) ColumnRange,
        (int Min, int Max) RowRange,
        PlateRules PlateList);

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

    /// <summary>
    /// 全文件唯一读取规格档的地方：返回该档的声明行与"已删除"时的档名。两者都为 <c>null</c> 的只有未定义的取值（只可能来自手工构造）。
    /// </summary>
    private static (ProfileRules? Declared, string? Retired) RulesOf(MapData map) => Lookup(map.Profile);

    private static (ProfileRules? Declared, string? Retired) Lookup(MapProfile profile) =>
        (Rules.GetValueOrDefault(profile), RetiredProfiles.GetValueOrDefault(profile));

    /// <summary>前三大回合单玩家最多 9 枚基础部署，出生区必须容得下。</summary>
    private const int ProtectionPhaseDeployments = 9;

    public static MapValidationResult Validate(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        ImmutableArray<MapValidationFailure>.Builder f = ImmutableArray.CreateBuilder<MapValidationFailure>();

        ValidateStructure(map, f);
        (ProfileRules? declared, string? retired) = RulesOf(map);
        if (retired is not null)
        {
            // 已删除的档（缺规格档字段的旧地图文件按标准档读入）：明确报"已删除"并拒绝，不往下跑任何一条规则。
            f.Add(new MapValidationFailure(
                "MAP_PROFILE_RETIRED",
                $"{retired}已于 retire-legacy-maps 删除：所有地图都由互不连通的棋盘组成，只有棋盘档（Board）的地图可以加载。",
                ImmutableArray<Coord>.Empty));
            return new MapValidationResult(f.ToImmutable());
        }

        if (declared is not { } rules)
        {
            // 没有声明行的只有未定义的取值（只可能来自手工构造）：拒绝加载并说明原因，不往下跑任何一条规则。
            f.Add(new MapValidationFailure(
                "MAP_PROFILE_UNKNOWN",
                "该规格档没有校验声明表：只有棋盘档（Board）的地图可以加载。",
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
        ValidateLandmarks(map, f);
        ReportDistances(map, f);

        return new MapValidationResult(f.ToImmutable());
    }

    /// <summary>
    /// 外接列数与行数必须各落在该档声明的区间内。不通过返回 <c>false</c>。
    /// </summary>
    private static bool ValidateExtent(
        MapData map, (int Min, int Max) columns, (int Min, int Max) rows, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (map.Width > columns.Max)
        {
            f.Add(new MapValidationFailure(
                "MAP_TOO_WIDE",
                $"地图宽 {map.Width} 列，超出该规格档的 {columns.Max} 列上限。",
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

        if (map.Height > rows.Max)
        {
            f.Add(new MapValidationFailure(
                "MAP_TOO_TALL",
                $"地图高 {map.Height} 行，超出该规格档的 {rows.Max} 行上限。",
                ImmutableArray<Coord>.Empty));
            return false;
        }

        if (map.Height < rows.Min)
        {
            f.Add(new MapValidationFailure(
                "MAP_TOO_SHORT",
                $"地图高 {map.Height} 行，不足该规格档的 {rows.Min} 行下限。",
                ImmutableArray<Coord>.Empty));
            return false;
        }

        return true;
    }

    /// <summary>
    /// 棋盘清单（board-map「棋盘清单」「棋盘档预算与校验」第 1–5 条）：清单不得为空，并逐块校验棋盘、检查棋盘之外没有可落子格。
    /// 返回 <c>false</c> 表示清单为空、后面的规则不必再跑。
    /// </summary>
    private static bool ValidatePlateList(MapData map, PlateRules plates, ImmutableArray<MapValidationFailure>.Builder f)
    {
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
    /// 校验规则第 5 条：可落子格总数必须落在该人数的预算区间内（棋盘档 4 人 250–1000，见声明表）。
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

        // 规则第 4 条：报区间与方向（棋盘档 4 人恰 5 个）。原"出生区数必须等于人数"的报文只服务标准档，retire-legacy-maps 段 C 删除。
        int zones = map.BirthZones.Length;
        (int minZones, int maxZones) = budget.BirthZones;
        if (zones < minZones || zones > maxZones)
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

            // 单区可落子格必须落在该人数的区间内（棋盘档一律 25–49）。
            if (budget?.BirthZoneCells is { } range
                && (playable < range.Min || playable > range.Max))
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_SIZE_OUT_OF_RANGE",
                    $"{BirthZoneLabel.Of(i)} 有 {playable} 个可落子格，超出 {map.MaxPlayers} 人地图的 {range.Min}–{range.Max} 区间。",
                    ImmutableArray<Coord>.Empty));
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

    /// <summary>中央入口与标注的咽喉格必须可落子（棋盘档不要求标注咽喉，生成器恒写空；字段仍在，写了就得合法）。</summary>
    private static void ValidateLandmarks(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
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
    }

    /// <summary>
    /// 各出生区到每个距离目标的最短落子距离（沿气边），距离报告的唯一口径。
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
    /// 距离报告：各出生区到最近公共信物格、中央入口与最近咽喉的最短落子距离（沿气边——崖壁、栅栏、未架桥深水挡住的路不算路），
    /// 三类目标各一条报告项，不因极差或不可达拒绝（棋盘互不连通，不可达是常态）。有出生区不可达时逐区写"不可达"、不计极差
    /// （本图没有的目标同样记"不可达"并注明）；全部可达时写逐区距离与极差。
    /// retire-legacy-maps 段 C：原"极差超容差即拒绝""目标不可达即拒绝"只服务标准档 / 边疆档，随两档删除。
    /// </summary>
    private static void ReportDistances(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (map.BirthZones.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (BirthZoneDistance metric in DistanceTable(map))
        {
            if (metric.Distances.Any(d => d is null))
            {
                string partial = string.Join("，", metric.Distances.Select((d, z) => $"{BirthZoneLabel.Of(z)} = {(d is { } v ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "不可达")}"));
                string none = metric.Targets.IsDefaultOrEmpty ? $"（本图没有{metric.Name}）" : string.Empty;
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_DISTANCE_REPORT",
                    $"各出生区到{metric.Name}的最短落子距离：{partial}{none}；有出生区不可达，不计极差（只报告，不因不可达拒绝）。",
                    ImmutableArray<Coord>.Empty) { Severity = MapFindingSeverity.Report });
                continue;
            }

            int[] ds = [.. metric.Distances.Select(d => d!.Value)];
            string detail = string.Join("，", ds.Select((d, z) => $"{BirthZoneLabel.Of(z)} = {d}"));
            f.Add(new MapValidationFailure(
                "BIRTH_ZONE_DISTANCE_REPORT",
                $"各出生区到{metric.Name}的最短落子距离：{detail}；极差 {ds.Max() - ds.Min()}（只报告，不因极差拒绝）。",
                ImmutableArray<Coord>.Empty) { Severity = MapFindingSeverity.Report });
        }
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
