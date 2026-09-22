using System.Text.RegularExpressions;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Relics;

namespace Siege.Core.Tests.BatchDeployment;

/// <summary>规格：batch-deployment —— Requirement: 批次数量与库存约束</summary>
public class 批次数量与库存约束Tests
{
    [Fact]
    public void 超出部署上限()
    {
        // 变异验证：ValidateShape 的 `placements.Count > DeployLimit` 改成 `>=`→ 第 3 枚就被拒，本测试红 1；
        // 删掉该判断 → 本测试与「高部署上限无硬顶」红 2。
        GameBoard board = TestMaps.Blank(size: 7);
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 3));
        Assert.Null(batch.Stage(TestMaps.At("A1"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("B2"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("C3"), PieceType.Basic));

        BatchFailure? failure = batch.Stage(TestMaps.At("D4"), PieceType.Basic);

        Assert.NotNull(failure);
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, failure.Kind);
        Assert.Contains("已达部署上限", failure.Message);
        Assert.Contains("3", failure.Message);
        Assert.Equal(["D4"], failure.Coords.Notations());
        Assert.Equal(3, batch.Count);
    }

    [Fact]
    public void 超出手牌库存()
    {
        // 变异验证：ValidateShape 库存比较 `needed > stock` 改成 `needed > stock + 1` → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7);
        IReadOnlyDictionary<PieceType, int> stock = BatchFixtures.Stock((PieceType.Line, 2), (PieceType.Basic, 5));
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 5, stock: stock));
        Assert.Null(batch.Stage(TestMaps.At("A1"), PieceType.Line));
        Assert.Null(batch.Stage(TestMaps.At("B2"), PieceType.Line));

        BatchFailure? failure = batch.Stage(TestMaps.At("C3"), PieceType.Line);

        Assert.NotNull(failure);
        Assert.Equal(BatchFailureKind.InsufficientStock, failure.Kind);
        Assert.Equal(PieceType.Line, failure.StockType);
        Assert.Contains("库存不足", failure.Message);
        Assert.Contains("连珠子", failure.Message);
        Assert.Equal(["A1", "B2", "C3"], failure.Coords.Notations());
        Assert.Equal(2, batch.Count);

        // 库存缺失的类型视为 0
        Assert.Equal(BatchFailureKind.InsufficientStock, batch.Stage(TestMaps.At("C3"), PieceType.Fortress)!.Kind);
        // 其他类型仍可继续暂放
        Assert.Null(batch.Stage(TestMaps.At("C3"), PieceType.Basic));
    }

    [Fact]
    public void 高部署上限无硬顶()
    {
        // 设计文档 §8.1：军令信物把上限提高到 9，不因任何全局上限而拒绝。
        // 变异验证：在 ValidateShape 加 `Math.Min(context.DeployLimit, 3)` 硬顶 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7);
        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 9));

        for (int i = 0; i < 9; i++)
        {
            Assert.Null(batch.Stage(new Coord(i % 7, i / 7), PieceType.Basic));
        }

        Assert.Equal(9, batch.Count);
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, batch.Stage(TestMaps.At("D3"), PieceType.Basic)!.Kind);
    }

    [Fact]
    public void 额度不跨回合()
    {
        // 上限 5 只落 2 枚；下一小回合的上限由该回合快照（3）决定，未用的 3 点不结转。
        // 这是契约测试：本层不持有跨回合状态，上限只来自每回合新建的 BatchContext（design.md 接口契约）；
        // 能让它红的变异是"StagedBatch 从静态字段累加上回合剩余额度"，没有对应的实现行可改，故不做变异记录。
        GameBoard board = TestMaps.Blank(size: 7);
        SettlementDriver driver = BatchFixtures.Driver(board);
        var turn1 = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 5));
        Assert.Null(turn1.Stage(TestMaps.At("A1"), PieceType.Basic));
        Assert.Null(turn1.Stage(TestMaps.At("G7"), PieceType.Basic));
        Assert.True(driver.Confirm(turn1).Confirmed);

        var turn2 = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0, limit: 3));
        Assert.Null(turn2.Stage(TestMaps.At("B2"), PieceType.Basic));
        Assert.Null(turn2.Stage(TestMaps.At("C2"), PieceType.Basic));
        Assert.Null(turn2.Stage(TestMaps.At("D2"), PieceType.Basic));

        BatchFailure? failure = turn2.Stage(TestMaps.At("E2"), PieceType.Basic);
        Assert.NotNull(failure);
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, failure.Kind);
        Assert.Contains("最多 3 枚", failure.Message);
    }

    [Fact]
    public void 绕过暂放直接提交超量批次仍被拒绝()
    {
        // 确认路径复用同一份第 1–2 步校验（implement.md 2.2），不信任 UI 层已经拦过。
        // 变异验证：Rehearse 里删掉 ValidateShape 调用 → 本测试红 1。
        GameBoard board = TestMaps.Blank(size: 7);
        SettlementDriver driver = BatchFixtures.Driver(board);
        string before = board.Serialize();

        SettlementOutcome overLimit = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0, limit: 3),
            [BatchFixtures.P("A1"), BatchFixtures.P("B1"), BatchFixtures.P("C1"), BatchFixtures.P("D1")]);
        Assert.False(overLimit.Confirmed);
        Assert.Equal(BatchFailureKind.DeployLimitExceeded, overLimit.Failure!.Kind);

        SettlementOutcome overStock = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0, limit: 3, stock: BatchFixtures.Stock((PieceType.Fortress, 1))),
            [BatchFixtures.P("A1", PieceType.Fortress), BatchFixtures.P("B1", PieceType.Fortress)]);
        Assert.False(overStock.Confirmed);
        Assert.Equal(BatchFailureKind.InsufficientStock, overStock.Failure!.Kind);

        Assert.Equal(before, board.Serialize());
        Assert.Equal(0, driver.History.Count);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(5, 4)]
    [InlineData(8, 5)]
    public void 基础值随阶段提高(int majorRound, int expected)
    {
        // growth-pass-1 batch-deployment 规格：未控制任何军令，分别在第 3 / 5 / 8 大回合进入部署阶段 → 部署上限 3 / 4 / 5（D1/D2，裁决 1）。
        // 走完整流程：BeginTurn 生成快照 → EnterDeploy 建批次；暂放恰好 expected 枚成功，第 expected + 1 枚被拒。
        // 变异验证 M-GP2（BaseDeployLimitFor 阶段表写死 3：`<= 6 => 3`、`_ => 3`）→ 全套红 19，含本 Theory 的 5/8 两行；
        // M-GP3（RelicLedger.BuildSnapshot 基础改为 BaseDeployLimitFor(1)）→ 全套红 47，含本 Theory 的 5/8 两行。
        // M-GP1（边界整体后移一回合）本 Theory 不红（3/5/8 都不在边界上），由「基础值随阶段提高_阶段边界」的 4、7 两行抓。
        MatchFlow match = MatchFixtures.Started().AtRound(majorRound, MatchFixtures.All);

        match.BeginTurn();
        Assert.Equal(MatchFixtures.P0, match.CurrentPlayer);
        Assert.Equal((majorRound, expected), (match.CurrentSnapshot!.MajorRound, match.CurrentSnapshot.DeployLimit));
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        Assert.Equal(expected, batch.Context.DeployLimit);

        string[] cells = ["A1", "C1", "B2", "A3", "C3", "B1"];
        for (int i = 0; i < expected; i++)
        {
            Assert.Null(batch.Stage(TestMaps.At(cells[i]), PieceType.Basic));
        }

        Assert.Equal(BatchFailureKind.DeployLimitExceeded, batch.Stage(TestMaps.At(cells[expected]), PieceType.Basic)!.Kind);
        Assert.Equal(expected, batch.Count);
        Assert.True(match.Confirm().Confirmed);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(6, 4)]
    [InlineData(7, 5)]
    [InlineData(15, 5)]
    public void 基础值随阶段提高_阶段边界(int majorRound, int expected)
    {
        // growth-pass-1 design.md D2：阶段边界与构筑保护期、§16 三段对齐——第 1–3 / 4–6 / 7+ 大回合为 3 / 4 / 5。两侧边界各取一行。
        // 经效果快照生成（账本）与无信物默认快照两条路径读取，二者都只能经由 BaseDeployLimitFor。
        // 变异验证 M-GP1（`<= 3 => 3` 改 `<= 4 => 3`、`<= 6 => 4` 改 `<= 7 => 4`，即第 4 大回合仍取 3）→ 全套红 6，含本 Theory 的 4、7 两行、「分阶段基础部署上限唯一实现」、
        // 「来源可拆分」、「分阶段基础值只在生成快照时读取一次」、「成长轴部署阈值按分阶段基础值」。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("A1", TestMaps.P0);
        ledger.Settle(board, majorRound);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, majorRound);

        Assert.Equal(expected, snapshot.DeployLimit);
        Assert.Equal(expected, EffectSnapshot.BaseDeployLimitFor(majorRound));
        Assert.Equal(snapshot, EffectSnapshot.Defaults(TestMaps.P0, majorRound, 0));
    }

    [Fact]
    public void 分阶段基础部署上限唯一实现()
    {
        // growth-pass-1 D2 / 裁决 1：阶段表全项目只定义一处（EffectSnapshot.BaseDeployLimitFor），其余读取点一律调用它。
        // 源码文本扫描 Core / Sim / Presentation / Godot 脚本（Godot 不在 siege.sln，只有文本扫描能覆盖，testing.md「游离工程」）：
        //   1) 阶段表形状 `<= 3 => 3` 恰好出现一次且位于 EffectSnapshot.cs（反面断言：判据在唯一实现里确实命中）；
        //   2) 旧常量名 BaseDeployLimit（不带 For）不得再出现；
        //   3) 不得出现把部署相关变量 / 常量 / 命名实参直接写成 3 / 4 / 5 的字面量（含 `BaseDeploy = 3`、`deployLimit: 3` 这类前缀非词边界的写法）。
        // 样本口径下界：四个目录各自非空，总字符数 ≥ 300 000，防止扫空目录恒绿。
        // 变异验证 M-GP8：RelicLedger.BuildSnapshot 改为 `int deploy = 3;`（第二处写死基础值）→ 全套红 48，含本测试（规则 3）；
        // M-GP1（阶段表边界改写）→ 本测试红（规则 1 的形状不再命中）。M-GP2 只改表内数值、不新增第二处，本测试不红（由数值 Theory 抓）。
        // check 变异 M-C1：Siege.Presentation HandInfoPanel 注入 `internal const int BaseDeploy = 3;` → 原判据 `\bdeploy\w*` 因 Base 与 Deploy 间无词边界而全套 0 红（假守门），
        //   去掉前导 \b 并纳入 `:` 后红 1（本测试）。
        string root = PresentationFixtures.RepoRoot();
        string[] dirs =
        [
            Path.Combine(root, "src", "Siege.Core"),
            Path.Combine(root, "src", "Siege.Sim"),
            Path.Combine(root, "src", "Siege.Presentation"),
            Path.Combine(root, "src", "godot", "scripts"),
        ];

        var files = new List<(string Path, string Text)>();
        foreach (string dir in dirs)
        {
            string[] found =
            [
                .. Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal),
            ];
            Assert.True(found.Length > 0, $"{dir} 没有扫到源码");
            files.AddRange(found.Select(f => (f, File.ReadAllText(f))));
        }

        Assert.True(files.Sum(f => f.Text.Length) >= 300_000, $"只扫到 {files.Sum(f => f.Text.Length)} 字符");

        var table = new Regex(@"<=\s*3\s*=>\s*3\b");
        var legacy = new Regex(@"\bBaseDeployLimit\b");
        var literal = new Regex(@"(?i)deploy\w*\s*[=:]\s*[345]\s*[;,)]");

        string[] tableHits = [.. files.Where(f => table.IsMatch(f.Text)).Select(f => Path.GetFileName(f.Path))];
        Assert.Equal(["EffectSnapshot.cs"], tableHits);
        Assert.Empty(files.Where(f => legacy.IsMatch(f.Text)).Select(f => f.Path));
        Assert.Empty(files.Where(f => literal.IsMatch(f.Text)).Select(f => $"{f.Path}: {literal.Match(f.Text).Value}"));
    }

    [Fact]
    public void 基础值随阶段提高_插旗阶段()
    {
        // growth-pass-1 D2 边界补钉（check 阶段）：插旗阶段 MajorRound = 0，PublishSupplement「任何阶段可用」会经账本副本为每名玩家生成效果快照，
        // 此时基础部署上限必须落在第一阶段 3（BaseDeployLimitFor 的 `<= 3` 分支覆盖 0），不得因 0 不在 1–3 区间而取到其它阶段值或抛出。
        // 变异验证 M-C4：BaseDeployLimitFor 首分支前插入 `< 1 => 5` → 全套红 2（本测试、既有「插旗阶段也能发布补充载荷」——后者只钉 Value，本测试另钉来源拆分的 Base）。
        MatchFlow match = MatchFixtures.Create();
        Assert.Equal((MatchPhase.FlagPlanting, 0), (match.Phase, match.MajorRound));

        StructureParameter deploy = match.PublishSupplement().Structures.Single(s => s.Player == MatchFixtures.P0).Parameters!.DeployLimit;

        Assert.Equal((3, 3), (deploy.Value, deploy.Base));
        Assert.Empty(deploy.Sources);
        Assert.Equal(3, EffectSnapshot.BaseDeployLimitFor(0));
    }
}
