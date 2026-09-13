using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.RelicControlSpec;

/// <summary>规格：relic-control —— Requirement: 信物控制判定</summary>
public class 信物控制判定Tests
{
    [Fact]
    public void 占据即控制()
    {
        // 设计文档 §7.3：P0 的棋子位于信物格 E7 上，P1 两枚棋子（E8、D7）覆盖该格 → P0 控制，不进入争议。
        // 变异验证 M-C7：RecalculateCore 对 Occupied 也按覆盖者数量判争议 → 红 1（本测试）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("E7", TestMaps.P0).Place("E8", TestMaps.P1).Place("D7", TestMaps.P1);

        ledger.Settle(board, 1);

        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P0), ledger.ControlOf(TestMaps.At("E7")));
        Assert.Equal(4, ledger.SnapshotFor(TestMaps.P0, board, 0, 1).DeployLimit);
        Assert.Equal(3, ledger.SnapshotFor(TestMaps.P1, board, 0, 1).DeployLimit);
    }

    [Fact]
    public void 唯一覆盖即控制()
    {
        // E7 为空且只有 P1 的棋子（F7）覆盖它 → P1 控制。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Conscription()));
        board.Place("F7", TestMaps.P1);

        ledger.Settle(board, 1);

        Assert.Equal(new RelicControl(RelicControlKind.Controlled, TestMaps.P1), ledger.ControlOf(TestMaps.At("E7")));
        Assert.Equal(4, ledger.SnapshotFor(TestMaps.P1, board, 0, 1).FreePickCount);
    }

    [Fact]
    public void 多人覆盖即争议()
    {
        // E7 为空且同时被 P0（D7）与 P2（F7）覆盖 → 争议，两人都不获得效果。
        // 变异验证 M-C8：RecalculateCore 把 Contested 映射成「覆盖者中编号最小者控制」→ 红 3，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Prospecting()));
        board.Place("D7", TestMaps.P0).Place("F7", RelicFixtures.P2);

        ledger.Settle(board, 1);

        Assert.Equal(RelicControl.Contested, ledger.ControlOf(TestMaps.At("E7")));
        Assert.Equal(5, ledger.SnapshotFor(TestMaps.P0, board, 0, 1).RevealCount);
        Assert.Equal(5, ledger.SnapshotFor(RelicFixtures.P2, board, 0, 1).RevealCount);
    }

    [Fact]
    public void 无人覆盖即无人控制()
    {
        // E7 为空且四周无任何棋子（远处 A1 有 P0 的棋子）→ 无人控制。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("A1", TestMaps.P0);

        ledger.Settle(board, 1);

        Assert.Equal(RelicControl.Uncontrolled, ledger.ControlOf(TestMaps.At("E7")));
        Assert.False(ledger.IsRevealed(TestMaps.At("E7")));
        Assert.Equal(3, ledger.SnapshotFor(TestMaps.P0, board, 0, 1).DeployLimit);
    }

    [Fact]
    public void 控制判定与领地层覆盖判定交叉一致()
    {
        // 规格：控制判定 SHALL 复用 add-territory-power 的唯一覆盖查询。对 1000 个种子驱动的随机盘面（4 人基准图 14 个信物格），
        // 断言本层四态与 CoverageMap.OwnershipOf 逐格不矛盾：Occupied/Exclusive ↔ Controlled(同一人)，Contested ↔ Contested，Neutral ↔ Uncontrolled。
        // 变异验证 M-C7（占据也判争议）、M-C8（争议取编号最小者）都让本测试红。
        MapData map = FourPlayerBaseMap.Create();
        RelicGenerationRecord record = RelicGenerator.Generate(map, new GameSeed(11));
        PlayerId[] players = [TestMaps.P0, TestMaps.P1, RelicFixtures.P2, RelicFixtures.P3];
        var seen = new HashSet<RelicControlKind>();

        for (ulong seed = 0; seed < 1000; seed++)
        {
            GameBoard board = RelicFixtures.RandomBoard(GameBoard.Load(map), new GameSeed(seed).Stream("cross-check"), players);
            var ledger = new RelicLedger(record);
            ledger.RecalculateControl(board, RelicFixtures.AllActive(players));
            CoverageMap coverage = CoverageMap.Compute(board);

            foreach (Coord c in ledger.Coords)
            {
                CellOwnership ownership = coverage.OwnershipOf(c);
                RelicControl control = ledger.ControlOf(c);
                seen.Add(control.Kind);
                switch (ownership.Kind)
                {
                    case OwnershipKind.Occupied:
                    case OwnershipKind.Exclusive:
                        Assert.Equal(new RelicControl(RelicControlKind.Controlled, ownership.Owner), control);
                        Assert.True(ownership.IsControlledBy(control.Holder!.Value));
                        break;
                    case OwnershipKind.Contested:
                        Assert.Equal(RelicControl.Contested, control);
                        Assert.Null(coverage.UniqueCoverer(c));
                        break;
                    case OwnershipKind.Neutral:
                        Assert.Equal(RelicControl.Uncontrolled, control);
                        Assert.Equal(CellCoverage.None, coverage.CoverageOf(c));
                        break;
                    default:
                        Assert.Fail($"信物格 {c} 归属为 {ownership.Kind}");
                        break;
                }
            }
        }

        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void 本层不做邻接遍历()
    {
        // design.md D4 / boundaries.md「单一实现原则」：Relics/ 源码不得出现邻居遍历或第二套覆盖实现——
        // 不调用 Neighbors、Adjacency、LibertiesOf，也不手写坐标偏移；唯一允许的盘面查询入口是 CoverageMap。
        // 变异验证 M-C9：RelicLedger 里加一段 `board.Neighbors(relic.Coord)` 的自算覆盖 → 红 1（本测试）。
        string dir = Path.Combine(Determinism.随机子流隔离Tests.SourceRoot(), "src", "Siege.Core", "Relics");
        string[] files = Directory.GetFiles(dir, "*.cs");
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string token in new[] { "Neighbors(", "Adjacency", "LibertiesOf", "GroupAt(", "new Coord(", ".X ", ".Y " })
            {
                Assert.False(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} 含 {token}");
            }
        }

        Assert.Contains(files, f => File.ReadAllText(f).Contains("CoverageMap.Compute", StringComparison.Ordinal));
    }

    [Fact]
    public void 信物格为障碍时响亮失败()
    {
        // 地图数据不一致（信物格同时是障碍）在第 4 步揭示与第 5 步重算都必须抛出，不得先静默标记「已揭示」再在下一步才失败。
        // 变异验证 K-7（trellis-check）：Reveal 对 Obstacle 改为 continue → 红 1（本测试）。
        var spec = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        Coord e7 = TestMaps.At("E7");
        MapData map = TestMaps.Synthetic(size: 9, maxPlayers: 4, obstacles: [e7], relics: [KeyValuePair.Create(e7, spec)]);
        GameBoard board = GameBoard.LoadUnvalidated(map);
        var ledger = new RelicLedger(new RelicGenerationRecord(RelicFixtures.Seed, map.Id, [new RelicPlacement(e7, RelicFixtures.Command(), spec)], true, 0));

        Assert.Throws<SiegeRuleException>(() => ledger.Reveal(board, 1));
        Assert.False(ledger.IsRevealed(e7));
        Assert.Throws<SiegeRuleException>(() => ledger.RecalculateControl(board));
    }

    [Fact]
    public void 名册外玩家响亮失败()
    {
        // boundaries.md：盘面上有棋子但名册未列 → 抛出，不静默视为参赛中。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("E6", TestMaps.P0).Place("A1", TestMaps.P1);

        Assert.Throws<SiegeRuleException>(() => ledger.RecalculateControl(board, RelicFixtures.AllActive(TestMaps.P1)));
        Assert.Throws<SiegeRuleException>(() => ledger.SnapshotFor(TestMaps.P0, board, RelicFixtures.AllActive(TestMaps.P1), 0, 1));
        Assert.Throws<SiegeRuleException>(() => ledger.ReadInitiativeBonuses(board, RelicFixtures.AllActive(TestMaps.P1)));
        Assert.Throws<KeyNotFoundException>(() => ledger.ControlOf(TestMaps.At("A1")));
    }
}
