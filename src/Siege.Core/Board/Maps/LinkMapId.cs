using System.Globalization;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 链接档生成图的地图标识（board-link-mode D2；map-expand-25 D1 / 裁决 Q4）：<c>link:&lt;地图种子&gt;:&lt;人数&gt;</c>，
/// 种子为十进制无符号 64 位整数，人数为 <see cref="MinPlayers"/>–<see cref="MaxPlayers"/>。标识即地图初始状态的完整描述：凭它能重新生成同一张初始图。这里是解析与规范化的<b>唯一</b>实现。
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
        "链接图标识的格式为 link:<地图种子>:<人数>：种子是十进制无符号 64 位整数，人数为 2–4；例如 link:1:4、link:12345:2。";

    /// <summary>是否属于链接图标识一族（<c>link</c> 或以 <c>link:</c> 开头）。只看前缀，不保证能解析。</summary>
    public static bool IsLinkMap(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        return id == Prefix || id.StartsWith(Prefix + ":", StringComparison.Ordinal);
    }

    /// <summary>是否是裸 <c>link</c>：请求"随机取一个地图种子"，由入口最外层处理。</summary>
    public static bool IsBareRequest(string? mapId) => mapId?.Trim() == Prefix;

    /// <summary>规范化标识 <c>link:&lt;种子&gt;:&lt;人数&gt;</c>。人数不在 2–4 抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
    public static string Format(ulong mapSeed, int players)
    {
        RequirePlayers(players);
        return $"{Prefix}:{mapSeed.ToString(CultureInfo.InvariantCulture)}:{players.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>人数不在 <see cref="MinPlayers"/>–<see cref="MaxPlayers"/> 即抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
    internal static void RequirePlayers(int players)
    {
        if (players < MinPlayers || players > MaxPlayers)
        {
            throw new ArgumentOutOfRangeException(nameof(players), players, $"链接图的人数必须在 {MinPlayers}–{MaxPlayers} 之间。");
        }
    }

    /// <summary>
    /// 解析完整标识，返回地图种子与人数。格式不对抛 <see cref="FormatException"/>，消息说明格式；
    /// 旧的两段写法 <c>link:&lt;种子&gt;</c> 另说明已作废，并给出补上人数的写法。
    /// </summary>
    public static (ulong Seed, int Players) Parse(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        string[] parts = id.Split(':');
        if (parts[0] == Prefix && parts.Length == 2 && IsDigits(parts[1]))
        {
            throw new FormatException(
                $"链接图标识 \"{id}\" 是已作废的旧格式（只有地图种子，没有人数）：地图已改为按人数生成的浮空岛，旧标识不再能加载。"
                + $"请写成 {id}:<人数>，例如 {id}:4。{FormatHelp}");
        }

        if (parts[0] != Prefix || parts.Length != 3)
        {
            throw new FormatException($"无法解析链接图标识 \"{id}\"。{FormatHelp}");
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

        return (seed, players);
    }

    /// <summary>把任一合法写法规范化（去空白、去前导零：<c> link:007:04 </c> → <c>link:7:4</c>）。</summary>
    public static string Normalize(string? mapId)
    {
        (ulong seed, int players) = Parse(mapId);
        return Format(seed, players);
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
