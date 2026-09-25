using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;
using Siege.Core.Relics;

namespace Siege.Core.Tests;

/// <summary>征募与手牌测试的公共夹具：账本工厂、快照构造、种子搜索与结算钩子适配。</summary>
internal static class HandFixtures
{
    internal static readonly PlayerId P0 = TestMaps.P0;
    internal static readonly PlayerId P1 = TestMaps.P1;
    internal static readonly PlayerId P2 = new(2);
    internal static readonly PlayerId P3 = new(3);

    internal static readonly GameSeed Seed = new(0x5EED_2026_0913UL);

    internal static HandLedger Ledger(GameSeed? seed = null, params PlayerId[] players) =>
        new(players.Length == 0 ? [P0, P1, P2, P3] : players, seed ?? Seed);

    /// <summary>指定对局内容集的四人账本（more-pieces-relics D8），匠人取默认权重。</summary>
    internal static HandLedger Ledger(ContentSet contentSet, GameSeed? seed = null) =>
        new([P0, P1, P2, P3], seed ?? Seed, RecruitWeights.DefaultArtisanWeight, contentSet);

    /// <summary>按账本当前持有类型数构造快照；不传徽记时为无信物默认值。</summary>
    internal static EffectSnapshot Snapshot(
        HandLedger ledger,
        PlayerId player,
        int reveal = EffectSnapshot.BaseRevealCount,
        int freePick = EffectSnapshot.BaseFreePickCount,
        int slots = EffectSnapshot.BaseTypeSlots,
        int round = 1,
        params (PieceType Type, int Count)[] emblems) =>
        new(player, round, reveal, freePick, slots, EffectSnapshot.BaseDeployLimitFor(round),
            emblems.ToImmutableSortedDictionary(e => e.Type, e => e.Count), ledger.HeldTypeCount(player));

    /// <summary>开始小回合并返回该玩家的私有句柄。</summary>
    internal static PlayerHandAccess Begin(
        HandLedger ledger,
        PlayerId player,
        int reveal = EffectSnapshot.BaseRevealCount,
        int freePick = EffectSnapshot.BaseFreePickCount,
        int slots = EffectSnapshot.BaseTypeSlots,
        int round = 1,
        params (PieceType Type, int Count)[] emblems)
    {
        ledger.BeginTurn(player, Snapshot(ledger, player, reveal, freePick, slots, round, emblems));
        return ledger.AccessFor(player);
    }

    /// <summary>
    /// 从 <paramref name="start"/> 起顺序搜索种子，返回第一个让 <see cref="P0"/> 在默认快照下的首个面板满足谓词的种子。
    /// 搜索本身是确定的（固定起点、固定顺序），找不到即失败。
    /// </summary>
    internal static GameSeed SeedWhere(Func<ImmutableArray<PieceType>, bool> panel, int reveal = 5, ulong start = 1, int limit = 20000)
    {
        for (ulong v = start; v < start + (ulong)limit; v++)
        {
            var seed = new GameSeed(v);
            HandLedger ledger = Ledger(seed);
            PlayerHandAccess access = Begin(ledger, P0, reveal: reveal);
            if (panel(access.EnterRecruit().CandidateTypes))
            {
                return seed;
            }
        }

        throw new InvalidOperationException("在搜索范围内找不到满足条件的面板。");
    }

    /// <summary>面板中某类型的候选位下标（按出现顺序）。</summary>
    internal static int[] IndicesOf(this RecruitPanelView panel, PieceType type) =>
        [.. panel.Candidates.Where(c => c.Type == type).Select(c => c.Index)];

    /// <summary>候选类型的计数。</summary>
    internal static int CountOf(this IEnumerable<PieceType> types, PieceType type) => types.Count(t => t == type);

    internal static IReadOnlyDictionary<PieceType, int> Deployed(params (PieceType Type, int Count)[] items) =>
        items.ToDictionary(i => i.Type, i => i.Count);
}

/// <summary>
/// 把 <see cref="HandLedger"/> 接到结算顺序第 1 步（扣减）与 Pass 事件。正式接线属于 add-match-flow；
/// 这里只验证「走真实结算驱动器时，暂放不扣减、确认才扣减、Pass 触发撤销」。
/// </summary>
internal sealed class HandHooks(HandLedger ledger) : ISettlementHooks
{
    public void DeductHand(PlayerId player, IReadOnlyDictionary<PieceType, int> deployed) => ledger.DeductHand(player, deployed);

    public void OnRevealRelics(SettlementContext context)
    {
    }

    public void OnRecalculatePower(SettlementContext context)
    {
    }

    public void OnCheckEndConditions(SettlementContext context)
    {
    }

    public void OnPass(PlayerId player) => ledger.OnPass(player);
}
