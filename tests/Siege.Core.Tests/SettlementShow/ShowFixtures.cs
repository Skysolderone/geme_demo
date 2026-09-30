using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Presentation.Show;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>settlement-show 测试的公共夹具：按规格算例摆盘面与势力读数、把节拍 / 遮罩投影成文本（含 ImmutableArray 的 record 不能直接 Assert.Equal）。</summary>
internal static class ShowFixtures
{
    // 规格文本里的 P1–P4（与 MatchFixtures 的 P0–P3 无关，只是算例里的名字）。
    internal static readonly PlayerId P1 = new(1);
    internal static readonly PlayerId P2 = new(2);
    internal static readonly PlayerId P3 = new(3);
    internal static readonly PlayerId P4 = new(4);

    /// <summary>空盘（11×11 平地）。</summary>
    internal static GameBoard Stones() => TestMaps.Blank();

    /// <summary>在空盘上摆若干普通子。</summary>
    internal static GameBoard Stones(params (string Cell, PlayerId Owner)[] stones)
    {
        GameBoard board = Stones();
        foreach ((string cell, PlayerId owner) in stones)
        {
            board.Place(cell, owner);
        }

        return board;
    }

    /// <summary>只给总势力的读数：分项按"领地 0、军势 = 总值"（settlement-show-callouts 之前的算例只关心总值）。</summary>
    internal static PowerReading Reading(PlayerId player, long total, int? rank) => new(player, total, rank, 0, total);

    /// <summary>带分项的读数：总值 = 领地 + 军势（与 Core 的 <c>PlayerPower.Total</c> 口径一致）。</summary>
    internal static PowerReading Reading(PlayerId player, long territory, long groupScore, int? rank) => new(player, territory + groupScore, rank, territory, groupScore);

    /// <summary>结算的一侧（只有盘面与势力读数；棋串、信物、玩家状态、终局结果按需 <c>with</c> 补上）。</summary>
    internal static SettlementSide Side(GameBoard board, params PowerReading[] power) => SettlementSide.Of(board, [.. power]);

    /// <summary>一条公开信物状态（默认未揭示、无人控制）。</summary>
    internal static RelicPublicState Relic(string cell, RelicType type, bool revealed) =>
        new(Coord.Parse(cell), new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard), revealed, revealed ? new RelicContent(type, 1) : null, revealed ? 2 : null, new RelicControl(RelicControlKind.Uncontrolled, null));

    internal static StatusReading Status(PlayerId player, PlayerStatus status) => new(player, status);

    internal static ImmutableArray<Placement> Order(params string[] cells) => [.. cells.Select(c => new Placement(Coord.Parse(c), PieceType.Basic))];

    /// <summary>规格 1.2 算例：P1 在 C3、C4 落子并提掉 P2 的 D3；势力 P1 5→9、P2 6→4；名次 P1 2→1、P2 1→2。</summary>
    internal static (GameBoard Before, GameBoard After, ImmutableArray<PowerReading> PowerBefore, ImmutableArray<PowerReading> PowerAfter) CaptureExample() =>
        (
            Stones(("D3", P2), ("E5", P2)),
            Stones(("C3", P1), ("C4", P1), ("E5", P2)),
            [Reading(P1, 5, 2), Reading(P2, 6, 1)],
            [Reading(P1, 9, 1), Reading(P2, 4, 2)]
        );

    /// <summary>1.2 算例的节拍序列（有结算记录：放置顺序 C3、C4）。</summary>
    internal static ImmutableArray<SettlementBeat> CaptureExampleBeats(bool withRecord = true)
    {
        (GameBoard before, GameBoard after, ImmutableArray<PowerReading> pb, ImmutableArray<PowerReading> pa) = CaptureExample();
        return SettlementBeats.Generate(before, after, pb, pa, withRecord ? Order("C3", "C4") : null);
    }

    /// <summary>节拍序列的文本投影：每个节拍一行，条目按序列出。</summary>
    internal static string Text(ImmutableArray<SettlementBeat> beats) => string.Join("\n", beats.Select(Text));

    internal static string Text(SettlementBeat beat) => beat switch
    {
        PlacementBeat p => $"落子{(p.Compressed ? "(压缩)" : string.Empty)}[{string.Join(",", p.Pieces.Select(x => $"{x.Coord.ToNotation()}:{x.Type}:P{x.Owner.Value}"))}]",
        CaptureBeat c => $"提子[{string.Join(",", c.Pieces.Select(x => $"{x.Coord.ToNotation()}:P{x.Owner.Value}"))}]",
        PowerBeat w => $"势力[{string.Join(",", w.Changes.Select(x => $"P{x.Player.Value}:{x.OldValue}->{x.NewValue}({x.DeltaText}):{x.OldRank?.ToString() ?? "-"}->{x.NewRank?.ToString() ?? "-"}"))}]",
        RelicRevealBeat r => $"信物[{string.Join(",", r.Relics.Select(x => $"{x.Coord.ToNotation()}:{x.Name}"))}]",
        BannerBeat b => $"横幅[{string.Join(",", b.Banners)}]",
        _ => beat.ToString()!,
    };

    /// <summary>遮罩的文本投影。</summary>
    internal static string Text(ShowMask mask) =>
        $"隐藏[{string.Join(",", mask.Hidden.Order().Select(c => c.ToNotation()))}] 出现[{string.Join(",", mask.Appearing.OrderBy(k => k.Key).Select(k => $"{k.Key.ToNotation()}:{k.Value}"))}] "
        + $"仍显示[{string.Join(",", mask.StillShown.Select(x => x.Coord.ToNotation()))}] 淡出{mask.CaptureFadePermille} "
        + $"势力[{string.Join(",", mask.Power.OrderBy(k => k.Key.Value).Select(k => $"P{k.Key.Value}:{k.Value.Value}:{k.Value.ProgressPermille}:{(k.Value.Rolling ? "滚动" : "旧值")}"))}]";

    /// <summary>遮罩里飘字 / 信物闪光 / 横幅的文本投影（settlement-show-callouts）。</summary>
    internal static string CalloutText(ShowMask mask) =>
        $"飘字[{string.Join(",", mask.Callouts.Select(c => $"{c.Coord.ToNotation()}:{c.Kind}:{c.Text}:{c.AgePermille}"))}] 合计[{mask.CaptureSummary}] "
        + $"闪光[{string.Join(",", mask.RelicFlash.OrderBy(k => k.Key).Select(k => $"{k.Key.ToNotation()}:{k.Value}"))}] "
        + $"横幅[{(mask.Banner is { } b ? $"{b.Text}:{b.Index}/{b.Count}:{b.ProgressPermille}" : string.Empty)}]";

    /// <summary>某玩家势力显示的分段投影："段:段内进度:显示值:段文案"。</summary>
    internal static string StageText(PowerDisplay display) => $"{display.Stage}:{display.StagePermille}:{display.Value}:{display.StageText}";

    internal static string StageText(PowerStageDisplay display) => $"{display.Stage}:{display.StagePermille}:{display.Value}:{display.StageText}";

    internal static BigInteger Pow10(int exponent) => BigInteger.Pow(10, exponent);
}
