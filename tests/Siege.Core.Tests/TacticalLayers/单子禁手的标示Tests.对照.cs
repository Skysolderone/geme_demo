using System.Diagnostics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Tests.CaptureResolution;
using Siege.Core.Tests.LifeShape;
using Xunit.Abstractions;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// 规格：tactical-layers「单子禁手的标示」—— Scenario: 与完整预演一致（forbidden-marks D3，tasks 1.3 / 1.4）。
/// 期望值由<b>公开的</b> <see cref="BatchRehearsal.Rehearse"/> 逐格算出（完整八步，额度与库存给足），不经查询内部的任何入口。
/// </summary>
public partial class 单子禁手的标示Tests(ITestOutputHelper output)
{
    /// <summary>AI 权重写死（testing.md「依赖 AI 实际怎么走的断言要把权重写死」）：样本口径下界依赖这几颗种子下的实际走法。</summary>
    private static readonly EvaluationWeights Fixed = new(
        PowerGain: 10, EnemyLoss: 10, Relic: 5, Safety: 20, Growth: 2, Initiative: 1, Supply: 1, Eye: 5, Threat: 10);

    /// <summary>逐格完整预演：候选 = 范围内未被暂放占用的空格，每格各跑一次公开的八步预演。</summary>
    private static string[] ByFullRehearsal(
        GameBoard board, PlayerId player, IReadOnlySet<Coord> range, IReadOnlyList<Placement> staged, BoardHistory history, out int candidates)
    {
        var context = new BatchContext
        {
            Player = player,
            DeployLimit = staged.Count + 1,
            LegalRange = range,
            Stock = Enum.GetValues<PieceType>().ToDictionary(t => t, _ => 1000),
            WorkshopActive = true,
        };
        var taken = staged.Select(p => p.Coord).ToHashSet();
        var lines = new List<string>();
        candidates = 0;
        foreach (Coord c in range.Where(c => board[c].IsPlayableEmpty && !taken.Contains(c)).Order())
        {
            candidates++;
            RehearsalResult result = BatchRehearsal.Rehearse(board, context, [.. staged, new Placement(c, PieceType.Basic)], history);
            if (result.IsLegal)
            {
                continue;
            }

            BatchFailure failure = result.Failure!;
            Assert.Contains(failure.Kind, new[] { BatchFailureKind.BreaksLife, BatchFailureKind.Suicide, BatchFailureKind.Superko });
            lines.Add($"{c.ToNotation()}:{failure.Kind}"
                + (failure.DuplicateOfSequence is { } s ? $":#{s}" : string.Empty)
                + (failure.LifeOwner is { } o ? $":{o}" : string.Empty));
        }

        return [.. lines];
    }

    /// <summary>对照一个局面；返回（候选格数, 走完整预演的格数, 禁手数）。</summary>
    private static (int Candidates, int Full, int Forbidden) AssertSame(
        string label, GameBoard board, PlayerId player, IReadOnlySet<Coord> range, IReadOnlyList<Placement> staged, BoardHistory history)
    {
        string boardBefore = board.Serialize();
        string historyBefore = history.Serialize();
        string[] expected = ByFullRehearsal(board, player, range, staged, history, out int candidates);

        ForbiddenMoveReport report = ForbiddenMoves.Query(board, player, range, staged, history);
        ForbiddenMoveReport exhaustive = ForbiddenMoves.Query(board, player, range, staged, history, prefilter: false);

        Assert.True(expected.SequenceEqual(Project(report)), $"{label}：预筛版与逐格完整预演不同。\n期望 {string.Join(" ", expected)}\n实际 {string.Join(" ", Project(report))}");
        Assert.True(expected.SequenceEqual(Project(exhaustive)), $"{label}：不预筛版与逐格完整预演不同。");
        Assert.Equal(candidates, report.CandidateCount);
        Assert.Equal(candidates, exhaustive.FullRehearsalCount);
        Assert.Equal(boardBefore, board.Serialize());
        Assert.Equal(historyBefore, history.Serialize());
        return (candidates, report.FullRehearsalCount, expected.Length);
    }

    /// <summary>固定种子、四名简单难度 AI（权重与剪枝参数写死）的一局，推进到第 <paramref name="turns"/> 个小回合之后（小回合边界）。</summary>
    /// <remarks>
    /// <paramref name="skipProtection"/>：插旗后直接把大回合拨到第 4（构筑保护期之后，全图可落子）。棋盘图上 AI 每个小回合约 1 秒，
    /// 自然走完保护期的 12 个小回合就要十几秒，而保护期内的候选只有出生棋盘那几十格；拨大回合只改变"从哪一轮开始下"，此后每一手仍是 AI 按规则下出来的。
    /// </remarks>
    private static MatchFlow Played(string mapId, ulong seed, int turns, out MatchRunner runner, bool skipProtection = false)
    {
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve(mapId), new GameSeed(seed), MatchFixtures.All, MatchOptions.Immediate);
        match.PlantPrototype();
        if (skipProtection)
        {
            match.Debug.SetMajorRound(PublicRules.BuildProtectionRounds + 1);
        }

        runner = new MatchRunner(match);
        // 剪枝参数同样写死，且取得很窄（候选格上限 8、贪心一条、停手阈值 0）：这里只要一个有接触的中盘局面，不要棋力；默认套件里每个小回合都得便宜。
        AiSearchConfig search = new AiSearchConfig(
            CandidatePointCount: 4, CandidateBatchCount: 1, ImmediateOnly: true, CandidateCellLimit: 8, PassThreshold: 0).Validated();
        foreach (PlayerId p in match.Players)
        {
            runner.SetController(p, HeuristicAi.Create(match, p, AiDifficulty.Easy, Fixed, search));
        }

        for (int i = 0; i < turns && match.Phase == MatchPhase.InProgress; i++)
        {
            runner.RunTurn();
        }

        Assert.Equal(MatchPhase.InProgress, match.Phase);
        return match;
    }

    [Theory]
    [InlineData("siege-4p-base-v5", 7UL, 14, 3, 4, false, true)]
    [InlineData("siege-4p-base-v5", 11UL, 16, 3, 3, false, true)]
    [InlineData("board:1", 3UL, 5, 2, 2, true, false)]
    public void 与完整预演一致(string mapId, ulong seed, int firstTurns, int step, int checkpoints, bool skipProtection, bool requireContact) =>
        Compare(mapId, seed, firstTurns, step, checkpoints, skipProtection, requireContact);

    /// <summary>
    /// 棋盘图上自然走完保护期、下到有接触的中盘（24 个小回合，约 25 秒）：默认套件里的缩小版是上面 <c>board:1</c> 那一行
    /// （拨过保护期、7 个小回合、尚无接触，只守"大图上逐格相同"），同一个测试体（testing.md「慢测试」）。
    /// </summary>
    [SlowFact]
    [Trait("Category", "Slow")]
    public void 与完整预演一致_棋盘图中盘() => Compare("board:1", 3UL, 14, 5, 3, skipProtection: false, requireContact: true);

    private void Compare(string mapId, ulong seed, int firstTurns, int step, int checkpoints, bool skipProtection, bool requireContact)
    {
        // 规格 Scenario「与完整预演一致」：中盘局面由固定种子的单局 AI 对局推进得到（几十个小回合以内，不跑批量）。
        // 每个检查点对当前行动玩家各对照三种暂放：空、一枚合法暂放、一枚禁手暂放（有的话；整批不合法 → 不预筛的路径）。
        // 变异（红数为三个测试类合计）：M-P3 预筛漏掉"自身有气"→ 红 10、M-P6 基准不合法仍预筛 → 红 6、M-C2 候选不排除已暂放的格 → 红 10，本测试都在其中。
        // M-P1 / M-P2 / M-P4 在 AI 下出来的局面里碰不到（提子后同形、不提子同形、暂放立栅造眼都太罕见），各由下面一条专门的局面守住。
        // M-P5（预筛去掉"不贴他人已确定活形棋串"）→ 红 0：等价变异——这一条是 D3 的保守条款，安全性已由"不在他人眼空间里"保证，见 ForbiddenMoves 的说明。
        var clock = Stopwatch.StartNew();
        MatchFlow match = Played(mapId, seed, firstTurns, out MatchRunner runner, skipProtection);
        long playMs = clock.ElapsedMilliseconds;
        int candidates = 0, full = 0, forbidden = 0, positions = 0, illegalBase = 0;
        for (int k = 0; k < checkpoints && match.Phase == MatchPhase.InProgress; k++)
        {
            PlayerId player = match.CurrentPlayer!.Value;
            IReadOnlySet<Coord> range = match.LegalRangeFor(player);
            if (k > 0 && match.Map.PlayableCount > AiSearchConfig.LargeMapPlayableThreshold)
            {
                // 大图：第一个检查点对照整个合法落子范围，其余只对照离棋子三格以内的那一圈（逐格完整预演在 900 格的图上每格约 1 ms，
                // 全范围反复对照会把默认套件拖长十几秒）。范围是查询的输入，收窄它不改变每个候选格的判定。
                Coord[] stones = [.. match.Board.AllCoords().Where(x => match.Board[x].Occupant is not null)];
                range = range.Where(x => stones.Any(s => Math.Abs(s.X - x.X) <= 3 && Math.Abs(s.Y - x.Y) <= 3)).ToHashSet();
            }
            string label = $"{mapId} 种子 {seed} 检查点 {k}（第 {match.MajorRound} 大回合，{player}）";

            (int c, int f, int n) = AssertSame(label + " 空暂放", match.Board, player, range, [], match.History);
            candidates += c;
            full += f;
            forbidden += n;
            positions++;

            ForbiddenMoveReport empty = ForbiddenMoves.Query(match.Board, player, range, [], match.History);
            Coord[] open = [.. range.Where(x => match.Board[x].IsPlayableEmpty && empty.At(x) is null).Order()];
            if (open.Length > 0)
            {
                (c, f, n) = AssertSame(label + " 合法暂放", match.Board, player, range, [new Placement(open[open.Length / 2], PieceType.Basic)], match.History);
                candidates += c;
                full += f;
                forbidden += n;
                positions++;
            }

            if (empty.Moves.Length > 0)
            {
                // 整批不合法的基准不预筛（全部候选走完整预演），单列，不计入预筛比例。
                (c, f, _) = AssertSame(label + " 禁手暂放", match.Board, player, range, [new Placement(empty.Moves[0].Coord, PieceType.Basic)], match.History);
                Assert.Equal(c, f);
                illegalBase++;
            }

            var play = Stopwatch.StartNew();
            for (int i = 0; k + 1 < checkpoints && i < step && match.Phase == MatchPhase.InProgress; i++)
            {
                runner.RunTurn();
            }

            playMs += play.ElapsedMilliseconds;
        }

        output.WriteLine($"{mapId} 种子 {seed}：局面 {positions}（另有整批不合法的基准 {illegalBase} 个），候选格 {candidates}，走完整预演 {full}，禁手 {forbidden}，用时 {clock.ElapsedMilliseconds} ms（其中推进对局 {playMs} ms）");

        // 样本口径下界（testing.md）：确实对照了足够多的候选格，且预筛确实筛掉了大半——否则"逐格相同"只是两边都跑了完整预演。
        Assert.True(positions >= checkpoints, $"局面数 {positions}");
        Assert.True(candidates >= 200, $"候选格 {candidates}");
        Assert.True(!requireContact || full > 0, "样本里没有任何候选走完整预演：预筛的两个条件都没被触发过。");
        Assert.True(full < candidates, $"走完整预演 {full} / {candidates}：预筛没有起作用。");
    }

    [Fact]
    public void 与完整预演一致_有气却提子后同形()
    {
        // 预筛"邻格有一口气敌串"这一条的守门局面（三方，真实落子得到的历史）：
        // 开局 P2 在 B2 / C1、P0 在 A3。第 1 次提交 P0 落 A1；第 2 次提交 P0 落 B1（A1–B1 只剩 A2 一口气）；第 3 次提交 P1 落 A2 提走 A1、B1。
        // 此刻轮到 P0：A1 自身有气（B1 空），但邻格 A2 的 P1 子只剩 A1 一口气——落 A1 提走 A2，盘面回到第 1 次提交 → 同形。
        // 变异 M-P1（预筛去掉"邻格有不多于一口气的敌串"条件）→ A1 被当成安静候选、只在未提子的盘面上比同形 → 漏报，红 1（本测试）。
        GameBoard board = TestMaps.Blank(size: 9).Place("B2", P2).Place("C1", P2).Place("A3", P0);
        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(driver.Confirm(BatchFixtures.Context(board, P0), [BatchFixtures.P("A1")]).Confirmed);
        Assert.True(driver.Confirm(BatchFixtures.Context(board, P0), [BatchFixtures.P("B1")]).Confirmed);
        SettlementOutcome taken = driver.Confirm(BatchFixtures.Context(board, P1), [BatchFixtures.P("A2")]);
        Assert.True(taken.Confirmed, taken.Failure?.Message);
        Assert.Equal(["A1", "B1"], taken.CaptureRecord!.Captured.Select(s => s.Coord).Notations());
        Assert.Equal(["A1"], board.LibertiesOf(board.GroupAt(TestMaps.At("A2"))!).Notations());
        Assert.True(board.GivesLiberty(TestMaps.At("B1")));

        IReadOnlySet<Coord> range = RangeOf(board, P0);
        (int candidates, int full, _) = AssertSame("送二还一", board, P0, range, [], driver.History);

        Assert.Equal(["A1:Superko:#1"], Project(ForbiddenMoves.Query(board, P0, range, [], driver.History)));
        Assert.True(full < candidates);
    }

    [Fact]
    public void 与完整预演一致_不提子也可能同形()
    {
        // 安静候选仍要过第 8 步的守门局面：全局同形不区分提交者、不要求提子。历史里登记一份"当前盘面 + P0 在 E5"的提交
        // （真实对局里要靠多方循环提子才能回到这种盘面，这里直接登记；登记本身走公开的 BoardHistory.Record）。
        // E5 四邻全空、不贴任何棋子 → 预筛判为安静；落下后盘面与第 1 次提交同形。
        // 变异 M-P2（安静候选跳过同形比对）→ 漏报，红 1（本测试）。
        GameBoard board = TestMaps.Blank(size: 9).Place("B2", P1).Place("G7", P0);
        var history = new BoardHistory();
        GameBoard repeated = board.Clone();
        repeated.Place(TestMaps.At("E5"), P0, PieceType.Fortress);
        Assert.Equal(1, history.Record(repeated.Serialize()));
        Assert.Equal(2, history.Record(board.Serialize()));

        IReadOnlySet<Coord> range = RangeOf(board, P0);
        (int candidates, int full, _) = AssertSame("不提子同形", board, P0, range, [], history);

        Assert.Equal(["E5:Superko:#1"], Project(ForbiddenMoves.Query(board, P0, range, [], history)));
        Assert.Equal(79, candidates);
        Assert.Equal(0, full);
    }

    [Fact]
    public void 与完整预演一致_暂放含改造()
    {
        // 暂放里有改造（立栅切开活形 / 不切开）时同样逐格相同：沿用「查询_破坏活形带所有者」的盘面，另加一条不破坏活形的立栅。
        GameBoard board = LifeShapeFixtures.Grid(
        [
            ".......",
            ".......",
            "00000..",
            ".###.#.",
        ]);
        IReadOnlySet<Coord> range = RangeOf(board, P1);
        var history = new BoardHistory();

        (_, _, int broken) = AssertSame(
            "立栅切开活形", board, P1, range, [BatchFixtures.Artisan("C3", TerrainEdit.Fence(TestMaps.At("C2"), TestMaps.At("D2")))], history);
        (_, _, int harmless) = AssertSame(
            "立栅不碰活形", board, P1, range, [BatchFixtures.Artisan("F3", TerrainEdit.Fence(TestMaps.At("F3"), TestMaps.At("G3")))], history);

        Assert.Equal(16, broken);
        Assert.Equal(0, harmless);
    }

    [Fact]
    public void 与完整预演一致_暂放立栅造出他人的新眼()
    {
        // 预筛"不在结算后盘面上他人的眼空间里"这一条的守门局面。P0 的大串原有两个单格眼 D7、H1（已确定活形）；它围着的 3×3 空区 C3–E5
        // 在 F4 处贴着 P1 的棋子，不封闭、不是眼，因此不在 P1 的禁入格里。P1 暂放两枚匠人：H3 立栅 H1–H2（眼 H1 不再贴 P0 串），
        // G4 立栅 E4–F4（3×3 空区从此只贴 P0 → 成了 P0 的新眼，眼值 2）。暂放自身合法：P0 串仍有 D7 + 新眼 = 3。
        // 再落 D4（新眼正中，四邻全空、不贴任何棋子）：新眼贴上 P1 的棋子不再封闭，P0 串只剩 D7 → 破坏活形。
        // 变异 M-P4（预筛去掉"不在他人眼空间里"）→ D4 被当成安静候选漏报，红 1（本测试）。
        GameBoard board = LifeShapeFixtures.Grid(
        [
            "..0.0....",
            ".00000...",
            ".0...0...",
            ".0...1...",
            ".0...0...",
            ".0000000.",
            "......#.#",
        ]);
        LifeShapeReport before = LifeShapeReport.Analyze(board);
        Assert.Equal(LifeState.Alive, before.LifeOf("B2"));
        Assert.Equal(["H1", "D7"], before.EyeSpacesOf("B2"));
        IReadOnlySet<Coord> range = RangeOf(board, P1);
        Assert.Contains(TestMaps.At("D4"), range);
        Placement[] staged =
        [
            BatchFixtures.Artisan("H3", TerrainEdit.Fence(TestMaps.At("H1"), TestMaps.At("H2"))),
            BatchFixtures.Artisan("G4", TerrainEdit.Fence(TestMaps.At("E4"), TestMaps.At("F4"))),
        ];
        RehearsalResult alone = BatchRehearsal.Rehearse(board, BatchFixtures.Context(board, P1, range: range), staged, new BoardHistory());
        Assert.True(alone.IsLegal, alone.Failure?.Message);
        Assert.Equal(3, LifeShapeReport.Analyze(alone.ProjectedBoard!).GroupLifeAt(TestMaps.At("B2"))!.EyeValueSum);

        (int candidates, int full, int forbidden) = AssertSame("立栅造出新眼", board, P1, range, staged, new BoardHistory());

        ForbiddenMoveReport report = ForbiddenMoves.Query(board, P1, range, staged, new BoardHistory());
        Assert.Equal((ForbiddenMoveKind.BreaksLife, (PlayerId?)P0), (report.At(TestMaps.At("D4"))!.Kind, report.At(TestMaps.At("D4"))!.LifeOwner));
        Assert.Equal(["C3", "D3", "E3", "C4", "D4", "E4", "C5", "D5", "E5"], report.Moves.Select(m => m.Coord).Notations());
        Assert.Equal(9, forbidden);
        Assert.True(full < candidates);
    }

    [PerfTheory]
    [Trait("Category", "Perf")]
    [InlineData("board:1:n10", 3UL, 28)]
    public void 单次查询耗时(string mapId, ulong seed, int turns)
    {
        // tasks 1.4：中盘局面的单次查询耗时，只出数字不守门（testing.md「慢测试与计时测试」）。预热后各取 20 次的中位数。
        MatchFlow match = Played(mapId, seed, turns, out _);
        PlayerId player = match.CurrentPlayer!.Value;
        IReadOnlySet<Coord> range = match.LegalRangeFor(player);
        ForbiddenMoveReport report = ForbiddenMoves.Query(match.Board, player, range, [], match.History);

        double Median(bool prefilter)
        {
            for (int i = 0; i < 3; i++)
            {
                ForbiddenMoves.Query(match.Board, player, range, [], match.History, prefilter);
            }

            var samples = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                var sw = Stopwatch.StartNew();
                ForbiddenMoves.Query(match.Board, player, range, [], match.History, prefilter);
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }

            samples.Sort();
            return samples[samples.Count / 2];
        }

        double without = Median(prefilter: false);
        double with = Median(prefilter: true);
        output.WriteLine(
            $"{mapId} 种子 {seed} 第 {turns} 个小回合后（第 {match.MajorRound} 大回合，可落子格 {match.Map.PlayableCount}，盘上 {match.Board.AllGroups().Sum(g => g.Size)} 子，历史 {match.History.Count} 次提交）："
            + $"候选 {report.CandidateCount}，走完整预演 {report.FullRehearsalCount}，禁手 {report.Moves.Length}；预筛前 {without:F2} ms，预筛后 {with:F2} ms");
    }
}
