using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Siege.Core.Board;

/// <summary>
/// 地图数据的文件格式（JSON）。坐标一律用围棋记法，使手工编辑的地图文件可被人直接读写。
/// </summary>
/// <remarks>
/// 地形、障碍、出生区与信物格位置由设计师固定，因此它们必须能脱离代码维护。
/// 往返读写 MUST 无信息丢失——丢失的那一项通常是豁免理由或预算档位，而它们恰恰只在校验失败时才被注意到。
/// 规格：openspec/changes/add-board-core/specs/map-definition —— Requirement: 地图为设计师固定的静态数据
/// </remarks>
public static class MapFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 设计师手写的键名大小写不必与 DTO 一致；未知字段（如 _comment）保持宽容，不启用 Disallow。
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>序列化为 JSON。</summary>
    public static string ToJson(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var dto = new MapDto
        {
            Id = map.Id,
            Width = map.Width,
            Height = map.Height,
            MaxPlayers = map.MaxPlayers,
            // 标准档不写出：四份既有 maps/*.json 逐字节不变；缺字段按标准档读入（frontier-map 1.1）。
            Profile = map.Profile == MapProfile.Standard ? null : map.Profile,
            Obstacles = Sorted(map.Obstacles),
            BirthZones = [.. map.BirthZones.Select(Sorted)],
            RelicCells = map.RelicCells
                .OrderBy(kv => kv.Key)
                .ToDictionary(
                    kv => kv.Key.ToNotation(),
                    kv => new RelicDto { Zone = kv.Value.Zone, Budget = kv.Value.Budget }),
            Sites = map.Sites
                .OrderBy(kv => kv.Key)
                .ToDictionary(kv => kv.Key.ToNotation(), kv => (SiteTier?)kv.Value),
            ChokePoints = Sorted(map.ChokePoints),
            CentralEntrance = map.CentralEntrance.ToNotation(),
            DistanceTolerance = map.DistanceTolerance,
            ToleranceRelaxReason = map.ToleranceRelaxReason,
            MinTwoEyeArea = map.MinTwoEyeArea,
            PocketExemptions = map.PocketExemptions
                .Order()
                .ToDictionary(
                    c => c.ToNotation(),
                    c => map.PocketExemptionReasons.TryGetValue(c, out string? r) ? r : string.Empty),
            Heights = Rows(map, c => (char)('0' + map.HeightAt(c))),
            Surfaces = Rows(map, c => SurfaceCode(map.SurfaceAt(c))),
            Bridges = Sorted(map.TerrainData.Bridges),
            Fences = [.. map.TerrainData.Fences.OrderBy(f => f.A).ThenBy(f => f.B).Select(f => f.ToString())],
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>从 JSON 反序列化。</summary>
    public static MapData FromJson(string json)
    {
        MapDto dto = JsonSerializer.Deserialize<MapDto>(json, Options)
                     ?? throw new FormatException("地图文件为空或不是合法 JSON。");

        List<List<string>> zones = Required(dto.BirthZones, "BirthZones");
        Dictionary<string, RelicDto> relicCells = Required(dto.RelicCells, "RelicCells");
        Dictionary<string, string> exemptions = Required(dto.PocketExemptions, "PocketExemptions");
        Dictionary<string, SiteTier?> sites = Required(dto.Sites, "Sites");

        return new MapData
        {
            Id = Required(dto.Id, "Id"),
            Width = dto.Width,
            Height = dto.Height,
            MaxPlayers = dto.MaxPlayers,
            Profile = ParseProfile(dto.Profile),
            Obstacles = Parse(Required(dto.Obstacles, "Obstacles")),
            BirthZones = [.. zones.Select((z, i) => Parse(Required(z, $"BirthZones[{i}]")))],
            RelicCells = relicCells.ToImmutableDictionary(
                kv => Coord.Parse(kv.Key),
                kv => new RelicCellSpec(
                    Required(kv.Value, $"RelicCells[\"{kv.Key}\"]").Zone,
                    kv.Value.Budget)),
            Sites = sites.ToImmutableDictionary(
                kv => Coord.Parse(kv.Key),
                kv => kv.Value ?? throw new FormatException($"地图文件的 Sites[\"{kv.Key}\"] 缺档位：据点档位必填（Tent / Campfire / Stele）。")),
            ChokePoints = Parse(Required(dto.ChokePoints, "ChokePoints")),
            CentralEntrance = Coord.Parse(dto.CentralEntrance),
            DistanceTolerance = dto.DistanceTolerance,
            ToleranceRelaxReason = dto.ToleranceRelaxReason,
            MinTwoEyeArea = dto.MinTwoEyeArea,
            PocketExemptions = exemptions.Keys.Select(Coord.Parse).ToImmutableHashSet(),
            PocketExemptionReasons = exemptions.ToImmutableDictionary(
                kv => Coord.Parse(kv.Key), kv => kv.Value),
            TerrainData = ParseTerrain(dto),
        };
    }

    /// <summary>规格档缺省为标准档；写成数字且不是已定义的档位时响亮失败（字符串形式的未知值由 JSON 反序列化直接拒绝）。</summary>
    private static MapProfile ParseProfile(MapProfile? profile)
    {
        MapProfile value = profile ?? MapProfile.Standard;
        return Enum.IsDefined(value)
            ? value
            : throw new FormatException($"地图文件的 Profile 为 {(int)value}：规格档只能是 Standard（标准）/ Frontier（边疆）。");
    }

    /// <summary>
    /// 地形字段全部可省略（旧 v2 文件按全平地读入）；写出时按行字符串自上而下（最高行号在前，与看图方向一致）。
    /// 桥与栅栏的合法性由 <see cref="TerrainData"/> 构造期校验，这里把它的 <see cref="ArgumentException"/> 换成指名字段的 <see cref="FormatException"/>。
    /// </summary>
    private static TerrainData ParseTerrain(MapDto dto)
    {
        List<string> heightRows = Required(dto.Heights, "Heights");
        List<string> surfaceRows = Required(dto.Surfaces, "Surfaces");
        List<string> bridges = Required(dto.Bridges, "Bridges");
        List<string> fences = Required(dto.Fences, "Fences");

        ImmutableDictionary<Coord, int>.Builder heights = ImmutableDictionary.CreateBuilder<Coord, int>();
        foreach ((Coord c, char code) in Cells(dto, heightRows, "Heights"))
        {
            if (code is < '0' or > (char)('0' + TerrainData.MaxHeight))
            {
                throw new FormatException($"地图文件的 Heights 在 {c.ToNotation()} 处为 '{code}'：高度只能是 0–{TerrainData.MaxHeight}。");
            }

            if (code != '0')
            {
                heights[c] = code - '0';
            }
        }

        ImmutableDictionary<Coord, Surface>.Builder surfaces = ImmutableDictionary.CreateBuilder<Coord, Surface>();
        foreach ((Coord c, char code) in Cells(dto, surfaceRows, "Surfaces"))
        {
            Surface surface = SurfaceFromCode(code)
                ?? throw new FormatException($"地图文件的 Surfaces 在 {c.ToNotation()} 处为 '{code}'：地表只能是 G（草地）/ R（土路）/ F（林地）/ W（深水）。");
            if (surface != Surface.Grass)
            {
                surfaces[c] = surface;
            }
        }

        ImmutableHashSet<FenceEdge>.Builder fenceSet = ImmutableHashSet.CreateBuilder<FenceEdge>();
        foreach (string text in fences)
        {
            string[] ends = Required(text, "Fences[]").Split('-');
            if (ends.Length != 2)
            {
                throw new FormatException($"地图文件的 Fences 项 \"{text}\" 不是 \"F6-G6\" 形式。");
            }

            fenceSet.Add(new FenceEdge(Coord.Parse(ends[0]), Coord.Parse(ends[1])));
        }

        try
        {
            return new TerrainData(heights.ToImmutable(), surfaces.ToImmutable(), Parse(bridges), fenceSet.ToImmutable());
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"地图文件的地形数据不合法：{ex.Message}", ex);
        }
    }

    /// <summary>按行字符串（自上而下）逐格枚举；行数或行长与外接尺寸不符即报出字段名。空列表表示缺省。</summary>
    private static IEnumerable<(Coord Coord, char Code)> Cells(MapDto dto, List<string> rows, string field)
    {
        if (rows.Count == 0)
        {
            yield break;
        }

        if (rows.Count != dto.Height)
        {
            throw new FormatException($"地图文件的 {field} 有 {rows.Count} 行，与外接高度 {dto.Height} 不符。");
        }

        for (int i = 0; i < rows.Count; i++)
        {
            string row = Required(rows[i], $"{field}[{i}]");
            if (row.Length != dto.Width)
            {
                throw new FormatException($"地图文件的 {field} 第 {i + 1} 行长度 {row.Length} 与外接宽度 {dto.Width} 不符。");
            }

            int y = dto.Height - 1 - i;
            for (int x = 0; x < row.Length; x++)
            {
                yield return (new Coord(x, y), row[x]);
            }
        }
    }

    private static List<string> Rows(MapData map, Func<Coord, char> code)
    {
        var rows = new List<string>(map.Height);
        for (int y = map.Height - 1; y >= 0; y--)
        {
            var chars = new char[map.Width];
            for (int x = 0; x < map.Width; x++)
            {
                chars[x] = code(new Coord(x, y));
            }

            rows.Add(new string(chars));
        }

        return rows;
    }

    /// <summary>地表 ↔ 单字符码的唯一映射（与 <see cref="SurfaceFromCode"/> 同在一处）。</summary>
    private static char SurfaceCode(Surface surface) => surface switch
    {
        Surface.Grass => 'G',
        Surface.Road => 'R',
        Surface.Forest => 'F',
        Surface.DeepWater => 'W',
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, "未知地表。"),
    };

    private static Surface? SurfaceFromCode(char code) => char.ToUpperInvariant(code) switch
    {
        'G' => Surface.Grass,
        'R' => Surface.Road,
        'F' => Surface.Forest,
        'W' => Surface.DeepWater,
        _ => null,
    };

    /// <summary>
    /// 显式写成 <c>null</c> 的字段会让反序列化产出一个坏 <see cref="MapData"/>，
    /// 报出的却是 <c>ArgumentNullException: Parameter 'source'</c>——看不出是哪个字段。
    /// 这里把它换成指名道姓的 <see cref="FormatException"/>。
    /// </summary>
    private static T Required<T>(T? value, string field)
        where T : class =>
        value ?? throw new FormatException($"地图文件的 {field} 字段为 null：缺省请直接省略该字段，不要写 null。");

    private static List<string> Sorted(IEnumerable<Coord> coords) =>
        [.. coords.Order().Select(c => c.ToNotation())];

    private static ImmutableHashSet<Coord> Parse(IEnumerable<string> notations) =>
        notations.Select(Coord.Parse).ToImmutableHashSet();

    private sealed class MapDto
    {
        public string Id { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        public int MaxPlayers { get; set; }

        /// <summary>规格档（Standard 标准 / Frontier 边疆）。省略即标准档；标准档写出时也省略。</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MapProfile? Profile { get; set; }

        public List<string> Obstacles { get; set; } = [];

        public List<List<string>> BirthZones { get; set; } = [];

        public Dictionary<string, RelicDto> RelicCells { get; set; } = [];

        /// <summary>据点：格 → 档位（Tent 营帐 / Campfire 篝火 / Stele 石碑）。省略即无据点（v3 及更早的文件）。</summary>
        public Dictionary<string, SiteTier?> Sites { get; set; } = [];

        public List<string> ChokePoints { get; set; } = [];

        public string CentralEntrance { get; set; } = string.Empty;

        public int DistanceTolerance { get; set; } = 1;

        public string? ToleranceRelaxReason { get; set; }

        public int MinTwoEyeArea { get; set; } = 8;

        /// <summary>必死口袋豁免：格 → 理由。每条豁免都必须写明理由，否则校验不通过。</summary>
        public Dictionary<string, string> PocketExemptions { get; set; } = [];

        /// <summary>每格高度，按行字符串自上而下（第一行是最高行号），字符 0/1/2。省略即全 0。</summary>
        public List<string> Heights { get; set; } = [];

        /// <summary>每格地表，按行字符串自上而下，字符 G 草地 / R 土路 / F 林地 / W 深水。省略即全草地。</summary>
        public List<string> Surfaces { get; set; } = [];

        /// <summary>预置桥所在的深水格。</summary>
        public List<string> Bridges { get; set; } = [];

        /// <summary>栅栏边，形如 <c>F6-G6</c>。</summary>
        public List<string> Fences { get; set; } = [];
    }

    private sealed class RelicDto
    {
        public RelicZone Zone { get; set; }

        public BudgetTier Budget { get; set; }
    }
}
