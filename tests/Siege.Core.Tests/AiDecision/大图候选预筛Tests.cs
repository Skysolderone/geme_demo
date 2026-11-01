using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision —— Requirement: 大图局部候选预筛（big-map 裁决 10 / D8）。只在大图（<c>MapTier.Large</c>）上把 AI 的候选枚举范围收窄到
/// "己方任一棋子或己方旗心切比雪夫 ≤ 8 的格 ∪ 对手任一棋子切比雪夫 ≤ 2 的格"∩ 合法范围；己方无子时 = 旗心 13×13 ∩ 合法范围。小图原样返回合法范围（同一引用）。
/// 唯一实现 <see cref="LargeMapFocus.LocalRange"/>；部署决策、搭桥候选、匠人诊断与专家前瞻（模拟对手、两层扫描）都经它。
/// </summary>
/// <remarks>
/// <para>夹具：<c>link:1:4:l</c> 按原型路径插旗锁定（四面旗相距 ≥ 60），摆到第 4 大回合（全图阶段）；棋子直接摆在旗格上（旗格必是可落子的低地）。</para>
/// <para>变异验证（段 C implement 实跑，备份 → 改 → 跑本类与 15 份决策基线 → 逐字节还原）：
/// M-C1 <c>LargeMapFocus.LocalRange</c> 的档位守门 <c>view.Tier != MapTier.Large</c> 改为恒假（小图也预筛）→ 「小图原样返回合法范围同一引用」红、决策序列基线 <c>链接图决策序列与基线相同</c>（种子 1 / 2）红
/// （「小图部署候选为全部合法空格」仍绿：平坦夹具的棋盘 0 整块都在 E5 / F5 的 8 格内，预筛恰为恒等——这条守的是 K = 0 的既有契约，不是档位守门）；
/// M-C2 <c>OwnReach</c> 8 → 7 → 「有子时为己方八格并对手两格」红（期望集合多出距离恰 8 的一圈）；
/// M-C3 <c>HeuristicTurnController.ScorePoints</c> 改回按 <c>context.LegalRange</c> 枚举 → 「部署候选只在局部范围内」「专家前瞻与搭桥候选也在局部范围内」红（预演次数 ≥ 合法空格数 / 4）。</para>
/// </remarks>
public class 大图候选预筛Tests
{
    private static readonly Lazy<MapData> LazyLarge = new(() => MapCatalog.Resolve("link:1:4:l"));

    /// <summary>大图锁定后摆到第 4 大回合（全图阶段），每人 50 枚普通子，P0 首位行动。</summary>
    private static MatchFlow LargeAtRound4(GameSeed? seed = null)
    {
        MatchFlow match = MatchFlow.Create(LazyLarge.Value, seed ?? TestMaps.Seed, All, Options);
        match.PlantPrototype();
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        return match.AtRound(4, [P0, P1, P2, P3]);
    }

    private static Coord FlagOf(MatchFlow match, PlayerId p) => match.Flags.FlagCellOf(p)!.Value;

    private static IReadOnlySet<Coord> FocusOf(MatchFlow match, PlayerId p) =>
        LargeMapFocus.LocalRange(match.Publish(), match.Board, p, match.LegalRangeFor(p));

    [Fact]
    public void 有子时为己方八格并对手两格()
    {
        MatchFlow match = LargeAtRound4();
        Coord mine = FlagOf(match, P0);
        Coord rival = FlagOf(match, P1);
        match.Stones(P0, mine.ToNotation()).Stones(P1, rival.ToNotation());
        IReadOnlySet<Coord> legal = match.LegalRangeFor(P0);

        IReadOnlySet<Coord> focus = FocusOf(match, P0);

        // 算例：合法范围 ∩（旗心 / 己方子 ≤ 8 ∪ 对手子 ≤ 2），两片相距 ≥ 60、互不相交。
        var expected = legal.Where(c => FlagRules.Distance(c, mine) <= LargeMapFocus.OwnReach || FlagRules.Distance(c, rival) <= LargeMapFocus.RivalReach).ToHashSet();
        Assert.True(focus.SetEquals(expected));
        Assert.True(focus.Count < legal.Count / 10, $"局部范围 {focus.Count} 应远小于合法范围 {legal.Count}");
        Assert.Contains(focus, c => FlagRules.Distance(c, rival) == 2);
        Assert.DoesNotContain(focus, c => FlagRules.Distance(c, rival) is >= 3 and <= 20);
        Assert.Contains(focus, c => FlagRules.Distance(c, mine) == 8);
        Assert.DoesNotContain(focus, c => FlagRules.Distance(c, mine) is >= 9 and <= 20);
        Assert.True(focus.IsSubsetOf(legal));
    }

    [Fact]
    public void 无子时为旗心十三格方域()
    {
        MatchFlow match = LargeAtRound4();
        Coord mine = FlagOf(match, P0);
        // 对手子贴在己方 13×13 之外（距旗心 8）：无子时不并入对手周边，范围仍是旗心 13×13 ∩ 合法。
        Coord near = Enumerable.Range(0, 17).SelectMany(dy => Enumerable.Range(0, 17).Select(dx => new Coord(mine.X - 8 + dx, mine.Y - 8 + dy)))
            .First(c => FlagRules.Distance(c, mine) == 8 && match.Map.IsPlayable(c));
        match.Stones(P1, near.ToNotation(), FlagOf(match, P1).ToNotation());
        IReadOnlySet<Coord> legal = match.LegalRangeFor(P0);
        BoardPlate plate = match.Map.BirthBoard(match.Publish().Players.Single(p => p.Player == P0).BirthZone!.Value);

        IReadOnlySet<Coord> focus = FocusOf(match, P0);

        Assert.Equal(13, plate.Width);
        Assert.Equal(mine, new Coord(plate.Origin.X + 6, plate.Origin.Y + 6));
        Assert.True(focus.SetEquals(legal.Where(plate.Contains)));
        Assert.DoesNotContain(focus, c => FlagRules.Distance(c, near) <= 2 && !plate.Contains(c));
    }

    [Fact]
    public void 小图原样返回合法范围同一引用()
    {
        MatchFlow match = AiFixtures.Round5().Stones(P0, "E5").Stones(P1, "J5");
        IReadOnlySet<Coord> legal = match.LegalRangeFor(P0);
        Assert.Equal(MapTier.Small, match.Publish().Tier);

        Assert.Same(legal, LargeMapFocus.LocalRange(match.Publish(), match.Board, P0, legal));
    }

    [Fact]
    public void 小图部署候选为全部合法空格()
    {
        // 既有契约（候选格上限 K = 0）：进入完整枚举的格 = 全部合法空格；本条守"小图不经过预筛"。
        MatchFlow match = AiFixtures.Round5().Stones(P0, "E5", "F5").Stones(P1, "J5");
        HeuristicTurnController ai = HeuristicAi.Create(match, P0, AiDifficulty.Standard, config: AiSearchConfig.Standard with { CandidateCellLimit = 0 });
        StagedBatch batch = match.OpenDeploy();
        Assert.Equal(P0, match.CurrentPlayer);

        ai.Deploy(batch, match.Rehearse);

        ImmutableArray<Coord> all = [.. batch.Context.LegalRange.Where(c => match.Board[c].IsPlayableEmpty).Order()];
        Assert.Equal(all, ai.LastCandidateCells);
    }

    [Fact]
    public void 部署候选只在局部范围内()
    {
        MatchFlow match = LargeAtRound4();
        match.Stones(P0, FlagOf(match, P0).ToNotation()).Stones(P1, FlagOf(match, P1).ToNotation());
        HeuristicTurnController ai = HeuristicAi.Create(match, P0, AiDifficulty.Standard, config: AiSearchConfig.ForMap(AiDifficulty.Standard, match.Map.PlayableCount));
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, ai.Config.CandidateCellLimit);
        StagedBatch batch = match.OpenDeploy();
        Assert.Equal(P0, match.CurrentPlayer);
        IReadOnlySet<Coord> focus = LargeMapFocus.LocalRange(match.Publish(), match.Board, P0, batch.Context.LegalRange);
        int rehearsals = 0;

        ai.Deploy(batch, () =>
        {
            rehearsals++;
            return match.Rehearse();
        });

        Assert.NotEmpty(ai.LastCandidateCells);
        Assert.True(ai.LastCandidateCells.All(focus.Contains));
        Assert.False(ai.LastChoice!.IsPass);
        Assert.True(ai.LastChoice.Placements.All(p => focus.Contains(p.Coord)));
        // 不预筛时仅候选格上限的廉价预演就要对约 1.5 万个合法空格各预演一次；预筛后预演总数 ≤ 局部范围 + 组批开销。
        Assert.True(rehearsals < batch.Context.LegalRange.Count / 4, $"预演 {rehearsals} 次，合法格 {batch.Context.LegalRange.Count}");
        Assert.True(rehearsals <= focus.Count + 800, $"预演 {rehearsals} 次，局部范围 {focus.Count} 格");
    }

    [Fact]
    public void 大图同种子两次决策相同()
    {
        string First()
        {
            MatchFlow match = LargeAtRound4(new GameSeed(11));
            match.Stones(P0, FlagOf(match, P0).ToNotation()).Stones(P2, FlagOf(match, P2).ToNotation());
            HeuristicTurnController ai = HeuristicAi.Create(match, P0, AiDifficulty.Standard, config: AiSearchConfig.ForMap(AiDifficulty.Standard, match.Map.PlayableCount));
            StagedBatch batch = match.OpenDeploy();
            ai.Deploy(batch, match.Rehearse);
            return $"{ai.LastChoice!.Key}|{string.Join(",", ai.LastCandidateCells)}|{string.Join(";", ai.Decisions)}";
        }

        string a = First();
        string b = First();

        Assert.Equal(a, b);
        Assert.DoesNotContain("D:pass", a, StringComparison.Ordinal);
    }

    [Fact]
    public void 专家前瞻与搭桥候选也在局部范围内()
    {
        MatchFlow match = LargeAtRound4();
        match.Stones(P0, FlagOf(match, P0).ToNotation()).Stones(P1, FlagOf(match, P1).ToNotation()).Stones(P2, FlagOf(match, P2).ToNotation());
        match.Debug.SeedHand(P0, (PieceType.Artisan, 5));
        HeuristicTurnController ai = HeuristicAi.Create(match, P0, AiDifficulty.Expert, config: AiSearchConfig.ForMap(AiDifficulty.Expert, match.Map.PlayableCount));
        Assert.True(ai.Config.LookaheadWidth > 0);
        Assert.Equal(1, ai.Config.EditSeed);
        StagedBatch batch = match.OpenDeploy();
        Assert.Equal(P0, match.CurrentPlayer);
        IReadOnlySet<Coord> focus = LargeMapFocus.LocalRange(match.Publish(), match.Board, P0, batch.Context.LegalRange);
        int legalEmpty = batch.Context.LegalRange.Count(c => match.Board[c].IsPlayableEmpty);
        int rehearsals = 0;

        ai.Deploy(batch, () =>
        {
            rehearsals++;
            return match.Rehearse();
        });

        Assert.True(ai.LastCandidateCells.All(focus.Contains));
        // 搭桥候选：只对局部范围内、未进完整枚举的空格枚举搭桥目标（每格至多 4 个水邻），不再扫全图。
        Assert.NotNull(ai.LastBridgeSeed);
        Assert.True(ai.LastBridgeSeed!.Scored <= 4 * (focus.Count - ai.LastCandidateCells.Length), $"搭桥单点 {ai.LastBridgeSeed.Scored}，局部范围 {focus.Count}");
        Assert.True(rehearsals < legalEmpty / 4, $"本人预演 {rehearsals} 次，合法空格 {legalEmpty}");
        // 专家前瞻：模拟对手经同一 Deploy 入口、以对手自己的棋子为锚；不预筛时每名模拟对手仅候选格上限的廉价预演就 ≥ 合法空格数。
        Assert.NotNull(ai.LastLookahead);
        Assert.Equal(LookaheadStatus.Applied, ai.LastLookahead!.Status);
        Assert.True(ai.LastLookahead.SimulatedRehearsals > 0);
        Assert.True(ai.LastLookahead.SimulatedRehearsals < legalEmpty, $"模拟对手预演 {ai.LastLookahead.SimulatedRehearsals} 次，合法空格 {legalEmpty}");
    }
}
