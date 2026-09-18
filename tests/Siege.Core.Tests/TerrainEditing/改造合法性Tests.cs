using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.TerrainEditing;

/// <summary>
/// 规格：terrain-edit —— Requirement: 匠人落子即改造 / 改造动作集（没有逆向动作）/ 批次内不链式；
/// batch-deployment —— Requirement: 批次数量与库存约束（改造不额外占额度）。
/// tasks 2.2 / 2.3。
/// </summary>
public class 改造合法性Tests
{
    // 9×9：D4 / D5 深水（D5 已预置桥），F4 林地，G6-H6 预置栅栏。
    private static GameBoard Board() => TestMaps.Blank(
        TestMaps.Terrain(
            surfaces: [("D4", Surface.DeepWater), ("D5", Surface.DeepWater)],
            bridges: ["D5"],
            fences: [("G6", "H6")]),
        size: 9);

    private static GameBoard Forested() => TestMaps.Blank(
        TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater), ("F4", Surface.Forest)]),
        size: 9);

    private static StagedBatch Batch(GameBoard board, int limit = 3) =>
        new(board, BatchFixtures.Context(board, TestMaps.P0, limit));

    [Fact]
    public void 格目标只含几何四邻且不含自身格()
    {
        // 裁决 T-2：格目标（搭桥、烧林）口径是几何四邻。匠人不能改自己脚下那格——四邻不含自身，
        // 因此"要烧哪片林，先站到它旁边"，落点选择本身有意义。
        GameBoard board = Forested();

        // 站在 F4（林地）自己身上：能对四邻立栅，但 MUST NOT 出现"烧 F4"。
        Assert.DoesNotContain(TerrainEdit.Burn(TestMaps.At("F4")), TerrainEditRules.LegalTargets(board.Map, TestMaps.At("F4")));

        // 站在 E4：F4 与 D4 都是四邻 → 可烧 F4、可搭桥 D4。
        var fromE4 = TerrainEditRules.LegalTargets(board.Map, TestMaps.At("E4"));
        Assert.Contains(TerrainEdit.Burn(TestMaps.At("F4")), fromE4);
        Assert.Contains(TerrainEdit.Bridge(TestMaps.At("D4")), fromE4);

        // 站在 B7（隔着老远）：F4 与 D4 都不是四邻 → 一个都不在合法集合里；也不能给不相邻的两格之间立栅。
        var fromB7 = TerrainEditRules.LegalTargets(board.Map, TestMaps.At("B7"));
        Assert.DoesNotContain(TerrainEdit.Burn(TestMaps.At("F4")), fromB7);
        Assert.DoesNotContain(TerrainEdit.Bridge(TestMaps.At("D4")), fromB7);
        Assert.DoesNotContain(TerrainEdit.Fence(TestMaps.At("E4"), TestMaps.At("F4")), fromB7);

        // 格目标必须就是落点的四邻；边目标（T-11）只要求至少一端是四邻格。
        var neighbors = board.Neighbors(TestMaps.At("E4"));
        foreach (TerrainEdit edit in fromE4)
        {
            if (edit.Kind == TerrainEditKind.Fence)
            {
                Assert.True(neighbors.Contains(edit.Edge.A) || neighbors.Contains(edit.Edge.B), edit.ToString());
            }
            else
            {
                Assert.Contains(edit.Cell, neighbors);
            }
        }
    }

    [Fact]
    public void 边目标可落在外圈但不得更远()
    {
        // 裁决 T-11（design D-B′）：立栅的边只要求至少一端是匠人落点的几何四邻格。
        // F6 的四邻是 F5 / E6 / G6 / F7；F7-F8 的一端 F7 是四邻 → 合法；F8-F9 两端都不是 → 非法。
        GameBoard board = TestMaps.Blank(size: 9);
        Coord at = TestMaps.At("F6");
        var targets = TerrainEditRules.LegalTargets(board.Map, at);

        Assert.Contains(TerrainEdit.Fence(TestMaps.At("F7"), TestMaps.At("F8")), targets);
        Assert.Contains(TerrainEdit.Fence(TestMaps.At("F6"), TestMaps.At("F7")), targets);   // 原口径的内圈边仍在
        Assert.Contains(TerrainEdit.Fence(TestMaps.At("E7"), TestMaps.At("F7")), targets);   // 外圈的横向边
        Assert.DoesNotContain(TerrainEdit.Fence(TestMaps.At("F8"), TestMaps.At("F9")), targets);

        Assert.Null(TerrainEditRules.Reject(board.Map, at, TerrainEdit.Fence(TestMaps.At("F7"), TestMaps.At("F8"))));
        Assert.Contains(
            "两端都不是匠人落点的几何四邻格",
            TerrainEditRules.Reject(board.Map, at, TerrainEdit.Fence(TestMaps.At("F8"), TestMaps.At("F9")))!);

        // 候选边由 4 条增至 16 条（棋盘中央、无预置栅栏）：4 条内圈 + 12 条外圈。
        var fences = targets.Where(e => e.Kind == TerrainEditKind.Fence).ToList();
        Assert.Equal(16, fences.Count);
        Assert.Equal(16, fences.Distinct().Count());
        Assert.Equal(4, fences.Count(e => e.Edge.A == at || e.Edge.B == at));

        // 边的两端本身仍必须几何相邻，且都在棋盘内。
        Assert.All(fences, e => Assert.True(Adjacency.AreAdjacent(e.Edge.A, e.Edge.B)));
        Assert.Contains("不是几何四邻", TerrainEditRules.Reject(board.Map, at, TerrainEdit.Fence(TestMaps.At("F7"), TestMaps.At("G8")))!);

        // 贴边落点：越界方向的边不进候选（A1 只有 A2 / B1 两个四邻）。
        Assert.All(
            TerrainEditRules.LegalTargets(board.Map, TestMaps.At("A1")).Where(e => e.Kind == TerrainEditKind.Fence),
            e => Assert.All(e.Cells, c => Assert.True(board.Map.Contains(c))));
    }

    [Fact]
    public void 边目标不得离得更远时整批非法()
    {
        // 规格场景「边目标不得离得更远」走到批次层：拒绝理由与失败类别都要说清楚。
        GameBoard board = TestMaps.Blank(size: 9);
        StagedBatch batch = Batch(board);

        BatchFailure? failure = batch.Stage(
            TestMaps.At("F6"), PieceType.Artisan, TerrainEdit.Fence(TestMaps.At("F8"), TestMaps.At("F9")));

        Assert.Equal(BatchFailureKind.TerrainEditIllegal, failure!.Kind);
        Assert.Contains("两端都不是匠人落点的几何四邻格", failure.Message);
        Assert.Empty(batch.Placements);

        // 同一落点换成外圈边即合法。
        Assert.Null(batch.Stage(TestMaps.At("F6"), PieceType.Artisan, TerrainEdit.Fence(TestMaps.At("F7"), TestMaps.At("F8"))));
    }

    [Fact]
    public void 已被改造过的目标不在合法集合里()
    {
        // R-5：已架桥的深水格、已有栅栏的边、已是草地的格都不是合法目标（"没有逆向动作"的自然推论）。
        GameBoard board = Board();

        // D5 已预置桥；C5 在它旁边。
        Assert.DoesNotContain(TerrainEdit.Bridge(TestMaps.At("D5")), TerrainEditRules.LegalTargets(board.Map, TestMaps.At("C5")));
        Assert.Contains("已架桥", TerrainEditRules.Reject(board.Map, TestMaps.At("C5"), TerrainEdit.Bridge(TestMaps.At("D5")))!);

        // G6-H6 已预置栅栏。
        Assert.DoesNotContain(TerrainEdit.Fence(TestMaps.At("G6"), TestMaps.At("H6")), TerrainEditRules.LegalTargets(board.Map, TestMaps.At("G6")));
        Assert.Contains("已有栅栏", TerrainEditRules.Reject(board.Map, TestMaps.At("G6"), TerrainEdit.Fence(TestMaps.At("H6"), TestMaps.At("G6")))!);

        // 草地不是林地，烧不了。
        Assert.Contains("林地", TerrainEditRules.Reject(board.Map, TestMaps.At("B2"), TerrainEdit.Burn(TestMaps.At("B3")))!);
    }

    [Fact]
    public void 拒绝理由与合法目标集合一致()
    {
        // 守门：Reject 与 LegalTargets 是同一个判定的两个出口，口径 MUST NOT 分歧。
        // 穷举全盘每个落点 × 全盘每个格目标 × 全盘每一条几何边（不止以落点为端的那些，T-11 后外圈边必须进穷举）。
        GameBoard board = Board();

        // 全盘所有几何相邻的边，外加若干"两端不相邻 / 出界"的坏边——Reject 必须逐条与集合一致。
        var edges = new List<TerrainEdit>();
        foreach (Coord a in board.AllCoords())
        {
            foreach (Coord b in board.AllCoords())
            {
                if (a < b && Adjacency.AreAdjacent(a, b))
                {
                    edges.Add(TerrainEdit.Fence(a, b));
                }
            }
        }

        edges.Add(TerrainEdit.Fence(TestMaps.At("E4"), TestMaps.At("F5")));   // 斜向，两端不相邻
        edges.Add(TerrainEdit.Fence(TestMaps.At("A1"), TestMaps.At("H8")));   // 隔着老远

        int legal = 0;
        foreach (Coord cell in board.AllCoords())
        {
            var targets = TerrainEditRules.LegalTargets(board.Map, cell).ToHashSet();
            var candidates = new List<TerrainEdit>(edges);
            foreach (Coord n in board.AllCoords())
            {
                candidates.Add(TerrainEdit.Bridge(n));
                candidates.Add(TerrainEdit.Burn(n));
            }

            foreach (TerrainEdit edit in candidates)
            {
                bool inSet = targets.Contains(edit);
                Assert.Equal(inSet, TerrainEditRules.IsLegal(board.Map, cell, edit));
                Assert.Equal(inSet, TerrainEditRules.Reject(board.Map, cell, edit) is null);
                legal += inSet ? 1 : 0;
            }

            // 穷举确实覆盖了该落点的全部合法目标（否则"一致"只在被枚举到的子集上成立）。
            Assert.Empty(targets.Except(candidates));
        }

        // 样本口径下界：这盘面确实存在合法目标，不是"两边都恒为空"的假绿。
        Assert.True(legal > 0, "样本里一个合法目标都没有，这条守门什么都没证明。");
    }

    [Fact]
    public void 非匠人不得改造()
    {
        // terrain-edit「非匠人不得改造」：其余五种棋子携带改造目标的批次 MUST 非法。
        GameBoard board = Forested();
        StagedBatch batch = Batch(board);

        BatchFailure? failure = batch.Stage(TestMaps.At("E4"), PieceType.Fortress, TerrainEdit.Burn(TestMaps.At("F4")));

        Assert.Equal(BatchFailureKind.TerrainEditIllegal, failure!.Kind);
        Assert.Contains("只有匠人能改造", failure.Message);
        Assert.Contains("堡垒子", failure.Message);
        Assert.Empty(batch.Placements);

        // 同一落点同一目标换成匠人即合法。
        Assert.Null(batch.Stage(TestMaps.At("E4"), PieceType.Artisan, TerrainEdit.Burn(TestMaps.At("F4"))));
    }

    [Fact]
    public void 目标必须相邻()
    {
        GameBoard board = Forested();
        StagedBatch batch = Batch(board);

        BatchFailure? failure = batch.Stage(TestMaps.At("B2"), PieceType.Artisan, TerrainEdit.Burn(TestMaps.At("F4")));

        Assert.Equal(BatchFailureKind.TerrainEditIllegal, failure!.Kind);
        Assert.Contains("不是几何四邻", failure.Message);
        Assert.Equal(["B2", "F4"], failure.Coords.Order().Notations());
    }

    [Fact]
    public void 匠人不带目标与无目标可改时仍可落子()
    {
        // 裁决 T-6：改造可选。① 不带目标按普通棋子处理、地形不变；
        // ② 四周没有任何合法目标时落点仍然合法。
        GameBoard board = Board();
        StagedBatch batch = Batch(board);
        Assert.Null(batch.Stage(TestMaps.At("B2"), PieceType.Artisan));

        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(driver.Confirm(batch.Context, batch.Placements).Confirmed);
        Assert.Empty(board.TerrainEdits);

        // ② 构造一个"全无合法目标"的落点：T-11 放宽后要把 16 条候选边（4 内圈 + 12 外圈）全部预置栅栏，
        //    四邻也无深水无林地。少封一条就有合法目标，这一条会红。
        GameBoard boxed = TestMaps.Blank(
            TestMaps.Terrain(fences:
            [
                ("E4", "E3"), ("E4", "D4"), ("E4", "F4"), ("E4", "E5"),
                ("D5", "D4"), ("D5", "C5"), ("D5", "E5"), ("D5", "D6"),
                ("F5", "F4"), ("F5", "E5"), ("F5", "G5"), ("F5", "F6"),
                ("E6", "E5"), ("E6", "D6"), ("E6", "F6"), ("E6", "E7"),
            ]),
            size: 9);
        Assert.Empty(TerrainEditRules.LegalTargets(boxed.Map, TestMaps.At("E5")));
        Assert.Null(Batch(boxed).Stage(TestMaps.At("E5"), PieceType.Artisan));
    }

    [Fact]
    public void 同一批次内同一目标只能被改造一次()
    {
        // D-D「批次内不链式」：同一目标批内唯一。E4 与 E6 都与 E5 相邻，都想给 E5-? 立栅时用同一条边即重复。
        GameBoard board = Forested();
        StagedBatch batch = Batch(board);
        Assert.Null(batch.Stage(TestMaps.At("E4"), PieceType.Artisan, TerrainEdit.Fence(TestMaps.At("E4"), TestMaps.At("E5"))));

        // 另一枚匠人落在 E5，指定同一条边（两端顺序相反）——FenceEdge 归一后是同一个目标。
        BatchFailure? failure = batch.Stage(TestMaps.At("E5"), PieceType.Artisan, TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("E4")));

        Assert.Equal(BatchFailureKind.DuplicateEditInBatch, failure!.Kind);
        Assert.Contains("重复的改造目标", failure.Message);
        Assert.Single(batch.Placements);
    }

    [Fact]
    public void 当批不能站上新桥也不能拿它当跳板()
    {
        // D-D：全部合法性按批次开始前的地形判定。本批新架的桥 MUST NOT 在同一批次内作为落点或改造目标。
        GameBoard board = Forested();
        StagedBatch batch = Batch(board);
        Assert.Null(batch.Stage(TestMaps.At("C4"), PieceType.Artisan, TerrainEdit.Bridge(TestMaps.At("D4"))));

        // ① 不能往本批新架的桥上落子。
        Assert.Equal(BatchFailureKind.Unplayable, batch.Stage(TestMaps.At("D4"), PieceType.Basic)!.Kind);

        // ② 也不能把它当作下一次改造的落点（同样因为此刻仍不可落子）。
        Assert.Equal(
            BatchFailureKind.Unplayable,
            batch.Stage(TestMaps.At("D4"), PieceType.Artisan, TerrainEdit.Fence(TestMaps.At("D4"), TestMaps.At("D3")))!.Kind);

        // ③ 下一批次可以：结算后 D4 成为合法落点。
        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(driver.Confirm(batch.Context, batch.Placements).Confirmed);
        Assert.Null(Batch(board).Stage(TestMaps.At("D4"), PieceType.Basic));

        // ④「下一批次可以使用」的后半句：也可以以新桥为起点继续向外架桥（T-4 宽河分批次逐格架）。
        // 需要一条两格宽的河，Forested() 只有一格，另起一份地形。
        GameBoard river = TestMaps.Blank(
            TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater), ("E4", Surface.DeepWater)]), size: 9);
        StagedBatch first = Batch(river);
        Assert.Null(first.Stage(TestMaps.At("C4"), PieceType.Artisan, TerrainEdit.Bridge(TestMaps.At("D4"))));

        // 本批次里 E4 还不是 D4 的合法跳板——匠人根本站不上 D4。
        Assert.Equal(BatchFailureKind.Unplayable, first.Stage(TestMaps.At("D4"), PieceType.Artisan, TerrainEdit.Bridge(TestMaps.At("E4")))!.Kind);

        Assert.True(BatchFixtures.Driver(river).Confirm(first.Context, first.Placements).Confirmed);
        Assert.Null(Batch(river).Stage(TestMaps.At("D4"), PieceType.Artisan, TerrainEdit.Bridge(TestMaps.At("E4"))));
    }

    [Fact]
    public void 改造不额外占额度()
    {
        // batch-deployment「改造不额外占额度」：上限 3 时三枚带改造的匠人合法，已用额度为 3。
        GameBoard board = TestMaps.Blank(
            TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater), ("F4", Surface.Forest)]),
            size: 9);
        StagedBatch batch = Batch(board, limit: 3);

        Assert.Null(batch.Stage(TestMaps.At("C4"), PieceType.Artisan, TerrainEdit.Bridge(TestMaps.At("D4"))));
        Assert.Null(batch.Stage(TestMaps.At("E4"), PieceType.Artisan, TerrainEdit.Burn(TestMaps.At("F4"))));
        Assert.Null(batch.Stage(TestMaps.At("B7"), PieceType.Artisan, TerrainEdit.Fence(TestMaps.At("B7"), TestMaps.At("B8"))));

        Assert.Equal(3, batch.Count);
        Assert.All(batch.Placements, p => Assert.NotNull(p.Edit));

        // 第 4 枚（哪怕不带改造）仍然超上限：改造与匠人共用同一枚额度，不另计也不豁免。
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, batch.Stage(TestMaps.At("H8"), PieceType.Basic)!.Kind);
    }

    [Fact]
    public void 改造目标随暂放留在批次里()
    {
        // Placement.ToString 含改造：AI 候选去重的键就是拼它，漏了会把"带 / 不带改造"的两个候选静默合并。
        GameBoard board = Forested();
        StagedBatch batch = Batch(board);
        batch.Stage(TestMaps.At("E4"), PieceType.Artisan, TerrainEdit.Burn(TestMaps.At("F4")));

        Assert.Equal(TerrainEdit.Burn(TestMaps.At("F4")), batch.Placements[0].Edit);
        Assert.Equal("E4:Artisan+X:F4", batch.Placements[0].ToString());
        Assert.NotEqual(new Placement(TestMaps.At("E4"), PieceType.Artisan).ToString(), batch.Placements[0].ToString());
    }

    [Fact]
    public void 改造记法往返()
    {
        // 盘面序列化的改造段、日志的目标字段与失败文案共用这一份记法，反向解析在 Parse。
        TerrainEdit[] all =
        [
            TerrainEdit.Bridge(TestMaps.At("D4")),
            TerrainEdit.Burn(TestMaps.At("F4")),
            TerrainEdit.Fence(TestMaps.At("G6"), TestMaps.At("H6")),
        ];

        Assert.Equal(["B:D4", "X:F4", "F:G6-H6"], all.Select(e => e.ToString()));
        Assert.Equal(all, all.Select(e => TerrainEdit.Parse(e.ToString())));

        // 边的两端顺序不影响记法与相等性（FenceEdge 归一）。
        Assert.Equal(TerrainEdit.Fence(TestMaps.At("G6"), TestMaps.At("H6")), TerrainEdit.Fence(TestMaps.At("H6"), TestMaps.At("G6")));
        Assert.Throws<FormatException>(() => TerrainEdit.Parse("Z:D4"));
        Assert.Throws<FormatException>(() => TerrainEdit.Parse("F:D4"));
    }
}
