using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>
/// 规格：openspec/specs/turn-sequence —— Requirement: 构筑保护期的落子限制 / 第 4 大回合起的全图落子；terrain-edit —— Requirement: 构筑期内的改造范围（big-map 裁决 5，D5）。
/// 大图锁定后出生棋盘 = 旗心 13×13：第 1–3 大回合合法落子范围 ⊆ 13×13 ∩ 可落子 − 禁入，改造范围 = 13×13 的全部格；第 4 大回合起全图可落子 − 禁入。
/// <c>PublicRules.LegalRange</c> / <c>EditScope</c> 不改分支，只靠落座图的出生棋盘（D4）。小图口径（8×8 座位）由既有测试守。
/// </summary>
/// <remarks>
/// 变异验证：由 <c>锁定落座Tests</c> 的 M-S1（<c>FlagRules.BirthPlateOf</c> 原点改为旗 − (4, 4)）一并实跑——本类「前三大回合限旗心13x13」红（合法范围不再以旗为心、改造范围 ≠ 13×13 方域）；本类未另做变异。
/// </remarks>
public class 大图构筑范围Tests
{
    private static readonly Lazy<MapData> LazyLarge = new(() => MapCatalog.Resolve("link:1:4:l"));

    private static MatchFlow StartedLarge()
    {
        MatchFlow match = MatchFlow.Create(LazyLarge.Value, TestMaps.Seed, All, Options);
        match.PlantPrototype();
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        return match;
    }

    [Fact]
    public void 前三大回合限旗心13x13()
    {
        MatchFlow match = StartedLarge();
        foreach (int round in new[] { 1, 2, 3 })
        {
            match.AtRound(round, [P0, P1, P2, P3]);
            foreach (PlayerId p in All)
            {
                Coord flag = match.Flags.FlagCellOf(p)!.Value;
                var box = new HashSet<Coord>();
                var boxPlayable = new HashSet<Coord>();
                for (int dy = -6; dy <= 6; dy++)
                {
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        var c = new Coord(flag.X + dx, flag.Y + dy);
                        box.Add(c);
                        if (match.Map.IsPlayable(c))
                        {
                            boxPlayable.Add(c);
                        }
                    }
                }

                IReadOnlySet<Coord> legal = match.LegalRangeFor(p);
                Assert.True(legal.IsSubsetOf(boxPlayable), $"第 {round} 大回合 {p} 的合法范围越出旗心 13×13。");
                Assert.Equal(boxPlayable.Count, legal.Count);   // 空盘没有禁入格：恰等于 13×13 ∩ 可落子
                Assert.True(legal.Count >= FlagRules.MinRingPlayable);
                Assert.True(match.EditScopeFor(p).SetEquals(box));   // 改造范围 = 13×13 全部格（含不可落子格，不扣禁入）
            }
        }
    }

    [Fact]
    public void 第四大回合起全图()
    {
        MatchFlow match = StartedLarge().AtRound(4, [P0, P1, P2, P3]);
        ImmutableHashSet<Coord> playable = [.. match.Map.AllCoords().Where(match.Map.IsPlayable)];
        foreach (PlayerId p in All)
        {
            Assert.True(match.LegalRangeFor(p).SetEquals(playable));
            Assert.Equal(match.Map.Width * match.Map.Height, match.EditScopeFor(p).Count);
        }

        Assert.True(playable.Count >= 2300);   // large-60：校验器大图行的可落子下限
    }

    [Fact]
    public void 构筑期内越出旗心的暂放被拒()
    {
        // 第 1 大回合：当前玩家在自己 13×13 内暂放被接受；在别人旗心（相距 ≥ 旗距）暂放被拒。
        MatchFlow match = StartedLarge();
        PlayerId mover = match.CurrentPlayer!.Value;
        PlayerId other = All.First(p => p != mover);
        match.BeginTurn();
        match.EnterRecruit();
        Batch.StagedBatch batch = match.EnterDeploy();
        Coord own = match.Flags.FlagCellOf(mover)!.Value;
        Coord far = match.Flags.FlagCellOf(other)!.Value;
        Assert.Contains(own, batch.Context.LegalRange);
        Assert.DoesNotContain(far, batch.Context.LegalRange);
        Assert.True(FlagRules.Distance(own, far) >= FlagRules.FlagGap);
    }
}
