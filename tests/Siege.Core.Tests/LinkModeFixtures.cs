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
    internal const string MapId = "link:1:4";

    private static readonly Lazy<MapData> LazyMap = new(() => MapCatalog.Resolve(MapId));

    internal static readonly PlayerId P0 = TestMaps.P0;
    internal static readonly PlayerId P1 = TestMaps.P1;
    internal static readonly PlayerId P2 = TestMaps.P2;
    internal static readonly PlayerId P3 = TestMaps.P3;
    internal static readonly PlayerId[] All = TestMaps.All;

    /// <summary><see cref="MapId"/>（<c>link:1:4</c>）的初始图：4 人浮空岛，4 个出生座位 + 4 块中立平台（seat-gap-25；map-expand-25 时为 10 块出生平台；原为 <c>link:1</c> 的 5 块紧贴出生棋盘）。</summary>
    internal static MapData Map => LazyMap.Value;

    /// <summary>链接模式、插旗立即（open-map 段 B：决赛倒计时 K 随拼接规则删除，选项即缺省）。</summary>
    internal static MatchOptions Options => MatchOptions.Immediate with { ScoringVersion = Siege.Core.Scoring.ScoringVersion.V1 };

    /// <summary>插旗阶段的链接模式对局。</summary>
    internal static MatchFlow Create(GameSeed? seed = null, MatchOptions? options = null) =>
        MatchFlow.Create(Map, seed ?? TestMaps.Seed, All, options ?? Options);

    /// <summary>按 <paramref name="zones"/>（P0..P3 各自的出生棋盘）锁定并开始第 1 大回合；每人手牌 50 枚普通子。</summary>
    internal static MatchFlow Started(int[] zones, GameSeed? seed = null, MatchOptions? options = null) => Plant(Create(seed, options), zones);

    private static MatchFlow Plant(MatchFlow match, int[] zones)
    {
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.Debug.LockFlagsAs(All.Select((p, i) => (p, zones[i])));   // seat-gap-25 E4：给定座次直接锁定，不做撞座位改派（规则层夹具）
        return match;
    }

    /// <summary>
    /// 平坦的手写链接档图（link-map-terrain 第 2 组）：<c>link:1</c> 的出生棋盘带主题地形（出生棋盘 0 是高台，外圈内侧有高度 2 的格），
    /// 角上的气与领地随主题变化；依赖"平坦的角"做势力算例的测试改用这张图。载体是 <see cref="LinkMapFixtures.Births"/>（五块 6×6，中间带只有东列两端两格林地）。
    /// 标识取 <c>link:90003</c>（链接模式只接受 link: 标识；不经生成器）。
    /// </summary>
    internal static readonly MapData FlatMap = LinkMapFixtures.Build("link:90003:4", LinkMapFixtures.Births);

    /// <summary>
    /// 在 <see cref="FlatMap"/> 上按 <paramref name="zones"/>（P0..P3 各自的出生棋盘）锁定并开始第 1 大回合；每人手牌 50 枚普通子。
    /// map-expand-25 段 A2：手写的 3+2 紧贴图在浮空岛校验器下恒不合规（画布、平台高度、坡道），改经 <see cref="MatchFlow.CreateUnvalidated"/> 建局。
    /// </summary>
    internal static MatchFlow StartedFlat(int[] zones, MatchOptions? options = null)
    {
        options ??= Options;
        MatchFlow match = MatchFlow.CreateUnvalidated(FlatMap, TestMaps.Seed, All, options);
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.Debug.LockFlagsAs(All.Select((p, i) => (p, zones[i])));   // seat-gap-25 E4：给定座次直接锁定，不做撞座位改派（规则层夹具）
        return match;
    }

    // ---------- 平坦 8×8 棋盘 0 的手写链接档图（retire-legacy-rules D2：原 9×9 Legacy 流程夹具的迁移载体） ----------

    /// <summary>平坦变体棋盘 0 的边长（链接档出生棋盘边长上限）。</summary>
    internal const int PlainSize = 8;

    /// <summary>平坦变体的地图标识。</summary>
    internal const string PlainMapId = "link:90008:4";

    /// <summary>
    /// 平坦变体的棋盘清单：棋盘 0 为 8×8、左下角 (2, 2)（画布口径同生成器：底边第 2 行、最西列第 2 列）；其余四块同 <see cref="LinkMapFixtures.Births"/> 的 6×6，
    /// 按间距 3 依次排在东侧（x = 13 / 22 / 31 / 40）。主题沿用 <see cref="LinkMapFixtures.Births"/> 的次序，只用于呈现。
    /// </summary>
    internal static readonly ImmutableArray<BoardPlate> PlainBoards =
    [
        new(new Coord(2, 2), PlainSize, PlainSize, BoardPlateKind.Birth, BoardTheme.Plateau),
        new(new Coord(13, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.River),
        new(new Coord(22, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Forest),
        new(new Coord(31, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Palisade),
        new(new Coord(40, 2), 6, 6, BoardPlateKind.Birth, BoardTheme.Mixed),
    ];

    /// <summary>平坦变体地图（无地形）。</summary>
    internal static MapData PlainMap() => PlainMap(null);

    /// <summary>
    /// 平坦变体地图（标识 <see cref="PlainMapId"/>）：棋盘 0 全是高度 0 的可落子草地——<b>没有</b>林地、深水、岩石，
    /// 其余四块棋盘同样平坦。原 9×9 夹具的局面整体平移 (+2, +2) 后落在棋盘 0 上（9×9 的 <c>A1</c> → 这里的 <c>C3</c>），
    /// 棋盘外一律障碍，与 9×9 的盘边封堵语义相同。偏离校验之处：中间带没有改造目标（裁决 M7）——所以只能经
    /// <see cref="MatchFlow.CreateUnvalidated"/> 建局。<paramref name="terrain"/> 写在本图坐标上（需要地形的用例：深水 / 林地 / 栅栏放棋盘 0 中间带）。
    /// </summary>
    internal static MapData PlainMap(TerrainData? terrain)
    {
        const int Margin = 2;
        int width = PlainBoards.Max(b => b.Origin.X + b.Width) + Margin;
        int height = PlainBoards.Max(b => b.Origin.Y + b.Height) + Margin;
        ImmutableHashSet<Coord> playable = [.. PlainBoards.SelectMany(b => b.Cells())];
        return new MapData
        {
            Id = PlainMapId,
            Width = width,
            Height = height,
            MaxPlayers = 4,
            Profile = MapProfile.Link,
            Boards = PlainBoards,
            MapVersion = 0,
            Obstacles = [.. TestMaps.Rect(0, 0, width, height).Where(c => !playable.Contains(c))],
            TerrainData = terrain ?? TerrainData.Flat,
            BirthZones = [.. PlainBoards.Select(b => (ImmutableHashSet<Coord>)[.. b.CoreCells()])],
            ChokePoints = [],
            CentralEntrance = PlainBoards[0].CoreOrigin,
        };
    }

    /// <summary>
    /// 平坦变体建局用的选项：调用方给的选项（缺省 <see cref="Options"/>）强制决赛倒计时 K = 0。
    /// 原 9×9 夹具的用例都写于决赛倒计时之前；建局后另断言地图确为链接图（<see cref="RequireLink"/>）。
    /// retire-legacy-rules 第 3 组：对局模式删除，<see cref="MatchFlow.CreateUnvalidated"/> 恒按链接规则建局，原"强制链接模式"一项随之去掉。
    /// </summary>
    internal static MatchOptions PlainOptions(MatchOptions? options) =>
        (options ?? Options) with { ScoringVersion = Siege.Core.Scoring.ScoringVersion.V1 };

    /// <summary>断言对局建在链接图上（见 <see cref="PlainOptions"/>；retire-legacy-rules 第 3 组起对局只有链接规则，原"模式为链接"的断言改为断言地图标识）。</summary>
    internal static MatchFlow RequireLink(MatchFlow match) =>
        LinkMapId.IsLinkMap(match.Map.Id) ? match : throw new InvalidOperationException($"夹具建局不在链接图上（{match.Map.Id}）。");

    /// <summary>平坦变体上建局、停在插旗阶段。</summary>
    internal static MatchFlow PlainCreate(GameSeed? seed = null, MatchOptions? options = null) =>
        PlainCreate(null, seed, options);

    /// <summary>同上，另可指定地形。</summary>
    internal static MatchFlow PlainCreate(TerrainData? terrain, GameSeed? seed = null, MatchOptions? options = null)
    {
        MapData map = PlainMap(terrain);
        return RequireLink(MatchFlow.CreateUnvalidated(map, seed ?? TestMaps.Seed, All, PlainOptions(options)));
    }

    /// <summary>
    /// 平坦变体上建局并依次插旗（缺省四人同锁棋盘 0：单一战场、锁定旗数 4 不进首轮配对、没有链接，第 1 大回合起合法范围即整块棋盘 0），
    /// 给每人 50 枚普通子，进入第 1 大回合。
    /// </summary>
    internal static MatchFlow PlainStarted(GameSeed? seed = null, int[]? zones = null, MatchOptions? options = null) =>
        PlainStarted(null, seed, zones, options);

    /// <summary>同上，另可指定地形。</summary>
    internal static MatchFlow PlainStarted(TerrainData? terrain, GameSeed? seed = null, int[]? zones = null, MatchOptions? options = null)
    {
        MatchFlow match = PlainCreate(terrain, seed, options);
        zones ??= [0, 0, 0, 0];
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.Debug.LockFlagsAs(All.Select((p, i) => (p, zones[i])));   // seat-gap-25 E4：给定座次直接锁定，不做撞座位改派（规则层夹具）
        return match;
    }

    /// <summary>
    /// 原 9×9 夹具坐标 → 平坦变体棋盘 0 上的坐标：整体平移 (+2, +2)（<c>A1</c> → <c>C3</c>）。只用于多个文件共用、同时被规则层（9×9）与链接载体引用的坐标数组；
    /// 单个用例里的坐标一律直接写平移后的值。
    /// </summary>
    internal static string[] Shift(params string[] cells) =>
        [.. cells.Select(Coord.Parse).Select(c => new Coord(c.X + 2, c.Y + 2).ToNotation())];

    // ---------- 流程推进扩展（retire-legacy-rules 1.2：原 9×9 流程夹具的扩展方法，原样迁来） ----------

    /// <summary>
    /// 把对局摆到第 <paramref name="majorRound"/> 大回合开始、指定顺序。出局判定不再有保护期豁免（restore-go-core-rules 裁决 #3），
    /// 这里也就没有"保护状态"可摆。
    /// </summary>
    internal static MatchFlow AtRound(this MatchFlow match, int majorRound, PlayerId[]? order = null)
    {
        match.Debug.SetMajorRound(majorRound);
        match.Debug.SetOrder(order ?? All);
        return match;
    }

    /// <summary>
    /// 直接在权威盘面摆子（绕过规则，只为构造局面），然后重算派生量。重算会照常置位「曾建立正势力」标记（与真实结算同一置位点），
    /// 所以这样摆上去的棋子若随后被全部提走，该玩家会按规则出局。
    /// </summary>
    internal static MatchFlow Stones(this MatchFlow match, PlayerId owner, params string[] cells)
    {
        foreach (string cell in cells)
        {
            match.Board.Place(Coord.Parse(cell), owner, PieceType.Basic);
        }

        match.Debug.Recalculate();
        return match;
    }

    /// <summary>跑完当前玩家的小回合：开始 → 征募（不选） → 部署指定落点 → 确认。落点为空即 Pass。</summary>
    internal static SettlementOutcome PlayTurn(this MatchFlow match, params string[] cells)
    {
        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        foreach (string cell in cells)
        {
            BatchFailure? failure = batch.Stage(Coord.Parse(cell), PieceType.Basic);
            if (failure is not null)
            {
                throw new InvalidOperationException($"夹具暂放失败：{failure.Message}");
            }
        }

        SettlementOutcome outcome = match.Confirm();
        if (!outcome.Confirmed)
        {
            throw new InvalidOperationException($"夹具确认失败：{outcome.Failure!.Message}");
        }

        return outcome;
    }

    /// <summary>当前玩家 Pass。</summary>
    internal static SettlementOutcome PassTurn(this MatchFlow match) => match.PlayTurn();

    /// <summary>某玩家在盘面上的棋子数。</summary>
    internal static int StoneCount(this MatchFlow match, PlayerId player) => match.Board.GroupsOf(player).Sum(g => g.Size);

    /// <summary>某种类事件的数量。</summary>
    internal static int CountEvents(this MatchFlow match, FlowEventKind kind) => match.Events.Count(e => e.Kind == kind);

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
}
