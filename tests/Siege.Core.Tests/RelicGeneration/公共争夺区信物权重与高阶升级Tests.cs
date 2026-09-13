using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicGeneration;

/// <summary>规格：relic-generation —— Requirement: 公共争夺区信物权重与高阶升级</summary>
public class 公共争夺区信物权重与高阶升级Tests
{
    private static readonly MapData Map = FourPlayerBaseMap.Create();

    [Fact]
    public void 高阶比例()
    {
        // 裁决记录 1：升级判定对六类统一按 20%。10000 种子 × 6 个公共区格，总体升级率落在 [19%, 21%]；
        // 每一类（含流派徽记）单独统计也都在 [17%, 23%]——徽记若被排除在升级之外，其升级率为 0，本测试红。
        // 同时验证公共区权重表 30/15/10/15/15/15（±1 个百分点）。
        // 变异验证 M-G6：Draw 里 `type == SchoolEmblem ? 1 : 升级判定`（20% 只对五类）→ 红 1（本测试，徽记升级率 0）；
        // M-G7：StandardUpgradePermille 默认改 300 → 红 1（本测试）。
        int[] counts = new int[RelicWeights.Order.Length];
        int[] advanced = new int[RelicWeights.Order.Length];
        int total = 0;
        for (ulong seed = 0; seed < 10000; seed++)
        {
            foreach (RelicPlacement p in RelicGenerator.Generate(Map, new GameSeed(seed)).Placements)
            {
                if (p.Spec.Zone != RelicZone.Contested)
                {
                    continue;
                }

                int i = RelicWeights.IndexOf(p.Content.Type);
                counts[i]++;
                total++;
                if (p.Content.IsAdvanced)
                {
                    advanced[i]++;
                }
            }
        }

        Assert.Equal(60000, total);
        int[] expected = [30, 15, 10, 15, 15, 15];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(counts[i] * 1000 / total, (expected[i] * 10) - 10, (expected[i] * 10) + 10);
            Assert.InRange(advanced[i] * 1000 / counts[i], 170, 230);
        }

        Assert.InRange(advanced.Sum() * 1000 / total, 190, 210);
    }

    [Fact]
    public void 高阶徽记等效()
    {
        // 设计文档 §8.1 / §9.1：高阶徽记等效两枚普通徽记，在 基础权重 × (1 + 0.75 × 数量) 中按数量 2 代入。
        // 控制 1 枚高阶堡垒徽记的玩家与控制 2 枚普通堡垒徽记的玩家，调整后权重完全相同：堡垒 20 × (1 + 1.5) = 50 → 整数形式 20 × 10 = 200（分母 4）。
        // 变异验证 M-E1：BuildSnapshot 徽记分支改为 `+= 1`（忽略 Magnitude）→ 红 1（本测试）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(
            ("B2", RelicFixtures.Emblem(PieceType.Fortress, count: 2)),
            ("G7", RelicFixtures.Emblem(PieceType.Fortress)),
            ("G2", RelicFixtures.Emblem(PieceType.Fortress)));
        board.Place("B2", TestMaps.P0).Place("G7", TestMaps.P1).Place("G2", TestMaps.P1);

        EffectSnapshot advanced = ledger.SnapshotFor(TestMaps.P0, board, heldTypeCount: 0, majorRound: 1);
        EffectSnapshot twoNormal = ledger.SnapshotFor(TestMaps.P1, board, heldTypeCount: 0, majorRound: 1);

        Assert.Equal(2, advanced.EmblemCountOf(PieceType.Fortress));
        Assert.Equal(2, twoNormal.EmblemCountOf(PieceType.Fortress));
        Assert.Equal(10, advanced.EmblemWeightNumerator(PieceType.Fortress));
        Assert.Equal(200, advanced.AdjustedWeight(PieceType.Fortress, 20));
        Assert.Equal(advanced.AdjustedWeight(PieceType.Fortress, 20), twoNormal.AdjustedWeight(PieceType.Fortress, 20));
        // 未绑定的类型不受影响：普通子 40 × 4 = 160
        Assert.Equal(160, advanced.AdjustedWeight(PieceType.Basic, 40));
    }
}
