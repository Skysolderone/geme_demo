using System.Globalization;
using Siege.Core.Ai;
using Siege.Core.Board.Maps;

namespace Siege.Presentation.MapSelect;

/// <summary>选图清单里一项的类别。</summary>
public enum MapOptionKind
{
    /// <summary>内置图：标识与显示名登记在目录的内置表里。</summary>
    Builtin,

    /// <summary>随机图（边疆档生成器）：标识由地图种子与平台数拼出。</summary>
    Random,

    /// <summary>棋盘图（棋盘档生成器，board-map）：标识由地图种子与棋盘数拼出。</summary>
    Board,
}

/// <summary>
/// 选图清单里的一项：内置图（<see cref="BuiltinId"/> 即其地图标识，<see cref="Title"/> 是目录登记的显示名），
/// 或按种子生成的"随机图" / "棋盘图"（<see cref="BuiltinId"/> 为 <c>null</c>，标识随种子与参数变化）。
/// </summary>
public sealed record MapOption(string Title, string? BuiltinId, MapOptionKind Kind)
{
    /// <summary>是否是"随机图"这一项。</summary>
    public bool IsRandom => Kind == MapOptionKind.Random;

    /// <summary>是否是"棋盘图"这一项。</summary>
    public bool IsBoard => Kind == MapOptionKind.Board;
}

/// <summary>
/// 开局选图界面的视图模型（map-generator D7，零引擎依赖）：选图面板的状态机。
/// 清单 = "棋盘图" + 目录的内置表（<see cref="MapCatalog.BuiltinMaps"/>：标识与显示名）+ "随机图"，本类不自带地图清单、也不自带显示名对照表。
/// </summary>
/// <remarks>
/// <para><b>只产出地图标识</b>（<see cref="CurrentId"/>），不解析、不生成地图：调用方拿标识去走三个入口共用的那一份"标识 → 地图"解析，
/// 成功（预览也搭好）后调 <see cref="Accept"/>，失败（如生成器耗尽尝试次数）调 <see cref="RollBack"/> 回到上一张成功的图并显示原因。
/// 改状态的操作返回"标识是否变了"——变了调用方才需要重新解析并重搭预览。</para>
/// <para><b>不读时钟</b>："换一张"的新种子由调用方注入（规则内核与表现层都不取随机种子，取种子只在入口最外层）。</para>
/// <para><b>预选项是棋盘图</b>（board-map D9）：只有图形版选图界面如此；批量 / 终端入口与无人值守演示的缺省地图仍由目录决定，本类不碰。</para>
/// <para>规格：openspec/changes/map-generator/specs/map-selection、openspec/changes/board-map/specs/map-selection —— Requirement: 开局选图界面</para>
/// </remarks>
public sealed class MapSelectModel
{
    /// <summary>非法种子输入的提示。</summary>
    public const string SeedHelp = "地图种子须为非负整数（十进制数字，最大 18446744073709551615）。";

    private readonly MapOption[] _options;
    private readonly int _boardIndex;
    private readonly int _randomIndex;
    private State _current;
    private State _accepted;
    private AiDifficulty _difficulty;

    /// <summary>以调用方注入的初始地图种子建模：预选棋盘图，用的就是这个种子；切到"随机图"时也用它。</summary>
    /// <param name="difficulty">难度的预选（expert-lookahead D10：图形版 <c>--difficulty=</c>），缺省标准。</param>
    public MapSelectModel(ulong initialMapSeed, AiDifficulty difficulty = AiDifficulty.Standard)
    {
        _difficulty = Defined(difficulty);
        _options =
        [
            new MapOption("棋盘图", null, MapOptionKind.Board),
            .. MapCatalog.BuiltinMaps.Select(m => new MapOption(m.Title, m.Id, MapOptionKind.Builtin)),
            new MapOption("随机图（边疆档）", null, MapOptionKind.Random),
        ];
        _boardIndex = Array.FindIndex(_options, o => o.IsBoard);
        _randomIndex = Array.FindIndex(_options, o => o.IsRandom);
        _current = new State(
            _boardIndex, initialMapSeed, MapGenParameters.DefaultPlatforms, MapGenParameters.RandomPick.NewSurfaces, BoardMapParameters.DefaultBoards);
        _accepted = _current;
        SeedText = Invariant(initialMapSeed);
    }

    /// <summary>选项清单：棋盘图在最前，内置图按目录的登记顺序，"随机图"在最后。</summary>
    public IReadOnlyList<MapOption> Options => _options;

    /// <summary>当前选中项的下标。</summary>
    public int SelectedIndex => _current.Index;

    /// <summary>当前是否选中"随机图"。</summary>
    public bool IsRandomSelected => _options[_current.Index].IsRandom;

    /// <summary>当前是否选中"棋盘图"。</summary>
    public bool IsBoardSelected => _options[_current.Index].IsBoard;

    /// <summary>当前选中的是否是按种子生成的图（随机图或棋盘图）：种子输入与"换一张"只对它们起作用。</summary>
    public bool IsSeededSelected => IsRandomSelected || IsBoardSelected;

    /// <summary>地图种子，随机图与棋盘图共用（选中内置图时保留着，切回来还在）。</summary>
    public ulong MapSeed => _current.Seed;

    /// <summary>随机图的平台数。</summary>
    public int PlatformCount => _current.Platforms;

    /// <summary>棋盘图的棋盘数。</summary>
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

    /// <summary>平台数还能不能减。</summary>
    public bool CanDecreasePlatforms => IsRandomSelected && _current.Platforms > MapGenParameters.MinPlatforms;

    /// <summary>平台数还能不能加。</summary>
    public bool CanIncreasePlatforms => IsRandomSelected && _current.Platforms < MapGenParameters.MaxPlatforms;

    /// <summary>棋盘数还能不能减。</summary>
    public bool CanDecreaseBoards => IsBoardSelected && _current.Boards > BoardMapParameters.MinBoards;

    /// <summary>棋盘数还能不能加。</summary>
    public bool CanIncreaseBoards => IsBoardSelected && _current.Boards < BoardMapParameters.MaxBoards;

    /// <summary>当前地图的完整标识（下次可用命令行复现）：内置图即其标识，随机图与棋盘图经各自标识的唯一实现规范化。</summary>
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
    /// 按地图标识预选一项（仅供截图 / 自检的启动选项）：内置标识选中对应项；完整的生成图标识选中"随机图"并带上其中的种子与平台数；
    /// 完整的棋盘图标识选中"棋盘图"并带上其中的种子与棋盘数。
    /// 其余（未带种子的随机请求、地图文件路径、写错的标识）返回 <c>false</c>、状态不变——选图界面只列棋盘图、目录的内置表与随机图。
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

        try
        {
            if (GeneratedMapId.IsGenerated(id))
            {
                (ulong seed, MapGenParameters parameters) = GeneratedMapId.Parse(id);
                Move(_current with { Index = _randomIndex, Seed = seed, Platforms = parameters.PlatformCount, NewSurfaces = parameters.NewSurfaces });
                return true;
            }

            if (BoardMapId.IsBoardMap(id))
            {
                (ulong seed, BoardMapParameters parameters) = BoardMapId.Parse(id);
                Move(_current with { Index = _boardIndex, Seed = seed, Boards = parameters.BoardCount });
                return true;
            }
        }
        catch (FormatException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// 输入种子并确认（随机图与棋盘图）。非法输入（不是十进制非负整数，或超出 64 位）给出 <see cref="SeedHelp"/>、当前图不变、输入框保留原文。
    /// 合法性与标识解析同一口径：先要求全是 ASCII 数字，再交给标识的唯一实现解析（两种标识的种子段写法相同）。
    /// </summary>
    public bool SubmitSeed(string? text)
    {
        EnsureOpen();
        if (!IsSeededSelected)
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

            seed = GeneratedMapId.Parse($"{GeneratedMapId.Prefix}:{digits}").MapSeed;
        }
        catch (FormatException)
        {
            SeedText = typed;
            Notice = SeedHelp;
            return false;
        }

        return Move(_current with { Seed = seed });
    }

    /// <summary>换一张（随机图与棋盘图）：种子取调用方注入的 <paramref name="newMapSeed"/>（恰与当前相同则顺延 1，保证真的换了）。</summary>
    public bool Reroll(ulong newMapSeed)
    {
        EnsureOpen();
        if (!IsSeededSelected)
        {
            return false;
        }

        // "换一张"随机出的生成图一律开新地表（terrain-surfaces D7），即使进入时预选的是不带 :s1 的标识。棋盘图没有这个开关，保持原值不影响其标识。
        return Move(_current with
        {
            Seed = newMapSeed == _current.Seed ? unchecked(newMapSeed + 1UL) : newMapSeed,
            NewSurfaces = IsRandomSelected ? MapGenParameters.RandomPick.NewSurfaces : _current.NewSurfaces,
        });
    }

    /// <summary>平台数加减；到边界不再变化（不夹取越界值，按钮此时应禁用）。</summary>
    public bool AdjustPlatforms(int delta)
    {
        EnsureOpen();
        int next = _current.Platforms + delta;
        if (!IsRandomSelected || next is < MapGenParameters.MinPlatforms or > MapGenParameters.MaxPlatforms)
        {
            return false;
        }

        return Move(_current with { Platforms = next });
    }

    /// <summary>棋盘数加减（board-map D7：7–10）；到边界不再变化（不夹取越界值，按钮此时应禁用）。调整后按当前种子重新生成。</summary>
    public bool AdjustBoards(int delta)
    {
        EnsureOpen();
        int next = _current.Boards + delta;
        if (!IsBoardSelected || next is < BoardMapParameters.MinBoards or > BoardMapParameters.MaxBoards)
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
        MapOptionKind.Board => BoardMapId.Format(state.Seed, new BoardMapParameters { BoardCount = state.Boards }),
        MapOptionKind.Random => GeneratedMapId.Format(state.Seed, new MapGenParameters { PlatformCount = state.Platforms, NewSurfaces = state.NewSurfaces }),
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

    /// <summary>
    /// 选图状态。<paramref name="NewSurfaces"/>：随机图是否开新地表——缺省与"换一张"为开，按标识预选时取标识里的值，输入种子 / 调平台数时保持不变。
    /// <paramref name="Boards"/>：棋盘图的棋盘数。
    /// </summary>
    private readonly record struct State(int Index, ulong Seed, int Platforms, bool NewSurfaces, int Boards);
}
