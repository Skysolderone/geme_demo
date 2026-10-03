using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Presentation.Show;

/// <summary>
/// 演出音效提示的种类（show-sound-cues design.md D1）。枚举的整数值即同一帧内的稳定排序序号：按节拍顺序 落子 → 提子 → 信物 → 领地 → 军势 → 横幅。
/// </summary>
public enum SoundCue
{
    /// <summary>落子"嗒"：某枚棋子由"未落下"变为"已落下"（压缩落子整批一次）。</summary>
    Placement,

    /// <summary>提子"啪"：跨入提子节拍。</summary>
    Capture,

    /// <summary>信物"叮"：跨入信物揭示节拍。</summary>
    Relic,

    /// <summary>领地到账"咣"：某玩家势力段进入领地段。</summary>
    Territory,

    /// <summary>军势到账（更低更重的"咣"）：某玩家势力段进入军势段。</summary>
    Group,

    /// <summary>横幅低音：横幅节拍里每一条开始。</summary>
    Banner,
}

/// <summary>
/// 由时间线一次推进的结果纯函数地导出音效提示（settlement-show「演出音效提示」，design.md D1）：只比较推进前后的遮罩与本次跨过的节拍，
/// 不读时钟、不消费随机、不修改任何状态；同一帧内同种提示合并为一个，输出按 <see cref="SoundCue"/> 的枚举序排列。
/// </summary>
/// <remarks>
/// <para>"跨入"某节拍不能只看 <see cref="ShowTimeline.Advance"/>（它报告的是<b>播完</b>的节拍）：单帧可能跨入并跨过同一节拍，而播完后遮罩为空。
/// 所以每种提示同时看"之后是否处于该状态"与"本次是否跨过该节拍"两个来源。</para>
/// <para>创建时间线本身没有任何 <c>Advance</c> 跨越，而首拍在进度 0 就已"当前"（势力节拍在 0 ms 即领地段、提子 / 信物 / 横幅在 0 ms 即显示）：
/// 图形侧建时间线后 MUST 以 <see cref="ShowMask.Empty"/> 为"前"调一次本方法，否则首拍的那一声会漏掉。零时长模式下遮罩恒空、无跨过节拍，自然没有提示。</para>
/// </remarks>
public static class SoundCues
{
    /// <summary>遮罩里没有该玩家（势力节拍已跨过 / 播完）时的段序：全部段都已走完。</summary>
    private const int RankDone = 4;

    /// <summary>
    /// 导出 <paramref name="before"/>（推进前遮罩）到 <paramref name="after"/>（推进后遮罩）之间、连同本次跨过的节拍 <paramref name="crossed"/> 应发出的提示。
    /// </summary>
    public static ImmutableArray<SoundCue> Between(ShowMask before, ShowMask after, ImmutableArray<SettlementBeat> crossed)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ImmutableArray<SettlementBeat> passed = crossed.IsDefault ? [] : crossed;

        ImmutableArray<SoundCue>.Builder cues = ImmutableArray.CreateBuilder<SoundCue>(6);

        // 落子：某格由"尚未落下"（在 Hidden 里）变为"已落下 / 正在落下"（不在 Hidden 里，含播完后的空遮罩）。每枚一次、同帧合并。
        if (PlacementAppeared(before, after))
        {
            cues.Add(SoundCue.Placement);
        }

        // 提子 / 信物：之前不是当前节拍，且（之后是当前节拍 或 本帧跨过了该节拍）。
        if (before.CaptureSummary is null && (after.CaptureSummary is not null || passed.Any(b => b is CaptureBeat)))
        {
            cues.Add(SoundCue.Capture);
        }

        if (before.RelicFlash.IsEmpty && (!after.RelicFlash.IsEmpty || passed.Any(b => b is RelicRevealBeat)))
        {
            cues.Add(SoundCue.Relic);
        }

        // 领地 / 军势：某玩家的段由"该段之前"跨到"该段或之后"，且该玩家确实有这一段（增量为 0 的段被跳过，不发声）。
        (bool territory, bool group) = PowerStagesEntered(before, after);
        if (territory)
        {
            cues.Add(SoundCue.Territory);
        }

        if (group)
        {
            cues.Add(SoundCue.Group);
        }

        // 横幅：已开始的条数增加（之后仍在横幅节拍里按当前序号计；跨过横幅节拍按其条数计）。
        if (BannersStarted(after, passed) > BannersStarted(before, []))
        {
            cues.Add(SoundCue.Banner);
        }

        return cues.ToImmutable();
    }

    private static bool PlacementAppeared(ShowMask before, ShowMask after)
    {
        foreach (Coord coord in before.Hidden)
        {
            if (!after.Hidden.Contains(coord))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>段的序：未开始 0 → 领地 1 → 军势 2 → 定格 3；遮罩里没有该玩家（节拍已跨过 / 播完）记为 4，即全部段都已走完。</summary>
    private static int Rank(PowerStage stage) => stage switch
    {
        PowerStage.Pending => 0,
        PowerStage.Territory => 1,
        PowerStage.Group => 2,
        PowerStage.Hold => 3,
        _ => 3,
    };

    private static (bool Territory, bool Group) PowerStagesEntered(ShowMask before, ShowMask after)
    {
        bool territory = false;
        bool group = false;
        foreach (PlayerId player in before.Power.Keys.Concat(after.Power.Keys).Distinct())
        {
            // 之前不在遮罩里（创建瞬间）按未开始；之后不在遮罩里按全部走完。
            PowerDisplay? was = before.Power.TryGetValue(player, out PowerDisplay? b) ? b : null;
            PowerDisplay? now = after.Power.TryGetValue(player, out PowerDisplay? a) ? a : null;
            PowerChange change = (was ?? now)!.Change;
            int from = was is null ? 0 : Rank(was.Stage);
            int to = now is null ? RankDone : Rank(now.Stage);
            if (to <= from)
            {
                continue;
            }

            territory |= !change.TerritoryDelta.IsZero && from < Rank(PowerStage.Territory) && to >= Rank(PowerStage.Territory);
            group |= !change.GroupDelta.IsZero && from < Rank(PowerStage.Group) && to >= Rank(PowerStage.Group);
        }

        return (territory, group);
    }

    /// <summary>横幅节拍里已开始的条数：正在显示第 i 条即 i+1；不在横幅节拍里而本帧跨过了它即全部条数；否则 0。</summary>
    private static int BannersStarted(ShowMask mask, ImmutableArray<SettlementBeat> passed)
    {
        if (mask.Banner is { } banner)
        {
            return banner.Index + 1;
        }

        foreach (SettlementBeat beat in passed)
        {
            if (beat is BannerBeat banners)
            {
                return banners.Banners.Length;
            }
        }

        return 0;
    }
}
