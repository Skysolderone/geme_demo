using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// 第一阶段（MaxRerolls = 0）出生区类型计数，按 <see cref="RelicWeights.Order"/> 下标。10000 种子 × 8 格 = 80000 样本。
    /// 只跑信物生成，不跑对局（tasks 2.2）。
    /// </summary>
    private static int[] StageOneBirthCounts(ContentSet set)
    {
        RelicGenerationOptions stageOneOnly = RelicGenerationOptions.Default with { MaxRerolls = 0, ContentSet = set };
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
        return counts;
    }

    [Fact]
    public void 权重分布收敛()
    {
        // more-pieces-relics MODIFIED：缺省内容集 v2 的千分制表——徽记 360 / 探勘 160 / 兵站 120 / 征召 64 / 军令 56 / 先锋 40 / 连营、犄角、驿站、工坊各 50。
        // 第一阶段的权重抽取用 MaxRerolls = 0 观察（第二阶段的预算校正会按设计改变最终分布，那是另一条 Requirement）。
        // 80000 样本，容差 ±10‰（p = 36% 时 3σ 约 5‰）。v1 百分制表（45 / 20 / 15 / 8 / 7 / 5）的同一断言见 `内容集v1保持旧表`。
        // 变异验证 M-G5（改写前）：BirthTable 改为 [45, 20, 15, 8, 5, 7]（对调军令与先锋）→ 红 1（本测试）。
        int[] counts = StageOneBirthCounts(ContentSet.V2);
        int[] expected = [360, 160, 120, 64, 56, 40, 50, 50, 50, 50];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(counts[i] * 1000 / 80000, expected[i] - 10, expected[i] + 10);
        }
    }

    [Fact]
    public void 新四类在出生区各约5()
    {
        // 规格「新四类在出生区各约 5%」：v2 下连营、犄角、驿站、工坊各 50‰，原六类 = 原表 × 80%（36 / 16 / 12 / 6.4 / 5.6 / 4%）。
        // 容差收紧到 ±5‰（p = 5% 时 3σ ≈ 2.3‰），能分开 64 与 56、56 与 50。
        int[] counts = StageOneBirthCounts(ContentSet.V2);
        foreach (RelicType fresh in new[] { RelicType.Encampment, RelicType.Pincer, RelicType.Relay, RelicType.Workshop })
        {
            Assert.InRange(counts[RelicWeights.IndexOf(fresh)] * 1000 / 80000, 45, 55);
        }

        int[] original = [450, 200, 150, 80, 70, 50];   // v1 的千分比，× 80% 即 v2
        for (int i = 0; i < original.Length; i++)
        {
            Assert.InRange(counts[i] * 1000 / 80000, (original[i] * 8 / 10) - 5, (original[i] * 8 / 10) + 5);
        }
    }

    /// <summary>
    /// 引入新信物之前（段 A 提交 854d371 的代码）用 <c>Generate(map, seed)</c> 对各内置图种子 0–499 生成、逐局 <c>Serialize()</c> 拼接后的 SHA-256，
    /// 以及未收敛局数。段 B 动生成器之前抓取（临时测试，已删除）。
    /// </summary>
    private static readonly (string MapId, string Sha256, int Unconverged)[] PreChangeGolden =
    [
        ("siege-4p-base-v5", "66836BA88F895F1C439C7836B03207591DEA4FBF2234BCC98C1AB19584565C88", 128),
        ("siege-2p-base-v1", "3BA92151471C3409FB64793F41322C2E26C595EFA9B7E5835530B35DB97A488F", 108),
        ("siege-3p-base-v1", "C35D114B0589A705DF622EC5E076343D7CFD82ED6EC2EF1173E6EA550F2407E6", 106),
        ("siege-frontier-v2", "06C13EEE966899CE2FE54DF2F241748F3DB8A74F879D639F54B0FFC127AD057B", 251),
    ];

    [Fact]
    public void 内容集v1保持旧表()
    {
        // 规格「内容集 v1 保持旧表」：同一种子在 v1 下的生成结果与引入新信物之前逐格一致（含收敛标记与重抽次数），不出现任何新四类。
        // 期望值是改动前抓取的黄金哈希（见 PreChangeGolden），不是由当前实现推出来的。
        foreach ((string mapId, string sha256, int unconverged) in PreChangeGolden)
        {
            MapData map = MapCatalog.Resolve(mapId);
            var text = new StringBuilder();
            int notConverged = 0;
            for (ulong seed = 0; seed < 500; seed++)
            {
                RelicGenerationRecord record = RelicGenerator.Generate(map, new GameSeed(seed), ContentSet.V1);
                Assert.Equal(ContentSet.V1, record.ContentSet);
                Assert.All(record.Placements, p => Assert.True(RelicWeights.IndexOf(p.Content.Type) < 6, $"{mapId} 种子 {seed} 出现 {p}"));
                Assert.All(
                    record.Placements.Where(p => p.Content.EmblemPiece is not null),
                    p => Assert.Contains(p.Content.EmblemPiece!.Value, ContentSets.PieceTypesOf(ContentSet.V1)));
                text.Append(record.Serialize());
                notConverged += record.Converged ? 0 : 1;
            }

            Assert.Equal(unconverged, notConverged);
            Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));
        }

        // v1 第一阶段分布仍是百分制旧表 45 / 20 / 15 / 8 / 7 / 5（改写前 `权重分布收敛` 的断言原样移到这里）。
        int[] counts = StageOneBirthCounts(ContentSet.V1);
        int[] expected = [45, 20, 15, 8, 7, 5];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(counts[i] * 1000 / 80000, (expected[i] * 10) - 10, (expected[i] * 10) + 10);
        }

        Assert.Equal(0, counts[6..].Sum());
    }

    [Theory]
    [InlineData(ContentSet.V2, 10)]
    [InlineData(ContentSet.V1, 6)]
    public void 徽记可绑定匠人(ContentSet set, int kinds)
    {
        // 规格 relic-generation「徽记可绑定匠人」（more-pieces-relics MODIFIED）：徽记绑定的棋子类型在该对局内容集的全部棋子类型中等概率抽取——
        // v2 十种各约十分之一，含匠人与旗手子、铁链子、哨兵子、界碑子；v1 仍为原六种各约六分之一（与改动前相同）。
        // v2 用 6000 个种子（徽记样本约 1.4 万，p = 10% 时 3σ ≈ 7.5‰），容差 ±15‰；v1 沿用 3000 个种子、±20‰。
        // 变异验证见测试报告 M-A5（EmblemPieces 把匠人排除在外）；段 A M-C7 / 段 B MB-G2（绑定集合改回 Enum.GetValues）。
        var counts = new SortedDictionary<PieceType, int>();
        int total = 0;
        ulong seeds = set == ContentSet.V2 ? 6000UL : 3000UL;
        for (ulong seed = 0; seed < seeds; seed++)
        {
            foreach (RelicPlacement p in RelicGenerator.Generate(Map, new GameSeed(seed), set).Placements)
            {
                if (p.Content.EmblemPiece is { } piece)
                {
                    counts[piece] = counts.TryGetValue(piece, out int n) ? n + 1 : 1;
                    total++;
                }
            }
        }

        Assert.Equal(ContentSets.PieceTypesOf(set).Order(), counts.Keys);
        Assert.Equal(kinds, counts.Count);
        Assert.Contains(PieceType.Artisan, counts.Keys);
        Assert.True(total > 6000, $"样本量 {total} 太小，不足以判断分布。");
        int expected = 1000 / kinds;
        int tolerance = set == ContentSet.V2 ? 15 : 20;
        foreach ((PieceType piece, int n) in counts)
        {
            int permille = n * 1000 / total;
            Assert.InRange(permille, expected - tolerance, expected + tolerance);
            Assert.True(n > 0, $"{piece} 从未被徽记绑定。");
        }
    }
}
