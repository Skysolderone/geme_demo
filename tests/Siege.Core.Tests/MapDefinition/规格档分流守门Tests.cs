using System.Text.RegularExpressions;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：map-definition —— Requirement: 地图规格档 / 地图静态校验规则：design D2 的声明表守门——校验器对规格档的分支只在声明表里。
/// retire-legacy-maps 1.6：原在 <c>边疆档静态校验Tests</c>（该类其余用例随边疆档删除）；原样迁到这里，类名即 <c>MapValidator</c> 注释里引用的
/// "守门 规格档分流守门Tests"。仓库根目录改取 <see cref="TestPaths.RepoRoot"/>（原 FrontierFixtures.RepoRoot，原样迁出）。
/// </summary>
public class 规格档分流守门Tests
{
    [Fact]
    public void 校验器对规格档的分支只在声明表里()
    {
        // tasks 1.5 / design D2 风险项：规格档让校验器出现两套分支，日后漂移。守门判据（源码扫描 MapValidator.cs）：
        //   ① 规格档枚举的字面量（MapProfile.Xxx）只允许出现在声明表 Rules 的初始化式区间内；
        //   ② 地图的规格档属性（裸标识符 Profile，成员访问与属性模式都算）全文件只出现一次——RulesOf 的查表；
        //   ③ 反面命中：声明表里确实有两档各一行；样本口径：文件长度下界；
        //   ④ 查表只查一次，声明行的每个字段只被一条规则读一次（检查阶段补）。
        // 正则不以 \b 开头（testing.md）。
        // 变异 M-A7a：在 ValidateBudgets 里加 `if (map.Profile == MapProfile.Frontier) { return; }` → ①② 同时红。
        // 变异 M-A7b：在 ValidateDistanceBalance 里加 `if (map.Profile != 0 && …) { return; }`（不写枚举字面量）→ ② 红。
        // 变异 M-A7c：在 ValidateRelicCells 里加 `if (map.Profile != MapProfile.Standard && …) { return; }` → ①② 红。
        // （三条变异的附加条件都取运行时恒假，不改行为——只有本守门会红，实测各红 1。）
        string source = File.ReadAllText(ValidatorPath());
        Assert.True(source.Length > 20_000, $"样本口径：MapValidator.cs 只有 {source.Length} 个字符。");

        const string tableStart = "private static readonly ImmutableDictionary<MapProfile, ProfileRules> Rules =";
        int start = source.IndexOf(tableStart, StringComparison.Ordinal);
        Assert.True(start >= 0, "找不到规格档声明表 Rules。");
        Assert.Equal(start, source.LastIndexOf(tableStart, StringComparison.Ordinal));   // 只有一张表
        // 表内各档的人数预算以 "}.ToImmutableDictionary()," 收尾（逗号），带分号的第一处才是声明表本身的结尾。
        int end = source.IndexOf("}.ToImmutableDictionary();", start, StringComparison.Ordinal);
        Assert.True(end > start, "找不到规格档声明表的结尾。");
        Assert.True(end - start < 2_000, "声明表区间异常地长：结尾锚点可能匹配到了别处。");   // retire-legacy-maps 4.2：只剩链接档一行（带说明注释），上限由 3500 收到 2000

        // retire-legacy-maps 4.2（L7）：口径改为"规格档字面量只剩 MapProfile.Link 一处、且在声明表内"（原四档各一行）。
        MatchCollection literals = Regex.Matches(source, @"MapProfile\s*\.\s*\w+");
        Assert.Contains(literals, m => m.Value.EndsWith("Link", StringComparison.Ordinal));
        Assert.Single(literals);   // 链接档一行，别无他处
        string[] scattered = [.. literals.Where(m => m.Index < start || m.Index > end).Select(m => $"{m.Value} @ 第 {LineOf(source, m.Index)} 行")];
        Assert.True(scattered.Length == 0, "规格档字面量出现在声明表之外：" + string.Join("；", scattered));

        // ② 数的是裸标识符 Profile，不是 ".Profile"：属性模式 `map is { Profile: not 0 }` / `{ Profile: > 0 }` 前面没有点、也不写枚举字面量，
        // 按成员访问去数会整条漏过（检查阶段用旧的两条正则在 M-C1 变异体上复算：都不命中）。前后的否定环视只为排除类型名 MapProfile / ProfileRules——
        // 它们正是要放行的，不属于 testing.md 说的"以词边界开头漏抓复合标识符"。
        // 变异 M-C1：在 ValidateRelicCells 里加 `if (map is { Profile: not 0 } && …恒假) { return; }` → 本测试红 1。
        MatchCollection reads = Regex.Matches(source, @"(?<![\w])Profile(?![\w])");
        string[] readLines = [.. reads.Select(m => $"第 {LineOf(source, m.Index)} 行")];
        Assert.True(reads.Count == 1, "地图的规格档属性只允许在 RulesOf 里读一次（含属性模式），实际：" + string.Join("、", readLines));
        Assert.Contains("RulesOf(MapData map) => Rules.GetValueOrDefault(map.Profile)", source, StringComparison.Ordinal);

        // ④ 查表只查一次（定义 + Validate 里的一次调用），声明行的每个字段只被一条规则读一次：
        // 拿"某个字段的取值"当"是不是边疆档"的代理去给别的规则分流，同样是散落分支，只是不写规格档三个字。
        // 变异 M-C2：在 ValidatePockets 前加 `if (rules.ZonesMustExceedPlayers && …恒假) { return …; }` → 本测试红 1。
        Assert.Equal(2, Regex.Matches(source, @"RulesOf\s*\(").Count);
        // board-map 段 B：外接宽度上限（原先是表外的常量 MaxWidth = 25）与"棋盘清单必须为空"（原先是表外的 BOARDS_NOT_ALLOWED 分支）
        // 改成声明行的字段 ColumnRange / PlateList，同样各只读一次；字段名有意不与地图数据的成员（Width / Boards）同名，免得本正则把 map.Xxx 数进来。
        // board-map 段 B 修正：棋盘档的行数区间（20–50）是声明行的新字段 RowRange（同理不叫 Height），也只读一次；标准档与边疆档行数不限。
        // 变异 M-B3（段 B 实跑）：在 ValidatePockets 前加 `if (rules.PlateList is null && …恒假) { return …; }` → 本测试红 1。
        Assert.DoesNotMatch(@"MaxWidth|MaxHeight|const\s+int\s+\w*(Width|Height)", source);
        // retire-legacy-maps 4.2：距离处理方式、连通性模式、咽喉标注、出生区须到达中央入口四个字段与通道宽 / 长随旧档删除——声明行只剩六个字段，各只读一次；
        // 删掉的字段与两种处理方式的枚举类型不得再出现（含注释之外的任何写法）。
        Assert.DoesNotMatch(@"(?<![\w])(DistanceHandling|PlateConnectivity|RequireLandmarks|BirthZonesMustReachEntrance|CorridorWidth|CorridorLength|ValidateCorridors|ValidateDistanceBalance|ValidateBirthZoneConnectivity)(?![\w])", source);
        // map-expand-25 D7：外接列 / 行区间（ColumnRange / RowRange）并入按人数的预算行（画布边长恰等），声明行只剩四个字段；删掉的两个字段名不得再出现。
        Assert.DoesNotMatch(@"(?<![\w])(ColumnRange|RowRange)(?![\w])", source);
        // seat-gap-25 E5：出生区数恰等于人数（预算行区间 (n, n)），"出生区数须多于人数"的声明字段删除，声明行只剩三个字段；删掉的字段名不得再出现。
        // 变异 M-S9（seat-gap-25 检查阶段实跑，只跑本类）：在 MapValidator.cs 的 RulesOf 上方加一行注释 `// 变异 ZonesMustExceedPlayers` → 本测试红 1（红在下面这条 DoesNotMatch）；备份还原、逐字节比对、刷新时间戳。
        Assert.DoesNotMatch(@"(?<![\w])ZonesMustExceedPlayers(?![\w])", source);
        foreach (string field in new[] { "SupportedPlayers", "Budgets", "PlateList" })
        {
            MatchCollection fieldReads = Regex.Matches(source, @"\.\s*" + field + @"(?![\w])");
            Assert.True(
                fieldReads.Count == 1,
                $"声明表字段 {field} 只允许被一条规则读一次，实际 {fieldReads.Count} 次：" + string.Join("、", fieldReads.Select(m => $"第 {LineOf(source, m.Index)} 行")));
        }


        // 棋盘清单里的棋盘类别不是规格档：它只在棋盘清单规则的几个函数里读，且这些函数只经 PlateList 那一次读取进入。
        Assert.Single(Regex.Matches(source, @"ValidatePlateList\s*\(map, rules"));
    }

    private static int LineOf(string source, int index) => source.AsSpan(0, index).Count('\n') + 1;

    private static string ValidatorPath() =>
        Path.Combine(TestPaths.RepoRoot(), "src", "Siege.Core", "Board", "MapValidator.cs");
}
