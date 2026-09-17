using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.SiteControlSpec;

/// <summary>规格：site-control —— Requirement: 据点控制判定（scoring-sites 2.1）</summary>
public class 据点控制判定Tests
{
    private static readonly PlayerId P2 = ScoringFixtures.P2;

    [Fact]
    public void 占据即控制()
    {
        // 规格 Scenario：A 的棋子位于某石碑格上，B 的两枚棋子覆盖该格 → 占据，由 A 控制。
        // 变异验证 M-S1（段 A2）：SiteControl 映射 Occupied 分支改为读覆盖（Contested）→ 红，含本测试。
        GameBoard board = TestMaps.Blank(size: 9).WithSites(("E5", SiteTier.Stele))
            .Place("E5", TestMaps.P0).Place("D5", TestMaps.P1).Place("F5", TestMaps.P1);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(new CellCoverage(1, TestMaps.P1), snapshot.Coverage.CoverageOf(TestMaps.At("E5")));   // 前提：B 确实覆盖该格
        Assert.Equal(new SiteState(TestMaps.At("E5"), SiteTier.Stele, SiteControlKind.Occupied, TestMaps.P0), snapshot.StateAt("E5"));
        Assert.Equal(45, snapshot.Of(TestMaps.P0).SiteScore);
        Assert.Equal(0, snapshot.Of(TestMaps.P1).SiteScore);
    }

    [Fact]
    public void 唯一覆盖即控制()
    {
        // 规格 Scenario：某篝火格为空，只有 B 的棋子覆盖它 → 唯一覆盖，由 B 控制。
        GameBoard board = TestMaps.Blank(size: 9).WithSites(("E5", SiteTier.Campfire)).Place("E4", TestMaps.P1);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(new SiteState(TestMaps.At("E5"), SiteTier.Campfire, SiteControlKind.UniqueCoverage, TestMaps.P1), snapshot.StateAt("E5"));
        Assert.Equal(new SiteHolding(TestMaps.At("E5"), SiteTier.Campfire, 15, SiteControlKind.UniqueCoverage), Assert.Single(snapshot.Of(TestMaps.P1).Sites));
    }

    [Fact]
    public void 多人覆盖即争议()
    {
        // 规格 Scenario：某营帐格为空，同时被 A 与 C 覆盖 → 争议，A 与 C 均不得分。
        // 变异验证 M-S2（段 A2）：SiteControl 把 Contested 映射成"取第一个覆盖来源为控制者"→ 红，含本测试。
        GameBoard board = TestMaps.Blank(size: 9).WithSites(("E5", SiteTier.Tent)).Place("E4", TestMaps.P0).Place("E6", P2);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(new SiteState(TestMaps.At("E5"), SiteTier.Tent, SiteControlKind.Contested, null), snapshot.StateAt("E5"));
        Assert.Equal((0L, 0L), (snapshot.Of(TestMaps.P0).SiteScore, snapshot.Of(P2).SiteScore));
        Assert.Equal((1L, 1L), (snapshot.Of(TestMaps.P0).Total, snapshot.Of(P2).Total));
    }

    [Fact]
    public void 无人覆盖即无人()
    {
        // Requirement 正文第 4 条：据点格为空且无人覆盖 → 无人，无人控制。
        GameBoard board = TestMaps.Blank(size: 9).WithSites(("E5", SiteTier.Stele)).Place("A1", TestMaps.P0);

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(new SiteState(TestMaps.At("E5"), SiteTier.Stele, SiteControlKind.Unclaimed, null), snapshot.StateAt("E5"));
        Assert.Equal(0, snapshot.Of(TestMaps.P0).SiteScore);
    }

    [Fact]
    public void 居高临下制造争议()
    {
        // 规格 Scenario：h=0 的篝火 E5 为空，低地主人 A 在同高的 D5 覆盖它，邻家 B 在与之几何相邻的 h=2 高台 E6 上 → A、B 同时覆盖，争议；
        // A 若要独得该篝火必须落子占据。覆盖用真实 CoverageTargets 断言，不手算。
        TerrainData terrain = TestMaps.Terrain(heights: [("E6", 2)]);
        GameBoard board = TestMaps.Blank(terrain, size: 9).WithSites(("E5", SiteTier.Campfire))
            .Place("D5", TestMaps.P0).Place("E6", TestMaps.P1);

        Assert.Contains(TestMaps.At("E5"), board.CoverageTargets(TestMaps.At("E6")));
        Assert.Contains(TestMaps.At("E5"), board.CoverageTargets(TestMaps.At("D5")));
        Assert.Equal(new SiteState(TestMaps.At("E5"), SiteTier.Campfire, SiteControlKind.Contested, null), PowerCalculator.Compute(board).StateAt("E5"));

        board.Place("E5", TestMaps.P0);
        PowerSnapshot occupied = PowerCalculator.Compute(board);
        Assert.Equal(new SiteState(TestMaps.At("E5"), SiteTier.Campfire, SiteControlKind.Occupied, TestMaps.P0), occupied.StateAt("E5"));
        Assert.Equal(15, occupied.Of(TestMaps.P0).SiteScore);
    }

    [Fact]
    public void 仰视无法争夺()
    {
        // 规格 Scenario：h=2 的营帐 E5 为空，只被主人 A 在同高 D5 的棋子覆盖；B 的棋子位于与之几何相邻的 h=0 格 E4 → B 不覆盖，唯一覆盖，由 A 控制。
        TerrainData terrain = TestMaps.Terrain(heights: [("E5", 2), ("D5", 2)]);
        GameBoard board = TestMaps.Blank(terrain, size: 9).WithSites(("E5", SiteTier.Tent))
            .Place("D5", TestMaps.P0).Place("E4", TestMaps.P1);

        Assert.DoesNotContain(TestMaps.At("E5"), board.CoverageTargets(TestMaps.At("E4")));
        PowerSnapshot snapshot = PowerCalculator.Compute(board);
        Assert.Equal(new SiteState(TestMaps.At("E5"), SiteTier.Tent, SiteControlKind.UniqueCoverage, TestMaps.P0), snapshot.StateAt("E5"));
        Assert.Equal((5L, 0L), (snapshot.Of(TestMaps.P0).SiteScore, snapshot.Of(TestMaps.P1).SiteScore));
    }

    [Fact]
    public void 林地据点只能占据()
    {
        // Requirement 正文：位于林地格上的据点因不接收覆盖，只能通过占据取得控制（覆盖关系的自然后果，不是特例）。
        TerrainData terrain = TestMaps.Terrain(surfaces: [("E5", Surface.Forest)]);
        GameBoard board = TestMaps.Blank(terrain, size: 9).WithSites(("E5", SiteTier.Stele)).Place("E4", TestMaps.P0);

        Assert.Equal(SiteControlKind.Unclaimed, PowerCalculator.Compute(board).StateAt("E5").Kind);

        board.Place("E5", TestMaps.P0);
        Assert.Equal(new SiteState(TestMaps.At("E5"), SiteTier.Stele, SiteControlKind.Occupied, TestMaps.P0), PowerCalculator.Compute(board).StateAt("E5"));
    }

    [Fact]
    public void 全部据点按坐标序给出()
    {
        // 快照携带地图上全部据点（含争议与无人），坐标字典序；表现层与遥测据此比对变化，不各自判定。
        GameBoard board = TestMaps.Blank(size: 9).WithSites(("H8", SiteTier.Tent), ("B2", SiteTier.Stele), ("E5", SiteTier.Campfire));

        PowerSnapshot snapshot = PowerCalculator.Compute(board);

        Assert.Equal(["B2", "E5", "H8"], snapshot.SiteStates.Select(s => s.Coord).Notations());
        Assert.All(snapshot.SiteStates, s => Assert.Equal(SiteControlKind.Unclaimed, s.Kind));
        Assert.Equal(SiteValues.Standard, snapshot.SiteValues);
    }

    [Fact]
    public void 据点控制实现只读覆盖表()
    {
        // 守门（tasks 2.1）：据点控制唯一实现 MUST 只读 CoverageMap，实现文件内不得出现邻接 / 覆盖目标 / 高度调用。
        // 正则不以词边界开头（testing.md）：`\w*Neighbors\(` 同时挡 Neighbors( 与 LibertyNeighbors(。
        // 样本下界：文件字符数 ≥ 800；反面命中：判据在 CoverageMap.cs（覆盖唯一实现）里确实命中 CoverageTargets(，且本文件确实读 OwnershipOf(。
        // 变异验证 M-S3（段 A2）：在 SiteControl.Compute 循环内注入 `_ = board.CoverageTargets(coord);` → 红 1（本测试）；
        //           M-S3b：注入 `_ = board.Map.HeightAt(coord);` → 红 1（本测试）。
        string root = PresentationFixtures.RepoRoot();
        string source = File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Scoring", "SiteControl.cs"));
        string code = string.Join('\n', source.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.True(code.Length >= 800, $"样本过小：{code.Length} 字符");

        var forbidden = new Regex(@"\w*Neighbors\s*\(|CoverageTargets\s*\(|HeightAt\s*\(");
        Assert.Empty(forbidden.Matches(code).Select(m => m.Value));
        Assert.Contains("OwnershipOf(", code, StringComparison.Ordinal);

        string coverage = File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Scoring", "CoverageMap.cs"));
        Assert.Matches(forbidden, coverage);
    }
}
