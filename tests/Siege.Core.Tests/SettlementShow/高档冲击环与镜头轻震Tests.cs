using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 高档冲击环与镜头轻震（tiered-number-show 1.3，design.md D4）。
/// 亮环与轻震都由时间线的状态纯函数地给出：自条目末步开始起，亮环 400 ms（四档一圈、五档两圈），五档轻震 250 ms；一至三档没有；零时长下不出现。
/// 算例用规格的两条四步棋串：军势 18（四档，末步 660 ms 开始、440 ms）与军势 36（五档，末步 660 ms 开始、660 ms），其后接势力节拍让演出不提前结束。
/// </summary>
/// <remarks>
/// 变异验证（tiered-number-show 段 A）。每条变异由脚本做：二进制读入原文并另存备份 → 断言锚点恰命中 1 次 → 改写 → 跑 SettlementShow + TacticalLayers 两个命名空间（基线 215 通过 / 2 跳过）→ finally 里重新写回原文（mtime 随之刷新）并逐字节比对；全部还原后确认跑 0 红。
/// M-I1「亮环与轻震从条目第一步起算」——<c>ShowTimeline.AddReveals</c> 里 <c>sinceFinalMs</c> 改为自条目第一步开始计 → 红 7（四档一圈、五档两圈并轻震、低档没有、轻震结束、零时长下不出现、飘字的停留时长Tests.揭示条目逐步推进、军势揭示节拍Tests.压缩后遮罩与音效按压缩后的时刻）。
/// M-I3「亮环 400 ms 改 300 ms」——<c>ImpactRing.DurationMs</c> → 红 4（四档一圈、五档两圈并轻震、轻震结束、同一时刻至多一个轻震）。
/// M-I4「轻震不结束」——去掉 <c>sinceFinalMs &lt; style.ShakeMs</c> → 红 2（轻震结束、同一时刻至多一个轻震）。
/// M-I5「轻震重叠取先开始的」——<c>shake = …</c> 改为 <c>shake ??= …</c> → 红 1（同一时刻至多一个轻震）。
/// M-I6「遮罩判空不计新中间态」——<c>ShowMask.IsEmpty</c> 去掉揭示条目 / 亮环 / 轻震 / 已开始步四项 → 红 1（零时长下不出现 的"只有军势揭示一个节拍时遮罩不空"）。
/// M-I7「亮环圈数恒为 1」→ 红 3（五档两圈并轻震、轻震结束、同一时刻至多一个轻震）。
/// M-T6「三档也有亮环」（样式表三档圈数 0 改 1）→ 红 2（低档没有、数值档位Tests.分档样式表 tier=3）。
/// 零时长守卫的既有变异 M-SC2（<c>Mask()</c> 去掉 <c>IsFinished ||</c>）复跑：红 6，含本类 零时长下不出现。
/// 段 B（引擎层接线）补一条源码扫描 引擎层的轻震偏移是进度的确定函数，变异两条（脚本同上，只跑本类，基线 7 通过）：
/// M-B1「偏移里混入随机」——<c>src/godot/scripts/BoardView.cs</c> 的 <c>ShakeOffsetOf</c> 把横向分量乘上 <c>GD.Randf()</c> → 红 1（引擎层的轻震偏移是进度的确定函数）。
/// M-B2「偏移不取自遮罩的进度」——<c>BoardView.RefreshShow</c> 里 <c>SetShake(mask.ShakePermille)</c> 改为 <c>SetShake(_showFrames)</c> → 红 1（同上）。
/// </remarks>
public class 高档冲击环与镜头轻震Tests
{
    /// <summary>一条四步棋串的军势揭示节拍 + 势力节拍（900 ms）。</summary>
    private static ShowTimeline Timeline(int baseTotal, int bonus, long power, ShowDuration duration = ShowDuration.Normal) => new(
        [
            new PowerRevealBeat([RevealEntry.From(Group(P1, baseTotal, bonus, 1, power, "D4", "E4", "F4"))]),
            new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)]),
        ],
        duration);

    /// <summary>军势 18（⌊(10+2)×1.5⌋）：四档。</summary>
    private static ShowTimeline TierFour() => Timeline(10, 2, 18);

    /// <summary>军势 36（⌊(20+4)×1.5⌋）：五档。</summary>
    private static ShowTimeline TierFive(ShowDuration duration = ShowDuration.Normal) => Timeline(20, 4, 36, duration);

    /// <summary>末步的开始时刻：前三步各 220 ms。</summary>
    private const int FinalStartMs = 660;

    [Fact]
    public void 四档一圈()
    {
        // 末步开始之前（前三步）没有亮环。
        ShowTimeline timeline = TierFour();
        timeline.Advance(FinalStartMs - 1);
        Assert.Equal("环[] 震[]", ImpactText(timeline.Mask()));

        // 末步开始后 100 ms：该格（常驻标注的落点 E4）一圈亮环在显示，进度 100 / 400；画面不震。
        timeline.Advance(1 + 100);
        ShowMask mask = timeline.Mask();
        Assert.Equal("环[E4×1:250] 震[]", ImpactText(mask));
        Assert.Null(mask.ShakePermille);

        // 亮环自末步开始持续 400 ms：399 ms 仍在，400 ms 起不再显示（此时仍在揭示节拍里，末步共 440 ms）。
        timeline.Advance(299);
        Assert.Equal("环[E4×1:997] 震[]", ImpactText(timeline.Mask()));
        timeline.Advance(1);
        Assert.IsType<PowerRevealBeat>(timeline.Current);
        Assert.Equal("环[] 震[]", ImpactText(timeline.Mask()));
    }

    [Fact]
    public void 五档两圈并轻震()
    {
        ShowTimeline timeline = TierFive();
        timeline.Advance(FinalStartMs - 1);
        Assert.Equal("环[] 震[]", ImpactText(timeline.Mask()));

        // 末步开始的那一刻：两圈亮环、轻震都从进度 0 起。
        timeline.Advance(1);
        Assert.Equal("环[E4×2:0] 震[0]", ImpactText(timeline.Mask()));

        // 末步开始后 100 ms：两圈亮环（100 / 400），画面处于轻震中（100 / 250）。
        timeline.Advance(100);
        ShowMask mask = timeline.Mask();
        Assert.Equal("环[E4×2:250] 震[400]", ImpactText(mask));
        ImpactRing ring = Assert.Single(mask.Rings);
        Assert.Equal((Coord.Parse("E4"), 2), (ring.Coord, ring.Count));
        Assert.Equal(400, ImpactRing.DurationMs);
    }

    [Fact]
    public void 低档没有()
    {
        // 末步为三档（军势 10）：逐毫秒走完整场演出（揭示 880 ms + 势力 900 ms），自始至终没有亮环、画面不震。
        ShowTimeline timeline = Timeline(5, 2, 10);
        int shownFrames = 0;
        for (int ms = 0; !timeline.IsFinished; ms++)
        {
            ShowMask mask = timeline.Mask();
            Assert.True(mask.Rings.IsEmpty && mask.ShakePermille is null, $"第 {ms} ms：{ImpactText(mask)}");
            shownFrames += mask.Reveals.IsEmpty ? 0 : 1;
            timeline.Advance(1);
        }

        // 样本口径下界：条目确实一直在显示（否则上面的"没有"是对着空遮罩说的）。
        Assert.Equal(880 + 900, shownFrames);

        // 一、二档同样没有。
        foreach ((int baseTotal, int bonus, long power) in new[] { (1, 1, 3L), (3, 1, 6L) })
        {
            ShowTimeline low = Timeline(baseTotal, bonus, power);
            low.Advance(FinalStartMs + 100);
            Assert.Equal("环[] 震[]", ImpactText(low.Mask()));
            Assert.NotEmpty(low.Mask().Reveals);
        }
    }

    [Fact]
    public void 轻震结束()
    {
        // 五档末步开始后 249 ms 仍在轻震，250 ms 起画面不再轻震；亮环（400 ms）此时还在。
        ShowTimeline timeline = TierFive();
        timeline.Advance(FinalStartMs + 249);
        Assert.Equal("环[E4×2:622] 震[996]", ImpactText(timeline.Mask()));
        timeline.Advance(1);
        ShowMask mask = timeline.Mask();
        Assert.Null(mask.ShakePermille);
        Assert.Equal("环[E4×2:625] 震[]", ImpactText(mask));

        // 亮环 400 ms 后也结束；条目的结果仍在显示。
        timeline.Advance(150);
        mask = timeline.Mask();
        Assert.Equal("环[] 震[]", ImpactText(mask));
        Assert.NotEmpty(mask.Reveals);
    }

    [Fact]
    public void 零时长下不出现()
    {
        // 无人值守（零时长）：遮罩恒空，亮环、轻震、揭示条目都不出现，推进也不出现。
        ShowTimeline timeline = TierFive(ShowDuration.Zero);
        Assert.True(timeline.IsFinished);
        ShowMask mask = timeline.Mask();
        Assert.True(mask.IsEmpty);
        Assert.Equal("环[] 震[]", ImpactText(mask));
        Assert.Empty(mask.Reveals);
        Assert.Empty(mask.RevealStepTiers);
        timeline.Advance(FinalStartMs + 100);
        Assert.True(timeline.Mask().IsEmpty);

        // 正常时长下遮罩不空：新增的中间态计入"遮罩是否为空"（只有军势揭示一个节拍，遮罩里没有别的中间态可以代替它们）。
        var only = new ShowTimeline([new PowerRevealBeat([RevealEntry.From(Group(P1, 20, 4, 1, 36, "D4"))])], ShowDuration.Normal);
        only.Advance(FinalStartMs + 100);
        ShowMask shown = only.Mask();
        Assert.Equal((1, 1, true, 4), (shown.Reveals.Length, shown.Rings.Length, shown.ShakePermille is not null, shown.RevealStepTiers.Length));
        Assert.False(shown.IsEmpty);
    }

    [Fact]
    public void 同一时刻至多一个轻震()
    {
        // 八条一步的五档棋串：各步之和 8 × 660 = 5280 ms → 压缩到 1600 ms，条目每 200 ms 开始一条，短于轻震的 250 ms。
        // 第 210 ms：第 1 条的轻震进度 840‰ 尚未结束，第 2 条刚开始 10 ms——只给后开始的那一个（40‰）；两条的亮环各自都在。
        string[] cells = ["A1", "C1", "E1", "G1", "A3", "C3", "E3", "G3"];
        ImmutableArray<RevealEntry> entries = [.. cells.Select(c => RevealEntry.From(Group(P1, 40, 0, 0, 40, c)))];
        var beat = new PowerRevealBeat(entries);
        Assert.Equal((5280, 1600, 200), (beat.RawDurationMs, beat.DurationMs, beat.StepStartMs(1, 0)));

        var timeline = new ShowTimeline([beat, new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)])], ShowDuration.Normal);
        timeline.Advance(210);
        Assert.Equal("环[A1×2:525,C1×2:25] 震[40]", ImpactText(timeline.Mask()));

        // 最后一条的末步在 1400 ms 开始：跨进势力节拍后（1700 ms）亮环仍按绝对时刻走完，轻震已结束。
        timeline.Advance(1700 - 210);
        Assert.IsType<PowerBeat>(timeline.Current);
        Assert.Equal("环[G3×2:750] 震[]", ImpactText(timeline.Mask()));
        timeline.Advance(100);
        Assert.Equal("环[] 震[]", ImpactText(timeline.Mask()));
    }

    [Fact]
    public void 引擎层的轻震偏移是进度的确定函数()
    {
        // 规格：轻震的偏移 MUST 是进度的确定函数，不得使用随机。呈现层只给进度（上面各条），偏移在引擎层折算——
        // src/godot 不在 siege.sln 里，IL 守门扫不到，用源码文本扫描补上（配样本下界与反面命中）：
        // 画演出层、写相机节点的 BoardView.cs 里不得出现任何随机源或时钟读数；偏移函数的入参只有千分比，且两处刷新都把遮罩给的进度原样交给它。
        string path = Path.Combine(PresentationFixtures.RepoRoot(), "src", "godot", "scripts", "BoardView.cs");
        string view = File.ReadAllText(path);
        Assert.True(view.Length >= 50_000, $"只读到 {view.Length} 字符");

        Regex nondeterministic = new(@"(?i)rand\w*|stopwatch|datetime|getticks\w*");
        Assert.Empty(nondeterministic.Matches(view).Select(m => m.Value).Distinct());
        Assert.Matches(nondeterministic, "float jitter = GD.Randf();");
        Assert.Matches(nondeterministic, "var rng = new RandomNumberGenerator();");
        Assert.Matches(nondeterministic, "ulong now = Time.GetTicksMsec();");

        Assert.Single(Regex.Matches(view, @"static\s+Vector3\s+ShakeOffsetOf\s*\(\s*int\s+permille\s*\)"));
        Assert.Equal(
            ["SetShake(mask.ShakePermille)", "SetShake(mask?.ShakePermille)"],
            Regex.Matches(view, @"(?<![A-Za-z])SetShake\(([^)]*)\)").Select(m => m.Value).Where(v => !v.Contains("int?", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }
}
