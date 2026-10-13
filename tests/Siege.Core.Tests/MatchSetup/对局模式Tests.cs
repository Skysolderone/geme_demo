using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// board-link-mode 实现裁决 B1：对局配置的模式（Legacy 缺省 / Link），链接模式只能配链接图、链接图只能以链接模式建局。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：M-B1 删去 <c>MatchFlow.Build</c> 里的 <c>RequireModeMatchesMap</c> 调用 → 本类「链接模式不能配旧地图」「链接图不能以旧模式建局」红 2，
/// 「缺省为旧模式」仍绿。M-B20（段 B 后半）人数校验改为 &gt; 4 → 「链接模式只有四人局」红 1。
/// </remarks>
public class 对局模式Tests
{
    [Fact]
    public void 缺省为旧模式()
    {
        Assert.Equal(MatchMode.Legacy, new MatchOptions().Mode);
        Assert.Equal(MatchMode.Legacy, MatchOptions.Immediate.Mode);
        MatchFlow match = MatchFixtures.Create();
        Assert.Equal(MatchMode.Legacy, match.Mode);
        Assert.Null(match.LinkStage);
        Assert.Equal(MatchMode.Legacy, match.Publish().Mode);
    }

    [Fact]
    public void 链接模式不能配旧地图()
    {
        // retire-legacy-maps：载体由 v5 换成 9×9 手写夹具（非链接图、4 人），断言不变。
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            MatchFixtures.Create(options: MatchOptions.Immediate with { Mode = MatchMode.Link }));
        Assert.Contains("链接模式只能配链接图", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 公开建局恒为链接模式()
    {
        // retire-legacy-maps 3.5（裁决 L6 = C）：原「链接图不能以旧模式建局」——公开 Create 不再接受旧模式：选项里的模式缺省（Legacy）照样建成链接模式；
        // 非链接图经公开 Create 一律报"链接模式只能配链接图"。旧模式只剩 CreateUnvalidated（手写夹具，见「缺省为旧模式」）。
        MatchFlow match = MatchFlow.Create(LinkModeFixtures.Map, MatchFixtures.Seed, MatchFixtures.All, MatchOptions.Immediate);
        Assert.Equal(MatchMode.Legacy, MatchOptions.Immediate.Mode);   // 样本口径：传入的确是缺省（旧）模式
        Assert.Equal(MatchMode.Link, match.Mode);
        Assert.True(match.Flags.IsBlind);
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(LinkModeFixtures.Map with { Id = "test-not-link" }, MatchFixtures.Seed, MatchFixtures.All, MatchOptions.Immediate));   // 合规地图、标识不是 link:
        Assert.Contains("链接模式只能配链接图", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 链接模式只有四人局()
    {
        // 裁决 3b / S1：链接模式的对局 MUST 恰为 4 名玩家，人数不符即建局报错。
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(LinkModeFixtures.Map, MatchFixtures.Seed, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2], LinkModeFixtures.Options));
        Assert.Contains("4 人局", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(LinkModeFixtures.Map, MatchFixtures.Seed, [MatchFixtures.P0, MatchFixtures.P1], LinkModeFixtures.Options));
    }

    [Fact]
    public void 链接图以链接模式建局()
    {
        MatchFlow match = LinkModeFixtures.Create();
        Assert.Equal(MatchMode.Link, match.Mode);
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);
        Assert.Equal(LinkMapGenerator.BirthBoards, match.Flags.ZoneCount);
        Assert.Equal(MatchMode.Link, match.Publish().Mode);
        Assert.True(match.Flags.IsBlind);
        Assert.False(MatchFixtures.Create().Flags.IsBlind);
    }
}
