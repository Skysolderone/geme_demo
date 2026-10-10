using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Core.Tests;

/// <summary>
/// 链接模式（board-link-mode 段 B）的对局夹具：<c>link:1</c> 初始图（进程内只生成一次）、Link 模式选项、按指定出生棋盘开局、整大回合 Pass 推进。
/// 出生棋盘 / 战场编号一律用 0 起的内部索引（规格 Scenario 写的"棋盘 1–5"对应这里的 0–4）。
/// </summary>
internal static class LinkModeFixtures
{
    internal const string MapId = "link:1";

    private static readonly Lazy<MapData> LazyMap = new(() => MapCatalog.Resolve(MapId));

    internal static readonly PlayerId P0 = MatchFixtures.P0;
    internal static readonly PlayerId P1 = MatchFixtures.P1;
    internal static readonly PlayerId P2 = MatchFixtures.P2;
    internal static readonly PlayerId P3 = MatchFixtures.P3;
    internal static readonly PlayerId[] All = MatchFixtures.All;

    /// <summary><c>link:1</c> 的初始图（5 块隔绝的出生棋盘）。</summary>
    internal static MapData Map => LazyMap.Value;

    /// <summary>链接模式、插旗立即。</summary>
    internal static MatchOptions Options => MatchOptions.Immediate with { Mode = MatchMode.Link };

    /// <summary>插旗阶段的链接模式对局。</summary>
    internal static MatchFlow Create(GameSeed? seed = null, MatchOptions? options = null) =>
        MatchFlow.Create(Map, seed ?? MatchFixtures.Seed, All, options ?? Options);

    /// <summary>按 <paramref name="zones"/>（P0..P3 各自的出生棋盘）锁定并开始第 1 大回合；每人手牌 50 枚普通子。可挂接配对来源。</summary>
    internal static MatchFlow Started(int[] zones, IPairingSource? source = null, GameSeed? seed = null)
    {
        MatchFlow match = Create(seed);
        match.PairingSource = source;
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(All.Select((p, i) => (p, zones[i])));
        return match;
    }

    /// <summary>当前大回合内剩下的玩家全部 Pass，直到大回合结束（或对局离开进行中阶段）。链接模式下整轮 Pass 不结束对局。</summary>
    internal static MatchFlow PassRound(this MatchFlow match)
    {
        int round = match.MajorRound;
        while (match.Phase == MatchPhase.InProgress && match.MajorRound == round)
        {
            match.PassTurn();
        }

        return match;
    }

    /// <summary>连续 Pass 掉 <paramref name="rounds"/> 个大回合。</summary>
    internal static MatchFlow PassRounds(this MatchFlow match, int rounds)
    {
        for (int i = 0; i < rounds; i++)
        {
            match.PassRound();
        }

        return match;
    }

    /// <summary>在权威盘面直接摆子（绕过规则），随后重算派生量。</summary>
    internal static MatchFlow Place(this MatchFlow match, PlayerId owner, params Coord[] cells)
    {
        foreach (Coord cell in cells)
        {
            match.Board.Place(cell, owner, PieceType.Basic);
        }

        match.Debug.Recalculate();
        return match;
    }

    /// <summary>出生棋盘 <paramref name="zone"/> 左上角起的相对坐标。</summary>
    internal static Coord BirthCell(MapData map, int zone, int dx, int dy)
    {
        ImmutableHashSet<Coord> cells = map.BirthZones[zone];
        return new Coord(cells.Min(c => c.X) + dx, cells.Min(c => c.Y) + dy);
    }

    /// <summary>当前玩家的小回合推进到部署阶段，返回暂放批次。</summary>
    internal static StagedBatch ToDeploy(this MatchFlow match)
    {
        match.BeginTurn();
        match.EnterRecruit();
        return match.EnterDeploy();
    }

    /// <summary>首轮配对 / 决赛事件的便捷写法。</summary>
    internal static PairingDecision Pairs(int? waiter, params (int X, int Y)[] pairs) => new([.. pairs], waiter);
}
