using Siege.Core.Match;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.SimulationHarness.LinkSimFixtures;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/simulation-harness —— Requirement: 随机子流隔离（配对 MUST NOT 是对局内的随机子流）。tasks 3.1。
/// </summary>
public class 随机子流隔离_链接模式Tests
{
    [Fact]
    public void 配对不扰动子流()
    {
        // 规格 Scenario「配对不扰动子流」：同一种子下两局采用不同的配对 → 出生棋盘的信物分布逐格相同，第 1 大回合行动顺序相同，锁定结果相同；
        // 而且配对之前（前 11 个小回合）每个小回合的快照逐行相同——配对来源不消费任何对局子流。
        ulong seed = FourZoneSeed.Value;
        MatchLog a = TwoLinkLog.Value;
        string PairsOf(MatchLog log) => string.Join(";", log.EventsOf(LogEventType.PairingApplied).Select(e => string.Join(",", e.Pairing!.Pairs.Select(p => $"{p[0]}-{p[1]}"))));
        MatchLog? b = Enumerable.Range(1, 20)
            .Select(p => MatchSession.Create(Config(turnLimit: 13) with { PairingSeed = (ulong)p }, seed).Run())
            .FirstOrDefault(l => PairsOf(l) != PairsOf(a));
        Assert.NotNull(b);   // 样本口径：两局的配对确实不同

        Assert.Equal(a.Header.Zones, b.Header.Zones);
        Assert.Equal(a.Header.FirstOrder, b.Header.FirstOrder);
        Assert.Equal(a.Header.Relics.Select(r => r.ToString()), b.Header.Relics.Select(r => r.ToString()));
        // 第 12 个小回合（第 3 大回合的最后一手）里发生链接，其快照含新增的公共棋盘信物，随配对而变；在它之前逐行相同。
        Assert.Equal(SimFixtures.TurnTexts(a.Turns.Take(11)), SimFixtures.TurnTexts(b.Turns.Take(11)));
    }

    [Fact]
    public void 配对来源不是对局子流()
    {
        // 规格："配对 MUST NOT 是对局内的随机子流"、Q10"工具层随机源，不登记为 GameSeed 子流"：Sim 的配对来源源码里不出现 GameSeed、不派生任何命名子流。
        // 源码扫描配样本口径下界与反面命中（MatchSession 确实用 GameSeed）。
        // 变异 MC-13：SeededPairingSource 改为从 GameSeed 派生命名子流（new GameSeed(…).Stream("pairing-seed")）→ 本测试红。
        string root = Path.Combine(Determinism.随机子流隔离Tests.SourceRoot(), "src", "Siege.Sim", "Running");
        string sources = File.ReadAllText(Path.Combine(root, "PairingSources.cs"));
        Assert.True(sources.Length > 2_000, "样本口径：配对来源文件过小。");
        Assert.Contains("class SeededPairingSource", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("new GameSeed", sources, StringComparison.Ordinal);
        Assert.DoesNotContain(".Stream(", sources, StringComparison.Ordinal);
        Assert.Contains("new GameSeed", File.ReadAllText(Path.Combine(root, "MatchSession.cs")), StringComparison.Ordinal);
    }
}
