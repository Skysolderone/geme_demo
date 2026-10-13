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
/// 地图静态校验。把必死口袋、出生区容量不足、拼接错位这类问题挡在加载期——
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
    /// 各地图共用、不在表里的规则：结构、地形边界、可落子格与信物格预算、出生区（保护期容量、出生区内无新地表）、信物格合法、中央入口与咽喉位于可落子格、
    /// 容差与豁免理由、必死口袋，以及链接档的拼接后校验（<see cref="ValidateSpliced"/>）。
    /// 旋转对称不在校验器里（原 MapSymmetry 只由标准档基准图的测试调用，已随旧地图删除——retire-legacy-maps）。
    /// </summary>
    private static readonly ImmutableDictionary<MapProfile, ProfileRules> Rules =
        new Dictionary<MapProfile, ProfileRules>
        {
            // 链接档（board-link-mode D2 拼接版 / 裁决 J1–J7）：可落子格 = 5 块出生棋盘之和 125–245、信物恰 10（每块出生棋盘 2 个）、出生区恰 5 个、各 25–49 格；
            // 棋盘边长 5–7、不同战场的棋盘至少隔 1 格（同一战场内的棋盘边对边紧贴）、没有通道；连通性按分量（每个分量含出生棋盘、分量数 = 5 − 版本号）；
            // 不要求到中央入口可达、不做距离均衡、不要求标注咽喉；画布列 20–75、行 8–21（裁决 J8-6：种子 1–20 穷举实测——J8-1 以西不动后含初始图宽 23–68、高 9–18——向外留余量）。
            [MapProfile.Link] = new(
                "链接档只提供 4 人的预算表（本模式只有 4 人局）",
                new Dictionary<int, Budget>
                {
                    [4] = new(125, 245, 10, 10, (5, 5), (25, 49)),
                }.ToImmutableDictionary(),
                ZonesMustExceedPlayers: true,
                ColumnRange: (20, 75),
                RowRange: (8, 21),
                PlateList: new((5, 7), (5, 7), MinGap: 1, BirthRelics: 2)),
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
    /// 棋盘清单规则：棋盘边长区间、出生棋盘边长区间、不同战场的棋盘两两的最小间隔、每块出生棋盘的信物格数。
    /// retire-legacy-maps 4.2：通道的宽度与长度区间随棋盘档删除（链接档没有通道）。
    /// </summary>
    private sealed record PlateRules(
        (int Min, int Max) Side,
        (int Min, int Max) BirthSide,
        int MinGap,
        int BirthRelics);

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
        ValidateLandmarks(map, f);
        ValidateTolerance(map, f);
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
    /// 棋盘清单（board-map「棋盘清单」、link-map-generation「物化状态校验」）：清单不得为空，逐块校验棋盘，再做拼接后校验。
    /// 返回 <c>false</c> 表示清单与该档根本不符，后面的规则不必再跑。
    /// retire-legacy-maps 4.2："清单必须为空"（标准档 / 边疆档）与通道、全图连通两条棋盘档规则随旧档删除。
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

        // 链接档（拼接，board-link-mode J1–J7）：同一战场内的棋盘边对边紧贴，间隔只在不同战场之间要求；没有通道，可落子格都必须属于某块棋盘；分量数 = 出生棋盘数 − 版本号。
        Battlefields fields = Battlefields.Of(map);
        int[,] owner = ValidatePlateCells(map, plates, f);
        ValidatePlateGaps(map, plates, fields, f);
        ValidateBirthPlates(map, f);
        ValidateFlatScenery(map, owner, f);
        ValidateSpliced(map, plates, owner, fields, f);
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
    /// 第 1、2 条：棋盘在盘内、边长在区间内，外接矩形内每一格都是 h=0 草地的可落子格。
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
            (int Min, int Max) side = plate.Kind == BoardPlateKind.Birth ? plates.BirthSide : plates.Side;
            if (plate.Width < side.Min || plate.Width > side.Max || plate.Height < side.Min || plate.Height > side.Max)
            {
                string birth = plate.Kind == BoardPlateKind.Birth ? $"，出生棋盘 {plates.BirthSide.Min}–{plates.BirthSide.Max}" : string.Empty;
                f.Add(new MapValidationFailure(
                    "BOARD_SIZE_OUT_OF_RANGE",
                    $"{Describe(plate)} 的宽与高必须各在合法边长 {plates.Side.Min}–{plates.Side.Max} 之内{birth}。",
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

    /// <summary>
    /// 第 3 条：棋盘外接矩形两两不重叠，且至少间隔若干格（两个方向上间隔的较大者；投影重叠的方向记为负）。
    /// 只对<b>不同战场</b>（<paramref name="fields"/>）的棋盘要求间隔——同一战场内的棋盘是拼接出来的，边对边紧贴；重叠由 <see cref="ValidateSpliced"/> 另查。
    /// </summary>
    private static void ValidatePlateGaps(MapData map, PlateRules plates, Battlefields fields, ImmutableArray<MapValidationFailure>.Builder f)
    {
        for (int i = 0; i < map.Boards.Length; i++)
        {
            for (int j = i + 1; j < map.Boards.Length; j++)
            {
                BoardPlate a = map.Boards[i];
                BoardPlate b = map.Boards[j];
                if (fields.At(a.Origin) is { } fa && fields.At(b.Origin) is { } fb && ReferenceEquals(fa, fb))
                {
                    continue;
                }

                int dx = Math.Max(a.Origin.X - (b.Origin.X + b.Width), b.Origin.X - (a.Origin.X + a.Width));
                int dy = Math.Max(a.Origin.Y - (b.Origin.Y + b.Height), b.Origin.Y - (a.Origin.Y + a.Height));
                int gap = Math.Max(dx, dy);
                if (gap < plates.MinGap)
                {
                    string how = gap < 0 ? "重叠" : $"只隔 {gap} 格";
                    f.Add(new MapValidationFailure(
                        "BOARD_TOO_CLOSE",
                        $"{Describe(a)} 与 {Describe(b)} {how}：棋盘两两至少间隔 {plates.MinGap} 格（未链接的战场不得相邻）。",
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

    /// <summary>第 6、7 条：全图不得有深水；信物格必须在棋盘内（通道格不得是信物格）。</summary>
    private static void ValidateFlatScenery(MapData map, int[,] owner, ImmutableArray<MapValidationFailure>.Builder f)
    {
        ImmutableArray<Coord> water = [.. map.AllCoords().Where(c => map.SurfaceAt(c) == Surface.DeepWater)];
        if (!water.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "BOARD_MAP_DEEP_WATER",
                "棋盘档地图不得含深水格（含架了桥的）：棋盘与通道之外的格子必须全部是障碍格。",
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
    /// 链接档的拼接校验（board-link-mode D1 / D2 拼接版，规格 link-map-generation「物化状态校验」）：
    /// 可落子格都属于某块棋盘（没有通道）；棋盘两两不重叠；每块出生棋盘恰 <see cref="PlateRules.BirthRelics"/> 个信物格；
    /// 按几何四邻导出的连通分量（<see cref="Battlefields"/>）每个都含出生棋盘，且分量数 = 出生棋盘数 − 地图版本号——少了即有未链接的战场被拼到了一起（相邻），多了即有已链接的战场没有连通。
    /// 分量之间**不要求**连通：开局 5 块出生棋盘本就彼此隔绝。
    /// </summary>
    private static void ValidateSpliced(MapData map, PlateRules plates, int[,] owner, Battlefields fields, ImmutableArray<MapValidationFailure>.Builder f)
    {
        ImmutableArray<Coord> loose = [.. map.AllCoords().Where(c => map.IsPlayable(c) && owner[c.X, c.Y] < 0)];
        if (!loose.IsEmpty)
        {
            f.Add(new MapValidationFailure(
                "SCENERY_CELL_PLAYABLE",
                "拼接图上的可落子格都必须属于某块出生棋盘：没有通道，棋盘之外必须全部是障碍格。",
                loose));
        }

        for (int i = 0; i < map.Boards.Length; i++)
        {
            BoardPlate plate = map.Boards[i];
            bool inside = plate.Width > 0 && plate.Height > 0 && plate.Origin.X + plate.Width <= map.Width && plate.Origin.Y + plate.Height <= map.Height;
            if (!inside)
            {
                continue;
            }

            ImmutableArray<Coord> overlapped = [.. plate.Cells().Where(c => owner[c.X, c.Y] != i)];
            if (!overlapped.IsEmpty)
            {
                f.Add(new MapValidationFailure(
                    "BOARD_OVERLAP", $"{Describe(plate)} 与清单里靠前的棋盘重叠：拼接只能边对边紧贴，不得重叠。", overlapped));
            }

            if (plate.Kind == BoardPlateKind.Birth)
            {
                int want = plates.BirthRelics;
                int relics = map.RelicCells.Keys.Count(plate.Contains);
                if (relics != want)
                {
                    f.Add(new MapValidationFailure(
                        "BIRTH_BOARD_RELIC_COUNT", $"{Describe(plate)} 有 {relics} 个信物格，每块出生棋盘必须恰有 {want} 个。", [plate.Origin]));
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

        int births = map.Boards.Count(b => b.Kind == BoardPlateKind.Birth);
        int expected = births - map.MapVersion;
        int actual = fields.All.Count(field => field.HasBirthBoard);
        if (actual != expected)
        {
            // 少了：有两个未链接的战场相邻、被当成一个——指出全部含多块出生棋盘的分量；多了：版本号对应的链接没有连通——指出全部分量。
            ImmutableArray<Coord> cells = [.. fields.All.Where(field => field.HasBirthBoard && (actual > expected || field.BirthZones.Length > 1)).SelectMany(field => field.Cells)];
            f.Add(new MapValidationFailure(
                "BATTLEFIELD_COUNT_MISMATCH",
                $"地图版本号 {map.MapVersion} 对应 {expected} 个战场，实际导出 {actual} 个："
                + (actual < expected ? "有未链接的战场相邻、被拼到了一起（未链接的战场之间 MUST 不相邻）。" : "有已链接的战场没有沿接缝连通。"),
                cells));
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

        // retire-legacy-maps 4.2："咽喉必须显式标注"（CHOKE_NOT_ANNOTATED）随距离均衡删除；标了的咽喉仍须位于可落子格。
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
