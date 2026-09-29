using System.Text.RegularExpressions;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Presentation.Hand;

namespace Siege.Core.Tests.HandInfoPanel;

/// <summary>
/// 规格：hand-info-panel —— Requirement: 征募面板可收起（recruit-panel-collapse 1.1）。
/// 状态模型 <see cref="RecruitPanelCollapse"/> 在 Presentation，纯状态、直接单测；Godot 侧（不在解决方案里，对 IL / 反射守门隐身）用源码扫描守门，
/// 扫描配样本口径下界与反面命中（testing.md「不在解决方案里的工程对守门测试完全隐身」）。
/// </summary>
/// <remarks>
/// 变异验证（1.2，脚本二进制读写、还原后逐字节比对并刷新 mtime）：
/// M-RC1「新阶段不重置」——<c>Sync</c> 里删掉 <c>IsCollapsed = false</c> → 红 3（新征募阶段默认展开、换一个征募阶段即重置、不在本机征募阶段时收起键无效）；
/// M-RC2「切换时清空已选」——<c>Present</c> 把面板换成 <c>PicksMade = 0</c>、候选 <c>IsPicked</c> 全清的副本 → 红 1（展开后状态不变）。
/// 两条均在 HandInfoPanel 命名空间 25 条里跑，还原后基线 25/25 绿。
/// </remarks>
public class 征募面板可收起Tests
{
    private static readonly RecruitPhaseKey Round5Turn1 = new(5, 1);

    /// <summary>规格 Scenario「展开后状态不变」的算例：已选取 1 枚、剩余 1 次免费选取——免费选取数取 2（缺省 3 时选 1 剩 2，对不上算例）。</summary>
    private static PlayerHandAccess OnePickedOneLeft()
    {
        HandLedger ledger = HandFixtures.Ledger();
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, reveal: 5, freePick: 2);
        access.EnterRecruit();
        access.Pick(0);
        return access;
    }

    [Fact]
    public void 收起后看全场()
    {
        PlayerHandAccess access = OnePickedOneLeft();
        RecruitPanelView panel = access.Panel();
        var model = new RecruitPanelCollapse();
        model.Sync(Round5Turn1);

        RecruitCenterView expanded = model.Present(panel);
        Assert.False(expanded.IsCollapsed);

        // 按 V：面板收起为提示条；提示条文案（design D3）带展示数与剩余免费选取数。
        Assert.True(model.Toggle(handPanelOpen: false));
        RecruitCenterView collapsed = model.Present(panel);
        Assert.True(collapsed.IsCollapsed);
        Assert.Equal("征募（已收起）· 按 V 展开 · 展示 5 · 还可免费选取 1", collapsed.BarText);

        // 再按 V：恢复原面板。
        Assert.True(model.Toggle(handPanelOpen: false));
        Assert.False(model.Present(panel).IsCollapsed);
    }

    [Fact]
    public void 展开后状态不变()
    {
        PlayerHandAccess access = OnePickedOneLeft();
        RecruitPanelView before = access.Panel();
        Assert.Equal((5, 1, 1), (before.ShowCount, before.PicksMade, before.PicksRemaining));
        Assert.True(before.Candidates[0].IsPicked);

        var model = new RecruitPanelCollapse();
        model.Sync(Round5Turn1);
        Assert.True(model.Toggle(handPanelOpen: false));
        Assert.Equal(before, model.Present(access.Panel()).Panel);

        // 推屏 / 缩放 / 开信息层：同一征募阶段内的若干次刷新，收起状态保持。
        for (int i = 0; i < 3; i++)
        {
            model.Sync(Round5Turn1);
            Assert.True(model.Present(access.Panel()).IsCollapsed);
        }

        Assert.True(model.Toggle(handPanelOpen: false));
        RecruitCenterView after = model.Present(access.Panel());
        Assert.False(after.IsCollapsed);

        // 展示的候选、已选取、剩余选取数与收起前完全相同——呈现出来的面板与账本里的面板两处都钉住。
        Assert.Equal(before, after.Panel);
        Assert.Equal(before.Candidates.Select(c => (c.Index, c.Type, c.IsPicked, c.IsSelectable)),
            after.Panel.Candidates.Select(c => (c.Index, c.Type, c.IsPicked, c.IsSelectable)));
        Assert.Equal((1, 1), (after.Panel.PicksMade, after.Panel.PicksRemaining));
        Assert.Equal(before, access.Panel());
        Assert.Equal(1, access.PrivateView().PendingGained);
    }

    [Fact]
    public void 新征募阶段默认展开()
    {
        // 真实 MatchFlow 跨两个大回合：第 5 大回合 P0 征募时收起、完成征募并落子；其余三人各落一子（不 Pass——全员 Pass 会触发终局），
        // 第 6 大回合轮到 P0 再次进入征募阶段 → 面板以展开状态出现。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        var cells = new Dictionary<PlayerId, Queue<string>>
        {
            [MatchFixtures.P1] = new(["J1", "H1", "G1"]),
            [MatchFixtures.P2] = new(["A9", "A8", "A7"]),
            [MatchFixtures.P3] = new(["J9", "H9", "G9"]),
        };
        var model = new RecruitPanelCollapse();

        match.BeginTurn();
        Assert.Equal(MatchFixtures.P0, match.CurrentPlayer);
        RecruitPanelView first = match.EnterRecruit();
        RecruitPhaseKey? k1 = RecruitPhaseKey.Of(match.Publish(), MatchFixtures.P0);
        Assert.Equal(new RecruitPhaseKey(5, 1), k1);
        model.Sync(k1);
        Assert.True(model.Toggle(handPanelOpen: false));
        Assert.True(model.Present(first).IsCollapsed);

        StagedBatch batch = match.EnterDeploy();
        Assert.Null(batch.Stage(Coord.Parse("A1"), PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);
        model.Sync(RecruitPhaseKey.Of(match.Publish(), MatchFixtures.P0));

        while (match.CurrentPlayer != MatchFixtures.P0)
        {
            match.PlayTurn(cells[match.CurrentPlayer!.Value].Dequeue());
            model.Sync(RecruitPhaseKey.Of(match.Publish(), MatchFixtures.P0));
        }

        match.BeginTurn();
        RecruitPanelView second = match.EnterRecruit();
        RecruitPhaseKey? k2 = RecruitPhaseKey.Of(match.Publish(), MatchFixtures.P0);
        Assert.Equal(6, k2!.Value.MajorRound);
        model.Sync(k2);
        Assert.False(model.IsCollapsed);
        Assert.False(model.Present(second).IsCollapsed);
    }

    [Fact]
    public void 换一个征募阶段即重置_不经过非征募阶段也重置()
    {
        RecruitPanelView panel = OnePickedOneLeft().Panel();
        var model = new RecruitPanelCollapse();
        model.Sync(Round5Turn1);
        model.Toggle(handPanelOpen: false);
        Assert.True(model.IsCollapsed);

        model.Sync(new RecruitPhaseKey(5, 2));
        Assert.False(model.Present(panel).IsCollapsed);

        model.Toggle(handPanelOpen: false);
        model.Sync(new RecruitPhaseKey(6, 2));
        Assert.False(model.IsCollapsed);
    }

    [Fact]
    public void 征募阶段标识只认本机玩家的征募阶段()
    {
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P2, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P3]);
        match.BeginTurn();
        Assert.Null(RecruitPhaseKey.Of(match.Publish(), MatchFixtures.P2));   // 整理手牌阶段不算
        match.EnterRecruit();
        Assert.Null(RecruitPhaseKey.Of(match.Publish(), MatchFixtures.P0));   // 别人的征募阶段不算
        Assert.Equal(new RecruitPhaseKey(5, 1), RecruitPhaseKey.Of(match.Publish(), MatchFixtures.P2));
        match.EnterDeploy();
        Assert.Null(RecruitPhaseKey.Of(match.Publish(), MatchFixtures.P2));   // 部署阶段不算
        Assert.True(match.Confirm().Confirmed);

        match.BeginTurn();
        match.EnterRecruit();
        Assert.Equal(new RecruitPhaseKey(5, 2), RecruitPhaseKey.Of(match.Publish(), MatchFixtures.P0));
    }

    [Fact]
    public void 不在本机征募阶段时收起键无效()
    {
        var model = new RecruitPanelCollapse();
        Assert.False(model.Toggle(handPanelOpen: false));
        Assert.False(model.IsCollapsed);

        model.Sync(Round5Turn1);
        model.Toggle(handPanelOpen: false);
        model.Sync(null);   // 完成征募进入部署
        Assert.False(model.Toggle(handPanelOpen: false));
        Assert.False(model.IsCollapsed);
    }

    [Fact]
    public void 收起状态下开关手牌面板后回到收起()
    {
        // design D4：手牌信息面板优先级不变；收起状态下按 H 打开、再关闭 → 回到"收起"。手牌面板开着时 V 不生效（否则看不见的状态被翻转）。
        RecruitPanelView panel = OnePickedOneLeft().Panel();
        var model = new RecruitPanelCollapse();
        var hand = new HandPanelState();
        model.Sync(Round5Turn1);
        model.Toggle(hand.IsOpen);
        Assert.True(model.IsCollapsed);

        hand.ClickButton();
        Assert.False(model.Toggle(hand.IsOpen));
        model.Sync(Round5Turn1);
        Assert.True(model.IsCollapsed);

        hand.ClickButton();
        Assert.False(hand.IsOpen);
        Assert.True(model.Present(panel).IsCollapsed);

        hand.ClickButton();
        hand.Back();
        Assert.True(model.Present(panel).IsCollapsed);
    }

    [Fact]
    public void 收起不进对局状态_恢复后面板为展开()
    {
        // 收起只在表现层：存档恢复后是新建的界面状态，面板以展开出现。
        var restored = new RecruitPanelCollapse();
        Assert.False(restored.IsCollapsed);
        restored.Sync(Round5Turn1);
        Assert.False(restored.Present(OnePickedOneLeft().Panel()).IsCollapsed);
    }

    // ---------- 源码扫描守门 ----------

    private static readonly string Src = Path.Combine(PresentationFixtures.RepoRoot(), "src");

    /// <summary>收起状态的标识符；任何一个出现在对局状态、存档、日志的代码里都算泄漏。</summary>
    private static readonly Regex CollapseTokens = new(@"RecruitPanelCollapse\w*|RecruitPhaseKey\w*|RecruitCenterView\w*|IsCollapsed\w*|已收起|_recruitCollapse\w*", RegexOptions.CultureInvariant);

    private static (string Path, string Text)[] Sources(string relativeDir)
    {
        (string, string)[] files =
        [
            .. Directory.EnumerateFiles(Path.Combine(Src, relativeDir), "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(p => (Path.GetRelativePath(Src, p).Replace('\\', '/'), File.ReadAllText(p))),
        ];
        return files;
    }

    private static string Godot(string name) => File.ReadAllText(Path.Combine(Src, "godot", "scripts", name));

    [Fact]
    public void 收起不进对局状态_Core与Sim与存档代码不含收起状态()
    {
        (string Path, string Text)[] core = Sources("Siege.Core");
        (string Path, string Text)[] sim = Sources("Siege.Sim");
        (string Path, string Text)[] godot = Sources(Path.Combine("godot", "scripts"));

        // 样本口径下界：扫到空目录时下面的断言恒真。
        Assert.True(core.Length >= 80, $"Core 只扫到 {core.Length} 个文件");
        Assert.True(sim.Length >= 15, $"Sim 只扫到 {sim.Length} 个文件");
        Assert.True(core.Sum(f => f.Text.Length) >= 500_000);
        Assert.Contains(core, f => f.Path == "Siege.Core/Match/MatchFlow.Persistence.cs");
        Assert.Contains(sim, f => f.Path == "Siege.Sim/Running/MatchSession.cs");

        // 对局状态、存档、日志、回放（Core / Sim）与图形版的对局驱动、带入带出档案：一处都不得出现。
        string[] stateFiles = ["godot/scripts/MatchSession.cs", "godot/scripts/GameRoot.Carry.cs", "godot/scripts/Hud.Carry.cs", "godot/scripts/BoardView.cs"];
        Assert.All(stateFiles, f => Assert.Contains(godot, g => g.Path == f));
        string[] leaks =
        [
            .. core.Concat(sim).Concat(godot.Where(g => stateFiles.Contains(g.Path)))
                .Where(f => CollapseTokens.IsMatch(f.Text))
                .Select(f => $"{f.Path}: {CollapseTokens.Match(f.Text).Value}"),
        ];
        Assert.Empty(leaks);

        // 反面命中：判据在模型与接线文件里确实命中（正则写错时上面恒真）。
        Assert.Matches(CollapseTokens, File.ReadAllText(Path.Combine(Src, "Siege.Presentation", "Hand", "RecruitPanelCollapse.cs")));
        Assert.Matches(CollapseTokens, Godot("GameRoot.cs"));
        Assert.Matches(CollapseTokens, Godot("Hud.cs"));

        // Sim 引用了 Presentation：再从 IL 上确认 Sim 程序集没有碰收起模型（源码扫描挡不住 using 别名之类的绕过）。
        Type[] collapseTypes = [typeof(RecruitPanelCollapse), typeof(RecruitPhaseKey), typeof(RecruitCenterView)];
        string[] ilHits =
        [
            .. PresentationFixtures.IlReferences(typeof(Siege.Sim.Running.MatchSession).Assembly)
                .Where(r => collapseTypes.Contains(r.Target.DeclaringType))
                .Select(r => $"{r.Caller.DeclaringType?.Name}.{r.Caller.Name} → {r.Target.Name}"),
        ];
        Assert.Empty(ilHits);
    }

    [Fact]
    public void 图形版经状态模型决定收起()
    {
        string hud = Godot("Hud.cs");
        string root = Godot("GameRoot.cs");
        (string Path, string Text)[] godot = Sources(Path.Combine("godot", "scripts"));
        Assert.True(godot.Length >= 15, $"只扫到 {godot.Length} 个脚本");
        Assert.True(hud.Length >= 20_000 && root.Length >= 40_000);

        // Hud：征募面板 / 提示条由模型的呈现结果决定，面板内容吃模型给出的 Panel，提示条文案取模型的 BarText（本层不拼文案）。
        Assert.Contains(".Present(", hud, StringComparison.Ordinal);
        Assert.Contains(".IsCollapsed", hud, StringComparison.Ordinal);
        Assert.Contains(".BarText", hud, StringComparison.Ordinal);
        string[] buildCalls = [.. Regex.Matches(hud, @"(?<!void )BuildRecruit\(([^)]*)\)").Select(m => m.Groups[1].Value)];
        Assert.NotEmpty(buildCalls);
        Assert.All(buildCalls, arg => Assert.EndsWith(".Panel", arg, StringComparison.Ordinal));

        // D4：RefreshCenter 里手牌信息面板的判断在征募面板之前（优先级不变）。
        int refresh = hud.IndexOf("private void RefreshCenter(", StringComparison.Ordinal);
        Assert.True(refresh > 0);
        int handOpen = hud.IndexOf("handPanel.IsOpen", refresh, StringComparison.Ordinal);
        int present = hud.IndexOf(".Present(", refresh, StringComparison.Ordinal);
        Assert.True(handOpen > 0 && present > handOpen, "手牌信息面板的判断必须先于征募面板的呈现");

        // 文案与状态只在模型里：图形版不写"已收起"字面量、不给 IsCollapsed 赋值。
        Assert.DoesNotContain(godot, f => f.Text.Contains("已收起", StringComparison.Ordinal));
        Assert.DoesNotContain(godot, f => Regex.IsMatch(f.Text, @"IsCollapsed\s*=(?!=)"));

        // GameRoot：V 键、"收起"按钮与点提示条走同一个入口，入口里先同步阶段再切换；切换语义不在引擎侧复写。
        Assert.Single(Regex.Matches(root, @"_recruitCollapse\.Toggle\("));
        Assert.True(Regex.Matches(root, @"ToggleRecruitCollapse\b").Count >= 3, "V 键与 HUD 事件都应转到 ToggleRecruitCollapse");
        Assert.Contains("RecruitPhaseKey.Of(", root, StringComparison.Ordinal);
        Assert.Contains("InputBindings.RecruitCollapseAction", root, StringComparison.Ordinal);

        // 收起期间推屏 / 回家 / 全局预览照常：相机键在 _Input 里认领，那里不看收起状态。
        int input = root.IndexOf("public override void _Input(", StringComparison.Ordinal);
        int unhandled = root.IndexOf("public override void _UnhandledInput(", StringComparison.Ordinal);
        Assert.True(input > 0 && unhandled > input);
        Assert.DoesNotMatch(CollapseTokens, root[input..unhandled]);
    }

    [Fact]
    public void V键与手柄键不与既有按键冲突()
    {
        string bindings = Godot("InputBindings.cs");
        int install = bindings.IndexOf("public void Install()", StringComparison.Ordinal);
        Assert.True(install > 0);
        string body = bindings[install..];

        string[] keys = [.. Regex.Matches(body, @"Key\.(\w+)").Select(m => m.Groups[1].Value)];
        string[] pads = [.. Regex.Matches(body, @"JoyButton\.(\w+)").Select(m => m.Groups[1].Value)];
        Assert.True(keys.Length >= 15, $"只扫到 {keys.Length} 个键");
        Assert.True(pads.Length >= 10, $"只扫到 {pads.Length} 个手柄键");
        Assert.Contains("V", keys);
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal(pads.Length, pads.Distinct().Count());

        // 收起动作注册了 V 与一个手柄键。
        System.Text.RegularExpressions.Match line = Regex.Match(body, @"Register\(RecruitCollapseAction,[^;]*;");
        Assert.True(line.Success);
        Assert.Contains("Key.V", line.Value, StringComparison.Ordinal);
        Assert.Contains("JoyButton.", line.Value, StringComparison.Ordinal);

        // F12 截图键在 GameRoot 里直接判断，也不得与 V 相撞；V 在全部脚本里只注册这一次。
        Assert.Single(Sources(Path.Combine("godot", "scripts")).SelectMany(f => Regex.Matches(f.Text, @"Key\.V\b")));
    }

    [Fact]
    public void 提示条不遮挡左侧信息层面板与顶部条()
    {
        // 画布 stretch=canvas_items + aspect=expand，基准 1600×900：画布宽恒 ≥ 1600，中心 x ≥ 800。
        string hud = Godot("Hud.cs");
        static (float AnchorX, float Left, float Top, float Right, float Bottom) AnchorOf(string text, string target)
        {
            System.Text.RegularExpressions.Match m = Regex.Match(text, @"Ui\.Anchor\(" + Regex.Escape(target) + @",\s*([\d.]+)f,\s*([\d.]+)f,\s*(-?[\d.]+)f,\s*(-?[\d.]+)f,\s*(-?[\d.]+)f,\s*(-?[\d.]+)f\)");
            Assert.True(m.Success, $"找不到 {target} 的 Ui.Anchor");
            float F(int i) => float.Parse(m.Groups[i].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(0f, F(2));   // 都贴顶
            return (F(1), F(3), F(4), F(5), F(6));
        }

        var bar = AnchorOf(hud, "_recruitBar");
        var notice = AnchorOf(hud, "_notice");
        Assert.Equal(0.5f, bar.AnchorX);
        Assert.Equal(0.5f, notice.AnchorX);

        // 顶部中央的行动顺序条（BuildOrderBar）。
        int orderAt = hud.IndexOf("private void BuildOrderBar()", StringComparison.Ordinal);
        Assert.True(orderAt > 0);
        System.Text.RegularExpressions.Match orderAnchor = Regex.Match(hud[orderAt..], @"Ui\.Anchor\(panel,\s*0\.5f,\s*0f,\s*-?[\d.]+f,\s*[\d.]+f,\s*[\d.]+f,\s*([\d.]+)f\)");
        Assert.True(orderAnchor.Success);
        float orderBottom = float.Parse(orderAnchor.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        // 左侧信息层面板（BuildLayerPanel，贴左）与右上排名面板（贴右，宽 300）。
        int layer = hud.IndexOf("private void BuildLayerPanel()", StringComparison.Ordinal);
        System.Text.RegularExpressions.Match layerAnchor = Regex.Match(hud[layer..], @"Ui\.Anchor\(panel,\s*0f,\s*0f,\s*[\d.]+f,\s*([\d.]+)f,\s*([\d.]+)f,\s*([\d.]+)f\)");
        Assert.True(layerAnchor.Success);
        float layerRight = float.Parse(layerAnchor.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(layerRight >= 300f);

        const float half = 800f;
        Assert.True(half + bar.Left > layerRight, $"提示条左缘 {half + bar.Left} 压到信息层面板右缘 {layerRight}");
        Assert.True(half + bar.Right < 1600f - 300f, "提示条右缘压到势力排名面板");
        Assert.True(bar.Top >= notice.Bottom && bar.Top >= orderBottom, "提示条压到顶部顺序条 / 通知条");
        Assert.True(bar.Bottom - bar.Top <= 48f, "提示条应是一条窄条");
    }
}
