using System.Text.Json.Nodes;
using Siege.Core.Board;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/match-setup —— Requirement: 对局配置公开完整地图标识（存档恢复地图）、原型插旗替代路径（锁定结果可持久化）；
/// 实现裁决 B5：存档带模式、战场结算；retire-legacy-rules 第 3 组：无模式字段的存档一律拒绝（「链接图上缺模式字段的存档被拒」）。
/// open-map 段 B（裁决 B2 / B3）：原 <c>链接模式存档Tests</c> 里只测配对 / 链接重放的 4 条随代码删除，与配对无关的 6 条搬到本类；
/// 新增「含拼接字段的旧存档被拒」（match-setup「决赛倒计时配置」REMOVED 的 Migration：含该字段的旧存档按已移除规则拒绝，不回填）。
/// </summary>
/// <remarks>
/// 持久化守门两条腿（testing.md）：逐字段比对活对象与恢复对象 + 再存档逐字节相等 + 恢复后续跑同样决策再比。
/// 变异（历史，段 B 后半实跑）：M-B19 <c>ExportLink</c> 不写 <c>PassedThisRound</c> → 「小回合边界存档续跑一致」红 1；
/// M-C5 恢复路径去掉模式 ↔ 地图 / 人数校验 → 「链接图上缺模式字段的存档被拒」红 1。
/// </remarks>
public class 对局存档Tests
{
    [Fact]
    public void 小回合边界存档续跑一致()
    {
        // 恢复后续跑同样的决策：两局走到终局，结果、名次与再存档逐字节相同（随机子流位置、本大回合 Pass 记录都用上了）。
        MatchFlow match = Started([1, 1, 3, 4]).PlayRounds(3, P0, P2);
        match.PassTurn();    // 第 4 大回合中途：已有人 Pass
        Assert.NotEmpty(match.PassedThisRound);

        MatchFlow restored = RoundTrip(match);
        AssertSameState(match, restored);

        foreach (MatchFlow m in new[] { match, restored })
        {
            for (int i = 0; i < 40 && m.Phase == MatchPhase.InProgress; i++)
            {
                m.PassRound();
            }
        }

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(match.Serialize(), restored.Serialize());
        Assert.Equal(match.Result!.Reason, restored.Result!.Reason);
        Assert.Equal(match.Result.Standings.Select(s => (s.Rank, s.Player, s.Group)), restored.Result.Standings.Select(s => (s.Rank, s.Player, s.Group)));
    }

    [Fact]
    public void 终局后存档()
    {
        MatchFlow match = Started([0, 0, 0, 2]).PlayRound(P0, P1).PassRounds(3);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        MatchFlow restored = RoundTrip(match);
        AssertSameState(match, restored);
        Assert.Equal(EndReason.LastBattlefieldSettled, restored.Result!.Reason);
        Assert.Equal(
            match.Result!.Standings.Select(s => (s.Rank, s.Player, s.Input.Power, s.Input.Reason)),
            restored.Result.Standings.Select(s => (s.Rank, s.Player, s.Input.Power, s.Input.Reason)));
    }

    [Fact]
    public void 锁定结果可持久化()
    {
        // match-setup Scenario：插旗完成后保存 → 每名玩家锁定的出生棋盘编号与锁定时各棋盘旗数被完整保存并可恢复。
        MatchFlow match = Started([2, 2, 0, 4]);
        MatchFlow restored = RoundTrip(match);
        Assert.Equal(match.PlayerStates.Select(s => s.BirthZone), restored.PlayerStates.Select(s => s.BirthZone));
        Assert.Equal(match.LockedFlagCounts, restored.LockedFlagCounts);
        // retire-legacy-rules 第 3 组：对局模式成员删除，原"恢复后为链接模式"改为断言恢复后再存档的模式字段仍为链接模式。
        Assert.Equal("Link", JsonNode.Parse(restored.Serialize())!["Mode"]!.GetValue<string>());
    }

    [Fact]
    public void 旧存档被拒绝()
    {
        // match-setup Scenario「旧存档被拒绝」（Q6 / 实现裁决 D7）：恢复一个地图标识不是 link: 的旧存档 → 玩家入口 MatchFlow.Restore 拒绝读取并提示属于已移除的地图模式；
        // 变异 M-D3（段 D-a 实跑）：Restore 去掉地图标识门 → 本测试红 1。
        // retire-legacy-rules 1.5：对局只能建在链接档图上，旧存档样本改为"把一份链接存档的地图标识改成非 link: 的 test-match-9x9"，交玩家入口 → 照样拒绝。
        MatchFlow link = Started([0, 1, 3, 4]);
        string linkJson = link.Serialize();
        JsonObject node = JsonNode.Parse(linkJson)!.AsObject();
        node["MapId"] = "test-match-9x9";
        string json = node.ToJsonString();
        Assert.Equal("test-match-9x9", MatchFlow.SavedMapId(json));

        FormatException ex = Assert.Throws<FormatException>(() => MatchFlow.Restore(Map, json));
        Assert.Contains("已移除的地图模式", ex.Message, StringComparison.Ordinal);
        Assert.Contains("test-match-9x9", ex.Message, StringComparison.Ordinal);
        Assert.Contains("link:<地图种子>", ex.Message, StringComparison.Ordinal);

        // 反面：链接模式存档照常经玩家入口恢复。
        Assert.Equal(linkJson, MatchFlow.Restore(Map, linkJson).Serialize());
    }

    [Fact]
    public void 链接图上缺模式字段的存档被拒()
    {
        // 去掉模式字段 = 按旧模式读入；链接图只能以链接模式恢复 → 拒绝（FormatException，与"地图不一致"同口径）。
        MatchFlow match = Started([0, 1, 3, 4]);
        JsonObject root = JsonNode.Parse(match.Serialize())!.AsObject();
        root.Remove("Mode");
        FormatException ex = Assert.Throws<FormatException>(() => MatchFlow.Restore(Map, root.ToJsonString()));
        Assert.Contains("链接模式", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 链接恢复须四人()
    {
        // 裁决 S1 → map-expand-25 D1（人数恰等于地图人数）在恢复路径同样成立（段 B 质检遗留，tasks 3.0）：把一份 4 人图的存档手改成 3 人 → 恢复被拒，
        // 报出的是人数（不是别处碰巧失败）；未改的原存档照常恢复（反面）。
        // 变异 MC-23：RestoreCore 的人数校验传恒为 4 的人数 → 本测试红（3 人存档被接受或在别处失败）。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(1);
        string json = match.Serialize();
        Assert.Equal(json, MatchFlow.Restore(Map, json).Serialize());

        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonArray players = root["Players"]!.AsArray();
        Assert.Equal(4, players.Count);   // 样本口径：手改前确是 4 人
        players.RemoveAt(3);
        FormatException ex = Assert.Throws<FormatException>(() => MatchFlow.Restore(Map, root.ToJsonString()));
        Assert.Contains("是 4 人局，实际 3 人", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pairings", "[]")]
    [InlineData("Links", "[]")]
    [InlineData("PendingPairing", "null")]
    [InlineData("FirstPairingStepDone", "true")]
    [InlineData("FinalLinked", "false")]
    [InlineData("FinalCountdownRounds", "2")]
    [InlineData("CountdownStart", "7")]
    [InlineData("Deadlines", "[]")]
    [InlineData("Countdowns", "[]")]
    public void 含拼接字段的旧存档被拒(string field, string value)
    {
        // open-map 裁决 B2（match-setup「决赛倒计时配置」REMOVED / battlefield「配对事件」「链接事件」REMOVED）：改动前的链接模式存档在 Link 段里带配对 / 链接 / K 字段，
        // 恢复时 MUST 明确拒绝并提示属于已移除的拼接规则——而且先于地图摘要比对（真旧存档的地图摘要同样对不上，要报的是"拼接规则已移除"而不是"地图不一致"）。
        // 样本：在一份新存档的 Link 段里手工加回该字段，并把地图摘要改坏。反面：不加字段、摘要正确的同一份照常恢复。
        // 变异：Parse 不扫描已删除字段 → 带 Pairings=[] 的存档会被静默恢复（或因摘要不符报"地图不一致"），本测试红。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(1);
        string json = match.Serialize();
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        root["Link"]![field] = JsonNode.Parse(value);
        root["MapDigest"] = "00";
        string tampered = root.ToJsonString();

        FormatException ex = Assert.Throws<FormatException>(() => MatchFlow.Restore(Map, tampered));
        Assert.StartsWith(MatchFlow.RetiredLinkFieldMessage, ex.Message, StringComparison.Ordinal);
        Assert.Contains("拼接规则已移除", ex.Message, StringComparison.Ordinal);
        Assert.Contains(field, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("地图不一致", ex.Message, StringComparison.Ordinal);

        // 嵌套在结算 / 玩家条目里的旧字段（Deadline / WaitingRounds）同样拒绝。
        JsonObject nested = JsonNode.Parse(json)!.AsObject();
        nested["Link"]!["Players"]![0]!["WaitingRounds"] = 1;
        Assert.Contains("WaitingRounds", Assert.Throws<FormatException>(() => MatchFlow.Restore(Map, nested.ToJsonString())).Message, StringComparison.Ordinal);

        // 反面：原存档照常恢复；新存档的 Link 段没有任何已删除字段。
        Assert.Equal(json, MatchFlow.Restore(Map, json).Serialize());
        JsonObject fresh = JsonNode.Parse(json)!["Link"]!.AsObject();
        Assert.All(MatchFlow.RetiredLinkFields, f => Assert.False(fresh.ContainsKey(f), f));
        Assert.DoesNotContain("\"Pairings\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"FinalCountdownRounds\"", json, StringComparison.Ordinal);
    }

    private static MatchFlow RoundTrip(MatchFlow match)
    {
        string json = match.Serialize();
        MatchFlow restored = MatchFlow.Restore(Map, json);
        Assert.Equal(json, restored.Serialize());
        return restored;
    }

    /// <summary>活对象与恢复对象逐字段比对（含结算流程状态）。</summary>
    private static void AssertSameState(MatchFlow a, MatchFlow b)
    {
        Assert.Equal(a.Phase, b.Phase);
        Assert.Equal(a.MajorRound, b.MajorRound);
        Assert.Equal(a.ActionOrder, b.ActionOrder);
        Assert.Equal(a.CurrentPlayer, b.CurrentPlayer);
        Assert.Equal(a.Board.Serialize(), b.Board.Serialize());
        Assert.Equal(MapFile.ToJson(a.Map), MapFile.ToJson(b.Map));
        Assert.Equal(a.LockedFlagCounts, b.LockedFlagCounts);
        Assert.Equal(a.PassedThisRound, b.PassedThisRound);
        Assert.Equal(a.SettledBattlefields, b.SettledBattlefields);
        Assert.Equal(a.Settlements.Select(Text), b.Settlements.Select(Text));
        Assert.Equal(a.PlayerStates.Select(Text), b.PlayerStates.Select(Text));
        Assert.Equal(a.Relics.PublicStates().Select(r => r.ToString()), b.Relics.PublicStates().Select(r => r.ToString()));
    }

    private static string Text(BattlefieldSettlementRecord s) =>
        $"{s.Sequence}|{s.MajorRound}|{s.Battlefield}|{s.Reason}|{s.Winner}|{s.EliminationOrder}|{string.Join(",", s.Entries)}";

    private static string Text(PlayerFlowState s) =>
        $"{s}|{s.EliminationReason}|{s.PowerAtDefeat}";
}
