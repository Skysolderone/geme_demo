using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 军势揭示节拍（tiered-number-show 1.2，design.md D2）。
/// 算例取自规格场景：基础 5、加值 2、倍增子 1、军势 10 → 四步"5"/"5+2"/"(5+2)×1.5"/"(5+2)×1.5 = 10"；三条四步棋串 2640 ms → 整拍 1600 ms 按比例压缩；
/// formation-tiers 的两个新场景：基础 5、加值 2、倍增子 1、阵型二阶、军势 23 → 五步（倍增在前、阵型在后）；3 枚普通子（阵型一阶、军势 4）→ 三步"3"/"3×1.5"/"3×1.5 = 4"。
/// 军势一律按算例原样给进棋串明细（节拍生成只转录、不重算）。
/// 档位按 formation-tiers 段 C 重定的阈值手算（一档 &lt; 8、二档 8–15、三档 16–31、四档 32–63、五档 ≥ 64；末步取军势的档、往前每步降一档、降到一档为止）：
/// 军势 10 → 二档，四步为一、一、一、二；军势 36 → 四档，四步为一、二、三、四，时长 220/220/220/440；军势 23 → 三档，五步为一、一、一、二、三；
/// 五档末步 660 ms 改用军势 66（基础 40、加值 4、倍增子 1：⌊44 × 1.5⌋）→ 二、三、四、五。
/// </summary>
/// <remarks>
/// 变异验证（tiered-number-show 段 A）。每条变异由脚本做：二进制读入原文并另存备份 → 断言锚点恰命中 1 次 → 改写 → 跑 SettlementShow + TacticalLayers 两个命名空间（基线 215 通过 / 2 跳过）→ finally 里重新写回原文（mtime 随之刷新）并逐字节比对；全部还原后确认跑 0 红。
/// M-R1「揭示排到势力之后」——<c>SettlementBeats.Generate</c> 里军势揭示节拍的 <c>Add</c> 挪到势力重算节拍之后 → 红 5（本类 排在信物揭示之后势力重算之前 / 军势揭示取自结算后快照、
/// 结算节拍序列的生成Tests.落子并提子的批次、落子与提子节拍的内容Tests.落子飘字只有类型名、演出期间对局不推进Tests.演出不影响对局结果）。
/// M-R2「不按军势排序」——<c>RevealEntries</c> 的 <c>.OrderBy(e => e.Power)</c> 改为 <c>.OrderBy(e => 0)</c> → 红 6（从小到大、本机玩家的结算完整揭示、超过上限按比例压缩、军势原样转录不重算、压缩后遮罩与音效按压缩后的时刻、数值档位Tests.排序按军势不按档位）。
/// M-R2b「同值取坐标序最大」——<c>.ThenBy(e => e.Coord)</c> 改为 <c>.ThenByDescending(e => e.Coord)</c> → 红 1（从小到大）。
/// M-R3「逐枚揭示」——<c>RevealEntries</c> 改为每枚落子各取一次所在棋串（不去重）→ 红 3（同一棋串只揭示一次、军势揭示取自结算后快照、结算节拍序列的生成Tests.落子并提子的批次）。
/// M-R4「前面的步不降档」——<c>StepsOf</c> 里 <c>finalTier - (last - i)</c> 改为 <c>finalTier</c> → 红 11（四步揭示、高档末步定格、降到一档为止、本机玩家的结算完整揭示、军势原样转录不重算、压缩后遮罩与音效按压缩后的时刻、
/// 结算节拍序列的生成Tests.落子并提子的批次、飘字的停留时长Tests.揭示条目逐步推进、演出音效提示Tests 的 揭示逐步发声且档位递增 / 序列确定性 / 零时长无提示）。
/// M-R4b「降档不止于一档」——去掉 <c>Math.Max(NumberTier.Lowest, …)</c> → 红 8（降到一档为止、四步揭示、压缩后遮罩与音效按压缩后的时刻 等）。
/// M-R5「高档末步不定格」——<c>StepsOf</c> 里末步时长恒取 <c>PowerRevealBeat.StepMs</c> → 红 7（高档末步定格、只有基础军势、本机玩家的结算完整揭示、军势原样转录不重算、
/// 飘字的停留时长Tests.高档停留更久、高档冲击环与镜头轻震Tests 的 四档一圈 / 同一时刻至多一个轻震）。
/// M-R6「压缩时开始时刻不按比例」——<c>PowerRevealBeat.Scale</c> 改为 <c>Math.Min(rawMs, DurationMs)</c> → 红 3（超过上限按比例压缩、压缩后遮罩与音效按压缩后的时刻、高档冲击环与镜头轻震Tests.同一时刻至多一个轻震）。
/// M-R7「落点不用常驻标注的落点」——<c>RevealEntry.From</c> 里 <c>GroupPowerLabels.AnchorOf(group.Stones)</c> 改为 <c>group.Stones[0]</c> → 红 5（同一棋串只揭示一次、落点与常驻标注同一格、高档冲击环与镜头轻震Tests 三条）。
/// M-R8「末步文案与短算式不同」——<c>StepsOf</c> 里 <c>$"{running} = {view.Power}"</c> 改为 <c>$"{running}={view.Power}"</c> → 红 9（末步累计文案等于短算式、四步揭示、高档末步定格、本机玩家的结算完整揭示、军势原样转录不重算、压缩后遮罩与音效按压缩后的时刻 等）。
/// M-R9「本机结算连揭示一起省掉」——<c>Generate</c> 里 <c>compressPlacement</c> 为真时不加军势揭示节拍 → 红 1（本机玩家的结算完整揭示）。
/// M-R10「有落子就揭示全部棋串」——<c>RevealEntries</c> 的筛选放宽为"只要本次有落子" → 红 2（没有落子则省略 的 A4 一段、结算节拍序列的生成Tests.落子并提子的批次）。
/// M-R11「整拍不设上限」——<c>DurationMs</c> 改为 <c>RawDurationMs</c> → 红 4（超过上限按比例压缩、本机玩家的结算完整揭示、压缩后遮罩与音效按压缩后的时刻、高档冲击环与镜头轻震Tests.同一时刻至多一个轻震）。
/// 检查阶段补了 军势原样转录不重算 / 压缩后遮罩与音效按压缩后的时刻 两条测试，之后把段 A 的 46 条变异全部复跑（两个命名空间，基线 217 通过 / 2 跳过）：全红，
/// 红数因这两条测试而增加的 12 条已在各自的记录里改成复跑后的数（本类 M-R2 / R4 / R4b / R5 / R6 / R8 / R11，另有 M-I1、M-H3、M-SD2、M-SD7、M-SC3）。
/// 检查阶段补的三条（脚本同上做法，另加 VisualStyleBaseline 命名空间，基线 240 通过 / 2 跳过；补测试之前这三条变异都是 0 红）：
/// M-K1「步内进度按未压缩时长」——<c>ShowTimeline.AddReveals</c> 里 <c>int stepMs = beat.StepEndMs(e, k) - stepStart</c> 改为 <c>step.DurationMs</c> → 红 1（压缩后遮罩与音效按压缩后的时刻）。
/// M-K2「音效按未压缩的开始时刻」——<c>PowerRevealBeat.StepTiersStartedBy</c> 里 <c>StepStartMs(e, k)</c> 改为 <c>RawStartMs(e, k)</c> → 红 1（压缩后遮罩与音效按压缩后的时刻）。
/// M-K4「军势按公式重算」——<c>RevealEntry.From</c> 开头把 <c>group.Power</c> 换成 <c>(基础 + 加值) × 3^n ÷ 2^n</c> → 红 1（军势原样转录不重算）。
/// 同批另两条（原有测试已能抓到）：M-K3「揭示节拍播完后仍给已开始步」——<c>ShowTimeline.Mask</c> 里 <c>if (i == _index)</c> 改为 <c>i &lt;= _index</c> → 红 2（演出音效提示Tests 的 序列确定性 / 揭示逐步发声且档位递增）；
/// M-K5「每帧重报已开始的步」——<c>SoundCues.RevealTierStarted</c> 的循环从 0 起 → 红 3（同上两条与本类 压缩后遮罩与音效按压缩后的时刻）。
/// 未做成变异的一处：<c>RevealEntries</c> 开头"没有落子即返回空"是捷径——去掉它，空的落子集合照样筛不出任何棋串，是等价变异。
/// formation-tiers 段 C 的变异（脚本同上：二进制读写、按文件行尾归一锚点并断言恰命中 1 次、finally 写回原文并逐字节比对、写回后刷新 mtime；
/// 只跑 SettlementShow / TacticalLayers / VisualStyleBaseline / BatchPreview 四个命名空间，基线 290 通过；全部还原后确认跑 0 红、工作树 diff 与变异前逐字节相同）：
/// M-FC1「阵型 0 阶也走带阵型的完整算式」——<c>GroupPowerView.From</c> 里 <c>power.FormationTier == 0</c> 改为 <c>&lt; 0</c> → 红 7（批次预演必须显示的信息Tests 两条、落子与提子节拍的内容Tests.落子飘字只有类型名、势力层领地与高地Tests 四条）。
/// M-FC2「阵型因子排到倍增因子之前」——<c>GroupPowerView.Factors</c> 里 <c>Add(FormationText)</c> 改为 <c>Insert(0, FormationText)</c> → 红 3（本类 阵型多一步 / 末步累计文案等于短算式、落子与提子节拍的内容Tests.落子飘字只有类型名）。
/// M-FC3「揭示漏掉阵型一步」——<c>StepsOf</c> 的因子循环只走第一个因子 → 红 4（本类 阵型多一步 / 末步累计文案等于短算式 / 降到一档为止、UI风格约束Tests.分档强调不靠粒子）。
/// M-FC4「热区按倍增子数取档」——<c>LayerContents.Power</c> 里 <c>g.MultiplierExponent</c> 改回 <c>g.MultiplierCount</c> → 红 1（势力层领地与高地Tests.热区按倍率指数取档）。
/// M-FC5「阈值 16 改 17」→ 红 2（见 数值档位Tests）。
/// M-FC6「无倍增子时完整算式仍写 × 1」——<c>power.MultiplierCount == 0 ? "× 阵型 …"</c> 改为 <c>&lt; 0</c> → 红 2（落子与提子节拍的内容Tests.落子飘字只有类型名、势力层领地与高地Tests.热区按倍率指数取档）。
/// M-FC7「阵型倍率文字取总倍率」——<c>new Multiplier(power.FormationTier).ToString()</c> 改为 <c>power.Multiplier.ToString()</c> → 红 5（本类两条、批次预演 / 落子飘字 / 热区各一条）。
/// M-FC8「每个倍率因子都把前面括起来」——<c>StepsOf</c> 里 <c>f == 0 &amp;&amp; view.PositionBonus != 0</c> 改为 <c>view.PositionBonus != 0</c> → 红 2（本类 阵型多一步 / 末步累计文案等于短算式）。
/// M-FC9「热区由表现层按棋子数自己定阶」——热区改为 <c>g.MultiplierCount + FormationTiers.TierOf(g.Stones.Length)</c>（v1 / v2 下数值都不变）→ 红 1（UI层不含规则计算Tests.表现层不调用规则计算入口）。
/// M-FC10「Godot 示例自己按棋子数定阶」——<c>GameRoot.Reveal.cs</c> 的 <c>RevealSampleEntries</c> 里加一行 <c>_ = Siege.Core.Scoring.FormationTiers.TierOf(3);</c> → 红 1（Godot层不含规则计算Tests.Godot层不调用规则计算入口）。
/// </remarks>
public class 军势揭示节拍Tests
{
    /// <summary>规格算例：基础 5、位置加值 2、倍增子 1 枚、军势 10。</summary>
    private static GroupPower FourStep(params string[] cells) => Group(P1, 5, 2, 1, 10, cells);

    /// <summary>一次落子（P1 在给定格各落一枚普通子，结算前空盘）的前后两侧；结算后一侧带给定的棋串明细。</summary>
    private static (SettlementSide Before, SettlementSide After) Sides(string[] placed, params GroupPower[] groups)
    {
        GameBoard after = Stones([.. placed.Select(c => (c, P1))]);
        return (Side(Stones(), Reading(P1, 0, 1)), Side(after, Reading(P1, 0, 5, 1)) with { Groups = [.. groups] });
    }

    private static PowerRevealBeat Reveal(string[] placed, params GroupPower[] groups)
    {
        (SettlementSide before, SettlementSide after) = Sides(placed, groups);
        return Assert.Single(SettlementBeats.Generate(before, after, null).OfType<PowerRevealBeat>());
    }

    [Fact]
    public void 四步揭示()
    {
        PowerRevealBeat beat = Reveal(["C3"], FourStep("C3"));
        RevealEntry entry = Assert.Single(beat.Entries);
        Assert.Equal(["5", "5+2", "(5+2)×1.5", "(5+2)×1.5 = 10"], entry.Steps.Select(s => s.RunningText));
        Assert.Equal(["5", "+2", "×1.5", "= 10"], entry.Steps.Select(s => s.Text));
        Assert.Equal([1, 1, 1, 2], entry.Steps.Select(s => s.Tier));   // 军势 10 在二档（8–15）
        Assert.Equal([220, 220, 220, 220], entry.Steps.Select(s => s.DurationMs));
        Assert.Equal((2, new BigInteger(10), P1, "C3"), (entry.FinalTier, entry.Power, entry.Owner, entry.Coord.ToNotation()));

        // 整拍 880 ms，未超上限：各步的开始时刻就是未压缩的时刻。
        Assert.Equal((880, 880), (beat.RawDurationMs, beat.DurationMs));
        Assert.Equal([0, 220, 440, 660], Enumerable.Range(0, 4).Select(k => beat.StepStartMs(0, k)));
        Assert.Equal([220, 440, 660, 880], Enumerable.Range(0, 4).Select(k => beat.StepEndMs(0, k)));

        // 只有加值 / 只有倍率：三步，中间一步分别是"+加值"与"×倍率"（只列非零项）。
        Assert.Equal(["5@1/220", "5+2@1/220", "5+2 = 7@1/220"], RevealEntry.From(Group(P1, 5, 2, 0, 7, "C3")).Steps.Select(Step));   // 7 在一档
        Assert.Equal(["5@1/220", "5×1.5@1/220", "5×1.5 = 7@1/220"], RevealEntry.From(Group(P1, 5, 0, 1, 7, "C3")).Steps.Select(Step));

        static string Step(RevealStep s) => $"{s.RunningText}@{s.Tier}/{s.DurationMs}";
    }

    [Fact]
    public void 只有基础军势()
    {
        // 基础 3、无位置加值、无倍增子：只有一步，这一步就是结果（design.md A3），累计文案"3"，一档。
        PowerRevealBeat beat = Reveal(["C3"], Group(P1, 3, 0, 0, 3, "C3"));
        RevealStep step = Assert.Single(Assert.Single(beat.Entries).Steps);
        Assert.Equal(("3", "3", 1, 220), (step.Text, step.RunningText, step.Tier, step.DurationMs));
        Assert.Equal(220, beat.DurationMs);

        // 一步条目按军势的档位取末步时长：基础 20（三档）→ 220 ms；基础 40（四档）→ 440 ms；基础 70（五档）→ 660 ms。
        Assert.Equal("D4:P1:20:20@3/220", Text(RevealEntry.From(Group(P1, 20, 0, 0, 20, "D4"))));
        Assert.Equal("D4:P1:40:40@4/440", Text(RevealEntry.From(Group(P1, 40, 0, 0, 40, "D4"))));
        Assert.Equal("D4:P1:70:70@5/660", Text(RevealEntry.From(Group(P1, 70, 0, 0, 70, "D4"))));
    }

    [Fact]
    public void 同一棋串只揭示一次()
    {
        // 一次结算在同一条棋串里落了 3 枚（C3、C4、C5）：只有 1 个条目，标在该棋串常驻标注的同一格（重心最近的 C4，不是第一枚）。
        PowerRevealBeat beat = Reveal(["C3", "C4", "C5"], FourStep("C3", "C4", "C5"));
        RevealEntry entry = Assert.Single(beat.Entries);
        Assert.Equal("C4", entry.Coord.ToNotation());
        Assert.Equal(880, beat.DurationMs);

        // 落在已有棋串上（结算前 C3、C4 已有子，本次只落 C5）：同样揭示这一条。
        GameBoard before = Stones(("C3", P1), ("C4", P1));
        GameBoard after = Stones(("C3", P1), ("C4", P1), ("C5", P1));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(
            Side(before, Reading(P1, 0, 2, 1)), Side(after, Reading(P1, 0, 10, 1)) with { Groups = [FourStep("C3", "C4", "C5")] }, null);
        Assert.Equal("C4", Assert.Single(Assert.Single(beats.OfType<PowerRevealBeat>()).Entries).Coord.ToNotation());
    }

    [Fact]
    public void 从小到大()
    {
        // 本次落子分属两条棋串，军势 12 与 3：3 在前、12 在后（明细里 12 排在前面，输出不跟输入顺序）。
        PowerRevealBeat beat = Reveal(["C3", "G7"], Group(P1, 12, 0, 0, 12, "C3"), Group(P1, 3, 0, 0, 3, "G7"));
        Assert.Equal(["G7:P1:3:3@1/220", "C3:P1:12:12@2/220"], beat.Entries.Select(Text));

        // 条目逐个依次播放：前一条目的末步结束后下一条目才开始。
        Assert.Equal((0, 220, 220, 440), (beat.StepStartMs(0, 0), beat.StepEndMs(0, 0), beat.StepStartMs(1, 0), beat.StepEndMs(1, 0)));

        // 军势相同按落点坐标序（行优先：C3 在 G7 之前；明细里 G7 排在前面）。
        PowerRevealBeat tie = Reveal(["C3", "G7"], Group(P1, 5, 0, 0, 5, "G7"), Group(P1, 5, 0, 0, 5, "C3"));
        Assert.Equal(["C3", "G7"], tie.Entries.Select(e => e.Coord.ToNotation()));
    }

    [Fact]
    public void 高档末步定格()
    {
        // 基础 20、位置加值 4、倍增子 1 枚、军势 36（四档 32–63）：档位一、二、三、四，时长 220、220、220、440。
        PowerRevealBeat beat = Reveal(["C3"], Group(P1, 20, 4, 1, 36, "C3"));
        RevealEntry entry = Assert.Single(beat.Entries);
        Assert.Equal(["20", "20+4", "(20+4)×1.5", "(20+4)×1.5 = 36"], entry.Steps.Select(s => s.RunningText));
        Assert.Equal([1, 2, 3, 4], entry.Steps.Select(s => s.Tier));
        Assert.Equal([220, 220, 220, 440], entry.Steps.Select(s => s.DurationMs));
        Assert.Equal(1100, beat.DurationMs);

        // 五档：末步 660 ms。基础 40、加值 4、倍增子 1、军势 66（⌊44 × 1.5⌋ ≥ 64）：档位二、三、四、五。
        RevealEntry five = RevealEntry.From(Group(P1, 40, 4, 1, 66, "C3"));
        Assert.Equal([2, 3, 4, 5], five.Steps.Select(s => s.Tier));
        Assert.Equal([220, 220, 220, 660], five.Steps.Select(s => s.DurationMs));
    }

    [Fact]
    public void 降到一档为止()
    {
        // design.md A2：共 4 步而末步只有二档时，前三步并列在一档（往前每步降一档，降到一档为止，不保证严格递增）。
        // 基础 5、加值 2、倍增子 1、军势 10（⌊7 × 1.5⌋，二档）。
        Assert.Equal([1, 1, 1, 2], RevealEntry.From(Group(P1, 5, 2, 1, 10, "C3")).Steps.Select(s => s.Tier));
        Assert.Equal([1, 1, 1, 1], RevealEntry.From(Group(P1, 1, 1, 1, 3, "C3")).Steps.Select(s => s.Tier));

        // 三步、末步五档：三、四、五（基础 60、加值 10、军势 70）。
        Assert.Equal([3, 4, 5], RevealEntry.From(Group(P1, 60, 10, 0, 70, "C3")).Steps.Select(s => s.Tier));

        // 五步（formation-tiers：带阵型）、末步五档：一、二、三、四、五（基础 10、加值 4、倍增子 1、阵型三阶、军势 ⌊14 × 81 / 16⌋ = 70）。
        Assert.Equal([1, 2, 3, 4, 5], RevealEntry.From(Formed(P1, 10, 4, 1, 3, 70, "C3")).Steps.Select(s => s.Tier));
    }

    [Fact]
    public void 超过上限按比例压缩()
    {
        // 三条棋串各四步、军势都在三档以下（10 / 9 / 12，阈值 8 / 16 / 32 / 64 下都是二档，末步 220 ms）：各步时长之和 12 × 220 = 2640 ms → 整拍 1600 ms，
        // 第 k 步（0 起，跨条目连续数）的开始时刻 = 220 × k × 1600 ÷ 2640（向下取整）。
        PowerRevealBeat beat = Reveal(["C3", "F6", "H9"], Group(P1, 5, 2, 1, 10, "C3"), Group(P1, 4, 2, 1, 9, "F6"), Group(P1, 6, 2, 1, 12, "H9"));
        Assert.Equal(["F6", "C3", "H9"], beat.Entries.Select(e => e.Coord.ToNotation()));
        Assert.Equal((2640, 1600), (beat.RawDurationMs, beat.DurationMs));

        int[] starts = [.. Enumerable.Range(0, 12).Select(k => beat.StepStartMs(k / 4, k % 4))];
        Assert.Equal([0, 133, 266, 400, 533, 666, 800, 933, 1066, 1200, 1333, 1466], starts);
        Assert.Equal(Enumerable.Range(0, 12).Select(k => 220 * k * 1600 / 2640), starts);

        // 每步的结束时刻就是下一步的开始时刻，最后一步结束于整拍末尾。
        int[] ends = [.. Enumerable.Range(0, 12).Select(k => beat.StepEndMs(k / 4, k % 4))];
        Assert.Equal([.. starts.Skip(1), 1600], ends);

        // 恰好不超上限的不压缩：7 步（四步 + 三步）= 1540 ms。
        PowerRevealBeat under = Reveal(["C3", "F6"], Group(P1, 5, 2, 1, 10, "C3"), Group(P1, 5, 2, 0, 7, "F6"));
        Assert.Equal((1540, 1540), (under.RawDurationMs, under.DurationMs));
        Assert.Equal([0, 220, 440, 660, 880, 1100, 1320], [.. Enumerable.Range(0, 3).Select(k => under.StepStartMs(0, k)), .. Enumerable.Range(0, 4).Select(k => under.StepStartMs(1, k))]);
    }

    [Fact]
    public void 没有落子则省略()
    {
        // 玩家 Pass 且势力有变：盘面不变（没有本次落子），即使结算后快照带棋串明细，也不含军势揭示节拍。
        GameBoard board = Stones(("C3", P1), ("E5", P2));
        SettlementSide before = Side(board, Reading(P1, 5, 2), Reading(P2, 6, 1)) with { Groups = [Group(P1, 5, 0, 0, 5, "C3"), Group(P2, 6, 0, 0, 6, "E5")] };
        SettlementSide after = Side(board, Reading(P1, 5, 2), Reading(P2, 8, 1)) with { Groups = [Group(P1, 5, 0, 0, 5, "C3"), Group(P2, 8, 0, 0, 8, "E5")] };
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, null);
        Assert.Equal("势力[P2:6->8(+2):1->1]", Text(beats));

        // 落了子但结算后一侧没有棋串明细：条目为空，节拍同样省略。
        ImmutableArray<SettlementBeat> bare = SettlementBeats.Generate(Side(Stones(), Reading(P1, 0, 1)), Side(Stones(("C3", P1)), Reading(P1, 0, 1, 1)), null);
        Assert.DoesNotContain(bare, b => b is PowerRevealBeat);
        Assert.Contains(bare, b => b is PlacementBeat);

        // 只揭示含本次落子的棋串：P1 落 C3，对手 P2 的棋串 E5 军势也变了（design.md A4）→ 只有 C3 一条。
        ImmutableArray<SettlementBeat> mixed = SettlementBeats.Generate(
            Side(Stones(("E5", P2)), Reading(P2, 6, 1)),
            Side(Stones(("C3", P1), ("E5", P2)), Reading(P1, 0, 5, 1), Reading(P2, 4, 2)) with { Groups = [Group(P2, 4, 0, 0, 4, "E5"), Group(P1, 5, 0, 0, 5, "C3")] },
            null);
        Assert.Equal(["C3:P1:5:5@1/220"], Assert.Single(mixed.OfType<PowerRevealBeat>()).Entries.Select(Text));
    }

    [Fact]
    public void 本机玩家的结算完整揭示()
    {
        // 本机玩家确认一个批次：落子节拍压缩为一次确认闪动（0.25 秒），军势揭示节拍的各步时长与对手的结算相同。
        (SettlementSide before, SettlementSide after) = Sides(["C3", "G7"], Group(P1, 20, 4, 1, 36, "C3"), FourStep("G7"));
        ImmutableArray<SettlementBeat> own = SettlementBeats.Generate(before, after, Order("C3", "G7"), compressPlacement: true);
        ImmutableArray<SettlementBeat> opponent = SettlementBeats.Generate(before, after, Order("C3", "G7"));

        Assert.True(Assert.IsType<PlacementBeat>(own[0]).Compressed);
        Assert.Equal((250, 500), (own[0].DurationMs, opponent[0].DurationMs));

        PowerRevealBeat mine = Assert.Single(own.OfType<PowerRevealBeat>());
        PowerRevealBeat theirs = Assert.Single(opponent.OfType<PowerRevealBeat>());
        Assert.Equal("揭示[G7:P1:10:5@1/220|5+2@1/220|(5+2)×1.5@1/220|(5+2)×1.5 = 10@2/220,C3:P1:36:20@1/220|20+4@2/220|(20+4)×1.5@3/220|(20+4)×1.5 = 36@4/440]", Text(mine));
        Assert.Equal(Text(theirs), Text(mine));
        Assert.Equal((1600, 1980), (mine.DurationMs, mine.RawDurationMs));   // 880 + (220 × 3 + 440)
        Assert.Equal(theirs.DurationMs, mine.DurationMs);
    }

    [Fact]
    public void 排在信物揭示之后势力重算之前()
    {
        // 节拍顺序：落子 → 提子 → 信物揭示 → 军势揭示 → 势力重算 → 横幅（六种齐全）。
        SettlementSide before = Side(Stones(("D3", P2), ("E5", P2)), Reading(P1, 5, 2), Reading(P2, 6, 1)) with
        {
            Relics = [Relic("F6", Siege.Core.Relics.RelicType.Command, revealed: false)],
            Statuses = [Status(P1, PlayerStatus.Active), Status(P2, PlayerStatus.Active)],
        };
        SettlementSide after = Side(Stones(("C3", P1), ("C4", P1), ("E5", P2)), Reading(P1, 9, 1), Reading(P2, 0, null)) with
        {
            Groups = [FourStep("C3", "C4")],
            Relics = [Relic("F6", Siege.Core.Relics.RelicType.Command, revealed: true)],
            Statuses = [Status(P1, PlayerStatus.Active), Status(P2, PlayerStatus.Eliminated)],
        };

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, Order("C3", "C4"));
        Assert.Equal(
            [typeof(PlacementBeat), typeof(CaptureBeat), typeof(RelicRevealBeat), typeof(PowerRevealBeat), typeof(PowerBeat), typeof(BannerBeat)],
            beats.Select(b => b.GetType()));
    }

    [Fact]
    public void 落点与常驻标注同一格()
    {
        // design.md D2：条目落点取常驻标注的落点（离重心最近的棋子，并列取坐标序最小），同一条棋串两处标在同一格。
        // 用 tactical-layers 规格的两个落点算例：D4、E4、F4 → E4；D4、E4 → D4；棋子顺序打乱（第一枚不是落点）。
        (string[] Cells, string Anchor)[] cases = [(["F4", "D4", "E4"], "E4"), (["E4", "D4"], "D4"), (["E5", "D4", "D5"], "D5")];
        foreach ((string[] cells, string anchor) in cases)
        {
            GroupPower group = FourStep(cells);
            Assert.Equal(anchor, RevealEntry.From(group).Coord.ToNotation());

            var content = new PowerLayerContent([new GroupScoreView(P1, group.Stones, GroupPowerView.From(group), 0)], [], []);
            Assert.Equal(anchor, Assert.Single(GroupPowerLabels.Of(content)).Coord.ToNotation());
        }
    }

    [Fact]
    public void 阵型多一步()
    {
        // formation-tiers 规格算例：基础 5、位置加值 2、倍增子 1 枚、阵型二阶、军势 23（⌊7 × 1.5 × 2.25⌋ = ⌊23.625⌋）。
        // 五步，倍增在前、阵型在后；档位按阈值 8 / 16 / 32 / 64：23 在三档，往前每步降一档、降到一档为止 → 一、一、一、二、三；三档末步不定格，五步都是 220 ms。
        PowerRevealBeat beat = Reveal(["C3"], Formed(P1, 5, 2, 1, 2, 23, "C3"));
        RevealEntry entry = Assert.Single(beat.Entries);
        Assert.Equal(["5", "5+2", "(5+2)×1.5", "(5+2)×1.5×2.25", "(5+2)×1.5×2.25 = 23"], entry.Steps.Select(s => s.RunningText));
        Assert.Equal(["5", "+2", "×1.5", "×2.25", "= 23"], entry.Steps.Select(s => s.Text));
        Assert.Equal([1, 1, 1, 2, 3], entry.Steps.Select(s => s.Tier));
        Assert.Equal([220, 220, 220, 220, 220], entry.Steps.Select(s => s.DurationMs));
        Assert.Equal((1100, 1100), (beat.RawDurationMs, beat.DurationMs));

        // 末步累计文案即短算式；阵型阶数取自明细（这条明细只有一枚棋子，阵型照样是二阶——呈现层不数棋子）。
        Assert.Equal("(5+2)×1.5×2.25 = 23", GroupPowerView.From(Formed(P1, 5, 2, 1, 2, 23, "C3")).ShortFormulaText);

        // 阵型阶数为 0 时与引入阵型之前逐字相同（同一条棋串去掉阵型、军势 10）：四步。
        Assert.Equal(["5", "5+2", "(5+2)×1.5", "(5+2)×1.5 = 10"], RevealEntry.From(Formed(P1, 5, 2, 1, 0, 10, "C3")).Steps.Select(s => s.RunningText));
    }

    [Fact]
    public void 只有阵型()
    {
        // formation-tiers 规格算例：3 枚普通子（基础 3、无加值、无倍增子、阵型一阶、军势 ⌊3 × 1.5⌋ = 4）→ 三步，军势 4 在一档。
        RevealEntry entry = Assert.Single(Reveal(["C3", "C4", "C5"], Formed(P1, 3, 0, 0, 1, 4, "C3", "C4", "C5")).Entries);
        Assert.Equal(["3", "3×1.5", "3×1.5 = 4"], entry.Steps.Select(s => s.RunningText));
        Assert.Equal(["3", "×1.5", "= 4"], entry.Steps.Select(s => s.Text));
        Assert.Equal([1, 1, 1], entry.Steps.Select(s => s.Tier));

        // 有加值、只有阵型：先把和括起来再乘阵型倍率（基础 3、加值 2、阵型一阶 → ⌊5 × 1.5⌋ = 7）。
        Assert.Equal(["3", "3+2", "(3+2)×1.5", "(3+2)×1.5 = 7"], RevealEntry.From(Formed(P1, 3, 2, 0, 1, 7, "C3")).Steps.Select(s => s.RunningText));
    }

    /// <summary>1.5^k 的精确十进制（k = 0..5），手写：3/2、9/4、27/8、81/16、243/32。</summary>
    private static readonly string[] Decimal15 = ["1", "1.5", "2.25", "3.375", "5.0625", "7.59375"];

    [Fact]
    public void 末步累计文案等于短算式()
    {
        // 守门（design.md D2 第 3 步）：对任意棋串明细，末步累计文案与势力层的军势短算式逐字相同；
        // 步数 = 1 + 加值非零 + 有倍增子 + 阵型一阶以上 + （有加值、倍增或阵型时的"= 军势"），每步的累计文案以上一步的累计文案开头（或把它括起来）。
        // 军势在测试里用独立的整数算式算出：⌊(基础 + 加值) × 3^n ÷ 2^n⌋，n = 倍增子 + 阵型阶数（formation-tiers：阵型 0–4 阶全覆盖）。
        int checkedGroups = 0;
        var shapes = new HashSet<int>();
        for (int baseTotal = 1; baseTotal <= 24; baseTotal++)
        {
            for (int bonus = 0; bonus <= 6; bonus++)
            {
                for (int multipliers = 0; multipliers <= 5; multipliers++)
                {
                    for (int formation = 0; formation <= 4; formation++)
                    {
                        int n = multipliers + formation;
                        BigInteger power = (baseTotal + bonus) * BigInteger.Pow(3, n) / BigInteger.Pow(2, n);
                        GroupPower group = Formed(P1, baseTotal, bonus, multipliers, formation, power, "D4");
                        RevealEntry entry = RevealEntry.From(group);
                        string shortFormula = GroupPowerView.From(group).ShortFormulaText;

                        Assert.Equal(shortFormula, entry.Steps[^1].RunningText);
                        int expectedSteps = 1 + (bonus != 0 ? 1 : 0) + (multipliers != 0 ? 1 : 0) + (formation != 0 ? 1 : 0)
                            + (bonus != 0 || multipliers != 0 || formation != 0 ? 1 : 0);
                        Assert.Equal(expectedSteps, entry.Steps.Length);
                        Assert.Equal($"{baseTotal}", entry.Steps[0].RunningText);
                        for (int i = 1; i < entry.Steps.Length; i++)
                        {
                            // 每一步在上一步的累计文案后面接上本步新出现的文案（倍率一步可能先把前面括起来）。
                            string previous = entry.Steps[i - 1].RunningText;
                            Assert.True(
                                entry.Steps[i].RunningText == previous + entry.Steps[i].Text
                                || entry.Steps[i].RunningText == $"({previous}){entry.Steps[i].Text}"
                                || entry.Steps[i].RunningText == $"{previous} {entry.Steps[i].Text}",
                                $"{shortFormula} 第 {i} 步：{entry.Steps[i].RunningText}");
                        }

                        // 倍率因子一步一个、倍增在前阵型在后，文字是 1.5^k 的精确十进制（期望写死，不调 Multiplier.ToString）。
                        string[] factorSteps = [.. entry.Steps.Select(s => s.Text).Where(t => t.StartsWith('×'))];
                        string[] expectedFactors = [.. new[] { multipliers, formation }.Where(k => k != 0).Select(k => "×" + Decimal15[k])];
                        Assert.Equal(expectedFactors, factorSteps);

                        Assert.Equal(power, entry.Power);
                        shapes.Add(expectedSteps);
                        checkedGroups++;
                    }
                }
            }
        }

        // 样本口径下界：一步、三步、四步、五步四种形态都覆盖到了（两步的形态不存在）。
        Assert.Equal(24 * 7 * 6 * 5, checkedGroups);
        Assert.Equal([1, 3, 4, 5], shapes.Order());
    }

    [Fact]
    public void 军势揭示取自结算后快照()
    {
        // 真实对局（公开快照 → 节拍）：P0 落 F6、E6 连成一条棋串 → 军势揭示节拍恰一个条目，
        // 军势、算式与落点都等于结算后公开快照势力明细里那条棋串的（不重算）；生成前后对局指纹不变，两次生成逐项相同。
        MatchFlow match = MatchFixtures.Started().AtRound(5, MatchFixtures.All);
        MatchPublicView before = match.Publish();
        SettlementOutcome outcome = match.PlayTurn("F6", "E6");
        MatchPublicView after = match.Publish();
        string fingerprint = PresentationFixtures.Fingerprint(match);

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, outcome.CaptureRecord);
        PowerRevealBeat reveal = Assert.Single(beats.OfType<PowerRevealBeat>());
        RevealEntry entry = Assert.Single(reveal.Entries);

        GroupPower group = Assert.Single(after.Power!.Players.SelectMany(p => p.Groups), g => g.Stones.Contains(Coord.Parse("F6")));
        Assert.Contains(Coord.Parse("E6"), group.Stones);
        Assert.Equal((group.Owner, group.Power), (entry.Owner, entry.Power));
        Assert.Equal(before.CurrentPlayer, entry.Owner);
        Assert.Equal(GroupPowerView.From(group).ShortFormulaText, entry.Steps[^1].RunningText);
        Assert.Contains(entry.Coord, group.Stones);

        // 位置：落子之后、势力重算之前。
        Assert.IsType<PlacementBeat>(beats[0]);
        Assert.True(beats.IndexOf(reveal) < beats.ToList().FindIndex(b => b is PowerBeat));

        Assert.Equal(Text(beats), Text(SettlementBeats.Generate(before, after, outcome.CaptureRecord)));
        Assert.Equal(fingerprint, PresentationFixtures.Fingerprint(match));
    }

    [Fact]
    public void 军势原样转录不重算()
    {
        // 规格：节拍生成不重算军势，数值一律取自结算后快照的势力明细。
        // 其余算例的军势都与"(基础 + 加值) × 1.5^n"自洽，重算一遍也得到同一个数——这里故意给一个与算式对不上的军势
        // （基础 5、加值 2、倍增子 1 枚，按公式是 10，快照里写 40）：条目的军势、末步文案、档位与末步时长都跟着快照的 40 走。
        PowerRevealBeat beat = Reveal(["C3", "G7"], Group(P1, 5, 2, 1, 40, "C3"), Group(P1, 20, 0, 0, 20, "G7"));
        Assert.Equal(
            ["G7:P1:20:20@3/220", "C3:P1:40:5@1/220|5+2@2/220|(5+2)×1.5@3/220|(5+2)×1.5 = 40@4/440"],
            beat.Entries.Select(Text));
    }

    [Fact]
    public void 压缩后遮罩与音效按压缩后的时刻()
    {
        // 「超过上限按比例压缩」的同一组算例（三条四步棋串，2640 ms → 1600 ms）：第 k 步开始于 220 × k × 1600 ÷ 2640，
        // 即 0、133、266、400 | 533、666、800、933 | 1066、1200、1333、1466，全拍结束于 1600。画面（遮罩）与音效提示都按压缩后的时刻走，不按未压缩的 220 ms。
        ImmutableArray<SettlementBeat> beats =
        [
            Reveal(["C3", "F6", "H9"], Group(P1, 5, 2, 1, 10, "C3"), Group(P1, 4, 2, 1, 9, "F6"), Group(P1, 6, 2, 1, 12, "H9")),
            new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)]),
        ];
        var timeline = new ShowTimeline(beats, ShowDuration.Normal);
        Assert.Equal("Reveal:1", Cues(SoundCues.Between(ShowMask.Empty, timeline.Mask(), [])));

        // 第 200 ms：第一条（F6）的第二步已在 133 ms 开始（未压缩要到 220 ms），步内进度 (200 − 133) ÷ (266 − 133) = 503‰；第二条 533 ms 才轮到。
        // 档位按阈值 8 / 16 / 32 / 64：三条军势 9 / 10 / 12 都是二档，四步为一、一、一、二。
        Assert.Equal("Reveal:1", Advance(200));
        Assert.Equal("F6:4+2:+2:1:503:未到:2:0", RevealText(timeline.Mask()));

        // 第 300 ms：第三步（一档）已在 266 ms 开始。
        Assert.Equal("Reveal:1", Advance(100));

        // 第 1500 ms：三条的末步分别开始于 400、933、1466 ms；最后一步的步内进度 (1500 − 1466) ÷ (1600 − 1466) = 253‰，
        // 结果年龄按二档的 1400 ms 停留计：1100、567、34 ms → 785、405、24‰。
        Assert.Equal("Reveal:2", Advance(1200));
        Assert.Equal(
            "F6:(4+2)×1.5 = 9:= 9:2:1000:末步:2:785,C3:(5+2)×1.5 = 10:= 10:2:1000:末步:2:405,H9:(6+2)×1.5 = 12:= 12:2:253:末步:2:24",
            RevealText(timeline.Mask()));

        // 第 1600 ms 整拍结束、进入势力节拍：十二步都已开始过，不再有"揭示"。
        Assert.Equal("Group:1", Advance(100));
        Assert.IsType<PowerBeat>(timeline.Current);

        string Advance(int ms)
        {
            ShowMask before = timeline.Mask();
            ImmutableArray<SettlementBeat> crossed = timeline.Advance(ms);
            return Cues(SoundCues.Between(before, timeline.Mask(), crossed));
        }

        static string Cues(ImmutableArray<SoundCue> cues) => string.Join(",", cues.Select(c => $"{c.Kind}:{c.Tier}"));
    }

    [Fact]
    public void 节拍内容只取自公开信息()
    {
        foreach (Type root in new[] { typeof(PowerRevealBeat), typeof(RevealEntry), typeof(RevealStep), typeof(RevealDisplay), typeof(ImpactRing) })
        {
            Assert.Empty(PresentationFixtures.PrivateLeaks(root));
        }
    }
}
