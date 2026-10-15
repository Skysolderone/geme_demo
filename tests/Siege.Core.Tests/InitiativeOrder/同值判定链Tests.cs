using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Flow = Siege.Core.Match.InitiativeOrder;

namespace Siege.Core.Tests.InitiativeOrder;

/// <summary>规格：initiative-order —— Requirement: 同值判定链</summary>
public class 同值判定链Tests
{
    private static InitiativeEntry Entry(PlayerId p, int value, int bonus, long power, int? previous = null, int seedRank = 0) =>
        new(p, 4 - value + bonus, power, bonus, value, previous, seedRank);

    [Fact]
    public void 先手修正打破平局()
    {
        // 设计文档 §11.2：先手值均为 2，一人修正 +2、另一人 0 → +2 者行动更早。
        // 变异验证 M-I5：Comparer 删除第 1 级（修正）→ 红 1（本测试：P1 势力更高会先行）。
        InitiativeReport report = Flow.Generate(3, [Entry(TestMaps.P0, 2, 2, 12), Entry(TestMaps.P1, 2, 0, 55)]);
        Assert.Equal(new[] { TestMaps.P0, TestMaps.P1 }, report.NextOrder);
    }

    [Fact]
    public void 势力值打破平局()
    {
        // 设计文档 §11.2：先手值与修正均相同，势力 60 与 45 → 60 者行动更早。
        // 变异验证 M-I6：Comparer 删除第 2 级（势力）→ 红 1（本测试：上轮位置让 P0 先行）。
        InitiativeReport report = Flow.Generate(3, [Entry(TestMaps.P0, 2, 0, 45, previous: 2), Entry(TestMaps.P1, 2, 0, 60, previous: 0)]);
        Assert.Equal(new[] { TestMaps.P1, TestMaps.P0 }, report.NextOrder);
    }

    [Fact]
    public void 上轮顺序打破平局()
    {
        // 设计文档 §11.2：先手值、修正与势力全部相同，上一大回合分别第 1 位与第 3 位行动 → 第 3 位者本次更早。
        // 变异验证 M-I7：第 3 级改为"上轮更早者优先"（pa.CompareTo(pb)）→ 红 1（本测试）。
        InitiativeReport report = Flow.Generate(3, [Entry(TestMaps.P0, 2, 0, 45, previous: 0, seedRank: 0), Entry(TestMaps.P1, 2, 0, 45, previous: 2, seedRank: 1)]);
        Assert.Equal(new[] { TestMaps.P1, TestMaps.P0 }, report.NextOrder);
    }

    [Fact]
    public void 首回合种子兜底()
    {
        // 设计文档 §11.2：第一大回合结束时全部判定条件相同且无上一大回合可比 → 按对局种子给出稳定顺序，同种子重放一致。
        // 纯函数：previous 为 null 时按 SeedRank；流程：四人各在自己出生区对称位置落 1 子，势力全等 → 下一轮顺序 = 种子顺序。
        // 变异验证 M-I8：Comparer 末尾返回 0（忽略 SeedRank）→ 红 1（本测试：OrderBy 稳定排序退化为玩家编号序，与选定种子的 SeedRank 不同）。
        InitiativeReport pure = Flow.Generate(1, [Entry(TestMaps.P0, 3, 0, 5, seedRank: 1), Entry(TestMaps.P1, 3, 0, 5, seedRank: 0)]);
        Assert.Equal(new[] { TestMaps.P1, TestMaps.P0 }, pure.NextOrder);

        // 选一个 SeedRank 顺序不是 P0>P1>P2>P3 的种子，让断言真正依赖种子。
        GameSeed seed = Enumerable.Range(1, 50).Select(v => new GameSeed((ulong)v))
            .First(s => !LinkModeFixtures.Started([0, 1, 2, 3], seed: s).PlayEqualRound().InitiativeReports[0].NextOrder.SequenceEqual(TestMaps.All));

        MatchFlow a = LinkModeFixtures.Started([0, 1, 2, 3], seed: seed).PlayEqualRound();
        MatchFlow b = LinkModeFixtures.Started([0, 1, 2, 3], seed: seed).PlayEqualRound();
        InitiativeReport report = a.InitiativeReports.Single();
        Assert.All(report.Entries, e => Assert.Null(e.PreviousPosition));
        Assert.Single(report.Entries.Select(e => e.Value).Distinct());
        Assert.Single(report.Entries.Select(e => e.Power).Distinct());
        Assert.Equal(report.Entries.OrderBy(e => e.SeedRank).Select(e => e.Player), report.NextOrder);
        Assert.Equal(report.NextOrder, b.InitiativeReports.Single().NextOrder);
        Assert.NotEqual(TestMaps.All, report.NextOrder);
    }
}

file static class EqualRound
{
    /// <summary>
    /// 第 1 大回合四人各在自己出生棋盘的对称位置落 1 子（每人 1 子 + 4 独占格 = 势力 5）。
    /// retire-legacy-rules 1.5：载体由 9×9 旧模式夹具（四角出生区的 B2 / H2 / B8 / H8）换为 link:1（四人各锁一块出生棋盘），对称位置取核心区中心（四邻都是平坦的核心区格）。
    /// </summary>
    internal static MatchFlow PlayEqualRound(this MatchFlow match)
    {
        for (int i = 0; i < 4; i++)
        {
            int zone = match.StateOf(match.CurrentPlayer!.Value).BirthZone!.Value;
            match.PlayTurn(LinkModeFixtures.CoreCell(match.Map, zone, 1, 1).ToNotation());
        }

        return match;
    }
}
