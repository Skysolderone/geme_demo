using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>frontier-map 裁决 12：AI 搜索配置的"候选格上限"旋钮（缺省 0 = 不限制，v4 与一切既有行为零变化）。</summary>
public class 候选格上限Tests
{
    /// <summary>
    /// 黄金值：取自引入候选格上限<b>之前</b>的代码（工作树 = 段 A + 段 B）的实际运行结果——v4、种子 31、4 名 Standard AI、6 大回合，
    /// 全部小回合快照（去耗时）逐行拼接后的 SHA-256。
    /// </summary>
    private const string V4GoldenTurnHash = "83755037FDBD41D6040A22B913C589461E20AACF76C7E8F01075ACD5D5403E18";

    private static string TurnHash(MatchLog log) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', SimFixtures.TurnTexts(log.Turns)))));

    private static AiSearchConfig StandardWith(int cellLimit) => AiSearchConfig.Standard with { CandidateCellLimit = cellLimit };

    /// <summary>第 5 大回合、部署上限 8、60+ 合法空格的开放局面（与「不穷举排列」同一局面）。</summary>
    private static MatchFlow OpenPosition()
    {
        MatchFlow match = AiFixtures.Round5().Stones(AiFixtures.P1, "E5", "F6", "D7").Stones(AiFixtures.P2, "G3");
        match.SetDeployLimit(8);
        return match;
    }

    private sealed record DeployTrace(HeuristicTurnController Ai, int Rehearsals, int Empties, int Types, string Placements);

    private static DeployTrace DeployOnce(MatchFlow match, AiSearchConfig config)
    {
        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Standard, config: config);
        StagedBatch batch = match.OpenDeploy();
        int empties = batch.Context.LegalRange.Count(c => batch.Board[c].IsPlayableEmpty);
        int types = batch.Context.Stock.Count(kv => kv.Value > 0);
        int rehearsals = 0;
        ai.Deploy(batch, () =>
        {
            rehearsals++;
            return match.Rehearse();
        });
        return new DeployTrace(ai, rehearsals, empties, types, string.Join(" ", batch.Placements.Select(p => p.ToString())));
    }

    private static string RankingText(HeuristicTurnController ai) =>
        string.Join("|", ai.LastPointRanking.Select(p => $"{p.Coord.ToNotation()}:{p.Type}:{p.Edit}:{p.Total}"));

    // ---------- K = 0：零变化 ----------

    [Fact]
    public void 缺省不限制时标准图整局与改动前逐步相同()
    {
        // 变异 M-K1：RankPoints 的启用条件改成恒真（K = 0 也预筛，Take(0) 取空）→ 本测试红。
        MatchLog log = BatchRunner.Execute(SimFixtures.Config(seedStart: 31, maxRounds: 6, difficulty: AiDifficulty.Standard), parallelism: 1)[0];

        Assert.False(log.IsFailed);
        Assert.True(log.Turns.Count >= 24, $"小回合 {log.Turns.Count}");
        Assert.Null(log.Header.Config.CandidateCellLimit);
        Assert.Equal(V4GoldenTurnHash, TurnHash(log));
    }

    [Fact]
    public void 难度默认参数不启用上限()
    {
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Equal(0, AiSearchConfig.ForDifficulty(d).CandidateCellLimit));
        Assert.Equal(0, HeuristicAi.Create(OpenPosition(), AiFixtures.P0).Config.CandidateCellLimit);
    }

    // ---------- K > 0 ----------

    [Fact]
    public void 启用后只对前K格做完整枚举()
    {
        // 变异 M-K2：PrefilterCells 去掉 Take(K) → 本测试红。
        const int k = 8;
        DeployTrace unlimited = DeployOnce(OpenPosition(), AiSearchConfig.Standard);
        DeployTrace limited = DeployOnce(OpenPosition(), StandardWith(k));

        Assert.True(limited.Empties >= 60, $"合法空格 {limited.Empties}");
        Assert.Equal(unlimited.Empties, unlimited.Ai.LastCandidateCells.Length);
        Assert.Equal(k, limited.Ai.LastCandidateCells.Length);
        Assert.Equal(limited.Ai.LastCandidateCells.Order(), limited.Ai.LastCandidateCells);
        var cells = limited.Ai.LastCandidateCells.ToHashSet();
        Assert.All(limited.Ai.LastPointRanking, p => Assert.Contains(p.Coord, cells));
        Assert.All(limited.Ai.LastCandidates, c => Assert.All(c.Placements, p => Assert.Contains(p.Coord, cells)));

        // 预演次数：预筛每格 1 次 + K 格 × 类型数（库存无匠人，每格每类型 1 次）+ 组合 M × (N + 1)。
        int combine = AiSearchConfig.Standard.CandidateBatchCount * (AiSearchConfig.Standard.CandidatePointCount + 1);
        Assert.True(limited.Rehearsals <= limited.Empties + (k * limited.Types) + combine, $"预演 {limited.Rehearsals} 次");
    }

    [Fact]
    public void 预筛按代表类型的格分取前K同分按坐标序()
    {
        // 独立复算：代表类型 = 持有类型里枚举序最前的一种、不带改造；格分 = 既有评估函数的总分；降序、同分按坐标序、取前 K。
        // 变异 M-K3：PrefilterCells 的 OrderByDescending 改成 OrderBy（取最差的 K 格）→ 本测试红。变异 M-K11：同分次序改成坐标逆序 → 本测试红。
        //（单删 ThenBy 是等价变异：LINQ 排序稳定，而输入已按坐标序。）
        const int k = 6;
        MatchFlow oracleMatch = OpenPosition();
        HeuristicTurnController oracleAi = HeuristicAi.Create(oracleMatch, AiFixtures.P0);
        StagedBatch batch = oracleMatch.OpenDeploy();
        BatchEvaluator evaluator = oracleAi.CreateEvaluator();
        PieceType representative = batch.Context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order().First();
        var scored = new List<(Coord Cell, long Total)>();
        foreach (Coord cell in batch.Context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order())
        {
            batch.Clear();
            Assert.Null(batch.Stage(cell, representative));
            RehearsalResult result = oracleMatch.Rehearse();
            if (result.IsLegal)
            {
                scored.Add((cell, evaluator.Evaluate(batch.Placements, result, batch.Context).Total));
            }
        }

        Coord[] expected = [.. scored.OrderByDescending(s => s.Total).ThenBy(s => s.Cell).Take(k).Select(s => s.Cell).Order()];

        DeployTrace limited = DeployOnce(OpenPosition(), StandardWith(k));

        Assert.Equal(expected, limited.Ai.LastCandidateCells);
    }

    /// <summary>
    /// A2 是深水、B1 是敌子（气：A1、C1；B2 是己子）：A1 的气边邻格只有 B1，不带改造落 A1 是自杀手。
    /// 匠人落 A1 同时立栅 B1–C1，则 B1 只剩 A1 一口气、随落子被提，A1 合法且提一子（terrain-edit T-3：改造先于提子与自杀手判定）；
    /// 在 A2 搭桥也能让 A1 合法（补一口气），但不提子。这是一手"只有带改造的匠人才下得出"的妙手。
    /// </summary>
    private static MatchFlow ArtisanOnlyCellPosition(bool holdsArtisan)
    {
        MatchFlow match = MatchFixtures.Started(TestMaps.Terrain(surfaces: [("A2", Surface.DeepWater)]))
            .AtRound(5, [AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3])
            .Stones(AiFixtures.P0, "B2")
            .Stones(AiFixtures.P1, "B1");
        match.Debug.SeedHand(AiFixtures.P0, holdsArtisan ? [(PieceType.Basic, 50), (PieceType.Artisan, 5)] : [(PieceType.Basic, 50)]);
        return match;
    }

    [Fact]
    public void 只有带改造的匠人才落得下的格不被预筛漏掉()
    {
        // 落子合法性与类型无关，代表类型（普通子）落不下的格别的类型不带改造也落不下；唯一的例外是匠人的改造先于自杀手判定。
        // 预筛对这类格退而用匠人逐个合法改造目标预演，格分取最高的合法总分；其余格仍只预演一次。
        // 变异 M-K13：去掉回退 → A1 不进候选，本测试红。变异 M-K14：回退条件去掉"代表类型落不下"（每格都逐个改造预演）→ 预演次数上界红。
        // 变异 M-K15：格分改取第一个合法改造（搭桥补气，分低）而不是最高分（立栅提子）→ A1 名次掉出前 K，本测试红。
        Coord a1 = TestMaps.At("A1");
        MatchFlow oracleMatch = ArtisanOnlyCellPosition(holdsArtisan: true);
        HeuristicTurnController oracleAi = HeuristicAi.Create(oracleMatch, AiFixtures.P0);
        StagedBatch batch = oracleMatch.OpenDeploy();
        BatchEvaluator evaluator = oracleAi.CreateEvaluator();
        Coord[] empties = [.. batch.Context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order()];
        ImmutableArray<TerrainEdit> a1Edits = TerrainEditRules.LegalTargets(batch.Board.Map, a1);

        // 前提：A1 不带改造是自杀手，带改造（立栅 B1–C1 / 在 A2 搭桥）才合法；其余空格普通子都落得下。
        var scored = new List<(Coord Cell, long Total)>();
        foreach (Coord cell in empties)
        {
            batch.Clear();
            Assert.Null(batch.Stage(cell, PieceType.Basic));
            RehearsalResult plain = oracleMatch.Rehearse();
            Assert.Equal(cell != a1, plain.IsLegal);
            if (plain.IsLegal)
            {
                scored.Add((cell, evaluator.Evaluate(batch.Placements, plain, batch.Context).Total));
            }
        }

        var a1Totals = new List<long>();
        foreach (TerrainEdit edit in a1Edits)
        {
            batch.Clear();
            Assert.Null(batch.Stage(a1, PieceType.Artisan, edit));
            RehearsalResult edited = oracleMatch.Rehearse();
            if (edited.IsLegal)
            {
                a1Totals.Add(evaluator.Evaluate(batch.Placements, edited, batch.Context).Total);
            }
        }

        batch.Clear();
        Assert.Contains(TerrainEdit.Bridge(TestMaps.At("A2")), a1Edits);
        Assert.Contains(TerrainEdit.Fence(TestMaps.At("B1"), TestMaps.At("C1")), a1Edits);
        Assert.NotEmpty(a1Totals);
        scored.Add((a1, a1Totals.Max()));

        // K 取"恰好把 A1 收进来"的名次：A1 在前 K 格里，K 仍小于合法空格数（预筛生效）。
        (Coord Cell, long Total)[] ranked = [.. scored.OrderByDescending(s => s.Total).ThenBy(s => s.Cell)];
        int k = Array.FindIndex(ranked, s => s.Cell == a1) + 1;
        Assert.InRange(k, 1, empties.Length - 1);   // A1 垫底时无法既收进它又让预筛生效——那样局面要重摆
        Coord[] expected = [.. ranked.Take(k).Select(s => s.Cell).Order()];
        Assert.Contains(a1, expected);

        DeployTrace limited = DeployOnce(ArtisanOnlyCellPosition(holdsArtisan: true), StandardWith(k));

        Assert.Equal(expected, limited.Ai.LastCandidateCells);
        Assert.True(a1Totals.Max() > a1Totals.Min(), "格分取的是各合法改造里最高的总分（立栅提子高于搭桥补气），不是第一个合法的。");

        // 开销：预筛每格 1 次 + 只在 A1 上逐个改造再试 + K 格的完整枚举（普通子 1 次、匠人 1 + 改造目标数）+ 组合 M × (N + 1)。
        int fullEnumeration = limited.Ai.LastCandidateCells.Sum(c => 2 + TerrainEditRules.LegalTargets(batch.Board.Map, c).Length);
        int combine = AiSearchConfig.Standard.CandidateBatchCount * (AiSearchConfig.Standard.CandidatePointCount + 1);
        Assert.True(
            limited.Rehearsals <= empties.Length + a1Edits.Length + fullEnumeration + combine,
            $"预演 {limited.Rehearsals} 次，上界 {empties.Length} + {a1Edits.Length} + {fullEnumeration} + {combine}");

        // 手里没有匠人：A1 谁也落不下，不进候选，也不为它多做预演。
        DeployTrace noArtisan = DeployOnce(ArtisanOnlyCellPosition(holdsArtisan: false), StandardWith(k));
        Assert.DoesNotContain(a1, noArtisan.Ai.LastCandidateCells);
        Assert.True(noArtisan.Rehearsals <= empties.Length + k + combine, $"预演 {noArtisan.Rehearsals} 次");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void K不小于合法空格数时等价于不限制(int extra)
    {
        // 变异 M-K4：启用条件去掉 `cells.Length > K`（K 够大也照样预筛）→ 预演次数多出一轮 → 本测试红。
        DeployTrace unlimited = DeployOnce(OpenPosition(), AiSearchConfig.Standard);
        DeployTrace wide = DeployOnce(OpenPosition(), StandardWith(unlimited.Empties + extra));

        Assert.Equal(unlimited.Rehearsals, wide.Rehearsals);
        Assert.Equal(unlimited.Ai.LastCandidateCells, wide.Ai.LastCandidateCells);
        Assert.Equal(RankingText(unlimited.Ai), RankingText(wide.Ai));
        Assert.Equal(unlimited.Ai.LastCandidates.Select(c => $"{c.Key}={c.Total}"), wide.Ai.LastCandidates.Select(c => $"{c.Key}={c.Total}"));
        Assert.Equal(unlimited.Placements, wide.Placements);
        Assert.Equal(unlimited.Ai.Decisions, wide.Ai.Decisions);
    }

    [Theory]
    [InlineData("a", "F5")]
    [InlineData("b", "E5")]
    [InlineData("c", "F5")]
    public void 小K下仍找到妙手(string position, string key)
    {
        // 提子点 / 救命点没有单独的豁免名单：提子走"敌方损失"维、补气走"安全"维，都与落下的类型无关，代表类型的格分自然把它们排进前 K。
        // 局面同「高部署上限下的候选剪枝 · 必须找到的妙手」，K 取 4。
        MatchFlow match = position switch
        {
            "a" => 启发式评价维度Tests.CaptureRelicPosition(),
            "b" => AiFixtures.Round5()
                .Stones(AiFixtures.P0, "D4", "D5", "F5", "G4", "E3", "F3")
                .Stones(AiFixtures.P1, "D6", "E6", "C5", "C4", "E4", "F4", "D3"),
            "c" => AiFixtures.Round5(relics: [("E6", RelicFixtures.Conscription())])
                .Stones(AiFixtures.P0, "D5", "D6", "E4", "E7", "F7", "G6")
                .Stones(AiFixtures.P1, "E5", "E6", "F6"),
            _ => throw new ArgumentOutOfRangeException(nameof(position)),
        };

        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Standard, config: StandardWith(4));
        StagedBatch batch = match.OpenDeploy();
        ai.Deploy(batch, match.Rehearse);

        Assert.Equal(4, ai.LastCandidateCells.Length);
        Assert.Contains(TestMaps.At(key), ai.LastCandidateCells);
        Assert.Contains(batch.Placements, p => p.Coord == TestMaps.At(key));
        SettlementOutcome outcome = match.Confirm();
        Assert.True(outcome.Confirmed);
        Assert.True(outcome.CaptureRecord is { } captured && captured.Captured.Length >= 1, "妙手必须真的提子");
    }

    [Fact]
    public void 启用后同种子两次运行逐步相同()
    {
        // v4（105 格）上显式 K = 2：每个小回合都走预筛。预筛不消费随机流、平分按坐标序 → 两次运行快照逐字节相同。
        // K 取 2（小于部署上限）是为了让这一局必然不同于不限制的那一局——实测 K = 8 在这个种子上与不限制逐步相同，证明不了 K 传到了 AI。
        static MatchLog Run() => BatchRunner.Execute(
            SimFixtures.Config(seedStart: 31, maxRounds: 6, difficulty: AiDifficulty.Standard) with { CandidateCellLimit = 2 }, parallelism: 1)[0];

        // 变异 M-K9：Sim 的 MatchSession 建 AI 时不传跑局配置的 K → 与不限制的那一局相同 → 本测试红。
        MatchLog a = Run();
        MatchLog b = Run();

        Assert.False(a.IsFailed);
        Assert.Equal(TurnHash(a), TurnHash(b));
        Assert.NotEqual(V4GoldenTurnHash, TurnHash(a));   // K 确实传到了 AI：与不限制的那一局不同
        Assert.Equal(2, a.Header.Config.CandidateCellLimit);
    }

    // ---------- 缺省 K：阈值逻辑只有一处 ----------

    [Fact]
    public void 大图缺省取上限小图为零显式配置优先()
    {
        // 变异 M-K5：DefaultCellLimitFor 的 `>` 改成 `>=` → 边界断言红。
        Assert.Equal(0, AiSearchConfig.DefaultCellLimitFor(AiSearchConfig.LargeMapPlayableThreshold));
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, AiSearchConfig.DefaultCellLimitFor(AiSearchConfig.LargeMapPlayableThreshold + 1));
        Assert.True(AiSearchConfig.LargeMapCellLimit > 0);

        int v4 = MapCatalog.Resolve(null).PlayableCount;
        int frontier = MapCatalog.Resolve(FrontierMapV1.Id).PlayableCount;
        Assert.True(v4 <= AiSearchConfig.LargeMapPlayableThreshold, $"v4 可落子格 {v4}");
        Assert.True(frontier > AiSearchConfig.LargeMapPlayableThreshold, $"边疆图可落子格 {frontier}");
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Equal(AiSearchConfig.ForDifficulty(d), AiSearchConfig.ForMap(d, v4)));
        Assert.Equal(StandardWith(AiSearchConfig.LargeMapCellLimit), AiSearchConfig.ForMap(AiDifficulty.Standard, frontier));
        Assert.Equal(AiSearchConfig.Standard, AiSearchConfig.ForMap(AiDifficulty.Standard, frontier, cellLimit: 0));
        Assert.Equal(StandardWith(5), AiSearchConfig.ForMap(AiDifficulty.Standard, v4, cellLimit: 5));
    }

    [Fact]
    public void 三个入口共用同一处阈值逻辑()
    {
        // src/godot 不在 siege.sln 里，只能做源码文本扫描（testing.md）；配样本口径下界与反面命中。
        // 变异 M-K6：把 src/godot/scripts/MatchSession.cs 的建 AI 一行改回 HeuristicAi.Create(Match, player, Difficulty) → 本测试红。M-K10：终端版同样改回 → 本测试红。
        string root = FrontierFixtures.RepoRoot();
        string[][] entries =
        [
            ["src", "Siege.Sim", "Play", "PlayCommand.cs"],
            ["src", "Siege.Sim", "Running", "MatchSession.cs"],
            ["src", "godot", "scripts", "MatchSession.cs"],
        ];
        foreach (string[] entry in entries)
        {
            string text = File.ReadAllText(Path.Combine([root, .. entry]));
            Assert.Contains("AiSearchConfig.ForMap(", text, StringComparison.Ordinal);
            string[] creates = [.. text.Split('\n').Where(l => l.Contains("HeuristicAi.Create(", StringComparison.Ordinal) && !l.TrimStart().StartsWith("//", StringComparison.Ordinal))];
            Assert.NotEmpty(creates);
            Assert.All(creates, l => Assert.Contains("search)", l, StringComparison.OrdinalIgnoreCase));
        }

        // 阈值与缺省 K 两个常量只在 Core 里被读：下游不得自己比较可落子格数。
        string[] downstream =
        [
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "Siege.Sim"), "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "godot", "scripts"), "*.cs", SearchOption.AllDirectories),
        ];
        Assert.True(downstream.Length >= 20, $"样本口径：只扫到 {downstream.Length} 个下游文件。");
        Assert.Empty(downstream
            .Where(p => File.ReadAllText(p) is var t && (t.Contains("LargeMapPlayableThreshold", StringComparison.Ordinal) || t.Contains("LargeMapCellLimit", StringComparison.Ordinal)))
            .Select(Path.GetFileName));
        Assert.Contains("LargeMapPlayableThreshold", File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Ai", "AiDifficulty.cs")), StringComparison.Ordinal);
    }

    // ---------- 回放 ----------

    [Fact]
    public void 首部没有上限项的大图旧日志按不限制回放()
    {
        // 该项出现之前的边疆图日志首部没有 CandidateCellLimit：当时就是不限制。回放 MUST NOT 按今天的大图缺省 K 重跑，重建的首部也不得多出一项。
        // 变异 M-K12：Replayer 改回不带 recorded 的 MatchSession.Create → 重建首部多出该项、第 1 行即分歧 → 本测试红。
        RunConfig frontier = SimFixtures.Config(maxRounds: 1) with { MapId = FrontierMapV1.Id };
        MatchLog zero = BatchRunner.Execute(frontier with { CandidateCellLimit = 0 }, parallelism: 1)[0];
        string text = zero.DeterministicText();
        Assert.Contains("\"CandidateCellLimit\":0,", text, StringComparison.Ordinal);
        MatchLog old = MatchLog.Parse(text.Replace("\"CandidateCellLimit\":0,", string.Empty, StringComparison.Ordinal));
        Assert.Null(old.Header.Config.CandidateCellLimit);

        ReplayResult replay = Replayer.Replay(old);

        Assert.True(replay.Identical, replay.ToString());
        Assert.Null(replay.Replayed.Header.Config.CandidateCellLimit);

        // 会话层：新建的局按地图取缺省 K 并写进首部配置；按首部重建的局不取。
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, MatchSession.Create(frontier, 1).CellLimit);
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, MatchSession.Create(frontier, 1).Config.CandidateCellLimit);
        Assert.Equal(0, MatchSession.Create(frontier, 1, map: null, recorded: true).CellLimit);
        Assert.Equal(7, MatchSession.Create(frontier with { CandidateCellLimit = 7 }, 1, map: null, recorded: true).CellLimit);
    }

    // ---------- 配置记录 ----------

    [Fact]
    public void 剪枝参数的文本往返与旧文本兼容()
    {
        AiSearchConfig old = JsonSerializer.Deserialize<AiSearchConfig>("""{"CandidatePointCount":12,"CandidateBatchCount":8,"ImmediateOnly":false}""")!;
        Assert.Equal(AiSearchConfig.Standard, old);

        AiSearchConfig limited = StandardWith(16);
        Assert.Equal(limited, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(limited)));
        Assert.Throws<ArgumentOutOfRangeException>(() => StandardWith(-1).Validated());
    }

    [Fact]
    public void 跑局配置的文本往返()
    {
        // 变异 M-K7：RunConfig.CandidateCellLimit 标 [JsonIgnore] → 本测试红。
        RunConfig unset = SimFixtures.Config();
        Assert.Null(unset.CandidateCellLimit);
        Assert.DoesNotContain("CandidateCellLimit", unset.ToJson(), StringComparison.Ordinal);
        Assert.Null(RunConfig.FromJson(unset.ToJson()).CandidateCellLimit);

        foreach (int k in new[] { 0, 16 })
        {
            RunConfig set = unset with { CandidateCellLimit = k };
            Assert.Contains($"\"CandidateCellLimit\": {k}", set.ToJson(), StringComparison.Ordinal);
            Assert.Equal(k, RunConfig.FromJson(set.ToJson()).CandidateCellLimit);
        }

        Assert.Throws<ArgumentException>(() => (unset with { CandidateCellLimit = -1 }).Validated());
    }

    [Fact]
    public void 批次配置记录写入实际生效的上限()
    {
        // 边疆图、Easy、1 个大回合（保护期内只有自家平台可落，跑得快）。读 config.json 原文，不经反序列化，避免缺省值掩盖漏写。
        // 变异 M-K8：BatchRunner.ExecuteToDirectory 去掉 ResolvedFor → config.json 不含该项 → 本测试红。
        RunConfig auto = SimFixtures.Config(maxRounds: 1) with { MapId = FrontierMapV1.Id };
        string autoDir = SimFixtures.TempDir("cell-limit-auto");
        BatchRunner.ExecuteToDirectory(auto, autoDir, parallelism: 1);
        Assert.Contains($"\"CandidateCellLimit\": {AiSearchConfig.LargeMapCellLimit}", File.ReadAllText(Path.Combine(autoDir, "config.json")), StringComparison.Ordinal);
        MatchLog autoLog = MatchLog.Read(Directory.EnumerateFiles(autoDir, "match-*.jsonl").Single());
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, autoLog.Header.Config.CandidateCellLimit);

        // 显式 0（不限制）优先于大图缺省，并如实写出。
        string zeroDir = SimFixtures.TempDir("cell-limit-zero");
        BatchRunner.ExecuteToDirectory(auto with { CandidateCellLimit = 0 }, zeroDir, parallelism: 1);
        Assert.Contains("\"CandidateCellLimit\": 0", File.ReadAllText(Path.Combine(zeroDir, "config.json")), StringComparison.Ordinal);

        // 标准图：配置记录与引入本项之前一样，不多出这一项。
        string v4Dir = SimFixtures.TempDir("cell-limit-v4");
        BatchRunner.ExecuteToDirectory(SimFixtures.Config(maxRounds: 1), v4Dir, parallelism: 1);
        Assert.DoesNotContain("CandidateCellLimit", File.ReadAllText(Path.Combine(v4Dir, "config.json")), StringComparison.Ordinal);
    }
}
