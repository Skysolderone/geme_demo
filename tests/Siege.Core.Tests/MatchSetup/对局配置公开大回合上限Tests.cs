using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 对局配置公开大回合上限（round-cap D3）；另含 implement 1.1 / 1.3 的配置固定与持久化守门。</summary>
public class 对局配置公开大回合上限Tests
{
    [Fact]
    public void 插旗阶段可见上限()
    {
        // 设计文档 §13.1：上限与地图标识、种子一样始终公开，插旗阶段即可读（15，或 0 表示不设上限）。
        // 变异验证 M-R7：Publish 把 MaxMajorRounds 写死为 0 → 红 5（本测试、上限开局固定进行中不可改、上限进入存档且旧存档回填15、第15大回合结束仍未终局、上限随存档往返）。
        MatchFlow match = MatchFixtures.Create();
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);
        Assert.Equal(15, MatchOptions.DefaultMaxMajorRounds);
        Assert.Equal(15, MatchOptions.Default.MaxMajorRounds);
        Assert.Equal(15, match.Publish().MaxMajorRounds);

        MatchFlow unlimited = MatchFixtures.Create(options: MatchOptions.Immediate with { MaxMajorRounds = 0 });
        Assert.Equal(MatchPhase.FlagPlanting, unlimited.Phase);
        Assert.Equal(0, unlimited.Publish().MaxMajorRounds);
    }

    [Fact]
    public void 上限进入对局记录()
    {
        // 任意一局日志首部记录了该局的大回合上限，与地图标识、种子并列；文本往返后仍可读。
        // 变异验证 M-R8：BuildHeader 不写 MaxMajorRounds → 红 2（本测试、上限写入对局配置并以规则原因终局）。
        MatchLog log = SimFixtures.Sample.Value[0];
        Assert.Equal(4, log.Header.MaxMajorRounds);   // SimFixtures.Sample 用 maxRounds: 4

        string header = log.FullText().Split('\n')[0];
        Assert.Contains("\"Kind\":\"header\"", header);
        Assert.Contains("\"MapId\":", header);
        Assert.Contains("\"Seed\":", header);
        Assert.Contains("\"MaxMajorRounds\":4", header);
        Assert.Equal(4, MatchLog.Parse(log.FullText()).Header.MaxMajorRounds);
    }

    [Fact]
    public void 上限开局固定进行中不可改()
    {
        // implement 1.1：非负整数、初值 15；插旗阶段可设（含 0），对局开始后修改抛规则异常。
        // 变异验证 M-R9：ConfigureMaxMajorRounds 的阶段检查改为 `if (false)` → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Create();
        match.ConfigureMaxMajorRounds(0);
        Assert.Equal(0, match.MaxMajorRounds);
        match.ConfigureMaxMajorRounds(20);
        Assert.Equal(20, match.Publish().MaxMajorRounds);
        Assert.Throws<ArgumentOutOfRangeException>(() => match.ConfigureMaxMajorRounds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchFixtures.Create(options: MatchOptions.Immediate with { MaxMajorRounds = -1 }));

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Throws<SiegeRuleException>(() => match.ConfigureMaxMajorRounds(15));
        Assert.Equal(20, match.MaxMajorRounds);
        Assert.Equal(20, match.Publish().MaxMajorRounds);
    }

    [Fact]
    public void 上限进入存档且旧存档回填15()
    {
        // implement 1.3 两条腿：用与回填值不同的上限 12（testing.md「回填字段用非回填值证伪」）在第 7 大回合存档——逐字段 + 逐字节；
        // 旧格式存档（无该字段）恢复得 15 且可查知回填。
        // 变异验证 M-R6：Serialize 不写 MaxMajorRounds → 红 2（本测试：恢复得 15 ≠ 12；上限随存档往返）。
        // 变异验证 M-R10：旧存档回填改为 0 → 红 1（本测试）。
        // 变异验证 M-RC2（check）：RestoreCore 忽略存档里的上限、一律用默认 15 → 红 1（本测试：恢复得 15 ≠ 12）。
        MatchFlow match = MatchFixtures.Started(options: MatchOptions.Immediate with { MaxMajorRounds = 12 }).AtRound(7, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.PlayTurn("B2");
        string json = match.Serialize();
        Assert.Contains("\"MaxMajorRounds\": 12", json);

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, json);
        Assert.Equal(12, restored.MaxMajorRounds);
        Assert.Equal(12, restored.Options.MaxMajorRounds);
        Assert.Equal(12, restored.Publish().MaxMajorRounds);
        Assert.False(restored.MaxMajorRoundsBackfilled);
        Assert.Equal(match.MajorRound, restored.MajorRound);
        Assert.Equal(match.CurrentPlayer, restored.CurrentPlayer);
        Assert.Equal(json, restored.Serialize());

        // 旧格式：round-cap 之前的存档没有 MaxMajorRounds 字段。样本由当前存档剥掉该行得到（不依赖 sim-out/）。
        string legacy = string.Join('\n', json.Split('\n').Where(l => !l.Contains("\"MaxMajorRounds\"")));
        Assert.DoesNotContain("MaxMajorRounds", legacy);
        MatchFlow fromLegacy = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, legacy);
        Assert.Equal(15, fromLegacy.MaxMajorRounds);
        Assert.Equal(15, fromLegacy.Publish().MaxMajorRounds);
        Assert.True(fromLegacy.MaxMajorRoundsBackfilled);
        Assert.Equal(match.MajorRound, fromLegacy.MajorRound);
        Assert.Contains("\"MaxMajorRounds\": 15", fromLegacy.Serialize());   // 再存档即带上回填后的值

        // 内嵌的旧格式片段：带 MaxMajorRounds 的现行存档与不带的旧存档在其它字段上逐字节相同
        Assert.Equal(legacy, string.Join('\n', fromLegacy.Serialize().Split('\n').Where(l => !l.Contains("\"MaxMajorRounds\""))));
    }
}
