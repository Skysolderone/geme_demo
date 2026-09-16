using Siege.Core.Board;

namespace Siege.Core.Scoring;

/// <summary>
/// 落后者征募补偿的两档加成（catch-up-recruit 裁决 1）：<see cref="RevealBonus"/> 加在征募展示数上，<see cref="PickBonus"/> 加在免费选取数上。
/// 只是一个值对象——它不知道自己从哪来，也不结转：每个小回合由 <see cref="CatchUpCompensation"/> 重新算一次。
/// </summary>
public readonly record struct CatchUpBonus(int RevealBonus, int PickBonus)
{
    /// <summary>无补偿。</summary>
    public static CatchUpBonus None => default;

    /// <summary>两档中至少命中一档。</summary>
    public bool IsAny => RevealBonus > 0 || PickBonus > 0;

    /// <summary>补偿总点数（两档各至多 1，合计 0–2）。</summary>
    public int Total => RevealBonus + PickBonus;

    public override string ToString() => IsAny ? $"落后补偿 展示+{RevealBonus} 选取+{PickBonus}" : "无落后补偿";
}

/// <summary>
/// 落后者征募补偿的<b>唯一判定实现</b>（catch-up-recruit 裁决 1–3，recruitment「落后者征募补偿」）：
/// 名次 &gt; ⌈参赛人数 ÷ 2⌉ → 展示数 +1；名次等于当前参赛玩家中的最大名次且 &gt; 1 → 免费选取数 +1（两档可同时命中）。
/// </summary>
/// <remarks>
/// <para><b>名次来源唯一</b>：一律取 <see cref="PowerSnapshot.Ranking"/> 里的竞争名次（并列共享，1、1、3），本类不再自己排一次序，
/// 也不触发任何势力重算——读的就是"上一次结算 / Pass 后"的现成排名（裁决 2、design.md D3）。</para>
/// <para><b>参赛人数</b>取 <see cref="PowerSnapshot.Ranking"/> 中的玩家总数：已出局 / 已弃赛者不在名次里（<see cref="PlayerPower.IsRanked"/>），
/// 因此既不计入人数，也拿不到补偿。</para>
/// <para><b>最大名次</b>在并列时不等于人数：势力 60/45/20/20 的名次是 1、2、3、3，最大名次 3；并列最后的两人都算最后一名。</para>
/// </remarks>
public static class CatchUpCompensation
{
    /// <summary>
    /// 按名次判定补偿。<paramref name="rank"/> 为竞争名次（从 1 起），<paramref name="participantCount"/> 为参赛人数，
    /// <paramref name="maxRank"/> 为当前参赛玩家中的最大名次。
    /// </summary>
    public static CatchUpBonus Evaluate(int rank, int participantCount, int maxRank)
    {
        if (participantCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(participantCount), participantCount, "参赛人数须为正整数。");
        }

        if (rank < 1 || rank > participantCount)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, $"名次须在 1..{participantCount} 之间。");
        }

        if (maxRank < 1 || maxRank > participantCount || maxRank < rank)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRank), maxRank, $"最大名次须在 {rank}..{participantCount} 之间。");
        }

        // ⌈参赛人数 ÷ 2⌉ 的整数写法：4 人 → 2，3 人 → 2，2 人 → 1。不要写成 rank * 2 > participantCount，3 人局的第 2 名会被误判。
        int threshold = (participantCount + 1) / 2;
        int reveal = rank > threshold ? 1 : 0;
        int pick = rank == maxRank && rank > 1 ? 1 : 0;
        return new CatchUpBonus(reveal, pick);
    }

    /// <summary>
    /// 从一份势力快照读取 <paramref name="player"/> 的补偿。<paramref name="enabled"/> 为 <c>false</c>（对局配置关闭）、
    /// 快照为 <c>null</c>（尚未重算过）或该玩家不在名次中（已出局 / 已弃赛）时一律无补偿。
    /// </summary>
    public static CatchUpBonus For(PowerSnapshot? power, PlayerId player, bool enabled)
    {
        if (!enabled || power is null || power.Ranking.IsDefaultOrEmpty)
        {
            return CatchUpBonus.None;
        }

        if (power.RankOf(player) is not { } rank)
        {
            return CatchUpBonus.None;
        }

        int participants = power.Ranking.Sum(g => g.Players.Length);
        int maxRank = power.Ranking[^1].Rank;
        return Evaluate(rank, participants, maxRank);
    }
}
