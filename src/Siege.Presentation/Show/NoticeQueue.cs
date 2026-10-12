using System;
using System.Collections.Generic;

namespace Siege.Presentation.Show;

/// <summary>
/// 他场结算提示条的显示队列（settlement-show「战场结算横幅」，board-link-mode 段 D-d）：多条依次显示、每条停留 <see cref="DwellMs"/> 后自动消失，不再覆盖式只留最近一条。
/// 零时长（无人值守）下入队即丢弃——提示条不显示，但入队的文案仍由调用方记日志。纯状态机，推进由调用方按帧间隔毫秒驱动。
/// </summary>
public sealed class NoticeQueue
{
    /// <summary>每条提示停留的时长（约 3 s）。</summary>
    public const int DwellMs = 3000;

    private readonly Queue<string> _pending = new();
    private readonly ShowDuration _duration;
    private int _elapsed;

    public NoticeQueue(ShowDuration duration)
    {
        _duration = duration;
    }

    /// <summary>正在显示的一条；没有为 <c>null</c>。</summary>
    public string? Current { get; private set; }

    /// <summary>排在后面还没显示的条数。</summary>
    public int PendingCount => _pending.Count;

    /// <summary>没有在显示、也没有排队。</summary>
    public bool IsEmpty => Current is null && _pending.Count == 0;

    /// <summary>当前这条已显示的毫秒数。</summary>
    public int ElapsedMs => _elapsed;

    /// <summary>入队若干条（按给定顺序）。零时长直接丢弃；正常时长下没有在显示的则第一条立即显示。</summary>
    public void Enqueue(IEnumerable<string> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        if (_duration == ShowDuration.Zero)
        {
            return;
        }

        foreach (string notice in notices)
        {
            if (!string.IsNullOrEmpty(notice))
            {
                _pending.Enqueue(notice);
            }
        }

        if (Current is null)
        {
            Next();
        }
    }

    /// <summary>推进 <paramref name="ms"/> 毫秒：当前这条满 <see cref="DwellMs"/> 即换下一条（多余的时间顺延给下一条），没有下一条则清空。</summary>
    public void Advance(int ms)
    {
        if (ms < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ms), ms, "推进的毫秒数不能为负。");
        }

        if (Current is null)
        {
            return;
        }

        _elapsed += ms;
        while (Current is not null && _elapsed >= DwellMs)
        {
            _elapsed -= DwellMs;
            Next();
        }
    }

    private void Next()
    {
        if (_pending.Count > 0)
        {
            Current = _pending.Dequeue();
        }
        else
        {
            Current = null;
            _elapsed = 0;
        }
    }
}
