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
    // retire-legacy-maps 1.6：载体由 v5（8 个出生区信物格）改为 link:1 初始图（5 块出生棋盘 × 2 = 10 个），样本数与期望格数随之改，断言不变。
    private static readonly MapData Map = MapCatalog.Resolve("link:1");

    /// <summary>第一阶段统计的样本数：10000 种子 × 10 格。</summary>
    private const int StageOneSamples = 100000;

    [Fact]
    public void 出生区无高阶信物()
    {
        // 规格 Scenario：link: 初始地图 10 个出生区信物格（5 块出生棋盘各 2 个）；2000 个种子下强度全为 +1（徽记为单枚等效）。
        // 变异验证 M-G4：Draw 里去掉 `spec.Zone == BirthZone ? 0 :`，改用 options.UpgradePermilleOf(spec.Budget) 且 Birth 档给 200 → 红 1（本测试）。
        for (ulong seed = 0; seed < 2000; seed++)
        {
            RelicGenerationRecord record = RelicGenerator.Generate(Map, new GameSeed(seed));
            RelicPlacement[] birth = [.. record.Placements.Where(p => p.Spec.Zone == RelicZone.BirthZone)];
            Assert.Equal(10, birth.Length);
            Assert.All(birth, p => Assert.False(p.Content.IsAdvanced, $"种子 {seed} 出生区 {p} 为高阶"));
            Assert.All(birth, p => Assert.Equal(1, p.Content.Magnitude));
        }
    }

    /// <summary>
    /// 第一阶段（MaxRerolls = 0）出生区类型计数，按 <see cref="RelicWeights.Order"/> 下标。10000 种子 × 10 格 = 100000 样本。
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

        Assert.Equal(StageOneSamples, total);
        return counts;
    }

    [Fact]
    public void 权重分布收敛()
    {
        // more-pieces-relics MODIFIED：缺省内容集 v2 的千分制表——徽记 360 / 探勘 160 / 兵站 120 / 征召 64 / 军令 56 / 先锋 40 / 连营、犄角、驿站、工坊各 50。
        // 第一阶段的权重抽取用 MaxRerolls = 0 观察（第二阶段的预算校正会按设计改变最终分布，那是另一条 Requirement）。
        // 100000 样本，容差 ±10‰（p = 36% 时 3σ 约 5‰）。v1 百分制表（45 / 20 / 15 / 8 / 7 / 5）的同一断言见 `内容集v1保持旧表`。
        // 变异验证 M-G5（改写前）：BirthTable 改为 [45, 20, 15, 8, 5, 7]（对调军令与先锋）→ 红 1（本测试）。
        int[] counts = StageOneBirthCounts(ContentSet.V2);
        int[] expected = [360, 160, 120, 64, 56, 40, 50, 50, 50, 50];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.InRange(counts[i] * 1000 / StageOneSamples, expected[i] - 10, expected[i] + 10);
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
            Assert.InRange(counts[RelicWeights.IndexOf(fresh)] * 1000 / StageOneSamples, 45, 55);
        }

        int[] original = [450, 200, 150, 80, 70, 50];   // v1 的千分比，× 80% 即 v2
        for (int i = 0; i < original.Length; i++)
        {
            Assert.InRange(counts[i] * 1000 / StageOneSamples, (original[i] * 8 / 10) - 5, (original[i] * 8 / 10) + 5);
        }
    }

    /// <summary>
    /// <c>link:1</c> 初始图在内容集 v1 下种子 0–499 生成、逐局 <c>Serialize()</c> 拼接后的 SHA-256，以及未收敛局数。
    /// retire-legacy-maps 1.6 改钉：原黄金值取自引入新信物之前（段 A 提交 854d371）的代码对四张内置图（v5 / 2 人 / 3 人 / 边疆）的生成结果，随旧地图删除；
    /// 链接图在那时尚不存在，无从取"引入之前"的值。这里在删除旧地图之前、以当时的实现对 <c>link:1</c> 重钉（v1 生成路径自那以后未改，四张旧图的旧黄金值直到删除前一直为绿）。
    /// </summary>
    private static readonly (string MapId, string Sha256, int Unconverged)[] PreChangeGolden =
    [
        ("link:1", "9C39B357E3B984DAF502D6680CF98A3B64F8D58CF13C44AB596C1C966B26B835", 109),
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
            Assert.InRange(counts[i] * 1000 / StageOneSamples, (expected[i] * 10) - 10, (expected[i] * 10) + 10);
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
