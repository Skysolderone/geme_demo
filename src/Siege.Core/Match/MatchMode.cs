namespace Siege.Core.Match;

/// <summary>
/// 对局模式（board-link-mode D2b / 实现裁决 B1）。序列化名称稳定。
/// </summary>
/// <remarks>
/// <para><see cref="Legacy"/>：引入链接模式之前的全部规则，行为与之前<b>逐字节相同</b>——旧地图上的黄金值与决策序列基线靠它守住围棋内核。缺省值。</para>
/// <para><see cref="Link"/>：盲选插旗、战场内落子、阶段机、配对与链接（battlefield / match-setup / turn-sequence）。只能配链接图（<c>link:&lt;地图种子&gt;</c>），
/// 反之链接图也只能以本模式建局，不匹配即建局报错。所有新规则分支一律以 <c>Mode == Link</c> 为门。</para>
/// </remarks>
public enum MatchMode
{
    /// <summary>旧规则（缺省）。段 C 删除玩家可达入口后只作 Core 测试夹具保留。</summary>
    Legacy,

    /// <summary>链接模式：盲选隔绝棋盘、按战场落子、配对链接与决赛。</summary>
    Link,
}

/// <summary>
/// 链接模式在"对局进行中"之内的阶段标签（battlefield「阶段推进」：构筑 → 待首轮配对 → 交战 → 待决赛链接 → 交战 → 终局）。
/// 两个"待…"阶段是会阻塞小回合的顶层阶段（<see cref="MatchPhase.AwaitingPairing"/> / <see cref="MatchPhase.AwaitingFinalLink"/>），
/// 这里只区分可以行动的三段。序列化名称稳定。
/// </summary>
public enum LinkStage
{
    /// <summary>构筑期：第 1–3 大回合，首轮配对尚未发生。</summary>
    Building,

    /// <summary>交战：首轮配对之后（含无需配对、直接进入交战的情形），决赛链接之前。</summary>
    Fighting,

    /// <summary>决赛：决赛链接之后。</summary>
    Final,
}
