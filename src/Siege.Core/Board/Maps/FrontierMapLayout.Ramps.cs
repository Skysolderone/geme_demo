namespace Siege.Core.Board.Maps;

/// <summary><see cref="FrontierMapLayout"/> 第 3 步（上）：缓坡。</summary>
internal sealed partial class FrontierMapLayout
{
    /// <summary>每个平台 1–2 处缓坡，每处 2–3 格宽，开在平台方块<b>外</b>一格（h=1）；再往外一格是缓坡口（h=0）。</summary>
    private bool CutRamps(out string reason)
    {
        for (int i = 0; i < _n; i++)
        {
            bool small = i >= _n - 2;
            PlatformRect r = Platforms[i];
            Side primary;
            Ramp? first;
            if (small)
            {
                primary = _smallFacing[i];
                int width = 2 + _rng.NextInt(2);
                int start = _rng.NextInt(3 - width + 1);
                bool horizontal = primary is Side.Left or Side.Right;
                int along = (horizontal ? _cy : _cx) - 1 + start - (horizontal ? r.Y : r.X);
                first = MakeRamp(i, primary, along, width);
            }
            else
            {
                // 朝中央广场的那一侧：先看哪个方向离得更远（用 2 倍坐标避免半格）。
                int dx = (2 * _cx) - ((2 * r.X) + r.Side - 1);
                int dy = (2 * _cy) - ((2 * r.Y) + r.Side - 1);
                primary = Math.Abs(dy) >= Math.Abs(dx)
                    ? (dy > 0 ? Side.Top : Side.Bottom)
                    : (dx > 0 ? Side.Right : Side.Left);
                first = RandomRamp(i, primary);
            }

            if (first is null)
            {
                reason = $"平台 {i + 1} 朝中央的一侧开不出缓坡。";
                return false;
            }

            AddRamp(first);

            if (_rng.NextInt(2) == 0)
            {
                // 第二处缓坡：另一条轴上朝中央的那一侧；正对中央时随机取一侧。开不出就算了。
                bool primaryHorizontal = primary is Side.Left or Side.Right;
                int delta = primaryHorizontal
                    ? (2 * _cy) - ((2 * r.Y) + r.Side - 1)
                    : (2 * _cx) - ((2 * r.X) + r.Side - 1);
                bool positive = delta > 0 || (delta == 0 && _rng.NextInt(2) == 0);
                Side secondary = primaryHorizontal
                    ? (positive ? Side.Top : Side.Bottom)
                    : (positive ? Side.Right : Side.Left);
                if (RandomRamp(i, secondary) is { } second)
                {
                    AddRamp(second);
                }
            }
        }

        reason = string.Empty;
        return true;
    }

    private Ramp? RandomRamp(int platform, Side side)
    {
        int width = 2 + _rng.NextInt(2);
        int along = _rng.NextInt(Platforms[platform].Side - width + 1);
        return MakeRamp(platform, side, along, width);
    }

    private Ramp? MakeRamp(int platform, Side side, int along, int width)
    {
        PlatformRect r = Platforms[platform];
        var cells = new P[width];
        var mouth = new P[width];
        for (int k = 0; k < width; k++)
        {
            (cells[k], mouth[k]) = side switch
            {
                Side.Left => (new P(r.X - 1, r.Y + along + k), new P(r.X - 2, r.Y + along + k)),
                Side.Right => (new P(r.X1 + 1, r.Y + along + k), new P(r.X1 + 2, r.Y + along + k)),
                Side.Bottom => (new P(r.X + along + k, r.Y - 1), new P(r.X + along + k, r.Y - 2)),
                _ => (new P(r.X + along + k, r.Y1 + 1), new P(r.X + along + k, r.Y1 + 2)),
            };

            // 缓坡口不得落在最外一圈（留给外海）；缓坡格本身必须还是空地或走廊。
            if (!InCorridorArea(mouth[k]) || Cells[cells[k].X, cells[k].Y] is not (Cell.Fill or Cell.Ground)
                || Plaza[cells[k].X, cells[k].Y])
            {
                return null;
            }
        }

        return new Ramp(platform, side, cells, mouth);
    }

    private void AddRamp(Ramp ramp)
    {
        foreach (P c in ramp.Cells)
        {
            Cells[c.X, c.Y] = Cell.Ramp;
        }

        foreach (P m in ramp.Mouth)
        {
            if (Cells[m.X, m.Y] == Cell.Fill)
            {
                Cells[m.X, m.Y] = Cell.Ground;
            }
        }

        _ramps.Add(ramp);
    }

    private static bool InCorridorArea(P p) => p.X >= 1 && p.X <= W - 2 && p.Y >= 1 && p.Y <= H - 2;

    private static bool InMap(P p) => p.X >= 0 && p.X < W && p.Y >= 0 && p.Y < H;
}
