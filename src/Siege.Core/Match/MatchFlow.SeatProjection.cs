using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 按座位投影的公开快照（board-link-mode 实现裁决 C4；ai-seat-vision 裁决 V1 起为正式 AI 的缺省视野，规格 ai-decision「正式对战 AI 的信息边界」）：与 <see cref="Publish"/> 同一类型，
/// 供正式 AI（缺省按座位视野，经 <see cref="Ai.HeuristicAi.VisionFor"/>）与终端的人类玩家读取，评价代码与终端渲染不需要知道差别。
/// </summary>
/// <remarks>
/// 口径与 <see cref="PublishFor"/>（<see cref="SeatPublicView"/>）一致：本座位所在战场之外——未与之链接的战场——的棋子不出现（其上挂着的信物随之成为空格上的信物，信物层本身全公开），
/// 他人战场玩家的手牌类型为空；势力快照与活形分析对投影盘面现算（唯一实现 <see cref="PowerCalculator"/> / <see cref="LifeShapeReport.Analyze"/>），
/// 他人战场玩家的势力因此只剩本战场里的部分（即 0）。地形、行动顺序、在局状态与结果是全局项，不过滤；带入清单是私有信息，本来就不在全知视图里（R22）。
/// 插旗阶段（还没有战场）原样返回 <see cref="Publish"/>。
/// </remarks>
public sealed partial class MatchFlow
{
    /// <summary>按座位投影的公开快照（见类型说明）。</summary>
    public MatchPublicView PublishSeatProjection(PlayerId seat)
    {
        MatchPublicView full = Publish();
        if (SeatField(seat) is not { } field)
        {
            return full;
        }

        ImmutableHashSet<Coord> visible = [.. field.Cells];
        ImmutableHashSet<PlayerId> sameField = [.. _players.Where(p => _records[p].BirthZone is int z && field.BirthZones.Contains(z))];

        GameBoard board = full.Board;   // Publish 给的已是副本
        StripOutside(board, visible);
        PowerSnapshot? power = full.Power is null ? null : PowerCalculator.Compute(board, Roster, ScoringVersion);
        ImmutableArray<Recruit.HandPublicView> hands = [.. full.Hands.Select(h => sameField.Contains(h.Player) ? h : h with { Types = [] })];

        return full with
        {
            Board = board,
            BoardSerialized = board.Serialize(),
            Power = power,
            Hands = hands,
            LifeShape = LifeShapeReport.Analyze(board),
            PassedThisRound = [.. full.PassedThisRound.Where(sameField.Contains)],
        };
    }

    /// <summary>
    /// 把一次零副作用预演的结果投影到同一口径（按座位视野的 AI 用——缺省即是，见 <see cref="Ai.HeuristicAi.VisionFor"/>）：预演后盘面去掉本座位战场之外的棋子（副本上做），
    /// 合法性、失败与提子不变（提子只发生在本战场里）。插旗阶段原样返回。
    /// </summary>
    public RehearsalResult ProjectRehearsal(PlayerId seat, RehearsalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ProjectedBoard is not { } projected || SeatField(seat) is not { } field)
        {
            return result;
        }

        GameBoard board = projected.Clone();
        StripOutside(board, [.. field.Cells]);
        return result with { ProjectedBoard = board };
    }

    /// <summary>本座位所在的战场；插旗阶段或尚未锁定出生棋盘时为 <c>null</c>（不投影）。</summary>
    private Battlefield? SeatField(PlayerId seat)
    {
        PlayerRecord record = Require(seat);
        return Phase == MatchPhase.FlagPlanting || record.BirthZone is not int zone
            ? null
            : PublicRules.BattlefieldOf(Board, zone);
    }

    private static void StripOutside(GameBoard board, ImmutableHashSet<Coord> visible) =>
        board.RemoveStones([.. board.AllCoords().Where(c => board[c].Occupant is not null && !visible.Contains(c))]);
}
