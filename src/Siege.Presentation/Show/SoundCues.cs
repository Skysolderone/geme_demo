using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Presentation.Style;

namespace Siege.Presentation.Show;

/// <summary>
/// 演出音效提示的种类（show-sound-cues design.md D1）。枚举的整数值即同一帧内的稳定排序序号：按节拍顺序 落子 → 提子 → 信物 → 揭示 → 领地 → 军势 → 横幅
/// （tiered-number-show D7 在信物之后、领地之前插入"揭示"；序号只在内存使用、不落盘）。
/// </summary>
public enum SoundCueKind
{
    /// <summary>落子"嗒"：某枚棋子由"未落下"变为"已落下"（压缩落子整批一次）。</summary>
    Placement,

    /// <summary>提子"啪"：跨入提子节拍。</summary>
    Capture,

    /// <summary>信物"叮"：跨入信物揭示节拍。</summary>
    Relic,

    /// <summary>揭示短音：军势揭示节拍的每一步开始。</summary>
    Reveal,

    /// <summary>领地到账"咣"：某玩家势力段进入领地段。</summary>
    Territory,

    /// <summary>军势到账（更低更重的"咣"）：某玩家势力段进入军势段。</summary>
    Group,

    /// <summary>横幅低音：横幅节拍里每一条开始。</summary>
    Banner,
}

/// <summary>
/// 一个音效提示（tiered-number-show D7）：种类 + 数值档位。揭示取该步的档位，领地到账与军势到账取该段增量的档位，其余恒为一档；
/// 图形侧按档位给揭示 / 领地 / 军势三种升调（<see cref="NumberTierStyle.PitchSemitones"/>）。
/// </summary>
public readonly record struct SoundCue(SoundCueKind Kind, int Tier = NumberTier.Lowest);

/// <summary>
/// 由时间线一次推进的结果纯函数地导出音效提示（settlement-show「演出音效提示」，design.md D1）：只比较推进前后的遮罩与本次跨过的节拍，
/// 不读时钟、不消费随机、不修改任何状态；同一帧内同种提示合并为一个、档位取其中最高的，输出按 <see cref="SoundCueKind"/> 的枚举序排列。
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

        ImmutableArray<SoundCue>.Builder cues = ImmutableArray.CreateBuilder<SoundCue>(7);

        // 落子：某格由"尚未落下"（在 Hidden 里）变为"已落下 / 正在落下"（不在 Hidden 里，含播完后的空遮罩）。每枚一次、同帧合并。
        if (PlacementAppeared(before, after))
        {
            cues.Add(new SoundCue(SoundCueKind.Placement));
        }

        // 提子 / 信物：之前不是当前节拍，且（之后是当前节拍 或 本帧跨过了该节拍）。
        if (before.CaptureSummary is null && (after.CaptureSummary is not null || passed.Any(b => b is CaptureBeat)))
        {
            cues.Add(new SoundCue(SoundCueKind.Capture));
        }

        if (before.RelicFlash.IsEmpty && (!after.RelicFlash.IsEmpty || passed.Any(b => b is RelicRevealBeat)))
        {
            cues.Add(new SoundCue(SoundCueKind.Relic));
        }

        // 揭示：军势揭示节拍里本帧新开始的步（之后仍在该节拍里按已开始的步计；跨过该节拍按其全部步计）。同帧多步合并，取最高档。
        int reveal = RevealTierStarted(before, after, passed);
        if (reveal > 0)
        {
            cues.Add(new SoundCue(SoundCueKind.Reveal, reveal));
        }

        // 领地 / 军势：某玩家的段由"该段之前"跨到"该段或之后"，且该玩家确实有这一段（增量为 0 的段被跳过，不发声）。
        // 档位取该段增量的档；同帧多名玩家进入同一段合并，取最高档。
        (int territory, int group) = PowerStagesEntered(before, after);
        if (territory > 0)
        {
            cues.Add(new SoundCue(SoundCueKind.Territory, territory));
        }

        if (group > 0)
        {
            cues.Add(new SoundCue(SoundCueKind.Group, group));
        }

        // 横幅：已开始的条数增加（之后仍在横幅节拍里按当前序号计；跨过横幅节拍按其条数计）。
        if (BannersStarted(after, passed) > BannersStarted(before, []))
        {
            cues.Add(new SoundCue(SoundCueKind.Banner));
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

    /// <summary>
    /// 本帧新开始的揭示步里最高的档位；没有新开始的步为 0。推进后仍在军势揭示节拍里：取遮罩给出的已开始各步；
    /// 不在而本帧跨过了它：取其全部步。推进前已开始的步数从中扣除（推进前不在该节拍里即 0）。
    /// </summary>
    private static int RevealTierStarted(ShowMask before, ShowMask after, ImmutableArray<SettlementBeat> passed)
    {
        ImmutableArray<int> started = after.RevealStepTiers;
        if (started.IsEmpty)
        {
            foreach (SettlementBeat beat in passed)
            {
                if (beat is PowerRevealBeat reveal)
                {
                    started = reveal.StepTiersStartedBy(reveal.DurationMs);
                    break;
                }
            }
        }

        int highest = 0;
        for (int i = before.RevealStepTiers.Length; i < started.Length; i++)
        {
            highest = Math.Max(highest, started[i]);
        }

        return highest;
    }

    /// <summary>本帧进入领地段 / 军势段的玩家里各自最高的增量档位；没有玩家进入该段为 0。</summary>
    private static (int Territory, int Group) PowerStagesEntered(ShowMask before, ShowMask after)
    {
        int territory = 0;
        int group = 0;
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

            if (!change.TerritoryDelta.IsZero && from < Rank(PowerStage.Territory) && to >= Rank(PowerStage.Territory))
            {
                territory = Math.Max(territory, change.TerritoryTier);
            }

            if (!change.GroupDelta.IsZero && from < Rank(PowerStage.Group) && to >= Rank(PowerStage.Group))
            {
                group = Math.Max(group, change.GroupTier);
            }
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
