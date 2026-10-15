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
/// tasks 3.1 点名的「AI 在能一手立栅提子时选择该手」在段 B 的旧边目标口径下不可实现（立栅永远提不了子），
/// 裁决 T-11 放宽边目标后成立，见 <see cref="AI在能一手立栅提子时选择该手"/>。本文件另钉三件事：
/// ① 枚举确实同时产出"带改造"与"不改造"两类候选，三种动作都在；
/// ② 选中的批次<b>连改造一起</b>摆回暂放（复摆漏 Edit 会静默丢改造）；
/// ③ 非匠人一律没有改造候选。
/// 全部用 Standard 难度：Easy 的征募前瞻只看基础军势，匠人进得了面板却几乎不落盘（段 A 检查第 4 项）。
/// </remarks>
public class AI枚举改造目标Tests
{
    private static readonly PlayerId Me = TestMaps.P0;

    /// <summary>F6 / F7 深水、H6 林地的对局（retire-legacy-rules：平坦 8×8 链接载体、四人同锁棋盘 0，原 9×9 坐标平移 (+2, +2)），第 5 大回合。</summary>
    private static MatchFlow Match() => LinkModeFixtures
        .PlainStarted(TestMaps.Terrain(surfaces: [("F6", Surface.DeepWater), ("F7", Surface.DeepWater), ("H6", Surface.Forest)]))
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

    /// <summary>
    /// 与 <see cref="Match"/> 同地形，但把 G6 的 16 条候选边（T-11）预先封掉 14 条，只留 G5–G6 与 G4–G5。
    /// 不这么做，单点排行榜的 12 个名额会被同分的栅栏候选占满，烧林（记法 <c>X:</c> 排在最后）被挤出榜外——
    /// 这条要钉的是"三种动作都进了枚举"，不是"三种动作都挤得进前 12"（挤占本身记在 implement.md 供段 D 参考）。
    /// </summary>
    private static MatchFlow Boxed() => LinkModeFixtures
        .PlainStarted(TestMaps.Terrain(
            surfaces: [("F6", Surface.DeepWater), ("F7", Surface.DeepWater), ("H6", Surface.Forest)],
            fences:
            [
                ("G5", "F5"), ("G5", "H5"),
                ("F6", "F5"), ("F6", "E6"), ("F6", "G6"), ("F6", "F7"),
                ("H6", "H5"), ("H6", "G6"), ("H6", "J6"), ("H6", "H7"),
                ("G7", "G6"), ("G7", "F7"), ("G7", "H7"), ("G7", "G8"),
            ]))
        .AtRound(5);

    [Fact]
    public void 单点枚举同时产出带改造与不改造两类候选()
    {
        // D-J：对每枚暂放匠人枚举"全部合法目标 + 不改造"。同一落点因此会出现多个候选，只差在改造上。
        // 变异 M-B5：RankPoints 只 yield null（不枚举改造）→ 本条红。
        MatchFlow match = Boxed();
        (StagedBatch batch, SettlementDriver driver) = Staging(match, PieceType.Artisan, 1, "G6");

        ImmutableArray<PointScore> ranking = Rank(Ai(match), batch, driver);

        Assert.Contains(ranking, p => p.Edit is null);
        Assert.Contains(ranking, p => p.Edit is not null);

        // 三种动作都被枚举到：G6 旁是深水 F6 与林地 H6，还剩两条没封的边可立栅。
        Assert.Contains(ranking, p => p.Edit?.Kind == TerrainEditKind.Bridge);
        Assert.Contains(ranking, p => p.Edit?.Kind == TerrainEditKind.Burn);
        Assert.Contains(ranking, p => p.Edit?.Kind == TerrainEditKind.Fence);

        // T-11：留下的两条边里有一条是**外圈边**（G4–G5 不以落点 G6 为端），放宽后的口径确实进了 AI 枚举。
        Assert.Contains(ranking, p => p.Edit == TerrainEdit.Fence(TestMaps.At("G4"), TestMaps.At("G5")));

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
    public void AI在能一手立栅提子时选择该手()
    {
        // tasks 3.1 点名的验证：P1 孤子 G7 被堵到只剩 G7–G8 一口气，AI 手里只有匠人且只能落 G9。
        // G9 不与 G7 相邻——只有 T-11 放宽后的外圈边 G7–G8 才是它的合法目标，带上它当场提子；
        // 同一落点不带改造提不到子。AI 必须选带栅栏那一手。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5);
        GameBoard board = match.Board;
        board.Place(TestMaps.At("G7"), TestMaps.P1, PieceType.Basic);
        foreach (string cell in new[] { "G6", "F7", "H7" })
        {
            board.Place(TestMaps.At(cell), Me, PieceType.Basic);
        }

        (StagedBatch batch, SettlementDriver driver) = Staging(match, PieceType.Artisan, 1, "G9");
        HeuristicTurnController ai = Ai(match);
        ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));

        Placement chosen = Assert.Single(ai.LastChoice!.Placements);
        Assert.Equal(TestMaps.At("G9"), chosen.Coord);
        Assert.Equal(TerrainEdit.Fence(TestMaps.At("G7"), TestMaps.At("G8")), chosen.Edit);

        // 该手确实提子（不是"碰巧评分最高"）：复摆后预演的提子集合是 G7。
        Assert.Equal(["G7"], driver.Rehearse(batch.Context, batch.Placements).Captures.Select(c => c.Coord).Notations());
    }

    [Fact]
    public void 非匠人没有改造候选()
    {
        MatchFlow match = Match();
        (StagedBatch batch, SettlementDriver driver) = Staging(match, PieceType.Fortress, 1, "E6", "G6");

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
        (StagedBatch batch, SettlementDriver driver) = Staging(match, PieceType.Artisan, 3, "E6", "G6");
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
