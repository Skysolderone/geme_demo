using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.Battlefield;

/// <summary>规格：openspec/changes/board-link-mode/specs/battlefield —— Requirement: 战场的定义与规则作用域</summary>
/// <remarks>
/// 变异验证（段 A 实跑，只跑本类）：
/// M-A1 Battlefields.Compute 的 Id 取 zonesOf[number].Max() → 「隔绝棋盘各成战场」不红（单棋盘分量最小 = 最大）、「链接合并战场」红（合并后 Id 应为较小者）；
/// M-A2 Battlefields.Compute 改走 Adjacency.LibertyNeighbors → 「场景格不可改造」红（立栅后战场数变多）、四邻接Tests 的 ⑥ 反面断言红；
/// M-A3 TerrainEditRules.Reject 删去障碍格端点判定 → 「场景格不可改造」红、改造合法性Tests.拒绝理由与合法目标集合一致 红。
/// </remarks>
public class 战场的定义与规则作用域Tests
{
    [Fact]
    public void 隔绝棋盘各成战场()
    {
        // Scenario：加载 link: 地图，开局 5 块出生棋盘尚未链接 → 恰好 5 个战场，每个战场的格集合 = 一块出生棋盘的格集合，标识 = 该棋盘编号。
        foreach (MapData map in new[] { LinkMapGenerator.Generate(1), LinkMapGenerator.Generate(7), LinkMapFixtures.Initial() })
        {
            Battlefields fields = Battlefields.Of(map);
            Assert.Equal(5, fields.Count);
            for (int i = 0; i < 5; i++)
            {
                Siege.Core.Board.Battlefield field = fields.All[i];
                Assert.Equal(i, field.Id);
                Assert.True(field.HasBirthBoard);
                Assert.Equal(map.Boards[i].Cells().Where(map.IsPlayable).Order(), field.Cells);   // link-map-terrain：棋盘内的岩石 / 未架桥深水不属于战场
                Assert.Equal([i], field.BirthZones);
                Assert.Equal([i], field.Boards);
                Assert.Same(field, fields.ById(i));
                Assert.All(field.Cells, c => Assert.Same(field, fields.At(c)));
            }

            // 场景格不属于任何战场；越界也不属于。
            Assert.All(map.AllCoords().Where(c => !map.IsPlayable(c)), c => Assert.Null(fields.At(c)));
            Assert.Null(fields.At(new Coord(map.Width, 0)));
            Assert.Null(fields.At(new Coord(0, map.Height)));
            Assert.Null(fields.ById(5));

            // GameBoard 暴露同一份导出。
            GameBoard board = GameBoard.Load(map);
            Assert.Same(fields, board.Battlefields);
        }
    }

    [Fact]
    public void 战场导出按地图实例记忆化()
    {
        // D1：战场随地图版本号记忆化——同一实例只算一次；换一份实例（with 出来的也算）从头导出，不会把旧结果带过去。
        MapData map = LinkMapFixtures.Initial();
        Assert.Same(Battlefields.Of(map), Battlefields.Of(map));

        MapData copy = map with { };
        Assert.NotSame(Battlefields.Of(map), Battlefields.Of(copy));
        Assert.Equal(Battlefields.Of(map).Count, Battlefields.Of(copy).Count);

        // 改造换图：GameBoard.ApplyTerrainEdits 换上新 MapData，战场导出跟着换实例（立栅不改变战场数）。
        GameBoard board = GameBoard.Load(map);
        Battlefields before = board.Battlefields;
        board.ApplyTerrainEdits([TerrainEdit.Fence(new Coord(3, 3), new Coord(4, 3))]);
        Assert.NotSame(before, board.Battlefields);
        Assert.Equal(5, board.Battlefields.Count);
    }

    [Fact]
    public void 规则不跨战场()
    {
        // Scenario：玩家 A 与 B 分别在两个未链接的战场上各有一条棋串 → 气、覆盖与领地互不影响，任一方落子不改变另一方的任何结算结果。
        GameBoard board = GameBoard.Load(LinkMapFixtures.Initial());
        var a = new PlayerId(0);
        var b = new PlayerId(1);
        Coord a1 = new(4, 4);          // 0 号棋盘中心
        Coord b1 = new(12, 4);         // 1 号棋盘中心
        board.Place(a1, a, PieceType.Basic);
        board.Place(b1, b, PieceType.Basic);
        Group groupA = board.GroupAt(a1)!;
        ImmutableArray<Coord> libertiesBefore = board.LibertiesOf(groupA);
        ImmutableArray<Coord> coverageBefore = board.CoverageTargets(a1);

        // B 在自己的战场里把整块棋盘的边缘填满，A 的气与覆盖目标逐项不变；两条棋串没有任何共同的气边邻格。
        foreach (Coord c in LinkMapFixtures.Births[1].Cells().Where(c => c != b1 && (c.X == 10 || c.X == 14 || c.Y == 2 || c.Y == 6)))
        {
            board.Place(c, b, PieceType.Basic);
        }

        Assert.Equal(libertiesBefore, board.LibertiesOf(board.GroupAt(a1)!));
        Assert.Equal(coverageBefore, board.CoverageTargets(a1));
        Assert.Empty(board.Battlefields.ById(0)!.Cells.Intersect(board.Battlefields.ById(1)!.Cells));
        Assert.All(board.Battlefields.ById(0)!.Cells, c => Assert.All(board.LibertyNeighbors(c), n => Assert.Equal(0, board.Battlefields.At(n)!.Id)));
        Assert.All(board.Battlefields.ById(0)!.Cells, c => Assert.All(board.CoverageTargets(c), n => Assert.Equal(0, board.Battlefields.At(n)!.Id)));
    }

    [Fact]
    public void 场景格不可改造()
    {
        // Scenario：匠人尝试在两个战场之间的场景格上搭桥、立栅或烧林 → 系统拒绝该改造，战场数量不变。
        // 实现口径（见 TerrainEditRules 类注释）：搭桥 / 烧林在场景格（障碍）上被拒绝；立栅不按规格档分流，
        // 落在场景格边上的栅栏是合法但无效果的动作（切不到任何气边、不改变任何格的可落子性），因此战场数量与格集合不变。
        MapData map = LinkMapFixtures.Initial();
        var corner = new Coord(7, 4);          // 0 号棋盘东缘的格（link-map-terrain：夹具改为 6×6，x 2–7）
        var scenery = new Coord(8, 4);         // 紧邻的场景格（障碍）
        var scenery2 = new Coord(9, 4);

        Assert.Contains("深水", TerrainEditRules.Reject(map, corner, TerrainEdit.Bridge(scenery)), StringComparison.Ordinal);
        Assert.Contains("林地", TerrainEditRules.Reject(map, corner, TerrainEdit.Burn(scenery)), StringComparison.Ordinal);
        // 工坊范围内的格目标只落在棋盘内（夹具中间带的林地 G4 / G7 是合法的烧林目标），场景格上没有搭桥 / 烧林目标。
        Assert.DoesNotContain(TerrainEditRules.LegalTargets(map, corner, workshop: true), e => e.Kind != TerrainEditKind.Fence && !map.Boards.Any(b => b.Contains(e.Cell)));

        // 场景格上的栅栏：合法但无效果——应用后场景格仍不可落子、气边集合逐格不变、战场不变。
        GameBoard fenced = GameBoard.Load(map);
        Dictionary<Coord, ImmutableArray<Coord>> libertiesBefore = map.AllCoords().Where(map.IsPlayable).ToDictionary(c => c, fenced.LibertyNeighbors);
        foreach (TerrainEdit edit in new[] { TerrainEdit.Fence(scenery, scenery2), TerrainEdit.Fence(corner, scenery) })
        {
            Assert.Null(TerrainEditRules.Reject(map, corner, edit));
            fenced.ApplyTerrainEdits([edit]);
        }

        Assert.False(fenced.Map.IsPlayable(scenery));
        Assert.All(libertiesBefore, kv => Assert.Equal(kv.Value, fenced.LibertyNeighbors(kv.Key)));
        Assert.Equal(5, fenced.Battlefields.Count);

        // 把 0 号棋盘每个格上匠人的全部合法改造都做一遍，战场数量仍是 5、每个战场的格集合不变。
        GameBoard board = GameBoard.Load(map);
        ImmutableArray<Siege.Core.Board.Battlefield> before = board.Battlefields.All;
        var applied = new HashSet<TerrainEdit>();
        foreach (Coord cell in LinkMapFixtures.Births[0].Cells())
        {
            foreach (TerrainEdit edit in TerrainEditRules.LegalTargets(board.Map, cell, workshop: true))
            {
                if (TerrainEditRules.IsLegal(board.Map, cell, edit, workshop: true) && applied.Add(edit))
                {
                    board.ApplyTerrainEdits([edit]);
                }
            }
        }

        Assert.NotEmpty(applied);   // 样本口径：确实做了改造（棋盘内的栅栏）
        Assert.Equal(5, board.Battlefields.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(before[i].Cells, board.Battlefields.All[i].Cells);
        }

        // 场景格仍全部不可落子。
        Assert.All(map.AllCoords().Where(c => !map.IsPlayable(c)), c => Assert.False(board.Map.IsPlayable(c)));
    }

    [Fact]
    public void 旧地图的孤立可落子区不属于任何战场编号()
    {
        // 非链接档：全部可落子格连成一片 → 恰 1 个战场、标识 0、含全部出生区；手工挖出的孤立格成为"无出生棋盘"的分量，排在最后。
        // retire-legacy-maps 1.8：原取标准图 v5；旧图删除后改用 9×9 手写夹具（MatchFixtures.Map：单连通、四角 4 个出生区、无棋盘清单），断言不变。
        MapData v5 = MatchFixtures.Map();
        Battlefields whole = Battlefields.Of(v5);
        Assert.Single(whole.All);
        Assert.Equal(0, whole.All[0].Id);
        Assert.Equal(Enumerable.Range(0, v5.BirthZones.Length), whole.All[0].BirthZones);
        Assert.Empty(whole.All[0].Boards);

        // 挑第一个"不在出生区、四邻也不在出生区且不是信物格"的可落子格，把它的四邻全改成障碍：它成为无出生棋盘的分量，Id 为 −1、排在最后。
        // （check 阶段改写：原先按 A1 的几何写成 if 分支，变异 HasBirthBoard 恒真时不红——分支从未执行；改写后同一变异 → 本测试红 1。）
        Coord pick = v5.AllCoords().First(c =>
            v5.IsPlayable(c) && v5.BirthZoneOf(c) is null
            && Adjacency.Neighbors(v5.Width, v5.Height, c).All(n => v5.BirthZoneOf(n) is null && !v5.RelicCells.ContainsKey(n)));
        MapData isolated = v5 with { Obstacles = v5.Obstacles.Union(Adjacency.Neighbors(v5.Width, v5.Height, pick)) };
        Battlefields split = Battlefields.Of(isolated);
        Assert.True(split.Count >= 2);
        Assert.Equal(0, split.All[0].Id);
        Assert.False(split.All[^1].HasBirthBoard);
        Assert.Equal(-1, split.All[^1].Id);
        Assert.Contains(split.All, f => f.Id == -1 && f.Cells.SequenceEqual([pick]));
        Assert.Null(split.At(Adjacency.Neighbors(v5.Width, v5.Height, pick)[0]));
    }

    [Fact]
    public void 棋盘左下角是岩石时按棋盘内可落子格归属()
    {
        // link-map-terrain design D7（battlefield 接口）/ tasks 1.3：战场到棋盘的归属取"棋盘内任一可落子格所在的分量"，不再假设左下角格可落子。
        // 手写链接档图：第 3 块出生棋盘（下标 2）的左下角改成岩石，其余格仍连通。旧实现按左下角格取分量 → 该棋盘不属于任何战场。
        // 变异验证（link-map-terrain 1.3 实跑，只跑本类）：M-T1 Battlefields.Compute 的棋盘归属改回取 plate.Origin 的分量 → 本测试红 1，本类其余不红。
        MapData initial = LinkMapFixtures.Initial();
        BoardPlate plate = initial.Boards[2];
        MapData map = initial with { Obstacles = initial.Obstacles.Add(plate.Origin) };
        Assert.False(map.IsPlayable(plate.Origin));

        Battlefields fields = Battlefields.Of(map);
        Assert.Equal(5, fields.Count);
        Siege.Core.Board.Battlefield field = fields.ById(2)!;
        Assert.Equal([2], field.Boards);
        Assert.Equal(plate.Cells().Where(map.IsPlayable).Order(), field.Cells);

        // 每块棋盘恰属于一个战场。
        Assert.Equal(Enumerable.Range(0, 5), fields.All.SelectMany(f => f.Boards).Order());
    }
}
