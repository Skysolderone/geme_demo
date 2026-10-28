using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.PieceRelics;
using Siege.Core.Scoring;

namespace Siege.Core.Ai;

/// <summary>
/// AI 评价"信物"维的盘面总分（设计文档 §15.1 维度 3；relic-redesign 段 C1 按 design.md D5 重写）。三个桶，全部以<b>势力增量</b>为单位：
/// <list type="number">
/// <item><b>己方持物</b>：挂在己方棋子上的信物对所在棋串军势的边际贡献（<see cref="GroupPower.RelicContribution"/>，由唯一的势力计算给出）取正；
/// 望台只改覆盖、不进军势，每件按未校准平值 <see cref="UncalibratedWatchtowerHeldValue"/> 计。</item>
/// <item><b>敌方持物</b>：参赛中敌方棋子上的同一量取负。弃赛 / 出局者棋子上的不计。</item>
/// <item><b>地图信物格与预告格</b>：对每个<b>与某名参赛玩家棋子相邻</b>（气边四邻）的空格信物，按"该玩家落普通子拾取后己方势力增量"（<see cref="RelicSurvey"/>
/// 在决策开始时用 Clone + 假落子经唯一势力计算算一次；决策前仍挂在棋子上的信物也预先算好"该子被提走后"的这一量，提走掉落时才有数可查）记入：
/// 己方相邻取正、敌方相邻取负，地图信物折 1 / <see cref="UncalibratedMapRelicDivisor"/>，预告格（落位前不可拾取，R11）再折到 1 / <see cref="UncalibratedForecastDivisor"/>。
/// 无人相邻的信物格对谁都是 0——落子即拾取，机会均等。</item>
/// </list>
/// 于是（<c>Score(后) − Score(前)</c>）：拾取 = 地图格项消失 + 己方持物项出现；提走敌方持物子 = 敌方持物负项消失 + 掉落件紧贴己方提子的棋子、地图格正项出现
/// （"提走后掉落可被己方拾取"）；落在敌方相邻的地图格 / 预告格上 = 敌方的机会项消失（阻断）；落到预告格上己方的机会项也消失（不要堵自己的刷新）。
/// </summary>
/// <remarks>
/// <para>只读公开信息：盘面信物层、公开预告（R22）、势力快照；不读任何私有信息。<b>不自算任何信物效果</b>——军势边际贡献全部来自 <see cref="PowerCalculator"/>，
/// 本类只决定每件信物落进哪个桶与折价（守门 <c>新棋子与新信物的AI适配Tests.AI源码不另写新加值公式</c>）。</para>
/// <para>持物项与"即时势力增量"维有重合（信物的即时效果已经经势力计算进入势力增量），是有意为之：信物持续生效且可被提走掉落，持有本身值得比一次性势力更高的权重；
/// 具体倍数由 <c>EvaluationWeights.Relic</c> 定，<b>未校准</b>。</para>
/// <para>机会表 <see cref="RelicSurvey"/> 按决策开始前的盘面算一次、整次决策共用（同一次决策内信物格与预告都不变），每次评价只按"后"盘面的占用与相邻关系查表求和，
/// 不再做任何 Clone 或势力计算——预筛与完整口径都付得起。</para>
/// </remarks>
public static class RelicEstimate
{
    /// <summary>望台的持有价值：<b>未校准的初值 2</b>（望台只改覆盖，覆盖带来的领地分不能归到某条棋串，取平值；约为四方向距离 2 的四格里一半成为独占）。</summary>
    public const int UncalibratedWatchtowerHeldValue = 2;

    /// <summary>地图信物格的机会折价分母：<b>未校准的初值 2</b>（相邻者落子即拾取，但对手也能）。</summary>
    public const int UncalibratedMapRelicDivisor = 2;

    /// <summary>预告格的机会折价分母：<b>未校准的初值 4</b>（落位前不可拾取、落位时被占即跳过，比地图信物更不确定）。</summary>
    public const int UncalibratedForecastDivisor = 4;

    /// <summary>预告格假落子时给临时信物的编号起点：远离本局正常编号（从 0 递增），只在 Clone 副本里出现。</summary>
    private const int ProvisionalIdBase = int.MaxValue - 1024;

    /// <summary>
    /// 决策开始时的机会表：对每个地图信物格与预告格、每名参赛玩家，"该玩家在此格落一枚普通子拾取后己方势力的增量"。
    /// 由 <see cref="Survey"/> 构造；<see cref="Score"/> 只查表。
    /// </summary>
    public sealed class RelicSurvey
    {
        internal RelicSurvey(ImmutableArray<RelicOpportunity> cells) => Cells = cells;

        /// <summary>空表（盘面没有信物、也没有预告，或简单难度不算本维）。</summary>
        public static readonly RelicSurvey Empty = new([]);

        /// <summary>按坐标字典序的全部机会格。</summary>
        public ImmutableArray<RelicOpportunity> Cells { get; }
    }

    /// <summary>一个机会格：坐标、是否预告格（否则是信物格——决策前为空格信物或挂在棋子上）、各参赛玩家拾取后的势力增量（按编号升序）。只对"后"盘面上仍为空的格计分。</summary>
    public sealed record RelicOpportunity(Coord Cell, bool IsForecast, ImmutableSortedDictionary<PlayerId, BigInteger> GainBy);

    /// <summary>
    /// 建机会表（每次部署决策一次）。对盘面上每件信物所在格（空格信物按原样；挂在棋子上的先在副本里移走那枚棋子——它被提走后信物就在这格成为地图信物）
    /// 与 <paramref name="forecast"/> 的每个仍为空、无信物的预告格，对每名参赛中玩家：Clone 盘面 →（预告格先放一件临时同种信物）→ 该玩家落一枚普通子 →
    /// 经 <see cref="PowerCalculator.Evaluate(GameBoard, CoverageMap, Group, ScoringVersion)"/> 算新成棋串的信物边际贡献，减去该玩家原本与此格相邻的各棋串的边际贡献（它们已在持物桶里），
    /// 望台再加平值。覆盖表沿用决策前的（只影响界碑项，估值精度足够、不另算覆盖）。没有信物也没有预告时返回 <see cref="RelicSurvey.Empty"/>，一次 Clone 都不做。
    /// </summary>
    public static RelicSurvey Survey(
        GameBoard board, PowerSnapshot power, RelicForecast? forecast, IReadOnlyDictionary<PlayerId, PlayerStatus> roster, ScoringVersion scoring)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(roster);
        bool anyForecast = forecast is { IsEmpty: false };
        if (!board.HasRelics && !anyForecast)
        {
            return RelicSurvey.Empty;
        }

        ImmutableArray<PlayerId> active = [.. roster.Where(kv => kv.Value == PlayerStatus.Active).Select(kv => kv.Key).Order()];
        var targets = new SortedDictionary<Coord, (RelicKind Kind, bool IsForecast)>();
        foreach (BoardRelic relic in board.RelicsOnBoard())
        {
            targets[relic.Cell] = (relic.Token.Kind, false);
        }

        if (anyForecast)
        {
            foreach (RelicSpawnSlot slot in forecast!.Slots)
            {
                if (board.Contains(slot.Cell) && board[slot.Cell].Occupant is null && board.RelicAt(slot.Cell) is null && !targets.ContainsKey(slot.Cell))
                {
                    targets[slot.Cell] = (slot.Kind, true);
                }
            }
        }

        ImmutableArray<RelicOpportunity>.Builder cells = ImmutableArray.CreateBuilder<RelicOpportunity>(targets.Count);
        int provisional = ProvisionalIdBase;
        foreach ((Coord cell, (RelicKind kind, bool isForecast)) in targets)
        {
            ImmutableSortedDictionary<PlayerId, BigInteger>.Builder gains = ImmutableSortedDictionary.CreateBuilder<PlayerId, BigInteger>();
            foreach (PlayerId player in active)
            {
                gains.Add(player, GainOf(board, power, cell, kind, isForecast ? provisional++ : null, player, scoring));
            }

            cells.Add(new RelicOpportunity(cell, isForecast, gains.ToImmutable()));
        }

        return new RelicSurvey(cells.MoveToImmutable());
    }

    /// <summary>
    /// 盘面总分（见类说明的三个桶）。<paramref name="power"/> 必须是同一盘面 <paramref name="board"/> 的势力快照（评价器已为势力增量维算过，这里复用、不再算）。
    /// </summary>
    public static BigInteger Score(
        GameBoard board, PowerSnapshot power, PlayerId me, IReadOnlyDictionary<PlayerId, PlayerStatus> roster, RelicSurvey survey)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(survey);

        BigInteger score = BigInteger.Zero;
        if (board.HasRelics)
        {
            // 桶 1 / 2：持物。军势边际贡献逐棋串读快照；望台按件读信物层（只看种类归桶，不算效果）。
            foreach ((PlayerId player, PlayerStatus status) in roster)
            {
                if (status != PlayerStatus.Active)
                {
                    continue;
                }

                BigInteger held = BigInteger.Zero;
                foreach (GroupPower group in power.Of(player).Groups)
                {
                    held += group.RelicContribution;
                }

                score += player == me ? held : -held;
            }

            foreach (BoardRelic relic in board.RelicsOnBoard())
            {
                if (relic.Token.Kind == RelicKind.Watchtower && board[relic.Cell].Occupant is { } holder
                    && roster.TryGetValue(holder.Owner, out PlayerStatus holderStatus) && holderStatus == PlayerStatus.Active)
                {
                    score += holder.Owner == me ? UncalibratedWatchtowerHeldValue : -UncalibratedWatchtowerHeldValue;
                }
            }
        }

        // 桶 3：机会格。仍为空的格才算；与某名参赛玩家棋子气边相邻即该玩家有机会（己方正、敌方负），地图格折 1/2、预告格折 1/4。
        foreach (RelicOpportunity opportunity in survey.Cells)
        {
            if (board[opportunity.Cell].Occupant is not null)
            {
                continue;
            }

            int divisor = opportunity.IsForecast ? UncalibratedForecastDivisor : UncalibratedMapRelicDivisor;
            foreach (PlayerId adjacent in AdjacentOwners(board, opportunity.Cell))
            {
                if (opportunity.GainBy.TryGetValue(adjacent, out BigInteger gain))
                {
                    BigInteger share = gain / divisor;
                    score += adjacent == me ? share : -share;
                }
            }
        }

        return score;
    }

    /// <summary>与 <paramref name="cell"/> 气边相邻的棋子的所有者（去重、按编号升序；相邻棋子由盘面的 <see cref="GameBoard.AdjacentStones"/> 给出）。</summary>
    private static ImmutableArray<PlayerId> AdjacentOwners(GameBoard board, Coord cell)
    {
        var owners = new SortedSet<PlayerId>();
        foreach (Coord stone in board.AdjacentStones(cell))
        {
            owners.Add(board[stone].Occupant!.Value.Owner);
        }

        return [.. owners];
    }

    /// <summary><paramref name="player"/> 在 <paramref name="cell"/> 落一枚普通子拾取这件信物后，己方势力的增量估计（见 <see cref="Survey"/>）。</summary>
    private static BigInteger GainOf(
        GameBoard board, PowerSnapshot power, Coord cell, RelicKind kind, int? provisionalId, PlayerId player, ScoringVersion scoring)
    {
        GameBoard clone = board.Clone();
        if (provisionalId is { } id)
        {
            clone.AttachRelic(cell, RelicToken.Spawned(id, kind));
        }
        else if (board[cell].Occupant is not null)
        {
            // 决策前挂在棋子上：按"该子被提走、信物留在原格"的盘面估（RemoveStones 不碰信物层，D1）。
            clone.RemoveStones([cell]);
        }

        clone.Place(cell, player, PieceType.Basic);
        Group merged = clone.GroupAt(cell) ?? throw new SiegeRuleException($"假落子后 {cell.ToNotation()} 上没有棋串。");
        BigInteger after = PowerCalculator.Evaluate(clone, power.Coverage, merged, scoring).RelicContribution;

        // 原本相邻的己方棋串已计入持物桶：只算新成棋串比它们多出的贡献。
        BigInteger before = BigInteger.Zero;
        var seen = new HashSet<Coord>();
        foreach (Coord stone in board.AdjacentStones(cell))
        {
            if (board[stone].Occupant!.Value.Owner == player && board.GroupAt(stone) is { } group && seen.Add(group.Stones[0]))
            {
                before += power.Of(player).Groups.First(g => g.Stones[0] == group.Stones[0]).RelicContribution;
            }
        }

        BigInteger gain = after - before;
        return kind == RelicKind.Watchtower ? gain + UncalibratedWatchtowerHeldValue : gain;
    }
}
