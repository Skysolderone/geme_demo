using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Text;

namespace Siege.Core.Tests.MapSelection;

/// <summary>
/// 规格：openspec/specs/map-selection —— Requirement: 开局选图界面（relic-sites 裁决 1–4：大图在缩略上点格插旗、"开始"的禁用 / 启用与文案、换图清空）。
/// 视图模型零引擎依赖：合规与原因只问 <see cref="FlagRules.FlagCellProblem"/>，像素 ↔ 格不在这里；小图不启用、"开始"照旧。
/// </summary>
public class 面板插旗视图模型Tests
{
    private static readonly Lazy<MapData> LazyLarge = new(() => MapCatalog.Resolve("link:7:4:l"));

    private static MapData Large => LazyLarge.Value;

    [Fact]
    public void 大图未选格时开始禁用并提示_选中后文案改为插旗并开始()
    {
        var pick = new FlagPickModel();
        Assert.False(pick.Enabled);
        Assert.True(pick.CanStart);   // 没绑图 / 小图：开始照旧可用
        Assert.Equal("开始", pick.StartText);
        Assert.Equal(string.Empty, pick.StartHint);

        pick.Bind(Large);
        Assert.True(pick.Enabled);
        Assert.False(pick.CanStart);
        Assert.Equal("开始", pick.StartText);
        Assert.Equal(Labels.PickFlagHint, pick.StartHint);
        Assert.Equal(Labels.PickFlagHint, pick.HoverReadout);

        Coord legal = FlagRules.LegalFlagCells(Large)[0];
        Assert.True(pick.Pick(legal));
        Assert.Equal(legal, pick.Picked);
        Assert.True(pick.CanStart);
        Assert.Equal(Labels.PlantAndStart, pick.StartText);
        Assert.Equal(string.Empty, pick.StartHint);
        Assert.Equal(string.Empty, pick.Notice);
        Assert.Contains(legal.ToNotation(), pick.HoverReadout, StringComparison.Ordinal);
    }

    [Fact]
    public void 点不合规格只给Core的原因_选中格不变()
    {
        var pick = new FlagPickModel();
        pick.Bind(Large);
        var corner = new Coord(0, 0);   // 13×13 出不了图
        Assert.False(pick.IsLegal(corner));
        Assert.False(pick.Pick(corner));
        Assert.Null(pick.Picked);
        Assert.Equal(FlagRules.FlagCellProblem(Large, corner), pick.Notice);
        Assert.Equal(FlagRules.FlagCellProblem(Large, corner), pick.ProblemOf(corner));
        Assert.False(pick.CanStart);
        Assert.Equal(pick.Notice, pick.HoverReadout);   // 没有悬停时显示最近一次的原因

        // 先选中再点不合规格：选中格保留，提示给原因。
        Coord legal = FlagRules.LegalFlagCells(Large)[0];
        Assert.True(pick.Pick(legal));
        Assert.False(pick.Pick(corner));
        Assert.Equal(legal, pick.Picked);
        Assert.True(pick.CanStart);

        // 悬停读数：坐标 + 原因；合规格写"合规"。
        pick.SetHover(corner);
        Assert.StartsWith($"{corner.ToNotation()}：", pick.HoverReadout, StringComparison.Ordinal);
        Assert.Contains(pick.ProblemOf(corner)!, pick.HoverReadout, StringComparison.Ordinal);
        pick.SetHover(legal);
        Assert.Contains("合规", pick.HoverReadout, StringComparison.Ordinal);
        pick.SetHover(null);
        Assert.Equal(pick.Notice, pick.HoverReadout);   // 没有悬停：最近一次不合规的原因优先于选中格的确认
        Assert.True(pick.Pick(legal));   // 再点合规格：原因清空，读数回到选中格
        Assert.Contains(legal.ToNotation(), pick.HoverReadout, StringComparison.Ordinal);
    }

    [Fact]
    public void 换图即清空选中格_同一张图不清_小图不启用()
    {
        var pick = new FlagPickModel();
        pick.Bind(Large);
        Coord legal = FlagRules.LegalFlagCells(Large)[0];
        pick.SetHover(legal);
        Assert.True(pick.Pick(legal));

        // 同一张图（生成失败回滚、重复刷新）：不清。
        pick.Bind(MapCatalog.Resolve("link:7:4:l"));
        Assert.Equal(legal, pick.Picked);
        Assert.Equal(legal, pick.Hover);

        // 换种子 / 人数 / 尺寸都是换标识：清空选中与悬停，开始回禁用。
        pick.Bind(MapCatalog.Resolve("link:8:4:l"));
        Assert.Null(pick.Picked);
        Assert.Null(pick.Hover);
        Assert.False(pick.CanStart);

        pick.Bind(MapCatalog.Resolve("link:8:3:l"));
        Assert.True(pick.Enabled);
        Assert.False(pick.CanStart);

        // 小图（旧模式）：不启用，"开始"照旧可用、不提示、悬停不记、点格无效。
        pick.Bind(MapCatalog.Resolve("link:8:4"));
        Assert.False(pick.Enabled);
        Assert.True(pick.CanStart);
        Assert.Equal("开始", pick.StartText);
        Assert.Equal(string.Empty, pick.StartHint);
        Assert.Equal(string.Empty, pick.HoverReadout);
        pick.SetHover(new Coord(5, 5));
        Assert.Null(pick.Hover);
        Assert.False(pick.Pick(new Coord(5, 5)));
        Assert.Null(pick.Picked);

        // 解绑（建局之后）：清空。
        pick.Bind(Large);
        Assert.True(pick.Pick(legal));
        pick.Bind(null);
        Assert.Null(pick.Picked);
        Assert.False(pick.Enabled);
    }

    [Fact]
    public void 悬停遗址格显示档位名()
    {
        var pick = new FlagPickModel();
        pick.Bind(Large);
        Assert.NotEmpty(Large.RelicSites);
        foreach (RelicSite site in Large.RelicSites)
        {
            Assert.Equal(site.Tier, pick.SiteAt(site.Cell));
            string readout = pick.Readout(site.Cell);
            Assert.Contains(site.Cell.ToNotation(), readout, StringComparison.Ordinal);
            Assert.Contains(Labels.RelicSite(site.Tier), readout, StringComparison.Ordinal);
            Assert.False(pick.IsLegal(site.Cell));   // 旗心距遗址 ≥ 7（relic-sites 裁决 9）
        }

        Assert.Equal("稀有信物遗址", Labels.RelicSite(RelicTier.Rare));
        Assert.Equal("普通信物遗址", Labels.RelicSite(RelicTier.Common));
        Coord plain = FlagRules.LegalFlagCells(Large)[0];
        Assert.Null(pick.SiteAt(plain));
        Assert.DoesNotContain("遗址", pick.Readout(plain), StringComparison.Ordinal);
        Assert.Equal(FlagRules.PlateReach, FlagPickModel.PlateReach);
    }
}
