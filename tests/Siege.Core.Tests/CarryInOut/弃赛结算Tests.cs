using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 弃赛结算（段 A 只测纯函数：弃赛时名次 + 结算；写档案在段 B）</summary>
public class 弃赛结算Tests
{
    private static readonly PlayerId A = new(0);
    private static readonly PlayerId B = new(1);
    private static readonly PlayerId C = new(2);
    private static readonly PlayerId D = new(3);

    private static (PlayerId, PlayerStatus, BigInteger) At(PlayerId p, PlayerStatus status, int power) => (p, status, power);

    [Fact]
    public void 弃赛百分之五十()
    {
        // 规格 Scenario「弃赛 50%」：第 6 大回合 D 势力 30 弃赛，A = 50、B = 30、C = 12，D 带入换型令
        // → 弃赛名次第 2（只有 A 严格更高，与 B 并列共享），带出 ⌊16 / 2⌋ = 8，换型令返还。
        int rank = ResignationRank.Compute(D, [
            At(A, PlayerStatus.Active, 50), At(B, PlayerStatus.Active, 30), At(C, PlayerStatus.Active, 12), At(D, PlayerStatus.Active, 30)]);
        Assert.Equal(2, rank);

        CarryIn commission = CarryFixtures.Commission(PieceType.Fortress);
        Assert.Equal(new CarryOutResult(CarryOutcome.Resigned, 2, 8, commission, Returned: true), CarryOutSettlement.Resigned(4, rank, 6, commission));
    }

    [Fact]
    public void 弃赛时第1名()
    {
        // 规格 Scenario：第 5 大回合 D 以势力 80 居第 1 时弃赛，此后 A、B、C 都完赛 → D 带出 ⌊24 / 2⌋ = 12；终局 D 最终名次第 4，带出不因此改变。
        // 整局结算读弃赛快照的名次（第 1），MUST NOT 读最终名次（第 4，否则是 ⌊10 / 2⌋ = 5）。
        MatchResult result = CarryFixtures.Result(
            CarryFixtures.Input(0, PlayerStatus.Active, 120), CarryFixtures.Input(1, PlayerStatus.Active, 90),
            CarryFixtures.Input(2, PlayerStatus.Active, 70), CarryFixtures.Input(3, PlayerStatus.Resigned, 80));
        Assert.Equal(4, result.Of(D).Rank);

        ImmutableSortedDictionary<PlayerId, CarryOutResult> settled = CarryOutSettlement.Settle(
            result, [CarryFixtures.Resignation(D, 5, 1)], new Dictionary<PlayerId, CarryIn> { [D] = CarryFixtures.Spare }, CarryFixtures.Four);

        Assert.Equal(new CarryOutResult(CarryOutcome.Resigned, 1, 12, CarryFixtures.Spare, Returned: true), settled[D]);
        Assert.Equal([24, 16, 12], new[] { A, B, C }.Select(p => settled[p].Points));
    }

    [Fact]
    public void 向下取整()
    {
        // 规格 Scenario：4 人局 D 在弃赛名次第 4 时弃赛 → 带出 ⌊10 / 2⌋ = 5。
        Assert.Equal(5, CarryOutSettlement.Resigned(4, 4, 6, null).Points);

        // 现行表值全是偶数，⌊P/2⌋ 与 ⌈P/2⌉ 在表上不可区分；取整口径（design.md D5「为以后改表预先定好」）用奇数值钉住。
        Assert.Equal(5, CarryOutSettlement.ResignShare(11));
        Assert.Equal(7, CarryOutSettlement.ResignShare(15));
        Assert.Equal(0, CarryOutSettlement.ResignShare(1));
        Assert.Equal(12, CarryOutSettlement.ResignShare(24));
    }

    [Fact]
    public void 保护期内弃赛不带出补给点()
    {
        // 规格 Scenario：第 2 大回合 D 以势力 20 居第 1 时弃赛、带入备用子 → 带出 0，备用子返还。
        Assert.Equal(new CarryOutResult(CarryOutcome.Resigned, 1, 0, CarryFixtures.Spare, Returned: true), CarryOutSettlement.Resigned(4, 1, 2, CarryFixtures.Spare));

        // 边界（带等号的比较式先拿退化局面算一遍）：保护期 = 第 1–3 大回合，第 3 大回合仍为 0，第 4 大回合起按 50%。
        Assert.Equal(0, CarryOutSettlement.Resigned(4, 1, 1, null).Points);
        Assert.Equal(0, CarryOutSettlement.Resigned(4, 1, MatchFlow.BuildProtectionRounds, null).Points);
        Assert.Equal(12, CarryOutSettlement.Resigned(4, 1, MatchFlow.BuildProtectionRounds + 1, null).Points);

        // 整局结算同样读快照的大回合。
        ImmutableSortedDictionary<PlayerId, CarryOutResult> settled = CarryOutSettlement.Settle(
            CarryFixtures.Result(
                CarryFixtures.Input(0, PlayerStatus.Active, 30), CarryFixtures.Input(1, PlayerStatus.Active, 20),
                CarryFixtures.Input(2, PlayerStatus.Active, 10), CarryFixtures.Input(3, PlayerStatus.Resigned, 20)),
            [CarryFixtures.Resignation(D, 2, 1)], new Dictionary<PlayerId, CarryIn> { [D] = CarryFixtures.Spare }, CarryFixtures.Four);
        Assert.Equal(0, settled[D].Points);
        Assert.True(settled[D].Returned);
    }

    [Fact]
    public void 已出局者不计入弃赛名次()
    {
        // 规格 Scenario：C 已出局（势力 0），D 以势力 5 弃赛，A = 40、B = 20 → D 的弃赛名次为第 3，带出 ⌊12 / 2⌋ = 6。
        int rank = ResignationRank.Compute(D, [
            At(A, PlayerStatus.Active, 40), At(B, PlayerStatus.Active, 20), At(C, PlayerStatus.Eliminated, 0), At(D, PlayerStatus.Active, 5)]);
        Assert.Equal(3, rank);
        Assert.Equal(6, CarryOutSettlement.Resigned(4, rank, 6, null).Points);

        // 纯函数守门：真实对局里出局者无子、势力恒为 0，"计入出局者"在任何真实盘面上都不可见；这里给出局者一个非零势力，它仍不计入。
        Assert.Equal(3, ResignationRank.Compute(D, [
            At(A, PlayerStatus.Active, 40), At(B, PlayerStatus.Active, 20), At(C, PlayerStatus.Eliminated, 99), At(D, PlayerStatus.Active, 5)]));
    }

    [Fact]
    public void 此前已弃赛者计入弃赛名次()
    {
        // 规格正文：弃赛时名次在"未出局玩家"中排，参赛者与此前已弃赛者都计入（他们仍在公开排名上，design.md D5）。
        // 样本：B 已弃赛、此刻势力 60 高于 D 的 30 → D 第 2；只在参赛者中排会得出第 1。
        Assert.Equal(2, ResignationRank.Compute(D, [
            At(A, PlayerStatus.Active, 10), At(B, PlayerStatus.Resigned, 60), At(C, PlayerStatus.Active, 5), At(D, PlayerStatus.Active, 30)]));

        // 并列共享较高名次：与已弃赛者同为 30 也共享第 1。
        Assert.Equal(1, ResignationRank.Compute(D, [
            At(A, PlayerStatus.Active, 10), At(B, PlayerStatus.Resigned, 30), At(C, PlayerStatus.Active, 5), At(D, PlayerStatus.Active, 30)]));
    }
}
