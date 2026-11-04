using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Presentation.Text;

namespace Siege.Presentation.MapSelect;

/// <summary>
/// 新局面板上在大陆缩略里点格插旗的状态（relic-sites 裁决 1–4）：悬停格、本机选中格、"开始"按钮的可用性与文案。零引擎依赖。
/// 合规与否、不合规的原因只问 Core 的唯一实现（<see cref="FlagRules.FlagCellProblem"/>），本类不复刻任何判据；像素 ↔ 格的换算不在这里（引擎侧 <c>BoardGeometry</c> 那一份映射）。
/// 只对大图基图启用：小图（旧模式）与锁定后的图 <see cref="Enabled"/> 为假，"开始"照旧可用、不显示任何提示。
/// </summary>
public sealed class FlagPickModel
{
    private MapData? _map;
    private HashSet<Coord> _legal = [];

    /// <summary>当前绑定的预览地图是不是大图基图（只有它能在面板上插旗）。</summary>
    public bool Enabled => _map is { Tier: MapTier.Large } && _map.BirthZones.IsDefaultOrEmpty;

    /// <summary>悬停格（引擎侧按像素换算后交来）；不启用或指针不在缩略上为 <c>null</c>。</summary>
    public Coord? Hover { get; private set; }

    /// <summary>本机选中的旗格（点下即判合规，只记合规的）；未选为 <c>null</c>。</summary>
    public Coord? Picked { get; private set; }

    /// <summary>最近一次点格不合规的原因（Core 文案）；合规或未点过为空串。</summary>
    public string Notice { get; private set; } = string.Empty;

    /// <summary>
    /// 可插旗遮罩（relic-zones 裁决 1：不可插格整层压暗）：下标 = y × <see cref="MaskWidth"/> + x，真即合规旗格（Core <see cref="FlagRules.LegalFlagCells"/> 那一份）。
    /// 按地图标识算一次缓存（150×150 不能每次悬停重算）；不启用时为空。
    /// </summary>
    public ImmutableArray<bool> LegalMask { get; private set; } = [];

    /// <summary>遮罩宽（= 地图画布宽）；不启用为 0。</summary>
    public int MaskWidth { get; private set; }

    /// <summary>遮罩高（= 地图画布高）；不启用为 0。</summary>
    public int MaskHeight { get; private set; }

    /// <summary>稀有遗址区域（relic-zones 裁决 2，Core <see cref="RelicSites.RareZones(MapData)"/>）：缩略只画区域、不画稀有遗址的精确格。不启用时为空。</summary>
    public ImmutableArray<RareSiteZone> RareZones { get; private set; } = [];

    /// <summary>旗心到 13×13 构筑范围边的格数（画框用，取 Core 常量）。</summary>
    public static int PlateReach => FlagRules.PlateReach;

    /// <summary>
    /// 绑定当前预览的地图（裁决 4）：标识变了（换种子 / 人数 / 尺寸）即清空选中格、悬停与提示；同一张图（生成失败回滚）不清。传 <c>null</c> 即解除绑定（建局之后）。
    /// </summary>
    public void Bind(MapData? map)
    {
        if (!string.Equals(map?.Id, _map?.Id, StringComparison.Ordinal))
        {
            Picked = null;
            Hover = null;
            Notice = string.Empty;
            _map = map;
            RebuildMask();
        }

        _map = map;
    }

    /// <summary>换图时重算遮罩与区域；小图 / 解绑清空。</summary>
    private void RebuildMask()
    {
        if (!Enabled)
        {
            _legal = [];
            LegalMask = [];
            MaskWidth = 0;
            MaskHeight = 0;
            RareZones = [];
            return;
        }

        MapData map = _map!;
        _legal = [.. FlagRules.LegalFlagCells(map)];
        var mask = new bool[map.Width * map.Height];
        foreach (Coord c in _legal)
        {
            mask[(c.Y * map.Width) + c.X] = true;
        }

        LegalMask = ImmutableArray.Create(mask);
        MaskWidth = map.Width;
        MaskHeight = map.Height;
        RareZones = RelicSites.RareZones(map);
    }

    /// <summary>该格在不在某个稀有遗址区域里（relic-zones 裁决 7）。</summary>
    public bool InRareZone(Coord cell) => RareZones.Any(z => z.Contains(cell));

    /// <summary>某格不合规的原因；合规为 <c>null</c>。不启用时给出"不是大图基图"的原因。</summary>
    public string? ProblemOf(Coord cell) => _map is null ? "尚未选定地图。" : FlagRules.FlagCellProblem(_map, cell);

    /// <summary>某格此刻能否插旗（只在启用时可能为真）。</summary>
    public bool IsLegal(Coord cell) => Enabled && _legal.Contains(cell);

    /// <summary>悬停到某格（或离开缩略）。</summary>
    public void SetHover(Coord? cell) => Hover = Enabled ? cell : null;

    /// <summary>点某格：合规即记为选中格并清空提示，返回 <c>true</c>；不合规只把原因写进 <see cref="Notice"/>、选中格不变。</summary>
    public bool Pick(Coord cell)
    {
        if (!Enabled)
        {
            return false;
        }

        if (ProblemOf(cell) is { } problem)
        {
            Notice = problem;
            return false;
        }

        Picked = cell;
        Notice = string.Empty;
        return true;
    }

    /// <summary>
    /// 某格若是公开的信物遗址，给出档位；否则 <c>null</c>（裁决 10：悬停显示"普通信物遗址"）。
    /// 稀有遗址不公开精确格（relic-zones 裁决 2 / 7），这里一律不报——悬停只给 <see cref="Labels.RareZone"/>。
    /// </summary>
    public RelicTier? SiteAt(Coord cell)
    {
        if (_map is null || _map.RelicSites.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (RelicSite site in _map.RelicSites)
        {
            if (site.Cell == cell && site.Tier == RelicTier.Common)
            {
                return site.Tier;
            }
        }

        return null;
    }

    /// <summary>某格的一行读数：坐标 + 合规 / 不合规原因，若是普通遗址再加档位名；在稀有遗址区域里而原因没提区域时补一句区域说明（不带遗址坐标）。</summary>
    public string Readout(Coord cell)
    {
        string verdict = ProblemOf(cell) is { } problem ? problem : "合规，点此插旗";
        string site = SiteAt(cell) is { } tier ? $"；{Labels.RelicSite(tier)}"
            : InRareZone(cell) && !verdict.Contains("稀有信物遗址区域", StringComparison.Ordinal) ? $"；{Labels.RareZone}"
            : string.Empty;
        return $"{cell.ToNotation()}：{verdict}{site}";
    }

    /// <summary>缩略下方的一行：悬停格的读数；没有悬停时是选中格的确认或"先点一格"的提示；不启用为空串。</summary>
    public string HoverReadout =>
        !Enabled ? string.Empty
        : Hover is { } hover ? Readout(hover)
        : Notice.Length > 0 ? Notice
        : Picked is { } picked ? $"已选 {picked.ToNotation()}，点「{Labels.PlantAndStart}」开局；再点别的格可改选"
        : Labels.PickFlagHint;

    /// <summary>"开始"能不能按（裁决 3）：大图须先选格；小图照旧。</summary>
    public bool CanStart => !Enabled || Picked is not null;

    /// <summary>"开始"按钮的文案（裁决 3）：大图选中格后是"插旗并开始"。</summary>
    public string StartText => Enabled && Picked is not null ? Labels.PlantAndStart : "开始";

    /// <summary>"开始"旁的提示（裁决 3）：大图未选格时提示先点一格；其余为空串。</summary>
    public string StartHint => Enabled && Picked is null ? Labels.PickFlagHint : string.Empty;
}
