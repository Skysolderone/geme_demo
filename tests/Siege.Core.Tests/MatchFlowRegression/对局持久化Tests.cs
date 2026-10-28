using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.MatchFlowRegression;

/// <summary>implement 1.1 / 8.2：对局顶层状态在小回合边界的持久化与恢复。</summary>
/// <remarks>
/// retire-legacy-rules 1.5：载体由 9×9 旧模式夹具换为平坦 8×8 链接档图（<c>LinkModeFixtures.PlainStarted</c>，四人同锁棋盘 0、手工信物），
/// 恢复走测试口 <see cref="MatchFlow.RestoreUnvalidated"/> 的链接路径（空盘 → 重放链接 → 写盘面 → 信物揭示）；坐标平移到棋盘 0，逐条见迁移清单第 6 节。
/// </remarks>
public class 对局持久化Tests
{
    [Fact]
    public void 小回合边界存档恢复后状态完全一致()
    {
        // 阶段、顺序、大回合、保护状态、Pass 计数、出局 / 弃赛状态、已提交盘面历史、信物揭示、手牌、弃赛快照、先手明细全部往返；
        // 恢复后继续同样的决策（含征募抽取）产出逐字节相同的存档——三条随机子流的消费位置也被正确恢复。
        // 变异验证 M-P1：Serialize 不写 PassStreak → 红 1；M-P2：HandLedger.Restore 不推进 recruit 子流 → 红 1（续跑后面板不同）。
        // 段 C 改摆法：出局判据改为"曾建立正势力且势力归零"。P2 首轮落 C10（标记置位），第 5 大回合 P0 另有 D10，
        // 批次 G7 / E5 / C9 的 C9 提光 C10 → P2 出局（原为 P2 首轮 Pass、盘面与手牌皆空）。
        // retire-legacy-rules 1.5：载体为平坦 8×8 链接档图、四人同锁棋盘 0；原 9×9 坐标列 +2、第 1–7 行 +2 / 第 8–9 行 +1（左上角 A9 贴棋盘 0 的角 C10），断言不变。
        MatchFlow match = LinkModeFixtures.PlainStarted();
        string?[] cell = ["D4", "K4", "C10", "K9"];
        for (int i = 0; i < 4; i++)
        {
            string? c = cell[match.CurrentPlayer!.Value.Value];
            _ = c is null ? match.PassTurn() : match.PlayTurn(c);
        }

        match.AtRound(5, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3]).Stones(TestMaps.P0, "D10");
        match.Resign(TestMaps.P3);
        match.PlayTurn("G7", "E5", "C9");   // P0：提光 P2 的 C10 → P2 出局（relic-redesign W1：原"控制军令与先锋"随旧信物删除，信物层见下）
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(TestMaps.P2).Status);
        match.PassTurn();             // P1 → 出局 / 弃赛者被跳过，大回合结束，第 6 大回合顺序 [P0, P1]
        Assert.Equal(6, match.MajorRound);
        Assert.Equal(1, match.PassStreak);
        // 段 F 6.4c：局面里带一处地形改造（经唯一写入口，与 同形与存档纳入设施Tests 同法）。存档记的是**开局**地图摘要，
        // 恢复时 MUST 传 Board.BaseMap；传改造后的活地形 match.Map 会报"地图不一致"（改前实跑红：FormatException）。
        TerrainEdit fence = TerrainEdit.Fence(TestMaps.At("J4"), TestMaps.At("J5"));
        match.Board.ApplyTerrainEdits([fence]);
        // relic-redesign W1：局面里带信物层——P0 的 G7 挂一件带入保险件、空格 H9 一件地图信物（盘面序列化的 # 段随存档往返）。
        match.Board.AttachRelic(TestMaps.At("G7"), RelicToken.Carried(0, RelicKind.Banner, TestMaps.P0, insured: true));
        match.Board.AttachRelic(TestMaps.At("H9"), RelicToken.Spawned(1, RelicKind.Watchtower));
        Assert.NotEqual(MapFile.ToJson(match.Board.BaseMap), MapFile.ToJson(match.Map));

        string json = match.Serialize();
        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, json);
        Assert.Equal([fence], restored.Board.TerrainEdits);
        Assert.Equal(MapFile.ToJson(match.Map), MapFile.ToJson(restored.Map));   // 恢复出的活地形与改造后的逐字节相同，不只是"没抛异常"

        Assert.Equal(match.Phase, restored.Phase);
        Assert.Equal(match.MajorRound, restored.MajorRound);
        Assert.Equal(TurnStage.Idle, restored.Stage);
        Assert.Equal(match.ActionOrder, restored.ActionOrder);
        Assert.Equal(match.CurrentPlayer, restored.CurrentPlayer);
        Assert.Equal(match.PassStreak, restored.PassStreak);
        Assert.Equal(match.PlayerStates, restored.PlayerStates);
        Assert.Equal(match.Board.Serialize(), restored.Board.Serialize());
        Assert.Equal(match.History.Serialize(), restored.History.Serialize());
        Assert.Equal(match.Board.RelicsOnBoard(), restored.Board.RelicsOnBoard());
        Assert.Equal(2, restored.Board.RelicsOnBoard().Length);
        Assert.Equal(match.RelicBatches.Select(b => b.ToString()), restored.RelicBatches.Select(b => b.ToString()));
        Assert.Equal(match.NextRelicId, restored.NextRelicId);
        Assert.Equal(match.Hands.PublicViews(), restored.Hands.PublicViews());
        Assert.Equal(match.Hands.RecruitStreamConsumed, restored.Hands.RecruitStreamConsumed);
        foreach (PlayerId p in TestMaps.All)
        {
            Assert.Equal(match.Hands.Debug.PrivateViewOf(p), restored.Hands.Debug.PrivateViewOf(p));
        }

        Assert.Equal(match.Resignations.Select(r => (r.Player, r.Board, r.Hand, r.Effects, r.Power)),
            restored.Resignations.Select(r => (r.Player, r.Board, r.Hand, r.Effects, r.Power)));
        Assert.Equal(match.InitiativeReports.Select(r => r.NextOrder), restored.InitiativeReports.Select(r => r.NextOrder));
        Assert.Equal(match.InitiativeReports.SelectMany(r => r.Entries), restored.InitiativeReports.SelectMany(r => r.Entries));
        // PlayerPower 含 ImmutableArray 字段，record 相等退化为数组引用比较，须按值投影比较。
        Assert.Equal(PowerText(match), PowerText(restored));
        Assert.Equal(json, restored.Serialize());

        // 续跑：同样的决策 → 同样的面板、同样的存档
        foreach (MatchFlow m in new[] { match, restored })
        {
            m.BeginTurn();
            Assert.Equal(4, m.CurrentSnapshot!.DeployLimit);   // 第 6 大回合基础 4（relic-redesign W1：旧军令 +1 随旧信物删除）
            RecruitPanelView panel = m.EnterRecruit();
            m.CurrentHand().Pick(panel.Candidates.First(c => c.IsSelectable).Index);
            Assert.Null(m.EnterDeploy().Stage(TestMaps.At("H8"), PieceType.Basic));
            Assert.True(m.Confirm().Confirmed);
        }

        Assert.Equal(match.Hands.Debug.PrivateViewOf(TestMaps.P0), restored.Hands.Debug.PrivateViewOf(TestMaps.P0));
        Assert.Equal(match.Serialize(), restored.Serialize());
    }

    private static string PowerText(MatchFlow m)
    {
        Scoring.PowerSnapshot s = m.Scoreboard.Latest!;
        string players = string.Join(";", s.Players.Select(p =>
            $"{p.Player}:{p.Status}:{p.Total}:[{string.Join(",", p.ExclusiveCells.Notations())}]:[{string.Join("|", p.Groups.Select(g => $"{string.Join(",", g.Stones.Notations())}={g.Power}"))}]"));
        string ranking = string.Join(";", s.Ranking.Select(r => $"{r.Rank}:{r.Power}:{string.Join(",", r.Players.Order())}"));
        return players + "\n" + ranking;
    }

    [Fact]
    public void 已结束对局与终局结果可恢复()
    {
        // 段 C 改摆法：P1 / P2 / P3 各有一子（标记置位），被 P0 一个批次同时提光 → 三人同时出局、共享出局序号 → 只剩 P0
        // （原为三人盘面与手牌皆空、P0 落 B2）。同时出局者的共享名次也随存档往返。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P0, "G7", "D3", "J10", "J3")
            .Stones(TestMaps.P1, "C3")
            .Stones(TestMaps.P2, "K10")
            .Stones(TestMaps.P3, "K3");
        match.PlayTurn("C4", "K9", "K4");
        Assert.Equal(EndReason.LastPlayerStanding, match.Result!.Reason);
        Assert.Equal([1, 2, 2, 2], match.Result.Standings.Select(s => s.Rank));

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Serialize());
        Assert.Equal(MatchPhase.Ended, restored.Phase);
        Assert.Equal(match.Result.Reason, restored.Result!.Reason);
        Assert.Equal(match.Result.Standings, restored.Result.Standings);
        Assert.Equal(match.Result.Winners, restored.Result.Winners);
    }

    [Fact]
    public void 小回合进行中不能存档()
    {
        MatchFlow match = LinkModeFixtures.PlainStarted();
        match.BeginTurn();
        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => match.Serialize());
        Assert.Contains("小回合边界", ex.Message);
    }

    [Fact]
    public void 存档地图不符即拒绝()
    {
        MatchFlow match = LinkModeFixtures.PlainStarted();
        MapData other = LinkModeFixtures.PlainMap() with { Id = "another-map" };
        Assert.Throws<FormatException>(() => MatchFlow.RestoreUnvalidated(other, match.Serialize()));
    }

    [Fact]
    public void 匠人权重随存档往返且旧存档回填()
    {
        // artisan-terrain-edit R-2 / R-6：匠人权重属于对局配置，入存档；旧存档没有该字段 → 回填 10 并留痕。
        // 两条腿（testing.md 持久化守门）：① 非回填值 18 往返，逐字段比活对象 + 再存档逐字节相等；
        // ② 剥掉 ArtisanWeight 字段的旧存档 → 回填 10、ArtisanWeightBackfilled 为 true。
        // 变异验证见测试报告 M-A6（Serialize 不写 ArtisanWeight）。
        // more-pieces-relics 段 A：下面用六档字面量表独立抽面板，依赖征募序列 → 写死内容集 v1（v1 棋池与引入新棋子之前相同）。
        MatchFlow match = LinkModeFixtures.PlainStarted(options: MatchOptions.Immediate with { ArtisanWeight = 18, ContentSet = ContentSet.V1 });
        Assert.Equal(18, match.ArtisanWeight);
        Assert.False(match.ArtisanWeightBackfilled);

        string json = match.Serialize();
        Assert.Contains("\"ArtisanWeight\": 18", json);
        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, json);
        Assert.Equal(18, restored.ArtisanWeight);
        Assert.False(restored.ArtisanWeightBackfilled);
        Assert.Equal(json, restored.Serialize());

        // 独立读路径（testing.md：结果对象与活对象两边都要钉）——restored.Options 与 restored.ArtisanWeight 是同一个字段的两个读法，
        // 证明不了"恢复出来的手牌账本真拿到了 18"。这里从恢复后的账本实抽一次面板，与 18 权重下的字面量表独立对齐。
        RandomStream expected = TestMaps.Seed.Stream(GameSeed.Recruit);
        expected.Advance(restored.Hands.RecruitStreamConsumed);
        int[] table18 = [160, 80, 72, 48, 40, 72];   // 基础 × 4（无徽记），匠人 18 × 4 = 72
        PieceType[] direct = [.. Enumerable.Range(0, 10).Select(_ => RecruitWeights.Order[expected.WeightedPick(table18)])];
        RecruitPanelView panel = HandFixtures.Begin(restored.Hands, TestMaps.P0, reveal: 10).EnterRecruit();
        Assert.Equal(direct, panel.CandidateTypes);

        RandomStream atDefault = TestMaps.Seed.Stream(GameSeed.Recruit);
        atDefault.Advance(restored.Hands.RecruitStreamConsumed);
        int[] table10 = [160, 80, 72, 48, 40, 40];
        Assert.NotEqual([.. Enumerable.Range(0, 10).Select(_ => RecruitWeights.Order[atDefault.WeightedPick(table10)])], panel.CandidateTypes);

        System.Text.Json.Nodes.JsonNode legacyNode = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        Assert.True(legacyNode.AsObject().Remove("ArtisanWeight"));
        string legacy = legacyNode.ToJsonString();
        Assert.DoesNotContain("ArtisanWeight", legacy);
        MatchFlow fromLegacy = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, legacy);
        Assert.Equal(MatchOptions.DefaultArtisanWeight, fromLegacy.ArtisanWeight);
        Assert.Equal(10, fromLegacy.ArtisanWeight);
        Assert.True(fromLegacy.ArtisanWeightBackfilled);
    }

    [Fact]
    public void 终局独占空格数随存档往返而含据点分值的旧存档被拒()
    {
        // 两条腿（testing.md 持久化守门）：
        // ① 非回填值往返：终局名次的"独占空格数"是非 0 值 → 恢复后逐字段相等 + 再存档逐字节相等；
        // ② 旧存档：往存档里塞回已废弃的 SiteValues 段 → 明确报错并指出该字段已废弃（design.md Migration Plan 3：旧存档不迁移）。
        // 变异验证 M-B18（段 B，实跑红 1）：MatchFlow.Parse 去掉 RejectRetiredFields 调用（退回静默忽略）→ ② 红。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All)
            .Stones(TestMaps.P0, "D7", "E7");
        match.Resign(TestMaps.P1);
        match.Resign(TestMaps.P2);
        match.Resign(TestMaps.P3);
        int cells = match.Result!.Of(TestMaps.P0).Input.ExclusiveCells;
        Assert.True(cells > 0, "前提：终局独占空格数非 0");

        string json = match.Serialize();
        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, json);
        Assert.Equal(
            match.Result.Standings.Select(s => (s.Player, s.Rank, s.Input.Power, s.Input.HeldRelics, s.Input.ExclusiveCells, s.Input.Stones)),
            restored.Result!.Standings.Select(s => (s.Player, s.Rank, s.Input.Power, s.Input.HeldRelics, s.Input.ExclusiveCells, s.Input.Stones)));
        Assert.Equal(json, restored.Serialize());

        System.Text.Json.Nodes.JsonNode legacyNode = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        legacyNode["SiteValues"] = new System.Text.Json.Nodes.JsonObject
        {
            ["Tent"] = 5, ["Campfire"] = 15, ["Stele"] = 45,
        };
        string legacy = legacyNode.ToJsonString();

        FormatException ex = Assert.Throws<FormatException>(
            () => MatchFlow.RestoreUnvalidated(match.Board.BaseMap, legacy));
        Assert.Contains("SiteValues", ex.Message, StringComparison.Ordinal);
        Assert.Contains("废弃", ex.Message, StringComparison.Ordinal);

        // 反面：其余未知字段照旧宽容，不是"任何多余字段都拒绝"。
        System.Text.Json.Nodes.JsonNode extraNode = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        extraNode["_comment"] = "手工批注";
        MatchFlow withExtra = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, extraNode.ToJsonString());
        Assert.Equal(json, withExtra.Serialize());
    }

    [Fact]
    public void 曾建立正势力标记随存档往返()
    {
        // restore-go-core-rules D3：「曾建立正势力」标记随存档往返。有效断言是 false 的那半边——true 的一方恢复后重算也能得到，
        // 从未落子者的 false 若被恢复时的重算"算出来"就无从分辨，所以钉住：P3 从未落子（手牌清空）→ 存档 false → 恢复后仍 false →
        // 随后的 Pass 不让他出局；P0 已有子 → true。另把 JSON 里 P3 的标记改成 true 再恢复：此时 P3 势力 0 且标记置位，
        // 下一次检查即出局——证明恢复读的是存档里的值，而不是按盘面重算。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, [TestMaps.P1, TestMaps.P3, TestMaps.P0, TestMaps.P2])
            .Stones(TestMaps.P0, "G7");
        match.Debug.SeedHand(TestMaps.P3);
        Assert.True(match.StateOf(TestMaps.P0).HasEstablishedPower);
        Assert.False(match.StateOf(TestMaps.P3).HasEstablishedPower);

        string json = match.Serialize();
        Assert.Contains("\"HasEstablishedPower\": false", json, StringComparison.Ordinal);
        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, json);
        Assert.Equal(match.PlayerStates, restored.PlayerStates);
        Assert.False(restored.StateOf(TestMaps.P3).HasEstablishedPower);
        Assert.True(restored.StateOf(TestMaps.P0).HasEstablishedPower);
        Assert.Equal(json, restored.Serialize());

        restored.PassTurn();   // P1
        restored.PassTurn();   // P3 自己 Pass：势力 0 但从未置位 → 不出局
        Assert.Equal(PlayerStatus.Active, restored.StateOf(TestMaps.P3).Status);

        System.Text.Json.Nodes.JsonNode forged = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        foreach (System.Text.Json.Nodes.JsonNode? player in forged["Players"]!.AsArray())
        {
            if ((int)player!["Player"]! == TestMaps.P3.Value)
            {
                player["HasEstablishedPower"] = true;
            }
        }

        MatchFlow marked = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, forged.ToJsonString());
        Assert.True(marked.StateOf(TestMaps.P3).HasEstablishedPower);
        marked.PassTurn();     // P1 Pass → 检查全部参赛玩家：P3 标记置位且势力 0 → 出局
        Assert.Equal(PlayerStatus.Eliminated, marked.StateOf(TestMaps.P3).Status);
    }

    [Theory]
    [InlineData("MaxMajorRounds", "15")]
    [InlineData("DominanceStartRound", "7")]
    [InlineData("CatchUpRecruit", "true")]
    [InlineData("DominanceCandidate", "null")]
    [InlineData("DominancePending", "[]")]
    public void 含大回合上限碾压或补偿字段的旧存档被拒(string field, string value)
    {
        // match-setup 三条 REMOVED 的 Migration：大回合上限 / 碾压起始大回合 / 落后补偿开关三项不再入存档，旧存档不兼容，
        // 加载时明确报错（不回填）；碾压候选与待回应名单（elimination-endgame「势力碾压」REMOVED）同理。
        // 旧存档里这五个字段即使取值为 null / 空数组也会写出，所以按"字段出现"判定。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All);
        string json = match.Serialize();
        Assert.DoesNotContain($"\"{field}\"", json, StringComparison.Ordinal);   // 新存档不再写出

        System.Text.Json.Nodes.JsonNode legacyNode = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        legacyNode[field] = System.Text.Json.Nodes.JsonNode.Parse(value);
        FormatException ex = Assert.Throws<FormatException>(
            () => MatchFlow.RestoreUnvalidated(match.Board.BaseMap, legacyNode.ToJsonString()));
        Assert.Contains(field, ex.Message, StringComparison.Ordinal);
        Assert.Contains("废弃", ex.Message, StringComparison.Ordinal);
    }
}
