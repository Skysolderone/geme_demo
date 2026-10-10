using System.Collections.Immutable;
using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Sim.Config;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/ai-decision —— Requirement: 正式对战 AI 的信息边界（第一版例外：全知集中一处、可关；候选不越战场）。
/// 实现裁决 C4，tasks 3.3。
/// </summary>
public class 正式对战AI的信息边界_链接模式Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 候选不越战场(bool seatVision)
    {
        // 规格 Scenario「候选不越战场」：正式 AI 在双人战场 1 内决策、战场 4 尚未与之链接 → 候选落点全部位于战场 1 内（全知与关闭全知都如此）。
        // 开局 P0、P1 同在棋盘 1（内部 0），P2、P3 在棋盘 4、5；第 3 大回合结束时 4、5 链接，棋盘 1 不参与配对。跑到第 4 大回合，看 P0 / P1 的决策。
        // 变异 MC-19：PublicRules.LegalRange 的链接模式分支改为全图可落子格 → 候选越出战场，本测试红。
        MatchFlow match = Started([0, 0, 3, 4], new ScriptedPairingSource());
        AiSearchConfig config = AiSearchConfig.Easy with { SeatVision = seatVision };
        var runner = new MatchRunner(match);
        // 样本口径（P0、P1 到第 4 大回合仍在局）依赖 AI 实际走法：权重写死，不跟随默认值（testing.md「依赖 AI 实际怎么走的断言要把权重写死」）。
        var ais = match.Players.ToDictionary(p => p, p => HeuristicAi.Create(match, p, AiDifficulty.Easy, SimFixtures.PreCalibrationWeights, config));
        foreach ((PlayerId p, HeuristicTurnController ai) in ais)
        {
            runner.SetController(p, ai);
        }

        while (match.Phase == MatchPhase.InProgress && match.MajorRound < 4)
        {
            runner.RunTurn();
        }

        // 样本口径：棋盘 1 的双人战场仍未结算、两人都在局，且没有与 4、5 的合并战场链接。
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        string why = string.Join(" | ", match.PlayerStates.Select(s => $"{s.Player}:{s.Status}:{s.EliminationReason}:{s.EliminatedInMajorRound}"))
            + " || " + string.Join(" | ", match.Settlements.Select(s => $"{s.MajorRound}:{s.Battlefield}:{s.Reason}:{s.Winner}"));
        Assert.True(match.StateOf(P0).Status == PlayerStatus.Active && match.StateOf(P1).Status == PlayerStatus.Active, why);
        Assert.Single(match.Links);
        Assert.NotEqual(match.BattlefieldOf(P0), match.BattlefieldOf(P2));

        int checkedTurns = 0;
        while (match.Phase == MatchPhase.InProgress && match.MajorRound == 4)
        {
            PlayerId current = match.CurrentPlayer!.Value;
            ImmutableHashSet<Coord> field = [.. match.Board.Battlefields.ById(match.BattlefieldOf(current))!.Cells];
            runner.RunTurn();
            if (current == P0 || current == P1)
            {
                ImmutableArray<Coord> candidates = ais[current].LastCandidateCells;
                Assert.NotEmpty(candidates);   // 样本口径：确实做了部署决策
                Assert.All(candidates, c => Assert.Contains(c, field));
                Assert.All(ais[current].LastChoice?.Placements ?? [], p => Assert.Contains(p.Coord, field));
                checkedTurns++;
            }
        }

        Assert.Equal(2, checkedTurns);
    }

    [Fact]
    public void 全知例外集中可关()
    {
        // 规格 Scenario「全知例外集中可关」：关闭 AI 全知开关 → AI 的读取接口对其他战场返回"未展开"（没有棋子、信物未揭示、势力与手牌类型不可见），
        // 评价代码不需修改即可运行。缺省为全知（第一版例外）。
        // 变异 MC-20：HeuristicAi.VisionFor 恒给 match.Publish → 关闭全知后 AI 仍读到他人战场的棋子，本测试红；
        // MC-21：PublishSeatProjection 不重算势力（沿用全知势力快照）→ 他人势力断言红。
        MatchFlow match = Started([0, 0, 3, 4]);
        Coord mine = BirthCell(Map, 0, 1, 1);
        Coord theirs = BirthCell(Map, 3, 2, 2);
        match.Place(P0, mine).Place(P2, theirs, BirthCell(Map, 3, 2, 3));
        match.Debug.SeedHand(P2, (PieceType.Fortress, 1));

        MatchPublicView full = match.Publish();
        MatchPublicView seat = match.PublishSeatProjection(P0);
        Assert.NotNull(full.Board[theirs].Occupant);   // 样本口径：他人战场上确实有棋子
        Assert.True(full.Power!.Of(P2).Total > 0);

        // 他人战场：没有棋子、势力为 0（只计本战场）、手牌类型不可见、信物未揭示且无人控制。
        Assert.Null(seat.Board[theirs].Occupant);
        Assert.Empty(seat.Board.AllCoords().Where(c => seat.Board[c].Occupant is { } o && o.Owner != P0 && o.Owner != P1));
        Assert.Equal(0, seat.Power!.Of(P2).Total);
        Assert.Empty(seat.Hands.Single(h => h.Player == P2).Types);
        Assert.NotEmpty(full.Hands.Single(h => h.Player == P2).Types);
        ImmutableHashSet<Coord> field = [.. match.Board.Battlefields.ById(match.BattlefieldOf(P0))!.Cells];
        Assert.All(seat.Relics.Where(r => !field.Contains(r.Coord)), r => Assert.False(r.IsRevealed));

        // 本战场：与全知视图逐格相同，自己的势力不变；地形与全局项不过滤。
        Assert.Equal(full.Board[mine].Occupant, seat.Board[mine].Occupant);
        Assert.Equal(full.Power.Of(P0).Total, seat.Power.Of(P0).Total);
        Assert.Equal(full.Hands.Single(h => h.Player == P1).Types, seat.Hands.Single(h => h.Player == P1).Types);
        Assert.Equal(full.ActionOrder, seat.ActionOrder);
        Assert.Equal(full.Players, seat.Players);
        Assert.Equal(full.Board.Map, seat.Board.Map);

        // 分流只在 VisionFor：缺省（null / 预设）= 全知、预演结果原样；Seat = 投影视图 + 同口径的预演结果。
        Assert.NotNull(HeuristicAi.VisionFor(match, P0, null).Observe().Board[theirs].Occupant);
        Assert.Null(HeuristicAi.VisionFor(match, P0, null).Rehearsal);
        Assert.NotNull(HeuristicAi.VisionFor(match, P0, AiSearchConfig.Standard).Observe().Board[theirs].Occupant);
        AiVisionPorts closed = HeuristicAi.VisionFor(match, P0, AiSearchConfig.Standard with { SeatVision = true });
        Assert.Null(closed.Observe().Board[theirs].Occupant);
        var rehearsal = new Batch.RehearsalResult(true, false, null, [], match.Board.Clone());
        Assert.NotNull(rehearsal.ProjectedBoard![theirs].Occupant);
        Assert.Null(closed.Rehearsal!(rehearsal).ProjectedBoard![theirs].Occupant);
        Assert.Equal(match.Board[mine].Occupant, closed.Rehearsal(rehearsal).ProjectedBoard![mine].Occupant);
        Assert.NotNull(rehearsal.ProjectedBoard[theirs].Occupant);   // 投影在副本上做，不改原预演结果
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.False(AiSearchConfig.ForDifficulty(d).SeatVision));   // 四档预设都是全知

        // 旧模式：投影即全知视图（没有战场之分）。
        MatchFlow legacy = MatchFixtures.Started();
        Assert.Equal(legacy.Publish().BoardSerialized, legacy.PublishSeatProjection(MatchFixtures.P0).BoardSerialized);
    }

    [Fact]
    public void 关闭全知时评价代码照常运行()
    {
        // 关闭全知的 AI 走完整的决策（征募、候选、评价、确认）：四名 AI 都用按座位投影的视图跑一个大回合，不抛错、每人都落了子
        // （他人战场的棋子不会因"预演前看不到、预演后看得到"被评价成敌方势力变化而压到停手阈值以下）。
        // 变异 MC-24：VisionFor 的关闭分支不给预演口径（Rehearsal 为 null）→ 先手之后的 AI 全部 Pass，本测试红。
        MatchFlow match = Started([0, 0, 3, 4]);
        match.Place(P2, BirthCell(Map, 3, 2, 2));
        var runner = new MatchRunner(match);
        AiSearchConfig config = AiSearchConfig.Standard with { SeatVision = true };
        foreach (PlayerId p in match.Players)
        {
            // "每人都落了子"依赖 AI 实际走法：权重写死（同上）。
            runner.SetController(p, HeuristicAi.Create(match, p, AiDifficulty.Standard, SimFixtures.PreCalibrationWeights, config));
        }

        for (int i = 0; i < 4; i++)
        {
            PlayerId current = match.CurrentPlayer!.Value;
            int before = match.Board.AllCoords().Count(c => match.Board[c].Occupant?.Owner == current);
            runner.RunTurn();
            Assert.True(match.Board.AllCoords().Count(c => match.Board[c].Occupant?.Owner == current) > before, $"关闭全知的 AI {current} 一子未落。");
        }

        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 视野开关缺省不写出()
    {
        // 开关在搜索配置里，缺省（全知）不写出：旧配置与旧日志首部逐字节不变；关闭时写出并可往返。
        // 变异 MC-22：SeatVision 去掉 WhenWritingDefault → 缺省配置也写出 "SeatVision"，本测试红（同时会让带 Search 的既有首部黄金值红）。
        JsonSerializerOptions options = RunConfig.JsonOptions;
        string standard = JsonSerializer.Serialize(AiSearchConfig.Standard, options);
        Assert.DoesNotContain("SeatVision", standard, StringComparison.Ordinal);
        string seat = JsonSerializer.Serialize(AiSearchConfig.Standard with { SeatVision = true }, options);
        Assert.Contains("\"SeatVision\": true", seat, StringComparison.Ordinal);
        Assert.True(JsonSerializer.Deserialize<AiSearchConfig>(seat, options)!.SeatVision);
        Assert.False(JsonSerializer.Deserialize<AiSearchConfig>(standard, options)!.SeatVision);
    }

    [Fact]
    public void 全知例外只在一处分流()
    {
        // 规格："该例外 SHALL 在代码中集中于一处"。源码扫描 src/Siege.Core/Ai：
        // ① 视野开关只在 HeuristicAi.cs 读取（定义在 AiDifficulty.cs）——评价代码不读它；② 正式 AI 的 MatchFlow.Publish 委托只在 VisionFor 一处（调试旁路 DebugTurnController / MatchDebugView 除外）；
        // ③ 投影视图与预演口径只经 VisionFor 到达；预演口径只在 HeuristicTurnController 的部署入口套用。配样本口径下界与反面命中。
        string dir = Path.Combine(Determinism.随机子流隔离Tests.SourceRoot(), "src", "Siege.Core", "Ai");
        (string Name, string Text)[] files = [.. Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];
        Assert.True(files.Length >= 10, $"样本口径：只扫到 {files.Length} 个 AI 源文件。");

        Assert.Equal(["AiDifficulty.cs", "HeuristicAi.cs"], files.Where(f => f.Text.Contains("SeatVision", StringComparison.Ordinal)).Select(f => f.Name).Order());
        Assert.Equal(["HeuristicAi.cs"], files.Where(f => f.Text.Contains("PublishSeatProjection(", StringComparison.Ordinal)).Select(f => f.Name));
        string heuristic = files.Single(f => f.Name == "HeuristicAi.cs").Text;
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(heuristic, @"match\.Publish\b(?!SeatProjection)"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(heuristic, @"VisionFor\(match, player, config\)").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(heuristic, @"RehearsalView = vision\.Rehearsal").Count);
        Assert.Equal(["HeuristicAi.cs"], files.Where(f => f.Text.Contains("ProjectRehearsal(", StringComparison.Ordinal)).Select(f => f.Name));
        Assert.Equal(["HeuristicAi.cs", "HeuristicTurnController.cs"], files.Where(f => f.Text.Contains("RehearsalView", StringComparison.Ordinal)).Select(f => f.Name).Order());
        Assert.Equal(["DebugTurnController.cs", "HeuristicAi.cs", "MatchDebugView.cs"],
            files.Where(f => System.Text.RegularExpressions.Regex.IsMatch(f.Text, @"match\.Publish\b(?!SeatProjection)")).Select(f => f.Name).Order());
    }
}
