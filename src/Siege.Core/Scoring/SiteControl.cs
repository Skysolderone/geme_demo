using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Scoring;

/// <summary>据点的四种控制状态（scoring-sites D-B，与信物控制同口径）。</summary>
public enum SiteControlKind
{
    /// <summary>空格且无人覆盖：无人控制。</summary>
    Unclaimed,

    /// <summary>空格且被多名玩家覆盖：争议，无人控制。</summary>
    Contested,

    /// <summary>空格且被恰好一名玩家覆盖：该玩家以唯一覆盖控制。</summary>
    UniqueCoverage,

    /// <summary>被棋子直接占据：棋子所有者控制（优先于覆盖）。</summary>
    Occupied,
}

/// <summary>一个据点此刻的状态。<see cref="Controller"/> 只在 <see cref="SiteControlKind.Occupied"/> 与 <see cref="SiteControlKind.UniqueCoverage"/> 时非空。</summary>
public sealed record SiteState(Coord Coord, SiteTier Tier, SiteControlKind Kind, PlayerId? Controller)
{
    /// <summary>是否有控制者（占据或唯一覆盖）。</summary>
    public bool IsControlled => Controller is not null;
}

/// <summary>某玩家控制中的一个据点（势力明细用）：坐标、档位、分值、控制方式。</summary>
public sealed record SiteHolding(Coord Coord, SiteTier Tier, int Value, SiteControlKind Kind);

/// <summary>
/// 据点控制判定的<b>唯一</b>实现（scoring-sites 2.1）：占据 &gt; 唯一覆盖 &gt; 争议 &gt; 无人。
/// </summary>
/// <remarks>
/// <para>只读 <see cref="CoverageMap.OwnershipOf"/>——它已经把"占据优先"压在同一份覆盖数据之上（信物控制同样读它），
/// 本类只做 <see cref="OwnershipKind"/> → <see cref="SiteControlKind"/> 的映射，MUST NOT 自行遍历邻格、覆盖目标或高度。
/// 林地据点只能占据是覆盖关系的自然后果，这里没有特例。</para>
/// <para>守门：<c>据点控制判定Tests.据点控制实现只读覆盖表</c> 扫描本文件不得出现邻接 / 覆盖目标 / 高度调用。</para>
/// <para>规格：openspec/changes/scoring-sites/specs/site-control —— Requirement: 据点控制判定</para>
/// </remarks>
public static class SiteControl
{
    /// <summary>按坐标字典序给出地图上每个据点的状态。</summary>
    public static ImmutableArray<SiteState> Compute(GameBoard board, CoverageMap coverage)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(coverage);

        ImmutableArray<SiteState>.Builder states = ImmutableArray.CreateBuilder<SiteState>(board.Map.Sites.Count);
        foreach ((Coord coord, SiteTier tier) in board.Map.Sites.OrderBy(s => s.Key))
        {
            CellOwnership ownership = coverage.OwnershipOf(coord);
            states.Add(ownership.Kind switch
            {
                OwnershipKind.Occupied => new SiteState(coord, tier, SiteControlKind.Occupied, ownership.Owner),
                OwnershipKind.Exclusive => new SiteState(coord, tier, SiteControlKind.UniqueCoverage, ownership.Owner),
                OwnershipKind.Contested => new SiteState(coord, tier, SiteControlKind.Contested, null),
                OwnershipKind.Neutral => new SiteState(coord, tier, SiteControlKind.Unclaimed, null),
                _ => throw new SiegeRuleException($"据点 {coord.ToNotation()} 位于不可落子格（{ownership.Kind}）：地图校验规则 8 应已拒绝。"),
            });
        }

        return states.MoveToImmutable();
    }
}
