using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 弃赛时刻的完整快照（裁决记录 5）：盘面序列化、手牌两段账、效果快照、控制信物列表与势力值，
/// 以及弃赛时势力名次（<paramref name="RankAtResign"/>，carry-in-out D5）：供带出结算读取，MUST NOT 进入 <see cref="FinalStandings"/>。
/// 恢复自引入带入带出之前的旧存档时名次为 <c>null</c>，MUST NOT 重算（快照里没有其他玩家在弃赛时刻的势力）。
/// </summary>
public sealed record ResignationSnapshot(
    PlayerId Player,
    int MajorRound,
    string Board,
    HandPrivateView Hand,
    EffectSnapshot Effects,
    ImmutableArray<Coord> ControlledRelics,
    BigInteger Power,
    int? RankAtResign);

/// <summary>
/// 弃赛时势力名次（elimination-endgame「主动弃赛」，carry-in-out D5）的<b>唯一</b>实现，纯函数：
/// <c>1 + 此刻总势力严格高于弃赛者的未出局玩家数</c>——参赛者与此前已弃赛者都计入，出局者不计；并列共享较高名次。
/// </summary>
public static class ResignationRank
{
    /// <summary>
    /// 计算 <paramref name="resigner"/> 的弃赛时势力名次。<paramref name="field"/> 是此刻全部玩家的状态与总势力（含弃赛者本人，本人此时仍为参赛）。
    /// </summary>
    public static int Compute(PlayerId resigner, IEnumerable<(PlayerId Player, PlayerStatus Status, BigInteger Power)> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        (PlayerId Player, PlayerStatus Status, BigInteger Power)[] all = [.. field];
        BigInteger own = all.Single(f => f.Player == resigner).Power;
        return 1 + all.Count(f => f.Player != resigner && f.Status != PlayerStatus.Eliminated && f.Power > own);
    }
}
