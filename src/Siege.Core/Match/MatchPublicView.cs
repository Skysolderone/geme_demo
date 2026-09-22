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
/// 大回合上限、碾压起始大回合与落后补偿开关三项配置及碾压候选状态已随 restore-go-core-rules（裁决 #4 / #7 / #15）删除，视图里不再有对应字段。
/// <see cref="ArtisanWeight"/> 是匠人征募权重（artisan-terrain-edit R-2）：开局固定、始终公开、插旗阶段即可读。
/// <see cref="MapId"/> 是完整的地图标识（map-generator D3：对生成图即 <c>gen:&lt;地图种子&gt;[:p&lt;平台数&gt;]</c>，凭它能重新得到同一张图），
/// <see cref="Seed"/> 是对局种子的文本（<see cref="Siege.Core.Determinism.GameSeed.ToString"/>）。二者始终公开、插旗阶段即可读，
/// <b>分开给出、MUST NOT 合并成一个数</b>：地图种子只决定地图，对局种子只决定对局里的随机，互不影响。都是字符串——视图的结构里不放随机源类型。
/// <para><b>地形在这里是活的</b>：<see cref="Board"/> 的 <c>Map</c> 含本局已完成的改造，<see cref="BoardSerialized"/> 的改造段同理；
/// 设施无归属，视图里 MUST NOT 出现改造者（R-3）。</para>
/// </summary>
public sealed record MatchPublicView(
    string MapId,
    string Seed,
    MatchPhase Phase,
    int MajorRound,
    int ArtisanWeight,
    TurnStage Stage,
    PlayerId? CurrentPlayer,
    ImmutableArray<PlayerId> ActionOrder,
    ImmutableArray<PlayerFlowState> Players,
    GameBoard Board,
    string BoardSerialized,
    PowerSnapshot? Power,
    ImmutableArray<RelicPublicState> Relics,
    ImmutableArray<HandPublicView> Hands,
    int PassStreak,
    MatchResult? Result);
