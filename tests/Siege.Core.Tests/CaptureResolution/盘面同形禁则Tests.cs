using System.Text.RegularExpressions;
using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.CaptureResolution;

/// <summary>规格：capture-resolution —— Requirement: 盘面同形禁则</summary>
public class 盘面同形禁则Tests
{
    [Fact]
    public void 循环提子被禁止()
    {
        // 变异验证：Rehearse 删掉第 6 步 → 本测试红 1（「非盘面差异不豁免同形」「同形返回历史序号」等连带红）。
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);

        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic).Confirmed);   // S1
        // 提回去得到的是初始盘面：初始盘面不是提交，不入集合
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic).Confirmed);   // S2
        string s2 = board.Serialize();

        SettlementOutcome outcome = BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic);

        Assert.False(outcome.Confirmed);
        Assert.Equal(BatchFailureKind.Superko, outcome.Failure!.Kind);
        Assert.Equal(1, outcome.Failure.DuplicateOfSequence);
        Assert.Equal(s2, board.Serialize());
        Assert.Equal(2, driver.History.Count);
    }

    [Fact]
    public void 换类型仍构成同形()
    {
        // superko-occupancy 反转自旧「换类型不构成同形」：结算后各格占用者、设施与地表都与第 1 次提交一致，
        // 只是 D4 由普通子变为堡垒子 → 仍触发同形，批次被拒绝并报序号 1。
        // 变异验证（superko-occupancy 1.2，过滤 CaptureResolution|BoardTopology|TerrainEditing|同形）：M-K1 SuperkoKey 保留类型（column % 2 == 0 → >= 0）→ 红 6（含本测试）；
        // M-K2 FindDuplicate 绕过投影直接查原串 → 红 12（含本测试）。
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic).Confirmed);
        string s1 = board.Serialize();
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic).Confirmed);
        Assert.False(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic).Confirmed);
        string s2 = board.Serialize();

        RehearsalResult preview = driver.Rehearse(
            BatchFixtures.Context(board, TestMaps.P0), [new Placement(TestMaps.At("D4"), PieceType.Fortress)]);
        SettlementOutcome outcome = BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Fortress);

        // 前提：换类型后的结算后盘面按序列化确实与第 1 次提交不同（差别只在类型），否则本测试等于「循环提子被禁止」。
        Assert.NotEqual(s1, preview.ProjectedBoard!.Serialize());
        Assert.Equal(GameBoard.SuperkoKey(s1), GameBoard.SuperkoKey(preview.ProjectedBoard.Serialize()));
        Assert.False(outcome.Confirmed);
        Assert.Equal(BatchFailureKind.Superko, outcome.Failure!.Kind);
        Assert.Equal(1, outcome.Failure.DuplicateOfSequence);
        Assert.Equal(s2, board.Serialize());
        Assert.Equal(2, driver.History.Count);
    }

    [Fact]
    public void 多人互提循环换类型也被禁止()
    {
        // superko-occupancy：三名玩家在角上两格（A1 / B1）轮流提子与回填，P3 的墙（A2、B2、C1）封住外侧。
        // 第一轮 6 次提交全用普通子，占用态互不相同；第二轮 P1 用堡垒子回填 B1，
        // 结算后各格占用者与第 1 次提交相同（只差类型）→ 触发同形，循环无法继续。
        // 变异验证：M-K1（投影保留类型）→ 红（本测试在最后一步被接受）；M-K2（FindDuplicate 绕过投影）→ 红。
        PlayerId p3 = new(3);
        GameBoard board = TestMaps.Blank(size: 5)
            .Place("A2", p3).Place("B2", p3).Place("C1", p3)
            .Place("A1", TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);
        (PlayerId Player, string Cell)[] round =
        [
            (TestMaps.P1, "B1"), (BatchFixtures.P2, "A1"), (TestMaps.P0, "B1"),
            (TestMaps.P1, "A1"), (BatchFixtures.P2, "B1"), (TestMaps.P0, "A1"),
        ];
        var keys = new List<string>();
        foreach ((PlayerId player, string cell) in round)
        {
            SettlementOutcome step = driver.Confirm(BatchFixtures.Context(board, player), [BatchFixtures.P(cell)]);
            Assert.True(step.Confirmed, $"{player} 回填 {cell} 被拒绝：{step.Failure?.Message}");
            Assert.Single(step.CaptureRecord!.Captured);
            keys.Add(GameBoard.SuperkoKey(board.Serialize()));
        }

        Assert.Equal(6, keys.Distinct(StringComparer.Ordinal).Count());
        string s6 = board.Serialize();

        SettlementOutcome outcome = driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P1), [BatchFixtures.P("B1", PieceType.Fortress)]);

        Assert.False(outcome.Confirmed);
        Assert.Equal(BatchFailureKind.Superko, outcome.Failure!.Kind);
        Assert.Equal(1, outcome.Failure.DuplicateOfSequence);
        Assert.Equal(s6, board.Serialize());
        Assert.Equal(6, driver.History.Count);
    }

    [Fact]
    public void 非盘面差异不豁免同形()
    {
        // 逐格一致，但当前玩家的手牌、部署上限与合法范围都完全不同 → 仍同形。
        // 变异验证：BoardHistory.Record 把 context 信息拼进盘面串（或比对时附带库存）→ 本测试红 1。
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(driver.Confirm(
            BatchFixtures.Context(board, TestMaps.P0, limit: 3, stock: BatchFixtures.Stock((PieceType.Basic, 1))),
            [BatchFixtures.P("D4")]).Confirmed);
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic).Confirmed);

        BatchContext different = BatchFixtures.Context(
            board, TestMaps.P0, limit: 9,
            stock: BatchFixtures.Stock((PieceType.Basic, 7), (PieceType.Fortress, 2), (PieceType.Synergy, 1)),
            range: new HashSet<Coord> { TestMaps.At("D4"), TestMaps.At("A1") });
        SettlementOutcome outcome = driver.Confirm(different, [BatchFixtures.P("D4")]);

        Assert.False(outcome.Confirmed);
        Assert.Equal(BatchFailureKind.Superko, outcome.Failure!.Kind);
        Assert.Equal(1, outcome.Failure.DuplicateOfSequence);
    }

    [Fact]
    public void 中间态不入集合()
    {
        // 暂放过程中经过与 S1 相同的预览态（预演判同形）；只改类型仍同形（superko-occupancy），
        // 再加暂放一子使结算后占用不同后确认 → 合法，且集合只新增最终盘面。
        // 变异验证：Rehearse 在第 6 步之后 history.Record(projected.Serialize()) → 本测试红 1。
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P0);
        SettlementDriver driver = BatchFixtures.Driver(board);
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P0, 'A', PieceType.Basic).Confirmed);
        string s1 = board.Serialize();
        Assert.True(BatchFixtures.TakeKo(driver, TestMaps.P1, 'A', PieceType.Basic).Confirmed);

        var batch = new StagedBatch(board, BatchFixtures.Context(board, TestMaps.P0));
        Assert.Null(batch.Stage(TestMaps.At("D4"), PieceType.Basic));
        RehearsalResult preview = driver.Rehearse(batch.Context, batch.Placements);
        Assert.Equal(BatchFailureKind.Superko, preview.Failure!.Kind);
        Assert.Equal(s1, preview.ProjectedBoard!.Serialize());
        Assert.Equal(2, driver.History.Count);

        Assert.Null(batch.Replace(TestMaps.At("D4"), PieceType.Line));
        Assert.Equal(BatchFailureKind.Superko, driver.Rehearse(batch.Context, batch.Placements).Failure?.Kind);
        Assert.Equal(2, driver.History.Count);

        Assert.Null(batch.Stage(TestMaps.At("A1"), PieceType.Basic));
        SettlementOutcome outcome = driver.Confirm(batch);

        Assert.True(outcome.Confirmed, outcome.Failure?.Message);
        Assert.Equal(3, driver.History.Count);
        Assert.Equal(board.Serialize(), driver.History.Entries[2].Board);
        Assert.Equal(1, driver.History.Entries.Count(e => e.Board == s1));
    }

    [Fact]
    public void 同键返回最早提交序号()
    {
        // superko-occupancy D2：历史按"比对键 → 最早提交序号"索引。只差类型的盘面同键，返回最早一次；占用不同的盘面不误判。
        var history = new BoardHistory();
        Assert.Equal(1, history.Record("0B/--"));
        Assert.Equal(2, history.Record("1B/--"));
        Assert.Equal(3, history.Record("--/0B"));

        Assert.Equal(1, history.FindDuplicate("0F/--"));
        Assert.Equal(2, history.FindDuplicate("1A/--"));
        Assert.Equal(3, history.FindDuplicate("--/0L"));
        Assert.Null(history.FindDuplicate("2B/--"));
        Assert.Null(history.FindDuplicate("0B/0B"));

        // 旧规则下产生的历史（旧存档）可能含两条只差类型的提交：读档重建索引后返回较早的那一条。
        // 变异验证：M-K5 Append 用索引器覆盖（取最新）→ 红 1（仅本测试）；M-K6 Deserialize 只加条目不建索引 → 红 3（含本测试）。
        BoardHistory legacy = BoardHistory.Deserialize("1:0B/--\n2:1B/--\n3:0F/--");
        Assert.Equal(1, legacy.FindDuplicate("0L/--"));
        Assert.Equal(3, legacy.Count);
    }

    [Fact]
    public void 同形比对键投影只有一处实现()
    {
        // superko-occupancy D1：比对键投影只在 GameBoard.SuperkoKey 一处，唯一调用方是 BoardHistory（第 8 步经 FindDuplicate 到达）。
        // 源码扫描整个 src/（含不在 sln 里的 src/godot，去 // 注释）：
        //   ① SuperkoKey( 只出现在 GameBoard.cs（定义）与 BoardHistory.cs（调用）；
        //   ② 形状扫描：任何文件里出现"六种类型码里 ≥ 4 个组成的字符集合 / 字面量"（如 "[BFLMSA]"）即视为自写一份去类型投影；
        //      类型码映射本身只在 GameBoard.cs（TypeCode / TypeFromCode，按 case 分行写，不命中本形状）。
        // 反面：定义与调用确实被扫到；形状扫描认得出 Regex.Replace(s, "[BFLMSA]", "") 这种第二实现。口径下界：文件数 ≥ 120（实测 138）。
        // 变异 M-K3（实跑）：BatchRehearsal 第 8 步前插入一处 _ = GameBoard.SuperkoKey(projected.Serialize()) → 红 1（仅本测试）。
        // 变异 M-K4（实跑）：BatchRehearsal 第 8 步前插入 Regex.Replace(projected.Serialize(), "[BFLMSA]", "-") 的自写投影 → 红 1（仅本测试）。
        string src = Path.Combine(PresentationFixtures.RepoRoot(), "src");
        string[] files = [.. Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}.godot{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
        Assert.True(files.Length >= 120, $"只扫到 {files.Length} 个文件");
        Assert.Contains(files, f => f.Contains($"{Path.DirectorySeparatorChar}godot{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var keyUsers = new List<string>();
        var shapeHits = new List<string>();
        foreach (string file in files)
        {
            string code = Regex.Replace(File.ReadAllText(file), "//[^\n]*", string.Empty);
            if (code.Contains("SuperkoKey(", StringComparison.Ordinal))
            {
                keyUsers.Add(Path.GetFileName(file));
            }

            if (TypeCodeSetShape.IsMatch(code))
            {
                shapeHits.Add(Path.GetFileName(file));
            }
        }

        Assert.Equal(["BoardHistory.cs", "GameBoard.cs"], keyUsers.Order(StringComparer.Ordinal));
        Assert.Empty(shapeHits);

        string gameBoard = File.ReadAllText(Path.Combine(src, "Siege.Core", "Board", "GameBoard.cs"));
        Assert.Contains("public static string SuperkoKey(string serialized)", gameBoard, StringComparison.Ordinal);
        Assert.Contains("GameBoard.SuperkoKey(", File.ReadAllText(Path.Combine(src, "Siege.Core", "Batch", "BoardHistory.cs")), StringComparison.Ordinal);
        Assert.Matches(TypeCodeSetShape, "string key = Regex.Replace(s, \"[BFLMSA]\", \"\");");
        Assert.Matches(TypeCodeSetShape, "if (\"FLSM\".Contains(ch)) continue;");
    }

    /// <summary>"六种类型码里 ≥ 4 个组成的字符集合或字面量"：自写去类型投影的典型形状。</summary>
    private static readonly Regex TypeCodeSetShape = new(@"[""\[][BFLMSA]{4,6}[""\]]", RegexOptions.CultureInvariant);
}
