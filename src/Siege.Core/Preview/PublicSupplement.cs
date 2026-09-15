using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Preview;

/// <summary>结构参数的一个信物来源：信物格、类型与强度（该信物已揭示且由该玩家控制，属 §13.1 公开信息）。</summary>
public sealed record ParameterSource(Coord Coord, RelicType Type, int Magnitude);

/// <summary>一项结构参数：基础值、当前值与全部信物来源。<c>Base + Σ Magnitude == Value</c> 由组装方核对，不一致即抛出。</summary>
public sealed record StructureParameter(int Base, int Value, ImmutableArray<ParameterSource> Sources)
{
    /// <summary>信物加成总计。</summary>
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
/// <param name="Structures">每名玩家的公开结构参数及信物来源。</param>
/// <param name="CurrentOrderReport">生成本大回合顺序的先手值明细；第 1 大回合（随机顺序）为 <c>null</c>。</param>
/// <param name="NextOrderForecast">若本大回合此刻结束，下一大回合的先手值明细与顺序预测（tactical-ui D7）；不在对局进行中时为 <c>null</c>。</param>
public sealed record PublicSupplement(
    string BoardSerialized,
    int MajorRound,
    ImmutableArray<GroupLiberties> Liberties,
    ImmutableArray<PlayerStructure> Structures,
    InitiativeReport? CurrentOrderReport,
    InitiativeReport? NextOrderForecast);
