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
    public void 合法目标枚举只含几何四邻且不含自身格()
    {
        // 裁决 T-2：目标口径是几何四邻。匠人不能改自己脚下那格——四邻不含自身，
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

        // 全部目标都以落点为一端 / 就是落点的四邻。
        var neighbors = board.Neighbors(TestMaps.At("E4"));
        Assert.All(fromE4, e => Assert.All(e.Cells, c => Assert.True(c == TestMaps.At("E4") || neighbors.Contains(c))));
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
        // 穷举全盘每个落点 × 它四邻上三种动作的全部构造，两边必须逐条一致。
        GameBoard board = Board();
        int legal = 0;
        foreach (Coord cell in board.AllCoords())
        {
            var targets = TerrainEditRules.LegalTargets(board.Map, cell).ToHashSet();
            foreach (Coord n in board.AllCoords())
            {
                TerrainEdit[] candidates = n == cell
                    ? [TerrainEdit.Bridge(n), TerrainEdit.Burn(n)]
                    : [TerrainEdit.Bridge(n), TerrainEdit.Burn(n), TerrainEdit.Fence(cell, n)];
                foreach (TerrainEdit edit in candidates)
                {
                    bool inSet = targets.Contains(edit);
                    Assert.Equal(inSet, TerrainEditRules.IsLegal(board.Map, cell, edit));
                    Assert.Equal(inSet, TerrainEditRules.Reject(board.Map, cell, edit) is null);
                    legal += inSet ? 1 : 0;
                }
            }
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

        // ② 构造一个"四邻全无合法目标"的落点：四条边都已有栅栏，四邻无深水无林地。
        GameBoard boxed = TestMaps.Blank(
            TestMaps.Terrain(fences: [("E4", "E5"), ("E5", "E6"), ("D5", "E5"), ("E5", "F5")]),
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
