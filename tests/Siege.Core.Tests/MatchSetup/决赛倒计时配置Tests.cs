using System.Text.Json.Nodes;
using Siege.Core.Match;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：match-setup「决赛倒计时配置」（final-countdown）：K 为整数 ≥ 0、缺省 2，始终公开，写入存档、对局日志首部与批次配置记录；负数建局报错；
/// 恢复不含该字段的存档按 K = 0 处理并留痕。另含 tasks 1.3 的存档往返（起点 R、各战场期限、倒计时记录、结算记录的期限）。
/// </summary>
/// <remarks>
/// 变异验证（final-countdown 1.3 实跑，见 implement.md）：M-FC4 存档不写 <c>Deadlines</c>（导出为空表）→ 红 1（「决赛倒计时随存档往返」）；
/// M-FC5 恢复缺字段时按缺省 2 而非 0 → 红 1（「旧存档按不启用读取」）。
/// </remarks>
public class 决赛倒计时配置Tests
{
    [Fact(Skip = "open-map 段 B 删除")]
    public void 缺省值()
    {
        // 规格 Scenario：不显式配置开一局 → K = 2，存档与日志首部都写明。
        MatchFlow match = MatchFlow.Create(Map, TestMaps.Seed, All);
        Assert.Equal(2, match.FinalCountdownRounds);
        Assert.Equal(MatchOptions.DefaultFinalCountdownRounds, MatchOptions.Default.FinalCountdownRounds);
        match.PlantSequentially(All.Select((p, i) => (p, i)));
        JsonNode saved = JsonNode.Parse(match.Serialize())!;
        Assert.Equal(2, saved["Link"]!["FinalCountdownRounds"]!.GetValue<int>());

        // 日志首部（批次配置记录同一份 RunConfig）：未配置即落成 2 写明；显式 K = 0 照写 0，不是缺字段。
        JsonNode header = JsonNode.Parse(MatchSession.Create(SimFixtures.Config(turnLimit: 1), 5).Run().DeterministicText().Split('\n')[0])!;
        Assert.Equal(2, header["Config"]!["FinalCountdownRounds"]!.GetValue<int>());
        MatchLog zero = MatchSession.Create(SimFixtures.Config(turnLimit: 1) with { FinalCountdownRounds = 0 }, 5).Run();
        Assert.Equal(0, JsonNode.Parse(zero.DeterministicText().Split('\n')[0])!["Config"]!["FinalCountdownRounds"]!.GetValue<int>());
        Assert.Equal(0, zero.FinalCountdownRounds);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 负数建局报错()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => MatchFlow.Create(Map, TestMaps.Seed, All, CountdownOptions(-1)));
        Assert.Contains("决赛倒计时", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => (SimFixtures.Config() with { FinalCountdownRounds = -1 }).Validated());   // 批次配置同口径
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 旧存档按不启用读取()
    {
        // 规格 Scenario：恢复一个不含该字段的链接模式存档 → K = 0，恢复后行为与保存前一致。
        // 旧存档 = 引入倒计时之前的链接模式存档：那时对局等同 K = 0。拿 K = 0 的局在第 7 大回合开始（已有等待者）存档、抹掉倒计时四项，
        // 恢复后 K = 0 并留痕；两边接着走同样的步子到第 10 大回合，存档逐字节相同、都没有到期结算。
        MatchFlow original = Started([0, 1, 3, 4], new ScriptedPairingSource(), options: CountdownOptions(0));
        original.PlayRounds(5, All).PlayRound(P0, P1);
        Assert.Single(original.Settlements);
        JsonObject root = JsonNode.Parse(original.Serialize())!.AsObject();
        JsonObject link = root["Link"]!.AsObject();
        foreach (string field in new[] { "FinalCountdownRounds", "CountdownStart", "Deadlines", "Countdowns" })
        {
            link.Remove(field);
        }

        MatchFlow restored = MatchFlow.Restore(Map, root.ToJsonString());
        restored.PairingSource = new ScriptedPairingSource();

        Assert.Equal(0, restored.FinalCountdownRounds);
        Assert.True(restored.FinalCountdownBackfilled);
        Assert.False(original.FinalCountdownBackfilled);
        original.PlayRounds(4, P0, P1);
        restored.PlayRounds(4, P0, P1);
        Assert.Single(restored.Settlements);
        Assert.Equal(original.Serialize(), restored.Serialize());

        // 同样抹掉字段的 K = 2 存档也按不启用读：已赋的期限随字段一起不在了，到期限大回合不结算。
        MatchFlow counting = Started([0, 1, 3, 4], new ScriptedPairingSource(), options: CountdownOptions(2));
        counting.PlayRounds(5, All).PlayRound(P0, P1);
        Assert.Equal(8, counting.DeadlineOf(0));
        JsonObject stripped = JsonNode.Parse(counting.Serialize())!.AsObject();
        foreach (string field in new[] { "FinalCountdownRounds", "CountdownStart", "Deadlines", "Countdowns" })
        {
            stripped["Link"]!.AsObject().Remove(field);
        }

        MatchFlow old = MatchFlow.Restore(Map, stripped.ToJsonString());
        old.PairingSource = new ScriptedPairingSource();
        Assert.Equal(0, old.FinalCountdownRounds);
        Assert.Null(old.DeadlineOf(0));
        old.PlayRounds(2, P0, P1);
        Assert.Single(old.Settlements);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 决赛倒计时随存档往返()
    {
        // tasks 1.3「存档往返」：K、起点 R、当前期限、倒计时记录、结算记录的期限都随存档往返（活对象与恢复对象逐字段比，取值非缺省），
        // 恢复后接着走同样的步子与原局逐字节相同（期限确实被用上：第 8 大回合两边都到期结算）。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource(), options: CountdownOptions(2));
        match.PlayRounds(5, All).PlayRound(P0, P1);
        Assert.Equal(7, match.MajorRound);

        MatchFlow restored = MatchFlow.Restore(Map, match.Serialize());
        restored.PairingSource = new ScriptedPairingSource();
        Assert.Equal(2, restored.FinalCountdownRounds);
        Assert.False(restored.FinalCountdownBackfilled);
        Assert.Equal(6, match.FinalCountdownStart);
        Assert.Equal(match.FinalCountdownStart, restored.FinalCountdownStart);
        Assert.Equal("0:8", string.Join(",", match.BattlefieldDeadlines.Select(kv => $"{kv.Key}:{kv.Value}")));
        Assert.Equal(string.Join(",", match.BattlefieldDeadlines.Select(kv => $"{kv.Key}:{kv.Value}")), string.Join(",", restored.BattlefieldDeadlines.Select(kv => $"{kv.Key}:{kv.Value}")));
        Assert.NotEmpty(match.FinalCountdowns);
        Assert.Equal(match.FinalCountdowns, restored.FinalCountdowns);
        Assert.Equal(match.Serialize(), restored.Serialize());

        match.PlayRounds(2, P0, P1);
        restored.PlayRounds(2, P0, P1);
        Assert.Equal(SettlementReason.Expired, match.Settlements[^1].Reason);
        Assert.Equal(match.Serialize(), restored.Serialize());

        // 到期结算之后再存一次：结算记录的期限（非空）同样往返。
        MatchFlow again = MatchFlow.Restore(Map, match.Serialize());
        Assert.Equal(8, match.Settlements[^1].Deadline);
        Assert.Equal(match.Settlements.Select(s => (s.Sequence, s.Reason, s.Deadline)), again.Settlements.Select(s => (s.Sequence, s.Reason, s.Deadline)));
    }
}
