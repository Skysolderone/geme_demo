using System.Globalization;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 链接档生成图的地图标识（board-link-mode D2；map-expand-25 D1 / 裁决 Q4；big-map D1）：<c>link:&lt;地图种子&gt;:&lt;人数&gt;[:&lt;档&gt;]</c>，
/// 种子为十进制无符号 64 位整数，人数为 <see cref="MinPlayers"/>–<see cref="MaxPlayers"/>，档位（<see cref="MapTier"/>）为可选第四段：<c>s</c> 小图、<c>l</c> 大图；
/// 三段写法 = 小图，规范化输出时小图恒写三段（big-map 裁决 11：现有标识、存档、日志与黄金值逐字节不变），大图写四段。标识即地图初始状态的完整描述：凭它能重新生成同一张初始图。这里是解析与规范化的<b>唯一</b>实现。
/// </summary>
/// <remarks>
/// 旧的两段写法 <c>link:&lt;种子&gt;</c>（5 块紧贴布局时期）已作废：<see cref="Parse"/> 报错并给出新格式示例，旧存档 / 回放 / 日志不再能加载（裁决 Q4）。
/// 裸 <c>link</c>（"随机取一个种子"）不是完整标识，<see cref="Parse"/> 不接受——取种子只发生在入口的最外层（Core 不读时钟），
/// 入口用 <see cref="IsBareRequest"/> 识别它，取到种子、定下人数后用 <see cref="Format"/> 拼出完整标识再解析；入口取种子时先用 <see cref="FriendlySeed"/> 把时间戳折成短种子。
/// </remarks>
public static class LinkMapId
{
    /// <summary>标识前缀。</summary>
    public const string Prefix = "link";

    /// <summary>人数下限（含）。</summary>
    public const int MinPlayers = 2;

    /// <summary>人数上限（含）。</summary>
    public const int MaxPlayers = 4;

    private const string FormatHelp =
        "链接图标识的格式为 link:<地图种子>:<人数>[:<档>]：种子是十进制无符号 64 位整数，人数为 2–4，档位可选（s 小图 = 三段写法，l 大图）；例如 link:1:4、link:12345:2、link:7:4:l。";

    /// <summary>档位段的写法：小图 <c>s</c>、大图 <c>l</c>（big-map D1）。</summary>
    public const string SmallTierCode = "s";
    public const string LargeTierCode = "l";

    /// <summary>是否属于链接图标识一族（<c>link</c> 或以 <c>link:</c> 开头）。只看前缀，不保证能解析。</summary>
    public static bool IsLinkMap(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        return id == Prefix || id.StartsWith(Prefix + ":", StringComparison.Ordinal);
    }

    /// <summary>是否是裸 <c>link</c>：请求"随机取一个地图种子"，由入口最外层处理。</summary>
    public static bool IsBareRequest(string? mapId) => mapId?.Trim() == Prefix;

    /// <summary>规范化的小图标识 <c>link:&lt;种子&gt;:&lt;人数&gt;</c>（三段）。人数不在 2–4 抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
    public static string Format(ulong mapSeed, int players) => Format(mapSeed, players, MapTier.Small);

    /// <summary>
    /// 规范化标识（big-map D1）：小图输出三段 <c>link:&lt;种子&gt;:&lt;人数&gt;</c>（与引入档位之前逐字节相同），大图输出四段 <c>link:&lt;种子&gt;:&lt;人数&gt;:l</c>。
    /// 人数不在 2–4、档位未定义抛 <see cref="ArgumentOutOfRangeException"/>。
    /// </summary>
    public static string Format(ulong mapSeed, int players, MapTier tier)
    {
        RequirePlayers(players);
        string head = $"{Prefix}:{mapSeed.ToString(CultureInfo.InvariantCulture)}:{players.ToString(CultureInfo.InvariantCulture)}";
        return tier switch
        {
            MapTier.Small => head,
            MapTier.Large => $"{head}:{LargeTierCode}",
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "链接图的档位只能是小图或大图。"),
        };
    }

    /// <summary>档位段的写法（<see cref="SmallTierCode"/> / <see cref="LargeTierCode"/>）。</summary>
    public static string TierCode(MapTier tier) => tier switch
    {
        MapTier.Small => SmallTierCode,
        MapTier.Large => LargeTierCode,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "链接图的档位只能是小图或大图。"),
    };

    /// <summary>人数不在 <see cref="MinPlayers"/>–<see cref="MaxPlayers"/> 即抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
    internal static void RequirePlayers(int players)
    {
        if (players < MinPlayers || players > MaxPlayers)
        {
            throw new ArgumentOutOfRangeException(nameof(players), players, $"链接图的人数必须在 {MinPlayers}–{MaxPlayers} 之间。");
        }
    }

    /// <summary>
    /// 解析完整标识，返回地图种子、人数与档位（big-map D1：三段 = 小图；四段的第四段 <c>s</c> / <c>l</c>）。格式不对抛 <see cref="FormatException"/>，消息说明格式；
    /// 旧的两段写法 <c>link:&lt;种子&gt;</c> 另说明已作废，并给出补上人数的写法。
    /// </summary>
    public static (ulong Seed, int Players, MapTier Tier) Parse(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        string[] parts = id.Split(':');
        if (parts[0] == Prefix && parts.Length == 2 && IsDigits(parts[1]))
        {
            throw new FormatException(
                $"链接图标识 \"{id}\" 是已作废的旧格式（只有地图种子，没有人数）：地图已改为按人数生成的浮空岛，旧标识不再能加载。"
                + $"请写成 {id}:<人数>，例如 {id}:4。{FormatHelp}");
        }

        if (parts[0] != Prefix || parts.Length is not (3 or 4))
        {
            throw new FormatException($"无法解析链接图标识 \"{id}\"。{FormatHelp}");
        }

        MapTier tier = MapTier.Small;
        if (parts.Length == 4)
        {
            tier = parts[3] switch
            {
                SmallTierCode => MapTier.Small,
                LargeTierCode => MapTier.Large,
                _ => throw new FormatException($"链接图标识 \"{id}\" 的档位 \"{parts[3]}\" 无法识别：只能是 {SmallTierCode}（小图）或 {LargeTierCode}（大图）。{FormatHelp}"),
            };
        }

        if (!IsDigits(parts[1]) || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            throw new FormatException($"链接图标识 \"{id}\" 的地图种子 \"{parts[1]}\" 不是十进制无符号 64 位整数。{FormatHelp}");
        }

        if (!IsDigits(parts[2]) || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int players)
            || players < MinPlayers || players > MaxPlayers)
        {
            throw new FormatException($"链接图标识 \"{id}\" 的人数 \"{parts[2]}\" 不在 {MinPlayers}–{MaxPlayers} 之间。{FormatHelp}");
        }

        return (seed, players, tier);
    }

    /// <summary>把任一合法写法规范化（去空白、去前导零：<c> link:007:04 </c> → <c>link:7:4</c>；小图的四段写法 <c>link:1:4:s</c> 回到三段，大图恒四段）。</summary>
    public static string Normalize(string? mapId)
    {
        (ulong seed, int players, MapTier tier) = Parse(mapId);
        return Format(seed, players, tier);
    }

    /// <summary><see cref="FriendlySeed"/> 的取值上界（不含）：九位十进制数以内，便于读写与口头交流。</summary>
    public const ulong FriendlySeedLimit = 1_000_000_000UL;

    /// <summary>
    /// 把一个原始计数（入口取的时间戳）折成便于人读写的地图种子：乘大奇数、异或移位两轮后取九位以内的十进制数。
    /// 纯函数——时间戳由入口最外层取（Core 不读时钟）；三个入口"随机取一个地图种子"都经这里折一次，
    /// 相邻两次取到的种子不挨着，也不和对局种子长得几乎一样。
    /// retire-legacy-maps 2.1：自已删除的 <c>GeneratedMapId.FriendlySeed</c>（边疆档生成图标识）原样迁入，输出逐位相同。
    /// </summary>
    public static ulong FriendlySeed(ulong raw)
    {
        ulong z = unchecked(raw * 0x9E3779B97F4A7C15UL);
        z ^= z >> 32;
        z = unchecked(z * 0xD6E8FEB86659FD93UL);
        z ^= z >> 32;
        return z % FriendlySeedLimit;
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
