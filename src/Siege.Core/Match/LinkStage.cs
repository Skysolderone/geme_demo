namespace Siege.Core.Match;

/// <summary>
/// "对局进行中"之内的阶段标签（battlefield「阶段推进」：构筑 → 待首轮配对 → 交战 → 待决赛链接 → 交战 → 终局）。
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
