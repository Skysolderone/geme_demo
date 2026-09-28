using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision（expert-strength）—— Requirement: 前瞻的近似两层加分。
/// 依赖 AI 实际走法的断言一律写死权重（<see cref="LookaheadFixtures.Weights"/>）与停手阈值。
/// </summary>
public class 前瞻的近似两层加分Tests
{
    private static MatchFlow Contact() => 前瞻模拟的公平信息Tests.ContactPosition();

    [Fact]
    public void 加分按千分数取整()
    {
        // 规格算例：两层权重 500‰、最大单点增量 131 → 两层加分 ⌊500 × 131 / 1000⌋ = 65（65.5 向下取整）。另取 250‰ → 32（32.75）、3 → 0（0.75）。
        // 真实决策：同一局面、同一前瞻集上，500‰ 的每个候选加分 = 1000‰ 的加分（即最大单点增量本身）的一半向下取整。
        // 变异 E-T3（四舍五入代替向下取整）→ 见段 A 实施记录。
        Assert.Equal(new BigInteger(65), ExpertLookahead.TwoPlyBonus(500, 131));
        Assert.Equal(new BigInteger(32), ExpertLookahead.TwoPlyBonus(250, 131));
        Assert.Equal(BigInteger.Zero, ExpertLookahead.TwoPlyBonus(250, 3));
        Assert.Equal(new BigInteger(131), ExpertLookahead.TwoPlyBonus(1000, 131));

        LookaheadRecord full = Record(Decide(Contact(), AiDifficulty.Expert, StrengthConfig(permille: 1000, supplement: 0)));
        LookaheadRecord half = Record(Decide(Contact(), AiDifficulty.Expert, StrengthConfig(permille: 500, supplement: 0)));
        Assert.Equal(LookaheadStatus.Applied, full.Status);
        Assert.Contains(full.Entries, e => e.TwoPlyBonus > 0);
        Assert.Equal(full.Entries.Select(e => e.CandidateKey), half.Entries.Select(e => e.CandidateKey));
        Assert.Equal(full.Entries.Select(e => e.TwoPlyBonus / 2), half.Entries.Select(e => e.TwoPlyBonus));
    }

    [Fact]
    public void 下限为0()
    {
        // 最大单点增量不大于 0 → 两层加分为 0（先取下限 0 再乘权重、再向下取整），前瞻后分数等于一层分数。
        // 变异 E-T2：tasks 1.5 所列"先乘权重再取下限 0"在整数运算下与正确实现逐值相同（λ ≥ 0、BigInteger 除法向零截断），是等价变异；
        // 改用替代变异"去掉下限 0"→ 见段 A 实施记录。
        Assert.Equal(BigInteger.Zero, ExpertLookahead.TwoPlyBonus(1000, -50));
        Assert.Equal(BigInteger.Zero, ExpertLookahead.TwoPlyBonus(500, -1));
        Assert.Equal(BigInteger.Zero, ExpertLookahead.TwoPlyBonus(1000, 0));
    }

    [Fact]
    public void 两层加分改变选择()
    {
        // 规格算例：两层权重 500‰，前瞻集中 A 排在 B 之前、一层分数相同；A 之后最大单点增量 0、B 之后 200 → B 的前瞻后分数比 A 高 100，专家选 B。
        BigInteger oneLayer = 400;
        BigInteger a = oneLayer + ExpertLookahead.TwoPlyBonus(500, 0);
        BigInteger b = oneLayer + ExpertLookahead.TwoPlyBonus(500, 200);
        Assert.Equal(new BigInteger(100), b - a);
        Assert.Equal(1, ExpertLookahead.SelectIndex([a, b]));

        // 真实局面：v5 种子 1 的固定局面集上，两层加分（1000‰、多样候选关闭）至少改变一次选择（相对一层配置），且改变时被选者的加分严格更高。
        int compared = 0;
        int changed = 0;
        ProbePositions(every: 2, (match, batch) =>
        {
            (HeuristicTurnController one, _, _) = Shadow(match, batch.Context, AiDifficulty.Expert, ExpertConfig(PassThreshold));
            (HeuristicTurnController two, _, _) = Shadow(match, batch.Context, AiDifficulty.Expert, StrengthConfig(PassThreshold, supplement: 0));
            LookaheadRecord record = Record(two);
            compared++;
            if (one.LastChoice?.Key != two.LastChoice?.Key)
            {
                changed++;
                Assert.Equal(record.Entries.Select(e => e.CandidateKey), Record(one).Entries.Select(e => e.CandidateKey));
                LookaheadEntry oneLayerChoice = record.Entries.Single(e => e.CandidateKey == one.LastChoice!.Key);
                Assert.True(record.Chosen!.TwoPlyBonus > oneLayerChoice.TwoPlyBonus, record.ToText());
            }
        }, 1);

        Assert.True(compared >= 10, $"只比较了 {compared} 个局面");
        Assert.True(changed >= 1, $"{compared} 个局面上两层加分一次都没改变选择");
    }

    [Fact]
    public void 按下一大回合的口径枚举()
    {
        // 专家（P0）在第 3 大回合（保护期末）做部署决策 → 两层加分的单点按第 4 大回合枚举：落点为全图可落子格扣除 P0 在局面上的禁入格（不再限于出生区），
        // 部署上限为 4 加 P0 控制的已揭示军令强度（C3 的 +2 军令，P2 先完成一次结算使其揭示）= 6。
        // 变异 E-T4（下一手按当前大回合：出生区、上限 3 + 2）→ 见段 A 实施记录。
        MatchFlow match = MatchFixtures.Started(relics: [("C3", RelicFixtures.Command(2))])
            .AtRound(3, [P2, P0, P1, P3])
            .Stones(P0, "C3", "B2").Stones(P1, "H2").Stones(P3, "H8");
        match.PlayTurn("B8");
        Assert.Equal(P0, match.CurrentPlayer);
        (ExpertLookahead engine, MatchPublicView view, BatchContext mine, _) = Engine(match, StrengthConfig());
        RelicPublicState c3 = Assert.Single(view.Relics);
        Assert.True(c3.IsRevealed);
        Assert.True(c3.Control.GrantsEffectTo(P0));
        Assert.Equal(3, view.MajorRound);

        BatchContext next = engine.NextMoveContext(view, mine)!;

        Assert.Equal(P0, next.Player);
        Assert.Equal(4 + 2, next.DeployLimit);
        Coord[] expected = [.. view.Board.AllCoords().Where(c => view.Board[c].Terrain == Terrain.Playable).Except(view.LifeShape.ForbiddenCellsFor(P0)).Order()];
        Assert.Equal(expected, next.LegalRange.Order());
        Assert.Contains(TestMaps.At("E5"), next.LegalRange);   // 出生区之外
    }

    [Fact]
    public void 只用本人持有的类型()
    {
        // 类型取专家决策起点手中数量 > 0 的全部类型、不扣除候选 c 用掉的枚数（D5 第 1 条：下一大回合的征募可能补回），不带改造目标。
        // ① 构造口径：决策起点手中普通子 × 1、堡垒子 × 2 → 两层单点枚举普通子与堡垒子两种。
        // ② 真实决策（部署上限 1，候选都是单子）：手中普通子 × 1、堡垒子 × 1（每个候选都用掉唯一的那一枚）与普通子 × 2、堡垒子 × 2 两局，
        //    前瞻记录（含两层加分与两层预演次数）逐字相同——扣除 c 的落子就会让前一局的类型集合少一种。
        // 变异 E-T5（类型集合扣除 c 的落子）→ 见段 A 实施记录。
        (ExpertLookahead engine, MatchPublicView view, BatchContext mine, _) = Engine(Contact(), StrengthConfig());
        BatchContext held = mine with { Stock = new Dictionary<PieceType, int> { [PieceType.Basic] = 1, [PieceType.Fortress] = 2, [PieceType.Line] = 0 } };
        BatchContext next = engine.NextMoveContext(view, held)!;
        Assert.Equal([PieceType.Basic, PieceType.Fortress], next.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order());

        LookaheadRecord Run(int count)
        {
            MatchFlow match = Contact();
            match.Debug.SeedHand(P0, (PieceType.Basic, count), (PieceType.Fortress, count));
            match.SetDeployLimit(1);
            StagedBatch batch = match.OpenDeploy();
            Assert.Equal(count, batch.Context.StockOf(PieceType.Basic));
            Assert.Equal(count, batch.Context.StockOf(PieceType.Fortress));
            HeuristicTurnController ai = HeuristicAi.Create(match, P0, AiDifficulty.Expert, Weights, StrengthConfig(supplement: 0));
            ai.Deploy(batch, match.Rehearse);
            return Record(ai);
        }

        LookaheadRecord single = Run(1);
        LookaheadRecord doubled = Run(2);
        Assert.Equal(LookaheadStatus.Applied, single.Status);
        Assert.All(single.Entries, e => Assert.Single(CoordsOf(e.CandidateKey)));
        Assert.True(single.TwoPlyRehearsals > 0);
        Assert.Equal(doubled.ToText(), single.ToText());
    }

    [Fact]
    public void 两层权重为0时不做扫描()
    {
        // 两层权重为 0 → 每个候选的两层加分为 0，不做任何两层单点预演；模拟对手的预演次数与打开两层加分时相同（前瞻记录的预演次数与改动前的专家相同）。
        // 变异 E-T7（两层权重为 0 时照样扫描，只是乘 0）→ 见段 A 实施记录。
        LookaheadRecord off = Record(Decide(Contact(), AiDifficulty.Expert, StrengthConfig(permille: 0, supplement: 0)));
        LookaheadRecord on = Record(Decide(Contact(), AiDifficulty.Expert, StrengthConfig(permille: 1000, supplement: 0)));

        Assert.Equal(LookaheadStatus.Applied, off.Status);
        Assert.All(off.Entries, e => Assert.Equal(BigInteger.Zero, e.TwoPlyBonus));
        Assert.Equal(0, off.TwoPlyRehearsals);
        Assert.True(on.TwoPlyRehearsals > 0);
        Assert.Equal(on.SimulatedRehearsals, off.SimulatedRehearsals);
    }

    [Fact]
    public void 无对手的候选在B1上计加分()
    {
        // 第 2 大回合、只剩 P0（专家）与 P1；P1 在盘上只有 B2 一子（曾建立正势力），只剩一口气 B3。部署上限 1：候选 B3 提走 P1 的全部棋子（之后没有下一名对手），
        // 其余候选之后下一名对手仍是 P1 → B3 不做模拟，其两层加分在 B1 上计算（为正）；其余候选在 B2 上计算。
        // 变异 E-T6（无对手的候选记 0，即 E5 补丁口径）→ 见段 A 实施记录。
        MatchFlow Position()
        {
            MatchFlow m = 前瞻中的下一名对手Tests.LastOpponentPosition();
            m.SetDeployLimit(1);
            return m;
        }

        LookaheadRecord record = Record(Decide(Position(), AiDifficulty.Expert, StrengthConfig(permille: 1000, supplement: 0)));
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        LookaheadEntry a = Assert.Single(record.Entries, e => e.CandidateKey == "B3:Basic");
        Assert.Null(a.Responder);
        Assert.True(a.TwoPlyBonus > 0, record.ToText());
        Assert.Equal(a.ScoreBefore + a.TwoPlyBonus, a.ScoreAfter);
        Assert.Contains(record.Entries, e => e.Responder == P1);

        // A 的加分就是 B1（投影视图）上的两层加分：独立复算 B1 与同形历史（决策起点盘面 + B1）。
        MatchFlow match = Position();
        (ExpertLookahead engine, MatchPublicView view, BatchContext mine, BoardHistory history) = Engine(match, StrengthConfig(permille: 1000, supplement: 0));
        RehearsalResult b1 = BatchRehearsal.Rehearse(match.Board, mine, [new Placement(TestMaps.At("B3"), PieceType.Basic)], match.History);
        MatchPublicView projected = ExpertLookahead.Project(view, b1.ProjectedBoard!);
        history.Record(projected.BoardSerialized);
        Assert.Equal(a.TwoPlyBonus, engine.TwoPlyBonusOn(projected, history, mine));
    }

    [Fact]
    public void 全部无对手时不计加分()
    {
        // 前瞻集中每个候选之后都没有下一名对手（每个候选都提走 P1 在盘上的全部棋子）→ 每个候选都不做模拟、不计两层加分，前瞻后分数等于自身总分，选择与高难相同。
        HeuristicTurnController hard = DecideHard(前瞻中的下一名对手Tests.LastOpponentPosition());
        HeuristicTurnController expert = Decide(前瞻中的下一名对手Tests.LastOpponentPosition(), AiDifficulty.Expert, StrengthConfig(permille: 1000, supplement: 0));
        LookaheadRecord record = Record(expert);

        Assert.True(record.Entries.Length >= 2, record.ToText());
        Assert.All(record.Entries, e =>
        {
            Assert.Null(e.Responder);
            Assert.Equal(BigInteger.Zero, e.TwoPlyBonus);
            Assert.Equal(e.ScoreBefore, e.ScoreAfter);
        });
        Assert.Equal(0, record.TwoPlyRehearsals);
        Assert.Equal(0, record.SimulatedRehearsals);
        Assert.Equal(hard.LastChoice!.Key, expert.LastChoice!.Key);
    }

    [Fact]
    public void 专家出局时加分为0()
    {
        // 某候选之后，模拟回应提走专家在盘上的全部棋子（专家曾建立正势力）→ 专家在 B2 上已出局，两层加分为 0、不做两层单点预演。
        // 以 B2 = 决策起点盘面去掉专家唯一的棋子构造；对照：同一引擎在决策起点（专家未出局）上的加分为正。
        MatchFlow match = AiFixtures.Round5().Stones(P0, "E5").Stones(P1, "B2").Stones(P2, "B8").Stones(P3, "H2");
        (ExpertLookahead engine, MatchPublicView view, BatchContext mine, BoardHistory history) = Engine(match, StrengthConfig());
        GameBoard b2 = view.Board.Clone();
        b2.Clear(TestMaps.At("E5"));
        MatchPublicView after = ExpertLookahead.Project(view, b2);
        Assert.Equal(PlayerStatus.Eliminated, after.Players.Single(p => p.Player == P0).Status);

        Assert.Equal(BigInteger.Zero, engine.TwoPlyBonusOn(after, history, mine));
        Assert.Equal(0, engine.TwoPlyRehearsals);
        Assert.True(engine.TwoPlyBonusOn(view, history, mine) > 0);
        Assert.True(engine.TwoPlyRehearsals > 0);
    }
}
