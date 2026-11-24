using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Core.Simul;

/// <summary>
/// 新玩法（同时决策，simul-a）的规则纯函数：部署校验（J2-3）、同格冲突与 d6（J2-5）、活形锁定格与势力修正（J2-8）、占满判定（J2-9）。
/// 输入只含回合开始盘面 S0 / 结算后盘面与调用方给出的批次；不写任何对局状态。
/// </summary>
public static class SimulRules
{
    /// <summary>
    /// 每回合部署上限（Q07【现有】3）：恒取现有分阶段表的第一阶段值（<see cref="EffectSnapshot.BaseDeployLimitFor"/> 第 1 回合），不随回合升到 4 / 5。
    /// 不另写字面量——分阶段部署上限的数值只在那张表里（守门 <c>批次数量与库存约束Tests.分阶段基础部署上限唯一实现</c>）。
    /// </summary>
    public static int DeployLimit => EffectSnapshot.BaseDeployLimitFor(1);

    /// <summary>冲突掷骰子流名（J2-5）：消费顺序见 <see cref="ResolveConflicts"/>。登记于 determinism.md。</summary>
    public const string DiceStream = "simul-dice";

    /// <summary>d6 的面数。</summary>
    public const int DieFaces = 6;

    /// <summary>八邻禁落的公共区编号：不属于任何出生岛（出生座位）的格合为一个区（K3）。</summary>
    public const int PublicZone = -1;

    /// <summary>胜利名额（Q07）：2 人局 1 名、3 人局 1 名、4 人局 2 名。</summary>
    public static int WinnerSlots(int players) => players switch
    {
        2 or 3 => 1,
        4 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(players), players, "新玩法只支持 2–4 人局。"),
    };

    /// <summary>冲突的基础战力（G1）：取现有基础军势（堡垒 4，其余 1）。</summary>
    public static int BasePower(PieceType type) => PieceEffects.BasePower(type);

    /// <summary>八邻禁落的"区"（K3）：所在出生岛（出生座位整块）的编号；不在任何出生岛内为 <see cref="PublicZone"/>。</summary>
    public static int ZoneOf(MapData map, Coord c)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.BirthBoardAt(c) ?? PublicZone;
    }

    /// <summary>该格是否为活形内部格（已确定活形的眼空间格，不论属于谁）。新玩法里这些格双方都不能部署（G2），也不需要补满（J2-9）。</summary>
    public static bool IsLocked(LifeShapeReport life, Coord c)
    {
        ArgumentNullException.ThrowIfNull(life);
        return life.EyeSpaceAt(c) is { } space && life.IsProtected(space);
    }

    /// <summary>两格的切比雪夫距离。</summary>
    public static int Chebyshev(Coord a, Coord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// 部署校验（J2-3）：只按回合开始盘面 <paramref name="s0"/>（及其活形分析 <paramref name="life"/>）与本人批次判定，<b>不判自杀、不判同形</b>。
    /// 逐枚：在盘内 → 非活形内部格（双方禁入）→ 在合法范围内（<see cref="PublicRules.LegalRange"/>，前 3 回合只限出生座位）→ S0 上是空格 → 批内不重格；
    /// 不挂带入信物（K1）；整批：枚数 ≤ <see cref="DeployLimit"/>、每类 ≤ 库存；八邻禁落（同区两落点切比雪夫距离 ≥ 2）；改造只有匠人能带、按 S0 地形与改造范围合法、批内目标不重。
    /// </summary>
    public static SimulBatchCheck Validate(
        GameBoard s0,
        LifeShapeReport life,
        PlayerId player,
        int birthZone,
        int round,
        IReadOnlyDictionary<PieceType, int> stock,
        IReadOnlyList<Placement> placements)
    {
        ArgumentNullException.ThrowIfNull(s0);
        ArgumentNullException.ThrowIfNull(life);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(placements);

        var issues = ImmutableArray.CreateBuilder<SimulBatchIssue>();
        IReadOnlySet<Coord> range = PublicRules.LegalRange(s0, birthZone, player, life, round);
        var seen = new HashSet<Coord>();
        foreach (Placement p in placements)
        {
            Coord c = p.Coord;
            string at = c.ToNotation();
            if (!s0.Contains(c))
            {
                issues.Add(new SimulBatchIssue(SimulRejectKind.OutOfRange, c, $"{at} 不在地图上。"));
            }
            else if (IsLocked(life, c))
            {
                issues.Add(new SimulBatchIssue(SimulRejectKind.Locked, c, $"{at} 是活形内部格，已锁定：双方都不能部署。"));
            }
            else if (!range.Contains(c))
            {
                string why = round <= PublicRules.BuildProtectionRounds ? $"前 {PublicRules.BuildProtectionRounds} 回合只能部署在自己的出生岛上" : "不是可部署格";
                issues.Add(new SimulBatchIssue(SimulRejectKind.OutOfRange, c, $"{at} 不在可部署范围内：{why}。"));
            }
            else if (s0[c].Occupant is not null)
            {
                issues.Add(new SimulBatchIssue(SimulRejectKind.Occupied, c, $"{at} 在回合开始时已有单位。"));
            }

            if (!seen.Add(c))
            {
                issues.Add(new SimulBatchIssue(SimulRejectKind.DuplicateCell, c, $"{at} 暂放了两次。"));
            }

            if (p.AttachCarried is not null)
            {
                issues.Add(new SimulBatchIssue(SimulRejectKind.RelicNotAllowed, c, $"{at} 挂了带入信物：新玩法本段不使用信物。"));
            }
        }

        if (placements.Count > DeployLimit)
        {
            issues.Add(new SimulBatchIssue(SimulRejectKind.OverLimit, null, $"本回合最多部署 {DeployLimit} 个单位，暂放了 {placements.Count} 个。"));
        }

        foreach (IGrouping<PieceType, Placement> byType in placements.GroupBy(p => p.Type).OrderBy(g => g.Key))
        {
            int have = stock.TryGetValue(byType.Key, out int n) ? n : 0;
            if (byType.Count() > have)
            {
                issues.Add(new SimulBatchIssue(SimulRejectKind.OverStock, null,
                    $"手牌里{BatchFailure.DisplayName(byType.Key)}只有 {have} 个，暂放了 {byType.Count()} 个。"));
            }
        }

        for (int j = 1; j < placements.Count; j++)
        {
            for (int i = 0; i < j; i++)
            {
                Coord a = placements[i].Coord;
                Coord b = placements[j].Coord;
                if (a != b && Chebyshev(a, b) < 2 && s0.Contains(a) && s0.Contains(b) && ZoneOf(s0.Map, a) == ZoneOf(s0.Map, b))
                {
                    issues.Add(new SimulBatchIssue(SimulRejectKind.Crowded, b,
                        $"{b.ToNotation()} 与 {a.ToNotation()} 在同一座岛上相邻（含斜邻）：同一回合自己的落点之间至少隔一格。"));
                }
            }
        }

        IReadOnlySet<Coord>? editScope = null;
        var edits = new HashSet<TerrainEdit>();
        foreach (Placement p in placements)
        {
            if (p.Edit is not { } edit)
            {
                continue;
            }

            if (p.Type != PieceType.Artisan)
            {
                issues.Add(new SimulBatchIssue(SimulRejectKind.EditNotArtisan, p.Coord, $"{p.Coord.ToNotation()} 不是匠人，不能带地形改造。"));
                continue;
            }

            editScope ??= PublicRules.EditScope(s0, birthZone, round);
            if (TerrainEditRules.Reject(s0.Map, p.Coord, edit, editScope) is { } reason)
            {
                issues.Add(new SimulBatchIssue(SimulRejectKind.EditIllegal, p.Coord, $"{p.Coord.ToNotation()} 的改造 {edit} 不合法：{reason}"));
            }

            if (!edits.Add(edit))
            {
                issues.Add(new SimulBatchIssue(SimulRejectKind.EditDuplicate, p.Coord, $"改造 {edit} 在本回合批次里出现了两次。"));
            }
        }

        return new SimulBatchCheck(issues.ToImmutable());
    }

    /// <summary>
    /// 冲突中一名投入者在 <paramref name="s0"/> 上的支援数：与 <paramref name="cell"/> 有气边相接、属于 <paramref name="player"/> 的单位数（每个 +1）。
    /// 只读回合开始盘面——本回合任何新部署都不算（E2）。
    /// </summary>
    public static int SupportOf(GameBoard s0, PlayerId player, Coord cell)
    {
        ArgumentNullException.ThrowIfNull(s0);
        int support = 0;
        foreach (Coord n in s0.LibertyNeighbors(cell))
        {
            if (s0[n].Occupant is { } o && o.Owner == player)
            {
                support++;
            }
        }

        return support;
    }

    /// <summary>
    /// 同格冲突（J2-5 / Q14）：两名及以上玩家选了同一格即发生冲突。战力 = 基础战力 + S0 上的支援数；最高战力唯一者胜；
    /// 最高战力并列时只有这几人掷 d6（<paramref name="dice"/>，即 <see cref="DiceStream"/>），点数低的出局，最高点仍并列只让这几人重掷。
    /// 消费顺序：冲突格按坐标字典序（<see cref="Coord.CompareTo"/>，先行后列），同一格内按玩家编号升序，每次 <c>NextInt(6) + 1</c>；没有并列时不消费。
    /// </summary>
    /// <param name="s0">回合开始盘面（支援数只读它）。</param>
    /// <param name="batches">揭晓的全部批次。</param>
    /// <param name="dice">冲突掷骰子流（整局持有同一实例）。</param>
    public static ImmutableArray<SimulConflict> ResolveConflicts(GameBoard s0, IEnumerable<SimulRevealedBatch> batches, RandomStream dice)
    {
        ArgumentNullException.ThrowIfNull(s0);
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(dice);

        var byCell = new SortedDictionary<Coord, List<(PlayerId Player, PieceType Type)>>();
        foreach (SimulRevealedBatch batch in batches)
        {
            foreach (Placement p in batch.Placements)
            {
                if (!byCell.TryGetValue(p.Coord, out List<(PlayerId, PieceType)>? list))
                {
                    list = [];
                    byCell.Add(p.Coord, list);
                }

                list.Add((batch.Player, p.Type));
            }
        }

        var conflicts = ImmutableArray.CreateBuilder<SimulConflict>();
        foreach ((Coord cell, List<(PlayerId Player, PieceType Type)> entries) in byCell)
        {
            if (entries.Count < 2)
            {
                continue;
            }

            ImmutableArray<SimulConflictEntrant> entrants =
            [
                .. entries.OrderBy(e => e.Player).Select(e => new SimulConflictEntrant(e.Player, e.Type, BasePower(e.Type), SupportOf(s0, e.Player, cell))),
            ];
            int top = entrants.Max(e => e.Power);
            List<PlayerId> contenders = [.. entrants.Where(e => e.Power == top).Select(e => e.Player)];
            var rolls = ImmutableArray.CreateBuilder<ImmutableArray<SimulDieRoll>>();
            while (contenders.Count > 1)
            {
                ImmutableArray<SimulDieRoll> round = [.. contenders.Select(p => new SimulDieRoll(p, dice.NextInt(DieFaces) + 1))];
                rolls.Add(round);
                int best = round.Max(r => r.Value);
                contenders = [.. round.Where(r => r.Value == best).Select(r => r.Player)];
            }

            conflicts.Add(new SimulConflict(cell, entrants, rolls.ToImmutable(), contenders[0]));
        }

        return conflicts.ToImmutable();
    }

    /// <summary>
    /// 新玩法的势力（Q06 / J2-8）：现有势力算法（<paramref name="base"/>，独占空格 + 军势）之外，活形内部空格算所有者领地——
    /// 领地 = 现有独占空格里不是别人活形内部格的那些，再并上本人的活形内部格；计分仍滤掉荒漠（<see cref="PowerCalculator.ScoresTerritory"/>）；
    /// 军势原样。只对新玩法生效，现有势力算法不动。
    /// </summary>
    /// <remarks>
    /// 修正的必要性（J2-8 核对结论）：现有独占空格按覆盖关系判定，只覆盖棋子的四邻（及岩台远格 / 隔一格深水）。
    /// 眼空间最多 12 格，离所有者单位 ≥ 2 格的内部格无人覆盖（中立），林地格不接收覆盖，从崖上或隔水覆盖进来的对手会把内部格变成争议格——
    /// 这些格在现有算法下都不算所有者的独占空格。
    /// </remarks>
    public static SimulPowerSnapshot Power(GameBoard board, LifeShapeReport life, PowerSnapshot @base)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(life);
        ArgumentNullException.ThrowIfNull(@base);

        var lockedOwner = new SortedDictionary<Coord, PlayerId>();
        foreach (EyeSpace space in life.EyeSpaces)
        {
            if (!life.IsProtected(space))
            {
                continue;
            }

            foreach (Coord c in space.Cells)
            {
                lockedOwner[c] = space.Owner;
            }
        }

        ImmutableArray<SimulPlayerPower>.Builder players = ImmutableArray.CreateBuilder<SimulPlayerPower>(@base.Players.Length);
        foreach (PlayerPower p in @base.Players)
        {
            var territory = new SortedSet<Coord>();
            foreach (Coord c in p.ExclusiveCells)
            {
                if (!lockedOwner.TryGetValue(c, out PlayerId owner) || owner == p.Player)
                {
                    territory.Add(c);
                }
            }

            ImmutableArray<Coord> locked = [.. lockedOwner.Where(kv => kv.Value == p.Player).Select(kv => kv.Key)];
            territory.UnionWith(locked);
            ImmutableArray<Coord> cells = [.. territory];
            ImmutableArray<Coord> scored = [.. cells.Where(c => PowerCalculator.ScoresTerritory(board.Map, c))];
            players.Add(new SimulPlayerPower(p.Player, p.Status, cells, scored, locked, p.GroupScore, p));
        }

        return new SimulPowerSnapshot(@base, players.MoveToImmutable());
    }

    /// <summary>占满（J2-9 第 2 条）：盘面上不存在"非活形锁定的空可部署格"。活形内部格不需要补。</summary>
    public static bool IsFull(GameBoard board, LifeShapeReport life)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(life);
        foreach (Coord c in board.AllCoords())
        {
            if (board[c].IsPlayableEmpty && !IsLocked(life, c))
            {
                return false;
            }
        }

        return true;
    }
}
