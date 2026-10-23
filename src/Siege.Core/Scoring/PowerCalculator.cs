using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Scoring;

/// <summary>
/// 势力计算（设计文档 §10）的唯一实现：覆盖 → 空格归属三态 → 逐棋串军势 → 总势力 → 名次。
/// 每次调用对整个盘面全量重算（design.md D2），不做增量、不缓存、不保留任何成长层数。
/// </summary>
/// <remarks>
/// <para>公式（restore-go-core-rules D1；formation-tiers D1）：<c>棋串军势 = ⌊(基础军势总和 + 位置加值) × 3^n / 2^n⌋</c>，
/// <c>n = 倍增子数量 + 阵型阶数</c>（倍增子数量不封顶；阵型阶数见 <see cref="FormationTiers"/>，计分规则 v1 下恒为 0）；
/// 位置加值 = 连珠 + 协同 + 高地 + 旗手 + 铁链 + 哨兵 + 界碑（more-pieces-relics 由三项扩为七项），先求和、再与基础军势相加、最后整体乘倍率，对每条棋串各取整一次。</para>
/// <para><c>总势力 = 计分独占空格数 + 全部棋串军势之和</c>（D2）：独占空格直接取 <see cref="CoverageMap.ExclusiveCellsOf"/> 的空格归属结果，
/// 本类不另行统计覆盖；再经 <see cref="ScoresTerritory"/> 过滤掉荒漠（terrain-surfaces D4）。争议格、中立格（含空林地格）不计分，
/// 棋子所在格只算军势，领地分不进倍率，不对总势力二次取整。</para>
/// <para>棋串军势与总势力用 <see cref="BigInteger"/>：不溢出、不截断、不饱和；计分路径不出现浮点。</para>
/// <para>玩家状态只用于名次过滤与明细标记；覆盖与军势对弃赛者、出局者的遗留棋子一视同仁（D7）。</para>
/// <para><b>计分规则版本</b>（formation-tiers D2）：带 <see cref="ScoringVersion"/> 参数的入口按该版本计分；<b>不带版本的入口语义固定为 v1</b>
/// （<see cref="ScoringVersions.Legacy"/>，阵型阶数恒 0），只留给按设计文档旧算例直接构造盘面的测试——产品代码 MUST NOT 调用它们，
/// 对局内的一切计分 MUST 传该对局的版本（守门：<c>计分规则版本Tests.产品代码不调用不带版本的计分入口</c>）。</para>
/// <para>规格：openspec/changes/restore-go-core-rules/specs/power-score、openspec/changes/formation-tiers/specs/power-score</para>
/// </remarks>
public static class PowerCalculator
{
    /// <summary>
    /// 棋串军势公式：<paramref name="baseTotal"/> 与 <paramref name="positionBonus"/>（连珠 + 协同 + 高地 + 旗手 + 铁链 + 哨兵 + 界碑）先相加，
    /// 再经唯一的 <see cref="Multiplier.Apply"/> 整体乘倍率并向下取整；<paramref name="multiplierCount"/> 是倍增子数量，不封顶。
    /// <b>不带阵型阶数的形态 = 计分规则 v1</b>（阵型 0 阶，倍率指数 = 倍增子数量）：只留给旧算例测试，产品代码 MUST NOT 调用。
    /// </summary>
    public static BigInteger GroupPowerOf(int baseTotal, int positionBonus, int multiplierCount) =>
        GroupPowerOf(baseTotal, positionBonus, multiplierCount, formationTier: 0);

    /// <summary>
    /// 棋串军势公式（formation-tiers D1）：倍率指数 = <paramref name="multiplierCount"/>（倍增子数量）+ <paramref name="formationTier"/>（阵型阶数），
    /// 仍经唯一的 <see cref="Multiplier.Apply"/> 精确整数运算，逐串向下取整一次。
    /// </summary>
    public static BigInteger GroupPowerOf(int baseTotal, int positionBonus, int multiplierCount, int formationTier) =>
        new Multiplier(multiplierCount + formationTier).Apply((BigInteger)baseTotal + positionBonus);

    /// <summary>
    /// 独占空格是否计领地分（terrain-surfaces D1 / D4 的唯一落点）：荒漠格可以被独占（归属、信物控制照常），但不计分。
    /// 过滤只在"归属 → 领地分"这一步，空格归属三态本身不看地表。
    /// </summary>
    public static bool ScoresTerritory(MapData map, Coord cell)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.SurfaceAt(cell) != Surface.Desert;
    }

    /// <summary>
    /// 计算一条棋串的军势明细。<paramref name="coverage"/> 是同一盘面的覆盖表（界碑子的独占判定读它，与领地分同一份，不另算覆盖）。
    /// 七项位置加值先求和、再与基础军势相加、最后整体乘倍率（more-pieces-relics：四种新来源与原三项一样被倍率放大）。
    /// <b>不带版本 = 计分规则 v1</b>（不计阵型）：只留给旧算例测试，产品代码 MUST NOT 调用。
    /// </summary>
    public static GroupPower Evaluate(GameBoard board, CoverageMap coverage, Group group) =>
        Evaluate(board, coverage, group, ScoringRelicCounts.None, ScoringVersions.Legacy);

    /// <summary>
    /// 同 <see cref="Evaluate(GameBoard, CoverageMap, Group)"/>，另计棋串所有者控制的计分信物（more-pieces-relics D3）：
    /// 连营并入连珠加值、犄角并入协同加值，与其余位置加值一起被倍率放大。
    /// <b>不带版本 = 计分规则 v1</b>（不计阵型）：只留给旧算例测试，产品代码 MUST NOT 调用。
    /// </summary>
    public static GroupPower Evaluate(GameBoard board, CoverageMap coverage, Group group, ScoringRelicCounts relics) =>
        Evaluate(board, coverage, group, relics, ScoringVersions.Legacy);

    /// <summary>
    /// 按计分规则版本 <paramref name="scoring"/> 计算一条棋串的军势明细（formation-tiers D1 / D3）：阵型阶数按整条棋串的棋子数取
    /// （<see cref="FormationTiers.TierOf(ScoringVersion, int)"/>，v1 恒 0），与倍增子数量相加成倍率指数。这是 <see cref="GroupPower"/> 的唯一构造点。
    /// </summary>
    public static GroupPower Evaluate(GameBoard board, CoverageMap coverage, Group group, ScoringRelicCounts relics, ScoringVersion scoring)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(group);

        int baseTotal = PieceEffects.BaseTotal(board, group);
        int lineBonus = PieceEffects.LineBonus(board, group, relics.Encampments);
        int synergyBonus = PieceEffects.SynergyBonus(board, group, relics.Pincers);
        int highGroundBonus = PieceEffects.HighGroundBonus(board, group);
        int bannerBonus = PieceEffects.BannerBonus(board, group);
        int chainBonus = PieceEffects.ChainBonus(board, group);
        int sentryBonus = PieceEffects.SentryBonus(board, group);
        int boundaryBonus = PieceEffects.BoundaryBonus(board, coverage, group);
        int multiplierCount = PieceEffects.MultiplierCount(board, group);
        int formationTier = FormationTiers.TierOf(scoring, group.Size);
        int positionBonus = lineBonus + synergyBonus + highGroundBonus + bannerBonus + chainBonus + sentryBonus + boundaryBonus;
        BigInteger power = GroupPowerOf(baseTotal, positionBonus, multiplierCount, formationTier);

        // 计分信物的子拆分（遥测用）：同一份加值实现在"无计分信物"下再求一次，差即额外部分；不控制计分信物时不重算。
        int encampmentBonus = relics.Encampments == 0 ? 0 : lineBonus - PieceEffects.LineBonus(board, group, 0);
        int pincerBonus = relics.Pincers == 0 ? 0 : synergyBonus - PieceEffects.SynergyBonus(board, group, 0);
        return new GroupPower(group.Owner, group.Stones, baseTotal, lineBonus, synergyBonus, highGroundBonus,
            bannerBonus, chainBonus, sentryBonus, boundaryBonus, multiplierCount, power)
        {
            EncampmentBonus = encampmentBonus,
            PincerBonus = pincerBonus,
            FormationTier = formationTier,
        };
    }

    /// <summary>
    /// 无名册重载：把盘面上出现的全部玩家视为参赛中。只适用于尚无流程层状态的场景（如信物、征募层的单元测试）；
    /// 正式对局 MUST 走带名册与版本的重载，否则弃赛与出局状态无从得知。<b>不带版本 = 计分规则 v1</b>（不计阵型），产品代码 MUST NOT 调用。
    /// </summary>
    public static PowerSnapshot Compute(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return ComputeCore(board, roster: null, knownRelics: null, ScoringVersions.Legacy);
    }

    /// <summary>
    /// 全量计算。名册 <paramref name="roster"/> 来自流程层，MUST 列出盘面上的每一名玩家（含已弃赛、已出局者）：
    /// 盘面上出现名册外的玩家几乎一定是接线错误，抛 <see cref="SiegeRuleException"/> 而不是静默视为参赛中。
    /// 名册列出但盘面上没有棋子的玩家（例如刚被提光）势力为 0，仍按状态参与名次。
    /// </summary>
    /// <remarks>
    /// 本重载不读任何计分信物（连营 / 犄角视为无人控制）——等同于传入空的已知信物内容。
    /// <b>不带版本 = 计分规则 v1</b>（不计阵型）：只留给旧算例测试，产品代码 MUST NOT 调用。
    /// </remarks>
    public static PowerSnapshot Compute(GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus> roster)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roster);
        return ComputeCore(board, roster, knownRelics: null, ScoringVersions.Legacy);
    }

    /// <summary>
    /// 全量计算并读取计分信物（more-pieces-relics D3，relic-effects「计分信物：连营与犄角」）。
    /// <paramref name="knownRelics"/> 是调用方<b>已知</b>的信物内容（坐标 → 类型）：正式结算传真实内容（<see cref="RelicLedger.TrueContents"/>），
    /// 预演与 AI 只传批次开始前已揭示的公开内容。控制不由调用方给出，而是在本次计算里按同一份覆盖表与名册现算
    /// （唯一实现 <see cref="RelicControl.Of"/>）：争议 / 无人控制不生效，已弃赛 / 已出局者控制的（封锁）不为其加分。
    /// 只按当前盘面，不经效果快照——失去控制的那一次重算里立即失效。
    /// <b>不带版本 = 计分规则 v1</b>（不计阵型）：只留给旧算例测试，产品代码 MUST NOT 调用。
    /// </summary>
    public static PowerSnapshot Compute(
        GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus> roster, IReadOnlyDictionary<Coord, RelicType> knownRelics) =>
        Compute(board, roster, knownRelics, ScoringVersions.Legacy);

    /// <summary>
    /// 按计分规则版本 <paramref name="scoring"/> 全量计算（formation-tiers D2）——<b>产品代码的唯一入口</b>：结算后的势力快照、批次预演、
    /// AI 评价与专家前瞻、顺序预测都走这里并传对局的版本（<c>MatchOptions.ScoringVersion</c> / <c>MatchPublicView.ScoringVersion</c>）。
    /// 名册与已知信物内容的含义同上一个重载。
    /// </summary>
    public static PowerSnapshot Compute(
        GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus> roster, IReadOnlyDictionary<Coord, RelicType> knownRelics, ScoringVersion scoring)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(knownRelics);
        return ComputeCore(board, roster, knownRelics, ScoringVersions.RequireValid(scoring));
    }

    /// <summary>
    /// 某玩家此刻控制的连营 / 犄角枚数。只看 <paramref name="knownRelics"/> 里列出的格；控制经 <see cref="RelicControl.Of"/> 判定，
    /// 只有 <see cref="RelicControl.GrantsEffectTo"/>（参赛中且控制）才计。
    /// </summary>
    private static ScoringRelicCounts ScoringRelicsOf(
        PlayerId player, CoverageMap coverage, IReadOnlyDictionary<Coord, RelicType>? knownRelics, IReadOnlyDictionary<PlayerId, PlayerStatus>? roster)
    {
        if (knownRelics is null)
        {
            return ScoringRelicCounts.None;
        }

        int encampments = 0;
        int pincers = 0;
        foreach ((Coord coord, RelicType type) in knownRelics)
        {
            if (type is not (RelicType.Encampment or RelicType.Pincer) || !RelicControl.Of(coverage, coord, roster).GrantsEffectTo(player))
            {
                continue;
            }

            if (type == RelicType.Encampment)
            {
                encampments++;
            }
            else
            {
                pincers++;
            }
        }

        return new ScoringRelicCounts(encampments, pincers);
    }

    private static PowerSnapshot ComputeCore(
        GameBoard board, IReadOnlyDictionary<PlayerId, PlayerStatus>? roster, IReadOnlyDictionary<Coord, RelicType>? knownRelics, ScoringVersion scoring)
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
            ImmutableArray<Coord> scored = [.. exclusive.Where(c => ScoresTerritory(board.Map, c))];
            ScoringRelicCounts relics = ScoringRelicsOf(player, coverage, knownRelics, roster);
            ImmutableArray<GroupPower> groups = allGroups
                .Where(g => g.Owner == player)
                .Select(g => Evaluate(board, coverage, g, relics, scoring))
                .ToImmutableArray();

            BigInteger groupTotal = BigInteger.Zero;
            foreach (GroupPower g in groups)
            {
                groupTotal += g.Power;
            }

            // 领地分 = 计分独占空格数：直接取空格归属结果（coverage-territory「空格归属三态」），不另行统计覆盖；荒漠只在这里被滤掉。
            details.Add(new PlayerPower(player, status, exclusive, scored, groups, scored.Length + groupTotal));
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
        foreach (IGrouping<BigInteger, PlayerPower> tier in byPower)
        {
            ImmutableArray<PlayerId> ids = tier.Select(p => p.Player).Order().ToImmutableArray();
            ranking.Add(new RankGroup(placed + 1, tier.Key, ids));
            placed += ids.Length;
        }

        return ranking.ToImmutable();
    }
}
