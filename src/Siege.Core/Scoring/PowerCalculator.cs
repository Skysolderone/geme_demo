using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Scoring;

/// <summary>
/// 势力计算（设计文档 §10）的唯一实现：覆盖 → 领地三态 → 逐棋串军势 → 总势力 → 名次。
/// 每次调用对整个盘面全量重算（design.md D2），不做增量、不缓存、不保留任何成长层数。
/// </summary>
/// <remarks>
/// <para>公式：<c>棋串军势 = ⌊(基础军势总和 + 位置加值) × 1.5^min(倍增子数量, 5)⌋</c>（封顶在 <see cref="Multiplier"/> 内部做），取整在乘倍率之后、对每条棋串各执行一次；
/// <c>总势力 = 独占空格数 + 全部棋串军势之和</c>，领地分不进倍率，不对总势力二次取整。</para>
/// <para>玩家状态只用于名次过滤与明细标记；覆盖与军势对弃赛者、出局者的遗留棋子一视同仁（D7）。</para>
/// <para>规格：openspec/changes/add-territory-power/specs/power-score</para>
/// </remarks>
public static class PowerCalculator
{
    /// <summary>棋串军势公式。<paramref name="baseTotal"/> 与 <paramref name="positionBonus"/> 之和乘以 <c>3^e</c> 后整数除以 <c>2^e</c>，<c>e = min(n, 5)</c>；<paramref name="multiplierCount"/> 传原始数量。</summary>
    public static long GroupPowerOf(int baseTotal, int positionBonus, int multiplierCount) =>
        new Multiplier(multiplierCount).Apply(baseTotal + positionBonus);

    /// <summary>计算一条棋串的军势明细。</summary>
    public static GroupPower Evaluate(GameBoard board, Group group)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(group);

        int baseTotal = PieceEffects.BaseTotal(board, group);
        int lineBonus = PieceEffects.LineBonus(board, group);
        int synergyBonus = PieceEffects.SynergyBonus(board, group);
        int multiplierCount = PieceEffects.MultiplierCount(board, group);
        long power = GroupPowerOf(baseTotal, lineBonus + synergyBonus, multiplierCount);
        return new GroupPower(group.Owner, group.Stones, baseTotal, lineBonus, synergyBonus, multiplierCount, power);
    }

    /// <summary>
    /// 无名册重载：把盘面上出现的全部玩家视为参赛中。只适用于尚无流程层状态的场景（如信物、征募层的单元测试）；
    /// 正式对局 MUST 走带名册的重载，否则弃赛与出局状态无从得知。
    /// </summary>
    public static PowerSnapshot Compute(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return ComputeCore(board, roster: null);
    }

    /// <summary>
    /// 全量计算。名册 <paramref name="roster"/> 来自流程层，MUST 列出盘面上的每一名玩家（含已弃赛、已出局者）：
    /// 盘面上出现名册外的玩家几乎一定是接线错误，抛 <see cref="SiegeRuleException"/> 而不是静默视为参赛中。
    /// 名册列出但盘面上没有棋子的玩家（例如刚被提光）势力为 0，仍按状态参与名次。
    /// </summary>
    public static PowerSnapshot Compute(GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus> roster)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roster);
        return ComputeCore(board, roster);
    }

    private static PowerSnapshot ComputeCore(GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus>? roster)
    {
        CoverageMap coverage = CoverageMap.Compute(board);
        ImmutableArray<Group> allGroups = board.AllGroups();

        var players = roster is null ? new SortedSet<PlayerId>() : new SortedSet<PlayerId>(roster.Keys);
        foreach (Group group in allGroups)
        {
            if (roster is not null && !roster.ContainsKey(group.Owner))
            {
                throw new SiegeRuleException(
                    $"盘面上出现名册外的玩家 {group.Owner}（棋串 {group}）：势力计算的名册必须列出盘面上的每一名玩家，包括已弃赛与已出局者。");
            }

            players.Add(group.Owner);
        }

        ImmutableArray<PlayerPower>.Builder details = ImmutableArray.CreateBuilder<PlayerPower>(players.Count);
        foreach (PlayerId player in players)
        {
            PlayerStatus status = roster is null ? PlayerStatus.Active : roster[player];
            ImmutableArray<Coord> exclusive = coverage.ExclusiveCellsOf(player);
            ImmutableArray<GroupPower> groups = allGroups
                .Where(g => g.Owner == player)
                .Select(g => Evaluate(board, g))
                .ToImmutableArray();

            long groupTotal = 0;
            foreach (GroupPower g in groups)
            {
                groupTotal = checked(groupTotal + g.Power);
            }

            details.Add(new PlayerPower(player, status, exclusive, groups, checked(exclusive.Length + groupTotal)));
        }

        ImmutableArray<PlayerPower> playerPowers = details.ToImmutable();
        return new PowerSnapshot(coverage, playerPowers, Rank(playerPowers));
    }

    /// <summary>
    /// 排除已弃赛与已出局玩家，按势力从高到低分组；同值同组、不打破并列。
    /// <see cref="RankGroup.Rank"/> 取竞争名次（并列后跳号，1、1、3）；稠密名次（1、1、2）= 组在数组中的下标 + 1。
    /// 两种都能从返回值无损得到，§11.2 先手值该用哪一种由 add-match-flow 裁决，本层不定。
    /// </summary>
    private static ImmutableArray<RankGroup> Rank(ImmutableArray<PlayerPower> players)
    {
        ImmutableArray<RankGroup>.Builder ranking = ImmutableArray.CreateBuilder<RankGroup>();
        var byPower = players
            .Where(p => p.IsRanked)
            .GroupBy(p => p.Total)
            .OrderByDescending(g => g.Key);

        int placed = 0;
        foreach (IGrouping<long, PlayerPower> tier in byPower)
        {
            ImmutableArray<PlayerId> ids = tier.Select(p => p.Player).Order().ToImmutableArray();
            ranking.Add(new RankGroup(placed + 1, tier.Key, ids));
            placed += ids.Length;
        }

        return ranking.ToImmutable();
    }
}
