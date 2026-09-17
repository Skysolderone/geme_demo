using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Scoring;

/// <summary>
/// 一条棋串的军势明细（设计文档 §10.1 / §17）。全部字段由当前盘面一次算出，是稳定结构，UI 与遥测直接消费、不各自重算。
/// </summary>
/// <param name="Owner">棋串所有者。</param>
/// <param name="Stones">棋子坐标集合，字典序。</param>
/// <param name="BaseTotal">基础军势总和。</param>
/// <param name="LineBonus">来自连珠线的位置加值。</param>
/// <param name="SynergyBonus">来自协同子的位置加值。</param>
/// <param name="HighGroundBonus">来自高地压制的位置加值（scoring-sites D-E：每枚棋子至多 1 点）。</param>
/// <param name="MultiplierCount">倍增子的原始数量（未封顶；遥测按它看"堆了多少"）。</param>
/// <param name="Power">取整后军势：<c>⌊基础 × 1.5^min(n, 3)⌋ + 加值</c>，逐棋串各取整一次（multiplier-rebalance：加值不被倍率放大）。</param>
public sealed record GroupPower(
    PlayerId Owner,
    ImmutableArray<Coord> Stones,
    int BaseTotal,
    int LineBonus,
    int SynergyBonus,
    int HighGroundBonus,
    int MultiplierCount,
    long Power)
{
    /// <summary>位置加值总计 = 连珠来源 + 协同来源 + 高地来源（design.md D3：分来源记账；scoring-sites 加高地）。</summary>
    public int PositionBonus => LineBonus + SynergyBonus + HighGroundBonus;

    /// <summary>倍率的精确表示，按生效指数计算，最大 <c>3.375</c>（封顶指数 <see cref="Multiplier.MaxExponent"/> = 3）。</summary>
    public Multiplier Multiplier => new(MultiplierCount);

    /// <summary>生效倍率指数 <c>min(倍增子数量, <see cref="Multiplier.MaxExponent"/>)</c>（cap-multiplier D3：与原始数量并列可读）。</summary>
    public int EffectiveMultiplierCount => Multiplier.Exponent;

    public override string ToString() =>
        $"{Owner}[{string.Join(",", Stones.Select(s => s.ToNotation()))}] 基础{BaseTotal}+加值{PositionBonus}(连珠{LineBonus}/协同{SynergyBonus}/高地{HighGroundBonus}) ×{Multiplier} = {Power}";
}

/// <summary>
/// 一名玩家的势力明细。<see cref="Total"/> 只评价当前盘面：不可消耗、不累计历史积分，这里没有也不会有"历史"字段。
/// </summary>
/// <param name="Player">玩家。</param>
/// <param name="Status">参赛状态（由流程层提供，原样携带，供 UI 标记"已弃赛"）。</param>
/// <param name="ExclusiveCells">独占空格坐标集合，字典序；棋子所在格不在其中。只供盘面层归属读法展示，<b>不计分</b>（scoring-sites D8）。</param>
/// <param name="Sites">该玩家控制中的据点（占据或唯一覆盖），坐标字典序；各项分值取自对局配置。</param>
/// <param name="Groups">逐棋串拆分，按棋串最小坐标字典序。</param>
/// <param name="Total">总势力 = 据点分 + 全部棋串军势之和。据点分不参与任何倍率。</param>
public sealed record PlayerPower(
    PlayerId Player,
    PlayerStatus Status,
    ImmutableArray<Coord> ExclusiveCells,
    ImmutableArray<SiteHolding> Sites,
    ImmutableArray<GroupPower> Groups,
    long Total)
{
    /// <summary>据点分总计：所控制据点的分值之和。</summary>
    public long SiteScore => Sites.Sum(s => (long)s.Value);

    /// <summary>是否参与势力名次（只有参赛中的玩家参与）。</summary>
    public bool IsRanked => Status == PlayerStatus.Active;
}

/// <summary>
/// 名次组：同一势力值的参赛玩家共享一个名次，并列如实输出、不在本层打破（裁决记录 4）。
/// </summary>
/// <remarks>
/// <para><see cref="Rank"/> 是<b>竞争名次</b>：1 + 势力严格更高的参赛玩家数，并列后跳号（1、1、3）。
/// <b>稠密名次</b>（1、1、2）= 本组在 <see cref="PowerSnapshot.Ranking"/> 中的下标 + 1。组内 <see cref="Power"/> 原样保留，
/// 因此两种名次与"并列了几人、势力多少"都能从快照无损推出。</para>
/// <para>§11.2 <c>先手值 = 参赛人数 − 势力名次 + 先手修正</c> 中"势力名次"用竞争还是稠密名次，设计文档与 power-score 规格均未明说，
/// 这是 add-match-flow 必须做的裁决（两者对并列之后的玩家先手值相差 1）；本层只保证信息完整，不替它定。</para>
/// </remarks>
public sealed record RankGroup(int Rank, long Power, ImmutableArray<PlayerId> Players)
{
    /// <summary>是否并列。</summary>
    public bool IsTied => Players.Length > 1;
}

/// <summary>
/// 一次势力重算的完整结果：覆盖表、全部据点状态、据点分值配置、每名玩家的明细、势力名次。整份结果只依赖当前盘面、玩家状态与据点分值输入。
/// </summary>
/// <param name="SiteStates">地图上全部据点此刻的状态（含争议与无人），坐标字典序；表现层与遥测直接消费，不各自判定。</param>
/// <param name="SiteValues">本次计算所用的据点分值（对局配置）。</param>
public sealed record PowerSnapshot(
    CoverageMap Coverage,
    ImmutableArray<SiteState> SiteStates,
    SiteValues SiteValues,
    ImmutableArray<PlayerPower> Players,
    ImmutableArray<RankGroup> Ranking)
{
    /// <summary>某玩家的势力明细；名册与盘面上都没有该玩家时抛出。</summary>
    public PlayerPower Of(PlayerId player) =>
        Players.FirstOrDefault(p => p.Player == player)
        ?? throw new KeyNotFoundException($"势力快照中没有玩家 {player}。");

    /// <summary>某玩家的竞争名次（并列跳号，见 <see cref="RankGroup"/>）；已弃赛、已出局或不在名册的玩家为 <c>null</c>。</summary>
    public int? RankOf(PlayerId player)
    {
        foreach (RankGroup group in Ranking)
        {
            if (group.Players.Contains(player))
            {
                return group.Rank;
            }
        }

        return null;
    }
}
