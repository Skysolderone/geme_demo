using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests.CaptureResolution;

/// <summary>规格：capture-resolution —— Requirement: 已提交盘面历史的持久化</summary>
public class 已提交盘面历史的持久化Tests
{
    [Fact]
    public void 存档恢复后同形仍生效()
    {
        // 第 20 次提交后存档恢复，随后 P0 在劫 A 用堡垒子提劫会重现第 17 次提交的占用（第 17 次用的是连珠子）→ 仍拒绝并报序号 17。
        // superko-occupancy：KoScript 改为"填子造互异盘面"后目标序号由 12 改为 17，并借此钉住"读档重建的索引也忽略类型"。
        // 变异验证：Deserialize 忽略输入返回空集合 → 本测试红 1；Deserialize 把序号从 0 起编 → 本测试红 1。
        // trellis-check 复核：Serialize 丢掉最后一条 → 红。superko-occupancy M-K6：Deserialize 只加 _entries 不建索引 → 红 3（含本测试）；
        // M-K1（投影保留类型）→ 红（第 17 次用连珠子、本次用堡垒子，只差类型）。
        GameBoard board = BatchFixtures.KoBoard(holderA: TestMaps.P1, holderB: TestMaps.P1);
        SettlementDriver original = BatchFixtures.Driver(board);
        List<string> boards = KoScript.Play(original, upTo: 20);
        Assert.Equal(20, boards.Select(GameBoard.SuperkoKey).Distinct().Count());
        Assert.Equal(20, original.History.Count);

        string saved = original.History.Serialize();
        BoardHistory restored = BoardHistory.Deserialize(saved);
        SettlementDriver resumed = new(board.Clone(), restored, new RecordingHooks());
        Assert.Equal(original.History.Entries, restored.Entries);

        SettlementOutcome outcome = BatchFixtures.TakeKo(resumed, TestMaps.P0, 'A', PieceType.Fortress);

        Assert.False(outcome.Confirmed);
        Assert.Equal(BatchFailureKind.Superko, outcome.Failure!.Kind);
        Assert.Equal(17, outcome.Failure.DuplicateOfSequence);
        // 对照：没有历史的话这一批是合法的——被拒绝确实是历史在起作用；且结算后盘面与第 17 次只差类型。
        SettlementDriver amnesiac = new(board.Clone(), new BoardHistory(), new RecordingHooks());
        SettlementOutcome accepted = BatchFixtures.TakeKo(amnesiac, TestMaps.P0, 'A', PieceType.Fortress);
        Assert.True(accepted.Confirmed);
        Assert.NotEqual(boards[16], amnesiac.Board.Serialize());
        Assert.Equal(GameBoard.SuperkoKey(boards[16]), GameBoard.SuperkoKey(amnesiac.Board.Serialize()));
    }

    [Fact]
    public void 序号与内容按提交顺序往返()
    {
        // 变异验证：Serialize 丢掉序号或用 ',' 分隔 → 本测试红 1。
        var history = new BoardHistory();
        Assert.Equal(string.Empty, history.Serialize());
        Assert.Empty(BoardHistory.Deserialize(string.Empty).Entries);

        Assert.Equal(1, history.Record("0B--/----"));
        Assert.Equal(2, history.Record("0B1F/----"));
        Assert.Equal(3, history.Record("0B1F/2L--"));

        string text = history.Serialize();
        Assert.Equal("1:0B--/----\n2:0B1F/----\n3:0B1F/2L--", text);
        BoardHistory restored = BoardHistory.Deserialize(text);
        Assert.Equal(history.Entries, restored.Entries);
        Assert.Equal(2, restored.FindDuplicate("0B1F/----"));
        Assert.Null(restored.FindDuplicate("0B1F/2L"));
        Assert.Equal(4, restored.Record("----/----"));

        // 存档经文本层往返后的 \r\n 行尾与结尾换行不算损坏，且 \r 不得混进盘面串（否则同形比对静默失效）
        // 变异验证（trellis-check）：Deserialize 删掉 TrimEnd('\r') → 本测试红 1（FindDuplicate 返回 null）。
        BoardHistory crlf = BoardHistory.Deserialize("1:0B--/----\r\n2:0B1F/----\r\n");
        Assert.Equal(history.Entries.Take(2), crlf.Entries);
        Assert.Equal(2, crlf.FindDuplicate("0B1F/----"));
        Assert.Empty(BoardHistory.Deserialize("\n").Entries);

        // 序号不连续或格式损坏视为存档损坏
        Assert.Throws<FormatException>(() => BoardHistory.Deserialize("1:0B--/----\n3:0B1F/----"));
        Assert.Throws<FormatException>(() => BoardHistory.Deserialize("0B--/----"));
    }
}
