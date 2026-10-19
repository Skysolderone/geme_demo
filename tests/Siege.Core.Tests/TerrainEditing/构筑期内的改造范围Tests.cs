using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.TerrainEditing;

/// <summary>
/// 规格：openspec/changes/open-map/specs/terrain-edit —— Requirement: 构筑期内的改造范围（负责人 2026-10-19 裁决 O6）。
/// 载体：手写的 3+2 紧贴链接档图（<see cref="LinkMapFixtures.Births"/>：棋盘 0 C3–H8 与棋盘 1 J3–O8 在 H / J 列紧贴），在接缝另一侧 J5 加一格林地，
/// 经 <see cref="MatchFlow.CreateUnvalidated"/> 建局（加林地不改任何校验项，但夹具本就只经此口建局）。
/// </summary>
/// <remarks>
/// 变异验证（open-map 1.3 实跑，只跑本类）：M-O3a <c>TerrainEditRules.Reject</c> 的立栅分支不看 scope → 「构筑期内跨接缝立栅被拒」「候选枚举与拒绝理由同源」红 2；
/// M-O3b <c>PublicRules.EditScope</c> 构筑期也返回全图 → 本类红 4（只有「第4大回合起可跨接缝改造」仍绿）。
/// </remarks>
public class 构筑期内的改造范围Tests
{
    private static readonly Coord Seam = TestMaps.At("H5");      // 棋盘 0 东缘
    private static readonly Coord Across = TestMaps.At("J5");    // 接缝另一侧：棋盘 1 西缘（加林地）

    /// <summary>P0 在棋盘 0、P1 在棋盘 1、P2 / P3 在棋盘 3 / 4；P0 手里有匠人。</summary>
    private static MatchFlow Started()
    {
        MapData built = LinkMapFixtures.Build("link:90011", LinkMapFixtures.Births);
        MapData map = built with { TerrainData = LinkMapGeneration.物化状态校验Tests.With(built, surfaces: [("J5", Surface.Forest)]) };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, TestMaps.Seed, All, TestMaps.Relics(map), Options);
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 20), (PieceType.Artisan, 5));
        }

        match.PlantSequentially(All.Select((p, i) => (p, new[] { 0, 1, 3, 4 }[i])));
        Assert.Equal(Surface.Forest, match.Map.SurfaceAt(Across));
        return match;
    }

    [Fact]
    public void 构筑期内跨接缝立栅被拒()
    {
        // Scenario：第 3 大回合，匠人落在自己棋盘靠接缝的格 H5，提交的立栅目标边 H5–J5 一端在相邻的另一块棋盘内 → 该批次非法，原因指出构筑期内改造目标须在自己的出生棋盘内。
        MatchFlow match = Started().AtRound(3, [P0, P1, P2, P3]);
        StagedBatch batch = match.ToDeploy();
        Assert.Equal(match.Map.BirthBoard(0).Cells().ToImmutableHashSet(), batch.Context.EditScope);

        BatchFailure? fence = batch.Stage(Seam, PieceType.Artisan, TerrainEdit.Fence(Seam, Across));
        Assert.Equal(BatchFailureKind.TerrainEditIllegal, fence?.Kind);
        Assert.Contains(TerrainEditRules.OutOfScopeReason, fence!.Message, StringComparison.Ordinal);

        BatchFailure? burn = batch.Stage(Seam, PieceType.Artisan, TerrainEdit.Burn(Across));
        Assert.Equal(BatchFailureKind.TerrainEditIllegal, burn?.Kind);
        Assert.Contains(TerrainEditRules.OutOfScopeReason, burn!.Message, StringComparison.Ordinal);

        // 两端都在自己棋盘内的边照常合法（匠人本身的落点仍受合法落子范围约束）。
        Assert.Null(batch.Stage(Seam, PieceType.Artisan, TerrainEdit.Fence(Seam, TestMaps.At("H6"))));
    }

    [Fact]
    public void 构筑期内候选不含棋盘外目标()
    {
        // Scenario：第 2 大回合枚举某匠人落点（H5）的合法改造目标 → 候选中没有任何一端在其他棋盘内的边，也没有其他棋盘内的格；改造范围不扣禁入格。
        MatchFlow match = Started().AtRound(2, [P0, P1, P2, P3]);
        IReadOnlySet<Coord> scope = match.EditScopeFor(P0);
        HashSet<Coord> board0 = [.. match.Map.BirthBoard(0).Cells()];
        Assert.Equal(board0, scope);
        Assert.Equal(PublicRules.EditScope(match.Board, 0, 2), scope);

        ImmutableArray<TerrainEdit> scoped = TerrainEditRules.LegalTargets(match.Map, Seam, workshop: false, scope);
        ImmutableArray<TerrainEdit> unscoped = TerrainEditRules.LegalTargets(match.Map, Seam, workshop: false);
        Assert.NotEmpty(scoped);
        Assert.All(scoped, e => Assert.True(e.Kind == TerrainEditKind.Fence ? board0.Contains(e.Edge.A) && board0.Contains(e.Edge.B) : board0.Contains(e.Cell), e.ToString()));
        Assert.Contains(unscoped, e => e.Kind == TerrainEditKind.Burn && e.Cell == Across);   // 样本口径：不限范围时接缝另一侧的林地与跨接缝的边都是候选
        Assert.Contains(unscoped, e => e.Kind == TerrainEditKind.Fence && (e.Edge.A == Across || e.Edge.B == Across));
        Assert.DoesNotContain(scoped, e => e.Kind == TerrainEditKind.Burn && e.Cell == Across);
        Assert.DoesNotContain(scoped, e => e.Kind == TerrainEditKind.Fence && (e.Edge.A == Across || e.Edge.B == Across));

        // AI 的改造候选经同一份枚举（HeuristicTurnController 传 BatchContext.EditScope）：匠人改造复盘里立栅的"范围内目标数"= 按范围枚举的目标数之和，小于不限范围时。
        StagedBatch batch = match.ToDeploy();
        HeuristicTurnController ai = HeuristicAi.Create(match, P0, AiDifficulty.Standard, SimFixtures.PreCalibrationWeights, AiSearchConfig.Standard with { CandidateCellLimit = 0 });
        ArtisanEditReview review = ai.ReviewArtisanEdits(batch, match.Rehearse);
        Coord[] legal = [.. batch.Context.LegalRange.Where(c => match.Board[c].IsPlayableEmpty)];
        int scopedFences = legal.Sum(c => TerrainEditRules.LegalTargets(match.Map, c, batch.Context.WorkshopActive, scope).Count(e => e.Kind == TerrainEditKind.Fence));
        int unscopedFences = legal.Sum(c => TerrainEditRules.LegalTargets(match.Map, c, batch.Context.WorkshopActive).Count(e => e.Kind == TerrainEditKind.Fence));
        Assert.True(unscopedFences > scopedFences);   // 样本口径
        Assert.Equal(scopedFences, review.KindReviews.Single(k => k.Kind == TerrainEditKind.Fence).RangeTargets);
    }

    [Fact]
    public void 第4大回合起可跨接缝改造()
    {
        // Scenario：第 4 大回合，匠人落在接缝一侧 H5，对接缝另一侧的林地格 J5 提交烧林 → 该批次合法；改造范围为全图。
        MatchFlow match = Started().AtRound(4, [P0, P1, P2, P3]);
        Assert.Equal(match.Board.AllCoords().ToImmutableHashSet(), match.EditScopeFor(P0));
        StagedBatch batch = match.ToDeploy();
        Assert.Null(batch.Stage(Seam, PieceType.Artisan, TerrainEdit.Burn(Across)));
        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(Surface.Grass, match.Map.SurfaceAt(Across));
    }

    [Fact]
    public void 候选枚举与拒绝理由同源()
    {
        // 带范围的 LegalTargets 与 Reject 口径一致：对棋盘 0 每个格、不限范围时的每个候选，"在带范围的集合里" ⇔ "带范围的 Reject 为 null"；
        // 被范围挡掉的候选全部以 OutOfScopeReason 拒绝。
        MatchFlow match = Started().AtRound(2, [P0, P1, P2, P3]);
        IReadOnlySet<Coord> scope = match.EditScopeFor(P0);
        int blocked = 0;
        foreach (Coord cell in match.Map.BirthBoard(0).Cells())
        {
            foreach (bool workshop in new[] { false, true })
            {
                HashSet<TerrainEdit> scoped = [.. TerrainEditRules.LegalTargets(match.Map, cell, workshop, scope)];
                foreach (TerrainEdit edit in TerrainEditRules.LegalTargets(match.Map, cell, workshop))
                {
                    string? reason = TerrainEditRules.Reject(match.Map, cell, edit, workshop, scope);
                    Assert.Equal(scoped.Contains(edit), reason is null);
                    if (reason is not null)
                    {
                        blocked++;
                        Assert.StartsWith(TerrainEditRules.OutOfScopeReason, reason, StringComparison.Ordinal);
                    }
                }
            }
        }

        Assert.True(blocked > 0);   // 样本口径：棋盘 0 东缘确有跨接缝的候选被挡
    }
}
