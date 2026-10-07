using System.Text.RegularExpressions;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：frontier-map design D2 的规格档声明表守门（boundaries.md「地图规格档的分流」：校验器别处不得出现对规格档的分支）。
/// retire-legacy-maps 段 C 由 <c>边疆档静态校验Tests</c> 改名而来：边疆档删除后，原文件里的边疆档用例全部随档删除——
/// 边疆档距离只报告、均衡的边疆图同样给出距离报告、边疆档不可达仍拒绝、不对称的边疆图通过校验（连同"校验器不引用对称检查"的源码扫描：
/// <c>MapSymmetry</c> 已于段 B 整删，扫描恒真）、超宽地图被拒绝而不是抛异常（25 列上限只属于标准 / 边疆档；棋盘档 60 列上限的同类断言在
/// <c>棋盘档预算与校验Tests.棋盘档的尺寸与清单要求</c>）。只留下声明表守门，随声明表收缩同步收紧。
/// </summary>
public class 规格档分流守门Tests
{
    [Fact]
    public void 校验器对规格档的分支只在声明表里()
    {
        // tasks 1.5 / design D2 风险项：规格档让校验器出现多套分支，日后漂移。守门判据（源码扫描 MapValidator.cs）：
        //   ① 规格档枚举的字面量（MapProfile.Xxx）只允许出现在两张声明表的初始化式区间内：已删除档表 RetiredProfiles（标准档一行）与声明表 Rules（棋盘档一行）；
        //   ② 地图的规格档属性（裸标识符 Profile，成员访问与属性模式都算）全文件只出现一次——RulesOf 的查表；
        //   ③ 反面命中：两张表里确实各有那一行；样本口径：文件长度下界；
        //   ④ 查表只查一次，声明行的每个字段只被一条规则读一次（检查阶段补）；
        //   ⑤ retire-legacy-maps 段 C：只服务标准 / 边疆档的声明字段（距离处理方式、可达性三项）与规则的拒绝码不得回到校验器里。
        // 正则不以 \b 开头（testing.md）。
        // 历史变异（声明表三行时实跑）：M-A7a / M-A7b / M-A7c 在 ValidateBudgets / ValidateDistanceBalance / ValidateRelicCells 里加读规格档的恒假分支 → ①② 红；
        // M-C1 属性模式 `map is { Profile: not 0 }` → ② 红；M-C2 / M-B3 / V8 拿声明字段当档位代理 → ④ 红。
        // 段 C 变异（实跑，各红 1 = 本测试）：MC-G1 在 ValidateLandmarks 里加 `if (map.Profile == MapProfile.Board && map.Width < 0) { return; }` → ①② 红；
        // MC-G2 在 ReportDistances 里加一条恒假条件下报 "DISTANCE_IMBALANCE" 的分支 → ⑤ 红；MC-G3 在 ValidateBudgets 里再读一次 rules.ZonesMustExceedPlayers（恒假条件）→ ④ 红。
        // 另：MC-V1（删掉 RetiredProfiles 的标准档行）→ ①③ 红（连同 地图规格档Tests 两条）。
        string source = File.ReadAllText(ValidatorPath());
        Assert.True(source.Length > 20_000, $"样本口径：MapValidator.cs 只有 {source.Length} 个字符。");

        (int retiredStart, int retiredEnd) = Region(source, "private static readonly ImmutableDictionary<MapProfile, string> RetiredProfiles =");
        (int rulesStart, int rulesEnd) = Region(source, "private static readonly ImmutableDictionary<MapProfile, ProfileRules> Rules =");
        bool InTables(int index) => (index > retiredStart && index < retiredEnd) || (index > rulesStart && index < rulesEnd);

        MatchCollection literals = Regex.Matches(source, @"MapProfile\s*\.\s*\w+");
        string retiredTable = source[retiredStart..retiredEnd];
        string rulesTable = source[rulesStart..rulesEnd];
        Assert.Contains("[MapProfile.Standard] = \"标准档\"", retiredTable, StringComparison.Ordinal);   // ③ 反面：已删除档表里有标准档
        Assert.Contains("[MapProfile.Board] = new(", rulesTable, StringComparison.Ordinal);              // ③ 反面：声明表里有棋盘档
        Assert.Single(Regex.Matches(retiredTable, @"MapProfile\s*\.\s*\w+"));
        Assert.Single(Regex.Matches(rulesTable, @"MapProfile\s*\.\s*\w+"));
        Assert.Equal(2, literals.Count);   // 两张表各一行，别无他处
        string[] scattered = [.. literals.Where(m => !InTables(m.Index)).Select(m => $"{m.Value} @ 第 {LineOf(source, m.Index)} 行")];
        Assert.True(scattered.Length == 0, "规格档字面量出现在声明表之外：" + string.Join("；", scattered));

        // ② 数的是裸标识符 Profile，不是 ".Profile"：属性模式 `map is { Profile: not 0 }` 前面没有点、也不写枚举字面量，按成员访问去数会整条漏过。
        // 前后的否定环视只为排除类型名 MapProfile / ProfileRules——它们正是要放行的。
        MatchCollection reads = Regex.Matches(source, @"(?<![\w])Profile(?![\w])");
        string[] readLines = [.. reads.Select(m => $"第 {LineOf(source, m.Index)} 行")];
        Assert.True(reads.Count == 1, "地图的规格档属性只允许在 RulesOf 里读一次（含属性模式），实际：" + string.Join("、", readLines));
        Assert.Contains("RulesOf(MapData map) => Lookup(map.Profile)", source, StringComparison.Ordinal);

        // ④ 查表只查一次（RulesOf 与 Lookup 各：定义 + 一次调用），声明行的每个字段只被一条规则读一次：
        // 拿"某个字段的取值"当"是不是某档"的代理去给别的规则分流，同样是散落分支，只是不写规格档几个字。
        Assert.Equal(2, Regex.Matches(source, @"RulesOf\s*\(").Count);
        Assert.Equal(2, Regex.Matches(source, @"Lookup\s*\(").Count);
        Assert.DoesNotMatch(@"MaxWidth|MaxHeight|const\s+int\s+\w*(Width|Height)", source);
        foreach (string field in new[] { "SupportedPlayers", "Budgets", "ZonesMustExceedPlayers", "ColumnRange", "RowRange", "PlateList" })
        {
            MatchCollection fieldReads = Regex.Matches(source, @"\.\s*" + field + @"(?![\w])");
            Assert.True(
                fieldReads.Count == 1,
                $"声明表字段 {field} 只允许被一条规则读一次，实际 {fieldReads.Count} 次：" + string.Join("、", fieldReads.Select(m => $"第 {LineOf(source, m.Index)} 行")));
        }

        // 棋盘清单里的棋盘类别不是规格档：它只在棋盘清单规则的几个函数里读，且这些函数只经 PlateList 那一次读取进入。
        Assert.Single(Regex.Matches(source, @"ValidatePlateList\s*\(map, rules"));

        // ⑤ 删掉的声明字段与只服务旧档的拒绝码不得回来（retire-legacy-maps 段 C）。
        Assert.DoesNotMatch(@"DistanceHandling|Reachability|RejectUnreachableTargets|RequireEntrancePath|RequireChokes", source);
        foreach (string code in new[]
        {
            "DISTANCE_IMBALANCE", "LANDMARK_UNREACHABLE", "BIRTH_ZONE_ISOLATED", "CHOKE_NOT_ANNOTATED", "TOLERANCE_NEGATIVE",
            "TOLERANCE_RELAX_WITHOUT_REASON", "POCKET_EXEMPTION_WITHOUT_REASON", "DEAD_POCKET", "BIRTH_ZONE_SPECIAL_SURFACE",
            "BOARDS_NOT_ALLOWED", "BIRTH_ZONE_COUNT_MISMATCH",
        })
        {
            Assert.DoesNotContain(code, source, StringComparison.Ordinal);
        }

        Assert.Contains("MAP_PROFILE_RETIRED", source, StringComparison.Ordinal);   // 反面：扫描器确实读到了校验器（新码在）

        // ⑥ 拒绝码 / 报告码一律是整串字面量，且在现行码白名单内（检查阶段补）：上面⑤按子串查旧码，
        // 拆成 "DEAD_" + "POCKET"、或用目标类型推断的 new("…", …) 构造，都能让旧规则悄悄回来而⑤不红。
        // 变异 MC-G4（检查方实跑）：ValidateLandmarks 里加恒假条件下 `new MapValidationFailure("DEAD_" + "POCKET", …)` → 改前 0 红（只剩⑤，被拆串绕过），改后本测试红。
        // 变异 MC-G5（检查方实跑）：同一处改写成目标类型推断的 `f.Add(new("X_" + "Y", …))` → 本测试红 1（由⑥的 new("码" 检查抓到）。
        string[] allowedCodes =
        [
            "BIRTH_BOARD_MISMATCH", "BIRTH_BOARD_SIZE_MISMATCH", "BIRTH_ZONE_COUNT_NOT_ABOVE_PLAYERS", "BIRTH_ZONE_COUNT_OUT_OF_RANGE",
            "BIRTH_ZONE_DISTANCE_REPORT", "BIRTH_ZONE_OUT_OF_BOUNDS", "BIRTH_ZONE_OVERLAP", "BIRTH_ZONE_SIZE_OUT_OF_RANGE", "BIRTH_ZONE_TOO_SMALL",
            "BOARDS_REQUIRED", "BOARD_CELL_NOT_FLAT_GRASS", "BOARD_CELL_NOT_PLAYABLE", "BOARD_MAP_DEEP_WATER", "BOARD_MAP_FENCE",
            "BOARD_OUT_OF_BOUNDS", "BOARD_SIZE_OUT_OF_RANGE", "BOARD_TOO_CLOSE", "CENTRAL_ENTRANCE_NOT_PLAYABLE", "CHOKE_NOT_PLAYABLE",
            "MAP_DIMENSION_INVALID", "MAP_ID_MISSING", "MAP_PROFILE_RETIRED", "MAP_PROFILE_UNKNOWN", "MAP_TOO_NARROW", "MAP_TOO_SHORT",
            "MAP_TOO_TALL", "MAP_TOO_WIDE", "OBSTACLE_OUT_OF_BOUNDS", "PLAYABLE_COUNT_OUT_OF_RANGE", "RELIC_BUDGET_MISMATCH",
            "RELIC_COUNT_OUT_OF_RANGE", "RELIC_ON_NON_PLAYABLE", "RELIC_OUTSIDE_BOARD", "RELIC_ZONE_MISMATCH", "SCENERY_CELL_PLAYABLE",
            "TERRAIN_OUT_OF_BOUNDS", "UNSUPPORTED_PLAYER_COUNT",
        ];
        // 不得用目标类型推断的 new("码", …) 构造（会绕过下面按类型名的抓取）。只认"码样"的大写字面量打头：声明表的 new("棋盘档只提供…", …) 不在此列。
        Assert.DoesNotMatch(@"new\s*\(\s*""[A-Z_]+""", source);
        MatchCollection constructions = Regex.Matches(source, @"new\s+MapValidationFailure\s*\(\s*([^,]*),");
        Assert.True(constructions.Count >= 35, $"样本口径：只抓到 {constructions.Count} 处 MapValidationFailure 构造。");
        Assert.Equal(constructions.Count, Regex.Matches(source, @"MapValidationFailure\s*\(").Count - 1);   // 减去类型声明本身：每一处构造都被抓到
        foreach (System.Text.RegularExpressions.Match m in constructions)
        {
            string arg = m.Groups[1].Value.Trim();
            Assert.True(
                Regex.IsMatch(arg, @"^""[A-Z_]+""$") && allowedCodes.Contains(arg.Trim('"')),
                $"第 {LineOf(source, m.Index)} 行的码 {arg} 不是现行白名单里的整串字面量。");
        }

        Assert.Equal(allowedCodes.Length, constructions.Select(m => m.Groups[1].Value.Trim()).Distinct().Count());   // 反面：白名单里每个码都确实在用，不留死条目
    }

    /// <summary>声明表初始化式的区间：从声明行到第一处带分号的 <c>}.ToImmutableDictionary();</c>。声明只许出现一次，区间不得异常地长。</summary>
    private static (int Start, int End) Region(string source, string declaration)
    {
        int start = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到声明：{declaration}");
        Assert.Equal(start, source.LastIndexOf(declaration, StringComparison.Ordinal));
        // 表内的人数预算以 "}.ToImmutableDictionary()," 收尾（逗号），带分号的第一处才是声明表本身的结尾。
        int end = source.IndexOf("}.ToImmutableDictionary();", start, StringComparison.Ordinal);
        Assert.True(end > start, $"找不到声明表的结尾：{declaration}");
        Assert.True(end - start < 2_000, "声明表区间异常地长：结尾锚点可能匹配到了别处。");
        return (start, end);
    }

    private static int LineOf(string source, int index) => source.AsSpan(0, index).Count('\n') + 1;

    private static string ValidatorPath() =>
        Path.Combine(TestMaps.RepoRoot(), "src", "Siege.Core", "Board", "MapValidator.cs");
}
