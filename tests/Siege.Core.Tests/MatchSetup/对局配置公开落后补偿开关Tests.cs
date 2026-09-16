using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 对局配置公开落后补偿开关（catch-up-recruit 裁决 4）；另含 implement 1.1 的配置固定与日志首部守门。</summary>
public class 对局配置公开落后补偿开关Tests
{
    [Fact]
    public void 插旗阶段可见补偿开关()
    {
        // 规格：匿名插旗阶段公开视图中可读到本局是否开启落后者征募补偿；标准局默认开启。
        MatchFlow match = MatchFixtures.Create();
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);
        Assert.True(MatchOptions.DefaultCatchUpRecruit);
        Assert.True(MatchOptions.Default.CatchUpRecruit);
        Assert.True(MatchOptions.Immediate.CatchUpRecruit);
        Assert.True(match.Publish().CatchUpRecruit);

        MatchFlow off = MatchFixtures.Create(options: MatchFixtures.CatchUpOff);
        Assert.Equal(MatchPhase.FlagPlanting, off.Phase);
        Assert.False(off.Publish().CatchUpRecruit);
    }

    [Fact]
    public void 旧存档回填()
    {
        // 规格：恢复不含该开关字段的旧存档 → 按标准局默认值"开启"回填，且可查知发生了回填。
        // 两条腿：**非回填值**「关闭」往返（逐字段 + 逐字节）；剥掉该字段的旧格式恢复得"开启"且 Backfilled。
        // 变异验证 M-CU8：Serialize 不写 CatchUpRecruit → 红 1（本测试：往返得到回填的 true，且 json 不含该字段）。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.CatchUpOff with { DominanceStartRound = 0 })
            .AtRound(5, MatchFixtures.All);
        match.PlayTurn("B2");
        string json = match.Serialize();
        Assert.Contains("\"CatchUpRecruit\": false", json);

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, json);
        Assert.False(restored.CatchUpRecruit);
        Assert.False(restored.Options.CatchUpRecruit);
        Assert.False(restored.Publish().CatchUpRecruit);
        Assert.False(restored.CatchUpRecruitBackfilled);
        Assert.Equal(json, restored.Serialize());

        string legacy = string.Join('\n', json.Split('\n').Where(l => !l.Contains("\"CatchUpRecruit\"")));
        Assert.DoesNotContain("CatchUpRecruit", legacy);
        MatchFlow fromLegacy = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, legacy);
        Assert.True(fromLegacy.CatchUpRecruit);
        Assert.True(fromLegacy.Publish().CatchUpRecruit);
        Assert.True(fromLegacy.CatchUpRecruitBackfilled);
        Assert.Equal(match.MajorRound, fromLegacy.MajorRound);
        Assert.Contains("\"CatchUpRecruit\": true", fromLegacy.Serialize());
        Assert.Equal(legacy, string.Join('\n', fromLegacy.Serialize().Split('\n').Where(l => !l.Contains("\"CatchUpRecruit\""))));
    }

    [Fact]
    public void 补偿开关开局固定进行中不可改()
    {
        // implement 1.1 算例：插旗阶段可设，对局开始后修改抛规则异常。
        MatchFlow match = MatchFixtures.Create();
        match.ConfigureCatchUpRecruit(false);
        Assert.False(match.CatchUpRecruit);
        match.ConfigureCatchUpRecruit(true);
        Assert.True(match.Publish().CatchUpRecruit);

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Throws<SiegeRuleException>(() => match.ConfigureCatchUpRecruit(false));
        Assert.True(match.CatchUpRecruit);
        Assert.True(match.Publish().CatchUpRecruit);
    }

    [Fact]
    public void 补偿开关进入对局记录()
    {
        // 规格：写入对局日志首部，取自对局本身；文本往返后仍可读。跑局配置与对局配置不一致时会话拒绝建立。
        MatchLog log = SimFixtures.Sample.Value[0];
        Assert.True(log.Header.CatchUpRecruit);
        Assert.Contains("\"CatchUpRecruit\":true", log.FullText().Split('\n')[0]);
        Assert.True(MatchLog.Parse(log.FullText()).Header.CatchUpRecruit);

        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.CatchUpOff);
        Assert.Throws<SiegeRuleException>(() => Siege.Sim.Running.MatchSession.ForMatch(match, SimFixtures.Config()));
    }
}
