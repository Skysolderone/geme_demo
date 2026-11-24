using Siege.Core.Board;
using Siege.Core.Simul;
using static Siege.Core.Tests.Simul.SimulFixtures;

namespace Siege.Core.Tests.Simul;

/// <summary>
/// openspec/specs/simultaneous-round —— Requirement: 部署校验（v0.1 §8.3；Q02、G2、G3、K3）。
/// 夹具坐标：出生岛 0 = C3–K10（核心区 E5–G7），公共平台 0 = L3–N10，出生岛 1 从 O3 起。
/// </summary>
/// <remarks>变异记录（A1 实跑）：M3 八邻改四邻（切比雪夫 → 曼哈顿）→ 红 4（同岛相邻拒绝、按整座出生岛、跨区互不影响、撤回后重新判定）；M9 本人活形格不禁入 → 红 1（活形内部格双方禁入）；M16 不拒绝带入信物 → 红 1（不能挂带入信物）。</remarks>
public sealed class 部署校验Tests
{
    /// <summary>E10 的活形：P0 的整体 D3-F3-C4-D4-E4-F4 围住两只单格眼 C3、E3（眼值 1 + 1 = 2，已确定活形）。</summary>
    internal static readonly string[] AliveWithEyesC3E3 = ["D3", "F3", "C4", "D4", "E4", "F4"];

    [Fact]
    public void 只按回合开始盘面校验()
    {
        SimulFlow flow = Started(2);
        SimulPrep a = flow.PrepFor(P0);
        SimulPrep b = flow.PrepFor(P1);
        a.Stage(Unit("E5"));
        b.Stage(Unit("E5"));
        Assert.True(a.Check().IsValid);
        Assert.True(b.Check().IsValid);
        Assert.True(a.Submit().IsValid);
        Assert.True(b.Check().IsValid);   // 别人已提交的暂放不进本人的校验
    }

    [Fact]
    public void 自杀式部署可以提交()
    {
        // E7：F7 四邻 F6 / E7 / G7 / F8 全是 P1 的单子（各自另有气），P0 落 F7 不提任何子。
        SimulFlow flow = Started(2, f => f.Debug.Stones(P1, "F6", "E7", "G7", "F8"));
        SimulBatchCheck check = flow.CheckOf(P0, "F7");
        Assert.True(check.IsValid, check.FirstReason);
        Assert.True(flow.PrepFor(P0).Submit().IsValid);
    }

    [Fact]
    public void 八邻禁落同岛相邻拒绝()
    {
        SimulFlow flow = Started(2);
        SimulBatchCheck check = flow.CheckOf(P0, "D4", "E5");
        Assert.False(check.IsValid);
        SimulBatchIssue issue = Assert.Single(check.Issues);
        Assert.Equal(SimulRejectKind.Crowded, issue.Kind);
        Assert.Equal(Coord.Parse("E5"), issue.Cell);
        Assert.Contains("相邻", issue.Message, StringComparison.Ordinal);

        // 直线相邻同样拒绝（八邻含四邻）。
        Assert.True(flow.CheckOf(P0, "D4", "D5").Has(SimulRejectKind.Crowded));
    }

    [Fact]
    public void 八邻禁落隔一格接受()
    {
        SimulFlow flow = Started(2);
        Assert.True(flow.CheckOf(P0, "D4", "F6").IsValid);
        Assert.True(flow.CheckOf(P0, "D4", "F4", "D6").IsValid);
    }

    [Fact]
    public void 八邻禁落跨区互不影响()
    {
        // 第 4 回合全图可部署：K5 在出生岛 0 的东缘，L6 在公共平台 0，二者斜邻但分属两个区（K3）。
        SimulFlow flow = Started(2, round: 4);
        Assert.Equal(0, SimulRules.ZoneOf(flow.Map, Coord.Parse("K5")));
        Assert.Equal(SimulRules.PublicZone, SimulRules.ZoneOf(flow.Map, Coord.Parse("L6")));
        Assert.True(flow.CheckOf(P0, "K5", "L6").IsValid);
        Assert.True(flow.CheckOf(P0, "K5", "L5").IsValid);

        // 对照：同在公共平台的两格斜邻 → 拒绝。
        Assert.True(flow.CheckOf(P0, "L6", "M7").Has(SimulRejectKind.Crowded));
    }

    [Fact]
    public void 八邻禁落按整座出生岛不按核心区()
    {
        // E5 在核心区 E5–G7 里，D4 不在核心区、但同在出生岛 0：仍算同一区。
        SimulFlow flow = Started(2);
        BoardPlate island = flow.Map.BirthBoard(0);
        Assert.True(island.InCore(Coord.Parse("E5")));
        Assert.False(island.InCore(Coord.Parse("D4")));
        Assert.Null(flow.Map.BirthZoneOf(Coord.Parse("D4")));
        Assert.True(flow.CheckOf(P0, "E5", "D4").Has(SimulRejectKind.Crowded));
    }

    [Fact]
    public void 八邻禁落只约束本人()
    {
        SimulFlow flow = Started(2);
        flow.Submit(P0, "E5");
        SimulPrep other = flow.PrepFor(P1);
        other.Stage(Unit("F6"));
        Assert.True(other.Check().IsValid);
        Assert.True(other.Submit().IsValid);
        SimulRoundReport report = flow.ResolveRound();
        Assert.Empty(report.Conflicts);
        Assert.Equal(P0, flow.OwnerAt("E5"));
        Assert.Equal(P1, flow.OwnerAt("F6"));
    }

    [Fact]
    public void 撤回一枚后重新判定()
    {
        SimulFlow flow = Started(2);
        SimulPrep prep = flow.PrepFor(P0);
        prep.Stage(Unit("E5"));
        prep.Stage(Unit("F6"));
        Assert.True(prep.Check().Has(SimulRejectKind.Crowded));
        Assert.False(prep.Submit().IsValid);

        prep.Unstage(Coord.Parse("F6"));
        Assert.True(prep.Check().IsValid);

        prep.Stage(Unit("F6"));
        prep.Move(Coord.Parse("F6"), Coord.Parse("G7"));
        Assert.True(prep.Check().IsValid);
    }

    [Fact]
    public void 活形内部格双方禁入()
    {
        SimulFlow flow = Started(2, f => f.Debug.Stones(P0, AliveWithEyesC3E3));
        LifeShapeReport life = LifeShapeReport.Analyze(flow.Board);
        Assert.True(SimulRules.IsLocked(life, Coord.Parse("C3")));
        Assert.True(SimulRules.IsLocked(life, Coord.Parse("E3")));

        foreach (PlayerId p in new[] { P0, P1 })
        {
            SimulBatchCheck check = flow.CheckOf(p, "C3");
            SimulBatchIssue issue = Assert.Single(check.Issues);
            Assert.Equal(SimulRejectKind.Locked, issue.Kind);
            Assert.Contains("活形内部格", issue.Message, StringComparison.Ordinal);
        }

        // 对照：活形外的空格照常可部署。
        Assert.True(flow.CheckOf(P0, "G3").IsValid);
        Assert.True(flow.CheckOf(P1, "G3").IsValid);
    }

    [Fact]
    public void 前三回合只能部署在出生岛()
    {
        SimulFlow flow = Started(2);
        Assert.True(flow.CheckOf(P0, "L5").Has(SimulRejectKind.OutOfRange));
        Assert.True(flow.CheckOf(P0, "O5").Has(SimulRejectKind.OutOfRange));   // 别人的出生岛（P0 锁在出生岛 0）

        SimulFlow later = Started(2, round: 4);
        Assert.True(later.CheckOf(P0, "L5").IsValid);
        Assert.True(later.CheckOf(P0, "O5").IsValid);
    }

    [Fact]
    public void 部署上限恒为3()
    {
        // 现有模式第 7 回合起部署上限是 5；新玩法恒为 3（Q07）。
        SimulFlow flow = Started(2, round: 7);
        SimulBatchCheck four = flow.CheckOf(P0, "C3", "E3", "G3", "J3");
        SimulBatchIssue issue = Assert.Single(four.Issues);
        Assert.Equal(SimulRejectKind.OverLimit, issue.Kind);
        Assert.True(flow.CheckOf(P0, "C3", "E3", "G3").IsValid);
        Assert.Equal(3, SimulRules.DeployLimit);
    }

    [Fact]
    public void 不能挂带入信物()
    {
        // K1：段 A 的新玩法关闭信物，带入件的挂出一律拒绝（不静默丢掉）。
        SimulFlow flow = Started(2);
        SimulPrep prep = flow.PrepFor(P0);
        prep.Stage(new Siege.Core.Batch.Placement(Coord.Parse("D4"), PieceType.Basic, AttachCarried: 0));
        SimulBatchIssue issue = Assert.Single(prep.Check().Issues);
        Assert.Equal(SimulRejectKind.RelicNotAllowed, issue.Kind);
        Assert.False(prep.Submit().IsValid);
    }

    [Fact]
    public void 改造只有匠人能带且按回合开始地形判定()
    {
        SimulFlow flow = Started(2, terrain: Forest("G5"), extra: [(PieceType.Artisan, 2)]);
        SimulPrep prep = flow.PrepFor(P0);
        prep.Stage(Unit("F5", PieceType.Basic, TerrainEdit.Burn(Coord.Parse("G5"))));
        Assert.True(prep.Check().Has(SimulRejectKind.EditNotArtisan));

        prep.ChangeType(Coord.Parse("F5"), PieceType.Artisan);
        Assert.True(prep.Check().IsValid, prep.Check().FirstReason);

        prep.SetEdit(Coord.Parse("F5"), TerrainEdit.Burn(Coord.Parse("F6")));   // F6 不是林地
        Assert.True(prep.Check().Has(SimulRejectKind.EditIllegal));
    }
}
