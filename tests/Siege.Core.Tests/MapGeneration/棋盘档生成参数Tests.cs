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
/// M-B12 边疆档域常量 AttemptDomain 末位 D → E → 红 1：边疆档生成图不变（retire-legacy-maps 段 B：边疆档随机源与该测试随 gen: 删除）；
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
    public void 校验闭环_尝试耗尽即报错并给出原因_不返回地图()
    {
        // 规格 map-generation「校验闭环」Scenario 尝试耗尽（对棋盘档同样生效）。retire-legacy-maps 段 B 检查补：原守门只在边疆档生成器上
        // （生成校验闭环Tests.尝试耗尽即报错并给出原因_不返回地图，随 gen: 删除），棋盘档生成器的 MapGenerationException 路径此后零覆盖。
        // 样本 200 张全部第 0 次就成（实测），自然失败触发不到耗尽路径；改用测试入口给定边长：5 块 5×5 + 2 块 7×7 = 223 格，
        // 低于 4 人目标带下限 300，每次尝试都在"规模作废"一关落空（与公开入口同一条循环）。
        // 变异（retire-legacy-maps 段 B 检查实跑全量）：GenerateDetailed 循环条件 `attempt < maxAttempts` 改成 `<=` → 只红本测试。
        var parameters = new BoardMapParameters { Players = 4, BoardCount = 7 };
        BoardSize[] births = [new(5, 5), new(5, 5), new(5, 5), new(5, 5), new(5, 5)];
        BoardSize[] publics = [new(7, 7), new(7, 7)];
        var ex = Assert.Throws<MapGenerationException>(() => BoardMapGenerator.GenerateDetailed(12345, parameters, 3, births, publics));
        Assert.Contains("地图 board:12345 在 3 次尝试内", ex.Message, StringComparison.Ordinal);
        Assert.Contains("第 2 次尝试规模作废：可落子格 223 落在 4 人目标带 300–800 之外", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("第 3 次尝试", ex.Message, StringComparison.Ordinal);   // 恰试 3 次（序号 0–2），不多试

        // 反面：同一入口给定落在目标带内的边长（资源布点 的那组）→ 返回通过校验的地图，不报错。
        MapData ok = BoardMapGenerator.GenerateDetailed(
            4242, new BoardMapParameters { BoardCount = 8 }, BoardMapGenerator.DefaultMaxAttempts,
            [new(6, 5), new(6, 5), new(6, 5), new(6, 5), new(6, 5)], [new(11, 11), new(9, 10), new(7, 8)]).Map;
        Assert.True(MapValidator.Validate(ok).IsValid);
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
    public void 棋盘档随机序列只由种子人数与序号决定且互不重复()
    {
        // 「棋盘档生成的随机序列只由地图种子、人数与尝试序号决定」。原名「棋盘档随机序列与边疆档互不相同」：
        // retire-legacy-maps 段 B 删除边疆档随机源（ForAttempt / ForSurfaces），与它们互不相交的一段随之删除；派生方式另由下一条的黄金值钉住。
        Assert.Equal(Take(MapRandom.ForBoardAttempt(12345, 4, 3), 64), Take(MapRandom.ForBoardAttempt(12345, 4, 3), 64));
        foreach (ulong seed in new ulong[] { 0, 1, 12345, ulong.MaxValue })
        {
            string[] board = [.. Enumerable.Range(0, 512).Select(k => string.Join(",", Take(MapRandom.ForBoardAttempt(seed, 4, k), 8)))];
            Assert.Equal(512, board.Distinct(StringComparer.Ordinal).Count());
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
        // 段 B 修正二 24×40、可落子 468、摘要 21C2717A…8840；board-isolated-gen 段 A 初版 38×44、可落子 457、摘要 14E954C4…F92D；
        // board-isolated-gen 终版 42×44、可落子 463、摘要 43663256…75CB（builtin-board-maps 段 A 出生棋盘改为同尺寸、可转 90° 后重定为现值；随机源头两个值不变）。
        // 红了不等于错——确认要改再更新此值，并在实施记录里写明。
        Assert.Equal(new[] { GoldenA, GoldenB }, Take(MapRandom.ForBoardAttempt(12345, 4, 0), 2));
        GeneratedBoardMap g = BoardMapGenerator.GenerateDetailed(12345);
        Assert.Equal(("board:12345", GoldenPlayable, GoldenWidth, GoldenHeight), (g.Map.Id, g.Map.PlayableCount, g.Map.Width, g.Map.Height));
        Assert.True(GoldenDigest == Digest(g.Map), $"board:12345 的导出文本摘要变了：现为 {Digest(g.Map)}。");
    }

    private const ulong GoldenA = 14574461902476217853UL;
    private const ulong GoldenB = 14705341623990255760UL;
    private const int GoldenPlayable = 539;
    private const int GoldenWidth = 46;
    private const int GoldenHeight = 37;
    private const string GoldenDigest = "1C82BE295057F1E0BF797F1C5B9FDCF14C5EE9D256FA5E58B730C8907167A329";

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
