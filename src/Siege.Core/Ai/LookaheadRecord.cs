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

/// <summary>前瞻集成员的来源（expert-strength D3 / D9）。</summary>
public enum CandidateSource
{
    /// <summary>原排序：候选生成（与高难相同）去重后按候选选择规则排在前 W 个。</summary>
    Original,

    /// <summary>多样补充：前瞻集不足 W 个时，排除锚候选的某个落点格后重跑不扰动的贪心组批得到。</summary>
    Supplement,
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
/// <param name="ScoreAfter">
/// 前瞻后分数：一层分数（回应之后的局面上、以决策起点为"前"的九维加权总分）与 <paramref name="TwoPlyBonus"/> 之和；不做模拟时等于 <paramref name="ScoreBefore"/>
/// （混合无对手的前瞻集里，没有下一名对手的候选另加它在 B1 上的两层加分）。
/// </param>
/// <param name="Source">候选来源（expert-strength）：原排序或多样补充。</param>
/// <param name="TwoPlyBonus">两层加分（expert-strength D4）：⌊λ‰ × max(0, 专家下一手最佳单点增量) / 1000⌋；一层分数 = 前瞻后分数 − 两层加分。</param>
public sealed record LookaheadEntry(
    string CandidateKey,
    BigInteger ScoreBefore,
    PlayerId? Responder,
    int? ResponderRound,
    int? SimulatedDeployLimit,
    string? ResponseKey,
    BigInteger ScoreAfter,
    CandidateSource Source = CandidateSource.Original,
    BigInteger TwoPlyBonus = default)
{
    /// <summary>确定性文本投影。来源为原排序、两层加分为 0 时不写这两项（expert-strength D9：一层配置下与改动前逐字节相同，G1 黄金值按它取哈希）。</summary>
    public override string ToString() =>
        $"[{CandidateKey}] {ScoreBefore}->{ScoreAfter} R={Responder?.ToString() ?? "-"}@{ResponderRound?.ToString() ?? "-"} D={SimulatedDeployLimit?.ToString() ?? "-"} r=[{ResponseKey ?? "-"}]"
        + (Source == CandidateSource.Original ? string.Empty : $" src={Source}")
        + (TwoPlyBonus.IsZero ? string.Empty : $" two={TwoPlyBonus}");
}

/// <summary>
/// 专家一次部署决策的前瞻记录（expert-lookahead；段 B 由记录控制器写入日志）。只含确定性内容，不含墙钟耗时。
/// </summary>
/// <param name="Status">前瞻状态。</param>
/// <param name="Entries">前瞻集（按候选选择规则排序）；Pass 时为空。</param>
/// <param name="ChosenIndex">被选候选在 <paramref name="Entries"/> 中的下标；Pass 为 −1。</param>
/// <param name="SimulatedRehearsals">模拟对手一方的预演次数（单点排序、贪心组批与回应复算；不含候选自身的 B1 预演）。</param>
/// <param name="SupplementRehearsals">
/// 多样补充的排除重跑经本人 <c>rehearse</c> 委托做的预演次数（expert-strength D8 代理的一部分；这部分已含在调用方经委托数到的次数里，单列只为给出占比）。
/// </param>
/// <param name="TwoPlyRehearsals">两层扫描的单点预演次数（expert-strength D8；直接调用预演，不经本人委托，代理须另加）。</param>
public sealed record LookaheadRecord(
    LookaheadStatus Status, ImmutableArray<LookaheadEntry> Entries, int ChosenIndex, int SimulatedRehearsals, int SupplementRehearsals = 0, int TwoPlyRehearsals = 0)
{
    /// <summary>Pass 的记录。</summary>
    public static readonly LookaheadRecord Passed = new(LookaheadStatus.Pass, [], -1, 0);

    /// <summary>被选候选；Pass 为 <c>null</c>。</summary>
    public LookaheadEntry? Chosen => ChosenIndex >= 0 ? Entries[ChosenIndex] : null;

    /// <summary>是否与前瞻集的第一个（即同一局面上高难的选择）不同。</summary>
    public bool ChangedChoice => ChosenIndex > 0;

    /// <summary>确定性文本投影（含集合的 record 不能直接比较相等）。多样补充与两层扫描的预演次数为 0 时不写（一层配置下与改动前逐字节相同）。</summary>
    public string ToText() =>
        $"{Status} #{ChosenIndex} sim={SimulatedRehearsals}"
        + (SupplementRehearsals == 0 ? string.Empty : $" sup={SupplementRehearsals}")
        + (TwoPlyRehearsals == 0 ? string.Empty : $" two={TwoPlyRehearsals}")
        + $" {string.Join(" ; ", Entries.Select(e => e.ToString()))}";
}
