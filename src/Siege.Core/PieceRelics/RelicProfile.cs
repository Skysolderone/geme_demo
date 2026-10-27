using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Siege.Core.Carry;

namespace Siege.Core.PieceRelics;

/// <summary>
/// 在途记录（档案 v2，design.md D7）：开局扣库存时写入、结算时清除，所以同一局只能结算一次（以对局标识去重）。
/// <paramref name="Carried"/> 是本局带入清单（可为空：本局未带入）。
/// </summary>
public sealed record RelicInFlight(string MatchId, ImmutableArray<CarriedRelic> Carried)
{
    /// <summary>值相等：逐项比较（<see cref="ImmutableArray{T}"/> 默认是引用相等）。</summary>
    public bool Equals(RelicInFlight? other) =>
        other is not null && MatchId == other.MatchId && Carried.SequenceEqual(other.Carried);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MatchId);
        foreach (CarriedRelic item in Carried)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// 本机玩家档案 v2（relic-redesign design.md D7 / R18 / R20）：格式版本、补给点、五种信物库存与至多一条在途记录。
/// 与旧 <c>Carry.CarryProfile</c>（v1）并存，接线段再切换；读入 v1 时按原价把补给折成补给点（<see cref="MigrateFromV1"/>）。
/// </summary>
/// <remarks>
/// JSON 形状固定、不缩进：<c>{"version":2,"points":17,"inventory":{"WarDrum":1,"Command":0,"Watchtower":2,"Chameleon":0,"Banner":1},"inFlight":null}</c>；
/// 在途记录为 <c>{"matchId":"…","carried":[{"kind":"Banner","insured":true}]}</c>。信物按枚举名写出。读入容忍任意空白与行尾；
/// 未知字段、重复键、负数、未知种类一律按损坏处理（档案 MUST NOT 保存对局内的成长，所以不认识的东西不能忽略后写回丢掉）。
/// </remarks>
public sealed record RelicProfile
{
    /// <summary>本程序支持的档案格式版本（R20：升到 2）。</summary>
    public const int CurrentVersion = 2;

    /// <summary>旧补给档案的版本（读入时迁移）。</summary>
    public const int LegacyVersion = 1;

    /// <summary>空档案：补给点 0、库存全 0、无在途记录。</summary>
    public static RelicProfile Empty { get; } = new();

    /// <summary>新档案（R18 / D7）：赠送 3 件普通信物，按固定次序战鼓 / 号令 / 望台各 1（件数未校准，见 <see cref="RelicKinds.UncalibratedStarterGift"/>）。</summary>
    public static RelicProfile New { get; } = RelicKinds.UncalibratedStarterGift.Aggregate(Empty, (profile, kind) => profile.WithStock(kind, 1));

    public int Version { get; init; } = CurrentVersion;

    /// <summary>补给点（非负）。</summary>
    public int Points { get; init; }

    /// <summary>各信物库存（非负）；未列出的视为 0。</summary>
    public ImmutableSortedDictionary<RelicKind, int> Inventory { get; init; } = ImmutableSortedDictionary<RelicKind, int>.Empty;

    /// <summary>在途记录；没有进行中的对局时为 <c>null</c>。</summary>
    public RelicInFlight? InFlight { get; init; }

    /// <summary>某种信物的库存。</summary>
    public int StockOf(RelicKind kind) => Inventory.GetValueOrDefault(kind);

    /// <summary>某种信物库存加 <paramref name="delta"/> 后的档案；结果为负即抛出。</summary>
    internal RelicProfile WithStock(RelicKind kind, int delta)
    {
        int stock = StockOf(kind) + delta;
        return stock >= 0
            ? this with { Inventory = Inventory.SetItem(kind, stock) }
            : throw new InvalidOperationException($"{kind.DisplayName()}库存不足（现有 {StockOf(kind)}，变动 {delta}）。");
    }

    /// <summary>
    /// v1 迁移（R20 / D7）：补给库存按原价折回补给点（备用子 3、征召签 2、换型令 4，取 <see cref="Supplies.PriceOf"/>，不另写价格表），信物库存为 0；
    /// v1 残留的在途记录按中途退出结算（补给丢失、带出 0）后清除。v1 档案不是新档案，不送新档案的 3 件。结果的版本为 2。
    /// </summary>
    public static RelicProfile MigrateFromV1(CarryProfile legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        int refund = 0;
        foreach (SupplyKind kind in Supplies.Order)
        {
            refund += legacy.StockOf(kind) * Supplies.PriceOf(kind);
        }

        return Empty with { Points = legacy.Points + refund };
    }

    /// <summary>写出档案 JSON（固定键序、不缩进，五种信物总是全部写出）。</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteNumber("points", Points);
            writer.WriteStartObject("inventory");
            foreach (RelicKind kind in RelicKinds.Order)
            {
                writer.WriteNumber(kind.ToString(), StockOf(kind));
            }

            writer.WriteEndObject();
            if (InFlight is { } flight)
            {
                writer.WriteStartObject("inFlight");
                writer.WriteString("matchId", flight.MatchId);
                writer.WriteStartArray("carried");
                foreach (CarriedRelic item in flight.Carried)
                {
                    writer.WriteStartObject();
                    writer.WriteString("kind", item.Kind.ToString());
                    writer.WriteBoolean("insured", item.Insured);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
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

    /// <summary>同 <see cref="Parse(string, out bool)"/>，不关心是否经过迁移。</summary>
    public static RelicProfile Parse(string json) => Parse(json, out _);

    /// <summary>
    /// 解析档案 JSON。格式版本高于 <see cref="CurrentVersion"/> 时抛 <see cref="RelicProfileVersionException"/>（不再往下校验）；
    /// 版本 1 时按 v1 解析（唯一实现 <see cref="CarryProfile.Parse"/>）再迁移，<paramref name="migratedFromV1"/> 为 <c>true</c>；
    /// 无法解析、缺必需字段（version / points / inventory）、负数、非整数、重复键、未知字段、未知信物名、在途记录不合形时抛 <see cref="FormatException"/>。
    /// </summary>
    public static RelicProfile Parse(string json, out bool migratedFromV1)
    {
        ArgumentNullException.ThrowIfNull(json);
        migratedFromV1 = false;
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
                throw new RelicProfileVersionException(version);
            }

            if (version == LegacyVersion)
            {
                migratedFromV1 = true;
                return MigrateFromV1(CarryProfile.Parse(json));
            }

            if (version < LegacyVersion)
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

            ImmutableSortedDictionary<RelicKind, int>.Builder inventory = ImmutableSortedDictionary.CreateBuilder<RelicKind, int>();
            Dictionary<string, JsonElement> stocks = Fields(inventoryElement, "库存");
            foreach (string name in stocks.Keys)
            {
                inventory.Add(ParseKind(name), RequireNonNegative(stocks, name, "库存"));
            }

            RelicInFlight? inFlight = fields.TryGetValue("inFlight", out JsonElement flight) && flight.ValueKind != JsonValueKind.Null
                ? ParseInFlight(flight)
                : null;
            return new RelicProfile { Version = version, Points = points, Inventory = inventory.ToImmutable(), InFlight = inFlight };
        }
    }

    private static RelicInFlight ParseInFlight(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("在途记录不是对象");
        }

        Dictionary<string, JsonElement> fields = Fields(element, "在途记录");
        foreach (string name in fields.Keys)
        {
            if (name is not ("matchId" or "carried"))
            {
                throw new FormatException($"在途记录有未知字段 {name}");
            }
        }

        string matchId = fields.TryGetValue("matchId", out JsonElement id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } text
            ? text
            : throw new FormatException("在途记录缺少对局标识");
        if (!fields.TryGetValue("carried", out JsonElement carried) || carried.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("在途记录缺少带入清单（carried）或它不是数组");
        }

        ImmutableArray<CarriedRelic>.Builder items = ImmutableArray.CreateBuilder<CarriedRelic>();
        foreach (JsonElement entry in carried.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("带入清单的一项不是对象");
            }

            Dictionary<string, JsonElement> itemFields = Fields(entry, "带入项");
            if (itemFields.Count != 2
                || !itemFields.TryGetValue("kind", out JsonElement kind) || kind.ValueKind != JsonValueKind.String
                || !itemFields.TryGetValue("insured", out JsonElement insured) || insured.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new FormatException("带入项须恰有 kind（字符串）与 insured（布尔）两个字段");
            }

            items.Add(new CarriedRelic(ParseKind(kind.GetString()!), insured.GetBoolean()));
        }

        var loadout = new RelicLoadout(items.ToImmutable());
        return loadout.ShapeRefusal() is { } refusal
            ? throw new FormatException($"在途记录的带入清单不合规：{refusal}")
            : new RelicInFlight(matchId, loadout.Items);
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

    /// <summary>按枚举名解析信物种类（区分大小写、不接受数字）。</summary>
    private static RelicKind ParseKind(string name) =>
        Enum.GetNames<RelicKind>().Contains(name, StringComparer.Ordinal)
            ? Enum.Parse<RelicKind>(name)
            : throw new FormatException($"未知信物 {name}");
}

/// <summary>档案格式版本高于本程序支持的版本：档案 MUST NOT 被写回，本局按关闭带入带出进行。</summary>
public sealed class RelicProfileVersionException : Exception
{
    public RelicProfileVersionException(int fileVersion)
        : base($"档案格式版本 {fileVersion} 高于本程序支持的版本 {RelicProfile.CurrentVersion}。")
    {
        FileVersion = fileVersion;
    }

    public int FileVersion { get; }
}
