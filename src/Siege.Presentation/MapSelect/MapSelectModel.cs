using System.Globalization;
using Siege.Core.Ai;
using Siege.Core.Board.Maps;

namespace Siege.Presentation.MapSelect;

/// <summary>
/// 新局界面的视图模型（board-link-mode D8 / 实现裁决 D7，tasks 4.1；零引擎依赖）：整个游戏只剩链接模式一种地图 <c>link:&lt;地图种子&gt;:&lt;人数&gt;</c>，
/// 界面只有"当前地图种子 + 完整标识、种子输入、换一张、人数（map-expand-25 D1：2 / 3 / 4，缺省 4）、AI 难度、开始"。旧地图清单与显示名对照表已删除（规格 map-selection「开局选图界面」）。
/// </summary>
/// <remarks>
/// <para><b>只产出地图标识</b>（<see cref="CurrentId"/>），不解析、不生成地图：调用方拿标识去走三个入口共用的那一份"标识 → 地图"解析（<c>MapCatalog.Resolve</c>），
/// 成功（预览也搭好）后调 <see cref="Accept"/>，失败调 <see cref="RollBack"/> 回到上一张成功的图并显示原因。改状态的操作返回"标识是否变了"——变了调用方才需要重新解析并重搭预览。</para>
/// <para><b>不读时钟</b>："换一张"的新种子由调用方注入（规则内核与表现层都不取随机种子，取种子只在入口最外层）。</para>
/// <para>批量 / 终端入口的缺省地图由目录决定（<c>MapCatalog.DefaultId</c> = <c>link:1:4</c>），本类不碰。</para>
/// <para>人数是标识的一部分（浮空岛按人数生成）：改人数与换种子一样是"标识变了"，调用方同样重新解析并重搭预览；回退（<see cref="RollBack"/>）把种子与人数一起退回上一张成功的图。</para>
/// <para>规格：openspec/changes/board-link-mode/specs/map-selection —— Requirement: 开局选图界面</para>
/// </remarks>
public sealed class MapSelectModel
{
    /// <summary>非法种子输入的提示。</summary>
    public const string SeedHelp = "地图种子须为非负整数（十进制数字，最大 18446744073709551615）。";

    /// <summary>人数的缺省（map-expand-25 D1：新局界面缺省 4 人）。</summary>
    public const int DefaultPlayers = 4;

    private ulong _current;
    private int _players;
    private ulong _accepted;
    private int _acceptedPlayers;
    private AiDifficulty _difficulty;

    /// <summary>以调用方注入的初始地图种子建模（"已取好一个地图种子"），人数为 <see cref="DefaultPlayers"/>。</summary>
    /// <param name="difficulty">难度的预选（expert-lookahead D10：图形版 <c>--difficulty=</c>），缺省标准。</param>
    public MapSelectModel(ulong initialMapSeed, AiDifficulty difficulty = AiDifficulty.Standard)
    {
        _difficulty = Defined(difficulty);
        _current = initialMapSeed;
        _accepted = initialMapSeed;
        _players = DefaultPlayers;
        _acceptedPlayers = DefaultPlayers;
        SeedText = Invariant(initialMapSeed);
    }

    /// <summary>当前地图种子。</summary>
    public ulong MapSeed => _current;

    /// <summary>当前人数（2–4；开局人数 = 地图人数 = 本值）。</summary>
    public int Players => _players;

    /// <summary>人数选择的选项：2 / 3 / 4（链接图标识人数段的全部取值）。</summary>
    public static IReadOnlyList<int> PlayerOptions { get; } = [.. Enumerable.Range(LinkMapId.MinPlayers, LinkMapId.MaxPlayers - LinkMapId.MinPlayers + 1)];

    /// <summary>当前地图的完整标识 <c>link:&lt;种子&gt;:&lt;人数&gt;</c>（下次可用命令行 <c>--map=</c> 复现），经链接图标识的唯一实现拼出。</summary>
    public string CurrentId => LinkMapId.Format(_current, _players);

    /// <summary>种子输入框该显示的文本：操作成功后是当前种子，非法输入后保留用户敲的原文。</summary>
    public string SeedText { get; private set; }

    /// <summary>面板上的提示（非法输入、生成失败）；没有则为空串。</summary>
    public string Notice { get; private set; } = string.Empty;

    /// <summary>是否已确认开局。确认之后不再接受任何操作。</summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>难度选择的选项：四档，按枚举次序（显示名见 <c>Labels.Difficulty</c>）。</summary>
    public static IReadOnlyList<AiDifficulty> DifficultyOptions { get; } = Enum.GetValues<AiDifficulty>();

    /// <summary>所选难度（开局时交给建局）。</summary>
    public AiDifficulty Difficulty => _difficulty;

    /// <summary>选择难度。不影响地图标识与预览；确认开局后不再接受。</summary>
    public void SelectDifficulty(AiDifficulty difficulty)
    {
        EnsureOpen();
        _difficulty = Defined(difficulty);
    }

    /// <summary>
    /// 选择人数（2 / 3 / 4）：人数是标识的一部分，返回"标识是否变了"——变了调用方重新解析并重搭预览（同 <see cref="SubmitSeed"/>）。
    /// 不在 2–4 抛 <see cref="ArgumentOutOfRangeException"/>；确认开局后不再接受。
    /// </summary>
    public bool SelectPlayers(int players)
    {
        EnsureOpen();
        if (!PlayerOptions.Contains(players))
        {
            throw new ArgumentOutOfRangeException(nameof(players), players, $"人数须为 {LinkMapId.MinPlayers}–{LinkMapId.MaxPlayers}。");
        }

        return Move(_current, players);
    }

    /// <summary>
    /// 按地图标识预选（仅供截图 / 自检的启动选项 <c>--map-select --map=link:&lt;种子&gt;:&lt;人数&gt;</c>）：完整的链接图标识带上其中的种子与人数，返回 <c>true</c>。
    /// 其余一切（裸 <c>link</c>、旧标识族、地图文件路径、写错的标识）返回 <c>false</c>、状态不变——新局界面只有链接图。
    /// </summary>
    public bool TrySelectId(string? mapId)
    {
        EnsureOpen();
        if (!LinkMapId.IsLinkMap(mapId))
        {
            return false;
        }

        try
        {
            // 裸 link（没有种子）、已作废的两段写法与写错的标识都由唯一实现的 Parse 抛出 FormatException → 拒绝。
            (ulong seed, int players) = LinkMapId.Parse(mapId);
            Move(seed, players);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// 输入种子并确认。非法输入（不是十进制非负整数，或超出 64 位）给出 <see cref="SeedHelp"/>、当前图不变、输入框保留原文。
    /// 合法性与标识解析同一口径：先要求全是 ASCII 数字，再交给链接图标识的唯一实现解析。
    /// </summary>
    public bool SubmitSeed(string? text)
    {
        EnsureOpen();
        string typed = text ?? string.Empty;
        string digits = typed.Trim();
        ulong seed;
        try
        {
            if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            {
                throw new FormatException();
            }

            seed = LinkMapId.Parse($"{LinkMapId.Prefix}:{digits}:{_players}").Seed;
        }
        catch (FormatException)
        {
            SeedText = typed;
            Notice = SeedHelp;
            return false;
        }

        return Move(seed, _players);
    }

    /// <summary>换一张（人数不变）：种子取调用方注入的 <paramref name="newMapSeed"/>（恰与当前相同则顺延 1，保证真的换了）。</summary>
    public bool Reroll(ulong newMapSeed)
    {
        EnsureOpen();
        return Move(newMapSeed == _current ? unchecked(newMapSeed + 1UL) : newMapSeed, _players);
    }

    /// <summary>调用方已按 <see cref="CurrentId"/> 解析出地图并搭好预览：当前状态成为"上一张成功的图"。</summary>
    public void Accept() => (_accepted, _acceptedPlayers) = (_current, _players);

    /// <summary>调用方解析 / 搭预览失败：回到上一张成功的图（种子与人数一起），面板显示 <paramref name="message"/>。</summary>
    public void RollBack(string message)
    {
        _current = _accepted;
        _players = _acceptedPlayers;
        SeedText = Invariant(_current);
        Notice = message ?? string.Empty;
    }

    /// <summary>确认开局：给出<b>已接受</b>的地图标识（尚未搭成预览的候选不会被带进对局）。此后本模型不再接受操作。</summary>
    public string Confirm()
    {
        EnsureOpen();
        _current = _accepted;
        _players = _acceptedPlayers;
        SeedText = Invariant(_current);
        IsConfirmed = true;
        return CurrentId;
    }

    private static AiDifficulty Defined(AiDifficulty difficulty) =>
        Enum.IsDefined(difficulty) ? difficulty : throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未知难度。");

    private static string Invariant(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>状态迁移的唯一出口：清提示、同步输入框文本，返回标识是否变了（种子或人数）。</summary>
    private bool Move(ulong seed, int players)
    {
        bool changed = seed != _current || players != _players;
        _current = seed;
        _players = players;
        SeedText = Invariant(seed);
        Notice = string.Empty;
        return changed;
    }

    private void EnsureOpen()
    {
        if (IsConfirmed)
        {
            throw new InvalidOperationException("已确认开局，新局界面不再接受操作。");
        }
    }
}
