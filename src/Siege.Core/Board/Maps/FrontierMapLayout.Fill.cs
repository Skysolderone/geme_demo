namespace Siege.Core.Board.Maps;

/// <summary><see cref="FrontierMapLayout"/> 第 5 步：填充与点缀——外海与岩石、平台内撒岩石、补空地、林地、栅栏。</summary>
internal sealed partial class FrontierMapLayout
{
    private bool FillAndDecorate(out string reason)
    {
        FillRemainder();
        if (!ScatterPlatformRocks(out reason))
        {
            return false;
        }

        PlantForests();

        // 栅栏之前先查一遍"一子堵死"：已经有了就不必再试栅栏（哪一段都会被撤回），直接作废并说清原因。
        if (FindChokeCell() is { } choke)
        {
            reason = $"在 ({choke.X},{choke.Y}) 落一枚子就能把某个平台与中央入口隔开。";
            return false;
        }

        if (!RaiseFences())
        {
            reason = "放不下任何一段不拦出 1 格宽口子的栅栏。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>剩余格：最外一圈是外海，次外圈多半是水，内陆是岩石，外加几处小水塘（不挨主河——主河保持一格宽）。</summary>
    private void FillRemainder()
    {
        int ponds = 2 + _rng.NextInt(3);
        for (int k = 0; k < ponds; k++)
        {
            var center = new P(3 + _rng.NextInt(W - 6), 3 + _rng.NextInt(H - 6));
            int radius = 1 + _rng.NextInt(2);
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
            {
                for (int x = center.X - radius; x <= center.X + radius; x++)
                {
                    var p = new P(x, y);
                    if (Math.Abs(x - center.X) + Math.Abs(y - center.Y) <= radius && InMap(p)
                        && Cells[x, y] == Cell.Fill && !TouchesCell(p, Cell.River) && !TouchesCell(p, Cell.Bridge))
                    {
                        Cells[x, y] = Cell.Water;
                    }
                }
            }
        }

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                if (Cells[x, y] != Cell.Fill)
                {
                    continue;
                }

                int edge = Math.Min(Math.Min(x, W - 1 - x), Math.Min(y, H - 1 - y));
                bool water = edge == 0 || (edge == 1 && _rng.NextInt(10) < 7);
                Cells[x, y] = water ? Cell.Water : Cell.Rock;
            }
        }
    }

    private bool TouchesCell(P p, Cell kind)
    {
        foreach (P d in Dirs)
        {
            var n = new P(p.X + d.X, p.Y + d.Y);
            if (InMap(n) && Cells[n.X, n.Y] == kind)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 平台内撒岩石（每个平台 ≤ 外接面积的 20%），数量同时用来把可落子格总数调进目标区间；仍偏少则在走廊边上开几格空地 / 林地补足。
    /// 每撒一格立即自检：平台内可落子格必须仍连成一块（比"口袋面积 ≥ 8"更严，必然满足口袋规则），不合格的那一格撤回。
    /// 不撒在四角（保住外接方块）与缓坡正对的平台格上。
    /// </summary>
    private bool ScatterPlatformRocks(out string reason)
    {
        int playable = CountPlayable();
        int target = TargetMin + _rng.NextInt(TargetMax - TargetMin + 1);

        int[] min = new int[_n];
        int[] cap = new int[_n];
        int minTotal = 0;
        int capTotal = 0;
        for (int i = 0; i < _n; i++)
        {
            min[i] = Platforms[i].Side - 4;
            cap[i] = Platforms[i].Area / 5;
            minTotal += min[i];
            capTotal += cap[i];
        }

        int wanted = Math.Clamp(playable - target, minTotal, capTotal);
        int spare = wanted - minTotal;
        for (int i = 0; i < _n; i++)
        {
            int count = min[i] + (spare * (cap[i] - min[i]) / Math.Max(1, capTotal - minTotal));
            playable -= ScatterOn(i, count);
        }

        if (playable > BudgetMax)
        {
            reason = $"可落子格 {playable} 超出边疆档上限 {BudgetMax}，平台内的岩石已撒到 20%。";
            return false;
        }

        // 偏少：在走廊边上补空地（两成是林地），补到目标值为止。只补"补上就凑成一个 2×2 过渡带方块"的格——
        // 走廊变宽而不是长出 1 格宽的刺（走廊全长 ≥ 2 格宽的性质不被破坏）。
        while (playable < target)
        {
            var candidates = new List<P>();
            for (int y = 1; y <= H - 2; y++)
            {
                for (int x = 1; x <= W - 2; x++)
                {
                    var p = new P(x, y);
                    if (Cells[x, y] is (Cell.Rock or Cell.Water) && !TouchesCell(p, Cell.River) && CompletesBlock(p))
                    {
                        candidates.Add(p);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                // 没处可补了：只要离边疆档下限还有余量就到此为止，不为凑目标值长出 1 格宽的刺。
                if (playable >= BudgetMin + 10)
                {
                    break;
                }

                reason = $"可落子格只有 {playable}，走廊边上已无处可补。";
                return false;
            }

            P pick = candidates[_rng.NextInt(candidates.Count)];
            Cells[pick.X, pick.Y] = Cell.Ground;
            Forest[pick.X, pick.Y] = _rng.NextInt(10) < 2;
            playable++;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>把该格改成空地，就凑成一个四格全是过渡带格的 2×2 方块。</summary>
    private bool CompletesBlock(P p)
    {
        foreach (P d in new[] { new P(-1, -1), new P(1, -1), new P(-1, 1), new P(1, 1) })
        {
            if (IsLow(p.X + d.X, p.Y) && IsLow(p.X, p.Y + d.Y) && IsLow(p.X + d.X, p.Y + d.Y))
            {
                return true;
            }
        }

        return false;
    }

    private int ScatterOn(int platform, int count)
    {
        PlatformRect r = Platforms[platform];
        var candidates = new List<P>();
        for (int y = r.Y; y <= r.Y1; y++)
        {
            for (int x = r.X; x <= r.X1; x++)
            {
                bool corner = (x == r.X || x == r.X1) && (y == r.Y || y == r.Y1);
                if (!corner && !TouchesCell(new P(x, y), Cell.Ramp))
                {
                    candidates.Add(new P(x, y));
                }
            }
        }

        int placed = 0;
        while (placed < count && candidates.Count > 0)
        {
            int index = _rng.NextInt(candidates.Count);
            P pick = candidates[index];
            candidates.RemoveAt(index);
            Cells[pick.X, pick.Y] = Cell.PlatformRock;
            if (PlatformStaysWhole(r))
            {
                placed++;
            }
            else
            {
                Cells[pick.X, pick.Y] = Cell.Platform;
            }
        }

        return placed;
    }

    private bool PlatformStaysWhole(PlatformRect r)
    {
        int total = 0;
        P? start = null;
        for (int y = r.Y; y <= r.Y1; y++)
        {
            for (int x = r.X; x <= r.X1; x++)
            {
                if (Cells[x, y] == Cell.Platform)
                {
                    total++;
                    start ??= new P(x, y);
                }
            }
        }

        if (start is not { } s)
        {
            return false;
        }

        var seen = new bool[W, H];
        var queue = new Queue<P>();
        queue.Enqueue(s);
        seen[s.X, s.Y] = true;
        int reached = 0;
        while (queue.Count > 0)
        {
            P cur = queue.Dequeue();
            reached++;
            foreach (P d in Dirs)
            {
                var n = new P(cur.X + d.X, cur.Y + d.Y);
                if (r.Contains(n.X, n.Y) && !seen[n.X, n.Y] && Cells[n.X, n.Y] == Cell.Platform)
                {
                    seen[n.X, n.Y] = true;
                    queue.Enqueue(n);
                }
            }
        }

        return reached == total;
    }

    /// <summary>林地：广场四角取 1–2 个，走廊上再取 1–2 格（不挨缓坡、不在桥上）。</summary>
    private void PlantForests()
    {
        var corners = new List<P>
        {
            new(_cx - 2, _cy - 2), new(_cx + 2, _cy - 2), new(_cx - 2, _cy + 2), new(_cx + 2, _cy + 2),
        };
        int plazaForests = 1 + _rng.NextInt(2);
        for (int k = 0; k < plazaForests; k++)
        {
            int index = _rng.NextInt(corners.Count);
            Forest[corners[index].X, corners[index].Y] = true;
            corners.RemoveAt(index);
        }

        int corridorForests = 1 + _rng.NextInt(2);
        for (int k = 0; k < corridorForests; k++)
        {
            List<P> candidates = CorridorCells(p => !Forest[p.X, p.Y] && !TouchesCell(p, Cell.Ramp));
            if (candidates.Count == 0)
            {
                return;
            }

            P pick = candidates[_rng.NextInt(candidates.Count)];
            Forest[pick.X, pick.Y] = true;
        }
    }

    /// <summary>走廊格（h=0 空地、非广场），按坐标序。</summary>
    private List<P> CorridorCells(Func<P, bool> filter)
    {
        var cells = new List<P>();
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                var p = new P(x, y);
                if (Cells[x, y] == Cell.Ground && !Plaza[x, y] && filter(p))
                {
                    cells.Add(p);
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// 栅栏 1–2 段：两格相邻的空地之间（至少一端在走廊上）。每放一段立即自检：全图仍连成一块，且没有因此出现"一子堵死"的格
    /// （2 格宽的走廊拦掉一条道就只剩 1 格宽），否则撤回。
    /// </summary>
    private bool RaiseFences()
    {
        var candidates = new List<(P A, P B)>();
        var inPlaza = new List<(P A, P B)>();
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                if (Cells[x, y] != Cell.Ground)
                {
                    continue;
                }

                foreach (P d in new[] { Dirs[0], Dirs[2] })
                {
                    // 只在至少 3 格宽的地方拦：拦掉这条道，旁边还得剩两条并排的道（各在一侧，或同在一侧）。
                    var n = new P(x + d.X, y + d.Y);
                    if (!InMap(n) || Cells[n.X, n.Y] != Cell.Ground)
                    {
                        continue;
                    }

                    var across = new P(d.Y, d.X);
                    bool Lane(int k) => IsLow(x + (k * across.X), y + (k * across.Y)) && IsLow(n.X + (k * across.X), n.Y + (k * across.Y));
                    if ((Lane(1) && Lane(-1)) || (Lane(1) && Lane(2)) || (Lane(-1) && Lane(-2)))
                    {
                        (Plaza[x, y] && Plaza[n.X, n.Y] ? inPlaza : candidates).Add((new P(x, y), n));
                    }
                }
            }
        }

        int wanted = 1 + _rng.NextInt(2);
        TryFences(candidates, wanted);
        if (Fences.Count == 0)
        {
            // 走廊全是 2 格宽、哪儿拦都会拦出 1 格宽的口子时，退到中央广场里面拦一段。
            TryFences(inPlaza, 1);
        }

        return Fences.Count > 0;

        void TryFences(List<(P A, P B)> from, int count)
        {
            for (int t = 0; t < 60 && Fences.Count < count && from.Count > 0; t++)
            {
                int index = _rng.NextInt(from.Count);
                (P A, P B) pick = from[index];
                from.RemoveAt(index);
                Fences.Add(pick);
                if (!IsSingleRegion() || FindChokeCell() is not null)
                {
                    Fences.RemoveAt(Fences.Count - 1);
                }
            }
        }
    }
}
