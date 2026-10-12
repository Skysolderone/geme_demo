using System.Text.Json.Nodes;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
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
        // 段 F：没有公共棋盘，P1 落在拼过来的 4 号棋盘（已平移）上。
        Coord onPartner = match.Board.Battlefields.ById(1)!.Cells.First(c => match.Map.BirthZoneOf(c) == 4 && match.Board[c].Occupant is null);
        match.Place(P1, onPartner);
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
    public void 旧存档被拒绝()
    {
        // match-setup Scenario「旧存档被拒绝」（Q6 / 实现裁决 D7）：恢复一个地图标识不是 link: 的旧存档 → 玩家入口 MatchFlow.Restore 拒绝读取并提示属于已移除的地图模式；
        // Legacy 夹具走等价内部口 RestoreLegacy（校验与重建相同，只是不拒绝旧地图），围棋内核的回归护栏不受影响。
        // 变异 M-D3（段 D-a 实跑）：Restore 去掉地图标识门 → 本测试红 1。
        MapData v5 = MapCatalog.Legacy.Resolve(null);
        MatchFlow legacy = MatchFlow.Create(v5, MatchFixtures.Seed, MatchFixtures.All);
        legacy.PlantPrototype();
        string json = legacy.Serialize();
        Assert.Equal("siege-4p-base-v5", MatchFlow.SavedMapId(json));

        FormatException ex = Assert.Throws<FormatException>(() => MatchFlow.Restore(v5, json));
        Assert.Contains("已移除的地图模式", ex.Message, StringComparison.Ordinal);
        Assert.Contains("siege-4p-base-v5", ex.Message, StringComparison.Ordinal);
        Assert.Contains("link:<地图种子>", ex.Message, StringComparison.Ordinal);

        MatchFlow restored = MatchFlow.RestoreLegacy(v5, json);
        Assert.Equal(json, restored.Serialize());
        Assert.Equal(MatchMode.Legacy, restored.Mode);

        // 反面：链接模式存档照常经玩家入口恢复。
        MatchFlow link = Started([0, 1, 3, 4]);
        Assert.Equal(link.Serialize(), MatchFlow.Restore(Map, link.Serialize()).Serialize());
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
    public void 链接恢复须四人()
    {
        // 裁决 S1（链接模式只有 4 人局）在恢复路径同样成立（段 B 质检遗留，tasks 3.0）：把一份 4 人链接模式存档手改成 3 人 → 恢复被拒，
        // 报出的是人数（不是别处碰巧失败）；未改的原存档照常恢复（反面）。
        // 变异 MC-23：RestoreCore 的人数校验传恒为 4 的人数（RequireModeMatchesMap(options.Mode, map, 4)）→ 本测试红（3 人存档被接受或在别处失败）。
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(1);
        string json = match.Serialize();
        Assert.Equal(json, MatchFlow.Restore(Map, json).Serialize());

        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonArray players = root["Players"]!.AsArray();
        Assert.Equal(4, players.Count);   // 样本口径：手改前确是 4 人
        players.RemoveAt(3);
        FormatException ex = Assert.Throws<FormatException>(() => MatchFlow.Restore(Map, root.ToJsonString()));
        Assert.Contains("链接模式只有 4 人局，实际 3 人", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 链接重放与存档不符即拒绝()
    {
        MatchFlow match = Started([0, 1, 3, 4]).PassRounds(3);
        match.ApplyPairing([(0, 3), (1, 4)]);
        JsonObject root = JsonNode.Parse(match.Serialize())!.AsObject();
        root["Link"]!["Links"]![1]!["MapDigest"] = "00";
        Assert.Throws<FormatException>(() => MatchFlow.Restore(Map, root.ToJsonString()));

        // 段 F：平移向量与贴边也是记录的一部分——篡改任一项即拒绝（变异 M-F15：恢复不比对向量 → 下面第一条不抛）。
        JsonObject shifted = JsonNode.Parse(match.Serialize())!.AsObject();
        shifted["Link"]!["Links"]![1]!["Shifts"]![0]!["Dx"] = 99;
        Assert.Throws<FormatException>(() => MatchFlow.Restore(Map, shifted.ToJsonString()));
        JsonObject sided = JsonNode.Parse(match.Serialize())!.AsObject();
        sided["Link"]!["Links"]![0]!["Side"] = "North";
        Assert.Throws<FormatException>(() => MatchFlow.Restore(Map, sided.ToJsonString()));
    }

    [Fact]
    public void 锚点非零向量的拼接可存档恢复()
    {
        // 裁决 J8-3：最长共享边的候选全部越界时整个布局整体平移、锚点向量非零并记入链接记录；存档恢复按记录重放、逐项核对，篡改锚点向量即拒绝。
        // 手搭的 link: 图（不经生成器）：0 号 5×7、1 号 5×5、2 号 5×5、3 号 5×7、4 号 5×5 一字排开。四人四区、脚本缺省配对 (0,1)(2,3)：
        // 首轮东贴后 {0,1} 顶行只剩 0 号、{2,3} 底行只剩 3 号（3 号偏南贴到 y = 0）；决赛北贴唯一的最长共享边位置让 2 号越出原点 3 列 → 整体东移 3。
        MapData map = LinkMapFixtures.Build("link:900001",
        [
            new(new Coord(2, 2), 5, 7, BoardPlateKind.Birth),
            new(new Coord(10, 2), 5, 5, BoardPlateKind.Birth),
            new(new Coord(18, 2), 5, 5, BoardPlateKind.Birth),
            new(new Coord(26, 2), 5, 7, BoardPlateKind.Birth),
            new(new Coord(34, 2), 5, 5, BoardPlateKind.Birth),
        ]);
        MatchFlow match = MatchFlow.Create(map, MatchFixtures.Seed, All, LinkModeFixtures.Options);
        match.PairingSource = new ScriptedPairingSource();
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(All.Select((p, i) => (p, i)));
        Coord anchorStone = new(2, 8);   // 0 号顶行西端
        match.Place(P0, anchorStone);
        match.PassRounds(4);

        Assert.Equal(3, match.Links.Count);
        LinkRecord final = match.Links[2];
        Assert.Equal((0, 2, LinkSide.North), (final.X, final.Y, final.Side));
        Assert.Equal(new BattlefieldShift(0, 3, 0), final.ShiftOf(0));   // 锚点非零向量
        Assert.Equal(0, match.Map.Boards[2].Origin.X);   // 整体平移恰好不越界：被贴方最西列落在 0
        Assert.Equal(new Occupant(P0, PieceType.Basic), match.Board[match.Track(anchorStone, 0)].Occupant);
        Assert.Equal(new Coord(5, 8), match.Track(anchorStone, 0));

        string json = match.Serialize();
        MatchFlow restored = MatchFlow.Restore(map, json);
        Assert.Equal(json, restored.Serialize());
        Assert.Equal(MapFile.ToJson(match.Map), MapFile.ToJson(restored.Map));
        Assert.Equal(match.Board.Serialize(), restored.Board.Serialize());
        Assert.Equal(match.Links.Select(Battlefield.链接执行Tests.Text), restored.Links.Select(Battlefield.链接执行Tests.Text));

        JsonObject tampered = JsonNode.Parse(json)!.AsObject();
        tampered["Link"]!["Links"]![2]!["Shifts"]![0]!["Dx"] = 0;
        Assert.Throws<FormatException>(() => MatchFlow.Restore(map, tampered.ToJsonString()));
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
        Assert.Equal(a.Links.Select(Battlefield.链接执行Tests.Text), b.Links.Select(Battlefield.链接执行Tests.Text));
        Assert.Equal(a.Settlements.Select(Text), b.Settlements.Select(Text));
        Assert.Equal(a.PlayerStates.Select(Text), b.PlayerStates.Select(Text));
        Assert.Equal(a.Relics.PublicStates().Select(r => r.ToString()), b.Relics.PublicStates().Select(r => r.ToString()));
        Assert.Equal(a.PendingPairing is null, b.PendingPairing is null);
    }

    private static string Text(PairingRecord p) =>
        $"{p.Sequence}|{p.Kind}|{p.MajorRound}|{string.Join(",", p.Candidates)}|{string.Join(",", p.Pairs)}|{p.Waiter}|{p.Source}";

    private static string Text(BattlefieldSettlementRecord s) =>
        $"{s.Sequence}|{s.MajorRound}|{s.Battlefield}|{s.Reason}|{s.Winner}|{s.EliminationOrder}|{string.Join(",", s.Entries)}";

    private static string Text(PlayerFlowState s) =>
        $"{s}|{s.EliminationReason}|{s.PowerAtDefeat}|{s.WaitingRounds}";
}
