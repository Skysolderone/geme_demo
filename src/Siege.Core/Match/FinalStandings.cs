using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>终局名次的三个分组：完赛者 &gt; 弃赛者 &gt; 出局者。</summary>
public enum StandingGroup
{
    Finisher,
    Resigned,
    Eliminated,
}

/// <summary>终局名次比较链的输入：一名玩家在终局时刻的全部比较量。</summary>
/// <param name="Player">玩家。</param>
/// <param name="Status">终局时的状态。</param>
/// <param name="Power">当前势力值（弃赛者为弃赛时势力值）。</param>
/// <param name="ControlledRelics">控制中的信物数量。</param>
/// <param name="ControlledSites">控制中的据点数量（scoring-sites D-G：取代原"独占空格数"）。</param>
/// <param name="Stones">盘面棋子数。</param>
/// <param name="EliminationOrder">出局序号；未出局为 <c>null</c>。</param>
public sealed record StandingInput(
    PlayerId Player,
    PlayerStatus Status,
    long Power,
    int ControlledRelics,
    int ControlledSites,
    int Stones,
    int? EliminationOrder);

/// <summary>终局名次中的一项：竞争名次（并列共享同一名次）、玩家、所属分组。</summary>
public sealed record Standing(int Rank, PlayerId Player, StandingGroup Group, StandingInput Input);

/// <summary>
/// 终局名次比较链（设计文档 §12.3，design.md D7）的<b>唯一</b>实现，纯函数。
/// </summary>
/// <remarks>
/// 完赛者按 势力值 → 控制信物数 → 控制据点数 → 盘面棋子数 逐级比较，四项全同则并列；
/// 弃赛者排在全部完赛者之后，组内按弃赛时势力值；出局者排在最后，按出局先后倒序（越晚出局名次越高）。
/// 终局条件 1（只剩一名参赛玩家）下唯一的完赛者自然位列第 1，无需特殊分支。
/// </remarks>
public static class FinalStandings
{
    /// <summary>计算最终名次。</summary>
    public static ImmutableArray<Standing> Compute(IEnumerable<StandingInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        StandingInput[] all = [.. inputs];

        List<StandingInput> finishers = [.. all.Where(i => i.Status == PlayerStatus.Active).OrderBy(i => i, FinisherComparer.Instance)];
        List<StandingInput> resigned = [.. all.Where(i => i.Status == PlayerStatus.Resigned).OrderByDescending(i => i.Power).ThenBy(i => i.Player)];
        List<StandingInput> eliminated = [.. all.Where(i => i.Status == PlayerStatus.Eliminated)
            .OrderByDescending(i => i.EliminationOrder ?? throw new SiegeRuleException($"出局玩家 {i.Player} 没有出局序号。"))];

        ImmutableArray<Standing>.Builder result = ImmutableArray.CreateBuilder<Standing>(all.Length);
        int position = 0;

        Append(result, finishers, StandingGroup.Finisher, ref position, (a, b) => FinisherComparer.Instance.Compare(a, b) == 0);
        Append(result, resigned, StandingGroup.Resigned, ref position, (a, b) => a.Power == b.Power);
        Append(result, eliminated, StandingGroup.Eliminated, ref position, (_, _) => false);
        return result.MoveToImmutable();
    }

    /// <summary>竞争名次：与前一名完全相同则共享其名次，否则名次 = 当前位置。</summary>
    private static void Append(
        ImmutableArray<Standing>.Builder result,
        List<StandingInput> group,
        StandingGroup kind,
        ref int position,
        Func<StandingInput, StandingInput, bool> tied)
    {
        int rank = 0;
        for (int i = 0; i < group.Count; i++)
        {
            position++;
            if (i == 0 || !tied(group[i - 1], group[i]))
            {
                rank = position;
            }

            result.Add(new Standing(rank, group[i].Player, kind, group[i]));
        }
    }

    /// <summary>完赛者四级比较链。全部按"高者名次靠前"排序，四级全同返回 0。</summary>
    private sealed class FinisherComparer : IComparer<StandingInput>
    {
        internal static readonly FinisherComparer Instance = new();

        public int Compare(StandingInput? a, StandingInput? b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            int c = b.Power.CompareTo(a.Power);
            if (c != 0)
            {
                return c;
            }

            c = b.ControlledRelics.CompareTo(a.ControlledRelics);
            if (c != 0)
            {
                return c;
            }

            c = b.ControlledSites.CompareTo(a.ControlledSites);
            if (c != 0)
            {
                return c;
            }

            return b.Stones.CompareTo(a.Stones);
        }
    }
}

/// <summary>终局结果（对遥测与 UI 的契约）：终局原因、最终名次（含并列）与获胜者。</summary>
public sealed record MatchResult(EndReason Reason, int MajorRound, ImmutableArray<Standing> Standings)
{
    /// <summary>名次第 1 的全部玩家（并列时多名）。</summary>
    public ImmutableArray<PlayerId> Winners => [.. Standings.Where(s => s.Rank == 1).Select(s => s.Player)];

    /// <summary>某玩家的名次。</summary>
    public Standing Of(PlayerId player) =>
        Standings.FirstOrDefault(s => s.Player == player) ?? throw new KeyNotFoundException($"终局名次中没有玩家 {player}。");

    /// <summary>是否存在并列。</summary>
    public bool HasTies => Standings.GroupBy(s => s.Rank).Any(g => g.Count() > 1);
}
