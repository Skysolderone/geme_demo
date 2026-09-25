using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicGeneration;

/// <summary>规格：relic-generation —— Requirement: 区域强度预算</summary>
public class 区域强度预算Tests
{
    private static readonly MapData Map = FourPlayerBaseMap.Create();

    /// <summary>
    /// more-pieces-relics 段 B：本类各用例的字面量（稀有度 222 / 2000、预算 600 / 690 / 780、不收敛图的稀有度集合）都出自 v1 的百分制表，
    /// 生成改为按对局内容集取表后显式钉在 v1；v2 的千分制刻度另在 `稀有度按权重倒数计分且高阶两倍计` / `高风险区预算更高` 里补断言。
    /// </summary>
    private static readonly RelicGenerationOptions V1 = RelicGenerationOptions.Default with { ContentSet = ContentSet.V1 };

    /// <summary>在测试里独立复算：各出生区总稀有度（权重倒数 × 刻度取整，高阶 ×2；刻度 v1 为 10000、v2 为 100000）。</summary>
    private static long[] ZoneRarities(RelicGenerationRecord record)
    {
        long[] sums = new long[Map.BirthZones.Length];
        int scale = record.ContentSet == ContentSet.V1 ? 10000 : 100000;
        foreach (RelicPlacement p in record.Placements.Where(p => p.Spec.Zone == RelicZone.BirthZone))
        {
            int weight = RelicWeights.WeightOf(RelicZone.BirthZone, p.Content.Type, record.ContentSet);
            sums[Map.BirthZoneOf(p.Coord)!.Value] += scale / weight * p.Content.Magnitude;
        }

        return sums;
    }

    [Theory]
    [InlineData(ContentSet.V1)]
    [InlineData(ContentSet.V2)]
    public void 出生区稀有度均衡(ContentSet set)
    {
        // more-pieces-relics 段 B：v1 / v2 各跑一遍（v2 稀有度刻度 100000，独立复算见 ZoneRarities）。
        // tasks 2.3 实测 10000 个种子的未收敛率：标准图 v1 24.29% → v2 19.48%（其余内置图同向下降），未恶化。
        // 裁决记录 2：各出生区总稀有度与均值的偏差 ≤ 8%。对 500 个种子中标记为「已收敛」的生成结果逐个核验；
        // 同时断言收敛并非稀有事件（≥ 50% 的种子收敛），否则 8% 约束形同虚设。
        // 变异验证 M-G8：IsBalanced 的比较改为 `deviation * 1_000 > allowed`（容差放大 1000 倍）→ 红 1（本测试）；
        // M-G9：Generate 跳过 BalanceBirthZones、恒标 Converged = true → 红 1（本测试）。
        int converged = 0;
        for (ulong seed = 0; seed < 500; seed++)
        {
            RelicGenerationRecord record = RelicGenerator.Generate(Map, new GameSeed(seed), V1 with { ContentSet = set });
            Assert.Equal(set, record.ContentSet);
            if (!record.Converged)
            {
                continue;
            }

            converged++;
            long[] sums = ZoneRarities(record);
            long total = sums.Sum();
            foreach (long sum in sums)
            {
                // |sum − total/4| ≤ total/4 × 8%  ⇔  |sum×4 − total| × 100 ≤ total × 8
                Assert.True(Math.Abs((sum * 4) - total) * 100 <= total * 8, $"种子 {seed} 出生区稀有度 {string.Join("/", sums)} 偏差超过 8%");
            }
        }

        Assert.InRange(converged, 250, 500);
    }

    [Fact]
    public void 重抽未收敛时的确定性兜底()
    {
        // 裁决记录 4：构造一张必然不收敛的图——出生区 A 有 1 个信物格、出生区 B 有 2 个，容差 0。
        // 出生区稀有度集合 {222, 500, 666, 1250, 1428, 2000} 中，任意两枚之和都不等于任意单枚，因此偏差恒不为 0，任何种子都不可能收敛。
        // 生成必须在重试上限内返回：不死循环、不抛错、标记未收敛、保留权重抽取结果，且同样可复现。
        // 变异验证 M-G10：BalanceBirthZones 在超限时 throw → 红 1（本测试）；M-G11：去掉 `rerolls >= MaxRerolls` 的退出 → 本测试超时（用 5 秒守门）。
        MapData map = UnbalancedMap();
        RelicGenerationOptions strict = V1 with { RarityTolerancePermille = 0, MaxRerolls = 50 };
        var timer = System.Diagnostics.Stopwatch.StartNew();

        RelicGenerationRecord record = RelicGenerator.Generate(map, new GameSeed(3), strict);

        Assert.True(timer.ElapsedMilliseconds < 5000);
        Assert.False(record.Converged);
        Assert.Equal(50, record.Rerolls);
        Assert.Equal(map.RelicCells.Count, record.Placements.Length);
        Assert.All(record.Placements, p => Assert.Equal(map.RelicCells[p.Coord], p.Spec));
        Assert.All(record.Placements.Where(p => p.Spec.Zone == RelicZone.BirthZone), p => Assert.False(p.Content.IsAdvanced));
        Assert.Contains("converged=no", record.Serialize());
        Assert.Equal(record, RelicGenerator.Generate(map, new GameSeed(3), strict));

        // 同一张图放宽到 100% 容差即可收敛，说明不收敛来自约束本身而非生成器缺陷
        Assert.True(RelicGenerator.Generate(map, new GameSeed(3), strict with { RarityTolerancePermille = 1000 }).Converged);
    }

    /// <summary>9×9 合成图：出生区 0 = {A1, B1, A2}（信物 A1），出生区 1 = {J9, H9, J8}（信物 J9、H9），公共区信物 E5。只供生成器使用，不做地图校验。</summary>
    private static MapData UnbalancedMap() => new()
    {
        Id = "test-unbalanced-birth",
        Width = 9,
        Height = 9,
        MaxPlayers = 2,
        Obstacles = [],
        BirthZones =
        [
            [TestMaps.At("A1"), TestMaps.At("B1"), TestMaps.At("A2")],
            [TestMaps.At("J9"), TestMaps.At("H9"), TestMaps.At("J8")],
        ],
        RelicCells = new Dictionary<Coord, RelicCellSpec>
        {
            [TestMaps.At("A1")] = new(RelicZone.BirthZone, BudgetTier.Birth),
            [TestMaps.At("J9")] = new(RelicZone.BirthZone, BudgetTier.Birth),
            [TestMaps.At("H9")] = new(RelicZone.BirthZone, BudgetTier.Birth),
            [TestMaps.At("E5")] = new(RelicZone.Contested, BudgetTier.High),
        }.ToImmutableDictionary(),
        ChokePoints = [TestMaps.At("E5")],
        CentralEntrance = TestMaps.At("E5"),
    };

    [Fact]
    public void 同区同类型被降低概率但不禁止()
    {
        // 裁决记录 3：同区同类型计入偏离惩罚。对 2000 个种子的已收敛结果统计「某出生区两枚同类型」的出生区占比：
        // 有惩罚（默认 500‰）应显著低于无惩罚（0‰）——至少低 1/3——且不为零。
        // 变异验证 M-G12：IsBalanced 里 allowedShare 恒为 1000（惩罚失效）→ 红 1（本测试，两者相等）。
        (int Same, int Zones) Count(RelicGenerationOptions options)
        {
            int same = 0;
            int zones = 0;
            for (ulong seed = 0; seed < 2000; seed++)
            {
                RelicGenerationRecord record = RelicGenerator.Generate(Map, new GameSeed(seed), options);
                if (!record.Converged)
                {
                    continue;
                }

                for (int z = 0; z < Map.BirthZones.Length; z++)
                {
                    RelicType[] types = [.. record.Placements.Where(p => p.Spec.Zone == RelicZone.BirthZone && Map.BirthZoneOf(p.Coord) == z).Select(p => p.Content.Type)];
                    Assert.Equal(2, types.Length);
                    zones++;
                    if (types[0] == types[1])
                    {
                        same++;
                    }
                }
            }

            return (same, zones);
        }

        (int penalized, int penalizedZones) = Count(V1);
        (int free, int freeZones) = Count(V1 with { SameTypePenaltyPermille = 0 });

        Assert.True(penalized > 0, "惩罚不得把同类型组合完全禁止");
        long penalizedPermille = penalized * 1000L / penalizedZones;
        long freePermille = free * 1000L / freeZones;
        Assert.True(penalizedPermille * 3 <= freePermille * 2, $"有惩罚 {penalizedPermille}‰ 未显著低于无惩罚 {freePermille}‰");
    }

    [Fact]
    public void 组合仍随机()
    {
        // 100 个不同种子下，出生区之间出现过至少两种不同的类型组合（实际远多于两种），未退化为固定组合。
        // 变异验证 M-G13：BalanceBirthZones 把全部出生区信物改成「徽记 + 探勘」→ 红 1（本测试）。
        var combos = new HashSet<string>();
        for (ulong seed = 0; seed < 100; seed++)
        {
            RelicGenerationRecord record = RelicGenerator.Generate(Map, new GameSeed(seed));
            for (int z = 0; z < Map.BirthZones.Length; z++)
            {
                combos.Add(string.Join("+", record.Placements
                    .Where(p => p.Spec.Zone == RelicZone.BirthZone && Map.BirthZoneOf(p.Coord) == z)
                    .Select(p => p.Content.Type).Order()));
            }
        }

        Assert.True(combos.Count >= 2, $"只出现 {combos.Count} 种组合");
    }

    [Fact]
    public void 稀有度按权重倒数计分且高阶两倍计()
    {
        // design.md D2 / 规格「稀有度 SHALL 按类型权重的倒数计分，效果 +2 的高阶信物按 2 倍计」。
        // 出生区：先锋 5% → 2000，徽记 45% → 222；公共区：军令 15% → 666，+2 军令 → 1332，双倍徽记（30%）→ 666。
        // 变异验证 K-6（trellis-check）：RarityOf 去掉 `* content.Magnitude` → 红 2（本测试、高风险区预算更高）。此前该变异 0 红。
        Assert.Equal(2000, RelicWeights.RarityOf(RelicZone.BirthZone, RelicFixtures.Vanguard(), ContentSet.V1));
        Assert.Equal(222, RelicWeights.RarityOf(RelicZone.BirthZone, RelicFixtures.Emblem(PieceType.Basic), ContentSet.V1));
        Assert.Equal(666, RelicWeights.RarityOf(RelicZone.Contested, RelicFixtures.Command(), ContentSet.V1));
        Assert.Equal(1332, RelicWeights.RarityOf(RelicZone.Contested, RelicFixtures.Command(2), ContentSet.V1));
        Assert.Equal(666, RelicWeights.RarityOf(RelicZone.Contested, RelicFixtures.Emblem(PieceType.Basic, count: 2), ContentSet.V1));
        Assert.Equal(
            2 * RelicWeights.RarityOf(RelicZone.Contested, RelicFixtures.Depot(), ContentSet.V1),
            RelicWeights.RarityOf(RelicZone.Contested, RelicFixtures.Depot(2), ContentSet.V1));

        // more-pieces-relics D6：v2 千分制、刻度 100000——先锋 40‰ → 2500，徽记 360‰ → 277，新四类出生区 50‰ → 2000、公共区 70‰ → 1428；
        // 军令公共区 108‰ → 925，+2 军令 → 1850。v1 刻度保持 10000（上面各值与改动前逐位相同）。
        Assert.Equal(10000, RelicWeights.RarityScaleOf(ContentSet.V1));
        Assert.Equal(100000, RelicWeights.RarityScaleOf(ContentSet.V2));
        Assert.Equal(2500, RelicWeights.RarityOf(RelicZone.BirthZone, RelicFixtures.Vanguard(), ContentSet.V2));
        Assert.Equal(277, RelicWeights.RarityOf(RelicZone.BirthZone, RelicFixtures.Emblem(PieceType.Basic), ContentSet.V2));
        Assert.Equal(2000, RelicWeights.RarityOf(RelicZone.BirthZone, new RelicContent(RelicType.Workshop, 1), ContentSet.V2));
        Assert.Equal(1428, RelicWeights.RarityOf(RelicZone.Contested, new RelicContent(RelicType.Encampment, 1), ContentSet.V2));
        Assert.Equal(925, RelicWeights.RarityOf(RelicZone.Contested, RelicFixtures.Command(), ContentSet.V2));
        Assert.Equal(1850, RelicWeights.RarityOf(RelicZone.Contested, RelicFixtures.Command(2), ContentSet.V2));
    }

    [Fact]
    public void 高风险区预算更高()
    {
        // 预算 = 单格期望稀有度：出生区不升级为 600，Standard 档按 15% 升级为 690，High 档（中央 / 咽喉旁）按 30% 升级为 780，逐级严格更高。
        // 交叉检查：基准图 High 档信物格都在公共区；2000 个种子下 High 档格的平均稀有度严格高于出生区格。
        // 变异验证 M-G14：BudgetOf 对 High 返回 baseline（不乘升级率）→ 红 1（本测试）。
        RelicGenerationOptions options = V1;
        Assert.True(options.BudgetOf(BudgetTier.High) > options.BudgetOf(BudgetTier.Birth));
        Assert.True(options.BudgetOf(BudgetTier.Standard) > options.BudgetOf(BudgetTier.Birth));
        Assert.Equal(600, options.BudgetOf(BudgetTier.Birth));
        Assert.Equal(690, options.BudgetOf(BudgetTier.Standard));
        Assert.Equal(780, options.BudgetOf(BudgetTier.High));
        Assert.True(options.BudgetOf(BudgetTier.High) > options.BudgetOf(BudgetTier.Standard));

        // more-pieces-relics D6 / D7：v2 每类的"权重占比 × 稀有度"仍为 100，但升级只作用于原六类——预算 = 600 × (1 + 升级率) + 400。
        RelicGenerationOptions v2 = RelicGenerationOptions.Default with { ContentSet = ContentSet.V2 };
        Assert.Equal(1000, v2.BudgetOf(BudgetTier.Birth));
        Assert.Equal(1090, v2.BudgetOf(BudgetTier.Standard));
        Assert.Equal(1180, v2.BudgetOf(BudgetTier.High));

        Coord[] highCells = [.. Map.RelicCells.Where(kv => kv.Value.Budget == BudgetTier.High).Select(kv => kv.Key)];
        Assert.NotEmpty(highCells);
        Assert.All(highCells, c => Assert.Equal(RelicZone.Contested, Map.RelicCells[c].Zone));

        long highRarity = 0;
        long highCount = 0;
        long birthRarity = 0;
        long birthCount = 0;
        for (ulong seed = 0; seed < 2000; seed++)
        {
            foreach (RelicPlacement p in RelicGenerator.Generate(Map, new GameSeed(seed), options).Placements)
            {
                if (p.Spec.Budget == BudgetTier.High)
                {
                    highRarity += p.RarityIn(ContentSet.V1);
                    highCount++;
                }
                else if (p.Spec.Budget == BudgetTier.Birth)
                {
                    birthRarity += p.RarityIn(ContentSet.V1);
                    birthCount++;
                }
            }
        }

        Assert.True(highRarity / highCount > birthRarity / birthCount, $"High 档均值 {highRarity / highCount} 未高于出生区 {birthRarity / birthCount}");
        // 预算就是单格期望稀有度：High 档 4000 样本的均值应落在 780 附近（±40，约 5 个标准误）。
        // 不升级（或升级不按 2 倍计稀有度）时均值退化为 600，本断言红——只比较「High > 出生区」守不住这一点，因为校正后的出生区均值本就低于 600。
        Assert.InRange(highRarity / highCount, options.BudgetOf(BudgetTier.High) - 40, options.BudgetOf(BudgetTier.High) + 40);
    }
}
