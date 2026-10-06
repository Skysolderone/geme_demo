using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;

namespace Siege.Core.Tests.MapGeneration;

/// <summary>
/// 规格：openspec/changes/board-isolated-gen/specs/map-generation —— Requirement: 棋盘档生成参数
/// （含「地图种子与确定性」「校验闭环」对棋盘档生成同样生效的部分）。规范：.trellis/spec/core/determinism.md。
/// </summary>
/// <remarks>
/// 变异验证（board-map 段 B 实跑，每条只跑本类）：
/// M-B10 MapRandom.ForBoardAttempt 改用边疆档的派生式（Mix(种子) ^ Mix(序号 ^ AttemptDomain)）→ 红 2：随机序列互不相同、黄金值；
/// M-B12 边疆档域常量 AttemptDomain 末位 D → E → 红 1：边疆档生成图不变；
/// M-B13 BoardMapLayout 里加一个 `new HashSet&lt;int&gt;` → 红 1：源码守门。
/// board-isolated-gen 段 A（只跑本类，红数按 Theory 行计）：
/// P1 MaxBoardsFor 上限 +1 → 红 8：棋盘数越界 ×3、棋盘数下限随人数 ×4、缺省参数 ×1；
/// P2 MinBoardsFor 对 2 / 3 人也取"出生 + 2" → 红 8：棋盘数下限随人数 ×4、棋盘数决定公共棋盘数 ×2、图面不留多余空白、校验闭环；
/// P3 不给棋盘数时恒取 4 人缺省 7 → 红 2：按人数的缺省棋盘数 ×2；
/// P4 裁切留白 2 → 3 → 红 1：图面不留多余空白（只跑该测试）；
/// 2 人收窄为 4–5 后重跑：P1 红 9（多了 二人棋盘数6越界）、P2 红 9（同上）、P3 红 2、P4 红 1；
/// P6 MapRandom.ForBoardAttempt 去掉人数一项 → 红 7：同种子不同人数的公共棋盘互不相同 ×5、随机序列互不相同、黄金值；
/// R1 重抽上限 MaxSizeDraws 200 → 1（等于不重抽）：2 人 n6 删除后本类 0 红（各档在 64 次尝试内仍能抽成），
/// 改由 棋盘档布局规则Tests.边长整组重抽 守住 → 红 3；
/// P7 2 人公共棋盘上限改回 3（棋盘数上限 6）→ 红 3：二人棋盘数6越界、棋盘数下限随人数（2 人两行，报文区间变成 4–6）。
/// </remarks>
public class 棋盘档生成参数Tests
{
    private static ulong[] Take(RandomStream stream, int count) => [.. Enumerable.Range(0, count).Select(_ => stream.NextUInt64())];

    private static string Digest(MapData map) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MapFile.ToJson(map).Replace("\r\n", "\n", StringComparison.Ordinal))));

    [Theory]
    [InlineData(11)]
    [InlineData(6)]
    [InlineData(0)]
    public void 棋盘数越界(int boards)
    {
        // Scenario：请求 4 人、棋盘数 11 → 报错并指出 4 人的合法范围 7–10；不静默夹取。
        var parameters = new BoardMapParameters { BoardCount = boards };

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => BoardMapGenerator.Generate(1, parameters));

        Assert.Contains("4 人 7–10", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(parameters.EnsureValid);
        Assert.Contains("4 人 7–10", Assert.Throws<ArgumentOutOfRangeException>(() => BoardMapId.Format(1, parameters)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, 3, "2 人 4–5")]
    [InlineData(2, 7, "2 人 4–5")]
    [InlineData(3, 4, "3 人 5–8")]
    [InlineData(3, 9, "3 人 5–8")]
    public void 棋盘数下限随人数(int players, int boards, string range)
    {
        // Scenario：请求 2 人、棋盘数 3 → 报错并指出 2 人的合法范围 4–5（3 人同理 5–8）。标识入口同样报出。
        var parameters = new BoardMapParameters { Players = players, BoardCount = boards };

        Assert.Contains(range, Assert.Throws<ArgumentOutOfRangeException>(parameters.EnsureValid).Message, StringComparison.Ordinal);
        Assert.Contains(range, Assert.Throws<ArgumentOutOfRangeException>(() => BoardMapGenerator.Generate(1, parameters)).Message, StringComparison.Ordinal);
        string id = $"board:1:p{players}:n{boards}";
        Assert.Contains(range, Assert.Throws<FormatException>(() => BoardMapId.Parse(id)).Message, StringComparison.Ordinal);
        Assert.Contains(range, Assert.Throws<FormatException>(() => MapCatalog.Resolve(id)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 二人棋盘数6越界()
    {
        // 主会话裁决（2026-10-06）：2 人合法棋盘数收窄为 4–5（公共棋盘 1–2 块）。请求 2 人、棋盘数 6 → 报错并指出 2 人的合法范围 4–5。
        // 变异 P7（2 人公共棋盘上限改回 3，即棋盘数上限 6）→ 本测试红（连同 棋盘数下限随人数 2 人两行共红 3）。
        var parameters = new BoardMapParameters { Players = 2, BoardCount = 6 };

        Assert.Contains("2 人 4–5", Assert.Throws<ArgumentOutOfRangeException>(parameters.EnsureValid).Message, StringComparison.Ordinal);
        Assert.Contains("2 人 4–5", Assert.Throws<ArgumentOutOfRangeException>(() => BoardMapGenerator.Generate(1, parameters)).Message, StringComparison.Ordinal);
        Assert.Contains("2 人 4–5", Assert.Throws<FormatException>(() => BoardMapId.Parse("board:1:p2:n6")).Message, StringComparison.Ordinal);
        Assert.Contains("2 人 4–5", Assert.Throws<FormatException>(() => MapCatalog.Resolve("board:1:p2:n6")).Message, StringComparison.Ordinal);
        Assert.Equal(5, new BoardMapParameters { Players = 2, BoardCount = 5 }.BoardCount);   // 上限 5 本身合法
        new BoardMapParameters { Players = 2, BoardCount = 5 }.EnsureValid();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1)]
    [InlineData(0)]
    public void 人数越界(int players)
    {
        // Scenario：请求 5 人或 1 人的棋盘图 → 报错并指出合法人数 2–4。
        var parameters = new BoardMapParameters { Players = players };

        Assert.Contains("合法人数 2–4", Assert.Throws<ArgumentOutOfRangeException>(parameters.EnsureValid).Message, StringComparison.Ordinal);
        Assert.Contains("合法人数 2–4", Assert.Throws<ArgumentOutOfRangeException>(() => BoardMapGenerator.Generate(1, parameters)).Message, StringComparison.Ordinal);
        Assert.Contains("合法人数 2–4", Assert.Throws<FormatException>(() => BoardMapId.Parse($"board:1:p{players}")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 缺省参数()
    {
        // Scenario：只给地图种子、不给人数与棋盘数 → 4 人图，恰有 7 块棋盘，其中出生棋盘 5 块、公共棋盘 2 块；规格档为棋盘档。
        MapData map = BoardMapGenerator.Generate(3);

        Assert.Equal(7, map.Boards.Length);
        Assert.Equal(5, map.Boards.Count(b => b.Kind == BoardPlateKind.Birth));
        Assert.Equal(2, map.Boards.Count(b => b.Kind == BoardPlateKind.Public));
        Assert.Equal((4, MapProfile.Board, 5), (map.MaxPlayers, map.Profile, map.BirthZones.Length));
        Assert.Equal((4, 7, 5, 2), (BoardMapParameters.Default.Players, BoardMapParameters.Default.BoardCount, BoardMapParameters.Default.BirthBoards, BoardMapParameters.Default.PublicBoards));
        Assert.Equal((7, 10, 7), (BoardMapParameters.MinBoards, BoardMapParameters.MaxBoards, BoardMapParameters.DefaultBoards));   // 4 人常量与按人数的查询一致
        Assert.Equal((7, 10, 7), (BoardMapParameters.MinBoardsFor(4), BoardMapParameters.MaxBoardsFor(4), BoardMapParameters.DefaultBoardsFor(4)));
    }

    [Theory]
    [InlineData(3, 6, 4, 2)]   // Scenario「3 人缺省」：6 块，出生 4、公共 2
    [InlineData(2, 5, 3, 2)]
    public void 按人数的缺省棋盘数(int players, int boards, int births, int publics)
    {
        // Scenario：给地图种子与人数 3、不给棋盘数 → 恰有 6 块棋盘，其中出生棋盘 4 块、公共棋盘 2 块。地图的人数上限 = 3。
        var parameters = new BoardMapParameters { Players = players };
        MapData map = BoardMapGenerator.Generate(3, parameters);

        Assert.Equal(boards, parameters.BoardCount);
        Assert.Equal(boards, map.Boards.Length);
        Assert.Equal(births, map.Boards.Count(b => b.Kind == BoardPlateKind.Birth));
        Assert.Equal(publics, map.Boards.Count(b => b.Kind == BoardPlateKind.Public));
        Assert.Equal((players, births), (map.MaxPlayers, map.BirthZones.Length));
        Assert.Equal($"board:3:p{players}", map.Id);
    }

    [Theory]
    [InlineData(4, 7)]
    [InlineData(4, 10)]
    [InlineData(3, 5)]
    [InlineData(3, 8)]
    [InlineData(2, 4)]
    [InlineData(2, 5)]
    public void 棋盘数决定公共棋盘数(int players, int boards)
    {
        // 出生棋盘数 = 人数 + 1，公共棋盘数 = 棋盘数 − 出生棋盘数（测试内独立算式）；区间两端都取。
        MapData map = BoardGenFixtures.Generated(2, boards, players).Map;

        Assert.Equal(boards, map.Boards.Length);
        Assert.Equal(players + 1, map.Boards.Count(b => b.Kind == BoardPlateKind.Birth));
        Assert.Equal(boards - players - 1, map.Boards.Count(b => b.Kind == BoardPlateKind.Public));
        Assert.Equal(players, map.MaxPlayers);
    }

    [Theory]
    [InlineData(1UL, 4, 8)]
    [InlineData(12345UL, 4, 7)]
    [InlineData(987654321UL, 3, 6)]
    [InlineData(18446744073709551615UL, 2, 5)]
    public void 同种子同图(ulong seed, int players, int boards)
    {
        // Scenario：同一地图种子、人数与棋盘数生成两次 → 尺寸、棋盘清单与全部格子逐项相同。两次都不走缓存。
        var parameters = new BoardMapParameters { Players = players, BoardCount = boards };
        GeneratedBoardMap first = BoardMapGenerator.GenerateDetailed(seed, parameters);
        GeneratedBoardMap second = BoardMapGenerator.GenerateDetailed(seed, parameters);

        Assert.Equal((first.Map.Width, first.Map.Height, first.Attempt), (second.Map.Width, second.Map.Height, second.Attempt));
        Assert.Equal(first.Map.Boards.AsEnumerable(), second.Map.Boards.AsEnumerable());
        Assert.Equal(first.Map.AllCoords().Select(first.Map.IsPlayable), second.Map.AllCoords().Select(second.Map.IsPlayable));
        Assert.Equal(first.Map.RelicCells.OrderBy(kv => kv.Key), second.Map.RelicCells.OrderBy(kv => kv.Key));
        byte[] a = Encoding.UTF8.GetBytes(MapFile.ToJson(first.Map));
        Assert.True(a.Length > 2_000, $"样本口径：导出文本只有 {a.Length} 字节。");
        Assert.Equal(a, Encoding.UTF8.GetBytes(MapFile.ToJson(second.Map)));
    }

    [Fact]
    public void 图面不留多余空白()
    {
        // Scenario：检查任一棋盘档生成图 → 最靠外的棋盘格到地图四条外缘的距离都恰为 2 格。
        // 「地图外接尺寸由摆放结果决定：取全部棋盘的实际外接范围，四周各留 2 格场景，列数与行数各自独立、各在 15–60」。
        // 断言从地图数据反推：可落子格的外接范围，不读生成器的工作态。样本：全部 200 张。
        var widths = new SortedSet<int>();
        var heights = new SortedSet<int>();
        int nonSquare = 0;
        foreach ((ulong seed, int players, int boards) in BoardGenFixtures.Sample())
        {
            MapData map = BoardGenFixtures.Generated(seed, boards, players).Map;
            string id = BoardGenFixtures.Label(seed, players, boards);
            Coord[] playable = [.. map.AllCoords().Where(map.IsPlayable)];
            Assert.Equal(map.PlayableCount, playable.Length);
            (int west, int south) = (playable.Min(c => c.X), playable.Min(c => c.Y));
            (int east, int north) = (map.Width - 1 - playable.Max(c => c.X), map.Height - 1 - playable.Max(c => c.Y));
            Assert.True(
                (west, east, south, north) == (2, 2, 2, 2),
                $"{id}（{map.Width}×{map.Height}）到西 / 东 / 南 / 北外缘的距离为 {west} / {east} / {south} / {north}。");

            // 可落子格只有棋盘格：两种外接范围相同。
            Assert.Equal(
                (2, 2, map.Width - 3, map.Height - 3),
                (map.Boards.Min(b => b.Origin.X), map.Boards.Min(b => b.Origin.Y),
                    map.Boards.Max(b => b.Origin.X + b.Width - 1), map.Boards.Max(b => b.Origin.Y + b.Height - 1)));

            Assert.InRange(map.Width, 15, 60);
            Assert.InRange(map.Height, 15, 60);
            widths.Add(map.Width);
            heights.Add(map.Height);
            nonSquare += map.Width != map.Height ? 1 : 0;
        }

        // 样本口径：列数与行数各自独立——各不止一种取值，且确有列数 ≠ 行数的图。
        Assert.True(widths.Count >= 3 && heights.Count >= 3 && nonSquare >= 10, $"列数 {widths.Count} 种、行数 {heights.Count} 种、非正方 {nonSquare} 张。");
    }

    [Fact]
    public void 不同种子不同图_人数与棋盘数是标识的一部分()
    {
        foreach ((int players, int boards) in new[] { (4, 7), (3, 6), (2, 5) })
        {
            string[] layouts = [.. Enumerable.Range(1, 20).Select(seed => string.Join(
                ";", BoardGenFixtures.Generated((ulong)seed, boards, players).Map.Boards.Select(b => $"{b.Origin},{b.Width},{b.Height}")))];
            Assert.True(layouts.Distinct(StringComparer.Ordinal).Count() >= 19, $"{players} 人：种子 1–20 的棋盘布局重复超过 1 张。");
        }

        Assert.NotEqual(MapFile.ToJson(BoardGenFixtures.Generated(5, 7).Map), MapFile.ToJson(BoardGenFixtures.Generated(5, 8).Map));
        // 同种子、同棋盘数、不同人数：标识不同，出生棋盘数也不同。
        MapData four = BoardGenFixtures.Generated(5, 7).Map;
        MapData three = BoardGenFixtures.Generated(5, 7, 3).Map;
        Assert.Equal(("board:5", "board:5:p3:n7"), (four.Id, three.Id));
        Assert.Equal((5, 4), (four.BirthZones.Length, three.BirthZones.Length));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void 同种子不同人数的公共棋盘互不相同(ulong seed)
    {
        // 人数进入棋盘档随机流（主会话裁决）：同一种子的 2 / 3 / 4 人缺省图，公共棋盘清单（位置与宽高）两两不同。
        // 变异 P6（MapRandom.ForBoardAttempt 去掉人数那一项）→ 本测试 5 行全红（抽样先抽公共棋盘，同种子的公共棋盘边长序列相同）。
        string[] publics = [.. new[] { 2, 3, 4 }.Select(players => string.Join(";", BoardGenFixtures.Generated(seed, players + 3, players).Map.Boards
            .Where(b => b.Kind == BoardPlateKind.Public).Select(b => $"{b.Origin.ToNotation()},{b.Width}x{b.Height}")))];
        Assert.All(publics, text => Assert.NotEmpty(text));
        Assert.Equal(3, publics.Distinct(StringComparer.Ordinal).Count());

        // 只比宽高序列（不看位置）也两两不同：差别来自抽样，不只是摆放。
        string[] sizes = [.. new[] { 2, 3, 4 }.Select(players => string.Join(";", BoardGenFixtures.Generated(seed, players + 3, players).Map.Boards
            .Where(b => b.Kind == BoardPlateKind.Public).Select(b => $"{b.Width}x{b.Height}")))];
        Assert.Equal(3, sizes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void 校验闭环_返回的地图必过静态校验_尝试次数留有余量()
    {
        Assert.Equal(64, BoardMapGenerator.DefaultMaxAttempts);
        foreach ((ulong seed, int players, int boards) in BoardGenFixtures.Sample())
        {
            GeneratedBoardMap g = BoardGenFixtures.Generated(seed, boards, players);
            Assert.True(MapValidator.Validate(g.Map).IsValid, BoardGenFixtures.Label(seed, players, boards));
            Assert.InRange(g.Attempt, 0, 31);   // 留一半余量：逼近上限说明构造式保证在退化
            Assert.Equal(g.Attempt, g.Discarded.Length);
        }

        // 上限 0 是参数错误。
        Assert.Throws<ArgumentOutOfRangeException>(() => BoardMapGenerator.Generate(1, maxAttempts: 0));
    }

    [Fact]
    public void 经地图文件往返后逐项相同()
    {
        foreach ((ulong seed, int players) in new (ulong, int)[] { (1, 4), (2, 3), (3, 2) })
        {
            MapData map = BoardGenFixtures.Generated(seed, BoardGenFixtures.Ranges.Single(r => r.Players == players).Boards[1], players).Map;
            string json = MapFile.ToJson(map);
            MapData restored = MapFile.FromJson(json);
            Assert.Equal(json, MapFile.ToJson(restored));
            Assert.Equal(map.Boards.AsEnumerable(), restored.Boards.AsEnumerable());
            Assert.Equal(players, restored.MaxPlayers);
            Assert.True(MapValidator.Validate(restored).IsValid);
        }
    }

    [Fact]
    public void 棋盘档随机序列与边疆档互不相同()
    {
        // 「棋盘档生成 MUST 使用与边疆档生成互不相同的随机序列，且同样只由地图种子与尝试序号决定」。
        // 变异 M-B10（段 B 实跑）：ForBoardAttempt 改用边疆档的域常量 → 本测试红。
        Assert.Equal(Take(MapRandom.ForBoardAttempt(12345, 4, 3), 64), Take(MapRandom.ForBoardAttempt(12345, 4, 3), 64));
        foreach (ulong seed in new ulong[] { 0, 1, 12345, ulong.MaxValue })
        {
            string[] board = [.. Enumerable.Range(0, 512).Select(k => string.Join(",", Take(MapRandom.ForBoardAttempt(seed, 4, k), 8)))];
            Assert.Equal(512, board.Distinct(StringComparer.Ordinal).Count());
            string[] frontier =
            [
                .. Enumerable.Range(0, 512).Select(k => string.Join(",", Take(MapRandom.ForAttempt(seed, k), 8))),
                string.Join(",", Take(MapRandom.ForSurfaces(seed), 8)),
            ];
            Assert.Empty(board.Intersect(frontier, StringComparer.Ordinal));
        }

        Assert.NotEqual(Take(MapRandom.ForBoardAttempt(7, 4, 1), 8), Take(MapRandom.ForBoardAttempt(8, 4, 0), 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapRandom.ForBoardAttempt(1, 4, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapRandom.ForBoardAttempt(1, 0, 0));

        // 人数进入派生（board-isolated-gen）：同一种子与序号，2 / 3 / 4 人的随机源两两不同。
        string[] byPlayers = [.. new[] { 2, 3, 4 }.Select(p => string.Join(",", Take(MapRandom.ForBoardAttempt(12345, p, 0), 8)))];
        Assert.Equal(3, byPlayers.Distinct(StringComparer.Ordinal).Count());

        // 也不与对局的任何一条命名子流同构。
        var game = new GameSeed(12345);
        foreach (string name in new[] { GameSeed.RelicGeneration, GameSeed.Recruit, GameSeed.Setup, GameSeed.ZonePick })
        {
            Assert.NotEqual(Take(MapRandom.ForBoardAttempt(12345, 4, 0), 8), Take(game.Stream(name), 8));
        }
    }

    [Fact]
    public void 棋盘档随机源与生成图的黄金值()
    {
        // 钉住派生方式与生成器本身：它们一变，同一个 board: 标识就会生成另一张图，旧日志按标识重建地图随之失效。
        // 黄金值取自 board-isolated-gen 段 A 补改（人数进入棋盘档随机流、边长整组重抽）完成后的实跑——board-isolated-gen 重定；
        // 人数进入派生，前两个数（4 人第 0 个随机源的头两个值）随之改变，旧值 16155176619714753925 / 3336457412782954627。
        // 历次旧值：board-map 段 B 原值 44×44、摘要 04B085ED…8E0B；段 B 修正 42×42、可落子 540、摘要 B1065BDD…9574；
        // 段 B 修正二 24×40、可落子 468、摘要 21C2717A…8840；board-isolated-gen 段 A 初版 38×44、可落子 457、摘要 14E954C4…F92D。红了不等于错——确认要改再更新此值，并在实施记录里写明。
        Assert.Equal(new[] { GoldenA, GoldenB }, Take(MapRandom.ForBoardAttempt(12345, 4, 0), 2));
        GeneratedBoardMap g = BoardMapGenerator.GenerateDetailed(12345);
        Assert.Equal(("board:12345", GoldenPlayable, GoldenWidth, GoldenHeight), (g.Map.Id, g.Map.PlayableCount, g.Map.Width, g.Map.Height));
        Assert.True(GoldenDigest == Digest(g.Map), $"board:12345 的导出文本摘要变了：现为 {Digest(g.Map)}。");
    }

    private const ulong GoldenA = 14574461902476217853UL;
    private const ulong GoldenB = 14705341623990255760UL;
    private const int GoldenPlayable = 463;
    private const int GoldenWidth = 42;
    private const int GoldenHeight = 44;
    private const string GoldenDigest = "4366325614F6E6C4C37E4A051C4340536237C20EF06C91DF900AECD8CA8075CB";

    [Fact]
    public void 边疆档生成图不变()
    {
        // Scenario：用地图种子 1 到 50 以 gen: 标识各生成一张 → 每张图的导出文件与引入棋盘档生成之前逐字节相同。
        // 黄金值出处（直接比对，不是推断）：board-map 段 B 动手之前，用 `git archive HEAD` 把提交 149d899 的 Siege.Core 源码导出到临时目录
        // 单独编译，逐个生成 gen:1..50、导出文本行尾归一为 LF 后取 SHA-256。即：这 50 个摘要来自"引入棋盘档（含段 A 的坐标记法与地图文件改动）之前"的代码。
        // 其中 gen:1 / gen:7 与 生成确定性Tests 的既有黄金值相同（交叉印证取值方法）。
        Assert.Equal(50, FrontierGolden.Length);
        Assert.Equal(50, FrontierGolden.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("E726108A634E025733FE54FC6059CBE8E535C2F294E9C731E1C0EBF8D4B481DC", FrontierGolden[0]);
        Assert.Equal("BC137EAEA5E4E9048FD0C89242F1BA929460D5EEC61DE42719633F27C29A8C50", FrontierGolden[6]);

        var changed = new List<string>();
        for (int i = 0; i < FrontierGolden.Length; i++)
        {
            ulong seed = (ulong)(i + 1);
            MapData map = MapCatalog.Resolve($"gen:{seed}");
            Assert.Equal($"gen:{seed}", map.Id);
            Assert.Empty(map.Boards);
            if (Digest(map) != FrontierGolden[i])
            {
                changed.Add($"gen:{seed}");
            }
        }

        Assert.True(changed.Count == 0, "导出文本变了的边疆档生成图：" + string.Join("、", changed));
    }

    private static readonly string[] FrontierGolden =
    [
        "E726108A634E025733FE54FC6059CBE8E535C2F294E9C731E1C0EBF8D4B481DC",   // gen:1
        "7E9D05514ED9EBFDA704EBFC18608BF07A1EE4A140E509834A1030B59AE0AE56",   // gen:2
        "BA155B03828768B9B640845A3BAFF7343CD1875A73FA96FA31DEA78C86F110F7",   // gen:3
        "FFE0FC028DC2FB84366EC6C8DCD94D636BB80D3991FA63A90F6D61FFEDACE333",   // gen:4
        "47968088FBA02097936EBE8D5D59164C7156AFEFC82929C280C681D0F4043D8E",   // gen:5
        "C36471E312ADB3AC76557CAF988C69EC26E6B06A481797A3E864B939773D5054",   // gen:6
        "BC137EAEA5E4E9048FD0C89242F1BA929460D5EEC61DE42719633F27C29A8C50",   // gen:7
        "020F49B1D2A29A807E440DE8819739B12234BC237C2FAA59D0134C85D00CA0B5",   // gen:8
        "E3DA0DD353648CBAB3BBF2034DD5666731D08736FF532ECF207C714FBE843E9F",   // gen:9
        "D0249BC2D857910FB844D7DC96FD453F11B058F8A0E9C8DED0346A6EBE2A9677",   // gen:10
        "8A241A8843F72BBA311226D353EB7D3B6FD742AC73CEC8D1AC98A582D26B90A0",   // gen:11
        "5E9931AA902C03ABEB30BCDDE906A24C4201BD87CBA43AF8B1CA8C72B567A123",   // gen:12
        "856031DD3E16B5C809BBB6C9920758FD68E3B34F2F82D4176C29866F35828348",   // gen:13
        "009D212A0F8BEC06592659B5D1DFCEFD28402BED0F8F565D2D441B2883736820",   // gen:14
        "32BAA99F2FE2D025F7AD1BB85CC0F13F8277FC5B0EA41ABB82D791437284BD74",   // gen:15
        "CACBBEB5B0E95F712A08E984122EC659AD4F9DCD9945643F58A7346D7D596CE9",   // gen:16
        "91F67FBDB9BB312B5EC3037D2848E6B66C747C683B37AEC88397CA12B22E4973",   // gen:17
        "F09500246398B0C300CEBF2EC956029C1E5A48D91A26D2A683F9C09149F890F8",   // gen:18
        "672C6A8D81EFB25581D76E3E3C0AF631F974843A6BA7D2C6B16AB8FD1B92DF1A",   // gen:19
        "60C3D99E7745A22985A820C96AFC2C9ADB17ED27BF1222FE75503DC53945EDEC",   // gen:20
        "F596C5B405A14CA0B06325E439C1A180B2564B4112976FF1E9384BA4EF2C6DA0",   // gen:21
        "9A4F09E86BFB8F1EBE2EC0AC645C7E252B2CE68428902A97BB110DAF27E85EBF",   // gen:22
        "E63AFB5A6A9BA995690FE3CBC129BFBF3235C9AF8CB45A8DF66BFF6AB8C77147",   // gen:23
        "3C58FAA622C42167F3CA423B665B2199304675859AC4676EB405E2F166CC4F00",   // gen:24
        "B2D28BC8119EE4CC8ED3C36A7147723D5DE8426210149664266AA00EE014F51C",   // gen:25
        "B8F1FC09A106BE742C4CB5A983BD2D37F2A5987E058BC0959932B3BD55A0117F",   // gen:26
        "9B909F2A939393B54A936C49BFECD332DBAE1C3D18D7B0D0DDDBA2AB999D62A6",   // gen:27
        "BBDE50A4D7A448FA0A839213007F8792B951AB6DD32D3B49DD03CFE0CD894CCC",   // gen:28
        "C2DA2E539F7ED6E3189F3844AFDE3AB98C431F68A522148BEB77BDB2A234B91E",   // gen:29
        "B482B152A4E4E7E18094D918B073363FF74F23E2FE4F60C5F77721342876B59D",   // gen:30
        "B9CC490871F95AA7021A871ED4F1C02A9D3C4CDE17C37B8543D119355A297E01",   // gen:31
        "54E5D6ED8332341AB84D6ECCC97EE9703BA904A7B94D45FF0856DA43D7135D97",   // gen:32
        "FC8A6C10989DD5407F2644FBE7AC4065174F7150FE49F7F21D804CBAC3C85D08",   // gen:33
        "97A3C13C76555A72A6CC23B5BD52D386C399FE77329DBA02256ECCD53924AFE0",   // gen:34
        "4C81CCB67E42B3FE16EC6BD274E07E240306E6627646E09341716F97161FBBCE",   // gen:35
        "A4EC79B29AA221721414A54E8DA8C78E695A54EC70C9DAE71368A663AE0E5E6A",   // gen:36
        "C204E46D691370D97396990F01B091C9BC8C3458FCFA28E4E2EE3EA188C1907C",   // gen:37
        "22B5A8BA7A90DA9C7524612DD74332F545D37685C7D69B5BCC7C1B031314D03B",   // gen:38
        "B44A298062CA5AD5D4641519B0C4367765281AE37C5DF8992365E4C48AFE324B",   // gen:39
        "11C3A41C7208C20B64B5C397193B3A22F0E70F65E055D033B11B6D0FD7861AD6",   // gen:40
        "76358A568AA11F32355C12F88DEFB565C9D05A9048898D06767F1AFA30E97FC9",   // gen:41
        "E1CCDE9FD11781A4928B05D4171BE803CA57C75267B842899B81852389D91A90",   // gen:42
        "36DA8CB092EB912B2121C1C8C0BA0B3C6E5B9CC2D6DFA8F68EE8229DAFD12FC6",   // gen:43
        "6C2C147286173DEDB8E52E97726E865074E02B74EB9F0E84A600FFD676592CE3",   // gen:44
        "D92B29475B04000A15F44CEBF361BF6F1B5770C3F879CA73AC91CB2E25A925A4",   // gen:45
        "EF026102F1E79C8B43634A20D7EA88210BA0CF3B4EF70DAD27C9053DFCC03126",   // gen:46
        "85162A53142C3E84EDB4CEE637F8ECD800A64321BD8E5A2889C07CBED268BD50",   // gen:47
        "D8C4B363C4BA1458F286D0D2628F54BA5F2AD285E6F69C190484D30355411D06",   // gen:48
        "DA345D5E3180438B5382F06478F60AD8459FEF1A55FE6D9DF0DDAC57C6D5F091",   // gen:49
        "7CE63435DC92547B8FCA5F27FC49857827335B9E96566BC03AE702035F577381",   // gen:50
    ];

    [Fact]
    public void 棋盘档生成器源码不含散列次序遍历_浮点_时钟与环境_也不见对局种子()
    {
        // 守门（determinism.md：生成路径禁浮点、禁散列容器遍历、禁时钟与环境；生成器拿不到对局种子）。源码扫描，口径同边疆档生成器那一条。
        string maps = Path.Combine(FrontierFixtures.RepoRoot(), "src", "Siege.Core", "Board", "Maps");
        string[] layoutFiles = [.. Directory.EnumerateFiles(maps, "BoardMapLayout*.cs").Order(StringComparer.Ordinal)];
        Assert.NotEmpty(layoutFiles);
        string layout = StripComments(string.Join("\n", layoutFiles.Select(File.ReadAllText)));
        string generator = StripComments(File.ReadAllText(Path.Combine(maps, "BoardMapGenerator.cs")));
        string parameters = StripComments(File.ReadAllText(Path.Combine(maps, "BoardMapParameters.cs")));
        Assert.True(layout.Length > 5_000, $"样本口径：工作态源码只有 {layout.Length} 个字符。");

        Assert.Contains("_rng.NextInt(", layout, StringComparison.Ordinal);                              // 反面：扫到的确实是工作态
        Assert.Contains("MapRandom.ForBoardAttempt(", generator, StringComparison.Ordinal);              // 反面：扫到的确实是生成器
        Assert.DoesNotMatch(@"\b(HashSet|Dictionary|SortedSet|SortedDictionary|Hashtable|Lookup|GroupBy|ToHashSet|ToDictionary|ToLookup|Distinct|AsParallel)\b", layout);
        Assert.DoesNotMatch(@"\b(Immutable\w+|FrozenSet|FrozenDictionary|ISet|IDictionary|ConcurrentBag)\b", layout);

        // 生成器：散列容器只有灌数据用的不可变 builder，且任何 builder 都不出现在 foreach / LINQ 的数据源位置；地图数据自带的散列容器只许计数与查询。
        Assert.DoesNotMatch(@"(?<!Immutable)\b(HashSet|Dictionary)<", generator);
        string[] builders = [.. Regex.Matches(generator, @"var (\w+) = Immutable(?:HashSet|Dictionary)\.CreateBuilder").Select(m => m.Groups[1].Value)];
        // board-isolated-gen：咽喉 builder 随通道删除，按 var 声明的剩障碍与信物两个；出生区 builder 是按下标存取的数组 zoneBuilders，另扫它。
        Assert.True(builders.Length >= 2, $"样本口径：只认出 {builders.Length} 个 builder。");
        Assert.All(builders.Append("zoneBuilders"), name => Assert.DoesNotMatch($@"\bin {name}\b|\b{name}\.(Select|Where|First|Order|ToArray|ToList)", generator));
        Assert.Contains("zoneBuilders[i].ToImmutable()", generator, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"foreach\s*\([^)]*\bin\s+map\.(Obstacles|RelicCells|ChokePoints|BirthZones\[)|\bmap\.\w+(\.\w+)*\.(First|FirstOrDefault|Last|ElementAt|Take|Skip)\(", generator);

        foreach (string source in new[] { layout, generator, parameters })
        {
            Assert.DoesNotMatch(@"\b(double|float|decimal|Half|Single|Double|MathF|Math\.(Sqrt|Cbrt|Pow|Round|Floor|Ceiling|Truncate|Log|Log2|Log10|Exp|Sin|Cos|Tan|Atan|Atan2))\b", source);
            Assert.DoesNotMatch(@"\b\d+\.\d+[fdmFDM]?\b|\b\d+[fdmFDM]\b", source);
            Assert.DoesNotMatch(@"\b(DateTime|DateTimeOffset|Stopwatch|TimeProvider|Environment|Guid|Random\.Shared|new Random|RandomNumberGenerator|GetHashCode|HashCode)\b", source);
        }

        foreach (string path in layoutFiles.Append(Path.Combine(maps, "BoardMapGenerator.cs")).Append(Path.Combine(maps, "BoardMapParameters.cs")))
        {
            Assert.DoesNotContain("GameSeed", File.ReadAllText(path), StringComparison.Ordinal);
        }

        // 对局流程不见棋盘档的地图种子类型。
        string[] matchFiles = [.. Directory.EnumerateFiles(Path.Combine(FrontierFixtures.RepoRoot(), "src", "Siege.Core", "Match"), "*.cs", SearchOption.AllDirectories)];
        Assert.True(matchFiles.Length >= 10, $"样本口径：Match 目录只扫到 {matchFiles.Length} 个文件。");
        var token = new Regex(@"BoardMapGenerator|BoardMapLayout|BoardMapParameters|BoardMapId|GeneratedBoardMap");
        Assert.Empty(matchFiles.Where(path => token.IsMatch(File.ReadAllText(path))).Select(path => Path.GetFileName(path)));

        static string StripComments(string text) => Regex.Replace(text, @"//[^\n]*", string.Empty);
    }
}
