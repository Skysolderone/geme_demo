using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Xunit.Abstractions;

namespace Siege.Core.Tests.LinkMapGeneration;

/// <summary>规格：openspec/changes/board-link-mode/specs/link-map-generation —— Requirement: 链接时的公共棋盘生成</summary>
public class 链接时的公共棋盘生成Tests(ITestOutputHelper output)
{
    [Fact]
    public void 首轮链接生成公共棋盘()
    {
        // Scenario：在 link:1 初始地图上链接战场 0 与 3（规格记作 1 与 4，这里用 0 起的出生棋盘编号）→
        // 新增一块 9–11 的公共棋盘与两条 3–4 宽通道，公共棋盘有 2–3 个信物格，原有格不变。
        MapData initial = LinkMapGenerator.Generate(1);
        LinkResult result = LinkMapGenerator.Link(initial, 0, 3);
        MapData grown = result.Map;

        Assert.False(result.Degraded);
        BoardPlate added = Assert.IsType<BoardPlate>(result.PublicBoard);
        Assert.Equal(BoardPlateKind.Public, added.Kind);
        Assert.InRange(added.Width, LinkMapGenerator.PublicMinSide, LinkMapGenerator.PublicMaxSide);
        Assert.InRange(added.Height, LinkMapGenerator.PublicMinSide, LinkMapGenerator.PublicMaxSide);
        Assert.Equal(6, grown.Boards.Length);
        Assert.Equal(added, grown.Boards[5]);
        Assert.Equal(2, result.Corridors.Length);
        Assert.All(result.Corridors, c => Assert.InRange(Math.Min(c.Width, c.Height), 3, 4));
        Assert.All(result.Corridors, c => Assert.True(c.Length >= LinkMapGenerator.MinCorridorLength));

        // 公共棋盘信物 2–3 个，公共争夺区、档位高于出生档、不在最外一圈。
        (Coord Cell, RelicCellSpec Spec)[] relics = [.. grown.RelicCells.Where(kv => added.Contains(kv.Key)).Select(kv => (kv.Key, kv.Value))];
        Assert.InRange(relics.Length, 2, 3);
        Assert.Equal(Math.Min(added.Width, added.Height) <= LinkMapGenerator.SmallPublicSide ? 2 : 3, relics.Length);
        Assert.All(relics, r => Assert.Equal(RelicZone.Contested, r.Spec.Zone));
        Assert.All(relics, r => Assert.True(r.Spec.Budget > BudgetTier.Birth));
        Assert.Contains(relics, r => r.Spec.Budget == BudgetTier.High);

        // 新增的格 = 公共棋盘 + 两条通道，恰好是"原来是障碍、现在可落子"的那些格；其余逐格不变。
        HashSet<Coord> expected = [.. added.Cells(), .. result.Corridors.SelectMany(c => c.Cells())];
        HashSet<Coord> turned = [.. initial.AllCoords().Where(c => !initial.IsPlayable(c) && grown.IsPlayable(c))];
        Assert.Equal(expected.Order(), turned.Order());
        Assert.All(initial.AllCoords().Where(initial.IsPlayable), c => Assert.True(grown.IsPlayable(c)));

        Assert.Equal(1, grown.MapVersion);
        Assert.True(MapValidator.Validate(grown).IsValid, MapValidator.Validate(grown).ToString());
        Assert.Equal(4, Battlefields.Of(grown).Count);
        Assert.Contains("公共棋盘", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void 决赛链接()
    {
        // Scenario：两个双人战场各含两块出生棋盘与一块公共棋盘，对二者执行链接 → 在两战场最近的两块棋盘之间生成第三块公共棋盘与两条通道，合并为一个战场。
        MapData map = LinkMapGenerator.Generate(1);
        map = LinkMapGenerator.Link(map, 0, 3).Map;
        map = LinkMapGenerator.Link(map, 1, 4).Map;
        Battlefields before = Battlefields.Of(map);
        Assert.Equal(3, before.Count);
        Assert.Equal(3, before.ById(0)!.Boards.Length);   // 出生 0、出生 3、公共棋盘
        Assert.Equal(3, before.ById(1)!.Boards.Length);

        LinkResult final = LinkMapGenerator.Link(map, 0, 1);
        Battlefields after = Battlefields.Of(final.Map);
        Assert.Equal(2, after.Count);
        Siege.Core.Board.Battlefield merged = after.ById(0)!;
        Assert.Equal([0, 1, 3, 4], merged.BirthZones);
        Assert.Null(after.ById(1));
        if (!final.Degraded)
        {
            Assert.Equal(BoardPlateKind.Public, final.PublicBoard!.Value.Kind);
            Assert.Equal(5 + 3, final.Map.Boards.Length);
            Assert.Equal(7, merged.Boards.Length);   // 3 + 3 + 新公共棋盘
        }

        // 最近的两块棋盘：实际相连的一对不比任何其他跨战场棋盘对更远（备选位置只在最近一对放不下时才用）。
        int gap = 初始布局Tests.Gap(map.Boards[final.FromBoard], map.Boards[final.ToBoard]);
        int closest = before.ById(0)!.Boards.SelectMany(a => before.ById(1)!.Boards.Select(b => 初始布局Tests.Gap(map.Boards[a], map.Boards[b]))).Min();
        Assert.Equal(final.ClosestPair, gap == closest);
    }

    [Fact]
    public void 全部链接序列都能生成()
    {
        // Scenario：对 link:1–link:20 穷举三类链接序列（15 种四人四区配对按规范次序各接决赛、全部 2+1+1、全部 3+1），
        // 每一步都通过物化状态校验，退化次数按类别统计并报告（输出到测试日志，抄进任务报告）。
        // 这里独立重跑每一步并自己调校验器——不是只读生成器自己的核对统计。
        var links = new int[3];
        var degraded = new int[3];
        var alternate = new int[3];
        int maxCorridor = 0;
        int distinct = 0;
        var corridorLengths = new List<int>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            MapData initial = LinkMapGenerator.Generate(seed);
            var memo = new Dictionary<string, LinkResult>(StringComparer.Ordinal);
            foreach (LinkSequence sequence in LinkMapGenerator.Sequences)
            {
                MapData map = initial;
                string key = string.Empty;
                foreach ((int x, int y) in sequence.Steps)
                {
                    key += $"({x},{y});";
                    if (!memo.TryGetValue(key, out LinkResult? result))
                    {
                        int before = Battlefields.Of(map).Count;
                        result = LinkMapGenerator.Link(map, x, y, seed);
                        MapValidationResult validation = MapValidator.Validate(result.Map);
                        Assert.True(validation.IsValid, $"link:{seed} {sequence} 第 ({x},{y}) 步：{validation}");
                        Assert.Equal(map.MapVersion + 1, result.Map.MapVersion);
                        Assert.Equal(before - 1, Battlefields.Of(result.Map).Count);
                        Assert.Null(Battlefields.Of(result.Map).ById(Math.Max(x, y)));
                        foreach (LinkCorridor c in result.Corridors)
                        {
                            corridorLengths.Add(c.Length);
                            maxCorridor = Math.Max(maxCorridor, c.Length);
                        }

                        memo[key] = result;
                        distinct++;
                    }

                    // 同一前缀的链接只算一次几何，但每条序列的每一步都计入它所属类别的统计。
                    int kind = (int)sequence.Kind;
                    links[kind]++;
                    if (result.Degraded)
                    {
                        degraded[kind]++;
                    }
                    else if (!result.ClosestPair)
                    {
                        alternate[kind]++;
                    }

                    map = result.Map;
                }
            }
        }

        corridorLengths.Sort();
        string[] names = ["四人四区", "2+1+1", "3+1"];
        for (int k = 0; k < 3; k++)
        {
            output.WriteLine($"{names[k]}：链接 {links[k]} 次，退化 {degraded[k]} 次，备选棋盘对 {alternate[k]} 次");
        }

        output.WriteLine($"通道长度：中位 {corridorLengths[corridorLengths.Count / 2]}，p90 {corridorLengths[corridorLengths.Count * 9 / 10]}，最长 {maxCorridor}");

        // 样本口径：三类序列都确实跑到了——每个种子四人四区 15 条 × 3 步、2+1+1 30 条 × 2 步、3+1 10 条 × 1 步；
        // 去重后每个种子恰 70 次不同的链接几何（首轮 10 对 + 四区第二步 15 + 决赛 15 + 2+1+1 第二步 30）。
        Assert.Equal(20 * 45, links[0]);
        Assert.Equal(20 * 60, links[1]);
        Assert.Equal(20 * 10, links[2]);
        Assert.Equal(20 * 70, distinct);
        Assert.True(corridorLengths.Count >= 20 * 70, $"只记录到 {corridorLengths.Count} 条通道");
    }

    [Fact]
    public void 链接序列清单覆盖三类()
    {
        // 15 种配对（规范次序：含最小编号的一对先）= 15 条四人四区序列，每条 3 步；2+1+1 共 10 × 3 = 30 条，每条 2 步；3+1 共 10 条，每条 1 步。
        ImmutableArray<LinkSequence> all = LinkMapGenerator.Sequences;
        Assert.Equal(15, all.Count(s => s.Kind == LinkSequenceKind.FourZones));
        Assert.All(all.Where(s => s.Kind == LinkSequenceKind.FourZones), s => Assert.True(Math.Min(s.Steps[0].X, s.Steps[0].Y) < Math.Min(s.Steps[1].X, s.Steps[1].Y)));
        Assert.Equal(30, all.Count(s => s.Kind == LinkSequenceKind.TwoPlusOnePlusOne));
        Assert.Equal(10, all.Count(s => s.Kind == LinkSequenceKind.ThreePlusOne));
        Assert.All(all.Where(s => s.Kind == LinkSequenceKind.FourZones), s => Assert.Equal(3, s.Steps.Length));
        Assert.All(all.Where(s => s.Kind == LinkSequenceKind.TwoPlusOnePlusOne), s => Assert.Equal(2, s.Steps.Length));
        Assert.All(all.Where(s => s.Kind == LinkSequenceKind.ThreePlusOne), s => Assert.Single(s.Steps));
        Assert.Equal(15, all.Where(s => s.Kind == LinkSequenceKind.FourZones).Select(s => string.Join(",", s.Steps.Take(2).OrderBy(p => p.X))).Distinct().Count());
    }

    [Fact]
    public void 退化为直通道()
    {
        // 规格「链接时的公共棋盘生成」："全部失败时 SHALL 退化为 X、Y 最近两块棋盘之间一条 3–4 格宽的直通道（不生成公共棋盘）"。
        // 生成图上种子 1–20 × 全部序列的退化次数为 0（任务报告 §4），这条路径只能手工构造：0 号 (0,0) 与 1 号 (0,9) 同列、相隔 4 格，中间塞不下 9×9；
        // 2 号 (7,2) 贴着二者右侧的间隔带（与两者各隔 2 格），任何从 0 号向右的横向通道都会撞上它，公共棋盘放到右边 / 上边都接不通 0 号（上边被 1 号挡住）。
        // 变异（check 阶段实跑）：Link 退化分支的 `degraded = true` 改 `false` → 本测试红 1。
        ImmutableArray<BoardPlate> births =
        [
            new(new Coord(0, 0), 5, 5, BoardPlateKind.Birth),
            new(new Coord(0, 9), 5, 5, BoardPlateKind.Birth),
            new(new Coord(7, 2), 5, 5, BoardPlateKind.Birth),
            new(new Coord(20, 0), 5, 5, BoardPlateKind.Birth),
            new(new Coord(20, 20), 5, 5, BoardPlateKind.Birth),
        ];
        MapData initial = LinkMapFixtures.Build("test-link-degraded", births, [], []);
        Assert.True(MapValidator.Validate(initial).IsValid, MapValidator.Validate(initial).ToString());

        LinkResult result = LinkMapGenerator.Link(initial, 0, 1, mapSeed: 1);
        Assert.True(result.Degraded);
        Assert.Null(result.PublicBoard);
        LinkCorridor corridor = Assert.Single(result.Corridors);
        Assert.InRange(corridor.Width, 3, 4);   // 纵向通道：宽 = 通道宽
        Assert.Equal(4, corridor.Height);       // 长 = 两块棋盘的间隔
        Assert.Equal(5, corridor.Origin.Y);
        Assert.Contains("退化链接", result.Summary, StringComparison.Ordinal);

        // 退化链接同样经过物化状态校验：版本号 +1、棋盘清单不变、两个战场合并为一个、新增的格恰为那条通道。
        MapData grown = result.Map;
        Assert.Equal(1, grown.MapVersion);
        Assert.Equal(5, grown.Boards.Length);
        Assert.True(MapValidator.Validate(grown).IsValid, MapValidator.Validate(grown).ToString());
        Assert.Equal(4, Battlefields.Of(grown).Count);
        Assert.Equal([0, 1], Battlefields.Of(grown).ById(0)!.BirthZones);
        Assert.Equal(corridor.Cells().Order(), initial.AllCoords().Where(c => !initial.IsPlayable(c) && grown.IsPlayable(c)).Order());
    }
}
