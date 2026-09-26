using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Carry;

/// <summary>名次补给点表（carry-in-out「名次补给点表」，design.md D3）。表值是<b>未校准的初值</b>，不扫档。</summary>
public static class CarryPoints
{
    /// <summary>某参赛人数的点数表（第 1 名起）：4 人 24 / 16 / 12 / 10，3 人 24 / 16 / 10，2 人 24 / 10。</summary>
    public static ImmutableArray<int> TableOf(int playerCount) => playerCount switch
    {
        2 => UncalibratedTwoPlayer,
        3 => UncalibratedThreePlayer,
        4 => UncalibratedFourPlayer,
        _ => throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "名次补给点表只有 2–4 人。"),
    };

    /// <summary>该参赛人数是否有点数表（2–4 人）。</summary>
    public static bool Supports(int playerCount) => playerCount is >= 2 and <= 4;

    /// <summary>某参赛人数下某名次（竞争名次，并列者共享）的点数。</summary>
    public static int For(int playerCount, int rank)
    {
        ImmutableArray<int> table = TableOf(playerCount);
        return rank >= 1 && rank <= table.Length
            ? table[rank - 1]
            : throw new ArgumentOutOfRangeException(nameof(rank), rank, $"{playerCount} 人局没有第 {rank} 名。");
    }

    // 未校准的初值（design.md D3）：第 1 名恒 24、末名恒 10；任一格的一半（向上取整）必须大于任一补给价格（守门测试）。
    private static readonly ImmutableArray<int> UncalibratedFourPlayer = [24, 16, 12, 10];
    private static readonly ImmutableArray<int> UncalibratedThreePlayer = [24, 16, 10];
    private static readonly ImmutableArray<int> UncalibratedTwoPlayer = [24, 10];
}

/// <summary>带出结算的结局类别。</summary>
public enum CarryOutcome
{
    /// <summary>完赛：按最终名次全额带出，补给已消耗、不返还。</summary>
    Finished,

    /// <summary>弃赛：按弃赛时势力名次带出 50%（向下取整；保护期内为 0），补给返还。</summary>
    Resigned,

    /// <summary>出局：补给丢失，带出 0。</summary>
    Eliminated,

    /// <summary>未结算：批量跑局的截断局，没有名次。</summary>
    Unsettled,
}

/// <summary>一名玩家的带出结算：结局类别、所用名次（出局 / 未结算为 <c>null</c>）、带出补给点、带入的补给与它是否返还。</summary>
public sealed record CarryOutResult(CarryOutcome Outcome, int? Rank, int Points, CarryIn? CarryIn, bool Returned);

/// <summary>
/// 带出结算的<b>纯函数</b>（carry-in-out「完赛结算」「弃赛结算」「出局结算」，design.md D4–D6）。不读写档案。
/// </summary>
public static class CarryOutSettlement
{
    /// <summary>完赛：最终名次 <paramref name="rank"/> 的点数全额带出；带入物已消耗，不返还。</summary>
    public static CarryOutResult Finished(int playerCount, int rank, CarryIn? carryIn) =>
        new(CarryOutcome.Finished, rank, CarryPoints.For(playerCount, rank), carryIn, Returned: false);

    /// <summary>
    /// 弃赛：弃赛时势力名次的点数的一半（<see cref="ResignShare"/>）；在构筑保护期（第 1–3 大回合，<see cref="MatchFlow.BuildProtectionRounds"/>）内为 0。带入物返还。
    /// </summary>
    public static CarryOutResult Resigned(int playerCount, int rankAtResign, int majorRound, CarryIn? carryIn)
    {
        int full = CarryPoints.For(playerCount, rankAtResign);
        int points = majorRound <= MatchFlow.BuildProtectionRounds ? 0 : ResignShare(full);
        return new(CarryOutcome.Resigned, rankAtResign, points, carryIn, Returned: carryIn is not null);
    }

    /// <summary>出局：带入物丢失，带出 0。</summary>
    public static CarryOutResult Eliminated(CarryIn? carryIn) => new(CarryOutcome.Eliminated, null, 0, carryIn, Returned: false);

    /// <summary>未结算（截断局）：不带出、不返还，只作记录。</summary>
    public static CarryOutResult Unsettled(CarryIn? carryIn) => new(CarryOutcome.Unsettled, null, 0, carryIn, Returned: false);

    /// <summary>弃赛折算：表值的 50%，向下取整（现行表值全为偶数，取整口径为以后改表预先定好）。</summary>
    public static int ResignShare(int fullPoints) =>
        fullPoints >= 0 ? fullPoints / 2 : throw new ArgumentOutOfRangeException(nameof(fullPoints), fullPoints, "点数不得为负。");

    /// <summary>
    /// 整局结算：<paramref name="result"/> 为 <c>null</c>（截断局）时全员未结算；否则完赛者按最终名次，弃赛者按弃赛快照记录的弃赛时名次与大回合
    /// （MUST NOT 读最终名次），出局者丢失。参赛人数取 <paramref name="players"/> 的人数。
    /// </summary>
    public static ImmutableSortedDictionary<PlayerId, CarryOutResult> Settle(
        MatchResult? result,
        IEnumerable<ResignationSnapshot> resignations,
        IReadOnlyDictionary<PlayerId, CarryIn> carryIns,
        IReadOnlyCollection<PlayerId> players)
    {
        ArgumentNullException.ThrowIfNull(resignations);
        ArgumentNullException.ThrowIfNull(carryIns);
        ArgumentNullException.ThrowIfNull(players);
        ImmutableSortedDictionary<PlayerId, CarryOutResult>.Builder settled = ImmutableSortedDictionary.CreateBuilder<PlayerId, CarryOutResult>();
        if (result is null)
        {
            foreach (PlayerId player in players)
            {
                settled.Add(player, Unsettled(carryIns.GetValueOrDefault(player)));
            }

            return settled.ToImmutable();
        }

        Dictionary<PlayerId, ResignationSnapshot> resigned = resignations.ToDictionary(r => r.Player);
        int count = players.Count;
        foreach (Standing standing in result.Standings)
        {
            PlayerId player = standing.Player;
            if (!players.Contains(player))
            {
                throw new ArgumentException($"终局名次中的玩家 {player} 不在名册中。", nameof(result));
            }

            CarryIn? carry = carryIns.GetValueOrDefault(player);
            settled.Add(player, standing.Group switch
            {
                StandingGroup.Finisher => Finished(count, standing.Rank, carry),
                StandingGroup.Resigned => resigned.TryGetValue(player, out ResignationSnapshot? snapshot)
                    ? Resigned(count, snapshot.RankAtResign ?? throw new InvalidOperationException($"弃赛者 {player} 的快照没有弃赛时名次（引入带入带出之前的旧存档），不能结算。"),
                        snapshot.MajorRound, carry)
                    : throw new ArgumentException($"弃赛者 {player} 没有弃赛快照。", nameof(resignations)),
                StandingGroup.Eliminated => Eliminated(carry),
                _ => throw new ArgumentOutOfRangeException(nameof(result), standing.Group, "未知名次分组。"),
            });
        }

        if (settled.Count != count)
        {
            throw new ArgumentException($"终局名次有 {settled.Count} 名玩家，名册有 {count} 名。", nameof(result));
        }

        return settled.ToImmutable();
    }
}
