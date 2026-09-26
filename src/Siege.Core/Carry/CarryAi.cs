using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;

namespace Siege.Core.Carry;

/// <summary>
/// AI 带入（carry-in-out「AI 带入」，design.md D7）：每名 AI 从同一补给池（三种补给，不受任何人的库存限制）随机抽取，
/// 数量与本机玩家相同（人机对局）或取跑局配置（批量跑局）。AI 的决策不读带入，AI 不写档案。
/// </summary>
/// <remarks>
/// 抽取只用独立子流 <c>carry-ai</c>（<see cref="GameSeed.CarryAi"/>）：按玩家编号升序，每名 AI 先在固定次序（备用子、征召签、换型令）中等概率抽种类，
/// 抽到换型令再在换型候选的固定次序（<see cref="CarryCandidates.Of"/>）中等概率抽类型。征召签的类型不在这里抽——建局时由该玩家自己的
/// <c>carry-draft:&lt;编号&gt;</c> 子流解析（<see cref="CarrySetup.Resolve"/>）。所以 AI 的带入只由种子、数量、内容集与 AI 集合决定，
/// 与本机玩家选了哪一种无关；数量为 0 时不派生任何子流。
/// </remarks>
public static class CarryAi
{
    /// <summary>每名 AI 带入 <paramref name="count"/>（0 或 1）件补给。0 时返回空、不派生 <c>carry-ai</c> 子流。</summary>
    public static ImmutableSortedDictionary<PlayerId, CarryIn> Draw(GameSeed seed, IEnumerable<PlayerId> aiPlayers, int count, ContentSet contentSet)
    {
        ImmutableArray<PlayerId> ais = RequireRoster(aiPlayers, count);
        return count == 0 ? ImmutableSortedDictionary<PlayerId, CarryIn>.Empty : DrawFrom(seed.Stream(GameSeed.CarryAi), ais, contentSet);
    }

    /// <summary>测试接缝：在给定的流上抽取（数量为 0 时不消费它）。</summary>
    internal static ImmutableSortedDictionary<PlayerId, CarryIn> Draw(RandomStream stream, IEnumerable<PlayerId> aiPlayers, int count, ContentSet contentSet)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ImmutableArray<PlayerId> ais = RequireRoster(aiPlayers, count);
        return count == 0 ? ImmutableSortedDictionary<PlayerId, CarryIn>.Empty : DrawFrom(stream, ais, contentSet);
    }

    /// <summary>
    /// 人机对局的全部带入：本机玩家 <paramref name="human"/> 的选择 <paramref name="humanCarry"/>，加上其余玩家（AI）按"与本机玩家同等数量"抽取的带入。
    /// 本机玩家不带入时 AI 也不带，结果为空。
    /// </summary>
    public static ImmutableSortedDictionary<PlayerId, CarryIn> ForHumanMatch(
        GameSeed seed, IEnumerable<PlayerId> players, PlayerId human, CarryIn? humanCarry, ContentSet contentSet)
    {
        ArgumentNullException.ThrowIfNull(players);
        PlayerId[] all = [.. players];
        if (!all.Contains(human))
        {
            throw new ArgumentException($"本机玩家 {human} 不在名册中。", nameof(human));
        }

        int count = humanCarry is null ? 0 : 1;
        ImmutableSortedDictionary<PlayerId, CarryIn> ai = Draw(seed, all.Where(p => p != human), count, contentSet);
        return humanCarry is null ? ai : ai.Add(human, humanCarry);
    }

    private static ImmutableSortedDictionary<PlayerId, CarryIn> DrawFrom(RandomStream stream, ImmutableArray<PlayerId> ais, ContentSet contentSet)
    {
        ImmutableArray<CarryCandidate> candidates = CarryCandidates.Of(contentSet);
        ImmutableSortedDictionary<PlayerId, CarryIn>.Builder drawn = ImmutableSortedDictionary.CreateBuilder<PlayerId, CarryIn>();
        foreach (PlayerId ai in ais)
        {
            SupplyKind kind = Supplies.Order[stream.NextInt(Supplies.Order.Length)];
            PieceType? type = kind == SupplyKind.Commission ? candidates[stream.NextInt(candidates.Length)].Type : null;
            drawn.Add(ai, new CarryIn(kind, type));
        }

        return drawn.ToImmutable();
    }

    /// <summary>数量只能为 0 或 1（每名玩家每局至多带入 1 件）；AI 名单不得重复；返回按编号升序的名单。</summary>
    private static ImmutableArray<PlayerId> RequireRoster(IEnumerable<PlayerId> aiPlayers, int count)
    {
        ArgumentNullException.ThrowIfNull(aiPlayers);
        if (count is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "每名玩家每局至多带入 1 件补给：带入数量只能为 0 或 1。");
        }

        ImmutableArray<PlayerId> sorted = [.. aiPlayers.Order()];
        if (sorted.Distinct().Count() != sorted.Length)
        {
            throw new ArgumentException("AI 名单有重复玩家。", nameof(aiPlayers));
        }

        return sorted;
    }
}
