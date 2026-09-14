using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Match;

/// <summary>
/// 一名参赛玩家在某次顺序生成中的先手值明细（设计文档 §11.2 / §17 滚雪球分析的原始量）。
/// </summary>
/// <param name="Player">玩家。</param>
/// <param name="Rank">势力名次（竞争名次：1、1、3——裁决：并列口径用 <c>RankGroup.Rank</c>）。</param>
/// <param name="Power">势力值。</param>
/// <param name="Bonus">先手修正（先锋信物，大回合结束时单次读取）。</param>
/// <param name="Value">先手值 = (参赛人数 − 势力名次) + 先手修正。</param>
/// <param name="PreviousPosition">上一大回合的行动位置（0 基）；首回合结束时为 <c>null</c>。</param>
/// <param name="SeedRank">种子兜底顺序（越小越先）。</param>
public sealed record InitiativeEntry(
    PlayerId Player,
    int Rank,
    long Power,
    int Bonus,
    int Value,
    int? PreviousPosition,
    int SeedRank);

/// <summary>某个大回合结束时的先手值明细与生成的下一轮顺序（对下游的"行动顺序 + 先手值明细"契约）。</summary>
public sealed record InitiativeReport(int CompletedMajorRound, int ActiveCount, ImmutableArray<InitiativeEntry> Entries, ImmutableArray<PlayerId> NextOrder)
{
    /// <summary>某玩家的明细。</summary>
    public InitiativeEntry Of(PlayerId player) =>
        Entries.FirstOrDefault(e => e.Player == player) ?? throw new KeyNotFoundException($"先手值明细中没有玩家 {player}。");
}

/// <summary>
/// 先手值公式与三级同值判定链（设计文档 §11.2）的<b>唯一</b>实现，纯函数。
/// </summary>
/// <remarks>
/// <para>输入的势力名次已排除弃赛 / 出局者（由计分层按名册完成）；先手修正来自 <c>RelicLedger.ReadInitiativeBonuses</c>，
/// MUST 在大回合结束时读取、不缓存（design.md D4）。</para>
/// <para>同值链：先手修正更高 → 势力值更高 → 上一大回合行动更晚；首回合无上一大回合可比时按种子顺序。
/// 首回合的随机顺序不算"行动位置"——它不是按势力生成的，规格明确要求此时走种子兜底。</para>
/// </remarks>
public static class InitiativeOrder
{
    /// <summary>先手值 = (参赛人数 − 势力名次) + 先手修正。</summary>
    public static int ValueOf(int activeCount, int rank, int bonus) => (activeCount - rank) + bonus;

    /// <summary>按先手值从高到低生成下一大回合顺序，并列按同值链打破。</summary>
    public static InitiativeReport Generate(int completedMajorRound, ImmutableArray<InitiativeEntry> entries)
    {
        if (entries.IsDefaultOrEmpty)
        {
            throw new ArgumentException("至少需要一名参赛玩家。", nameof(entries));
        }

        ImmutableArray<InitiativeEntry> ordered = [.. entries.OrderBy(e => e, Comparer.Instance)];
        return new InitiativeReport(completedMajorRound, entries.Length, ordered, [.. ordered.Select(e => e.Player)]);
    }

    private sealed class Comparer : IComparer<InitiativeEntry>
    {
        internal static readonly Comparer Instance = new();

        public int Compare(InitiativeEntry? a, InitiativeEntry? b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);

            // 先手值高者先
            int c = b.Value.CompareTo(a.Value);
            if (c != 0)
            {
                return c;
            }

            // 1. 先手修正高者先
            c = b.Bonus.CompareTo(a.Bonus);
            if (c != 0)
            {
                return c;
            }

            // 2. 势力值高者先
            c = b.Power.CompareTo(a.Power);
            if (c != 0)
            {
                return c;
            }

            // 3. 上一大回合行动更晚者先（位置越大越晚）
            if (a.PreviousPosition is int pa && b.PreviousPosition is int pb)
            {
                c = pb.CompareTo(pa);
                if (c != 0)
                {
                    return c;
                }
            }

            // 首回合兜底：种子顺序
            return a.SeedRank.CompareTo(b.SeedRank);
        }
    }
}
