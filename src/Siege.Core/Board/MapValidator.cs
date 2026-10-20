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
/// 地图静态校验。把必死口袋、出生区容量不足、棋盘错位这类问题挡在加载期——
/// 漏到对局中会表现为"某玩家莫名其妙被迫 Pass"，极难归因。
/// 连通区一律沿气边（<see cref="Adjacency.LibertyNeighbors"/>）：崖壁、栅栏、未架桥深水挡住的路不算路。
/// "障碍占外接区域 25%–35%"在 terrain-model 取消：深水与崖壁同样在压缩空间，只数障碍已无意义，密度由可落子格区间把控。
/// </summary>
/// <remarks>规格：openspec/changes/terrain-model/specs/map-definition —— Requirement: 地图静态校验规则</remarks>
public static class MapValidator
{
    /// <summary>
    /// 规格档声明表（frontier-map D2）：规格档的人数预算与各条分流规则的处理方式，<b>全部</b>写在这一张表里。
    /// 校验器里对规格档的分支只允许出现在这里——各条规则只读 <see cref="ProfileRules"/> 的字段，不得再写"如果是某档"的散落分支
    /// （守门 <c>规格档分流守门Tests</c>：本文件里规格档枚举的字面量只在本表内、地图的规格档属性只在 <see cref="RulesOf"/> 读一次）。
    /// <para>
    /// retire-legacy-maps 4.2（裁决 L1 / L7）：标准档、边疆档、棋盘档随旧地图删除，表里只剩链接档一行；随之删除的分流规则：
    /// 距离均衡（拒绝 / 只报告两种处理）、出生区须到达中央入口、咽喉必须标注、全部棋盘须经通道连成一片、通道宽 / 长、旧档的列宽区间与"清单必须为空"。
    /// </para>
    /// 各地图共用、不在表里的规则：结构、地形边界、可落子格与信物格预算、出生区（保护期容量、出生区内无新地表）、信物格合法、
    /// 豁免理由、必死口袋，以及链接档的布局校验（<see cref="ValidateSpliced"/>）。中央入口 / 咽喉须可落子与容差两条随 retire-legacy-rules（L18）删除。
    /// 旋转对称不在校验器里（原 MapSymmetry 只由标准档基准图的测试调用，已随旧地图删除——retire-legacy-maps）。
    /// </summary>
    private static readonly ImmutableDictionary<MapProfile, ProfileRules> Rules =
        new Dictionary<MapProfile, ProfileRules>
        {
            // 链接档（board-link-mode D2 / 裁决 J1–J7；link-map-terrain D6 / 裁决 M1 / M4–M9；open-map D1 / D2）：可落子格 = 5 块出生棋盘内可落子格之和、信物 10–15（每块出生棋盘 2 个出生区档 + 至多 1 个公共区档）、
            // 出生区恰 5 个、各恰 9 格（核心区）；出生棋盘边长 6–8、带两两不同的主题、外圈平坦、核心区平坦、棋盘内连通、改造目标 ≥ 2；
            // 5 块建局时边对边紧贴成一张连通地图（3+2 两行）、没有通道；连通分量恰 1 个且含全部出生棋盘；每条接缝至少一条气边；
            // 不要求到中央入口可达、不做距离均衡、不要求标注咽喉。
            // 可落子格总数与画布区间（open-map tasks 1.1 回写）：种子 1–20 实测全图可落子 212–280、画布宽 23–27、高 17–20（种子 1–2000：可落子 184–300、宽 22–28、高 16–20）——
            // 可落子格取生成器的理论边界向外取整（5 块 6×6 减最长河道与混合主题的深水 = 173，5 块 8×8 = 320），画布取 3+2 布局的理论边界
            // （宽 = 第一行三块 18–24 + 第二行向东伸出至多 2 + 留白 4 → 22–30；高 = 两行 12–16 + 留白 4 → 16–20）。
            // （旧值：可落子 125–245、列 20–75、行 8–21；拼接版：列 20–85、行 8–24。）
            [MapProfile.Link] = new(
                "链接档只提供 4 人的预算表（本模式只有 4 人局）",
                new Dictionary<int, Budget>
                {
                    [4] = new(170, 320, 10, 15, (5, 5), (9, 9)),
                }.ToImmutableDictionary(),
                ZonesMustExceedPlayers: true,
                ColumnRange: (22, 30),
                RowRange: (16, 20),
                PlateList: new((6, 8), (6, 8), BirthRelics: 2, PublicRelicsMax: 1, MinEditTargets: 2, MaxSpecialSurfaces: 4)),
        }.ToImmutableDictionary();

    /// <summary>
    /// 一个规格档的全部分流声明：支持的人数说明、人数预算、出生区数是否须多于人数、外接列数与行数区间、棋盘清单规则。
    /// retire-legacy-maps 4.2：距离处理方式、连通性模式、咽喉标注、出生区到中央入口四个字段随旧档删除。
    /// </summary>
    private sealed record ProfileRules(
        string SupportedPlayers,
        ImmutableDictionary<int, Budget> Budgets,
        bool ZonesMustExceedPlayers,
        (int Min, int Max) ColumnRange,
        (int Min, int Max) RowRange,
        PlateRules PlateList);

    /// <summary>
    /// 棋盘清单规则：棋盘边长区间、出生棋盘边长区间、每块出生棋盘的出生区档信物格数与公共区档信物格数上限、
    /// 每块出生棋盘的可改造目标（林地格 + 未架桥深水格）下限。
    /// retire-legacy-maps 4.2：通道的宽度与长度区间随棋盘档删除（链接档没有通道）。link-map-terrain D6：加公共区档上限与改造目标下限。
    /// link-map-surfaces D4：加每块出生棋盘新地表（荒漠 / 沼泽 / 岩台 / 浅滩）格数上限。
    /// </summary>
    private sealed record PlateRules(
        (int Min, int Max) Side,
        (int Min, int Max) BirthSide,
        int BirthRelics,
        int PublicRelicsMax,
        int MinEditTargets,
        int MaxSpecialSurfaces);

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
                "该规格档没有校验声明表：只有链接档可以加载（标准档、边疆档、棋盘档已随 retire-legacy-maps 移除）。",
                ImmutableArray<Coord>.Empty));
            return new MapValidationResult(f.ToImmutable());
        }

        // 外接尺寸不合该档的区间、或棋盘清单与该档不符：规模已经不对，到此为止——后面的规则只会报出一堆派生失败。
        if (!ValidateExtent(map, rules.ColumnRange, rules.RowRange, f) || !ValidatePlateList(map, rules, f))
        {
            return new MapValidationResult(f.ToImmutable());
        }

        Budget? budget = rules.Budgets.TryGetValue(map.MaxPlayers, out Budget found) ? found : null;

        ValidateTerrainBounds(map, f);
        ValidatePlayableCount(map, budget, f);
        ValidateBudgets(map, rules, budget, f);
        ValidateBirthZones(map, budget, f);
        ValidateRelicCells(map, f);
        ValidatePocketExemptions(map, f);
        ValidatePockets(map, f);

        return new MapValidationResult(f.ToImmutable());
    }

    /// <summary>
    /// 外接宽度与行数必须落在该档声明的区间内。不通过返回 <c>false</c>。
    /// </summary>
    private static bool ValidateExtent(
        MapData map, (int Min, int Max) columns, (int Min, int Max) range, ImmutableArray<MapValidationFailure>.Builder f)
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
    /// 棋盘清单（board-map「棋盘清单」、link-map-generation「物化状态校验」、map-definition「地图静态校验规则」的清单规则）：清单不得为空，逐块校验棋盘，再做拼接后校验。
    /// 返回 <c>false</c> 表示清单与该档根本不符，后面的规则不必再跑。
    /// retire-legacy-maps 4.2："清单必须为空"（标准档 / 边疆档）与通道、全图连通两条棋盘档规则随旧档删除。
    /// link-map-terrain D6：删除"棋盘内全部是高度 0 的可落子草地"与"棋盘内不得有栅栏"，改为外圈 / 核心区 / 新地表 / 连通 / 改造目标 / 主题六条逐块规则与接缝气边校验。
    /// </summary>
    private static bool ValidatePlateList(MapData map, ProfileRules rules, ImmutableArray<MapValidationFailure>.Builder f)
    {
        PlateRules plates = rules.PlateList;
        if (map.Boards.IsDefaultOrEmpty)
        {
            f.Add(new MapValidationFailure(
                "BOARDS_REQUIRED", "链接档地图的棋盘清单不得为空。", ImmutableArray<Coord>.Empty));
            return false;
        }

        // 链接档（open-map D2）：5 块出生棋盘边对边紧贴成一张连通地图；没有通道，可落子格都必须属于某块棋盘；连通分量恰 1 个且含全部出生棋盘。
        Battlefields fields = Battlefields.Of(map);
        int[,] owner = ValidatePlateCells(map, plates, fields, f);
        ValidateThemes(map, f);
        ValidateBirthPlates(map, f);
        ValidateFlatScenery(map, owner, f);
        ValidateSpliced(map, plates, owner, fields, f);
        ValidateSeams(map, f);
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
    /// 第 1、2 条：棋盘在盘内、边长在区间内；出生棋盘再逐块校验外圈、核心区、新地表、连通与改造目标（<see cref="ValidateBirthBoardTerrain"/>）。
    /// 返回"格 → 所属棋盘下标"的表（不属于任何棋盘为 −1；重叠时取清单里靠前的一块，重叠本身由间隔规则报出）。
    /// </summary>
    private static int[,] ValidatePlateCells(MapData map, PlateRules plates, Battlefields fields, ImmutableArray<MapValidationFailure>.Builder f)
    {
        var owner = new int[Math.Max(0, map.Width), Math.Max(0, map.Height)];
        foreach (Coord c in map.AllCoords())
        {
            owner[c.X, c.Y] = -1;
        }

        for (int i = 0; i < map.Boards.Length; i++)
        {
            BoardPlate plate = map.Boards[i];
            (int Min, int Max) side = plate.Kind == BoardPlateKind.Birth ? plates.BirthSide : plates.Side;
            if (plate.Width < side.Min || plate.Width > side.Max || plate.Height < side.Min || plate.Height > side.Max)
            {
                string birth = plate.Kind == BoardPlateKind.Birth ? $"，出生棋盘 {plates.BirthSide.Min}–{plates.BirthSide.Max}" : string.Empty;
                f.Add(new MapValidationFailure(
                    "BOARD_SIZE_OUT_OF_RANGE",
                    $"{Describe(plate)} 的宽与高必须各在合法边长 {plates.Side.Min}–{plates.Side.Max} 之内{birth}。",
                    [plate.Origin]));
            }

            if (plate.Width <= 0 || plate.Height <= 0 || plate.Origin.X < 0 || plate.Origin.Y < 0
                || plate.Origin.X + plate.Width > map.Width || plate.Origin.Y + plate.Height > map.Height)
            {
                f.Add(new MapValidationFailure(
                    "BOARD_OUT_OF_BOUNDS", $"{Describe(plate)} 超出了地图的外接范围 {map.Width}×{map.Height}。", [plate.Origin]));
                continue;
            }

            foreach (Coord c in plate.Cells())
            {
                if (owner[c.X, c.Y] < 0)
                {
                    owner[c.X, c.Y] = i;
                }
            }

            if (plate.Kind == BoardPlateKind.Birth)
            {
                ValidateBirthBoardTerrain(map, plate, plates, fields, f);
            }
        }

        return owner;
    }

    /// <summary>
    /// 一块出生棋盘的清单规则（map-definition「地图静态校验规则」链接档清单、link-map-generation「初始布局」，link-map-terrain 裁决 M1 / M4–M7）：
    /// 外圈每格高度 0、可落子、草地或林地，外圈相关的边上没有栅栏（<c>BOARD_RIM_NOT_FLAT</c>）；
    /// 核心区 9 格高度 0 的草地、没有障碍 / 深水 / 桥，两端都在核心区内的边上没有栅栏（<c>BIRTH_CORE_NOT_FLAT</c>）；
    /// 荒漠 / 沼泽 / 岩台 / 浅滩只在中间带、每块至多 <see cref="PlateRules.MaxSpecialSurfaces"/> 格（<c>BOARD_SPECIAL_SURFACE</c>，link-map-surfaces D4）；棋盘内可落子格沿几何四邻连通（<c>BOARD_SPLIT</c>）；
    /// 林地格与未架桥深水格合计不少于下限（<c>BOARD_TOO_FEW_EDIT_TARGETS</c>）。
    /// </summary>
    /// <remarks>
    /// 连通性读战场导出（<see cref="Battlefields"/>，几何四邻的唯一分量实现），不另写一份 BFS：棋盘内的可落子格落在不止一个分量里即被切断。
    /// 外圈全部可落子（本方法的外圈规则）时，棋盘内被隔开的一片碰不到外圈、也就碰不到别的棋盘，"跨棋盘绕回来连上"不会发生。
    /// </remarks>
    private static void ValidateBirthBoardTerrain(MapData map, BoardPlate plate, PlateRules plates, Battlefields fields, ImmutableArray<MapValidationFailure>.Builder f)
    {
        // 外圈。
        ImmutableArray<Coord> rimCells = [.. plate.Cells().Where(plate.OnRim)
            .Where(c => !map.IsPlayable(c) || map.HeightAt(c) != 0 || map.SurfaceAt(c) is not (Surface.Grass or Surface.Forest))];
        FenceEdge[] rimFences = [.. map.TerrainData.Fences.Where(e => plate.OnRim(e.A) || plate.OnRim(e.B)).OrderBy(e => e.A).ThenBy(e => e.B)];
        if (!rimCells.IsEmpty || rimFences.Length > 0)
        {
            string edges = rimFences.Length > 0 ? $"；外圈相关的栅栏 {string.Join("、", rimFences)}" : string.Empty;
            f.Add(new MapValidationFailure(
                "BOARD_RIM_NOT_FLAT",
                $"{Describe(plate)} 的外圈每格必须高度 0、可落子、地表为草地或林地，外圈相关的边上不得有栅栏{edges}。",
                [.. rimCells.Concat(rimFences.SelectMany(e => new[] { e.A, e.B })).Distinct().Order()]));
        }

        // 核心区。
        ImmutableArray<Coord> coreCells = [.. plate.CoreCells()
            .Where(c => !map.IsPlayable(c) || map.HeightAt(c) != 0 || map.SurfaceAt(c) != Surface.Grass || map.HasBridge(c))];
        FenceEdge[] coreFences = [.. map.TerrainData.Fences.Where(e => plate.InCore(e.A) && plate.InCore(e.B)).OrderBy(e => e.A).ThenBy(e => e.B)];
        if (!coreCells.IsEmpty || coreFences.Length > 0)
        {
            string edges = coreFences.Length > 0 ? $"；核心区内部的栅栏 {string.Join("、", coreFences)}" : string.Empty;
            f.Add(new MapValidationFailure(
                "BIRTH_CORE_NOT_FLAT",
                $"{Describe(plate)} 的核心区 9 格必须都是高度 0 的草地，没有障碍、深水与桥，两端都在核心区内的边上不得有栅栏{edges}。",
                [.. coreCells.Concat(coreFences.SelectMany(e => new[] { e.A, e.B })).Distinct().Order()]));
        }

        // 新地表（link-map-surfaces D4 / 裁决 E5；原"棋盘内不得有新地表"随之放开）：荒漠、沼泽、岩台与浅滩只许在中间带（不在外圈、不在核心区），每块至多 MaxSpecialSurfaces 格。
        // 外圈"只许草地或林地"与核心区"9 格高度 0 草地"两条照旧报出；这里另给出更精确的失败信息。"核心区四邻不放"是生成器口径（S1），不是地图合法性要求，这里不查。
        ImmutableArray<Coord> special = [.. plate.Cells().Where(c => map.SurfaceAt(c) is Surface.Desert or Surface.Marsh or Surface.Crag or Surface.Shallows)];
        ImmutableArray<Coord> misplaced = [.. special.Where(c => !plate.InMiddleBand(c))];
        if (!misplaced.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "BOARD_SPECIAL_SURFACE",
                $"{Describe(plate)} 的荒漠、沼泽、岩台与浅滩只许在中间带：{string.Join("、", misplaced.Select(c => $"{c.ToNotation()}（{(plate.InCore(c) ? "核心区" : "外圈")}）是{map.SurfaceAt(c).DisplayName()}"))}。",
                misplaced));
        }

        if (special.Length > plates.MaxSpecialSurfaces)
        {
            f.Add(new MapValidationFailure(
                "BOARD_SPECIAL_SURFACE",
                $"{Describe(plate)} 有 {special.Length} 格荒漠、沼泽、岩台与浅滩：每块出生棋盘至多 {plates.MaxSpecialSurfaces} 格新地表。",
                special));
        }

        // 连通：棋盘内的可落子格只能落在一个战场分量里。保留格数最多的一片（并列取先出现的），其余各片即"被隔开的格"。
        List<List<Coord>> parts = [];
        var partOf = new Dictionary<Coord, List<Coord>>();
        foreach (Coord c in plate.Cells().Where(map.IsPlayable))
        {
            Coord key = fields.At(c)!.Cells[0];
            if (!partOf.TryGetValue(key, out List<Coord>? part))
            {
                part = [];
                partOf[key] = part;
                parts.Add(part);
            }

            part.Add(c);
        }

        if (parts.Count > 1)
        {
            List<Coord> main = parts.MaxBy(p => p.Count)!;
            ImmutableArray<Coord> cut = [.. parts.Where(p => !ReferenceEquals(p, main)).SelectMany(p => p).Order()];
            f.Add(new MapValidationFailure(
                "BOARD_SPLIT",
                $"{Describe(plate)} 内的可落子格沿几何四邻不连通：被岩石或深水隔开了 {cut.Length} 格。",
                cut));
        }

        // 可改造目标：林地格 + 未架桥深水格。
        int targets = plate.Cells().Count(c => map.SurfaceAt(c) == Surface.Forest || map.TerrainData.IsUnbridgedDeepWater(c));
        if (targets < plates.MinEditTargets)
        {
            f.Add(new MapValidationFailure(
                "BOARD_TOO_FEW_EDIT_TARGETS",
                $"{Describe(plate)} 内林地格与未架桥深水格合计 {targets} 个：每块出生棋盘必须至少 {plates.MinEditTargets} 个可改造目标。",
                [plate.Origin]));
        }
    }

    /// <summary>
    /// 主题（link-map-terrain D2 / 裁决 M2，<c>BOARD_THEME_INVALID</c>）：出生棋盘必须带一个取值合法的主题，非出生棋盘不得带主题；出生棋盘的主题两两不同。
    /// 主题只用于生成、校验与呈现，这里只核对取值，不按主题校验地形内容（各主题的地形特征由生成器构造与其单元测试守住）。
    /// </summary>
    private static void ValidateThemes(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        foreach (BoardPlate plate in map.Boards)
        {
            if (plate.Kind == BoardPlateKind.Birth && plate.Theme is not { } theme)
            {
                f.Add(new MapValidationFailure("BOARD_THEME_INVALID", $"{Describe(plate)} 没有主题：链接档的出生棋盘必须带主题。", [plate.Origin]));
            }
            else if (plate.Kind == BoardPlateKind.Birth && !Enum.IsDefined(plate.Theme!.Value))
            {
                f.Add(new MapValidationFailure("BOARD_THEME_INVALID", $"{Describe(plate)} 的主题取值 {(int)plate.Theme!.Value} 无法识别。", [plate.Origin]));
            }
            else if (plate.Kind != BoardPlateKind.Birth && plate.Theme is not null)
            {
                f.Add(new MapValidationFailure("BOARD_THEME_INVALID", $"{Describe(plate)} 不是出生棋盘，不得带主题。", [plate.Origin]));
            }
        }

        foreach (IGrouping<BoardTheme, BoardPlate> same in map.Boards
            .Where(p => p.Kind == BoardPlateKind.Birth && p.Theme is { } t && Enum.IsDefined(t))
            .GroupBy(p => p.Theme!.Value)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key))
        {
            f.Add(new MapValidationFailure(
                "BOARD_THEME_INVALID",
                $"{string.Join("、", same.Select(Describe))} 的主题都是{same.Key.DisplayName()}：同一张图的出生棋盘主题必须两两不同。",
                [.. same.Select(p => p.Origin)]));
        }
    }

    /// <summary>「棋盘清单」：出生棋盘与出生区一一对应，第 i 块出生棋盘的核心区（<see cref="BoardPlate.CoreCells"/>）恰等于出生区 i（link-map-terrain D1 / 裁决 M1）。</summary>
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
            if (!map.BirthZones[i].SetEquals(births[i].CoreCells()))
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_BOARD_MISMATCH",
                    $"第 {i + 1} 块{Describe(births[i])} 的核心区与{BirthZoneLabel.Of(i)} 不相等：出生区必须恰是该棋盘中央的 {BoardPlate.CoreSide}×{BoardPlate.CoreSide} 核心区。",
                    [births[i].Origin]));
            }
        }
    }

    /// <summary>深水只许在棋盘内（棋盘之外是不可落子、不可改造的场景格，深水是搭桥的目标）；信物格必须在棋盘内（通道格不得是信物格）。</summary>
    private static void ValidateFlatScenery(MapData map, int[,] owner, ImmutableArray<MapValidationFailure>.Builder f)
    {
        ImmutableArray<Coord> water = [.. map.AllCoords().Where(c => map.SurfaceAt(c) == Surface.DeepWater && owner[c.X, c.Y] < 0)];
        if (!water.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "BOARD_MAP_DEEP_WATER",
                "棋盘之外不得有深水格（含架了桥的）：棋盘之外的格子必须全部是不可改造的场景格。",
                water));
        }

        ImmutableArray<Coord> strayRelics = map.RelicCells.Keys
            .Where(c => map.IsPlayable(c) && owner[c.X, c.Y] < 0)
            .Order()
            .ToImmutableArray();
        if (!strayRelics.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "RELIC_OUTSIDE_BOARD", "信物格必须位于棋盘内，通道格不得是信物格。", strayRelics));
        }
    }

    /// <summary>
    /// 链接档的布局校验（规格 link-map-generation「物化状态校验」）：
    /// 可落子格都属于某块棋盘（没有通道）；棋盘两两不重叠；每块出生棋盘恰 <see cref="PlateRules.BirthRelics"/> 个出生区档信物格、至多 <see cref="PlateRules.PublicRelicsMax"/> 个公共区档信物格；
    /// 按几何四邻导出的连通分量（<see cref="Battlefields"/>）恰 1 个且含全部出生棋盘（open-map D2：建局时即紧贴成一张连通地图，对局中不变）。
    /// </summary>
    private static void ValidateSpliced(MapData map, PlateRules plates, int[,] owner, Battlefields fields, ImmutableArray<MapValidationFailure>.Builder f)
    {
        ImmutableArray<Coord> loose = [.. map.AllCoords().Where(c => map.IsPlayable(c) && owner[c.X, c.Y] < 0)];
        if (!loose.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "SCENERY_CELL_PLAYABLE",
                "可落子格都必须属于某块出生棋盘：没有通道，棋盘之外必须全部是障碍格。",
                loose));
        }

        for (int i = 0; i < map.Boards.Length; i++)
        {
            BoardPlate plate = map.Boards[i];
            bool inside = plate.Width > 0 && plate.Height > 0 && plate.Origin.X >= 0 && plate.Origin.Y >= 0
                && plate.Origin.X + plate.Width <= map.Width && plate.Origin.Y + plate.Height <= map.Height;
            if (!inside)
            {
                continue;
            }

            ImmutableArray<Coord> overlapped = [.. plate.Cells().Where(c => owner[c.X, c.Y] != i)];
            if (!overlapped.IsEmpty)
            {
                f.Add(new MapValidationFailure(
                    "BOARD_OVERLAP", $"{Describe(plate)} 与清单里靠前的棋盘重叠：棋盘只能边对边紧贴，不得重叠。", overlapped));
            }

            if (plate.Kind == BoardPlateKind.Birth)
            {
                int birth = map.RelicCells.Count(kv => plate.Contains(kv.Key) && kv.Value.Zone == RelicZone.BirthZone);
                int contested = map.RelicCells.Count(kv => plate.Contains(kv.Key) && kv.Value.Zone != RelicZone.BirthZone);
                if (birth != plates.BirthRelics || contested > plates.PublicRelicsMax)
                {
                    f.Add(new MapValidationFailure(
                        "BIRTH_BOARD_RELIC_COUNT",
                        $"{Describe(plate)} 有 {birth} 个出生区档、{contested} 个公共区档信物格：每块出生棋盘必须恰有 {plates.BirthRelics} 个出生区档（在核心区内）、至多 {plates.PublicRelicsMax} 个公共区档（在核心区外）。",
                        [plate.Origin]));
                }
            }
        }

        foreach (Battlefield field in fields.All)
        {
            if (!field.HasBirthBoard)
            {
                f.Add(new MapValidationFailure(
                    "BATTLEFIELD_WITHOUT_BIRTH_BOARD",
                    $"有一片 {field.Cells.Length} 格的可落子格不含任何出生棋盘：每个战场都必须由出生棋盘拼出来。",
                    field.Cells));
            }
        }

        // open-map D2：恰 1 个连通分量且含全部出生棋盘（5 块建局时即紧贴成一张连通地图；地图在对局中不变）。
        int births = map.Boards.Count(b => b.Kind == BoardPlateKind.Birth);
        Battlefield[] withBirth = [.. fields.All.Where(field => field.HasBirthBoard)];
        int covered = withBirth.Length == 1 ? withBirth[0].BirthZones.Length : 0;
        if (withBirth.Length != 1 || covered != births)
        {
            f.Add(new MapValidationFailure(
                "BATTLEFIELD_COUNT_MISMATCH",
                $"按几何四邻导出的连通分量为 {withBirth.Length} 个（含出生棋盘 {string.Join(" / ", withBirth.Select(field => field.BirthZones.Length))}）："
                + $"链接档地图必须恰有 1 个连通分量且含全部 {births} 块出生棋盘（出生棋盘建局时边对边紧贴成一张连通地图）。",
                [.. withBirth.SelectMany(field => field.Cells)]));
        }
    }

    /// <summary>
    /// 接缝气边（link-map-generation「物化状态校验」，link-map-terrain 裁决 M6，<c>LINK_SEAM_NO_LIBERTY</c>）：两块棋盘的外接矩形边对边相触即有一条接缝；
    /// 隔着接缝互为几何邻居的格对里，至少要有一对之间存在气边（<see cref="Adjacency.LibertyNeighbors"/>，唯一的气边实现）。
    /// 合规生成图上恒成立（外圈高度 0、无栅栏）——这条是给手写地图与以后改外圈规则时用的底线。
    /// </summary>
    private static void ValidateSeams(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        for (int i = 0; i < map.Boards.Length; i++)
        {
            for (int j = i + 1; j < map.Boards.Length; j++)
            {
                BoardPlate a = map.Boards[i];
                BoardPlate b = map.Boards[j];
                var pairs = new List<(Coord P, Coord Q)>();
                if (a.Origin.X + a.Width == b.Origin.X || b.Origin.X + b.Width == a.Origin.X)
                {
                    (BoardPlate west, BoardPlate east) = a.Origin.X < b.Origin.X ? (a, b) : (b, a);
                    for (int y = Math.Max(a.Origin.Y, b.Origin.Y); y < Math.Min(a.Origin.Y + a.Height, b.Origin.Y + b.Height); y++)
                    {
                        pairs.Add((new Coord(west.Origin.X + west.Width - 1, y), new Coord(east.Origin.X, y)));
                    }
                }
                else if (a.Origin.Y + a.Height == b.Origin.Y || b.Origin.Y + b.Height == a.Origin.Y)
                {
                    (BoardPlate south, BoardPlate north) = a.Origin.Y < b.Origin.Y ? (a, b) : (b, a);
                    for (int x = Math.Max(a.Origin.X, b.Origin.X); x < Math.Min(a.Origin.X + a.Width, b.Origin.X + b.Width); x++)
                    {
                        pairs.Add((new Coord(x, south.Origin.Y + south.Height - 1), new Coord(x, north.Origin.Y)));
                    }
                }

                pairs.RemoveAll(p => !map.Contains(p.P) || !map.Contains(p.Q));
                if (pairs.Count == 0 || pairs.Any(p => Adjacency.LibertyNeighbors(map, p.P).Contains(p.Q)))
                {
                    continue;
                }

                f.Add(new MapValidationFailure(
                    "LINK_SEAM_NO_LIBERTY",
                    $"{Describe(a)} 与 {Describe(b)} 之间的接缝（{pairs.Count} 对相邻格）没有任何气边：接缝两侧全部隔着栅栏、崖壁或不可落子格。",
                    [.. pairs.SelectMany(p => new[] { p.P, p.Q }).Order()]));
            }
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

        // 规则第 4 条：出生区数在区间外即报区间与方向（链接档恰 5 个）。
        // retire-legacy-maps 4b.2（L19）：区间退化成"恰等于人数"时的 BIRTH_ZONE_COUNT_MISMATCH 报文只属于已删除的标准档，链接档（5 区对 4 人）走不到，分支删除。
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

            // 出生区内可以有障碍或深水，但不得把该区压到区间之外（denser-map：4 人图每区 12–14 格）。
            if (budget?.BirthZoneCells is { } range
                && (playable < range.Min || playable > range.Max))
            {
                f.Add(new MapValidationFailure(
                    "BIRTH_ZONE_SIZE_OUT_OF_RANGE",
                    $"{BirthZoneLabel.Of(i)} 有 {playable} 个可落子格，超出 {map.MaxPlayers} 人地图的 {range.Min}–{range.Max} 区间。",
                    ImmutableArray<Coord>.Empty));
            }

            // 第 6 条（map-definition「地图静态校验规则」；terrain-surfaces 引入）：出生区是起手阵地，新地表只出现在公共区域，避免起手条件因地表而不对等。
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

    // retire-legacy-rules L18（裁决 R3）：原 ValidateLandmarks（中央入口 / 咽喉须可落子：CENTRAL_ENTRANCE_NOT_PLAYABLE、CHOKE_NOT_PLAYABLE）与
    // ValidateTolerance 的容差两条（TOLERANCE_NEGATIVE、TOLERANCE_RELAX_WITHOUT_REASON）只为已删除的距离均衡服务，随之删除；
    // MapData 的中央入口、咽喉、距离容差与放宽理由字段保留（地图文件照常读写）。原 ValidateTolerance 里的豁免理由一条移到下面，位置与次序不变。

    /// <summary>每条必死口袋豁免都必须写明理由。</summary>
    private static void ValidatePocketExemptions(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
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
    /// 各出生区到每个距离均衡目标的最短落子距离（沿气边），规则第 1 条的唯一口径。
    /// 目标依次为：最近公共信物、中央入口、最近咽喉，共三项（restore-go-core-rules 起由五项改三项）。
    /// 距离为 <c>null</c> 表示该出生区到不了任一目标格；目标集合为空时整项距离全为 <c>null</c>。
    /// retire-legacy-maps 4.2：距离均衡校验随旧档删除，本表只剩 <c>Siege.Sim map</c> 的距离打印一个使用者（L10：保留）。
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
