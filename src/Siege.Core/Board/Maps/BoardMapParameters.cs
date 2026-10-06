using System.Globalization;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 棋盘档生成参数（board-isolated-gen D1）：人数 2–4（缺省 4）与棋盘数。出生棋盘数 = 人数 + 1；棋盘数 = 出生棋盘数 + 公共棋盘数，
/// 范围随人数：4 人 7–10、3 人 5–8、2 人 4–5，缺省 = 出生棋盘数 + 2（4 人 7、3 人 6、2 人 5）。
/// 2 人只到 5（公共棋盘 1–2 块，主会话裁决 2026-10-06）：3 块公共棋盘压进 150–400 的预算会把公共棋盘挤到下限附近。
/// 规格档固定棋盘档；地图外接尺寸由摆放结果决定（按实际外接范围四周各留 2 格裁出，列、行各 15–60）。越界一律报错，MUST NOT 静默夹取。
/// </summary>
/// <remarks>
/// 不给 <see cref="BoardCount"/> 时取该人数的缺省棋盘数（所以只写 <c>new BoardMapParameters { Players = 3 }</c> 就是 3 人缺省 6 块）。
/// 规格：openspec/changes/board-isolated-gen/specs/map-generation —— Requirement: 棋盘档生成参数
/// </remarks>
public sealed record BoardMapParameters
{
    /// <summary>人数下限。</summary>
    public const int MinPlayers = 2;

    /// <summary>人数上限。</summary>
    public const int MaxPlayers = 4;

    /// <summary>缺省人数：标识里省略 <c>:p&lt;N&gt;</c> 段时即此值。</summary>
    public const int DefaultPlayers = 4;

    /// <summary>缺省人数（4 人）的棋盘数下限；其他人数见 <see cref="MinBoardsFor"/>。选图界面在第二个 change 之前只出 4 人图，读的是这三个常量。</summary>
    public const int MinBoards = 7;

    /// <summary>缺省人数（4 人）的棋盘数上限；其他人数见 <see cref="MaxBoardsFor"/>。</summary>
    public const int MaxBoards = 10;

    /// <summary>缺省人数（4 人）的缺省棋盘数；其他人数见 <see cref="DefaultBoardsFor"/>。</summary>
    public const int DefaultBoards = 7;

    /// <summary>缺省参数：4 人、7 块棋盘。</summary>
    public static BoardMapParameters Default { get; } = new();

    private readonly int? _boardCount;

    /// <summary>人数（2–4）。</summary>
    public int Players { get; init; } = DefaultPlayers;

    /// <summary>棋盘数（出生棋盘 + 公共棋盘）；不给即取该人数的缺省值。</summary>
    public int BoardCount
    {
        get => _boardCount ?? DefaultBoardsFor(Players);
        init => _boardCount = value;
    }

    /// <summary>出生棋盘数 = 人数 + 1（负责人裁决 2026-10-06 第 7 条）。</summary>
    public int BirthBoards => BirthBoardsFor(Players);

    /// <summary>公共棋盘数 = 棋盘数 − 出生棋盘数。</summary>
    public int PublicBoards => BoardCount - BirthBoards;

    /// <summary>该人数的出生棋盘数（人数 + 1）。</summary>
    public static int BirthBoardsFor(int players) => players + 1;

    /// <summary>该人数的棋盘数下限：4 人 7（公共棋盘至少 2 块，保持既有 <c>:n</c> 语义）、3 人 5、2 人 4（公共棋盘至少 1 块）。</summary>
    public static int MinBoardsFor(int players) => BirthBoardsFor(players) + (players == MaxPlayers ? 2 : 1);

    /// <summary>该人数的棋盘数上限：4 人 10、3 人 8、2 人 5（公共棋盘至多 5 / 4 / 2 块）。</summary>
    public static int MaxBoardsFor(int players) => BirthBoardsFor(players) + MaxPublicBoardsFor(players);

    /// <summary>该人数的公共棋盘数上限：4 人 5、3 人 4、2 人 2（2 人收窄见类注释）。</summary>
    private static int MaxPublicBoardsFor(int players) => players == MinPlayers ? 2 : players + 1;

    /// <summary>该人数的缺省棋盘数 = 出生棋盘数 + 2：4 人 7、3 人 6、2 人 5。</summary>
    public static int DefaultBoardsFor(int players) => BirthBoardsFor(players) + 2;

    /// <summary>该人数的合法棋盘数范围说明，如"4 人 7–10"。</summary>
    internal static string BoardRangeText(int players) =>
        $"{players} 人 {MinBoardsFor(players)}–{MaxBoardsFor(players)}（缺省 {DefaultBoardsFor(players)}，其中出生棋盘 {BirthBoardsFor(players)} 块）";

    /// <summary>全部人数的合法棋盘数范围说明。</summary>
    internal static string AllRangesText()
    {
        var parts = new List<string>();
        for (int players = MaxPlayers; players >= MinPlayers; players--)
        {
            parts.Add(BoardRangeText(players));
        }

        return string.Join("、", parts);
    }

    /// <summary>校验参数；不合法即抛出并说明合法范围。</summary>
    public void EnsureValid()
    {
        if (Players is < MinPlayers or > MaxPlayers)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Players), Players, $"人数必须在 {MinPlayers}–{MaxPlayers} 之间（缺省 {DefaultPlayers}）。合法人数 {MinPlayers}–{MaxPlayers}。");
        }

        if (BoardCount < MinBoardsFor(Players) || BoardCount > MaxBoardsFor(Players))
        {
            throw new ArgumentOutOfRangeException(
                nameof(BoardCount), BoardCount, $"棋盘数必须在该人数的合法范围内：{BoardRangeText(Players)}。");
        }
    }
}

/// <summary>
/// 棋盘档生成图的地图标识（board-isolated-gen D1）：<c>board:&lt;地图种子&gt;[:p&lt;人数&gt;][:n&lt;棋盘数&gt;]</c>，种子为十进制无符号 64 位整数；
/// 人数为 4 时省略 <c>:p</c> 段，棋盘数为该人数的缺省值时省略 <c>:n</c> 段，两段同时出现时 <c>:p</c> 在前。
/// 标识即地图的完整描述：凭它能重新生成同一张图。这里是解析与规范化的<b>唯一</b>实现。
/// </summary>
/// <remarks>
/// 裸 <c>board</c>（"随机取一个种子、4 人、缺省棋盘数"）不是完整标识，<see cref="Parse"/> 不接受——取种子只发生在三个入口的最外层（Core 不读时钟），
/// 入口用 <see cref="IsBareRequest"/> 识别它，取到种子后用 <see cref="Format"/> 拼出完整标识再解析。
/// 规格：openspec/changes/board-isolated-gen/specs/map-generation —— Requirement: 棋盘档生成图标识
/// </remarks>
public static class BoardMapId
{
    /// <summary>标识前缀。</summary>
    public const string Prefix = "board";

    /// <summary>格式与范围说明：报错时附上。</summary>
    internal static string FormatHelp =>
        "棋盘图标识的格式为 board:<地图种子>[:p<人数>][:n<棋盘数>]：种子是十进制无符号 64 位整数；"
        + $"合法人数 {BoardMapParameters.MinPlayers}–{BoardMapParameters.MaxPlayers}（缺省 4 时省略 :p 段）；"
        + $"棋盘数随人数：{BoardMapParameters.AllRangesText()}，为缺省值时省略 :n 段；两段同时出现时 :p 在前。"
        + "例如 board:12345、board:12345:n9、board:12345:p3、board:12345:p2:n4。";

    /// <summary>是否属于棋盘图标识一族（<c>board</c> 或以 <c>board:</c> 开头）。只看前缀，不保证能解析。</summary>
    public static bool IsBoardMap(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        return id == Prefix || id.StartsWith(Prefix + ":", StringComparison.Ordinal);
    }

    /// <summary>是否是裸 <c>board</c>：请求"随机取一个地图种子"，由入口最外层处理。</summary>
    public static bool IsBareRequest(string? mapId) => mapId?.Trim() == Prefix;

    /// <summary>规范化标识：人数为 4 时省略 <c>:p</c> 段，棋盘数为该人数的缺省值时省略 <c>:n</c> 段。</summary>
    public static string Format(ulong mapSeed, BoardMapParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.EnsureValid();
        string id = $"{Prefix}:{mapSeed.ToString(CultureInfo.InvariantCulture)}";
        if (parameters.Players != BoardMapParameters.DefaultPlayers)
        {
            id += $":p{parameters.Players.ToString(CultureInfo.InvariantCulture)}";
        }

        if (parameters.BoardCount != BoardMapParameters.DefaultBoardsFor(parameters.Players))
        {
            id += $":n{parameters.BoardCount.ToString(CultureInfo.InvariantCulture)}";
        }

        return id;
    }

    /// <summary>解析完整标识。格式不对、人数或棋盘数越界抛 <see cref="FormatException"/>，消息说明格式与范围。</summary>
    public static (ulong MapSeed, BoardMapParameters Parameters) Parse(string? mapId)
    {
        string id = mapId?.Trim() ?? string.Empty;
        string[] parts = id.Split(':');
        if (parts[0] != Prefix || parts.Length is < 2 or > 4)
        {
            throw new FormatException($"无法解析棋盘图标识 \"{id}\"。{FormatHelp}");
        }

        if (!IsDigits(parts[1]) || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            throw new FormatException($"棋盘图标识 \"{id}\" 的地图种子 \"{parts[1]}\" 不是十进制无符号 64 位整数。{FormatHelp}");
        }

        // 其余段依次只能是 :p（至多一次，且在 :n 之前）与 :n（至多一次）。
        int? players = null;
        int? boards = null;
        for (int i = 2; i < parts.Length; i++)
        {
            string part = parts[i];
            char tag = part.Length > 0 ? part[0] : '\0';
            bool expectP = players is null && boards is null;
            if (!((tag == 'p' && expectP) || (tag == 'n' && boards is null)))
            {
                throw new FormatException($"棋盘图标识 \"{id}\" 的第 {i + 1} 段 \"{part}\" 不合格式（:p 在前、:n 在后，各至多一次）。{FormatHelp}");
            }

            if (part.Length < 2 || !IsDigits(part[1..])
                || !int.TryParse(part[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                throw new FormatException($"棋盘图标识 \"{id}\" 的{(tag == 'p' ? "人数" : "棋盘数")}段 \"{part}\" 写法不对。{FormatHelp}");
            }

            if (tag == 'p')
            {
                players = value;
            }
            else
            {
                boards = value;
            }
        }

        int p = players ?? BoardMapParameters.DefaultPlayers;
        if (p is < BoardMapParameters.MinPlayers or > BoardMapParameters.MaxPlayers)
        {
            throw new FormatException(
                $"棋盘图标识 \"{id}\" 的人数 {p} 超出合法人数 {BoardMapParameters.MinPlayers}–{BoardMapParameters.MaxPlayers}。{FormatHelp}");
        }

        int n = boards ?? BoardMapParameters.DefaultBoardsFor(p);
        if (n < BoardMapParameters.MinBoardsFor(p) || n > BoardMapParameters.MaxBoardsFor(p))
        {
            throw new FormatException(
                $"棋盘图标识 \"{id}\" 的棋盘数 {n} 超出该人数的合法范围 {BoardMapParameters.BoardRangeText(p)}。{FormatHelp}");
        }

        return (seed, new BoardMapParameters { Players = p, BoardCount = n });
    }

    /// <summary>把任一合法写法规范化（<c>board:42:n7</c> → <c>board:42</c>，<c>board:42:p4:n9</c> → <c>board:42:n9</c>）。</summary>
    public static string Normalize(string? mapId)
    {
        (ulong seed, BoardMapParameters parameters) = Parse(mapId);
        return Format(seed, parameters);
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
