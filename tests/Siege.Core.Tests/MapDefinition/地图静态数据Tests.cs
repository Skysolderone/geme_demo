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
        // retire-legacy-maps 段 B：原取 v5 三次；改为 4 人内置棋盘图（按固定生成图标识生成）三次。
        string[] snapshots = [.. Enumerable.Range(0, 3).Select(_ => MapFile.ToJson(MapCatalog.Resolve(MapCatalog.DefaultId)))];

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
    public void 拒绝裁切适配()
    {
        // 规格 Scenario「拒绝裁切适配」：3 人地图必须独立手工制作。
        // 本层连"按人数裁切"这条路径都不存在；把 4 人图改标成 3 人并砍掉两列，
        // 只会得到一张校验不通过的图。retire-legacy-maps 段 B：底图由 v5 换成 4 人内置棋盘图。原断言"出生区数不符 + 信物数越界"是 v5（标准档）的口径；
        // 棋盘图上砍到 9 列先在结构层被拒（宽度低于 15 列下限、右侧障碍越界，结构不过即不再往下校验），只改标人数不裁切时报出生区数越区间（3 人要 4 个，原图 5 个）。
        MapData full = MapCatalog.Resolve(MapCatalog.DefaultId);
        MapData cropped = full with { MaxPlayers = 3, Width = 9 };

        MapValidationResult result = MapValidator.Validate(cropped);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Code == "MAP_TOO_NARROW");
        Assert.Contains(result.Failures, f => f.Code == "OBSTACLE_OUT_OF_BOUNDS");
        Assert.Contains(MapValidator.Validate(full with { MaxPlayers = 3 }).Failures, f => f.Code == "BIRTH_ZONE_COUNT_OUT_OF_RANGE");
        Assert.True(MapValidator.Validate(full).IsValid);   // 反面：裁切之前合法
        Assert.Throws<MapValidationException>(() => GameBoard.Load(cropped));
    }

    [Fact]
    public void 地图基准校验通过()
    {
        // retire-legacy-maps 段 B：基准图由 v5 换成三张内置棋盘图。
        Assert.All(MapCatalog.BuiltinIds, id => Assert.True(MapValidator.Validate(MapCatalog.Resolve(id)).IsValid, id));
        Assert.Equal(3, MapCatalog.BuiltinIds.Count);
    }
}
