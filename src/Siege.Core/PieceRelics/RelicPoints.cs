using System.Collections.Immutable;

namespace Siege.Core.PieceRelics;

/// <summary>
/// 名次补给点表与弃赛折算（R18 / R19：名次补给点表保留、弃赛沿用 50%）。接线段 W2 从旧 <c>Carry.CarryPoints</c> /
/// <c>CarryOutSettlement.ResignShare</c> 原样搬入（旧补给系统随之删除）。表值是<b>未校准的初值</b>，不扫档。
/// </summary>
public static class RelicPoints
{
    /// <summary>某参赛人数的点数表（第 1 名起）：4 人 24 / 16 / 12 / 10，3 人 24 / 16 / 10，2 人 24 / 10。</summary>
    public static ImmutableArray<int> TableOf(int playerCount) => playerCount switch
    {
        2 => UncalibratedTwoPlayer,
        3 => UncalibratedThreePlayer,
        4 => UncalibratedFourPlayer,
        _ => throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "名次补给点表只有 2–4 人。"),
    };

    /// <summary>该参赛人数是否有点数表（2–4 人）。</summary>
    public static bool Supports(int playerCount) => playerCount is >= 2 and <= 4;

    /// <summary>某参赛人数下某名次（竞争名次，并列者共享）的点数。</summary>
    public static int For(int playerCount, int rank)
    {
        ImmutableArray<int> table = TableOf(playerCount);
        return rank >= 1 && rank <= table.Length
            ? table[rank - 1]
            : throw new ArgumentOutOfRangeException(nameof(rank), rank, $"{playerCount} 人局没有第 {rank} 名。");
    }

    /// <summary>弃赛折算：表值的 50%，向下取整（现行表值全为偶数，取整口径为以后改表预先定好）。</summary>
    public static int ResignShare(int fullPoints) =>
        fullPoints >= 0 ? fullPoints / 2 : throw new ArgumentOutOfRangeException(nameof(fullPoints), fullPoints, "点数不得为负。");

    // 未校准的初值：第 1 名恒 24、末名恒 10。
    private static readonly ImmutableArray<int> UncalibratedFourPlayer = [24, 16, 12, 10];
    private static readonly ImmutableArray<int> UncalibratedThreePlayer = [24, 16, 10];
    private static readonly ImmutableArray<int> UncalibratedTwoPlayer = [24, 10];
}
