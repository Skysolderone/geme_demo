using System.Globalization;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 棋盘档生成参数（board-map D7）：棋盘数 7–10，缺省 7；其中出生棋盘固定 5 块，公共棋盘数 = 棋盘数 − 5（至少 2 块）。
/// 人数固定 4、规格档固定棋盘档；地图外接尺寸由摆放结果决定（按实际外接范围四周各留 2 格裁出，列、行各 20–50）。越界一律报错，MUST NOT 静默夹取。
/// </summary>
/// <remarks>规格：openspec/changes/board-map/specs/map-generation —— Requirement: 棋盘档生成参数</remarks>
public sealed record BoardMapParameters
{
    /// <summary>棋盘数下限。6 块时唯一的公共棋盘要接 5 条通道（当时每块棋盘 1–4 条）而矛盾，故为 7（2026-09-29 裁决）；规格仍要求至少 2 块公共棋盘。</summary>
    public const int MinBoards = 7;

    /// <summary>棋盘数上限。</summary>
    public const int MaxBoards = 10;

    /// <summary>缺省棋盘数：标识里省略 <c>:n&lt;N&gt;</c> 段时即此值。</summary>
    public const int DefaultBoards = 7;

    /// <summary>出生棋盘数，固定。</summary>
    public const int BirthBoards = 5;

    /// <summary>缺省参数：7 块棋盘。</summary>
    public static BoardMapParameters Default { get; } = new();

    /// <summary>棋盘数（出生棋盘 + 公共棋盘）。</summary>
    public int BoardCount { get; init; } = DefaultBoards;

    /// <summary>公共棋盘数 = 棋盘数 − 5。</summary>
    public int PublicBoards => BoardCount - BirthBoards;

    /// <summary>校验参数；不合法即抛出并说明合法范围。</summary>
    public void EnsureValid()
    {
        if (BoardCount is < MinBoards or > MaxBoards)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BoardCount), BoardCount, $"棋盘数必须在 {MinBoards}–{MaxBoards} 之间（缺省 {DefaultBoards}，其中出生棋盘固定 {BirthBoards} 块）。");
        }
    }
}

/// <summary>
/// 棋盘档生成图的地图标识（board-map D7）：<c>board:&lt;地图种子&gt;[:n&lt;棋盘数&gt;]</c>，种子为十进制无符号 64 位整数；
/// 棋盘数为缺省值时省略 <c>:n</c> 段。标识即地图的完整描述：凭它能重新生成同一张图。这里是解析与规范化的<b>唯一</b>实现。
/// </summary>
/// <remarks>
/// 裸 <c>board</c>（"随机取一个种子"）不是完整标识，<see cref="Parse"/> 不接受——取种子只发生在三个入口的最外层（Core 不读时钟），
/// 入口用 <see cref="IsBareRequest"/> 识别它，取到种子后用 <see cref="Format"/> 拼出完整标识再解析。
/// 规格：openspec/changes/board-map/specs/map-generation —— Requirement: 棋盘档生成图标识
/// </remarks>
public static class BoardMapId
{
    /// <summary>标识前缀。</summary>
    public const string Prefix = "board";

    private const string FormatHelp =
        "棋盘图标识的格式为 board:<地图种子>[:n<棋盘数>]：种子是十进制无符号 64 位整数，棋盘数 7–10（缺省 7 时省略，其中出生棋盘固定 5 块）；"
        + "例如 board:12345、board:12345:n9。";

    /// <summary>是否属于棋盘图标识一族（<c>board</c> 或以 <c>board:</c> 开头）。只看前缀，不保证能解析。</summary>
    public static bool IsBoardMap(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        return id == Prefix || id.StartsWith(Prefix + ":", StringComparison.Ordinal);
    }

    /// <summary>是否是裸 <c>board</c>：请求"随机取一个地图种子"，由入口最外层处理。</summary>
    public static bool IsBareRequest(string? mapId) => mapId?.Trim() == Prefix;

    /// <summary>规范化标识：棋盘数为缺省值时省略 <c>:n&lt;N&gt;</c> 段。</summary>
    public static string Format(ulong mapSeed, BoardMapParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.EnsureValid();
        string seed = mapSeed.ToString(CultureInfo.InvariantCulture);
        return parameters.BoardCount == BoardMapParameters.DefaultBoards
            ? $"{Prefix}:{seed}"
            : $"{Prefix}:{seed}:n{parameters.BoardCount.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>解析完整标识。格式不对或棋盘数越界抛 <see cref="FormatException"/>，消息说明格式与范围。</summary>
    public static (ulong MapSeed, BoardMapParameters Parameters) Parse(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        string[] parts = id.Split(':');
        if (parts[0] != Prefix || parts.Length is < 2 or > 3)
        {
            throw new FormatException($"无法解析棋盘图标识 \"{id}\"。{FormatHelp}");
        }

        if (!IsDigits(parts[1]) || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            throw new FormatException($"棋盘图标识 \"{id}\" 的地图种子 \"{parts[1]}\" 不是十进制无符号 64 位整数。{FormatHelp}");
        }

        int boards = BoardMapParameters.DefaultBoards;
        if (parts.Length == 3)
        {
            string n = parts[2];
            if (n.Length < 2 || n[0] != 'n' || !IsDigits(n[1..])
                || !int.TryParse(n[1..], NumberStyles.None, CultureInfo.InvariantCulture, out boards))
            {
                throw new FormatException($"棋盘图标识 \"{id}\" 的棋盘数段 \"{n}\" 写法不对。{FormatHelp}");
            }

            if (boards is < BoardMapParameters.MinBoards or > BoardMapParameters.MaxBoards)
            {
                throw new FormatException(
                    $"棋盘图标识 \"{id}\" 的棋盘数 {boards} 超出 {BoardMapParameters.MinBoards}–{BoardMapParameters.MaxBoards}。{FormatHelp}");
            }
        }

        return (seed, new BoardMapParameters { BoardCount = boards });
    }

    /// <summary>把任一合法写法规范化（<c>board:42:n7</c> → <c>board:42</c>）。</summary>
    public static string Normalize(string? mapId)
    {
        (ulong seed, BoardMapParameters parameters) = Parse(mapId);
        return Format(seed, parameters);
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
