using System.Numerics;
using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision（expert-lookahead）—— Requirement: 专家难度的一层前瞻</summary>
public class 专家难度的一层前瞻Tests
{
    /// <summary>
    /// 叫吃局面（9×9、第 5 大回合、顺序 P0 → P3、专家 P0 的部署上限 1，候选都是单子批次）：
    /// <code>
    ///  9 . . . . . X O O O     A = G8：P2（O，右上）的 3 子串 G9–J9 被 P0 的 F9 / H8 / J8 围成一口气 G8，A 提走它（自身总分 1157）。
    ///  8 . . . . . . ! X X
    ///  6 . O O O O . . . .     P0（X）的 4 子串 C5–F5 只剩一口气 G5；P1（O）围在上下左三面。
    ///  5 O X X X X ! . . .     B = G5：长出三口气，四子串得救（自身总分 1111）。
    ///  4 . O O O O . . . .     下一名对手 P1 的模拟回应：A 之后在 G5 提走四子串；B 之后提不到 P0 的子。
    ///    B C D E F G H J
    /// </code>
    /// 段 A 实测：A 的前瞻后分数 860（四子串被提走，按 ≤ 1 口气的危险计入安全维），B 为 991 → 专家选 B；高难按自身总分选 A。
    /// </summary>
    internal static MatchFlow AtariPosition()
    {
        MatchFlow match = AiFixtures.Round5()
            .Stones(P0, "C5", "D5", "E5", "F5", "F9", "H8", "J8")
            .Stones(P1, "B5", "C4", "D4", "E4", "F4", "C6", "D6", "E6", "F6")
            .Stones(P2, "G9", "H9", "J9");
        match.SetDeployLimit(1);
        return match;
    }

    [Fact]
    public void 前瞻改变选择()
    {
        // 前瞻集中 A 的自身总分高于 B；A 之后专家一条 4 子非活形棋串只剩一口气、被模拟回应提走，B 之后回应提不到专家的子，
        // B 的前瞻后分数高于 A → 专家选 B；同一局面的高难选 A。
        // 依赖裁决（2026-09-27 段 A 中）对 D2 的修正：修正前被提走的四子串危险扣分随之消失，A 的前瞻后分数反而升到 2540，专家弃串。
        // 变异 M-A10（去掉这条修正）→ 见段 A 实施记录。
        HeuristicTurnController hard = DecideHard(AtariPosition());
        HeuristicTurnController expert = DecideExpert(AtariPosition());
        LookaheadRecord record = Record(expert);

        // 前提（因果声明用断言钉住）：高难选的是提子的 A；A 与 B 都在专家的前瞻集里，A 排在 B 前面。
        Assert.Equal("G8:Basic", hard.LastChoice!.Key);
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        int a = record.Entries.ToList().FindIndex(e => e.CandidateKey == "G8:Basic");
        int b = record.Entries.ToList().FindIndex(e => e.CandidateKey == "G5:Basic");
        Assert.Equal(0, a);
        Assert.True(b > a, record.ToText());
        Assert.True(record.Entries[a].ScoreBefore > record.Entries[b].ScoreBefore);

        // A 之后的模拟回应在 G5 提走四子串；B 之后回应不落在 G5。
        Assert.Equal(P1, record.Entries[a].Responder);
        Assert.Contains(TestMaps.At("G5"), CoordsOf(record.Entries[a].ResponseKey!));
        Assert.DoesNotContain(TestMaps.At("G5"), CoordsOf(record.Entries[b].ResponseKey!));
        Assert.True(record.Entries[b].ScoreAfter > record.Entries[a].ScoreAfter, record.ToText());

        Assert.Equal("G5:Basic", expert.LastChoice!.Key);
        Assert.Equal(b, record.ChosenIndex);
        Assert.True(record.ChangedChoice);
    }

    [Fact]
    public void 被提走的己方棋子按危险计入()
    {
        // 第 6 步（裁决 2026-09-27 段 A 中）：候选 c 之后，模拟回应提走专家 n 枚决策起点就在盘上的棋子 →
        // 前瞻后分数 = 同一评价器对（c 的落点、c 的提子、B2）的九维加权总分 − 安全维权重 × n × 12（≤ 1 口气的每枚危险扣分）。
        // 必失局面：7 子串 B5–H5 只剩一口气 J5 且救不回来，每个候选之后 P1 都在 J5 提走它（n = 7）。
        // 变异 M-A10（去掉这条修正）→ 见段 A 实施记录。
        var items = 前瞻中的停手口径Tests.Recompute(前瞻中的停手口径Tests.DoomedPosition());

        Assert.True(items.Count >= 3);
        Assert.All(items, item =>
        {
            Assert.Equal(7, item.LostAtStart);
            Assert.Equal(item.Plain.Total - (Weights.Safety * 前瞻中的停手口径Tests.AtariDangerPerStone * 7), item.Entry.ScoreAfter);
        });
    }

    [Fact]
    public void 被提走的损失压低前瞻后分数()
    {
        // 专家一条已在叫吃中的 7 子串救不回来，每个候选之后下一名对手都提走它 → 每个候选的前瞻后分数都低于其自身总分。
        // 修正前（段 A 初版）实测 258 → 2930：危险扣分随串被提走而消失，"被提"反而加分；修正后被提走的棋子仍按 ≤ 1 口气的危险计。
        // 变异 M-A10（去掉这条修正）→ 见段 A 实施记录。
        LookaheadRecord record = Record(DecideExpert(前瞻中的停手口径Tests.DoomedPosition()));
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.All(record.Entries, e => Assert.True(e.ScoreAfter < e.ScoreBefore, e.ToString()));
    }

    [Fact]
    public void 同分取原次序()
    {
        // 选择规则（第 7 步）：前瞻后分数最高者；同分取前瞻集中排序靠前者。前瞻集第 2、第 3 个同为最高 → 选第 2 个（下标 1）。
        // 变异 M-A7b（比较由 > 改 >=，同分取后者）→ 见段 A 实施记录。
        Assert.Equal(1, ExpertLookahead.SelectIndex([BigInteger.Parse("5"), 9, 9, 1]));
        Assert.Equal(0, ExpertLookahead.SelectIndex([7, 7, 7]));
        Assert.Equal(2, ExpertLookahead.SelectIndex([-3, -2, -1, -1]));
    }

    /// <summary>
    /// 第 2 大回合、顺序 P0 → P3：下一名对手 P1 的出生区（G1–J3）已被自己的 9 枚子填满，合法落子范围为空 → 对每个候选的模拟回应都是 Pass。
    /// </summary>
    private static MatchFlow NoResponsePosition() =>
        MatchFixtures.Started().AtRound(2, [P0, P1, P2, P3])
            .Stones(P1, "G1", "H1", "J1", "G2", "H2", "J2", "G3", "H3", "J3");

    [Fact]
    public void 模拟回应全为Pass()
    {
        // 每个候选的模拟回应都是 Pass → 前瞻后分数等于自身总分，专家与同一局面的高难选同一个。
        // 变异 M-A7e（前瞻后分数的"前"改用 B1）→ 见段 A 实施记录。
        HeuristicTurnController hard = DecideHard(NoResponsePosition());
        HeuristicTurnController expert = DecideExpert(NoResponsePosition());
        LookaheadRecord record = Record(expert);

        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.True(record.Entries.Length >= 2, record.ToText());
        Assert.All(record.Entries, e =>
        {
            Assert.Equal(P1, e.Responder);
            Assert.Equal(string.Empty, e.ResponseKey);
            Assert.Equal(e.ScoreBefore, e.ScoreAfter);
        });
        Assert.Equal(hard.LastChoice!.Key, expert.LastChoice!.Key);
        Assert.Equal(0, record.ChosenIndex);
    }

    [Fact]
    public void 前瞻宽度为1时与高难逐步相同()
    {
        // v5、种子 1、整局：玩家 1 为"前瞻宽度 1、其余同专家"的配置，对照同座位的高难，其余三名标准。每一步决策与日志的小回合 / 事件逐项相同。
        // D1：前瞻集去掉空批次再取第一个，与高难在含空批次的集合上 CandidateSelection.Best 一致（非空候选总分严格大于空批次）。
        // 变异 M-A9（前瞻里消费一次 ai-<玩家>）→ 见段 A 实施记录。
        (MatchLog hardLog, IReadOnlyList<string> hardDecisions) = RunSeat1(AiDifficulty.Hard, AiSearchConfig.Hard);
        (MatchLog w1Log, IReadOnlyList<string> w1Decisions) = RunSeat1(AiDifficulty.Expert, AiSearchConfig.Expert with { LookaheadWidth = 1 });

        Assert.True(hardDecisions.Count >= 5, $"玩家 1 只有 {hardDecisions.Count} 次决策");
        Assert.Equal(hardDecisions, w1Decisions);

        // 段 B 2.3 起前瞻宽度 > 0 的控制者每次部署都留前瞻记录：宽度 1 的专家只会记"不前瞻"或 Pass，其余玩家没有记录。
        // 日志比对因此先去掉前瞻记录这一项（它是决策的派生物，不影响走法），其余逐项相同。
        TurnSnapshot[] seat1 = [.. w1Log.Turns.Where(t => t.Player == P1.Value)];
        Assert.NotEmpty(seat1);
        Assert.All(seat1, t => Assert.Contains(t.Lookahead!.Status, new[] { nameof(LookaheadStatus.NotApplied), nameof(LookaheadStatus.Pass) }));
        Assert.All(w1Log.Turns.Where(t => t.Player != P1.Value), t => Assert.Null(t.Lookahead));
        var w1Stripped = new MatchLog
        {
            Header = w1Log.Header,
            Turns = [.. w1Log.Turns.Select(t => t with { Lookahead = null })],
            Events = w1Log.Events,
            Result = w1Log.Result,
            Failure = w1Log.Failure,
        };
        Assert.Equal(SimFixtures.TurnTexts(hardLog.Turns), SimFixtures.TurnTexts(w1Stripped.Turns));

        // 首部之外的确定性文本（小回合、事件、终局，去耗时）逐行相同；首部因玩家 1 的难度与搜索配置不同而不同。
        string[] hardBody = hardLog.DeterministicText().Split('\n')[1..];
        string[] w1Body = w1Stripped.DeterministicText().Split('\n')[1..];
        Assert.True(hardBody.Length > 100, $"只有 {hardBody.Length} 行");
        Assert.Equal(hardBody, w1Body);
    }

    /// <summary>v5 种子 1 整局：玩家 1 按给定难度与显式搜索配置，其余三名标准；返回日志与玩家 1 的决策日志。</summary>
    internal static (MatchLog Log, IReadOnlyList<string> Decisions) RunSeat1(AiDifficulty difficulty, AiSearchConfig search)
    {
        MatchSession session = MatchSession.Create(
            V5Config(Standard, new PlayerAiConfig { Difficulty = difficulty, Search = search }, Standard, Standard), 1);
        HeuristicTurnController ai = session.AiOf(P1)!;
        Assert.Equal(search, ai.Config);
        MatchLog log = session.Run();
        Assert.Null(log.Failure);
        return (log, ai.Decisions);
    }

    /// <summary>
    /// 两块各差一眼的棋（9×9、第 5 大回合、专家 P0 的部署上限 1）：左下 B1 / A2 / B2 / C2 / D2 已有眼 A1，D1 围出第二眼 C1；
    /// 右下 H1 / J2 / H2 / G2 / F2 已有眼 J1，F1 围出第二眼 G1；P2 的 E3 挡住"一手同时补两块"的 E3。
    /// 停手阈值取 700：只有 D1、F1（做活，自身 1020）越得过，其余落点（E2 为 635，其他更低）都越不过 → 候选只有 {D1}、{F1} 两个。
    /// </summary>
    private static MatchFlow TwoCandidatePosition()
    {
        MatchFlow match = AiFixtures.Round5()
            .Stones(P0, "B1", "A2", "B2", "C2", "D2", "H1", "J2", "H2", "G2", "F2")
            .Stones(P2, "E3");
        match.SetDeployLimit(1);
        return match;
    }

    private const int TwoCandidateThreshold = 700;

    [Fact]
    public void 前瞻集不足W个()
    {
        // 候选生成去重后只有 2 个非空候选 → 前瞻集为这 2 个，两者都做模拟。
        HeuristicTurnController expert = DecideExpert(TwoCandidatePosition(), TwoCandidateThreshold);
        LookaheadRecord record = Record(expert);

        Assert.Equal(2, expert.LastCandidates.Count(c => !c.IsPass));
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.Equal(["D1:Basic", "F1:Basic"], record.Entries.Select(e => e.CandidateKey));
        Assert.All(record.Entries, e =>
        {
            Assert.Equal(P1, e.Responder);
            Assert.NotNull(e.ResponseKey);
        });
        Assert.True(record.SimulatedRehearsals > 0);
    }

    [Fact]
    public void 不前瞻时直接取前瞻集第一个()
    {
        // 第 3 步：前瞻宽度不大于 1、或前瞻集只有 1 个候选时，直接选前瞻集的第一个，不做模拟；记录状态为"不前瞻"（段 B 遥测「不前瞻也有记录」依赖它）。
        // ① 两个候选、前瞻宽度 1；② 前瞻宽度 4、但只剩左下一块差一眼的棋 → 候选只有 {D1}。两种情形都与同一局面上的高难选同一个。
        static MatchFlow OneCandidate()
        {
            MatchFlow match = AiFixtures.Round5().Stones(P0, "B1", "A2", "B2", "C2", "D2");
            match.SetDeployLimit(1);
            return match;
        }

        foreach ((Func<MatchFlow> position, int width) in new (Func<MatchFlow>, int)[] { (TwoCandidatePosition, 1), (OneCandidate, 4) })
        {
            HeuristicTurnController hard = DecideHard(position(), TwoCandidateThreshold);
            HeuristicTurnController expert = DecideExpert(position(), TwoCandidateThreshold, width);
            LookaheadRecord record = Record(expert);

            Assert.Equal(LookaheadStatus.NotApplied, record.Status);
            LookaheadEntry entry = Assert.Single(record.Entries);
            Assert.Equal(0, record.ChosenIndex);
            Assert.Equal(0, record.SimulatedRehearsals);
            Assert.Null(entry.Responder);
            Assert.Equal(entry.ScoreBefore, entry.ScoreAfter);
            Assert.False(record.ChangedChoice);
            Assert.Equal(hard.LastChoice!.Key, entry.CandidateKey);
            Assert.Equal(hard.LastChoice.Key, expert.LastChoice!.Key);
        }
    }

    [Fact]
    public void 前瞻不修改正式状态()
    {
        // 决策前后正式盘面、公开快照与流程状态的序列化逐字节相同，差别只在专家暂放的批次。
        MatchFlow match = AtariPosition();
        string board = match.Board.Serialize();
        int history = match.History.Count;
        StagedBatch batch = match.OpenDeploy();
        MatchPublicView before = match.Publish();
        string flowAfterOpen = AiFixtures.FlowText(match);

        HeuristicTurnController expert = HeuristicAi.Create(match, P0, AiDifficulty.Expert, Weights, ExpertConfig());
        expert.Deploy(batch, match.Rehearse);
        MatchPublicView after = match.Publish();

        Assert.Equal(LookaheadStatus.Applied, Record(expert).Status);
        Assert.Equal(board, match.Board.Serialize());
        Assert.Equal(before.BoardSerialized, after.BoardSerialized);
        Assert.Equal(AiFixtures.PowerText(before.Power!), AiFixtures.PowerText(after.Power!));
        Assert.Equal(string.Join(";", before.Relics), string.Join(";", after.Relics));
        Assert.Equal(string.Join(";", before.Players), string.Join(";", after.Players));
        Assert.Equal(flowAfterOpen, AiFixtures.FlowText(match));
        Assert.Equal(history, match.History.Count);
        Assert.Equal(expert.LastChoice!.Key, new CandidateBatch(batch.Placements, expert.LastChoice.Evaluation).Key);
        Assert.Equal(1, batch.Count);
        match.Rehearse();
        Assert.Equal(board, match.Board.Serialize());
    }

    [Fact]
    public void 前瞻宽度进入记录()
    {
        // D9 / Requirement 末段：实际生效的前瞻宽度写入配置记录；为 0 时不写出该字段，三档旧难度的记录与改动前逐字节相同。
        // 段 A 只钉 Core 的记录序列化（AiSearchConfig 与 RunConfig 的 Players[].Search）；"未显式给 Search 的专家在日志首部记前瞻宽度 4"属于段 B 2.3。
        // 字面量取自 HEAD 03d45f6 上 JsonSerializer.Serialize(预设) 的输出。
        // 变异 M-A2b（去掉 WhenWritingDefault，宽度 0 照样写出）→ 见段 A 实施记录。
        // v2-recalibration 段 A（1.6）：本测试钉的是记录格式（宽度 0 不写出、字段次序），不是缺省阈值；预设的阈值写死为字面量取值时的 80，
        // 字面量一字不改。缺省阈值本身由 难度分级Tests.难度名称与次序 与 默认评价权重的校准Tests.默认停手阈值被改动 守门。
        Assert.Equal("""{"CandidatePointCount":6,"CandidateBatchCount":1,"ImmediateOnly":true,"CandidateCellLimit":0,"PassThreshold":80}""",
            JsonSerializer.Serialize(AiSearchConfig.Easy with { PassThreshold = LookaheadFixtures.PassThreshold }));
        Assert.Equal("""{"CandidatePointCount":12,"CandidateBatchCount":8,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80}""",
            JsonSerializer.Serialize(AiSearchConfig.Standard with { PassThreshold = LookaheadFixtures.PassThreshold }));
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80}""",
            JsonSerializer.Serialize(AiSearchConfig.Hard with { PassThreshold = LookaheadFixtures.PassThreshold }));
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"LookaheadWidth":4}""",
            JsonSerializer.Serialize(AiSearchConfig.Expert with { PassThreshold = LookaheadFixtures.PassThreshold }));

        // 1 名专家 + 3 名标准的配置记录：只有专家那一名带前瞻宽度 4。
        var config = new RunConfig
        {
            Players =
            [
                new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = AiSearchConfig.Expert },
                new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Search = AiSearchConfig.Standard },
                new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Search = AiSearchConfig.Standard },
                new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Search = AiSearchConfig.Standard },
            ],
        };
        string json = config.ToJson();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "LookaheadWidth"));
        Assert.Contains("\"LookaheadWidth\": 4", json, StringComparison.Ordinal);
        Assert.Contains("\"Difficulty\": \"Expert\"", json, StringComparison.Ordinal);
        RunConfig back = JsonSerializer.Deserialize<RunConfig>(json, RunConfig.JsonOptions)!;
        Assert.Equal(4, back.Players[0].Search!.LookaheadWidth);
        Assert.Equal(AiDifficulty.Expert, back.Players[0].Difficulty);
        Assert.All(back.Players.Skip(1), p => Assert.Equal(AiSearchConfig.Standard, p.Search));
    }

    [Fact]
    public void 旧记录按不前瞻读入()
    {
        // 缺前瞻宽度字段的旧记录按 0 读入（同 PassThreshold 的先例）：配置文件与日志首部里的 Search 都走同一个反序列化。
        const string legacy = """{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80}""";
        AiSearchConfig old = JsonSerializer.Deserialize<AiSearchConfig>(legacy)!;
        Assert.Equal(0, old.LookaheadWidth);
        // v2-recalibration 段 A（1.6）：旧记录里的阈值是写入时的 80，与现行缺省无关——比对对象写死同一阈值，旧记录字面量不改。
        Assert.Equal(AiSearchConfig.Hard with { PassThreshold = LookaheadFixtures.PassThreshold }, old);
        Assert.Equal(4, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(AiSearchConfig.Expert))!.LookaheadWidth);

        // 宽度 0 的控制者不产生前瞻记录（与引入之前的行为一致）。
        HeuristicTurnController hard = HeuristicAi.Create(AtariPosition(), P0, AiDifficulty.Hard, Weights, old);
        Assert.Null(hard.LastLookahead);
    }

    [Fact]
    public void 前瞻宽度拒绝负数()
    {
        // D9：前瞻宽度是非负整数，Validated() 拒绝负数（与 K、停手阈值同一口径）。
        // 变异 M-A2c（Validated 放过负数）→ 见段 A 实施记录。
        Assert.Throws<ArgumentOutOfRangeException>(() => (AiSearchConfig.Hard with { LookaheadWidth = -1 }).Validated());
        Assert.Equal(0, (AiSearchConfig.Hard with { LookaheadWidth = 0 }).Validated().LookaheadWidth);
    }
}
