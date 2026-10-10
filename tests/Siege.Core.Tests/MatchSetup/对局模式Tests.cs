using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// board-link-mode 实现裁决 B1：对局配置的模式（Legacy 缺省 / Link），链接模式只能配链接图、链接图只能以链接模式建局。
/// </summary>
/// <remarks>
/// 变异验证（段 B 前半实跑）：M-B1 删去 <c>MatchFlow.Build</c> 里的 <c>RequireModeMatchesMap</c> 调用 → 本类「链接模式不能配旧地图」「链接图不能以旧模式建局」红 2，
/// 「缺省为旧模式」仍绿。
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
        MapData v5 = MapCatalog.Legacy.Resolve("siege-4p-base-v5");
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(v5, MatchFixtures.Seed, MatchFixtures.All, MatchOptions.Immediate with { Mode = MatchMode.Link }));
        Assert.Contains("链接模式只能配链接图", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 链接图不能以旧模式建局()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            MatchFlow.Create(LinkModeFixtures.Map, MatchFixtures.Seed, MatchFixtures.All, MatchOptions.Immediate));
        Assert.Contains("只能以链接模式建局", ex.Message, StringComparison.Ordinal);
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
