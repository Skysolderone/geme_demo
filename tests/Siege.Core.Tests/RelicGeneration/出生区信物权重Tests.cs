using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicGeneration;

/// <summary>规格：relic-generation —— Requirement: 出生区信物权重</summary>
public class 出生区信物权重Tests
{
    private static readonly MapData Map = FourPlayerBaseMap.Create();

    [Fact]
    public void 出生区无高阶信物()
    {
        // 4 人基准图 8 个出生区信物格；2000 个种子下强度全为 +1（徽记为单枚等效）。
        // 变异验证 M-G4：Draw 里去掉 `spec.Zone == BirthZone ? 0 :`，改用 options.UpgradePermilleOf(spec.Budget) 且 Birth 档给 200 → 红 1（本测试）。
        for (ulong seed = 0; seed < 2000; seed++)
        {
            RelicGenerationRecord record = RelicGenerator.Generate(Map, new GameSeed(seed));
            RelicPlacement[] birth = [.. record.Placements.Where(p => p.Spec.Zone == RelicZone.BirthZone)];
            Assert.Equal(8, birth.Length);
            Assert.All(birth, p => Assert.False(p.Content.IsAdvanced, $"种子 {seed} 出生区 {p} 为高阶"));
            Assert.All(birth, p => Assert.Equal(1, p.Content.Magnitude));
        }
    }

    [Fact]
    public void 权重分布收敛()
    {
        // 设计文档 §8.2：徽记 45 / 探勘 20 / 兵站 15 / 征召 8 / 军令 7 / 先锋 5。
        // 第一阶段的权重抽取用 MaxRerolls = 0 观察（第二阶段的预算校正会按设计改变最终分布，那是另一条 Requirement）。
        // 10000 种子 × 8 格 = 80000 样本，容差 ±1 个百分点（3σ 约 0.5）。
        // 变异验证 M-G5：BirthTable 改为 [45, 20, 15, 8, 5, 7]（对调军令与先锋）→ 红 1（本测试）。
        RelicGenerationOptions stageOneOnly = RelicGenerationOptions.Default with { MaxRerolls = 0 };
        int[] counts = new int[RelicWeights.Order.Length];
        int total = 0;
        for (ulong seed = 0; seed < 10000; seed++)
        {
            foreach (RelicPlacement p in RelicGenerator.Generate(Map, new GameSeed(seed), stageOneOnly).Placements)
            {
                if (p.Spec.Zone == RelicZone.BirthZone)
                {
                    counts[RelicWeights.IndexOf(p.Content.Type)]++;
                    total++;
                }
            }
        }

        Assert.Equal(80000, total);
        int[] expected = [45, 20, 15, 8, 7, 5];
        for (int i = 0; i < expected.Length; i++)
        {
            int permille = counts[i] * 1000 / total;
            Assert.InRange(permille, (expected[i] * 10) - 10, (expected[i] * 10) + 10);
        }
    }

    [Fact]
    public void 徽记可绑定匠人()
    {
        // 规格 relic-generation「徽记可绑定匠人」+ artisan-terrain-edit R-1：流派徽记的棋子类型在<b>全部</b>棋子类型中等概率抽取，
        // 类型增至六种后，六种各约六分之一，其中包含匠人。3000 个种子 × 全图信物格，容差 ±2 个百分点。
        // 变异验证见测试报告 M-A5（EmblemPieces 把匠人排除在外）。
        var counts = new SortedDictionary<PieceType, int>();
        int total = 0;
        for (ulong seed = 0; seed < 3000; seed++)
        {
            foreach (RelicPlacement p in RelicGenerator.Generate(Map, new GameSeed(seed)).Placements)
            {
                if (p.Content.EmblemPiece is { } piece)
                {
                    counts[piece] = counts.TryGetValue(piece, out int n) ? n + 1 : 1;
                    total++;
                }
            }
        }

        Assert.Equal(Enum.GetValues<PieceType>().Order(), counts.Keys);
        Assert.True(total > 6000, $"样本量 {total} 太小，不足以判断分布。");
        foreach ((PieceType piece, int n) in counts)
        {
            int permille = n * 1000 / total;
            Assert.InRange(permille, 167 - 20, 167 + 20);
            Assert.True(n > 0, $"{piece} 从未被徽记绑定。");
        }
    }
}
