using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Core.Tests.PieceRelics;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// relic-redesign 段 C1 遥测（match-telemetry 第 6 / 7 项的信物口径，prd.md AC8）：刷新预告 / 落位、拾取、提走掉落、带入挂出、R17 掉落六种流程事件进对局日志（带格与件数、可数）；
/// 日志棋串条目补记挂着的信物（种类 / 件数）并往返；小样本（≤ 20 局）里 AI 有拾取行为。
/// 变异验证（C1 实跑，均已恢复）：MT1-1 <c>MatchFlow.RecordRelicPickupsAndDrops</c> 不发拾取事件 → 红 2（拾取与掉落事件进日志、小样本AI有拾取行为）；
/// MT1-2 <c>MatchSession.CopyFlowEvents</c> 的信物 case 删掉（回到 default 吞掉）→ 红 3；MT1-3 <c>RelicCountsOf</c> 恒 null → 红 2（棋串条目、各棋子势力占比Tests 的样本下界）。
/// </summary>
public class 信物遥测Tests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly PlayerId P0 = TestMaps.P0;
    private static readonly PlayerId P1 = TestMaps.P1;

    /// <summary>
    /// 脚本局（不依赖 AI 走法）：平坦 8×8 载体、四人同锁棋盘 0；开局 P0 E6 / F5 / F7 围住 P1 的 F6（F6 挂战鼓，P1 持有），E3 空格上有一件地图百变。
    /// P0 第 1 大回合落 G6（提走 F6 → 战鼓掉落）与 E3（拾取百变），其余一律 Pass；截到第 8 个小回合。
    /// </summary>
    private static (MatchFlow Match, MatchSession Session) PickupAndDropSession()
    {
        MatchFlow match = LinkModeFixtures.PlainStarted(options: MatchOptions.Immediate with { ContentSet = ContentSet.V1 })
            .Stones(P0, "E6", "F5", "F7").Stones(P1, "F6", "C3");
        match.Board.AttachRelic(TestMaps.At("F6"), RelicToken.Spawned(0, RelicKind.WarDrum));
        match.Board.AttachRelic(TestMaps.At("E3"), RelicToken.Spawned(1, RelicKind.Chameleon));
        match.Debug.Recalculate();
        match.Debug.SeedHand(P0, (PieceType.Basic, 2), (PieceType.Synergy, 1));
        MatchSession session = MatchSession.ForMatch(match, LinkSimFixtures.Config(turnLimit: 8) with { ContentSet = match.ContentSet });
        session.SetController(P0, new LinkSimFixtures.ScriptedSeat(match, new Dictionary<int, (Coord, PieceType, TerrainEdit?)[]>
        {
            [1] = [(TestMaps.At("G6"), PieceType.Basic, null), (TestMaps.At("E3"), PieceType.Synergy, null)],
        }));
        foreach (PlayerId p in new[] { P1, TestMaps.P2, TestMaps.P3 })
        {
            session.SetController(p, new LinkSimFixtures.PassController());
        }

        return (match, session);
    }

    [Fact]
    public void 拾取与掉落事件进日志()
    {
        // 拾取：E3 的百变随落子挂到新子上（R4，盘面零操作）→ RelicsPickedUp，Coords = [E3]、Count 1、Player 0；
        // 掉落：提走 F6 后战鼓留在原格成为地图信物 → RelicsDropped，Coords = [F6]、Count 1、Player 0（提子者）。两者都在 Core 的流程事件里，日志只复制、不自己比对盘面。
        (MatchFlow match, MatchSession session) = PickupAndDropSession();
        MatchLog log = session.Run();

        Assert.Equal(1, match.CountEvents(FlowEventKind.RelicsPickedUp));
        Assert.Equal(1, match.CountEvents(FlowEventKind.RelicsDropped));
        LogEvent picked = Assert.Single(log.EventsOf(LogEventType.RelicsPickedUp));
        int p0Turn = log.Turns.First(t => t.Player == 0).Turn;
        Assert.Equal((0, 1, p0Turn), (picked.Player, picked.MajorRound, picked.Turn));
        Assert.Equal(["E3"], picked.Coords);
        Assert.Equal(BigInteger.One, picked.Values!["Count"]);
        Assert.Contains("E3#1:Chameleon", picked.Detail, StringComparison.Ordinal);
        LogEvent dropped = Assert.Single(log.EventsOf(LogEventType.RelicsDropped));
        Assert.Equal(0, dropped.Player);
        Assert.Equal(["F6"], dropped.Coords);
        Assert.Equal(BigInteger.One, dropped.Values!["Count"]);
        Assert.Contains("F6#0:WarDrum<P1", dropped.Detail, StringComparison.Ordinal);

        // 盘面核对：F6 空格、信物仍在；E3 的百变挂在 P0 的协同子上。
        Assert.Null(match.Board[TestMaps.At("F6")].Occupant);
        Assert.Equal(RelicKind.WarDrum, match.Board.RelicAt(TestMaps.At("F6"))!.Value.Kind);
        Assert.Equal(P0, match.Board[TestMaps.At("E3")].Occupant!.Value.Owner);

        // 往返：事件逐项解析回来。
        MatchLog parsed = MatchLog.Parse(log.DeterministicText());
        Assert.Equal(["E3"], Assert.Single(parsed.EventsOf(LogEventType.RelicsPickedUp)).Coords);
        Assert.Equal(["F6"], Assert.Single(parsed.EventsOf(LogEventType.RelicsDropped)).Coords);
    }

    [Fact]
    public void 棋串条目记信物种类与件数并往返()
    {
        // match-telemetry 第 7 项补记：拾取百变后 P0 的 E3 孤子条目 RelicCounts = {Chameleon: 1}；提子串 E6 / F5 / F7 / G6 没有信物 → 不写该项（null，JSON 整项省略）。
        // 读回：缺字段按 0（RelicCountOf）。写入端从快照盘面按棋子坐标逐枚数（RelicCountsOf），不读 Core 以外的任何状态。
        (MatchFlow match, MatchSession session) = PickupAndDropSession();
        MatchLog log = session.Run();
        TurnSnapshot turn = log.Turns.First(t => t.Player == 0);
        PlayerEntry p0 = turn.PlayersState.Single(p => p.Player == 0);
        GroupEntry e3 = p0.Groups.Single(g => g.Stones.SequenceEqual(["E3"]));
        GroupEntry wall = p0.Groups.Single(g => g.Stones.Contains("G6"));

        Assert.Equal(new Dictionary<string, int> { ["Chameleon"] = 1 }, e3.RelicCounts);
        Assert.Null(wall.RelicCounts);
        Assert.Equal(1, e3.RelicCountOf(RelicKind.Chameleon));
        Assert.Equal(0, wall.RelicCountOf(RelicKind.Chameleon));
        // 百变：位置加值 +2 进 Base 之外的位置项；协同子"其他类型数"多算 1 种（孤子没有其他类型 → 1 × 1 × 2 = 2）。
        Assert.Equal((1, 2), (e3.Base, e3.SynergyBonus));

        string text = log.DeterministicText();
        Assert.Contains("\"RelicCounts\":{\"Chameleon\":1}", text, StringComparison.Ordinal);
        MatchLog parsed = MatchLog.Parse(text);
        PlayerEntry readP0 = parsed.Turns.First(t => t.Player == 0).PlayersState.Single(p => p.Player == 0);
        Assert.Equal(1, readP0.Groups.Single(g => g.Stones.SequenceEqual(["E3"])).RelicCountOf(RelicKind.Chameleon));
        Assert.Null(readP0.Groups.Single(g => g.Stones.Contains("G6")).RelicCounts);

        // 真实盘面走写入函数：挂两件同种 → 件数 2。
        GameBoard board = TestMaps.Blank(size: 9).Place("B2", TestMaps.P0, PieceType.Basic).Place("C2", TestMaps.P0, PieceType.Basic);
        board.AttachRelic(TestMaps.At("B2"), RelicToken.Spawned(7, RelicKind.Command));
        board.AttachRelic(TestMaps.At("C2"), RelicToken.Spawned(8, RelicKind.Command));
        Siege.Core.Scoring.GroupPower group = Assert.Single(Siege.Core.Scoring.PowerCalculator.Compute(board).Of(TestMaps.P0).Groups);
        Assert.Equal(new Dictionary<string, int> { ["Command"] = 2 }, MatchSession.RelicCountsOf(board, group));
        Assert.Null(MatchSession.RelicCountsOf(TestMaps.Blank(size: 9).Place("B2", TestMaps.P0, PieceType.Basic), group));
    }

    [Fact]
    public void 带入挂出与R17掉落事件进日志()
    {
        // 带入带出开启、P0 带 2 件（战鼓保险、号令非保险）：P0 第 1 大回合落 E5 挂号令（下标 1）→ CarriedRelicsAttached，Coords = [E5]、Count 1；
        // 随后 P0 在小回合边界弃赛（SimFixtures.RankedMatch 同一做法）→ 保险件不掉、非保险件已挂出 → 没有 CarriedRelicsDropped。
        MatchFlow match = LinkModeFixtures.PlainStarted(options: CarryWiringFixtures.On((0, CarryWiringFixtures.Loadout((RelicKind.WarDrum, true), (RelicKind.Command, false)))) with { ContentSet = ContentSet.V1 });
        match.Debug.SeedHand(P0, (PieceType.Basic, 2));
        MatchSession session = MatchSession.ForMatch(match, LinkSimFixtures.Config(turnLimit: 8) with { ContentSet = match.ContentSet, CarryIn = 2 });
        session.SetController(P0, new AttachSeat(TestMaps.At("E5"), attach: 1));
        foreach (PlayerId p in new[] { P1, TestMaps.P2, TestMaps.P3 })
        {
            session.SetController(p, new LinkSimFixtures.PassController());
        }

        // 跑到 P0 行动过（挂出事件已发）为止，再在小回合边界弃赛。
        while (match.CountEvents(FlowEventKind.CarriedRelicsAttached) == 0)
        {
            Assert.True(session.RunTurn());
        }

        match.Resign(P0);
        MatchLog log = session.Run();

        LogEvent attached = Assert.Single(log.EventsOf(LogEventType.CarriedRelicsAttached));
        Assert.Equal((0, BigInteger.One), (attached.Player, attached.Values!["Count"]));
        Assert.Equal(["E5"], attached.Coords);
        Assert.Empty(log.EventsOf(LogEventType.CarriedRelicsDropped));

        // 第二局：P0 带 1 件非保险、不挂出就在小回合边界弃赛 → R17 掉落进日志。平坦载体只有 5 块出生棋盘、没有出生区以外的可落子格，
        // 掉落件无处可落而消失（R17「候选格不够时多出的件消失」）：Count 0、Lost 1、Coords 空，盘面上没有信物——Lost 这一支由此钉住。
        MatchFlow second = LinkModeFixtures.PlainStarted(options: CarryWiringFixtures.On((0, CarryWiringFixtures.Loadout((RelicKind.Command, false)))) with { ContentSet = ContentSet.V1 });
        MatchSession secondSession = MatchSession.ForMatch(second, LinkSimFixtures.Config(turnLimit: 8) with { ContentSet = second.ContentSet, CarryIn = 1 });
        foreach (PlayerId p in TestMaps.All)
        {
            secondSession.SetController(p, new LinkSimFixtures.PassController());
        }

        Assert.True(secondSession.RunTurn());   // 任一人行动一次（全员 Pass），P0 仍未挂出
        second.Resign(P0);
        MatchLog secondLog = secondSession.Run();

        LogEvent dropped = Assert.Single(secondLog.EventsOf(LogEventType.CarriedRelicsDropped));
        Assert.Equal((0, BigInteger.Zero, BigInteger.One), (dropped.Player, dropped.Values!["Count"], dropped.Values["Lost"]));
        Assert.Empty(dropped.Coords!);
        Assert.False(second.Board.HasRelics);
        Assert.Contains("1 件无处可落、消失", dropped.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void 小样本AI有拾取行为()
    {
        // AC8（≤ 20 局、不跑 200 局）：2 人局 ≤ 10 局（link:1:2 生成图、2 名标准难度、16 个小回合 = 8 个大回合）+ 4 人局 ≤ 5 局（link:1:4 生成图、4 名标准难度、32 个小回合 = 8 个大回合），
        // 第 4 / 6 / 8 大回合各落位一批。断言合计拾取次数 > 0，且每局都有落位事件可数（预告件数 ≥ 落位件数，被占格跳过只少不多；落位格数 = Count）。
        // 样本口径：标准难度（信物维参与评价；简单难度不算信物维，只会顺带拾取，不能证明估值生效）；生成图才有出生平台以外的刷新格（平坦手写载体整局刷不出信物，不能作样本）。
        // 局数小、只证"会拾取"，不作任何比例结论。实测（C1）：2 人 4 局 + 4 人 2 局约 1 分钟，拾取 9 次。
        var logs = new List<MatchLog>();
        for (ulong seed = 1; seed <= 4; seed++)
        {
            RunConfig two = SimFixtures.Config(seedStart: seed, turnLimit: 16, difficulty: AiDifficulty.Standard, players: 2) with { MapId = "link:1:2" };
            logs.Add(MatchSession.Create(two, seed).Run());
        }

        for (ulong seed = 1; seed <= 2; seed++)
        {
            RunConfig four = SimFixtures.Config(seedStart: seed, turnLimit: 32, difficulty: AiDifficulty.Standard) with { MapId = "link:1:4" };
            logs.Add(MatchSession.Create(four, seed).Run());
        }

        BigInteger pickups = logs.SelectMany(l => l.EventsOf(LogEventType.RelicsPickedUp)).Aggregate(BigInteger.Zero, (sum, e) => sum + e.Values!["Count"]);
        int forecasts = logs.Sum(l => l.EventsOf(LogEventType.RelicsForecast).Count);
        int landings = logs.Sum(l => l.EventsOf(LogEventType.RelicsLanded).Count);
        foreach (MatchLog log in logs)
        {
            Assert.False(log.IsFailed, log.Failure?.ToString());
            Assert.True(log.EventsOf(LogEventType.RelicsLanded).Any(e => e.Values!["Count"] > 0), $"种子 {log.Seed} 整局没有信物落位");
            foreach (LogEvent landed in log.EventsOf(LogEventType.RelicsLanded))
            {
                Assert.True(landed.Values!["Count"] <= landed.Values["Forecast"], landed.Detail);
                Assert.Equal((int)landed.Values["Count"], landed.Coords!.Count);
            }
        }

        output.WriteLine($"AC8 小样本：{logs.Count} 局（2 人 4 局 + 4 人 2 局），AI 拾取 {pickups} 次，预告 {forecasts} 次、落位 {landings} 次；逐局拾取 {string.Join("/", logs.Select(l => l.EventsOf(LogEventType.RelicsPickedUp).Aggregate(BigInteger.Zero, (sum, e) => sum + e.Values!["Count"])))}");
        Assert.True(pickups > 0, $"{logs.Count} 局里 AI 拾取 {pickups} 次（预告 {forecasts}、落位 {landings}）");
    }

    /// <summary>每次部署都落 <paramref name="cell"/> 并挂带入清单第 <paramref name="attach"/> 件（只用于第一个小回合）。</summary>
    private sealed class AttachSeat(Coord cell, int attach) : ITurnController
    {
        public void OrganizeHand(Siege.Core.Recruit.PlayerHandAccess hand, int overflow)
        {
        }

        public void Recruit(Siege.Core.Recruit.PlayerHandAccess hand, Siege.Core.Recruit.RecruitPanelView panel)
        {
        }

        public void Deploy(Siege.Core.Batch.StagedBatch batch, Func<Siege.Core.Batch.RehearsalResult> rehearse)
        {
            if (batch.Stage(cell, PieceType.Basic, null, attach) is { } failure)
            {
                throw new InvalidOperationException(failure.Message);
            }
        }

        public bool OnRejected(Siege.Core.Batch.StagedBatch batch, Siege.Core.Batch.BatchFailure failure) => false;
    }
}
