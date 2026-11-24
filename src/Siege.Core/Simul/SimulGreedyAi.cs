using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Core.Simul;

/// <summary>一名 AI 玩家一个回合的决策结果（供日志）：征召选取的类型、提交的批次、评价过的候选格数与"只有自己落下"时的预期势力增量。</summary>
public sealed record SimulAiDecision(
    PlayerId Player,
    ImmutableArray<PieceType> Recruited,
    ImmutableArray<Placement> Placements,
    int Evaluated,
    BigInteger ExpectedGain);

/// <summary>
/// 新玩法的最简贪心 AI（simul-a G6 / design J4）。只读三样东西：公开视图（回合开始盘面 S0、活形、势力、名册）、本人的准备句柄（手牌、征召面板、
/// 可部署格与校验）、对局计分版本；<b>不读</b>任何人的暂放，<b>不消费</b>任何随机。合法性只经句柄（<see cref="SimulPrep.Check"/>）判定，提交也走句柄。
/// </summary>
/// <remarks>
/// <para>三步：① 征召——按基础战力从高到低（同值按面板下标）选满免费选取数；② 选点——逐枚贪心：对每个候选格推演"只有自己落下"
/// （<see cref="SimulRules.ProjectAlone"/>）后的新玩法势力，取增量最大且校验通过的格，再在该格上比较手里各类型的增量取最大者；增量 ≤ 0 即停；③ 提交。</para>
/// <para>候选格：可部署格中与任一单位切比雪夫距离 ≤ <see cref="CandidateReach"/> 的格；盘面附近没有单位时取全部可部署格。并列一律按坐标字典序、类型枚举序打破。</para>
/// <para>不带地形改造、不考虑冲突与别人的部署。强度与权重问题只记录不修（"AI 放最后"）。</para>
/// </remarks>
public static class SimulGreedyAi
{
    /// <summary>候选格预筛的半径（切比雪夫距离）。</summary>
    public const int CandidateReach = 2;

    /// <summary>为 <paramref name="prep"/> 的玩家做完本回合的征召、暂放并提交，返回决策记录。</summary>
    public static SimulAiDecision Play(SimulPublicView view, SimulPrep prep)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(prep);
        if (view.Phase != SimulPhase.InProgress)
        {
            throw new SiegeRuleException($"对局阶段为 {view.Phase}，AI 无从决策。");
        }

        if (prep.IsSubmitted)
        {
            throw new SiegeRuleException($"玩家 {prep.Player} 本回合已提交。");
        }

        PlayerId me = prep.Player;
        ImmutableArray<PieceType> recruited = Recruit(prep);

        GameBoard s0 = view.Board;
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = view.Seats.ToImmutableSortedDictionary(s => s.Player, s => s.Status);
        ImmutableArray<Coord> candidates = Candidates(s0, prep.DeployableCells());
        prep.Clear();

        BigInteger start = PowerAfter(view, roster, me, []);
        BigInteger current = start;
        int evaluated = 0;
        while (prep.Placements.Length < SimulRules.DeployLimit)
        {
            Dictionary<PieceType, int> stock = Remaining(prep);
            if (stock.Count == 0)
            {
                break;
            }

            // 代表类型：剩余最多的类型（同数按枚举序）。先用它给候选格排序，选定格后再比较各类型。
            PieceType probe = stock.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            ImmutableArray<Placement> staged = prep.Placements;
            var scored = new List<(Coord Cell, BigInteger Gain)>();
            foreach (Coord c in candidates)
            {
                if (staged.Any(p => p.Coord == c))
                {
                    continue;
                }

                scored.Add((c, PowerAfter(view, roster, me, [.. staged, new Placement(c, probe)]) - current));
                evaluated++;
            }

            Coord? chosen = null;
            foreach ((Coord cell, BigInteger gain) in scored.OrderByDescending(s => s.Gain).ThenBy(s => s.Cell))
            {
                if (gain <= BigInteger.Zero)
                {
                    break;
                }

                prep.Stage(cell, probe);
                if (prep.Check().IsValid)
                {
                    chosen = cell;
                    break;
                }

                prep.Unstage(cell);
            }

            if (chosen is not { } at)
            {
                break;
            }

            // 在选定格上比较手里各类型（同增量按枚举序）。
            PieceType best = probe;
            BigInteger bestPower = PowerAfter(view, roster, me, prep.Placements);
            foreach (PieceType type in stock.Keys.Order())
            {
                if (type == probe)
                {
                    continue;
                }

                prep.ChangeType(at, type);
                BigInteger power = prep.Check().IsValid ? PowerAfter(view, roster, me, prep.Placements) : BigInteger.MinusOne;
                evaluated++;
                if (power > bestPower || (power == bestPower && type < best))
                {
                    best = type;
                    bestPower = power;
                }
            }

            prep.ChangeType(at, best);
            current = bestPower;
        }

        ImmutableArray<Placement> placements = prep.Placements;
        SimulBatchCheck check = prep.Submit();
        if (!check.IsValid)
        {
            throw new SiegeRuleException($"AI {me} 的批次未通过校验：{check.FirstReason}");
        }

        return new SimulAiDecision(me, recruited, placements, evaluated, current - start);
    }

    /// <summary>征召：按基础战力从高到低（同值按面板下标）逐个选取可选的候选，选满免费选取数为止。</summary>
    private static ImmutableArray<PieceType> Recruit(SimulPrep prep)
    {
        RecruitPanelView panel = prep.HandPhase == TurnPhase.Recruit ? prep.Panel() : prep.EnterRecruit();
        var picked = ImmutableArray.CreateBuilder<PieceType>();
        foreach (RecruitCandidateView candidate in panel.Candidates.OrderByDescending(c => SimulRules.BasePower(c.Type)).ThenBy(c => c.Index))
        {
            RecruitPanelView now = prep.Panel();
            if (now.PicksMade >= now.FreePickCount)
            {
                break;
            }

            if (now.Candidates[candidate.Index].IsSelectable)
            {
                prep.Pick(candidate.Index);
                picked.Add(candidate.Type);
            }
        }

        return picked.ToImmutable();
    }

    /// <summary>候选格：可部署格中离任一单位切比雪夫距离 ≤ <see cref="CandidateReach"/> 的格；附近一个单位都没有时取全部可部署格。坐标序。</summary>
    private static ImmutableArray<Coord> Candidates(GameBoard s0, ImmutableArray<Coord> deployable)
    {
        ImmutableArray<Coord> near =
        [
            .. deployable.Where(c =>
            {
                for (int dy = -CandidateReach; dy <= CandidateReach; dy++)
                {
                    for (int dx = -CandidateReach; dx <= CandidateReach; dx++)
                    {
                        int x = c.X + dx;
                        int y = c.Y + dy;
                        if (x >= 0 && y >= 0 && x < s0.Width && y < s0.Height && s0[new Coord(x, y)].Occupant is not null)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }),
        ];
        return near.IsEmpty ? deployable : near;
    }

    /// <summary>手里每类型扣掉已暂放之后的剩余数（只列 &gt; 0 的）。</summary>
    private static Dictionary<PieceType, int> Remaining(SimulPrep prep)
    {
        HandPrivateView hand = prep.Hand();
        var stock = new Dictionary<PieceType, int>();
        foreach (PieceType type in hand.Types)
        {
            int left = hand.CountOf(type) - prep.Placements.Count(p => p.Type == type);
            if (left > 0)
            {
                stock[type] = left;
            }
        }

        return stock;
    }

    /// <summary>"只有自己落下" <paramref name="placements"/> 之后，本人的新玩法势力。</summary>
    private static BigInteger PowerAfter(SimulPublicView view, IReadOnlyDictionary<PlayerId, PlayerStatus> roster, PlayerId me, IEnumerable<Placement> placements)
    {
        GameBoard board = SimulRules.ProjectAlone(view.Board, me, placements);
        PowerSnapshot @base = PowerCalculator.Compute(board, roster, view.ScoringVersion);
        return SimulRules.Power(board, LifeShapeReport.Analyze(board), @base).Of(me).Total;
    }
}
