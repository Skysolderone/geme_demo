using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：map-definition —— Requirement: 地图为设计师固定的静态数据（地图文件的虚空码，terrain-bonus 段 B 裁决 2 / 实现决策 J3）。
/// 地表行在障碍（虚空）格写专用码 V，与可落子的岩石 K 等地表区分；Obstacles 字段只存虚空、字段名不变。读入时文件里只要出现 V，就要求"V 格集合 == Obstacles"，
/// 不一致即 <see cref="FormatException"/> 点名格；一个 V 都没有的旧文件兼容读入（以 Obstacles 为准）。写出一律用 V。
/// </summary>
/// <remarks>变异验证（段 B 阶段 1 实跑，过滤 MapDefinition.地图文件*）：M2a ParseObstacles 不校验（一致判据改恒真）→ 「虚空码与障碍不一致被拒」红 3；
/// M2b ToJson 虚空照写地表码 → 「虚空码读写往返」「没有虚空码的旧文件兼容读入」红 2。</remarks>
public class 地图文件虚空码Tests
{
    /// <summary>测试内独立解析：地表行（自上而下）里写 V 的格。</summary>
    private static HashSet<Coord> VoidCellsInText(string json, int height)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        string[] rows = [.. doc.RootElement.GetProperty("Surfaces").EnumerateArray().Select(e => e.GetString()!)];
        Assert.Equal(height, rows.Length);
        var cells = new HashSet<Coord>();
        for (int i = 0; i < rows.Length; i++)
        {
            for (int x = 0; x < rows[i].Length; x++)
            {
                if (rows[i][x] == 'V')
                {
                    cells.Add(new Coord(x, height - 1 - i));
                }
            }
        }

        return cells;
    }

    [Fact]
    public void 虚空码读写往返()
    {
        // 生成图 link:1:4：障碍格（虚空）在地表行写 V、岩石写 K；读回后障碍、地表逐字段相同，再写一次逐字节相同。
        // 另带一张手写小图：岩石 K 紧贴虚空 A1，两者在文件里可区分。V 读入大小写不敏感。
        MapData generated = MapCatalog.Resolve("link:1:4");
        TerrainData terrain = TestMaps.Terrain(surfaces: [("B1", Surface.Rock), ("A2", Surface.Rock), ("C3", Surface.DeepWater)]);
        MapData handmade = TestMaps.Synthetic(size: 9, maxPlayers: 4, obstacles: [TestMaps.At("A1"), TestMaps.At("J9")], terrain: terrain);

        foreach (MapData original in new[] { generated, handmade })
        {
            string json = MapFile.ToJson(original);
            HashSet<Coord> voids = VoidCellsInText(json, original.Height);
            Assert.NotEmpty(original.Obstacles);
            Assert.True(voids.SetEquals(original.Obstacles), $"{original.Id}：地表行的 V 格（{voids.Count}）与障碍（{original.Obstacles.Count}）不等");
            Assert.Contains(original.AllCoords(), c => original.SurfaceAt(c) == Surface.Rock && original.IsPlayable(c));   // 样本口径：确有可落子的岩石

            MapData loaded = MapFile.FromJson(json);
            Assert.Equal(original.Obstacles.Order(), loaded.Obstacles.Order());
            Assert.Equal(original.TerrainData.Surfaces.OrderBy(kv => kv.Key), loaded.TerrainData.Surfaces.OrderBy(kv => kv.Key));
            Assert.Equal(original.TerrainData.Heights.OrderBy(kv => kv.Key), loaded.TerrainData.Heights.OrderBy(kv => kv.Key));
            Assert.Equal(json, MapFile.ToJson(loaded));

            // 小写 v 同样读作虚空（与地表码同一大小写习惯）。
            MapData lower = MapFile.FromJson(json.Replace("V", "v", StringComparison.Ordinal));
            Assert.Equal(original.Obstacles.Order(), lower.Obstacles.Order());
        }

        Assert.Equal('V', MapFile.VoidCode);
        Assert.Equal("虚空", MapFile.VoidDisplayName);
    }

    [Theory]
    [InlineData("[\"A1\"]", "[\"GGG\",\"GGG\",\"VGV\"]", "C1")]       // 写了 V 但不在 Obstacles 里
    [InlineData("[\"A1\",\"C3\"]", "[\"GGG\",\"GGG\",\"VGG\"]", "C3")]  // 在 Obstacles 里但没写 V
    [InlineData("[\"B2\"]", "[\"GGG\",\"GKG\",\"VGG\"]", "B2")]       // V 与障碍各在一处，两侧都点名
    public void 虚空码与障碍不一致被拒(string obstacles, string surfaces, string expectedCell)
    {
        // 文件里出现了 V，就要求 V 格集合 == Obstacles；不一致即 FormatException，报文点名不一致的格。
        string json = "{\"Profile\":\"Link\",\"Id\":\"t\",\"Width\":3,\"Height\":3,\"MaxPlayers\":2,\"CentralEntrance\":\"B2\",\"Obstacles\":" + obstacles + ",\"Surfaces\":" + surfaces + "}";

        var ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));

        Assert.Contains("Obstacles", ex.Message, StringComparison.Ordinal);
        Assert.Contains(expectedCell, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 没有虚空码的旧文件兼容读入()
    {
        // 段 B 之前的文件：虚空格在地表行写的是普通码（G），全文件一个 V 都没有 → 以 Obstacles 为准读入；K 是可落子岩石。再写出时虚空改写成 V。
        const string text = "{\"Id\":\"old\",\"Width\":3,\"Height\":3,\"MaxPlayers\":4,\"Profile\":\"Link\",\"Obstacles\":[\"A1\"],\"BirthZones\":[],\"ChokePoints\":[],\"CentralEntrance\":\"B2\","
            + "\"Heights\":[\"000\",\"000\",\"000\"],\"Surfaces\":[\"GGG\",\"GKG\",\"GGG\"],\"Bridges\":[],\"Fences\":[]}";
        Assert.DoesNotContain("V", text[text.IndexOf("Surfaces", StringComparison.Ordinal)..], StringComparison.Ordinal);

        MapData onDisk = MapFile.FromJson(text);

        Assert.Equal(new[] { TestMaps.At("A1") }, onDisk.Obstacles.Order());
        Assert.Equal(Surface.Rock, onDisk.SurfaceAt(TestMaps.At("B2")));
        Assert.True(onDisk.IsPlayable(TestMaps.At("B2")));
        Assert.Equal(8, onDisk.PlayableCount);
        Assert.Equal(new[] { TestMaps.At("A1") }, VoidCellsInText(MapFile.ToJson(onDisk), 3));
    }
}
