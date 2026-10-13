using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Regex = System.Text.RegularExpressions.Regex;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：frontier-map / map-definition —— Requirement: 地图规格档</summary>
public class 地图规格档Tests
{
    [Fact]
    public void 规格档读写往返()
    {
        // Scenario：把一张链接档地图的初始状态导出为文件再读入 → 规格档仍为链接，棋盘清单与其余字段逐项相等。
        // 「写出时漏字段」只有往返测试抓得到（testing.md）。
        // retire-legacy-maps 1.6：载体由边疆档夹具（FrontierFixtures，随边疆档删除）改为手写链接档初始图，并按规格补比棋盘清单；断言不变。
        // 变异 M-A1：MapFile.ToJson 不写 Profile（恒 null）→ 本测试红。变异 M-A2：FromJson 不读 Profile → 本测试红。
        MapData original = LinkMapFixtures.Initial();

        string json = MapFile.ToJson(original);
        MapData restored = MapFile.FromJson(json);

        Assert.Contains("\"Profile\": \"Link\"", json, StringComparison.Ordinal);
        Assert.Equal(MapProfile.Link, restored.Profile);
        Assert.Equal(original.Boards.AsEnumerable(), restored.Boards.AsEnumerable());
        Assert.Equal(5, restored.Boards.Length);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        Assert.Equal(original.MaxPlayers, restored.MaxPlayers);
        Assert.Equal(original.Obstacles.Order(), restored.Obstacles.Order());
        Assert.Equal(5, restored.BirthZones.Length);
        for (int i = 0; i < original.BirthZones.Length; i++)
        {
            Assert.Equal(original.BirthZones[i].Order(), restored.BirthZones[i].Order());
        }

        Assert.Equal(original.RelicCells.OrderBy(kv => kv.Key), restored.RelicCells.OrderBy(kv => kv.Key));
        Assert.Equal(original.ChokePoints.Order(), restored.ChokePoints.Order());
        Assert.Equal(original.CentralEntrance, restored.CentralEntrance);
        Assert.Equal(original.DistanceTolerance, restored.DistanceTolerance);
        Assert.Equal(original.MinTwoEyeArea, restored.MinTwoEyeArea);
        Assert.Equal(json, MapFile.ToJson(restored));
        Assert.True(MapValidator.Validate(restored).IsValid);
    }

    [Fact]
    public void 未定义的规格档值被拒绝而不是抛异常()
    {
        // 没有声明行的只剩未定义的取值（只可能来自手工构造）：报成拒绝项并说明原因，不崩。
        // retire-legacy-maps 1.6：原名「棋盘档按自己的声明行校验_未定义的规格档值仍被拒绝而不是抛异常」；棋盘档那一半随棋盘档删除，
        // 未定义取值这一半的载体由棋盘档夹具改为手写链接档初始图，断言不变。
        Assert.True(MapValidator.Validate(LinkMapFixtures.Initial()).IsValid);
        MapData unknown = LinkMapFixtures.Initial() with { Profile = (MapProfile)9 };
        MapValidationFailure failure = Assert.Single(MapValidator.Validate(unknown).Failures);
        Assert.Equal("MAP_PROFILE_UNKNOWN", failure.Code);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(unknown));
    }

    [Fact]
    public void 未定义的规格档数字被指名报出()
    {
        // retire-legacy-maps 1.6：载体由边疆档夹具改为手写链接档初始图，断言不变。
        string json = MapFile.ToJson(LinkMapFixtures.Initial()).Replace("\"Profile\": \"Link\"", "\"Profile\": 7", StringComparison.Ordinal);

        var ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));
        Assert.Contains("Profile", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "缺少规格档字段")]
    [InlineData("\"Standard\"", "Standard 已移除")]
    [InlineData("\"Frontier\"", "Frontier 已移除")]
    [InlineData("\"Board\"", "Board 已移除")]
    [InlineData("\"board\"", "Board 已移除")]
    [InlineData("0", "Standard 已移除")]
    [InlineData("1", "Frontier 已移除")]
    [InlineData("2", "Board 已移除")]
    public void 已移除的规格档被拒绝(string? profile, string expected)
    {
        // retire-legacy-maps 4.1（裁决 L7，规格 map-definition「地图规格档」）：规格档只剩链接档；地图文件的规格档字段缺失、
        // 或写的是已移除的 Standard / Frontier / Board（字符串不分大小写，或其原数值 0 / 1 / 2）时拒绝读入，并说明该档已移除、只接受 Link。
        string canonical = MapFile.ToJson(LinkMapFixtures.Initial());
        Assert.Contains("\"Profile\": \"Link\",", canonical, StringComparison.Ordinal);   // 样本口径：恒写出规格档
        string json = profile is null
            ? canonical.Replace("\"Profile\": \"Link\",", string.Empty, StringComparison.Ordinal)
            : canonical.Replace("\"Profile\": \"Link\"", $"\"Profile\": {profile}", StringComparison.Ordinal);
        Assert.NotEqual(canonical, json);

        FormatException ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Link", ex.Message, StringComparison.Ordinal);
        Assert.Equal(MapProfile.Link, MapFile.FromJson(canonical.Replace("\"Profile\": \"Link\"", "\"Profile\": 3", StringComparison.Ordinal)).Profile);   // 反面：数值 3 仍是链接档
    }

    [Fact]
    public void 缺省规格档为链接档且恒写出()
    {
        // retire-legacy-maps 4.1：MapData.Profile 缺省改为 Link；文件恒写规格档字段（原"标准档不写出"随标准档删除）。
        MapData synthetic = TestMaps.Synthetic(size: 9, maxPlayers: 4);
        Assert.Equal(MapProfile.Link, synthetic.Profile);
        Assert.Equal([MapProfile.Link], Enum.GetValues<MapProfile>());
        Assert.Contains("\"Profile\": \"Link\"", MapFile.ToJson(synthetic), StringComparison.Ordinal);
    }

    [Fact]
    public void 规格档不改规则()
    {
        // Scenario：h=0 棋串三面敌子、第四面是 h=2 空格 → 无气，两档判定相同（terrain 标准算例 F6 / F7）。
        TerrainData cliff = TestMaps.Terrain(heights: [("F7", 2)]);
        foreach (MapProfile profile in Enum.GetValues<MapProfile>())
        {
            GameBoard board = GameBoard.LoadUnvalidated(TestMaps.Blank(cliff).Map with { Profile = profile })
                .Place("F6", TestMaps.P0)
                .Place("E6", TestMaps.P1)
                .Place("G6", TestMaps.P1)
                .Place("F5", TestMaps.P1);

            Group group = board.GroupAt(TestMaps.At("F6"))!;
            Assert.Empty(board.LibertiesOf(group));
            Assert.True(board.IsCaptured(group));
        }
    }

    [Fact]
    public void 规格档只被地图数据文件格式与校验器引用()
    {
        // D1 接口契约：下游（对局、AI、表现层、入口）不感知规格档。规格档枚举的类型名只允许出现在
        // 定义处、地图数据、文件格式、校验器，以及内置地图的生成器目录（段 B 的边疆图要在那里标规格档）。
        // 变异 M-A8：在 Siege.Core/Match/MatchFlow.cs 注入一句读 MapProfile.Frontier 的分支 → 本测试红。
        string src = Path.Combine(TestPaths.RepoRoot(), "src");   // retire-legacy-maps：RepoRoot 由已删除的 FrontierFixtures 原样迁到 TestPaths
        string[] allowed =
        [
            Path.Combine("Siege.Core", "Board", "MapProfile.cs"),
            Path.Combine("Siege.Core", "Board", "MapData.cs"),
            Path.Combine("Siege.Core", "Board", "MapFile.cs"),
            Path.Combine("Siege.Core", "Board", "MapValidator.cs"),
        ];
        string mapsDir = Path.Combine("Siege.Core", "Board", "Maps") + Path.DirectorySeparatorChar;

        string[] files = [.. Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}.godot{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];
        // 判据是"类型名 MapProfile 或裸标识符 Profile"二者之一：只扫类型名的话，下游写 `(int)Map.Profile == 1`、
        // `Map is { Profile: not 0 }`、`Map.Profile.ToString() == "Frontier"` 都不含 MapProfile 这个词，整条漏过
        // （检查阶段在 M-C3 变异体上复算旧判据：不命中）。否定环视只为不把 MapProfile / ProfileRules 这类类型名算成属性读取。
        // 变异 M-C3：在 MatchFlow.PlantPrototype 里加 `if ((int)Board.BaseMap.Profile == 1 && …恒假) { … }` → 本测试红 1。
        var profileToken = new Regex(@"MapProfile|(?<![\w])Profile(?![\w])");
        string[] hits = [.. files.Where(p => profileToken.IsMatch(File.ReadAllText(p)))
            .Select(p => Path.GetRelativePath(src, p))];

        Assert.True(files.Length >= 100, $"样本口径：只扫到 {files.Length} 个源文件。");           // 防扫了个空目录还绿
        Assert.Contains(files, p => p.Contains($"{Path.DirectorySeparatorChar}godot{Path.DirectorySeparatorChar}", StringComparison.Ordinal));   // 游离工程也在扫描范围
        Assert.All(allowed, a => Assert.Contains(a, hits));                                          // 反面：判据在唯一归属处确实命中
        Assert.Empty(hits.Where(h => !allowed.Contains(h) && !h.StartsWith(mapsDir, StringComparison.Ordinal)));

        // 白名单里的 MapData 不得替下游包一层：`public bool IsFrontier => Profile == …` 之后，任何人读 `map.IsFrontier` 都不含上面两个记号。
        // MapData.cs 里裸标识符 Profile 只允许出现一次——属性声明本身。
        // 变异 M-C4：在 MapData 里加 `public bool IsFrontier => Profile != MapProfile.Standard;` → 本测试红 1。
        string mapData = File.ReadAllText(Path.Combine(src, "Siege.Core", "Board", "MapData.cs"));
        Assert.Contains("public MapProfile Profile { get; init; }", mapData, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(mapData, @"(?<![\w])Profile(?![\w])"));
    }
}
