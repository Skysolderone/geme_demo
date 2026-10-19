using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// board-link-mode F.3：日志与跑局层"坐标随链接平移"的唯一工具 <see cref="LinkLayout"/>。规格 match-telemetry「对局日志的记录内容」——链接前的坐标按链接前布局解释；
/// link-map-generation「链接时的棋盘拼接」——平移向量供回放、存档与演出使用。
/// </summary>
/// <remarks>
/// 守门判据：只凭开局棋盘与日志里的向量换算出的坐标，与 Core 拼接时实际搬运格的映射（<see cref="LinkResult.Translate"/> 逐次复合）逐格相同——
/// 穷举种子 1–20 的全部链接序列、每个起止版本对、每个可落子格。
/// 变异验证（段 F.3 实跑）：MF3-1 <c>LinkLayout</c> 构造里不随合并改写战场标识（被贴方合并后仍按旧标识取向量）→ 红 16（含本类「与拼接时的格映射逐格相同」）；
/// MF3-2 <c>TranslateEdit</c> 一端场景格的栅栏只平移可落子端 → 「改造随棋盘平移」红 1。
/// </remarks>
public class 坐标随链接平移Tests
{
    [Fact(Skip = "open-map 段 B 删除")]
    public void 与拼接时的格映射逐格相同()
    {
        int checkedCells = 0;
        int globalShifts = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            MapData opening = LinkMapGenerator.Generate(seed);
            foreach (LinkSequence sequence in LinkMapGenerator.Sequences)
            {
                var maps = new List<MapData> { opening };
                var results = new List<LinkResult>();
                foreach (LinkStep step in sequence.Steps)
                {
                    LinkResult result = LinkMapGenerator.Link(maps[^1], step.X, step.Y, step.Side);
                    results.Add(result);
                    maps.Add(result.Map);
                    globalShifts += result.Shifts.Single(s => s.Battlefield == result.Anchor).IsZero ? 0 : 1;
                }

                var layout = new LinkLayout(opening.Boards, results.Select(r => new LinkShiftStep(r.Anchor, r.Mover, r.Shifts)));
                Assert.Equal(results.Count, layout.Versions);
                for (int from = 0; from <= results.Count; from++)
                {
                    foreach (Coord c in maps[from].AllCoords().Where(maps[from].IsPlayable))
                    {
                        Coord expected = c;
                        for (int to = from; to <= results.Count; to++)
                        {
                            Assert.Equal(expected, layout.Translate(c, from, to));
                            checkedCells++;
                            if (to < results.Count)
                            {
                                expected = results[to].Translate(expected);
                            }
                        }
                    }

                    // 每个版本上的棋盘位置与地图的棋盘清单相同。
                    Assert.Equal(maps[from].Boards.Select(b => b.Origin), Enumerable.Range(0, opening.Boards.Length).Select(z => layout.BoardAt(z, from).Origin));
                }
            }
        }

        Assert.True(checkedCells > 100_000, $"样本口径：只比对了 {checkedCells} 格");
        Assert.True(globalShifts > 0, "样本口径：穷举里确有整体越界平移（锚点向量非零）");
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 改造随棋盘平移()
    {
        // 与 GameBoard.ApplySplice 同口径：栅栏两端各随所在棋盘平移；一端是场景格时随另一端平移；两端都是场景格的不动。
        MapData opening = LinkMapFixtures.Initial();
        LinkResult result = LinkMapGenerator.Link(opening, 2, 3, LinkSide.East);
        var layout = new LinkLayout(opening.Boards, [new LinkShiftStep(result.Anchor, result.Mover, result.Shifts)]);
        BattlefieldShift mover = result.Shifts.Single(s => s.Battlefield == 3);
        Assert.False(mover.IsZero);

        // 夹具 3 号棋盘 x 29–34，东贴到 2 号（x 20–25）之后平移 (-3, 0)（link-map-terrain：夹具改为 6×6，几何同步改写，口径不变）。
        Coord inner = new(29, 3);                // 3 号棋盘西缘
        Coord west = new(28, 3);                 // 紧邻的场景格
        Coord scenery = new(27, 3);              // 两块棋盘之间的场景格
        Assert.Equal(-3, mover.Dx);
        Assert.Equal(TerrainEdit.Fence(new Coord(26, 3), new Coord(25, 3)), layout.TranslateEdit(TerrainEdit.Fence(inner, west), 0, 1));   // 随可落子端平移，成了接缝栅栏
        Assert.Equal(TerrainEdit.Fence(new Coord(26, 3), new Coord(27, 3)), layout.TranslateEdit(TerrainEdit.Fence(inner, new Coord(30, 3)), 0, 1));
        Assert.Equal(TerrainEdit.Fence(new Coord(27, 0), new Coord(27, 1)), layout.TranslateEdit(TerrainEdit.Fence(new Coord(27, 0), new Coord(27, 1)), 0, 1));   // 两端场景格、仍是场景格：不动
        Assert.Null(layout.TranslateEdit(TerrainEdit.Fence(scenery, new Coord(27, 4)), 0, 1));                                                                     // 两端场景格、被拼过来的棋盘占到：丢弃
        Assert.Throws<ArgumentException>(() => layout.Translate(scenery, 0, 1));

        // 与盘面实际迁移逐条一致（GameBoard.ApplySplice 是 Core 侧的唯一实现）。
        TerrainEdit[] edits = [TerrainEdit.Fence(inner, west), TerrainEdit.Fence(new Coord(27, 0), new Coord(27, 1)), TerrainEdit.Fence(scenery, new Coord(27, 4)), TerrainEdit.Fence(inner, new Coord(30, 3))];
        GameBoard board = GameBoard.Load(opening);
        board.ApplyTerrainEdits(edits);
        board.ApplySplice(result.Map, result.Translate);
        Assert.Equal(edits.Select(e => layout.TranslateEdit(e, 0, 1)).OfType<TerrainEdit>(), board.TerrainEdits);
    }

    [Fact(Skip = "open-map 段 B 删除")]
    public void 缺少向量的记录被拒绝()
    {
        MapData opening = LinkMapFixtures.Initial();
        LinkResult result = LinkMapGenerator.Link(opening, 0, 1, LinkSide.East);
        ImmutableArray<BattlefieldShift> missing = [.. result.Shifts.Where(s => s.Battlefield != 4)];
        FormatException ex = Assert.Throws<FormatException>(() => new LinkLayout(opening.Boards, [new LinkShiftStep(0, 1, missing)]));
        Assert.Contains("缺少战场", ex.Message, StringComparison.Ordinal);
    }
}
