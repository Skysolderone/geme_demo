using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Relics;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>规格：openspec/changes/board-link-mode/specs/link-map-generation —— Requirement: 链接时的棋盘拼接（段 F，裁决 A / J1 / J3 / J7）</summary>
/// <remarks>
/// 出生棋盘 / 战场编号用 0 起的内部索引（规格 Scenario 写的"战场 1 与 4"对应这里的 0 与 3）。
/// 变异验证（段 F 实跑，只跑本类）：
/// M-F4 <c>LinkMapGenerator.Link</c> 并列取偏北 / 偏东（候选循环改为降序）→ 「共享边最长」红 1（被贴方落到 y = 8 而非 7）；
/// M-F5 <c>Link</c> 其余战场的重排起点改为 <c>mergedMaxX + 1 + Gap − 1</c> → 「全部链接序列都能拼接」校验红（未链接战场只隔 2 格仍合法 → 改为 Gap − 3 即相邻则 BATTLEFIELD_COUNT_MISMATCH）；
/// M-F6 <c>Link</c> 不平移信物格（<c>relics</c> 改为 <c>current.RelicCells</c>）→ 校验 BIRTH_BOARD_RELIC_COUNT 红、「平移保内容」红。
/// 段 F.2b（裁决 J8）：M-F18 以西的未参与战场也收拢 → 红 25（含「合并战场以西的战场不动」）；M-F19 并列不排除越界候选 → 「越界候选被排除」红 1；
/// M-F20 全越界时不整体平移 → 红 4（含「越界候选被排除」「锚点非零向量的拼接可存档恢复」）。
/// </remarks>
public class 链接时的棋盘拼接Tests(ITestOutputHelper output)
{
    [Fact]
    public void 首轮拼接()
    {
        // Scenario：在 link:1 初始地图上链接战场 0 与 3（规格记作 1 与 4），锚点为 0 → 3 平移到 0 东侧紧贴，共享边 = 两块棋盘较矮者的高度；
        // 合并后可落子格 = 两者之和、全图没有新增格；战场 0 的格与棋子逐格不变，战场 3 的格与棋子按同一向量整体平移。
        MapData initial = LinkMapGenerator.Generate(1);
        Battlefields before = Battlefields.Of(initial);
        BoardPlate a = initial.Boards[0];
        BoardPlate b = initial.Boards[3];
        output.WriteLine($"link:1 战场 0 {a.Width}×{a.Height}，战场 3 {b.Width}×{b.Height}（规格算例写 7×7 / 5×5 / 74 格，按结构事实断言）");

        GameBoard board = GameBoard.Load(initial);
        var p0 = new PlayerId(0);
        var p3 = new PlayerId(3);
        Coord stoneA = a.Origin;
        Coord stoneB = b.CoreOrigin;   // link-map-terrain：棋盘内可能有岩石 / 深水，取核心区的格（恒可落子）
        board.Place(stoneA, p0, PieceType.Basic);
        board.Place(stoneB, p3, PieceType.Fortress);

        LinkResult result = LinkMapGenerator.Link(initial, 0, 3, LinkSide.East);
        board.ApplySplice(result.Map, result.Translate);
        MapData grown = result.Map;

        Assert.Equal((0, 3, LinkSide.East), (result.Anchor, result.Mover, result.Side));
        BattlefieldShift shift = result.Shifts.Single(s => s.Battlefield == 3);
        Assert.Equal(a.Origin.X + a.Width, b.Origin.X + shift.Dx);            // 被贴方最西列 = 锚点最东列 + 1
        Assert.Equal(Math.Min(a.Height, b.Height), result.SharedEdge);          // 两块矩形的共享边 = 较矮者的高
        Assert.Equal(0, result.Shifts.Single(s => s.Battlefield == 0).Dx);
        Assert.Equal(0, result.Shifts.Single(s => s.Battlefield == 0).Dy);
        Assert.Equal(5, result.Shifts.Length);

        Battlefields after = Battlefields.Of(grown);
        Assert.Equal(4, after.Count);
        Siege.Core.Board.Battlefield merged = after.ById(0)!;
        Assert.Null(after.ById(3));
        Assert.Equal([0, 3], merged.BirthZones);
        Assert.Equal(before.ById(0)!.Cells.Length + before.ById(3)!.Cells.Length, merged.Cells.Length);
        HashSet<Coord> expected = [.. before.ById(0)!.Cells, .. before.ById(3)!.Cells.Select(c => new Coord(c.X + shift.Dx, c.Y + shift.Dy))];
        Assert.Equal(expected.Order(), merged.Cells);
        Assert.Equal(initial.PlayableCount, grown.PlayableCount);               // 没有新增格
        Assert.Equal(1, grown.MapVersion);
        Assert.True(MapValidator.Validate(grown).IsValid, MapValidator.Validate(grown).ToString());

        // 棋子：锚点的不动，被贴方的随向量走；接缝两侧互为几何邻居。
        Assert.Equal(new Occupant(p0, PieceType.Basic), board[stoneA].Occupant);
        Assert.Equal(new Occupant(p3, PieceType.Fortress), board[new Coord(stoneB.X + shift.Dx, stoneB.Y + shift.Dy)].Occupant);
        Assert.Equal(2, board.AllGroups().Length);
        Coord seamWest = new(a.Origin.X + a.Width - 1, Enumerable.Range(a.Origin.Y, a.Height).First(y => grown.IsPlayable(new Coord(a.Origin.X + a.Width, y))));
        Assert.Contains(new Coord(seamWest.X + 1, seamWest.Y), board.Neighbors(seamWest));
        Assert.Contains(new Coord(seamWest.X + 1, seamWest.Y), board.LibertyNeighbors(seamWest));
        Assert.Contains("拼接", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void 共享边最长()
    {
        // Scenario：锚点东侧边界由高低不齐的两块棋盘组成（0 号 6×6 在下、1 号 8×7 北贴在上且更宽，东沿只有 1 号的 7 行），被贴方为 6×6 →
        // 纵向位置取共享边最长者（6）：候选底行 y = 8 与 9 并列，取偏南的 8。（link-map-terrain：边长 6–8，原算例为 5×5 / 7×6 / 5×5。）
        ImmutableArray<BoardPlate> b = LinkMapFixtures.Births;
        ImmutableArray<BoardPlate> boards =
        [
            b[0] with { Origin = new Coord(2, 2) },
            b[1] with { Origin = new Coord(2, 8), Width = 8, Height = 7 },
            b[2] with { Origin = new Coord(13, 2) },
            b[3] with { Origin = new Coord(22, 2) },
            b[4] with { Origin = new Coord(31, 2) },
        ];
        MapData map = LinkMapFixtures.Build("test-link-uneven", boards, mapVersion: 1);
        Assert.True(MapValidator.Validate(map).IsValid, MapValidator.Validate(map).ToString());
        Assert.Equal(4, Battlefields.Of(map).Count);

        LinkResult result = LinkMapGenerator.Link(map, 0, 2, LinkSide.East);
        Assert.Equal(6, result.SharedEdge);
        Assert.Equal(new BattlefieldShift(2, -3, 6), result.Shifts.Single(s => s.Battlefield == 2));   // 最西列 13 → 10，底行 2 → 8（偏南）
        Assert.Equal(new Coord(10, 8), result.Map.Boards[2].Origin);
        // 其余两块按原次序排到合并战场东侧，间距 3：合并外接矩形最东列 15 → 19 与 28；画布东 / 北边界外留 2 格。
        Assert.Equal(new BattlefieldShift(3, -3, 0), result.Shifts.Single(s => s.Battlefield == 3));
        Assert.Equal(new BattlefieldShift(4, -3, 0), result.Shifts.Single(s => s.Battlefield == 4));
        Assert.Equal(new Coord(19, 2), result.Map.Boards[3].Origin);
        Assert.Equal(new Coord(28, 2), result.Map.Boards[4].Origin);
        Assert.Equal((36, 17), (result.Map.Width, result.Map.Height));
        Assert.Equal(3, Battlefields.Of(result.Map).Count);
        Assert.Equal([0, 1, 2], Battlefields.Of(result.Map).ById(0)!.BirthZones);
    }

    [Fact]
    public void 决赛拼接()
    {
        // Scenario：两个已合并的战场执行决赛链接 → 含最小出生棋盘编号的战场为锚点，另一战场贴在其北侧，共享边 ≥ 1，合并为一个战场。
        MapData map = LinkMapGenerator.Generate(1);
        map = LinkMapGenerator.Link(map, 0, 3, LinkSide.East).Map;
        map = LinkMapGenerator.Link(map, 1, 4, LinkSide.East).Map;
        Battlefields before = Battlefields.Of(map);
        Assert.Equal(3, before.Count);

        LinkResult final = LinkMapGenerator.Link(map, 1, 0, LinkSide.North);   // 顺序无关：锚点恒为小者
        Assert.Equal((0, 1, LinkSide.North), (final.Anchor, final.Mover, final.Side));
        Assert.True(final.SharedEdge >= 1);
        BattlefieldShift shift = final.Shifts.Single(s => s.Battlefield == 1);
        Assert.Equal(before.ById(0)!.Cells.Max(c => c.Y) + 1, before.ById(1)!.Cells.Min(c => c.Y) + shift.Dy);   // 被贴方最南行 = 锚点最北行 + 1

        Battlefields after = Battlefields.Of(final.Map);
        Assert.Equal(2, after.Count);
        Siege.Core.Board.Battlefield merged = after.ById(0)!;
        Assert.Equal([0, 1, 3, 4], merged.BirthZones);
        Assert.Null(after.ById(1));
        Assert.Equal(before.ById(0)!.Cells.Length + before.ById(1)!.Cells.Length, merged.Cells.Length);
        Assert.True(before.ById(0)!.Cells.All(merged.Cells.Contains));
        Assert.Equal(before.ById(1)!.Cells.Select(c => new Coord(c.X + shift.Dx, c.Y + shift.Dy)).Order(), merged.Cells.Except(before.ById(0)!.Cells).Order());
        Assert.Equal(3, final.Map.MapVersion);
        Assert.True(MapValidator.Validate(final.Map).IsValid, MapValidator.Validate(final.Map).ToString());
    }

    [Fact]
    public void 合并战场以西的战场不动()
    {
        // Scenario（裁决 J8-1）：一次链接后，未参与战场整体位于合并战场以西 → 坐标不变；只有以东（或与之交叠 / 紧贴）的被收拢东移。
        // 夹具五块 6×6 在 x = 2 / 11 / 20 / 29 / 38：链接 2 与 3（东贴）→ 合并战场 x 20–31；0、1 号在其以西不动，4 号收拢到 35（间距 3）。
        MapData initial = LinkMapFixtures.Initial();
        LinkResult result = LinkMapGenerator.Link(initial, 2, 3, LinkSide.East);

        Assert.Equal(new BattlefieldShift(0, 0, 0), result.Shifts.Single(s => s.Battlefield == 0));
        Assert.Equal(new BattlefieldShift(1, 0, 0), result.Shifts.Single(s => s.Battlefield == 1));
        Assert.Equal(new BattlefieldShift(2, 0, 0), result.Shifts.Single(s => s.Battlefield == 2));
        Assert.Equal(new BattlefieldShift(3, -3, 0), result.Shifts.Single(s => s.Battlefield == 3));
        Assert.Equal(new BattlefieldShift(4, -3, 0), result.Shifts.Single(s => s.Battlefield == 4));
        Assert.Equal(initial.Boards[0], result.Map.Boards[0]);
        Assert.Equal(initial.Boards[1], result.Map.Boards[1]);
        Assert.Equal(new Coord(35, 2), result.Map.Boards[4].Origin);
        Assert.Equal(43, result.Map.Width);
        Assert.True(MapValidator.Validate(result.Map).IsValid, MapValidator.Validate(result.Map).ToString());
    }

    [Fact]
    public void 越界候选被排除()
    {
        // Scenario（裁决 J8-3）：决赛北贴、锚点在最西、锚点顶行只剩西块、被贴方底行只剩东块。
        // 情形一：锚点顶行宽（0 号 6×8 + 1 号 8×8，顶行 x 2–15），被贴方东块 6 宽可落在 9 个位置共享边都是 6；偏西的 4 个会让被贴方西块越出原点，
        // 排除后取最西的不越界者（被贴方最西列恰为 0），锚点不动。（link-map-terrain：边长 6–8，原算例为 5×7 / 7×7 / 5×5 / 5×7 / 5×5。）
        ImmutableArray<BoardPlate> b = LinkMapFixtures.Births;
        ImmutableArray<BoardPlate> wide =
        [
            b[0] with { Origin = new Coord(2, 2), Height = 8 },
            b[1] with { Origin = new Coord(8, 2), Width = 8, Height = 8 },
            b[2] with { Origin = new Coord(19, 4) },
            b[3] with { Origin = new Coord(25, 2), Height = 8 },
            b[4] with { Origin = new Coord(34, 2) },
        ];
        MapData map = LinkMapFixtures.Build("test-link-west-wide", wide, mapVersion: 2);
        Assert.True(MapValidator.Validate(map).IsValid, MapValidator.Validate(map).ToString());
        LinkResult kept = LinkMapGenerator.Link(map, 0, 2, LinkSide.North);
        Assert.Equal(6, kept.SharedEdge);
        Assert.Equal(new BattlefieldShift(0, 0, 0), kept.Shifts.Single(s => s.Battlefield == 0));
        Assert.Equal(new BattlefieldShift(2, -19, 8), kept.Shifts.Single(s => s.Battlefield == 2));
        Assert.Equal(new BattlefieldShift(4, -15, 0), kept.Shifts.Single(s => s.Battlefield == 4));
        Assert.Equal(0, kept.Map.Boards[2].Origin.X);
        Assert.True(MapValidator.Validate(kept.Map).IsValid, MapValidator.Validate(kept.Map).ToString());

        // 情形二：锚点顶行只剩 0 号 6 宽（1 号 6×6 更矮），被贴方东块只有一个共享边 6 的位置、且会让西块越出原点 4 列 →
        // 整个布局（含锚点）整体东移 4，锚点向量 (4, 0) 记入链接记录；不抛异常。
        ImmutableArray<BoardPlate> narrow =
        [
            b[0] with { Origin = new Coord(2, 2), Height = 8 },
            b[1] with { Origin = new Coord(8, 2) },
            b[2] with { Origin = new Coord(17, 4) },
            b[3] with { Origin = new Coord(23, 2), Height = 8 },
            b[4] with { Origin = new Coord(32, 2) },
        ];
        MapData tight = LinkMapFixtures.Build("test-link-west-narrow", narrow, mapVersion: 2);
        Assert.True(MapValidator.Validate(tight).IsValid, MapValidator.Validate(tight).ToString());
        LinkResult shifted = LinkMapGenerator.Link(tight, 0, 2, LinkSide.North);
        Assert.Equal(6, shifted.SharedEdge);
        Assert.Equal(new BattlefieldShift(0, 4, 0), shifted.Shifts.Single(s => s.Battlefield == 0));
        Assert.Equal(new BattlefieldShift(2, -17, 8), shifted.Shifts.Single(s => s.Battlefield == 2));
        Assert.Equal(new BattlefieldShift(4, -11, 0), shifted.Shifts.Single(s => s.Battlefield == 4));
        Assert.Equal([new Coord(6, 2), new Coord(12, 2), new Coord(0, 12), new Coord(6, 10), new Coord(21, 2)], shifted.Map.Boards.Select(p => p.Origin));
        Assert.Equal((29, 20), (shifted.Map.Width, shifted.Map.Height));
        Assert.True(MapValidator.Validate(shifted.Map).IsValid, MapValidator.Validate(shifted.Map).ToString());

        // 锚点的格与棋子按锚点向量整体平移，盘面逐格搬运（GameBoard 层）。
        GameBoard board = GameBoard.Load(tight);
        board.Place(new Coord(2, 8), new PlayerId(0), PieceType.Basic);
        board.ApplySplice(shifted.Map, shifted.Translate);
        Assert.Equal(new Occupant(new PlayerId(0), PieceType.Basic), board[new Coord(6, 8)].Occupant);
    }

    [Fact]
    public void 全部链接序列都能拼接()
    {
        // Scenario：对 link:1–link:20 穷举三类链接序列（15 种四人四区配对按规范次序各接决赛、全部 2+1+1、全部 3+1），
        // 每一步共享边 ≥ 1、通过物化状态校验、合并后可落子格 = 参与战场格数之和；锚点（按其向量，通常为零）与被贴方各按单一向量整体平移；
        // 未参与者以西不动、其余只东移且底边不动（J8-1）。几何统计输出到测试日志，抄进任务报告。
        var links = new int[3];
        int distinct = 0;
        var shared = new SortedDictionary<int, int>();
        var widths = new List<int>();
        var heights = new List<int>();
        var playable = new List<int>();
        var totals = new List<int>();
        int movedOthers = 0;
        int stayedWest = 0;
        int touchingPacked = 0;
        int globalShifts = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            MapData initial = LinkMapGenerator.Generate(seed);
            totals.Add(initial.PlayableCount);
            widths.Add(initial.Width);
            heights.Add(initial.Height);
            var memo = new Dictionary<string, LinkResult>(StringComparer.Ordinal);
            foreach (LinkSequence sequence in LinkMapGenerator.Sequences)
            {
                MapData map = initial;
                string key = string.Empty;
                foreach (LinkStep step in sequence.Steps)
                {
                    key += step.ToString();
                    if (!memo.TryGetValue(key, out LinkResult? result))
                    {
                        Battlefields before = Battlefields.Of(map);
                        result = LinkMapGenerator.Link(map, step.X, step.Y, step.Side);
                        MapValidationResult validation = MapValidator.Validate(result.Map);
                        Assert.True(validation.IsValid, $"link:{seed} {sequence} 第 {step} 步：{validation}");
                        Assert.True(result.SharedEdge >= 1, $"link:{seed} {sequence} 第 {step} 步共享边 {result.SharedEdge}");
                        Assert.Equal(map.MapVersion + 1, result.Map.MapVersion);
                        Battlefields after = Battlefields.Of(result.Map);
                        Assert.Equal(before.Count - 1, after.Count);
                        Assert.Null(after.ById(result.Mover));
                        Siege.Core.Board.Battlefield merged = after.ById(result.Anchor)!;
                        Assert.Equal(before.ById(result.Anchor)!.Cells.Length + before.ById(result.Mover)!.Cells.Length, merged.Cells.Length);
                        Assert.Equal(map.PlayableCount, result.Map.PlayableCount);
                        BattlefieldShift anchor = result.Shifts.Single(s => s.Battlefield == result.Anchor);
                        BattlefieldShift mover = result.Shifts.Single(s => s.Battlefield == result.Mover);
                        Assert.True(before.ById(result.Anchor)!.Cells.All(c => merged.Cells.Contains(new Coord(c.X + anchor.Dx, c.Y + anchor.Dy))), "锚点按单一向量");
                        Assert.True(before.ById(result.Mover)!.Cells.All(c => merged.Cells.Contains(new Coord(c.X + mover.Dx, c.Y + mover.Dy))), "被贴方按单一向量平移");
                        globalShifts += anchor.IsZero ? 0 : 1;
                        int mergedMinX = Math.Min(before.ById(result.Anchor)!.Cells.Min(c => c.X), before.ById(result.Mover)!.Cells.Min(c => c.X) + mover.Dx - anchor.Dx);
                        foreach (BattlefieldShift other in result.Shifts.Where(s => s.Battlefield != result.Anchor && s.Battlefield != result.Mover))
                        {
                            ImmutableArray<Coord> cells = before.ById(other.Battlefield)!.Cells;
                            Assert.Equal(anchor.Dy, other.Dy);
                            Assert.Equal(cells.Select(c => new Coord(c.X + other.Dx, c.Y + other.Dy)).Order(), after.ById(other.Battlefield)!.Cells);
                            int maxX = cells.Max(c => c.X);
                            bool west = maxX + 1 < mergedMinX;
                            if (west)
                            {
                                Assert.Equal(anchor.Dx, other.Dx);   // 以西者只随整体平移
                            }
                            else
                            {
                                Assert.True(cells.Min(c => c.X) + other.Dx > merged.Cells.Max(c => c.X) + 1, "其余收拢到合并战场东侧且不相邻");
                            }

                            stayedWest += west ? 1 : 0;
                            movedOthers += !west && other.Dx != anchor.Dx ? 1 : 0;
                            touchingPacked += !west && maxX < mergedMinX ? 1 : 0;
                        }

                        shared[result.SharedEdge] = shared.GetValueOrDefault(result.SharedEdge) + 1;
                        widths.Add(result.Map.Width);
                        heights.Add(result.Map.Height);
                        playable.Add(merged.Cells.Length);
                        memo[key] = result;
                        distinct++;
                    }

                    links[(int)sequence.Kind]++;
                    map = result.Map;
                }
            }
        }

        string[] names = ["四人四区", "2+1+1", "3+1"];
        for (int k = 0; k < 3; k++)
        {
            output.WriteLine($"{names[k]}：链接 {links[k]} 次");
        }

        output.WriteLine($"去重后 {distinct} 次拼接；共享边分布 {string.Join(" ", shared.Select(kv => $"{kv.Key}:{kv.Value}"))}");
        output.WriteLine($"画布宽 {widths.Min()}–{widths.Max()}，高 {heights.Min()}–{heights.Max()}（含初始图）；合并战场可落子格 {playable.Min()}–{playable.Max()}；全图可落子格 {totals.Min()}–{totals.Max()}");
        output.WriteLine($"未参与战场：以西不动 {stayedWest} 次，收拢东移 {movedOthers} 次（其中以西但紧贴而被收拢 {touchingPacked} 次）；整体越界平移 {globalShifts} 次");

        Assert.Equal(20 * 45, links[0]);
        Assert.Equal(20 * 60, links[1]);
        Assert.Equal(20 * 10, links[2]);
        Assert.Equal(20 * 80, distinct);   // 首轮 10 对东贴 + 四区第二步 15 + 决赛 15 + 2+1+1 第二步 30 + 3+1 的 10 对北贴
        Assert.True(stayedWest > 0, "样本口径：确有以西不动的未参与战场");
        Assert.All(widths, w => Assert.InRange(w, LinkCanvas.MinColumns, LinkCanvas.MaxColumns));
        Assert.All(heights, h => Assert.InRange(h, LinkCanvas.MinRows, LinkCanvas.MaxRows));
        Assert.All(totals, n => Assert.InRange(n, LinkCanvas.MinPlayable, LinkCanvas.MaxPlayable));
    }

    /// <summary>
    /// 校验器链接行的画布与可落子格区间（裁决 J8-6 / link-map-terrain M9：按本类穷举实测向外留余量；与 <c>MapValidator</c> 声明行同值，改一处要同步另一处）。
    /// link-map-terrain 3.3 实测（种子 1–20、1600 次去重拼接）：画布宽 26–76、高 10–20，全图可落子 212–280；旧值（5–7 边长纯平棋盘）列 20–75、行 8–21、可落子 125–245。
    /// </summary>
    internal static class LinkCanvas
    {
        internal const int MinColumns = 20;
        internal const int MaxColumns = 85;
        internal const int MinRows = 8;
        internal const int MaxRows = 24;
        internal const int MinPlayable = 170;
        internal const int MaxPlayable = 320;
    }

    [Fact]
    public void 平移保内容()
    {
        // Scenario：被平移战场在链接前有 3 枚棋子、1 道栅栏、1 枚已揭示信物 → 链接后这些内容出现在平移后的对应格，类型、归属、揭示状态逐项相同。
        MapData initial = LinkMapGenerator.Generate(2);
        GameBoard board = GameBoard.Load(initial);
        var ledger = new RelicLedger(RelicGenerator.Generate(initial, MatchFixtures.Seed));
        var p1 = new PlayerId(1);
        Coord relic = initial.RelicCells.Keys.Where(initial.BirthZones[4].Contains).Order().First();
        Coord[] cells = [.. initial.BirthZones[4].Order()];
        Coord[] stones = [relic, cells[0], cells[1]];
        PieceType[] types = [PieceType.Basic, PieceType.Fortress, PieceType.Multiplier];
        for (int i = 0; i < 3; i++)
        {
            board.Place(stones[i], p1, types[i]);
        }

        var fenceA = new Coord(cells[0].X, cells[0].Y + 1);
        var fenceB = new Coord(cells[0].X + 1, cells[0].Y + 1);
        board.ApplyTerrainEdits([TerrainEdit.Fence(fenceA, fenceB)]);
        Assert.NotEmpty(ledger.Reveal(board, 1));
        Assert.True(ledger.IsRevealed(relic));
        RelicContent content = ledger.PublicStateOf(relic).Content!.Value;

        LinkResult result = LinkMapGenerator.Link(board.LinkedMap, 0, 4, LinkSide.East);
        board.ApplySplice(result.Map, result.Translate);
        ledger.Translate(result.Translate);
        BattlefieldShift shift = result.Shifts.Single(s => s.Battlefield == 4);
        Assert.False(shift.IsZero);
        Coord Moved(Coord c) => new(c.X + shift.Dx, c.Y + shift.Dy);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(new Occupant(p1, types[i]), board[Moved(stones[i])].Occupant);
            Assert.Equal(Moved(stones[i]), result.Translate(stones[i]));
        }

        Assert.True(board.Map.HasFence(Moved(fenceA), Moved(fenceB)));
        Assert.Equal(TerrainEdit.Fence(Moved(fenceA), Moved(fenceB)), Assert.Single(board.TerrainEdits));
        Assert.True(ledger.IsRevealed(Moved(relic)));
        Assert.Equal(content, ledger.PublicStateOf(Moved(relic)).Content);
        Assert.Equal(1, ledger.PublicStateOf(Moved(relic)).RevealedInMajorRound);
        Assert.Contains(ledger.RevealEvents, e => e.Coord == Moved(relic) && e.Content == content);
        Assert.All(ledger.RevealEvents, e => Assert.True(board.Map.RelicCells.ContainsKey(e.Coord)));
        Assert.Equal(initial.RelicCells.Count, ledger.Coords.Count());
        Assert.Equal(board.Map.RelicCells.Keys.Order(), ledger.Coords);
        Assert.Equal(3, board.AllGroups().Sum(g => g.Stones.Length));   // 棋子一枚没丢
    }
}
