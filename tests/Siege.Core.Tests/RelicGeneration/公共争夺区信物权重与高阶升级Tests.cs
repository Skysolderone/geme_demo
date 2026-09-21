using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicGeneration;

/// <summary>规格：relic-generation —— Requirement: 公共争夺区信物权重与高阶升级</summary>
public class 公共争夺区信物权重与高阶升级Tests
{
    /// <summary>
    /// 公共区 4 Standard + 2 High、出生区 4×2 的合成图——与 v2 基准图的信物分布一致。
    /// 两条统计测试的期望值（总体升级率 20% = (4×150 + 2×300) / 6、样本 60000 / 40000 / 20000）依赖这个 4:2 分布；
    /// v3 基准图在 C4 下公共区只能是 4 + 1（均值 18%），terrain-model 段 B 把用例换到合成图上，期望值不变。
    /// </summary>
    private static readonly MapData Map = SixContestedMap();

    private static MapData SixContestedMap()
    {
        ImmutableHashSet<Coord>[] zones =
        [
            [TestMaps.At("A1"), TestMaps.At("B1")],
            [TestMaps.At("J1"), TestMaps.At("K1")],
            [TestMaps.At("A11"), TestMaps.At("B11")],
            [TestMaps.At("J11"), TestMaps.At("K11")],
        ];
        var relics = new Dictionary<Coord, RelicCellSpec>();
        foreach (Coord c in zones.SelectMany(z => z))
        {
            relics[c] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
        }

        foreach (string n in new[] { "D4", "H4", "D8", "H8" })
        {
            relics[TestMaps.At(n)] = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        }

        foreach (string n in new[] { "F4", "F8" })
        {
            relics[TestMaps.At(n)] = new RelicCellSpec(RelicZone.Contested, BudgetTier.High);
        }

        return TestMaps.Synthetic(size: 11, maxPlayers: 4, relics: relics) with { BirthZones = [.. zones] };
    }

    [Fact]
    public void 高阶比例()
    {
        // 裁决记录 1：升级判定对六类统一按 20%。10000 种子 × 6 个公共区格，总体升级率落在 [19%, 21%]；
        // 每一类（含流派徽记）单独统计也都在 [17%, 23%]——徽记若被排除在升级之外，其升级率为 0，本测试红。
        // 同时验证公共区权重表 30/15/10/15/15/15（±1 个百分点）。
        // 变异验证 M-G6：Draw 里 `type == SchoolEmblem ? 1 : 升级判定`（20% 只对五类）→ 红 1（本测试，徽记升级率 0）；
        // M-G7：两档升级率同时改 300 → 红 1（本测试，总体超 21%）。
        // 两档默认为 Standard 150‰ / High 300‰，4:2 分布下公共区均值恰为 20%；分档差异由 `高档升级率严格高于标准档` 守门。
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
    public void 基准图上公共信物升级率落在宽口径()
    {
        // terrain-model 裁决 35（B-7）：`高阶比例` 换到合成 4:2 图后没有统计测试在真实基准图上跑。
        // restore-go-core-rules 起基准图为 v5（地形与信物格同 v3 / v4）。
        // v3（siege-4p-base-v3，C4）公共区 = 桥头 4 Standard（150‰）+ 岛心 1 High（300‰），均值 (4×150 + 300) / 5 = 180‰；
        // 宽口径 [15%, 21%]（"约 20%"的升级口径在 4+1 分布下的容许带），种子数与统计方式沿用 `高阶比例`（10000 种子）。
        // 先钉 50000 = 10000 × 5：公共区少展开一个轨道（段 B N-2 的形状）本条先红，不会被宽口径吞掉。
        // 变异验证 M-D3：两档升级率同改 300‰ → 本测试红（30% 越上界）；不用 "High 300 → 150"（恰压 15.0% 下界，抽样噪声下红绿不定）。
        MapData v3 = FourPlayerBaseMap.Create();
        Assert.Equal("siege-4p-base-v5", v3.Id);

        int total = 0;
        int advanced = 0;
        for (ulong seed = 0; seed < 10000; seed++)
        {
            foreach (RelicPlacement p in RelicGenerator.Generate(v3, new GameSeed(seed)).Placements)
            {
                if (p.Spec.Zone != RelicZone.Contested)
                {
                    continue;
                }

                total++;
                advanced += p.Content.IsAdvanced ? 1 : 0;
            }
        }

        Assert.Equal(50000, total);
        Assert.InRange(advanced * 1000 / total, 150, 210);
    }

    [Fact]
    public void 高档升级率严格高于标准档()
    {
        // 负责人裁决（2026-09-13）：Standard 150‰ / High 300‰。§3.3 要求中央、咽喉与高风险边缘承担更高的信物强度预算，
        // 此前两档都是 200‰，High 在生成结果里是空操作。合成图 F4/F8 为 High，D4/H4/D8/H8 为 Standard（与 v2 基准图相同）。
        // 变异验证：两档改回同为 200 → 本测试红（High 落到 [17,23]%，不在 [27,33]%）。
        int standardTotal = 0, standardAdvanced = 0, highTotal = 0, highAdvanced = 0;
        for (ulong seed = 0; seed < 10000; seed++)
        {
            foreach (RelicPlacement p in RelicGenerator.Generate(Map, new GameSeed(seed)).Placements)
            {
                switch (p.Spec.Budget)
                {
                    case BudgetTier.Standard:
                        standardTotal++;
                        standardAdvanced += p.Content.IsAdvanced ? 1 : 0;
                        break;
                    case BudgetTier.High:
                        highTotal++;
                        highAdvanced += p.Content.IsAdvanced ? 1 : 0;
                        break;
                }
            }
        }

        Assert.Equal(40000, standardTotal);
        Assert.Equal(20000, highTotal);
        Assert.InRange(standardAdvanced * 1000 / standardTotal, 130, 170);
        Assert.InRange(highAdvanced * 1000 / highTotal, 270, 330);
        Assert.True(highAdvanced * 1000 / highTotal > standardAdvanced * 1000 / standardTotal);
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
