using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;

namespace Siege.Core.Ai;

/// <summary>专家一次部署决策的前瞻状态（match-telemetry「专家前瞻的记录」第 2 项）。</summary>
public enum LookaheadStatus
{
    /// <summary>已前瞻：前瞻宽度 ≥ 2 且前瞻集至少 2 个候选，逐个模拟了下一名对手的回应。</summary>
    Applied,

    /// <summary>不前瞻：前瞻宽度不大于 1，或前瞻集只有 1 个候选——直接取前瞻集的第一个。</summary>
    NotApplied,

    /// <summary>Pass：候选生成只得到空批次（前瞻集为空），与高难同样 Pass。</summary>
    Pass,
}

/// <summary>
/// 前瞻集中的一个候选（match-telemetry「专家前瞻的记录」第 3 项）。只含确定性内容。
/// </summary>
/// <param name="CandidateKey">候选的批次键（<see cref="CandidateBatch.Key"/>）。</param>
/// <param name="ScoreBefore">前瞻前分数：候选自身的加权总分。</param>
/// <param name="Responder">下一名对手；没有（只剩自己，或本候选不做模拟）为 <c>null</c>。</param>
/// <param name="ResponderRound">下一名对手的行动大回合（本大回合或下一大回合）；没有对手为 <c>null</c>。</param>
/// <param name="SimulatedDeployLimit">模拟部署上限（= 模拟对手持有的普通子数）；没有对手为 <c>null</c>。</param>
/// <param name="ResponseKey">模拟回应的批次键：Pass 为空串；没有对手为 <c>null</c>。</param>
/// <param name="ScoreAfter">前瞻后分数：回应之后的局面上、以决策起点为"前"的九维加权总分；不做模拟时等于 <paramref name="ScoreBefore"/>。</param>
public sealed record LookaheadEntry(
    string CandidateKey,
    BigInteger ScoreBefore,
    PlayerId? Responder,
    int? ResponderRound,
    int? SimulatedDeployLimit,
    string? ResponseKey,
    BigInteger ScoreAfter)
{
    public override string ToString() =>
        $"[{CandidateKey}] {ScoreBefore}->{ScoreAfter} R={Responder?.ToString() ?? "-"}@{ResponderRound?.ToString() ?? "-"} D={SimulatedDeployLimit?.ToString() ?? "-"} r=[{ResponseKey ?? "-"}]";
}

/// <summary>
/// 专家一次部署决策的前瞻记录（expert-lookahead；段 B 由记录控制器写入日志）。只含确定性内容，不含墙钟耗时。
/// </summary>
/// <param name="Status">前瞻状态。</param>
/// <param name="Entries">前瞻集（按候选选择规则排序）；Pass 时为空。</param>
/// <param name="ChosenIndex">被选候选在 <paramref name="Entries"/> 中的下标；Pass 为 −1。</param>
/// <param name="SimulatedRehearsals">模拟对手一方的预演次数（单点排序、贪心组批与回应复算；不含候选自身的 B1 预演）。</param>
public sealed record LookaheadRecord(LookaheadStatus Status, ImmutableArray<LookaheadEntry> Entries, int ChosenIndex, int SimulatedRehearsals)
{
    /// <summary>Pass 的记录。</summary>
    public static readonly LookaheadRecord Passed = new(LookaheadStatus.Pass, [], -1, 0);

    /// <summary>被选候选；Pass 为 <c>null</c>。</summary>
    public LookaheadEntry? Chosen => ChosenIndex >= 0 ? Entries[ChosenIndex] : null;

    /// <summary>是否与前瞻集的第一个（即同一局面上高难的选择）不同。</summary>
    public bool ChangedChoice => ChosenIndex > 0;

    /// <summary>确定性文本投影（含集合的 record 不能直接比较相等）。</summary>
    public string ToText() =>
        $"{Status} #{ChosenIndex} sim={SimulatedRehearsals} {string.Join(" ; ", Entries.Select(e => e.ToString()))}";
}
