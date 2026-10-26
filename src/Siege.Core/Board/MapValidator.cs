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
    /// <para>retire-legacy-maps 4.2（裁决 L1 / L7）：标准档、边疆档、棋盘档随旧地图删除，表里只剩链接档一行。</para>
    /// <para>
    /// map-expand-25 D7（多平台浮空岛）：链接档按人数 2 / 3 / 4 各一行预算——可落子格区间、信物区间（每平台 2 个出生区档 + 低地至多 n+2 个公共区档）、
    /// 出生区数 = 平台数区间（裁决 R2）、出生区恰 9 格（核心区）、公共信物上限 n+2、岛边长 W(n) 与画布边长 W(n)+2M（恰等；原表外的列 / 行区间并入每行）。
    /// W(n) = 30 / 35 / 40（段 A1 实施修订：design D2 原定 26 / 31 / 36 在"中央 8×8 + 其余 ≥ 6 + 距岛边 2 + 间隔 3"下放不够平台，见 <c>LinkMapGenerator.IslandBaseSide</c>）。
    /// 可落子格区间取种子 1–20 实测（段 A1）向外取整，回写记录见任务 10-26-map-expand-25。
    /// 平台清单规则：边长 6–8、每块恰 2 个出生区档信物、改造目标 ≥ 2、新地表 ≤ 4、两两间隔 ≥ 3、坡道 2–3 个。
    /// </para>
    /// 各地图共用、不在表里的规则：结构、地形边界、可落子格与信物格预算、出生区（保护期容量、出生区内无新地表）、信物格合法、豁免理由、必死口袋。
    /// 旋转对称不在校验器里（原 MapSymmetry 只由标准档基准图的测试调用，已随旧地图删除——retire-legacy-maps）。
    /// </summary>
    private static readonly ImmutableDictionary<MapProfile, ProfileRules> Rules =
        new Dictionary<MapProfile, ProfileRules>
        {
            // 链接档（map-expand-25 D7）：按人数的浮空岛；没有咽喉、不做距离均衡、不要求到中央入口可达。
            [MapProfile.Link] = new(
                "链接档只提供 2–4 人的预算表",
                new Dictionary<int, Budget>
                {
                    [2] = new(760, 876, 12, 20, (6, 8), (9, 9), PublicRelicsMax: 4, IslandSide: 30, Canvas: 34),
                    [3] = new(1050, 1201, 16, 25, (8, 10), (9, 9), PublicRelicsMax: 5, IslandSide: 35, Canvas: 39),
                    [4] = new(1400, 1576, 20, 30, (10, 12), (9, 9), PublicRelicsMax: 6, IslandSide: 40, Canvas: 44),
                }.ToImmutableDictionary(),
                ZonesMustExceedPlayers: true,
                PlateList: new((6, 8), (6, 8), BirthRelics: 2, MinEditTargets: 2, MaxSpecialSurfaces: 4, MinGap: 3, Ramps: (2, 3))),
        }.ToImmutableDictionary();

    /// <summary>
    /// 一个规格档的全部分流声明：支持的人数说明、人数预算、出生区数是否须多于人数、棋盘清单规则。
    /// retire-legacy-maps 4.2：距离处理方式、连通性模式、咽喉标注、出生区到中央入口四个字段随旧档删除。
    /// map-expand-25 D7：外接列数与行数区间并入按人数的预算行（画布边长恰等于 W(n)+2M）。
    /// </summary>
    private sealed record ProfileRules(
        string SupportedPlayers,
        ImmutableDictionary<int, Budget> Budgets,
        bool ZonesMustExceedPlayers,
        PlateRules PlateList);

    /// <summary>
    /// 棋盘清单规则：棋盘边长区间、出生棋盘（平台）边长区间、每块平台的出生区档信物格数、可改造目标（林地格 + 未架桥深水格）下限、
    /// 新地表（荒漠 / 沼泽 / 岩台 / 浅滩）格数上限、两块平台外接矩形的最小间隔、每块平台的坡道数区间。
    /// map-expand-25 D7：每块至多 1 个公共区档信物的上限删除（公共信物移到低地，上限按人数进预算行），加间隔与坡道。
    /// </summary>
    private sealed record PlateRules(
        (int Min, int Max) Side,
        (int Min, int Max) BirthSide,
        int BirthRelics,
        int MinEditTargets,
        int MaxSpecialSurfaces,
        int MinGap,
        (int Min, int Max) Ramps);

    /// <summary>一档人数的规模预算（map-expand-25 D7：另带公共信物上限、岛边长与画布边长）。</summary>
    private readonly record struct Budget(
        int MinPlayable,
        int MaxPlayable,
        int MinRelics,
        int MaxRelics,
        (int Min, int Max) BirthZones,
        (int Min, int Max)? BirthZoneCells,
        int PublicRelicsMax,
        int IslandSide,
        int Canvas);

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

        Budget? budget = rules.Budgets.TryGetValue(map.MaxPlayers, out Budget found) ? found : null;

        // 画布尺寸不合该人数的预算、或棋盘清单与该档不符：规模已经不对，到此为止——后面的规则只会报出一堆派生失败。
        // 人数不受支持时没有预算行，画布不查，由 ValidateBudgets 报 UNSUPPORTED_PLAYER_COUNT。
        if (!ValidateExtent(map, budget, f) || !ValidatePlateList(map, rules, budget, f))
        {
            return new MapValidationResult(f.ToImmutable());
        }

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
    /// 画布宽与高必须恰等于该人数预算行的画布边长 W(n)+2M（map-expand-25 D7）。没有预算行（人数不受支持）时不查。不通过返回 <c>false</c>。
    /// </summary>
    private static bool ValidateExtent(MapData map, Budget? known, ImmutableArray<MapValidationFailure>.Builder f)
    {
        if (known is not { } budget)
        {
            return true;
        }

        int canvas = budget.Canvas;
        (string Code, string Message)? wrong =
            map.Width > canvas ? ("MAP_TOO_WIDE", $"地图宽 {map.Width} 列，超出 {map.MaxPlayers} 人浮空岛的画布 {canvas} 列。")
            : map.Width < canvas ? ("MAP_TOO_NARROW", $"地图宽 {map.Width} 列，不足 {map.MaxPlayers} 人浮空岛的画布 {canvas} 列。")
            : map.Height > canvas ? ("MAP_TOO_TALL", $"地图高 {map.Height} 行，超出 {map.MaxPlayers} 人浮空岛的画布 {canvas} 行。")
            : map.Height < canvas ? ("MAP_TOO_SHORT", $"地图高 {map.Height} 行，不足 {map.MaxPlayers} 人浮空岛的画布 {canvas} 行。")
            : null;
        if (wrong is { } w)
        {
            f.Add(new MapValidationFailure(w.Code, w.Message, ImmutableArray<Coord>.Empty));
            return false;
        }

        return true;
    }

    /// <summary>
    /// 棋盘清单（board-map「棋盘清单」、map-definition「地图静态校验规则」的清单规则）：清单不得为空，逐块校验平台，再做整岛校验（<see cref="ValidateIsland"/>）。
    /// 返回 <c>false</c> 表示清单与该档根本不符，后面的规则不必再跑。
    /// map-expand-25 D7：删除外圈平坦、棋盘外不得有深水、紧贴布局（含"可落子格都属于棋盘"与战场分量恰 1 个）、接缝气边、主题两两不同、每块至多 1 个公共信物；
    /// 改为平台高度与坡道、主题不为高台、平台间隔、整岛气边连通、可落子区外接矩形、公共信物在低地且不超过 n+2。
    /// </summary>
    private static bool ValidatePlateList(MapData map, ProfileRules rules, Budget? budget, ImmutableArray<MapValidationFailure>.Builder f)
    {
        PlateRules plates = rules.PlateList;
        if (map.Boards.IsDefaultOrEmpty)
        {
            f.Add(new MapValidationFailure(
                "BOARDS_REQUIRED", "链接档地图的棋盘清单不得为空。", ImmutableArray<Coord>.Empty));
            return false;
        }

        Battlefields fields = Battlefields.Of(map);
        ValidatePlateCells(map, plates, fields, f);
        ValidateThemes(map, f);
        ValidateBirthPlates(map, f);
        ValidateIsland(map, plates, budget, f);
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
    /// 第 1、2 条：棋盘在盘内、边长在区间内；出生棋盘（平台）再逐块校验高度与坡道、隔断线、核心区、新地表、连通、改造目标与出生区信物（<see cref="ValidateBirthBoardTerrain"/>）。
    /// 重叠与间隔由 <see cref="ValidateIsland"/> 报出。
    /// </summary>
    private static void ValidatePlateCells(MapData map, PlateRules plates, Battlefields fields, ImmutableArray<MapValidationFailure>.Builder f)
    {
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

            if (plate.Kind == BoardPlateKind.Birth)
            {
                ValidateBirthBoardTerrain(map, plate, plates, fields, f);
            }
        }
    }

    /// <summary>
    /// 一块平台（出生棋盘）的清单规则（map-definition「地图静态校验规则」链接档清单；map-expand-25 D3 / D7）：
    /// 平台内可落子格除坡道外高度 2，坡道高度 1、数量在区间内、都在外圈非角格且朝外的邻格是高度 0 的可落子低地（<c>PLATE_HEIGHT</c>）；
    /// 河流隔断线恰 1 座桥（<c>BOARD_BARRIER_INVALID</c>）；核心区 9 格高度 2 的草地、没有障碍 / 深水 / 桥，两端都在核心区内的边上没有栅栏（<c>BIRTH_CORE_NOT_FLAT</c>）；
    /// 荒漠 / 沼泽 / 岩台 / 浅滩只在中间带、每块至多 <see cref="PlateRules.MaxSpecialSurfaces"/> 格（<c>BOARD_SPECIAL_SURFACE</c>）；平台内可落子格沿几何四邻连通（<c>BOARD_SPLIT</c>）；
    /// 林地格与未架桥深水格合计不少于下限（<c>BOARD_TOO_FEW_EDIT_TARGETS</c>）；恰 <see cref="PlateRules.BirthRelics"/> 个出生区档信物格（<c>BIRTH_BOARD_RELIC_COUNT</c>）。
    /// </summary>
    /// <remarks>连通性读战场导出（<see cref="Battlefields"/>，几何四邻的唯一分量实现），不另写一份 BFS：平台内的可落子格落在不止一个分量里即被切断。</remarks>
    private static void ValidateBirthBoardTerrain(MapData map, BoardPlate plate, PlateRules plates, Battlefields fields, ImmutableArray<MapValidationFailure>.Builder f)
    {
        // 高度与坡道（D3）：坡道 = 平台内高度 1 的可落子格。朝外的邻格用坐标算术取（外圈非角格恰有一个朝外方向），不枚举邻居。
        int left = plate.Origin.X, right = plate.Origin.X + plate.Width - 1, bottom = plate.Origin.Y, top = plate.Origin.Y + plate.Height - 1;
        Coord? Outlet(Coord c)
        {
            bool corner = (c.X == left || c.X == right) && (c.Y == bottom || c.Y == top);
            return corner || !plate.OnRim(c) ? null
                : c.X == left ? new Coord(c.X - 1, c.Y)
                : c.X == right ? new Coord(c.X + 1, c.Y)
                : c.Y == bottom ? new Coord(c.X, c.Y - 1)
                : new Coord(c.X, c.Y + 1);
        }

        bool Lowland(Coord o) => map.Contains(o) && map.IsPlayable(o) && map.HeightAt(o) == 0 && !map.Boards.Any(b => b.Contains(o));
        Coord[] playable = [.. plate.Cells().Where(map.IsPlayable)];
        Coord[] ramps = [.. playable.Where(c => map.HeightAt(c) == 1)];
        Coord[] flat = [.. playable.Where(c => map.HeightAt(c) is not (1 or 2))];
        Coord[] badRamps = [.. ramps.Where(c => Outlet(c) is not { } o || !Lowland(o) || map.SurfaceAt(c) != Surface.Grass)];
        if (flat.Length > 0)
        {
            f.Add(new MapValidationFailure(
                "PLATE_HEIGHT", $"{Describe(plate)} 内可落子格除坡道（高度 1）外必须是高度 2：{string.Join("、", flat.Select(c => $"{c.ToNotation()} 高 {map.HeightAt(c)}"))}。", [.. flat]));
        }

        if (ramps.Length < plates.Ramps.Min || ramps.Length > plates.Ramps.Max)
        {
            f.Add(new MapValidationFailure(
                "PLATE_HEIGHT", $"{Describe(plate)} 有 {ramps.Length} 个坡道（高度 1 的格）：每块平台必须有 {plates.Ramps.Min}–{plates.Ramps.Max} 个。", ramps.Length > 0 ? [.. ramps] : [plate.Origin]));
        }

        if (badRamps.Length > 0)
        {
            f.Add(new MapValidationFailure(
                "PLATE_HEIGHT", $"{Describe(plate)} 的坡道必须是外圈非角格上的草地，且朝外的邻格是高度 0 的可落子低地（不是深水、岩石、岛外或另一块平台）。", [.. badRamps]));
        }

        int birthRelics = map.RelicCells.Count(kv => plate.Contains(kv.Key) && kv.Value.Zone == RelicZone.BirthZone);
        if (birthRelics != plates.BirthRelics)
        {
            f.Add(new MapValidationFailure(
                "BIRTH_BOARD_RELIC_COUNT",
                $"{Describe(plate)} 有 {birthRelics} 个出生区档信物格：每块平台必须恰有 {plates.BirthRelics} 个（在核心区内）。",
                [plate.Origin]));
        }

        List<Barrier> barriers = BarriersOf(map, plate);

        // 隔断线（terrain-barriers D5，实施修订 J5）：平台内若有横穿平台的河道，它不得经过核心区、须恰 1 座桥，线上除桥外的格与线外的格之间没有气边。不要求一定有隔断线。
        // map-expand-25：高台主题去掉、平台整体抬到高度 2 后"每格高度 ≥ 1 的整行 / 整列"不再是山脊，只认河道。
        foreach (Barrier line in barriers)
        {
            string name = $"河道 {line.Cells[0].ToNotation()}–{line.Cells[^1].ToNotation()}";
            ImmutableArray<Coord> passages = [.. line.Cells.Where(map.HasBridge)];
            ImmutableArray<Coord> inCore = [.. line.Cells.Where(plate.InCore)];
            ImmutableArray<Coord> leaks = [.. line.Cells
                .Where(c => passages.Length != 1 || c != passages[0])
                .Where(c => Adjacency.LibertyNeighbors(map, c).Any(n => !line.Cells.Contains(n)))];
            if (!inCore.IsEmpty)
            {
                f.Add(new MapValidationFailure(
                    "BOARD_BARRIER_INVALID", $"{Describe(plate)} 的{name}横穿棋盘并经过核心区：隔断线不得经过核心区。", inCore));
            }

            if (passages.Length != 1)
            {
                f.Add(new MapValidationFailure(
                    "BOARD_BARRIER_INVALID",
                    $"{Describe(plate)} 的{name}横穿棋盘，有 {passages.Length} 座桥：隔断线须恰有 1 处通道。",
                    passages.IsEmpty ? line.Cells : passages));
            }
            else if (!leaks.IsEmpty)
            {
                f.Add(new MapValidationFailure(
                    "BOARD_BARRIER_INVALID", $"{Describe(plate)} 的{name}横穿棋盘，除通道外仍有格与两侧相连：两侧之间除通道外不得有气边。", leaks));
            }
        }

        // 核心区（map-expand-25 D7：随平台抬升改为高度 2）。
        ImmutableArray<Coord> coreCells = [.. plate.CoreCells()
            .Where(c => !map.IsPlayable(c) || map.HeightAt(c) != 2 || map.SurfaceAt(c) != Surface.Grass || map.HasBridge(c))];
        FenceEdge[] coreFences = [.. map.TerrainData.Fences.Where(e => plate.InCore(e.A) && plate.InCore(e.B)).OrderBy(e => e.A).ThenBy(e => e.B)];
        if (!coreCells.IsEmpty || coreFences.Length > 0)
        {
            string edges = coreFences.Length > 0 ? $"；核心区内部的栅栏 {string.Join("、", coreFences)}" : string.Empty;
            f.Add(new MapValidationFailure(
                "BIRTH_CORE_NOT_FLAT",
                $"{Describe(plate)} 的核心区 9 格必须都是高度 2 的草地，没有障碍、深水与桥，两端都在核心区内的边上不得有栅栏{edges}。",
                [.. coreCells.Concat(coreFences.SelectMany(e => new[] { e.A, e.B })).Distinct().Order()]));
        }

        // 新地表（link-map-surfaces D4 / 裁决 E5；原"棋盘内不得有新地表"随之放开）：荒漠、沼泽、岩台与浅滩只许在中间带（不在外圈、不在核心区），每块至多 MaxSpecialSurfaces 格。
        // 核心区"9 格高度 2 草地"照旧报出；这里另给出更精确的失败信息。"核心区四邻不放"是生成器口径（S1），不是地图合法性要求，这里不查。
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

    /// <summary>一条横穿平台的河道隔断线（terrain-barriers D5）：整列（<see cref="Column"/>）或整行、第几列 / 行，线上的格按先行后列。</summary>
    private sealed record Barrier(bool Column, int Index, ImmutableArray<Coord> Cells);

    /// <summary>
    /// 棋盘内的隔断线（terrain-barriers D1 / D5，实施修订 J1 / J2 / J5）：只看地图数据，不读生成器，也不看主题。候选是中间带的整列（先）与整行（后），即不是外圈那一列 / 行；
    /// 整列 / 整行每格都是深水（含桥）即河道（map-expand-25：山脊随高台主题去掉）。
    /// </summary>
    private static List<Barrier> BarriersOf(MapData map, BoardPlate plate)
    {
        var lines = new List<Barrier>();
        void Consider(bool column, int index, ImmutableArray<Coord> cells)
        {
            if (cells.All(c => map.SurfaceAt(c) == Surface.DeepWater))
            {
                lines.Add(new Barrier(column, index, cells));
            }
        }

        for (int x = plate.Origin.X + 1; x < plate.Origin.X + plate.Width - 1; x++)
        {
            int col = x;
            Consider(true, x, [.. Enumerable.Range(plate.Origin.Y, plate.Height).Select(y => new Coord(col, y))]);
        }

        for (int y = plate.Origin.Y + 1; y < plate.Origin.Y + plate.Height - 1; y++)
        {
            int row = y;
            Consider(false, y, [.. Enumerable.Range(plate.Origin.X, plate.Width).Select(x => new Coord(x, row))]);
        }

        return lines;
    }

    /// <summary>
    /// 主题（link-map-terrain D2；map-expand-25 D7 / 裁决 R3，<c>PLATE_THEME</c>）：出生棋盘（平台）必须带一个取值合法的主题、且不是高台，非出生棋盘不得带主题。
    /// 主题可以重复（原"两两不同"随 R3 删除）。主题只用于生成、校验与呈现，这里只核对取值，不按主题校验地形内容。
    /// </summary>
    private static void ValidateThemes(MapData map, ImmutableArray<MapValidationFailure>.Builder f)
    {
        foreach (BoardPlate plate in map.Boards)
        {
            if (plate.Kind == BoardPlateKind.Birth && plate.Theme is not { } theme)
            {
                f.Add(new MapValidationFailure("PLATE_THEME", $"{Describe(plate)} 没有主题：链接档的平台必须带主题。", [plate.Origin]));
            }
            else if (plate.Kind == BoardPlateKind.Birth && !Enum.IsDefined(plate.Theme!.Value))
            {
                f.Add(new MapValidationFailure("PLATE_THEME", $"{Describe(plate)} 的主题取值 {(int)plate.Theme!.Value} 无法识别。", [plate.Origin]));
            }
            else if (plate.Kind == BoardPlateKind.Birth && plate.Theme == BoardTheme.Plateau)
            {
                f.Add(new MapValidationFailure("PLATE_THEME", $"{Describe(plate)} 的主题是高台：平台主题只能是河流、林地、栅栏阵或混合（高台主题已去掉）。", [plate.Origin]));
            }
            else if (plate.Kind != BoardPlateKind.Birth && plate.Theme is not null)
            {
                f.Add(new MapValidationFailure("PLATE_THEME", $"{Describe(plate)} 不是出生棋盘，不得带主题。", [plate.Origin]));
            }
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

    /// <summary>
    /// 整岛规则（map-expand-25 D7）：
    /// 平台两两不重叠、外接矩形间隔 ≥ <see cref="PlateRules.MinGap"/>（<c>ISLAND_PLATE_GAP</c>）；全部可落子格沿气边连成一片（<c>ISLAND_LIBERTY_SPLIT</c>）；
    /// 可落子区外接矩形 ≥ W(n)×W(n)（<c>ISLAND_EXTENT</c>，验收的核心断言）；公共区档信物都在低地（不在任何平台外接矩形内）、至多 n+2 个（<c>ISLAND_PUBLIC_RELIC</c>）。
    /// 人数不受支持（没有预算行）时只查前两条。
    /// </summary>
    private static void ValidateIsland(MapData map, PlateRules plates, Budget? known, ImmutableArray<MapValidationFailure>.Builder f)
    {
        for (int i = 0; i < map.Boards.Length; i++)
        {
            for (int j = i + 1; j < map.Boards.Length; j++)
            {
                BoardPlate a = map.Boards[i];
                BoardPlate b = map.Boards[j];
                if (!PlatesApart(a, b, plates.MinGap))
                {
                    f.Add(new MapValidationFailure(
                        "ISLAND_PLATE_GAP",
                        $"{Describe(a)} 与 {Describe(b)} 重叠或间隔不足 {plates.MinGap} 格：平台之间必须隔着至少 {plates.MinGap} 格宽的低地。",
                        [a.Origin, b.Origin]));
                }
            }
        }

        ImmutableArray<Coord> split = LibertySplit(map);
        if (!split.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "ISLAND_LIBERTY_SPLIT",
                $"全图可落子格沿气边不连通：有 {split.Length} 格与最大的一片隔着崖壁、栅栏、深水或障碍。",
                split));
        }

        if (known is not { } budget)
        {
            return;
        }

        Coord[] playable = [.. map.AllCoords().Where(map.IsPlayable)];
        int columns = playable.Length == 0 ? 0 : playable.Max(c => c.X) - playable.Min(c => c.X) + 1;
        int rows = playable.Length == 0 ? 0 : playable.Max(c => c.Y) - playable.Min(c => c.Y) + 1;
        if (columns < budget.IslandSide || rows < budget.IslandSide)
        {
            f.Add(new MapValidationFailure(
                "ISLAND_EXTENT",
                $"可落子区外接矩形 {columns}×{rows}：{map.MaxPlayers} 人浮空岛必须至少 {budget.IslandSide}×{budget.IslandSide}。",
                ImmutableArray<Coord>.Empty));
        }

        Coord[] contested = [.. map.RelicCells.Where(kv => kv.Value.Zone != RelicZone.BirthZone).Select(kv => kv.Key).Order()];
        Coord[] onPlate = [.. contested.Where(c => map.Boards.Any(p => p.Contains(c)))];
        if (onPlate.Length > 0)
        {
            f.Add(new MapValidationFailure(
                "ISLAND_PUBLIC_RELIC", "公共区档信物格必须在平台之间的低地上，不得在平台内。", [.. onPlate]));
        }

        if (contested.Length > budget.PublicRelicsMax)
        {
            f.Add(new MapValidationFailure(
                "ISLAND_PUBLIC_RELIC",
                $"有 {contested.Length} 个公共区档信物格：{map.MaxPlayers} 人浮空岛至多 {budget.PublicRelicsMax} 个（人数 + 2）。",
                [.. contested]));
        }
    }

    /// <summary>两块平台外接矩形在横向或纵向上隔开至少 <paramref name="gap"/> 格（<c>ISLAND_PLATE_GAP</c> 的唯一判定，生成器落位也调它）。重叠时为 <c>false</c>。</summary>
    internal static bool PlatesApart(BoardPlate a, BoardPlate b, int gap) =>
        a.Origin.X + a.Width + gap <= b.Origin.X || b.Origin.X + b.Width + gap <= a.Origin.X
        || a.Origin.Y + a.Height + gap <= b.Origin.Y || b.Origin.Y + b.Height + gap <= a.Origin.Y;

    /// <summary>
    /// 全部可落子格沿气边（<see cref="Adjacency.LibertyNeighbors"/>）分片后，最大一片之外的格（并列取先出现的一片为最大，按坐标升序）；连成一片时为空。
    /// <c>ISLAND_LIBERTY_SPLIT</c> 的唯一判定，生成器的低地自检也调它。
    /// </summary>
    internal static ImmutableArray<Coord> LibertySplit(MapData map)
    {
        var visited = new HashSet<Coord>();
        List<ImmutableArray<Coord>> parts = [];
        foreach (Coord start in map.AllCoords())
        {
            if (!visited.Contains(start) && map.IsPlayable(start))
            {
                parts.Add(FloodFill(map, start, visited));
            }
        }

        if (parts.Count <= 1)
        {
            return [];
        }

        ImmutableArray<Coord> main = parts.MaxBy(p => p.Length);
        return [.. parts.Where(p => p != main).SelectMany(p => p).Order()];
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
