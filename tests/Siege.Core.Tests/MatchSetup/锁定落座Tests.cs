using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/specs/match-setup —— Requirement: 锁定时落座（big-map 裁决 4 / 5，D4）：锁定那一刻基图 + 旗格 → 落座图（9×9 出生圈、13×13 出生棋盘、版本 1）原地换进盘面；
/// 存档 / 恢复按旗格重做落座；小图的存档、地图导出与公开快照逐字节 / 逐字段不变。
/// </summary>
/// <remarks>
/// 变异验证（段 A 实跑，mutate.py，备份 → 改 → 跑 → 逐字节还原 → 刷新时间戳）：
/// M-S1 <c>FlagRules.BirthPlateOf</c> 的出生棋盘原点写成旗 − (4, 4)（<c>PlateReach</c> → <c>RingReach</c>）→ 跑本类 + <c>大图构筑范围Tests</c>：红 6、绿 1（平台偏移后 13×13 不再以旗为心、合规旗格与落座图校验连带变化）；
/// M-S2 <c>Serialize</c> 不写 <c>FlagCell</c>（恒 null）→ 跑本类：红 1、绿 3（「大图存档往返重做落座」恢复时报缺旗格）。
/// 未做（只列出、未实跑）：<c>MatchSaveData.MapTier</c> 去掉 WhenWritingNull（小图存档多出字段）。
/// </remarks>
public class 锁定落座Tests
{
    private static readonly Lazy<MapData> LazyLarge = new(() => MapCatalog.Resolve("link:1:4:l"));

    private static MapData Large => LazyLarge.Value;

    private static MatchFlow StartedLarge(GameSeed? seed = null)
    {
        MatchFlow match = MatchFlow.Create(Large, seed ?? TestMaps.Seed, All, Options);
        match.PlantPrototype();
        return match;
    }

    [Fact]
    public void 锁定后基图换成落座图()
    {
        MatchFlow match = MatchFlow.Create(Large, TestMaps.Seed, All, Options);
        Assert.Same(Large, match.Board.BaseMap);
        Assert.Equal(0, match.Board.BaseMap.MapVersion);
        match.PlantPrototype();

        MapData seated = match.Board.BaseMap;
        Assert.NotSame(Large, seated);
        Assert.Same(seated, match.Map);   // 锁定时盘上没有棋子与改造：活地图就是落座图
        Assert.Equal(1, seated.MapVersion);
        Assert.Equal(Large.Id, seated.Id);
        Assert.Equal(MapTier.Large, seated.Tier);
        Assert.Equal(4, seated.BirthZones.Length);
        Assert.Equal(4, seated.BirthBoards().Count());
        Assert.Equal(Large.Boards.Length + 4, seated.Boards.Length);
        Assert.Equal(Large.Boards, seated.Boards.Skip(4));   // 中立平台原样在后
        Assert.True(MapValidator.Validate(seated).IsValid, MapValidator.Validate(seated).ToString());
        Assert.Equal(MapFile.Digest(seated), MapFile.Digest(FlagRules.Seat(Large, match.Flags.LockedCells)));   // 由 (基图, 旗格) 完全决定

        // 地形、障碍不动：逐格高度 / 地表 / 可落子相同，桥与栅栏相同。
        foreach (Coord c in Large.AllCoords())
        {
            Assert.Equal((Large.IsPlayable(c), Large.HeightAt(c), Large.SurfaceAt(c), Large.HasBridge(c)), (seated.IsPlayable(c), seated.HeightAt(c), seated.SurfaceAt(c), seated.HasBridge(c)));
        }

        Assert.Equal(Large.TerrainData.Fences, seated.TerrainData.Fences);

        foreach (PlayerId p in All)
        {
            int zone = match.StateOf(p).BirthZone!.Value;
            Coord flag = match.Flags.FlagCellOf(p)!.Value;
            Assert.Equal(flag, match.Flags.LockedCells[zone]);
            BoardPlate plate = seated.BirthBoard(zone);
            Assert.Equal((flag.X - 6, flag.Y - 6, 13, 13, BoardPlateKind.Birth, (BoardTheme?)null), (plate.Origin.X, plate.Origin.Y, plate.Width, plate.Height, plate.Kind, plate.Theme));
            // 出生区 = 旗心 9×9 ∩ 可落子（测试内独立算式）。
            var ring = new HashSet<Coord>();
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    var c = new Coord(flag.X + dx, flag.Y + dy);
                    if (Large.IsPlayable(c))
                    {
                        ring.Add(c);
                    }
                }
            }

            Assert.True(ring.SetEquals(seated.BirthZones[zone]));
            Assert.True(ring.Count >= FlagRules.MinRingPlayable);
            Assert.Equal(zone, seated.BirthZoneOf(flag));
            Assert.Equal(0, match.BattlefieldOf(p));   // 整张大陆是唯一战场，含全部出生区
        }

        Assert.Equal([0, 1, 2, 3], match.Board.Battlefields.All[0].BirthZones);
    }

    [Fact]
    public void 大图存档往返重做落座()
    {
        // 锁定后存档：记旗格与档位；按标识重建的是基图，恢复时先重做落座再比摘要（追加裁决 19）。活对象与恢复对象逐字段比 + 再存档逐字节相等。
        MatchFlow match = StartedLarge().PlayRounds(1, P0, P2);
        string json = match.Serialize();
        JsonNode root = JsonNode.Parse(json)!;
        Assert.Equal("Large", root["MapTier"]!.GetValue<string>());
        Assert.Equal(MapFile.Digest(match.Board.BaseMap), root["MapDigest"]!.GetValue<string>());
        Assert.Equal(match.Flags.LockedCells.Select(c => c.ToNotation()), root["Players"]!.AsArray().OrderBy(p => p!["BirthZone"]!.GetValue<int>()).Select(p => p!["FlagCell"]!.GetValue<string>()));
        Assert.Equal(100, root["RoundCap"]!.GetValue<int>());

        MatchFlow restored = MatchFlow.Restore(MapCatalog.Resolve(MatchFlow.SavedMapId(json)), json);
        Assert.Equal(MapFile.Digest(match.Board.BaseMap), MapFile.Digest(restored.Board.BaseMap));
        Assert.Equal(1, restored.Board.BaseMap.MapVersion);
        Assert.True(restored.Flags.IsLocked);
        Assert.Equal(match.Flags.LockedCells, restored.Flags.LockedCells);
        foreach (PlayerId p in All)
        {
            Assert.Equal(match.StateOf(p).BirthZone, restored.StateOf(p).BirthZone);
            Assert.Equal(match.Flags.FlagCellOf(p), restored.Flags.FlagCellOf(p));
            Assert.Equal(match.LegalRangeFor(p).Count, restored.LegalRangeFor(p).Count);
        }

        Assert.Equal(match.RoundCap, restored.RoundCap);
        Assert.False(restored.RoundCapBackfilled);
        Assert.Equal(match.Board.Serialize(), restored.Board.Serialize());
        Assert.Equal(json, restored.Serialize());

        // 锁定前存档：还没有落座，摘要是基图的；恢复后旗格原样、未锁定。
        MatchFlow planting = MatchFlow.Create(Large, TestMaps.Seed, All, Options);
        Coord flag = FlagRules.LegalFlagCells(Large)[3];
        planting.Flags.PlantAt(P1, flag);
        string early = planting.Serialize();
        Assert.Equal(MapFile.Digest(Large), JsonNode.Parse(early)!["MapDigest"]!.GetValue<string>());
        MatchFlow earlyRestored = MatchFlow.Restore(Large, early);
        Assert.False(earlyRestored.Flags.IsLocked);
        Assert.Equal(flag, earlyRestored.Flags.FlagCellOf(P1));
        Assert.Null(earlyRestored.Flags.FlagCellOf(P0));
        Assert.Equal(early, earlyRestored.Serialize());

        // 旗格被改成不合规格的存档：响亮失败，不静默落座到别处。
        string forged = json.Replace(match.Flags.LockedCells[0].ToNotation(), "A1", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => MatchFlow.Restore(Large, forged));
    }

    [Fact]
    public void 小图存档与快照零改动()
    {
        // 小图零字节原则（D1）：新字段在小图上一律不写出——存档没有 MapTier / FlagCell，地图导出没有 Tier；公开快照档位为小图、RoundCap 100 且是缺省。
        MatchFlow small = Started([0, 1, 2, 3]).PlayRounds(1, P0);
        string json = small.Serialize();
        Assert.DoesNotContain("MapTier", json, StringComparison.Ordinal);
        Assert.DoesNotContain("FlagCell", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Tier\"", json, StringComparison.Ordinal);
        Assert.Equal(100, JsonNode.Parse(json)!["RoundCap"]!.GetValue<int>());
        Assert.Same(Map, small.Board.BaseMap);   // 小图锁定时不换地图
        Assert.Equal(0, small.Board.BaseMap.MapVersion);
        Assert.Empty(small.Flags.LockedCells);
        Assert.Null(small.Flags.FlagCellOf(P0));

        MatchPublicView view = small.Publish();
        Assert.Equal(MapTier.Small, view.Tier);
        Assert.Equal(100, view.RoundCap);
        Assert.True(view.RoundCapIsDefault);

        MatchFlow restored = MatchFlow.RestoreUnvalidated(small.Board.BaseMap, json);
        Assert.Equal(json, restored.Serialize());
        Assert.DoesNotContain("\"Tier\"", MapFile.ToJson(Map), StringComparison.Ordinal);
    }

    [Fact]
    public void 大图公开快照带档位与时长()
    {
        MatchFlow match = StartedLarge();
        MatchPublicView view = match.Publish();
        Assert.Equal(MapTier.Large, view.Tier);
        Assert.Equal(100, view.RoundCap);
        Assert.True(view.RoundCapIsDefault);
        Assert.Equal("link:1:4:l", view.MapId);
        Assert.Equal(4, view.Board.BaseMap.BirthZones.Length);
        // 落座图进入按座位投影与座位视图：出生棋盘占位是 13×13、不带主题。
        ImmutableArray<BoardPlaceholder> placeholders = match.PublishFor(P0).Placeholders;
        Assert.Equal(4, placeholders.Length);
        Assert.All(placeholders, b => Assert.Equal((13, 13, (BoardTheme?)null), (b.Width, b.Height, b.Theme)));
    }
}
