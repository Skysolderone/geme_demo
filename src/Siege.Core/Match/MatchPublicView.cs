using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 对局的公开快照（裁决：单线程游戏循环，表现层与 AI 只消费结算完成后发布的快照，不直接读权威盘面）。
/// 全部字段是该时刻的副本或不可变值：<see cref="Board"/> 是 <see cref="GameBoard.Clone"/> 出的独立副本，
/// 之后权威盘面的变化不会反映到这里。结构上不含任何私有信息（手牌数量、征募面板、未揭示信物内容）。
/// <see cref="MaxMajorRounds"/> 是对局配置的大回合上限（0 = 不限），与地图、种子一样始终公开，插旗阶段即可读。
/// <see cref="DominanceStartRound"/> 是碾压起始大回合（0 = 关闭），同样始终公开；<see cref="Dominance"/> 是碾压候选与待回应名单（无候选为 <c>null</c>）。
/// <see cref="CatchUpRecruit"/> 是落后者征募补偿开关（catch-up-recruit 裁决 4），插旗阶段即公开。
/// <see cref="SiteValues"/> 是据点分值配置（site-control「据点公开」），插旗阶段即公开；<see cref="SiteStates"/> 是地图上全部据点此刻的控制状态（坐标字典序），
/// 任何阶段都非空：势力快照已生成时即其 <c>SiteStates</c>，插旗阶段（<see cref="Power"/> 为 <c>null</c>）由据点控制唯一实现按当前盘面给出——表现层不自推。
/// <see cref="ArtisanWeight"/> 是匠人征募权重（artisan-terrain-edit R-2），照 <see cref="SiteValues"/> 的口径：开局固定、始终公开、插旗阶段即可读。
/// <para><b>地形在这里是活的</b>：<see cref="Board"/> 的 <c>Map</c> 含本局已完成的改造，<see cref="BoardSerialized"/> 的改造段同理；
/// 设施无归属，视图里 MUST NOT 出现改造者（R-3）。</para>
/// </summary>
public sealed record MatchPublicView(
    MatchPhase Phase,
    int MajorRound,
    int MaxMajorRounds,
    int DominanceStartRound,
    bool CatchUpRecruit,
    SiteValues SiteValues,
    int ArtisanWeight,
    TurnStage Stage,
    PlayerId? CurrentPlayer,
    ImmutableArray<PlayerId> ActionOrder,
    ImmutableArray<PlayerFlowState> Players,
    GameBoard Board,
    string BoardSerialized,
    PowerSnapshot? Power,
    ImmutableArray<SiteState> SiteStates,
    ImmutableArray<RelicPublicState> Relics,
    ImmutableArray<HandPublicView> Hands,
    int PassStreak,
    DominanceState? Dominance,
    MatchResult? Result);
