using Siege.Core.Board.Maps;
using System.Numerics;
using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision（expert-lookahead）—— Requirement: 专家难度的一层前瞻</summary>
public class 专家难度的一层前瞻Tests
{
    /// <summary>
    /// 叫吃局面（第 5 大回合、顺序 P0 → P3、专家 P0 的部署上限 1，候选都是单子批次）：
    /// <code>
    /// 10 . . . . . X O O O     A = H9：P2（O，右上）的 3 子串 H10–K10 被 P0 的 G10 / J9 / K9 围成一口气 H9，A 提走它。
    ///  9 . . . . . . ! X X
    ///  7 . O O O O . . . .     P0（X）的 4 子串 D6–G6 只剩一口气 H6；P1（O）围在上下左三面。
    ///  6 O X X X X ! . . .     B = H6：长出三口气，四子串得救。
    ///  5 . O O O O . . . .     下一名对手 P1 的模拟回应：A 之后在 H6 提走四子串；B 之后提不到 P0 的子。
    ///    C D E F G H J K
    /// </code>
    /// retire-legacy-rules 1.4：载体为平坦 8×8 链接档图（四人同锁棋盘 0），原 9×9 局面 B4–J9 平移 (+1, +1) 落在棋盘 0 的 C5–K10（右上角仍贴盘角）。
    /// 原 9×9 段 A 实测：A 的自身总分 1157、前瞻后分数 860（四子串被提走，按 ≤ 1 口气的危险计入安全维），B 为 1111 / 991 → 专家选 B；高难按自身总分选 A。
    /// </summary>
    internal static MatchFlow AtariPosition()
    {
        MatchFlow match = AiFixtures.Round5()
            .Stones(P0, "D6", "E6", "F6", "G6", "G10", "J9", "K9")
            .Stones(P1, "C6", "D5", "E5", "F5", "G5", "D7", "E7", "F7", "G7")
            .Stones(P2, "H10", "J10", "K10");
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
        Assert.Equal("H9:Basic", hard.LastChoice!.Key);
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        int a = record.Entries.ToList().FindIndex(e => e.CandidateKey == "H9:Basic");
        int b = record.Entries.ToList().FindIndex(e => e.CandidateKey == "H6:Basic");
        Assert.Equal(0, a);
        Assert.True(b > a, record.ToText());
        Assert.True(record.Entries[a].ScoreBefore > record.Entries[b].ScoreBefore);

        // A 之后的模拟回应在 H6 提走四子串；B 之后回应不落在 H6。
        Assert.Equal(P1, record.Entries[a].Responder);
        Assert.Contains(TestMaps.At("H6"), CoordsOf(record.Entries[a].ResponseKey!));
        Assert.DoesNotContain(TestMaps.At("H6"), CoordsOf(record.Entries[b].ResponseKey!));
        Assert.True(record.Entries[b].ScoreAfter > record.Entries[a].ScoreAfter, record.ToText());

        Assert.Equal("H6:Basic", expert.LastChoice!.Key);
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
    public void 前瞻后分数等于一层分数()
    {
        // expert-tiebreak D3（迁移自 expert-strength「前瞻后分数是两项之和」）：前瞻后分数 = 一层分数，两层加分不加进去、只参与平局裁决。
        // 规格算例：两层权重 1000‰，一层分数 −40、两层加分 65 → 前瞻后分数 −40（记录端见 专家前瞻的记录Tests.两层加分与一层分数分开记录）。
        // 取样：叫吃局面（第 5 大回合）上同一前瞻集，两层权重 0（一层配置）与 1000‰（多样候选关闭、带宽 0、20 两档）——逐候选前瞻后分数与一层配置逐项相同；
        // 被选下标等于按（一层分数, 两层加分, 带宽）的平局裁决。样本口径：至少一个候选的两层加分为正。
        // 变异 T-S2（平局集内改回"一层 + 两层"相加）→ 本测试红。
        LookaheadRecord one = Record(DecideExpert(AtariPosition()));
        foreach (int band in new[] { 0, 20 })
        {
            LookaheadRecord two = Record(Decide(AtariPosition(), AiDifficulty.Expert, StrengthConfig(supplement: 0) with { TwoPlyTieBand = band }));

            Assert.Equal(LookaheadStatus.Applied, two.Status);
            Assert.Equal(one.Entries.Select(e => e.CandidateKey), two.Entries.Select(e => e.CandidateKey));
            Assert.All(one.Entries, e => Assert.Equal(BigInteger.Zero, e.TwoPlyBonus));
            Assert.Contains(two.Entries, e => e.TwoPlyBonus > 0);
            Assert.Equal(one.Entries.Select(e => e.ScoreAfter), two.Entries.Select(e => e.ScoreAfter));
            Assert.Equal(ExpertLookahead.SelectIndex([.. two.Entries.Select(e => e.ScoreAfter)], [.. two.Entries.Select(e => e.TwoPlyBonus)], band), two.ChosenIndex);
        }
    }

    [Fact]
    public void 带内由两层加分裁决()
    {
        // 规格算例（expert-tiebreak 1.3）：两层权重 1000‰、带宽 20；A 排在 B 之前，A 一层 100 / 两层 0，B 一层 85 / 两层 200 → 85 ≥ 100 − 20，B 在平局集内，选 B。
        // 带边界：B 一层恰为 80（= 100 − 20）仍在集内。变异 T-S1（平局集用 > 代替 ≥）→ 本测试红。
        Assert.Equal(1, ExpertLookahead.SelectIndex([100, 85], [0, 200], 20));
        Assert.Equal(1, ExpertLookahead.SelectIndex([100, 80], [0, 200], 20));
        // 集内只比两层加分、不与一层分数相加：A 100 / 10、B 85 / 20 → 选 B（相加口径 110 > 105 会选 A）。变异 T-S2（集内改回相加）→ 本测试红。
        Assert.Equal(1, ExpertLookahead.SelectIndex([100, 85], [10, 20], 20));
        // 集内两层加分并列 → 先比一层分数（见 两层加分并列时再比一层分数），一层也并列才取前瞻集靠前者。变异 T-S3（并列改取下标最大）→ 本测试红。
        Assert.Equal(1, ExpertLookahead.SelectIndex([90, 100, 95, 85], [0, 50, 50, 50], 10));
        Assert.Equal(0, ExpertLookahead.SelectIndex([100, 95], [7, 7], 20));
        Assert.Equal(0, ExpertLookahead.SelectIndex([100, 100], [7, 7], 20));
        Assert.Equal(1, ExpertLookahead.SelectIndex([90, 100, 100], [7, 7, 7], 20));
    }

    [Fact]
    public void 两层加分并列时再比一层分数()
    {
        // 规格算例（负责人裁决 2026-10-21，expert-tiebreak 段 A 待决项 1）：两层权重 1000‰、带宽 20，前瞻集 A / B / C / D 一层 3864 / 3862 / 3862 / 3878、两层加分都是 829 →
        // 四者都在平局集内（≥ 3858），两层并列，选一层最高的 D（原"并列取靠前"会选 A）。一层也并列时才取前瞻集靠前者。
        // 变异 T-S4（去掉一层分数二级键，退回并列取下标最小）→ 本测试红。
        Assert.Equal(3, ExpertLookahead.SelectIndex([3864, 3862, 3862, 3878], [829, 829, 829, 829], 20));
        Assert.Equal(1, ExpertLookahead.SelectIndex([3858, 3878, 3878, 3870], [829, 829, 829, 829], 20));
        Assert.Equal(0, ExpertLookahead.SelectIndex([3864, 3878], [830, 829], 20));   // 一级键仍是两层加分
    }

    [Fact]
    public void 带外不被两层加分翻转()
    {
        // 规格算例：两层权重 1000‰、带宽 20；A 一层 100 / 两层 0，B 一层 70 / 两层 1000 → B 不在平局集内，选 A（相加口径会选 B：70 + 1000 > 100）。
        // 变异 T-S2（集内改回相加）→ 本测试红。
        Assert.Equal(0, ExpertLookahead.SelectIndex([100, 70], [0, 1000], 20));
        Assert.Equal(0, ExpertLookahead.SelectIndex([100, 79], [0, 1000], 20));
    }

    [Fact]
    public void 带宽为0只裁决严格平局()
    {
        // 规格算例：两层权重 1000‰、带宽 0；A、B 一层都是 100，A 在前，两层 10 / 30 → 选 B；若 B 的一层为 99 → 选 A。
        // 首个候选不在平局集内时（前瞻集按自身总分排序，一层分数可能后者更高）不得因"从下标 0 起算"而误选它。
        Assert.Equal(1, ExpertLookahead.SelectIndex([100, 100], [10, 30], 0));
        Assert.Equal(0, ExpertLookahead.SelectIndex([100, 99], [10, 30], 0));
        Assert.Equal(1, ExpertLookahead.SelectIndex([85, 100], [500, 0], 0));
        // 两层权重为 0（两层加分恒 0）时退化为"一层分数最高、同分取靠前者"，与单参重载逐项相同。
        Assert.Equal(ExpertLookahead.SelectIndex([5, 9, 9, 1]), ExpertLookahead.SelectIndex([5, 9, 9, 1], [0, 0, 0, 0], 0));
    }

    [Fact]
    public void 旧专家日志按一层配置回放()
    {
        // 本 change 之前的专家日志首部只有前瞻宽度 4、没有多样补充上限与两层权重 → 按一层配置重建，回放逐步一致，重新产生的前瞻记录与日志逐条相同。
        // 替身（段 A）：sim-out 不入库，这里用"显式一层配置产出的日志"——两项为 0 不写出，首部与改动前的专家日志同形；一层配置与改动前的专家逐步相同由 G1 钉住。
        // 段 B（tasks 2.1）补真实的 expert-lookahead 冒烟日志夹具。
        // retire-legacy-maps 1.4：样本局由 v5 改到 link:1（内置固定配对脚本），断言不变。
        RunConfig config = LinkConfig(new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = OneLayerConfig() }, Standard, Standard, Standard) with { TurnLimit = 16 };
        MatchLog log = CreateLinkSession(config, 1).Run();   // map-expand-25 段 A2：要经 Replayer 按标识回放，留在 link:1:4（其余样本改到平坦手写图，见 LookaheadFixtures.CreateSession）
        string header = log.DeterministicText().Split('\n')[0];
        Assert.Contains("\"LookaheadWidth\":4", header, StringComparison.Ordinal);
        Assert.DoesNotContain("DiverseSupplementLimit", header, StringComparison.Ordinal);
        Assert.DoesNotContain("TwoPlyWeightPermille", header, StringComparison.Ordinal);

        MatchLog old = MatchLog.Parse(log.FullText());
        AiSearchConfig search = old.Header.Config.Players[0].Search!;
        Assert.Equal(0, search.DiverseSupplementLimit);
        Assert.Equal(0, search.TwoPlyWeightPermille);
        Assert.Equal(OneLayerConfig(), search);
        Assert.Contains(old.LookaheadTurns, t => t.Lookahead!.Status == nameof(LookaheadStatus.Applied));
        ReplayResult replay = Replayer.Replay(old);
        Assert.True(replay.Identical, replay.ToString());
    }

    /// <summary>
    /// 第 5 大回合、顺序 P0 → P3：下一名对手 P1 的合法落子范围里没有空格 → 对每个候选的模拟回应都是 Pass。
    /// expert-tiebreak 1.5：原为第 2 大回合——构筑期决策不前瞻，局面移到全图阶段；平坦 8×8 载体的五块棋盘互不连通，四人同锁棋盘 0 时第 5 大回合的合法范围（所在战场 = 棋盘 0）
    /// 与第 2 大回合（出生棋盘 ∩ 战场）相同，"P1 的范围里没有空格"的构造不变。
    /// retire-legacy-rules 1.4：原 9×9 局面是"P1 的 3×3 出生区被自己的 9 枚子填满"——旧模式保护期内合法范围只是出生区；链接模式下合法范围是整个战场，
    /// 四人同锁棋盘 0 时只要有空格 P1 就能落。改为：P0 占满棋盘 0、只留三块直三眼空间（<see cref="NoResponseEyes"/>，眼值 1 + 1 + 1，P0 为已确定活形）——
    /// 全部空格都是 P0 活形的眼空间、对 P1 是禁入格，P1 的合法范围里没有空格；P0 自己可以点直三的中间（眼位 +1，非单格眼、不失活形），候选不止一个。
    /// </summary>
    private static MatchFlow NoResponsePosition()
    {
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [P0, P1, P2, P3]);
        match.Stones(P0, [.. match.Map.Boards[0].Cells().Select(c => c.ToNotation()).Except(NoResponseEyes)]);
        match.SetDeployLimit(1);   // 单子候选：三处直三中间各是一个候选（部署上限 3 时贪心组批会把三处并成同一个批次，前瞻集只剩 1 个）
        return match;
    }

    /// <summary>棋盘 0 上 P0 留下的三块直三眼空间：底边 C3–E3、顶边 C10–E10、东边 K5–K7。</summary>
    private static readonly string[] NoResponseEyes = ["C3", "D3", "E3", "C10", "D10", "E10", "K5", "K6", "K7"];

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

    [Fact(Skip = "AI 缺陷（map-expand-25 段 A3 用户裁决跳过）：ExpertLookahead.Choose（约 112–115 行）在非构筑期且前瞻宽度 ≤ 1 时直接取前瞻集第一个，"
        + "未与 CandidateSelection.Best 的 Pass 比较；换浮空岛后暴露。留给后续 AI 任务，本任务不改 AI 代码。")]
    public void 前瞻宽度为1且关闭多样候选时与高难逐步相同()
    {
        // G2（expert-strength D6；沿用 expert-lookahead 的"前瞻宽度为 1 时与高难逐步相同"，改配置）：
        // link:1、种子 1、整局（retire-legacy-maps 1.4：原 v5）：玩家 1 为"前瞻宽度 1、多样补充上限 0、两层权重 1000‰（显式给出）、其余同专家预设"的配置，对照同座位的高难，其余三名标准。每一步决策与日志的小回合 / 事件逐项相同。
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
    /// G1 黄金值（expert-strength tasks 1.1 起；retire-legacy-maps 1.4 改钉，裁决 L2 / L8）：在删除 <c>siege-4p-base-v5</c> 之前、以当时的实现在 <c>link:1</c> 上取下。
    /// <c>link:1</c>、对局种子 1–3、内置固定配对脚本、座位 1（P0）为专家、其余三名标准、写死权重与停手阈值 80（<see cref="LinkConfig"/>）、截断到第 12 大回合；每颗种子记
    /// （决策条数、决策序列的 SHA-256、ai-P0 子流消费次数、前瞻记录条数、前瞻记录 <see cref="LookaheadRecord.ToText"/> 逐条换行拼接的 SHA-256）。
    /// 原 v5 黄金值（种子 1–3）随 v5 删除，不再保留。
    /// </summary>
    internal static readonly (ulong Seed, int Decisions, string DecisionsHash, long Consumed, int Records, string RecordsHash)[] G1Golden =
    [
        // link-map-terrain 第 2 组重钉（M10）：link:1 的地图内容变了（出生棋盘 6–8、主题地形、出生区为核心区、10–15 个信物格），同一配置的走法随之变。旧值：
        // (1, 18, 9931058863ADB025…, 3069, 9, 6B948F9BE9D4C936…) (2, 22, C4572AE899F5F15E…, 7502, 11, C37EE64F2003D94B…) (3, 24, BAA330579B45A00F…, 6510, 12, 851B180F47D084F8…)。
        // 决策数 18 / 22 → 24：P0 的部署决策条数随走法变化（新图上三颗种子截断前都是 24 条）。
        // ai-seat-vision 1.2 重钉（裁决 V2，见实施记录 V7）：前瞻名册改为只含同战场玩家。夹具写死全知（LookaheadFixtures.LinkConfig）；三颗种子的决策序列、决策条数与
        // ai-P0 子流消费次数逐项不变，只有前瞻记录的确定性文本变了（下一名对手由他战场玩家换成同战场玩家、前瞻后分数随之变，截断前未改变任何一次选择）。旧 RecordsHash：
        // 1 → D2074DF53A1562876D099DACDAA3FE2AA2BF36001774D69C54CC8D18A61AE25B；2 → E84CF42356DDF481D25A6DA7E97D27ADC447637298A5CF4AA29A16317FA7D8B8；3 → 54E0E8F99651D05CF8B6CC94480499E6EE8E49FC0B9033B1811183AD6D10C040。
        // open-map 1.5 重钉：link:1 改为 3+2 紧贴布局、锁定后全图可见、第 4 大回合起全图可落、前瞻名册 = 全部在局玩家，三颗种子的走法整体改变（决策条数仍为 24、前瞻记录仍为 12）。旧值：
        // (1, 24, F0D3738FF8300586…, 6386, 12, 937E83BAD1649A51…) (2, 24, 7435EBED47DC95AD…, 7533, 12, 6F8FBBDACC0FC3B9…) (3, 24, 20FF2CD2850E4BA4…, 8184, 12, 0F5F1AF62E8153F6…)。
        // link-map-surfaces 2.2 重钉：link:1 中间带投放新地表（9 格），三颗种子的走法改变（决策条数仍为 24、ai-P0 消费仍为 8556、前瞻记录仍为 12）。旧值：
        // (1, 24, 0BEAB462B0D4E148…, 8556, 12, D5D6C56BA748682A…) (2, 24, D8A9DF8114E9B9B8…, 8556, 12, E560CE02355801C4…) (3, 24, 17DC195B11B4DF4A…, 8556, 12, B06D5D809605EA4C…)。
        // link-map-surfaces 1.3 再重钉（实施后裁决 F1 / F2，link:1 新地表 9 → 10 格）：条数 / 消费 / 记录数仍为 24 / 8556 / 12。第一次重钉值：
        // (1, B516EC076E8B6F99…, 684442611C88614D…) (2, DE29786E18D1CFC4…, E675F8FE0502263D…) (3, 6A6E03E6AFCC8B36…, 82EADCC9A485A2D5…)。
        // fence-eye-rule 2.2 重钉：活形判定无视栅栏（栅栏不再造眼），三颗种子的走法改变（条数 / 消费 / 记录数仍为 24 / 8556 / 12）。旧值：
        // (1, EB64AF2FB155B777…, 39001220DE9D06FA…) (2, D4DBE024E7A2B359…, 4A53F34D09955038…) (3, 24A9B777CD3BF721…, 4EC036D2E418FFD7…)。
        // terrain-barriers 3.1 重钉（地图变了：link:1 的河流 / 高台改为横穿棋盘的隔断线、大棋盘加湖泊 / 岩区，同一配置的走法随之变）；条数 / 消费 / 记录数仍为 24 / 8556 / 12。旧值：
        // (1, 565CB9392D540698…, D6304D1307C2ACA2…) (2, A5550052BCF414FE…, BB8C1F993F763A59…) (3, B9A222ADE7601D46…, 3061AE8C815967AD…)。
        // expert-tiebreak 1.7 重钉（构筑期不前瞻）：决策序列、条数与 ai-P0 消费逐项不变（24 / 8556），只有前瞻记录变了——构筑期决策的记录由"已前瞻 #0"改为"不前瞻 #0"
        // （候选表只剩高难的选择、前后分数相同、sim=0），其余记录逐条相同。首个分歧：每颗种子 P0 的第 1 条记录（第 1 大回合）；分歧的记录种子 1 为第 1–2 条（第 3 大回合 P0 末位、照常前瞻），
        // 种子 2 / 3 为第 1–3 条（见 .trellis/tasks/archive/2026-10/10-21-expert-tiebreak/rebaseline.txt）。旧 RecordsHash：
        // 1 → 9EB5DEF5AAF9EC398E716CF0D4E07F3FB873A7EE5E4B9DE8EE923A35FAF32320；2 → 96D0C33599A2274F32198DD4E23ED01D8ED763771851B20682111E0B2CFD9326；3 → 5BCAB1A5B7F19181B190F64BD247528147617A857347E6AF14DD3B93314890F6。
        // terrain-density 2.2 重钉（地图变了：出生棋盘边长 6–8 → 6–7，link:1 的五块棋盘与地形全部重抽，同一配置的走法随之变）；条数 / 消费 / 记录数仍为 24 / 8556 / 12。旧值：
        // (1, 697432832ABA4E55…, 4169B2F72BBF8178…) (2, 370170EE8D4031EF…, D2AE1D4A05092D7A…) (3, 5CE58EFA77268970…, 5EE764BC9925BE52…)。
        // expert-opponent-model 1.5 重钉（模拟对手改用专家本人的高难口径：N 12 → 24、M 1、不扰动）：种子 2 逐项不变；种子 1 决策 / 消费不变，前瞻记录第 10、12 条只差模拟预演次数
        // （sim 251 → 299、208 → 256，N 24 多预演第 13 名以后的单点、全部被拒），回应键与选择不变；种子 3 第 7 条（小回合 28）候选 2 的回应键多出 D12、第 12 条回应键多出 H9 且选择 #2 → #0，
        // 第 24 条决策 E12:Line → F15:Line。构筑期记录逐字相同（见 .trellis/tasks/10-22-expert-opponent-model/rebaseline.txt）。旧值：
        // (1, CA7E6B310E26D1A1…, 0420AA7C944FE161E22CDA7D7A44213ADD230F4EA641234A42FE665BF05B0A40) (3, 0103AE27973F2C55F4B6C08EFEC7BF075095BC573AA31648A7899D867CC561DA, 2F746F936888BE7DFB0512DAFF055E69D43601D1CF784FFCBB1BFFCE32B39866)。
        // ai-whole-batch 1.6 重钉（整批候选缺省开：一层配置随专家预设带开关 1，三名标准随跑局层缺省 1）：条数 / 消费 / 记录数仍为 24 / 8556 / 12（整批候选零随机）。
        // 首个分歧（全体玩家的决策序列与 P0 的前瞻记录逐小回合比对）：种子 1 第 33 小回合 P1（标准）选中配对候选；种子 2 第 41 小回合 P0（专家）前瞻后选中配对候选 #0；
        // 种子 3 第 29 小回合 P1（标准）选中剪枝候选（见 .trellis/tasks/10-22-ai-whole-batch/rebaseline.txt）。旧值（开关 0 下逐项不变，由 G1WholeBatchOffGolden 守）：
        // (1, CA7E6B310E26D1A1…, 5F47E90672832C44…) (2, 01E794D4421D2251…, FEC148D78BCD690A…) (3, AFFC6C75B9908670…, C472B8516CC677B9…)。
        // ai-bridge-candidate 1.6 重钉（搭桥候选缺省开：一层配置随专家预设带开关 1，三名标准随跑局层缺省 1）：条数 / 消费 / 记录数仍为 24 / 8556 / 12（搭桥候选零随机）。
        // 首个分歧（全体玩家的决策序列与 P0 的前瞻记录逐小回合比对）：种子 1 第 26 小回合 P2（标准）、种子 2 第 14 小回合 P2（标准）、种子 3 第 14 小回合 P3（标准），
        // 都选中搭桥候选（见 .trellis/tasks/10-22-ai-bridge-candidate/rebaseline.txt）。旧值（开关 0 下逐项不变，由 G1EditSeedOffGolden 守）：
        // (1, 09C8BB1DF33C9DE9…, 66150B7B2E52249F…) (2, 937FA090817BBF5D…, F8F59B6F5AD53C21…) (3, 58B9DB9D6C01892D…, 0C6FB2249A01BD6A…)。
        // map-expand-25 段 A2 重钉（载体变了：link:1:4 换成浮空岛后专家整局 / 12 大回合截断单条十几分钟，真实对局改在平坦手写图 FlatMap 上，见 LookaheadFixtures.CreateSession）；
        // 条数 / 消费 / 记录数仍为 24 / 8556 / 12。本值是当前实现在新载体上的自证值；平坦图上搭桥候选开与关三颗种子逐项相同（G1EditSeedOffGolden 与本表同值），
        // 整批候选开与关只在种子 2 相同。旧值：(1, 37A7618730455EDA…, B6880D164500CD5F…) (2, 523BFD4F35A9D986…, B488478039790B18…) (3, 4DE433DD807E1D2F…, 6BAD726E139A41E8…)。
        (1, 24, "5775E7FC56143455461B9C7BF12CC8811D479217BC725C1D9DA067A29DC3F759", 8556, 12, "D1BBDF3F3EAD55EE75655F7E777317098E5E9BD55CFF36C66FD2656BA1BA9328"),
        (2, 24, "CE2954F4010D112549826D398955976CD9F6E04BB696F387AD4002124E626706", 8556, 12, "13906D4A8BC3764A3B3A2383D74A8432FF2FA261E5DC4401802BB30DB63BF284"),
        (3, 24, "34C7048D63550F76DE2864E13FF40C2C8976A75A7CEFD2B2D9A89C3EBC6B5C08", 8556, 12, "EDF6C6B7B8A6C5D0871C2DA87292130DD07247F2CE480A7AE70D998904E01920"),
    ];

    /// <summary>
    /// ai-whole-batch 守门（规格「整批候选」：开关为 0 时与引入之前逐步相同）：G1 口径、专家与三名标准的整批候选开关都为 0 时的黄金值——
    /// 即引入整批候选之前（HEAD 0b1b4bf）的 <see cref="G1Golden"/>，逐项不改。
    /// map-expand-25 段 A2：载体改为平坦手写图后重钉，本表从此是开关 0 路径在新载体上的自证值，不再是"引入之前"的独立参照。
    /// </summary>
    internal static readonly (ulong Seed, int Decisions, string DecisionsHash, long Consumed, int Records, string RecordsHash)[] G1WholeBatchOffGolden =
    [
        (1, 24, "BCF4AE6E653F54BAA0EFCFFF6674B8647D9EC863495F3C9D9B8803C8B3F1B507", 8556, 12, "DE7450B991EF7046059C78EFBDFEBE2FB46D9D7DA832B45153C90EF140092B2B"),
        (2, 24, "CE2954F4010D112549826D398955976CD9F6E04BB696F387AD4002124E626706", 8556, 12, "13906D4A8BC3764A3B3A2383D74A8432FF2FA261E5DC4401802BB30DB63BF284"),
        (3, 24, "21132AD2B82F3749C6EFA28B407BA30B1B9A90B2467FFD553B218D162A157D01", 8556, 12, "27395539D582D6E2143C82EA801FB9EB14FB1D3C3030BBBCC4CCE32F235377A3"),
    ];

    /// <summary>
    /// ai-bridge-candidate 守门（规格「搭桥候选」：开关为 0 时与引入之前逐步相同）：G1 口径、专家与三名标准的搭桥候选开关都为 0 时的黄金值——
    /// 即引入搭桥候选之前（HEAD 01efcd4）的 <see cref="G1Golden"/>，逐项不改。
    /// map-expand-25 段 A2：载体改为平坦手写图后重钉（与 <see cref="G1Golden"/> 同值：平坦图上 12 个大回合内搭桥候选不改变走法），本表从此是开关 0 路径的自证值。
    /// </summary>
    internal static readonly (ulong Seed, int Decisions, string DecisionsHash, long Consumed, int Records, string RecordsHash)[] G1EditSeedOffGolden =
    [
        (1, 24, "5775E7FC56143455461B9C7BF12CC8811D479217BC725C1D9DA067A29DC3F759", 8556, 12, "D1BBDF3F3EAD55EE75655F7E777317098E5E9BD55CFF36C66FD2656BA1BA9328"),
        (2, 24, "CE2954F4010D112549826D398955976CD9F6E04BB696F387AD4002124E626706", 8556, 12, "13906D4A8BC3764A3B3A2383D74A8432FF2FA261E5DC4401802BB30DB63BF284"),
        (3, 24, "34C7048D63550F76DE2864E13FF40C2C8976A75A7CEFD2B2D9A89C3EBC6B5C08", 8556, 12, "EDF6C6B7B8A6C5D0871C2DA87292130DD07247F2CE480A7AE70D998904E01920"),
    ];

    /// <summary>
    /// G1 口径的一局：座位 1（P0）按 <paramref name="search"/> 做专家，与 <paramref name="goldens"/>（缺省 <see cref="G1Golden"/>）逐项比对。
    /// <paramref name="wholeBatch"/> 是跑局层的整批候选开关（只作用于三名标准；<c>null</c> = 缺省 1）。
    /// </summary>
    private static void AssertG1(
        AiSearchConfig search, ulong seed, int cellLimit = 0, int? wholeBatch = null,
        (ulong Seed, int Decisions, string DecisionsHash, long Consumed, int Records, string RecordsHash)[]? goldens = null, int? editSeed = null)
    {
        (IReadOnlyList<string> decisions, long consumed, List<string> records) = RunExpertSeat0(search, seed, cellLimit, wholeBatch, editSeed);
        string actual = $"{seed} {decisions.Count} {Sha256(decisions)} {consumed} {records.Count} {Sha256(records)}";

        // 样本口径：该局专家确有已前瞻的决策，决策不止几条，扰动子流确被消费。
        Assert.True(decisions.Count >= 10, actual);
        Assert.True(consumed > 0, actual);
        Assert.Contains(records, r => r.StartsWith(nameof(LookaheadStatus.Applied), StringComparison.Ordinal));
        (ulong Seed, int Decisions, string DecisionsHash, long Consumed, int Records, string RecordsHash) golden = (goldens ?? G1Golden).SingleOrDefault(g => g.Seed == seed);
        Assert.True(golden.Seed == seed, "缺黄金值：" + actual);
        string expected = $"{golden.Seed} {golden.Decisions} {golden.DecisionsHash} {golden.Consumed} {golden.Records} {golden.RecordsHash}";
        Assert.True(expected == actual, $"G1 黄金值不同：期望 {expected}；实际 {actual}");
    }

    /// <summary>
    /// <c>link:1</c> 一局（retire-legacy-maps 1.4：原 v5 整局）：座位 1（P0）按 <paramref name="search"/> 做专家，其余三名标准，内置固定配对脚本、截断到第 12 大回合；
    /// 返回 P0 的决策序列、ai-P0 子流消费次数与每次部署的前瞻记录文本。<paramref name="cellLimit"/> 是跑局配置的候选格上限（只作用于未显式给 Search 的三名标准）。
    /// </summary>
    internal static (IReadOnlyList<string> Decisions, long Consumed, List<string> Records) RunExpertSeat0(AiSearchConfig search, ulong seed, int cellLimit = 0, int? wholeBatch = null, int? editSeed = null)
    {
        MatchSession session = CreateSession(
            LinkConfig(new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = search }, Standard, Standard, Standard) with { CandidateCellLimit = cellLimit, WholeBatch = wholeBatch, EditSeed = editSeed }, seed);
        Siege.Core.Determinism.RandomStream stream = session.Match.Seed.Stream(HeuristicAi.StreamName(P0));
        var inner = new HeuristicTurnController(P0, session.Match.Publish, stream, AiDifficulty.Expert, Weights, search);
        var recorder = new LookaheadRecorder(inner);
        session.SetController(P0, recorder);
        RunToRound(session);
        return (inner.Decisions, stream.Consumed, recorder.Records);
    }

    internal static string Sha256(IEnumerable<string> lines) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines))));

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void 一层配置的专家黄金值(ulong seed)
    {
        // retire-legacy-maps 1.4：测试名随规格 Scenario 改名（原「一层配置与改动前的专家逐步相同」），地图改 link:1（原 v5），断言不变。
        // G1（expert-strength D6）：前瞻宽度 4、多样补充上限 0、两层权重 0、其余同专家预设的配置，在 link:1 种子 1–3 上（1 专家 + 3 标准、固定配对脚本、截断第 12 大回合）
        // 与钉入的黄金值逐项相同：决策序列、ai-P0 子流消费次数、前瞻记录的确定性文本。黄金值见 G1Golden。
        AssertG1(OneLayerConfig(), seed);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void 整批候选关闭时一层配置的专家黄金值与改前相同(ulong seed)
    {
        // ai-whole-batch 守门：专家（一层配置）与三名标准的整批候选开关都为 0 → 与引入整批候选之前的 G1 黄金值逐项相同（G1WholeBatchOffGolden）。
        // ai-bridge-candidate：两边同时关掉搭桥候选（引入整批候选之前没有它）。
        AssertG1(OneLayerConfig() with { WholeBatch = 0, EditSeed = 0 }, seed, wholeBatch: 0, goldens: G1WholeBatchOffGolden, editSeed: 0);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void 搭桥候选关闭时一层配置的专家黄金值与改前相同(ulong seed)
    {
        // ai-bridge-candidate 守门：专家（一层配置）与三名标准的搭桥候选开关都为 0（整批候选照缺省开）→ 与引入搭桥候选之前的 G1 黄金值逐项相同（G1EditSeedOffGolden）。
        AssertG1(OneLayerConfig() with { EditSeed = 0 }, seed, goldens: G1EditSeedOffGolden, editSeed: 0);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void 缺省预设的专家与一层配置逐步相同(ulong seed)
    {
        // retire-legacy-maps 1.4：测试名随规格 Scenario 改名（原「缺省预设的专家与改动前的专家逐步相同」），地图改 link:1（原 v5），断言不变。
        // 预设退回一层（负责人裁决 2026-09-28，段 B 后：扩样配对 好 7 / 同 33 / 差 20，p = 0.019，λ = 1000‰、S = 8 的专家弱于高难）：
        // 缺省专家预设（只改停手阈值为夹具写死的 80）在 link:1 种子 1–3 上与 G1 黄金值逐项相同。
        // 另钉解析路径：未显式给 Search 的专家玩家，会话落成的搜索配置就是这份预设。v5 本来不触发候选格上限；link:1 可落子格 > 150 会按地图落成 K = 24，
        // 这里显式给跑局配置 K = 0（候选格上限是跑局层参数，不是预设的一部分），使落成的配置仍逐字段等于预设、走法与 G1 同口径。
        // 变异 E-P1 / E-P2（预设的多样补充上限改回 8 / 两层权重改回 1000）→ 本测试红（见预设退回记录）。
        AiSearchConfig preset = AiSearchConfig.Expert with { PassThreshold = PassThreshold };
        MatchSession session = CreateSession(LinkConfig(new PlayerAiConfig { Difficulty = AiDifficulty.Expert }, Standard, Standard, Standard) with { CandidateCellLimit = 0 }, seed);
        // open-map 段 B：全知开关随拼接规则删除，落成的配置逐字段等于预设。
        Assert.Equal(preset, session.AiOf(P0)!.Config);
        AssertG1(preset, seed);
    }

    /// <summary>link:1 种子 1 整局（retire-legacy-maps 1.4：原 v5）：玩家 1 按给定难度与显式搜索配置，其余三名标准；返回日志与玩家 1 的决策日志。</summary>
    internal static (MatchLog Log, IReadOnlyList<string> Decisions) RunSeat1(AiDifficulty difficulty, AiSearchConfig search)
    {
        MatchSession session = CreateSession(
            LinkConfig(Standard, new PlayerAiConfig { Difficulty = difficulty, Search = search }, Standard, Standard), 1);
        HeuristicTurnController ai = session.AiOf(P1)!;
        // 显式的搜索配置原样生效。
        Assert.Equal(search, ai.Config);
        MatchLog log = session.Run();
        Assert.Null(log.Failure);
        return (log, ai.Decisions);
    }

    /// <summary>
    /// 两块各差一眼的棋（第 5 大回合、专家 P0 的部署上限 1）：左下 D3 / C4 / D4 / E4 / F4 已有眼 C3，F3 围出第二眼 E3；
    /// 右上（左下那块绕棋盘 0 中心转 180°）J10 / K9 / J9 / H9 / G9 已有眼 K10，G10 围出第二眼 H10。
    /// 停手阈值取 700：只有 F3、G10（做活）越得过 → 候选只有 {F3}、{G10} 两个。
    /// retire-legacy-rules 1.4：原 9×9 两块并排在底边（B1–D2 与 F2–J2，中间隔 E 列、P2 的 E3 挡住"一手同时补两块"），棋盘 0 只有 8 列放不下"4 + 1 + 4"，
    /// 改为左下一块（平移 (+2, +2)）+ 右上一块（同形转 180°），两块相距甚远，不再需要 P2 的挡子。
    /// </summary>
    private static MatchFlow TwoCandidatePosition()
    {
        MatchFlow match = AiFixtures.Round5()
            .Stones(P0, "D3", "C4", "D4", "E4", "F4", "J10", "K9", "J9", "H9", "G9");
        match.SetDeployLimit(1);
        return match;
    }

    private const int TwoCandidateThreshold = 700;

    // ---------- expert-tiebreak 1.2：构筑期不前瞻（design.md D1 / D2） ----------

    /// <summary>
    /// 接触局面（P0 与 P1 在棋盘 0 中部接触，落子同 <see cref="前瞻模拟的公平信息Tests.ContactPosition"/>；平坦 8×8 链接载体、四人同锁棋盘 0），摆到第 <paramref name="round"/> 大回合、给定顺序。
    /// 停手阈值 0 下基础候选有多个非空候选（用例逐个断言 ≥ 3）。
    /// </summary>
    internal static MatchFlow BuildContact(int round, PlayerId[] order) =>
        LinkModeFixtures.PlainStarted().AtRound(round, order)
            .Stones(P0, "E6", "F6", "F5").Stones(P1, "G6", "G7", "F7").Stones(P2, "D9").Stones(P3, "J4");

    /// <summary>第 3 大回合、顺序 P1 → P2 → P3 → P0（专家末位）：前三人先 Pass，轮到专家。</summary>
    internal static MatchFlow Round3LastSeat()
    {
        MatchFlow match = BuildContact(3, [P1, P2, P3, P0]);
        match.PassTurn();
        match.PassTurn();
        match.PassTurn();
        Assert.Equal(P0, match.CurrentPlayer);
        Assert.Equal(3, match.MajorRound);
        return match;
    }

    /// <summary>断言构筑期决策的前瞻记录：不前瞻、候选表只有高难的选择、前瞻前后分数相同、无对手，模拟 / 排除重跑 / 两层预演都为 0；专家与高难选同一个。</summary>
    internal static void AssertBuildPhase(HeuristicTurnController hard, HeuristicTurnController expert)
    {
        LookaheadRecord record = Record(expert);
        Assert.True(expert.LastCandidates.Count(c => !c.IsPass) >= 3, $"基础候选只有 {expert.LastCandidates.Count(c => !c.IsPass)} 个非空候选");
        Assert.Equal(hard.LastCandidates.Select(c => c.Key), expert.LastCandidates.Select(c => c.Key));
        Assert.Equal(LookaheadStatus.NotApplied, record.Status);
        LookaheadEntry only = Assert.Single(record.Entries);
        Assert.Equal(0, record.ChosenIndex);
        Assert.Equal(hard.LastChoice!.Key, only.CandidateKey);
        Assert.Equal(hard.LastChoice.Key, expert.LastChoice!.Key);
        Assert.Equal(hard.LastChoice.Total, only.ScoreBefore);
        Assert.Equal(only.ScoreBefore, only.ScoreAfter);
        Assert.Null(only.Responder);
        Assert.Null(only.ResponderRound);
        Assert.Null(only.ResponseKey);
        Assert.Equal(CandidateSource.Original, only.Source);
        Assert.Equal(BigInteger.Zero, only.TwoPlyBonus);
        Assert.Equal((0, 0, 0), (record.SimulatedRehearsals, record.SupplementRehearsals, record.TwoPlyRehearsals));
    }

    [Fact]
    public void 第3大回合末位照常前瞻()
    {
        // 规格（expert-tiebreak 1.2）：第 3 大回合专家是本大回合行动顺序中最后一名在局玩家，基础候选有 ≥ 3 个非空候选 →
        // 不是构筑期决策（下一名对手在第 4 大回合全图行动），按第 4–9 步前瞻，状态"已前瞻"；每个候选的下一名对手都按第 4 大回合模拟。
        // 变异 T-B1（判据改成 r ≤ R）→ 本测试红。
        HeuristicTurnController expert = DecideExpert(Round3LastSeat());
        LookaheadRecord record = Record(expert);

        Assert.True(expert.LastCandidates.Count(c => !c.IsPass) >= 3, record.ToText());
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.True(record.Entries.Length >= 2, record.ToText());
        Assert.All(record.Entries, e =>
        {
            Assert.NotNull(e.Responder);
            Assert.Equal(4, e.ResponderRound);
        });
        Assert.True(record.SimulatedRehearsals > 0);
    }

    [Fact]
    public void 第3大回合非末位不前瞻()
    {
        // 规格：第 3 大回合专家之后还有在局玩家，基础候选有 ≥ 3 个非空候选 → 构筑期决策：专家选同一局面上高难的选择，状态"不前瞻"，
        // 不做多样补充的排除重跑、不模拟、不做两层扫描（打开 S 8 / λ 1000 / B 20 也一样）。另取第 1、2 大回合（r < R）与第 3 大回合居中（前面的 P1 已 Pass）。
        // 变异 T-B2（判据只看 r：r < R）、T-B3（构筑期仍跑排除重跑）→ 本测试红。
        (int Round, PlayerId[] Order, int Passes)[] cases =
        [
            (3, [P0, P1, P2, P3], 0),
            (3, [P1, P0, P2, P3], 1),
            (2, [P0, P1, P2, P3], 0),
            (1, [P0, P1, P2, P3], 0),
        ];
        foreach ((int round, PlayerId[] order, int passes) in cases)
        {
            MatchFlow Position()
            {
                MatchFlow match = BuildContact(round, order);
                for (int i = 0; i < passes; i++)
                {
                    match.PassTurn();
                }

                Assert.Equal(P0, match.CurrentPlayer);
                Assert.Equal(round, match.MajorRound);
                return match;
            }

            HeuristicTurnController hard = DecideHard(Position());
            AssertBuildPhase(hard, DecideExpert(Position()));
            AssertBuildPhase(hard, Decide(Position(), AiDifficulty.Expert, StrengthConfig() with { TwoPlyTieBand = 20 }));
        }
    }

    [Fact]
    public void 其后只剩已出局者时不算构筑期()
    {
        // 规格：第 3 大回合专家之后的玩家都已弃赛或出局 → 不是构筑期决策（下一名对手按第 4 大回合预测）。
        // 顺序 P1 → P0（专家）→ P2 → P3：P1 先在 D8 提走 P2 在盘上唯一的子 D9（P2 曾建立正势力 → 出局），P3 弃赛。专家之后只剩出局者与弃赛者 → 已前瞻，下一名对手 P1 @ 第 4 大回合。
        // 对照：P3 不弃赛时专家之后仍有在局玩家 → 构筑期，不前瞻（判定不看 Pass 名单，只看参赛状态）。
        // 变异 T-B4（"在局"不看参赛状态，按顺序位置判）→ 本测试红。
        MatchFlow Position(bool resignP3)
        {
            MatchFlow match = BuildContact(3, [P1, P0, P2, P3]).Stones(P1, "C9", "E9", "D10");
            match.PlayTurn("D8");
            Assert.Equal(PlayerStatus.Eliminated, match.StateOf(P2).Status);
            if (resignP3)
            {
                match.Resign(P3);
                Assert.Equal(PlayerStatus.Resigned, match.StateOf(P3).Status);
            }

            Assert.Equal(P0, match.CurrentPlayer);
            return match;
        }

        HeuristicTurnController expert = DecideExpert(Position(resignP3: true));
        LookaheadRecord record = Record(expert);
        Assert.True(expert.LastCandidates.Count(c => !c.IsPass) >= 3, record.ToText());
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.All(record.Entries, e =>
        {
            Assert.Equal(P1, e.Responder);
            Assert.Equal(4, e.ResponderRound);
        });

        AssertBuildPhase(DecideHard(Position(resignP3: false)), DecideExpert(Position(resignP3: false)));
    }

    [Fact]
    public void 构筑期专家与高难逐步相同()
    {
        // 守门（expert-tiebreak D6 / tasks 1.6）：link:1、对局种子 1–3、内置固定配对脚本、1 专家（座位 1）+ 3 标准、写死权重与停手阈值（LinkConfig），截断到第 2 大回合结束；
        // 专家分别取专家预设与"W 4 / S 8 / λ 1000 / B 20"，对照同座位换成高难的对局：每一步决策与 ai-P0 子流的消费逐项相同；
        // 专家的每条前瞻记录都是"不前瞻"或"Pass"，排除重跑与两层扫描（及模拟）的预演次数都为 0。跑局配置给候选格上限 0，使专家与高难落成同一 K。
        // 变异 T-B5（去掉构筑期判定）→ 本测试红。
        AiSearchConfig[] experts =
        [
            AiSearchConfig.Expert with { PassThreshold = PassThreshold },
            AiSearchConfig.Expert with { PassThreshold = PassThreshold, DiverseSupplementLimit = StrengthSupplement, TwoPlyWeightPermille = StrengthPermille, TwoPlyTieBand = 20 },
        ];
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            (IReadOnlyList<string> hardDecisions, List<long> hardConsumed, _) = RunSeat0ToRound(AiDifficulty.Hard, AiSearchConfig.Hard with { PassThreshold = PassThreshold }, seed, 2);
            Assert.True(hardDecisions.Count >= 2, $"种子 {seed}：高难只有 {hardDecisions.Count} 条决策");
            Assert.True(hardConsumed.Count >= 2 && hardConsumed[^1] > 0, $"种子 {seed}：高难部署 {hardConsumed.Count} 次、没有消费扰动子流");
            foreach (AiSearchConfig search in experts)
            {
                (IReadOnlyList<string> decisions, List<long> consumed, List<LookaheadRecord> records) = RunSeat0ToRound(AiDifficulty.Expert, search, seed, 2);
                Assert.Equal(hardDecisions, decisions);
                Assert.Equal(hardConsumed, consumed);   // 每次部署之后的累计消费逐项相同
                Assert.NotEmpty(records);
                Assert.Contains(records, r => r.Status == LookaheadStatus.NotApplied);
                Assert.All(records, r =>
                {
                    Assert.Contains(r.Status, new[] { LookaheadStatus.NotApplied, LookaheadStatus.Pass });
                    Assert.Equal((0, 0, 0), (r.SimulatedRehearsals, r.SupplementRehearsals, r.TwoPlyRehearsals));
                    Assert.True(r.Entries.Length <= 1, r.ToText());
                });
            }
        }
    }

    /// <summary>
    /// <c>link:1</c> 一局（1.6 守门口径）：座位 1（P0）按 <paramref name="difficulty"/> / <paramref name="search"/>，其余三名标准，内置固定配对脚本、跑局候选格上限 0、截断到第 <paramref name="rounds"/> 大回合结束；
    /// 返回 P0 的决策序列（含征募与整理）、每次部署之后 ai-P0 子流的累计消费次数与每次部署的前瞻记录（高难没有记录）。
    /// </summary>
    internal static (IReadOnlyList<string> Decisions, List<long> Consumed, List<LookaheadRecord> Records) RunSeat0ToRound(AiDifficulty difficulty, AiSearchConfig search, ulong seed, int rounds)
    {
        MatchSession session = CreateSession(
            LinkConfig(new PlayerAiConfig { Difficulty = difficulty, Search = search }, Standard, Standard, Standard) with { CandidateCellLimit = 0 }, seed);
        Siege.Core.Determinism.RandomStream stream = session.Match.Seed.Stream(HeuristicAi.StreamName(P0));
        var inner = new HeuristicTurnController(P0, session.Match.Publish, stream, difficulty, Weights, search);
        var consumed = new List<long>();
        var records = new List<LookaheadRecord>();
        session.SetController(P0, new ProbeController(inner, _ => { }, () =>
        {
            consumed.Add(stream.Consumed);
            if (inner.LastLookahead is { } record)
            {
                records.Add(record);
            }
        }));
        RunToRound(session, rounds);
        return (inner.Decisions, consumed, records);
    }

    [Fact]
    public void 前瞻集不足W个()
    {
        // 候选生成去重后只有 2 个非空候选 → 前瞻集为这 2 个，两者都做模拟。
        HeuristicTurnController expert = DecideExpert(TwoCandidatePosition(), TwoCandidateThreshold);
        LookaheadRecord record = Record(expert);

        Assert.Equal(2, expert.LastCandidates.Count(c => !c.IsPass));
        Assert.Equal(LookaheadStatus.Applied, record.Status);
        Assert.Equal(["F3:Basic", "G10:Basic"], record.Entries.Select(e => e.CandidateKey));
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
        // ① 两个候选、前瞻宽度 1；② 前瞻宽度 4、但只剩左下一块差一眼的棋 → 候选只有 {F3}。两种情形都与同一局面上的高难选同一个。
        static MatchFlow OneCandidate()
        {
            MatchFlow match = AiFixtures.Round5().Stones(P0, "D3", "C4", "D4", "E4", "F4");
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
        // cycle-stop 2.2：四档预设带停滞大回合数 6（接在末尾；为 0 不写出——按 0 的记录与改动前逐字节相同，见下面 K = 0 的三行）。
        // ai-whole-batch 1.2：四档预设带整批候选开关 1（接在停滞大回合数之后；为 0 不写出——K = 0 且开关 0 的三行与改动前逐字节相同）。
        // ai-bridge-candidate 1.2：四档预设带搭桥候选开关 1（接在整批候选开关之后；为 0 不写出——K = 0 且两个开关都为 0 的三行与改动前逐字节相同）。
        Assert.Equal("""{"CandidatePointCount":6,"CandidateBatchCount":1,"ImmediateOnly":true,"CandidateCellLimit":0,"PassThreshold":80,"StagnationRounds":6,"WholeBatch":1,"EditSeed":1}""",
            JsonSerializer.Serialize(AiSearchConfig.Easy with { PassThreshold = LookaheadFixtures.PassThreshold }));
        Assert.Equal("""{"CandidatePointCount":12,"CandidateBatchCount":8,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"StagnationRounds":6,"WholeBatch":1,"EditSeed":1}""",
            JsonSerializer.Serialize(AiSearchConfig.Standard with { PassThreshold = LookaheadFixtures.PassThreshold }));
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"StagnationRounds":6,"WholeBatch":1,"EditSeed":1}""",
            JsonSerializer.Serialize(AiSearchConfig.Hard with { PassThreshold = LookaheadFixtures.PassThreshold }));
        Assert.Equal("""{"CandidatePointCount":6,"CandidateBatchCount":1,"ImmediateOnly":true,"CandidateCellLimit":0,"PassThreshold":80}""",
            JsonSerializer.Serialize(AiSearchConfig.Easy with { PassThreshold = LookaheadFixtures.PassThreshold, StagnationRounds = 0, WholeBatch = 0, EditSeed = 0 }));
        Assert.Equal("""{"CandidatePointCount":12,"CandidateBatchCount":8,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80}""",
            JsonSerializer.Serialize(AiSearchConfig.Standard with { PassThreshold = LookaheadFixtures.PassThreshold, StagnationRounds = 0, WholeBatch = 0, EditSeed = 0 }));
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80}""",
            JsonSerializer.Serialize(AiSearchConfig.Hard with { PassThreshold = LookaheadFixtures.PassThreshold, StagnationRounds = 0, WholeBatch = 0, EditSeed = 0 }));
        // expert-strength：专家预设退回一层（负责人裁决 2026-09-28，段 B 后：多样补充上限 0、两层权重 0，为 0 不写出）——专家一行回到 expert-lookahead 时的字面量；
        // 三档旧难度的三行一字不改。两项非 0 即写出、字段次序接在前瞻宽度之后，由显式 S 8 / λ 1000 的配置钉住。
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"LookaheadWidth":4,"StagnationRounds":6,"WholeBatch":1,"EditSeed":1}""",
            JsonSerializer.Serialize(AiSearchConfig.Expert with { PassThreshold = LookaheadFixtures.PassThreshold }));
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"LookaheadWidth":4,"DiverseSupplementLimit":8,"TwoPlyWeightPermille":1000,"StagnationRounds":6,"WholeBatch":1,"EditSeed":1}""",
            JsonSerializer.Serialize(ExpandedExpert with { PassThreshold = LookaheadFixtures.PassThreshold }));
        // 一层配置（两项为 0）的序列化与 expert-lookahead 时代的专家预设逐字节相同（ai-whole-batch：一层配置随预设带开关 1，关掉后即引入前的一层配置）。
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"LookaheadWidth":4}""",
            JsonSerializer.Serialize(OneLayerConfig() with { WholeBatch = 0, EditSeed = 0 }));

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

        // expert-tiebreak 1.1：两层平局带宽为 0 时不写出——上面专家预设、一层配置与 S 8 / λ 1000 三行字面量不含该字段，配置记录同样不含。
        Assert.DoesNotContain("TwoPlyTieBand", json, StringComparison.Ordinal);
        Assert.Equal(0, back.Players[0].Search!.TwoPlyTieBand);
    }

    [Fact]
    public void 显式给出的多样补充上限两层权重与带宽进入记录()
    {
        // expert-tiebreak 1.1 算例：{前瞻宽度 4, 多样补充上限 8, 两层权重 1000‰, 两层平局带宽 20} 依次写出 W / S / λ / B（B 紧接两层权重、在停滞大回合数之前）；
        // 三名标准玩家不含这些字段。配置记录（RunConfig.ToJson / FromJson）与日志首部（MatchLog）都走同一个序列化。
        // 变异 T-C1（B 为 0 照样写出）→ 前瞻宽度进入记录 红；本测试钉字段次序与非 0 写出。
        AiSearchConfig banded = ExpandedExpert with { PassThreshold = LookaheadFixtures.PassThreshold, TwoPlyTieBand = 20 };
        Assert.Equal("""{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"LookaheadWidth":4,"DiverseSupplementLimit":8,"TwoPlyWeightPermille":1000,"TwoPlyTieBand":20,"StagnationRounds":6,"WholeBatch":1,"EditSeed":1}""",
            JsonSerializer.Serialize(banded));   // ai-whole-batch 1.2：预设带整批候选开关 1，接在末尾

        var config = new RunConfig
        {
            Players =
            [
                new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = banded },
                new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Search = AiSearchConfig.Standard },
                new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Search = AiSearchConfig.Standard },
                new PlayerAiConfig { Difficulty = AiDifficulty.Standard, Search = AiSearchConfig.Standard },
            ],
        };
        string json = config.ToJson();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "TwoPlyTieBand"));
        Assert.Contains("\"TwoPlyTieBand\": 20", json, StringComparison.Ordinal);
        int w = json.IndexOf("\"LookaheadWidth\"", StringComparison.Ordinal);
        int s = json.IndexOf("\"DiverseSupplementLimit\"", StringComparison.Ordinal);
        int l = json.IndexOf("\"TwoPlyWeightPermille\"", StringComparison.Ordinal);
        int b = json.IndexOf("\"TwoPlyTieBand\"", StringComparison.Ordinal);
        Assert.True(w >= 0 && w < s && s < l && l < b, json);
        RunConfig back = RunConfig.FromJson(json);
        Assert.Equal(banded, back.Players[0].Search);
        Assert.All(back.Players.Skip(1), p => Assert.Equal(0, p.Search!.TwoPlyTieBand));

        // 日志首部：专家一名记有 B 20（紧接两层权重），三名标准不含；离线解析按首部读回。
        RunConfig run = LinkConfig(new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = banded }, Standard, Standard, Standard) with { TurnLimit = 2 };
        MatchLog log = CreateSession(run, 1).Run();
        string header = log.DeterministicText().Split('\n')[0];
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(header, "TwoPlyTieBand"));
        Assert.Contains("\"LookaheadWidth\":4,\"DiverseSupplementLimit\":8,\"TwoPlyWeightPermille\":1000,\"TwoPlyTieBand\":20", header, StringComparison.Ordinal);
        MatchLog parsed = MatchLog.Parse(log.FullText());
        Assert.Equal([20, 0, 0, 0], Enumerable.Range(0, 4).Select(parsed.TwoPlyTieBandOf));
        Assert.Equal(banded, parsed.Header.Config.Players[0].Search);
    }

    [Fact]
    public void 无效带宽被拒绝()
    {
        // expert-tiebreak 1.1：两层平局带宽为负、或大于 0 而两层权重为 0 的配置被拒绝，不静默改为 0（design.md Open Questions 3 的文案）。
        // 入口：搜索配置的 Validated()（控制者构造、配置文件 RunConfig.FromJson、日志首部重建会话 MatchSession.Create 共用）。
        // 变异 T-C2（放过"B > 0 而 λ = 0"）、T-C3（放过负数）→ 本测试红。
        Assert.Throws<ArgumentOutOfRangeException>(() => (ExpandedExpert with { TwoPlyTieBand = -1 }).Validated());
        ArgumentException noWeight = Assert.Throws<ArgumentException>(() => (AiSearchConfig.Expert with { TwoPlyTieBand = 20 }).Validated());
        Assert.Contains("两层平局带宽只在两层权重 > 0 时生效", noWeight.Message, StringComparison.Ordinal);
        Assert.Equal(20, (ExpandedExpert with { TwoPlyTieBand = 20 }).Validated().TwoPlyTieBand);
        Assert.Equal(0, (AiSearchConfig.Expert with { TwoPlyTieBand = 0 }).Validated().TwoPlyTieBand);

        // 配置文件入口：报错退出，不改成 0。
        RunConfig Bad(AiSearchConfig search) => new()
        {
            Players =
            [
                new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = search },
                Standard, Standard, Standard,
            ],
        };
        Assert.Throws<ArgumentException>(() => RunConfig.FromJson(Bad(AiSearchConfig.Expert with { TwoPlyTieBand = 20 }).ToJson()));
        Assert.Throws<ArgumentOutOfRangeException>(() => RunConfig.FromJson(Bad(ExpandedExpert with { TwoPlyTieBand = -1 }).ToJson()));

        // 日志首部入口：首部里 B 20 而 λ 为 0 的配置在重建会话（回放走的同一入口）时被拒绝。
        RunConfig run = LinkConfig(new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = ExpandedExpert with { PassThreshold = LookaheadFixtures.PassThreshold, TwoPlyTieBand = 20 } }, Standard, Standard, Standard) with { TurnLimit = 1 };
        string text = CreateSession(run, 1).Run().FullText();
        Assert.Contains("\"TwoPlyWeightPermille\":1000,", text, StringComparison.Ordinal);
        MatchLog forged = MatchLog.Parse(text.Replace("\"TwoPlyWeightPermille\":1000,", string.Empty, StringComparison.Ordinal));
        Assert.Equal(20, forged.TwoPlyTieBandOf(0));
        Assert.Equal(0, forged.TwoPlyWeightPermilleOf(0));
        Assert.Throws<ArgumentException>(() => CreateSession(forged.Header.Config, 1));

        // 控制者构造同样拒绝。
        Assert.Throws<ArgumentException>(() => HeuristicAi.Create(AtariPosition(), P0, AiDifficulty.Expert, Weights, AiSearchConfig.Expert with { TwoPlyTieBand = 20 }));
    }

    [Fact]
    public void 旧记录按不前瞻读入()
    {
        // 缺前瞻宽度字段的旧记录按 0 读入（同 PassThreshold 的先例）：配置文件与日志首部里的 Search 都走同一个反序列化。
        const string legacy = """{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80}""";
        AiSearchConfig old = JsonSerializer.Deserialize<AiSearchConfig>(legacy)!;
        Assert.Equal(0, old.LookaheadWidth);
        // v2-recalibration 段 A（1.6）：旧记录里的阈值是写入时的 80，与现行缺省无关——比对对象写死同一阈值，旧记录字面量不改。
        // cycle-stop 2.2：缺停滞大回合数同样按 0 读。
        // ai-whole-batch 1.2：缺整批候选开关同样按 0 读。
        Assert.Equal(AiSearchConfig.Hard with { PassThreshold = LookaheadFixtures.PassThreshold, StagnationRounds = 0, WholeBatch = 0, EditSeed = 0 }, old);
        Assert.Equal(0, old.StagnationRounds);
        Assert.Equal(4, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(AiSearchConfig.Expert))!.LookaheadWidth);

        // expert-strength：多样补充上限与两层权重同一口径——缺字段按 0 读；旧专家记录（只有前瞻宽度 4）读为一层配置，即退回后的专家预设；显式两项往返不丢。
        Assert.Equal(0, old.DiverseSupplementLimit);
        Assert.Equal(0, old.TwoPlyWeightPermille);
        const string legacyExpert = """{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":0,"PassThreshold":80,"LookaheadWidth":4}""";
        Assert.Equal(OneLayerConfig() with { WholeBatch = 0, EditSeed = 0 }, JsonSerializer.Deserialize<AiSearchConfig>(legacyExpert));   // ai-whole-batch：旧专家记录缺开关按 0 读
        Assert.Equal(AiSearchConfig.Expert with { PassThreshold = LookaheadFixtures.PassThreshold, StagnationRounds = 0, WholeBatch = 0, EditSeed = 0 }, JsonSerializer.Deserialize<AiSearchConfig>(legacyExpert));   // cycle-stop：旧专家记录缺 K 按 0 读
        Assert.Equal(AiSearchConfig.Expert, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(AiSearchConfig.Expert)));
        Assert.Equal(ExpandedExpert, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(ExpandedExpert)));

        // expert-tiebreak 1.1：缺两层平局带宽按 0 读（旧记录、旧专家记录、S 8 / λ 1000 的记录都没有该字段）；显式 B 往返不丢。
        Assert.Equal(0, old.TwoPlyTieBand);
        Assert.Equal(0, JsonSerializer.Deserialize<AiSearchConfig>(legacyExpert)!.TwoPlyTieBand);
        Assert.Equal(0, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(ExpandedExpert))!.TwoPlyTieBand);
        AiSearchConfig banded = ExpandedExpert with { TwoPlyTieBand = 20 };
        Assert.Equal(banded, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(banded)));

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
