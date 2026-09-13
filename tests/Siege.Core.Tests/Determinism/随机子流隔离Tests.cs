using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Tests.Determinism;

/// <summary>规范：.trellis/spec/core/determinism.md ——「随机子流隔离」与「禁止浮点」。这是 §17 全部平衡分析的地基。</summary>
public class 随机子流隔离Tests
{
    private static readonly GameSeed Seed = new(0xC0FFEE_2026_0913UL);

    [Fact]
    public void 同种子同子流逐项一致()
    {
        // 变异验证 M-D1：GameSeed.Stream 在派生根里混入 Environment.TickCount → 红 4（本测试 + 三条生成可复现测试）。
        RandomStream a = Seed.Stream(GameSeed.RelicGeneration);
        RandomStream b = Seed.Stream(GameSeed.RelicGeneration);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }

        Assert.Equal(1000, a.Consumed);
    }

    [Fact]
    public void 改变一条子流的消费次数不影响另一条()
    {
        // 规范原文：改变其中一条子流的消费次数，MUST NOT 影响其他子流的取值。
        // 变异验证 M-D2：GameSeed.Stream 改为忽略 name（三条子流共用一条序列）→ 本测试红（两次 relic 序列仍相等，但「不同子流名序列不同」红）；
        // M-D3：让 Stream 返回一个共享的静态 RandomStream 实例（真正的共用序列）→ 本测试红。
        ulong[] Take(int recruitConsumption)
        {
            RandomStream recruit = Seed.Stream(GameSeed.Recruit);
            for (int i = 0; i < recruitConsumption; i++)
            {
                recruit.NextInt(100);
            }

            RandomStream relic = Seed.Stream(GameSeed.RelicGeneration);
            return [.. Enumerable.Range(0, 64).Select(_ => relic.NextUInt64())];
        }

        Assert.Equal(Take(0), Take(1));
        Assert.Equal(Take(0), Take(37));
    }

    [Fact]
    public void 不同子流名序列不同()
    {
        // 变异验证 M-D2（Stream 忽略 name）→ 红 1（本测试）。
        RandomStream relic = Seed.Stream(GameSeed.RelicGeneration);
        RandomStream recruit = Seed.Stream(GameSeed.Recruit);
        RandomStream setup = Seed.Stream(GameSeed.Setup);

        ulong[] r = [.. Enumerable.Range(0, 8).Select(_ => relic.NextUInt64())];
        ulong[] c = [.. Enumerable.Range(0, 8).Select(_ => recruit.NextUInt64())];
        ulong[] s = [.. Enumerable.Range(0, 8).Select(_ => setup.NextUInt64())];

        Assert.NotEqual(r, c);
        Assert.NotEqual(r, s);
        Assert.NotEqual(c, s);
        // 不同种子、同子流名也必须不同
        Assert.NotEqual(r, [.. Enumerable.Range(0, 8).Select(_ => new GameSeed(Seed.Value + 1).Stream(GameSeed.RelicGeneration).NextUInt64())]);
    }

    [Fact]
    public void 改变征募决策后信物生成逐格不变()
    {
        // 规范强制回归：改变某玩家的一次征募决策（= recruit 子流消费次数变化）后，该局信物生成结果逐格不变。
        // 变异验证 M-D3（Stream 返回共享实例）→ 本测试红。
        MapData map = FourPlayerBaseMap.Create();

        RelicGenerationRecord baseline = RelicGenerator.Generate(map, Seed);

        RandomStream recruit = Seed.Stream(GameSeed.Recruit);
        for (int i = 0; i < 17; i++)
        {
            recruit.WeightedPick([40, 20, 18, 12, 10]);
        }

        RelicGenerationRecord after = RelicGenerator.Generate(map, Seed);
        Assert.Equal(baseline.Placements, after.Placements);
    }

    [Fact]
    public void 加权抽样只走整数累积权重()
    {
        // 权重 0 的项永远抽不到；单项权重 → 恒返回该项；统计频率与权重成比例（3000 次，±4 个百分点）。
        // 变异验证 M-D4：WeightedPick 改为 roll <= cumulative → 权重 0 的项被抽到 → 红 1（本测试）。
        RandomStream rng = Seed.Stream("weights-test");
        Assert.Equal(1, rng.WeightedPick([0, 5, 0]));

        int[] counts = new int[3];
        for (int i = 0; i < 3000; i++)
        {
            counts[rng.WeightedPick([0, 3, 1])]++;
        }

        Assert.Equal(0, counts[0]);
        Assert.InRange(counts[1], 3000 * 71 / 100, 3000 * 79 / 100);
        Assert.Throws<ArgumentException>(() => rng.WeightedPick([0, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.WeightedPick([1, -1]));
    }

    [Fact]
    public void 等概率整数无偏且在界内()
    {
        RandomStream rng = Seed.Stream("ints");
        int[] counts = new int[7];
        for (int i = 0; i < 7000; i++)
        {
            int v = rng.NextInt(7);
            Assert.InRange(v, 0, 6);
            counts[v]++;
        }

        Assert.All(counts, c => Assert.InRange(c, 850, 1150));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextPermille(1001));
    }

    [Fact]
    public void 种子文本往返()
    {
        Assert.Equal(Seed, GameSeed.Parse(Seed.ToString()));
        Assert.Equal("00000000C0FFEE20", new GameSeed(0xC0FFEE20).ToString());
    }

    [Fact]
    public void 随机与生成路径不含浮点()
    {
        // 规范「禁止浮点」：Determinism/ 与 Relics/ 的源码不得出现 double / float / decimal 类型或 Math.Round/Floor。
        // 变异验证 M-D5：在 RelicGenerator 里加一行 `double _ = 0.08;` → 红 1（本测试）。
        string root = SourceRoot();
        string[] files = [.. Directory.GetFiles(Path.Combine(root, "src", "Siege.Core", "Determinism"), "*.cs"),
                          .. Directory.GetFiles(Path.Combine(root, "src", "Siege.Core", "Relics"), "*.cs")];
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string token in new[] { "double", "float", "decimal", "Math.Round", "Math.Floor", "Math.Ceiling", "System.Random" })
            {
                Assert.False(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} 含 {token}");
            }
        }
    }

    internal static string SourceRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "siege.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到 siege.sln 所在目录。");
    }
}
