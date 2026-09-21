namespace Siege.Core.Board.Maps;

/// <summary><see cref="FrontierMapLayout"/> 第 6 步：布点——石碑、篝火、信物（平台内不放营帐）。</summary>
internal sealed partial class FrontierMapLayout
{
    private bool PlaceSitesAndRelics(out string reason)
    {
        // 中央入口兼高档公共信物。
        Relics[_cx, _cy] = new RelicCellSpec(RelicZone.Contested, BudgetTier.High);

        // 石碑 4：风车形摆在广场外圈，没有一块与中心相邻；旋向由随机源定。广场四角才可能是林地，外圈中段不是。
        int mirror = _rng.NextInt(2) == 0 ? 1 : -1;
        foreach (P o in new[] { new P(-1, 2), new P(2, 1), new P(1, -2), new P(-2, -1) })
        {
            Sites[_cx + (o.X * mirror), _cy + o.Y] = SiteTier.Stele;
        }

        for (int i = 0; i < _n; i++)
        {
            if (!PlacePlatformRelics(i))
            {
                reason = $"平台 {i + 1} 放不下信物。";
                return false;
            }
        }

        // 公共信物先于篝火：桥头优先给信物，篝火再避开它们。
        if (!PlacePublicRelics())
        {
            reason = "走廊上放不下 6 个标准档公共信物。";
            return false;
        }

        var fires = new List<P>();
        for (int i = 0; i < _n; i++)
        {
            if (!PlaceCampfire(i, fires))
            {
                reason = "走廊上放不下足够的篝火。";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private bool IsFree(P p) => Sites[p.X, p.Y] is null && Relics[p.X, p.Y] is null;

    /// <summary>平台内只放信物（1 或 2 个，彼此至少 2 步）；平台内没有据点——不放营帐（design 裁决 18）。</summary>
    private bool PlacePlatformRelics(int platform)
    {
        PlatformRect r = Platforms[platform];
        var taken = new List<P>();
        int relics = r.Side >= 7 ? 2 : 1;
        for (int k = 0; k < relics; k++)
        {
            P? pick = null;
            for (int spacing = 2; spacing >= 1 && pick is null; spacing--)
            {
                var candidates = new List<P>();
                for (int y = r.Y; y <= r.Y1; y++)
                {
                    for (int x = r.X; x <= r.X1; x++)
                    {
                        var p = new P(x, y);
                        if (Cells[x, y] == Cell.Platform && IsFree(p) && taken.TrueForAll(o => Manhattan(o, p) >= spacing))
                        {
                            candidates.Add(p);
                        }
                    }
                }

                if (candidates.Count > 0)
                {
                    pick = candidates[_rng.NextInt(candidates.Count)];
                }
            }

            if (pick is not { } cell)
            {
                return false;
            }

            Relics[cell.X, cell.Y] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
            taken.Add(cell);
        }

        return true;
    }

    private static int Manhattan(P a, P b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>篝火：优先放在该平台缓坡口外 2–3 步的走廊格上；广场里不放（小平台的缓坡直通广场时改放到别处的走廊上），彼此拉开。</summary>
    private bool PlaceCampfire(int platform, List<P> fires)
    {
        int[,] steps = GroundSteps(platform);
        List<P> near = CorridorCells(p =>
            steps[p.X, p.Y] is >= 2 and <= 3 && SuitsSite(p) && fires.TrueForAll(o => Manhattan(o, p) >= 3));
        List<P> pool = near;
        for (int spacing = 5; pool.Count == 0 && spacing >= 0; spacing--)
        {
            int required = spacing;
            pool = CorridorCells(p => SuitsSite(p) && fires.TrueForAll(o => Manhattan(o, p) >= required));
        }

        if (pool.Count == 0)
        {
            return false;
        }

        P pick = pool[_rng.NextInt(pool.Count)];
        Sites[pick.X, pick.Y] = SiteTier.Campfire;
        fires.Add(pick);
        return true;
    }

    private bool SuitsSite(P p) => IsFree(p) && !Forest[p.X, p.Y];

    /// <summary>从某平台的缓坡格出发，只沿 h=0 空地走的步数（缓坡口 = 1）；到不了为 −1。</summary>
    private int[,] GroundSteps(int platform)
    {
        var steps = new int[W, H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                steps[x, y] = -1;
            }
        }

        var queue = new Queue<P>();
        foreach (Ramp ramp in _ramps)
        {
            if (ramp.Platform != platform)
            {
                continue;
            }

            foreach (P c in ramp.Cells)
            {
                steps[c.X, c.Y] = 0;
                queue.Enqueue(c);
            }
        }

        while (queue.Count > 0)
        {
            P cur = queue.Dequeue();
            foreach (P d in Dirs)
            {
                var n = new P(cur.X + d.X, cur.Y + d.Y);
                if (InMap(n) && Cells[n.X, n.Y] == Cell.Ground && steps[n.X, n.Y] < 0)
                {
                    steps[n.X, n.Y] = steps[cur.X, cur.Y] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        return steps;
    }

    /// <summary>公共信物：中心高档已放；标准档 6 个——桥头优先（每座桥一个、两岸交错，至多 4 个），其余撒在走廊上彼此拉开。</summary>
    private bool PlacePublicRelics()
    {
        var placed = new List<P> { Entrance };
        int standard = 0;

        var seenBridge = new bool[W, H];
        int bridgeIndex = 0;
        for (int y = 0; y < H && standard < 4; y++)
        {
            for (int x = 0; x < W && standard < 4; x++)
            {
                if (Cells[x, y] != Cell.Bridge || seenBridge[x, y])
                {
                    continue;
                }

                // 收集这座桥（相邻桥格）的全部桥头：与桥格相邻的走廊格。
                var heads = new List<P>();
                var queue = new Queue<P>();
                queue.Enqueue(new P(x, y));
                seenBridge[x, y] = true;
                while (queue.Count > 0)
                {
                    P cur = queue.Dequeue();
                    foreach (P d in Dirs)
                    {
                        var n = new P(cur.X + d.X, cur.Y + d.Y);
                        if (!InMap(n))
                        {
                            continue;
                        }

                        if (Cells[n.X, n.Y] == Cell.Bridge && !seenBridge[n.X, n.Y])
                        {
                            seenBridge[n.X, n.Y] = true;
                            queue.Enqueue(n);
                        }
                        else if (Cells[n.X, n.Y] == Cell.Ground && !Plaza[n.X, n.Y] && SuitsSite(n) && !heads.Contains(n)
                            && placed.TrueForAll(o => Manhattan(o, n) >= 3))
                        {
                            heads.Add(n);
                        }
                    }
                }

                if (heads.Count > 0)
                {
                    heads.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
                    P head = bridgeIndex % 2 == 0 ? heads[0] : heads[^1];
                    Relics[head.X, head.Y] = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
                    placed.Add(head);
                    standard++;
                }

                bridgeIndex++;
            }
        }

        while (standard < 6)
        {
            List<P> pool = [];
            for (int spacing = 6; pool.Count == 0 && spacing >= 1; spacing--)
            {
                int required = spacing;
                pool = CorridorCells(p => SuitsSite(p) && placed.TrueForAll(o => Manhattan(o, p) >= required));
            }

            if (pool.Count == 0)
            {
                return false;
            }

            P pick = pool[_rng.NextInt(pool.Count)];
            Relics[pick.X, pick.Y] = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
            placed.Add(pick);
            standard++;
        }

        return true;
    }
}
