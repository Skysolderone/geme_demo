using System.Reflection;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.Recruitment;

/// <summary>规格：recruitment —— Requirement: 征募的信息边界</summary>
public class 征募的信息边界Tests
{
    [Fact]
    public void 对手不可见()
    {
        // 设计文档 §13.2 / §15.1：A 征募期间，B/C/D 及其对战 AI 拿到的只有 HandLedger 的公开成员与自己的句柄。
        // 结构级断言：HandLedger 的 public 成员不返回 RecruitPanelView / HandPrivateView；B 的句柄只能读 B 自己的面板（B 不在征募阶段 → 抛出）。
        // 变异验证 M-R16：HandLedger.PanelOf 改 public → 红 1（本测试）。「句柄读别人的面板」这个变异无从表达——PlayerHandAccess 的方法没有玩家参数，这正是结构性边界。
        foreach (MemberInfo member in typeof(HandLedger).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Type? returned = member switch
            {
                MethodInfo m => m.ReturnType,
                PropertyInfo p => p.PropertyType,
                _ => null,
            };
            Assert.False(returned == typeof(RecruitPanelView) || returned == typeof(HandPrivateView),
                $"HandLedger 的公开成员 {member.Name} 泄漏了私有类型 {returned?.Name}");
        }

        // 句柄的公开方法一律不带玩家参数：B 的句柄在类型上就表达不出"读 A 的面板"
        foreach (MethodInfo m in typeof(PlayerHandAccess).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType == typeof(PlayerId));
        }

        HandLedger ledger = HandFixtures.Ledger();
        PlayerHandAccess a = HandFixtures.Begin(ledger, HandFixtures.P0);
        RecruitPanelView panel = a.EnterRecruit();
        Assert.Equal(5, panel.ShowCount);

        foreach (PlayerId other in new[] { HandFixtures.P1, HandFixtures.P2, HandFixtures.P3 })
        {
            PlayerHandAccess handle = ledger.AccessFor(other);
            Assert.Equal(other, handle.Player);
            Assert.Throws<SiegeRuleException>(() => handle.Panel());
            Assert.Throws<SiegeRuleException>(() => handle.Pick(0));
            HandPublicView view = ledger.PublicView(HandFixtures.P0);
            Assert.Equal([PieceType.Basic], view.Types);
        }
    }

    [Fact]
    public void 选择过程不可见()
    {
        // 设计文档 §13.2：A 完成选取后，其他玩家只能拿到 A 的公开视图——类型集合会随选取变化（这是 §13.1 公开的），
        // 但公开视图与征募记录都不经公开路径暴露"选了哪些候选位 / 未选的候选"；面板在 A 结束小回合后消失。
        // 变异验证 M-R18：HandPublicView 加一个 PickedIndices 字段 → 红 1（本测试的结构断言）。
        GameSeed seed = HandFixtures.SeedWhere(p => p.Contains(PieceType.Fortress));
        HandLedger ledger = HandFixtures.Ledger(seed);
        PlayerHandAccess a = HandFixtures.Begin(ledger, HandFixtures.P0);
        RecruitPanelView panel = a.EnterRecruit();
        a.Pick(panel.IndicesOf(PieceType.Fortress)[0]);

        HandPublicView view = ledger.PublicView(HandFixtures.P0);
        Assert.Equal([PieceType.Basic, PieceType.Fortress], view.Types);
        PropertyInfo[] props = typeof(HandPublicView).GetProperties();
        Assert.Equal(["IsActing", "IsEmpty", "Player", "Types"], props.Select(p => p.Name).Order());

        ledger.DeductHand(HandFixtures.P0, HandFixtures.Deployed((PieceType.Basic, 1)));
        ledger.EndTurn(HandFixtures.P0);
        Assert.Throws<SiegeRuleException>(() => a.Panel());
    }

    [Fact]
    public void 调试旁路显式标记为测试专用()
    {
        // 设计文档 §15.3：调试 AI 的全量读取走 internal 旁路，只对 InternalsVisibleTo 的测试程序集可达。
        // 变异验证 M-R19：HandDebugAccess 改 public → 红 1（本测试）。
        Assert.False(typeof(HandDebugAccess).IsPublic);
        Assert.False(typeof(HandLedger).GetProperty("Debug", BindingFlags.Public | BindingFlags.Instance) is not null);

        HandLedger ledger = HandFixtures.Ledger();
        PlayerHandAccess a = HandFixtures.Begin(ledger, HandFixtures.P0);
        RecruitPanelView panel = a.EnterRecruit();
        Assert.Equal(panel, ledger.Debug.PanelOf(HandFixtures.P0));
        Assert.Equal(a.PrivateView(), ledger.Debug.PrivateViewOf(HandFixtures.P0));
    }
}
