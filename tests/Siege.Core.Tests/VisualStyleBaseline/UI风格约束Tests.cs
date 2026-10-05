using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Presentation.Show;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: UI 风格约束</summary>
/// <remarks>
/// tiered-number-show 增两条 Scenario 的数据层部分（上限与"不只靠颜色"）；画面本身（截图、灰度对照）归段 B 与人工检查清单。
/// 这两条与 <c>数值档位Tests.分档样式表</c> 的分工：那一条逐格钉住样式表的取值，调参时会跟着改；这里钉的是规格写明的<b>上限</b>，调参不得越过。
/// 变异验证（tiered-number-show 段 A 检查；二进制读写、锚点恰命中 1 次、finally 还原后逐字节比对并刷新 mtime，跑 SettlementShow + TacticalLayers + VisualStyleBaseline）：
/// M-V1「五档三圈亮环」——<c>NumberTierStyle.All</c> 五档 <c>RingCount: 2</c> 改为 <c>3</c> → 红 5（本类 分档强调不靠粒子，数值档位Tests.分档样式表 tier=5，高档冲击环与镜头轻震Tests 的 五档两圈并轻震 / 轻震结束 / 同一时刻至多一个轻震）。
/// M-V2「二档字号与一档相同」——二档 <c>RevealFontSize: 84</c> 改为 <c>72</c> → 红 3（本类 灰度下档位可辨，数值档位Tests 的 分档样式表 tier=2 / 样式表各列逐档单调不减）。
/// M-V3「轻震 250 ms 改 300 ms」——五档 <c>ShakeMs: 250</c> 改为 <c>300</c> → 红 5（与 M-V1 同样五条）。
/// 粒子扫描的反面命中用测试内字面量（引擎层目前零命中，没有可供命中的真实文件）。
/// </remarks>
public class UI风格约束Tests
{
    [Fact]
    public void 信息密度()
    {
        // 设计文档 §20：深色半透明面板、克制金色边框、高对比信息色，PC 策略游戏信息密度，无手游式大按钮。
        // 数据层基准（1080p 参考）：面板半透明（alpha 128–240）且暗（亮度 ≤ 48）；边框为金色系（R > G > B）且线宽 ≤ 2；
        // 信息文字与面板亮度差 ≥ 160；按钮高度低于手游式下限、正文字号 ≤ 16。实际观感与弹窗形态归阶段 B + 人工检查清单。
        // 变异验证 M-UI1：UiTheme.PanelFill 的 alpha 改为 255（不透明）→ 本测试红 1。
        Assert.InRange(UiTheme.PanelFill.A, (byte)128, (byte)240);
        Assert.True(UiTheme.PanelFill.Luma <= 48, $"面板亮度 {UiTheme.PanelFill.Luma}");
        Assert.True(UiTheme.PanelBorder.R > UiTheme.PanelBorder.G && UiTheme.PanelBorder.G > UiTheme.PanelBorder.B, UiTheme.PanelBorder.Hex);
        Assert.InRange(UiTheme.BorderWidthPx, 1, 2);
        Assert.True(UiTheme.InfoText.Luma - UiTheme.PanelFill.Luma >= 160, $"对比 {UiTheme.InfoText.Luma - UiTheme.PanelFill.Luma}");
        Assert.True(UiTheme.DangerText.Luma - UiTheme.PanelFill.Luma >= 80, $"警示色对比 {UiTheme.DangerText.Luma - UiTheme.PanelFill.Luma}");
        Assert.True(UiTheme.ButtonHeightPx < UiTheme.MobileStyleButtonHeightPx);
        Assert.InRange(UiTheme.BodyFontPx, 12, 16);
    }

    [Fact]
    public void 分档强调不靠粒子()
    {
        // 规格上限：亮环每次至多两圈、持续不超过 0.4 秒；镜头轻震每次不超过 0.25 秒。样式表任何一档都不得越过。
        Assert.True(NumberTierStyle.All.Max(s => s.RingCount) <= 2, $"亮环圈数 {NumberTierStyle.All.Max(s => s.RingCount)}");
        Assert.True(ImpactRing.DurationMs <= 400, $"亮环时长 {ImpactRing.DurationMs}");
        Assert.True(NumberTierStyle.All.Max(s => s.ShakeMs) <= 250, $"轻震时长 {NumberTierStyle.All.Max(s => s.ShakeMs)}");

        // 一次结算揭示出五档的军势（规格算例：基础 20、加值 4、倍增子 1、军势 36），逐毫秒走完整场演出：
        // 画面上的中间态只有揭示条目（数字）、至多两圈亮环与轻震；亮环累计不超过 400 ms，轻震累计不超过 250 ms。
        var group = new GroupPower(new PlayerId(1), [Coord.Parse("D4")], BaseTotal: 20, LineBonus: 4, SynergyBonus: 0, HighGroundBonus: 0,
            BannerBonus: 0, ChainBonus: 0, SentryBonus: 0, BoundaryBonus: 0, MultiplierCount: 1, Power: 36);
        var timeline = new ShowTimeline(
            [new PowerRevealBeat([RevealEntry.From(group)]), new PowerBeat([new PowerChange(new PlayerId(1), 3, 5, 1, 1)])],
            ShowDuration.Normal);
        int ringMs = 0, shakeMs = 0, revealMs = 0;
        while (!timeline.IsFinished)
        {
            ShowMask mask = timeline.Mask();
            Assert.True(mask.Rings.Sum(r => r.Count) <= 2, $"同时 {mask.Rings.Sum(r => r.Count)} 圈亮环");
            ringMs += mask.Rings.IsEmpty ? 0 : 1;
            shakeMs += mask.ShakePermille is null ? 0 : 1;
            revealMs += mask.Reveals.IsEmpty ? 0 : 1;
            timeline.Advance(1);
        }

        // 样本口径下界：五档确实触发了亮环与轻震（否则上面的"不超过"是对着空遮罩说的），条目自始至终在显示。
        Assert.Equal((400, 250, 1320 + 900), (ringMs, shakeMs, revealMs));

        // 不使用粒子：引擎层（src/godot 不在 siege.sln 里，IL 守门扫不到）的脚本里不得出现任何粒子节点或粒子材质。源码文本扫描，配样本下界与反面命中。
        Regex particles = new(@"(?i)particle\w*");
        string scripts = Path.Combine(PresentationFixtures.RepoRoot(), "src", "godot", "scripts");
        (string Name, string Text)[] files = [.. Directory.GetFiles(scripts, "*.cs").Order().Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];
        Assert.True(files.Length >= 10, $"只扫到 {files.Length} 个脚本");
        Assert.True(files.Sum(f => f.Text.Length) >= 50_000, $"只扫到 {files.Sum(f => f.Text.Length)} 字符");
        Assert.Empty(files.Where(f => particles.IsMatch(f.Text)).Select(f => f.Name));
        Assert.Matches(particles, "AddChild(new GpuParticles3D());");
        Assert.Matches(particles, "var burst = new CPUParticles2D { Emitting = true };");
    }

    [Fact]
    public void 灰度下档位可辨()
    {
        // 档位之间不得只靠颜色区分：字号同时分档，任意两档的揭示字号都不同（逐档严格增大），转成灰度后仍能凭字号分辨。
        int[] fonts = [.. NumberTierStyle.All.Select(s => s.RevealFontSize)];
        Assert.Equal(5, fonts.Distinct().Count());
        for (int i = 1; i < fonts.Length; i++)
        {
            Assert.True(fonts[i] > fonts[i - 1], $"{i + 1} 档字号 {fonts[i]} 对 {i} 档 {fonts[i - 1]}");
        }

        // 规格场景：一档与五档的字号不同（五档更大），不看颜色也分得出。
        Assert.True(NumberTierStyle.For(5).RevealFontSize > NumberTierStyle.For(1).RevealFontSize);

        // 常驻标注只用阵营色（不分档着色），完全靠字号分档：同样逐档严格增大。
        int[] labels = [.. NumberTierStyle.All.Select(s => s.GroupLabelFontSize)];
        for (int i = 1; i < labels.Length; i++)
        {
            Assert.True(labels[i] > labels[i - 1], $"常驻标注 {i + 1} 档字号 {labels[i]} 对 {i} 档 {labels[i - 1]}");
        }
    }
}
