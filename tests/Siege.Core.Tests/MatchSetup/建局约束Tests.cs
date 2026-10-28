using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// board-link-mode 实现裁决 B1 的建局约束：链接模式只能配链接图；map-expand-25 D1 起对局人数恰等于地图人数（标识 <c>link:&lt;种子&gt;:&lt;人数&gt;</c> 的人数段，2–4；原"恰为 4 人局"）。
/// retire-legacy-rules 2.1：原 <c>对局模式Tests</c> 改名；只测旧模式缺省 / 公开建局恒链接的三条随旧规则删除（清单第 3 节 #22–#24），保留本类两条。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：M-B1 删去 <c>MatchFlow.Build</c> 里的 <c>RequireModeMatchesMap</c> 调用 → 本类「链接模式不能配旧地图」红。
/// M-B20（段 B 后半）人数校验改为 &gt; 4 → 「链接模式只有四人局」红 1。
/// </remarks>
public class 建局约束Tests
{
    [Fact]
    public void 链接模式不能配旧地图()
    {
        // retire-legacy-maps：载体由 v5 换成 9×9 手写夹具（非链接图、4 人），断言不变。
        // retire-legacy-rules 1.5：9×9 地图数据移到 TestMaps，经测试口以链接选项建局（与原写法同为"链接模式 + 非链接图"），断言不变。
        MapData nine = TestMaps.NineByNine();
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            MatchFlow.CreateUnvalidated(nine, TestMaps.Seed, TestMaps.All, LinkModeFixtures.Options));
        Assert.Contains("链接模式只能配链接图", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 人数须等于地图人数()
    {
        // map-expand-25 D1（原裁决 3b / S1「链接模式只有四人局」）：对局人数 MUST 恰等于地图人数，人数不符即建局报错（不静默改）。
        // 4 人图配 3 人 / 2 人 → 报错，报文给出两边的人数。
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(LinkModeFixtures.Map, TestMaps.Seed, [TestMaps.P0, TestMaps.P1, TestMaps.P2], LinkModeFixtures.Options));
        Assert.Contains("link:1:4 是 4 人局，实际 3 人", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(LinkModeFixtures.Map, TestMaps.Seed, [TestMaps.P0, TestMaps.P1], LinkModeFixtures.Options));

        // 2 人图配 2 人建局成功（原硬锁下这里报"只有 4 人局"），配 3 人报错；3 人图配 3 人成功。
        // 变异 MB-1（实跑）：RequireLinkMatch 的判据改回 players != 4 → 本测试红 1（2 人图 2 人建局抛出）。
        MapData two = MapCatalog.Resolve("link:1:2");
        Assert.Equal(2, two.MaxPlayers);   // 样本口径
        Assert.Equal(MatchPhase.FlagPlanting, MatchFlow.Create(two, TestMaps.Seed, [TestMaps.P0, TestMaps.P1], LinkModeFixtures.Options).Phase);
        ArgumentException three = Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(two, TestMaps.Seed, [TestMaps.P0, TestMaps.P1, TestMaps.P2], LinkModeFixtures.Options));
        Assert.Contains("最多支持 2 人", three.Message, StringComparison.Ordinal);
        MapData triple = MapCatalog.Resolve("link:1:3");
        Assert.Equal(3, MatchFlow.Create(triple, TestMaps.Seed, [TestMaps.P0, TestMaps.P1, TestMaps.P2], LinkModeFixtures.Options).Players.Length);
        Assert.Contains("link:1:3 是 3 人局，实际 2 人", Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(triple, TestMaps.Seed, [TestMaps.P0, TestMaps.P1], LinkModeFixtures.Options)).Message, StringComparison.Ordinal);
    }
}
