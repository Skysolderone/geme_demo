using Siege.Core.Board;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 兵站丢失导致的槽位缩水</summary>
public class 兵站丢失导致的槽位缩水Tests
{
    [Fact]
    public void 槽位缩水触发强制弃牌()
    {
        // 设计文档 §5.2：P0 持有 6 种类型，唯一的兵站 E7 在上一大回合被夺走 → 本小回合快照槽位 5、标记超限 1 种。
        // 变异验证 M-E13：OverflowTypeCount 改为 `HeldTypeCount - TypeSlots`（不 clamp）→ 红 2；M-E14：改为恒 0 → 红 1（本测试）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Depot()));
        board.Place("E6", TestMaps.P0);
        ledger.Settle(board, 1);
        EffectSnapshot before = ledger.SnapshotFor(TestMaps.P0, board, heldTypeCount: 6, majorRound: 1);
        Assert.Equal((6, 0), (before.TypeSlots, before.OverflowTypeCount));

        board.Place("E8", TestMaps.P1);
        ledger.Settle(board, 2);

        EffectSnapshot after = ledger.SnapshotFor(TestMaps.P0, board, heldTypeCount: 6, majorRound: 2);
        Assert.Equal((5, 1, 6), (after.TypeSlots, after.OverflowTypeCount, after.HeldTypeCount));
        // 持有数不超过槽位时不标超限
        Assert.Equal(0, ledger.SnapshotFor(TestMaps.P0, board, heldTypeCount: 5, majorRound: 2).OverflowTypeCount);
    }

    [Fact]
    public void 本层不执行弃牌()
    {
        // design.md D7：本层只给槽位数与超限种数，不碰手牌账目——Relics/ 源码里没有任何手牌、弃牌、库存的入口；
        // 快照携带的持有类型数就是输入值，本层不改它。
        // 变异验证 M-E15：给 RelicLedger 加 `DiscardType(PlayerId, PieceType)` 方法 → 红 1（本测试）。
        string dir = Path.Combine(Determinism.随机子流隔离Tests.SourceRoot(), "src", "Siege.Core", "Relics");
        foreach (string file in Directory.GetFiles(dir, "*.cs"))
        {
            string text = File.ReadAllText(file);
            foreach (string token in new[] { "Discard", "Stock", "Hand", "Recruit(" })
            {
                Assert.False(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} 含 {token}");
            }
        }

        Assert.DoesNotContain(typeof(RelicLedger).GetMethods(), m => m.GetParameters().Any(p => p.ParameterType == typeof(PieceType)));
    }
}
