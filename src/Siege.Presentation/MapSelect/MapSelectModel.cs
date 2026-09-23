using System.Globalization;
using Siege.Core.Board.Maps;

namespace Siege.Presentation.MapSelect;

/// <summary>
/// 选图清单里的一项：内置图（<see cref="BuiltinId"/> 即其地图标识，<see cref="Title"/> 是目录登记的显示名）或"随机图"（<see cref="BuiltinId"/> 为 <c>null</c>）。
/// </summary>
public sealed record MapOption(string Title, string? BuiltinId)
{
    /// <summary>是否是"随机图"这一项。</summary>
    public bool IsRandom => BuiltinId is null;
}

/// <summary>
/// 开局选图界面的视图模型（map-generator D7，零引擎依赖）：选图面板的状态机。
/// 清单 = 目录的内置表（<see cref="MapCatalog.BuiltinMaps"/>：标识与显示名）+ "随机图"，本类不自带地图清单、也不自带显示名对照表。
/// </summary>
/// <remarks>
/// <para><b>只产出地图标识</b>（<see cref="CurrentId"/>），不解析、不生成地图：调用方拿标识去走三个入口共用的那一份"标识 → 地图"解析，
/// 成功（预览也搭好）后调 <see cref="Accept"/>，失败（如生成器耗尽尝试次数）调 <see cref="RollBack"/> 回到上一张成功的图并显示原因。
/// 改状态的操作返回"标识是否变了"——变了调用方才需要重新解析并重搭预览。</para>
/// <para><b>不读时钟</b>："换一张"的新种子由调用方注入（规则内核与表现层都不取随机种子，取种子只在入口最外层）。</para>
/// <para>规格：openspec/changes/map-generator/specs/map-selection —— Requirement: 开局选图界面</para>
/// </remarks>
public sealed class MapSelectModel
{
    /// <summary>非法种子输入的提示。</summary>
    public const string SeedHelp = "地图种子须为非负整数（十进制数字，最大 18446744073709551615）。";

    private readonly MapOption[] _options;
    private State _current;
    private State _accepted;

    /// <summary>以调用方注入的初始地图种子建模：缺省选中目录的缺省地图；第一次切到"随机图"时用的就是这个种子。</summary>
    public MapSelectModel(ulong initialMapSeed)
    {
        _options = [.. MapCatalog.BuiltinMaps.Select(m => new MapOption(m.Title, m.Id)), new MapOption("随机图", null)];
        int selected = Array.FindIndex(_options, o => o.BuiltinId == MapCatalog.DefaultId);
        _current = new State(Math.Max(selected, 0), initialMapSeed, MapGenParameters.DefaultPlatforms, MapGenParameters.RandomPick.NewSurfaces);
        _accepted = _current;
        SeedText = Invariant(initialMapSeed);
    }

    /// <summary>选项清单，按目录的登记顺序，"随机图"在最后。</summary>
    public IReadOnlyList<MapOption> Options => _options;

    /// <summary>当前选中项的下标。</summary>
    public int SelectedIndex => _current.Index;

    /// <summary>当前是否选中"随机图"。</summary>
    public bool IsRandomSelected => _options[_current.Index].IsRandom;

    /// <summary>随机图的地图种子（选中内置图时保留着，切回来还在）。</summary>
    public ulong MapSeed => _current.Seed;

    /// <summary>随机图的平台数。</summary>
    public int PlatformCount => _current.Platforms;

    /// <summary>种子输入框该显示的文本：操作成功后是当前种子，非法输入后保留用户敲的原文。</summary>
    public string SeedText { get; private set; }

    /// <summary>面板上的提示（非法输入、生成失败）；没有则为空串。</summary>
    public string Notice { get; private set; } = string.Empty;

    /// <summary>是否已确认开局。确认之后不再接受任何操作。</summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>平台数还能不能减。</summary>
    public bool CanDecreasePlatforms => IsRandomSelected && _current.Platforms > MapGenParameters.MinPlatforms;

    /// <summary>平台数还能不能加。</summary>
    public bool CanIncreasePlatforms => IsRandomSelected && _current.Platforms < MapGenParameters.MaxPlatforms;

    /// <summary>当前地图的完整标识（下次可用命令行复现）：内置图即其标识，随机图经标识的唯一实现规范化。</summary>
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
    /// 按地图标识预选一项（仅供截图 / 自检的启动选项）：内置标识选中对应项，完整的生成图标识选中"随机图"并带上其中的种子与平台数。
    /// 其余（未带种子的随机请求、地图文件路径、写错的标识）返回 <c>false</c>、状态不变——选图界面只列目录的内置表与随机图。
    /// </summary>
    public bool TrySelectId(string? mapId)
    {
        EnsureOpen();
        string id = mapId?.Trim() ?? string.Empty;
        int builtin = Array.FindIndex(_options, o => o.BuiltinId == id);
        if (builtin >= 0)
        {
            Move(_current with { Index = builtin });
            return true;
        }

        if (!GeneratedMapId.IsGenerated(id))
        {
            return false;
        }

        try
        {
            (ulong seed, MapGenParameters parameters) = GeneratedMapId.Parse(id);
            Move(new State(_options.Length - 1, seed, parameters.PlatformCount, parameters.NewSurfaces));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// 输入种子并确认。非法输入（不是十进制非负整数，或超出 64 位）给出 <see cref="SeedHelp"/>、当前图不变、输入框保留原文。
    /// 合法性与标识解析同一口径：先要求全是 ASCII 数字，再交给标识的唯一实现解析。
    /// </summary>
    public bool SubmitSeed(string? text)
    {
        EnsureOpen();
        if (!IsRandomSelected)
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

    /// <summary>换一张：种子取调用方注入的 <paramref name="newMapSeed"/>（恰与当前相同则顺延 1，保证真的换了）。</summary>
    public bool Reroll(ulong newMapSeed)
    {
        EnsureOpen();
        if (!IsRandomSelected)
        {
            return false;
        }

        // "换一张"随机出的生成图一律开新地表（terrain-surfaces D7），即使进入时预选的是不带 :s1 的标识。
        return Move(_current with
        {
            Seed = newMapSeed == _current.Seed ? unchecked(newMapSeed + 1UL) : newMapSeed,
            NewSurfaces = MapGenParameters.RandomPick.NewSurfaces,
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

    private string IdOf(State state) =>
        _options[state.Index].BuiltinId ?? GeneratedMapId.Format(state.Seed, new MapGenParameters { PlatformCount = state.Platforms, NewSurfaces = state.NewSurfaces });

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

    /// <summary>选图状态。<paramref name="NewSurfaces"/>：随机图是否开新地表——缺省与"换一张"为开，按标识预选时取标识里的值，输入种子 / 调平台数时保持不变。</summary>
    private readonly record struct State(int Index, ulong Seed, int Platforms, bool NewSurfaces);
}
