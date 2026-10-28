using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Camera;
using Siege.Presentation.Style;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// 规格：tactical-layers「单子禁手的标示」的呈现部分（forbidden-marks D4 / D6，tasks 2.1 / 2.2）：
/// 默认棋盘格视图的阻断类别、标记手法、指向时的原因文案，以及"只在轮到本人时显示"。
/// retire-legacy-rules 1.6：流程夹具为平坦 8×8 链接载体上四人同锁棋盘 0，坐标由原 9×9 逐用例映射（共用盘面 <c>Surrounded</c> 为 A 列 → C、其余列 +1、行 +1）。
/// </summary>
public partial class 单子禁手的标示Tests
{
    private static readonly PlacementBlock[] Crossed =
        [PlacementBlock.LifeForbidden, PlacementBlock.BreaksLife, PlacementBlock.Suicide, PlacementBlock.Superko];

    [Fact]
    public void 自杀点事先打叉()
    {
        // 规格 Scenario：轮到 P0，空格 F6 四邻全是 P1 的棋子且各有不止一口气 → F6 在 P0 的默认棋盘上打叉，指向它可得知"自杀手"。
        // 变异 M-V1（ViewerWorld.Board 不叠加本人的禁手）→ 红 7；M-V7（格视图的阻断类别不看单子禁手）→ 红 7；
        // M-V5（自杀手改用同形的手法）→ 红 2；M-V8（悬停读数只认活棋禁入）→ 红 4。本测试均在其中。
        MatchFlow match = Surrounded();
        DefaultBoardView board = match.World(P0).Board();

        BoardCellView cell = board.CellAt(TestMaps.At("F6"));
        Assert.Equal(PlacementBlock.Suicide, cell.Block);
        Assert.Equal(ForbiddenMoveKind.Suicide, cell.Forbidden!.Kind);
        Assert.Equal(Terrain.Playable, cell.Terrain);
        Assert.Null(cell.Occupant);
        Assert.Equal(PlacementMarkStyle.SuicideCross, PlacementBlocks.StyleOf(cell.Block));
        Assert.True(PlacementBlocks.PartsOf(PlacementBlocks.StyleOf(cell.Block)).HasFlag(PlacementMarkParts.Cross));
        Assert.Equal("F6 · 自杀手：落下后无气", HoverReadout.Of(TestMaps.At("F6"), board));

        // 其余格没有标记；F6 仍在合法落子范围里（范围契约只扣禁入格）——它与"超出合法落子范围"是两回事。
        // map-expand-25 段 B：座位视图含整张图（画布留白的障碍格也在，它们按地形阻断），这里只看可落子格。
        Assert.Equal(["F6"], board.Cells.Where(c => c.Terrain == Terrain.Playable && c.Block != PlacementBlock.None).Select(c => c.Coord).Notations());
        Assert.Contains(TestMaps.At("F6"), match.LegalRangeFor(P0));
        Assert.Equal("C6", HoverReadout.Of(TestMaps.At("C6"), board));
    }

    [Fact]
    public void 能提子的点不是自杀点()
    {
        // 规格 Scenario：F5 只剩 F6 这一口气 → F6 不标为自杀手。
        MatchFlow match = Surrounded().Stones(P0, "E5", "G5", "F4");
        DefaultBoardView board = match.World(P0).Board();

        Assert.Equal(PlacementBlock.None, board.CellAt(TestMaps.At("F6")).Block);
        Assert.Null(board.CellAt(TestMaps.At("F6")).Forbidden);
        Assert.Equal("F6", HoverReadout.Of(TestMaps.At("F6"), board));
    }

    [Fact]
    public void 劫点事先打叉()
    {
        // 规格 Scenario：P0 刚在 E6 提走 P1 的 D6，P1 立刻在 D6 落子会使盘面与此前某次提交的盘面重复 → D6 在 P1 的默认棋盘上打叉，
        // 指向它可得知"同形"与重复的提交序号。全程走真实小回合：
        // 第 1 次提交 P0 落 D7（劫形成：P1 的 D6 只剩 E6 一口气）；第 2 次提交 P1 落 K3；P2、P3 Pass；下一大回合第 3 次提交 P0 落 E6 提 D6。
        MatchFlow match = AiFixtures.Round5().Stones(P0, "C6", "D5").Stones(P1, "E5", "E7", "F6", "D6");
        match.PlayTurn("D7");
        match.PlayTurn("K3");
        match.PassTurn();
        match.PassTurn();
        match.AtRound(6, [P0, P1, P2, P3]);
        SettlementOutcome taken = match.PlayTurn("E6");
        Assert.Equal(["D6"], taken.CaptureRecord!.Captured.Select(s => s.Coord).Notations());
        Assert.Equal(3, taken.CaptureRecord.Sequence);
        Assert.Equal(P1, match.CurrentPlayer);

        DefaultBoardView board = match.World(P1).Board();

        BoardCellView ko = board.CellAt(TestMaps.At("D6"));
        Assert.Equal(PlacementBlock.Superko, ko.Block);
        Assert.Equal(2, ko.Forbidden!.DuplicateOfSequence);
        Assert.Equal(PlacementMarkStyle.SuperkoCross, PlacementBlocks.StyleOf(ko.Block));
        Assert.Equal("D6 · 同形：与第 2 次提交的盘面重复", HoverReadout.Of(TestMaps.At("D6"), board));

        // 与确认时预演给出的原因一致：P1 真去落 D6，被拒的原因正是同形、序号 2。
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("D6"), PieceType.Basic));
        BatchFailure failure = match.Rehearse().Failure!;
        Assert.Equal((BatchFailureKind.Superko, (int?)2), (failure.Kind, failure.DuplicateOfSequence));
    }

    [Fact]
    public void 暂放改变禁手()
    {
        // 规格 Scenario：F6 对 P0 是自杀点；P0 先暂放 F4，使得再在 F6 落子时整批提走 F5 → F6 的叉消失。撤回后叉回来。
        MatchFlow match = Surrounded().Stones(P0, "E5", "G5");
        StagedBatch batch = match.OpenDeploy();
        Assert.Equal(PlacementBlock.Suicide, match.World(P0).Board().CellAt(TestMaps.At("F6")).Block);

        Assert.Null(batch.Stage(TestMaps.At("F4"), PieceType.Basic));
        DefaultBoardView staged = match.World(P0).Board();

        Assert.Equal(PlacementBlock.None, staged.CellAt(TestMaps.At("F6")).Block);
        Assert.DoesNotContain(staged.Cells, c => c.Forbidden is not null);
        // 暂放不进默认棋盘：F4 在默认棋盘上仍是空格（暂放由预演呈现单独叠加）。
        Assert.Null(staged.CellAt(TestMaps.At("F4")).Occupant);

        Assert.True(batch.Unstage(TestMaps.At("F4")));
        Assert.Equal(PlacementBlock.Suicide, match.World(P0).Board().CellAt(TestMaps.At("F6")).Block);
    }

    [Fact]
    public void 四类标记可区分()
    {
        // 规格 Scenario：同一盘面上同时存在活棋禁入格、破坏活形格、自杀点与劫点 → 四者的标记形状两两不同（MUST NOT 只依赖颜色）。
        // 盘面：P0 的环形活形（单格眼 F7 / H7）→ 轮到 P1 时 F7、H7 是活棋禁入（来自公开视图）。其余三类经观察者本人的禁手结果叠加；
        // 这里直接给出一份含三类的结果——真实查询里破坏活形只在暂放含改造时出现，且一出现就是全部候选格（整批判定），
        // 三类同屏只能由呈现层的输入构造（见实施报告），而本条要钉的是"任何输入下四种手法都分得开"。
        PlayerId[] order = [P1, P0, P2, P3];
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, order).Stones(P0, BatchDeployment.非法批次必须给出可定位的原因Tests.RingF7H7);
        var report = new ForbiddenMoveReport(
            P1,
            [
                new ForbiddenMove(TestMaps.At("C4"), ForbiddenMoveKind.BreaksLife, LifeOwner: P0),
                new ForbiddenMove(TestMaps.At("C7"), ForbiddenMoveKind.Suicide),
                new ForbiddenMove(TestMaps.At("D7"), ForbiddenMoveKind.Superko, DuplicateOfSequence: 7),
            ],
            CandidateCount: 3,
            FullRehearsalCount: 3);
        DefaultBoardView board = ViewerWorld.Build(
            P1, match.Publish(), match.PublishSupplement(), match.Hands.AccessFor(P1).PrivateView(), ownPreview: null, report).Board();

        Assert.Equal(PlacementBlock.LifeForbidden, board.CellAt(TestMaps.At("F7")).Block);
        Assert.Equal(PlacementBlock.BreaksLife, board.CellAt(TestMaps.At("C4")).Block);
        Assert.Equal(PlacementBlock.Suicide, board.CellAt(TestMaps.At("C7")).Block);
        Assert.Equal(PlacementBlock.Superko, board.CellAt(TestMaps.At("D7")).Block);

        // 形状通道：四种手法的图元组成两两不同，且都带叉；颜色不参与比较。
        // 变异 M-V5b（破坏活形去掉角标，与活棋禁入同形）→ 红 1（本测试）；M-V6（同形文案不带序号）→ 红 2；M-V6b（破坏活形文案不带所有者）→ 红 2。
        PlacementMarkParts[] parts = [.. Crossed.Select(b => PlacementBlocks.PartsOf(PlacementBlocks.StyleOf(b)))];
        Assert.Equal(4, parts.Distinct().Count());
        Assert.All(parts, p => Assert.True(p.HasFlag(PlacementMarkParts.Cross)));
        Assert.Equal(4, Crossed.Select(PlacementBlocks.StyleOf).Distinct().Count());

        // 设计 D4 的表：活棋禁入 = 压暗底 + 方框 + 叉；破坏活形再加角标；自杀手只有叉；同形 = 叉 + 圆环。
        Assert.Equal(PlacementMarkParts.Shade | PlacementMarkParts.Frame | PlacementMarkParts.Cross, parts[0]);
        Assert.Equal(parts[0] | PlacementMarkParts.CornerBadge, parts[1]);
        Assert.Equal(PlacementMarkParts.Cross, parts[2]);
        Assert.Equal(PlacementMarkParts.Cross | PlacementMarkParts.Ring, parts[3]);
        Assert.Equal(
            [PlacementMarkTint.OwnerFaction, PlacementMarkTint.OwnerFaction, PlacementMarkTint.Red, PlacementMarkTint.Orange],
            Crossed.Select(b => PlacementBlocks.TintOf(PlacementBlocks.StyleOf(b))));

        // 与"地形不可落子""超出合法落子范围"可区分：那两类没有任何图元。
        Assert.Equal(PlacementMarkParts.None, PlacementBlocks.PartsOf(PlacementBlocks.StyleOf(PlacementBlock.Terrain)));
        Assert.Equal(PlacementMarkParts.None, PlacementBlocks.PartsOf(PlacementBlocks.OutOfRangeStyle));

        // 原因文案：活形两类带所有者，同形带序号。
        Assert.Equal($"F7 · 活棋禁入（{Labels.Player(P0)}）", HoverReadout.Of(TestMaps.At("F7"), board));
        Assert.Equal($"C4 · 破坏活形（{Labels.Player(P0)}）", HoverReadout.Of(TestMaps.At("C4"), board));
        Assert.Equal("C7 · 自杀手：落下后无气", HoverReadout.Of(TestMaps.At("C7"), board));
        Assert.Equal("D7 · 同形：与第 7 次提交的盘面重复", HoverReadout.Of(TestMaps.At("D7"), board));
        Assert.Equal(P0, PlacementBlocks.OwnerOf(board.CellAt(TestMaps.At("C4"))));
        Assert.Equal(P0, PlacementBlocks.OwnerOf(board.CellAt(TestMaps.At("F7"))));
        Assert.Null(PlacementBlocks.OwnerOf(board.CellAt(TestMaps.At("C7"))));
    }

    [Fact]
    public void 破坏活形格带所有者()
    {
        // 真实查询给出的破坏活形：P1 暂放匠人并立栅切开 P0 的一字两眼（查询部分同名用例的盘面，这里搬到流程夹具上）。
        // 此时 P1 的默认棋盘上全部候选格都是破坏活形、所有者 P0；P0 的两个眼仍是活棋禁入（来自公开视图，二者不重叠）。
        // retire-legacy-rules 1.6：载体为平坦 8×8 链接档图（四人同锁棋盘 0），原 9×9 盘面平移 (+2, +2)：岩石 D3 / E3 / F3 / H3，P0 一排 C4–G4、眼 C3 / G3，匠人 E5 立栅 E4–F4。
        MapData plain = LinkModeFixtures.PlainMap();
        MapData map = plain with { Obstacles = plain.Obstacles.Union([TestMaps.At("D3"), TestMaps.At("E3"), TestMaps.At("F3"), TestMaps.At("H3")]) };
        MatchFlow match = LinkModeFixtures.RequireLink(
            MatchFlow.CreateUnvalidated(map, TestMaps.Seed, TestMaps.All, LinkModeFixtures.PlainOptions(MatchOptions.Immediate)));
        foreach (PlayerId p in TestMaps.All)
        {
            match.Debug.SeedHand(p, (PieceType.Artisan, 5), (PieceType.Basic, 5));
        }

        match.PlantSequentially(TestMaps.All.Select(p => (p, 0)));
        match.AtRound(5, [P1, P0, P2, P3]).Stones(P0, "C4", "D4", "E4", "F4", "G4");
        StagedBatch batch = match.OpenDeploy();
        DefaultBoardView before = match.World(P1).Board();
        Assert.Equal(["C3", "G3"], before.Cells.Where(c => c.Block == PlacementBlock.LifeForbidden).Select(c => c.Coord).Notations());
        Assert.DoesNotContain(before.Cells, c => c.Forbidden is not null);

        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Artisan, TerrainEdit.Fence(TestMaps.At("E4"), TestMaps.At("F4"))));
        Assert.Equal(BatchFailureKind.BreaksLife, match.Rehearse().Failure!.Kind);
        DefaultBoardView board = match.World(P1).Board();

        BoardCellView[] broken = [.. board.Cells.Where(c => c.Block == PlacementBlock.BreaksLife)];
        Assert.NotEmpty(broken);
        Assert.All(broken, c => Assert.Equal(P0, c.Forbidden!.LifeOwner));
        Assert.Equal($"C7 · 破坏活形（{Labels.Player(P0)}）", HoverReadout.Of(TestMaps.At("C7"), board));
        Assert.Equal(PlacementBlock.LifeForbidden, board.CellAt(TestMaps.At("C3")).Block);
        Assert.Equal(PlacementBlock.None, board.CellAt(TestMaps.At("E5")).Block);
    }

    [Fact]
    public void 别人的回合不显示()
    {
        // 规格 Scenario：轮到别的玩家（AI）行动 → 本机玩家的默认棋盘上不显示自杀手、同形与破坏活形标记。
        // J6 四邻全是 P0 的棋子：它对 P1 是自杀点，但此刻轮到 P0。P0 行动完（Pass）轮到 P1，J6 才打叉。
        // 变异 M-V2a（组装时不查"禁手属于谁"）→ 红 1；M-V2b（组装时不查"是否轮到本人"）→ 红 1；都是本测试。
        MatchFlow match = Surrounded().Stones(P0, "J5", "J7", "H6", "K6");
        Assert.Equal(P0, match.CurrentPlayer);

        ViewerWorld waiting = match.World(P1);

        Assert.Null(waiting.OwnForbidden);
        Assert.DoesNotContain(waiting.Board().Cells, c => c.Forbidden is not null);
        Assert.DoesNotContain(waiting.Board().Cells, c => Crossed.Contains(c.Block));
        Assert.Equal("J6", HoverReadout.Of(TestMaps.At("J6"), waiting.Board()));
        Assert.Equal("F6", HoverReadout.Of(TestMaps.At("F6"), waiting.Board()));

        // 当前行动玩家的禁手塞不进别人的视图：属于他人即抛出；不是本人的回合也抛出。
        ForbiddenMoveReport ofP0 = match.ForbiddenMovesOfCurrentPlayer()!;
        Assert.Equal(["F6:Suicide"], Project(ofP0));
        Assert.Throws<ArgumentException>(() => ViewerWorld.Build(
            P1, match.Publish(), match.PublishSupplement(), match.Hands.AccessFor(P1).PrivateView(), ownPreview: null, ofP0));
        Assert.Throws<ArgumentException>(() => ViewerWorld.Build(
            P1, match.Publish(), match.PublishSupplement(), match.Hands.AccessFor(P1).PrivateView(), ownPreview: null, ofP0 with { Player = P1 }));

        // 轮到 P0 时，一份标着属于 P1 的禁手同样进不了 P0 的视图（"属于谁"与"轮到谁"各查各的）。
        Assert.Throws<ArgumentException>(() => ViewerWorld.Build(
            P0, match.Publish(), match.PublishSupplement(), match.Hands.AccessFor(P0).PrivateView(), ownPreview: null, ofP0 with { Player = P1 }));

        // 从公开世界构建的默认棋盘恒无单子禁手。
        Assert.DoesNotContain(DefaultBoardView.From(match.PublicWorldOf()).Cells, c => c.Forbidden is not null);

        match.PassTurn();
        Assert.Equal(P1, match.CurrentPlayer);
        DefaultBoardView mine = match.World(P1).Board();
        Assert.Equal(["J6"], mine.Cells.Where(c => c.Forbidden is not null).Select(c => c.Coord).Notations());
        Assert.Equal(PlacementBlock.Suicide, mine.CellAt(TestMaps.At("J6")).Block);
    }

    [Fact]
    public void 没有当前行动玩家时不显示()
    {
        // 插旗阶段与终局没有当前行动玩家 → 不显示（D6）。
        MatchFlow match = LinkModeFixtures.PlainCreate();
        Assert.Null(match.CurrentPlayer);
        Assert.DoesNotContain(match.World(P0).Board().Cells, c => c.Forbidden is not null);
    }

    [Fact]
    public void 库存不影响禁手()
    {
        // 规格 Scenario：P0 手牌为空，F6 对 P0 是自杀点 → F6 仍打叉。
        MatchFlow match = Surrounded();
        match.Debug.SeedHand(P0);
        Assert.Empty(match.Hands.AccessFor(P0).PrivateView().Types);

        DefaultBoardView board = match.World(P0).Board();

        Assert.Equal(PlacementBlock.Suicide, board.CellAt(TestMaps.At("F6")).Block);
    }

    [Fact]
    public void 禁手叠加与盘面不同刻即抛出()
    {
        // 查询结果里的格必须是默认棋盘上的空可落子格：指到棋子格说明两者不是同一时刻取的，响亮失败而不是画错。
        MatchFlow match = Surrounded();
        var stale = new ForbiddenMoveReport(P0, [new ForbiddenMove(TestMaps.At("F5"), ForbiddenMoveKind.Suicide)], 1, 1);

        Assert.Throws<ArgumentException>(() => DefaultBoardView.From(match.PublicWorldOf()).WithForbidden(stale));
    }

    [Fact]
    public void 界面不自行判断禁手()
    {
        // 规格：界面 MUST NOT 自行判断气、提子、同形或活形。
        // ① 表现层程序集不调用单子禁手查询与合法性预演（只读查询结果）；② Godot 脚本只经对局流程的入口取结果，且只在轮到本机玩家时取；
        // ③ 棋盘绘制按视图模型的阻断类别与图元组成取图元。样本口径下界与反面命中一并断言。
        // 变异 M-V3（Godot BoardView 里直接引用 ForbiddenMoves）→ 红 1；M-V4（会话改成"有当前行动玩家就取"，不看是不是本机玩家）→ 红 1；都是本测试。
        string[] references =
        [
            .. IlReferences(PresentationAssembly)
                .Where(r => r.Target is System.Reflection.MethodBase)
                .Select(r => $"{r.Target.DeclaringType!.Name}.{r.Target.Name}")
                .Distinct(),
        ];
        Assert.DoesNotContain("ForbiddenMoves.Query", references);
        Assert.DoesNotContain("BatchRehearsal.Rehearse", references);
        Assert.DoesNotContain("BatchRehearsal.Settle", references);
        Assert.Contains("ForbiddenMoveReport.get_Moves", references);

        string dir = Path.Combine(RepoRoot(), "src", "godot", "scripts");
        (string Name, string Text)[] scripts = [.. Directory.GetFiles(dir, "*.cs").Order().Select(f => (Path.GetFileName(f), File.ReadAllText(f)))];
        Assert.True(scripts.Length >= 10, $"只扫到 {scripts.Length} 个脚本");
        Assert.DoesNotContain(scripts, s => s.Text.Contains("ForbiddenMoves.", StringComparison.Ordinal));

        string session = scripts.Single(s => s.Name == "MatchSession.cs").Text;
        Assert.Contains("IsMyTurn ? Match.ForbiddenMovesOfCurrentPlayer() : null", session, StringComparison.Ordinal);
        Assert.Equal(1, scripts.Sum(s => System.Text.RegularExpressions.Regex.Matches(s.Text, @"ForbiddenMovesOfCurrentPlayer\(").Count));

        string view = scripts.Single(s => s.Name == "BoardView.cs").Text;
        Assert.Contains("PlacementBlocks.PartsOf(", view, StringComparison.Ordinal);
        Assert.Contains("PlacementBlocks.TintOf(", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ForbiddenMoveKind", view, StringComparison.Ordinal);
    }
}
