using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// design D-J / tasks 3.1：AI 对每枚暂放匠人枚举其全部合法改造目标 + "不改造"，与落点组合进入既有评分。
/// </summary>
/// <remarks>
/// 规格场景「AI 在能一手立栅提子时选择该手」<b>不可实现</b>（见 <c>改造先于提子Tests</c> 的待决 B-1：
/// 现行规则下任何改造都不可能直接致提子）。因此这里改钉三件可实现的事：
/// ① 枚举确实同时产出"带改造"与"不改造"两类候选，三种动作都在；
/// ② 选中的批次<b>连改造一起</b>摆回暂放（复摆漏 Edit 会静默丢改造）；
/// ③ 非匠人一律没有改造候选。
/// 全部用 Standard 难度：Easy 的征募前瞻只看基础军势，匠人进得了面板却几乎不落盘（段 A 检查第 4 项）。
/// </remarks>
public class AI枚举改造目标Tests
{
    private static readonly PlayerId Me = MatchFixtures.P0;

    /// <summary>D4 / D5 深水、F4 林地的 9×9 对局，第 5 大回合（保护期已过，全图可落子）。</summary>
    private static MatchFlow Match() => MatchFixtures
        .Started(TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater), ("D5", Surface.DeepWater), ("F4", Surface.Forest)]))
        .AtRound(5);

    private static HeuristicTurnController Ai(MatchFlow match) =>
        new(Me, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(Me)), AiDifficulty.Standard);

    /// <summary>
    /// 把合法落子范围收窄到 <paramref name="cells"/>：单点排行榜只保留前 N = 12 名，
    /// 全图枚举时"带改造"的候选会被高分的普通落点挤出榜外，那样这几条就什么都测不到。
    /// </summary>
    private static (StagedBatch Batch, SettlementDriver Driver) Staging(
        MatchFlow match, PieceType type, int limit = 1, params string[] cells)
    {
        GameBoard board = match.Board;
        IReadOnlySet<Coord>? range = cells.Length == 0 ? null : cells.Select(TestMaps.At).ToHashSet();
        var batch = new StagedBatch(board, BatchFixtures.Context(board, Me, limit, BatchFixtures.Stock((type, 5)), range));
        return (batch, new SettlementDriver(board, match.History, new RecordingHooks { Board = board }));
    }

    private static ImmutableArray<PointScore> Rank(HeuristicTurnController ai, StagedBatch batch, SettlementDriver driver)
    {
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));
        batch.Clear();
        return ai.LastPointRanking;
    }

    [Fact]
    public void 单点枚举同时产出带改造与不改造两类候选()
    {
        // D-J：对每枚暂放匠人枚举"全部合法目标 + 不改造"。同一落点因此会出现多个候选，只差在改造上。
        // 变异 M-B5：RankPoints 只 yield null（不枚举改造）→ 本条红。
        MatchFlow match = Match();
        (StagedBatch batch, SettlementDriver driver) = Staging(match, PieceType.Artisan, 1, "C4", "E4");

        ImmutableArray<PointScore> ranking = Rank(Ai(match), batch, driver);

        Assert.Contains(ranking, p => p.Edit is null);
        Assert.Contains(ranking, p => p.Edit is not null);

        // 三种动作都被枚举到：C4 旁是深水 D4，E4 旁是林地 F4，任何落点都能对四邻立栅。
        Assert.Contains(ranking, p => p.Edit?.Kind == TerrainEditKind.Bridge);
        Assert.Contains(ranking, p => p.Edit?.Kind == TerrainEditKind.Burn);
        Assert.Contains(ranking, p => p.Edit?.Kind == TerrainEditKind.Fence);

        // 同一落点既有"不改造"也有"带改造"（两者是独立候选，不会被静默合并）。
        Assert.Contains(
            ranking.GroupBy(p => p.Coord),
            g => g.Any(p => p.Edit is null) && g.Any(p => p.Edit is not null));

        // 枚举出来的每个改造目标都确实合法（经唯一实现），不是 AI 自己拍脑袋造的。
        Assert.All(
            ranking.Where(p => p.Edit is not null),
            p => Assert.True(TerrainEditRules.IsLegal(match.Board.Map, p.Coord, p.Edit!.Value)));
    }

    [Fact]
    public void 非匠人没有改造候选()
    {
        MatchFlow match = Match();
        (StagedBatch batch, SettlementDriver driver) = Staging(match, PieceType.Fortress, 1, "C4", "E4");

        ImmutableArray<PointScore> ranking = Rank(Ai(match), batch, driver);

        Assert.NotEmpty(ranking);
        Assert.All(ranking, p => Assert.Null(p.Edit));
    }

    [Fact]
    public void 选中的批次连改造一起摆回暂放()
    {
        // 复摆漏 Edit 会静默丢改造：匠人照落、地形不动，而候选分是按带改造算的。
        // 变异 M-B6：Deploy 末尾的复摆去掉 placement.Edit → 本条红。
        MatchFlow match = Match();
        (StagedBatch batch, SettlementDriver driver) = Staging(match, PieceType.Artisan, 3, "C4", "E4");
        HeuristicTurnController ai = Ai(match);

        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));

        CandidateBatch choice = ai.LastChoice!;
        Assert.NotEmpty(choice.Placements);
        Assert.Equal(
            choice.Placements.Select(p => p.ToString()),
            batch.Placements.Select(p => p.ToString()));

        // 样本口径下界：这一手确实带了改造，否则"复摆是否保留改造"什么都没测到。
        Assert.Contains(choice.Placements, p => p.Edit is not null);
    }

    [Fact]
    public void 候选去重的键区分带改造与不带改造()
    {
        // CandidateBatch.Key 拼的是 Placement.ToString；不含改造时，"同落点带 / 不带改造"的两个候选
        // 会在 Deploy 的 keys.Add 去重时被吞掉一个。
        var plain = new CandidateBatch(
            [BatchFixtures.P("C4", PieceType.Artisan)], EvaluationBreakdown.Zero(EvaluationWeights.Default));
        var edited = new CandidateBatch(
            [BatchFixtures.Artisan("C4", TerrainEdit.Bridge(TestMaps.At("D4")))],
            EvaluationBreakdown.Zero(EvaluationWeights.Default));

        Assert.NotEqual(plain.Key, edited.Key);
    }
}
