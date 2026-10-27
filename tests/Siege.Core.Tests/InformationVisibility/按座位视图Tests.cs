using System.Text.Json;
using System.Text.Json.Serialization;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.InformationVisibility;

/// <summary>
/// 规格：openspec/changes/open-map/specs/information-visibility —— Requirement: 始终公开的信息（锁定后对全部玩家公开、全图可见）；
/// match-setup —— Requirement: 开局信息公开范围、匿名同时插旗（插旗阶段只见占位）。实现裁决 B4：<see cref="MatchFlow.PublishFor"/>（过滤边界 = 所在战场 = 全图）。
/// open-map 1.4：原「其他战场不可见」各条按反向重导（他人棋盘 / 势力 / 归属都可见）；「链接后立即可见」Skip（段 B 删除）。
/// </summary>
/// <remarks>
/// 变异验证（段 B 后半实跑）：M-B16 <c>PublishFor</c> 的可见格改为全图可落子格 → 「他人战场呈现为未展开」「数据层过滤」「链接后立即可见」「战场结算结果全局公开」红 4；
/// M-B17 同战场判定恒真 → 「他人势力不可见」「多人同区锁定后只见同棋盘者」「数据层过滤」「链接后立即可见」红 4。
/// 段 B check（裁决 S8）：M-C1 <c>SeatCellView.Ownership</c> 的争议格改报中立 → 「同战场空格归属三态可读」红 1。
/// </remarks>
public class 按座位视图Tests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter(), new BigIntegerJsonConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public void 他人棋盘锁定后即展开()
    {
        // Scenario（open-map「他人棋盘从第 1 大回合起可见」）：玩家 A 在棋盘 1，查询棋盘 4 内任一格 → 返回该格的地形与占用（原「他人战场呈现为未展开」反向）。
        MatchFlow match = Started([0, 1, 3, 4]);
        match.Place(P2, CoreCell(match.Map, 3, 1, 1));
        SeatPublicView a = match.PublishFor(P0);

        Coord other = CoreCell(match.Map, 3, 1, 1);
        Assert.True(a.IsExpanded(other));
        Assert.Equal(P2, a.CellAt(other)!.Owner);
        Assert.True(a.IsExpanded(BirthCell(match.Map, 0, 0, 0)));
        // G1（link-map-terrain 2.6）：展开的是整块棋盘的外接矩形（含岩石与未架桥深水）；map-expand-25 段 B：插旗阶段之后展开整张图的全部格。
        Assert.Equal(match.Map.Width * match.Map.Height, a.Cells.Length);
        Assert.Equal(0, a.Battlefield);
    }

    [Fact]
    public void 本战场棋盘内的岩石与未架桥深水可见()
    {
        // G1（负责人 2026-10-14 裁决 A，tasks 2.6）：战斗阶段按座位可见的格 = 本战场所含各棋盘外接矩形内的全部格，包括岩石与未架桥深水
        // （否则河流与岩石在视图里成了空缺）；open-map：本战场 = 全图，全部平台都在。Core 视图与表现层的按座位过滤一致。
        // 载体：从 link:1:4 起找第一张"河流主题棋盘里同时有岩石与未架桥深水"的图（只在每张图的前 5 块平台里找）。
        (MapData map, int zone) = Enumerable.Range(1, 200).Select(s => LinkMapGenerator.Generate((ulong)s, 4))
            .SelectMany(m => Enumerable.Range(0, 5).Select(z => (Map: m, Zone: z)))
            .First(t => t.Map.Boards[t.Zone].Theme == BoardTheme.River && t.Map.Boards[t.Zone].Cells().Any(c => t.Map.Obstacles.Contains(c)));
        BoardPlate plate = map.BirthBoard(zone);
        Coord[] water = [.. plate.Cells().Where(map.TerrainData.IsUnbridgedDeepWater)];
        Coord[] rocks = [.. plate.Cells().Where(map.Obstacles.Contains)];
        Assert.NotEmpty(water);   // 样本口径
        Assert.NotEmpty(rocks);
        int[] others = [.. Enumerable.Range(0, 5).Where(z => z != zone).Take(3)];
        MatchFlow match = MatchFlow.Create(map, TestMaps.Seed, All, Options);
        match.PlantSequentially([(P0, zone), (P1, others[0]), (P2, others[1]), (P3, others[2])]);
        Assert.NotEqual(MatchPhase.FlagPlanting, match.Phase);

        SeatPublicView a = match.PublishFor(P0);
        Assert.Equal(plate.Cells().Order().Select(c => c.ToNotation()), a.Cells.Where(c => plate.Contains(Coord.Parse(c.Coord))).Select(c => c.Coord));
        SeatCellView w = a.CellAt(water[0])!;
        Assert.Equal((Terrain.Obstacle, Surface.DeepWater, false), (w.Terrain, w.Surface, w.Bridge));
        Assert.True(w.Ownership is { Kind: OwnershipKind.Obstacle }, $"{w.Ownership}");
        SeatCellView r = a.CellAt(rocks[0])!;
        Assert.Equal((Terrain.Obstacle, Surface.Grass), (r.Terrain, r.Surface));

        // 其他棋盘（open-map）：整块外接矩形（含它们的岩石与深水）也全部出现。
        // map-expand-25 段 A2：浮空岛有 10–12 块平台，原"5 块里除自己外的 4 块"改为全部其他平台。
        Assert.All(Enumerable.Range(0, map.Boards.Length).Where(z => z != zone), z => Assert.All(map.BirthBoard(z).Cells(), c => Assert.NotNull(a.CellAt(c))));

        // 表现层按座位过滤与 Core 视图同一组格；全部平台都整块可见，棋盘清单全部保留。
        DefaultBoardView view = DefaultBoardView.From(match.PublicWorldOf()).ForSeat(a, zone);
        Assert.Equal(a.Cells.Select(c => Coord.Parse(c.Coord)).Order(), view.Cells.Select(c => c.Coord).Order());
        Assert.Equal(map.Boards.AsEnumerable(), view.Boards.AsEnumerable());
    }

    [Fact]
    public void 他人势力可见()
    {
        // Scenario（open-map「他人棋盘从第 1 大回合起可见」）：A 在棋盘 1，查询棋盘 4 内玩家 C 的势力 → 返回 C 的势力明细（领地分、独占格、手牌类型）。
        MatchFlow match = Started([0, 1, 3, 4]);
        match.Place(P2, CoreCell(match.Map, 3, 1, 1));
        SeatPlayerView c = match.PublishFor(P0).Players.Single(p => p.Player == P2);

        Assert.Equal(PlayerStatus.Active, c.Status);
        Assert.True(c.SameBattlefield);
        Assert.Equal(match.Scoreboard.Latest!.Of(P2).Total, c.Power);
        Assert.Equal(match.Scoreboard.Latest!.Of(P2).TerritoryScore, c.TerritoryScore);
        Assert.NotEmpty(c.ExclusiveCells);
        Assert.NotNull(c.HandTypes);
        SeatPlayerView self = match.PublishFor(P0).Players.Single(p => p.Player == P0);
        Assert.True(self.SameBattlefield);
        Assert.NotNull(self.Power);
        Assert.NotNull(self.HandTypes);
    }

    [Fact]
    public void 数据层含全图()
    {
        // Scenario（open-map，原「数据层过滤」反向）：以座位 A 的视图序列化整局公开信息 → 序列化结果含全图每一格的数据与全部玩家的势力数值。
        MatchFlow match = Started([0, 1, 3, 4]);
        foreach (int zone in new[] { 1, 3, 4 })
        {
            match.Place(All[Array.IndexOf([0, 1, 3, 4], zone)], BirthCell(match.Map, zone, 2, 2));
        }

        string json = JsonSerializer.Serialize(match.PublishFor(P0), Json);

        HashSet<Coord> all = [.. match.Map.Boards.SelectMany(b => b.Cells())];
        Assert.Equal(513, all.Count);   // 样本口径：link:1:4 十块平台 7×8 ×2 + 6×6 + 6×7 + 7×7 ×3 + 8×8 ×2 + 6×8 = 513 格（map-expand-25 浮空岛；terrain-density 时为五块 198 格）
        Assert.All(all, c => Assert.Contains($"\"{c.ToNotation()}\"", json, StringComparison.Ordinal));
        // 全部玩家的势力数值都出现：没有任何 Power 为 null 的玩家。
        Assert.DoesNotContain("\"Power\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 同战场空格归属三态可读()
    {
        // 「始终公开的信息」第 3 项（裁决 S8）：每个空格的归属——独占及独占者 / 争议 / 中立——对全部玩家公开（open-map：唯一战场 = 全图）；
        // 取自势力快照里唯一的覆盖表（与领地分同一份）。
        MatchFlow match = Started([1, 1, 3, 4]);
        match.Place(P0, BirthCell(match.Map, 1, 2, 2));
        match.Place(P1, BirthCell(match.Map, 1, 4, 2));
        match.Place(P2, BirthCell(match.Map, 3, 2, 2));
        SeatPublicView a = match.PublishFor(P0);
        PowerSnapshot power = match.Scoreboard.Latest!;

        Assert.All(a.Cells, cell => Assert.Equal(power.Coverage.OwnershipOf(Coord.Parse(cell.Coord)), cell.Ownership));
        Assert.Contains(a.Cells, cell => cell.Ownership == new CellOwnership(OwnershipKind.Exclusive, P0));
        Assert.Contains(a.Cells, cell => cell.Ownership == new CellOwnership(OwnershipKind.Exclusive, P1));
        Assert.Contains(a.Cells, cell => cell.Ownership is { Kind: OwnershipKind.Contested, Owner: null });
        Assert.Contains(a.Cells, cell => cell.Ownership is { Kind: OwnershipKind.Neutral, Owner: null });
        // 与同战场对手的势力明细（独占空格坐标集合）同一份结果。
        foreach (PlayerId p in new[] { P0, P1 })
        {
            Assert.Equal(
                a.Players.Single(v => v.Player == p).ExclusiveCells.Order(StringComparer.Ordinal),
                a.Cells.Where(cell => cell.Ownership == new CellOwnership(OwnershipKind.Exclusive, p)).Select(cell => cell.Coord).Order(StringComparer.Ordinal));
        }

        // 别的棋盘（open-map）：C 的独占空格在 A 的视图里同样给出、归属指向 C。
        Assert.NotEmpty(power.Of(P2).ExclusiveCells);
        Assert.All(power.Of(P2).ExclusiveCells, c => Assert.Equal(new CellOwnership(OwnershipKind.Exclusive, P2), a.CellAt(c)!.Ownership));
        Assert.Contains(match.PublishFor(P2).Cells, cell => cell.Ownership == new CellOwnership(OwnershipKind.Exclusive, P2));   // C 自己同样可读
    }

    [Fact]
    public void 插旗阶段的可见信息()
    {
        // Scenario：插旗阶段 → 可见全部占位（map-expand-25：link:1:4 为 10 块平台）的位置与边长、对局种子与地图标识，看不到任何棋盘内部的格，也看不到任何旗帜。
        // relic-redesign W1：原载体 LinkModeFixtures.RelicMap（手写信物格夹具）随旧信物删除；开局没有信物（第 4 大回合起刷新），插旗阶段与锁定后的信物列表都为空。
        MatchFlow match = Create();
        match.Flags.Plant(P1, 2);
        SeatPublicView a = match.PublishFor(P0);
        Assert.Equal(10, a.Placeholders.Length);
        Assert.Empty(a.Cells);
        Assert.Empty(a.Relics);
        Assert.Equal(MapId, a.MapId);
        Assert.Equal(TestMaps.Seed.ToString(), a.Seed);
        Assert.All(a.Players, p => Assert.False(p.SameBattlefield));
        Assert.Equal((match.Map.BirthBoard(2).Width, match.Map.BirthBoard(2).Height), (a.Placeholders[2].Width, a.Placeholders[2].Height));   // 占位取整块棋盘

        // Scenario：锁定后只见自己的棋盘 → 可见棋盘 3 的全部格，其余仍只是占位。
        match.Flags.Plant(P0, 2);
        SeatPublicView locked = match.PublishFor(P0);
        Assert.Equal(match.Map.BirthBoard(2).Cells().Order().Select(c => c.ToNotation()), locked.Cells.Select(c => c.Coord));
        Assert.Empty(locked.Relics);
        Assert.Null(locked.RelicForecast);
    }

    [Fact]
    public void 多人同区锁定后全员互见()
    {
        // match-setup Scenario → open-map：A 与 B 都锁定棋盘 2 → 锁定后 A、B 与其他玩家都在唯一战场上，彼此可见（原「只见同棋盘者」反向）。
        MatchFlow match = Started([1, 1, 3, 4]);
        Assert.True(match.PublishFor(P0).Players.Single(p => p.Player == P1).SameBattlefield);
        Assert.True(match.PublishFor(P2).Players.Single(p => p.Player == P0).SameBattlefield);
        Assert.True(match.PublishFor(P2).Players.Single(p => p.Player == P1).SameBattlefield);
        Assert.All(All, seat => Assert.All(match.PublishFor(seat).Players, p => Assert.True(p.SameBattlefield)));
    }

    [Fact]
    public void 战场结算结果全局公开()
    {
        // Scenario（open-map）：唯一战场结算、C 胜出 → 全部玩家可读到"战场 1 已结算，胜者 C"与终局结果；盘面照旧可读。
        MatchFlow match = Started([0, 1, 3, 3]).PlayRound(P2).PassRound();
        SeatSettlementView settled = Assert.Single(match.PublishFor(P0).Settlements);
        Assert.Equal((0, P2), (settled.Battlefield, settled.Winner));
        Assert.True(match.PublishFor(P0).IsExpanded(BirthCell(match.Map, 3, 0, 0)));
        Assert.Equal(PlayerStatus.Eliminated, match.PublishFor(P0).Players.Single(p => p.Player == P3).Status);
        Assert.NotNull(match.PublishFor(P0).Result);
    }

    [Fact]
    public void 行动顺序全局公开带入清单只给本人()
    {
        // Scenario：任意玩家查询当前行动顺序 → 可读到全部在局玩家的顺序。
        // relic-redesign R22（接线段 W2 改写，原"带入全局公开"属旧补给规则）：带入清单挂出之前只对本人可见——别的座位看不到，本座位看到自己的未挂出件。
        MatchFlow match = MatchFlow.Create(Map, TestMaps.Seed, All, Options with
        {
            CarryInOut = true,
            CarryIns = System.Collections.Immutable.ImmutableSortedDictionary<PlayerId, Siege.Core.PieceRelics.RelicLoadout>.Empty
                .Add(P2, new([new Siege.Core.PieceRelics.CarriedRelic(Siege.Core.PieceRelics.RelicKind.WarDrum, true)])),
        });
        match.PlantSequentially(All.Select((p, i) => (p, new[] { 0, 1, 3, 4 }[i])));
        SeatPublicView a = match.PublishFor(P0);
        Assert.Equal(match.ActionOrder, a.ActionOrder);
        Assert.Equal(4, a.ActionOrder.Length);
        Assert.Empty(a.OwnCarried);
        SeatCarriedView own = Assert.Single(match.PublishFor(P2).OwnCarried);
        Assert.Equal((0, Siege.Core.PieceRelics.RelicKind.WarDrum, true), (own.Index, own.Kind, own.Insured));
    }
}
