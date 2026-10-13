using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：map-definition —— Requirement: 地图为设计师固定的静态数据</summary>
public class 地图静态数据Tests
{
    [Fact]
    public void 加载不引入随机()
    {
        // 本层不消费任何随机源——它连"对局种子"这个参数都没有。
        // 因此独立构建多次必须逐字节一致；对局内唯一的随机来源是信物的"具体内容"，
        // 由信物系统按种子生成，不属于本层。
        // retire-legacy-maps 1.6：载体由 v5（内置图）改为 link:1（链接档生成图，地图种子相同即同一张图），断言不变。
        string[] snapshots = [.. Enumerable.Range(0, 3).Select(_ => MapFile.ToJson(MapCatalog.Resolve("link:1")))];

        Assert.Equal(snapshots[0], snapshots[1]);
        Assert.Equal(snapshots[0], snapshots[2]);
    }

    [Fact]
    public void 信物格只记录位置与分区不含内容()
    {
        // 地图数据里 MUST NOT 有"信物类型/强度"字段——那是信物系统按对局种子生成的。
        // 用反射守门：给 RelicCellSpec 加任何内容字段都会让这个测试红。
        // （只断言"Zone 是两个枚举值之一"是恒真的，枚举本来就只有两个成员。）
        string[] members = [.. typeof(RelicCellSpec).GetProperties().Select(p => p.Name).Order()];

        Assert.Equal(["Budget", "Zone"], members);

        // 地图上与信物有关的字段只有"哪些格是信物格"这一项。
        string[] relicFields = [.. typeof(MapData).GetProperties()
            .Select(p => p.Name)
            .Where(n => n.Contains("Relic", StringComparison.Ordinal))
            .Order()];
        Assert.Equal(["RelicCells"], relicFields);
    }

    [Fact]
    public void 地图基准校验通过()
    {
        // retire-legacy-maps 1.6：原为 v5 基准图通过校验；改为链接档生成图 link:1 通过校验（规格 Scenario「生成图经过校验」）。
        Assert.True(MapValidator.Validate(MapCatalog.Resolve("link:1")).IsValid);
    }
}
