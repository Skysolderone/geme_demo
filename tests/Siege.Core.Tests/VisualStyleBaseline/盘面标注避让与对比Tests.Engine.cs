using System.Text.RegularExpressions;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>
/// 规格：visual-style-baseline —— Requirement: 盘面标注的避让与对比，引擎层接线的源码守门（board-labels 段 B，design D6 第二条与 D5）。
/// <c>src/godot</c> 不在 sln 里，单元测试碰不到它：规格写的"引擎层只按给出的进度画透明度、MUST NOT 自己判断重叠""算式行 MUST NOT 向白色混色"
/// "标注颜色由呈现层给出""引擎层 MUST NOT 另写透明度数值"只能读脚本源码守（<see cref="PresentationFixtures.GodotScriptCode"/> 去注释，
/// <see cref="PresentationFixtures.MethodBody"/> 取方法体且签名须恰好出现一次）。这些守门钉在写法上，重构这几个方法时要同步改这里。
/// 关键的赋值一律按"整句恰好如此、整份脚本里只赋值一次"断言：把混色 / 自算颜色挪进辅助方法，整句就不再是呈现层的那个调用，照样红。
/// </summary>
/// <remarks>
/// 变异验证（段 B；脚本做法同段 A：二进制读入原文、带时间戳的备份 → 断言锚点恰命中 1 次 → 改写 → 跑 VisualStyleBaseline
/// + TacticalLayers + SettlementShow 三个命名空间 → finally 里写回原文、逐字节比对、刷新 mtime；红数取自测试输出的统计行；基线 310 通过 / 2 跳过，
/// 全部还原后 <c>git diff</c> 与变异前逐字节相同、复跑 0 红）。编号标在下列各测试的注释里，25 条全部红 1，红的都是对应的那一条：
/// 让位（<c>BoardView.cs</c>）B-Y1「宽高对调」、B-Y2「传空遮罩」、B-Y3「右行查左表」、B-Y4「远边恒取 1000」、B-Y5「描边不乘透明度」、
/// B-Y6「另写 FarRevealed：r.Coord.Y &gt;= _height − 3」（绕过尝试）、B-Y7「逐帧刷新漏调」、B-Y8「去掉复原」、B-Y9「远边表记成近边节点」→ 坐标标注透明度只取自让位结果；
/// 算式行 B-R1「恢复 Lerp(Colors.White, 0.4f)」、B-R2「混色挪进辅助方法 FormulaTint」（绕过尝试）、B-R3「字号写回 max(64, 60%)」、B-R4「描边写回按比例」、
/// B-R5「重新声明 RevealFormulaMinFontSize」→ 算式行颜色字号描边取自呈现层不向白色混色；
/// 常驻标注 B-G1「字色 Lightened(0.75f)」、B-G2「描边 Darkened(0.45f)」、B-G3「描边改取 Visuals.CoordinateLabelOutline（值同、不经呈现层）」、B-G4「描边不经下限」、
/// B-G5「恒用右前角」、B-G6「引擎层按 item.Coord.X == _width − 1 另判角位」（绕过尝试）、B-G7「左前角 x 写成正」→ 常驻标注配色描边与角位取自呈现层；
/// 两色 B-C1「BoardView 另写 Color.Color8(18, 19, 23)」（绕过尝试）、B-C2「Visuals 改回字面量」→ 坐标标注两色只在呈现层定义；
/// 次要文字 B-U1「定义改回 0.62f」、B-U2「Hud.cs 里写 Ui.InfoText with { A = 0.62f }」（绕过尝试；hud-theme / hud-panels 的既有守门对它 0 红）→ 次要文字透明度。
/// 检查方（同一脚本做法，只跑 VisualStyleBaseline，基线 79 通过）：14 条，补规则前 C-K1「新 partial 文件 BoardView.Labels.cs 读 mask.Reveals、按
/// _height - 4 &lt; r.Coord.Y 自判远边并直接改远边列透明度」、C-K3「DrawGroupPower 追加 label.Modulate = Color.FromHsv(...)」、C-K4「Hud.cs 写
/// new Color(Ui.InfoText, 0.62f)」、C-K6「Label 工厂缺省描边色写成 Colors.Black」、C-K8「新 partial 遍历常驻标注层改描边色」5 条 0 红；补规则（各测试里标"检查方补"）后
/// 全部红 1，另 C-K11「FinishShow 不再标脏」、C-K12「右行数字漏 AddChild」、C-K13「新 partial 只读 mask.Reveals」、C-K14「RefreshShow 直接改远边列透明度」各红 1；
/// C-K2「透明度用 Mathf.Lerp」、C-K5「MutedText 写成 new Color(InfoText, 0.62f)」、C-K7「描边写成 Colors.Black」、C-K9「另加 A = 0.5f * 0.8f」、C-K10「去掉复原」原本就红 1。
/// </remarks>
public partial class 盘面标注避让与对比Tests
{
    /// <summary>BoardView 去注释后的源码，并钉住样本口径。</summary>
    private static string BoardViewCode()
    {
        string view = PresentationFixtures.GodotScriptCode("BoardView.cs");
        Assert.True(view.Length >= 50_000, $"只读到 {view.Length} 字符");
        return view;
    }

    [Fact]
    public void 坐标标注透明度只取自让位结果()
    {
        // 规格：哪些坐标标注让位、此刻显现多少由呈现层给出；引擎层只按进度画透明度，MUST NOT 自己判断重叠。
        string view = BoardViewCode();
        string draw = PresentationFixtures.MethodBody(view, "private void DrawCoordinateYield(ShowMask mask)");
        string show = PresentationFixtures.MethodBody(view, "private static void ShowCoordinateLabel(Label3D label, int permille)");
        Assert.True(draw.Length >= 400, $"方法体只有 {draw.Length} 字符");

        // ① 让位结果只有一个来源：呈现层的 CoordinateLabelYield.Of(遮罩, 宽, 高)，且整份引擎层只在这一处调用它（B-Y1 改宽高 / B-Y2 传空遮罩）。
        Assert.Matches(@"CoordinateYieldView\s+yielded\s*=\s*CoordinateLabelYield\.Of\(\s*mask\s*,\s*_width\s*,\s*_height\s*\)\s*;", draw);
        Dictionary<string, string> all = HudScriptScan.AllScripts();
        Assert.Equal(["BoardView.cs: CoordinateLabelYield.Of("], HudScriptScan.Hits(all, new Regex(@"CoordinateLabelYield\s*\.\s*\w+\s*\(")));

        // ② 三排标注逐个按结果的进度画：远边列 / 左行 / 右行各查自己的表（B-Y3 右行查左表 / B-Y4 远边恒取满值）。
        Assert.Matches(@"ShowCoordinateLabel\(\s*_farColumnLabels\[x\]\s*,\s*yielded\.FarColumn\(x\)\s*\)\s*;", draw);
        Assert.Matches(@"ShowCoordinateLabel\(\s*_leftRowLabels\[y\]\s*,\s*yielded\.LeftRow\(y\)\s*\)\s*;", draw);
        Assert.Matches(@"ShowCoordinateLabel\(\s*_rightRowLabels\[y\]\s*,\s*yielded\.RightRow\(y\)\s*\)\s*;", draw);
        Assert.Equal(4, Regex.Matches(view, @"ShowCoordinateLabel\(").Count);

        // ③ 透明度 = 进度 ÷ 1000，字与描边同乘；色相仍是坐标标注两色（B-Y5 描边不乘透明度）。
        Assert.Matches(@"float\s+alpha\s*=\s*permille\s*/\s*1000f\s*;", show);
        Assert.Matches(@"label\.Modulate\s*=\s*new\s+Color\(\s*Visuals\.CoordinateLabel\s*,\s*alpha\s*\)\s*;", show);
        Assert.Matches(@"label\.OutlineModulate\s*=\s*new\s+Color\(\s*Visuals\.CoordinateLabelOutline\s*,\s*alpha\s*\)\s*;", show);

        // ④ 不自己判断重叠：画让位的两个方法里不读揭示条目、锚格、显示时长，也不出现让位范围的常量；整份引擎层不读揭示锚格的行列分量
        //    （B-Y6 在 BoardView 里另写一份"锚格靠近远边"的判断——绕过尝试）。反面命中：判据对这类写法确实命中。
        Regex ownJudgement = new(@"(?i)reveal\w*|\.Coord\b|shownms\w*|resultage\w*|FarRowReach|ColumnSpan|SideColumnReach|RowSpan|FadeMs|YieldPermille");
        Assert.Empty(ownJudgement.Matches(draw + show).Select(m => m.Value).Distinct());
        Regex anchorAxis = new(@"(?i)reveal\w*\s*\.\s*Coord\s*\.\s*[XY]\b|\w*\.Coord\.[XY]\s*[<>]=?\s*_(?:width|height)|_(?:width|height)\s*-\s*\w*(?:Reach|Span)\w*");
        Assert.Empty(HudScriptScan.Hits(all, anchorAxis));
        Assert.Matches(anchorAxis, "if (reveal.Coord.Y >= _height - 3)");
        Assert.Matches(anchorAxis, "bool far = r.Coord.Y >= _height;");
        Assert.Matches(anchorAxis, "int top = _height - FarReach;");

        // ⑤ 两条刷新路径都把遮罩交给它（完整刷新带当前遮罩、演出逐帧带本帧遮罩），别处不调（B-Y7 逐帧路径漏调）。
        Assert.Equal(
            ["DrawCoordinateYield(mask ?? ShowMask.Empty)", "DrawCoordinateYield(mask)"],
            Regex.Matches(view, @"(?<![A-Za-z])DrawCoordinateYield\(([^)]*)\)").Select(m => m.Value).Where(v => !v.Contains("ShowMask mask", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Matches(@"DrawCoordinateYield\(mask \?\? ShowMask\.Empty\)", PresentationFixtures.MethodBody(view, "public void Refresh(\n        ViewerWorld world,"));
        Assert.Matches(@"DrawCoordinateYield\(mask\)", PresentationFixtures.MethodBody(view, "public void RefreshShow(ViewerWorld world, SceneTreatment treatment, ShowMask mask)"));

        // ⑥ 遮罩为空时复原：结果空且上一次也没让位才不碰；上一次让过位就整排重画到完全显示（B-Y8 去掉"上一次让过位"的复原）。
        //    记号只在这里与重搭标注时改写（重搭即新节点、一律完全显示）。
        Assert.Matches(@"if\s*\(\s*yielded\.IsEmpty\s*&&\s*!_coordinatesYielded\s*\)\s*\{\s*return\s*;\s*\}\s*_coordinatesYielded\s*=\s*!yielded\.IsEmpty\s*;", draw);
        Assert.Equal(2, Regex.Matches(view, @"(?<![A-Za-z_.])_coordinatesYielded\s*=(?!=)").Count);
        Assert.Matches(@"_coordinatesYielded\s*=\s*false\s*;", PresentationFixtures.MethodBody(view, "private void BuildCoordinateLabels()"));

        // ⑦ 节点表只记让位的三排：远边列是 far: true 那一个，近边列字母不进表（B-Y9 把近边节点记进远边表）。
        string build = PresentationFixtures.MethodBody(view, "private void BuildCoordinateLabels()");
        Assert.Matches(@"_farColumnLabels\[x\]\s*=\s*Label\([^;]*far:\s*true\)", build);
        Assert.Matches(@"_leftRowLabels\[y\]\s*=\s*Label\([^;]*right:\s*false\)", build);
        Assert.Matches(@"_rightRowLabels\[y\]\s*=\s*Label\([^;]*right:\s*true\)", build);
        Assert.Matches(@"labels\.AddChild\(Label\([^;]*far:\s*false\)", build);
        Assert.Matches(@"labels\.AddChild\(\s*_farColumnLabels\[x\]\s*\)\s*;", build);
        Assert.Matches(@"labels\.AddChild\(\s*_leftRowLabels\[y\]\s*\)\s*;", build);
        Assert.Matches(@"labels\.AddChild\(\s*_rightRowLabels\[y\]\s*\)\s*;", build);

        // ⑧ 检查方补（C-K1：新起 partial 文件 BoardView.Labels.cs，读 mask.Reveals、按 "_height - 4 < r.Coord.Y" 自判远边并直接改远边列透明度，
        //    RefreshShow 里多调一次——上面 ①–⑦ 全部 0 红）。规则不认文件名：三张节点表与 "CoordinateLabels" 节点只许出现在字段声明、
        //    BuildCoordinateLabels 与 DrawCoordinateYield 里；BoardView 的全部 partial 文件里读揭示条目只许在 DrawRings / DrawReveals 里。
        Regex tables = new(@"_(?:farColumn|leftRow|rightRow)Labels\b");
        Assert.Equal([], HudScriptScan.Hits(all, tables).Where(h => !h.StartsWith("BoardView.cs: ", StringComparison.Ordinal)));
        Assert.Equal(3 + tables.Matches(build).Count + tables.Matches(draw).Count, tables.Matches(view).Count);
        Assert.Equal(["BoardView.cs: \"CoordinateLabels\""], HudScriptScan.Hits(all, new Regex("\"CoordinateLabels\"")));
        string[] partials = [.. all.Where(kv => Regex.IsMatch(kv.Value, @"partial\s+class\s+BoardView\b")).Select(kv => kv.Key)];
        Assert.Contains("BoardView.cs", partials);
        Regex reveals = new(@"\.\s*Reveals\b");
        int allowed = reveals.Matches(PresentationFixtures.MethodBody(view, "private void DrawRings(ShowMask mask)")).Count
            + reveals.Matches(PresentationFixtures.MethodBody(view, "private void DrawReveals(ShowMask mask)")).Count;
        Assert.True(allowed >= 2, $"DrawRings / DrawReveals 里只读到 {allowed} 处揭示条目");
        Assert.Equal(allowed, partials.Sum(p => reveals.Matches(all[p]).Count));

        // ⑨ 复原的触发（检查方补）：演出播完（含弃赛截断走的 FinishShow）一定标脏，下一帧完整刷新取的是 ShownMask()——时间线播完即空遮罩，
        //    DrawCoordinateYield 据记号把让过位的三排复原（C-K11 删掉 FinishShow 的标脏 → 红）。
        string root = PresentationFixtures.GodotScriptCode("GameRoot.cs");
        Assert.Matches(@"_dirty\s*=\s*true\s*;", PresentationFixtures.MethodBody(root, "private void FinishShow()"));
        Assert.Matches(@"ShowMask\s+mask\s*=\s*ShownMask\(\)\s*;\s*_board\.Refresh\([^;]*,\s*mask\s*\)\s*;", PresentationFixtures.MethodBody(root, "private void RefreshViews()"));
    }

    [Fact]
    public void 算式行颜色字号描边取自呈现层不向白色混色()
    {
        // 规格：算式行使用与结果相同的揭示色，MUST NOT 向白色混色；描边宽度不低于下限（下限与式子在 BoardLabelStyle，段 A 已钉）。
        string view = BoardViewCode();
        string draw = PresentationFixtures.MethodBody(view, "private void DrawReveals(ShowMask mask)");
        Assert.True(draw.Length >= 1_500, $"方法体只有 {draw.Length} 字符");

        // ① 不混色：画揭示的方法里没有 Lerp / Lightened / Colors.White（B-R1 恢复 color.Lerp(Colors.White, 0.4f)）。
        Assert.DoesNotMatch(@"Lerp\(|Lightened\(|Darkened\(|Colors\.White", draw);

        // ② 三项整句取自 BoardLabelStyle，按该步档位（B-R2 把混色挪进辅助方法 FormulaTint(color)——绕过尝试；B-R3 字号写回本地比例；B-R4 描边写回按比例）。
        Assert.Matches(@"int\s+fontSize\s*=\s*BoardLabelStyle\.RevealFormulaFontSizeOf\(\s*reveal\.StepTier\s*\)\s*;", draw);
        Assert.Matches(@"Label3D\s+trail\s*=\s*Label\([^;]*,\s*fontSize\s*,\s*BoardLabelStyle\.RevealFormulaOutlineOf\(\s*reveal\.StepTier\s*\)\s*\)\s*;", draw);
        Assert.Matches(@"trail\.Modulate\s*=\s*new\s+Color\(\s*Visuals\.ToColor\(\s*BoardLabelStyle\.RevealFormulaColorOf\(\s*reveal\.StepTier\s*\)\s*\)\s*,\s*alpha\s*\)\s*;", draw);
        Assert.Single(Regex.Matches(view, @"trail\.Modulate\s*="));
        Assert.Single(Regex.Matches(view, @"fontSize\s*=(?!=)[^;]*RevealFormula"));

        // ③ 本地常量已删：引擎层不再有算式行的字号比例与下限（B-R5 在 BoardView 重新声明 RevealFormulaMinFontSize = 64）。
        Assert.Empty(HudScriptScan.Hits(HudScriptScan.AllScripts(), new Regex(@"const\s+int\s+\w*(?:Formula|Trail)\w*")));
    }

    [Fact]
    public void 常驻标注配色描边与角位取自呈现层()
    {
        // 规格：常驻标注的字色与描边色由呈现层给出（描边 = 坐标标注描边色）、描边不低于下限、放在哪个角由呈现层随标注给出。
        string view = BoardViewCode();
        string draw = PresentationFixtures.MethodBody(view, "private void DrawGroupPower(ShowMask mask)");
        string offset = PresentationFixtures.MethodBody(view, "private static Vector3 GroupLabelOffsetOf(LabelCorner corner)");

        // ① 不自己换算颜色：没有提亮 / 压暗 / 混色、不直接取阵营色（B-G1 字色恢复 FactionColorOf(...).Lightened(0.75f)；B-G2 描边恢复 Darkened(0.45f)）。
        Assert.DoesNotMatch(@"Lightened\(|Darkened\(|Lerp\(|FactionColorOf\(|Color8\(|Colors\.", draw);

        // ② 颜色整句取自 BoardLabelStyle.GroupLabelColors，再乘透明度（B-G3 描边改用 Visuals.CoordinateLabelOutline 之外的本地颜色）。
        Assert.Matches(@"\(\s*Rgba\s+text\s*,\s*Rgba\s+outline\s*\)\s*=\s*BoardLabelStyle\.GroupLabelColors\(\s*item\.Owner\s*\)\s*;", draw);
        Assert.Matches(@"label\.Modulate\s*=\s*new\s+Color\(\s*Visuals\.ToColor\(\s*text\s*\)\s*,\s*alpha\s*\)\s*;", draw);
        Assert.Matches(@"label\.OutlineModulate\s*=\s*new\s+Color\(\s*Visuals\.ToColor\(\s*outline\s*\)\s*,\s*alpha\s*\)\s*;", draw);

        // ③ 描边宽度经呈现层的下限规则（B-G4 直接用按字号比例的 style.GroupLabelOutlineSize）；位置偏移只按呈现层给的角位（B-G5 恒用右前角）。
        Assert.Matches(@"Label\(\s*item\.Text\s*,\s*CenterOf\(\s*item\.Coord\s*\)\s*\+\s*GroupLabelOffsetOf\(\s*item\.Corner\s*\)\s*,\s*style\.GroupLabelFontSize\s*,\s*BoardLabelStyle\.GroupLabelOutlineFor\(\s*style\.GroupLabelOutlineSize\s*\)\s*\)", draw);

        // ④ 角位只翻译成偏移：左前角 x 取负，其余照旧；不看列号与棋盘宽度（B-G6 在引擎层按 item.Coord.X == _width - 1 另判一次——绕过尝试）。
        Assert.Matches(@"LabelCorner\.FrontLeft\s*=>\s*new\s+Vector3\(\s*-0\.33f\s*,\s*0\.12f\s*,\s*0\.34f\s*\)", offset);
        Assert.Matches(@"_\s*=>\s*new\s+Vector3\(\s*0\.33f\s*,\s*0\.12f\s*,\s*0\.34f\s*\)", offset);
        Assert.DoesNotMatch(@"_width|\.Coord\.|\.X\b", draw + offset);

        // ⑤ 检查方补：颜色只赋值一次、不另造颜色（C-K3 在呈现层那句之后追加 label.Modulate = Color.FromHsv(...)——①–④ 0 红）；
        //    常驻标注层只在搭建与 DrawGroupPower 里出现（C-K8 新起 partial 文件遍历该层改描边色——0 红）。
        Assert.Single(Regex.Matches(draw, @"label\.Modulate\s*=(?!=)"));
        Assert.Single(Regex.Matches(draw, @"label\.OutlineModulate\s*=(?!=)"));
        Assert.DoesNotMatch(@"Color\s*\.\s*From\w*\(|new\s+Color\(\s*[-\d.]", draw);
        Regex layer = new(@"_groupLabelLayer\b");
        Assert.Equal([], HudScriptScan.Hits(HudScriptScan.AllScripts(), layer).Where(h => !h.StartsWith("BoardView.cs: ", StringComparison.Ordinal)));
        Assert.Equal(3 + layer.Matches(draw).Count, layer.Matches(view).Count);
    }

    [Fact]
    public void 坐标标注两色只在呈现层定义()
    {
        // design D3：坐标标注两色搬到呈现层，Visuals 只翻译；引擎层任何脚本都不再写 (206, 202, 190) / (18, 19, 23) 这两个值
        // （B-C1 在 BoardView 另写一份 Color.Color8(18, 19, 23)——绕过尝试；B-C2 Visuals 改回字面量）。反面命中：判据对字面量写法确实命中。
        Dictionary<string, string> all = HudScriptScan.AllScripts();
        Regex literal = new(@"(?:Color8|new\s+Color|new|FromHtml)\s*\(\s*(?:206\s*,\s*202\s*,\s*190|18\s*,\s*19\s*,\s*23)\b|(?:206|18)\s*/\s*255f?\s*,\s*(?:202|19)\s*/\s*255");
        Assert.Empty(HudScriptScan.Hits(all, literal));
        Assert.Matches(literal, "Color.Color8(18, 19, 23)");
        Assert.Matches(literal, "new Color(206 / 255f, 202 / 255f, 190 / 255f)");
        Assert.Matches(literal, "new(18, 19, 23)");

        string visuals = all["Visuals.cs"];
        Assert.Matches(@"public\s+static\s+readonly\s+Color\s+CoordinateLabel\s*=\s*ToColor\(\s*BoardLabelStyle\.CoordinateLabel\s*\)\s*;", visuals);
        Assert.Matches(@"public\s+static\s+readonly\s+Color\s+CoordinateLabelOutline\s*=\s*ToColor\(\s*BoardLabelStyle\.CoordinateLabelOutline\s*\)\s*;", visuals);

        // 检查方补（C-K6：Label 工厂的缺省描边色写成 Colors.Black——近边列字母与未让位的标注都走缺省值，上面 0 红）：工厂缺省两色取 Visuals 的翻译值。
        string factory = PresentationFixtures.MethodBody(all["BoardView.cs"], "private static Label3D Label(");
        Assert.Matches(@"\bModulate\s*=\s*Visuals\.CoordinateLabel\s*,", factory);
        Assert.Matches(@"\bOutlineModulate\s*=\s*Visuals\.CoordinateLabelOutline\s*,", factory);
    }

    /// <summary>
    /// D5 的守门（由 <see cref="次要文字透明度"/> 调用）：<c>Ui.MutedText</c> 是正文色乘以 <c>UiTheme.MutedTextAlphaPermille</c> 换算出的透明度，
    /// 定义里没有浮点字面量；引擎层任何脚本都不另写 <c>A = 0.xx</c> 式的透明度（B-U1 定义改回 0.62f；B-U2 在 Hud.cs 写 Ui.InfoText with { A = 0.62f }——绕过尝试）。
    /// </summary>
    private static void MutedText只由UiTheme的千分比换算()
    {
        Dictionary<string, string> all = HudScriptScan.AllScripts();
        System.Text.RegularExpressions.Match definition = Assert.Single(Regex.Matches(all[HudScriptScan.Factory], @"public\s+static\s+Color\s+MutedText\s*=>[^;]*;"));
        Assert.Matches(@"=>\s*Visuals\.ToColor\(\s*UiTheme\.InfoText\s*\)\s*with\s*\{\s*A\s*=\s*UiTheme\.MutedTextAlphaPermille\s*/\s*1000f\s*\}\s*;$", definition.Value);
        Assert.DoesNotMatch(@"\d\.\d", definition.Value);

        Regex alphaLiteral = new(@"(?<![A-Za-z_])A\s*=\s*\d*\.\d");
        Assert.Empty(HudScriptScan.Hits(all, alphaLiteral));
        Assert.Matches(alphaLiteral, "Ui.InfoText with { A = 0.62f }");
        Assert.Matches(alphaLiteral, "color with {A=.6f}");

        // 检查方补（C-K4：Hud.cs 写 new Color(Ui.InfoText, 0.62f)——带透明度的 Color 构造，上面与 hud-theme 守门都 0 红）：
        // HUD 脚本、总览页与控件工厂里不得以"颜色 + 浮点字面量"构造带透明度的颜色。
        Dictionary<string, string> hud = HudScriptScan.Read([.. HudScriptScan.HudScripts, HudScriptScan.Gallery, HudScriptScan.Factory], 60_000);
        Regex colorAlpha = new(@"new\s*(?:Color)?\s*\(\s*[A-Za-z_][\w.]*\s*,\s*\d*\.\d");
        Assert.Empty(HudScriptScan.Hits(hud, colorAlpha));
        Assert.Matches(colorAlpha, "new Color(Ui.InfoText, 0.62f)");
        Assert.Matches(colorAlpha, "new(InfoText, .62f)");
    }
}
