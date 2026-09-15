using Siege.Core.Board;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 阵营区分不得只依赖颜色</summary>
public class 阵营区分不得只依赖颜色Tests
{
    [Fact]
    public void 色觉辅助()
    {
        // 设计文档 §20 / 裁决 6、7：四方主色红 / 蓝 / 金 / 紫，同时各有两两不同的旗帜图案（三角 / 塔楼 / 太阳 / 莲花）——
        // 去色后仍可凭旗帜区分。数据层断言映射唯一且两两不同；去色截图可辨归阶段 B + 人工检查清单。
        // 变异验证 M-FA1：FactionTable 中金方的旗帜改为 BannerEmblem.Triangle → 本测试红 1。
        Assert.Equal(4, FactionTable.All.Length);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(new PlayerId(i), FactionTable.All[i].Player);
            Assert.Same(FactionTable.All[i], FactionTable.For(new PlayerId(i)));
        }

        Assert.Equal([FactionColor.Red, FactionColor.Blue, FactionColor.Gold, FactionColor.Purple], FactionTable.All.Select(f => f.Color));
        Assert.Equal([BannerEmblem.Triangle, BannerEmblem.Tower, BannerEmblem.Sun, BannerEmblem.Lotus], FactionTable.All.Select(f => f.Emblem));
        Assert.Equal(4, FactionTable.All.Select(f => f.Emblem).Distinct().Count());
        Assert.Equal(4, FactionTable.All.Select(f => f.Primary).Distinct().Count());
        Assert.Equal(4, FactionTable.All.Select(f => f.Name).Distinct().Count());
        Assert.Equal("金方(P2)", Labels.Player(new PlayerId(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => FactionTable.For(new PlayerId(4)));
    }
}
