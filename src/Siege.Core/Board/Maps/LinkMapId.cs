using System.Globalization;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 链接档生成图的地图标识（board-link-mode D2 / 规格 link-map-generation「链接档地图标识与确定性」）：<c>link:&lt;地图种子&gt;</c>，
/// 种子为十进制无符号 64 位整数，没有其他参数。标识即地图初始状态的完整描述：凭它能重新生成同一张初始图。这里是解析与规范化的<b>唯一</b>实现。
/// </summary>
/// <remarks>
/// 裸 <c>link</c>（"随机取一个种子"）不是完整标识，<see cref="Parse"/> 不接受——取种子只发生在入口的最外层（Core 不读时钟），
/// 入口用 <see cref="IsBareRequest"/> 识别它，取到种子后用 <see cref="Format"/> 拼出完整标识再解析。写法参照 <see cref="BoardMapId"/>。
/// </remarks>
public static class LinkMapId
{
    /// <summary>标识前缀。</summary>
    public const string Prefix = "link";

    private const string FormatHelp = "链接图标识的格式为 link:<地图种子>：种子是十进制无符号 64 位整数，没有其他参数；例如 link:1、link:12345。";

    /// <summary>是否属于链接图标识一族（<c>link</c> 或以 <c>link:</c> 开头）。只看前缀，不保证能解析。</summary>
    public static bool IsLinkMap(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        return id == Prefix || id.StartsWith(Prefix + ":", StringComparison.Ordinal);
    }

    /// <summary>是否是裸 <c>link</c>：请求"随机取一个地图种子"，由入口最外层处理。</summary>
    public static bool IsBareRequest(string? mapId) => mapId?.Trim() == Prefix;

    /// <summary>规范化标识 <c>link:&lt;种子&gt;</c>。</summary>
    public static string Format(ulong mapSeed) => $"{Prefix}:{mapSeed.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>解析完整标识，返回地图种子。格式不对抛 <see cref="FormatException"/>，消息说明格式。</summary>
    public static ulong Parse(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        string[] parts = id.Split(':');
        if (parts[0] != Prefix || parts.Length != 2)
        {
            throw new FormatException($"无法解析链接图标识 \"{id}\"。{FormatHelp}");
        }

        if (!IsDigits(parts[1]) || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            throw new FormatException($"链接图标识 \"{id}\" 的地图种子 \"{parts[1]}\" 不是十进制无符号 64 位整数。{FormatHelp}");
        }

        return seed;
    }

    /// <summary>把任一合法写法规范化（去空白、去前导零：<c> link:007 </c> → <c>link:7</c>）。</summary>
    public static string Normalize(string? mapId) => Format(Parse(mapId));

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
