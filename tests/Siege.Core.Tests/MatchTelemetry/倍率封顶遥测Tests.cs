using System.Text.Json;
using System.Text.RegularExpressions;
using Siege.Core.Scoring;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>cap-multiplier（implement.md 2.3）：Sim 快照棋串明细与峰值记录的"生效倍率指数"字段、旧日志回填、报告"高倍率棋串"双列。</summary>
public class 倍率封顶遥测Tests
{
    [Fact]
    public void 快照与峰值记录同时保留原始数量与生效指数()
    {
        // 序列化 → 解析往返后逐字段比对（testing.md：含集合字段的 record 不能直接 Assert.Equal）。数量 8 / 生效 5 两个字段都要落盘、都要读回。
        // 第二条棋串刻意写入与回填值不同的生效指数 4：若字段只靠回填而没有真正写入/读回，8 → 回填 5 会让第一条恒真，只有第二条能红。
        // 变异验证 M-M6：GroupEntry.EffectiveMultiplierCount 的 init 丢弃传入值（= null）→ 红 1（本测试）。
        GroupEntry[] groups =
        [
            new() { Stones = ["A1", "A2"], Base = 8, MultiplierCount = 8, EffectiveMultiplierCount = 5, Power = 60 },
            new() { Stones = ["C1"], Base = 1, MultiplierCount = 8, EffectiveMultiplierCount = 4, Power = 5 },
        ];
        PeakEntry peak = new() { MultiplierCount = 8, EffectiveMultiplierCount = 5, MajorRound = 1, Player = 0, Power = 60, Stones = ["A1", "A2"] };
        MatchLog log = SimFixtures.Synthetic(
            1,
            [SimFixtures.Turn(1, 1, 0, [65, 0, 0, 0], ["A2:Multiplier"], groupsOfPlayer: groups)],
            [],
            SimFixtures.ResultOf(1, [0], peak: peak));

        string text = log.DeterministicText();
        Assert.Contains("\"MultiplierCount\":8,\"EffectiveMultiplierCount\":5", text);
        Assert.Contains("\"MultiplierCount\":8,\"EffectiveMultiplierCount\":4", text);

        MatchLog restored = MatchLog.Parse(text);
        List<GroupEntry> restoredGroups = restored.Turns[0].PlayersState[0].Groups;
        Assert.Equal(2, restoredGroups.Count);
        Assert.Equal((8, 5, 60L), (restoredGroups[0].MultiplierCount, restoredGroups[0].EffectiveMultiplierCount, restoredGroups[0].Power));
        Assert.Equal((8, 4, 5L), (restoredGroups[1].MultiplierCount, restoredGroups[1].EffectiveMultiplierCount, restoredGroups[1].Power));
        Assert.Equal((8, 5), (restored.Result!.Peak!.MultiplierCount, restored.Result.Peak.EffectiveMultiplierCount));
    }

    [Fact]
    public void 旧日志缺生效指数按封顶回填()
    {
        // cap-multiplier 之前的日志行没有 EffectiveMultiplierCount：解析时按 min(MultiplierCount, 5) 回填，旧日志仍可读、可分析。内嵌旧格式 JSON，不依赖 sim-out/。
        // 变异验证 M-M5：GroupEntry/PeakEntry 的回填改为 `?? MultiplierCount`（不封顶）→ 红 2（本测试 8 → 8、「高倍率棋串报告双列输出」）。
        const string oldGroupLine = "{\"Stones\":[\"A1\",\"A2\"],\"Base\":8,\"LineBonus\":0,\"SynergyBonus\":0,\"MultiplierCount\":8,\"Power\":60}";
        const string oldSmallGroupLine = "{\"Stones\":[\"C1\"],\"Base\":3,\"LineBonus\":0,\"SynergyBonus\":0,\"MultiplierCount\":3,\"Power\":10}";
        const string oldPeakLine = "{\"MultiplierCount\":7,\"MajorRound\":4,\"Player\":2,\"Power\":91,\"Stones\":[\"E5\",\"E6\"]}";

        GroupEntry big = JsonSerializer.Deserialize<GroupEntry>(oldGroupLine, LogJson.Options)!;
        GroupEntry small = JsonSerializer.Deserialize<GroupEntry>(oldSmallGroupLine, LogJson.Options)!;
        PeakEntry peak = JsonSerializer.Deserialize<PeakEntry>(oldPeakLine, LogJson.Options)!;

        Assert.Equal((8, 5), (big.MultiplierCount, big.EffectiveMultiplierCount));
        Assert.Equal((3, 3), (small.MultiplierCount, small.EffectiveMultiplierCount));
        Assert.Equal((7, 5), (peak.MultiplierCount, peak.EffectiveMultiplierCount));

        // 整份旧格式日志：把新格式序列化后剥掉该字段，走 MatchLog.Parse 与分析器，回填值进入生效指数分布。
        MatchLog log = SimFixtures.Synthetic(
            2,
            [SimFixtures.Turn(1, 1, 0, [60, 0, 0, 0], ["A2:Multiplier"], groupsOfPlayer: [big])],
            [],
            SimFixtures.ResultOf(1, [0], peak: peak));
        string oldText = Regex.Replace(log.DeterministicText(), "\"EffectiveMultiplierCount\":\\d+,", string.Empty);
        Assert.DoesNotContain("EffectiveMultiplierCount", oldText);

        MatchLog restored = MatchLog.Parse(oldText);
        Assert.Equal((8, 5), (restored.Turns[0].PlayersState[0].Groups[0].MultiplierCount, restored.Turns[0].PlayersState[0].Groups[0].EffectiveMultiplierCount));
        MultiplierSection m = BalanceAnalyzer.Analyze([restored]).Multiplier;
        Assert.Equal(new KeyValuePair<int, int>(7, 1), Assert.Single(m.PeakCountDistribution));
        Assert.Equal(new KeyValuePair<int, int>(5, 1), Assert.Single(m.PeakEffectiveExponentDistribution));
    }

    [Fact]
    public void 高倍率棋串报告双列输出()
    {
        // 峰值 8 枚与 3 枚两局 → 原始数量分布 3×1，8×1；生效指数分布 3×1，5×1。两列都要出现在报告"高倍率棋串"段落。
        // 变异验证 M-M7：BalanceAnalyzer.Multiplier 的 effective 改按 peak.MultiplierCount 计数 → 红 2（本测试与「旧日志缺生效指数按封顶回填」）。
        MatchLog eight = SimFixtures.Synthetic(
            3,
            [SimFixtures.Turn(1, 1, 0, [60, 0, 0, 0], ["A2:Multiplier"])],
            [],
            SimFixtures.ResultOf(1, [0], peak: new PeakEntry { MultiplierCount = 8, MajorRound = 1, Player = 0, Power = 60, Stones = ["A1"] }));
        MatchLog three = SimFixtures.Synthetic(
            4,
            [SimFixtures.Turn(1, 1, 1, [0, 10, 0, 0], ["H1:Multiplier"])],
            [],
            SimFixtures.ResultOf(1, [1], peak: new PeakEntry { MultiplierCount = 3, MajorRound = 1, Player = 1, Power = 10, Stones = ["H1"] }));

        BalanceReport report = BalanceAnalyzer.Analyze([eight, three]);
        MultiplierSection m = report.Multiplier;

        Assert.Equal(2, m.MatchesWithPeak);
        Assert.Equal([3, 8], m.PeakCountDistribution.Keys);
        Assert.Equal([3, 5], m.PeakEffectiveExponentDistribution.Keys);
        Assert.Equal(60, m.MaxPeakPower);
        Assert.Equal(5, Multiplier.MaxExponent);
        Assert.Contains("峰值倍增子数分布（原始数量）3×1，8×1；峰值生效倍率指数分布（封顶 5）3×1，5×1", ReportWriter.Render(report));
    }
}
