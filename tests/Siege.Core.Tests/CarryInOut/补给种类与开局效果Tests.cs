using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 补给种类与开局效果</summary>
/// <remarks>
/// 开局手牌一律按"回合前基数 + 本轮新增"两段投影（<see cref="CarryFixtures.HandText"/>）：开局手牌计入基数（裁决记录 5），带入的换入子也一样。
/// </remarks>
public class 补给种类与开局效果Tests
{
    private static readonly PlayerId P0 = MatchFixtures.P0;
    private static readonly PlayerId P1 = MatchFixtures.P1;
    private static readonly PlayerId P2 = MatchFixtures.P2;

    /// <summary>插旗并锁定（各占各的出生区），把第 1 大回合的行动顺序摆成 P0 先行。</summary>
    private static MatchFlow Locked(MatchOptions options)
    {
        MatchFlow match = MatchFixtures.Create(options: options);
        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        match.Debug.SetOrder(MatchFixtures.All);
        return match;
    }

    [Fact]
    public void 备用子()
    {
        // 规格 Scenario：A 带入备用子开局 → 初始手牌普通子 × 6，占 1 个类型槽；第 1 大回合的部署上限仍为 3。
        MatchFlow match = Locked(CarryFixtures.On((0, CarryFixtures.Spare)));

        Assert.Equal("Basic×6+0", CarryFixtures.HandText(match, P0));
        Assert.Equal(1, match.Hands.Debug.PrivateViewOf(P0).OccupiedSlots);
        Assert.Equal("Basic×5+0", CarryFixtures.HandText(match, P1));

        Assert.Equal(1, match.MajorRound);
        match.BeginTurn();
        Assert.Equal(P0, match.CurrentPlayer);
        Assert.Equal(3, match.CurrentSnapshot!.DeployLimit);
    }

    [Fact]
    public void 换型令()
    {
        // 规格 Scenario：A 带入换型令并指定连珠子 → 普通子 × 4、连珠子 × 1，占 2 个类型槽。
        MatchFlow match = MatchFixtures.Create(options: CarryFixtures.On((0, CarryFixtures.Commission(PieceType.Line))));

        Assert.Equal("Basic×4+0 Line×1+0", CarryFixtures.HandText(match, P0));
        Assert.Equal(2, match.Hands.Debug.PrivateViewOf(P0).OccupiedSlots);
        Assert.Equal(new CarryIn(SupplyKind.Commission, PieceType.Line), match.CarryIns[P0]);
    }

    [Fact]
    public void 征召签()
    {
        // 规格 Scenario：v2 下 B 带入征召签、抽签结果为哨兵子 → B 的初始手牌为普通子 × 4、哨兵子 × 1，公开视图记录"征召签 → 哨兵子"（日志在段 C）。
        // 样本：在种子 1–64 中取第一颗让 P1 的征召签抽出哨兵子的种子（10% 的类型，64 颗里没有的概率 < 0.2%，找不到即响亮失败）。
        MatchOptions options = CarryFixtures.On((1, CarryFixtures.Draft));
        GameSeed? seed = Enumerable.Range(1, 64).Select(i => new GameSeed((ulong)i))
            .Select(s => (GameSeed?)s)
            .FirstOrDefault(s => MatchFixtures.Create(s, options).CarryIns[P1].Type == PieceType.Sentry);
        Assert.True(seed is not null, "样本口径：种子 1–64 中应有 P1 抽出哨兵子的一颗");

        MatchFlow match = MatchFixtures.Create(seed, options);

        Assert.Equal(ContentSet.V2, match.ContentSet);
        Assert.Equal("Basic×4+0 Sentry×1+0", CarryFixtures.HandText(match, P1));
        Assert.Equal(new CarryIn(SupplyKind.DraftLot, PieceType.Sentry), match.CarryIns[P1]);
        Assert.Equal(new CarryIn(SupplyKind.DraftLot, PieceType.Sentry), match.Publish().CarryIns[P1]);
    }

    [Fact]
    public void 不带入者不变()
    {
        // 规格 Scenario：C 未带入 → 普通子 × 5，与引入带入带出之前相同（对照同一种子、关闭带入带出的一局）。
        MatchFlow carried = MatchFixtures.Create(options: CarryFixtures.On((0, CarryFixtures.Spare), (1, CarryFixtures.Commission(PieceType.Fortress))));
        MatchFlow plain = MatchFixtures.Create(options: MatchOptions.Immediate);

        Assert.Equal("Basic×5+0", CarryFixtures.HandText(carried, P2));
        Assert.Equal(CarryFixtures.HandText(plain, P2), CarryFixtures.HandText(carried, P2));
        Assert.False(carried.CarryIns.ContainsKey(P2));
    }

    [Fact]
    public void 效果只在开局()
    {
        // 规格 Scenario：带入备用子的 A 进入第 1 大回合的征募阶段 → 征募面板展示 5 枚、最多免费选取 3 枚，与未带入者相同。
        // 对照：同一种子、关闭带入带出的一局，P0 的快照结构参数与面板逐项相同（征募候选也相同——初始手牌不消费征募子流）。
        MatchFlow carried = Locked(CarryFixtures.On((0, CarryFixtures.Spare)));
        MatchFlow plain = Locked(MatchOptions.Immediate);

        RecruitPanelView withSpare = Panel(carried);
        RecruitPanelView without = Panel(plain);

        Assert.Equal(5, withSpare.ShowCount);
        Assert.Equal(3, withSpare.FreePickCount);
        Assert.Equal(without.ShowCount, withSpare.ShowCount);
        Assert.Equal(without.FreePickCount, withSpare.FreePickCount);
        Assert.Equal(without.TypeSlots, withSpare.TypeSlots);
        Assert.Equal(without.Candidates.Select(c => c.Type), withSpare.Candidates.Select(c => c.Type));
        Assert.Equal(plain.CurrentSnapshot!.DeployLimit, carried.CurrentSnapshot!.DeployLimit);
        Assert.Equal(plain.CurrentSnapshot.RevealCount, carried.CurrentSnapshot.RevealCount);
        Assert.Equal(plain.CurrentSnapshot.FreePickCount, carried.CurrentSnapshot.FreePickCount);
        Assert.Equal(plain.CurrentSnapshot.TypeSlots, carried.CurrentSnapshot.TypeSlots);

        static RecruitPanelView Panel(MatchFlow match)
        {
            match.BeginTurn();
            Assert.Equal(MatchFixtures.P0, match.CurrentPlayer);
            return match.EnterRecruit();
        }
    }
}
