using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Ai;

/// <summary>改造诊断里的一手（单点）：落点、评价收益与在单点排序中的名次。</summary>
/// <param name="Placement">落点（匠人时含改造目标）。</param>
/// <param name="Gain">评价收益 = 该单点加权总分 − Pass 加权总分；与停手阈值、<see cref="MoveOutcome.Gain"/> 同口径。</param>
/// <param name="Rank">在正式单点排序（全部合法单点，未截断）里的名次，从 1 起；预筛外另行打分的单点不在排序里，为 <c>null</c>。</param>
public sealed record ArtisanOption(Placement Placement, BigInteger Gain, int? Rank);

/// <summary>一类改造（搭桥 / 立栅 / 烧林）在一次诊断里的处境（<see cref="ArtisanEditReview.StatusOf"/> 按判定次序取第一条）。</summary>
public enum ArtisanKindStatus
{
    /// <summary>无合法目标：合法落子范围内没有任何一格能把这一类当作匠人的合法改造目标（层 ① = 0）。</summary>
    NoTarget,

    /// <summary>候选生成阶段排除：有规则合法目标，但它们所在的格全部被候选格上限预筛（<c>PrefilterCells</c>）挡在完整枚举之外（① &gt; 0、② = 0）。</summary>
    ExcludedByPrefilter,

    /// <summary>进入了完整枚举，但每个候选都暂放被拒、预演不合法或被活形硬约束淘汰（② &gt; 0、③ = 0）。</summary>
    AllRejected,

    /// <summary>有合法候选，但最好一手排在单点排序前 N（<see cref="ArtisanEditReview.CandidatePointCount"/>）之外，贪心组批看不到它。</summary>
    OutsideTopN,

    /// <summary>有合法候选且在前 N 内，但不是排序第一名（评价落后于第一名）。</summary>
    Behind,

    /// <summary>这一类的最好一手就是单点排序第一名。</summary>
    Leading,
}

/// <summary>
/// 一类改造的三层计数与最好一手（ai-terrain-review 1.1）。
/// </summary>
/// <param name="Kind">改造类别。</param>
/// <param name="RangeTargets">层 ①：合法落子范围内全部可落子空格上、经唯一实现 <see cref="TerrainEditRules.LegalTargets(MapData, Coord, bool)"/> 得到的这一类合法目标数，
/// 按"（匠人落点, 目标）对"计（同一深水格从两侧都够得着计 2）。不预演、不打分。</param>
/// <param name="EnumeratedTargets">层 ②：其中落点在"进入完整枚举的格"（候选格上限预筛之后）上的目标对数。</param>
/// <param name="Candidates">层 ③：完整枚举里这一类的匠人单点经暂放、预演与活形硬约束后留下的个数。</param>
/// <param name="Best">这一类的最好一手（单点排序里第一个该类单点）；③ = 0 时为 <c>null</c>。</param>
/// <param name="OutsideCandidates">预筛外的格（① − ②）上这一类的匠人单点经同一打分路径留下的个数；立栅不做（<c>null</c>）。</param>
/// <param name="OutsideBest">预筛外的最好一手（同一排序口径）；没有为 <c>null</c>。</param>
public sealed record ArtisanKindReview(
    TerrainEditKind Kind,
    int RangeTargets,
    int EnumeratedTargets,
    int Candidates,
    ArtisanOption? Best,
    int? OutsideCandidates,
    ArtisanOption? OutsideBest)
{
    /// <summary>
    /// 改造本身的评价效果：<see cref="Best"/> 与"同一落点、匠人不带改造"的单点逐维的加权贡献之差（九维，次序同 <see cref="EvaluationDimension"/>）。
    /// 两者都来自同一次完整枚举；没有最好一手、或同格不改造的单点落选时为空。
    /// </summary>
    public ImmutableArray<BigInteger> EditEffect { get; init; } = [];
}

/// <summary>AI 一次部署实际落下的匠人改造类别（诊断报告的"实际选择"）。</summary>
public enum ArtisanChoice
{
    /// <summary>Pass。</summary>
    Pass,

    /// <summary>落了子，但没有匠人。</summary>
    NoArtisan,

    /// <summary>落了匠人，未带改造。</summary>
    ArtisanNoEdit,

    /// <summary>搭桥。</summary>
    Bridge,

    /// <summary>立栅。</summary>
    Fence,

    /// <summary>烧林。</summary>
    Burn,
}

/// <summary>
/// 匠人改造诊断的结果（<see cref="HeuristicTurnController.ReviewArtisanEdits"/>）。全部来自正式决策的同一条候选生成 + 预演 + 评价路径。
/// </summary>
/// <param name="Player">被诊断的玩家。</param>
/// <param name="PassThreshold">本次决策实际生效的停手阈值。</param>
/// <param name="DeployLimit">部署上限。</param>
/// <param name="ArtisanStock">手牌里的匠人数。</param>
/// <param name="LegalCells">合法落子范围内的可落子空格数。</param>
/// <param name="CandidateCells">进入完整枚举的格数（候选格上限预筛之后）。</param>
/// <param name="CandidatePointCount">单点排序截取前 N 的 N（贪心组批只看前 N）。</param>
/// <param name="Candidates">全部合法单点数（任何类型）。</param>
/// <param name="Top">单点排序第一名（任何类型）；没有合法单点为 <c>null</c>。</param>
/// <param name="NoEdit">匠人不带改造的最好一手；没有为 <c>null</c>。</param>
/// <param name="KindReviews">三类改造，次序同 <see cref="Kinds"/>。</param>
public sealed record ArtisanEditReview(
    PlayerId Player,
    int PassThreshold,
    int DeployLimit,
    int ArtisanStock,
    int LegalCells,
    int CandidateCells,
    int CandidatePointCount,
    int Candidates,
    ArtisanOption? Top,
    ArtisanOption? NoEdit,
    ImmutableArray<ArtisanKindReview> KindReviews)
{
    /// <summary>三类改造的固定次序：搭桥、立栅、烧林（<see cref="TerrainEditKind"/> 枚举序）。</summary>
    public static readonly ImmutableArray<TerrainEditKind> Kinds = [TerrainEditKind.Bridge, TerrainEditKind.Fence, TerrainEditKind.Burn];

    /// <summary>
    /// 按诊断结果，正式决策的停手判定是否必然 Pass：没有合法单点、部署上限为 0，或排序第一名的收益不大于实际停手阈值（同 <see cref="BestMoveReview.ImpliesPass"/>；
    /// 决胜一手可在其后改落）。与对局里实际的 Pass 对照，可自检诊断确实走了正式决策的同一条路径。
    /// </summary>
    public bool ImpliesPass => Top is null || DeployLimit == 0 || Top.Gain <= PassThreshold;

    /// <summary>某一类的计数。</summary>
    public ArtisanKindReview Of(TerrainEditKind kind) => KindReviews.Single(k => k.Kind == kind);

    /// <summary>某一类的处境（按 <see cref="ArtisanKindStatus"/> 的声明次序取第一条成立者）。</summary>
    public ArtisanKindStatus StatusOf(TerrainEditKind kind)
    {
        ArtisanKindReview k = Of(kind);
        return k.RangeTargets == 0 ? ArtisanKindStatus.NoTarget
            : k.EnumeratedTargets == 0 ? ArtisanKindStatus.ExcludedByPrefilter
            : k.Candidates == 0 ? ArtisanKindStatus.AllRejected
            : k.Best!.Rank > CandidatePointCount ? ArtisanKindStatus.OutsideTopN
            : k.Best.Rank > 1 ? ArtisanKindStatus.Behind
            : ArtisanKindStatus.Leading;
    }
}
