using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Regex = System.Text.RegularExpressions.Regex;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：frontier-map / map-definition —— Requirement: 地图规格档</summary>
public class 地图规格档Tests
{
    [Fact]
    public void 缺省为标准档()
    {
        // Scenario：读入一张没有规格档字段的地图文件 → 规格档为标准。
        // retire-legacy-maps 段 B：原读四份 maps/siege-4p-base-v1..v5.json（随旧图删除）；改为把棋盘图导出文本去掉 Profile 一行后读入。
        string text = MapFile.ToJson(MapCatalog.Resolve(MapCatalog.DefaultId));
        Assert.Contains("\"Profile\": \"Board\"", text, StringComparison.Ordinal);
        string stripped = Regex.Replace(text, "\n  \"Profile\": \"Board\",", string.Empty);
        Assert.DoesNotContain("\"Profile\"", stripped, StringComparison.Ordinal);

        Assert.Equal(MapProfile.Standard, MapFile.FromJson(stripped).Profile);
        Assert.Equal(MapProfile.Board, MapFile.FromJson(text).Profile);   // 反面：带字段时读出的是棋盘档
    }

    [Fact]
    public void 标准档写出时不带规格档字段()
    {
        // 标准档省略该字段（写出逐字节与引入规格档之前相同）。原名「标准档写出时不带规格档字段且基准图文件逐字节不变」：
        // retire-legacy-maps 段 B 删除 v5 与磁盘上的 maps/siege-4p-base-v5.json，"与磁盘文件逐字节相同"一半随之删除；改用标准档合成图。
        MapData standard = TestMaps.Synthetic(size: 9, maxPlayers: 4);
        Assert.Equal(MapProfile.Standard, standard.Profile);
        string json = MapFile.ToJson(standard);
        Assert.DoesNotContain("\"Profile\"", json, StringComparison.Ordinal);
        Assert.Equal(json, MapFile.ToJson(MapFile.FromJson(json)));
        Assert.Contains("\"Profile\"", MapFile.ToJson(standard with { Profile = MapProfile.Frontier }), StringComparison.Ordinal);   // 反面：非缺省档写出字段
    }

    [Fact]
    public void 规格档读写往返()
    {
        // Scenario：边疆档地图导出再读入 → 规格档仍为边疆，其余字段逐项相等。
        // 「写出时漏字段」只有往返测试抓得到（testing.md）；边疆 ≠ 缺省值，漏写 / 漏读都会读回标准档。
        // 变异 M-A1：MapFile.ToJson 不写 Profile（恒 null）→ 本测试红。变异 M-A2：FromJson 不读 Profile → 本测试红。
        MapData original = FrontierFixtures.Map();

        string json = MapFile.ToJson(original);
        MapData restored = MapFile.FromJson(json);

        Assert.Contains("\"Profile\": \"Frontier\"", json, StringComparison.Ordinal);
        Assert.Equal(MapProfile.Frontier, restored.Profile);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        Assert.Equal(original.MaxPlayers, restored.MaxPlayers);
        Assert.Equal(original.Obstacles.Order(), restored.Obstacles.Order());
        Assert.Equal(6, restored.BirthZones.Length);
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
    public void 棋盘档读写往返()
    {
        // Scenario（board-map）：棋盘档地图导出再读入 → 规格档仍为棋盘，棋盘清单与其余字段逐项相等。
        // 夹具宽 29 列：1 号出生棋盘在双字母列上，漏改列标解析时这里读不回来。
        MapData original = BoardMapFixtures.Map();

        string json = MapFile.ToJson(original);
        MapData restored = MapFile.FromJson(json);

        Assert.Contains("\"Profile\": \"Board\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Boards\"", json, StringComparison.Ordinal);
        Assert.Contains("\"AB9\"", json, StringComparison.Ordinal);
        Assert.Equal(MapProfile.Board, restored.Profile);
        Assert.Equal(3, restored.Boards.Length);
        Assert.Equal(original.Boards.AsEnumerable(), restored.Boards.AsEnumerable());
        Assert.Equal(new BoardPlate(Coord.Parse("X5"), 5, 5, BoardPlateKind.Birth), restored.Boards[1]);
        Assert.Equal(new BoardPlate(Coord.Parse("L3"), 9, 9, BoardPlateKind.Public), restored.Boards[2]);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        Assert.Equal(original.MaxPlayers, restored.MaxPlayers);
        Assert.Equal(original.Obstacles.Order(), restored.Obstacles.Order());
        Assert.Equal(original.BirthZones.Length, restored.BirthZones.Length);
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
    }

    [Fact]
    public void 棋盘档按自己的声明行校验_未定义的规格档值仍被拒绝而不是抛异常()
    {
        // board-map 段 B：棋盘档有了声明行。合规的 4 人棋盘档图被接受（逐条 Scenario 见 棋盘档预算与校验Tests）；
        // 段 A 的 29×13 2 人夹具不合棋盘档的规模（行数不足 20 行），按棋盘档自己的规则被拒绝，而不是"没有声明表"。
        Assert.True(MapValidator.Validate(BoardMapFixtures.FourPlayerMap()).IsValid);
        GameBoard.Load(BoardMapFixtures.FourPlayerMap());

        MapValidationResult small = MapValidator.Validate(BoardMapFixtures.Map());
        Assert.Equal("MAP_TOO_SHORT", Assert.Single(small.Failures).Code);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(BoardMapFixtures.Map()));

        // 没有声明行的只剩未定义的取值（只可能来自手工构造）：报成拒绝项并说明原因，不崩。
        MapData unknown = BoardMapFixtures.FourPlayerMap() with { Profile = (MapProfile)9 };
        MapValidationFailure failure = Assert.Single(MapValidator.Validate(unknown).Failures);
        Assert.Equal("MAP_PROFILE_UNKNOWN", failure.Code);
        Assert.Throws<MapValidationException>(() => GameBoard.Load(unknown));
    }

    [Fact]
    public void 未定义的规格档数字被指名报出()
    {
        string json = MapFile.ToJson(FrontierFixtures.Map()).Replace("\"Profile\": \"Frontier\"", "\"Profile\": 7", StringComparison.Ordinal);

        var ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));
        Assert.Contains("Profile", ex.Message, StringComparison.Ordinal);
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
        string src = Path.Combine(FrontierFixtures.RepoRoot(), "src");
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
