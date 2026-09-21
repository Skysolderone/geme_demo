using System.Diagnostics;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>规格：openspec/changes/map-generator/specs/map-generation —— Requirement: 校验闭环。</summary>
public class 生成校验闭环Tests
{
    // 闭环测试不依赖"样本里恰好有重试过的种子"：用 internal 测试入口 GenerateDetailed(…, forcedFailures: k) 让前 k 次尝试直接不通过。
    // 第 k 次尝试的随机源只由（种子, k）决定，所以"前 k 次被判不通过"与"前 k 次真的没过"对后面的尝试是同一回事。

    [Theory]
    [InlineData(12345UL, 6)]
    [InlineData(7UL, 5)]
    [InlineData(21UL, 8)]
    public void 失败后确定性重试_同一种子总是落到同一尝试序号(ulong seed, int platforms)
    {
        // Scenario: 失败后确定性重试。
        var parameters = new MapGenParameters { PlatformCount = platforms };
        GeneratedMap natural = FrontierMapGenerator.GenerateDetailed(seed, parameters);

        // 把"自然通过的那一次"也判为不通过 → 必然落到更后面的某一次，而且每次都落到同一次、得到同一张图。
        int forced = natural.Attempt + 1;
        GeneratedMap retried = FrontierMapGenerator.GenerateDetailed(seed, parameters, FrontierMapGenerator.DefaultMaxAttempts, forced);
        GeneratedMap again = FrontierMapGenerator.GenerateDetailed(seed, parameters, FrontierMapGenerator.DefaultMaxAttempts, forced);
        Assert.True(retried.Attempt >= forced, $"前 {forced} 次已判不通过，却返回了第 {retried.Attempt} 次的图。");
        Assert.Equal(retried.Attempt, again.Attempt);
        Assert.Equal(MapFile.ToJson(retried.Map), MapFile.ToJson(again.Map));
        Assert.NotEqual(MapFile.ToJson(natural.Map), MapFile.ToJson(retried.Map));          // 换了尝试序号就是另一张图
        Assert.True(MapValidator.Validate(retried.Map).IsValid);

        // 前面几次"不通过"不影响后面的尝试：不加干预再生成，仍是自然通过的那一次、那一张图。
        GeneratedMap replay = FrontierMapGenerator.GenerateDetailed(seed, parameters);
        Assert.Equal(natural.Attempt, replay.Attempt);
        Assert.Equal(MapFile.ToJson(natural.Map), MapFile.ToJson(replay.Map));

        // 尝试序号只供诊断，不进标识。
        Assert.Equal(GeneratedMapId.Format(seed, parameters), retried.Map.Id);
    }

    [Fact]
    public void 尝试耗尽即报错并给出原因_不返回地图()
    {
        // Scenario: 尝试耗尽。上限 3、前 3 次都不通过 → 报错；消息带标识、上限与最后一次（第 2 次）的原因。
        // 变异验证 MG-4：把 GenerateDetailed 的循环条件 `attempt < maxAttempts` 改成 `<=`（多试一次）→ 本测试红。
        var ex = Assert.Throws<MapGenerationException>(() => FrontierMapGenerator.GenerateDetailed(12345, null, 3, forcedFailures: 3));
        Assert.Contains("gen:12345", ex.Message, StringComparison.Ordinal);
        Assert.Contains("3 次尝试内", ex.Message, StringComparison.Ordinal);
        Assert.Contains("第 2 次尝试", ex.Message, StringComparison.Ordinal);
        Assert.Matches("构造作废：.+|未通过边疆档静态校验：.+|不满足布局规则：.+", ex.Message);

        // 上限恰好够用时得到的就是不设上限时的那张图。
        GeneratedMap natural = MapGenFixtures.Generated(12345);
        GeneratedMap enough = FrontierMapGenerator.GenerateDetailed(12345, null, natural.Attempt + 1);
        Assert.Equal(MapFile.ToJson(natural.Map), MapFile.ToJson(enough.Map));
        if (natural.Attempt > 0)
        {
            // 真实的失败原因（不是测试入口注入的）也读得懂。
            var real = Assert.Throws<MapGenerationException>(() => FrontierMapGenerator.GenerateDetailed(12345, null, natural.Attempt));
            Assert.Matches("构造作废：.+|未通过边疆档静态校验：.+|不满足布局规则：.+", real.Message);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void 尝试上限必须为正(int maxAttempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrontierMapGenerator.Generate(1, null, maxAttempts));
    }

    [Fact]
    public void 样本无一耗尽缺省上限_且用到的尝试序号远小于上限()
    {
        Assert.Equal(64, FrontierMapGenerator.DefaultMaxAttempts);
        int worst = MapGenFixtures.Sample().Max(s => MapGenFixtures.Generated(s.Seed, s.Platforms).Attempt);
        Assert.InRange(worst, 0, 31);       // 留一半余量：逼近 64 说明构造式保证在退化，应当先修生成器而不是调上限
    }

    [Fact]
    public void 生成耗时()
    {
        // Scenario: 生成耗时——种子 1–50 单张（含校验与重试）中位数 ≤ 1 秒。实测中位数约 10 ms；
        // 总时长另取宽松上界（50 张 ≤ 60 秒），防的是数量级退化，不是机器抖动。这里不走缓存。
        FrontierMapGenerator.Generate(1000);                                   // 预热 JIT，不计时
        var millis = new List<long>();
        var total = Stopwatch.StartNew();
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var one = Stopwatch.StartNew();
            FrontierMapGenerator.Generate(seed);
            millis.Add(one.ElapsedMilliseconds);
        }

        total.Stop();
        millis.Sort();
        Assert.True(millis[millis.Count / 2] <= 1000, $"单张中位数 {millis[millis.Count / 2]} ms。");
        Assert.True(total.Elapsed < TimeSpan.FromSeconds(60), $"50 张共 {total.ElapsedMilliseconds} ms。");
    }
}
