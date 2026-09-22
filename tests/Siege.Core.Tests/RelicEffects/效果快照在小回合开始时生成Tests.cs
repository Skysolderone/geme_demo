using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 效果快照在小回合开始时生成</summary>
public class 效果快照在小回合开始时生成Tests
{
    [Fact]
    public void 分阶段基础值只在生成快照时读取一次()
    {
        // growth-pass-1 D3 / 裁决 2：基础部署上限按"生成快照时的当前大回合"读一次，小回合内不变、跨大回合不追溯。
        // 流程上无法在小回合内跨过大回合边界：大回合只在全员行动完后推进，Debug.SetMajorRound 也要求 Idle（小回合边界）。
        // 所以用快照对象本身证明：第 3 大回合最后一位玩家的快照（基础 3）在大回合推进到 4、为下一位生成基础 4 的快照之后，仍持有 3 / 第 3 大回合；
        // 本小回合的批次上下文取的也是这份快照的值。
        // 变异验证 M-GP7（快照重读大回合：BuildSnapshot 写静态 EffectSnapshot.LastRound，DeployLimit 改为 `BaseDeployLimitFor(LastRound) + 军令加成` 现算）
        // → 全套红 14，含本测试（round3 读成 4）；该变异引入进程级静态状态，并行测试互相污染，红数不稳定（Sim 批量跑局等也会红），以"本测试红"为准。
        MatchFlow match = MatchFixtures.Started().AtRound(3, MatchFixtures.All);
        match.PassTurn();
        match.PassTurn();
        match.PassTurn();
        Assert.Equal((3, MatchFixtures.P3), (match.MajorRound, match.CurrentPlayer!.Value));

        match.BeginTurn();
        EffectSnapshot round3 = match.CurrentSnapshot!;
        Assert.Equal((3, 3), (round3.MajorRound, round3.DeployLimit));
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.Equal(3, batch.Context.DeployLimit);
        Assert.Null(batch.Stage(TestMaps.At("H8"), PieceType.Basic));
        Assert.True(match.Confirm().Confirmed);

        Assert.Equal(4, match.MajorRound);
        match.BeginTurn();
        EffectSnapshot round4 = match.CurrentSnapshot!;
        Assert.Equal((4, 4), (round4.MajorRound, round4.DeployLimit));
        Assert.Equal((3, 3), (round3.MajorRound, round3.DeployLimit));

        // 账本层：同一账本先后为第 3、第 4 大回合生成快照，先生成的那份不被后者改写
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("A1", TestMaps.P0);
        ledger.Settle(board, 3);
        EffectSnapshot early = ledger.SnapshotFor(TestMaps.P0, board, 0, 3);
        ledger.Settle(board, 7);
        EffectSnapshot late = ledger.SnapshotFor(TestMaps.P0, board, 0, 7);
        Assert.Equal((3, 5), (early.DeployLimit, late.DeployLimit));
    }

    [Fact]
    public void 名次不影响快照()
    {
        // 规格 Scenario「名次不影响快照」（restore-go-core-rules 裁决 #7 / D7）：两名玩家控制的信物完全相同（都不控制任何信物）、
        // 处于同一大回合，势力名次分别为第 1 与第 4 → 二者的快照参数完全相同。
        // 势力独立复算（四邻接，总势力 = 独占空格 + 棋串军势）：P0 A1-D1 → 4 + 5 = 9；P1 G1 H1 J1 → 3 + 4 = 7；P2 A9 B9 → 2 + 3 = 5；P3 J9 → 1 + 2 = 3。
        // 第 5 大回合的分阶段基础部署上限为 4。
        // 先红：旧实现（按名次给征募加成）下 P3 的快照为 展示 6 / 选取 4，P0 为 5 / 3 → 本测试红。
        // 变异验证 M-D1：MatchFlow.BeginTurn 生成快照后，名次 ≥ 3 者展示 / 选取各 +1 → 红 4（本测试、最后一名没有补偿、落后不获补偿、候选格上限黄金哈希）。
        MatchFlow match = MatchFixtures.Started()
            .AtRound(5, [MatchFixtures.P3, MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2])
            .Stones(MatchFixtures.P0, "A1", "B1", "C1", "D1")
            .Stones(MatchFixtures.P1, "G1", "H1", "J1")
            .Stones(MatchFixtures.P2, "A9", "B9")
            .Stones(MatchFixtures.P3, "J9");
        Assert.Equal(4, match.Scoreboard.Latest!.RankOf(MatchFixtures.P3));
        Assert.Equal(1, match.Scoreboard.Latest!.RankOf(MatchFixtures.P0));

        match.BeginTurn();
        Assert.Equal(MatchFixtures.P3, match.CurrentPlayer);
        EffectSnapshot last = match.CurrentSnapshot!;
        match.EnterRecruit();
        match.EnterDeploy();
        Assert.True(match.Confirm().Confirmed);   // P3 Pass，盘面不变

        Assert.Equal((5, MatchFixtures.P0), (match.MajorRound, match.CurrentPlayer!.Value));
        Assert.Equal(1, match.Scoreboard.Latest!.RankOf(MatchFixtures.P0));
        match.BeginTurn();
        EffectSnapshot first = match.CurrentSnapshot!;

        Assert.Equal((5, 5), (last.MajorRound, first.MajorRound));
        Assert.Equal((5, 3, 5, 4), (last.RevealCount, last.FreePickCount, last.TypeSlots, last.DeployLimit));
        Assert.Equal(
            (first.RevealCount, first.FreePickCount, first.TypeSlots, first.DeployLimit),
            (last.RevealCount, last.FreePickCount, last.TypeSlots, last.DeployLimit));
        Assert.Equal(first.EmblemCounts, last.EmblemCounts);
        Assert.Empty(last.EmblemCounts);
    }

    [Fact]
    public void 新占信物本回合不生效()
    {
        // 设计文档 §5.1：小回合开始时生成快照（部署上限 3）；本小回合的批次占领军令 E7（走真实结算驱动器）→ 已生成的快照仍为 3；下一小回合的快照为 4。
        // 变异验证 M-E6：EffectSnapshot.DeployLimit 改为持有 Func<int> 回读账本（活视图）→ 红 3，含本测试；
        // M-E7：SnapshotFor 不重算控制、直接读上次结算的 Control → 「先手玩家夺走后手信物」红（本测试不红，因为结算钩子已重算）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        var driver = new SettlementDriver(board, new BoardHistory(), new RelicHooks(ledger) { MajorRound = 1 });

        EffectSnapshot before = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);
        Assert.Equal(3, before.DeployLimit);

        Assert.True(driver.Confirm(BatchFixtures.Context(board, TestMaps.P0, before.DeployLimit), [BatchFixtures.P("E7")]).Confirmed);

        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P0), ledger.ControlOf(TestMaps.At("E7")));
        Assert.Equal(3, before.DeployLimit);
        Assert.Equal(4, ledger.SnapshotFor(TestMaps.P0, board, 0, 2).DeployLimit);
    }

    [Fact]
    public void 先手玩家夺走后手信物()
    {
        // 设计文档 §5.1：后手 P1 上一大回合控制探勘 E7（E8 唯一覆盖）。本大回合先手 P0 先行动：落 E6 使 E7 争议。
        // 轮到 P1 时生成快照 → 不含该探勘的展示数加成（5 而非 6）。
        // 这里刻意不在 P0 行动后调用 RecalculateControl，以验证 SnapshotFor 读的是「此刻盘面」而不是上次结算的缓存（M-E7 让本测试红）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Prospecting()));
        board.Place("E8", TestMaps.P1);
        ledger.Settle(board, 4);
        Assert.Equal(6, ledger.SnapshotFor(TestMaps.P1, board, 0, 4).RevealCount);

        board.Place("E6", TestMaps.P0);

        Assert.Equal(5, ledger.SnapshotFor(TestMaps.P1, board, 0, 5).RevealCount);
        Assert.Equal(5, ledger.SnapshotFor(TestMaps.P0, board, 0, 5).RevealCount);
    }

    [Fact]
    public void 快照期间丢失不影响本回合()
    {
        // P0 小回合开始时控制征召 E7（E6 唯一覆盖）→ 快照免费选取数 4；本回合结算中 P1 落 E8 使其争议 → 本回合快照仍 4；下一小回合恢复为 3。
        // 变异验证 M-E6（活视图）→ 本测试红。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Conscription()));
        board.Place("E6", TestMaps.P0);
        ledger.Settle(board, 1);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);
        Assert.Equal(4, snapshot.FreePickCount);

        board.Place("E8", TestMaps.P1);
        ledger.Settle(board, 1);
        Assert.Equal(RelicControl.Contested, ledger.ControlOf(TestMaps.At("E7")));

        Assert.Equal(4, snapshot.FreePickCount);
        Assert.Equal(3, ledger.SnapshotFor(TestMaps.P0, board, 0, 2).FreePickCount);
    }

    [Fact]
    public void 快照是不可变值对象()
    {
        // design.md D5：快照全部属性只读，且不持有账本或盘面引用；两次读取同一快照的值恒等。
        // 变异验证 M-E6（活视图）→ 本测试红（出现可写或委托类型的属性）。
        foreach (System.Reflection.PropertyInfo p in typeof(EffectSnapshot).GetProperties())
        {
            Assert.False(p.CanWrite, $"EffectSnapshot.{p.Name} 可写");
            Assert.False(typeof(Delegate).IsAssignableFrom(p.PropertyType), $"EffectSnapshot.{p.Name} 是委托");
            Assert.NotEqual(typeof(RelicLedger), p.PropertyType);
            Assert.NotEqual(typeof(GameBoard), p.PropertyType);
        }

        Assert.All(typeof(EffectSnapshot).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public),
            f => Assert.True(f.IsInitOnly, $"字段 {f.Name} 非只读"));
    }

    [Fact]
    public void 快照生成代码不引用势力名次()
    {
        // 守门（restore-go-core-rules tasks 4.1 / D7：快照 MUST NOT 读取势力名次）。三条腿：
        // ① 签名：RelicLedger.SnapshotFor 各重载与 EffectSnapshot 构造函数的参数、EffectSnapshot 的属性，类型都不得来自 Siege.Core.Scoring
        //    （名次、势力快照、势力榜都在那里）——名次想进快照只能经参数或字段，这条把两条路都堵上；
        // ② 文本：src/Siege.Core/Relics/ 下的源码不得出现名次 / 势力榜相关的词（不依赖词边界，testing.md「扫描正则不依赖词边界」）；
        // ③ 调用点：全 src/ 里每一处 SnapshotFor( 的实参不得读名次或势力榜。
        // 变异验证（段 D，各红 1 = 本测试）：M-D3 无名册重载加可选参数 `Siege.Core.Scoring.PlayerPower? power = null`（只有 ① 能抓）；
        // M-D4 RelicLedger 加 `internal static int BonusForRank(int rank)`（② 抓）；M-D5 BeginTurn 的实参改成 `held + (0 * (Scoreboard.Latest?.RankOf(player) ?? 0))`（行为不变，只有 ③ 能抓）。
        // 限度：MatchFlow 在 SnapshotFor 返回之后再按名次改写快照（M-D1）不经过这三条腿，由「名次不影响快照」等行为测试挡。
        string scoring = typeof(Siege.Core.Scoring.PowerSnapshot).Namespace!;

        System.Reflection.MethodInfo[] overloads = [.. typeof(RelicLedger).GetMethods().Where(m => m.Name == nameof(RelicLedger.SnapshotFor))];
        Assert.Equal(2, overloads.Length);
        Type[] parameterTypes =
        [
            .. overloads.SelectMany(m => m.GetParameters()).Select(p => p.ParameterType),
            .. typeof(EffectSnapshot).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType),
            .. typeof(EffectSnapshot).GetProperties().Select(p => p.PropertyType),
        ];
        Assert.All(parameterTypes, t => Assert.NotEqual(scoring, t.Namespace));

        string root = PresentationFixtures.RepoRoot();
        string[] relicFiles = Directory.GetFiles(Path.Combine(root, "src", "Siege.Core", "Relics"), "*.cs", SearchOption.AllDirectories);
        Assert.True(relicFiles.Length >= 5, $"只扫到 {relicFiles.Length} 个信物层源文件");
        const string rankWords = "(?i)rank|scoreboard|powersnapshot|standing|catchup|名次|排名";
        string[] relicHits =
        [
            .. relicFiles.SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), rankWords, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5))
                .Select(m => $"{Path.GetFileName(f)}: {m.Value}")),
        ];
        Assert.Empty(relicHits);

        string[] sources = SourceFiles(root);
        string[] calls =
        [
            .. sources.SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"\.SnapshotFor\(([^;]*);", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5))
                .Select(m => $"{Path.GetFileName(f)}: {m.Groups[1].Value}")),
        ];
        // 样本口径下界：流程层小回合开始、弃赛快照、结构参数三处调用点
        Assert.True(calls.Length >= 3, $"只找到 {calls.Length} 处 SnapshotFor 调用");
        Assert.All(calls, c => Assert.DoesNotMatch("(?i)rank|scoreboard|standing|power|catchup|名次", c));
    }

    [Fact]
    public void 名次半数阈值算式不在源码中出现()
    {
        // 守门（restore-go-core-rules tasks 4.1 / 4.2，testing.md「违禁 token 清单挡不住照抄一份算式」）：
        // 已删除的名次加成判定是 `rank > (participants + 1) / 2`（⌈n÷2⌉ 的整数写法）；另一种写法是 `rank * 2 > participants`。
        // 删掉唯一实现之后，类型名黑名单再也挡不住任何东西——照抄一份算式回来是唯一的复活路径。
        // 扫描范围：整个 src/（Core / Presentation / Sim / Godot 脚本），排除 obj / bin / .godot；期望零命中。
        // 变异验证（段 D，各红 1 = 本测试）：M-D6 Godot Hud 加 `rank > (participants + 1) / 2 ? 1 : 0`；M-D7 Core RelicLedger 加 `r * 2 > n ? 1 : 0`；
        // M-D8（只改测试）反面字面量改成 `(participants + 2) / 2` → 反面断言红。
        string[] shapes = [@"\+\s*1\s*\)\s*/\s*2", @"\*\s*2\s*>"];

        // 反面：判据确实抓得住被删掉的那两种写法，否则"零命中"只是规则失效
        Assert.Matches(shapes[0], "int reveal = rank > (participants + 1) / 2 ? 1 : 0;");
        Assert.Matches(shapes[1], "int reveal = rank * 2 > participants ? 1 : 0;");

        string root = PresentationFixtures.RepoRoot();
        string[] files = SourceFiles(root);
        // 样本口径下界：路径写错时下面的"零命中"会恒真；Godot 脚本必须在扫描范围里
        Assert.True(files.Length >= 100, $"只扫到 {files.Length} 个源文件");
        Assert.True(files.Count(f => f.Contains($"{Path.DirectorySeparatorChar}godot{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) >= 10, "Godot 脚本不在扫描范围内");

        string[] hits =
        [
            .. files.SelectMany(f => shapes.Where(s => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), s, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)))
                .Select(s => $"{Path.GetRelativePath(root, f)}: {s}")).Order(),
        ];
        Assert.Empty(hits);
    }

    private static string[] SourceFiles(string root)
    {
        char sep = Path.DirectorySeparatorChar;
        return
        [
            .. Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                    && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
                    && !f.Contains($"{sep}.godot{sep}", StringComparison.Ordinal))
                .Order(),
        ];
    }
}
