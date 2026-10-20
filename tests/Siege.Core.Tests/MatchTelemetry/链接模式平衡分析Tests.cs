using Siege.Core.Board;
using Siege.Core.Board.Maps;
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
    public void 旧日志被拒绝()
    {
        // 规格 Scenario「旧日志被拒绝」：analyze 命令遇到地图标识不是 link: 的日志 → 拒绝并提示属于已移除的地图模式，不写报告（与 replay 同口径，实现裁决 C3）；
        // 内部分析接口仍能读旧日志（Legacy 夹具的分析回归测试）。反面：链接模式日志照常出报告。
        // 变异 MC-32：Program.Analyze 去掉地图标识检查 → 旧日志照常出报告、退出码 0，本测试红。
        // retire-legacy-maps 1.8：旧日志原由 v5 真实跑一局得到；旧地图删除后 Sim 不再能产出非链接日志，改用合成日志并把首部地图标识写成 siege-4p-base-v5，断言不变。
        string legacyDir = SimFixtures.TempDir("analyze-legacy");
        MatchLog synthetic = SimFixtures.Synthetic(1, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(6, [0]));
        new MatchLog { Header = synthetic.Header with { MapId = "siege-4p-base-v5" }, Turns = synthetic.Turns, Events = synthetic.Events, Result = synthetic.Result }.WriteTo(legacyDir);
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
        Assert.Contains("## 链接模式（open-map", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(linkDir, "report.txt")));
    }
}
