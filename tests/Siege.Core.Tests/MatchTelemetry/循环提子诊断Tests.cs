using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：openspec/changes/cycle-stop/specs/match-telemetry —— Requirement: 循环提子诊断（ADDED）；stagnation-trace 1.2 加 Scenario「停滞停手次数可查」。
/// 四条 Scenario 同名；夹具为手写日志（分析口径用，不跑对局），「诊断不改对局」用一局真实 AI 链接局，「停滞停手次数可查」另用一局真实 AI 链接局交叉核对。
/// </summary>
/// <remarks>
/// <para>局部键 = 窗口内被提格 ∪ 四邻的占用者 + 设施；周期只数局部键实际变化后的"回到先前状态"（连续 Pass 不算）。</para>
/// <para>变异验证（cycle-stop 1.1 实跑，每条只跑本类）：
/// M-CS1 判定 <c>≥ 2</c> 改 <c>≥ 3</c> → 「循环局被识别」红 1（夹具恰好 2 名净势力 ≤ 0；首版三人全 ≤ 0 时 0 红，已改样本）；
/// M-CS2 判定去掉 <c>period is not null</c> → 「正常收官不误判」红 1；
/// M-CS3 <c>ShortestPeriod</c> 不压缩连续相同键（每个键都记成一个状态）→ 「正常收官不误判」红 1（整轮 Pass 的末段连续相同状态被当成周期 1）；
///   注：把配对循环的 <c>i &lt; j - 1</c> 改 <c>i &lt; j</c> 是等价变异（压缩后相邻状态必不同），0 红，不作为守门证据；
/// M-CS4 <c>BalanceAnalyzer.Analyze</c> 不看开关、一律做诊断 → 「诊断不改对局」红 1（关闭时报告多出一段）。</para>
/// <para>stagnation-trace 1.2 变异（实跑记录见任务 notes.md）：M-TC1 计数改数 <c>Passed</c> → 「停滞停手次数可查」红；M-TC2 跑局层快照不写 <c>Stalled</c> → 「停滞停手次数可查」红；
/// M-TC3 只数窗口内的快照 → 「停滞停手次数可查」红。</para>
/// </remarks>
public class 循环提子诊断Tests
{
    private const string BasicStone = "Basic";

    /// <summary>手写快照：各玩家的全部棋子（占用者）、本小回合的落子 / 提子与总势力；别的字段取缺省。</summary>
    private static TurnSnapshot Snapshot(int turn, int round, int player, string[][] stonesOf, long[] totals, string[]? placements = null, string[]? captures = null) =>
        new()
        {
            Turn = turn,
            MajorRound = round,
            Player = player,
            Passed = placements is null,
            Placements = [.. placements ?? []],
            Captures = [.. captures ?? []],
            Edits = [],
            Life = new LifeTurnEntry(),
            ActionOrder = [.. Enumerable.Range(0, totals.Length)],
            PlayersState = [.. totals.Select((t, i) => new PlayerEntry
            {
                Player = i,
                Status = "Active",
                HasEstablishedPower = true,
                Total = t,
                Groups = stonesOf[i].Length == 0 ? [] : [new GroupEntry { Stones = [.. stonesOf[i]], Base = stonesOf[i].Length, Power = stonesOf[i].Length }],
            })],
        };

    /// <summary>
    /// 规格算例：3 名玩家在 D4 / E4 / D5 / E5 四格上轮流提子，别处（P / Q / R 列，远离局部键的四邻）每大回合各加 1 子，共 14 个大回合后被 turn_limit 截断。
    /// 每个大回合：P0 落 D4 提 E4(P1) 与 E5(P2) → P1 落 E4 提 D5(P2) → P2 落 D5、E5 提 D4(P0)；局部状态每 3 个小回合回到原样。
    /// </summary>
    private static MatchLog CyclicLog(int rounds = 14, long[]? totals = null)
    {
        totals ??= [10, 10, 10];
        var turns = new List<TurnSnapshot>();
        var elsewhere = new List<string>[] { [], [], [] };
        int turn = 0;
        for (int round = 1; round <= rounds; round++)
        {
            elsewhere[0].Add($"P{round}");
            turns.Add(Snapshot(++turn, round, 0,
                [["D4", .. elsewhere[0]], [.. elsewhere[1]], ["D5", .. elsewhere[2]]], totals,
                [$"D4:{BasicStone}", $"P{round}:{BasicStone}"], [$"E4:P1:{BasicStone}", $"E5:P2:{BasicStone}"]));
            elsewhere[1].Add($"Q{round}");
            turns.Add(Snapshot(++turn, round, 1,
                [["D4", .. elsewhere[0]], ["E4", .. elsewhere[1]], [.. elsewhere[2]]], totals,
                [$"E4:{BasicStone}", $"Q{round}:{BasicStone}"], [$"D5:P2:{BasicStone}"]));
            elsewhere[2].Add($"R{round}");
            turns.Add(Snapshot(++turn, round, 2,
                [[.. elsewhere[0]], ["E4", .. elsewhere[1]], ["D5", "E5", .. elsewhere[2]]], totals,
                [$"D5:{BasicStone}", $"E5:{BasicStone}", $"R{round}:{BasicStone}"], [$"D4:P0:{BasicStone}"]));
        }

        return SimFixtures.Synthetic(0x4343, turns, [], SimFixtures.TruncatedResult(rounds, players: 3), players: 3);
    }

    /// <summary>把指定玩家的总势力改成逐小回合上升（10 + 小回合序号），其余玩家不动。</summary>
    private static MatchLog Rising(MatchLog log, params int[] players)
    {
        for (int i = 0; i < log.Turns.Count; i++)
        {
            int t = log.Turns[i].Turn;
            log.Turns[i] = log.Turns[i] with
            {
                PlayersState = [.. log.Turns[i].PlayersState.Select(p => players.Contains(p.Player) ? p with { Total = new BigInteger(10 + t) } : p)],
            };
        }

        return log;
    }

    [Fact]
    public void 循环局被识别()
    {
        // 规格 Scenario：三名玩家在同一片格互相提子、被 turn_limit 截断 → 判"循环提子"，给出局部周期、参与者与各自净势力变化 ≤ 0。
        // 算例（tasks 1.1）：周期 = 3 名玩家各一小回合 = 3；被提格 |C| = 4；窗口 = 最后 12 个大回合（R3–R14，36 小回合），别处新增落子 = 12 × 3 = 36。
        // P2 的势力逐小回合上升（净 > 0），P0 / P1 恒 10（净 0）：恰好 2 名玩家净势力 ≤ 0——判定阈值 ≥ 2 的下界样本。
        MatchLog log = Rising(CyclicLog(), 2);
        Assert.Equal(42, log.Turns.Count);   // 样本口径：14 个大回合 × 3 人
        Assert.True(log.Result!.Truncated);

        CycleReviewSection section = Assert.IsType<CycleReviewSection>(BalanceAnalyzer.Analyze([log], new AnalysisOptions { ReviewCycles = true }).CycleReview);
        Assert.Equal((1, 1, 1, 1), (section.Logs, section.Truncated, section.Cyclic, section.CyclicTruncated));
        CycleReviewEntry e = Assert.Single(section.Entries);

        Assert.True(e.Cyclic);
        Assert.Equal((3, 14, 36), (e.WindowFirstRound, e.EndMajorRound, e.WindowTurns));
        Assert.Equal(3, e.Period);
        Assert.Equal(36 - 3, e.Repeats);   // 36 个局部状态，前 3 个是新状态，其余每个都回到先前状态
        Assert.Equal(["D4", "E4", "D5", "E5"], e.CapturedCells);   // 坐标顺序（行优先）
        Assert.Equal(12, e.LocalCells);     // 2×2 方块 ∪ 四邻 = 4 + 8
        Assert.Equal(36, e.OutsidePlacements);
        Assert.Equal(36, e.OutsideChanges); // 每个小回合都在别处加了 1 子
        Assert.Equal(0, e.LocalEdits);
        Assert.Equal((36, 36, 24 + 24 + 36, 48 - 1), (e.MoveTurns, e.CaptureTurns, e.TotalPlacements, e.RecapturePlacements));   // C 上的 48 次落子里只有窗口第一手（D4）之前没有本窗口内的提子记录

        Assert.Equal([0, 1, 2], e.Players.Select(p => p.Player));
        Assert.Equal([true, true, false], e.Players.Select(p => p.NetPower <= 0));
        Assert.Equal(2, e.Players.Count(p => p.NetPower <= 0));   // 样本口径：恰好 2 名（≥ 2 的下界）
        Assert.Equal([24, 24, 36], e.Players.Select(p => p.Placements));
        Assert.Equal([24, 12, 12], e.Players.Select(p => p.Captures));
        Assert.Equal([12, 12, 24], e.Players.Select(p => p.Captured));
        Assert.Equal([(12, 12), (12, 12), (0, 0)], e.Players.Select(p => (p.StagnationAtEnd, p.MaxStagnation)));   // 势力恒 10：第 1 大回合首为 0、第 2 大回合首为 10（有净增），第 3–14 大回合首均无净增 → 12；P2 逐回合上升 → 0

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze([log], new AnalysisOptions { ReviewCycles = true }));
        Assert.Contains("判为循环提子 1 局（截断局中 1 局", text, StringComparison.Ordinal);
        Assert.Contains("0000000000004343 截断 R14 窗口 R3–R14（36 小回合）；循环提子；周期 3 小回合 × 33；被提格 4 / 局部格 12；C 之外新增落子 36", text, StringComparison.Ordinal);

        // 只有一名玩家净势力 ≤ 0 时不判循环（判定的两个条件缺一不可）；周期照常报告。
        CycleReviewEntry twoRising = CycleAnalyzer.ReviewMatch(Rising(CyclicLog(), 0, 1), 12);
        Assert.Equal(3, twoRising.Period);
        Assert.Equal(1, twoRising.Players.Count(p => p.NetPower <= 0));
        Assert.False(twoRising.Cyclic);
    }

    [Fact]
    public void 正常收官不误判()
    {
        // 规格 Scenario：以整轮 Pass 正常终局、末段只有零星提子的日志 → 不判"循环提子"。
        // 夹具：4 人 15 个大回合；第 5 与第 9 大回合各提一次子（被提格不再被动），末段全员 Pass（局部键连续相同——不是"回到先前状态"）；
        // P1、P2 势力整段不变（净势力 ≤ 0 的玩家 ≥ 2 成立），判定只能靠"不存在局部周期"挡住。
        var turns = new List<TurnSnapshot>();
        string[][] stones = [["A1", "A2"], ["H1", "H2"], ["A8"], ["H8"]];
        long[] totals = [4, 3, 1, 1];
        int turn = 0;
        for (int round = 1; round <= 15; round++)
        {
            for (int player = 0; player < 4; player++)
            {
                string[]? placements = null;
                string[]? captures = null;
                if (round == 5 && player == 0)
                {
                    stones = [["A1", "A2", "B2"], ["H1", "H2"], [], ["H8"]];   // P0 落 B2 提 A8(P2)（示意，占用者口径）
                    placements = ["B2:Basic"];
                    captures = ["A8:P2:Basic"];
                    totals = [8, 3, 0, 1];
                }
                else if (round == 9 && player == 3)
                {
                    stones = [["A1", "A2", "B2"], ["H2"], [], ["H8", "G1"]];   // P3 落 G1 提 H1(P1)
                    placements = ["G1:Basic"];
                    captures = ["H1:P1:Basic"];
                    totals = [8, 1, 0, 3];
                }
                else if (round <= 10 && player == 0)
                {
                    stones = [[.. stones[0], $"C{round}"], stones[1], stones[2], stones[3]];   // 别处零星落子
                    placements = [$"C{round}:Basic"];
                    totals = [totals[0] + 1, totals[1], totals[2], totals[3]];
                }

                turns.Add(Snapshot(++turn, round, player, stones, totals, placements, captures));
            }
        }

        MatchLog log = SimFixtures.Synthetic(0x5151, turns, [], SimFixtures.ResultOf(15, [0]));
        Assert.Equal(LogResult.LegacyAllPassed, log.Result!.Reason);
        Assert.Contains(log.Turns.Where(t => t.MajorRound >= 4), t => t.Captures.Count > 0);   // 样本口径：窗口内确有提子

        CycleReviewSection section = Assert.IsType<CycleReviewSection>(BalanceAnalyzer.Analyze([log], new AnalysisOptions { ReviewCycles = true }).CycleReview);
        Assert.Equal((1, 0, 0), (section.Logs, section.Truncated, section.Cyclic));
        CycleReviewEntry e = Assert.Single(section.Entries);
        Assert.False(e.Cyclic);
        Assert.Null(e.Period);
        Assert.Equal(0, e.Repeats);
        Assert.Equal(["H1", "A8"], e.CapturedCells);   // 坐标顺序（行优先）
        Assert.True(e.Players.Count(p => p.NetPower <= 0) >= 2, "样本口径：净势力 ≤ 0 的玩家 ≥ 2，判定只靠周期挡");
        Assert.Equal(0, e.LocalEdits);
        Assert.Equal((8, 2, 8, 8, 0), (e.MoveTurns, e.CaptureTurns, e.TotalPlacements, e.OutsidePlacements, e.RecapturePlacements));   // 窗口 R4–R15：P0 第 4–10 大回合各落 1 子（第 5 大回合落的是提子手 B2）+ P3 第 9 大回合落 1 子，都不在 C 上
        Assert.Contains("判为循环提子 0 局", ReportWriter.Render(BalanceAnalyzer.Analyze([log], new AnalysisOptions { ReviewCycles = true })), StringComparison.Ordinal);

        // 周期压缩：连续相同键不算回到先前状态；中间经过别的状态再回来才算。
        Assert.Equal(((int?)null, 0), CycleAnalyzer.ShortestPeriod(["a", "a", "a", "b", "b"]));
        Assert.Equal(((int?)3, 1), CycleAnalyzer.ShortestPeriod(["a", "b", "b", "a"]));
        Assert.Equal(((int?)2, 2), CycleAnalyzer.ShortestPeriod(["a", "b", "a", "b"]));
    }

    [Fact]
    public void 停滞停手次数可查()
    {
        // 规格 Scenario（stagnation-trace 1.2）：以显式开关分析一批含停滞停手的日志 → 报告给出每局与汇总的停滞停手次数，与日志中 D:stall 条目数相同。
        // 决策条目不随对局日志写出（S1）：日志里的留痕是快照 Stalled = true（只在部署决策条目为 D:stall 的小回合写出），分析按它计数；缺字段的旧日志计 0。
        // 手写批次：A 局 3 次停手（第 1 / 7 / 14 大回合；第 1 大回合在末段窗口 R3–R14 之外——整局计、不限窗口）、B 局（旧日志，无该字段）0 次、
        // C 局没有结果行（未完成）被排除——它的 5 次停手 MUST NOT 计入汇总。
        MatchLog a = CyclicLog();
        foreach (int i in new[] { 0, 20, 40 })
        {
            a.Turns[i] = a.Turns[i] with { Stalled = true };
        }

        MatchLog b = Rising(CyclicLog(), 2);
        MatchLog c = CyclicLog();
        for (int i = 0; i < 5; i++)
        {
            c.Turns[i] = c.Turns[i] with { Stalled = true };
        }

        c.Result = null;
        c = new MatchLog { Header = c.Header with { Seed = "0000000000009999" }, Turns = c.Turns, Events = c.Events };
        a = new MatchLog { Header = a.Header with { Seed = "0000000000001111" }, Turns = a.Turns, Events = a.Events, Result = a.Result };
        b = new MatchLog { Header = b.Header with { Seed = "0000000000002222" }, Turns = b.Turns, Events = b.Events, Result = b.Result };
        Assert.DoesNotContain(b.FullText().Split('\n'), l => l.Contains("\"Stalled\"", StringComparison.Ordinal));   // 样本口径：B 是不含该字段的旧式日志
        Assert.Contains(MatchLog.Parse(a.FullText()).Turns, t => t.Stalled == true);   // 样本口径：字段经文本往返仍在

        CycleReviewSection section = Assert.IsType<CycleReviewSection>(CycleAnalyzer.Review([a, b, c]));
        Assert.Equal(2, section.Logs);   // C 被排除
        Assert.Equal([3, 0], section.Entries.Select(e => e.Stalls));
        Assert.Equal(3, section.Stalls);
        Assert.Equal(3, CycleAnalyzer.ReviewMatch(MatchLog.Parse(a.FullText())).Stalls);   // 读文件口径与内存口径相同

        string report = ReportWriter.Render(BalanceAnalyzer.Analyze([MatchLog.Parse(a.FullText()), MatchLog.Parse(b.FullText())], new AnalysisOptions { ReviewCycles = true }));
        Assert.Contains("- 停滞停手 3 次（按 D:stall 计；整局计，不限窗口），涉及 1 局", report, StringComparison.Ordinal);
        Assert.Contains(report.Split('\n'), l => l.Contains("  - 0000000000001111 ", StringComparison.Ordinal) && l.Contains("停滞停手 3 次（按 D:stall 计）", StringComparison.Ordinal));
        Assert.Contains(report.Split('\n'), l => l.Contains("  - 0000000000002222 ", StringComparison.Ordinal) && l.Contains("停滞停手 0 次（按 D:stall 计）", StringComparison.Ordinal));
        Assert.DoesNotContain("停滞停手", ReportWriter.Render(BalanceAnalyzer.Analyze([a, b])), StringComparison.Ordinal);   // 缺省报告不出现

        // 真实跑局交叉核对：link:1 简单 × 4、停滞大回合数显式取 1（只为在短局里触发停手，缺省 K 不变），
        // 跑局层快照的 Stalled 数 = 四名 AI 决策条目里 D:stall 的个数 = 诊断给出的次数；且确有停手（样本口径下界）。
        // 样本下界依赖 AI 实际走法：四名 AI 的评价权重写死为定值前缺省（testing.md「依赖 AI 实际怎么走的断言要把权重写死」），不跟随 EvaluationWeights.Default。
        // 实测（stagnation-trace 1.2）：种子 1、80 小回合内 D:stall 47 次。
        // map-expand-25 段 A2：link:1:4 换成浮空岛后这局 D:stall 0 次（单条十余分钟），载体改为手写平坦图（见 LinkSimFixtures.FlatStallSession）；
        // 平坦图上种子 1–12 × 80 小回合的 D:stall 次数探针：只有种子 2 / 6 / 12 非 0（1 / 2 / 1 次），取种子 6。
        MatchSession session = LinkSimFixtures.FlatStallSession(StallRunTurns, StallRunSeed);
        MatchLog real = session.Run();
        int entries = session.Match.Players.Sum(p => session.AiOf(p)!.Decisions.Count(d => d == HeuristicTurnController.StallDecision));
        Assert.True(entries > 0, $"样本口径：这局里应有停滞停手（D:stall 条目），实测 {entries}");
        Assert.Equal(entries, real.Turns.Count(t => t.Stalled == true));
        Assert.All(real.Turns.Where(t => t.Stalled == true), t => Assert.True(t.Passed, $"第 {t.Turn} 个小回合记了停手却有落子"));
        Assert.Equal(entries, CycleAnalyzer.ReviewMatch(MatchLog.Parse(real.FullText())).Stalls);
    }

    private const ulong StallRunSeed = 6;

    private const int StallRunTurns = 80;

    [Fact]
    public void 诊断不改对局()
    {
        // 规格 Scenario：对同一种子跑局并在分析时开启诊断 → 对局日志与决策序列基线逐字节不变。
        // 诊断只读日志对象（不回放、不消费随机）：开启前后日志文本逐字节相同；缺省报告没有这一段、开启后的报告以缺省报告为前缀。
        // 决策序列基线由 决策序列基线Tests 对 5 份 decisions-link1-*.txt 守住（本改动不触碰 Core / AI，基线逐字节不变）。
        MatchLog log = LinkSimFixtures.Round4Log.Value;
        string before = log.FullText();
        Assert.True(before.Split('\n').Length > 20, "样本口径：真实链接局的日志行数");

        string off = ReportWriter.Render(BalanceAnalyzer.Analyze([log]));
        string on = ReportWriter.Render(BalanceAnalyzer.Analyze([log], new AnalysisOptions { ReviewCycles = true }));
        string offAgain = ReportWriter.Render(BalanceAnalyzer.Analyze([log]));

        Assert.Equal(before, log.FullText());
        Assert.DoesNotContain("循环提子诊断", off, StringComparison.Ordinal);
        Assert.Contains("## 循环提子诊断", on, StringComparison.Ordinal);
        Assert.StartsWith(off, on, StringComparison.Ordinal);
        Assert.Equal(off, offAgain);

        // 同一日志经文本往返再诊断，结论逐字段相同（诊断是日志的纯函数）。
        CycleReviewEntry a = CycleAnalyzer.ReviewMatch(log);
        CycleReviewEntry b = CycleAnalyzer.ReviewMatch(MatchLog.Parse(before));
        Assert.Equal(
            (a.Seed, a.Truncated, a.EndMajorRound, a.WindowFirstRound, a.WindowTurns, a.LocalCells, a.Period, a.Repeats, a.OutsidePlacements, a.OutsideChanges, a.LocalEdits, a.MoveTurns, a.CaptureTurns, a.TotalPlacements, a.RecapturePlacements, a.Cyclic),
            (b.Seed, b.Truncated, b.EndMajorRound, b.WindowFirstRound, b.WindowTurns, b.LocalCells, b.Period, b.Repeats, b.OutsidePlacements, b.OutsideChanges, b.LocalEdits, b.MoveTurns, b.CaptureTurns, b.TotalPlacements, b.RecapturePlacements, b.Cyclic));
        Assert.Equal(a.CapturedCells, b.CapturedCells);
        Assert.Equal(a.Players.Select(p => p.ToString()), b.Players.Select(p => p.ToString()));
    }
}
