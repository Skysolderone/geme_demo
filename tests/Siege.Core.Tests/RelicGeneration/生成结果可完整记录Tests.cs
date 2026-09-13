using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicGeneration;

/// <summary>规格：relic-generation —— Requirement: 生成结果可完整记录</summary>
public class 生成结果可完整记录Tests
{
    private static readonly MapData Map = FourPlayerBaseMap.Create();

    [Fact]
    public void 分布可导出()
    {
        // 记录包含种子、地图、收敛标记与每个信物格的坐标 / 类型 / 强度 / 分区；仅凭记录里的种子与地图即可离线复现同一次生成。
        // 变异验证 M-G15：RelicGenerationRecord.Serialize 漏掉 Spec → 红 1（本测试）；M-G16：Generate 记录 seed 时 +1 → 红 1（本测试，复现不一致）。
        var seed = new GameSeed(0xABCDEF);
        RelicGenerationRecord record = RelicGenerator.Generate(Map, seed);
        var ledger = new RelicLedger(record);

        Assert.Equal(seed, ledger.Generation.Seed);
        Assert.Equal(Map.Id, record.MapId);
        Assert.Equal(Map.RelicCells.Count, record.Placements.Length);

        string text = record.Serialize();
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal($"seed={seed};map={Map.Id};converged={(record.Converged ? "yes" : "no")};rerolls={record.Rerolls}", lines[0]);
        Assert.Equal(record.Placements.Length, lines.Length - 1);
        foreach (RelicPlacement p in record.Placements)
        {
            string line = Assert.Single(lines.Skip(1), l => l.StartsWith(p.Coord.ToNotation() + " ", StringComparison.Ordinal));
            Assert.Contains(p.Spec.Zone.ToString(), line);
            Assert.Contains(p.Spec.Budget.ToString(), line);
            Assert.Contains(p.Content.Type.ToString(), line);
            Assert.Contains(p.Content.Magnitude.ToString(), line);
            if (p.Content.EmblemPiece is { } piece)
            {
                Assert.Contains(piece.ToString(), line);
            }
        }

        // 离线复现：从记录取种子，在同一地图上重新生成
        RelicGenerationRecord replay = RelicGenerator.Generate(FourPlayerBaseMap.Create(), GameSeed.Parse(lines[0].Split(';')[0]["seed=".Length..]));
        Assert.Equal(record.Placements, replay.Placements);
        Assert.Equal(record.Converged, replay.Converged);
    }
}
