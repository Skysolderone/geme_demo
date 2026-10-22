using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Sim.Config;
using Siege.Sim.Running;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision（expert-lookahead）—— Requirement: 前瞻模拟的公平信息</summary>
public class 前瞻模拟的公平信息Tests
{
    /// <summary>第 5 大回合、顺序 P0（专家）→ P3，P0 与下一名对手 P1 在盘面中部接触。</summary>
    internal static MatchFlow ContactPosition(params (PieceType Type, int Count)[] p1Hand)
    {
        MatchFlow match = AiFixtures.Round5()
            .Stones(P0, "E6", "F6", "F5")
            .Stones(P1, "G6", "G7", "F7")
            .Stones(P2, "D9").Stones(P3, "J4");
        match.Debug.SeedHand(P1, p1Hand);
        return match;
    }

    [Fact]
    public void 对手真实手牌不同结果不变()
    {
        // 两局的盘面、顺序与信物逐项相同，只有下一名对手 P1 的真实手牌不同：普通子 × 2 / 普通子 × 9 与堡垒子 × 3。
        // 模拟对手按"部署上限枚普通子"建模（D5），前瞻集、每个候选的模拟回应、前瞻后分数与最终选择逐项相同。
        // 样本口径：至少一个模拟回应落了 3 枚以上——真实手牌只有 2 枚的那一局若被读到，回应必然不同。
        // 变异 M-A6a（经 HeuristicAi.Create 把对手真实手牌注入模拟库存）→ 见段 A 实施记录。
        HeuristicTurnController few = DecideExpert(ContactPosition((PieceType.Basic, 2)));
        HeuristicTurnController many = DecideExpert(ContactPosition((PieceType.Basic, 9), (PieceType.Fortress, 3)));
        LookaheadRecord a = Record(few);
        LookaheadRecord b = Record(many);

        Assert.Equal(LookaheadStatus.Applied, a.Status);
        Assert.All(a.Entries, e => Assert.Equal(P1, e.Responder));
        Assert.Contains(a.Entries, e => CoordsOf(e.ResponseKey!).Length >= 3);
        Assert.Equal(a.ToText(), b.ToText());
        Assert.Equal(few.LastChoice!.Key, many.LastChoice!.Key);

        // expert-strength：打开多样候选与两层加分（显式 S 8、λ 1000‰——专家预设已退回一层，阈值同上）后同样逐项相同——前瞻集（含来源）、两层加分、前瞻后分数与选择。
        // 样本口径：至少一个候选的两层加分为正。变异 E-F1（两层扫描经测试接缝读对手真实手牌）→ 见段 A 实施记录。
        LookaheadRecord presetFew = Record(Decide(ContactPosition((PieceType.Basic, 2)), AiDifficulty.Expert, StrengthConfig()));
        LookaheadRecord presetMany = Record(Decide(ContactPosition((PieceType.Basic, 9), (PieceType.Fortress, 3)), AiDifficulty.Expert, StrengthConfig()));
        Assert.Contains(presetFew.Entries, e => e.TwoPlyBonus > 0);
        Assert.Equal(presetFew.ToText(), presetMany.ToText());
    }

    [Fact]
    public void 其余对手的隐藏信息不影响两层加分()
    {
        // 两局的公开状态与专家本人手牌逐项相同，只有不是下一名对手的另一名对手（P2；下一名对手是 P1）的真实手牌不同：普通子 × 2 / 普通子 × 9 与堡垒子 × 3。
        // 专家（多样补充与两层加分都打开）每个候选的两层加分、前瞻记录与最终选择逐项相同。
        // 其余对手的征募面板只在其本人的小回合生成，决策起点并不存在，无从另设（记入段 A 实施记录）。
        // 变异 E-F1（两层扫描经测试接缝读对手真实手牌）→ 见段 A 实施记录。
        MatchFlow Position(params (PieceType Type, int Count)[] p2Hand)
        {
            MatchFlow match = ContactPosition((PieceType.Basic, 4));
            match.Debug.SeedHand(P2, p2Hand);
            return match;
        }

        HeuristicTurnController few = Decide(Position((PieceType.Basic, 2)), AiDifficulty.Expert, StrengthConfig());
        HeuristicTurnController many = Decide(Position((PieceType.Basic, 9), (PieceType.Fortress, 3)), AiDifficulty.Expert, StrengthConfig());
        LookaheadRecord a = Record(few);
        LookaheadRecord b = Record(many);

        Assert.Equal(LookaheadStatus.Applied, a.Status);
        Assert.All(a.Entries, e => Assert.NotEqual(P2, e.Responder));
        Assert.Contains(a.Entries, e => e.TwoPlyBonus > 0);
        Assert.Equal(a.Entries.Select(e => e.TwoPlyBonus), b.Entries.Select(e => e.TwoPlyBonus));
        Assert.Equal(a.ToText(), b.ToText());
        Assert.Equal(few.LastChoice!.Key, many.LastChoice!.Key);
    }

    /// <summary>
    /// 第 5 大回合、顺序 P2 → P0（专家）→ P1 → P3。E5 是 +2 军令、G5 是普通军令：P1 占据 E5、以 G4 唯一覆盖 G5；
    /// P2 先落一子完成一次结算，两枚军令随之揭示。决策起点 P1 控制两枚，公开部署上限 4 + 2 + 1 = 7。
    /// </summary>
    private static MatchFlow CommandPosition()
    {
        MatchFlow match = LinkModeFixtures.PlainStarted(relics: [("F6", RelicFixtures.Command(2)), ("H6", RelicFixtures.Command())])
            .AtRound(5, [P2, P0, P1, P3])
            .Stones(P0, "E4").Stones(P1, "F6", "H5").Stones(P3, "J9");
        match.PlayTurn("D9");
        Assert.Equal(P0, match.CurrentPlayer);
        return match;
    }

    [Fact]
    public void 模拟部署上限按公开信息推得()
    {
        // 设计文档 §5.4 算例（tasks 1.3）：第 5 大回合模拟下一名对手 P1，它在 B1 上控制 1 枚已揭示的 +2 军令；另 1 枚它原先控制的普通军令被专家本批次（G6）
        // 覆盖成争议 → 模拟对手持有普通子 × 6，部署上限 4 + 2 = 6。
        // 变异 M-A6b（部署上限不计军令）、M-P1（部署上限计入争议军令）→ 见段 A 实施记录。
        MatchFlow match = CommandPosition();
        MatchPublicView view = match.Publish();
        Assert.All(view.Relics, r => Assert.True(r.IsRevealed));
        Assert.Equal(7, PublicRelicEffects.Deploy(P1, 5, view.Relics).DeployLimit);

        RehearsalResult b1 = Rehearse(match, "H7");
        MatchPublicView projected = ExpertLookahead.Project(view, b1.ProjectedBoard!);
        Assert.Equal(RelicControlKind.Contested, projected.Relics.Single(r => r.Coord == TestMaps.At("H6")).Control.Kind);
        Assert.True(projected.Relics.Single(r => r.Coord == TestMaps.At("F6")).Control.GrantsEffectTo(P1));
        Assert.Equal((P1, 5), ExpertLookahead.NextOpponent(view, P0, projected));

        BatchContext context = ExpertLookahead.SimulatedContext(projected, P1, 5);
        Assert.Equal(6, context.DeployLimit);
        Assert.Equal(6, context.StockOf(PieceType.Basic));
        Assert.Single(context.Stock);
        Assert.False(context.WorkshopActive);
    }

    /// <summary>
    /// 第 2 大回合、顺序 P3 → P0（专家）→ P1 → P2。K3 是普通军令（棋盘 0 的东南角），P1 以 J3 / K4 唯一覆盖它；P3 先落一子完成一次结算，军令随之揭示。
    /// retire-legacy-rules 1.4：原 9×9 的 H2 由 P1 的 H3 覆盖、专家在保护期内够不着；链接载体上构筑期四人同锁棋盘 0，专家的批次能落到军令旁把它覆盖成争议，
    /// 故把军令移到盘角、由 P1 两子封住（专家落不进角上、也提不掉这两子），模拟对手的部署上限在每个候选之后都是 3 + 1。
    /// </summary>
    private static MatchFlow ProtectionPosition()
    {
        MatchFlow match = LinkModeFixtures.PlainStarted(relics: [("K3", RelicFixtures.Command())])
            .AtRound(2, [P3, P0, P1, P2])
            .Stones(P0, "D4").Stones(P1, "J3", "K4").Stones(P2, "D9");
        match.PlayTurn("J9");
        Assert.Equal(P0, match.CurrentPlayer);
        return match;
    }

    [Fact]
    public void 构筑期不模拟回应()
    {
        // 规格（expert-tiebreak，替换原 Scenario「构筑期内只在本战场回应」）：第 2 大回合专家做部署决策，本大回合专家之后还有在局玩家（P1、P2）→
        // 不构造任何模拟对手、不做模拟预演，前瞻记录的模拟预演次数为 0；打开多样补充与两层加分时排除重跑与两层预演也为 0。
        // 原断言（迁移前）：已前瞻、每个候选的下一名对手 P1 @ 第 2 大回合、模拟部署上限 3 + 1 = 4、回应落点都在棋盘 0。
        // 变异 T-B5（去掉构筑期判定）、T-B3（构筑期仍跑排除重跑）→ 本测试红。
        foreach (AiSearchConfig config in new[] { ExpertConfig(), StrengthConfig() with { TwoPlyTieBand = 20 } })
        {
            MatchFlow position = ProtectionPosition();
            Assert.Equal(2, position.MajorRound);
            HeuristicTurnController hard = DecideHard(ProtectionPosition());
            HeuristicTurnController expert = Decide(position, AiDifficulty.Expert, config);
            LookaheadRecord record = Record(expert);

            Assert.True(expert.LastCandidates.Count(c => !c.IsPass) >= 2, record.ToText());
            Assert.Equal(LookaheadStatus.NotApplied, record.Status);
            LookaheadEntry only = Assert.Single(record.Entries);
            Assert.Null(only.Responder);
            Assert.Null(only.SimulatedDeployLimit);
            Assert.Equal((0, 0, 0), (record.SimulatedRehearsals, record.SupplementRehearsals, record.TwoPlyRehearsals));
            Assert.Equal(hard.LastChoice!.Key, expert.LastChoice!.Key);
        }
    }

    [Fact]
    public void 第3大回合末位专家按第4大回合全图模拟()
    {
        // 规格（expert-tiebreak）：第 3 大回合专家末位，下一名对手的行动大回合为第 4 大回合 → 模拟回应的落点范围为全图可落子格扣除其禁入格（不再限于其出生棋盘），
        // 部署上限按第 4 大回合（基础 4）。载体：link:1 初始图（5 块紧贴成一张连通地图），P0 / P1 锁棋盘 0、P2 锁棋盘 3、P3 锁棋盘 4；第 1–2 大回合四人各落 1 子，
        // 第 3 大回合顺序 P1 → P2 → P3 → P0，前三人 Pass。
        MatchFlow match = LinkModeFixtures.Started([0, 0, 3, 4]);
        match.PlayRounds(2, TestMaps.All);
        match.AtRound(3, [P1, P2, P3, P0]);
        match.PassTurn();
        match.PassTurn();
        match.PassTurn();
        Assert.Equal(P0, match.CurrentPlayer);
        Assert.Equal(3, match.MajorRound);

        LookaheadRecord record = Record(Decide(match, AiDifficulty.Expert, ExpertConfig() with { CandidateCellLimit = AiSearchConfig.LargeMapCellLimit }));
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.All(record.Entries, e =>
        {
            Assert.NotNull(e.Responder);
            Assert.Equal(4, e.ResponderRound);
            Assert.True(e.SimulatedDeployLimit >= 4, e.ToString());
        });

        // 范围：对每名可能的应答者，按第 4 大回合构造的模拟上下文 = 全图可落子格扣除其禁入格，含其出生棋盘之外的格。
        MatchPublicView view = match.Publish();
        MatchPublicView projected = ExpertLookahead.Project(view, view.Board, P0);
        foreach (PlayerId responder in record.Entries.Select(e => e.Responder!.Value).Distinct())
        {
            BatchContext context = ExpertLookahead.SimulatedContext(projected, responder, 4);
            Coord[] expected = [.. projected.Board.AllCoords().Where(c => projected.Board.Map.IsPlayable(c)).Except(projected.LifeShape.ForbiddenCellsFor(responder)).Order()];
            Assert.Equal(expected, context.LegalRange.Order());
            BoardPlate birth = projected.Board.Map.BirthBoard(projected.Players.Single(p => p.Player == responder).BirthZone!.Value);
            Assert.Contains(context.LegalRange, c => !birth.Contains(c));
            Assert.Equal(ExpertLookahead.SimulatedContext(projected, responder, 3).DeployLimit + 1, context.DeployLimit);   // 分阶段基础上限 3 → 4，信物加成相同
        }
    }

    /// <summary>
    /// 第 5 大回合、顺序 P0（专家）→ P3；E5 是一格未揭示的信物（真实内容由用例给出），P0 与下一名对手 P1 的子都在它两格之外；专家的部署上限 1。
    /// 专家的候选里有首次覆盖 E5 的单子批次（E4 / E6 一带）。夹具的灵敏度（段 A 实测）：若 E5 按真实内容（+2 军令，价值 20，而分区期望为 5）揭示给模拟对手，
    /// P1 对这些候选的回应会改为在 E5 争夺（例如 E4 之后由 B2 / F2 / E6 / H8 变为 B2 / E5 / E6 / H8），前瞻后分数随之改变——兵站（价值 5）与期望相同，不改变回应。
    /// </summary>
    private static MatchFlow HiddenRelicPosition(RelicContent content)
    {
        MatchFlow match = LinkModeFixtures.PlainStarted(relics: [("F6", content)]).AtRound(5, [P0, P1, P2, P3])
            .Stones(P0, "D6", "D5", "E4")
            .Stones(P1, "H6", "H7", "G8")
            .Stones(P2, "C9").Stones(P3, "J3");
        match.SetDeployLimit(1);
        return match;
    }

    [Fact]
    public void 未揭示信物不被读取()
    {
        // 两局的公开状态逐项相同，某候选会首次覆盖同一格未揭示信物 E5：一局真实内容是军令，另一局是兵站 → 专家的前瞻后分数与最终选择逐项相同。
        // 未揭示的信物在整个模拟中保持未揭示、只按分区期望估值（D8）。
        // 变异 M-A4（投影时按真实内容揭示新覆盖的信物）→ 见段 A 实施记录。
        HeuristicTurnController command = DecideExpert(HiddenRelicPosition(RelicFixtures.Command(2)));
        HeuristicTurnController depot = DecideExpert(HiddenRelicPosition(RelicFixtures.Depot()));
        LookaheadRecord a = Record(command);
        LookaheadRecord b = Record(depot);

        Assert.Equal(LookaheadStatus.Applied, a.Status);
        Assert.Contains(a.Entries, e => CoordsOf(e.CandidateKey).Any(c => IsAdjacentOrOn(c, TestMaps.At("F6"))));
        Assert.Equal(a.ToText(), b.ToText());
        Assert.Equal(command.LastChoice!.Key, depot.LastChoice!.Key);

        // expert-strength：打开多样候选与两层加分后同样逐项相同（两层扫描在 B2 上只按期望估值未揭示信物，不读内容）。
        LookaheadRecord presetCommand = Record(Decide(HiddenRelicPosition(RelicFixtures.Command(2)), AiDifficulty.Expert, StrengthConfig()));
        LookaheadRecord presetDepot = Record(Decide(HiddenRelicPosition(RelicFixtures.Depot()), AiDifficulty.Expert, StrengthConfig()));
        Assert.Contains(presetCommand.Entries, e => e.TwoPlyBonus > 0);
        Assert.Equal(presetCommand.ToText(), presetDepot.ToText());
    }

    private static bool IsAdjacentOrOn(Coord c, Coord target) => Math.Abs(c.X - target.X) + Math.Abs(c.Y - target.Y) <= 1;

    // ---------- 模拟对手的口径（expert-opponent-model，design D1：专家本人的高难口径） ----------

    [Fact]
    public void 模拟对手取专家本人的口径()
    {
        // 规格（expert-opponent-model）：专家预设（候选落点数 24、贪心条数 32）在本局候选格上限 24、停手阈值 20 下做一次已前瞻的部署决策 →
        // 模拟对手的搜索配置为 N 24、M 1、只看即时收益为否、候选格上限 24、停手阈值 20，前瞻宽度、多样补充上限、两层权重与两层平局带宽都为 0。
        // 期望值逐项写成字面量（不经任何预设推得）；停滞大回合数随专家本人（预设 6；模拟对手每次新建、没有停滞记忆，循环提子停手不会触发）。
        // 改前（标准预设派生）红：候选落点数为 12。
        AiSearchConfig expert = AiSearchConfig.Expert with { CandidateCellLimit = 24 };
        Assert.Equal((24, 32, 20, 4), (expert.CandidatePointCount, expert.CandidateBatchCount, expert.PassThreshold, expert.LookaheadWidth));   // 前提：专家预设的口径

        LookaheadRecord record = Record(Decide(ContactPosition((PieceType.Basic, 4)), AiDifficulty.Expert, expert));
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.All(record.Entries, e => Assert.NotNull(e.Responder));
        Assert.True(record.SimulatedRehearsals > 0, record.ToText());

        AiSearchConfig simulated = Engine(ContactPosition((PieceType.Basic, 4)), expert).Engine.SimulatedConfig;
        var expected = new AiSearchConfig(
            CandidatePointCount: 24, CandidateBatchCount: 1, ImmediateOnly: false, CandidateCellLimit: 24, PassThreshold: 20,
            LookaheadWidth: 0, DiverseSupplementLimit: 0, TwoPlyWeightPermille: 0, TwoPlyTieBand: 0, StagnationRounds: 6);
        Assert.Equal(expected, simulated);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    public void 模拟对手随专家显式配置(int points)
    {
        // 规格（expert-opponent-model）：专家的搜索配置显式给出候选落点数 12、多样补充上限 8、两层权重 1000‰、两层平局带宽 20 →
        // 模拟对手的候选落点数为 12、贪心条数 1，前瞻宽度、多样补充上限、两层权重与两层平局带宽都为 0；配置校验通过，前瞻照常进行。
        // 12 是 Scenario 原值，恰与标准预设的 N 相同（标准预设的 S / λ / B 本就为 0），这一例在改前代码上不红；补 16 一例，使"随专家本人"与"随标准预设"可分（改前红：12 ≠ 16）。
        // 带宽 > 0 而两层权重为 0 的配置被 Validated 拒绝：派生时漏清两层平局带宽，模拟对手一构造就抛，两例都红（变异记录见任务 implement.md）。
        AiSearchConfig expert = StrengthConfig() with { CandidatePointCount = points, TwoPlyTieBand = 20 };
        Assert.Equal((8, 1000, 20), (expert.DiverseSupplementLimit, expert.TwoPlyWeightPermille, expert.TwoPlyTieBand));

        AiSearchConfig simulated = Engine(ContactPosition((PieceType.Basic, 4)), expert).Engine.SimulatedConfig;
        Assert.Equal((points, 1), (simulated.CandidatePointCount, simulated.CandidateBatchCount));
        Assert.Equal((0, 0, 0, 0), (simulated.LookaheadWidth, simulated.DiverseSupplementLimit, simulated.TwoPlyWeightPermille, simulated.TwoPlyTieBand));
        Assert.Same(simulated, simulated.Validated());

        LookaheadRecord record = Record(Decide(ContactPosition((PieceType.Basic, 4)), AiDifficulty.Expert, expert));
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.All(record.Entries, e => Assert.NotNull(e.Responder));
        Assert.True(record.SimulatedRehearsals > 0, record.ToText());
    }

    /// <summary>
    /// 「模拟回应等于专家口径的不扰动贪心」的样本：G1 口径（一层配置的专家 + 3 标准、link:1）的对局种子。改前代码上逐一探查种子 1 / 2 / 3（截断第 12 大回合）与种子 6（整局）：
    /// 改前的记录与"标准预设派生"的独立复算逐条相同（36 / 35 / 35 / 69 条）；两种口径给出不同回应的候选只在种子 3 出现（第 7 大回合小回合 28、第 12 大回合小回合 45 各一条），故取 3。
    /// </summary>
    internal const ulong IndependentResponseSeed = 3;

    [Fact]
    public void 模拟回应等于专家口径的不扰动贪心()
    {
        // 规格（expert-opponent-model）：对前瞻集中的每个候选 c，以 c 的下一名对手为主体、按第 1–3、5、6 项构造模拟输入，
        // 再以专家本人的搜索配置（贪心条数改为 1、不前瞻）的启发式在 B1 上不扰动地求一次批次 → 与 c 的前瞻记录中的模拟回应批次键相同。
        // 样本：G1 口径的一局（IndependentResponseSeed），逐个检查专家已前瞻决策的每个候选，直到遇到第一个"按标准预设派生会得到不同回应"的候选为止。
        // 样本口径：该候选确实存在（N 12 与 N 24 的不扰动贪心在此给出不同批次）——改前（标准预设派生）的记录在这里与独立复算不同，本测试红。
        List<ResponseCheck> checks = CheckSimulatedResponses(IndependentResponseSeed, OneLayerConfig(), MaxMajorRounds, stopAtDiscriminating: true);

        Assert.NotEmpty(checks);
        Assert.Contains(checks, c => c.ExpertDerived != c.StandardDerived);
        Assert.All(checks, c => Assert.True(c.ExpertDerived == c.Recorded, c.ToString()));
    }

    /// <summary>一次独立复算：小回合、大回合、候选键、前瞻记录里的模拟回应键、按专家本人口径与按标准预设口径不扰动求得的批次键（Pass 为空串）。</summary>
    internal sealed record ResponseCheck(int Turn, int MajorRound, string CandidateKey, string Recorded, string ExpertDerived, string StandardDerived);

    /// <summary>
    /// G1 口径（<c>link:1</c>、座位 1 专家按 <paramref name="search"/>、其余三名标准、全知观察）的一局，跑到第 <paramref name="maxMajorRounds"/> 大回合结束或终局；
    /// 在专家每次已前瞻的决策之后、确认之前，对每个做了模拟的候选独立复算模拟回应（只用前瞻的公开纯函数 Project / NextOpponent / SimulatedContext 构造输入，
    /// 模拟对手经公开构造函数新建、配置在这里另写，不经 <see cref="ExpertLookahead.SimulatedConfig"/>）。<paramref name="stopAtDiscriminating"/> 为真时，
    /// 遇到第一个两种口径给出不同批次的候选，做完该次决策后停下。
    /// </summary>
    internal static List<ResponseCheck> CheckSimulatedResponses(ulong seed, AiSearchConfig search, int maxMajorRounds, bool stopAtDiscriminating, int? cellLimit = 0)
    {
        MatchSession session = CreateSession(
            LinkConfig(new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = search }, Standard, Standard, Standard) with { CandidateCellLimit = cellLimit }, seed);
        MatchFlow match = session.Match;
        var inner = new HeuristicTurnController(P0, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(P0)), AiDifficulty.Expert, Weights, search);
        AiSearchConfig expertDerived = search with { CandidateBatchCount = 1, LookaheadWidth = 0, DiverseSupplementLimit = 0, TwoPlyWeightPermille = 0, TwoPlyTieBand = 0 };
        AiSearchConfig standardDerived = AiSearchConfig.Standard with
        {
            CandidateBatchCount = 1, CandidateCellLimit = search.CandidateCellLimit, PassThreshold = search.PassThreshold, LookaheadWidth = 0,
        };

        var checks = new List<ResponseCheck>();
        bool stop = false;
        StagedBatch? current = null;
        session.SetController(P0, new ProbeController(inner, batch => current = batch, () =>
        {
            if (inner.LastLookahead is not { Status: LookaheadStatus.Applied } record)
            {
                return;
            }

            MatchPublicView view = match.Publish();
            foreach (LookaheadEntry entry in record.Entries.Where(e => e.Responder is not null))
            {
                CandidateBatch candidate = inner.LastCandidates.Single(c => c.Key == entry.CandidateKey);
                RehearsalResult b1 = BatchRehearsal.Rehearse(match.Board, current!.Context, candidate.Placements, match.History);
                Assert.True(b1.IsLegal, b1.Failure?.Message);
                MatchPublicView projected = ExpertLookahead.Project(view, b1.ProjectedBoard!, P0);
                (PlayerId responder, int round) = ExpertLookahead.NextOpponent(view, P0, projected) ?? throw new InvalidOperationException("记录有下一名对手而复算没有。");
                Assert.Equal((entry.Responder, entry.ResponderRound), (responder, round));
                BatchContext context = ExpertLookahead.SimulatedContext(projected, responder, round);
                Assert.Equal(entry.SimulatedDeployLimit, context.DeployLimit);
                var history = new BoardHistory();
                history.Record(view.BoardSerialized);
                history.Record(projected.BoardSerialized);

                string Respond(AiSearchConfig config)
                {
                    var batch = new StagedBatch(projected.Board, context);
                    Siege.Core.Determinism.RandomStream stream = match.Seed.Stream("模拟回应守门");
                    var opponent = new HeuristicTurnController(responder, () => projected, stream, AiDifficulty.Hard, Weights, config);
                    opponent.Deploy(batch, () => BatchRehearsal.Rehearse(projected.Board, context, batch.Placements, history));
                    Assert.Equal(0, stream.Consumed);   // 不扰动：贪心条数 1 只走 k = 0 的无扰动次序，零随机消费
                    return opponent.LastChoice is { IsPass: false } response ? response.Key : string.Empty;
                }

                var check = new ResponseCheck(session.TurnCount + 1, view.MajorRound, entry.CandidateKey, entry.ResponseKey!, Respond(expertDerived), Respond(standardDerived));
                checks.Add(check);
                stop |= stopAtDiscriminating && check.ExpertDerived != check.StandardDerived;
            }
        }));

        while (!stop && match.Phase == MatchPhase.InProgress && match.MajorRound <= maxMajorRounds && session.RunTurn())
        {
        }

        return checks;
    }

    [Fact]
    public void 被模拟玩家的难度不影响模拟()
    {
        // 规格（expert-opponent-model）：两局的公开状态逐项相同，下一名对手（P1）在一局中是标准难度、在另一局中是高难 →
        // 两局中专家的模拟回应、前瞻后分数与最终选择逐项相同。两局都经同一个运行器驱动一个完整小回合（整理、征募、部署、确认），
        // 被模拟玩家的控制者（标准 N 12 / M 8 与高难 N 24 / M 32）挂在同一运行器上；模拟对手的配置只由专家本人的配置推得，不读它们。
        // 样本口径：已前瞻、每个候选的下一名对手都是 P1、两名 P1 的配置确实不同。
        // 先红例外（主会话已接受，见任务 implement.md）：改前代码上同样绿——前瞻只拿到公开快照，公开快照不含任何难度 / 搜索配置，信息边界内写不出使其变红的变异；
        // 本测试是回归守门（将来把对手控制者或其配置引入前瞻即红），越界读取由信息边界守门（类型闭包 + 源码扫描）兜底。
        (LookaheadRecord Record, string Choice, string Board) Run(AiDifficulty responder)
        {
            MatchFlow match = ContactPosition((PieceType.Basic, 4));
            var runner = new MatchRunner(match);
            HeuristicTurnController expert = HeuristicAi.Create(match, P0, AiDifficulty.Expert, Weights, AiSearchConfig.Expert with { CandidateCellLimit = 24 });
            HeuristicTurnController opponent = HeuristicAi.Create(match, P1, responder, Weights);
            Assert.Equal(AiSearchConfig.ForDifficulty(responder), opponent.Config);
            runner.SetController(P0, expert);
            runner.SetController(P1, opponent);
            runner.SetController(P2, HeuristicAi.Create(match, P2, AiDifficulty.Standard, Weights));
            runner.SetController(P3, HeuristicAi.Create(match, P3, AiDifficulty.Standard, Weights));
            Assert.Equal(P0, match.CurrentPlayer);
            runner.RunTurn();
            Assert.NotEqual(P0, match.CurrentPlayer);
            return (Record(expert), expert.LastChoice!.Key, match.Publish().BoardSerialized);
        }

        Assert.NotEqual(AiSearchConfig.Standard, AiSearchConfig.Hard);
        (LookaheadRecord standard, string standardChoice, string standardBoard) = Run(AiDifficulty.Standard);
        (LookaheadRecord hard, string hardChoice, string hardBoard) = Run(AiDifficulty.Hard);

        Assert.Equal(LookaheadStatus.Applied, standard.Status);
        Assert.All(standard.Entries, e => Assert.Equal(P1, e.Responder));
        Assert.Equal(standard.Entries.Select(e => (e.CandidateKey, e.ResponseKey, e.ScoreAfter)), hard.Entries.Select(e => (e.CandidateKey, e.ResponseKey, e.ScoreAfter)));
        Assert.Equal(standard.ToText(), hard.ToText());
        Assert.Equal(standardChoice, hardChoice);
        Assert.Equal(standardBoard, hardBoard);
    }

    [Fact]
    public void 守门覆盖前瞻代码()
    {
        // 信息边界 SHALL 在数据层成立：类型化可达闭包覆盖前瞻组件（每次决策新建、不经控制者实例字段可达，单列为根），
        // 源码扫描覆盖 Siege.Core/Ai（含前瞻）与为前瞻抽出的两个纯函数文件（点名登记，不靠目录通配）。
        // 纯函数文件的扫描：去掉注释后不得出现违禁类型名（ForbiddenForOfficialAi 全部）、TrueContents(、账本内部的 RelicState。
        // 反面命中：同一判据在 RelicLedger.cs 里确实命中；口径下界：两个点名文件都存在且非空。
        // 变异 M-G1（前瞻加调用 TrueContents( 的私有方法）、M-G2（ExpertLookahead 加 HandLedger 字段）、M-G3（纯函数文件读 RelicPlacement.Content）→ 见段 A 实施记录。
        Assert.Empty(AiFixtures.Violations(typeof(HeuristicTurnController)));
        Assert.Empty(AiFixtures.Violations(typeof(ExpertLookahead)));
        Assert.Contains(typeof(BatchEvaluator), AiFixtures.ReachableTypes(typeof(ExpertLookahead)));
        // expert-strength：多样补充与两层加分的新入口类型（每次决策内新建，不经 ExpertLookahead 的字段可达）逐个列为根。
        // 变异 E-F3（Simulation 加一个 HandLedger 成员）→ 见段 A 实施记录。
        Assert.Empty(AiFixtures.Violations(typeof(ExpertLookahead.Simulation)));
        Assert.Empty(AiFixtures.Violations(typeof(ExpertLookahead.LookaheadMember)));
        Assert.Contains(typeof(MatchPublicView), AiFixtures.ReachableTypes(typeof(ExpertLookahead.Simulation)));

        string root = PresentationFixtures.RepoRoot();
        string[] aiFiles = Directory.GetFiles(Path.Combine(root, "src", "Siege.Core", "Ai"), "*.cs", SearchOption.AllDirectories);
        Assert.Contains(aiFiles, f => Path.GetFileName(f) == "ExpertLookahead.cs");
        // expert-strength：多样补充与两层加分的新代码都在已扫描的 ExpertLookahead.cs / HeuristicTurnController.cs 里（不新增文件、不新增入口类型）——口径下界。
        Assert.Contains("TwoPlyBonusOn", File.ReadAllText(aiFiles.Single(f => Path.GetFileName(f) == "ExpertLookahead.cs")), StringComparison.Ordinal);
        // expert-tiebreak：构筑期判定（只读公开快照的大回合、行动顺序与参赛状态）同在已扫描的 ExpertLookahead.cs 里——口径下界。
        Assert.Contains("IsBuildPhaseDecision", File.ReadAllText(aiFiles.Single(f => Path.GetFileName(f) == "ExpertLookahead.cs")), StringComparison.Ordinal);
        Assert.Contains("DiverseSupplementLimit", File.ReadAllText(aiFiles.Single(f => Path.GetFileName(f) == "HeuristicTurnController.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain(aiFiles, f => LifeShape.空区与封闭眼空间Tests.StripComments(File.ReadAllText(f)).Contains("TrueContents(", StringComparison.Ordinal));

        Assert.Equal(["PublicRelicEffects.cs", "PublicRules.cs"], PureRuleFiles.Select(Path.GetFileName));
        foreach (string file in PureRuleFiles)
        {
            string code = LifeShape.空区与封闭眼空间Tests.StripComments(File.ReadAllText(Path.Combine(root, file)));
            Assert.True(code.Length > 500, $"{file} 只有 {code.Length} 个字符");
            Assert.Empty(LeaksIn(code));
        }

        string ledger = LifeShape.空区与封闭眼空间Tests.StripComments(File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Relics", "RelicLedger.cs")));
        Assert.Contains("TrueContents(", LeaksIn(ledger));
        Assert.Contains("RelicState", LeaksIn(ledger));
    }

    /// <summary>为前瞻抽出的公开规则纯函数所在文件（expert-lookahead D7，点名登记）。</summary>
    internal static readonly string[] PureRuleFiles =
    [
        Path.Combine("src", "Siege.Core", "Relics", "PublicRelicEffects.cs"),
        Path.Combine("src", "Siege.Core", "Match", "PublicRules.cs"),
    ];

    /// <summary>源码里出现的违禁标识（不以词边界开头，按标识符字符做前后界，见 testing.md「源码扫描守门的正则不要以 \b 开头」）。</summary>
    internal static string[] LeaksIn(string code)
    {
        IEnumerable<string> names = AiFixtures.ForbiddenForOfficialAi.Select(t => t.Name).Append("RelicState");
        var hits = names.Where(n => Regex.IsMatch(code, $"(?<![A-Za-z0-9_]){n}(?![A-Za-z0-9_])")).ToList();
        if (code.Contains("TrueContents(", StringComparison.Ordinal))
        {
            hits.Add("TrueContents(");
        }

        return [.. hits.Order(StringComparer.Ordinal)];
    }

    // ---------- 投影公开视图（tasks 1.4，design D8） ----------

    /// <summary>
    /// 投影定义的字段（D8）：盘面、势力、信物（揭示 + 控制）、流程状态、活形。其余字段（阶段、当前玩家、手牌类型）不投影。
    /// retire-legacy-rules 1.4：流程状态里 board-link-mode 新增的出局原因 / 落败时势力 / 等待大回合三项不在 D8 的投影定义内（投影不填，AI 也不读）——
    /// 旧载体上它们恒为空、比较的从来只是 D8 字段；迁到链接载体后真实快照会记出局原因，这里显式剔除这三项，口径与迁移前相同。
    /// </summary>
    internal static string ProjectedFields(MatchPublicView v) => string.Join("\n",
        v.BoardSerialized,
        AiFixtures.PowerText(v.Power!),
        string.Join(";", v.Relics.Select(r => r.ToString())),
        string.Join(";", v.Players.Select(p => (p with { EliminationReason = null, PowerAtDefeat = null }).ToString())),
        string.Join(";", v.LifeShape.Groups.Select(g => $"{g.Group.Owner}:{g.Life}:{string.Join(",", g.Group.Stones.Order().Select(c => c.ToNotation()))}")),
        string.Join(";", v.Players.Select(p => $"{p.Player}:{string.Join(",", v.LifeShape.ForbiddenCellsFor(p.Player).Order().Select(c => c.ToNotation()))}")),
        v.Board.Serialize());

    /// <summary>当前玩家摆上 <paramref name="cells"/>：先按决策起点的快照投影预演后的盘面，再真实确认；返回（投影, 确认后发布的快照）。</summary>
    private static (MatchPublicView Projected, MatchPublicView Published) ProjectThenConfirm(MatchFlow match, params string[] cells)
    {
        RehearsalResult b1 = Rehearse(match, cells);
        Assert.True(b1.IsLegal, b1.Failure?.Message);
        MatchPublicView projected = ExpertLookahead.Project(match.Publish(), b1.ProjectedBoard!);
        Assert.True(match.Confirm().Confirmed);
        return (projected, match.Publish());
    }

    [Fact]
    public void 投影视图与真实结算后的快照一致_提子()
    {
        // P0 在 K9 提走 P1 的 K10（P1 在 D4 另有子，不出局；retire-legacy-rules：平坦 8×8 链接载体，四角贴棋盘 0 的角）。
        MatchFlow match = AiFixtures.Round5().Stones(P0, "J10").Stones(P1, "K10", "D4").Stones(P2, "D9").Stones(P3, "J4");
        (MatchPublicView projected, MatchPublicView published) = ProjectThenConfirm(match, "K9");
        Assert.Null(published.Board[TestMaps.At("K10")].Occupant);
        Assert.Equal(PlayerStatus.Active, published.Players.Single(p => p.Player == P1).Status);
        Assert.Equal(ProjectedFields(published), ProjectedFields(projected));
    }

    [Fact]
    public void 投影视图与真实结算后的快照一致_出局()
    {
        // P0 在 K9 提走 P1 在盘上唯一的子 K10（曾建立正势力）→ P1 出局，出局序号 1、出局大回合 5。
        MatchFlow match = AiFixtures.Round5().Stones(P0, "J10").Stones(P1, "K10").Stones(P2, "D9").Stones(P3, "J4");
        (MatchPublicView projected, MatchPublicView published) = ProjectThenConfirm(match, "K9");
        Assert.Equal(PlayerStatus.Eliminated, published.Players.Single(p => p.Player == P1).Status);
        Assert.Equal(ProjectedFields(published), ProjectedFields(projected));
    }

    [Fact]
    public void 投影视图与真实结算后的快照一致_控制易手()
    {
        // H9 是已揭示的军令，被 P1 占据；P0 在 H8 提走 H9 后四面围住它 → 控制由 P1 易手为 P0（retire-legacy-rules：平坦 8×8 链接载体，原 9×9 的 G8 / G7）。
        MatchFlow match = LinkModeFixtures.PlainStarted(relics: [("H9", RelicFixtures.Command())])
            .AtRound(5, [P2, P0, P1, P3])
            .Stones(P0, "G9", "H10", "J9").Stones(P1, "H9", "D4").Stones(P3, "J4");
        match.PlayTurn("D9");
        Assert.True(match.Publish().Relics.Single().Control.GrantsEffectTo(P1));

        (MatchPublicView projected, MatchPublicView published) = ProjectThenConfirm(match, "H8");
        Assert.True(published.Relics.Single().Control.GrantsEffectTo(P0));
        Assert.Equal(ProjectedFields(published), ProjectedFields(projected));
    }

    [Fact]
    public void 投影不揭示新覆盖的信物()
    {
        // D8：投影时揭示状态不变——决策起点未揭示、在 B1 上首次被覆盖的信物仍未揭示、内容为空（只按期望估值），控制照常按 B1 重算。
        MatchFlow match = HiddenRelicPosition(RelicFixtures.Command(2));
        RehearsalResult b1 = Rehearse(match, "E6");
        MatchPublicView projected = ExpertLookahead.Project(match.Publish(), b1.ProjectedBoard!);
        RelicPublicState e5 = projected.Relics.Single();

        Assert.False(e5.IsRevealed);
        Assert.Null(e5.Content);
        Assert.True(e5.Control.GrantsEffectTo(P0));
    }
}
