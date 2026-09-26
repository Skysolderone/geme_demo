using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;

namespace Siege.Core.Carry;

/// <summary>
/// 对局配置里"带入带出"两项的校验与征召签解析（match-setup「带入带出配置」，design.md D8）。
/// 建局（<c>MatchFlow.Create</c>）与恢复存档共用这一处。
/// </summary>
public static class CarrySetup
{
    /// <summary>
    /// 校验并解析各玩家带入：关闭时有带入、带入者不在名册、备用子带类型、换型令缺类型或类型不在候选内——一律拒绝（<see cref="ArgumentException"/>，给出原因）。
    /// 征召签按 <see cref="CarryCandidates.Draw"/> 解析出类型写回；已记录类型的征召签（存档 / 日志）MUST 与抽签结果一致。
    /// 无带入时原样返回，不派生任何子流。
    /// </summary>
    public static ImmutableSortedDictionary<PlayerId, CarryIn> Resolve(
        bool carryInOut, ImmutableSortedDictionary<PlayerId, CarryIn> carryIns, IReadOnlyCollection<PlayerId> players, ContentSet contentSet, GameSeed seed)
    {
        ArgumentNullException.ThrowIfNull(carryIns);
        ArgumentNullException.ThrowIfNull(players);
        if (carryIns.Count == 0)
        {
            return carryIns;
        }

        if (!carryInOut)
        {
            throw new ArgumentException($"带入带出已关闭，却给出了 {carryIns.Count} 名玩家的带入；关闭时各玩家带入必须为空。", nameof(carryIns));
        }

        ImmutableSortedDictionary<PlayerId, CarryIn>.Builder resolved = ImmutableSortedDictionary.CreateBuilder<PlayerId, CarryIn>();
        foreach ((PlayerId player, CarryIn carry) in carryIns)
        {
            ArgumentNullException.ThrowIfNull(carry);
            if (!players.Contains(player))
            {
                throw new ArgumentException($"带入者 {player} 不在本局名册中。", nameof(carryIns));
            }

            resolved.Add(player, carry.Kind switch
            {
                SupplyKind.SpareStone => carry.Type is null
                    ? carry
                    : throw new ArgumentException($"玩家 {player} 的备用子不应指定类型（给出了 {carry.Type}）。", nameof(carryIns)),
                SupplyKind.Commission => carry.Type is { } type && CarryCandidates.Contains(contentSet, type)
                    ? carry
                    : throw new ArgumentException(
                        $"玩家 {player} 的换型令指定类型 {carry.Type?.ToString() ?? "（缺）"} 不在内容集 {contentSet} 的换型候选中"
                        + $"（候选：{string.Join(" / ", CarryCandidates.Of(contentSet).Select(c => c.Type))}）。",
                        nameof(carryIns)),
                SupplyKind.DraftLot => ResolveDraft(player, carry, contentSet, seed),
                _ => throw new ArgumentOutOfRangeException(nameof(carryIns), carry.Kind, $"玩家 {player} 的补给种类未知。"),
            });
        }

        return resolved.ToImmutable();
    }

    /// <summary>征召签：用该玩家自己的子流抽一次；已记录类型的（存档 / 日志）必须与抽签结果一致。</summary>
    private static CarryIn ResolveDraft(PlayerId player, CarryIn carry, ContentSet contentSet, GameSeed seed)
    {
        PieceType drawn = CarryCandidates.Draw(seed, player, contentSet);
        if (carry.Type is { } recorded && recorded != drawn)
        {
            throw new ArgumentException($"玩家 {player} 的征召签记录为 {recorded}，按种子抽签应为 {drawn}：记录与种子不一致。", nameof(carry));
        }

        return carry with { Type = drawn };
    }
}
