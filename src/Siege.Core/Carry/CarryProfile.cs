using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Siege.Core.Board;

namespace Siege.Core.Carry;

/// <summary>
/// 在途记录（design.md D9）：开局扣库存时写入、结算时清除，所以同一局只能结算一次。
/// <paramref name="Carry"/> 为 <c>null</c> 表示本局未带入；征召签记录的是开局时已抽得的类型。
/// </summary>
public sealed record CarryInFlight(string MatchId, CarryIn? Carry);

/// <summary>
/// 本机玩家档案（carry-in-out「本地玩家档案」）：格式版本、补给点、各补给库存与至多一条在途记录。
/// 档案 MUST NOT 保存任何对局内的成长、等级或属性——所以读入时未知字段一律按损坏处理，而不是忽略后写回丢掉。
/// </summary>
/// <remarks>
/// JSON 形状固定、不缩进：<c>{"version":1,"points":17,"inventory":{"SpareStone":1,"DraftLot":0,"Commission":2},"inFlight":null}</c>；
/// 在途记录为 <c>{"matchId":"…","supply":"Commission","type":"Fortress"}</c>，未带入时 <c>supply</c> 与 <c>type</c> 为 <c>null</c>。
/// 补给与棋子类型按枚举名写出（<see cref="SupplyKind"/> 是末尾追加式枚举）。读入容忍任意空白与行尾。
/// </remarks>
public sealed record CarryProfile
{
    /// <summary>本程序支持的档案格式版本。以后升级时按版本迁移（design.md Migration Plan 4）。</summary>
    public const int CurrentVersion = 1;

    /// <summary>空档案：补给点 0、库存全 0、无在途记录。新档案没有启动资金（design.md D9）。</summary>
    public static CarryProfile Empty { get; } = new();

    public int Version { get; init; } = CurrentVersion;

    /// <summary>补给点（非负）。</summary>
    public int Points { get; init; }

    /// <summary>各补给库存（非负）；未列出的补给视为 0。</summary>
    public ImmutableSortedDictionary<SupplyKind, int> Inventory { get; init; } = ImmutableSortedDictionary<SupplyKind, int>.Empty;

    /// <summary>在途记录；没有进行中的对局时为 <c>null</c>。</summary>
    public CarryInFlight? InFlight { get; init; }

    /// <summary>某补给的库存。</summary>
    public int StockOf(SupplyKind kind) => Inventory.GetValueOrDefault(kind);

    /// <summary>可带入的补给（库存 ≥ 1），按补给固定次序。补给选择界面只列出这些（「库存为 0 不能带」）。</summary>
    public ImmutableArray<SupplyKind> Carriable => [.. Supplies.Order.Where(k => StockOf(k) > 0)];

    /// <summary>某补给库存加 <paramref name="delta"/> 后的档案。</summary>
    internal CarryProfile WithStock(SupplyKind kind, int delta) => this with { Inventory = Inventory.SetItem(kind, StockOf(kind) + delta) };

    /// <summary>写出档案 JSON（固定键序、不缩进，三种补给总是全部写出）。</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteNumber("points", Points);
            writer.WriteStartObject("inventory");
            foreach (SupplyKind kind in Supplies.Order)
            {
                writer.WriteNumber(kind.ToString(), StockOf(kind));
            }

            writer.WriteEndObject();
            if (InFlight is { } flight)
            {
                writer.WriteStartObject("inFlight");
                writer.WriteString("matchId", flight.MatchId);
                WriteNullable(writer, "supply", flight.Carry?.Kind.ToString());
                WriteNullable(writer, "type", flight.Carry?.Type?.ToString());
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("inFlight");
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// 解析档案 JSON。格式版本高于 <see cref="CurrentVersion"/> 时抛 <see cref="CarryProfileVersionException"/>（不再往下校验：新版本的字段本程序不认识）；
    /// 无法解析、缺必需字段（version / points / inventory）、负数、非整数、重复键、未知字段、未知补给名或棋子类型、在途记录不合形时抛 <see cref="FormatException"/>，消息说明原因。
    /// </summary>
    public static CarryProfile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"不是合法的 JSON（{ex.Message}）", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("顶层不是 JSON 对象");
            }

            Dictionary<string, JsonElement> fields = Fields(root, "档案");
            int version = RequireInt(fields, "version", "档案");
            if (version > CurrentVersion)
            {
                throw new CarryProfileVersionException(version);
            }

            if (version < 1)
            {
                throw new FormatException($"格式版本 {version} 不合法");
            }

            foreach (string name in fields.Keys)
            {
                if (name is not ("version" or "points" or "inventory" or "inFlight"))
                {
                    throw new FormatException($"未知字段 {name}");
                }
            }

            int points = RequireNonNegative(fields, "points", "档案");
            if (!fields.TryGetValue("inventory", out JsonElement inventoryElement) || inventoryElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("缺少库存（inventory）或它不是对象");
            }

            ImmutableSortedDictionary<SupplyKind, int>.Builder inventory = ImmutableSortedDictionary.CreateBuilder<SupplyKind, int>();
            Dictionary<string, JsonElement> stocks = Fields(inventoryElement, "库存");
            foreach (string name in stocks.Keys)
            {
                inventory.Add(ParseName<SupplyKind>(name, "补给"), RequireNonNegative(stocks, name, "库存"));
            }

            CarryInFlight? inFlight = fields.TryGetValue("inFlight", out JsonElement flight) && flight.ValueKind != JsonValueKind.Null
                ? ParseInFlight(flight)
                : null;
            return new CarryProfile { Version = version, Points = points, Inventory = inventory.ToImmutable(), InFlight = inFlight };
        }
    }

    private static CarryInFlight ParseInFlight(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("在途记录不是对象");
        }

        Dictionary<string, JsonElement> fields = Fields(element, "在途记录");
        foreach (string name in fields.Keys)
        {
            if (name is not ("matchId" or "supply" or "type"))
            {
                throw new FormatException($"在途记录有未知字段 {name}");
            }
        }

        string matchId = fields.TryGetValue("matchId", out JsonElement id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } text
            ? text
            : throw new FormatException("在途记录缺少对局标识");
        string? supply = OptionalString(fields, "supply");
        string? type = OptionalString(fields, "type");
        if (supply is null)
        {
            return type is null ? new CarryInFlight(matchId, null) : throw new FormatException("在途记录未带入补给却给出了类型");
        }

        SupplyKind kind = ParseName<SupplyKind>(supply, "补给");
        PieceType? pieceType = type is null ? null : ParseName<PieceType>(type, "棋子类型");
        bool wellFormed = kind == SupplyKind.SpareStone ? pieceType is null : pieceType is not null;
        return wellFormed
            ? new CarryInFlight(matchId, new CarryIn(kind, pieceType))
            : throw new FormatException($"在途记录的{supply}{(kind == SupplyKind.SpareStone ? "不应带类型" : "缺少类型")}");
    }

    /// <summary>对象的全部字段；重复键按损坏处理（JsonDocument 本身允许重复键，读哪一个没有定义）。</summary>
    private static Dictionary<string, JsonElement> Fields(JsonElement element, string owner)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!fields.TryAdd(property.Name, property.Value))
            {
                throw new FormatException($"{owner}的字段 {property.Name} 重复");
            }
        }

        return fields;
    }

    private static int RequireInt(Dictionary<string, JsonElement> fields, string name, string owner) =>
        fields.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
            ? number
            : throw new FormatException($"{owner}缺少整数字段 {name}");

    private static int RequireNonNegative(Dictionary<string, JsonElement> fields, string name, string owner)
    {
        int value = RequireInt(fields, name, owner);
        return value >= 0 ? value : throw new FormatException($"{owner}的 {name} 为负数（{value}）");
    }

    private static string? OptionalString(Dictionary<string, JsonElement> fields, string name) =>
        !fields.TryGetValue(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : throw new FormatException($"在途记录的 {name} 不是字符串");

    /// <summary>按枚举名解析（区分大小写、不接受数字）。</summary>
    private static T ParseName<T>(string name, string what)
        where T : struct, Enum =>
        Enum.GetNames<T>().Contains(name, StringComparer.Ordinal)
            ? Enum.Parse<T>(name)
            : throw new FormatException($"未知{what} {name}");

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}

/// <summary>档案格式版本高于本程序支持的版本：档案 MUST NOT 被写回，本局按关闭带入带出进行。</summary>
public sealed class CarryProfileVersionException : Exception
{
    public CarryProfileVersionException(int fileVersion)
        : base($"档案格式版本 {fileVersion} 高于本程序支持的版本 {CarryProfile.CurrentVersion}。")
    {
        FileVersion = fileVersion;
    }

    public int FileVersion { get; }
}
