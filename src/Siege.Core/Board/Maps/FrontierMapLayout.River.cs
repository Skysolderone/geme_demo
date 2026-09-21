namespace Siege.Core.Board.Maps;

/// <summary><see cref="FrontierMapLayout"/> 第 4 步：主河与桥——河先于走廊流过去，走廊只能在河道笔直处垂直过河。</summary>
internal sealed partial class FrontierMapLayout
{
    // 次序上主河先于走廊：先让河在空地里流过去，走廊再去垂直地跨它。反过来（走廊网刻好之后再找河道）时，带与带之间只有 2 格、
    // 整条都是走廊，河只能顺着走廊流，出来的是 4–6 格长的"桥"。

    /// <summary>
    /// 一格宽的主河，从一条边贯穿到对边，只流经还没决定用途的格（避开平台、缓坡、缓坡口与中央广场）。
    /// 要求两岸都有走廊网的结点——这样走廊网必然过河。几次都流不通则本次尝试作废。
    /// </summary>
    private bool RunRiver(out string reason)
    {
        int blocked = 0;
        int oneSided = 0;
        for (int t = 0; t < RiverTries; t++)
        {
            // 多数时候垂直于三条带流：带内平台之间的空当比带与带之间的 2 格缝宽，河才过得去；偶尔顺着带流（带间够宽时才流得通）。
            bool vertical = _rng.NextInt(4) == 0 ? _bandsVertical : !_bandsVertical;
            var noise = new int[W, H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    noise[x, y] = _rng.NextInt(6);
                }
            }

            // 端点只取"笔直往里三格都流得进去"的边缘格（正对着贴边平台的位置进不了图）。
            List<P> sources = RiverMouths(vertical, true);
            List<P> sinks = RiverMouths(vertical, false);
            List<P> passes = RiverPasses(vertical);
            List<P>? path = null;
            if (sources.Count > 0 && sinks.Count > 0 && passes.Count > 0)
            {
                // 先定隘口，再在离它最近的几个端点里取：河大体直着流过去，不满图兜圈子。
                P pass = passes[_rng.NextInt(passes.Count)];
                P from = PickNear(sources, pass);
                P to = PickNear(sinks, pass);
                path = FindRiverVia(from, pass, to, noise);
            }

            if (path is null)
            {
                blocked++;
                continue;
            }

            if (!SplitsRamps(path))
            {
                oneSided++;
                continue;
            }

            _river = path;
            for (int i = 0; i < path.Count; i++)
            {
                Cells[path[i].X, path[i].Y] = Cell.River;
                _riverIndex[path[i].X, path[i].Y] = i + 1;
            }

            reason = string.Empty;
            return true;
        }

        reason = $"主河改道 {RiverTries} 次仍不合格：流不通 {blocked} 次、缓坡全在同一岸 {oneSided} 次。";
        return false;
    }

    /// <summary>在离 <paramref name="pass"/> 最近的至多 4 个端点里随机取一个；远近相同按坐标序（插入排序，稳定）。</summary>
    private P PickNear(List<P> mouths, P pass)
    {
        var sorted = new List<P>();
        foreach (P m in mouths)
        {
            int at = sorted.Count;
            while (at > 0 && Manhattan(sorted[at - 1], pass) > Manhattan(m, pass))
            {
                at--;
            }

            sorted.Insert(at, m);
        }

        return sorted[_rng.NextInt(Math.Min(4, sorted.Count))];
    }

    /// <summary>主河可用的端点，按坐标序。竖河：上缘入、下缘出；横河：左缘入、右缘出。四角附近不取。</summary>
    private List<P> RiverMouths(bool vertical, bool source)
    {
        var mouths = new List<P>();
        int length = vertical ? W : H;
        for (int k = EdgeMargin; k <= length - 1 - EdgeMargin; k++)
        {
            bool open = true;
            for (int depth = 0; depth <= EdgeMargin && open; depth++)
            {
                int across = source == vertical ? (vertical ? H : W) - 1 - depth : depth;
                P p = vertical ? new P(k, across) : new P(across, k);
                open = Cells[p.X, p.Y] == Cell.Fill;
            }

            if (open)
            {
                int rim = source == vertical ? (vertical ? H : W) - 1 : 0;
                mouths.Add(vertical ? new P(k, rim) : new P(rim, k));
            }
        }

        return mouths;
    }

    /// <summary>
    /// 河道必经的"隘口"候选，按坐标序：竖河取左右两侧 7 格之内都有平台（或中央广场）的空格，横河取上下两侧——
    /// 河从两块高地之间穿过去，才会把缓坡分在两岸；任它自己挑最便宜的路，它只会贴着外圈绕开所有平台。
    /// </summary>
    private List<P> RiverPasses(bool vertical)
    {
        var passes = new List<P>();
        P side = vertical ? new P(1, 0) : new P(0, 1);
        for (int y = EdgeMargin; y <= H - 1 - EdgeMargin; y++)
        {
            for (int x = EdgeMargin; x <= W - 1 - EdgeMargin; x++)
            {
                var p = new P(x, y);
                if (Cells[x, y] == Cell.Fill && !TouchesCell(p, Cell.Ground) && !TouchesCell(p, Cell.Ramp)
                    && SeesHighGround(p, side) && SeesHighGround(p, new P(-side.X, -side.Y)))
                {
                    passes.Add(p);
                }
            }
        }

        return passes;
    }

    private bool SeesHighGround(P from, P step)
    {
        for (int k = 1; k <= 7; k++)
        {
            var p = new P(from.X + (k * step.X), from.Y + (k * step.Y));
            if (!InMap(p))
            {
                return false;
            }

            if (Zone[p.X, p.Y] >= 0 || Plaza[p.X, p.Y])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>经隘口的河道 = 入图 → 隘口、隘口 → 出图两段；后一段不碰、也不挨着前一段（河不自交、不并流）。</summary>
    private List<P>? FindRiverVia(P from, P pass, P to, int[,] noise)
    {
        List<P>? first = FindRiver(from, pass, noise, null);
        if (first is null)
        {
            return null;
        }

        var taken = new bool[W, H];
        for (int i = 0; i < first.Count - 1; i++)
        {
            P c = first[i];
            taken[c.X, c.Y] = true;
            if (i == first.Count - 2)
            {
                continue;       // 紧挨隘口的那一格：它的邻格里有隘口自己的邻格，不封
            }

            foreach (P d in Dirs)
            {
                var n = new P(c.X + d.X, c.Y + d.Y);
                if (InMap(n))
                {
                    taken[n.X, n.Y] = true;
                }
            }
        }

        taken[pass.X, pass.Y] = false;
        List<P>? second = FindRiver(pass, to, noise, taken);
        if (second is null)
        {
            return null;
        }

        first.AddRange(second.GetRange(1, second.Count - 1));
        return first;
    }

    /// <summary>
    /// 河道：带方向状态的最短路（整数代价，键 = 代价 × 2³² + 入队序号，全序无并列）。每格的代价带一点随机噪声（河道蜿蜒），
    /// 拐弯加价（留出笔直的河段，走廊才有地方垂直过河），贴着缓坡与缓坡口更贵（别把缓坡口堵在河湾里）。
    /// </summary>
    private List<P>? FindRiver(P from, P to, int[,] noise, bool[,]? taken)
    {
        var dist = new int[W, H, 4];
        var prev = new int[W, H, 4];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                for (int d = 0; d < 4; d++)
                {
                    dist[x, y, d] = int.MaxValue;
                    prev[x, y, d] = -1;
                }
            }
        }

        var queue = new PriorityQueue<(int X, int Y, int D), long>();
        long seq = 0;
        for (int d = 0; d < 4; d++)
        {
            dist[from.X, from.Y, d] = 0;
            queue.Enqueue((from.X, from.Y, d), seq++);
        }

        while (queue.TryDequeue(out (int X, int Y, int D) cur, out long key))
        {
            int cost = (int)(key >> 32);
            if (cost > dist[cur.X, cur.Y, cur.D])
            {
                continue;
            }

            if (cur.X == to.X && cur.Y == to.Y)
            {
                var path = new List<P>();
                (int x, int y, int d) = cur;
                while (true)
                {
                    path.Add(new P(x, y));
                    int pd = prev[x, y, d];
                    if (pd < 0)
                    {
                        break;
                    }

                    (x, y, d) = (x - Dirs[d].X, y - Dirs[d].Y, pd);
                }

                path.Reverse();
                return path;
            }

            for (int nd = 0; nd < 4; nd++)
            {
                var next = new P(cur.X + Dirs[nd].X, cur.Y + Dirs[nd].Y);
                if (!InMap(next) || Cells[next.X, next.Y] != Cell.Fill || (taken is not null && taken[next.X, next.Y]))
                {
                    continue;
                }

                // 河不沿着外缘两圈流（两端笔直入图 / 出图的那两格除外）：贴边流等于没有河，也碰不到几条走廊。
                int edge = Math.Min(Math.Min(next.X, W - 1 - next.X), Math.Min(next.Y, H - 1 - next.Y));
                if (edge < EdgeMargin && Manhattan(next, from) > 1 && Manhattan(next, to) > 1)
                {
                    continue;
                }

                // 贴着缓坡 / 缓坡口很贵（别把缓坡口堵在河湾里），贴着平台略贵（岸上留出刻走廊、架桥的地方）。
                bool crowded = TouchesCell(next, Cell.Ground) || TouchesCell(next, Cell.Ramp);
                int step = 3 + noise[next.X, next.Y] + (nd != cur.D ? 6 : 0) + (crowded ? 8 : 0) + (TouchesCell(next, Cell.Platform) ? 3 : 0);
                int total = cost + step;
                if (total < dist[next.X, next.Y, nd])
                {
                    dist[next.X, next.Y, nd] = total;
                    prev[next.X, next.Y, nd] = cur.D;
                    queue.Enqueue((next.X, next.Y, nd), ((long)total << 32) | seq++);
                }
            }
        }

        return null;
    }

    /// <summary>河道是否把缓坡分在了两岸：从中央入口出发、不过河（四邻相通，不论地形）走不到全部缓坡格。</summary>
    private bool SplitsRamps(List<P> path)
    {
        var seen = new bool[W, H];
        foreach (P c in path)
        {
            seen[c.X, c.Y] = true;
        }

        var queue = new Queue<P>();
        queue.Enqueue(Entrance);
        seen[_cx, _cy] = true;
        while (queue.Count > 0)
        {
            P cur = queue.Dequeue();
            foreach (P d in Dirs)
            {
                var n = new P(cur.X + d.X, cur.Y + d.Y);
                if (InMap(n) && !seen[n.X, n.Y])
                {
                    seen[n.X, n.Y] = true;
                    queue.Enqueue(n);
                }
            }
        }

        foreach (Ramp ramp in _ramps)
        {
            if (!seen[ramp.Cells[0].X, ramp.Cells[0].Y])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 朝 <paramref name="heading"/> 方向走的走廊能否在该河格上架桥（或走现成的桥）：河道在此笔直且与行进方向垂直；
    /// 上桥前、下桥后的两格刻得开；架上之后这座桥沿河不超过 <see cref="MaxBridgeSpan"/> 格。
    /// <paramref name="newCrossingOnly"/>：现成的桥不算，且离现成的桥至少隔 2 格河（并排拓宽一格之后仍是另一座桥）。
    /// </summary>
    private bool CanBridge(P p, P heading, bool newCrossingOnly)
    {
        if (!InCorridorArea(p) || Cells[p.X, p.Y] is not (Cell.River or Cell.Bridge))
        {
            return false;
        }

        int i = _riverIndex[p.X, p.Y] - 1;
        if (i < 1 || i > _river.Count - 2)
        {
            return false;
        }

        var flow = new P(_river[i + 1].X - p.X, _river[i + 1].Y - p.Y);
        bool straight = _river[i - 1] == new P(p.X - flow.X, p.Y - flow.Y);
        bool perpendicular = (flow.X * heading.X) + (flow.Y * heading.Y) == 0;
        if (!straight || !perpendicular)
        {
            return false;
        }

        foreach (P bank in new[] { new P(p.X + heading.X, p.Y + heading.Y), new P(p.X - heading.X, p.Y - heading.Y) })
        {
            if (!InCorridorArea(bank) || Cells[bank.X, bank.Y] is not (Cell.Fill or Cell.Ground))
            {
                return false;
            }
        }

        if (newCrossingOnly)
        {
            for (int k = Math.Max(0, i - 2); k <= Math.Min(_river.Count - 1, i + 2); k++)
            {
                if (Cells[_river[k].X, _river[k].Y] == Cell.Bridge)
                {
                    return false;
                }
            }

            return true;
        }

        if (Cells[p.X, p.Y] == Cell.Bridge)
        {
            return true;
        }

        int lo = i;
        int hi = i;
        while (lo > 0 && Cells[_river[lo - 1].X, _river[lo - 1].Y] == Cell.Bridge)
        {
            lo--;
        }

        while (hi < _river.Count - 1 && Cells[_river[hi + 1].X, _river[hi + 1].Y] == Cell.Bridge)
        {
            hi++;
        }

        return hi - lo + 1 <= MaxBridgeSpan;
    }

    /// <summary>
    /// 走廊网刻完之后数桥：相邻桥格算一座。不足 2 座时，挑两岸各一个结点、按距离从近到远，逼一条不走现成桥的走廊出来；
    /// 仍不足 2 座、某座桥沿河超过 <see cref="MaxBridgeSpan"/> 格、或桥格总数超过 <see cref="MaxBridgeCells"/>，本次尝试作废。
    /// </summary>
    private bool EnsureBridges(out string reason)
    {
        (int groups, int cells, int longest) = CountBridges();
        if (groups == 1)
        {
            bool[,] nearBank = SameBankAsEntrance();
            var pairs = new List<(int Distance, int A, int B)>();
            for (int a = 0; a < _nodes.Count; a++)
            {
                for (int b = a + 1; b < _nodes.Count; b++)
                {
                    if (nearBank[_nodes[a].X, _nodes[a].Y] != nearBank[_nodes[b].X, _nodes[b].Y])
                    {
                        pairs.Add((Distance(a, b), a, b));
                    }
                }
            }

            // 键（距离, a, b）两两不同：全序，排序结果不依赖排序算法是否稳定。
            pairs.Sort();
            foreach ((int _, int a, int b) in pairs)
            {
                if (CarveEdge(a, b, newCrossingOnly: true))
                {
                    break;
                }
            }

            (groups, cells, longest) = CountBridges();
        }

        if (groups < 2 || longest > MaxBridgeSpan || cells > MaxBridgeCells)
        {
            reason = $"桥不合格：{groups} 座、共 {cells} 格、最长一座沿河 {longest} 格（要求至少 2 座、每座不超过 {MaxBridgeSpan} 格、共不超过 {MaxBridgeCells} 格）。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private (int Groups, int Cells, int Longest) CountBridges()
    {
        int groups = 0;
        int cells = 0;
        int longest = 0;
        int span = 0;
        foreach (P c in _river)
        {
            bool bridge = Cells[c.X, c.Y] == Cell.Bridge;
            groups += bridge && span == 0 ? 1 : 0;
            cells += bridge ? 1 : 0;
            span = bridge ? span + 1 : 0;
            longest = Math.Max(longest, span);
        }

        return (groups, cells, longest);
    }

    /// <summary>与中央入口同岸的格（不过河、不过桥，四邻相通，不论地形）。</summary>
    private bool[,] SameBankAsEntrance()
    {
        var bank = new bool[W, H];
        var queue = new Queue<P>();
        queue.Enqueue(Entrance);
        bank[_cx, _cy] = true;
        while (queue.Count > 0)
        {
            P cur = queue.Dequeue();
            foreach (P d in Dirs)
            {
                var n = new P(cur.X + d.X, cur.Y + d.Y);
                if (InMap(n) && !bank[n.X, n.Y] && _riverIndex[n.X, n.Y] == 0)
                {
                    bank[n.X, n.Y] = true;
                    queue.Enqueue(n);
                }
            }
        }

        return bank;
    }
}
