using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;

namespace Siege.Core.PieceRelics;

/// <summary>预告 / 落位里的一件信物：所在格与种类（编号在落位时才分配）。</summary>
public sealed record RelicSpawnSlot(Coord Cell, RelicKind Kind)
{
    public override string ToString() => $"{Cell.ToNotation()} {Kind.DisplayName()}";
}

/// <summary>
/// 一批刷新的预告（relic-redesign D2 / R11）：落位大回合与本批每件的位置、种类。预告时即抽好，落位时不再抽，只跳过已被占或已有信物的格。
/// </summary>
public sealed record RelicForecast(int LandingRound, ImmutableArray<RelicSpawnSlot> Slots)
{
    /// <summary>预告所在的大回合（落位前 1 个大回合）。</summary>
    public int ForecastRound => LandingRound - RelicKinds.UncalibratedForecastLead;

    /// <summary>本批是否为空（场上未拾取件数已达上限，或没有候选格）。</summary>
    public bool IsEmpty => Slots.IsEmpty;

    /// <summary>值相等：逐项比较（<see cref="ImmutableArray{T}"/> 默认是引用相等，对记录没有意义）。</summary>
    public bool Equals(RelicForecast? other) =>
        other is not null && LandingRound == other.LandingRound && Slots.SequenceEqual(other.Slots);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LandingRound);
        foreach (RelicSpawnSlot slot in Slots)
        {
            hash.Add(slot);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// 地图信物的刷新、预告与掉落（relic-redesign design.md D2，替换旧 <c>RelicGenerator</c>；R8–R11、R17）。全部是<b>纯函数</b>：不改盘面，只返回结果，由调用方经
/// <see cref="GameBoard.AttachRelic"/> 写入。随机只来自子流 <see cref="GameSeed.RelicSpawn"/>（每批一条）与 <see cref="GameSeed.RelicDrop"/>（每名玩家一条），
/// 不扰动任何既有子流（.trellis/spec/core/determinism.md 子流表）。
/// </summary>
/// <remarks>
/// <para>时机：落位轮为第 4、6、8……大回合，在该大回合开始时落位；每批在落位前 1 个大回合开始时预告（第 3、5、7……）。</para>
/// <para>数量：<c>min(⌈n/2⌉, (n+2) − 场上未拾取件数)</c>，≤ 0 则本批空；n 取开局参赛人数（不随出局变化，保持可复现）。</para>
/// <para>位置：候选 = 可落子空格 − 出生区域 − 与任一棋子切比雪夫距离 &lt; 2 的格 − 已有信物的格，按坐标字典序排列后逐件不放回抽取。
/// 落位时若预告格已被占或已有信物，则跳过该件（不重抽，保持预告与落位一致）。</para>
/// <para>类型：85 / 15 普通 / 稀有，普通四种等权。数值全是未校准初值（<see cref="RelicKinds"/>）。</para>
/// <para>relic-redesign 加法段：只提供函数，对局流程尚未调用。</para>
/// </remarks>
public static class RelicSpawner
{
    /// <summary>该大回合开始时是否有一批落位（第 4、6、8……）。</summary>
    public static bool IsLandingRound(int majorRound) =>
        majorRound >= RelicKinds.UncalibratedFirstLandingRound
        && (majorRound - RelicKinds.UncalibratedFirstLandingRound) % RelicKinds.UncalibratedLandingInterval == 0;

    /// <summary>该大回合开始时是否预告下一批（第 3、5、7……，即下一个大回合是落位轮）。</summary>
    public static bool IsForecastRound(int majorRound) => IsLandingRound(majorRound + RelicKinds.UncalibratedForecastLead);

    /// <summary>场上未拾取地图信物的上限：开局人数 + 2（R9，未校准）。</summary>
    public static int Cap(int playerCount)
    {
        RequirePlayerCount(playerCount);
        return playerCount + RelicKinds.UncalibratedCapOverPlayers;
    }

    /// <summary>本批件数：<c>min(⌈n/2⌉, 上限 − 场上未拾取件数)</c>，不足 0 取 0（R9）。</summary>
    public static int BatchSize(int playerCount, int unclaimedOnBoard)
    {
        RequirePlayerCount(playerCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unclaimedOnBoard);
        int perBatch = (playerCount + RelicKinds.UncalibratedBatchDivisor - 1) / RelicKinds.UncalibratedBatchDivisor;
        return Math.Max(0, Math.Min(perBatch, Cap(playerCount) - unclaimedOnBoard));
    }

    /// <summary>
    /// 场上未拾取的地图信物件数（R9 上限的计数口径）：空格上、无带入者（地图刷出）的信物。
    /// 带入件掉在地上（R17 出局 / 弃赛掉落，或持物棋子被提后留下）不计——R17 明确掉落件不占上限，带入件一律按同一口径处理。
    /// </summary>
    public static int UnclaimedMapRelics(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        int count = 0;
        foreach (BoardRelic relic in board.RelicsOnBoard())
        {
            if (!relic.Token.IsCarried && board[relic.Cell].Occupant is null)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 刷新 / 掉落的候选格，按坐标字典序：可落子空格，去掉出生区域、与任一棋子切比雪夫距离 &lt; 2 的格、已有信物的格（R10）。
    /// </summary>
    /// <remarks>距离按坐标差直接算（这是"离棋子多远"的距离筛选，不是邻接关系，不经任何邻接枚举）。</remarks>
    public static ImmutableArray<Coord> Candidates(GameBoard board, IBirthArea birth)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(birth);
        int width = board.Width;
        int height = board.Height;
        int reach = RelicKinds.UncalibratedMinStoneDistance - 1;
        var near = new bool[width * height];
        foreach (Coord c in board.AllCoords())
        {
            if (board[c].Occupant is null)
            {
                continue;
            }

            for (int y = Math.Max(0, c.Y - reach); y <= Math.Min(height - 1, c.Y + reach); y++)
            {
                for (int x = Math.Max(0, c.X - reach); x <= Math.Min(width - 1, c.X + reach); x++)
                {
                    near[(y * width) + x] = true;
                }
            }
        }

        var candidates = new List<Coord>();
        foreach (Coord c in board.AllCoords())
        {
            if (board[c].IsPlayableEmpty && !near[(c.Y * width) + c.X] && board.RelicAt(c) is null && !birth.IsBirthArea(c))
            {
                candidates.Add(c);
            }
        }

        candidates.Sort();
        return [.. candidates];
    }

    /// <summary>
    /// 预告（R11）：<paramref name="majorRound"/> 不是预告轮时返回 <c>null</c>；否则用子流 <c>relic-spawn:&lt;落位大回合&gt;</c> 一次抽完本批——
    /// 件数见 <see cref="BatchSize"/>，逐件先不放回抽格、再抽种类。候选格不足时本批只有候选格那么多件。
    /// </summary>
    /// <param name="board">预告时（该大回合开始时）的盘面。</param>
    /// <param name="birth">出生区域。</param>
    /// <param name="seed">对局种子。</param>
    /// <param name="majorRound">当前大回合（预告轮）。</param>
    /// <param name="playerCount">开局参赛人数 n。</param>
    /// <param name="unclaimedOnBoard">场上未拾取件数（通常取 <see cref="UnclaimedMapRelics"/>）。</param>
    public static RelicForecast? Forecast(GameBoard board, IBirthArea birth, GameSeed seed, int majorRound, int playerCount, int unclaimedOnBoard)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(birth);
        if (!IsForecastRound(majorRound))
        {
            return null;
        }

        int landing = majorRound + RelicKinds.UncalibratedForecastLead;
        int count = BatchSize(playerCount, unclaimedOnBoard);
        if (count == 0)
        {
            return new RelicForecast(landing, []);
        }

        List<Coord> pool = [.. Candidates(board, birth)];
        RandomStream stream = seed.Stream(GameSeed.RelicSpawn(landing));
        ImmutableArray<RelicSpawnSlot>.Builder slots = ImmutableArray.CreateBuilder<RelicSpawnSlot>(count);
        for (int k = 0; k < count && pool.Count > 0; k++)
        {
            int pick = stream.NextInt(pool.Count);
            Coord cell = pool[pick];
            pool.RemoveAt(pick);
            slots.Add(new RelicSpawnSlot(cell, DrawKind(stream)));
        }

        return new RelicForecast(landing, slots.ToImmutable());
    }

    /// <summary>
    /// 落位（R8 / R11）：按预告次序，跳过已被占或已有信物的格（不重抽），其余依次分配编号 <paramref name="firstId"/>、<paramref name="firstId"/> + 1……
    /// 返回应写入盘面的信物（地图刷出：无带入者、非保险）。不改盘面。落位只少不多。
    /// </summary>
    public static ImmutableArray<BoardRelic> Land(GameBoard board, RelicForecast forecast, int firstId)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(forecast);
        ArgumentOutOfRangeException.ThrowIfNegative(firstId);
        ImmutableArray<BoardRelic>.Builder landed = ImmutableArray.CreateBuilder<BoardRelic>();
        int id = firstId;
        foreach (RelicSpawnSlot slot in forecast.Slots)
        {
            if (board[slot.Cell].Occupant is not null || board.RelicAt(slot.Cell) is not null)
            {
                continue;
            }

            landed.Add(new BoardRelic(slot.Cell, RelicToken.Spawned(id++, slot.Kind)));
        }

        return landed.ToImmutable();
    }

    /// <summary>
    /// R17 掉落：出局 / 弃赛者 <paramref name="player"/> 手里未挂出的非保险带入件，按与刷新相同的候选规则、用子流 <c>relic-drop:&lt;玩家编号&gt;</c>
    /// 逐件（按编号升序）不放回抽格，立即落位；不占刷新上限。候选格不足时多出的件不落位（视为消失）。不改盘面。
    /// 传入保险件或别人带入的件即抛 <see cref="ArgumentException"/>——保险件归还原主，不掉落（R6 / D6）。
    /// </summary>
    public static ImmutableArray<BoardRelic> Drop(GameBoard board, IBirthArea birth, GameSeed seed, PlayerId player, IEnumerable<RelicToken> items)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(birth);
        ArgumentNullException.ThrowIfNull(items);
        RelicToken[] ordered = [.. items.OrderBy(t => t.Id)];
        foreach (RelicToken token in ordered)
        {
            if (token.Origin != player || token.Insured)
            {
                throw new ArgumentException($"只有 {player} 自己带入的非保险件会掉落，{token} 不是。", nameof(items));
            }
        }

        if (ordered.Length == 0)
        {
            return ImmutableArray<BoardRelic>.Empty;
        }

        List<Coord> pool = [.. Candidates(board, birth)];
        RandomStream stream = seed.Stream(GameSeed.RelicDrop(player.Value));
        ImmutableArray<BoardRelic>.Builder dropped = ImmutableArray.CreateBuilder<BoardRelic>();
        foreach (RelicToken token in ordered)
        {
            if (pool.Count == 0)
            {
                break;
            }

            int pick = stream.NextInt(pool.Count);
            dropped.Add(new BoardRelic(pool[pick], token));
            pool.RemoveAt(pick);
        }

        return dropped.ToImmutable();
    }

    /// <summary>抽一件的种类：先按 85 / 15 加权抽档位，普通再从四种里等权抽一种（R12，未校准）。</summary>
    private static RelicKind DrawKind(RandomStream stream)
    {
        ReadOnlySpan<int> weights = [RelicKinds.UncalibratedCommonWeight, RelicKinds.UncalibratedRareWeight];
        ImmutableArray<RelicKind> tier = stream.WeightedPick(weights) == 0 ? RelicKinds.Common : RelicKinds.Rare;
        return tier.Length == 1 ? tier[0] : tier[stream.NextInt(tier.Length)];
    }

    private static void RequirePlayerCount(int playerCount)
    {
        if (playerCount is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "开局人数须在 1–16 之间。");
        }
    }
}
