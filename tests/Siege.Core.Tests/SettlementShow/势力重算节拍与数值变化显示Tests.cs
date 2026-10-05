using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 势力重算节拍与数值变化显示（turn-settlement-show 1.4、2.6）。
/// 插值算例取自 tasks.md 2.6：12→20 在 500‰ 时为 16；10^30→3×10^30 在 1000‰ 时恰为 3×10^30；15→9 的中间值都在 9..15 之间。
/// settlement-show-callouts 1.1 增分段到账四条 Scenario（两段到账 / 分项为零则跳过 / 分项之和等于总增量 / 名次变动提示，算例取自其 spec）。
/// </summary>
/// <remarks>
/// 变异验证 M-C1「分项之和不等于总增量」——<c>SettlementBeats.PowerChanges</c> 里 <c>now.GroupScore - was.GroupScore</c> 改为 <c>… + 1</c> → 红 1（分项之和等于总增量）。
/// 还原后逐字节校验、刷新 mtime，44/44 绿。
/// tiered-number-show 1.4 增「放大幅度按档」（design.md D5）。变异（脚本做法与记录见 数值档位Tests）：
/// M-P1「军势段取领地增量的档」——<c>PowerChange.GroupTier</c> 改为 <c>NumberTier.Of(TerritoryDelta)</c> → 红 7（本类 放大幅度按档、演出音效提示Tests 六条）。
/// M-P2「军势段读总增量的档」——<c>PowerChange.TierOf</c> 里 <c>PowerStage.Group => GroupTier</c> 改为 <c>TotalTier</c> → 红 9（本类 放大幅度按档、数值档位Tests.三处呈现同一数值同一档 八条）。
/// M-T3「样式表四档势力栏放大 180 改 170」→ 红 2（本类 放大幅度按档、数值档位Tests.分档样式表 tier=4）。
/// </remarks>
public class 势力重算节拍与数值变化显示Tests
{
    private static ShowTimeline PowerOnly(params PowerChange[] changes) => new([new PowerBeat([.. changes])], ShowDuration.Normal);

    [Fact]
    public void 两段到账()
    {
        // 玩家 A 势力 20 → 29，领地 +3、军势 +6：领地段 0.35 秒从 20 滚到 23 并标"领地 +3"，军势段 0.35 秒从 23 滚到 29 并标"军势 +6"，定格 29。
        var change = new PowerChange(P1, 20, 29, 1, 1, TerritoryDelta: 3, GroupDelta: 6);
        Assert.Equal(("领地 +3", "军势 +6"), (change.TerritoryText, change.GroupText));
        Assert.Equal("Territory:0:20:领地 +3", StageText(change.DisplayAt(0)));
        Assert.Equal("Territory:500:21:领地 +3", StageText(change.DisplayAt(175)));
        Assert.Equal("Group:0:23:军势 +6", StageText(change.DisplayAt(350)));
        Assert.Equal("Group:500:26:军势 +6", StageText(change.DisplayAt(525)));
        Assert.Equal("Hold:0:29:", StageText(change.DisplayAt(700)));
        Assert.Equal("Hold:500:29:", StageText(change.DisplayAt(800)));

        // 时间线按同一算法出遮罩：节拍未开始为 Pending（旧值），进行中按段给出。
        var timeline = new ShowTimeline([new CaptureBeat([new CapturedPiece(Coord.Parse("D3"), P2, PieceType.Basic)]), new PowerBeat([change])], ShowDuration.Normal);
        Assert.Equal("Pending:0:20:", StageText(timeline.Mask().Power[P1]));
        timeline.Advance(600 + 525);
        PowerDisplay display = timeline.Mask().Power[P1];
        Assert.Equal("Group:500:26:军势 +6", StageText(display));
        Assert.True(display.Rolling);
        timeline.Advance(175);
        Assert.Equal("Hold:0:29:", StageText(timeline.Mask().Power[P1]));
        timeline.Advance(200);
        Assert.True(timeline.IsFinished);
    }

    [Fact]
    public void 分项为零则跳过()
    {
        // 玩家 B 势力 15 → 12，领地 −3、军势 0：只有领地段（15 滚到 12，标"领地 −3"），没有军势段，定格从 350 ms 起占 550 ms。
        var change = new PowerChange(P2, 15, 12, 2, 2, TerritoryDelta: -3, GroupDelta: 0);
        Assert.Equal("Territory:500:14:领地 −3", StageText(change.DisplayAt(175)));
        Assert.Equal("Hold:0:12:", StageText(change.DisplayAt(350)));
        Assert.Equal("Hold:500:12:", StageText(change.DisplayAt(625)));
        Assert.DoesNotContain(PowerStage.Group, Enumerable.Range(0, PowerBeat.Ms).Select(ms => change.DisplayAt(ms).Stage));

        // 领地 0、军势 +8：只有军势段，从 0 ms 起。
        var groupOnly = new PowerChange(P1, 12, 20, 1, 1, TerritoryDelta: 0, GroupDelta: 8);
        Assert.Equal("Group:500:16:军势 +8", StageText(groupOnly.DisplayAt(175)));
        Assert.Equal("Hold:0:20:", StageText(groupOnly.DisplayAt(350)));

        // 两项都为 0（只变名次）：整拍定格在新值。
        var rankOnly = new PowerChange(P3, 4, 4, 2, 3, TerritoryDelta: 0, GroupDelta: 0);
        Assert.Equal("Hold:500:4:", StageText(rankOnly.DisplayAt(450)));
    }

    [Fact]
    public void 分项之和等于总增量()
    {
        // 对任一结算：每名玩家 领地Δ + 军势Δ = 新值 − 旧值。结算前有读数的按差；结算前没有势力快照的两项旧值按 0。
        GameBoard board = Stones(("C3", P1), ("D3", P2));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(
            board, board,
            [Reading(P1, territory: 4, groupScore: 16, rank: 2), Reading(P2, territory: 7, groupScore: 20, rank: 1)],
            [Reading(P1, territory: 7, groupScore: 22, rank: 1), Reading(P2, territory: 5, groupScore: 20, rank: 2)],
            null);
        PowerBeat power = Assert.IsType<PowerBeat>(Assert.Single(beats));
        Assert.Equal(["P1:+3/+6=+9", "P2:−2/±0=−2"], power.Changes.Select(c => $"P{c.Player.Value}:{Signed(c.TerritoryDelta)}/{Signed(c.GroupDelta)}={c.DeltaText}"));
        Assert.All(power.Changes, c => Assert.Equal(c.NewValue - c.OldValue, c.TerritoryDelta + c.GroupDelta));

        ImmutableArray<SettlementBeat> first = SettlementBeats.Generate(board, board, [], [Reading(P1, territory: 2, groupScore: 5, rank: 1)], null);
        PowerChange fresh = Assert.Single(Assert.IsType<PowerBeat>(Assert.Single(first)).Changes);
        Assert.Equal((new BigInteger(2), new BigInteger(5), new BigInteger(7)), (fresh.TerritoryDelta, fresh.GroupDelta, fresh.Delta));

        static string Signed(BigInteger v) => v.Sign switch { > 0 => "+" + v, < 0 => "−" + BigInteger.Negate(v), _ => "±0" };
    }

    [Fact]
    public void 名次变动提示()
    {
        // 玩家 A 由第 2 名升到第 1 名 → 势力重算节拍给出"名次 2 → 1"；没有名次的玩家不提示。
        var change = new PowerChange(P1, 5, 9, 2, 1, TerritoryDelta: 1, GroupDelta: 3);
        Assert.True(change.RankChanged);
        Assert.Equal("名次 2 → 1", change.RankText);
        Assert.Null(new PowerChange(P2, 6, 4, null, null, TerritoryDelta: 0, GroupDelta: -2).RankText);
        Assert.Null(new PowerChange(P3, 6, 4, 1, 1, TerritoryDelta: 0, GroupDelta: -2).RankText);
    }

    [Fact]
    public void 放大幅度按档()
    {
        // 玩家 A 领地 +3、军势 +20：领地段段首放大 1.3 倍（一档），军势段段首放大 1.8 倍（四档）；总增量 +23 也是四档。
        var change = new PowerChange(P1, 20, 43, 1, 1, TerritoryDelta: 3, GroupDelta: 20);
        Assert.Equal((1, 4, 4), (change.TerritoryTier, change.GroupTier, change.TotalTier));
        Assert.Equal(130, NumberTierStyle.For(change.TierOf(PowerStage.Territory)).RankScalePercent);
        Assert.Equal(180, NumberTierStyle.For(change.TierOf(PowerStage.Group)).RankScalePercent);

        // 三个档位各取各的增量：领地 +30（四档）、军势 +3（一档）、总增量 +33（五档）。
        var mixed = new PowerChange(P1, 20, 53, 1, 1, TerritoryDelta: 30, GroupDelta: 3);
        Assert.Equal((4, 1, 5), (mixed.TierOf(PowerStage.Territory), mixed.TierOf(PowerStage.Group), mixed.TierOf(PowerStage.Hold)));
        Assert.Equal(5, mixed.TierOf(PowerStage.Pending));

        // 一至五档依次 1.3、1.45、1.6、1.8、2.0 倍（增量 3 / 5 / 9 / 20 / 40；负增量按绝对值取档）。
        Assert.Equal(
            [130, 145, 160, 180, 200],
            new[] { 3, 5, 9, 20, 40 }.Select(delta => NumberTierStyle.For(new PowerChange(P1, 50, 50 + delta, 1, 1, TerritoryDelta: delta, GroupDelta: 0).TerritoryTier).RankScalePercent));
        Assert.Equal(180, NumberTierStyle.For(new PowerChange(P2, 50, 30, 1, 1, TerritoryDelta: 0, GroupDelta: -20).GroupTier).RankScalePercent);

        // 遮罩里的势力显示带着这条变化：引擎层按当前段读档位（不自己取档），未开始 / 定格读总增量的档。
        var timeline = new ShowTimeline([new PowerBeat([change])], ShowDuration.Normal);
        PowerDisplay territory = timeline.Mask().Power[P1];
        Assert.Equal((PowerStage.Territory, 1), (territory.Stage, territory.Change.TierOf(territory.Stage)));
        timeline.Advance(350);
        PowerDisplay group = timeline.Mask().Power[P1];
        Assert.Equal((PowerStage.Group, 4), (group.Stage, group.Change.TierOf(group.Stage)));
        timeline.Advance(350);
        PowerDisplay hold = timeline.Mask().Power[P1];
        Assert.Equal((PowerStage.Hold, 4), (hold.Stage, hold.Change.TierOf(hold.Stage)));
    }

    [Fact]
    public void 势力上升()
    {
        // 12→20：500‰ 时为 16，增量 "+8"；节拍结束时恰为 20。
        var change = new PowerChange(P1, 12, 20, 1, 1);
        Assert.Equal("+8", change.DeltaText);
        Assert.Equal(new BigInteger(16), PowerInterpolation.Lerp(12, 20, 500));
        Assert.Equal(new BigInteger(20), PowerInterpolation.Lerp(12, 20, 1000));

        // settlement-show-callouts D2 分段到账：只给总值的条目全部增量在军势段（0.35 秒）滚完，175 ms 处为一半；其后定格在 20。
        ShowTimeline timeline = PowerOnly(change);
        Assert.Empty(timeline.Advance(175));
        PowerDisplay display = timeline.Mask().Power[P1];
        Assert.Equal((new BigInteger(16), true, "+8"), (display.Value, display.Rolling, display.Change.DeltaText));
        Assert.Empty(timeline.Advance(275));   // 0.9 秒的一半：定格
        Assert.Equal(500, timeline.ProgressPermille);
        Assert.Equal((new BigInteger(20), PowerStage.Hold), (timeline.Mask().Power[P1].Value, timeline.Mask().Power[P1].Stage));

        Assert.Single(timeline.Advance(450));
        Assert.True(timeline.IsFinished);
        Assert.True(timeline.Mask().IsEmpty);   // 遮罩为空 → 记分显示即结算后快照的 20
    }

    [Fact]
    public void 势力下降()
    {
        // 15→9：增量 "−6"；每个中间值都是 9..15 之间的整数且单调不升。
        var change = new PowerChange(P2, 15, 9, 1, 1);
        Assert.Equal("−6", change.DeltaText);
        BigInteger previous = 15;
        for (int permille = 0; permille <= 1000; permille++)
        {
            BigInteger value = PowerInterpolation.Lerp(15, 9, permille);
            Assert.InRange(value, 9, 15);
            Assert.True(value <= previous, $"{permille}‰ 时 {value} > 前值 {previous}");
            previous = value;
        }

        Assert.Equal(new BigInteger(9), PowerInterpolation.Lerp(15, 9, 1000));
    }

    [Fact]
    public void 名次变动()
    {
        // 结算前 P1 名次 2、P2 名次 1，结算后互换：两条都记录名次变化。
        GameBoard board = Stones(("C3", P1), ("D3", P2));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(
            board, board, [Reading(P1, 5, 2), Reading(P2, 6, 1)], [Reading(P1, 9, 1), Reading(P2, 4, 2)], null);
        PowerBeat power = Assert.IsType<PowerBeat>(Assert.Single(beats));
        Assert.Equal(2, power.Changes.Length);
        Assert.All(power.Changes, c => Assert.True(c.RankChanged));
        Assert.Equal(((int?)2, (int?)1), (power.Changes[0].OldRank, power.Changes[0].NewRank));
        Assert.Equal(((int?)1, (int?)2), (power.Changes[1].OldRank, power.Changes[1].NewRank));

        // 只有名次变、值不变的玩家同样列入（如被并列超越）。
        ImmutableArray<SettlementBeat> rankOnly = SettlementBeats.Generate(
            board, board, [Reading(P1, 5, 1), Reading(P2, 4, 2)], [Reading(P1, 5, 1), Reading(P2, 4, 3)], null);
        Assert.Equal("势力[P2:4->4(±0):2->3]", Text(rankOnly));
    }

    [Fact]
    public void 超大势力值()
    {
        // 10^30 → 3×10^30：每个显示值都在两端之间、不溢出；1000‰ 时恰为 3×10^30。
        BigInteger from = Pow10(30);
        BigInteger to = 3 * Pow10(30);
        for (int permille = 0; permille <= 1000; permille += 7)
        {
            BigInteger value = PowerInterpolation.Lerp(from, to, permille);
            Assert.InRange(value, from, to);
        }

        Assert.Equal(to, PowerInterpolation.Lerp(from, to, 1000));
        Assert.Equal(2 * Pow10(30), PowerInterpolation.Lerp(from, to, 500));

        ShowTimeline timeline = PowerOnly(new PowerChange(P1, from, to, 1, 1));
        timeline.Advance(900);
        Assert.True(timeline.IsFinished);
    }

    [Fact]
    public void 无变化的玩家不出现()
    {
        // P4 势力与名次都不变：势力重算节拍不含 P4；遮罩里也没有 P4（记分显示不变）。
        GameBoard board = Stones(("C3", P1));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(
            board, board,
            [Reading(P1, 5, 2), Reading(P2, 6, 1), Reading(P4, 2, 3)],
            [Reading(P1, 9, 1), Reading(P2, 6, 2), Reading(P4, 2, 3)],
            null);
        PowerBeat power = Assert.IsType<PowerBeat>(Assert.Single(beats));
        Assert.DoesNotContain(power.Changes, c => c.Player == P4);
        Assert.Equal([P1, P2], power.Changes.Select(c => c.Player));

        var timeline = new ShowTimeline(beats, ShowDuration.Normal);
        Assert.False(timeline.Mask().Power.ContainsKey(P4));
    }

    [Fact]
    public void 已弃赛玩家名次为空仍列入()
    {
        // design.md A6：已弃赛 / 已出局玩家没有名次；只要总势力有变仍列入，名次变动提示不显示。
        GameBoard board = Stones(("C3", P1), ("H8", P3));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(
            board, board, [Reading(P1, 5, 1), Reading(P3, 7, null)], [Reading(P1, 5, 1), Reading(P3, 6, null)], null);
        PowerBeat power = Assert.IsType<PowerBeat>(Assert.Single(beats));
        PowerChange change = Assert.Single(power.Changes);
        Assert.Equal((P3, "−1", (int?)null, (int?)null, false), (change.Player, change.DeltaText, change.OldRank, change.NewRank, change.RankChanged));
    }
}
