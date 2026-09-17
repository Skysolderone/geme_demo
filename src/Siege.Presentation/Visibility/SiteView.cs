using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Presentation.Text;

namespace Siege.Presentation.Visibility;

/// <summary>
/// 一个据点的公开视图（site-control「据点公开」、information-visibility「据点控制公开」、scoring-sites 4.1）：
/// 位置、档位、当前分值配置、控制状态与控制者；争议时另列全部覆盖方。默认棋盘（地标）与势力层（据点项）共用这一份。
/// </summary>
/// <param name="Value">该档位在本局配置下的分值（取自公开快照的 <see cref="SiteValues"/>，不是常量）。</param>
/// <param name="Controller">控制者：只在占据与唯一覆盖时非空（取自 Core 的 <see cref="SiteState"/>）。</param>
/// <param name="Coverers">争议时的全部覆盖方（玩家序）；其余状态为空。</param>
public sealed record SiteView(
    Coord Coord,
    SiteTier Tier,
    string TierText,
    int Value,
    SiteControlKind Kind,
    PlayerId? Controller,
    ImmutableArray<PlayerId> Coverers,
    string StatusText);

/// <summary>据点视图的构建入口。只读公开快照，不做控制判定。</summary>
public static class SiteViews
{
    /// <summary>
    /// 地图上全部据点，坐标字典序。控制状态一律取 Core 公开快照的 <see cref="Core.Match.MatchPublicView.SiteStates"/>（据点控制唯一实现的输出，
    /// 插旗阶段也由 Core 给出），本类不判定、不补默认状态。
    /// </summary>
    public static ImmutableArray<SiteView> From(PublicWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        GameBoard board = world.View.Board;
        SiteValues values = world.View.SiteValues;
        CoverageMap? coverage = world.View.Power?.Coverage;
        return [.. world.View.SiteStates.Select(st => Build(st, coverage, board, values))];
    }

    private static SiteView Build(SiteState state, CoverageMap? coverage, GameBoard board, SiteValues values)
    {
        // 覆盖方只在争议时列出：取 Core 覆盖表给出的覆盖来源棋子的所有者（读数，不算覆盖）。插旗阶段没有势力快照（也就没有覆盖表）时不列。
        ImmutableArray<PlayerId> coverers = state.Kind == SiteControlKind.Contested && coverage is not null
            ? [.. coverage.SourcesOf(state.Coord).Select(s => board[s.Stone].Occupant!.Value.Owner).Distinct().Order()]
            : [];
        return new SiteView(state.Coord, state.Tier, Labels.SiteTier(state.Tier), values.Of(state.Tier), state.Kind, state.Controller, coverers,
            Labels.SiteStatus(state.Kind, state.Controller, coverers));
    }
}
