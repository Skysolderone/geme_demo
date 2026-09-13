using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicGeneration;

/// <summary>规格：relic-generation —— Requirement: 信物在开局一次性生成</summary>
public class 信物在开局一次性生成Tests
{
    private static readonly MapData Map = FourPlayerBaseMap.Create();

    [Fact]
    public void 种子可复现()
    {
        // 同一种子 + 同一地图执行两次 → 每个信物格的类型、强度、徽记绑定完全一致，收敛标记与重抽次数也一致。
        // 变异验证 M-G1：Generate 用 Environment.TickCount 扰动种子 → 红 3（本测试、「改变征募决策后信物生成逐格不变」、「分布可导出」）。
        for (ulong seed = 1; seed <= 50; seed++)
        {
            RelicGenerationRecord a = RelicGenerator.Generate(Map, new GameSeed(seed));
            RelicGenerationRecord b = RelicGenerator.Generate(Map, new GameSeed(seed));
            Assert.Equal(a, b);
            Assert.Equal(a.Placements, b.Placements);
        }
    }

    [Fact]
    public void 不同种子改变内容不改变位置()
    {
        // 变异验证 M-G2：Generate 丢掉最后一个信物格 → 红 1（本测试：坐标集合与地图不一致）。
        RelicGenerationRecord a = RelicGenerator.Generate(Map, new GameSeed(1));
        RelicGenerationRecord b = RelicGenerator.Generate(Map, new GameSeed(2));

        Coord[] expected = [.. Map.RelicCells.Keys.Order()];
        Assert.Equal(expected, a.Placements.Select(p => p.Coord));
        Assert.Equal(expected, b.Placements.Select(p => p.Coord));
        Assert.All(a.Placements, p => Assert.Equal(Map.RelicCells[p.Coord], p.Spec));
        Assert.NotEqual(a.Placements.Select(p => p.Content), b.Placements.Select(p => p.Content));
    }

    [Fact]
    public void 对局中不再变化()
    {
        // 账本经历揭示、重算、快照后，每格内容仍与开局生成记录一致；账本没有任何改写内容的入口。
        // 变异验证 M-G3：RelicLedger.Reveal 在揭示时把 Content 换成 +2 → 红 1（本测试）。
        RelicGenerationRecord record = RelicGenerator.Generate(Map, new GameSeed(7));
        GameBoard board = GameBoard.Load(Map);
        var ledger = new RelicLedger(record);

        for (int round = 1; round <= 3; round++)
        {
            foreach (Coord c in Map.RelicCells.Keys.Order().Skip(round * 3).Take(3))
            {
                board.Place(c, TestMaps.P0, PieceType.Basic);
            }

            ledger.Settle(board, round);
            ledger.SnapshotFor(TestMaps.P0, board, heldTypeCount: 0, majorRound: round);
        }

        Assert.Same(record, ledger.Generation);
        foreach (RelicPlacement placement in record.Placements)
        {
            RelicPublicState state = ledger.PublicStateOf(placement.Coord);
            if (state.IsRevealed)
            {
                Assert.Equal(placement.Content, state.Content);
            }
        }

        Assert.DoesNotContain(typeof(RelicLedger).GetMethods(), m => m.Name.Contains("Refresh") || m.Name.Contains("Move") || m.Name.Contains("Set"));
        Assert.All(typeof(RelicContent).GetProperties(), p => Assert.False(p.CanWrite, $"RelicContent.{p.Name} 可写"));
    }
}
