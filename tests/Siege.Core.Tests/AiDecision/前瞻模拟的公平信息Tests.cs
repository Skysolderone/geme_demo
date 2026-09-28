using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision（expert-lookahead）—— Requirement: 前瞻模拟的公平信息</summary>
public class 前瞻模拟的公平信息Tests
{
    /// <summary>第 5 大回合、顺序 P0（专家）→ P3，P0 与下一名对手 P1 在盘面中部接触。</summary>
    internal static MatchFlow ContactPosition(params (PieceType Type, int Count)[] p1Hand)
    {
        MatchFlow match = AiFixtures.Round5()
            .Stones(P0, "D5", "E5", "E4")
            .Stones(P1, "F5", "F6", "E6")
            .Stones(P2, "B8").Stones(P3, "H2");
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
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Command(2)), ("G5", RelicFixtures.Command())])
            .AtRound(5, [P2, P0, P1, P3])
            .Stones(P0, "C3").Stones(P1, "E5", "G4").Stones(P3, "H8");
        match.PlayTurn("B8");
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

        RehearsalResult b1 = Rehearse(match, "G6");
        MatchPublicView projected = ExpertLookahead.Project(view, b1.ProjectedBoard!);
        Assert.Equal(RelicControlKind.Contested, projected.Relics.Single(r => r.Coord == TestMaps.At("G5")).Control.Kind);
        Assert.True(projected.Relics.Single(r => r.Coord == TestMaps.At("E5")).Control.GrantsEffectTo(P1));
        Assert.Equal((P1, 5), ExpertLookahead.NextOpponent(view, P0, projected));

        BatchContext context = ExpertLookahead.SimulatedContext(projected, P1, 5);
        Assert.Equal(6, context.DeployLimit);
        Assert.Equal(6, context.StockOf(PieceType.Basic));
        Assert.Single(context.Stock);
        Assert.False(context.WorkshopActive);
    }

    /// <summary>
    /// 第 2 大回合、顺序 P3 → P0（专家）→ P1 → P2。H2 是普通军令，P1 以 H3 唯一覆盖它；P3 先落一子完成一次结算，军令随之揭示。
    /// </summary>
    private static MatchFlow ProtectionPosition()
    {
        MatchFlow match = MatchFixtures.Started(relics: [("H2", RelicFixtures.Command())])
            .AtRound(2, [P3, P0, P1, P2])
            .Stones(P0, "B2").Stones(P1, "H3").Stones(P2, "B8");
        match.PlayTurn("H8");
        Assert.Equal(P0, match.CurrentPlayer);
        return match;
    }

    [Fact]
    public void 保护期内只在出生区回应()
    {
        // 第 2 大回合模拟下一名对手 P1：模拟回应的全部落点都在 P1 的出生区（G1–J3）内，部署上限为 3 加其控制的军令强度（+1）= 4。
        HeuristicTurnController expert = DecideExpert(ProtectionPosition());
        LookaheadRecord record = Record(expert);
        IReadOnlySet<Coord> zone1 = MatchFixtures.Map().BirthZones[1];

        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.All(record.Entries, e =>
        {
            Assert.Equal(P1, e.Responder);
            Assert.Equal(2, e.ResponderRound);
            Assert.Equal(4, e.SimulatedDeployLimit);
            Assert.All(CoordsOf(e.ResponseKey!), c => Assert.Contains(c, zone1));
        });
        Assert.Contains(record.Entries, e => e.ResponseKey!.Length > 0);
    }

    /// <summary>
    /// 第 5 大回合、顺序 P0（专家）→ P3；E5 是一格未揭示的信物（真实内容由用例给出），P0 与下一名对手 P1 的子都在它两格之外；专家的部署上限 1。
    /// 专家的候选里有首次覆盖 E5 的单子批次（E4 / E6 一带）。夹具的灵敏度（段 A 实测）：若 E5 按真实内容（+2 军令，价值 20，而分区期望为 5）揭示给模拟对手，
    /// P1 对这些候选的回应会改为在 E5 争夺（例如 E4 之后由 B2 / F2 / E6 / H8 变为 B2 / E5 / E6 / H8），前瞻后分数随之改变——兵站（价值 5）与期望相同，不改变回应。
    /// </summary>
    private static MatchFlow HiddenRelicPosition(RelicContent content)
    {
        MatchFlow match = MatchFixtures.Started(relics: [("E5", content)]).AtRound(5, [P0, P1, P2, P3])
            .Stones(P0, "C5", "C4", "D3")
            .Stones(P1, "G5", "G6", "F7")
            .Stones(P2, "B8").Stones(P3, "H2");
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
        Assert.Contains(a.Entries, e => CoordsOf(e.CandidateKey).Any(c => IsAdjacentOrOn(c, TestMaps.At("E5"))));
        Assert.Equal(a.ToText(), b.ToText());
        Assert.Equal(command.LastChoice!.Key, depot.LastChoice!.Key);

        // expert-strength：打开多样候选与两层加分后同样逐项相同（两层扫描在 B2 上只按期望估值未揭示信物，不读内容）。
        LookaheadRecord presetCommand = Record(Decide(HiddenRelicPosition(RelicFixtures.Command(2)), AiDifficulty.Expert, StrengthConfig()));
        LookaheadRecord presetDepot = Record(Decide(HiddenRelicPosition(RelicFixtures.Depot()), AiDifficulty.Expert, StrengthConfig()));
        Assert.Contains(presetCommand.Entries, e => e.TwoPlyBonus > 0);
        Assert.Equal(presetCommand.ToText(), presetDepot.ToText());
    }

    private static bool IsAdjacentOrOn(Coord c, Coord target) => Math.Abs(c.X - target.X) + Math.Abs(c.Y - target.Y) <= 1;

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

    /// <summary>投影定义的字段（D8）：盘面、势力、信物（揭示 + 控制）、流程状态、活形。其余字段（阶段、当前玩家、手牌类型）不投影。</summary>
    internal static string ProjectedFields(MatchPublicView v) => string.Join("\n",
        v.BoardSerialized,
        AiFixtures.PowerText(v.Power!),
        string.Join(";", v.Relics.Select(r => r.ToString())),
        string.Join(";", v.Players.Select(p => p.ToString())),
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
        // P0 在 J8 提走 P1 的 J9（P1 在 B2 另有子，不出局）。
        MatchFlow match = AiFixtures.Round5().Stones(P0, "H9").Stones(P1, "J9", "B2").Stones(P2, "B8").Stones(P3, "H2");
        (MatchPublicView projected, MatchPublicView published) = ProjectThenConfirm(match, "J8");
        Assert.Null(published.Board[TestMaps.At("J9")].Occupant);
        Assert.Equal(PlayerStatus.Active, published.Players.Single(p => p.Player == P1).Status);
        Assert.Equal(ProjectedFields(published), ProjectedFields(projected));
    }

    [Fact]
    public void 投影视图与真实结算后的快照一致_出局()
    {
        // P0 在 J8 提走 P1 在盘上唯一的子 J9（曾建立正势力）→ P1 出局，出局序号 1、出局大回合 5。
        MatchFlow match = AiFixtures.Round5().Stones(P0, "H9").Stones(P1, "J9").Stones(P2, "B8").Stones(P3, "H2");
        (MatchPublicView projected, MatchPublicView published) = ProjectThenConfirm(match, "J8");
        Assert.Equal(PlayerStatus.Eliminated, published.Players.Single(p => p.Player == P1).Status);
        Assert.Equal(ProjectedFields(published), ProjectedFields(projected));
    }

    [Fact]
    public void 投影视图与真实结算后的快照一致_控制易手()
    {
        // G8 是已揭示的军令，被 P1 占据；P0 在 G7 提走 G8 后四面围住它 → 控制由 P1 易手为 P0。
        MatchFlow match = MatchFixtures.Started(relics: [("G8", RelicFixtures.Command())])
            .AtRound(5, [P2, P0, P1, P3])
            .Stones(P0, "F8", "G9", "H8").Stones(P1, "G8", "B2").Stones(P3, "H2");
        match.PlayTurn("B8");
        Assert.True(match.Publish().Relics.Single().Control.GrantsEffectTo(P1));

        (MatchPublicView projected, MatchPublicView published) = ProjectThenConfirm(match, "G7");
        Assert.True(published.Relics.Single().Control.GrantsEffectTo(P0));
        Assert.Equal(ProjectedFields(published), ProjectedFields(projected));
    }

    [Fact]
    public void 投影不揭示新覆盖的信物()
    {
        // D8：投影时揭示状态不变——决策起点未揭示、在 B1 上首次被覆盖的信物仍未揭示、内容为空（只按期望估值），控制照常按 B1 重算。
        MatchFlow match = HiddenRelicPosition(RelicFixtures.Command(2));
        RehearsalResult b1 = Rehearse(match, "D5");
        MatchPublicView projected = ExpertLookahead.Project(match.Publish(), b1.ProjectedBoard!);
        RelicPublicState e5 = projected.Relics.Single();

        Assert.False(e5.IsRevealed);
        Assert.Null(e5.Content);
        Assert.True(e5.Control.GrantsEffectTo(P0));
    }
}
