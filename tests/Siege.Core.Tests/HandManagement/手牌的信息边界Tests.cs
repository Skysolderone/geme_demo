using System.Collections;
using System.Reflection;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.HandManagement;

/// <summary>规格：hand-management —— Requirement: 手牌的信息边界</summary>
public class 手牌的信息边界Tests
{
    [Fact]
    public void 类型公开()
    {
        // 设计文档 §13.1：A 持普通子×6、堡垒子×1 → 其他玩家可见 A 持有普通子与堡垒子两种类型。
        // 变异验证 M-H16：PublicView 返回空类型集合 → 红 9，含本测试与弃赛者手牌类型保留展示。
        HandLedger ledger = HandFixtures.Ledger();
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 6), (PieceType.Fortress, 1));

        HandPublicView view = ledger.PublicView(HandFixtures.P0);

        Assert.Equal([PieceType.Basic, PieceType.Fortress], view.Types);
        Assert.True(view.IsActing);
        Assert.False(view.IsEmpty);
        Assert.Equal(4, ledger.PublicViews().Length);
    }

    [Fact]
    public void 数量隐藏()
    {
        // 设计文档 §13.2 / boundaries.md「视图分离」：公开视图是另一个类型，结构上不存在任何数量字段——
        // 没有 int/long 属性、没有字典、没有 HandEntry；类型集合是唯一的集合成员。
        // 变异验证 M-H17：HandPublicView 加 `int TotalCount` → 红 2（本测试 + 选择过程不可见）；把 Types 改成字典类型会在账本构造处直接编译失败。
        HandLedger ledger = HandFixtures.Ledger();
        ledger.Debug.SeedHand(HandFixtures.P0, (PieceType.Basic, 6), (PieceType.Fortress, 1));
        HandPublicView view = ledger.PublicView(HandFixtures.P0);

        foreach (PropertyInfo prop in typeof(HandPublicView).GetProperties())
        {
            Type t = prop.PropertyType;
            Assert.False(t == typeof(int) || t == typeof(long) || t == typeof(HandEntry), $"公开视图含数量字段 {prop.Name}: {t.Name}");
            Assert.False(typeof(IDictionary).IsAssignableFrom(t) || t.IsGenericType && t.GetGenericTypeDefinition().Name.Contains("Dictionary", StringComparison.Ordinal),
                $"公开视图含映射字段 {prop.Name}: {t.Name}");
            Assert.False(prop.Name.Contains("Count", StringComparison.OrdinalIgnoreCase) || prop.Name.Contains("Entr", StringComparison.OrdinalIgnoreCase),
                $"公开视图含疑似数量字段 {prop.Name}");
        }

        Assert.Equal(typeof(System.Collections.Immutable.ImmutableSortedSet<PieceType>), typeof(HandPublicView).GetProperty("Types")!.PropertyType);
        // 公开字段不经 GetProperties 暴露，单独封死
        Assert.Empty(typeof(HandPublicView).GetFields(BindingFlags.Public | BindingFlags.Instance));
        // 序列化文本也不含数量
        Assert.DoesNotContain("6", view.ToString());
        Assert.Equal(view, ledger.PublicView(HandFixtures.P0));
    }

    [Fact]
    public void 弃赛者手牌类型保留展示()
    {
        // 设计文档 §14.3：D 弃赛时持普通子与连珠子 → 保留展示这两种类型，并标记不再行动；弃赛后不再有小回合。
        // 变异验证 M-H19：Resign 清空手牌 → 红 2（本测试 + 小回合中途弃赛）；M-H20：Resign 不置 Resigned → 红 2（同上）；
        // check M-C5：PublicView 恒标 IsActing=true → 红 2（本测试 + 小回合中途弃赛）；M-C7：BeginTurn 去掉 Resigned 检查 → 红 1（本测试）。
        HandLedger ledger = HandFixtures.Ledger();
        ledger.Debug.SeedHand(HandFixtures.P3, (PieceType.Basic, 2), (PieceType.Line, 1));

        ledger.Resign(HandFixtures.P3);

        HandPublicView view = ledger.PublicView(HandFixtures.P3);
        Assert.Equal([PieceType.Basic, PieceType.Line], view.Types);
        Assert.False(view.IsActing);
        Assert.Contains("不再行动", view.ToString());
        Assert.Throws<SiegeRuleException>(() => ledger.BeginTurn(HandFixtures.P3, HandFixtures.Snapshot(ledger, HandFixtures.P3)));
        Assert.Throws<SiegeRuleException>(() => ledger.Resign(HandFixtures.P3));
        Assert.True(ledger.PublicView(HandFixtures.P0).IsActing);
    }

    [Fact]
    public void 小回合中途弃赛作废未提交征募()
    {
        // 征募阶段弃赛：本轮新增随之作废（等价于 Pass），记录照常写入，公开类型只保留回合前已有的。
        GameSeed seed = HandFixtures.SeedWhere(p => p.Contains(PieceType.Synergy));
        HandLedger ledger = HandFixtures.Ledger(seed);
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0);
        access.Pick(access.EnterRecruit().IndicesOf(PieceType.Synergy)[0]);

        ledger.Resign(HandFixtures.P0);

        Assert.Equal([PieceType.Basic], ledger.PublicView(HandFixtures.P0).Types);
        Assert.False(ledger.PublicView(HandFixtures.P0).IsActing);
        Assert.Equal(TurnPhase.Idle, ledger.PhaseOf(HandFixtures.P0));
        Assert.Single(ledger.Records);
        Assert.True(ledger.Records[0].Passed);
        Assert.Equal(1, ledger.Records[0].RevokedCount);
    }
}
