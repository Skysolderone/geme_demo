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

    [Fact]
    public void 前瞻后分数是两项之和()
    {
        // 前瞻后分数 = 一层分数 + 两层加分（规格算例：一层 −40、加分 65 → 25，以 25 参与选择）。
        // 取样：叫吃局面上同一前瞻集，两层权重 0（一层配置）与 1000‰（多样候选关闭）两种配置——逐候选
        // 前瞻后分数(1000‰) − 两层加分(1000‰) = 前瞻后分数(0)，即一层分数；样本口径：至少一个候选的两层加分为正。
        LookaheadRecord one = Record(DecideExpert(AtariPosition()));
        LookaheadRecord two = Record(Decide(AtariPosition(), AiDifficulty.Expert, StrengthConfig(supplement: 0)));

        Assert.Equal(LookaheadStatus.Applied, two.Status);
        Assert.Equal(one.Entries.Select(e => e.CandidateKey), two.Entries.Select(e => e.CandidateKey));
        Assert.All(one.Entries, e => Assert.Equal(BigInteger.Zero, e.TwoPlyBonus));
        Assert.Contains(two.Entries, e => e.TwoPlyBonus > 0);
        Assert.Equal(one.Entries.Select(e => e.ScoreAfter), two.Entries.Select(e => e.ScoreAfter - e.TwoPlyBonus));
        Assert.Equal(ExpertLookahead.SelectIndex([.. two.Entries.Select(e => e.ScoreAfter)]), two.ChosenIndex);
    }

    [Fact]
    public void 旧专家日志按一层配置回放()
    {
        // 本 change 之前的专家日志首部只有前瞻宽度 4、没有多样补充上限与两层权重 → 按一层配置重建，回放逐步一致，重新产生的前瞻记录与日志逐条相同。
        // 替身（段 A）：sim-out 不入库，这里用"显式一层配置产出的日志"——两项为 0 不写出，首部与改动前的专家日志同形；一层配置与改动前的专家逐步相同由 G1 钉住。
        // 段 B（tasks 2.1）补真实的 expert-lookahead 冒烟日志夹具。
        RunConfig config = BoardConfig(new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = OneLayerConfig() }, Standard, Standard, Standard) with { TurnLimit = 12 };   // retire-legacy-maps 段 A：16 → 12（4 人棋盘图）
        MatchLog log = MatchSession.Create(config, 1).Run();
        string header = log.DeterministicText().Split('\n')[0];
        Assert.Contains("\"LookaheadWidth\":4", header, StringComparison.Ordinal);
        Assert.DoesNotContain("DiverseSupplementLimit", header, StringComparison.Ordinal);
        Assert.DoesNotContain("TwoPlyWeightPermille", header, StringComparison.Ordinal);

        MatchLog old = MatchLog.Parse(log.FullText());
        AiSearchConfig search = old.Header.Config.Players[0].Search!;
        Assert.Equal(0, search.DiverseSupplementLimit);
        Assert.Equal(0, search.TwoPlyWeightPermille);
        // retire-legacy-maps 段 A：4 人棋盘图（465 格）上夹具给显式配置补候选格上限 24（LookaheadFixtures.WithMapCellLimit），首部随之写出；其余字段仍是一层配置。
        Assert.Equal(OneLayerConfig() with { CandidateCellLimit = 24 }, search);
        Assert.Contains(old.LookaheadTurns, t => t.Lookahead!.Status == nameof(LookaheadStatus.Applied));
        ReplayResult replay = Replayer.Replay(old);
        Assert.True(replay.Identical, replay.ToString());
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
    public void 前瞻宽度为1且关闭多样候选时与高难逐步相同()
    {
        // G2（expert-strength D6；沿用 expert-lookahead 的"前瞻宽度为 1 时与高难逐步相同"，改配置）：
        // 4 人棋盘图、种子 1、截断 20 小回合（retire-legacy-maps 段 A 之前为 v5 整局）：玩家 1 为"前瞻宽度 1、多样补充上限 0、两层权重 1000‰（显式给出）、其余同专家预设"的配置，对照同座位的高难，其余三名标准。每一步决策与日志的小回合 / 事件逐项相同。
        // 专家预设退回一层（负责人裁决 2026-09-28，段 B 后）之后两层权重不再取预设值，改为显式 1000‰——否则 E-G3（W = 1 时仍计两层加分）在 λ = 0 下测不到。
        // D1：前瞻集去掉空批次再取第一个，与高难在含空批次的集合上 CandidateSelection.Best 一致（非空候选总分严格大于空批次）。
        // 变异 M-A9（前瞻里消费一次 ai-<玩家>）→ 见 expert-lookahead 段 A 实施记录；变异 E-G3（W = 1 时仍计两层加分）→ 见 expert-strength 段 A 实施记录。
        (MatchLog hardLog, IReadOnlyList<string> hardDecisions) = RunSeat1(AiDifficulty.Hard, AiSearchConfig.Hard);
        AiSearchConfig w1Config = AiSearchConfig.Expert with { LookaheadWidth = 1, DiverseSupplementLimit = 0, TwoPlyWeightPermille = StrengthPermille };
        Assert.Equal(1000, w1Config.TwoPlyWeightPermille);
        (MatchLog w1Log, IReadOnlyList<string> w1Decisions) = RunSeat1(AiDifficulty.Expert, w1Config);

        Assert.True(hardDecisions.Count >= 5, $"玩家 1 只有 {hardDecisions.Count} 次决策");
        Assert.Equal(hardDecisions, w1Decisions);

        // 段 B 2.3 起前瞻宽度 > 0 的控制者每次部署都留前瞻记录：宽度 1 的专家只会记"不前瞻"或 Pass，其余玩家没有记录。
        // 日志比对因此先去掉前瞻记录这一项（它是决策的派生物，不影响走法），其余逐项相同。
        TurnSnapshot[] seat1 = [.. w1Log.Turns.Where(t => t.Player == P1.Value)];
        Assert.NotEmpty(seat1);
        Assert.All(seat1, t => Assert.Contains(t.Lookahead!.Status, new[] { nameof(LookaheadStatus.NotApplied), nameof(LookaheadStatus.Pass) }));

        // expert-strength：W ≤ 1 时不模拟、不计两层加分——每条"不前瞻"记录的唯一候选前瞻前后分数相同（两层权重非 0 也一样）。
        Assert.Contains(seat1, t => t.Lookahead!.Status == nameof(LookaheadStatus.NotApplied));
        Assert.All(seat1.SelectMany(t => t.Lookahead!.Candidates), c => Assert.Equal(c.Before, c.After));
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

    /// <summary>
    /// G1 黄金值（expert-strength tasks 1.1）：原值在改动任何代码之前（HEAD c630d59）用当时的专家预设（= 一层配置）于 v5 整局取下；
    /// retire-legacy-maps 段 A 改钉为本 change 的基线：4 人棋盘图、截断 20 小回合（<see cref="G1TurnLimit"/>）、候选格上限 24（<see cref="BoardConfig"/>），
    /// 座位 1（P0）为专家、其余三名标准、写死权重与停手阈值 80；每颗种子记
    /// （决策条数、决策序列的 SHA-256、ai-P0 子流消费次数、前瞻记录条数、前瞻记录 <see cref="LookaheadRecord.ToText"/> 逐条换行拼接的 SHA-256）。
    /// </summary>
    internal static readonly (ulong Seed, int Decisions, string DecisionsHash, long Consumed, int Records, string RecordsHash)[] G1Golden =
    [
        // retire-legacy-maps 段 A 重钉（原 v5 整局值：种子 1 = 14 / 763028…/ 4557 / 7 / 50A657…，种子 2 = 14 / 7EDF5E…/ 4867 / 7 / A47D60…，
        // 种子 3 = 20 / 2DA476…/ 5797 / 10 / BF4A83…）。截断 20 小回合下专家各有 5 次部署、全部已前瞻。
        (1, 10, "2048E379E2E596DB576D4FC485A5BFA87C87CD64A61A7148E097237C06D67027", 3565, 5, "89A6D376093434C54686BB0E0529E5B0AA3FB0A50BE03D74A51D9F962009C53F"),
        (2, 10, "10251AE1C05D506E010312537E512EA5F7C41C0A992498FA77DC7363CFA1EB7B", 3565, 5, "D918F2FB74A75A0EA05BC2F051165F4261F50A84619F34C2C382E6CA7923F68F"),
        (3, 10, "C487A28C3F9A278C946707B90B1E7E84C0615434A94D0D9CF77DEED678199A92", 3565, 5, "08788C0D8C8ABFF1C661EFBB938E01C9393296E0E7FC0E0CD4F0A2FD03565383"),
    ];

    /// <summary>
    /// G1 口径的对局结果按（搜索配置, 种子）记忆化（retire-legacy-maps 段 A：4 人棋盘图上一局专家约 3–4 s，「一层配置」与「缺省预设」两组各 3 局）。
    /// 键是配置（补上该图候选格上限之后，即对局里实际生效的那份）的<b>值</b>：两组配置逐字段相同时（预设退回一层之后正是如此）第二组直接复用——同一配置同一种子的对局本就逐步相同；
    /// 预设一旦被改（变异 E-P1 / E-P2），键不同、照常重跑，守门不受影响。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(AiSearchConfig, ulong), Lazy<(IReadOnlyList<string>, long, List<string>)>> G1Runs = new();

    /// <summary>G1 口径的一局：座位 1（P0）按 <paramref name="search"/> 做专家，与 <see cref="G1Golden"/> 逐项比对。</summary>
    private static void AssertG1(AiSearchConfig search, ulong seed)
    {
        (IReadOnlyList<string> decisions, long consumed, List<string> records) =
            G1Runs.GetOrAdd((WithMapCellLimit(search, BoardPlayableCells)!, seed), key => new Lazy<(IReadOnlyList<string>, long, List<string>)>(() => RunExpertSeat0(key.Item1, key.Item2))).Value;
        string actual = $"{seed} {decisions.Count} {Sha256(decisions)} {consumed} {records.Count} {Sha256(records)}";

        // 样本口径：该局专家确有已前瞻的决策，决策不止几条，扰动子流确被消费。
        Assert.True(decisions.Count >= 10, actual);
        Assert.True(consumed > 0, actual);
        Assert.Contains(records, r => r.StartsWith(nameof(LookaheadStatus.Applied), StringComparison.Ordinal));
        (ulong Seed, int Decisions, string DecisionsHash, long Consumed, int Records, string RecordsHash) golden = G1Golden.SingleOrDefault(g => g.Seed == seed);
        Assert.True(golden.Seed == seed, "缺黄金值：" + actual);
        Assert.Equal($"{golden.Seed} {golden.Decisions} {golden.DecisionsHash} {golden.Consumed} {golden.Records} {golden.RecordsHash}", actual);
    }

    /// <summary>G1 的小回合截断：20（专家 5 次部署、10 条决策，恰为 <see cref="AssertG1"/> 的样本下界）。</summary>
    private const int G1TurnLimit = 20;

    /// <summary>4 人棋盘图一局（截断 <see cref="G1TurnLimit"/>）：座位 1（P0）按 <paramref name="search"/>（补该图的候选格上限）做专家，其余三名标准；返回 P0 的决策序列、ai-P0 子流消费次数与每次部署的前瞻记录文本。</summary>
    internal static (IReadOnlyList<string> Decisions, long Consumed, List<string> Records) RunExpertSeat0(AiSearchConfig search, ulong seed)
    {
        MatchSession session = MatchSession.Create(
            BoardConfig(new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = search }, Standard, Standard, Standard) with { TurnLimit = G1TurnLimit }, seed);
        Siege.Core.Determinism.RandomStream stream = session.Match.Seed.Stream(HeuristicAi.StreamName(P0));
        search = WithMapCellLimit(search, session.Match.Map.PlayableCount)!;
        Assert.Equal(search, session.AiOf(P0)!.Config);   // 与会话按配置建出的专家同口径（含候选格上限）
        var inner = new HeuristicTurnController(P0, session.Match.Publish, stream, AiDifficulty.Expert, Weights, search);
        var recorder = new LookaheadRecorder(inner);
        session.SetController(P0, recorder);
        MatchLog log = session.Run();
        Assert.Null(log.Failure);
        return (inner.Decisions, stream.Consumed, recorder.Records);
    }

    internal static string Sha256(IEnumerable<string> lines) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines))));

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void 一层配置与改动前的专家逐步相同(ulong seed)
    {
        // G1（expert-strength D6）：前瞻宽度 4、多样补充上限 0、两层权重 0、其余同专家预设的配置，在 4 人棋盘图种子 1–3 上（retire-legacy-maps 段 A 前为 v5）（1 专家 + 3 标准）
        // 与改动之前的专家实现逐项相同：决策序列、ai-P0 子流消费次数、前瞻记录的确定性文本。黄金值见 G1Golden（改动前取下）。
        AssertG1(OneLayerConfig(), seed);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void 缺省预设的专家与改动前的专家逐步相同(ulong seed)
    {
        // 预设退回一层（负责人裁决 2026-09-28，段 B 后：扩样配对 好 7 / 同 33 / 差 20，p = 0.019，λ = 1000‰、S = 8 的专家弱于高难）：
        // 缺省专家预设（只改停手阈值为夹具写死的 80）在 4 人棋盘图种子 1–3 上与 G1 黄金值逐项相同。
        // 另钉解析路径：未显式给 Search 的专家玩家，会话落成的搜索配置就是这份预设——4 人棋盘图 465 格 > 150，按地图取候选格上限 24
        // （retire-legacy-maps 段 A：v5 只有 105 格、不触发上限，原断言是上限 0）。
        // 变异 E-P1 / E-P2（预设的多样补充上限改回 8 / 两层权重改回 1000）→ 本测试红（见预设退回记录）。
        AiSearchConfig preset = AiSearchConfig.Expert with { PassThreshold = PassThreshold, CandidateCellLimit = 24 };
        MatchSession session = MatchSession.Create(BoardConfig(new PlayerAiConfig { Difficulty = AiDifficulty.Expert }, Standard, Standard, Standard), seed);
        Assert.Equal(preset, session.AiOf(P0)!.Config);
        AssertG1(preset, seed);
    }

    /// <summary>4 人棋盘图种子 1（截断 20，玩家 1 恰 5 次部署）：玩家 1 按给定难度与显式搜索配置（补该图的候选格上限），其余三名标准；返回日志与玩家 1 的决策日志。</summary>
    internal static (MatchLog Log, IReadOnlyList<string> Decisions) RunSeat1(AiDifficulty difficulty, AiSearchConfig search)
    {
        MatchSession session = MatchSession.Create(
            BoardConfig(Standard, new PlayerAiConfig { Difficulty = difficulty, Search = search }, Standard, Standard) with { TurnLimit = 20 }, 1);
        HeuristicTurnController ai = session.AiOf(P1)!;
        Assert.Equal(WithMapCellLimit(search, session.Match.Map.PlayableCount), ai.Config);
        Assert.Equal(24, ai.Config.CandidateCellLimit);
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
        // expert-strength：专家预设退回一层（负责人裁决 2026-09-28，段 B 后：多样补充上限 0、两层权重 0，为 0 不写出）——专家一行回到 expert-lookahead 时的字面量；
        // 三档旧难度的三行一字不改。两项非 0 即写出、字段次序接在前瞻宽度之后，由显式 S 8 / λ 1000 的配置钉住。
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"LookaheadWidth":4}""",
            JsonSerializer.Serialize(AiSearchConfig.Expert with { PassThreshold = LookaheadFixtures.PassThreshold }));
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"LookaheadWidth":4,"DiverseSupplementLimit":8,"TwoPlyWeightPermille":1000}""",
            JsonSerializer.Serialize(ExpandedExpert with { PassThreshold = LookaheadFixtures.PassThreshold }));
        // 一层配置（两项为 0）的序列化与 expert-lookahead 时代的专家预设逐字节相同。
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"LookaheadWidth":4}""",
            JsonSerializer.Serialize(OneLayerConfig()));

        // 1 名专家（显式 S 8 / λ 1000）+ 3 名标准的配置记录：只有专家那一名带前瞻宽度 4 与两项。
        var config = new RunConfig
        {
            Players =
            [
                new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = ExpandedExpert },
                new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Search = AiSearchConfig.Standard },
                new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Search = AiSearchConfig.Standard },
                new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Search = AiSearchConfig.Standard },
            ],
        };
        string json = config.ToJson();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "LookaheadWidth"));
        Assert.Contains("\"LookaheadWidth\": 4", json, StringComparison.Ordinal);
        Assert.Contains("\"Difficulty\": \"Expert\"", json, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "DiverseSupplementLimit"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "TwoPlyWeightPermille"));
        Assert.Contains("\"DiverseSupplementLimit\": 8", json, StringComparison.Ordinal);
        Assert.Contains("\"TwoPlyWeightPermille\": 1000", json, StringComparison.Ordinal);
        RunConfig back = JsonSerializer.Deserialize<RunConfig>(json, RunConfig.JsonOptions)!;
        Assert.Equal(4, back.Players[0].Search!.LookaheadWidth);
        Assert.Equal(8, back.Players[0].Search!.DiverseSupplementLimit);
        Assert.Equal(1000, back.Players[0].Search!.TwoPlyWeightPermille);
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

        // expert-strength：多样补充上限与两层权重同一口径——缺字段按 0 读；旧专家记录（只有前瞻宽度 4）读为一层配置，即退回后的专家预设；显式两项往返不丢。
        Assert.Equal(0, old.DiverseSupplementLimit);
        Assert.Equal(0, old.TwoPlyWeightPermille);
        const string legacyExpert = """{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"LookaheadWidth":4}""";
        Assert.Equal(OneLayerConfig(), JsonSerializer.Deserialize<AiSearchConfig>(legacyExpert));
        Assert.Equal(AiSearchConfig.Expert with { PassThreshold = LookaheadFixtures.PassThreshold }, JsonSerializer.Deserialize<AiSearchConfig>(legacyExpert));
        Assert.Equal(AiSearchConfig.Expert, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(AiSearchConfig.Expert)));
        Assert.Equal(ExpandedExpert, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(ExpandedExpert)));

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

        // expert-strength D7：多样补充上限与两层权重同为非负整数。变异 E-C2（Validated 放过负数）→ 见段 A 实施记录。
        Assert.Throws<ArgumentOutOfRangeException>(() => (AiSearchConfig.Expert with { DiverseSupplementLimit = -1 }).Validated());
        Assert.Throws<ArgumentOutOfRangeException>(() => (AiSearchConfig.Expert with { TwoPlyWeightPermille = -1 }).Validated());
        Assert.Equal(AiSearchConfig.Expert, AiSearchConfig.Expert.Validated());
    }
}
