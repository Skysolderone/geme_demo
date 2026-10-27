using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;

namespace Siege.Core.PieceRelics;

/// <summary>
/// AI 带入（relic-redesign R21 / D5）：每名 AI 带入与本机玩家同等件数（人机对局）或取跑局配置（批量跑局），不读档案、不受库存限制。
/// </summary>
/// <remarks>
/// <para>抽取只用独立子流 <c>carry-ai</c>（<see cref="GameSeed.CarryAi"/>）。消费顺序（写入 determinism.md）：按玩家编号升序，每名 AI
/// ① 逐件 <c>NextInt(4)</c> 从普通四种（<see cref="RelicKinds.Common"/> 的固定次序）等概率抽 <c>count</c> 件；
/// ② 再逐件不放回 <c>NextInt(剩余未保险件数)</c> 抽 min(2, count) 件勾为保险。稀有（军旗）不进 AI 带入。</para>
/// <para>件数为 0 时返回空、不派生任何子流——全员不带入时与引入带入之前逐步相同。</para>
/// </remarks>
public static class RelicCarryAi
{
    /// <summary>每名 AI 带入 <paramref name="count"/>（0–5）件。0 时返回空、不派生 <c>carry-ai</c> 子流。</summary>
    public static ImmutableSortedDictionary<PlayerId, RelicLoadout> Draw(GameSeed seed, IEnumerable<PlayerId> aiPlayers, int count)
    {
        ImmutableArray<PlayerId> ais = RequireRoster(aiPlayers, count);
        return count == 0 ? ImmutableSortedDictionary<PlayerId, RelicLoadout>.Empty : DrawFrom(seed.Stream(GameSeed.CarryAi), ais, count);
    }

    /// <summary>测试接缝：在给定的流上抽取（件数为 0 时不消费它）。</summary>
    internal static ImmutableSortedDictionary<PlayerId, RelicLoadout> Draw(RandomStream stream, IEnumerable<PlayerId> aiPlayers, int count)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ImmutableArray<PlayerId> ais = RequireRoster(aiPlayers, count);
        return count == 0 ? ImmutableSortedDictionary<PlayerId, RelicLoadout>.Empty : DrawFrom(stream, ais, count);
    }

    /// <summary>
    /// 人机对局的全部带入：本机玩家 <paramref name="human"/> 的清单 <paramref name="humanLoadout"/>，加上其余玩家（AI）按"与本机玩家同等件数"抽取的带入。
    /// 本机玩家不带入（空清单）时 AI 也不带，结果为空。
    /// </summary>
    public static ImmutableSortedDictionary<PlayerId, RelicLoadout> ForHumanMatch(
        GameSeed seed, IEnumerable<PlayerId> players, PlayerId human, RelicLoadout humanLoadout)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(humanLoadout);
        PlayerId[] all = [.. players];
        if (!all.Contains(human))
        {
            throw new ArgumentException($"本机玩家 {human} 不在名册中。", nameof(human));
        }

        int count = humanLoadout.Count;
        ImmutableSortedDictionary<PlayerId, RelicLoadout> ai = Draw(seed, all.Where(p => p != human), count);
        return count == 0 ? ai : ai.Add(human, humanLoadout);
    }

    private static ImmutableSortedDictionary<PlayerId, RelicLoadout> DrawFrom(RandomStream stream, ImmutableArray<PlayerId> ais, int count)
    {
        ImmutableSortedDictionary<PlayerId, RelicLoadout>.Builder drawn = ImmutableSortedDictionary.CreateBuilder<PlayerId, RelicLoadout>();
        foreach (PlayerId ai in ais)
        {
            var kinds = new RelicKind[count];
            for (int i = 0; i < count; i++)
            {
                kinds[i] = RelicKinds.Common[stream.NextInt(RelicKinds.Common.Length)];
            }

            var insured = new bool[count];
            List<int> open = [.. Enumerable.Range(0, count)];
            for (int i = 0; i < Math.Min(RelicKinds.MaxInsured, count); i++)
            {
                int pick = stream.NextInt(open.Count);
                insured[open[pick]] = true;
                open.RemoveAt(pick);
            }

            drawn.Add(ai, new RelicLoadout([.. kinds.Select((k, i) => new CarriedRelic(k, insured[i]))]));
        }

        return drawn.ToImmutable();
    }

    /// <summary>件数只能为 0–5（R4）；AI 名单不得重复；返回按编号升序的名单。</summary>
    private static ImmutableArray<PlayerId> RequireRoster(IEnumerable<PlayerId> aiPlayers, int count)
    {
        ArgumentNullException.ThrowIfNull(aiPlayers);
        if (count < 0 || count > RelicKinds.MaxCarried)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, $"每名玩家每局至多带入 {RelicKinds.MaxCarried} 件信物：带入件数只能为 0–{RelicKinds.MaxCarried}。");
        }

        ImmutableArray<PlayerId> sorted = [.. aiPlayers.Order()];
        if (sorted.Distinct().Count() != sorted.Length)
        {
            throw new ArgumentException("AI 名单有重复玩家。", nameof(aiPlayers));
        }

        return sorted;
    }
}
