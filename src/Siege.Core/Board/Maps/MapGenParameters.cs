using System.Globalization;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 生成参数（map-generator 裁决 5）：平台数 5–8，缺省 6；图尺寸固定 25×30、人数固定 4、规格档固定边疆档。
/// 越界一律报错，MUST NOT 静默夹取。
/// </summary>
/// <remarks>规格：openspec/changes/map-generator/specs/map-generation —— Requirement: 生成参数</remarks>
public sealed record MapGenParameters
{
    /// <summary>平台数下限。</summary>
    public const int MinPlatforms = 5;

    /// <summary>平台数上限。</summary>
    public const int MaxPlatforms = 8;

    /// <summary>缺省平台数：标识里省略 <c>:p&lt;N&gt;</c> 段时即此值。</summary>
    public const int DefaultPlatforms = 6;

    /// <summary>缺省参数：6 个平台、边疆档、不投放新地表。标识里省略全部可选段时即此值。</summary>
    public static MapGenParameters Default { get; } = new();

    /// <summary>
    /// 入口"随机取一个地图种子"（裸 <c>gen</c> 与选图界面"换一张"）用的参数：缺省平台数、开新地表（terrain-surfaces D7）。
    /// 与 <see cref="Default"/> 分开，是因为手输 <c>gen:&lt;种子&gt;</c> 必须仍得到不含新地表、与既有标识逐字节相同的图。
    /// </summary>
    public static MapGenParameters RandomPick { get; } = new() { NewSurfaces = true };

    /// <summary>平台（出生区）数。</summary>
    public int PlatformCount { get; init; } = DefaultPlatforms;

    /// <summary>
    /// 新地表开关（terrain-surfaces D6）：开时在布局与校验全部完成之后另行投放荒漠 / 沼泽 / 岩台 / 浅滩；关时生成结果与引入开关之前逐字节相同。
    /// 标识里写作 <c>:s1</c> 段，缺省（关）省略。
    /// </summary>
    public bool NewSurfaces { get; init; }

    /// <summary>规格档。只有边疆档支持由地图种子生成；写成参数是为了让"请求标准档"有一个会响亮报错的入口，而不是无从表达。</summary>
    public MapProfile Profile { get; init; } = MapProfile.Frontier;

    /// <summary>校验参数；不合法即抛出并说明合法范围。</summary>
    public void EnsureValid()
    {
        if (Profile != MapProfile.Frontier)
        {
            throw new NotSupportedException("只有边疆档支持由地图种子生成；标准档地图必须是手工固定的内置图或地图文件。");
        }

        if (PlatformCount is < MinPlatforms or > MaxPlatforms)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PlatformCount), PlatformCount, $"平台数必须在 {MinPlatforms}–{MaxPlatforms} 之间（缺省 {DefaultPlatforms}）。");
        }
    }
}

/// <summary>
/// 生成图的地图标识（map-generator D3、terrain-surfaces D7）：<c>gen:&lt;地图种子&gt;[:p&lt;平台数&gt;][:s1]</c>，种子为十进制无符号 64 位整数；
/// 平台数为缺省值时省略 <c>:p</c> 段，新地表关时省略 <c>:s1</c> 段，两段同时出现时 <c>:p</c> 在前。
/// 标识即地图的完整描述：凭它能重新生成同一张图。这里是解析与规范化的<b>唯一</b>实现。
/// </summary>
/// <remarks>
/// 裸 <c>gen</c>（"随机取一个种子"）不是完整标识，<see cref="Parse"/> 不接受——取种子只发生在三个入口的最外层（Core 不读时钟），
/// 入口用 <see cref="IsBareRequest"/> 识别它，取到种子后用 <see cref="Format"/> 拼出完整标识再解析。
/// 规格：openspec/changes/map-generator/specs/map-generation —— Requirement: 生成图的地图标识
/// </remarks>
public static class GeneratedMapId
{
    /// <summary>标识前缀。</summary>
    public const string Prefix = "gen";

    private const string FormatHelp =
        "生成图标识的格式为 gen:<地图种子>[:p<平台数>][:s1]：种子是十进制无符号 64 位整数，平台数 5–8（缺省 6 时省略），"
        + ":s1 表示投放新地表（荒漠 / 沼泽 / 岩台 / 浅滩，不投放时省略，不写 :s0），两段同时出现时 :p 在前；例如 gen:12345、gen:12345:p7、gen:12345:p7:s1。";

    /// <summary>是否属于生成图标识一族（<c>gen</c> 或以 <c>gen:</c> 开头）。只看前缀，不保证能解析。</summary>
    public static bool IsGenerated(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        return id == Prefix || id.StartsWith(Prefix + ":", StringComparison.Ordinal);
    }

    /// <summary>是否是裸 <c>gen</c>：请求"随机取一个地图种子"，由入口最外层处理。</summary>
    public static bool IsBareRequest(string? mapId) => mapId?.Trim() == Prefix;

    /// <summary>规范化标识：平台数为缺省值时省略 <c>:p&lt;N&gt;</c> 段，新地表关时省略 <c>:s1</c> 段。</summary>
    public static string Format(ulong mapSeed, MapGenParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.EnsureValid();
        string seed = mapSeed.ToString(CultureInfo.InvariantCulture);
        string platforms = parameters.PlatformCount == MapGenParameters.DefaultPlatforms
            ? string.Empty
            : $":p{parameters.PlatformCount.ToString(CultureInfo.InvariantCulture)}";
        string surfaces = parameters.NewSurfaces ? ":" + NewSurfacesSegment : string.Empty;
        return $"{Prefix}:{seed}{platforms}{surfaces}";
    }

    /// <summary>新地表开的标识段（不含冒号）。</summary>
    public const string NewSurfacesSegment = "s1";

    /// <summary>解析完整标识。格式不对或平台数越界抛 <see cref="FormatException"/>，消息说明格式与范围。</summary>
    public static (ulong MapSeed, MapGenParameters Parameters) Parse(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        string[] parts = id.Split(':');
        if (parts[0] != Prefix || parts.Length is < 2 or > 4)
        {
            throw new FormatException($"无法解析生成图标识 \"{id}\"。{FormatHelp}");
        }

        if (!IsDigits(parts[1]) || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            throw new FormatException($"生成图标识 \"{id}\" 的地图种子 \"{parts[1]}\" 不是十进制无符号 64 位整数。{FormatHelp}");
        }

        // 可选段按固定次序：[:p<N>] 在前、[:s1] 在后；各至多一次。
        int next = 2;
        int platforms = MapGenParameters.DefaultPlatforms;
        if (next < parts.Length && parts[next].StartsWith('p'))
        {
            string p = parts[next++];
            if (p.Length < 2 || p[0] != 'p' || !IsDigits(p[1..])
                || !int.TryParse(p[1..], NumberStyles.None, CultureInfo.InvariantCulture, out platforms))
            {
                throw new FormatException($"生成图标识 \"{id}\" 的平台数段 \"{p}\" 写法不对。{FormatHelp}");
            }

            if (platforms is < MapGenParameters.MinPlatforms or > MapGenParameters.MaxPlatforms)
            {
                throw new FormatException(
                    $"生成图标识 \"{id}\" 的平台数 {platforms} 超出 {MapGenParameters.MinPlatforms}–{MapGenParameters.MaxPlatforms}。{FormatHelp}");
            }
        }

        bool newSurfaces = false;
        if (next < parts.Length && parts[next] == NewSurfacesSegment)
        {
            newSurfaces = true;
            next++;
        }

        if (next < parts.Length)
        {
            throw new FormatException($"生成图标识 \"{id}\" 的段 \"{parts[next]}\" 写法或位置不对。{FormatHelp}");
        }

        return (seed, new MapGenParameters { PlatformCount = platforms, NewSurfaces = newSurfaces });
    }

    /// <summary><see cref="FriendlySeed"/> 的取值上界（不含）：九位十进制数以内，便于读写与口头交流。</summary>
    public const ulong FriendlySeedLimit = 1_000_000_000UL;

    /// <summary>
    /// 把一个原始计数（入口取的时间戳）折成便于人读写的地图种子：乘大奇数、异或移位两轮后取九位以内的十进制数。
    /// 纯函数——时间戳由入口最外层取（Core 不读时钟）；三个入口"随机取一个地图种子"都经这里折一次，
    /// 相邻两次取到的种子不挨着，也不和对局种子长得几乎一样。
    /// </summary>
    public static ulong FriendlySeed(ulong raw)
    {
        ulong z = unchecked(raw * 0x9E3779B97F4A7C15UL);
        z ^= z >> 32;
        z = unchecked(z * 0xD6E8FEB86659FD93UL);
        z ^= z >> 32;
        return z % FriendlySeedLimit;
    }

    /// <summary>把任一合法写法规范化（<c>gen:42:p6</c> → <c>gen:42</c>）。</summary>
    public static string Normalize(string? mapId)
    {
        (ulong seed, MapGenParameters parameters) = Parse(mapId);
        return Format(seed, parameters);
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
