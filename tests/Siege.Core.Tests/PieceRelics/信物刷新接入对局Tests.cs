using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign 接线段 W1-2（design.md D2，R8 / R11 / R22 / R26）：刷新接入大回合流程——第 3 大回合开始预告、第 4 大回合开始落位（此后每 2 大回合一批）；
/// 预告与落位记录进对局状态、存档与公开视图；同种子同决策两局逐批相同。载体：<c>link:1:4</c> 生成图，四人各锁一块出生平台、每大回合各落 1 子（不触发战场结算）。
/// 变异验证（W1 实跑，只跑 PieceRelics 命名空间；MR-1 与 MS-2 / MD-1 同批、MR-2 / MR-3 与 MS-1 / MD-2 同批，同批变异互不相交）：MR-1 <c>StartMajorRoundRelics</c> 不落位 → 「第四大回合开始按预告落位」红（连同依赖落位的另 6 条）；
/// MR-2 存档不写 <c>RelicSpawn</c> 段的预告（只写已落位批次）→ 「存档往返含预告与落位」红；MR-3 预告的人数取在局人数 → 「n取开局人数」红。
/// </summary>
public class 信物刷新接入对局Tests
{
    private static readonly int[] FourZones = [0, 1, 2, 3];

    /// <summary>四人各锁一块平台，每大回合各落 1 子，推进到第 <paramref name="majorRound"/> 大回合开始（尚无人行动）。</summary>
    private static MatchFlow PlayedTo(int majorRound, GameSeed? seed = null)
    {
        MatchFlow match = Started(FourZones, seed);
        while (match.Phase == MatchPhase.InProgress && match.MajorRound < majorRound)
        {
            match.PlayRound(All);
        }

        Assert.Equal(MatchPhase.InProgress, match.Phase);   // 样本口径：没有因结算而终局
        Assert.Equal(majorRound, match.MajorRound);
        return match;
    }

    private static string Batches(MatchFlow match) =>
        string.Join("\n", match.RelicBatches.Select(b => b.ToString())) + $"\nnext={match.NextRelicId}";

    [Fact]
    public void 第一二大回合没有预告与信物()
    {
        // R8：开局不刷；第 3 大回合开始才预告第一批。
        MatchFlow match = PlayedTo(2);

        Assert.Empty(match.RelicBatches);
        Assert.False(match.Board.HasRelics);
        Assert.Null(match.PendingRelicForecast);
        Assert.Null(match.Publish().RelicForecast);
        Assert.Equal(0, match.CountEvents(FlowEventKind.RelicsForecast));
    }

    [Fact]
    public void 第三大回合开始预告下一批()
    {
        // D2 / R11：第 3 大回合开始时预告第 4 大回合落位的一批：⌈4/2⌉ = 2 件，位置在出生平台之外、离任何棋子切比雪夫距离 ≥ 2；
        // 与按同一时刻盘面、对局种子、开局人数 4、场上未拾取 0 件独立调用刷新器的结果逐项相同（接线没有换参数）。预告进公开快照，落位前盘面上没有信物。
        MatchFlow match = PlayedTo(3);

        RelicBatchRecord batch = Assert.Single(match.RelicBatches);
        Assert.False(batch.HasLanded);
        Assert.Equal((3, 4), (batch.Forecast.ForecastRound, batch.Forecast.LandingRound));
        Assert.Equal(2, batch.Forecast.Slots.Length);
        Assert.Equal(RelicSpawner.Forecast(match.Board, new BirthBoardArea(match.Map), match.Seed, 3, 4, 0), batch.Forecast);
        Assert.Equal(batch.Forecast, match.PendingRelicForecast);
        Assert.Equal(batch.Forecast, match.Publish().RelicForecast);
        Assert.False(match.Board.HasRelics);
        Assert.All(batch.Forecast.Slots, s =>
        {
            Assert.Null(match.Map.BirthBoardAt(s.Cell));
            Assert.All(match.Board.AllCoords().Where(c => match.Board[c].Occupant is not null), c => Assert.True(PieceRelicFixtures.Chebyshev(c, s.Cell) >= 2));
        });
        FlowEvent forecastEvent = Assert.Single(match.Events, e => e.Kind == FlowEventKind.RelicsForecast);
        Assert.Equal(3, forecastEvent.MajorRound);
    }

    [Fact]
    public void 第四大回合开始按预告落位()
    {
        // D2 / R8：第 4 大回合开始时按预告次序落位（预告格此刻仍空），编号从 0 起依次分配；地图刷出的信物无带入者、非保险。预告随之清空（第 4 大回合不是预告轮）。
        MatchFlow match = PlayedTo(4);

        RelicBatchRecord batch = Assert.Single(match.RelicBatches);
        Assert.True(batch.HasLanded);
        ImmutableArray<BoardRelic> landed = batch.Landed!.Value;
        Assert.Equal(batch.Forecast.Slots.Select(s => (s.Cell, s.Kind)), landed.Select(r => (r.Cell, r.Token.Kind)));   // 样本口径：两格都没被占，全部落位
        Assert.Equal([0, 1], landed.Select(r => r.Token.Id));
        Assert.All(landed, r =>
        {
            Assert.Equal(r.Token, match.Board.RelicAt(r.Cell));
            Assert.Null(r.Token.Origin);
            Assert.False(r.Token.Insured);
            Assert.Null(match.Board[r.Cell].Occupant);
        });
        Assert.Equal(2, match.NextRelicId);
        Assert.Null(match.PendingRelicForecast);
        Assert.Null(match.Publish().RelicForecast);
        Assert.Contains('#', match.Publish().BoardSerialized);
        Assert.Equal(4, Assert.Single(match.Events, e => e.Kind == FlowEventKind.RelicsLanded).MajorRound);
    }

    [Fact]
    public void 落位时被占的预告格跳过()
    {
        // D2：落位时若预告格已被占，跳过该件、不重抽（落位只少不多）；其余件照常落位，编号连续。
        MatchFlow match = PlayedTo(3);
        RelicSpawnSlot taken = match.PendingRelicForecast!.Slots[0];
        match.Place(P3, taken.Cell);

        match.PlayRound(All);

        RelicBatchRecord batch = Assert.Single(match.RelicBatches);
        ImmutableArray<BoardRelic> landed = batch.Landed!.Value;
        Assert.Equal(batch.Forecast.Slots.Length - 1, landed.Length);
        Assert.DoesNotContain(landed, r => r.Cell == taken.Cell);
        Assert.Null(match.Board.RelicAt(taken.Cell));
        Assert.Equal([0], landed.Select(r => r.Token.Id));
        Assert.Equal(1, match.NextRelicId);
    }

    [Fact]
    public void 第五大回合预告第二批()
    {
        // D2：第 5 大回合开始预告第 6 大回合的一批；场上未拾取件数取此刻盘面（第一批 2 件仍在空格上）→ min(2, 6 − 2) = 2 件。
        MatchFlow match = PlayedTo(5);

        Assert.Equal(2, match.RelicBatches.Count);
        RelicForecast second = match.PendingRelicForecast!;
        Assert.Equal(6, second.LandingRound);
        Assert.Equal(2, RelicSpawner.UnclaimedMapRelics(match.Board));
        Assert.Equal(RelicSpawner.Forecast(match.Board, new BirthBoardArea(match.Map), match.Seed, 5, 4, 2), second);
        Assert.DoesNotContain(second.Slots, s => match.Board.RelicAt(s.Cell) is not null);
    }

    [Fact]
    public void n取开局人数()
    {
        // D2：n 取开局参赛人数，不随出局 / 弃赛变化（保持可复现）。P3 在第 2 大回合弃赛；场上先摆 5 件未拾取的地图信物（编号 100 起，不与对局分配的编号相撞），
        // 第 3 大回合的预告按 4 人：min(⌈4/2⌉, 6 − 5) = 1 件；若按在局 3 人则是 min(2, 5 − 5) = 0 件——两种口径在这里可分。
        MatchFlow match = Started(FourZones);
        match.PlayRound(All);
        match.Resign(P3);
        ImmutableArray<Coord> spots = RelicSpawner.Candidates(match.Board, new BirthBoardArea(match.Map));
        for (int k = 0; k < 5; k++)
        {
            match.Board.AttachRelic(spots[k * 7], RelicToken.Spawned(100 + k, RelicKind.WarDrum));
        }

        match.PlayRound(All);

        Assert.Equal(3, match.MajorRound);
        RelicForecast forecast = match.PendingRelicForecast!;
        Assert.Single(forecast.Slots);
        Assert.Equal(RelicSpawner.Forecast(match.Board, new BirthBoardArea(match.Map), match.Seed, 3, 4, 5), forecast);
        Assert.True(RelicSpawner.Forecast(match.Board, new BirthBoardArea(match.Map), match.Seed, 3, 3, 5)!.IsEmpty);
    }

    [Fact]
    public void 同种子同决策两局逐批相同()
    {
        // AC2 / determinism.md：同种子、同地图、同决策的两局，每一批预告与落位逐项相同、盘面（含信物层）逐字节相同；换种子则刷新不同（反面：确由种子驱动）。
        MatchFlow a = PlayedTo(7);
        MatchFlow b = PlayedTo(7);
        MatchFlow other = PlayedTo(7, new GameSeed(0xA5A5_0002UL));

        Assert.Equal(3, a.RelicBatches.Count);   // 样本口径：第 3 / 5 / 7 大回合各预告一批，其中前两批已在第 4 / 6 大回合落位
        Assert.Equal(2, a.RelicBatches.Count(r => r.HasLanded));
        Assert.Equal(a.RelicBatches, b.RelicBatches);
        Assert.Equal(Batches(a), Batches(b));
        Assert.Equal(a.Board.Serialize(), b.Board.Serialize());
        Assert.NotEqual(Batches(a), Batches(other));
    }

    [Fact]
    public void 存档往返含预告与落位()
    {
        // R26：存档格式变化——新增 RelicSpawn 段（预告与落位记录、编号游标），信物层随盘面序列化的 # 段。第 5 大回合开始存档：第一批已落位、第二批只有预告。
        // 两条腿（testing.md「存档→恢复→再存档逐字节相等抓不到漏字段」）：逐字段比对活对象与恢复对象；恢复后续跑同样的决策到第 6 大回合（第二批落位），再比一次。
        MatchFlow match = PlayedTo(5);
        string json = match.Serialize();
        Assert.Contains("\"RelicSpawn\"", json, StringComparison.Ordinal);

        MatchFlow restored = MatchFlow.Restore(MapCatalog.Resolve(MatchFlow.SavedMapId(json)), json);

        Assert.Equal(match.RelicBatches, restored.RelicBatches);
        Assert.Equal(match.NextRelicId, restored.NextRelicId);
        Assert.Equal(match.PendingRelicForecast, restored.PendingRelicForecast);
        Assert.Equal(match.Board.RelicsOnBoard(), restored.Board.RelicsOnBoard());
        Assert.Equal(json, restored.Serialize());

        match.PlayRound(All);
        restored.PlayRound(All);
        Assert.Equal(6, restored.MajorRound);
        Assert.True(restored.RelicBatches[^1].HasLanded);
        Assert.Equal(Batches(match), Batches(restored));
        Assert.Equal(match.Serialize(), restored.Serialize());
    }

    [Fact]
    public void 旧信物账本存档与缺刷新段的存档被拒()
    {
        // R26：旧存档不兼容——含旧信物账本 Relics 字段的存档点名拒绝；缺 RelicSpawn 段的存档同样拒绝（MUST NOT 静默按"没有刷新"恢复）。
        MatchFlow match = PlayedTo(3);
        MapData map = MapCatalog.Resolve(MatchFlow.SavedMapId(match.Serialize()));

        JsonObject legacy = JsonNode.Parse(match.Serialize())!.AsObject();
        legacy["Relics"] = new JsonObject { ["Revealed"] = new JsonArray() };
        FormatException old = Assert.Throws<FormatException>(() => MatchFlow.Restore(map, legacy.ToJsonString()));
        Assert.Contains("旧信物账本", old.Message, StringComparison.Ordinal);

        JsonObject missing = JsonNode.Parse(match.Serialize())!.AsObject();
        Assert.True(missing.Remove("RelicSpawn"));
        FormatException absent = Assert.Throws<FormatException>(() => MatchFlow.Restore(map, missing.ToJsonString()));
        Assert.Contains("RelicSpawn", absent.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 座位视图公开预告与信物()
    {
        // R22：地图信物、预告、挂出的信物全公开——按座位视图（终端 / 正式 AI）与按座位投影都给出预告；按座位视图列出盘面上的每件信物（空格上持有者为空）。
        MatchFlow match = PlayedTo(5);

        SeatPublicView seat = match.PublishFor(P0);
        Assert.Equal(match.PendingRelicForecast, seat.RelicForecast);
        Assert.Equal(match.Board.RelicsOnBoard().Select(r => r.Cell.ToNotation()), seat.Relics.Select(r => r.Coord));
        Assert.All(seat.Relics, r => Assert.Null(r.Holder));
        Assert.Equal(match.PendingRelicForecast, match.PublishSeatProjection(P0).RelicForecast);
    }

    [Fact]
    public void 落子到地图信物上即拾取并计入势力()
    {
        // R4 接线：第 4 大回合落位后，当前玩家把子落在一件地图信物上 → 信物挂在新子上（持有者即该子主人），正式结算的势力含其效果，持有件数 +1。
        MatchFlow match = PlayedTo(4);
        BoardRelic target = match.Board.RelicsOnBoard().First(r => match.LegalRangeFor(match.CurrentPlayer!.Value).Contains(r.Cell));
        PlayerId mover = match.CurrentPlayer!.Value;

        match.PlayTurn(target.Cell.ToNotation());

        Assert.Equal(mover, match.Board[target.Cell].Occupant?.Owner);
        Assert.Equal(target.Token, match.Board.RelicAt(target.Cell));
        Assert.Equal(1, match.Board.HeldRelicCount(mover));
        Assert.Equal(1, RelicSpawner.UnclaimedMapRelics(match.Board));
    }

    [Fact]
    public void 战场结算的比较链读持有信物数()
    {
        // R24：战场结算（及终局名次）的第二比较项"持有信物数"= 挂在该玩家棋子上的件数（GameBoard.HeldRelicCount 唯一实现），不再是"控制信物数"。
        MatchFlow match = PlainStarted().AtRound(5).Stones(P1, "C3", "C4").Stones(P2, "K10");
        match.Board.AttachRelic(TestMaps.At("C3"), RelicToken.Spawned(0, RelicKind.Watchtower));
        match.Board.AttachRelic(TestMaps.At("C4"), RelicToken.Carried(1, RelicKind.Banner, P2, insured: true));
        match.Board.AttachRelic(TestMaps.At("H8"), RelicToken.Spawned(2, RelicKind.WarDrum));   // 空格上的地图信物：谁都不持有
        match.Debug.Recalculate();

        match.Debug.SettleBattlefield(match.BattlefieldOf(P0), P0);

        BattlefieldSettlementRecord record = match.Settlements[^1];
        Assert.Equal(2, record.Entries.Single(e => e.Player == P1).HeldRelics);   // 别人带入的保险件挂在 P1 子上，持有者仍是 P1（D1）
        Assert.Equal(0, record.Entries.Single(e => e.Player == P2).HeldRelics);
    }
}
