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
            Obstacles = Sorted(map.Obstacles),
            BirthZones = [.. map.BirthZones.Select(Sorted)],
            RelicCells = map.RelicCells
                .OrderBy(kv => kv.Key)
                .ToDictionary(
                    kv => kv.Key.ToNotation(),
                    kv => new RelicDto { Zone = kv.Value.Zone, Budget = kv.Value.Budget }),
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

        return new MapData
        {
            Id = Required(dto.Id, "Id"),
            Width = dto.Width,
            Height = dto.Height,
            MaxPlayers = dto.MaxPlayers,
            Obstacles = Parse(Required(dto.Obstacles, "Obstacles")),
            BirthZones = [.. zones.Select((z, i) => Parse(Required(z, $"BirthZones[{i}]")))],
            RelicCells = relicCells.ToImmutableDictionary(
                kv => Coord.Parse(kv.Key),
                kv => new RelicCellSpec(
                    Required(kv.Value, $"RelicCells[\"{kv.Key}\"]").Zone,
                    kv.Value.Budget)),
            ChokePoints = Parse(Required(dto.ChokePoints, "ChokePoints")),
            CentralEntrance = Coord.Parse(dto.CentralEntrance),
            DistanceTolerance = dto.DistanceTolerance,
            ToleranceRelaxReason = dto.ToleranceRelaxReason,
            MinTwoEyeArea = dto.MinTwoEyeArea,
            PocketExemptions = exemptions.Keys.Select(Coord.Parse).ToImmutableHashSet(),
            PocketExemptionReasons = exemptions.ToImmutableDictionary(
                kv => Coord.Parse(kv.Key), kv => kv.Value),
        };
    }

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

        public List<string> Obstacles { get; set; } = [];

        public List<List<string>> BirthZones { get; set; } = [];

        public Dictionary<string, RelicDto> RelicCells { get; set; } = [];

        public List<string> ChokePoints { get; set; } = [];

        public string CentralEntrance { get; set; } = string.Empty;

        public int DistanceTolerance { get; set; } = 1;

        public string? ToleranceRelaxReason { get; set; }

        public int MinTwoEyeArea { get; set; } = 8;

        /// <summary>必死口袋豁免：格 → 理由。每条豁免都必须写明理由，否则校验不通过。</summary>
        public Dictionary<string, string> PocketExemptions { get; set; } = [];
    }

    private sealed class RelicDto
    {
        public RelicZone Zone { get; set; }

        public BudgetTier Budget { get; set; }
    }
}
