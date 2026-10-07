using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Visibility;

namespace Siege.Core.Tests.MapSelection;

/// <summary>
/// 规格：openspec/changes/builtin-board-maps/specs/map-selection —— Requirement: 开局选图界面（视图模型部分，tasks 4.1）。
/// 清单只有三张内置棋盘图与随机棋盘图；随机棋盘图有种子、人数（2–4）与棋盘数（范围随人数）。
/// 视图模型只产出"地图标识"；地图一律由调用方经 <see cref="MapCatalog.Resolve"/> 得到，成功后 <c>Accept</c>、失败则 <c>RollBack</c>。
/// </summary>
public class 选图视图模型Tests
{
    private const ulong InitialSeed = 424242UL;

    /// <summary>选中随机棋盘图（4 人、缺省 7 块、种子即初始种子）并接受。</summary>
    private static MapSelectModel BoardSelected(ulong initialSeed = InitialSeed)
    {
        var model = new MapSelectModel(initialSeed);
        Assert.True(model.Select(BoardIndex(model)));
        model.Accept();
        Assert.Equal($"board:{initialSeed}", model.CurrentId);
        return model;
    }

    private static int BoardIndex(MapSelectModel model) => model.Options.ToList().FindIndex(o => o.IsBoard);

    private static int IndexOf(MapSelectModel model, string builtinId) => model.Options.ToList().FindIndex(o => o.BuiltinId == builtinId);

    /// <summary>某张图的默认棋盘视图模型（尚未插旗的对局，与选图界面的预览同构）。</summary>
    private static DefaultBoardView ViewOf(MapData map)
    {
        MatchFlow match = MatchFlow.CreateUnvalidated(
            map, MatchFixtures.Seed, [new PlayerId(0), new PlayerId(1)], MatchFixtures.Relics(map), MatchOptions.Immediate with { ContentSet = ContentSet.V1 });
        return DefaultBoardView.From(match.PublicWorldOf());
    }

    [Fact]
    public void 缺省进入选图()
    {
        // 规格 Scenario「缺省进入选图」：预选项是 siege-4p-board-v1，列表另有 3 人与 2 人内置棋盘图和随机棋盘图。
        // 变异 M-D1（builtin-board-maps 段 D，实跑）：预选项改为随机棋盘图 → MapSelection 与各入口选图过滤下红 22（含本测试）；
        // 变异 M-D2（实跑）：清单改回目录的全部内置图 → 红 6（含本测试与「旧地图不在界面上」）。
        var model = new MapSelectModel(InitialSeed);

        Assert.Equal(4, model.Options.Count);
        Assert.Equal(["siege-4p-board-v1", "siege-3p-board-v1", "siege-2p-board-v1", null], model.Options.Select(o => o.BuiltinId));
        Assert.Equal([MapOptionKind.Builtin, MapOptionKind.Builtin, MapOptionKind.Builtin, MapOptionKind.Board], model.Options.Select(o => o.Kind));
        // 选项标题是目录登记的显示名（裁决 3）：视图模型不自带"标识 → 名字"对照表。
        Assert.Equal(MapCatalog.BuiltinBoards.Select(b => b.Title), model.Options.Where(o => o.Kind == MapOptionKind.Builtin).Select(o => o.Title));
        Assert.Equal(["四人棋盘图（7 块）", "三人棋盘图（6 块）", "双人棋盘图（5 块）"], MapCatalog.BuiltinBoards.Select(b => b.Title));
        Assert.Contains("随机", Assert.Single(model.Options, o => o.IsBoard).Title, StringComparison.Ordinal);

        Assert.Equal(0, model.SelectedIndex);
        Assert.Equal("siege-4p-board-v1", model.CurrentId);
        Assert.Equal(MapCatalog.DefaultId, model.CurrentId);
        Assert.False(model.IsBoardSelected);
        Assert.Equal(string.Empty, model.Notice);
        // 选中内置棋盘图时随机棋盘图的调节不可用；种子已取好，切到随机棋盘图时用它。
        Assert.False(model.CanIncreasePlayers || model.CanDecreasePlayers || model.CanIncreaseBoards || model.CanDecreaseBoards);
        Assert.Equal((InitialSeed, "424242", 4, 7), (model.MapSeed, model.SeedText, model.Players, model.BoardCount));

        // 背景预览：按当前标识解析出的就是 4 人内置棋盘图。
        MapData map = MapCatalog.Resolve(model.CurrentId);
        Assert.Equal(("siege-4p-board-v1", 4, 7), (map.Id, map.MaxPlayers, map.Boards.Length));
    }

    [Fact]
    public void 旧地图不在界面上()
    {
        // 规格 Scenario「旧地图不在界面上」：列表里没有 v5、2p / 3p 标准图、边疆图与边疆档随机图；按标识预选它们也不行（--map-select 下报错退出）。
        // 变异 M-D3（实跑）：TrySelectId 对目录里任一内置标识都接受 → 红 1（本测试）。
        // retire-legacy-maps 段 B：四张旧图与 gen: 已从目录删除，命令行请求报"已删除"（见 已删除地图明确报错Tests）。
        var model = new MapSelectModel(InitialSeed);
        string[] legacy = ["siege-4p-base-v5", "siege-2p-base-v1", "siege-3p-base-v1", "siege-frontier-v2"];
        Assert.All(legacy, id => Assert.DoesNotContain(id, MapCatalog.BuiltinIds));
        Assert.All(legacy, id => Assert.Contains(id, MapCatalog.RetiredIds));
        Assert.DoesNotContain(model.Options, o => o.BuiltinId is not null && legacy.Contains(o.BuiltinId));
        Assert.DoesNotContain(model.Options, o => o.Title.Contains("边疆", StringComparison.Ordinal));
        Assert.All(model.Options, o => Assert.Contains(o.Kind, new[] { MapOptionKind.Builtin, MapOptionKind.Board }));

        foreach (string bad in legacy.Concat(["gen:12345", "gen:12345:p7", "gen", "board", "board:abc", "board:1:n6", "board:1:n11", "board:1:p5",
                     "board:1:p3:n9", "board:1:p2:n6", "board:1:n9:x", "boards:1", "no-such-map", "maps/x.json", ""]))
        {
            Assert.False(model.TrySelectId(bad), bad);
            Assert.Equal("siege-4p-board-v1", model.CurrentId);
        }

        // 旧地图经共用解析同样报"已删除"（原为"仍可直接建局"）。
        Assert.All(legacy, id => Assert.Throws<RetiredMapException>(() => MapCatalog.Resolve(id)));
    }

    [Fact]
    public void 选中随机棋盘图_显示种子人数棋盘数与地图尺寸()
    {
        // 规格：选中随机棋盘图时 SHALL 显示当前地图种子、人数、棋盘数与地图尺寸。尺寸取自按标识解析出的地图的默认棋盘视图模型（MapPreviewInfo）。
        var model = new MapSelectModel(InitialSeed);
        Assert.True(model.TrySelectId("board:1"));
        Assert.True(model.IsBoardSelected);
        Assert.Equal((1UL, 4, 7, "1"), (model.MapSeed, model.Players, model.BoardCount, model.SeedText));

        MapData map = MapCatalog.Resolve(model.CurrentId);
        Assert.Equal((model.CurrentId, model.BoardCount, model.Players), (map.Id, map.Boards.Length, map.MaxPlayers));
        DefaultBoardView view = ViewOf(map);
        string info = MapPreviewInfo.Of(view);
        Assert.StartsWith($"{map.Width}×{map.Height}，5 个出生区，", info, StringComparison.Ordinal);
        Assert.Contains("棋盘 7 块（出生 5、公共 2）", info, StringComparison.Ordinal);
        int playable = view.Cells.Count(c => c.Terrain == Terrain.Playable);
        Assert.True(playable >= 250, $"样本口径：可落子只有 {playable} 格。");
        Assert.Contains($"可落子 {playable} 格", info, StringComparison.Ordinal);

        // 棋盘清单为空的地图：说明里没有棋盘一项，与引入棋盘图之前的文案相同。
        // retire-legacy-maps 段 B：原用 v5（13×13，4 区，105 格）；改用测试内构造的边疆档小图（20×20，6 区，360 格）。
        // 段 C：边疆档删除，改用 4 人合规棋盘档图清空棋盘清单（36×25，5 区，287 格；视图模型经 Unvalidated 入口建局，不受校验影响）。
        Assert.Equal("36×25，5 个出生区，可落子 287 格", MapPreviewInfo.Of(ViewOf(BoardMapFixtures.FourPlayerMap() with { Boards = [] })));
    }

    [Fact]
    public void 换一张()
    {
        // 规格 Scenario「换一张」：种子变化，界面显示新的完整标识；人数与棋盘数不变。变异 M-D4（实跑）：Reroll 忽略注入值 → 红 2（含本测试）。
        MapSelectModel model = BoardSelected();
        Assert.True(model.AdjustPlayers(-1));
        Assert.True(model.AdjustBoards(+1));
        Assert.Equal("board:424242:p3:n7", model.CurrentId);

        Assert.True(model.Reroll(987654321UL));
        Assert.Equal((987654321UL, "987654321", "board:987654321:p3:n7"), (model.MapSeed, model.SeedText, model.CurrentId));
        Assert.True(model.Reroll(987654321UL));                // 注入值恰与当前相同：仍然要"换"
        Assert.Equal("board:987654322:p3:n7", model.CurrentId);

        // 内置棋盘图选中时不起作用。
        Assert.True(model.Select(0));
        Assert.False(model.Reroll(1UL));
        Assert.Equal("siege-4p-board-v1", model.CurrentId);
    }

    [Fact]
    public void 输入种子复现()
    {
        // 规格 Scenario「输入种子复现」：两次启动（初始种子不同）输入同一个种子、人数与棋盘数相同 → 同一张图。
        MapSelectModel first = BoardSelected(1UL);
        MapSelectModel second = BoardSelected(2UL);
        Assert.NotEqual(first.CurrentId, second.CurrentId);

        Assert.True(first.SubmitSeed("12345"));
        Assert.True(second.SubmitSeed(" 12345 "));
        Assert.True(first.AdjustPlayers(-2));
        Assert.True(second.AdjustPlayers(-2));

        Assert.Equal("board:12345:p2", first.CurrentId);
        Assert.Equal(first.CurrentId, second.CurrentId);
        Assert.Equal(MapFile.ToJson(MapCatalog.Resolve(first.CurrentId)), MapFile.ToJson(MapCatalog.Resolve(second.CurrentId)));
        Assert.Equal("12345", second.SeedText);

        // 与前导零、首尾空白无关；与当前相同的种子不重搭。
        Assert.False(second.SubmitSeed("12345"));
        Assert.True(second.SubmitSeed("007"));
        Assert.Equal(("board:7:p2", "7"), (second.CurrentId, second.SeedText));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-1")]
    [InlineData("+7")]
    [InlineData("1.5")]
    [InlineData("12 34")]
    [InlineData("5:p3")]
    [InlineData("0x10")]
    [InlineData("１２３")]
    [InlineData("18446744073709551616")]
    public void 非法种子(string text)
    {
        // 规格 Scenario「非法种子」：提示种子须为非负整数，当前地图不变。变异 M-D5（实跑）：非法输入时把种子置 0 → 红 11（本测试全部数据行）。
        MapSelectModel model = BoardSelected();
        Assert.True(model.AdjustPlayers(-1));
        model.Accept();

        Assert.False(model.SubmitSeed(text));

        Assert.Contains("非负整数", model.Notice, StringComparison.Ordinal);
        Assert.Equal("board:424242:p3", model.CurrentId);
        Assert.Equal((InitialSeed, 3, 6), (model.MapSeed, model.Players, model.BoardCount));
        Assert.Equal(text, model.SeedText);   // 输入框保留用户敲的内容，方便改

        // 随后一次合法输入清掉提示。
        Assert.True(model.SubmitSeed("18446744073709551615"));
        Assert.Equal(string.Empty, model.Notice);
        Assert.Equal("board:18446744073709551615:p3", model.CurrentId);
    }

    [Fact]
    public void 调整人数()
    {
        // 规格 Scenario「调整人数」：随机棋盘图人数 4 → 3 → 棋盘数变为 6，按当前种子重新生成 3 人图，完整标识以 :p3 结尾。
        // 变异 M-D6（实跑）：改人数时保留原棋盘数（7 对 3 人也合法）→ 红 16（含本测试）；
        // 变异 M-D7（实跑）：标识的人数恒取 4（IdOf 不传人数）→ 红 18（含本测试）。
        MapSelectModel model = BoardSelected();
        Assert.True(model.AdjustBoards(+2));
        Assert.Equal("board:424242:n9", model.CurrentId);

        Assert.True(model.CanDecreasePlayers);
        Assert.False(model.CanIncreasePlayers);
        Assert.False(model.AdjustPlayers(+1));    // 4 已是上限：不夹取、不变

        Assert.True(model.AdjustPlayers(-1));
        Assert.Equal((3, 6, InitialSeed), (model.Players, model.BoardCount, model.MapSeed));
        Assert.EndsWith(":p3", model.CurrentId, StringComparison.Ordinal);
        Assert.Equal("board:424242:p3", model.CurrentId);
        MapData three = MapCatalog.Resolve(model.CurrentId);
        Assert.Equal((3, 6, 4), (three.MaxPlayers, three.Boards.Length, three.Boards.Count(b => b.Kind == BoardPlateKind.Birth)));

        Assert.True(model.AdjustPlayers(-1));
        Assert.Equal(("board:424242:p2", 2, 5), (model.CurrentId, model.Players, model.BoardCount));
        Assert.False(model.CanDecreasePlayers);
        Assert.False(model.AdjustPlayers(-1));
        Assert.Equal(2, model.Players);

        // 回到 4 人：棋盘数回到 7、:p 段省略。
        Assert.True(model.AdjustPlayers(+2));
        Assert.Equal(("board:424242", 4, 7), (model.CurrentId, model.Players, model.BoardCount));

        // 内置棋盘图选中时不起作用；切回随机棋盘图人数还在。
        Assert.True(model.AdjustPlayers(-1));
        Assert.True(model.Select(1));
        Assert.False(model.AdjustPlayers(-1));
        Assert.Equal("siege-3p-board-v1", model.CurrentId);
        Assert.True(model.Select(BoardIndex(model)));
        Assert.Equal("board:424242:p3", model.CurrentId);
    }

    [Theory]
    [InlineData(4, 7, 10)]
    [InlineData(3, 5, 8)]
    [InlineData(2, 4, 5)]
    public void 调整棋盘数_范围随人数(int players, int min, int max)
    {
        // 规格：棋盘数在该人数的合法范围内调整（4 人 7–10、3 人 5–8、2 人 4–5），到边界不再变化（不夹取）。
        // 变异 M-D8（实跑）：棋盘数下限恒取 4 人的 7 → 红 2（3 人与 2 人两组）。
        MapSelectModel model = BoardSelected();
        Assert.Equal(players != 4, model.AdjustPlayers(players - 4));
        int def = model.BoardCount;
        Assert.Equal(BoardMapParameters.DefaultBoardsFor(players), def);

        while (model.CanDecreaseBoards)
        {
            Assert.True(model.AdjustBoards(-1));
        }

        Assert.Equal(min, model.BoardCount);
        Assert.False(model.AdjustBoards(-1));
        Assert.Equal(min, model.BoardCount);
        while (model.CanIncreaseBoards)
        {
            Assert.True(model.AdjustBoards(+1));
        }

        Assert.Equal(max, model.BoardCount);
        Assert.False(model.AdjustBoards(+1));
        Assert.Equal(max != def, model.CurrentId.EndsWith($":n{max}", StringComparison.Ordinal));   // 2 人的上限 5 即缺省值，省略 :n 段
        MapData map = MapCatalog.Resolve(model.CurrentId);
        Assert.Equal((players, max), (map.MaxPlayers, map.Boards.Length));
    }

    [Fact]
    public void 调整棋盘数()
    {
        // 规格 Scenario「调整棋盘数」：4 人随机棋盘图棋盘数 7 → 9 → 按当前种子重新生成 9 块棋盘的图，完整标识以 :n9 结尾。
        // 变异 M-D9（实跑）：调整棋盘数只改数、报告"标识未变"（调用方因此不重新生成）→ 红 6（含本测试）。
        MapSelectModel model = BoardSelected();
        Assert.False(model.CanDecreaseBoards);   // 缺省 7 即下限
        Assert.True(model.AdjustBoards(+1));
        Assert.Equal("board:424242:n8", model.CurrentId);
        Assert.True(model.AdjustBoards(+1));

        Assert.Equal((9, InitialSeed, 4), (model.BoardCount, model.MapSeed, model.Players));
        Assert.EndsWith(":n9", model.CurrentId, StringComparison.Ordinal);
        MapData map = MapCatalog.Resolve(model.CurrentId);
        Assert.Equal((model.CurrentId, 9), (map.Id, map.Boards.Length));
        Assert.Equal(4, map.Boards.Count(b => b.Kind == BoardPlateKind.Public));

        // 调回缺省 7：省略 :n 段。
        Assert.True(model.AdjustBoards(-2));
        Assert.Equal("board:424242", model.CurrentId);
    }

    [Fact]
    public void 强制进入选图的自检选项_按标识预选随机棋盘图带上人数()
    {
        // 规格 Scenario「强制进入选图的自检选项」：--map-select --map=board:12345 → 预选随机棋盘图 board:12345。
        // 调研指出的旧缺陷：board:x:p3 预选后丢掉人数、CurrentId 变成另一张 4 人图。变异 M-D10（实跑）：TrySelectId 不带上人数 → 红 1（本测试）。
        var model = new MapSelectModel(InitialSeed);

        Assert.True(model.TrySelectId(" board:12345 "));
        Assert.True(model.IsBoardSelected);
        Assert.Equal(BoardIndex(model), model.SelectedIndex);
        Assert.Equal(("board:12345", 12345UL, "12345", 4, 7), (model.CurrentId, model.MapSeed, model.SeedText, model.Players, model.BoardCount));

        Assert.True(model.TrySelectId("board:6:p3"));
        Assert.Equal(("board:6:p3", 3, 6), (model.CurrentId, model.Players, model.BoardCount));
        Assert.True(model.TrySelectId("board:6:p2:n4"));
        Assert.Equal(("board:6:p2:n4", 2, 4), (model.CurrentId, model.Players, model.BoardCount));
        Assert.True(model.TrySelectId("board:42:p4:n7"));   // 规范化：缺省人数与缺省棋盘数都省略
        Assert.Equal(("board:42", 4, 7), (model.CurrentId, model.Players, model.BoardCount));

        // 内置棋盘图按内置名预选。
        Assert.True(model.TrySelectId("siege-2p-board-v1"));
        Assert.Equal((2, "siege-2p-board-v1"), (model.SelectedIndex, model.CurrentId));
        Assert.False(model.IsBoardSelected);
    }

    [Fact]
    public void 确认后开局()
    {
        // 规格 Scenario「确认后开局」：日志首部的地图标识取 MapData.Id；每一项各验一遍"界面显示的标识 == 解析出的地图的标识"，人数 = 地图人数上限。
        int options = new MapSelectModel(12345UL).Options.Count;
        Assert.Equal(4, options);
        int[] expectedPlayers = [4, 3, 2, 4];
        for (int i = 0; i < options; i++)
        {
            var model = new MapSelectModel(12345UL);
            if (model.Select(i))
            {
                model.Accept();
            }

            string shown = model.CurrentId;
            string confirmed = model.Confirm();

            Assert.Equal(shown, confirmed);
            MapData map = MapCatalog.Resolve(confirmed);
            Assert.Equal((confirmed, expectedPlayers[i]), (map.Id, map.MaxPlayers));
            Assert.True(model.IsConfirmed);
            Assert.Throws<InvalidOperationException>(() => model.Select(0));
            Assert.Throws<InvalidOperationException>(() => model.Reroll(1UL));
            Assert.Throws<InvalidOperationException>(() => model.SubmitSeed("1"));
            Assert.Throws<InvalidOperationException>(() => model.AdjustPlayers(-1));
            Assert.Throws<InvalidOperationException>(() => model.AdjustBoards(1));
            Assert.Throws<InvalidOperationException>(() => model.TrySelectId("board:1"));
        }

        // 还没被接受的候选（预览尚未搭成功）不会被带进对局：确认给出的是上一次接受的标识。
        MapSelectModel pending = BoardSelected();
        Assert.True(pending.Reroll(99UL));
        Assert.Equal("board:424242", pending.Confirm());
        Assert.Equal("board:424242", pending.CurrentId);
    }

    [Fact]
    public void 生成失败_回到上一张成功的图并给出提示()
    {
        // 解析 / 建预览失败时调用方 RollBack：回到上一次 Accept 的状态（含人数与棋盘数），面板显示原因。
        // 变异 M-D11（实跑）：RollBack 只置提示、不恢复状态 → 红 1（本测试）。
        MapSelectModel model = BoardSelected();
        Assert.True(model.Reroll(5UL));
        model.Accept();

        Assert.True(model.Reroll(6UL));
        Assert.True(model.AdjustPlayers(-1));
        model.RollBack("地图生成失败：尝试次数耗尽。");

        Assert.Equal(("board:5", 5UL, "5", 4, 7), (model.CurrentId, model.MapSeed, model.SeedText, model.Players, model.BoardCount));
        Assert.Equal("地图生成失败：尝试次数耗尽。", model.Notice);

        // 从内置棋盘图切到随机棋盘图时失败：回到内置棋盘图那一项。
        var fromBuiltin = new MapSelectModel(InitialSeed);
        Assert.True(fromBuiltin.Select(BoardIndex(fromBuiltin)));
        fromBuiltin.RollBack("失败");
        Assert.False(fromBuiltin.IsBoardSelected);
        Assert.Equal("siege-4p-board-v1", fromBuiltin.CurrentId);
    }

    [Fact]
    public void 选中内置棋盘图时_种子人数与棋盘数操作不起作用()
    {
        var model = new MapSelectModel(InitialSeed);
        Assert.True(model.Select(IndexOf(model, "siege-3p-board-v1")));
        model.Accept();

        Assert.False(model.Reroll(1UL));
        Assert.False(model.SubmitSeed("12345"));
        Assert.False(model.AdjustPlayers(-1));
        Assert.False(model.AdjustBoards(+1));
        Assert.False(model.CanIncreasePlayers || model.CanDecreasePlayers || model.CanIncreaseBoards || model.CanDecreaseBoards);
        Assert.Equal("siege-3p-board-v1", model.CurrentId);
        Assert.Equal((InitialSeed, 4, 7), (model.MapSeed, model.Players, model.BoardCount));

        // 选中已选中的那一项：标识没变，不必重搭预览；越界下标抛出。
        Assert.False(model.Select(IndexOf(model, "siege-3p-board-v1")));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Select(model.Options.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Select(-1));
    }

    [Fact]
    public void 时间戳折成便于人读写的种子_九位以内_相邻输入散开()
    {
        // 变异 MC-7：FriendlySeed 原样返回 → 本测试红（超过九位、相邻输入只差 1）。
        // 折叠函数在 Core 的标识唯一实现旁（三个入口共用：图形版裸 board、选图界面"换一张"、批量 / 终端版裸 board；retire-legacy-maps 段 B 起裸 gen 报"已删除"）。
        // 只钉性质、不钉黄金值：retire-legacy-maps 段 B 检查变异"第二个乘数末位 3 → 5"0 红——种子是打印给用户的完整标识的一部分，算式本身不影响复现。
        ulong[] raws = [0UL, 1UL, 2UL, 1789820032123456UL, 1789820032123457UL, ulong.MaxValue];
        ulong[] seeds = [.. raws.Select(BoardMapId.FriendlySeed)];   // retire-legacy-maps 段 B：由 GeneratedMapId 迁到 BoardMapId

        Assert.All(seeds, s => Assert.InRange(s, 0UL, 999_999_999UL));
        Assert.Equal(seeds.Length, seeds.Distinct().Count());
        Assert.Equal(seeds, raws.Select(BoardMapId.FriendlySeed));   // 纯函数
        Assert.Equal(1_000_000_000UL, BoardMapId.FriendlySeedLimit);
        long gap = Math.Abs((long)seeds[3] - (long)seeds[4]);
        Assert.True(gap > 1000, $"相邻时间戳折出的种子只差 {gap}。");
    }
}
