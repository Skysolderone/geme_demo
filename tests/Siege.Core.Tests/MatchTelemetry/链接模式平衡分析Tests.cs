using Siege.Core.Match;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/match-telemetry —— Requirement: 对局日志的记录内容（离线分析部分：同区率、首轮配对分布、各阶段大回合数、退化链接率、等待时长；
/// Scenario「配对与链接可查」「战场结算可查」「等待时长可统计」「旧日志被拒绝」）。tasks 3.5。
/// </summary>
/// <remarks>
/// 样本（不跑批量）：<c>link:1</c> 上两局脚本固定配对的小局——A 四人四区（首轮两对 + 决赛，三次链接）、B 2+1+1（同区两人在第 2 大回合结算、胜者等待 1 个大回合，
/// 首轮一对 + 决赛）；控制者第 1 大回合各落 1 子、此后一律 Pass（<see cref="LinkSimFixtures.PlayScripted"/>）。期望值按规则逐局推得，写在各断言旁。
/// 变异验证（段 C 后半实跑）见各测试注释。
/// </remarks>
[Collection(ConsoleRedirect.Collection)]
public class 链接模式平衡分析Tests
{
    private static readonly Lazy<(MatchLog A, MatchLog B)> Sample = new(() =>
        (MatchLog.Parse(LinkSimFixtures.PlayScripted([0, 1, 2, 3]).Log.FullText()), MatchLog.Parse(LinkSimFixtures.PlayScripted([0, 0, 1, 2]).Log.FullText())));

    private static LinkModeSection Section()
    {
        (MatchLog a, MatchLog b) = Sample.Value;
        BalanceReport report = BalanceAnalyzer.Analyze([a, b]);
        return report.LinkMode ?? throw new InvalidOperationException("链接模式样本没有出链接模式统计。");
    }

    [Fact]
    public void 配对与链接可查()
    {
        // 规格 Scenario「配对与链接可查」的分析侧：同区率、开局布局、首轮配对分布、链接次数与退化率、通道长度、决赛战场格数都能离线统计。
        // A：四人四区，首轮 (1,2)(3,4)，决赛 1 次 → 链接 3；B：棋盘 1 两人同区，首轮只有 2、3 两块单人棋盘 → (2,3)，决赛 1 次 → 链接 2。
        // 变异 MC-28：同区判定改为"旗数 ≥ 3" → 同区局数 0，本测试红。
        LinkModeSection s = Section();
        (MatchLog a, MatchLog b) = Sample.Value;
        Assert.Equal(2, s.Matches);
        Assert.Equal(1, s.SharedMatches);
        Assert.Equal(0.5, s.SharedShare);
        Assert.Equal(new SortedDictionary<string, int> { ["1+1+1+1"] = 1, ["2+1+1"] = 1 }, s.Layouts);
        Assert.Equal(new SortedDictionary<string, int> { ["1-2 3-4"] = 1, ["2-3"] = 1 }, s.FirstRoundPairings);
        Assert.Equal(5, s.Links);

        List<LinkLogEntry> links = [.. new[] { a, b }.SelectMany(l => l.Events).Where(e => e.Link is not null).Select(e => e.Link!)];
        Assert.Equal(links.Count(l => l.Degraded), s.DegradedLinks);
        Assert.Equal(links.Sum(l => l.CorridorLengths.Count), s.CorridorLengths.Values.Sum());
        Assert.All(links, l => Assert.Equal(l.Degraded ? 1 : 2, l.CorridorLengths.Count));   // 公共棋盘两条通道，退化一条直通道
        Assert.All(links.SelectMany(l => l.CorridorLengths), length => Assert.True(length >= 2));

        // 决赛战场格数：每局一个样本，取决赛链接那一条的合并战场格数，且大于首轮任何一个战场。
        Assert.Equal(2, s.FinalBattlefieldCells.Values.Sum());
        int[] expectedFinal = [.. new[] { a, b }.Select(FinalCells).Order()];
        Assert.Equal(expectedFinal, s.FinalBattlefieldCells.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)));
        Assert.All(new[] { a, b }, l => Assert.True(FinalCells(l) > l.Events.Where(e => e.Link is { PairingSequence: 1 }).Max(e => e.Link!.BattlefieldCells)));
        Assert.True(s.EndPlayableCells > 0);

        static int FinalCells(MatchLog log)
        {
            int final = log.Events.Single(e => e.Pairing is { Kind: nameof(PairingKind.Final) }).Pairing!.Sequence;
            return log.Events.Where(e => e.Link?.PairingSequence == final).Select(e => e.Link!.BattlefieldCells).Single();
        }
    }

    [Fact]
    public void 战场结算可查()
    {
        // 规格 Scenario「战场结算可查」的分析侧：各阶段大回合数、终局原因分布、出局原因计数、过早 Pass 落败。
        // 两局都是：构筑 3（第 1–3 大回合）、首轮交战 1（第 4 大回合）、决赛 1（第 5 大回合）；都以最后战场结算终局。
        // A 落败 2 + 1、B 落败 1 + 1 + 1 → 战场落败 6 人次，全在整轮 Pass 结算里，且 Pass 时手里都还有子 → 过早 Pass 落败 6。
        // 变异 MC-29：首轮交战大回合数不减构筑期（少减 3）→ 本测试红；MC-30：过早 Pass 不看手牌（恒判手里有子）→ 只有下面"清空手牌"的反面断言红。
        LinkModeSection s = Section();
        Assert.Equal(3, s.BuildRounds);
        Assert.Equal(1, s.FirstFightRounds);
        Assert.Equal(1, s.FinalRounds);
        Assert.Equal(2, s.FinalMatches);
        Assert.Equal(new SortedDictionary<string, int> { [nameof(EndReason.LastBattlefieldSettled)] = 2 }, s.EndReasons);
        Assert.Equal(0, s.Truncated);
        Assert.Equal(6, s.BattlefieldDefeats);
        Assert.Equal(0, s.PowerZeroEliminations);
        Assert.Equal(6, s.AllPassedDefeats);
        Assert.Equal(6, s.PrematurePassDefeats);

        // 反面：把 A 里某名落败者结算那一大回合的快照手牌清空（"手里已无子可下"）→ 仍算整轮 Pass 落败，但不再算过早 Pass。
        MatchLog a = SimFixtures.Clone(Sample.Value.A);
        LogEvent settled = a.Events.First(e => e.Settlement is not null);
        int loser = settled.Settlement!.Entries.First(x => x.Player != settled.Settlement.Winner).Player;
        int index = a.Turns.FindLastIndex(t => t.MajorRound == settled.Settlement.MajorRound && t.Player == loser);
        TurnSnapshot own = a.Turns[index];
        a.Turns[index] = own with { PlayersState = [.. own.PlayersState.Select(p => p.Player == loser ? p with { HandTypes = [] } : p)] };
        (int allPassed, int premature) = LinkModeAnalyzer.PassDefeats(a);
        (int allPassedBefore, int prematureBefore) = LinkModeAnalyzer.PassDefeats(Sample.Value.A);
        Assert.Equal(allPassedBefore, allPassed);
        Assert.Equal(prematureBefore - 1, premature);

        // 截断局：没到决赛的局不进决赛均值；终局原因记 turn_limit。
        MatchLog truncated = MatchSession.Create(LinkSimFixtures.Config(turnLimit: 6), 1).Run();
        LinkModeSection t = BalanceAnalyzer.Analyze([truncated]).LinkMode!;
        Assert.Equal(1, t.Truncated);
        Assert.Equal(0, t.FinalMatches);
        Assert.Equal(new SortedDictionary<string, int> { ["turn_limit"] = 1 }, t.EndReasons);
        Assert.Equal(2, t.BuildRounds);   // 6 个小回合 = 第 1–2 大回合
        Assert.Equal(0, t.FirstFightRounds);
    }

    [Fact]
    public void 等待时长可统计()
    {
        // 规格 Scenario「等待时长可统计」：分析一批日志，报告给出等待大回合数的均值与最大值。
        // B 里棋盘 1 的胜者从第 2 大回合起独处一战场，第 4 大回合结束时仍是单人战场 → 等待 1；其余 7 名玩家 0。
        // 变异 MC-31：等待时长改取第一个快照 → 全为 0，本测试红。
        LinkModeSection s = Section();
        Assert.Equal(1, s.WaitingMax);
        Assert.Equal(1, s.Waiters);
        Assert.Equal(1.0 / 8, s.WaitingMean, 6);
        Assert.Equal(1, s.WaitingMeanOfWaiters);

        string report = ReportWriter.Render(BalanceAnalyzer.Analyze([Sample.Value.A, Sample.Value.B]));
        Assert.Contains("## 链接模式（board-link-mode）", report, StringComparison.Ordinal);
        Assert.Contains("等待大回合数：全部玩家均值 0.13，等待过的 1 人次均值 1，最大 1", report, StringComparison.Ordinal);
        Assert.Contains("首轮配对分布（棋盘编号 1–5，\"等\" = 等待者）：1-2 3-4×1，2-3×1", report, StringComparison.Ordinal);
        Assert.Contains("为上界）", report, StringComparison.Ordinal);   // 过早 Pass 落败的口径是上界，报告文字写明（3.6 / check）

        // 旧日志的报告没有这一段（逐字节不变由 平衡分析方向Tests 的黄金哈希守住）。
        Assert.DoesNotContain("链接模式", ReportWriter.Render(BalanceAnalyzer.Analyze(SimFixtures.Sample.Value)), StringComparison.Ordinal);
        Assert.Null(BalanceAnalyzer.Analyze(SimFixtures.Sample.Value).LinkMode);
    }

    [Fact]
    public void 真实跑局的链接日志可完整分析()
    {
        // 真实 AI 局（四人四区、截断在两次链接之后）的日志经文本往返后，全部分析段（含既有各段）都能跑完、报告含链接模式一段。
        MatchLog log = SimFixtures.Clone(LinkSimFixtures.TwoLinkLog.Value);
        BalanceReport report = BalanceAnalyzer.Analyze([log]);
        Assert.Equal(2, report.LinkMode!.Links);
        Assert.Equal(1, report.LinkMode.Truncated);
        Assert.True(report.LinkMode.TurnMaxMs > 0);
        Assert.Contains("## 链接模式（board-link-mode）", ReportWriter.Render(report), StringComparison.Ordinal);

        // 链接新增的公共棋盘信物也进信物统计（3.6 冒烟时实测：首部之外的信物格一旦被控制，既有的信物选择段即 KeyNotFound）。
        // 变异 MC-33：信物选择段改回只读首部信物 → 本测试红。
        MatchLog controlled = SimFixtures.Clone(LinkSimFixtures.TwoLinkLog.Value);
        RelicEntry linked = controlled.Events.First(e => e.Link is not null).Link!.NewRelics.First();
        int index = controlled.Turns.Count - 1;
        TurnSnapshot lastTurn = controlled.Turns[index];
        Assert.Contains(lastTurn.Relics, r => r.Coord == linked.Coord);   // 样本口径：快照里列出了这枚链接新增的信物
        controlled.Turns[index] = lastTurn with { Relics = [.. lastTurn.Relics.Select(r => r.Coord == linked.Coord ? r with { Control = "Controlled", Holder = 0 } : r)] };
        BalanceReport withLinked = BalanceAnalyzer.Analyze([controlled]);
        Assert.Equal(controlled.AllRelics.Count(r => r.Type == linked.Type), withLinked.Selection.Relics.Single(r => r.Type == linked.Type).Count);
        Assert.True(controlled.AllRelics.Count() > controlled.Header.Relics.Count);

        // 回归：旧模式日志没有链接事件，完整信物分布逐项即首部信物（信物选择段对旧日志的结果因此不变）。
        Assert.All(SimFixtures.Sample.Value, l => Assert.Equal(l.Header.Relics, l.AllRelics));
    }

    [Fact]
    public void 旧日志被拒绝()
    {
        // 规格 Scenario「旧日志被拒绝」：analyze 命令遇到地图标识不是 link: 的日志 → 拒绝并提示属于已移除的地图模式，不写报告（与 replay 同口径，实现裁决 C3）；
        // 内部分析接口仍能读旧日志（Legacy 夹具的分析回归测试）。反面：链接模式日志照常出报告。
        // 变异 MC-32：Program.Analyze 去掉地图标识检查 → 旧日志照常出报告、退出码 0，本测试红。
        string legacyDir = SimFixtures.TempDir("analyze-legacy");
        MatchSession.Create(SimFixtures.Config(turnLimit: 4), 1).Run().WriteTo(legacyDir);
        (int code, _, string err) = LinkSimFixtures.RunMain(null, null, "analyze", "--dir", legacyDir);
        Assert.Equal(1, code);
        Assert.Contains("已移除的地图模式", err, StringComparison.Ordinal);
        Assert.Contains("siege-4p-base-v5", err, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(legacyDir, "report.txt")));
        Assert.NotNull(BalanceAnalyzer.Analyze(MatchLog.ReadDirectory(legacyDir)));

        string linkDir = SimFixtures.TempDir("analyze-link");
        Sample.Value.A.WriteTo(linkDir);
        Sample.Value.B.WriteTo(linkDir);
        (int okCode, string output, string okErr) = LinkSimFixtures.RunMain(null, null, "analyze", "--dir", linkDir);
        Assert.True(okCode == 0, okErr);
        Assert.Contains("## 链接模式（board-link-mode）", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(linkDir, "report.txt")));
    }
}
