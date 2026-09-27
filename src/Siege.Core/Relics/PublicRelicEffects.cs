using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Relics;

/// <summary>
/// 某玩家部署阶段的公开结构参数（expert-lookahead D6 / D7）：分阶段基础部署上限、逐枚军令来源与工坊标记。
/// </summary>
/// <param name="BaseLimit">分阶段基础部署上限（<see cref="EffectSnapshot.BaseDeployLimitFor"/>）。</param>
/// <param name="CommandSources">由该玩家控制、内容已知的军令：坐标 → 强度。</param>
/// <param name="WorkshopActive">是否控制内容已知的工坊（多枚不叠加）。</param>
public sealed record PublicDeployEffects(int BaseLimit, ImmutableSortedDictionary<Coord, int> CommandSources, bool WorkshopActive)
{
    /// <summary>部署上限 = 基础值 + 各枚军令强度之和。</summary>
    public int DeployLimit => BaseLimit + CommandSources.Values.Sum();
}

/// <summary>
/// 部署上限 / 工坊与先锋修正的<b>唯一实现</b>（expert-lookahead D7）。输入是信物的控制与"已知内容"：
/// 对外重载只读 <see cref="RelicPublicState"/>（未揭示者内容为空，按"不提供效果"计）；账本经内部重载传入自己的内容（已结算盘面上受控信物必已揭示，两者结果相同）。
/// 控制只认 <see cref="RelicControl.GrantsEffectTo"/>（参赛中且控制）：争议、无人控制、封锁都不给效果。
/// 本文件在信息边界守门的源码扫描点名清单里。
/// </summary>
public static class PublicRelicEffects
{
    /// <summary>公开部署上限与工坊：<paramref name="majorRound"/> 是该玩家行动的大回合。</summary>
    public static PublicDeployEffects Deploy(PlayerId player, int majorRound, IEnumerable<RelicPublicState> relics)
    {
        ArgumentNullException.ThrowIfNull(relics);
        return DeployFromKnown(player, majorRound, relics.Select(Known));
    }

    /// <summary>
    /// <see cref="Deploy(PlayerId, int, IEnumerable{RelicPublicState})"/> 的本体：<paramref name="relics"/> 的内容为空即"不提供效果"。
    /// 账本在快照生成时经这里计算（传它自己的内容），与前瞻读公开状态是同一段代码。
    /// </summary>
    internal static PublicDeployEffects DeployFromKnown(PlayerId player, int majorRound, IEnumerable<KnownRelic> relics)
    {
        ArgumentNullException.ThrowIfNull(relics);
        ImmutableSortedDictionary<Coord, int>.Builder commands = ImmutableSortedDictionary.CreateBuilder<Coord, int>();
        bool workshop = false;
        foreach (KnownRelic relic in relics)
        {
            if (!relic.Control.GrantsEffectTo(player) || relic.Content is not { } content)
            {
                continue;
            }

            if (content.Type == RelicType.Command)
            {
                commands[relic.Coord] = content.Magnitude;
            }
            else if (content.Type == RelicType.Workshop)
            {
                workshop = true;
            }
        }

        return new PublicDeployEffects(EffectSnapshot.BaseDeployLimitFor(majorRound), commands.ToImmutable(), workshop);
    }

    /// <summary>公开先锋修正：每名 <paramref name="activePlayers"/> 的先手修正（无先锋为 0）。只列参赛中的玩家——已弃赛 / 已出局者不获先手收益。</summary>
    public static ImmutableSortedDictionary<PlayerId, int> InitiativeBonuses(IEnumerable<PlayerId> activePlayers, IEnumerable<RelicPublicState> relics)
    {
        ArgumentNullException.ThrowIfNull(relics);
        return InitiativeBonusesFromKnown(activePlayers, relics.Select(Known));
    }

    /// <summary><see cref="InitiativeBonuses(IEnumerable{PlayerId}, IEnumerable{RelicPublicState})"/> 的本体（账本在大回合结束时经这里读取）。</summary>
    internal static ImmutableSortedDictionary<PlayerId, int> InitiativeBonusesFromKnown(IEnumerable<PlayerId> activePlayers, IEnumerable<KnownRelic> relics)
    {
        ArgumentNullException.ThrowIfNull(activePlayers);
        ArgumentNullException.ThrowIfNull(relics);
        ImmutableArray<KnownRelic> vanguards = [.. relics.Where(r => r.Content is { Type: RelicType.Vanguard })];
        ImmutableSortedDictionary<PlayerId, int>.Builder bonuses = ImmutableSortedDictionary.CreateBuilder<PlayerId, int>();
        foreach (PlayerId player in activePlayers)
        {
            int bonus = 0;
            foreach (KnownRelic relic in vanguards)
            {
                if (relic.Control.GrantsEffectTo(player))
                {
                    bonus += relic.Content!.Value.Magnitude;
                }
            }

            bonuses[player] = bonus;
        }

        return bonuses.ToImmutable();
    }

    /// <summary>公开状态 → 已知内容：以揭示标记为准，未揭示的一律按内容为空（即使状态意外带了内容）。</summary>
    private static KnownRelic Known(RelicPublicState state) =>
        new(state.Coord, state.Control, state.IsRevealed ? state.Content : null);
}

/// <summary>纯函数的输入单元：一枚信物的坐标、控制与调用方已知的内容（未知为空）。</summary>
internal readonly record struct KnownRelic(Coord Coord, RelicControl Control, RelicContent? Content);
