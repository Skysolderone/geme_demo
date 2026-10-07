using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Regex = System.Text.RegularExpressions.Regex;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：frontier-map / board-map / retire-legacy-maps · map-definition —— Requirement: 地图规格档。
/// retire-legacy-maps 段 C：边疆档枚举成员删除、标准档只作缺省值保留且被校验器以"已删除"拒绝；
/// 原「规格档读写往返」（边疆档往返）随边疆档删除，非缺省档的往返由「棋盘档读写往返」守（变异 M-A1 / M-A2 同样会让它红）。
/// </summary>
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
        Assert.Contains("\"Profile\": \"Board\"", MapFile.ToJson(standard with { Profile = MapProfile.Board }), StringComparison.Ordinal);   // 反面：非缺省档写出字段（retire-legacy-maps 段 C：原用边疆档）
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
        // retire-legacy-maps 段 C：底图由边疆档小图换成 4 人内置棋盘图的导出文本。
        string json = MapFile.ToJson(MapCatalog.Resolve(MapCatalog.DefaultId));
        Assert.Contains("\"Profile\": \"Board\"", json, StringComparison.Ordinal);

        var ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json.Replace("\"Profile\": \"Board\"", "\"Profile\": 7", StringComparison.Ordinal)));
        Assert.Contains("Profile 为 7", ex.Message, StringComparison.Ordinal);

        // 未知的档名同样指名报出（改用原始 JSON 读入后不再交给枚举转换器，报的是 FormatException 而不是 JsonException）。
        var named = Assert.Throws<FormatException>(() => MapFile.FromJson(json.Replace("\"Profile\": \"Board\"", "\"Profile\": \"Hexagon\"", StringComparison.Ordinal)));
        Assert.Contains("Profile 为 Hexagon", named.Message, StringComparison.Ordinal);

        // 档名不分大小写（与原枚举转换器一致），数字形式的已定义取值照常读入。
        Assert.Equal(MapProfile.Board, MapFile.FromJson(json.Replace("\"Profile\": \"Board\"", "\"Profile\": \"board\"", StringComparison.Ordinal)).Profile);
        Assert.Equal(MapProfile.Board, MapFile.FromJson(json.Replace("\"Profile\": \"Board\"", "\"Profile\": 2", StringComparison.Ordinal)).Profile);
    }

    [Theory]
    [InlineData("\"Frontier\"")]
    [InlineData("\"frontier\"")]
    [InlineData("1")]
    public void 写着边疆档的地图文件读入即报已删除(string value)
    {
        // retire-legacy-maps 段 C：边疆档（枚举名 Frontier、原取值 1）的枚举成员删除。地图文件写着它时 MUST 明确报"边疆档已删除"，
        // 不得报成通用的"未知规格档"或 JSON 异常，更不得被当成别的档读入。
        // 变异 MC-P1（段 C 实跑）：ParseProfile 去掉字符串 Frontier 的专门分支 → 前两行落到"未知档名"的报文，本测试红 2。
        // 变异 MC-P2（段 C 实跑）：ParseProfile 去掉数字 1 的专门分支 → 第三行落到"未知取值"的报文，本测试红 1。
        string json = MapFile.ToJson(MapCatalog.Resolve(MapCatalog.DefaultId))
            .Replace("\"Profile\": \"Board\"", $"\"Profile\": {value}", StringComparison.Ordinal);
        Assert.Contains($"\"Profile\": {value}", json, StringComparison.Ordinal);

        var ex = Assert.Throws<FormatException>(() => MapFile.FromJson(json));

        Assert.Contains("边疆档已于 retire-legacy-maps 删除", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 标准档与缺规格档字段的地图被拒绝并报已删除(bool explicitStandard)
    {
        // retire-legacy-maps 段 C：标准档删除。缺 Profile 字段的旧地图文件（或显式写 Standard）照常读入为标准档，
        // 随后校验器只报一条 MAP_PROFILE_RETIRED（"标准档已于 retire-legacy-maps 删除"）并拒绝加载，不往下跑任何规则、不出报告项。
        // 底图是合法的 4 人内置棋盘图：换回棋盘档即通过——拒绝只来自档位。
        // 变异 MC-V1（段 C 实跑）：RetiredProfiles 表里删掉标准档那一行 → 落到 MAP_PROFILE_UNKNOWN，本测试红 2（连同 规格档分流守门 共红 3）。
        // 变异 MC-V2（段 C 实跑）：已删除档报出后不提前返回 → 另报 MAP_PROFILE_UNKNOWN（拒绝项不再只有一条），本测试红 2。
        // 变异 MC-W1（段 C 实跑）：MapFile 把档位写成数字 → 本类 9 条与 内置棋盘图Tests.内容不变 ×3 共红 12（三张内置棋盘图摘要由后者守住）。
        MapData board = MapCatalog.Resolve(MapCatalog.DefaultId);
        string text = MapFile.ToJson(board);
        string legacy = explicitStandard
            ? text.Replace("\"Profile\": \"Board\"", "\"Profile\": \"Standard\"", StringComparison.Ordinal)
            : Regex.Replace(text, "\n  \"Profile\": \"Board\",", string.Empty);
        Assert.NotEqual(text, legacy);
        MapData map = MapFile.FromJson(legacy);
        Assert.Equal(MapProfile.Standard, map.Profile);

        MapValidationResult result = MapValidator.Validate(map);

        MapValidationFailure failure = Assert.Single(result.Failures);
        Assert.Equal("MAP_PROFILE_RETIRED", failure.Code);
        Assert.Contains("标准档已于 retire-legacy-maps 删除", failure.Message, StringComparison.Ordinal);
        Assert.Empty(result.Reports);
        MapValidationException ex = Assert.Throws<MapValidationException>(() => GameBoard.Load(map));
        Assert.Contains("标准档已于 retire-legacy-maps 删除", ex.Message, StringComparison.Ordinal);

        // 反面：同一张图标回棋盘档即通过（且有三条距离报告项），证明拒绝只因档位。
        MapValidationResult asBoard = MapValidator.Validate(map with { Profile = MapProfile.Board });
        Assert.True(asBoard.IsValid, asBoard.ToString());
        Assert.Equal(3, asBoard.Reports.Length);
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
        // 定义处、地图数据、文件格式、校验器，以及内置地图的生成器目录（棋盘档生成器在那里标规格档）。
        // 变异 M-A8：在 Siege.Core/Match/MatchFlow.cs 注入一句读 MapProfile.Board 的分支 → 本测试红（retire-legacy-maps 段 C 前写的是 Frontier）。
        string src = Path.Combine(TestMaps.RepoRoot(), "src");
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
        // 变异 M-C4：在 MapData 里加 `public bool IsBoard => Profile == MapProfile.Board;` → 本测试红 1（retire-legacy-maps 段 C 前写的是 IsFrontier）。
        string mapData = File.ReadAllText(Path.Combine(src, "Siege.Core", "Board", "MapData.cs"));
        Assert.Contains("public MapProfile Profile { get; init; }", mapData, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(mapData, @"(?<![\w])Profile(?![\w])"));
    }
}
