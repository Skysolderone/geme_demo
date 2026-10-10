using System.Text.Json.Nodes;
using Siege.Core.Board;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/match-setup —— Requirement: 对局配置公开完整地图标识（存档恢复地图）、原型插旗替代路径（锁定结果可持久化）；
/// battlefield —— 链接事件 / 配对事件"写入存档、恢复时按记录重建"。实现裁决 B5：存档带模式、链接与配对、战场结算；Link 恢复先重放链接再重放改造；
/// 无模式字段的旧存档按旧模式读入（match-setup「旧存档被拒绝」在段 C 删旧入口之前不生效，见实施报告）。
/// </summary>
/// <remarks>
/// 持久化守门两条腿（testing.md）：逐字段比对活对象与恢复对象 + 再存档逐字节相等 + 恢复后续跑同样决策再比。
/// 变异验证（段 B 后半实跑）：M-B18 <c>RestoreLink</c> 先写盘面再重放链接 → 「存档恢复地图」「小回合边界存档续跑一致」「终局后存档」红 3；
/// M-B19 <c>ExportLink</c> 不写 <c>PassedThisRound</c> → 「小回合边界存档续跑一致」红 1；M-B21 旧模式存档也写 <c>Mode</c> → 「旧模式存档不写新字段且按旧模式读入」红 1。
/// 段 B check 实跑：M-C4 <c>RestoreLink</c> 不比对地图摘要 → 「链接重放与存档不符即拒绝」红 1；M-C5 恢复路径去掉模式 ↔ 地图 / 人数校验 → 「链接图上缺模式字段的存档被拒」红 1；
/// M-C7 <c>StandingSaveData.Reason</c> 去掉 <c>WhenWritingNull</c> → 「旧模式终局存档不写出局原因」红 1（补这条之前 0 红）。
/// </remarks>
public class 链接模式存档Tests
{
    [Fact]
    public void 存档恢复地图()
    {
        // Scenario：在已执行两次链接的对局上保存后恢复 → 恢复出的地图与保存前逐格相同，地图标识不变（含链接前后的棋子、栅栏与已揭示信物）。
        MatchFlow match = Started([0, 1, 3, 4]);
        Coord fenceA = BirthCell(match.Map, 3, 0, 0);
        match.Board.ApplyTerrainEdits([TerrainEdit.Fence(fenceA, new Coord(fenceA.X + 1, fenceA.Y))]);
        match.PlayRounds(3, P0, P2);
        match.ApplyPairing([(0, 3), (1, 4)]);
        BoardPlate plate = match.Map.Boards.First(b => b.Kind == BoardPlateKind.Public);
        match.Place(P1, plate.Origin);
        match.PlayRound(P0, P2);

        MatchFlow restored = RoundTrip(match);

        Assert.Equal(MapFile.ToJson(match.Map), MapFile.ToJson(restored.Map));
        Assert.Equal(MapFile.ToJson(match.Board.LinkedMap), MapFile.ToJson(restored.Board.LinkedMap));
        Assert.Same(Map, restored.Board.BaseMap);
        Assert.Equal(match.Map.Id, restored.Map.Id);
        AssertSameState(match, restored);
    }

    [Fact]
    public void 小回合边界存档续跑一致()
    {
        // 恢复后续跑同样的决策：两局走到终局，结果、名次与再存档逐字节相同（随机子流位置、本大回合 Pass 记录、阶段字段都用上了）。
        MatchFlow match = Started([1, 1, 3, 4], new ScriptedPairingSource()).PlayRounds(3, P0, P2);
        match.PassTurn();    // 第 4 大回合中途：已有人 Pass
        Assert.NotEmpty(match.PassedThisRound);

        MatchFlow restored = RoundTrip(match);
        restored.PairingSource = new ScriptedPairingSource();
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
    public void 待配对时存档()
    {
        // 停在待首轮配对时存档 → 恢复后待处理请求相同，提交同一配对得到逐格相同的地图。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        Assert.Equal(MatchPhase.AwaitingPairing, match.Phase);
        MatchFlow restored = RoundTrip(match);

        Assert.Equal(MatchPhase.AwaitingPairing, restored.Phase);
        Assert.Equal(match.PendingPairing!.Candidates, restored.PendingPairing!.Candidates);
        Assert.Equal(match.PendingPairing.Kind, restored.PendingPairing.Kind);
        match.ApplyPairing([(0, 4), (1, 3)]);
        restored.ApplyPairing([(0, 4), (1, 3)]);
        Assert.Equal(MapFile.ToJson(match.Map), MapFile.ToJson(restored.Map));
        Assert.Equal(match.Serialize(), restored.Serialize());
    }

    [Fact]
    public void 终局后存档()
    {
        MatchFlow match = Started([0, 0, 0, 2], new ScriptedPairingSource()).PlayRound(P0, P1).PassRounds(3);
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
        Assert.Equal(MatchMode.Link, restored.Mode);
    }

    [Fact]
    public void 旧模式存档不写新字段且按旧模式读入()
    {
        MatchFlow legacy = MatchFixtures.Started();
        string json = legacy.Serialize();
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        Assert.False(root.ContainsKey("Mode"));
        Assert.False(root.ContainsKey("Link"));
        Assert.Equal(MatchMode.Legacy, MatchFlow.RestoreUnvalidated(legacy.Map, legacy.Relics.Generation, json).Mode);
    }

    [Fact]
    public void 旧模式终局存档不写出局原因()
    {
        // 实现裁决 B1 / B5：旧模式下终局名次的每一项也不写出局原因（Reason 只在链接模式写出），存档字段集与改动前相同。
        // 局面：P1 势力归零出局、P2 / P3 弃赛 → 终局结果里既有出局者又有弃赛者。
        MatchFlow legacy = MatchFixtures.Started().AtRound(5, MatchFixtures.All)
            .Stones(MatchFixtures.P1, "A1")
            .Stones(MatchFixtures.P0, "B1");
        legacy.PlayTurn("A2");
        legacy.Resign(MatchFixtures.P2);
        legacy.Resign(MatchFixtures.P3);
        Assert.Equal(MatchPhase.Ended, legacy.Phase);

        JsonObject root = JsonNode.Parse(legacy.Serialize())!.AsObject();
        Assert.False(root.ContainsKey("Mode"));
        Assert.False(root.ContainsKey("Link"));
        JsonArray standings = root["Result"]!["Standings"]!.AsArray();
        Assert.Equal(4, standings.Count);
        Assert.All(standings, s => Assert.False(s!.AsObject().ContainsKey("Reason")));
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
    public void 链接重放与存档不符即拒绝()
    {
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        match.ApplyPairing([(0, 3), (1, 4)]);
        JsonObject root = JsonNode.Parse(match.Serialize())!.AsObject();
        root["Link"]!["Links"]![1]!["MapDigest"] = "00";
        Assert.Throws<FormatException>(() => MatchFlow.Restore(Map, root.ToJsonString()));
    }

    private static MatchFlow RoundTrip(MatchFlow match)
    {
        string json = match.Serialize();
        MatchFlow restored = MatchFlow.Restore(Map, json);
        Assert.Equal(json, restored.Serialize());
        return restored;
    }

    /// <summary>活对象与恢复对象逐字段比对（含链接流程状态）。</summary>
    private static void AssertSameState(MatchFlow a, MatchFlow b)
    {
        Assert.Equal(a.Mode, b.Mode);
        Assert.Equal(a.Phase, b.Phase);
        Assert.Equal(a.MajorRound, b.MajorRound);
        Assert.Equal(a.LinkStage, b.LinkStage);
        Assert.Equal(a.ActionOrder, b.ActionOrder);
        Assert.Equal(a.CurrentPlayer, b.CurrentPlayer);
        Assert.Equal(a.Board.Serialize(), b.Board.Serialize());
        Assert.Equal(MapFile.ToJson(a.Map), MapFile.ToJson(b.Map));
        Assert.Equal(a.LockedFlagCounts, b.LockedFlagCounts);
        Assert.Equal(a.PassedThisRound, b.PassedThisRound);
        Assert.Equal(a.SettledBattlefields, b.SettledBattlefields);
        Assert.Equal(a.Pairings.Select(Text), b.Pairings.Select(Text));
        Assert.Equal(a.Links.Select(l => (l.Sequence, l.PairingSequence, l.X, l.Y, l.MapVersion, l.MapDigest, l.Degraded, l.CorridorCells, string.Join(",", l.NewRelics))),
            b.Links.Select(l => (l.Sequence, l.PairingSequence, l.X, l.Y, l.MapVersion, l.MapDigest, l.Degraded, l.CorridorCells, string.Join(",", l.NewRelics))));
        Assert.Equal(a.Settlements.Select(Text), b.Settlements.Select(Text));
        Assert.Equal(a.PlayerStates.Select(Text), b.PlayerStates.Select(Text));
        Assert.Equal(a.Relics.PublicStates().Select(r => r.ToString()), b.Relics.PublicStates().Select(r => r.ToString()));
        Assert.Equal(a.Relics.LinkedPlacements.Select(p => p.ToString()), b.Relics.LinkedPlacements.Select(p => p.ToString()));
        Assert.Equal(a.PendingPairing is null, b.PendingPairing is null);
    }

    private static string Text(PairingRecord p) =>
        $"{p.Sequence}|{p.Kind}|{p.MajorRound}|{string.Join(",", p.Candidates)}|{string.Join(",", p.Pairs)}|{p.Waiter}|{p.Source}";

    private static string Text(BattlefieldSettlementRecord s) =>
        $"{s.Sequence}|{s.MajorRound}|{s.Battlefield}|{s.Reason}|{s.Winner}|{s.EliminationOrder}|{string.Join(",", s.Entries)}";

    private static string Text(PlayerFlowState s) =>
        $"{s}|{s.EliminationReason}|{s.PowerAtDefeat}|{s.WaitingRounds}";
}
