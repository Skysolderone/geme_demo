using System.Collections.Immutable;
using System.Numerics;
using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 数值档位（tiered-number-show 1.1，design.md D1）。
/// 算例即规格两个场景；样式表的期望值逐格抄自 design.md D1 的分档样式表（不是抄实现）。
/// formation-tiers 段 C：阈值按负责人裁决（计分规则 v2 的 20 局实测，2026-10-05）由 4 / 8 / 16 / 32 改为 8 / 16 / 32 / 64——
/// 一档 &lt; 8、二档 8–15、三档 16–31、四档 32–63、五档 ≥ 64；下面各边界算例按新阈值手算重写（每个下界取"下界 − 1"与"下界"两侧）。
/// </summary>
/// <remarks>
/// 变异验证（tiered-number-show 段 A）。每条变异由脚本做：二进制读入原文并另存备份 → 断言锚点恰命中 1 次 → 改写 → 跑 SettlementShow + TacticalLayers 两个命名空间（基线 215 通过 / 2 跳过）→ finally 里重新写回原文（mtime 随之刷新）并逐字节比对；全部还原后确认跑 0 红。
/// formation-tiers 段 C 复做（阈值已为 [8, 16, 32, 64]；脚本做法同下，只跑 SettlementShow / TacticalLayers / VisualStyleBaseline / BatchPreview 四个命名空间，基线 290 通过）：
/// M-FC5「阈值 16 改成 17」——<c>[8, 16, 32, 64]</c> 改为 <c>[8, 17, 32, 64]</c> → 红 2（边界取档 value=16、三处呈现同一数值同一档 value=16）。
/// 以下为 tiered-number-show 段 A 时（旧阈值）的记录：
/// M-T1「阈值 8 改成 9」——<c>NumberTier.Thresholds</c> 的 <c>[4, 8, 16, 32]</c> 改为 <c>[4, 9, 16, 32]</c> → 红 2（边界取档 value=8、三处呈现同一数值同一档 value=8）。
/// M-T2「不取绝对值」——<c>BigInteger.Abs(value)</c> 改为 <c>value</c> → 红 6（负数按绝对值 4 条、三处呈现同一数值同一档 value=−20、势力重算节拍与数值变化显示Tests.放大幅度按档）。
/// M-T3「样式表改一格」——四档 <c>RankScalePercent: 180</c> 改为 <c>170</c> → 红 2（分档样式表 tier=4、势力重算节拍与数值变化显示Tests.放大幅度按档）。
/// M-T4「只改测试不改实现」——本类 分档样式表 的二、三档 <c>InlineData</c> 字号 84 与 100 对调 → 红 2（tier=2、tier=3）：期望值不是照抄实现。
/// M-T5「引擎层自己取档」——<c>src/godot/scripts/Hud.cs</c> 的 <c>RefreshRank</c> 里加一行 <c>_ = Siege.Presentation.Style.NumberTier.Of(display.Value);</c> → 红 1（引擎层不取档）。
/// M-T6「三档也有亮环」——三档 <c>RingCount: 0</c> 改为 <c>1</c> → 红 2（分档样式表 tier=3、高档冲击环与镜头轻震Tests.低档没有）。
/// 段 B 检查补一条源码扫描 引擎层只按呈现层给的档位查样式表（加这条之前下面四条都是 0 红；脚本同上，只跑相关六个测试类）：
/// MC-2「亮环圈数写死」——<c>BoardView.DrawRings</c> 的 <c>i &lt; ring.Count</c> 改为 <c>i &lt; 1</c> → 红 1（本条）。
/// MC-6「势力栏放大写死 130%」——<c>Hud.RankFontPx</c> 的 <c>extraPercent</c> 改为常量 30（不查样式表）→ 红 1（本条）。
/// MC-7「揭示条目恒取一档样式」——<c>BoardView.DrawReveals</c> 的 <c>NumberTierStyle.For(reveal.StepTier)</c> 改为 <c>For(1)</c> → 红 1（本条）。
/// MC-8「常驻标注恒取一档样式」——<c>BoardView.DrawGroupPower</c> 的 <c>NumberTierStyle.For(item.Tier)</c> 改为 <c>For(1)</c> → 红 1（本条）。
/// </remarks>
public class 数值档位Tests
{
    [Theory]
    [InlineData("0", 1)]
    [InlineData("7", 1)]
    [InlineData("8", 2)]
    [InlineData("15", 2)]
    [InlineData("16", 3)]
    [InlineData("31", 3)]
    [InlineData("32", 4)]
    [InlineData("63", 4)]
    [InlineData("64", 5)]
    [InlineData("1000000", 5)]
    [InlineData("1000000000000000000000000000000", 5)]   // 10^30：任意精度整数，取档不经定宽整数与浮点
    public void 边界取档(string value, int tier)
    {
        Assert.Equal(tier, NumberTier.Of(BigInteger.Parse(value)));
    }

    [Theory]
    [InlineData("-20", 3)]
    [InlineData("-7", 1)]
    [InlineData("-8", 2)]
    [InlineData("-64", 5)]
    [InlineData("-1000000000000000000000000000000", 5)]
    public void 负数按绝对值(string value, int tier)
    {
        Assert.Equal(tier, NumberTier.Of(BigInteger.Parse(value)));
    }

    [Fact]
    public void 档位范围为一至五()
    {
        Assert.Equal((1, 5), (NumberTier.Lowest, NumberTier.Highest));
        Assert.Equal(NumberTier.Highest, NumberTierStyle.All.Length);
        Assert.Equal([1, 2, 3, 4, 5], NumberTierStyle.All.Select(s => s.Tier));
        Assert.All(Enumerable.Range(1, 5), tier => Assert.Equal(tier, NumberTierStyle.For(tier).Tier));
        Assert.Throws<ArgumentOutOfRangeException>(() => NumberTierStyle.For(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NumberTierStyle.For(6));
    }

    // design.md D1 分档样式表：揭示字号 / 描边 / 弹出幅度 ‰ / 结果停留 ms / 末步时长 ms / 亮环圈数 / 轻震 ms / 势力栏放大 % / 常驻标注字号 / 音高（半音）。
    [Theory]
    [InlineData(1, 72, 10, 1100, 1400, 220, 0, 0, 130, 88, 0)]
    [InlineData(2, 84, 12, 1200, 1400, 220, 0, 0, 145, 96, 2)]
    [InlineData(3, 100, 14, 1300, 1600, 220, 0, 0, 160, 106, 4)]
    [InlineData(4, 124, 18, 1450, 1900, 440, 1, 0, 180, 118, 7)]
    [InlineData(5, 156, 22, 1600, 2200, 660, 2, 250, 200, 132, 12)]
    public void 分档样式表(int tier, int font, int outline, int pop, int holdMs, int finalStepMs, int rings, int shakeMs, int rankScale, int labelFont, int semitones)
    {
        NumberTierStyle style = NumberTierStyle.For(tier);
        Assert.Equal(
            (font, outline, pop, holdMs, finalStepMs, rings, shakeMs, rankScale, labelFont, semitones),
            (style.RevealFontSize, style.RevealOutlineSize, style.PopPermille, style.ResultHoldMs, style.FinalStepMs, style.RingCount, style.ShakeMs,
                style.RankScalePercent, style.GroupLabelFontSize, style.PitchSemitones));
    }

    [Fact]
    public void 样式表各列逐档单调不减()
    {
        // 守门：改样式表任何一列都不得出现"高档反而更弱"。字号、弹出、势力栏放大、常驻标注字号、音高严格递增（逐档可见地加强）；其余列不减。
        (string Name, Func<NumberTierStyle, int> Column, bool Strict)[] columns =
        [
            ("揭示字号", s => s.RevealFontSize, true),
            ("描边", s => s.RevealOutlineSize, true),
            ("弹出幅度", s => s.PopPermille, true),
            ("结果停留", s => s.ResultHoldMs, false),
            ("末步时长", s => s.FinalStepMs, false),
            ("亮环圈数", s => s.RingCount, false),
            ("轻震", s => s.ShakeMs, false),
            ("势力栏放大", s => s.RankScalePercent, true),
            ("常驻标注字号", s => s.GroupLabelFontSize, true),
            ("常驻标注描边", s => s.GroupLabelOutlineSize, false),
            ("音高", s => s.PitchSemitones, true),
        ];
        foreach ((string name, Func<NumberTierStyle, int> column, bool strict) in columns)
        {
            int[] values = [.. NumberTierStyle.All.Select(column)];
            for (int i = 1; i < values.Length; i++)
            {
                Assert.True(strict ? values[i] > values[i - 1] : values[i] >= values[i - 1], $"{name}：{i + 1} 档 {values[i]} 对 {i} 档 {values[i - 1]}");
            }
        }

        // 颜色逐档不同（白 → 淡金 → 金 → 橙 → 红），都不透明。
        Assert.Equal(5, NumberTierStyle.All.Select(s => s.RevealColor).Distinct().Count());
        Assert.All(NumberTierStyle.All, s => Assert.Equal(255, s.RevealColor.A));
    }

    [Fact]
    public void 一档等于分档之前的呈现()
    {
        // design.md D1：一档的字号、停留、势力栏放大、常驻标注字号都等于现状——
        // 落子飘字近景字号 72 / 描边 10（CalloutLabelStyle.Near）、飘字寿命 1.4 秒、势力栏段首 1.3 倍、常驻标注字号 88 / 描边 20（follow-opponent D7）。
        NumberTierStyle first = NumberTierStyle.For(1);
        Assert.Equal((CalloutLabelStyle.Near.FontSize, CalloutLabelStyle.Near.OutlineSize), (first.RevealFontSize, first.RevealOutlineSize));
        Assert.Equal(Callout.LifetimeMs, first.ResultHoldMs);
        Assert.Equal((130, 88, 20), (first.RankScalePercent, first.GroupLabelFontSize, first.GroupLabelOutlineSize));

        // 一至三档的末步不加定格：与普通一步同长（design.md A1 只给四、五档加定格）。
        Assert.All(Enumerable.Range(1, 3), tier => Assert.Equal(PowerRevealBeat.StepMs, NumberTierStyle.For(tier).FinalStepMs));
        Assert.Equal([20, 21, 24, 26, 30], NumberTierStyle.All.Select(s => s.GroupLabelOutlineSize));
    }

    [Theory]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(15, 2)]
    [InlineData(16, 3)]
    [InlineData(31, 3)]
    [InlineData(32, 4)]
    [InlineData(63, 4)]
    [InlineData(64, 5)]
    [InlineData(-20, 3)]
    public void 三处呈现同一数值同一档(int value, int tier)
    {
        // 军势揭示、势力栏到账、常驻标注共用一张表：同一个数在三处读出同一档（期望档位写死，不调取档入口）。
        // 势力栏：领地增量、军势增量、总增量各自的档。
        var change = new PowerChange(P1, 100, 100 + (2 * value), 1, 1, TerritoryDelta: value, GroupDelta: value);
        Assert.Equal((tier, tier), (change.TerritoryTier, change.GroupTier));
        Assert.Equal((tier, tier, change.TotalTier), (change.TierOf(PowerStage.Territory), change.TierOf(PowerStage.Group), change.TierOf(PowerStage.Hold)));
        Assert.Equal(tier, new PowerChange(P1, 100, 100 + value, 1, 1).TotalTier);

        if (value < 0)
        {
            return;   // 军势没有负数：揭示与常驻标注只验非负值
        }

        // 军势揭示：末步档位。
        GroupPower group = Group(P1, value, 0, 0, value, "D4");
        Assert.Equal(tier, RevealEntry.From(group).FinalTier);

        // 常驻标注。
        var content = new PowerLayerContent([new GroupScoreView(P1, group.Stones, GroupPowerView.From(group), 0)], [], []);
        Assert.Equal(tier, Assert.Single(GroupPowerLabels.Of(content)).Tier);
    }

    [Fact]
    public void 引擎层不取档()
    {
        // design.md D1：引擎层不取档，只读呈现层给出的档位（按档位查样式表 NumberTierStyle.For 是合法的）。
        // src/godot 不在 siege.sln 里，IL 守门扫不到——用源码文本扫描补上，配样本下界与反面命中。
        Regex entry = new(@"NumberTier\s*\.\s*Of\s*\(");
        string scripts = Path.Combine(PresentationFixtures.RepoRoot(), "src", "godot", "scripts");
        (string Name, string Text)[] files = [.. Directory.GetFiles(scripts, "*.cs").Order().Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];
        Assert.True(files.Length >= 10, $"只扫到 {files.Length} 个脚本");
        Assert.True(files.Sum(f => f.Text.Length) >= 50_000, $"只扫到 {files.Sum(f => f.Text.Length)} 字符");
        Assert.Empty(files.Where(f => entry.IsMatch(f.Text)).Select(f => f.Name));

        // 反面命中：同一判据在呈现层确实扫得到取档调用（常驻标注、势力变化、军势揭示三处都经这一个入口）。
        string presentation = Path.Combine(PresentationFixtures.RepoRoot(), "src", "Siege.Presentation");
        string[] callers =
        [
            .. Directory.GetFiles(presentation, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Where(f => entry.IsMatch(File.ReadAllText(f)))
                .Select(f => Path.GetFileName(f))
                .Order(),
        ];
        Assert.Contains("GroupPowerLabels.cs", callers);
        Assert.Contains("SettlementBeats.cs", callers);
    }

    [Fact]
    public void 引擎层只按呈现层给的档位查样式表()
    {
        // design.md D1 / D4：引擎层只照着遮罩画——档位读呈现层给出的，再去查样式表；亮环的圈数与进度读遮罩。
        // 上一条只挡"调用取档入口"，挡不住"查样式表时写死档位 / 干脆不查表"。同样是源码文本扫描（去掉注释后只看代码）：
        // ① NumberTierStyle.For 的实参只能是呈现层对象上的档位成员（…Tier、TierOf(…)，或按档位分组后的 Key），不得是字面量、局部量或算式；
        // ② 样式表里归引擎层画的各列，在画它的那个脚本里确有读者（少一列 = 那一处没按档）；
        // ③ 亮环的圈数与进度取自遮罩条目，画棋盘的脚本不自己查"几圈 / 震多久 / 停多久"（那是时间线的事）。
        string[] names = ["BoardView.cs", "Hud.cs", "ShowSounds.cs", "GameRoot.cs", "GameRoot.Reveal.cs"];
        Dictionary<string, string> code = names.ToDictionary(n => n, PresentationFixtures.GodotScriptCode);
        Assert.True(code.Values.Sum(t => t.Length) >= 100_000, $"只扫到 {code.Values.Sum(t => t.Length)} 字符");

        Regex lookup = new(@"NumberTierStyle\s*\.\s*For\s*\(((?:[^()]|\([^()]*\))*)\)");
        string[] arguments = [.. code.Values.SelectMany(t => lookup.Matches(t)).Select(m => m.Groups[1].Value.Trim()).Distinct().Order(StringComparer.Ordinal)];
        Assert.True(arguments.Length >= 6, $"只扫到 {arguments.Length} 种实参：{string.Join(" | ", arguments)}");
        Regex givenByPresentation = new(@"^[A-Za-z_]\w*(?:\.\w+)*\.(?:\w*Tier|Key|TierOf\(\w+(?:\.\w+)*\))$");
        Assert.Empty(arguments.Where(a => !givenByPresentation.IsMatch(a)));
        Assert.DoesNotMatch(givenByPresentation, "1");
        Assert.DoesNotMatch(givenByPresentation, "tier");
        Assert.DoesNotMatch(givenByPresentation, "reveal.StepTier + 1");
        Assert.DoesNotMatch(givenByPresentation, "Math.Min(reveal.StepTier, 3)");

        (string Script, string Column)[] readers =
        [
            ("BoardView.cs", nameof(NumberTierStyle.RevealFontSize)),
            ("BoardView.cs", nameof(NumberTierStyle.RevealOutlineSize)),
            ("BoardView.cs", nameof(NumberTierStyle.PopPermille)),
            ("BoardView.cs", nameof(NumberTierStyle.RevealColor)),
            ("BoardView.cs", nameof(NumberTierStyle.GroupLabelFontSize)),
            ("BoardView.cs", nameof(NumberTierStyle.GroupLabelOutlineSize)),
            ("Hud.cs", nameof(NumberTierStyle.RankScalePercent)),
            ("ShowSounds.cs", nameof(NumberTierStyle.PitchSemitones)),
        ];
        Assert.Empty(readers.Where(r => !Regex.IsMatch(code[r.Script], $@"\.\s*{r.Column}\b")).Select(r => $"{r.Script} 不读 {r.Column}"));

        string rings = PresentationFixtures.MethodBody(code["BoardView.cs"], "private void DrawRings(ShowMask mask)");
        Assert.Matches(@"foreach\s*\(\s*ImpactRing\s+ring\s+in\s+mask\.Rings\s*\)", rings);
        Assert.Matches(@"<\s*ring\.Count\b", rings);
        Assert.Matches(@"\bring\.ProgressPermille\b", rings);
        Regex timelineColumns = new($@"\b(?:{nameof(NumberTierStyle.RingCount)}|{nameof(NumberTierStyle.ShakeMs)}|{nameof(NumberTierStyle.ResultHoldMs)}|{nameof(NumberTierStyle.FinalStepMs)})\b");
        Assert.DoesNotMatch(timelineColumns, code["BoardView.cs"]);
        Assert.DoesNotMatch(timelineColumns, code["Hud.cs"]);
        Assert.Matches(timelineColumns, "for (int i = 0; i < NumberTierStyle.For(tier).RingCount; i++)");
    }

    [Fact]
    public void 排序按军势不按档位()
    {
        // 档位只影响呈现：同一档里军势不同的两条棋串，军势揭示仍按军势（不是档位）从小到大排。
        ImmutableArray<PlacedPiece> placed = [new(Coord.Parse("C3"), PieceType.Basic, P1), new(Coord.Parse("G7"), PieceType.Basic, P1)];
        ImmutableArray<RevealEntry> entries = SettlementBeats.RevealEntries(placed, [Group(P1, 15, 0, 0, 15, "C3"), Group(P1, 9, 0, 0, 9, "G7")]);
        Assert.Equal(["G7:9:2", "C3:15:2"], entries.Select(e => $"{e.Coord.ToNotation()}:{e.Power}:{e.FinalTier}"));
    }
}
