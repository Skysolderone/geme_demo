using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 结算节拍序列的生成（turn-settlement-show 1.1–1.3、1.5）。
/// 节拍生成 <see cref="SettlementBeats"/> 在 Presentation，纯函数、直接单测。
/// </summary>
/// <remarks>
/// 变异验证（脚本 <c>shutil.copyfile</c> 备份、还原后 <c>filecmp</c> 逐字节比对并 <c>os.utime</c> 刷新 mtime；在 SettlementShow 命名空间 30 条里跑）：
/// M-S1「节拍顺序颠倒」——<c>SettlementBeats.Generate</c> 末尾改为 <c>return [.. beats.ToImmutable().Reverse()];</c>（势力 → 提子 → 落子）→ 红 9：
/// 本类 落子并提子的批次 / 无提子的批次 / 相同输入得到相同序列，落子与提子节拍的内容Tests.多名玩家的棋子同时被提，
/// 演出按节拍依次播放Tests 的 节拍切换时刻 / 提子节拍开始前被提棋子仍在 / 单次推进跨过多个节拍时逐个报告 / 本机玩家确认压缩为一次闪动，提速Tests.松开恢复。
/// 三条变异还原后基线 30/30 绿。
/// tiered-number-show 段 A 复跑 M-S1（SettlementShow + TacticalLayers 两个命名空间，基线 215 通过 / 2 跳过）：红 20——加入军势揭示节拍后顺序断言更多，
/// 含本类 落子并提子的批次 / 无提子的批次 / 相同输入得到相同序列。军势揭示节拍的插入位置另有 M-R1（见 军势揭示节拍Tests）。
/// </remarks>
public class 结算节拍序列的生成Tests
{
    [Fact]
    public void 三种节拍的数据模型()
    {
        // 落子节拍的每枚棋子不携带算式（tiered-number-show D3）：飘字只有类型名。
        Assert.Equal("堡垒子", new PlacedPiece(Coord.Parse("C4"), PieceType.Fortress, P1).CalloutText);

        // design.md D3：落子每枚 0.25 秒、整拍上限 1.0 秒；提子 0.6 秒；势力重算 0.9 秒。
        var two = new PlacementBeat([new PlacedPiece(Coord.Parse("C3"), PieceType.Basic, P1), new PlacedPiece(Coord.Parse("C4"), PieceType.Fortress, P1)]);
        Assert.Equal(500, two.DurationMs);
        Assert.Equal(("C3", PieceType.Basic, P1), (two.Pieces[0].Coord.ToNotation(), two.Pieces[0].Type, two.Pieces[0].Owner));
        Assert.Equal(("C4", PieceType.Fortress, P1), (two.Pieces[1].Coord.ToNotation(), two.Pieces[1].Type, two.Pieces[1].Owner));
        Assert.False(two.Compressed);

        // 5 枚：每枚间隔压缩，整拍 1.0 秒；本机玩家确认压缩为一次 0.25 秒（A2）。
        var five = new PlacementBeat([.. Enumerable.Range(1, 5).Select(i => new PlacedPiece(Coord.Parse($"A{i}"), PieceType.Basic, P1))]);
        Assert.Equal(1000, five.DurationMs);
        Assert.Equal(250, (five with { Compressed = true }).DurationMs);

        var capture = new CaptureBeat([new CapturedPiece(Coord.Parse("D3"), P2, PieceType.Basic)]);
        Assert.Equal(600, capture.DurationMs);
        Assert.Equal(("D3", P2, PieceType.Basic), (capture.Pieces[0].Coord.ToNotation(), capture.Pieces[0].Owner, capture.Pieces[0].Type));

        var change = new PowerChange(P1, 5, 9, OldRank: 2, NewRank: 1);
        var power = new PowerBeat([change]);
        Assert.Equal(900, power.DurationMs);
        Assert.Equal((P1, 5, 9, (int?)2, (int?)1), (change.Player, (int)change.OldValue, (int)change.NewValue, change.OldRank, change.NewRank));
        Assert.Equal(4, (int)change.Delta);
        Assert.Equal("+4", change.DeltaText);
        Assert.True(change.RankChanged);
        Assert.Equal("−2", new PowerChange(P2, 6, 4, 1, 2).DeltaText);
        Assert.False(new PowerChange(P3, 6, 4, null, null).RankChanged);
    }

    [Fact]
    public void 落子并提子的批次()
    {
        // 规格算例（tiered-number-show 改）：P1 在 C3、C4 落子并提掉 P2 的 D3，势力 P1 5→9、P2 6→4
        // → 落子(C3,C4) → 提子(D3) → 军势揭示(C3、C4 所在棋串) → 势力(P1 +4, P2 −2)。
        // 结算后快照的棋串明细：P1 的 C3-C4（基础 2、加值 4、倍增子 1 → 军势 9，阈值 8 / 16 / 32 / 64 下二档）与 P2 的 E5（军势 4，不含本次落子、不揭示）。
        (GameBoard before, GameBoard after, ImmutableArray<PowerReading> pb, ImmutableArray<PowerReading> pa) = CaptureExample();
        SettlementSide beforeSide = Side(before, [.. pb]);
        SettlementSide afterSide = Side(after, [.. pa]) with { Groups = [Group(P2, 4, 0, 0, 4, "E5"), Group(P1, 2, 4, 1, 9, "C3", "C4")] };

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(beforeSide, afterSide, Order("C3", "C4"));
        Assert.Equal(
            "落子[C3:Basic:P1,C4:Basic:P1]\n提子[D3:P2]\n揭示[C3:P1:9:2@1/220|2+4@1/220|(2+4)×1.5@1/220|(2+4)×1.5 = 9@2/220]\n势力[P1:5->9(+4):2->1,P2:6->4(−2):1->2]",
            Text(beats));
        Assert.Equal([typeof(PlacementBeat), typeof(CaptureBeat), typeof(PowerRevealBeat), typeof(PowerBeat)], beats.Select(b => b.GetType()));

        // 结算记录缺失时演出内容不缺项：只凭两份快照得出同样的四个节拍。
        Assert.Equal(Text(beats), Text(SettlementBeats.Generate(beforeSide, afterSide, null)));

        // 结算后一侧没有棋串明细（只给盘面与势力读数的算例）：军势揭示节拍省略，其余三个节拍不变。
        ImmutableArray<SettlementBeat> bare = CaptureExampleBeats();
        Assert.Equal(
            "落子[C3:Basic:P1,C4:Basic:P1]\n提子[D3:P2]\n势力[P1:5->9(+4):2->1,P2:6->4(−2):1->2]",
            Text(bare));
        Assert.Equal(Text(bare), Text(CaptureExampleBeats(withRecord: false)));
    }

    [Fact]
    public void 无提子的批次()
    {
        GameBoard before = Stones(("E5", P2));
        GameBoard after = Stones(("C3", P1), ("C4", P1), ("E5", P2));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(
            before, after, [Reading(P1, 5, 2), Reading(P2, 6, 1)], [Reading(P1, 7, 1), Reading(P2, 6, 2)], Order("C3", "C4"));
        Assert.Equal([typeof(PlacementBeat), typeof(PowerBeat)], beats.Select(b => b.GetType()));
        Assert.DoesNotContain(beats, b => b is CaptureBeat);
    }

    [Fact]
    public void Pass且势力不变()
    {
        GameBoard board = Stones(("E5", P2), ("C3", P1));
        ImmutableArray<PowerReading> power = [Reading(P1, 5, 2), Reading(P2, 6, 1)];
        Assert.Empty(SettlementBeats.Generate(board, board, power, power, null));
    }

    [Fact]
    public void Pass但势力有变()
    {
        // 盘面不变、P2 的势力变了（如信物效果随大回合变化）：只含势力重算节拍。
        GameBoard board = Stones(("E5", P2), ("C3", P1));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(board, board, [Reading(P1, 5, 2), Reading(P2, 6, 1)], [Reading(P1, 5, 2), Reading(P2, 8, 1)], null);
        Assert.Equal("势力[P2:6->8(+2):1->1]", Text(beats));
    }

    [Fact]
    public void 相同输入得到相同序列()
    {
        Assert.Equal(Text(CaptureExampleBeats()), Text(CaptureExampleBeats()));

        // 真实对局：结算前后两份公开快照 + 结算记录，两次生成逐项相同；生成不改变对局（指纹不变）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, MatchFixtures.All);   // 保护期（第 1–3 大回合）之后全图可落
        MatchPublicView before = match.Publish();
        SettlementOutcome outcome = match.PlayTurn("F6", "E6");
        MatchPublicView after = match.Publish();
        string fingerprint = PresentationFixtures.Fingerprint(match);

        ImmutableArray<SettlementBeat> first = SettlementBeats.Generate(before, after, outcome.CaptureRecord);
        ImmutableArray<SettlementBeat> second = SettlementBeats.Generate(before, after, outcome.CaptureRecord);
        Assert.Equal(Text(first), Text(second));
        Assert.Equal(fingerprint, PresentationFixtures.Fingerprint(match));

        // 快照投影确实读到了内容：落子节拍按放置顺序列出两枚、归属为行动玩家；公开快照带棋串明细，序列里有军势揭示节拍。
        Assert.Contains(first, b => b is PowerRevealBeat);
        PlacementBeat placement = Assert.IsType<PlacementBeat>(first[0]);
        Assert.Equal(["F6", "E6"], placement.Pieces.Select(p => p.Coord.ToNotation()));
        Assert.All(placement.Pieces, p => Assert.Equal(before.CurrentPlayer, p.Owner));
    }

    [Fact]
    public void 结算前没有势力快照时旧值按零()
    {
        // 本局首次结算之前公开快照里没有势力明细：旧值按 0、旧名次按无，节拍照常生成而不是抛出。
        Assert.Empty(SettlementBeats.Readings(null));
        GameBoard board = Stones(("C3", P1));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(board, board, [], [Reading(P1, 3, 1)], null);
        Assert.Equal("势力[P1:0->3(+3):-->1]", Text(beats));
    }
}
