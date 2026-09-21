namespace Siege.Core.Board.Maps;

/// <summary><see cref="FrontierMapLayout"/> 第 3 步（下）：走廊网——最小生成树、带方向状态的最短路、刻宽与拓宽（全长至少 2 格宽）。</summary>
internal sealed partial class FrontierMapLayout
{
    /// <summary>以中央广场与各缓坡口为结点，按曼哈顿距离建最小生成树，再加 1–2 条冗余边；每条边刻一条 2–3 格宽的 h=0 走廊。</summary>
    private bool CarveCorridors(out string reason)
    {
        // 结点 0 = 中央入口；其余 = 各缓坡（缓坡口中间那格；被别的缓坡占了就用缓坡格自身）。
        List<P> nodes = _nodes;
        List<int> owner = _nodeOwner;
        nodes.Add(Entrance);
        owner.Add(-1);
        foreach (Ramp ramp in _ramps)
        {
            P mouth = ramp.Mouth[ramp.Mouth.Length / 2];
            nodes.Add(Cells[mouth.X, mouth.Y] == Cell.Ground ? mouth : ramp.Cells[ramp.Cells.Length / 2]);
            owner.Add(ramp.Platform);
        }

        // Prim：并列按（树内结点下标, 树外结点下标）打破。
        var inTree = new bool[nodes.Count];
        inTree[0] = true;
        var edges = new List<(int A, int B)>();
        for (int added = 1; added < nodes.Count; added++)
        {
            int bestA = -1;
            int bestB = -1;
            for (int a = 0; a < nodes.Count; a++)
            {
                for (int b = 0; inTree[a] && b < nodes.Count; b++)
                {
                    if (!inTree[b] && (bestA < 0 || Distance(a, b) < Distance(bestA, bestB)))
                    {
                        (bestA, bestB) = (a, b);
                    }
                }
            }

            inTree[bestB] = true;
            edges.Add((bestA, bestB));
        }

        // 冗余边：不同平台之间、不太远、不在树里的结点对。
        int extra = 1 + _rng.NextInt(2);
        for (int e = 0; e < extra; e++)
        {
            var pairs = new List<(int A, int B)>();
            for (int a = 1; a < nodes.Count; a++)
            {
                for (int b = a + 1; b < nodes.Count; b++)
                {
                    if (owner[a] != owner[b] && Distance(a, b) <= 16 && !edges.Contains((a, b)) && !edges.Contains((b, a)))
                    {
                        pairs.Add((a, b));
                    }
                }
            }

            if (pairs.Count == 0)
            {
                break;
            }

            edges.Add(pairs[_rng.NextInt(pairs.Count)]);
        }

        foreach ((int a, int b) in edges)
        {
            if (!CarveEdge(a, b, newCrossingOnly: false))
            {
                reason = $"缓坡口 ({nodes[a].X},{nodes[a].Y}) 与 ({nodes[b].X},{nodes[b].Y}) 之间刻不出走廊。";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>两个结点的距离：曼哈顿距离；同一平台的两处缓坡之间加罚（别把生成树的边浪费在自家两个口之间）。</summary>
    private int Distance(int a, int b) => Manhattan(_nodes[a], _nodes[b]) + (_nodeOwner[a] == _nodeOwner[b] ? 6 : 0);

    /// <summary>在两个结点之间刻一条走廊；宽度、先横先纵、往中线哪一侧拓宽由随机源定。走不通返回 <c>false</c>。</summary>
    private bool CarveEdge(int a, int b, bool newCrossingOnly)
    {
        bool horizontalFirst = _rng.NextInt(2) == 0;
        int width = _rng.NextInt(10) < 7 ? 2 : 3;
        int sign = _rng.NextInt(2) == 0 ? 1 : -1;
        List<P>? path = FindCorridor(_nodes[a], _nodes[b], horizontalFirst, newCrossingOnly);
        if (path is null)
        {
            return false;
        }

        CarveAlong(path, width, sign);
        return true;
    }

    /// <summary>
    /// 走廊中线：带方向状态的最短路（整数代价）。已有走廊便宜（鼓励并线）、新开的格贵、拐弯另加代价（走廊尽量横平竖直）、
    /// 贴着最外一圈更贵。优先队列的键 = 代价 × 2³² + 入队序号，全序、无并列，结果不依赖堆的实现细节。
    /// <para>过河：只能在河道笔直处垂直穿过（见 <see cref="CanBridge"/>），在河上不拐弯；新架桥很贵，走现成的桥与走廊同价。
    /// <paramref name="newCrossingOnly"/> 为真时不许走现成的桥、也不许紧挨着现成的桥过河——用来逼出第二座桥。</para>
    /// </summary>
    private List<P>? FindCorridor(P from, P to, bool horizontalFirst, bool newCrossingOnly)
    {
        int[] order = horizontalFirst ? [0, 1, 2, 3] : [2, 3, 0, 1];
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
        foreach (int d in order)
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

            foreach (int nd in order)
            {
                var next = new P(cur.X + Dirs[nd].X, cur.Y + Dirs[nd].Y);
                if (!InCorridorArea(next))
                {
                    continue;
                }

                Cell cell = Cells[next.X, next.Y];
                bool isTarget = next.X == to.X && next.Y == to.Y;
                bool onWater = Cells[cur.X, cur.Y] is Cell.River or Cell.Bridge;
                bool ontoWater = cell is Cell.River or Cell.Bridge;
                if (onWater || ontoWater)
                {
                    // 河上不拐弯；上桥的方向必须与河垂直、两岸刻得开，且至少有一条并排的道也过得去（桥不窄于 2 格）。
                    var along = new P(Dirs[nd].Y, Dirs[nd].X);
                    if ((onWater && nd != cur.D)
                        || (ontoWater && !(CanBridge(next, Dirs[nd], newCrossingOnly)
                            && (CanBridge(new P(next.X + along.X, next.Y + along.Y), Dirs[nd], newCrossingOnly)
                                || CanBridge(new P(next.X - along.X, next.Y - along.Y), Dirs[nd], newCrossingOnly)))))
                    {
                        continue;
                    }
                }

                // 缓坡格可以借道（两个平台的缓坡背靠背时，缓坡口就是对方的缓坡），但不会被改刻。
                if (cell is not (Cell.Fill or Cell.Ground or Cell.Ramp or Cell.River or Cell.Bridge) && !isTarget)
                {
                    continue;
                }

                // 中线不走"怎么拓也拓不到 2 格宽"的格（一侧贴外圈、另一侧贴平台的窄缝）。
                if (cell == Cell.Fill && !CanBeWidened(next))
                {
                    continue;
                }

                bool rim = next.X == 1 || next.X == W - 2 || next.Y == 1 || next.Y == H - 2;
                int step = (cell == Cell.River ? 16 : cell == Cell.Fill ? 5 : 2) + (rim ? 4 : 0) + (nd != cur.D ? 3 : 0);
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

    /// <summary>沿中线刻走廊：中线本身 + 一侧（宽 2）或两侧（宽 3）；拐角处把外角补齐。一侧被平台 / 缓坡挡住就改刻另一侧。</summary>
    private void CarveAlong(List<P> path, int width, int sign)
    {
        if (path.Count < 2)
        {
            return;
        }

        for (int i = 0; i < path.Count; i++)
        {
            P c = path[i];
            if (Cells[c.X, c.Y] == Cell.Ramp)
            {
                continue;
            }

            P dirIn = i > 0 ? new P(c.X - path[i - 1].X, c.Y - path[i - 1].Y) : new P(path[i + 1].X - c.X, path[i + 1].Y - c.Y);
            P dirOut = i < path.Count - 1 ? new P(path[i + 1].X - c.X, path[i + 1].Y - c.Y) : dirIn;
            var leftIn = new P(-dirIn.Y * sign, dirIn.X * sign);
            var leftOut = new P(-dirOut.Y * sign, dirOut.X * sign);

            // 只有直行经过的格才可能架桥（河上不拐弯，由寻路保证）。
            P heading = dirIn == dirOut ? dirIn : default;
            TryCarve(c, heading);
            Widen(c, leftIn, width, heading);
            if (leftOut != leftIn)
            {
                Widen(c, leftOut, width, default);
                TryCarve(new P(c.X + leftIn.X + leftOut.X, c.Y + leftIn.Y + leftOut.Y), default);
                if (width == 3)
                {
                    TryCarve(new P(c.X - leftIn.X - leftOut.X, c.Y - leftIn.Y - leftOut.Y), default);
                }
            }
        }
    }

    private void Widen(P c, P left, int width, P heading)
    {
        bool carved = TryCarve(new P(c.X + left.X, c.Y + left.Y), heading);
        if (width == 3 || !carved)
        {
            TryCarve(new P(c.X - left.X, c.Y - left.Y), heading);
        }
    }

    /// <summary>把一格刻成走廊；河格只在给了行进方向、且该方向可以架桥时改成桥。返回该格最终是不是走廊 / 桥。</summary>
    private bool TryCarve(P p, P heading)
    {
        if (!InCorridorArea(p))
        {
            return false;
        }

        if (Cells[p.X, p.Y] is Cell.River or Cell.Bridge)
        {
            if (heading == default || !CanBridge(p, heading, newCrossingOnly: false))
            {
                return false;
            }

            // 桥的两头一并刻开（CanBridge 已确认刻得开）：并排那条道在岸上可能被挡而改刻另一侧，不能留一座上不了岸的桥。
            Cells[p.X, p.Y] = Cell.Bridge;
            foreach (P bank in new[] { new P(p.X + heading.X, p.Y + heading.Y), new P(p.X - heading.X, p.Y - heading.Y) })
            {
                if (Cells[bank.X, bank.Y] == Cell.Fill)
                {
                    Cells[bank.X, bank.Y] = Cell.Ground;
                }
            }

            return true;
        }

        if (Cells[p.X, p.Y] == Cell.Fill)
        {
            Cells[p.X, p.Y] = Cell.Ground;
            return true;
        }

        return Cells[p.X, p.Y] == Cell.Ground;
    }

    /// <summary>过渡带格：走廊 / 广场 / 桥（h=0）与缓坡（h=1）。</summary>
    private bool IsLow(int x, int y) => x >= 0 && x < W && y >= 0 && y < H && Cells[x, y] is (Cell.Ground or Cell.Ramp or Cell.Bridge);

    /// <summary>以 (x, y) 为西南角的 2×2 方块里，过渡带格有几个；含刻不了的格（盘外圈、平台、河……）时为 −1。</summary>
    private int LowInBlock(int x, int y)
    {
        int low = 0;
        for (int dy = 0; dy <= 1; dy++)
        {
            for (int dx = 0; dx <= 1; dx++)
            {
                var p = new P(x + dx, y + dy);
                if (IsLow(p.X, p.Y))
                {
                    low++;
                }
                else if (!InCorridorArea(p) || Cells[p.X, p.Y] != Cell.Fill)
                {
                    return -1;
                }
            }
        }

        return low;
    }

    /// <summary>该格属于某个四格全是过渡带格的 2×2 方块——走廊在这里至少 2 格宽。</summary>
    private bool IsWide(P p) =>
        LowInBlock(p.X - 1, p.Y - 1) == 4 || LowInBlock(p.X, p.Y - 1) == 4 || LowInBlock(p.X - 1, p.Y) == 4 || LowInBlock(p.X, p.Y) == 4;

    /// <summary>该格所在的四个 2×2 方块里，至少有一个还能刻成全过渡带格。</summary>
    private bool CanBeWidened(P p) =>
        LowInBlock(p.X - 1, p.Y - 1) >= 0 || LowInBlock(p.X, p.Y - 1) >= 0 || LowInBlock(p.X - 1, p.Y) >= 0 || LowInBlock(p.X, p.Y) >= 0;

    /// <summary>
    /// 走廊全长至少 2 格宽：刻完之后按坐标序逐格检查，不属于任何 2×2 过渡带方块的走廊格，就把它所在的某个方块补刻完整
    /// （取要补的格最少的那个，并列按西南、东南、西北、东北的次序）。补不出来则本次尝试作废。1 格宽的走廊一枚子就能堵死。
    /// </summary>
    private bool WidenNarrowCorridors(out string reason)
    {
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                var p = new P(x, y);
                if (Cells[x, y] != Cell.Ground || IsWide(p))
                {
                    continue;
                }

                P best = default;
                int bestLow = -1;
                foreach (P corner in new[] { new P(x - 1, y - 1), new P(x, y - 1), new P(x - 1, y), p })
                {
                    int low = LowInBlock(corner.X, corner.Y);
                    if (low > bestLow)
                    {
                        (best, bestLow) = (corner, low);
                    }
                }

                if (bestLow < 0)
                {
                    reason = $"走廊在 ({x},{y}) 只有 1 格宽，且无处拓宽。";
                    return false;
                }

                for (int dy = 0; dy <= 1; dy++)
                {
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        TryCarve(new P(best.X + dx, best.Y + dy), default);
                    }
                }
            }
        }

        reason = string.Empty;
        return true;
    }
}
