namespace Siege.Core.Board.Maps;

/// <summary><see cref="FrontierMapLayout"/> 自检：全图一块、走廊宽度、一子堵死。</summary>
internal sealed partial class FrontierMapLayout
{
    private bool CheckSingleRegion(out string reason)
    {
        reason = IsSingleRegion() ? string.Empty : "可落子格没有沿气边连成一块。";
        return reason.Length == 0;
    }

    /// <summary>
    /// 走廊宽度的终检（与测试同口径）：每个过渡带格都属于某个 2×2 过渡带方块；且没有"一子堵死"的格。
    /// 前者由拓宽与补空地的做法保证，后者主要防两个方块只搭一个角、栅栏拦道这类情形。
    /// </summary>
    private bool CheckCorridorWidth(out string reason)
    {
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                if (IsLow(x, y) && !IsWide(new P(x, y)))
                {
                    reason = $"走廊在 ({x},{y}) 只有 1 格宽。";
                    return false;
                }
            }
        }

        if (FindChokeCell() is { } choke)
        {
            reason = $"在 ({choke.X},{choke.Y}) 落一枚子就能把某个平台与中央入口隔开。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 按坐标序找第一个"一子堵死"的过渡带格：拿掉它之后，从中央入口沿气边走不到某个平台
    /// （拿掉的是中央入口自身时，从 1 号平台出发，要求走得到其余全部平台）。没有则为 <c>null</c>。
    /// </summary>
    private P? FindChokeCell()
    {
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                if (IsLow(x, y) && !ReachesAllPlatformsWithout(new P(x, y)))
                {
                    return new P(x, y);
                }
            }
        }

        return null;
    }

    private bool ReachesAllPlatformsWithout(P removed)
    {
        P start = Entrance;
        if (removed == Entrance)
        {
            // 平台是整块方块（平台内无障碍），1 号平台的西南角必为平台格。
            start = new P(Platforms[0].X, Platforms[0].Y);
        }

        var reachedZone = new bool[_n];
        var seen = new bool[W, H];
        var queue = new Queue<P>();
        queue.Enqueue(start);
        seen[start.X, start.Y] = true;
        seen[removed.X, removed.Y] = true;
        while (queue.Count > 0)
        {
            P cur = queue.Dequeue();
            if (Zone[cur.X, cur.Y] >= 0)
            {
                reachedZone[Zone[cur.X, cur.Y]] = true;
            }

            foreach (P d in Dirs)
            {
                var n = new P(cur.X + d.X, cur.Y + d.Y);
                if (!InMap(n) || seen[n.X, n.Y] || !IsPlayable(n) || Math.Abs(HeightOf(n) - HeightOf(cur)) > 1 || HasFence(cur, n))
                {
                    continue;
                }

                seen[n.X, n.Y] = true;
                queue.Enqueue(n);
            }
        }

        return Array.TrueForAll(reachedZone, r => r);
    }

    private bool IsPlayable(P p) => Cells[p.X, p.Y] is Cell.Platform or Cell.Ramp or Cell.Ground or Cell.Bridge;

    private int HeightOf(P p) => Cells[p.X, p.Y] switch
    {
        Cell.Platform => 2,
        Cell.Ramp => 1,
        _ => 0,
    };

    private int CountPlayable()
    {
        int count = 0;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                count += IsPlayable(new P(x, y)) ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>
    /// 与校验器同口径的连通判定：气边 = 两格可落子 ∧ 高差 ≤ 1 ∧ 之间无栅栏。全部可落子格连成一块 ⇒ 不存在任何口袋，
    /// 各平台到五类目标也必然可达。最终仍以 <see cref="MapValidator"/> 为准，这里只为让撒点 / 放栅栏能当场撤回。
    /// </summary>
    private bool IsSingleRegion()
    {
        int total = CountPlayable();
        var seen = new bool[W, H];
        var queue = new Queue<P>();
        queue.Enqueue(Entrance);
        seen[_cx, _cy] = true;
        int reached = 0;
        while (queue.Count > 0)
        {
            P cur = queue.Dequeue();
            reached++;
            foreach (P d in Dirs)
            {
                var n = new P(cur.X + d.X, cur.Y + d.Y);
                if (!InMap(n) || seen[n.X, n.Y] || !IsPlayable(n) || Math.Abs(HeightOf(n) - HeightOf(cur)) > 1 || HasFence(cur, n))
                {
                    continue;
                }

                seen[n.X, n.Y] = true;
                queue.Enqueue(n);
            }
        }

        return reached == total;
    }

    private bool HasFence(P a, P b)
    {
        foreach ((P fa, P fb) in Fences)
        {
            if ((fa == a && fb == b) || (fa == b && fb == a))
            {
                return true;
            }
        }

        return false;
    }
}
