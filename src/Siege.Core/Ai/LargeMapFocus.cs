using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Ai;

/// <summary>
/// 大图局部候选预筛（big-map 裁决 10 / D8，规格 ai-decision「大图局部候选预筛」）的<b>唯一实现</b>：只在 <see cref="MapTier.Large"/> 上把 AI 枚举候选落点的范围
/// 从合法落子范围收窄到"己方任一棋子或己方旗心切比雪夫距离 ≤ <see cref="OwnReach"/> 的格 ∪ 对手任一棋子切比雪夫距离 ≤ <see cref="RivalReach"/> 的格"；己方盘上没有棋子时 = 旗心 13×13（出生棋盘）∩ 合法范围。
/// 小图原样返回传入的合法范围（同一引用）——小图的决策、随机消费与留痕一个比特不变。
/// </summary>
/// <remarks>
/// <para>只读公开量：档位与出生区编号取自公开快照（<see cref="MatchPublicView.Tier"/> / <see cref="PlayerFlowState.BirthZone"/>），旗心 = 落座图里该出生区的出生棋盘（13×13，<c>FlagRules.BirthPlateOf</c>）的中心，
/// 棋子占用取自待决策的盘面。不消费任何随机流，同一盘面同一结果。切比雪夫距离与 <see cref="FlagRules.Distance"/> 同一口径（横纵相隔格数取大者，这里按方窗直接标格）。</para>
/// <para>消费点：部署决策与复盘的单点枚举（<c>HeuristicTurnController.ScorePoints</c>，含专家前瞻模拟的对手——它经同一 <c>Deploy</c> 入口、以对手自己的棋子为锚）、搭桥候选的全范围枚举、
/// 匠人改造诊断的范围枚举、专家两层加分的单点扫描（<c>ExpertLookahead.BestSingleGain</c>）。候选格上限 K 的预筛在它之后照常。合法落子范围本身（<c>BatchContext.LegalRange</c>）不动——它是规则量，人类玩家仍可落在任何合法格。</para>
/// <para>构筑期（第 1–3 大回合）的合法范围已限在 13×13 出生棋盘内、而旗心 ≤ 8 的 17×17 覆盖整块棋盘，所以预筛在构筑期恒为恒等；只在第 4 大回合起的全图阶段真正收窄。</para>
/// </remarks>
internal static class LargeMapFocus
{
    /// <summary>己方棋子与己方旗心的辐射半径（切比雪夫）。</summary>
    public const int OwnReach = 8;

    /// <summary>对手棋子的辐射半径（切比雪夫）。</summary>
    public const int RivalReach = 2;

    /// <summary>
    /// 玩家 <paramref name="player"/> 在盘面 <paramref name="board"/> 上的局部候选范围（见类型说明）。<paramref name="view"/> 不是大图、或该玩家尚未锁定出生区时，原样返回 <paramref name="legalRange"/>。
    /// </summary>
    public static IReadOnlySet<Coord> LocalRange(MatchPublicView view, GameBoard board, PlayerId player, IReadOnlySet<Coord> legalRange)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(legalRange);
        if (view.Tier != MapTier.Large)
        {
            return legalRange;
        }

        PlayerFlowState? state = view.Players.FirstOrDefault(p => p.Player == player);
        if (state?.BirthZone is not { } zone)
        {
            return legalRange;
        }

        MapData map = board.Map;
        BoardPlate plate = map.BirthBoard(zone);
        var anchors = new List<Coord>();
        var rivals = new List<Coord>();
        foreach (Coord c in board.AllCoords())
        {
            if (board[c].Occupant is { } occupant)
            {
                (occupant.Owner == player ? anchors : rivals).Add(c);
            }
        }

        if (anchors.Count == 0)
        {
            // 无子：旗心 13×13（出生棋盘）∩ 合法范围——不并入对手棋子周边（D8 字面，追加裁决 39）。
            return legalRange.Where(plate.Contains).ToHashSet();
        }

        // 旗心 = 出生棋盘中心（大图落座图的出生棋盘恒为旗心 13×13）。
        anchors.Add(new Coord(plate.Origin.X + ((plate.Width - 1) / 2), plate.Origin.Y + ((plate.Height - 1) / 2)));
        bool[] mask = new bool[map.Width * map.Height];
        Mark(mask, map, anchors, OwnReach);
        Mark(mask, map, rivals, RivalReach);
        return legalRange.Where(c => mask[(c.Y * map.Width) + c.X]).ToHashSet();
    }

    /// <summary>把每个 <paramref name="centers"/> 切比雪夫距离 ≤ <paramref name="reach"/> 的画布内格标进 <paramref name="mask"/>。</summary>
    private static void Mark(bool[] mask, MapData map, List<Coord> centers, int reach)
    {
        foreach (Coord center in centers)
        {
            int y0 = Math.Max(0, center.Y - reach), y1 = Math.Min(map.Height - 1, center.Y + reach);
            int x0 = Math.Max(0, center.X - reach), x1 = Math.Min(map.Width - 1, center.X + reach);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    mask[(y * map.Width) + x] = true;
                }
            }
        }
    }
}
