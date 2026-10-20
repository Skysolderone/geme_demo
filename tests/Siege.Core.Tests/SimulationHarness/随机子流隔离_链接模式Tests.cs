using Siege.Core.Match;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.SimulationHarness.LinkSimFixtures;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 规格：openspec/changes/open-map/specs/simulation-harness —— Requirement: 随机子流隔离（MODIFIED：配对条款随拼接规则删除）。
/// </summary>
public class 随机子流隔离_链接模式Tests
{
    [Fact]
    public void 配对来源已随拼接规则删除()
    {
        // open-map（simulation-harness「随机子流隔离」MODIFIED：地图布局 MUST NOT 消费任何对局内子流；配对条款删除）：
        // Sim 不再有任何配对来源——Running/ 下没有 PairingSources.cs，整个 Sim 源码不出现 PairingSource；对局子流仍只经 GameSeed（反面命中）。
        string root = Path.Combine(Determinism.随机子流隔离Tests.SourceRoot(), "src", "Siege.Sim");
        Assert.False(File.Exists(Path.Combine(root, "Running", "PairingSources.cs")));
        string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
        Assert.True(files.Length >= 20, $"样本口径：只扫到 {files.Length} 个 Sim 源文件。");
        Assert.Empty(files.Where(f => File.ReadAllText(f).Contains("PairingSource", StringComparison.Ordinal)));
        // 配对种子的名字只许出现在"已删除"拒绝表里（CommandLine.RetiredOptions / RunConfig.RetiredKeys / MatchLog.RetiredConfigFields）。
        Assert.Equal(["CommandLine.cs", "MatchLog.cs", "RunConfig.cs"], files.Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), "PairingSeed|pairing-seed")).Select(Path.GetFileName).Order());
        Assert.Contains("new GameSeed", File.ReadAllText(Path.Combine(root, "Running", "MatchSession.cs")), StringComparison.Ordinal);
    }
}
