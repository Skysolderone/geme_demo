using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Presentation.Hud;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 个人势力卡片（power-card）：常驻显示本机玩家的势力、名次与拆分；暂放时给"本手预计"，增量超过 100 改给落子后总势力的区间；
/// 确认后按领地段 → 军势段 → 收束 → 停留 → 缩回播放数值构筑。
/// </summary>
public class 个人势力卡片Tests
{
    private static PowerLayerContent Power(params PlayerPowerRowView[] rows) => new([], [.. rows], []);

    private static PlayerPowerRowView Row(PlayerId player, long total, int territory, int? rank) =>
        new(player, PlayerStatus.Active, total, territory, total - territory, rank, null);

    private static PreviewPresentation Preview(PlayerId viewer, int used, FailurePresentation? failure, params PowerChangeView[] changes) =>
        new(viewer, used, 3, $"{used} / 3", [], [], [], [], [.. changes], [], [], [], [], failure, null, failure is null);

    private static PowerChangeView Change(PlayerId player, long before, long after, int rankBefore, int rankAfter) =>
        new(player, true, before, after, after - before, rankBefore, rankAfter, rankBefore != rankAfter, string.Empty);

    [Fact]
    public void 常驻显示本机玩家的势力名次与拆分()
    {
        PowerCardView card = PowerCard.Build(Power(Row(P2, 8, 4, 2), Row(P1, 15, 12, 1)), P1, null, null)!;
        Assert.Equal("蓝方 · 第 1 名", card.TitleText);
        Assert.Equal("15", card.ValueText);
        Assert.Equal("领地 12 · 军势 3", card.BreakdownText);
        Assert.Equal(PowerForecastKind.None, card.Forecast);
        Assert.Null(card.ForecastText);

        // 插旗阶段没有势力快照 / 名单里没有本机玩家：不出卡片。
        Assert.Null(PowerCard.Build(Power(), P1, null, null));
        Assert.Null(PowerCard.Build(Power(Row(P2, 8, 4, 1)), P1, null, null));
    }

    [Fact]
    public void 本手增量不超过100给确切值()
    {
        PowerLayerContent power = Power(Row(P1, 15, 12, 2));

        // 部署额度用了 0：没有暂放，不给预估（即便预演里有一条 0 增量）。
        Assert.Equal(PowerForecastKind.None, PowerCard.Build(power, P1, Preview(P1, 0, null, Change(P1, 15, 15, 2, 2)), null)!.Forecast);

        PowerCardView card = PowerCard.Build(power, P1, Preview(P1, 2, null, Change(P1, 15, 51, 2, 1)), null)!;
        Assert.Equal(PowerForecastKind.Exact, card.Forecast);
        Assert.Equal("本手预计 +36", card.ForecastText);
        Assert.Null(card.ForecastRangeText);
        Assert.Equal("名次 第 2 → 第 1", card.ForecastRankText);

        // 恰好 100 仍给确切值（严格大于才给区间）。
        Assert.Equal("本手预计 +100", PowerCard.Build(power, P1, Preview(P1, 1, null, Change(P1, 15, 115, 2, 2)), null)!.ForecastText);

        // 负增量带警示，用 U+2212 负号。
        PowerCardView down = PowerCard.Build(power, P1, Preview(P1, 1, null, Change(P1, 15, 12, 2, 2)), null)!;
        Assert.Equal("本手预计 −3", down.ForecastText);
        Assert.True(down.ForecastNegative);
        Assert.Null(down.ForecastRankText);
    }

    [Fact]
    public void 本手增量超过100只给区间()
    {
        PowerCardView card = PowerCard.Build(Power(Row(P1, 300, 200, 1)), P1, Preview(P1, 3, null, Change(P1, 300, 1284, 1, 1)), null)!;
        Assert.Equal(PowerForecastKind.Range, card.Forecast);
        Assert.Equal("本手预计 +100 以上", card.ForecastText);
        Assert.Equal("预计到达 1100 – 1500", card.ForecastRangeText);
        Assert.DoesNotContain("1284", card.ForecastText + card.ForecastRangeText);
    }

    [Fact]
    public void 区间包住真实值且取两位有效数字()
    {
        Assert.Equal((new BigInteger(1100), new BigInteger(1500)), PowerCard.RangeOf(1284));
        Assert.Equal((new BigInteger(180), new BigInteger(230)), PowerCard.RangeOf(201));
        Assert.Equal((new BigInteger(90), new BigInteger(110)), PowerCard.RangeOf(100));
        foreach (BigInteger after in new BigInteger[] { 101, 999, 1000, 54321, BigInteger.Pow(10, 30) + 7 })
        {
            (BigInteger low, BigInteger high) = PowerCard.RangeOf(after);
            Assert.True(low <= after && after <= high, $"{after}: {low}–{high}");
        }

        // 大数沿用排名栏的缩写。
        PowerCardView card = PowerCard.Build(Power(Row(P1, 10, 5, 1)), P1, Preview(P1, 1, null, Change(P1, 10, 12_345_678, 1, 1)), null)!;
        Assert.Equal("预计到达 11.0M – 14.0M", card.ForecastRangeText);
    }

    [Fact]
    public void 批次不合法时不给预估()
    {
        var failure = new FailurePresentation(default, "自杀手", "无气", [], null, [], null);
        PowerCardView card = PowerCard.Build(Power(Row(P1, 15, 12, 1)), P1, Preview(P1, 1, failure, Change(P1, 15, 40, 1, 1)), null)!;
        Assert.Equal(PowerForecastKind.Invalid, card.Forecast);
        Assert.Equal("批次不合法，暂无预估", card.ForecastText);
    }

    [Fact]
    public void 演出中卡片与排名栏同步滚动()
    {
        var change = new PowerChange(P1, 20, 29, 2, 1, TerritoryDelta: 3, GroupDelta: 6);
        var timeline = new ShowTimeline([new PowerBeat([change])], ShowDuration.Normal);
        timeline.Advance(175);
        PowerCardView card = PowerCard.Build(Power(Row(P1, 29, 15, 1)), P1, null, timeline.Mask().Power[P1])!;
        Assert.Equal("21", card.ValueText);
        Assert.Equal("领地 +3", card.LiveDeltaText);
        Assert.Equal(string.Empty, card.BreakdownText);
        Assert.Equal("蓝方 · 第 1 名", card.TitleText);

        var down = new PowerChange(P1, 15, 12, 1, 1, TerritoryDelta: -3, GroupDelta: 0);
        timeline = new ShowTimeline([new PowerBeat([down])], ShowDuration.Normal);
        timeline.Advance(175);
        card = PowerCard.Build(Power(Row(P1, 12, 9, 1)), P1, null, timeline.Mask().Power[P1])!;
        Assert.Equal("领地 −3", card.LiveDeltaText);
        Assert.True(card.LiveDeltaNegative);
    }

    [Fact]
    public void 地图飘字从零滚到总增量()
    {
        var burst = new PowerBurst(new PowerChange(P1, 300, 1284, 3, 1, TerritoryDelta: 184, GroupDelta: 800), forViewer: false);
        int s = PowerBurst.SegmentMs;
        Assert.Equal(s + s + PowerBurst.FinaleMs + PowerBurst.HoldMs + PowerBurst.FadeMs, burst.DurationMs);

        Assert.Equal((PowerBurstPhase.Territory, "+0"), (burst.At(0).Phase, burst.At(0).ShownDeltaText));
        PowerBurstFrame territory = burst.At(s / 2);
        Assert.Equal("领地 +184", territory.StageText);
        Assert.InRange(territory.ShownDelta, BigInteger.Zero, new BigInteger(184));
        Assert.Equal((PowerBurstPhase.Group, "+184", "军势 +800"), (burst.At(s).Phase, burst.At(s).ShownDeltaText, burst.At(s).StageText));
        Assert.Equal((PowerBurstPhase.Finale, "+984"), (burst.At(s + s).Phase, burst.At(s + s).ShownDeltaText));
        Assert.Equal(PowerBurstPhase.Fade, burst.At(s + s + PowerBurst.FinaleMs + PowerBurst.HoldMs).Phase);
        Assert.Equal(PowerBurstPhase.Done, burst.At(burst.DurationMs).Phase);
        Assert.Equal("名次 3 → 1", burst.RankText);

        // 其他玩家的飘字不飞卡片：没有飞入 / 飙升段。
        Assert.DoesNotContain(PowerBurstPhase.Fly, Enumerable.Range(0, burst.DurationMs).Select(ms => burst.At(ms).Phase));

        // 飘字单调不减（缓出插值不回跳）。
        BigInteger last = 0;
        for (int ms = 0; ms <= burst.DurationMs; ms += 10)
        {
            BigInteger v = burst.At(ms).ShownDelta;
            Assert.True(v >= last, $"{ms} ms: {v} < {last}");
            last = v;
        }
    }

    [Fact]
    public void 本机飘字定格后飞入卡片并飙升()
    {
        var burst = new PowerBurst(new PowerChange(P1, 300, 1284, 3, 1, TerritoryDelta: 184, GroupDelta: 800), forViewer: true);
        int settled = (2 * PowerBurst.SegmentMs) + PowerBurst.FinaleMs + PowerBurst.HoldMs;
        Assert.Equal(settled + PowerBurst.FlyMs + PowerBurst.SoarMs, burst.DurationMs);

        // 光飞到之前，卡片保持旧值（不提前泄露新值）。
        Assert.Equal("300", burst.At(0).CardValueText);
        Assert.Equal((PowerBurstPhase.Fly, "300"), (burst.At(settled).Phase, burst.At(settled).CardValueText));

        PowerBurstFrame soar = burst.At(settled + PowerBurst.FlyMs + (PowerBurst.SoarMs / 2));
        Assert.Equal(PowerBurstPhase.Soar, soar.Phase);
        Assert.InRange(soar.CardValue, new BigInteger(301), new BigInteger(1283));
        Assert.Equal((PowerBurstPhase.Done, "1284"), (burst.At(burst.DurationMs).Phase, burst.At(burst.DurationMs).CardValueText));

        BigInteger last = 0;
        for (int ms = 0; ms <= burst.DurationMs; ms += 10)
        {
            BigInteger v = burst.At(ms).CardValue;
            Assert.True(v >= last, $"{ms} ms: {v} < {last}");
            last = v;
        }
    }

    [Fact]
    public void 分项为零的段跳过且只播上涨()
    {
        var groupOnly = new PowerBurst(new PowerChange(P1, 10, 18, 1, 1, TerritoryDelta: 0, GroupDelta: 8), forViewer: false);
        Assert.Equal(PowerBurstPhase.Group, groupOnly.At(0).Phase);
        Assert.DoesNotContain(PowerBurstPhase.Territory, Enumerable.Range(0, groupOnly.DurationMs).Select(ms => groupOnly.At(ms).Phase));

        Assert.True(PowerBurst.Wants(new PowerChange(P1, 10, 18, 1, 1)));
        Assert.False(PowerBurst.Wants(new PowerChange(P1, 10, 10, 1, 1)));
        Assert.False(PowerBurst.Wants(new PowerChange(P1, 10, 7, 1, 1)));
        Assert.Equal(new[] { 0, 1000, 750 }, new[] { PowerBurst.EaseOut(0), PowerBurst.EaseOut(1000), PowerBurst.EaseOut(500) });
    }

    [Fact]
    public void 高档位收束才轻震()
    {
        var small = new PowerBurst(new PowerChange(P1, 10, 15, 1, 1), forViewer: false);
        int finale = PowerBurst.SegmentMs;
        Assert.Null(small.ShakePermilleAt(small.At(finale)));

        var big = new PowerBurst(new PowerChange(P1, 10, 1010, 1, 1), forViewer: false);
        Assert.Equal(0, big.ShakePermilleAt(big.At(finale)));
        Assert.NotNull(big.ShakePermilleAt(big.At(finale + 100)));
        Assert.Null(big.ShakePermilleAt(big.At(0)));
        Assert.Null(big.ShakePermilleAt(big.At(finale + PowerBurst.FinaleMs - 1)));
    }
}

