using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.BatchDeployment;

/// <summary>规格：batch-deployment —— Requirement: 围杀不产生通用奖励</summary>
public class 围杀不产生通用奖励Tests
{
    [Fact]
    public void 提子无额外收益()
    {
        // 设计文档 §6.3：一次提走 7 枚敌子 → 手牌、征募参数与任何资源计数均不变。
        // 本层唯一的资源出口是"手牌扣减请求"：它只能扣实际部署的 3 枚，且此外没有任何回调。
        // 变异验证：Confirm 在提子后对 DeductHand 再发一次 {Basic: -captured} 的"奖励" → 本测试红 1。
        // trellis-check 复核：DeductHand 改成按提子集合计数（{Basic: 7}）→ 红 3。
        GameBoard board = TestMaps.Blank(size: 9);
        foreach (string c in new[] { "B5", "C5", "D5", "E5", "F5", "G5", "H5" })
        {
            board.Place(c, TestMaps.P1);
        }

        foreach (string c in new[] { "B4", "C4", "D4", "E4", "F4", "G4", "H4", "B6", "C6", "D6", "F6", "G6", "H6" })
        {
            board.Place(c, TestMaps.P0);
        }

        var hooks = new RecordingHooks { Board = board };
        SettlementDriver driver = BatchFixtures.Driver(board, hooks);
        IReadOnlyDictionary<PieceType, int> stock = BatchFixtures.Stock((PieceType.Basic, 4));
        BatchContext context = BatchFixtures.Context(board, TestMaps.P0, stock: stock);

        SettlementOutcome outcome = driver.Confirm(context, [BatchFixtures.P("A5"), BatchFixtures.P("J5"), BatchFixtures.P("E6")]);

        Assert.True(outcome.Confirmed);
        Assert.Equal(7, outcome.CaptureRecord!.Captured.Length);
        Assert.All(outcome.CaptureRecord.Captured, s => Assert.Equal(TestMaps.P1, s.Owner));

        (PlayerId player, IReadOnlyDictionary<PieceType, int> deployed) = Assert.Single(hooks.Deductions);
        Assert.Equal(TestMaps.P0, player);
        Assert.Equal(new Dictionary<PieceType, int> { [PieceType.Basic] = 3 }, deployed);
        Assert.Equal(["DeductHand", "OnRevealRelics", "OnRecalculatePower", "OnCheckEndConditions"], hooks.Steps);
        Assert.Empty(hooks.Passes);
        // 上游库存对象未被本层触碰
        Assert.Equal(4, stock[PieceType.Basic]);
        Assert.Single(stock);
    }
}
