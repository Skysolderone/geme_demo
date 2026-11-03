using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/specs/match-setup —— Requirement: 大图自由插旗（big-map 裁决 2 / 4，D3）：合规旗格、锁定时按插旗先后的间距改派、AI 盲选、两档入口互斥、锁定前旗格不可见；
/// relic-sites 裁决 9：旗心与任一信物遗址切比雪夫距离 ≥ 7（「合规旗格的判定」的独立算式随之加上遗址项；「旗心距遗址不足7被拒_AI盲选合规」）。
/// </summary>
/// <remarks>
/// 变异验证（段 A 实跑，mutate.py，备份 → 改 → 跑本类 → 逐字节还原 → 刷新时间戳）：
/// M-F1 <c>FlagRules.Fallback</c> 改派时不要求与已确认旗间距 ≥ 60（<c>gap &gt;= FlagGap</c> → <c>gap &gt;= 0</c>，只取离原旗最近的合规格）→ 本类红 4、绿 2
/// （「四面太近的旗按先后改派」「AI盲选锁定后两两间距不小于60」「AI盲选不扰动首回合顺序」「锁定前旗格不可见」：改派后的旗间距 &lt; 60 或落座图 SEAT_GAP 47 校验不过）。
/// 未做（只列出、未实跑）：按玩家编号而非插旗先后确认、盲选改用 setup 子流、合规判据去掉"不在平台内"。
/// </remarks>
public class 自由插旗Tests
{
    private const string LargeId = "link:1:4:l";
    private static readonly Lazy<MapData> LazyLarge = new(() => MapCatalog.Resolve(LargeId));

    private static MapData Large => LazyLarge.Value;

    private static MatchFlow CreateLarge(GameSeed? seed = null, PlayerId[]? players = null) =>
        MatchFlow.Create(Large, seed ?? TestMaps.Seed, players ?? All, Options);

    [Fact]
    public void 合规旗格的判定()
    {
        // D3 / 追加裁决 19：可落子、高度 0、非桥、不在平台内；旗心 13×13 在画布内且不压中立平台；9×9 出生圈内可落子 ≥ 40；relic-sites 裁决 9：与任一信物遗址切比雪夫距离 ≥ 7。
        ImmutableArray<Coord> legal = FlagRules.LegalFlagCells(Large);
        Assert.True(legal.Length * 2 >= Large.PlayableCount, $"合规旗格 {legal.Length} 不足可落子 {Large.PlayableCount} 的一半。");
        Assert.Equal(legal, legal.OrderBy(c => c.Y).ThenBy(c => c.X));   // 先行后列

        // 测试内独立算式逐格复核（不调 IsLegalFlagCell）：整张图每一格的"合规"判定与 LegalFlagCells 一致。
        var set = legal.ToHashSet();
        foreach (Coord c in Large.AllCoords())
        {
            bool ground = Large.IsPlayable(c) && Large.HeightAt(c) == 0 && !Large.HasBridge(c) && !Large.Boards.Any(p => p.Contains(c));
            bool inside = c.X >= 6 && c.Y >= 6 && c.X + 6 < Large.Width && c.Y + 6 < Large.Height;
            // 13×13 与任何中立平台外接矩形至少隔 1 格（追加裁决 19）。
            bool clear = Large.Boards.All(p => c.X + 7 < p.Origin.X || p.Origin.X + p.Width < c.X - 6 || c.Y + 7 < p.Origin.Y || p.Origin.Y + p.Height < c.Y - 6);
            // 与任一遗址切比雪夫距离 ≥ 7（relic-sites 裁决 9）。
            bool apart = Large.RelicSites.All(s => Math.Max(Math.Abs(c.X - s.Cell.X), Math.Abs(c.Y - s.Cell.Y)) >= 7);
            int ring = 0;
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = c.X + dx, y = c.Y + dy;
                    ring += x >= 0 && y >= 0 && x < Large.Width && y < Large.Height && Large.IsPlayable(new Coord(x, y)) ? 1 : 0;
                }
            }

            bool expected = ground && inside && clear && apart && ring >= 40;
            Assert.True(expected == set.Contains(c), $"{c.ToNotation()}：独立算式 {expected}，LegalFlagCells {set.Contains(c)}。");
            Assert.Equal(expected, FlagRules.IsLegalFlagCell(Large, c));
        }

        // 反面样本口径：平台格、桥格、画布边缘、遗址格各至少有一个被排除。
        Assert.Contains(Large.Boards[0].Cells(), c => !set.Contains(c));
        Assert.Contains(Large.TerrainData.Bridges, c => !set.Contains(c));
        Assert.DoesNotContain(new Coord(2, 2), set);
        Assert.NotEmpty(Large.RelicSites);
        Assert.All(Large.RelicSites, s => Assert.DoesNotContain(s.Cell, set));

        // 小图没有合规旗格（出生座位制）。
        Assert.Empty(FlagRules.LegalFlagCells(Map));
        Assert.False(FlagRules.IsLegalFlagCell(Map, new Coord(10, 10)));
    }

    [Fact]
    public void 四面太近的旗按先后改派()
    {
        // 验收：人为给出 4 个相距 < 60 的旗 → 锁定后按先后改派、结果确定且两两 ≥ 60。先插者保住原旗；后面每一面改派到"与已确认旗间距 ≥ 60 的合规格里离原旗最近者"。
        ImmutableArray<Coord> legal = FlagRules.LegalFlagCells(Large);
        Coord anchor = legal[legal.Length / 2];
        Coord[] near = [.. legal.Where(c => FlagRules.Distance(c, anchor) is > 5 and < 30).Take(3)];
        Assert.Equal(3, near.Length);
        (PlayerId, Coord)[] plants = [(P2, anchor), (P0, near[0]), (P3, near[1]), (P1, near[2])];

        static Dictionary<PlayerId, Coord> Lock((PlayerId Player, Coord Cell)[] plants)
        {
            MatchFlow match = CreateLarge();
            foreach ((PlayerId player, Coord cell) in plants)
            {
                match.Flags.PlantAt(player, cell);
                Assert.Equal(cell, match.Flags.FlagCellOf(player));   // 锁定前自己看到的仍是自己插的旗
            }

            match.LockFlags();
            Assert.Equal(MatchPhase.InProgress, match.Phase);
            return All.ToDictionary(p => p, p => match.Flags.FlagCellOf(p)!.Value);
        }

        Dictionary<PlayerId, Coord> first = Lock(plants);
        Dictionary<PlayerId, Coord> second = Lock(plants);
        Assert.Equal(first, second);   // 确定性：不消费随机
        Assert.Equal(anchor, first[P2]);   // 先插者保住原旗
        var set = legal.ToHashSet();
        foreach (PlayerId p in All)
        {
            Assert.Contains(first[p], set);
        }

        Coord[] confirmed = [first[P2], first[P0], first[P3], first[P1]];
        for (int i = 0; i < confirmed.Length; i++)
        {
            for (int j = i + 1; j < confirmed.Length; j++)
            {
                Assert.True(FlagRules.Distance(confirmed[i], confirmed[j]) >= 60, $"旗 {confirmed[i].ToNotation()} 与 {confirmed[j].ToNotation()} 相距 {FlagRules.Distance(confirmed[i], confirmed[j])} < 60。");
            }
        }

        // "最近"用测试内独立算式复核：第 k 面改派旗到原旗的距离 ≤ 任何与前 k 面确认旗都 ≥ 60 的合规格到原旗的距离。
        for (int k = 1; k < confirmed.Length; k++)
        {
            Coord original = plants[k].Item2;
            int actual = FlagRules.Distance(confirmed[k], original);
            int best = legal.Where(c => confirmed.Take(k).All(q => FlagRules.Distance(c, q) >= 60)).Min(c => FlagRules.Distance(c, original));
            Assert.Equal(best, actual);
        }

        // 换一个先后：后插者变先插者，保住的旗随之变——改派确实按插旗先后，不按玩家编号。
        Dictionary<PlayerId, Coord> swapped = Lock([(P0, near[0]), (P2, anchor), (P3, near[1]), (P1, near[2])]);
        Assert.Equal(near[0], swapped[P0]);
        Assert.NotEqual(anchor, swapped[P2]);
    }

    [Fact]
    public void AI盲选锁定后两两间距不小于60()
    {
        // 验收：4 名 AI 盲选（地图种子 1–20 × 对局种子 1–5）锁定后两两 ≥ 60；旗都是合规旗格；同种子重开两次相同。
        for (ulong mapSeed = 1; mapSeed <= 20; mapSeed++)
        {
            MapData map = MapCatalog.Resolve(LinkMapId.Format(mapSeed, 4, MapTier.Large));
            var legal = FlagRules.LegalFlagCells(map).ToHashSet();
            for (ulong gameSeed = 1; gameSeed <= 5; gameSeed++)
            {
                static Coord[] Run(MapData map, ulong gameSeed)
                {
                    MatchFlow match = MatchFlow.Create(map, new GameSeed(gameSeed), All, Options);
                    ImmutableArray<(PlayerId Player, int Zone)> zones = match.PlantPrototype();
                    Assert.Equal(MatchPhase.InProgress, match.Phase);
                    Assert.Equal([0, 1, 2, 3], zones.Select(z => z.Zone));   // D4：出生区编号按玩家编号升序
                    return [.. All.Select(p => match.Flags.FlagCellOf(p)!.Value)];
                }

                Coord[] flags = Run(map, gameSeed);
                Assert.Equal(flags, Run(map, gameSeed));
                Assert.All(flags, f => Assert.Contains(f, legal));
                for (int i = 0; i < flags.Length; i++)
                {
                    for (int j = i + 1; j < flags.Length; j++)
                    {
                        Assert.True(FlagRules.Distance(flags[i], flags[j]) >= 60, $"link:{mapSeed}:4:l 对局种子 {gameSeed}：旗 {flags[i].ToNotation()} 与 {flags[j].ToNotation()} 相距 {FlagRules.Distance(flags[i], flags[j])}。");
                    }
                }
            }
        }
    }

    [Fact]
    public void AI盲选不扰动首回合顺序()
    {
        // 盲选只消费 flag-pick：人工换一个旗格，首回合顺序与种子兜底顺序不变；两种人数都能开局。
        ImmutableArray<Coord> legal = FlagRules.LegalFlagCells(Large);
        MatchFlow a = CreateLarge();
        a.PlantPrototypeAt((P0, legal[0]));
        MatchFlow b = CreateLarge();
        b.PlantPrototypeAt((P0, legal[^1]));
        Assert.Equal(a.ActionOrder, b.ActionOrder);
        Assert.Equal(legal[0], a.Flags.FlagCellOf(P0));
        Assert.Equal(legal[^1], b.Flags.FlagCellOf(P0));
        Assert.Equal(a.Flags.FlagCellOf(P1), b.Flags.FlagCellOf(P1));   // 其余 AI 的抽样不随人工选择变（只经改派才可能变）

        MatchFlow two = MatchFlow.Create(MapCatalog.Resolve("link:3:2:l"), TestMaps.Seed, [P0, P1], Options);
        two.PlantPrototype();
        Assert.True(FlagRules.Distance(two.Flags.FlagCellOf(P0)!.Value, two.Flags.FlagCellOf(P1)!.Value) >= 60);
    }

    [Fact]
    public void 人工插旗排在AI之前_不被盲选挤走()
    {
        // D3：AI 盲选按 Players 次序排在人类之后——锁定时按插旗先后改派，先插者保住原旗。取 P0 的盲选旗格附近（< 60）的合规格给 P2 人工插旗：P2 保住原旗，P0 被改派；
        // 人工指定者不抽、不消费 flag-pick：编号在它之前的 AI 抽样与无人工时相同（之后的 AI 顺延拿到下一次抽样，见 determinism.md flag-pick 行）。
        ImmutableArray<(PlayerId Player, Coord Cell)> blind = PrototypeZoneAssignment.AssignBlindCells(Large, TestMaps.Seed, All);
        Coord p0 = blind.Single(b => b.Player == P0).Cell;
        Coord manual = FlagRules.LegalFlagCells(Large).First(c => FlagRules.Distance(c, p0) is > 0 and < FlagRules.FlagGap);

        ImmutableArray<(PlayerId Player, Coord Cell)> withManual = PrototypeZoneAssignment.AssignBlindCells(Large, TestMaps.Seed, All, (P2, manual));
        Assert.Equal(P2, withManual[0].Player);   // 人工指定者排在最前
        Assert.Equal([P0, P1, P3], withManual.Skip(1).Select(b => b.Player));
        Assert.Equal(blind.Where(b => b.Player == P0 || b.Player == P1).Select(b => b.Cell), withManual.Skip(1).Take(2).Select(b => b.Cell));   // P0 / P1 的旗格与无人工时相同

        MatchFlow match = CreateLarge();
        match.PlantPrototypeAt((P2, manual));
        Assert.Equal(manual, match.Flags.FlagCellOf(P2));
        Assert.NotEqual(p0, match.Flags.FlagCellOf(P0));
        Assert.True(FlagRules.Distance(match.Flags.FlagCellOf(P0)!.Value, manual) >= FlagRules.FlagGap);
    }

    [Fact]
    public void 旗心距遗址不足7被拒_AI盲选合规()
    {
        // relic-sites 裁决 9：遗址周围 13×13（距离 0–6）全部不合规且原因文案点名遗址；距离恰 7 且其余判据满足的格合规；PlantAt 的报文带原因；AI 盲选（LegalFlagCells）自动遵守。
        Assert.Equal(7, FlagRules.SiteClearance);
        Assert.NotEmpty(Large.RelicSites);
        int rejectedBySite = 0;
        foreach (RelicSite site in Large.RelicSites)
        {
            for (int dy = -6; dy <= 6; dy++)
            {
                for (int dx = -6; dx <= 6; dx++)
                {
                    int x = site.Cell.X + dx, y = site.Cell.Y + dy;
                    if (x < 0 || y < 0 || x >= Large.Width || y >= Large.Height)
                    {
                        continue;   // 画布外的格不构造坐标（Coord 不接受负值）；遗址可以离画布边只有 3 格，13×13 会出界。
                    }

                    var c = new Coord(x, y);
                    Assert.False(FlagRules.IsLegalFlagCell(Large, c), $"{c.ToNotation()} 距遗址 {site.Cell.ToNotation()} 只有 {Math.Max(Math.Abs(dx), Math.Abs(dy))} 却合规。");
                    string problem = FlagRules.FlagCellProblem(Large, c)!;
                    if (problem.Contains("遗址", StringComparison.Ordinal))
                    {
                        Assert.Contains(site.Cell.ToNotation(), problem, StringComparison.Ordinal);
                        rejectedBySite++;
                    }
                }
            }
        }

        Assert.True(rejectedBySite > 0, "没有任何格是因为遗址被拒的（都被更早的判据挡下），测不到遗址文案。");

        // 距离恰 7 的环上至少有一格合规（样本口径：规则是 ≥ 7 而不是 > 7）。
        Assert.Contains(Large.RelicSites, s => Enumerable.Range(-7, 15).SelectMany(dx => new[] { (dx, -7), (dx, 7), (-7, dx), (7, dx) })
            .Any(d => FlagRules.IsLegalFlagCell(Large, new Coord(s.Cell.X + d.Item1, s.Cell.Y + d.Item2))));

        // 插旗入口：报文含"不是合规旗格"与原因（遗址点名）；其他原因的报文可分辨（平台格说平台，不说遗址）。
        MatchFlow match = CreateLarge();
        Coord onSite = Large.RelicSites[0].Cell;
        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => match.Flags.PlantAt(P0, onSite));
        Assert.Contains($"{onSite.ToNotation()} 不是合规旗格", ex.Message, StringComparison.Ordinal);
        Assert.Contains("遗址", ex.Message, StringComparison.Ordinal);
        SiegeRuleException plate = Assert.Throws<SiegeRuleException>(() => match.Flags.PlantAt(P0, Large.Boards[0].Origin));
        Assert.Contains("平台", plate.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("遗址", plate.Message, StringComparison.Ordinal);
        Assert.Null(match.Flags.FlagCellOf(P0));

        // AI 盲选 + 落座：地图种子 1–5 × 对局种子 1–3，锁定后每面旗与每处遗址 ≥ 7、13×13 出生棋盘不含任何遗址、落座图过校验。
        for (ulong mapSeed = 1; mapSeed <= 5; mapSeed++)
        {
            MapData map = MapCatalog.Resolve(LinkMapId.Format(mapSeed, 4, MapTier.Large));
            for (ulong gameSeed = 1; gameSeed <= 3; gameSeed++)
            {
                MatchFlow m = MatchFlow.Create(map, new GameSeed(gameSeed), All, Options);
                m.PlantPrototype();
                foreach (PlayerId p in All)
                {
                    Coord flag = m.Flags.FlagCellOf(p)!.Value;
                    Assert.All(map.RelicSites, s => Assert.True(FlagRules.Distance(flag, s.Cell) >= 7, $"link:{mapSeed}:4:l 种子 {gameSeed}：旗 {flag.ToNotation()} 距遗址 {s.Cell.ToNotation()} 只有 {FlagRules.Distance(flag, s.Cell)}。"));
                }

                Assert.All(map.RelicSites, s => Assert.Null(m.Board.BaseMap.BirthBoardAt(s.Cell)));
                Assert.Equal(map.RelicSites, m.Board.BaseMap.RelicSites);
                Assert.True(MapValidator.Validate(m.Board.BaseMap).IsValid);
            }
        }
    }

    [Fact]
    public void 两档入口互斥()
    {
        // 小图入口拒绝旗格、大图入口拒绝座位编号（D3：按 map.Tier 只允许其一）。
        MatchFlow small = Create();
        Assert.False(small.Flags.IsLarge);
        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => small.Flags.PlantAt(P0, new Coord(10, 10)));
        Assert.Contains("小图", ex.Message, StringComparison.Ordinal);
        Assert.Throws<SiegeRuleException>(() => small.PlantPrototypeAt((P0, new Coord(10, 10))));
        Assert.Empty(small.Flags.LegalCells);

        MatchFlow large = CreateLarge();
        Assert.True(large.Flags.IsLarge);
        Assert.Equal(4, large.Flags.ZoneCount);
        ex = Assert.Throws<SiegeRuleException>(() => large.Flags.Plant(P0, 0));
        Assert.Contains("大图", ex.Message, StringComparison.Ordinal);
        Assert.Throws<SiegeRuleException>(() => large.PlantPrototype((P0, 0)));
        Assert.Throws<SiegeRuleException>(() => large.Flags.PlantAt(P0, new Coord(2, 2)));   // 不合规旗格
        Assert.Throws<SiegeRuleException>(() => large.PlantPrototypeAt((P0, Large.Boards[0].Origin)));   // 平台格不合规
        Assert.Equal(MatchPhase.FlagPlanting, large.Phase);
    }

    [Fact]
    public void 锁定前旗格不可见()
    {
        // 信息边界（D3）：公开视图只有"是否已锁定"；他人的旗格在锁定前对任何座位不可见；锁定后旗格随出生区公开。
        MatchFlow match = CreateLarge();
        ImmutableArray<Coord> legal = FlagRules.LegalFlagCells(Large);
        match.Flags.PlantAt(P0, legal[0]);
        Assert.Equal([nameof(FlagPublicView.IsLocked)], typeof(FlagPublicView).GetProperties().Select(p => p.Name));
        Assert.Equal(new FlagPublicView(false), match.Flags.PublicView());
        SeatFlagView other = match.Flags.ViewFor(P1);
        Assert.Null(other.OwnCell);
        Assert.Null(other.OwnZone);
        Assert.Empty(other.SameBoard);
        Assert.Equal(legal[0], match.Flags.ViewFor(P0).OwnCell);
        Assert.Null(match.Flags.ViewFor(P0).OwnZone);   // 出生区编号锁定时才有
        Assert.Null(match.Flags.FlagOf(P0));
        Assert.Empty(match.Publish().Board.BaseMap.BirthZones);   // 锁定前公开快照里没有出生区

        match.LockFlags();
        Assert.Equal(new FlagPublicView(true), match.Flags.PublicView());
        foreach (PlayerId p in All)
        {
            SeatFlagView view = match.Flags.ViewFor(p);
            Assert.Equal([p], view.SameBoard);
            Assert.Equal(match.StateOf(p).BirthZone, view.OwnZone);
            Assert.Equal(match.Flags.LockedCells[view.OwnZone!.Value], view.OwnCell);
        }

        Assert.All(match.LockedFlagCounts.Values, c => Assert.Equal(1, c));
        Assert.Contains("@", match.Events.Single(e => e.Kind == FlowEventKind.FlagsLocked).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void 插旗阶段座位视图公开整张基图()
    {
        // match-setup「大图自由插旗」/ information-visibility「开局信息公开范围」（段 B 追加裁决 29）：大图没有座位占位，插旗阶段各座位看到整张基图的地形、没有占位；
        // 他人的旗格不在其中。小图插旗阶段未选者仍一格不见（「开局信息公开范围」守）。
        MatchFlow match = CreateLarge();
        match.Flags.PlantAt(P0, FlagRules.LegalFlagCells(Large)[0]);
        foreach (PlayerId p in All)
        {
            SeatPublicView view = match.PublishFor(p);
            Assert.Equal(Large.Width * Large.Height, view.Cells.Length);
            Assert.Empty(view.Placeholders);
            Assert.Equal(Large.AllCoords().Order().Select(c => c.ToNotation()), view.Cells.Select(c => c.Coord));
        }

        Assert.Empty(Create().PublishFor(P1).Cells);

        // 锁定后：占位 = n 块 13×13 出生棋盘、全部展开，可见格仍是整张图。
        match.LockFlags();
        SeatPublicView locked = match.PublishFor(P1);
        Assert.Equal(All.Length, locked.Placeholders.Length);
        Assert.All(locked.Placeholders, p => Assert.Equal((FlagRules.BirthPlateSide, FlagRules.BirthPlateSide), (p.Width, p.Height)));
        Assert.Equal(Large.Width * Large.Height, locked.Cells.Length);
    }
}
