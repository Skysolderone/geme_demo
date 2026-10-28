using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.PieceRelics;

/// <summary>带出结算的结局类别（R5 / R16）。截断局不结算（沿用现行口径），不在这里。</summary>
public enum RelicOutcome
{
    /// <summary>完赛（任何名次）：带入的与终局挂在己方棋子上的信物按 D6 带出，名次补给点全额。</summary>
    Finished,

    /// <summary>弃赛：只回保险件；补给点为弃赛时名次表值的 50%，保护期内 0（R19）。</summary>
    Resigned,

    /// <summary>出局：只回保险件，0 点。</summary>
    Eliminated,

    /// <summary>中途退出：只回保险件，0 点。</summary>
    Abandoned,
}

/// <summary>一名玩家的结局：类别，完赛者的最终名次或弃赛者的弃赛时名次与弃赛大回合。</summary>
public sealed record RelicPlayerOutcome(RelicOutcome Outcome, int? Rank = null, int? MajorRound = null)
{
    /// <summary>完赛，最终名次 <paramref name="rank"/>（竞争名次，并列者共享）。</summary>
    public static RelicPlayerOutcome Finished(int rank) => new(RelicOutcome.Finished, rank);

    /// <summary>弃赛：弃赛时势力名次与弃赛所在大回合（MUST NOT 读最终名次）。</summary>
    public static RelicPlayerOutcome Resigned(int rankAtResign, int majorRound) => new(RelicOutcome.Resigned, rankAtResign, majorRound);

    /// <summary>出局。</summary>
    public static RelicPlayerOutcome Eliminated() => new(RelicOutcome.Eliminated);

    /// <summary>中途退出。</summary>
    public static RelicPlayerOutcome Abandoned() => new(RelicOutcome.Abandoned);

    /// <summary>是否完赛者（D6 第 2 条只认完赛者的棋子）。</summary>
    public bool IsFinisher => Outcome == RelicOutcome.Finished;
}

/// <summary>终局（结算时刻）挂在某枚棋子上的信物与该棋子的主人。</summary>
public readonly record struct HeldRelic(RelicToken Token, PlayerId Holder);

/// <summary>一名玩家的带出结算：结局、补给点与带出的信物（按编号升序）。</summary>
public sealed record RelicPlayerSettlement(PlayerId Player, RelicPlayerOutcome Outcome, int Points, ImmutableArray<RelicToken> BroughtOut)
{
    /// <summary>值相等：逐项比较（<see cref="ImmutableArray{T}"/> 默认是引用相等）。</summary>
    public bool Equals(RelicPlayerSettlement? other) =>
        other is not null && Player == other.Player && Outcome == other.Outcome && Points == other.Points && BroughtOut.SequenceEqual(other.BroughtOut);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Player);
        hash.Add(Outcome);
        hash.Add(Points);
        foreach (RelicToken token in BroughtOut)
        {
            hash.Add(token);
        }

        return hash.ToHashCode();
    }
}

/// <summary>一局的带出结算：每名玩家一项，另列无人带走（消失）的信物。每件信物恰好出现一次（某人带出或消失）。</summary>
public sealed record RelicMatchSettlement(ImmutableSortedDictionary<PlayerId, RelicPlayerSettlement> Players, ImmutableArray<RelicToken> Vanished)
{
    /// <summary>某玩家的结算。</summary>
    public RelicPlayerSettlement Of(PlayerId player) =>
        Players.TryGetValue(player, out RelicPlayerSettlement? settlement)
            ? settlement
            : throw new KeyNotFoundException($"结算里没有玩家 {player}。");
}

/// <summary>
/// 带出结算的<b>纯函数</b>（relic-redesign design.md D6，含 R27；R16 / R17 / R19）。不读写档案、不改盘面。
/// </summary>
/// <remarks>
/// <para>对每件信物（按编号）依次归属：
/// ① 保险件 → 原主（任何结局）；② 挂在某<b>完赛者</b>棋子上 → 该完赛者；③ 其余 → 原主若完赛则归原主，否则消失。
/// 地图刷出的信物只按第 ② 条带出。②在③之前，所以自己的非保险件挂在另一名完赛者棋子上时归对方（R27），同一件不会被两人带出。</para>
/// <para>出局 / 弃赛 / 中途退出者不是完赛者，按上面三条恰好只拿回保险件——与"在其出局 / 弃赛时立即结算"的结果相同，
/// 所以这里一次算全员即可（带入者出局后其保险件仍可能被别人挂着用，局终照样归原主）。</para>
/// <para>补给点（R19，沿用现行名次表与折算的唯一实现 <see cref="RelicPoints"/>）：
/// 完赛按最终名次全额；弃赛按弃赛时名次的 50%，构筑保护期内弃赛 0；出局与中途退出 0。参赛人数取 <c>outcomes</c> 的人数。</para>
/// <para>每件信物在一局结算中只归属一次：以编号去重，同一编号在输入里出现两种内容即抛出；档案层再以对局标识去重（<see cref="RelicProfileStore.Settle"/>）。</para>
/// </remarks>
public static class RelicSettlement
{
    /// <summary>某结局的补给点（R19）。</summary>
    public static int PointsOf(RelicPlayerOutcome outcome, int playerCount)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Outcome switch
        {
            RelicOutcome.Finished => RelicPoints.For(playerCount, RequireRank(outcome)),
            RelicOutcome.Resigned => (outcome.MajorRound ?? throw new ArgumentException("弃赛结局缺少弃赛大回合。", nameof(outcome))) <= MatchFlow.BuildProtectionRounds
                ? 0
                : RelicPoints.ResignShare(RelicPoints.For(playerCount, RequireRank(outcome))),
            RelicOutcome.Eliminated or RelicOutcome.Abandoned => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Outcome, "未知结局。"),
        };
    }

    /// <summary>
    /// 从结算时刻的盘面取持有关系后结算：盘面上格上有子的信物即挂在该子上（持有者 = 棋子主人），空格上的信物无人持有。
    /// <paramref name="carried"/> 是本局全部带入件（挂出的、未挂出的、掉在地上的都列；编号与盘面上的同一件一致）。
    /// </summary>
    public static RelicMatchSettlement Settle(GameBoard board, IReadOnlyDictionary<PlayerId, RelicPlayerOutcome> outcomes, IEnumerable<RelicToken> carried)
    {
        ArgumentNullException.ThrowIfNull(board);
        var held = new List<HeldRelic>();
        foreach (BoardRelic relic in board.RelicsOnBoard())
        {
            if (board[relic.Cell].Occupant is { } stone)
            {
                held.Add(new HeldRelic(relic.Token, stone.Owner));
            }
        }

        return Settle(outcomes, carried, held);
    }

    /// <summary>
    /// 结算全员（D6）。<paramref name="outcomes"/> MUST 列出每名参赛者；<paramref name="carried"/> 是本局全部带入件；
    /// <paramref name="held"/> 是结算时刻挂在棋子上的全部信物（含地图刷出的）与持有者。
    /// 带入件的带入者、持有者不在名册里，编号重复且内容不同，或同一件挂在两处，一律抛 <see cref="ArgumentException"/>。
    /// </summary>
    public static RelicMatchSettlement Settle(
        IReadOnlyDictionary<PlayerId, RelicPlayerOutcome> outcomes,
        IEnumerable<RelicToken> carried,
        IEnumerable<HeldRelic> held)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(held);
        int playerCount = outcomes.Count;

        var tokens = new SortedDictionary<int, RelicToken>();
        foreach (RelicToken token in carried)
        {
            if (token.Origin is not { } origin)
            {
                throw new ArgumentException($"{token} 不是带入件（地图刷出的信物只经持有关系参与结算）。", nameof(carried));
            }

            RequireListed(outcomes, origin, token, nameof(carried));
            Register(tokens, token, nameof(carried));
        }

        var holders = new Dictionary<int, PlayerId>();
        foreach (HeldRelic relic in held)
        {
            RequireListed(outcomes, relic.Holder, relic.Token, nameof(held));
            if (relic.Token.Origin is { } origin)
            {
                RequireListed(outcomes, origin, relic.Token, nameof(held));
            }

            if (!holders.TryAdd(relic.Token.Id, relic.Holder))
            {
                throw new ArgumentException($"信物 {relic.Token} 同时挂在两处。", nameof(held));
            }

            Register(tokens, relic.Token, nameof(held));
        }

        var brought = new Dictionary<PlayerId, ImmutableArray<RelicToken>.Builder>();
        foreach (PlayerId player in outcomes.Keys)
        {
            brought[player] = ImmutableArray.CreateBuilder<RelicToken>();
        }

        ImmutableArray<RelicToken>.Builder vanished = ImmutableArray.CreateBuilder<RelicToken>();
        foreach (RelicToken token in tokens.Values)
        {
            PlayerId? owner = OwnerOf(token, holders.TryGetValue(token.Id, out PlayerId holder) ? holder : null, outcomes);
            if (owner is { } p)
            {
                brought[p].Add(token);
            }
            else
            {
                vanished.Add(token);
            }
        }

        ImmutableSortedDictionary<PlayerId, RelicPlayerSettlement>.Builder players = ImmutableSortedDictionary.CreateBuilder<PlayerId, RelicPlayerSettlement>();
        foreach ((PlayerId player, RelicPlayerOutcome outcome) in outcomes)
        {
            players.Add(player, new RelicPlayerSettlement(player, outcome, PointsOf(outcome, playerCount), brought[player].ToImmutable()));
        }

        return new RelicMatchSettlement(players.ToImmutable(), vanished.ToImmutable());
    }

    /// <summary>D6 的三条归属，按序判定；<c>null</c> 即消失。</summary>
    private static PlayerId? OwnerOf(RelicToken token, PlayerId? holder, IReadOnlyDictionary<PlayerId, RelicPlayerOutcome> outcomes)
    {
        // ① 保险件 → 原主（任何结局）。
        if (token.Insured)
        {
            return token.Origin;
        }

        // ② 挂在某完赛者棋子上 → 该完赛者（R27：先于第 ③ 条，自己的非保险件挂在别的完赛者身上归对方）。
        if (holder is { } h && outcomes[h].IsFinisher)
        {
            return h;
        }

        // ③ 其余 → 原主若完赛则归原主，否则消失（地图刷出的没有原主，消失）。
        return token.Origin is { } origin && outcomes[origin].IsFinisher ? origin : null;
    }

    private static void Register(SortedDictionary<int, RelicToken> tokens, RelicToken token, string parameter)
    {
        if (tokens.TryGetValue(token.Id, out RelicToken existing))
        {
            if (existing != token)
            {
                throw new ArgumentException($"信物编号 {token.Id} 对应两种内容：{existing} 与 {token}。", parameter);
            }

            return;
        }

        tokens.Add(token.Id, token);
    }

    private static void RequireListed(IReadOnlyDictionary<PlayerId, RelicPlayerOutcome> outcomes, PlayerId player, RelicToken token, string parameter)
    {
        if (!outcomes.ContainsKey(player))
        {
            throw new ArgumentException($"信物 {token} 涉及的玩家 {player} 不在结算名册里。", parameter);
        }
    }

    private static int RequireRank(RelicPlayerOutcome outcome) =>
        outcome.Rank ?? throw new ArgumentException($"{outcome.Outcome} 结局缺少名次。", nameof(outcome));
}
