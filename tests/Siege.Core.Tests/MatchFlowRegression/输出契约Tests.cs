using System.Reflection;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.MatchFlowRegression;

/// <summary>implement 8.1：六个输出契约的边界输入——未知玩家响亮失败、公开快照不含私有类型、插旗未锁定时无合法范围。</summary>
public class 输出契约Tests
{
    private static readonly PlayerId Stranger = new(9);

    [Fact]
    public void 未知玩家一律抛SiegeRuleException()
    {
        // boundaries.md「显式输入的名册：未知玩家必须响亮失败」。
        // 变异验证 M-C6（check 自做）：Require 对未知玩家返回新建的 PlayerRecord → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started();
        Assert.Throws<SiegeRuleException>(() => match.StateOf(Stranger));
        Assert.Throws<SiegeRuleException>(() => match.LegalRangeFor(Stranger));
        Assert.Throws<SiegeRuleException>(() => match.Resign(Stranger));
        Assert.Throws<SiegeRuleException>(() => match.Flags.Plant(Stranger, 0));
        Assert.Throws<SiegeRuleException>(() => new MatchRunner(match).SetController(Stranger, new RandomTurnController(MatchFixtures.Seed.Stream("x"))));
    }

    [Fact]
    public void 公开快照的结构里不存在私有视图类型()
    {
        // boundaries.md「视图分离」：公开视图的结构里根本不存在私有字段——靠"约定不去读"守不住。
        Type[] forbidden = [typeof(HandPrivateView), typeof(RecruitPanelView), typeof(StagedBatch), typeof(PlayerHandAccess), typeof(HandLedgerState), typeof(MatchSaveData)];
        foreach (PropertyInfo p in typeof(MatchPublicView).GetProperties())
        {
            Type t = p.PropertyType;
            Type[] involved = t.IsGenericType ? [t, .. t.GetGenericArguments()] : [t];
            Assert.All(involved, x => Assert.DoesNotContain(x, forbidden));
            Assert.DoesNotContain("Private", p.Name, StringComparison.OrdinalIgnoreCase);
        }

        // 发布的盘面是独立副本：之后权威盘面的变化不反映到快照里
        MatchFlow match = MatchFixtures.Started().AtRound(5);
        MatchPublicView view = match.Publish();
        match.Stones(MatchFixtures.P0, "E5");
        Assert.Null(view.Board[TestMaps.At("E5")].Occupant);
        Assert.NotEqual(view.BoardSerialized, match.Board.Serialize());
    }

    [Fact]
    public void 插旗未锁定时不提供合法落子范围()
    {
        MatchFlow match = MatchFixtures.Create();
        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => match.LegalRangeFor(MatchFixtures.P0));
        Assert.Contains("FlagPlanting", ex.Message);
        Assert.Null(match.CurrentPlayer);
        Assert.Equal(0, match.MajorRound);
    }
}
