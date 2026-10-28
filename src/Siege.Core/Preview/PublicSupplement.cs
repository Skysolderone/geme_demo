using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Preview;

/// <summary>
/// 一项结构参数：基础值与当前值（restore-go-core-rules D7）。relic-redesign D4 起旧十类地图信物删除，结构参数不再有信物来源，
/// 当前值恒等于基础值（部署上限为分阶段基础值）；仍分两列，是为了界面与遥测沿用"基础 / 当前"的口径。
/// </summary>
public sealed record StructureParameter(int Base, int Value)
{
    /// <summary>基础值之外的加成总计（relic-redesign 起恒 0，除非测试经调试接缝改写了快照）。</summary>
    public int Bonus => Value - Base;
}

/// <summary>四项公开结构参数（设计文档 §14.3）：征募展示数、免费选取数、手牌类型槽、部署上限。</summary>
public sealed record StructureParameters(
    StructureParameter RevealCount,
    StructureParameter FreePickCount,
    StructureParameter TypeSlots,
    StructureParameter DeployLimit);

/// <summary>一名玩家的公开结构参数。非参赛中的玩家不再拥有小回合，<see cref="Parameters"/> 为 <c>null</c>。</summary>
public sealed record PlayerStructure(PlayerId Player, PlayerStatus Status, StructureParameters? Parameters);

/// <summary>
/// 公开快照的补充载荷（tactical-ui 裁决 9）：<see cref="MatchPublicView"/> 之外、表现层需要但 MUST NOT 自己算的公开派生量。
/// 结构上与 <see cref="MatchPublicView"/> 一样不含任何私有信息。
/// </summary>
/// <param name="BoardSerialized">组装时的盘面序列化，供消费方核对与同时刻的 <see cref="MatchPublicView.BoardSerialized"/> 一致。</param>
/// <param name="MajorRound">组装时的大回合。</param>
/// <param name="Liberties">全盘棋串与气。</param>
/// <param name="Structures">每名玩家的公开结构参数。</param>
/// <param name="CurrentOrderReport">生成本大回合顺序的先手值明细；第 1 大回合（随机顺序）为 <c>null</c>。</param>
/// <param name="NextOrderForecast">若本大回合此刻结束，下一大回合的先手值明细与顺序预测（tactical-ui D7）；不在对局进行中时为 <c>null</c>。</param>
public sealed record PublicSupplement(
    string BoardSerialized,
    int MajorRound,
    ImmutableArray<GroupLiberties> Liberties,
    ImmutableArray<PlayerStructure> Structures,
    InitiativeReport? CurrentOrderReport,
    InitiativeReport? NextOrderForecast);
