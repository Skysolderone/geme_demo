using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Siege.Core.Board;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>规格：frontier-map / map-definition —— Requirement: 地图静态校验规则（边疆档分流）与 design D2 的声明表守门</summary>
public class 边疆档静态校验Tests
{
    [Fact]
    public void 边疆档距离只报告()
    {
        // 规格 Scenario：平台 A 到中央入口的气边距离为 4，平台 B 为 11 → 接受，报告项给出每个平台到三类目标的距离。
        // 夹具是平地无障碍，距离 = 平台最近格到 L10 的曼哈顿距离（测试内独立复算，不调校验器的 BFS）：
        //   5 号台 x7–11,y1–5 → 最近格 L6 → |10−10| + |9−5| = 4；1 号台 x0–4,y0–4 → 最近格 E5 → 6 + 5 = 11。
        // 变异 M-A4：声明表里边疆档的距离处理改成 RejectOnImbalance → 本测试红（DISTANCE_IMBALANCE 成了拒绝项）。
        MapData map = FrontierFixtures.Map();
        int[] expected = [.. map.BirthZones.Select(z => z.Min(c => Math.Abs(c.X - FrontierFixtures.Entrance.X) + Math.Abs(c.Y - FrontierFixtures.Entrance.Y)))];
        Assert.Equal([11, 10, 10, 7, 4, 2], expected);

        MapValidationResult result = MapValidator.Validate(map);

        Assert.True(result.IsValid, result.ToString());
        Assert.Empty(result.Failures);
        Assert.Equal(3, result.Reports.Length);   // 三类目标各一条（restore-go-core-rules：五项改三项）
        Assert.All(result.Reports, r => Assert.Equal(MapFindingSeverity.Report, r.Severity));
        Assert.All(result.Reports, r => Assert.Equal("BIRTH_ZONE_DISTANCE_REPORT", r.Code));
        foreach (string target in new[] { "最近公共信物", "中央入口", "最近咽喉" })
        {
            MapValidationFailure line = Assert.Single(result.Reports, r => r.Message.Contains($"到{target}的", StringComparison.Ordinal));
            for (int z = 1; z <= 6; z++)
            {
                Assert.Contains($"出生区 {z} = ", line.Message, StringComparison.Ordinal);   // 逐平台列出
            }
        }

        MapValidationFailure entrance = Assert.Single(result.Reports, r => r.Message.Contains("到中央入口的", StringComparison.Ordinal));
        Assert.Contains("出生区 5 = 4", entrance.Message, StringComparison.Ordinal);
        Assert.Contains("出生区 1 = 11", entrance.Message, StringComparison.Ordinal);
        Assert.Contains("极差 9", entrance.Message, StringComparison.Ordinal);
        Assert.Contains("报告项", result.ToString(), StringComparison.Ordinal);   // map 子命令打印的就是这份文本

        // 反面：同一张图标成标准档，同样的极差是拒绝项，且没有报告项。
        MapValidationResult asStandard = MapValidator.Validate(map with { Profile = MapProfile.Standard });
        Assert.Contains(asStandard.Failures, f => f.Code == "DISTANCE_IMBALANCE" && f.Message.Contains("中央入口", StringComparison.Ordinal));
        Assert.Empty(asStandard.Reports);
    }

    [Fact]
    public void 均衡的边疆图同样给出距离报告()
    {
        // 规格：边疆档 MUST 照样算出三项距离作为报告项——不是"失衡了才报"。把六个平台都改成贴着入口的同距小区会破坏其他规则，
        // 这里改用容差：容差放到 99 时标准档口径下根本不失衡，报告项仍须三条。
        // 变异 M-A5：AlwaysReport 分支改成只在极差超容差时才报 → 本测试红。
        MapData map = FrontierFixtures.Map() with { DistanceTolerance = 99, ToleranceRelaxReason = "测试用：让极差不超容差" };

        MapValidationResult result = MapValidator.Validate(map);

        Assert.True(result.IsValid, result.ToString());
        Assert.Equal(3, result.Reports.Length);
    }

    [Fact]
    public void 边疆档不可达仍拒绝()
    {
        // 规格 Scenario：某平台沿气边到不了任何公共信物格 → 拒绝并指出该平台编号与目标"公共信物格"。
        // 构造：把 15 个信物都挪进平台内（出生区分区），只留 K10 一个公共信物，再四面立栅栏把它围死。
        // 栅栏断气边，任何平台都到不了它；入口与咽喉照常可达，所以拒绝项只应指向"最近公共信物"。
        // 变异 M-A6：不可达的拒绝挪进 RejectOnImbalance 分支（边疆档下不再报）→ 本测试红。
        MapData plain = FrontierFixtures.Map();
        Coord relic = new(9, 9);
        Assert.Equal("K10", relic.ToNotation());
        Assert.Null(plain.BirthZoneOf(relic));

        ImmutableDictionary<Coord, RelicCellSpec>.Builder cells = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        cells[relic] = new RelicCellSpec(RelicZone.Contested, BudgetTier.High);
        int placed = 0;
        foreach (ImmutableHashSet<Coord> zone in plain.BirthZones)
        {
            foreach (Coord c in zone.Order().Take(3))
            {
                if (placed == 15)
                {
                    break;
                }

                cells[c] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
                placed++;
            }
        }

        MapData map = plain with
        {
            RelicCells = cells.ToImmutable(),
            TerrainData = new TerrainData(
                ImmutableDictionary<Coord, int>.Empty,
                ImmutableDictionary<Coord, Surface>.Empty,
                [],
                [
                    new FenceEdge(relic, new Coord(8, 9)), new FenceEdge(relic, new Coord(10, 9)),
                    new FenceEdge(relic, new Coord(9, 8)), new FenceEdge(relic, new Coord(9, 10)),
                ]),
        };
        Assert.Equal(16, map.RelicCells.Count);
        Assert.Single(map.RelicCells.Where(kv => kv.Value.Zone == RelicZone.Contested));

        MapValidationResult result = MapValidator.Validate(map);

        Assert.False(result.IsValid);
        MapValidationFailure[] unreachable = [.. result.Failures.Where(f => f.Code == "LANDMARK_UNREACHABLE")];
        Assert.Equal(6, unreachable.Length);
        Assert.All(unreachable, f => Assert.Contains("公共信物", f.Message, StringComparison.Ordinal));
        Assert.All(unreachable, f => Assert.Equal(MapFindingSeverity.Reject, f.Severity));
        Assert.Contains(unreachable, f => f.Message.Contains("出生区 3 ", StringComparison.Ordinal));
        Assert.Throws<MapValidationException>(() => GameBoard.Load(map));
        // 不可达的那一项不出距离报告，其余两项照报。
        Assert.Equal(2, result.Reports.Length);
        Assert.DoesNotContain(result.Reports, r => r.Message.Contains("公共信物", StringComparison.Ordinal));
    }

    [Fact]
    public void 不对称的边疆图通过校验()
    {
        // tasks 1.4：对称检查不在 MapValidator 主流程里，边疆档 MUST NOT 被要求旋转对称。
        // 样本口径：夹具确实不对称（否则本测试恒真）。
        MapData map = FrontierFixtures.Map();
        Assert.False(MapSymmetry.IsC4Symmetric(map));
        Assert.NotEmpty(MapSymmetry.RotationDefects(map));

        Assert.True(MapValidator.Validate(map).IsValid);

        // 源码层面：校验器不引用对称检查。
        string source = File.ReadAllText(ValidatorPath());
        Assert.DoesNotContain("MapSymmetry.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsC4Symmetric", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 超宽地图被拒绝而不是抛异常()
    {
        // 裁决 7：宽度上限 25 列（列字母 A–Z 跳 I）。第 26 列构造不出坐标，校验器必须把它报成拒绝项，而不是在枚举格子时崩溃。
        // 变异 M-A18：Validate 里去掉超宽时的提前返回 → 枚举格子抛 ArgumentOutOfRangeException，本测试红。
        MapData wide = TestMaps.Synthetic(size: 25, maxPlayers: 4) with { Width = 26, Profile = MapProfile.Frontier };

        MapValidationResult result = MapValidator.Validate(wide);

        MapValidationFailure failure = Assert.Single(result.Failures, f => f.Code == "MAP_TOO_WIDE");
        Assert.Contains("26", failure.Message, StringComparison.Ordinal);
        Assert.Contains("25", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MapValidator.Validate(wide with { Width = 25 }).Failures, f => f.Code == "MAP_TOO_WIDE");
    }

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
        Assert.True(end - start < 2_000, "声明表区间异常地长：结尾锚点可能匹配到了别处。");

        MatchCollection literals = Regex.Matches(source, @"MapProfile\s*\.\s*\w+");
        Assert.Contains(literals, m => m.Value.EndsWith("Standard", StringComparison.Ordinal));
        Assert.Contains(literals, m => m.Value.EndsWith("Frontier", StringComparison.Ordinal));
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
        foreach (string field in new[] { "SupportedPlayers", "Budgets", "ZonesMustExceedPlayers", "Distance" })
        {
            MatchCollection fieldReads = Regex.Matches(source, @"\.\s*" + field + @"(?![\w])");
            Assert.True(
                fieldReads.Count == 1,
                $"声明表字段 {field} 只允许被一条规则读一次，实际 {fieldReads.Count} 次：" + string.Join("、", fieldReads.Select(m => $"第 {LineOf(source, m.Index)} 行")));
        }

        // 距离处理方式的枚举字面量：表内两档各一次，表外只有 ValidateDistanceBalance 里的那一次比较。
        MatchCollection handlings = Regex.Matches(source, @"DistanceHandling\s*\.\s*\w+");
        Assert.Equal(2, handlings.Count(m => m.Index > start && m.Index < end));
        Assert.Equal(1, handlings.Count(m => m.Index < start || m.Index > end));
    }

    private static int LineOf(string source, int index) => source.AsSpan(0, index).Count('\n') + 1;

    private static string ValidatorPath() =>
        Path.Combine(FrontierFixtures.RepoRoot(), "src", "Siege.Core", "Board", "MapValidator.cs");
}
