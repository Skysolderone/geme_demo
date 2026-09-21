namespace Siege.Core.Board.Maps;

/// <summary><see cref="FrontierMapLayout"/> 第 1–2 步：边长、中央广场与三条带摆位。</summary>
internal sealed partial class FrontierMapLayout
{
    /// <summary>
    /// 取 N 个边长（5–9，从大到小）并摆位。25×30 的图上留出 2 格外缘、平台两两隔 2 格之后空间很紧，纯随机落点九成以上放不下，
    /// 所以按"三条带"摆：中间一条带 = 中央广场 + 紧贴广场两侧的两个最小平台，两边各一条外围带放其余平台（每带 1–3 个）。
    /// 带的走向（横带 / 竖带）、哪几个平台进哪条带、带内次序、带内与带间的余量怎么分、广场落在哪，全部由随机源定。
    /// 边长组合带三条约束重采：外接面积之和有上限（否则光平台就撑破可落子格预算）；信物格总数（平台内 1 或 2 个 + 公共 7）落在 14–24；
    /// 三条带在两个方向上都摆得下。重采只是几次整数运算，取到即必然摆得下——摆位本身不会失败。
    /// </summary>
    private bool PlacePlatforms(out string reason)
    {
        int maxArea = 320 - (5 * _n);
        int outer = _n - 2;

        // 边长 5–9 的抽样权重：平台多了就偏向小边长，否则绝大多数组合过不了面积上限，白白重采。
        int[] weights = _n switch
        {
            <= 6 => [1, 1, 1, 1, 1],
            7 => [3, 2, 2, 1, 1],
            _ => [4, 3, 2, 1, 1],
        };
        for (int t = 0; t < 4000; t++)
        {
            int[] sides = new int[_n];
            int area = 0;
            int relics = 7;
            for (int i = 0; i < _n; i++)
            {
                sides[i] = 5 + _rng.WeightedPick(weights);
                area += sides[i] * sides[i];
                relics += sides[i] >= 7 ? 2 : 1;
            }

            if (area > maxArea || relics is < 14 or > 24)
            {
                continue;
            }

            Array.Sort(sides);
            Array.Reverse(sides);

            // u = 沿带方向，v = 跨带方向。横带：u = 列、v = 行；竖带反之。
            bool vertical = _rng.NextInt(5) < 2;
            int uLen = (vertical ? H : W) - (2 * EdgeMargin);
            int vLen = (vertical ? W : H) - (2 * EdgeMargin);
            int middle = sides[_n - 2];
            if (sides[_n - 1] + sides[_n - 2] + 5 + 2 > uLen)
            {
                continue;
            }

            int[] order = new int[outer];
            for (int i = 0; i < outer; i++)
            {
                order[i] = i;
            }

            for (int i = outer - 1; i > 0; i--)
            {
                int j = _rng.NextInt(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            int minBefore = Math.Max(1, outer - 3);
            int maxBefore = Math.Min(3, outer - 1);
            int before = minBefore + _rng.NextInt(maxBefore - minBefore + 1);
            int[] bandBefore = order[..before];
            int[] bandAfter = order[before..];
            int beforeMax = bandBefore.Max(i => sides[i]);
            int afterMax = bandAfter.Max(i => sides[i]);
            if (BandLength(bandBefore, sides) > uLen || BandLength(bandAfter, sides) > uLen
                || beforeMax + afterMax + middle > vLen - (2 * PlatformGap))
            {
                continue;
            }

            _sides = sides;
            _bandsVertical = vertical;
            Arrange(vertical, uLen, vLen, middle, bandBefore, bandAfter, beforeMax, afterMax);
            reason = string.Empty;
            return true;
        }

        reason = "取不到满足面积、信物预算且摆得下的边长组合。";
        return false;
    }

    private static int BandLength(int[] band, int[] sides) => band.Sum(i => sides[i]) + (PlatformGap * (band.Length - 1));

    private void Arrange(bool vertical, int uLen, int vLen, int middle, int[] bandBefore, int[] bandAfter, int beforeMax, int afterMax)
    {
        int uEnd = EdgeMargin + uLen - 1;
        int vEnd = EdgeMargin + vLen - 1;

        // 中间带在 v 方向的位置：前带要容得下 beforeMax，后带要容得下 afterMax，各隔 2 格。
        int midLo = EdgeMargin + beforeMax + PlatformGap;
        int midHi = vEnd - afterMax - PlatformGap - middle + 1;
        int mid0 = midLo + _rng.NextInt(midHi - midLo + 1);
        int mid1 = mid0 + middle - 1;
        int cv = mid0 + 2 + _rng.NextInt(middle - 5 + 1);

        // 两个小平台分居广场两侧；隔 1 格（缓坡直通广场）为主，三成隔 2 格。
        int first = _n - 1 - _rng.NextInt(2);
        int second = first == _n - 1 ? _n - 2 : _n - 1;
        int gapFirst = _rng.NextInt(10) < 3 ? 2 : 1;
        int gapSecond = _rng.NextInt(10) < 3 ? 2 : 1;
        if (_sides[first] + _sides[second] + 5 + gapFirst + gapSecond > uLen)
        {
            gapFirst = 1;
            gapSecond = 1;
        }

        // 广场中心在 u 方向的取值范围；尽量靠近地图中线（±2）。
        int cuLo = EdgeMargin + _sides[first] + gapFirst + 2;
        int cuHi = uEnd - _sides[second] - gapSecond - 2;
        int center = EdgeMargin + (uLen / 2);
        int nearLo = Math.Max(cuLo, center - 2);
        int nearHi = Math.Min(cuHi, center + 2);
        if (nearLo <= nearHi)
        {
            (cuLo, cuHi) = (nearLo, nearHi);
        }

        int cu = cuLo + _rng.NextInt(cuHi - cuLo + 1);
        (_cx, _cy) = vertical ? (cv, cu) : (cu, cv);

        _smallFacing = new Side[_n];
        _smallGap = new int[_n];
        PlaceSmall(first, cu - 2 - gapFirst - _sides[first], vertical ? Side.Top : Side.Right, gapFirst);
        PlaceSmall(second, cu + 3 + gapSecond, vertical ? Side.Bottom : Side.Left, gapSecond);

        void PlaceSmall(int index, int u, Side facing, int gap)
        {
            // 必须盖住广场中间三行（列），缓坡才能正对广场开；同时不出中间带。
            int s = _sides[index];
            int lo = Math.Max(mid0, cv + 2 - s);
            int hi = Math.Min(mid1 - s + 1, cv - 1);
            int v = lo + _rng.NextInt(hi - lo + 1);
            Platforms[index] = vertical ? new PlatformRect(v, u, s) : new PlatformRect(u, v, s);
            _smallFacing[index] = facing;
            _smallGap[index] = gap;
        }

        PlaceBand(bandBefore, EdgeMargin, mid0 - PlatformGap - 1);
        PlaceBand(bandAfter, mid1 + PlatformGap + 1, vEnd);

        void PlaceBand(int[] band, int v0, int v1)
        {
            // 带内余量随机分给 k + 1 个空当（两端与平台之间）；每个平台在带宽内各自随机浮动。
            int[] slack = new int[band.Length + 1];
            for (int k = uLen - BandLength(band, _sides); k > 0; k--)
            {
                slack[_rng.NextInt(slack.Length)]++;
            }

            int u = EdgeMargin + slack[0];
            for (int k = 0; k < band.Length; k++)
            {
                int s = _sides[band[k]];
                int v = v0 + _rng.NextInt(v1 - s + 1 - v0 + 1);
                Platforms[band[k]] = vertical ? new PlatformRect(v, u, s) : new PlatformRect(u, v, s);
                u += s + PlatformGap + slack[k + 1];
            }
        }

        for (int y = _cy - 2; y <= _cy + 2; y++)
        {
            for (int x = _cx - 2; x <= _cx + 2; x++)
            {
                Cells[x, y] = Cell.Ground;
                Plaza[x, y] = true;
            }
        }

        for (int i = 0; i < _n; i++)
        {
            PlatformRect r = Platforms[i];
            for (int y = r.Y; y <= r.Y1; y++)
            {
                for (int x = r.X; x <= r.X1; x++)
                {
                    Cells[x, y] = Cell.Platform;
                    Zone[x, y] = i;
                }
            }
        }
    }
}
