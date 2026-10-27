using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Core.Scoring;
using static Siege.Core.Tests.PieceRelics.CarryWiringFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign 接线段 W2：带入信物接入对局（R14–R17、R22，D1 / D5 / D6）。AC4（带入 6 件或保险 3 件被拒、未挂出不生效、保护期可挂）、
/// AC5 的接线部分（保险件被敌方完赛者持有仍归原主、原主出局非保险件不回库存、同一件不被两人带出、同局只结算一次），以及挂出编号、存档往返、可见性与 R17 掉落。
/// 纯函数口径（D6 逐条、档案 v2）另见 <see cref="带出结算归属Tests"/>、<see cref="信物档案Tests"/>、<see cref="带入清单校验Tests"/>。
/// </summary>
/// <remarks>
/// 变异验证（W2 实跑，均已恢复；"红 n"为本类与日志 / 配置 / 终端相关测试类合计）：
/// W1 <c>BatchRehearsal.Project</c> 不挂带入件（只正式第 2 步挂）→ 红 11（凡有挂出的局都在结算核对处抛"正式结算结果与预演副本不一致"）；
/// W2 <c>ValidateShape</c> 去掉"落点已有地图信物"一条 → 红 2（非法挂出被拒、终局结算按D6归属）；
/// W3 <c>RecordCarriedAttachments</c> 不推进游标 → 红 5（保护期内挂出、存档往返、终局结算按D6归属、AI前几批按落子顺序挂完、关闭时逐步相同的反面对照）；
/// W4 <c>DropCarriedOf</c> 把保险件也掉落 → 红 2（弃赛时非保险件掉到地图、日志的带出结算可查）；
/// W5 <c>PublishFor</c> 的 OwnCarried 不按座位过滤（给全员的清单）→ 红 1（带入清单只给本人）。
/// </remarks>
public class 带入接入对局Tests
{
    private static readonly PlayerId P0 = TestMaps.P0;
    private static readonly PlayerId P1 = TestMaps.P1;
    private static readonly PlayerId P2 = TestMaps.P2;
    private static readonly PlayerId P3 = TestMaps.P3;

    /// <summary>平坦夹具（四人同锁 8×8 的棋盘 0，第 1 大回合 = 构筑保护期），顺序 P0 → P3。</summary>
    private static MatchFlow Started(params (int Player, RelicLoadout Loadout)[] carries) =>
        LinkModeFixtures.PlainStarted(options: On(carries)).AtRound(1, TestMaps.All);

    private static StagedBatch Deploy(MatchFlow match)
    {
        match.BeginTurn();
        match.EnterRecruit();
        return match.EnterDeploy();
    }

    /// <summary>当前玩家落下这批（每项可带挂出下标）并确认，必须合法。</summary>
    private static void Turn(MatchFlow match, params (string Cell, int? Attach)[] placements)
    {
        StagedBatch batch = Deploy(match);
        foreach ((string cell, int? attach) in placements)
        {
            Assert.Null(batch.Stage(TestMaps.At(cell), PieceType.Basic, null, attach));
        }

        Assert.True(match.Confirm().Confirmed);
    }

    /// <summary>其他玩家依次 Pass，直到轮到 <paramref name="player"/>。</summary>
    private static void PassUntil(MatchFlow match, PlayerId player)
    {
        for (int guard = 0; match.CurrentPlayer != player; guard++)
        {
            Assert.True(guard < 20, "轮不到该玩家");
            match.PassTurn();
        }
    }

    [Fact]
    public void 保护期内挂出即挂在新子上并公开()
    {
        // R14：部署时给新落子挂带入件，保护期可挂；挂上即公开（R22）、按批内顺序从编号游标分配本局编号（D1）；预演副本与正式盘面的信物层一致。
        MatchFlow match = Started((0, Loadout((RelicKind.WarDrum, true), (RelicKind.Banner, false))));
        Assert.True(match.MajorRound <= MatchFlow.BuildProtectionRounds);
        StagedBatch batch = Deploy(match);
        Assert.Equal([0, 1], batch.Context.Carried!.Keys.Order());

        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Basic, null, 0));
        Assert.Null(batch.Stage(TestMaps.At("F5"), PieceType.Basic));
        RelicToken expected = RelicToken.Carried(0, RelicKind.WarDrum, P0, insured: true);
        RehearsalResult rehearsal = match.Rehearse();
        Assert.True(rehearsal.IsLegal);
        Assert.Equal(expected, rehearsal.ProjectedBoard!.RelicAt(TestMaps.At("E5")));
        Assert.Null(match.Board.RelicAt(TestMaps.At("E5")));   // 预演零副作用

        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(expected, match.Board.RelicAt(TestMaps.At("E5")));
        Assert.Null(match.Board.RelicAt(TestMaps.At("F5")));
        Assert.Equal(1, match.NextRelicId);
        Assert.Equal([1], match.PendingCarriedOf(P0).Keys);
        Assert.Contains(match.Events, e => e.Kind == FlowEventKind.CarriedRelicsAttached && e.Player == P0);

        SeatRelicView seen = Assert.Single(match.PublishFor(P1).Relics);
        Assert.Equal(("E5", 0, RelicKind.WarDrum, (PlayerId?)P0, true, (PlayerId?)P0), (seen.Coord, seen.Id, seen.Kind, seen.Origin, seen.Insured, seen.Holder));
    }

    [Fact]
    public void 未挂出的带入件不产生效果()
    {
        // AC4：带入了战鼓但没挂出 → 势力与不带入完全相同；挂出后那枚棋子基础军势 +3（单子棋串、无倍率 → 总势力恰好 +3）。
        MatchFlow plain = Started();
        MatchFlow carried = Started((0, Loadout((RelicKind.WarDrum, false))));
        MatchFlow attached = Started((0, Loadout((RelicKind.WarDrum, false))));
        Turn(plain, ("E5", null));
        Turn(carried, ("E5", null));
        Turn(attached, ("E5", 0));

        System.Numerics.BigInteger Total(MatchFlow m) => m.Scoreboard.Latest!.Of(P0).Total;
        Assert.Equal(Total(plain), Total(carried));
        Assert.Equal(Total(plain) + 3, Total(attached));
    }

    [Fact]
    public void 非法挂出被拒()
    {
        // R13 / R14：清单里没有的下标、同一件本批挂两次、落点已有地图信物（落子即拾取，每子 1 件）、已挂出的件、别人的清单——一律 CarriedRelicIllegal，暂放不变。
        MatchFlow match = Started((0, Loadout((RelicKind.WarDrum, false), (RelicKind.Command, false))));
        match.Board.AttachRelic(TestMaps.At("F6"), RelicToken.Spawned(99, RelicKind.Banner));
        StagedBatch batch = Deploy(match);

        Assert.Equal(BatchFailureKind.CarriedRelicIllegal, batch.Stage(TestMaps.At("E5"), PieceType.Basic, null, 5)!.Kind);
        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Basic, null, 0));
        Assert.Equal(BatchFailureKind.CarriedRelicIllegal, batch.Stage(TestMaps.At("E6"), PieceType.Basic, null, 0)!.Kind);
        Assert.Equal(BatchFailureKind.CarriedRelicIllegal, batch.Stage(TestMaps.At("F6"), PieceType.Basic, null, 1)!.Kind);
        Assert.Equal(1, batch.Count);
        Assert.Null(batch.Stage(TestMaps.At("F6"), PieceType.Basic));   // 不挂即拾取地图信物，合法
        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(RelicToken.Spawned(99, RelicKind.Banner), match.Board.RelicAt(TestMaps.At("F6")));

        // P1 没有带入：部署上下文里没有可挂的件。
        StagedBatch other = Deploy(match);
        Assert.Null(other.Context.Carried);
        Assert.Equal(BatchFailureKind.CarriedRelicIllegal, other.Stage(TestMaps.At("H8"), PieceType.Basic, null, 0)!.Kind);
        match.Pass();

        // 同一件不能挂两次：P0 下一个小回合，已挂出的第 0 件不再可挂。
        PassUntil(match, P0);
        StagedBatch again = Deploy(match);
        Assert.Equal([1], again.Context.Carried!.Keys);
        Assert.Equal(BatchFailureKind.CarriedRelicIllegal, again.Stage(TestMaps.At("H3"), PieceType.Basic, null, 0)!.Kind);
    }

    [Fact]
    public void 存档往返保留挂出编号与未挂出件()
    {
        // D7 / W2-2：每名玩家的"未挂出带入件"与已分配的本局编号随对局存档；恢复后下一次部署的可挂件与编号游标不变。存档里编号越界即拒绝。
        MatchFlow match = Started((0, Loadout((RelicKind.WarDrum, true), (RelicKind.Banner, false))), (2, Loadout((RelicKind.Chameleon, false))));
        Turn(match, ("E5", 0));
        string json = match.Serialize();

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, json);

        Assert.Equal(json, restored.Serialize());
        Assert.Equal(match.NextRelicId, restored.NextRelicId);
        Assert.Equal(match.PendingCarriedOf(P0), restored.PendingCarriedOf(P0));
        Assert.Equal(match.PendingCarriedOf(P2), restored.PendingCarriedOf(P2));
        Assert.Equal(match.Board.RelicAt(TestMaps.At("E5")), restored.Board.RelicAt(TestMaps.At("E5")));

        JsonObject node = JsonNode.Parse(json)!.AsObject();
        node["CarryIns"]![0]!["Items"]![1]!["Id"] = 7;   // P0 的军旗未挂出，却写了一个游标之外的编号
        Assert.Throws<FormatException>(() => MatchFlow.RestoreUnvalidated(match.Board.BaseMap, node.ToJsonString()));
    }

    [Fact]
    public void 带入清单只给本人()
    {
        // R22：带入清单挂出之前不公开——全知公开视图与别的座位都看不到，本座位只看到自己的未挂出件；挂出的那一件进信物层、全公开。
        MatchFlow match = Started((0, Loadout((RelicKind.Banner, true))), (1, Loadout((RelicKind.Watchtower, false), (RelicKind.Command, false))));

        Assert.False(match.Publish().Board.HasRelics);
        Assert.Empty(match.PublishFor(P2).OwnCarried);
        Assert.Equal([(0, RelicKind.Banner, true)], match.PublishFor(P0).OwnCarried.Select(c => (c.Index, c.Kind, c.Insured)));
        Assert.Equal([0, 1], match.PublishFor(P1).OwnCarried.Select(c => c.Index));
        Assert.DoesNotContain(typeof(MatchPublicView).GetProperties(), p => p.Name.Contains("Carr", StringComparison.Ordinal) && p.Name != nameof(MatchPublicView.CarryInOut));

        Turn(match, ("E5", 0));
        Assert.Empty(match.PublishFor(P0).OwnCarried);
        Assert.Equal(RelicKind.Banner, Assert.Single(match.PublishFor(P2).Relics).Kind);
        Assert.Equal(2, match.PublishFor(P1).OwnCarried.Length);
    }

    [Fact]
    public void 弃赛时非保险件掉到地图()
    {
        // R17：出局 / 弃赛时未挂出的非保险件按清单下标顺序取本局编号，经 relic-drop:<编号> 立即落到出生区域以外；保险件不掉，局终归还原主。
        // 不占刷新上限（掉落不看场上件数）。独立复算：同一时刻的盘面（开局空盘）按同一子流重抽，位置逐件相同。
        MapData map = MapCatalog.Resolve("link:1:4");
        var seed = new GameSeed(11);
        MatchFlow match = MatchFlow.Create(map, seed, TestMaps.All,
            On((3, Loadout((RelicKind.Command, false), (RelicKind.Banner, true), (RelicKind.Watchtower, false)))));
        match.PlantPrototype();
        GameBoard before = match.Board.Clone();

        match.Resign(P3);

        RelicToken[] tokens = [RelicToken.Carried(0, RelicKind.Command, P3, false), RelicToken.Carried(1, RelicKind.Watchtower, P3, false)];
        ImmutableArray<BoardRelic> dropped = match.Board.RelicsOnBoard();
        Assert.Equal(tokens, dropped.Select(r => r.Token).OrderBy(t => t.Id));
        Assert.All(dropped, r => Assert.False(new BirthBoardArea(map).IsBirthArea(r.Cell)));
        Assert.Equal(
            RelicSpawner.Drop(before, new BirthBoardArea(map), seed, P3, tokens).OrderBy(r => r.Cell),
            dropped.OrderBy(r => r.Cell));
        Assert.Equal([1], match.PendingCarriedOf(P3).Keys);   // 保险件留在手里
        Assert.Equal(2, match.NextRelicId);

        // 中途结算（D6）：只回保险件；保护期内弃赛 0 点。
        RelicPlayerSettlement early = match.EarlySettlementFor(P3);
        Assert.Equal((RelicOutcome.Resigned, 0), (early.Outcome.Outcome, early.Points));
        Assert.Equal([RelicKind.Banner], early.BroughtOut.Select(t => t.Kind));
    }

    [Fact]
    public void 终局结算按D6归属()
    {
        // AC5 接线：P1 把保险号令挂在 C3（棋盘 0 角落），P0 用 D3 + C4 提走它（P1 势力归零出局，未挂出的非保险望台按 R17 掉落——平坦夹具没有出生区域外的格，当场消失），
        // P0 再落 C3 拾取号令（落在地图信物上不能再挂带入件）。随后 P2、P3 弃赛，P0 只剩一人完赛。
        // D6：保险号令虽挂在完赛者 P0 的棋子上，仍归原主 P1（第 ① 条）；P0 未挂出的战鼓归 P0（第 ③ 条）；望台消失；每件恰好一个去向。
        // 档案（本机玩家 = P1）：开局扣除两件，出局时结算只回保险号令、0 点；同局再结算即忽略。
        MatchFlow match = Started((0, Loadout((RelicKind.WarDrum, false))), (1, Loadout((RelicKind.Command, true), (RelicKind.Watchtower, false))));
        using var temp = new TempRelicProfile();
        RelicProfileStore store = temp.Store();
        store.Load();
        store.Begin("m1", match.CarryIns[P1]);
        Assert.Equal((0, 0), (store.Current.StockOf(RelicKind.Command), store.Current.StockOf(RelicKind.Watchtower)));

        match.PassTurn();                       // P0
        Turn(match, ("C3", 0));                 // P1 挂保险号令 #0
        Assert.Equal(RelicToken.Carried(0, RelicKind.Command, P1, true), match.Board.RelicAt(TestMaps.At("C3")));
        PassUntil(match, P0);
        Turn(match, ("D3", null), ("C4", null)); // 提走 C3
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(P1).Status);
        Assert.Contains(match.Events, e => e.Kind == FlowEventKind.CarriedRelicsDropped && e.Player == P1);
        Assert.Null(match.Board[TestMaps.At("C3")].Occupant);
        Assert.NotNull(match.Board.RelicAt(TestMaps.At("C3")));   // 掉落：信物留在原格

        RelicPlayerSettlement early = match.EarlySettlementFor(P1);
        Assert.True(store.Settle("m1", early));
        Assert.Equal((1, 0, 0), (store.Current.StockOf(RelicKind.Command), store.Current.StockOf(RelicKind.Watchtower), store.Current.Points));
        Assert.False(store.Settle("m1", early));   // 同局只结算一次（在途记录以对局标识去重）

        PassUntil(match, P0);
        StagedBatch batch = Deploy(match);
        Assert.Equal(BatchFailureKind.CarriedRelicIllegal, batch.Stage(TestMaps.At("C3"), PieceType.Basic, null, 0)!.Kind);
        Assert.Null(batch.Stage(TestMaps.At("C3"), PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(P0, match.Board[TestMaps.At("C3")].Occupant!.Value.Owner);   // P0 持有 P1 的保险号令
        match.Resign(P2);
        match.Resign(P3);
        Assert.Equal(MatchPhase.Ended, match.Phase);

        RelicMatchSettlement settled = match.SettleCarried();
        Assert.Equal([RelicToken.Carried(2, RelicKind.WarDrum, P0, false)], settled.Of(P0).BroughtOut);
        Assert.Equal([RelicToken.Carried(0, RelicKind.Command, P1, true)], settled.Of(P1).BroughtOut);
        Assert.Equal([RelicToken.Carried(1, RelicKind.Watchtower, P1, false)], settled.Vanished);
        Assert.Equal(24, settled.Of(P0).Points);
        Assert.Equal(early, settled.Of(P1));   // 中途结算与终局给这名玩家的结果相同
        int[] all = [.. settled.Players.Values.SelectMany(p => p.BroughtOut).Concat(settled.Vanished).Select(t => t.Id)];
        Assert.Equal(all.Distinct().Count(), all.Length);   // 同一件不会被两人带出
        Assert.Equal(settled, match.SettleCarried(), SettlementComparer.Instance);   // 只读：再算一次结果相同
    }

    [Fact]
    public void 未终局不结算()
    {
        // 截断局（未终局）不结算：调用即抛；仍在局的玩家没有中途结算。
        MatchFlow match = Started((0, Loadout((RelicKind.WarDrum, false))));
        Assert.Throws<SiegeRuleException>(() => match.SettleCarried());
        Assert.Throws<SiegeRuleException>(() => match.EarlySettlementFor(P0));
    }

    [Theory]
    [InlineData(6, 0)]
    [InlineData(3, 3)]
    public void 建局拒绝超量清单(int count, int insured)
    {
        // AC4：带入 6 件或保险 3 件被拒（建局同一处校验，恢复存档同样拒绝）。
        RelicLoadout loadout = new([.. Enumerable.Range(0, count).Select(i => new CarriedRelic(RelicKind.WarDrum, i < insured))]);
        Assert.ThrowsAny<ArgumentException>(() => LinkModeFixtures.PlainCreate(options: On((0, loadout))));
    }

    [Fact]
    public void AI带入件数保险与子流()
    {
        // R21：AI 与本机玩家同等件数、只从普通四种抽、随机 min(2, 件数) 件保险；件数为 0 不消费 carry-ai；AI 的带入与本机玩家选了什么无关。
        // 独立复算（determinism.md 登记的消费顺序）：按编号升序，每名 AI 先 count 次 NextInt(4)，再 min(2, count) 次不放回 NextInt(剩余)。
        var seed = new GameSeed(41);
        Assert.Empty(RelicCarryAi.Draw(seed, [P1, P2], 0));
        RandomStream untouched = seed.Stream(GameSeed.CarryAi);
        Assert.Empty(RelicCarryAi.Draw(untouched, [P1, P2], 0));
        Assert.Equal(0, untouched.Consumed);

        ImmutableSortedDictionary<PlayerId, RelicLoadout> drawn = RelicCarryAi.Draw(seed, [P2, P1], 3);
        RandomStream stream = seed.Stream(GameSeed.CarryAi);
        foreach (PlayerId ai in new[] { P1, P2 })
        {
            RelicKind[] kinds = [.. Enumerable.Range(0, 3).Select(_ => RelicKinds.Common[stream.NextInt(4)])];
            List<int> open = [0, 1, 2];
            var insured = new HashSet<int>();
            for (int i = 0; i < 2; i++)
            {
                int pick = stream.NextInt(open.Count);
                insured.Add(open[pick]);
                open.RemoveAt(pick);
            }

            Assert.Equal(new RelicLoadout([.. kinds.Select((k, i) => new CarriedRelic(k, insured.Contains(i)))]), drawn[ai]);
            Assert.Null(drawn[ai].ShapeRefusal());
        }

        RelicLoadout a = Loadout((RelicKind.Banner, true), (RelicKind.WarDrum, false));
        RelicLoadout b = Loadout((RelicKind.Command, false), (RelicKind.Command, false));
        ImmutableSortedDictionary<PlayerId, RelicLoadout> withA = RelicCarryAi.ForHumanMatch(seed, TestMaps.All, P0, a);
        ImmutableSortedDictionary<PlayerId, RelicLoadout> withB = RelicCarryAi.ForHumanMatch(seed, TestMaps.All, P0, b);
        Assert.Equal(withA.Remove(P0), withB.Remove(P0));
        Assert.Equal(a, withA[P0]);
        Assert.Empty(RelicCarryAi.ForHumanMatch(seed, TestMaps.All, P0, RelicLoadout.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => RelicCarryAi.Draw(seed, [P1], 6));
    }

    [Fact]
    public void AI前几批按落子顺序挂完()
    {
        // R21（W2 临时规则）：AI 在最终复摆时按落子顺序把未挂出件逐枚挂上，落点已有地图信物的跳过。第 1–3 大回合没有地图信物，
        // 所以每名 AI 落下第 2 枚棋子时 2 件已全部挂出。样本：link:1、4 名 Easy、--carry-in 2、12 个小回合。
        Siege.Sim.Config.RunConfig config = SimFixtures.Config(turnLimit: 12) with { CarryIn = 2 };
        Siege.Sim.Running.MatchSession session = Siege.Sim.Running.MatchSession.Create(config, 9);
        session.Run();
        MatchFlow match = session.Match;

        int attachedPlayers = 0;
        foreach (PlayerId p in match.Players)
        {
            int placed = match.Board.GroupsOf(p).Sum(g => g.Size);
            if (placed >= 2)
            {
                Assert.Empty(match.PendingCarriedOf(p));
                Assert.Equal(2, match.Board.RelicsOnBoard().Count(r => r.Token.Origin == p));
                attachedPlayers++;
            }
        }

        Assert.True(attachedPlayers >= 2, $"样本口径：至少两名 AI 落了 ≥ 2 枚（实际 {attachedPlayers}）");
    }

    /// <summary>结算的逐项比较（<see cref="RelicMatchSettlement"/> 是 record，但内含不可变集合，默认相等按引用）。</summary>
    private sealed class SettlementComparer : IEqualityComparer<RelicMatchSettlement>
    {
        internal static readonly SettlementComparer Instance = new();

        public bool Equals(RelicMatchSettlement? x, RelicMatchSettlement? y) =>
            x is not null && y is not null && x.Players.SequenceEqual(y.Players) && x.Vanished.SequenceEqual(y.Vanished);

        public int GetHashCode(RelicMatchSettlement obj) => obj.Players.Count;
    }
}
