namespace Siege.Core.Board.Maps;

/// <summary><see cref="FrontierMapLayout"/> 第 6 步：布信物（平台内 1–2 个、公共区 7 个）。</summary>
internal sealed partial class FrontierMapLayout
{
    private bool PlaceRelics(out string reason)
    {
        // 中央入口兼高档公共信物。
        Relics[_cx, _cy] = new RelicCellSpec(RelicZone.Contested, BudgetTier.High);

        for (int i = 0; i < _n; i++)
        {
            if (!PlacePlatformRelics(i))
            {
                reason = $"平台 {i + 1} 放不下信物。";
                return false;
            }
        }

        if (!PlacePublicRelics())
        {
            reason = "走廊上放不下 6 个标准档公共信物。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool IsFree(P p) => Relics[p.X, p.Y] is null;

    /// <summary>可放公共信物的走廊格：空着且不是林地。</summary>
    private bool SuitsRelic(P p) => IsFree(p) && !Forest[p.X, p.Y];

    /// <summary>平台内只放信物（1 或 2 个，彼此至少 2 步）。</summary>
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
                        else if (Cells[n.X, n.Y] == Cell.Ground && !Plaza[n.X, n.Y] && SuitsRelic(n) && !heads.Contains(n)
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
                pool = CorridorCells(p => SuitsRelic(p) && placed.TrueForAll(o => Manhattan(o, p) >= required));
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
