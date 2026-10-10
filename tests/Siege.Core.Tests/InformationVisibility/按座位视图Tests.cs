using System.Text.Json;
using System.Text.Json.Serialization;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.InformationVisibility;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/information-visibility —— Requirement: 始终公开的信息（按战场）、其他战场不可见；
/// match-setup —— Requirement: 开局信息公开范围、匿名同时插旗（锁定后只见同棋盘者）。实现裁决 B4：<see cref="MatchFlow.PublishFor"/>。
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
    public void 他人战场呈现为未展开()
    {
        // Scenario：玩家 A 在战场 1，查询战场 4 内任一格 → 返回"未展开"，不含地形与占用。
        MatchFlow match = Started([0, 1, 3, 4]);
        match.Place(P2, BirthCell(match.Map, 3, 1, 1));
        SeatPublicView a = match.PublishFor(P0);

        Coord other = BirthCell(match.Map, 3, 1, 1);
        Assert.False(a.IsExpanded(other));
        Assert.Null(a.CellAt(other));
        Assert.True(a.IsExpanded(BirthCell(match.Map, 0, 0, 0)));
        Assert.Equal(match.Map.BirthZones[0].Count, a.Cells.Length);
        Assert.Equal(0, a.Battlefield);
    }

    [Fact]
    public void 他人势力不可见()
    {
        // Scenario：A 在战场 1，查询战场 4 内玩家 C 的势力 → 只返回 C 的在局状态，不含势力数值。
        MatchFlow match = Started([0, 1, 3, 4]);
        match.Place(P2, BirthCell(match.Map, 3, 1, 1));
        SeatPlayerView c = match.PublishFor(P0).Players.Single(p => p.Player == P2);

        Assert.Equal(PlayerStatus.Active, c.Status);
        Assert.False(c.SameBattlefield);
        Assert.Null(c.Power);
        Assert.Null(c.TerritoryScore);
        Assert.Empty(c.ExclusiveCells);
        Assert.Null(c.HandTypes);
        SeatPlayerView self = match.PublishFor(P0).Players.Single(p => p.Player == P0);
        Assert.True(self.SameBattlefield);
        Assert.NotNull(self.Power);
        Assert.NotNull(self.HandTypes);
    }

    [Fact]
    public void 链接后立即可见()
    {
        // Scenario：战场 1 与战场 4 链接 → A 立即可读到原战场 4 的全部棋子、设施与 C 的势力明细（含链接前的改造）。
        MatchFlow match = Started([0, 1, 3, 4]);
        Coord cStone = BirthCell(match.Map, 3, 1, 1);
        Coord fenceA = BirthCell(match.Map, 3, 0, 0);
        Coord fenceB = new(fenceA.X + 1, fenceA.Y);
        match.Place(P2, cStone);
        match.Board.ApplyTerrainEdits([TerrainEdit.Fence(fenceA, fenceB)]);
        Assert.DoesNotContain($"{fenceA.ToNotation()}-{fenceB.ToNotation()}", match.PublishFor(P0).Fences);
        match.PassRounds(3);
        match.ApplyPairing([(0, 3), (1, 4)]);

        SeatPublicView a = match.PublishFor(P0);
        Assert.Equal(P2, a.CellAt(cStone)!.Owner);
        Assert.Contains($"{fenceA.ToNotation()}-{fenceB.ToNotation()}", a.Fences);
        SeatPlayerView c = a.Players.Single(p => p.Player == P2);
        Assert.True(c.SameBattlefield);
        Assert.Equal(match.Scoreboard.Latest!.Of(P2).Total, c.Power);
        Assert.Contains(a.Groups, g => g.Owner == P2 && g.Stones.Contains(cStone.ToNotation()));
        Assert.Contains(a.Relics, r => match.Map.BirthZones[3].Contains(Coord.Parse(r.Coord)));
        Assert.Null(a.Players.Single(p => p.Player == P1).Power);    // 另一对仍不可见
    }

    [Fact]
    public void 数据层过滤()
    {
        // Scenario：以座位 A 的视图序列化整局公开信息 → 序列化结果中不含任何未与 A 链接的战场的格数据。
        MatchFlow match = Started([0, 1, 3, 4]);
        foreach (int zone in new[] { 1, 3, 4 })
        {
            match.Place(All[Array.IndexOf([0, 1, 3, 4], zone)], BirthCell(match.Map, zone, 2, 2));
        }

        match.PassRounds(3);
        match.ApplyPairing([(0, 3), (1, 4)]);
        string json = JsonSerializer.Serialize(match.PublishFor(P0), Json);

        HashSet<Coord> mine = [.. match.Board.Battlefields.ById(0)!.Cells];
        // 占位（每块出生棋盘的位置与边长）是全局公开信息，其原点坐标不算"格数据"，从判据里除去。
        HashSet<Coord> origins = [.. match.Map.BirthZones.Select(z => new Coord(z.Min(c => c.X), z.Min(c => c.Y)))];
        HashSet<Coord> theirs = [.. match.Board.Battlefields.All.Where(f => f.Id != 0).SelectMany(f => f.Cells).Where(c => !origins.Contains(c))];
        Assert.True(theirs.Count > 100);
        Assert.All(theirs, c => Assert.DoesNotContain($"\"{c.ToNotation()}\"", json, StringComparison.Ordinal));
        Assert.Contains($"\"{BirthCell(match.Map, 3, 2, 2).ToNotation()}\"", json, StringComparison.Ordinal);   // 反面：链接进来的格在
        Assert.True(mine.Count(c => json.Contains($"\"{c.ToNotation()}\"", StringComparison.Ordinal)) == mine.Count);
        // 他人势力数值不出现：P1 的棋子在战场 1，P0 视图里 P1 的势力为 null。
        Assert.Contains("\"Power\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 同战场空格归属三态可读()
    {
        // 「始终公开的信息」第 3 项（裁决 S8）：同战场内每个空格的归属——独占及独占者 / 争议 / 中立——对同战场玩家公开；
        // 取自势力快照里唯一的覆盖表（与领地分同一份），他人战场的格不给出。
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

        // 他人战场：C 的独占空格存在，但在 A 的视图里一格都不给出，也没有任何归属指向 C。
        Assert.NotEmpty(power.Of(P2).ExclusiveCells);
        Assert.All(power.Of(P2).ExclusiveCells, c => Assert.Null(a.CellAt(c)));
        Assert.DoesNotContain(a.Cells, cell => cell.Ownership?.Owner == P2);
        Assert.Contains(match.PublishFor(P2).Cells, cell => cell.Ownership == new CellOwnership(OwnershipKind.Exclusive, P2));   // 反面：C 自己可读
    }

    [Fact]
    public void 插旗阶段的可见信息()
    {
        // Scenario：插旗阶段 → 可见 5 个占位的位置与边长、对局种子与地图标识，看不到任何棋盘内部的格，也看不到任何旗帜。
        MatchFlow match = Create();
        match.Flags.Plant(P1, 2);
        SeatPublicView a = match.PublishFor(P0);
        Assert.Equal(5, a.Placeholders.Length);
        Assert.Empty(a.Cells);
        Assert.Empty(a.Relics);
        Assert.Equal(MapId, a.MapId);
        Assert.Equal(MatchFixtures.Seed.ToString(), a.Seed);
        Assert.All(a.Players, p => Assert.False(p.SameBattlefield));
        Assert.Equal(match.Map.BirthZones[2].Count, a.Placeholders[2].Width * a.Placeholders[2].Height);

        // Scenario：锁定后只见自己的棋盘 → 可见棋盘 3 的全部格与信物格位置，其余 4 块仍只是占位。
        match.Flags.Plant(P0, 2);
        SeatPublicView locked = match.PublishFor(P0);
        Assert.Equal(match.Map.BirthZones[2].Order().Select(c => c.ToNotation()), locked.Cells.Select(c => c.Coord));
        Assert.Single(locked.Relics);
        Assert.Null(locked.Relics[0].Content);
    }

    [Fact]
    public void 多人同区锁定后只见同棋盘者()
    {
        // match-setup Scenario：A 与 B 都锁定棋盘 2 → 锁定后 A 与 B 各自看到对方在棋盘 2 上，其他玩家看不到棋盘 2 上有谁。
        MatchFlow match = Started([1, 1, 3, 4]);
        Assert.True(match.PublishFor(P0).Players.Single(p => p.Player == P1).SameBattlefield);
        Assert.False(match.PublishFor(P2).Players.Single(p => p.Player == P0).SameBattlefield);
        Assert.False(match.PublishFor(P2).Players.Single(p => p.Player == P1).SameBattlefield);
    }

    [Fact]
    public void 战场结算结果全局公开()
    {
        // Scenario：战场 4 结算、C 胜出 → 其他战场的玩家可读到"战场 4 已结算，胜者 C"，但读不到战场 4 的盘面。
        MatchFlow match = Started([0, 1, 3, 3]).PlayRound(P2).PassRound();
        SeatSettlementView settled = Assert.Single(match.PublishFor(P0).Settlements);
        Assert.Equal((3, P2), (settled.Battlefield, settled.Winner));
        Assert.False(match.PublishFor(P0).IsExpanded(BirthCell(match.Map, 3, 0, 0)));
        Assert.Equal(PlayerStatus.Eliminated, match.PublishFor(P0).Players.Single(p => p.Player == P3).Status);
    }

    [Fact]
    public void 行动顺序与带入全局公开()
    {
        // Scenario：任意玩家查询当前行动顺序 → 可读到全部在局玩家的顺序；带入不论对方在哪块棋盘都可读。
        MatchFlow match = MatchFlow.Create(Map, MatchFixtures.Seed, All, Options with
        {
            CarryInOut = true,
            CarryIns = System.Collections.Immutable.ImmutableSortedDictionary<PlayerId, Carry.CarryIn>.Empty.Add(P2, new Carry.CarryIn(Carry.SupplyKind.SpareStone)),
        });
        match.PlantSequentially(All.Select((p, i) => (p, new[] { 0, 1, 3, 4 }[i])));
        SeatPublicView a = match.PublishFor(P0);
        Assert.Equal(match.ActionOrder, a.ActionOrder);
        Assert.Equal(4, a.ActionOrder.Length);
        Assert.Equal(P2, a.CarryIns.Single().Player);
        Assert.Equal(Carry.SupplyKind.SpareStone, a.CarryIns.Single().CarryIn.Kind);
    }

    [Fact]
    public void 旧模式全图展开()
    {
        // 实现裁决 B4：旧模式下按座位视图不过滤（全图可见、全员同战场）；全知视图 Publish 原样保留。
        MatchFlow legacy = MatchFixtures.Started();
        SeatPublicView a = legacy.PublishFor(MatchFixtures.P0);
        Assert.Equal(legacy.Map.PlayableCount, a.Cells.Length);
        Assert.All(a.Players, p => Assert.True(p.SameBattlefield));
        Assert.Null(a.Battlefield);
    }
}
