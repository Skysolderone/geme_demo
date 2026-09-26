using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;

namespace Siege.Core.Carry;

/// <summary>换型候选类型中的一项：类型与它在基础棋池中的基础权重。</summary>
public sealed record CarryCandidate(PieceType Type, int Weight);

/// <summary>
/// 换型候选类型（carry-in-out「换型候选类型」，design.md D2）：本局内容集的基础棋池去掉普通子、倍增子与匠人。
/// 权重取基础棋池的基础权重（<see cref="Recruit.RecruitWeights.BaseWeightOf(PieceType)"/>），不另抄一份表。
/// </summary>
/// <remarks>
/// 倍增子不进候选：整条棋串依次 ×1.5、不设生效枚数上限，放大量随棋串增长持续整局。
/// 匠人不进候选：落子即永久改造地形；且匠人征募权重是对局配置（可配成 0 让匠人不进池），带入匠人会绕过这项配置。
/// </remarks>
public static class CarryCandidates
{
    /// <summary>某内容集的换型候选，按类型次序（v2：堡垒 20 / 连珠 18 / 协同 10 / 旗手 8 / 铁链 8 / 哨兵 8 / 界碑 8；v1：堡垒 20 / 连珠 18 / 协同 10）。</summary>
    public static ImmutableArray<CarryCandidate> Of(ContentSet set) =>
        [.. ContentSets.PieceTypesOf(set).Where(t => !Excluded(t)).Select(t => new CarryCandidate(t, RecruitWeights.BaseWeightOf(t)))];

    /// <summary>某类型是否属于该内容集的换型候选。</summary>
    public static bool Contains(ContentSet set, PieceType type) => Of(set).Any(c => c.Type == type);

    /// <summary>
    /// 征召签的抽签：用该玩家自己的子流 <c>carry-draft:&lt;玩家编号&gt;</c>（<see cref="GameSeed.CarryDraft"/>）按候选权重抽一次。
    /// 每名玩家一条子流，任一玩家的结果与其他玩家带了什么无关（design.md D7）。
    /// </summary>
    public static PieceType Draw(GameSeed seed, PlayerId player, ContentSet set)
    {
        ImmutableArray<CarryCandidate> candidates = Of(set);
        int[] weights = [.. candidates.Select(c => c.Weight)];
        return candidates[seed.Stream(GameSeed.CarryDraft(player.Value)).WeightedPick(weights)].Type;
    }

    /// <summary>不进候选的类型：普通子（换出的就是它）、倍增子、匠人。</summary>
    private static bool Excluded(PieceType type) => type is PieceType.Basic or PieceType.Multiplier or PieceType.Artisan;
}
