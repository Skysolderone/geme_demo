using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// 四邻接是全项目<b>唯一</b>的邻接语义，这里是它<b>唯一</b>的实现。
/// 连接、气、覆盖与围杀判定 MUST 全部经由此处，MUST NOT 在别处手写邻居遍历。
/// </summary>
/// <remarks>
/// 越界方向与障碍格具有相同的封堵语义，因此本方法只负责"棋盘内的上下左右"，
/// 地形过滤由调用方按各自语义处理。
/// 规格：openspec/changes/add-board-core/specs/board-topology —— Requirement: 四邻接是唯一邻接语义
/// </remarks>
public static class Adjacency
{
    /// <summary>
    /// 返回 <paramref name="c"/> 的四邻接邻居中位于 <paramref name="width"/>×<paramref name="height"/>
    /// 棋盘内的格子，按字典序（先行后列）排列：下、左、右、上。不含斜向。
    /// </summary>
    public static ImmutableArray<Coord> Neighbors(int width, int height, Coord c)
    {
        ImmutableArray<Coord>.Builder builder = ImmutableArray.CreateBuilder<Coord>(4);

        if (c.Y > 0)
        {
            builder.Add(new Coord(c.X, c.Y - 1));
        }

        if (c.X > 0)
        {
            builder.Add(new Coord(c.X - 1, c.Y));
        }

        if (c.X < width - 1)
        {
            builder.Add(new Coord(c.X + 1, c.Y));
        }

        if (c.Y < height - 1)
        {
            builder.Add(new Coord(c.X, c.Y + 1));
        }

        return builder.ToImmutable();
    }
}
