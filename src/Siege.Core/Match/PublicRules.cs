using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 只读公开量的流程规则纯函数（expert-lookahead D7）：合法落子范围与出局判据的<b>唯一实现</b>。
/// 对局流程（小回合开始时的批次上下文、每次结算 / Pass 后的出局检查）与专家前瞻（模拟下一名对手）都调用这里，不各写一份。
/// 输入只含公开信息（盘面、大回合、出生区、活形分析、流程状态、势力快照），本文件在信息边界守门的源码扫描点名清单里。
/// </summary>
public static class PublicRules
{
    /// <summary>构筑保护期的大回合数（设计文档 §4.2）：第 1–3 大回合只能在锁定出生区内落子。</summary>
    public const int BuildProtectionRounds = 3;

    /// <summary>
    /// 合法落子范围（turn-sequence「合法落子范围的对外契约」）：保护期内为 <paramref name="birthZone"/> 的格集合，此后为全图可落子格
    /// （按当前地形现算，改造出来的桥立即可落）；两种情况都扣除 <paramref name="player"/> 的禁入格——扣除只在这里一处（裁决 R12）。
    /// </summary>
    /// <param name="board">盘面（地形取其当前 <see cref="GameBoard.Map"/>）。</param>
    /// <param name="majorRound">该玩家行动的大回合。</param>
    /// <param name="birthZone">该玩家锁定的出生区编号。</param>
    /// <param name="player">玩家。</param>
    /// <param name="life">对 <paramref name="board"/> 的活形分析（禁入格的唯一查询）。</param>
    public static IReadOnlySet<Coord> LegalRange(GameBoard board, int majorRound, int birthZone, PlayerId player, LifeShapeReport life)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(life);

        // 可落子格按当前地形现算：本局架出来的桥必须立刻成为合法落点（artisan-terrain-edit 2.6 缓存排查第 1 条）。
        IEnumerable<Coord> range = majorRound <= BuildProtectionRounds
            ? board.Map.BirthZones[birthZone]
            : board.AllCoords().Where(c => board[c].Terrain == Terrain.Playable);
        return range.Except(life.ForbiddenCellsFor(player)).ToImmutableHashSet();
    }

    /// <summary>出局判据（elimination-endgame「出局判定」，restore-go-core-rules 裁决 #3）：曾建立正势力且当前总势力为 0。</summary>
    public static bool IsEliminated(bool hasEstablishedPower, BigInteger totalPower) => hasEstablishedPower && totalPower == BigInteger.Zero;

    /// <summary>
    /// 一次出局检查：<paramref name="states"/> 中参赛中且满足 <see cref="IsEliminated"/> 的玩家，按输入次序。
    /// <paramref name="states"/> 的"曾建立正势力"须已按 <paramref name="power"/> 置位（置位只会让总势力 &gt; 0 的玩家变真，不改变结果）。
    /// </summary>
    public static ImmutableArray<PlayerId> Eliminated(IEnumerable<PlayerFlowState> states, PowerSnapshot power)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(power);
        return [.. states.Where(s => s.IsActive && IsEliminated(s.HasEstablishedPower, power.Of(s.Player).Total)).Select(s => s.Player)];
    }
}
