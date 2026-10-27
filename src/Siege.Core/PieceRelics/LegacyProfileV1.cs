using System.Text.Json;
using Siege.Core.Board;

namespace Siege.Core.PieceRelics;

/// <summary>
/// 旧补给档案 v1 的<b>只读</b>解析（R20 迁移所需的最少代码，接线段 W2 删除旧 <c>Carry/</c> 时从 <c>CarryProfile.Parse</c> / <c>Supplies.PriceOf</c> 收拢到这里）：
/// 只得出"补给点 + 三种补给按原价折回的点数"。旧补给本身已删除，这里不再有补给类型、写出或任何对局内用途。
/// </summary>
/// <remarks>
/// 严格程度与旧解析器相同：缺字段、负数、非整数、重复键、未知字段、未知补给名或棋子类型、在途记录不合形一律 <see cref="FormatException"/>
/// ——v1 档案读坏了要按损坏备份重置，不能静默迁移出错的点数。残留的在途记录按中途退出（补给丢失、带出 0）处理，即直接丢弃。
/// </remarks>
internal static class LegacyProfileV1
{
    /// <summary>v1 三种补给的原价（备用子 3、征召签 2、换型令 4；旧 <c>Supplies.PriceOf</c> 的未校准初值），按 v1 的补给名。</summary>
    internal static readonly IReadOnlyDictionary<string, int> UncalibratedSupplyPrices = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["SpareStone"] = 3,
        ["DraftLot"] = 2,
        ["Commission"] = 4,
    };

    /// <summary>
    /// 解析 v1 档案并返回迁移后的补给点：<c>points + Σ 库存 × 原价</c>。调用方已确认 <c>version == 1</c>。
    /// </summary>
    internal static int RefundedPoints(string json)
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
            if (RequireInt(fields, "version", "档案") != 1)
            {
                throw new FormatException("不是 v1 档案");
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

            long refund = 0;
            Dictionary<string, JsonElement> stocks = Fields(inventoryElement, "库存");
            foreach (string name in stocks.Keys)
            {
                int price = UncalibratedSupplyPrices.TryGetValue(name, out int p) ? p : throw new FormatException($"未知补给 {name}");
                refund += (long)RequireNonNegative(stocks, name, "库存") * price;
            }

            if (fields.TryGetValue("inFlight", out JsonElement flight) && flight.ValueKind != JsonValueKind.Null)
            {
                ValidateInFlight(flight);
            }

            long total = points + refund;
            return total <= int.MaxValue ? (int)total : throw new FormatException($"迁移后的补给点 {total} 超出范围");
        }
    }

    /// <summary>v1 在途记录只校验形状（按中途退出处理：补给丢失、带出 0，内容不进迁移结果）。</summary>
    private static void ValidateInFlight(JsonElement element)
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

        if (!fields.TryGetValue("matchId", out JsonElement id) || id.ValueKind != JsonValueKind.String || id.GetString() is not { Length: > 0 })
        {
            throw new FormatException("在途记录缺少对局标识");
        }

        string? supply = OptionalString(fields, "supply");
        string? type = OptionalString(fields, "type");
        if (supply is null)
        {
            if (type is not null)
            {
                throw new FormatException("在途记录未带入补给却给出了类型");
            }

            return;
        }

        if (!UncalibratedSupplyPrices.ContainsKey(supply))
        {
            throw new FormatException($"未知补给 {supply}");
        }

        if (type is not null && !Enum.GetNames<PieceType>().Contains(type, StringComparer.Ordinal))
        {
            throw new FormatException($"未知棋子类型 {type}");
        }

        bool spare = supply == "SpareStone";
        if (spare != (type is null))
        {
            throw new FormatException($"在途记录的{supply}{(spare ? "不应带类型" : "缺少类型")}");
        }
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
}
