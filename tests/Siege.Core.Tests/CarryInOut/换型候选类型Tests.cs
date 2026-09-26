using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 换型候选类型</summary>
public class 换型候选类型Tests
{
    [Fact]
    public void v2候选()
    {
        // 规格 Scenario：v2 下换型令可指定的类型恰为堡垒、连珠、协同、旗手、铁链、哨兵、界碑七种；权重为基础权重 20 / 18 / 10 / 8 / 8 / 8 / 8（合计 80）。
        Assert.Equal(
            [PieceType.Fortress, PieceType.Line, PieceType.Synergy, PieceType.Bannerman, PieceType.Chain, PieceType.Sentry, PieceType.Boundary],
            CarryCandidates.Of(ContentSet.V2).Select(c => c.Type));
        Assert.Equal([20, 18, 10, 8, 8, 8, 8], CarryCandidates.Of(ContentSet.V2).Select(c => c.Weight));
        Assert.Equal(80, CarryCandidates.Of(ContentSet.V2).Sum(c => c.Weight));
        Assert.False(CarryCandidates.Contains(ContentSet.V2, PieceType.Multiplier));
        Assert.False(CarryCandidates.Contains(ContentSet.V2, PieceType.Artisan));
        Assert.False(CarryCandidates.Contains(ContentSet.V2, PieceType.Basic));
        Assert.True(CarryCandidates.Contains(ContentSet.V2, PieceType.Boundary));
    }

    [Fact]
    public void v1候选()
    {
        // 规格 Scenario：v1 下候选恰为堡垒、连珠、协同三种，不含旗手子等四种新棋子；权重 20 / 18 / 10（合计 48）。
        Assert.Equal([PieceType.Fortress, PieceType.Line, PieceType.Synergy], CarryCandidates.Of(ContentSet.V1).Select(c => c.Type));
        Assert.Equal([20, 18, 10], CarryCandidates.Of(ContentSet.V1).Select(c => c.Weight));
        Assert.Equal(48, CarryCandidates.Of(ContentSet.V1).Sum(c => c.Weight));
        Assert.False(CarryCandidates.Contains(ContentSet.V1, PieceType.Bannerman));
        Assert.True(CarryCandidates.Contains(ContentSet.V1, PieceType.Synergy));
    }

    [Fact]
    public void 征召签的抽取概率()
    {
        // 规格 Scenario：v2 下征召签的抽取分布——堡垒子 20 / 80 = 25%，界碑子 8 / 80 = 10%，倍增子与匠人为 0。
        // 分布由候选权重决定：抽签 = 在该玩家自己的子流 "carry-draft:<编号>"（规格原文的子流名，这里写字面量钉住）上按候选权重做一次加权抽取。
        var weights = CarryCandidates.Of(ContentSet.V2).ToDictionary(c => c.Type, c => c.Weight);
        int total = weights.Values.Sum();
        Assert.Equal(25, weights[PieceType.Fortress] * 100 / total);
        Assert.Equal(10, weights[PieceType.Boundary] * 100 / total);
        Assert.False(weights.ContainsKey(PieceType.Multiplier));
        Assert.False(weights.ContainsKey(PieceType.Artisan));

        int[] table = [.. CarryCandidates.Of(ContentSet.V2).Select(c => c.Weight)];
        var drawn = new HashSet<PieceType>();
        for (ulong s = 1; s <= 40; s++)
        {
            foreach (int player in new[] { 0, 3 })
            {
                var seed = new GameSeed(s);
                PieceType expected = CarryCandidates.Of(ContentSet.V2)[seed.Stream($"carry-draft:{player}").WeightedPick(table)].Type;
                PieceType actual = CarryCandidates.Draw(seed, new PlayerId(player), ContentSet.V2);
                Assert.Equal(expected, actual);
                drawn.Add(actual);
            }
        }

        Assert.True(drawn.Count >= 5, $"样本口径：80 次抽签只出现 {drawn.Count} 种类型");
        Assert.Equal("carry-draft:2", GameSeed.CarryDraft(2));
        Assert.Equal("carry-ai", GameSeed.CarryAi);
    }

    [Theory]
    [InlineData(PieceType.Multiplier)]
    [InlineData(PieceType.Artisan)]
    [InlineData(PieceType.Basic)]
    public void 拒绝非候选类型(PieceType type)
    {
        // 规格 Scenario：以换型令指定倍增子或匠人开局 → 系统拒绝该带入并给出原因，对局不创建。普通子同样不是候选。
        ArgumentException ex = Assert.ThrowsAny<ArgumentException>(
            () => MatchFixtures.Create(options: CarryFixtures.On((0, CarryFixtures.Commission(type)))));
        Assert.Contains("候选", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 拒绝不合形的带入()
    {
        // 同一校验的其余形状：v1 下指定旗手子（v2 才有的候选）、换型令不指定类型、备用子带类型——都拒绝，不静默当成别的带入。
        Assert.ThrowsAny<ArgumentException>(
            () => MatchFixtures.Create(options: CarryFixtures.On(ContentSet.V1, (0, CarryFixtures.Commission(PieceType.Bannerman)))));
        Assert.ThrowsAny<ArgumentException>(
            () => MatchFixtures.Create(options: CarryFixtures.On((0, new CarryIn(SupplyKind.Commission)))));
        Assert.ThrowsAny<ArgumentException>(
            () => MatchFixtures.Create(options: CarryFixtures.On((0, new CarryIn(SupplyKind.SpareStone, PieceType.Fortress)))));

        // 反面：v1 下指定 v1 候选照常创建。
        MatchFlow match = MatchFixtures.Create(options: CarryFixtures.On(ContentSet.V1, (0, CarryFixtures.Commission(PieceType.Synergy))));
        Assert.Equal("Basic×4+0 Synergy×1+0", CarryFixtures.HandText(match, MatchFixtures.P0));
    }
}
