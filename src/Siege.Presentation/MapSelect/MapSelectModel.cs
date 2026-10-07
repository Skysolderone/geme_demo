using System.Globalization;
using Siege.Core.Ai;
using Siege.Core.Board.Maps;

namespace Siege.Presentation.MapSelect;

/// <summary>选图清单里一项的类别。</summary>
public enum MapOptionKind
{
    /// <summary>内置棋盘图：标识与显示名登记在目录的内置棋盘图表里。</summary>
    Builtin,

    /// <summary>随机棋盘图（棋盘档生成器）：标识由地图种子、人数与棋盘数拼出。</summary>
    Board,
}

/// <summary>
/// 选图清单里的一项：内置棋盘图（<see cref="BuiltinId"/> 即其地图标识，<see cref="Title"/> 是目录登记的显示名），
/// 或按种子生成的"随机棋盘图"（<see cref="BuiltinId"/> 为 <c>null</c>，标识随种子与参数变化）。
/// </summary>
public sealed record MapOption(string Title, string? BuiltinId, MapOptionKind Kind)
{
    /// <summary>是否是"随机棋盘图"这一项。</summary>
    public bool IsBoard => Kind == MapOptionKind.Board;
}

/// <summary>
/// 开局选图界面的视图模型（map-generator D7，零引擎依赖）：选图面板的状态机。
/// 清单 = 目录的内置棋盘图表（<see cref="MapCatalog.BuiltinBoards"/>：标识与显示名）+ "随机棋盘图"，本类不自带地图清单、也不自带显示名对照表。
/// </summary>
/// <remarks>
/// <para><b>只产出地图标识</b>（<see cref="CurrentId"/>），不解析、不生成地图：调用方拿标识去走三个入口共用的那一份"标识 → 地图"解析，
/// 成功（预览也搭好）后调 <see cref="Accept"/>，失败（如生成器耗尽尝试次数）调 <see cref="RollBack"/> 回到上一张成功的图并显示原因。
/// 改状态的操作返回"标识是否变了"——变了调用方才需要重新解析并重搭预览。</para>
/// <para><b>不读时钟</b>："换一张"的新种子由调用方注入（规则内核与表现层都不取随机种子，取种子只在入口最外层）。</para>
/// <para><b>预选项是目录的缺省地图</b>（builtin-board-maps D5：4 人内置棋盘图）。清单之外的只有地图文件（可用命令行直接建局）；旧图与 <c>gen:</c> 生成图已于 retire-legacy-maps 删除。</para>
/// <para>规格：openspec/changes/builtin-board-maps/specs/map-selection —— Requirement: 开局选图界面</para>
/// </remarks>
public sealed class MapSelectModel
{
    /// <summary>非法种子输入的提示。</summary>
    public const string SeedHelp = "地图种子须为非负整数（十进制数字，最大 18446744073709551615）。";

    private readonly MapOption[] _options;
    private readonly int _boardIndex;
    private State _current;
    private State _accepted;
    private AiDifficulty _difficulty;

    /// <summary>以调用方注入的初始地图种子建模：预选目录的缺省地图（4 人内置棋盘图）；切到"随机棋盘图"时用这个种子、4 人、缺省棋盘数。</summary>
    /// <param name="difficulty">难度的预选（expert-lookahead D10：图形版 <c>--difficulty=</c>），缺省标准。</param>
    public MapSelectModel(ulong initialMapSeed, AiDifficulty difficulty = AiDifficulty.Standard)
    {
        _difficulty = Defined(difficulty);
        _options =
        [
            .. MapCatalog.BuiltinBoards.Select(b => new MapOption(b.Title, b.Id, MapOptionKind.Builtin)),
            new MapOption("随机棋盘图", null, MapOptionKind.Board),
        ];
        _boardIndex = Array.FindIndex(_options, o => o.IsBoard);
        int preselected = Array.FindIndex(_options, o => o.BuiltinId == MapCatalog.DefaultId);
        if (preselected < 0)
        {
            throw new InvalidOperationException($"目录的缺省地图 {MapCatalog.DefaultId} 不是内置棋盘图，选图界面没有对应的预选项。");
        }

        _current = new State(
            preselected, initialMapSeed, BoardMapParameters.DefaultPlayers, BoardMapParameters.DefaultBoardsFor(BoardMapParameters.DefaultPlayers));
        _accepted = _current;
        SeedText = Invariant(initialMapSeed);
    }

    /// <summary>选项清单：内置棋盘图按目录的登记顺序，"随机棋盘图"在最后。</summary>
    public IReadOnlyList<MapOption> Options => _options;

    /// <summary>当前选中项的下标。</summary>
    public int SelectedIndex => _current.Index;

    /// <summary>当前是否选中"随机棋盘图"：种子输入、"换一张"、人数与棋盘数只对它起作用。</summary>
    public bool IsBoardSelected => _options[_current.Index].IsBoard;

    /// <summary>随机棋盘图的地图种子（选中内置棋盘图时保留着，切回来还在）。</summary>
    public ulong MapSeed => _current.Seed;

    /// <summary>随机棋盘图的人数（2–4）。</summary>
    public int Players => _current.Players;

    /// <summary>随机棋盘图的棋盘数（范围随人数）。</summary>
    public int BoardCount => _current.Boards;

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

    private static AiDifficulty Defined(AiDifficulty difficulty) =>
        Enum.IsDefined(difficulty) ? difficulty : throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未知难度。");

    /// <summary>人数还能不能减。</summary>
    public bool CanDecreasePlayers => IsBoardSelected && _current.Players > BoardMapParameters.MinPlayers;

    /// <summary>人数还能不能加。</summary>
    public bool CanIncreasePlayers => IsBoardSelected && _current.Players < BoardMapParameters.MaxPlayers;

    /// <summary>棋盘数还能不能减（下限随人数）。</summary>
    public bool CanDecreaseBoards => IsBoardSelected && _current.Boards > BoardMapParameters.MinBoardsFor(_current.Players);

    /// <summary>棋盘数还能不能加（上限随人数）。</summary>
    public bool CanIncreaseBoards => IsBoardSelected && _current.Boards < BoardMapParameters.MaxBoardsFor(_current.Players);

    /// <summary>当前地图的完整标识（下次可用命令行复现）：内置棋盘图即其内置名，随机棋盘图经棋盘图标识的唯一实现规范化。</summary>
    public string CurrentId => IdOf(_current);

    /// <summary>选中第 <paramref name="index"/> 项。</summary>
    public bool Select(int index)
    {
        EnsureOpen();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _options.Length);
        return Move(_current with { Index = index });
    }

    /// <summary>
    /// 按地图标识预选一项（仅供截图 / 自检的启动选项）：内置棋盘图的内置名选中对应项；完整的棋盘图标识选中"随机棋盘图"并带上其中的种子、人数与棋盘数。
    /// 其余（已删除的旧标识、未带种子的随机请求、地图文件路径、写错的标识）返回 <c>false</c>、状态不变——它们不在选图界面的清单上。
    /// </summary>
    public bool TrySelectId(string? mapId)
    {
        EnsureOpen();
        string id = mapId?.Trim() ?? string.Empty;
        int builtin = Array.FindIndex(_options, o => o.BuiltinId is not null && o.BuiltinId == id);
        if (builtin >= 0)
        {
            Move(_current with { Index = builtin });
            return true;
        }

        if (!BoardMapId.IsBoardMap(id))
        {
            return false;
        }

        try
        {
            (ulong seed, BoardMapParameters parameters) = BoardMapId.Parse(id);
            Move(new State(_boardIndex, seed, parameters.Players, parameters.BoardCount));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// 输入种子并确认（随机棋盘图）。非法输入（不是十进制非负整数，或超出 64 位）给出 <see cref="SeedHelp"/>、当前图不变、输入框保留原文。
    /// 合法性与标识解析同一口径：先要求全是 ASCII 数字，再交给棋盘图标识的唯一实现解析种子段。
    /// </summary>
    public bool SubmitSeed(string? text)
    {
        EnsureOpen();
        if (!IsBoardSelected)
        {
            return false;
        }

        string typed = text ?? string.Empty;
        string digits = typed.Trim();
        ulong seed;
        try
        {
            if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            {
                throw new FormatException();
            }

            seed = BoardMapId.Parse($"{BoardMapId.Prefix}:{digits}").MapSeed;
        }
        catch (FormatException)
        {
            SeedText = typed;
            Notice = SeedHelp;
            return false;
        }

        return Move(_current with { Seed = seed });
    }

    /// <summary>换一张（随机棋盘图）：种子取调用方注入的 <paramref name="newMapSeed"/>（恰与当前相同则顺延 1，保证真的换了）；人数与棋盘数不变。</summary>
    public bool Reroll(ulong newMapSeed)
    {
        EnsureOpen();
        if (!IsBoardSelected)
        {
            return false;
        }

        return Move(_current with { Seed = newMapSeed == _current.Seed ? unchecked(newMapSeed + 1UL) : newMapSeed });
    }

    /// <summary>
    /// 人数加减（2–4）；到边界不再变化（不夹取越界值，按钮此时应禁用）。调整后棋盘数重置为该人数的缺省值，按当前种子重新生成。
    /// </summary>
    public bool AdjustPlayers(int delta)
    {
        EnsureOpen();
        int next = _current.Players + delta;
        if (!IsBoardSelected || next is < BoardMapParameters.MinPlayers or > BoardMapParameters.MaxPlayers)
        {
            return false;
        }

        return Move(_current with { Players = next, Boards = BoardMapParameters.DefaultBoardsFor(next) });
    }

    /// <summary>棋盘数加减（范围随人数：4 人 7–10、3 人 5–8、2 人 4–5）；到边界不再变化（不夹取越界值，按钮此时应禁用）。调整后按当前种子重新生成。</summary>
    public bool AdjustBoards(int delta)
    {
        EnsureOpen();
        int next = _current.Boards + delta;
        if (!IsBoardSelected || next < BoardMapParameters.MinBoardsFor(_current.Players) || next > BoardMapParameters.MaxBoardsFor(_current.Players))
        {
            return false;
        }

        return Move(_current with { Boards = next });
    }

    /// <summary>调用方已按 <see cref="CurrentId"/> 解析出地图并搭好预览：当前状态成为"上一张成功的图"。</summary>
    public void Accept() => _accepted = _current;

    /// <summary>调用方解析 / 搭预览失败：回到上一张成功的图，面板显示 <paramref name="message"/>。</summary>
    public void RollBack(string message)
    {
        _current = _accepted;
        SeedText = Invariant(_current.Seed);
        Notice = message ?? string.Empty;
    }

    /// <summary>确认开局：给出<b>已接受</b>的地图标识（尚未搭成预览的候选不会被带进对局）。此后本模型不再接受操作。</summary>
    public string Confirm()
    {
        EnsureOpen();
        _current = _accepted;
        SeedText = Invariant(_current.Seed);
        IsConfirmed = true;
        return CurrentId;
    }

    private static string Invariant(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    private string IdOf(State state) => _options[state.Index].Kind switch
    {
        MapOptionKind.Board => BoardMapId.Format(state.Seed, new BoardMapParameters { Players = state.Players, BoardCount = state.Boards }),
        _ => _options[state.Index].BuiltinId!,
    };

    /// <summary>状态迁移的唯一出口：清提示、同步输入框文本，返回标识是否变了。</summary>
    private bool Move(State next)
    {
        string before = CurrentId;
        _current = next;
        SeedText = Invariant(next.Seed);
        Notice = string.Empty;
        return CurrentId != before;
    }

    private void EnsureOpen()
    {
        if (IsConfirmed)
        {
            throw new InvalidOperationException("已确认开局，选图界面不再接受操作。");
        }
    }

    /// <summary>选图状态。<paramref name="Players"/> / <paramref name="Boards"/>：随机棋盘图的人数与棋盘数（选中内置棋盘图时保留着）。</summary>
    private readonly record struct State(int Index, ulong Seed, int Players, int Boards);
}
