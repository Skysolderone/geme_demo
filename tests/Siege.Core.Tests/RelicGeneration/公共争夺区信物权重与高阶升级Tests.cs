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

    /// <summary>公共区各类型的出现数与升级数（按 <see cref="RelicWeights.Order"/> 下标），10000 种子 × 6 格；只跑信物生成、不跑对局。</summary>
    private static (int[] Counts, int[] Advanced) ContestedStats(ContentSet set)
    {
        int[] counts = new int[RelicWeights.Order.Length];
        int[] advanced = new int[RelicWeights.Order.Length];
        for (ulong seed = 0; seed < 10000; seed++)
        {
            foreach (RelicPlacement p in RelicGenerator.Generate(Map, new GameSeed(seed), set).Placements)
            {
                if (p.Spec.Zone != RelicZone.Contested)
                {
                    continue;
                }

                int i = RelicWeights.IndexOf(p.Content.Type);
                counts[i]++;
                if (p.Content.IsAdvanced)
                {
                    advanced[i]++;
                }
            }
        }

        Assert.Equal(60000, counts.Sum());
        return (counts, advanced);
    }

    [Theory]
    [InlineData(ContentSet.V2)]
    [InlineData(ContentSet.V1)]
    public void 高阶比例(ContentSet set)
    {
        // 裁决记录 1：升级判定对原有六类统一按 20%（more-pieces-relics MODIFIED：规格改为"统计公共区信物中属于原有六类的部分"）。
        // 10000 种子 × 6 个公共区格；原有六类合计升级率落在 [19%, 21%]，每一类（含流派徽记）单独统计也都在 [17%, 23%]——徽记若被排除在升级之外，其升级率为 0，本测试红。
        // 同时验证公共区权重表：v2 千分制 216/108/72/108/108/108/70×4，v1 百分制 30/15/10/15/15/15（±10‰）。
        // 变异验证 M-G6：Draw 里 `type == SchoolEmblem ? 1 : 升级判定`（20% 只对五类）→ 红 1（本测试，徽记升级率 0）；
        // M-G7：两档升级率同时改 300 → 红 1（本测试，总体超 21%）。
        // 两档默认为 Standard 150‰ / High 300‰，4:2 分布下公共区均值恰为 20%；分档差异由 `高档升级率严格高于标准档` 守门。
        (int[] counts, int[] advanced) = ContestedStats(set);
        int[] expected = set == ContentSet.V2 ? [216, 108, 72, 108, 108, 108, 70, 70, 70, 70] : [300, 150, 100, 150, 150, 150, 0, 0, 0, 0];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(counts[i] * 1000 / 60000, expected[i] - 10, expected[i] + 10);
        }

        for (int i = 0; i < 6; i++)
        {
            Assert.InRange(advanced[i] * 1000 / counts[i], 170, 230);
        }

        Assert.InRange(advanced[..6].Sum() * 1000 / counts[..6].Sum(), 190, 210);
    }

    [Fact]
    public void 新四类不升级()
    {
        // 规格「新四类不升级」（D7）：公共区的连营、犄角、驿站、工坊全部为强度 +1，升级比例为 0。
        // 样本口径下界：v2 下公共区新四类约占 28%（约 1.7 万枚），不是"一枚都没抽到所以恒为 0"。
        (int[] counts, int[] advanced) = ContestedStats(ContentSet.V2);
        Assert.True(counts[6..].Sum() > 10000, $"公共区新四类只有 {counts[6..].Sum()} 枚");
        Assert.Equal(0, advanced[6..].Sum());
    }

    [Fact]
    public void 新四类在公共区各约7()
    {
        // 规格「新四类在公共区各约 7%」：v2 下连营、犄角、驿站、工坊各 70‰，原六类 = 原表 × 72%（21.6 / 10.8 / 7.2 / 10.8 / 10.8 / 10.8%）。
        // 60000 样本，p = 7% 时 3σ ≈ 3.1‰，容差 ±5‰。
        (int[] counts, _) = ContestedStats(ContentSet.V2);
        foreach (RelicType fresh in new[] { RelicType.Encampment, RelicType.Pincer, RelicType.Relay, RelicType.Workshop })
        {
            Assert.InRange(counts[RelicWeights.IndexOf(fresh)] * 1000 / 60000, 65, 75);
        }

        int[] original = [300, 150, 100, 150, 150, 150];   // v1 的千分比，× 72% 即 v2
        for (int i = 0; i < original.Length; i++)
        {
            Assert.InRange(counts[i] * 1000 / 60000, (original[i] * 72 / 100) - 5, (original[i] * 72 / 100) + 5);
        }
    }

    [Fact]
    public void v2随机消费顺序与规格一致()
    {
        // 规格「公共争夺区信物权重与高阶升级」：每枚信物的消费顺序为「抽类型 → 仅原有六类做升级判定 → 仅流派徽记抽绑定类型」（D7）。
        // 在测试里用同一条 relic-gen 子流独立重放第一阶段（MaxRerolls = 0，第二阶段不再消费），权重表、升级率、徽记类型清单全部是字面量。
        // 新四类若照常消费升级抽签（哪怕丢弃结果），其后每一格的取值都会错位，本测试红。
        RelicType[] order =
        [
            RelicType.SchoolEmblem, RelicType.Prospecting, RelicType.Depot, RelicType.Conscription, RelicType.Command, RelicType.Vanguard,
            RelicType.Encampment, RelicType.Pincer, RelicType.Relay, RelicType.Workshop,
        ];
        int[] birth = [360, 160, 120, 64, 56, 40, 50, 50, 50, 50];
        int[] contested = [216, 108, 72, 108, 108, 108, 70, 70, 70, 70];
        PieceType[] emblemPieces =
        [
            PieceType.Basic, PieceType.Fortress, PieceType.Line, PieceType.Multiplier, PieceType.Synergy, PieceType.Artisan,
            PieceType.Bannerman, PieceType.Chain, PieceType.Sentry, PieceType.Boundary,
        ];
        RelicGenerationOptions stageOneOnly = RelicGenerationOptions.Default with { MaxRerolls = 0, ContentSet = ContentSet.V2 };

        int contestedNew = 0;
        int contestedAfterNew = 0;
        for (ulong seed = 0; seed < 300; seed++)
        {
            var gameSeed = new GameSeed(seed);
            RandomStream stream = gameSeed.Stream(GameSeed.RelicGeneration);
            var expected = new List<RelicContent>();
            bool sawNew = false;
            foreach (Coord cell in Map.RelicCells.Keys.Order())
            {
                RelicCellSpec spec = Map.RelicCells[cell];
                int index = stream.WeightedPick(spec.Zone == RelicZone.BirthZone ? birth : contested);
                int magnitude = 1;
                if (spec.Zone == RelicZone.Contested && index < 6 && stream.NextPermille(spec.Budget == BudgetTier.High ? 300 : 150))
                {
                    magnitude = 2;
                }

                PieceType? emblem = order[index] == RelicType.SchoolEmblem ? emblemPieces[stream.NextInt(emblemPieces.Length)] : null;
                expected.Add(new RelicContent(order[index], magnitude, emblem));
                if (spec.Zone == RelicZone.Contested)
                {
                    contestedAfterNew += sawNew ? 1 : 0;
                    if (index >= 6)
                    {
                        contestedNew++;
                        sawNew = true;
                    }
                }
            }

            Assert.Equal(expected, RelicGenerator.Generate(Map, gameSeed, stageOneOnly).Placements.Select(p => p.Content));
        }

        // 样本口径下界：公共区确实抽到过新四类，且其后还有公共区格要抽（否则"新四类多消费一次"无处显形）。
        Assert.True(contestedNew > 50, $"公共区新四类只有 {contestedNew} 枚");
        Assert.True(contestedAfterNew > 50, $"新四类之后的公共区格只有 {contestedAfterNew} 个");
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
        // more-pieces-relics 段 B 改写：缺省内容集 v2 的新四类不升级（D7），升级率只统计原有六类（规格「高阶比例」的新口径）；分母另记原六类枚数。
        MapData v3 = FourPlayerBaseMap.Create();
        Assert.Equal("siege-4p-base-v5", v3.Id);

        int total = 0;
        int original = 0;
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
                if (RelicWeights.IndexOf(p.Content.Type) < 6)
                {
                    original++;
                    advanced += p.Content.IsAdvanced ? 1 : 0;
                }
            }
        }

        Assert.Equal(50000, total);
        Assert.InRange(advanced * 1000 / original, 150, 210);
    }

    [Fact]
    public void 高档升级率严格高于标准档()
    {
        // 负责人裁决（2026-09-13）：Standard 150‰ / High 300‰。§3.3 要求中央、咽喉与高风险边缘承担更高的信物强度预算，
        // 此前两档都是 200‰，High 在生成结果里是空操作。合成图 F4/F8 为 High，D4/H4/D8/H8 为 Standard（与 v2 基准图相同）。
        // 变异验证：两档改回同为 200 → 本测试红（High 落到 [17,23]%，不在 [27,33]%）。
        // more-pieces-relics 段 B 改写：本测试钉的是两档升级率本身，与内容集无关；v2 的新四类不升级（D7）会把两档都稀释成 72%，
        // 故显式钉 v1（原六类即全部，期望 40000 / 20000 与区间原样保留）。v2 的升级口径见 `高阶比例(V2)` 与 `新四类不升级`。
        int standardTotal = 0, standardAdvanced = 0, highTotal = 0, highAdvanced = 0;
        for (ulong seed = 0; seed < 10000; seed++)
        {
            foreach (RelicPlacement p in RelicGenerator.Generate(Map, new GameSeed(seed), ContentSet.V1).Placements)
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
