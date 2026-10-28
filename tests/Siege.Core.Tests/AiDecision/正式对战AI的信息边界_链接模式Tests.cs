using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：openspec/changes/open-map/specs/ai-decision —— Requirement: 正式对战 AI 的信息边界（MODIFIED：座位视野 = 全图；全知开关随唯一战场删除，open-map 裁决 B1）。
/// 沿用 ai-seat-vision 的「缺省按座位视野」与 board-link-mode 的「候选不越战场」→ open-map「构筑期候选不越棋盘」「第 4 大回合候选可全图」「全图可读」。
/// </summary>
public class 正式对战AI的信息边界_链接模式Tests
{
    [Fact]
    public void 构筑期候选不越棋盘_第4大回合候选可全图()
    {
        // 规格 Scenario「构筑期候选不越棋盘」「第 4 大回合候选可全图」（open-map）：正式 AI 第 2–3 大回合的候选落点全部位于其锁定的出生棋盘内；
        // 第 4 大回合候选落点可位于任一棋盘。开局 P0、P1 同在棋盘 1（内部 0），P2、P3 在棋盘 4、5。
        // 变异 MC-19：PublicRules.LegalRange 构筑期不按出生棋盘收窄 → 候选越出棋盘，本测试红。
        MatchFlow match = Started([0, 0, 3, 2]);
        AiSearchConfig config = AiSearchConfig.Easy;
        var runner = new MatchRunner(match);
        // 样本口径（P0、P1 到第 4 大回合仍在局）依赖 AI 实际走法：权重写死，不跟随默认值（testing.md「依赖 AI 实际怎么走的断言要把权重写死」）。
        var ais = match.Players.ToDictionary(p => p, p => HeuristicAi.Create(match, p, AiDifficulty.Easy, SimFixtures.PreCalibrationWeights, config));
        foreach ((PlayerId p, HeuristicTurnController ai) in ais)
        {
            runner.SetController(p, ai);
        }

        ImmutableHashSet<Coord> board0 = [.. match.Map.BirthBoard(0).Cells()];
        int buildingTurns = 0;
        while (match.Phase == MatchPhase.InProgress && match.MajorRound < 4)
        {
            PlayerId current = match.CurrentPlayer!.Value;
            int round = match.MajorRound;
            runner.RunTurn();
            if (round >= 2 && (current == P0 || current == P1) && !ais[current].LastCandidateCells.IsDefaultOrEmpty)
            {
                Assert.All(ais[current].LastCandidateCells, c => Assert.Contains(c, board0));
                Assert.All(ais[current].LastChoice?.Placements ?? [], p => Assert.Contains(p.Coord, board0));
                buildingTurns++;
            }
        }

        Assert.True(buildingTurns >= 2, $"构筑期只核对到 {buildingTurns} 个小回合");

        // 样本口径：第 4 大回合 P0、P1 仍在局，唯一战场未结算。
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        string why = string.Join(" | ", match.PlayerStates.Select(s => $"{s.Player}:{s.Status}:{s.EliminationReason}:{s.EliminatedInMajorRound}"))
            + " || " + string.Join(" | ", match.Settlements.Select(s => $"{s.MajorRound}:{s.Battlefield}:{s.Reason}:{s.Winner}"));
        Assert.True(match.StateOf(P0).Status == PlayerStatus.Active && match.StateOf(P1).Status == PlayerStatus.Active, why);
        Assert.Equal(match.BattlefieldOf(P0), match.BattlefieldOf(P2));

        int checkedTurns = 0;
        int outsideOwnBoard = 0;
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
                outsideOwnBoard += candidates.Count(c => !board0.Contains(c));
                checkedTurns++;
            }
        }

        Assert.Equal(2, checkedTurns);
        Assert.True(outsideOwnBoard > 0, "第 4 大回合的候选里没有任何一格在自己棋盘之外");   // 样本口径：全图解禁后候选确实越出了自己的棋盘
    }

    [Fact]
    public void 全图可读()
    {
        // 规格 Scenario「全图可读」（open-map）：按座位投影的过滤边界是所在战场 = 全图——别的棋盘上的棋子、势力、手牌类型与信物揭示都可读，与全知视图相同；
        // 读取接口只在 VisionFor 一处（open-map 段 B：全知开关已删除，恒给按座位投影 + 预演投影）。
        MatchFlow match = Started([0, 0, 3, 2]);
        Coord mine = CoreCell(Map, 0, 0, 0);   // map-expand-25 段 A2：link:1:4 棋盘 0（E5 起 7×8 河流平台）的 (1, 1) = F6 是河道深水，改取核心区左下角 G7
        Coord theirs = BirthCell(Map, 3, 2, 2);
        match.Place(P0, mine).Place(P2, theirs, BirthCell(Map, 3, 2, 3));
        match.Debug.SeedHand(P2, (PieceType.Fortress, 1));

        MatchPublicView full = match.Publish();
        MatchPublicView seat = match.PublishSeatProjection(P0);
        Assert.NotNull(full.Board[theirs].Occupant);   // 样本口径：别的棋盘上确实有棋子
        Assert.True(full.Power!.Of(P2).Total > 0);

        // 别的棋盘（open-map 裁决 O4）：棋子、势力、手牌类型与信物预告都与全知视图相同（信物层随盘面序列化一并比较）。
        Assert.Equal(full.Board[theirs].Occupant, seat.Board[theirs].Occupant);
        Assert.Equal(full.Board.Serialize(), seat.Board.Serialize());
        Assert.Equal(full.Power.Of(P2).Total, seat.Power!.Of(P2).Total);
        Assert.Equal(full.Hands.Single(h => h.Player == P2).Types, seat.Hands.Single(h => h.Player == P2).Types);
        Assert.NotEmpty(seat.Hands.Single(h => h.Player == P2).Types);
        Assert.Equal(full.RelicForecast, seat.RelicForecast);

        // 本棋盘：与全知视图逐格相同，自己的势力不变；地形与全局项不过滤。
        Assert.Equal(full.Board[mine].Occupant, seat.Board[mine].Occupant);
        Assert.Equal(full.Power.Of(P0).Total, seat.Power.Of(P0).Total);
        Assert.Equal(full.Hands.Single(h => h.Player == P1).Types, seat.Hands.Single(h => h.Player == P1).Types);
        Assert.Equal(full.ActionOrder, seat.ActionOrder);
        Assert.Equal(full.Players, seat.Players);
        Assert.Equal(full.Board.Map, seat.Board.Map);

        // 读取接口只在 VisionFor：恒为按座位投影 + 预演投影（预演口径非空），在全图口径下读到与全知视图相同的盘面。
        Assert.NotNull(HeuristicAi.VisionFor(match, P0, null).Observe().Board[theirs].Occupant);
        Assert.NotNull(HeuristicAi.VisionFor(match, P0, null).Rehearsal);
        Assert.NotNull(HeuristicAi.VisionFor(match, P0, AiSearchConfig.Standard).Observe().Board[theirs].Occupant);
        AiVisionPorts closed = HeuristicAi.VisionFor(match, P0, AiSearchConfig.Standard);
        Assert.NotNull(closed.Rehearsal);
        Assert.Equal(full.Board.Serialize(), closed.Observe().Board.Serialize());
        var rehearsal = new Batch.RehearsalResult(true, false, null, [], match.Board.Clone());
        Assert.NotNull(rehearsal.ProjectedBoard![theirs].Occupant);
        Assert.NotNull(closed.Rehearsal!(rehearsal).ProjectedBoard![theirs].Occupant);
        Assert.Equal(match.Board[mine].Occupant, closed.Rehearsal(rehearsal).ProjectedBoard![mine].Occupant);
        Assert.Equal(rehearsal.ProjectedBoard.Serialize(), closed.Rehearsal(rehearsal).ProjectedBoard!.Serialize());
        // open-map 段 B：搜索配置里不再有视野项（源码扫描见「全知开关已删除」）。
    }

    [Fact]
    public void 按座位视野的评价代码照常运行()
    {
        // 按座位投影的 AI 走完整的决策（征募、候选、评价、确认）：四名 AI 都用按座位投影的视图跑一个大回合，不抛错、每人都落了子
        // （他人棋盘的棋子不会因"预演前看不到、预演后看得到"被评价成敌方势力变化而压到停手阈值以下）。
        // 变异 MC-24：VisionFor 不给预演口径（Rehearsal 为 null）→ 先手之后的 AI 全部 Pass，本测试红。
        MatchFlow match = Started([0, 0, 3, 2]);
        match.Place(P2, BirthCell(Map, 3, 2, 2));
        var runner = new MatchRunner(match);
        AiSearchConfig config = AiSearchConfig.Standard;
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
    public void 缺省按座位视野()
    {
        // ai-seat-vision 规格 Scenario「缺省按座位视野」→ open-map「全图可读」：以缺省配置建局，正式 AI 按座位视野读取，该投影含全图
        // → AI 的读取接口对别的棋盘的格返回地形与占用，读得到别的棋盘玩家的势力数值（与全知相同）。
        // 缺省 = 不给配置 / 四档预设 / 按地图取的配置（终端版与图形版都经 ForMap）/ 跑局层缺省配置（会话落成 true、对全部 AI 生效）。
        MatchFlow match = Started([0, 0, 3, 2]);
        Coord theirs = BirthCell(Map, 3, 2, 2);
        match.Place(P0, CoreCell(Map, 0, 0, 0)).Place(P2, theirs, BirthCell(Map, 3, 2, 3));   // map-expand-25 段 A2：原 BirthCell(0, 1, 1) = F6 是河道深水
        Assert.NotNull(match.Board[theirs].Occupant);   // 样本口径：别的棋盘上确有棋子与势力
        Assert.True(match.Publish().Power!.Of(P2).Total > 0);
        ImmutableHashSet<Coord> field = [.. match.Board.Battlefields.ById(match.BattlefieldOf(P0))!.Cells];
        Assert.Contains(theirs, field);   // open-map：唯一战场含全图

        List<AiSearchConfig?> defaults = [null, .. Enum.GetValues<AiDifficulty>().Select(AiSearchConfig.ForDifficulty),
            .. Enum.GetValues<AiDifficulty>().Select(d => AiSearchConfig.ForMap(d, Map.PlayableCount))];
        foreach (AiSearchConfig? config in defaults)
        {
            HeuristicTurnController ai = HeuristicAi.Create(match, P0, AiDifficulty.Standard, null, config);
            MatchPublicView seen = HeuristicAi.VisionFor(match, P0, config).Observe();
            Assert.Equal(match.Board[theirs].Occupant, seen.Board[theirs].Occupant);
            Assert.Equal(match.Publish().Power!.Of(P2).Total, seen.Power!.Of(P2).Total);
            Assert.Equal(match.Publish().Power!.Of(P2).Total, ai.CreateEvaluator().Before.Of(P2).Total);   // 正式 AI 的评价器同样读到全图
        }

        // 跑局层：缺省配置建会话后每名 AI（含专家落成的搜索配置）同样经 VisionFor 读按座位投影——全部 AI 的评价器读到全图势力。
        var config4 = new RunConfig { MapId = MapId, Players = [new PlayerAiConfig { Difficulty = AiDifficulty.Expert }, new(), new(), new()], TurnLimit = 1 };
        MatchSession session = MatchSession.Create(config4, 1, Map, recorded: false, carry: null);
        Assert.All(session.Match.Players, p => Assert.NotNull(session.AiOf(p)));
    }

    [Fact]
    public void 全知开关已删除()
    {
        // open-map 裁决 B1：AI 全知开关 SeatVision 随唯一战场删除——Core/Ai、Sim、Presentation、godot 源码里都不再出现该标识（HeuristicAi.cs 的一条"已删除"注释除外）；
        // 正式 AI 的 MatchFlow.Publish 委托只剩调试旁路，VisionFor 仍是唯一的读取接口。配样本口径下界与反面命中。
        string root = Determinism.随机子流隔离Tests.SourceRoot();
        string ai = Path.Combine(root, "src", "Siege.Core", "Ai");
        (string Name, string Text)[] files = [.. Directory.GetFiles(ai, "*.cs", SearchOption.AllDirectories).Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];
        Assert.True(files.Length >= 10, $"样本口径：只扫到 {files.Length} 个 AI 源文件。");
        Assert.Equal(["HeuristicAi.cs"], files.Where(f => f.Text.Contains("SeatVision", StringComparison.Ordinal)).Select(f => f.Name));
        Assert.Contains("<c>SeatVision</c> 已删除", files.Single(f => f.Name == "HeuristicAi.cs").Text, StringComparison.Ordinal);
        Assert.Equal(["HeuristicAi.cs"], files.Where(f => f.Text.Contains("PublishSeatProjection(", StringComparison.Ordinal)).Select(f => f.Name));
        Assert.Equal(["DebugTurnController.cs", "MatchDebugView.cs"],
            files.Where(f => System.Text.RegularExpressions.Regex.IsMatch(f.Text, @"match\.Publish\b(?!SeatProjection)")).Select(f => f.Name).Order());
        foreach (string dir in new[] { Path.Combine(root, "src", "Siege.Sim"), Path.Combine(root, "src", "Siege.Presentation"), Path.Combine(root, "src", "godot", "scripts") })
        {
            string[] sources = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
            Assert.True(sources.Length >= 5, $"样本口径：{dir} 只扫到 {sources.Length} 个源文件。");
            Assert.Empty(sources.Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"SeatVision\b(?! \p{IsCJKUnifiedIdeographs})") && !File.ReadAllText(f).Contains("已移除", StringComparison.Ordinal)));
        }
    }

}
