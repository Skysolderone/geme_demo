using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// board-link-mode 实现裁决 B1 的建局约束：链接模式只能配链接图、恰为 4 人局。
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
            MatchFlow.CreateUnvalidated(nine, TestMaps.Seed, TestMaps.All, TestMaps.Relics(nine), LinkModeFixtures.Options));
        Assert.Contains("链接模式只能配链接图", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 链接模式只有四人局()
    {
        // 裁决 3b / S1：链接模式的对局 MUST 恰为 4 名玩家，人数不符即建局报错。
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(LinkModeFixtures.Map, TestMaps.Seed, [TestMaps.P0, TestMaps.P1, TestMaps.P2], LinkModeFixtures.Options));
        Assert.Contains("4 人局", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(LinkModeFixtures.Map, TestMaps.Seed, [TestMaps.P0, TestMaps.P1], LinkModeFixtures.Options));
    }
}
