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

    /// <summary>
    /// 平坦的手写链接档图（link-map-terrain 第 2 组）：<c>link:1</c> 的出生棋盘带主题地形（出生棋盘 0 是高台，外圈内侧有高度 2 的格），
    /// 角上的气与领地随主题变化；依赖"平坦的角"做势力算例的测试改用这张图。载体是 <see cref="LinkMapFixtures.Births"/>（五块 6×6，中间带只有东列两端两格林地），
    /// 棋盘 0 的出生区档信物移到核心区左下角起 (3,1) / (1,3)，不压在算例的棋子下。标识取 <c>link:90003</c>（链接模式只接受 link: 标识；不经生成器）。
    /// </summary>
    internal static readonly MapData FlatMap = BuildFlatMap();

    private static MapData BuildFlatMap()
    {
        MapData built = LinkMapFixtures.Build("link:90003", LinkMapFixtures.Births);
        BoardPlate zero = built.Boards[0];
        var birth = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
        return built with
        {
            RelicCells = built.RelicCells.Where(kv => !zero.Contains(kv.Key))
                .Append(KeyValuePair.Create(new Coord(zero.Origin.X + 3, zero.Origin.Y + 1), birth))
                .Append(KeyValuePair.Create(new Coord(zero.Origin.X + 1, zero.Origin.Y + 3), birth))
                .ToImmutableDictionary(),
        };
    }

    /// <summary>在 <see cref="FlatMap"/> 上按 <paramref name="zones"/>（P0..P3 各自的出生棋盘）锁定并开始第 1 大回合；每人手牌 50 枚普通子。</summary>
    internal static MatchFlow StartedFlat(int[] zones)
    {
        MatchFlow match = MatchFlow.Create(FlatMap, MatchFixtures.Seed, All, Options);
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

    /// <summary>
    /// 跑完当前大回合：<paramref name="movers"/> 里的玩家各落 1 子（合法范围内按坐标序第一个能暂放且能确认的空格；都不行就 Pass），其余 Pass。
    /// 用来让某个多人战场"本大回合不是全员 Pass"、从而不被结算（battlefield「一方落子即不结算」）。
    /// </summary>
    internal static MatchFlow PlayRound(this MatchFlow match, params PlayerId[] movers)
    {
        int round = match.MajorRound;
        while (match.Phase == MatchPhase.InProgress && match.MajorRound == round)
        {
            PlayerId player = match.CurrentPlayer!.Value;
            if (movers.Contains(player))
            {
                match.PlaceOne();
            }
            else
            {
                match.PassTurn();
            }
        }

        return match;
    }

    /// <summary>连续 <paramref name="rounds"/> 个大回合按 <see cref="PlayRound"/> 推进。</summary>
    internal static MatchFlow PlayRounds(this MatchFlow match, int rounds, params PlayerId[] movers)
    {
        for (int i = 0; i < rounds; i++)
        {
            match.PlayRound(movers);
        }

        return match;
    }

    /// <summary>
    /// 当前玩家落 1 子：合法范围内取第一个能暂放、且整批能确认的空格；都不行就 Pass。
    /// 次序：本座位出生区（核心区）内的格在前，其余在后，各按坐标序。link-map-terrain 第 2 组：棋盘的外圈与中间带按主题带地形（崖壁、栅栏、深水），
    /// 核心区恒为高度 0 的草地——先落核心区，夹具的行为就不随主题地形变化（原按纯坐标序时，高台主题的崖壁会让第一枚子只剩一口气、第二大回合被提）。
    /// </summary>
    internal static void PlaceOne(this MatchFlow match)
    {
        PlayerId player = match.CurrentPlayer!.Value;
        StagedBatch batch = match.ToDeploy();
        int? zone = match.StateOf(player).BirthZone;
        foreach (Coord cell in match.LegalRangeFor(player).OrderBy(c => zone is { } z && match.Map.BirthZones[z].Contains(c) ? 0 : 1).ThenBy(c => c))
        {
            if (match.Board[cell].Occupant is not null || batch.Stage(cell, PieceType.Basic) is not null)
            {
                continue;
            }

            if (match.Confirm().Confirmed)
            {
                return;
            }

            batch.Clear();
        }

        match.Pass();
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

    /// <summary>
    /// 把开局坐标 <paramref name="before"/>（位于出生棋盘 <paramref name="zone"/>）沿本局全部链接记录平移到当前坐标——经唯一的换算工具 <see cref="Siege.Core.Board.Maps.LinkLayout"/>。
    /// </summary>
    internal static Coord Track(this MatchFlow match, Coord before, int zone)
    {
        var layout = new Siege.Core.Board.Maps.LinkLayout(match.Board.BaseMap.Boards, match.Links.Select(l => l.ShiftStep));
        if (layout.ZoneAt(before, 0) != zone)
        {
            throw new ArgumentException($"{before.ToNotation()} 不在开局出生棋盘 {zone} 上。", nameof(before));
        }

        return layout.Translate(before, 0, layout.Versions);
    }

    /// <summary>
    /// 出生棋盘 <paramref name="zone"/> 左下角起的相对坐标。link-map-terrain 第 2 组：出生区缩成核心区后改读棋盘清单（整块棋盘的左下角），
    /// 不再取出生区的最小坐标；棋盘内可能有岩石 / 深水，调用方取的偏移须落在可落子格上（用 <see cref="CoreCell"/> 取核心区内恒可落子的格）。
    /// </summary>
    internal static Coord BirthCell(MapData map, int zone, int dx, int dy)
    {
        BoardPlate plate = map.BirthBoard(zone);
        return new Coord(plate.Origin.X + dx, plate.Origin.Y + dy);
    }

    /// <summary>出生棋盘 <paramref name="zone"/> 核心区左下角起的相对坐标（0–2）：核心区是高度 0 的草地，恒可落子。</summary>
    internal static Coord CoreCell(MapData map, int zone, int dx, int dy)
    {
        Coord core = map.BirthBoard(zone).CoreOrigin;
        return new Coord(core.X + dx, core.Y + dy);
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
