using Siege.Core.Board;

namespace Siege.Core.PieceRelics;

/// <summary>
/// 一条棋串上挂着的信物件数，按种类（relic-redesign D3）。由 <see cref="RelicEffects.Tally"/> 从盘面信物层一次数出；同串多件叠加（R13）。
/// 各项效果的换算在这里一处给出，<c>PowerCalculator</c> 只取结果、不另写系数。
/// </summary>
public readonly record struct RelicTally(int WarDrums, int Commands, int Watchtowers, int Chameleons, int Banners)
{
    /// <summary>挂在本串上的信物总件数。</summary>
    public int Total => WarDrums + Commands + Watchtowers + Chameleons + Banners;

    /// <summary>战鼓：基础军势加值（每件 +3，未校准）。并入棋串基础军势。</summary>
    public int BaseBonus => WarDrums * RelicKinds.UncalibratedWarDrumBase;

    /// <summary>百变：该串协同子计"其他类型种数"时额外加的种数（每件 +1，未校准）。</summary>
    public int ExtraSynergyTypes => Chameleons * RelicKinds.UncalibratedChameleonExtraTypes;

    /// <summary>军旗：倍率指数加值（每件 +1，未校准）。</summary>
    public int Exponent => Banners * RelicKinds.UncalibratedBannerExponent;

    /// <summary>
    /// 信物位置加值：号令每件 ⌊<paramref name="groupSize"/> / 2⌋ + 百变每件 2（未校准）。与其余位置加值一起被倍率放大。
    /// 号令按整条棋串的棋子数取（棋串分裂后各自按新串重算）。
    /// </summary>
    public int PositionBonus(int groupSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(groupSize);
        return (Commands * (groupSize / RelicKinds.UncalibratedCommandDivisor)) + (Chameleons * RelicKinds.UncalibratedChameleonPosition);
    }
}

/// <summary>
/// 信物效果进计分的读取口（relic-redesign D3）：逐棋串读盘面信物层。望台不在这里——它改的是覆盖，由盘面的覆盖目标查询按信物层处理。
/// </summary>
public static class RelicEffects
{
    /// <summary>
    /// 数出挂在 <paramref name="group"/> 棋子上的信物件数（按种类）。只读 <see cref="GameBoard.RelicAt"/>，不读旧的静态信物格；
    /// 信物层为空时直接返回全 0（与信物层上线之前的计分逐项相同）。
    /// </summary>
    public static RelicTally Tally(GameBoard board, Group group)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(group);
        if (!board.HasRelics)
        {
            return default;
        }

        int drums = 0;
        int commands = 0;
        int watchtowers = 0;
        int chameleons = 0;
        int banners = 0;
        foreach (Coord stone in group.Stones)
        {
            switch (board.RelicAt(stone)?.Kind)
            {
                case RelicKind.WarDrum:
                    drums++;
                    break;
                case RelicKind.Command:
                    commands++;
                    break;
                case RelicKind.Watchtower:
                    watchtowers++;
                    break;
                case RelicKind.Chameleon:
                    chameleons++;
                    break;
                case RelicKind.Banner:
                    banners++;
                    break;
                case null:
                    break;
                default:
                    throw new SiegeRuleException($"{stone.ToNotation()} 上的信物种类未知。");
            }
        }

        return new RelicTally(drums, commands, watchtowers, chameleons, banners);
    }
}
