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
        string[] snapshots = [.. Enumerable.Range(0, 3).Select(_ => MapFile.ToJson(FourPlayerBaseMap.Create()))];

        Assert.Equal(snapshots[0], snapshots[1]);
        Assert.Equal(snapshots[0], snapshots[2]);
    }

    [Fact]
    public void 信物格只记录位置与分区不含内容()
    {
        // 地图数据里没有"信物类型/强度"字段——那是信物系统按种子生成的。
        MapData map = FourPlayerBaseMap.Create();

        foreach (RelicCellSpec spec in map.RelicCells.Values)
        {
            Assert.True(spec.Zone is RelicZone.BirthZone or RelicZone.Contested);
        }
    }

    [Fact]
    public void 地图基准校验通过()
    {
        Assert.True(MapValidator.Validate(FourPlayerBaseMap.Create()).IsValid);
    }
}
