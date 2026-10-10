using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/map-generation —— Requirement: 既有生成器降级为测试夹具（实现裁决 D7）。
/// 边疆档（<c>gen:</c>）与棋盘档（<c>board:</c>）生成器继续存在并逐字节不变，但只能经 <see cref="MapCatalog.Legacy"/> 与 <c>map</c> 调试子命令到达；
/// 玩家入口一律报错并说明只接受 <c>link:&lt;地图种子&gt;</c>（终端命令行由 <c>各入口按地图标识选图_链接模式Tests.旧标识报错</c> 守住）。
/// </summary>
public class 既有生成器降级为测试夹具Tests
{
    [Fact]
    public void 夹具生成不变()
    {
        // Scenario：Core 测试经夹具解析生成 gen:12345 与 board:12345 → 与直接调生成器逐字节相同（逐字节不变条款由各生成器自己的黄金值测试守住）。
        Assert.Equal(MapFile.ToJson(FrontierMapGenerator.Generate(12345)), MapFile.ToJson(MapCatalog.Legacy.Resolve("gen:12345")));
        Assert.Equal(MapFile.ToJson(BoardMapGenerator.Generate(12345)), MapFile.ToJson(MapCatalog.Legacy.Resolve("board:12345")));
        Assert.Equal("gen:12345", MapCatalog.Legacy.Resolve("gen:12345").Id);
        Assert.Equal("board:12345", MapCatalog.Legacy.Resolve("board:12345").Id);
    }

    [Theory]
    [InlineData("gen:12345")]
    [InlineData("gen:12345:p7:s1")]
    [InlineData("board:12345")]
    [InlineData("board:12345:n9")]
    [InlineData("gen")]
    [InlineData("board")]
    public void 玩家入口拒绝旧标识(string mapId)
    {
        // Scenario：以 --map=gen:12345 启动终端版 → 入口报错退出并说明只接受 link:<地图种子>。三个入口共用的解析一律拒绝 gen: / board: 标识族（含裸请求）。
        FormatException ex = Assert.Throws<FormatException>(() => MapCatalog.Resolve(mapId));
        Assert.Contains("只接受 link:<地图种子>", ex.Message, StringComparison.Ordinal);
        Assert.Contains(mapId, ex.Message, StringComparison.Ordinal);

        // 反面：链接图照常解析；夹具解析对同一标识照常（裸请求在夹具里也不是完整标识）。
        Assert.Equal("link:12345", MapCatalog.Resolve("link:12345").Id);
        if (mapId is "gen" or "board")
        {
            Assert.Throws<FormatException>(() => MapCatalog.Legacy.Resolve(mapId));
        }
        else
        {
            Assert.Equal(mapId.StartsWith("gen", StringComparison.Ordinal) ? GeneratedMapId.Normalize(mapId) : BoardMapId.Normalize(mapId), MapCatalog.Legacy.Resolve(mapId).Id);
        }
    }
}
