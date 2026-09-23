using Siege.Core.Board;

namespace Siege.Core.Ai;

/// <summary>
/// 棋串安全度的启发式近似（裁决 1）：气数、气的分布是否分散、两眼潜力。不做完整死活判定。
/// 眼信息（眼值之和、是否已确定活形）<b>只来自</b> <see cref="LifeShapeReport"/>（ai-eye D2）——本类不自带眼判定；
/// 气只走 <see cref="GameBoard.LibertiesOf"/>，分散气的邻接只走 <see cref="GameBoard.LibertyNeighbors"/>。
/// </summary>
/// <param name="Liberties">气数。</param>
/// <param name="EyeValueSum">该棋串全部眼空间的眼值之和（活形查询的 <see cref="GroupLife.EyeValueSum"/>）。</param>
/// <param name="DispersedLiberties">分散气数：不与本串任何其他气相邻的气。</param>
/// <param name="Size">棋串大小。</param>
/// <param name="IsAlive">是否已确定活形（活形查询的 <see cref="LifeState.Alive"/>）。</param>
public readonly record struct GroupSafety(int Liberties, int EyeValueSum, int DispersedLiberties, int Size, bool IsAlive)
{
    /// <summary>敌方一个小回合至少能落这么多子（基础部署上限），气数不超过它的棋串一手就能被提光。</summary>
    public const int DangerLiberties = 3;

    /// <summary>气数项的封顶。</summary>
    public const int LibertyCap = 4;

    /// <summary>两眼潜力每一点的分值。</summary>
    public const int EyeWeight = 6;

    /// <summary>分散气项的封顶。</summary>
    public const int DispersedCap = 2;

    /// <summary>
    /// 已确定活形棋串的安全分（ai-eye D2「活形中性」）：取公式上界——气数封顶 + 两眼潜力满 2 + 分散气封顶、无危险 = 4 + 12 + 2 = 18。
    /// 不随气数、气的分布变化：做活在本维度上是净收益，做活之后任何补气都不再加分。
    /// </summary>
    public const long AliveScore = LibertyCap + (2 * EyeWeight) + DispersedCap;

    /// <summary>两眼潜力：<c>min(2, 眼值之和)</c>，眼值来自活形查询。</summary>
    public int TwoEyePotential => Math.Min(2, EyeValueSum);

    /// <summary>
    /// 安全分。已确定活形恒为 <see cref="AliveScore"/>；其余：气数（封顶 4）+ 两眼潜力 × 6 + 分散气（封顶 2）− 危险。
    /// 批次制下敌方一回合落 3 子，所以危险阈值是 <see cref="DangerLiberties"/> 而不是围棋的"打吃"：1 气 大小 × 12、2 气 大小 × 8、3 气 大小 × 4。
    /// 故意不随棋子数线性增长，避免评价偏向把棋子撒开。
    /// </summary>
    public long Score
    {
        get
        {
            if (IsAlive)
            {
                return AliveScore;
            }

            long danger = Liberties switch
            {
                <= 1 => (long)Size * 12,
                2 => (long)Size * 8,
                DangerLiberties => (long)Size * 4,
                _ => 0,
            };
            return Math.Min(Liberties, LibertyCap) + (TwoEyePotential * EyeWeight) + Math.Min(DispersedLiberties, DispersedCap) - danger;
        }
    }

    /// <summary>分析一条棋串。<paramref name="life"/> 必须取自同一盘面的 <see cref="LifeShapeReport.Analyze"/>。</summary>
    public static GroupSafety Analyze(GameBoard board, GroupLife life)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(life);
        var liberties = board.LibertiesOf(life.Group).ToHashSet();
        int dispersed = 0;
        foreach (Coord liberty in liberties)
        {
            bool touchesLiberty = false;
            // 只看有气边的邻格：障碍、深水、崖壁、栅栏另一侧都够不到这口气。
            foreach (Coord n in board.LibertyNeighbors(liberty))
            {
                if (liberties.Contains(n))
                {
                    touchesLiberty = true;
                }
            }

            if (!touchesLiberty)
            {
                dispersed++;
            }
        }

        return new GroupSafety(liberties.Count, life.EyeValueSum, dispersed, life.Group.Size, life.Life == LifeState.Alive);
    }
}
