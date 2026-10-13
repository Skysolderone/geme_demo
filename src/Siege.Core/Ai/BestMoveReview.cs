using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Ai;

/// <summary>
/// 停手复盘里一手（单点或一个批次）的结果（ai-pass-review 1.1）：预演结算后、对手不回应时的势力对比。
/// </summary>
/// <param name="Placements">这一手的落点（单点即一枚）。</param>
/// <param name="Gain">评价收益 = 这一手的加权总分 − Pass 的加权总分；与停手阈值同口径（贪心加第一枚时比较的就是它）。</param>
/// <param name="Captures">预演提子数。</param>
/// <param name="PowerAfter">落下后己方势力（评价器口径）。</param>
/// <param name="RivalAfter">落下后本战场其他在局玩家的最高势力；战场里没有其他在局玩家为 <c>null</c>。</param>
public sealed record MoveOutcome(ImmutableArray<Placement> Placements, BigInteger Gain, int Captures, BigInteger PowerAfter, BigInteger? RivalAfter)
{
    /// <summary>"本可翻盘"：对手不回应时己方势力 ≥ 本战场其他在局玩家的最高势力（追平或反超）。没有对手为 <c>null</c>。</summary>
    public bool? ComesBack => RivalAfter is { } rival ? PowerAfter >= rival : null;
}

/// <summary>停手复盘时"没有可落的一手"的原因（按判定次序取第一条）。</summary>
public enum NoMoveReason
{
    /// <summary>有合法候选。</summary>
    None,

    /// <summary>手里没有棋子。</summary>
    NoStock,

    /// <summary>部署上限为 0。</summary>
    NoDeployQuota,

    /// <summary>合法落子范围里没有可落子的空格。</summary>
    NoLegalCell,

    /// <summary>有空格，但每个候选都预演不合法或被活形硬约束淘汰。</summary>
    NoLegalCandidate,
}

/// <summary>
/// 停手复盘的结果（<see cref="HeuristicTurnController.ReviewBestMove"/>）。全部来自正式决策的同一条候选生成 + 预演 + 评价路径。
/// </summary>
/// <param name="Player">被复盘的玩家。</param>
/// <param name="PassThreshold">本次决策实际生效的停手阈值（含无子豁免与链接模式落后者豁免）。</param>
/// <param name="DeployLimit">部署上限。</param>
/// <param name="Stock">手牌棋子总数。</param>
/// <param name="LegalCells">合法落子范围内的可落子空格数。</param>
/// <param name="CandidateCells">进入完整枚举的格数（候选格上限预筛之后）。</param>
/// <param name="Candidates">合法候选数：预演合法且未被活形硬约束淘汰的单点（格 × 类型 × 改造）个数。0 即"无任何合法落子"。</param>
/// <param name="PowerBefore">决策前己方势力。</param>
/// <param name="RivalBefore">决策前本战场其他在局玩家的最高势力；没有其他在局玩家为 <c>null</c>。</param>
/// <param name="Best">最好一手：单点排序第一名；无合法候选为 <c>null</c>。</param>
/// <param name="Forced">强制落子批次：同一排序前 N 的贪心组批、保留条件放宽为"合法即保留"；无合法候选或部署上限为 0 时为 <c>null</c>。</param>
/// <param name="CaptureCandidates">合法候选中预演有提子的个数。</param>
/// <param name="ComebackCandidates">合法候选中单点即"本可翻盘"的个数。</param>
public sealed record BestMoveReview(
    PlayerId Player,
    int PassThreshold,
    int DeployLimit,
    int Stock,
    int LegalCells,
    int CandidateCells,
    int Candidates,
    BigInteger PowerBefore,
    BigInteger? RivalBefore,
    MoveOutcome? Best,
    MoveOutcome? Forced,
    int CaptureCandidates,
    int ComebackCandidates)
{
    /// <summary>
    /// 完整枚举里落选的单点按原因计数：<c>暂放被拒:&lt;失败种类&gt;</c>（预演第 1–2 步，含活棋禁入）、<c>预演不合法:&lt;失败种类&gt;</c>（自杀手、同形等）、
    /// <c>活形硬约束</c>（填自己的单格眼或破坏己方活形且不提子）。候选格上限预筛掉的格不在其中。
    /// </summary>
    public ImmutableSortedDictionary<string, int> Rejections { get; init; } = ImmutableSortedDictionary<string, int>.Empty;

    /// <summary>没有可落的一手的原因；有合法候选且部署上限 &gt; 0 为 <see cref="NoMoveReason.None"/>。</summary>
    public NoMoveReason NoMove =>
        Stock == 0 ? NoMoveReason.NoStock
        : DeployLimit == 0 ? NoMoveReason.NoDeployQuota
        : LegalCells == 0 ? NoMoveReason.NoLegalCell
        : Candidates == 0 ? NoMoveReason.NoLegalCandidate
        : NoMoveReason.None;

    /// <summary>
    /// 按复盘结果，正式决策是否必然 Pass：没有可落的一手，或最好一手的收益不大于实际停手阈值。
    /// 与对局里实际的 Pass 对照，可自检复盘确实走了正式决策的同一条路径。
    /// </summary>
    public bool ImpliesPass => NoMove != NoMoveReason.None || Best!.Gain <= PassThreshold;
}
