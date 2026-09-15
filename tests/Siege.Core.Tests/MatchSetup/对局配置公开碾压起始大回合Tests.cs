using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 对局配置公开碾压起始大回合（dominance-victory 裁决 8）；另含 implement 1.1 的配置固定与日志首部守门。</summary>
public class 对局配置公开碾压起始大回合Tests
{
    [Fact]
    public void 插旗阶段可见碾压配置()
    {
        // 规格：匿名插旗阶段公开视图中可读到本局的碾压起始大回合（如 7，或 0 表示关闭）。
        MatchFlow match = MatchFixtures.Create();
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);
        Assert.Equal(7, MatchOptions.DefaultDominanceStartRound);
        Assert.Equal(7, MatchOptions.Default.DominanceStartRound);
        Assert.Equal(7, MatchOptions.Immediate.DominanceStartRound);
        Assert.Equal(7, match.Publish().DominanceStartRound);

        MatchFlow off = MatchFixtures.Create(options: MatchFixtures.DominanceOff);
        Assert.Equal(MatchPhase.FlagPlanting, off.Phase);
        Assert.Equal(0, off.Publish().DominanceStartRound);
    }

    [Fact]
    public void 旧存档回填()
    {
        // 规格：恢复不含碾压起始大回合字段的旧存档 → 按标准局初值 7 回填，且可查知发生了回填。
        // 两条腿：非回填值 6 往返（逐字段 + 逐字节）；剥掉 dominance-victory 三个字段的旧格式恢复得 7 且 Backfilled。
        // 变异验证 M-DV12：旧存档回填改为 0 → 红 1（本测试）。M-DV3（去掉起始门槛）亦红。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOn with { DominanceStartRound = 6 })
            .AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.PlayTurn("B2");   // 第 5 大回合 < 起始 6：5 对 0 也不建立候选
        string json = match.Serialize();
        Assert.Contains("\"DominanceStartRound\": 6", json);

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, json);
        Assert.Equal(6, restored.DominanceStartRound);
        Assert.Equal(6, restored.Options.DominanceStartRound);
        Assert.Equal(6, restored.Publish().DominanceStartRound);
        Assert.False(restored.DominanceStartRoundBackfilled);
        Assert.Equal(json, restored.Serialize());

        Assert.Null(match.Dominance);   // 旧格式样本不含候选，三个字段都是单行值（null / []），可按行剥离
        string[] fields = ["\"DominanceStartRound\"", "\"DominanceCandidate\"", "\"DominancePending\""];
        string legacy = string.Join('\n', json.Split('\n').Where(l => !fields.Any(l.Contains)));
        Assert.DoesNotContain("Dominance", legacy);
        MatchFlow fromLegacy = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, legacy);
        Assert.Equal(7, fromLegacy.DominanceStartRound);
        Assert.Equal(7, fromLegacy.Publish().DominanceStartRound);
        Assert.True(fromLegacy.DominanceStartRoundBackfilled);
        Assert.Null(fromLegacy.Dominance);
        Assert.Equal(match.MajorRound, fromLegacy.MajorRound);
        Assert.Contains("\"DominanceStartRound\": 7", fromLegacy.Serialize());
        Assert.Equal(legacy, string.Join('\n', fromLegacy.Serialize().Split('\n').Where(l => !fields.Any(l.Contains))));
    }

    [Fact]
    public void 碾压起始大回合开局固定进行中不可改()
    {
        // implement 1.1 算例：插旗阶段可设（含 0），对局开始后修改抛规则异常；负数拒绝。
        MatchFlow match = MatchFixtures.Create();
        match.ConfigureDominanceStartRound(0);
        Assert.Equal(0, match.DominanceStartRound);
        match.ConfigureDominanceStartRound(6);
        Assert.Equal(6, match.Publish().DominanceStartRound);
        Assert.Throws<ArgumentOutOfRangeException>(() => match.ConfigureDominanceStartRound(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchFixtures.Create(options: MatchOptions.Immediate with { DominanceStartRound = -1 }));

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Throws<SiegeRuleException>(() => match.ConfigureDominanceStartRound(4));
        Assert.Equal(6, match.DominanceStartRound);
        Assert.Equal(6, match.Publish().DominanceStartRound);
    }

    [Fact]
    public void 碾压起始大回合进入对局记录()
    {
        // 规格：写入对局日志首部，取自对局本身；文本往返后仍可读。跑局配置与对局配置不一致时会话拒绝建立。
        MatchLog log = SimFixtures.Sample.Value[0];
        Assert.Equal(7, log.Header.DominanceStartRound);
        Assert.Contains("\"DominanceStartRound\":7", log.FullText().Split('\n')[0]);
        Assert.Equal(7, MatchLog.Parse(log.FullText()).Header.DominanceStartRound);

        MatchFlow match = MatchFixtures.Started(options: MatchOptions.Immediate with { MaxMajorRounds = 4, DominanceStartRound = 6 });
        Assert.Throws<SiegeRuleException>(() => Siege.Sim.Running.MatchSession.ForMatch(match, SimFixtures.Config()));
    }
}
