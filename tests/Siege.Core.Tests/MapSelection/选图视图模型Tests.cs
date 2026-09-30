using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Visibility;

namespace Siege.Core.Tests.MapSelection;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/map-selection —— Requirement: 开局选图界面（tasks 3.1，视图模型部分）；
/// openspec/changes/board-map/specs/map-selection 改动了预选项（棋盘图）并新增棋盘图的种子 / 换一张 / 棋盘数。
/// 视图模型只产出"地图标识"；地图一律由调用方经 <see cref="MapCatalog.Resolve"/> 得到，成功后 <c>Accept</c>、失败则 <c>RollBack</c>。
/// </summary>
public class 选图视图模型Tests
{
    private const ulong InitialSeed = 424242UL;

    private static MapSelectModel RandomSelected(ulong initialSeed = InitialSeed)
    {
        var model = new MapSelectModel(initialSeed);
        Assert.True(model.Select(model.Options.Count - 1));
        model.Accept();
        return model;
    }

    /// <summary>选中第一张内置图（清单里棋盘图在最前，内置图从第 1 项起）。</summary>
    private static MapSelectModel BuiltinSelected(ulong initialSeed = InitialSeed)
    {
        var model = new MapSelectModel(initialSeed);
        Assert.True(model.Select(1));
        model.Accept();
        Assert.Equal(MapCatalog.BuiltinIds[0], model.CurrentId);
        return model;
    }

    /// <summary>某张图的默认棋盘视图模型（尚未插旗的对局，与选图界面的预览同构）。</summary>
    private static DefaultBoardView ViewOf(MapData map)
    {
        MatchFlow match = MatchFlow.CreateUnvalidated(
            map, MatchFixtures.Seed, [new PlayerId(0), new PlayerId(1)], MatchFixtures.Relics(map), MatchOptions.Immediate with { ContentSet = ContentSet.V1 });
        return DefaultBoardView.From(match.PublicWorldOf());
    }

    [Fact]
    public void 缺省进入选图_预选棋盘图且已取好种子_清单是棋盘图加目录内置表加随机图()
    {
        // 规格（board-map / map-selection）Scenario「缺省进入选图」：预选项是棋盘图，列表另有标准图、2 人图、3 人图、手工边疆图与随机图。
        // 变异 MC-1：清单改成视图模型里自带的字面量数组 → 守门「选图界面不自带地图清单」红；
        // 变异 M-C1（board-map 段 C，实跑）：构造函数的预选项改回目录缺省图（v5）→ MapSelection 过滤下红 6（含本测试）。
        var model = new MapSelectModel(InitialSeed);

        Assert.Equal(6, model.Options.Count);
        Assert.Equal([null, .. MapCatalog.BuiltinIds, null], model.Options.Select(o => o.BuiltinId));
        Assert.Equal(
            [MapOptionKind.Board, MapOptionKind.Builtin, MapOptionKind.Builtin, MapOptionKind.Builtin, MapOptionKind.Builtin, MapOptionKind.Random],
            model.Options.Select(o => o.Kind));
        // 选项标题是目录登记的显示名（裁决 3）：视图模型不自带"标识 → 名字"对照表。变异 MC-21：标题退回显示标识 → 本测试红。
        Assert.Equal(MapCatalog.BuiltinMaps.Select(m => m.Title), model.Options.Where(o => o.Kind == MapOptionKind.Builtin).Select(o => o.Title));
        Assert.Equal(MapCatalog.BuiltinIds, MapCatalog.BuiltinMaps.Select(m => m.Id));
        Assert.True(Assert.Single(model.Options, o => o.IsRandom).Title.Contains("随机", StringComparison.Ordinal));
        Assert.True(Assert.Single(model.Options, o => o.IsBoard).Title.Contains("棋盘", StringComparison.Ordinal));
        Assert.Equal(FourPlayerBaseMap.Id, model.Options[1].BuiltinId);
        Assert.Equal(TwoPlayerBaseMap.Id, model.Options[2].BuiltinId);
        Assert.Equal(ThreePlayerBaseMap.Id, model.Options[3].BuiltinId);
        Assert.Equal(FrontierMapV2.Id, model.Options[4].BuiltinId);

        // 预选棋盘图，种子即调用方注入的那个（"已取好一个地图种子"），棋盘数缺省 7、标识省略 :n 段。
        Assert.Equal(0, model.SelectedIndex);
        Assert.True(model.IsBoardSelected);
        Assert.False(model.IsRandomSelected);
        Assert.Equal(InitialSeed, model.MapSeed);
        Assert.Equal("424242", model.SeedText);
        Assert.Equal(7, model.BoardCount);
        Assert.Equal("board:424242", model.CurrentId);
        Assert.NotEqual(MapCatalog.DefaultId, model.CurrentId);
        Assert.Equal(string.Empty, model.Notice);

        // 批量 / 终端入口的缺省地图不受影响（design D9）。
        Assert.Equal(FourPlayerBaseMap.Id, MapCatalog.DefaultId);
    }

    [Fact]
    public void 选中棋盘图_显示种子棋盘数与地图尺寸()
    {
        // 规格：选中棋盘图时 SHALL 显示当前地图种子、棋盘数与地图尺寸。种子与棋盘数在视图模型上；
        // 尺寸取自按标识解析出的地图的默认棋盘视图模型（界面的一行说明由 MapPreviewInfo 给出，不读地图、不自行出图）。
        var model = new MapSelectModel(InitialSeed);
        Assert.True(model.TrySelectId("board:1"));
        Assert.Equal((1UL, 7, "1"), (model.MapSeed, model.BoardCount, model.SeedText));

        MapData map = MapCatalog.Resolve(model.CurrentId);
        Assert.Equal(model.CurrentId, map.Id);
        Assert.Equal(model.BoardCount, map.Boards.Length);
        DefaultBoardView view = ViewOf(map);
        string info = MapPreviewInfo.Of(view);

        Assert.InRange(map.Width, 20, 50);
        Assert.InRange(map.Height, 20, 50);
        Assert.StartsWith($"{map.Width}×{map.Height}，", info, StringComparison.Ordinal);
        Assert.Contains("棋盘 7 块（出生 5、公共 2）", info, StringComparison.Ordinal);
        int playable = view.Cells.Count(c => c.Terrain == Terrain.Playable);
        Assert.True(playable >= 250, $"样本口径：可落子只有 {playable} 格。");
        Assert.Contains($"可落子 {playable} 格", info, StringComparison.Ordinal);

        // 棋盘数不同，说明随之不同（样本取非缺省值）。
        Assert.True(model.AdjustBoards(+2));
        MapData nine = MapCatalog.Resolve(model.CurrentId);
        Assert.Contains("棋盘 9 块（出生 5、公共 4）", MapPreviewInfo.Of(ViewOf(nine)), StringComparison.Ordinal);

        // 棋盘清单为空的地图：说明里没有棋盘一项，与引入棋盘图之前的文案相同。
        Assert.Equal("13×13，4 个出生区，可落子 105 格", MapPreviewInfo.Of(ViewOf(FourPlayerBaseMap.Create())));
    }

    [Fact]
    public void 调整棋盘数_按当前种子重新生成_完整标识以n9结尾()
    {
        // 规格 Scenario「调整棋盘数」：棋盘数从 7 调到 9 → 按当前种子重新生成，完整标识以 :n9 结尾。
        // 变异 M-C2a（board-map 段 C，实跑）：标识的棋盘数恒取缺省值（不带 :n）→ MapSelection 过滤下红 4（含本测试）；
        // 变异 M-C2b（实跑）：调整棋盘数只改数、报告"标识未变"（调用方因此不重新生成）→ 红 3（含本测试）。
        var model = new MapSelectModel(InitialSeed);
        model.Accept();
        Assert.True(model.CanIncreaseBoards);
        Assert.False(model.CanDecreaseBoards);   // 缺省 7 即下限
        Assert.False(model.AdjustBoards(-1));
        Assert.Equal("board:424242", model.CurrentId);

        Assert.True(model.AdjustBoards(+1));
        Assert.Equal("board:424242:n8", model.CurrentId);
        Assert.True(model.AdjustBoards(+1));

        Assert.Equal(9, model.BoardCount);
        Assert.Equal(InitialSeed, model.MapSeed);   // 种子不变
        Assert.Equal("board:424242:n9", model.CurrentId);
        Assert.EndsWith(":n9", model.CurrentId, StringComparison.Ordinal);
        MapData map = MapCatalog.Resolve(model.CurrentId);
        Assert.Equal((model.CurrentId, 9), (map.Id, map.Boards.Length));
        Assert.Equal(4, map.Boards.Count(b => b.Kind == BoardPlateKind.Public));

        Assert.True(model.AdjustBoards(+1));
        Assert.Equal("board:424242:n10", model.CurrentId);
        Assert.False(model.CanIncreaseBoards);
        Assert.False(model.AdjustBoards(+1));
        Assert.Equal(BoardMapParameters.MaxBoards, model.BoardCount);

        // 调回缺省 7：省略 :n 段。
        Assert.True(model.AdjustBoards(-3));
        Assert.Equal("board:424242", model.CurrentId);
        Assert.Equal(BoardMapParameters.MinBoards, model.BoardCount);

        // 平台数是随机图（边疆档）的参数，选中棋盘图时不起作用；反之亦然。
        Assert.False(model.AdjustPlatforms(+1));
        Assert.False(model.CanIncreasePlatforms || model.CanDecreasePlatforms);
        Assert.True(model.AdjustBoards(+2));
        Assert.True(model.Select(model.Options.Count - 1));
        Assert.False(model.AdjustBoards(+1));
        Assert.False(model.CanIncreaseBoards || model.CanDecreaseBoards);
        Assert.Equal("gen:424242:s1", model.CurrentId);
        // 切回棋盘图，棋盘数还在。
        Assert.True(model.Select(0));
        Assert.Equal("board:424242:n9", model.CurrentId);
    }

    [Fact]
    public void 棋盘图_种子输入与换一张_按新种子生成()
    {
        // 规格：选中棋盘图时提供同样的种子输入与换一张。
        var model = new MapSelectModel(InitialSeed);
        model.Accept();
        Assert.True(model.AdjustBoards(+1));

        Assert.True(model.Reroll(987654321UL));
        Assert.Equal("board:987654321:n8", model.CurrentId);   // 换一张保持棋盘数
        Assert.Equal("987654321", model.SeedText);
        Assert.True(model.Reroll(987654321UL));                // 注入值恰与当前相同：仍然要"换"
        Assert.Equal("board:987654322:n8", model.CurrentId);

        Assert.True(model.SubmitSeed(" 12345 "));
        Assert.Equal("board:12345:n8", model.CurrentId);
        Assert.Equal(MapFile.ToJson(MapCatalog.Resolve("board:12345:n8")), MapFile.ToJson(MapCatalog.Resolve(model.CurrentId)));
        model.Accept();

        Assert.False(model.SubmitSeed("abc"));
        Assert.Contains("非负整数", model.Notice, StringComparison.Ordinal);
        Assert.Equal("board:12345:n8", model.CurrentId);
        Assert.Equal("abc", model.SeedText);

        // 生成失败回滚：回到上一张成功的棋盘图，棋盘数一并恢复。
        Assert.True(model.AdjustBoards(+2));
        model.RollBack("失败");
        Assert.Equal("board:12345:n8", model.CurrentId);
        Assert.Equal(8, model.BoardCount);
    }

    [Fact]
    public void 按标识预选棋盘图_完整标识可用_裸请求与写错的拒绝()
    {
        // 规格：--map-select 与 --map=board:12345 同给时，该标识只作为选图界面的预选项。
        var model = new MapSelectModel(InitialSeed);
        Assert.True(model.Select(1));

        Assert.True(model.TrySelectId(" board:12345 "));
        Assert.True(model.IsBoardSelected);
        Assert.Equal(0, model.SelectedIndex);
        Assert.Equal("board:12345", model.CurrentId);
        Assert.Equal((12345UL, "12345", 7), (model.MapSeed, model.SeedText, model.BoardCount));

        Assert.True(model.TrySelectId("board:42:n9"));
        Assert.Equal(("board:42:n9", 9), (model.CurrentId, model.BoardCount));
        Assert.True(model.TrySelectId("board:42:n7"));   // 规范化：缺省棋盘数省略
        Assert.Equal("board:42", model.CurrentId);

        foreach (string bad in new[] { "board", "board:abc", "board:1:n6", "board:1:n11", "board:1:p7", "board:1:n9:x", "boards:1" })
        {
            Assert.False(model.TrySelectId(bad), bad);
            Assert.Equal("board:42", model.CurrentId);
        }
    }

    [Fact]
    public void 选中随机图_显示注入的地图种子与缺省平台数_标识规范化()
    {
        MapSelectModel model = RandomSelected();

        Assert.True(model.IsRandomSelected);
        Assert.Equal(InitialSeed, model.MapSeed);
        Assert.Equal(MapGenParameters.DefaultPlatforms, model.PlatformCount);
        Assert.Equal("424242", model.SeedText);
        Assert.Equal("gen:424242:s1", model.CurrentId);   // 缺省平台数省略 :p6

        // 切回内置图再切回来，种子与平台数都还在。
        Assert.True(model.AdjustPlatforms(+1));
        Assert.True(model.Select(2));
        Assert.Equal(MapCatalog.BuiltinIds[1], model.CurrentId);   // 清单第 0 项是棋盘图，内置图下标顺延 1
        int random = model.Options.Count - 1;   // "随机图"恒在最后（small-maps 起内置图多了一张，不写死下标）
        Assert.True(model.Select(random));
        Assert.Equal("gen:424242:p7:s1", model.CurrentId);

        // 选中已选中的那一项：标识没变，不必重搭预览。
        Assert.False(model.Select(random));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Select(model.Options.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Select(-1));
    }

    [Fact]
    public void 换一张_种子取调用方注入的值_标识随之变化()
    {
        // 规格 Scenario「换一张」。变异 MC-3：Reroll 忽略注入值 → 本测试红。
        MapSelectModel model = RandomSelected();
        string before = model.CurrentId;

        Assert.True(model.Reroll(987654321UL));

        Assert.Equal(987654321UL, model.MapSeed);
        Assert.Equal("987654321", model.SeedText);
        Assert.Equal("gen:987654321:s1", model.CurrentId);
        Assert.NotEqual(before, model.CurrentId);

        // 注入值恰好与当前种子相同：仍然要"换"。
        model.Accept();
        Assert.True(model.Reroll(987654321UL));
        Assert.NotEqual("gen:987654321:s1", model.CurrentId);
    }

    [Fact]
    public void 输入种子复现_同种子同平台数得到同一标识与同一张地图()
    {
        // 规格 Scenario「输入种子复现」：两次启动（两个视图模型，初始种子不同）输入同一个种子、平台数相同。
        MapSelectModel first = RandomSelected(1UL);
        MapSelectModel second = RandomSelected(2UL);
        Assert.NotEqual(first.CurrentId, second.CurrentId);

        Assert.True(first.SubmitSeed("12345"));
        Assert.True(second.SubmitSeed(" 12345 "));
        Assert.True(first.AdjustPlatforms(+1));
        Assert.True(second.AdjustPlatforms(+1));

        Assert.Equal("gen:12345:p7:s1", first.CurrentId);
        Assert.Equal(first.CurrentId, second.CurrentId);
        Assert.Equal(MapFile.ToJson(MapCatalog.Resolve(first.CurrentId)), MapFile.ToJson(MapCatalog.Resolve(second.CurrentId)));
        Assert.Equal("12345", second.SeedText);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-1")]
    [InlineData("+7")]
    [InlineData("1.5")]
    [InlineData("12 34")]
    [InlineData("5:p7")]
    [InlineData("0x10")]
    [InlineData("１２３")]
    [InlineData("18446744073709551616")]
    public void 非法种子_提示须为非负整数_当前图不变(string text)
    {
        // 规格 Scenario「非法种子」。变异 MC-4：非法输入时把种子置 0 → 本测试红。
        MapSelectModel model = RandomSelected();
        Assert.True(model.AdjustPlatforms(+2));
        model.Accept();

        Assert.False(model.SubmitSeed(text));

        Assert.Contains("非负整数", model.Notice, StringComparison.Ordinal);
        Assert.Equal("gen:424242:p8:s1", model.CurrentId);
        Assert.Equal(InitialSeed, model.MapSeed);
        Assert.Equal(8, model.PlatformCount);
        Assert.Equal(text, model.SeedText);   // 输入框保留用户敲的内容，方便改

        // 随后一次合法输入清掉提示。
        Assert.True(model.SubmitSeed("18446744073709551615"));
        Assert.Equal(string.Empty, model.Notice);
        Assert.Equal("gen:18446744073709551615:p8:s1", model.CurrentId);
    }

    [Fact]
    public void 输入与当前相同的种子_不重搭_前导零按数值()
    {
        MapSelectModel model = RandomSelected();

        Assert.False(model.SubmitSeed("424242"));
        Assert.Equal(string.Empty, model.Notice);
        Assert.True(model.SubmitSeed("007"));
        Assert.Equal("gen:7:s1", model.CurrentId);
        Assert.Equal("7", model.SeedText);
    }

    [Fact]
    public void 平台数在5到8之间调整_到边界不再变化()
    {
        // 变异 MC-5：上界放宽到 9 → 本测试红（Format 会抛出）。
        MapSelectModel model = RandomSelected();
        Assert.True(model.CanDecreasePlatforms && model.CanIncreasePlatforms);

        Assert.True(model.AdjustPlatforms(-1));
        Assert.Equal("gen:424242:p5:s1", model.CurrentId);
        Assert.False(model.CanDecreasePlatforms);
        Assert.False(model.AdjustPlatforms(-1));
        Assert.Equal(5, model.PlatformCount);

        Assert.True(model.AdjustPlatforms(+1));
        Assert.Equal("gen:424242:s1", model.CurrentId);
        Assert.True(model.AdjustPlatforms(+1));
        Assert.True(model.AdjustPlatforms(+1));
        Assert.Equal("gen:424242:p8:s1", model.CurrentId);
        Assert.False(model.CanIncreasePlatforms);
        Assert.False(model.AdjustPlatforms(+1));
        Assert.Equal(MapGenParameters.MaxPlatforms, model.PlatformCount);
        Assert.Equal(MapGenParameters.MinPlatforms, 5);
    }

    [Fact]
    public void 选中内置图时_种子与平台数操作不起作用()
    {
        MapSelectModel model = BuiltinSelected();

        Assert.False(model.Reroll(1UL));
        Assert.False(model.SubmitSeed("12345"));
        Assert.False(model.AdjustPlatforms(+1));
        Assert.False(model.AdjustBoards(+1));
        Assert.False(model.CanIncreasePlatforms || model.CanDecreasePlatforms);
        Assert.False(model.CanIncreaseBoards || model.CanDecreaseBoards);

        Assert.Equal(MapCatalog.BuiltinIds[0], model.CurrentId);
        Assert.Equal(BoardMapParameters.DefaultBoards, model.BoardCount);
        Assert.Equal(InitialSeed, model.MapSeed);
        Assert.Equal(MapGenParameters.DefaultPlatforms, model.PlatformCount);
    }

    [Fact]
    public void 生成失败_回到上一张成功的图并给出提示()
    {
        // 地图由调用方解析；解析 / 建预览失败（如生成器耗尽尝试次数）时调用方 RollBack：回到上一次 Accept 的状态，面板显示原因。
        // 变异 MC-6：RollBack 只置提示、不恢复状态 → 本测试红。
        MapSelectModel model = RandomSelected();
        Assert.True(model.Reroll(5UL));
        model.Accept();

        Assert.True(model.Reroll(6UL));
        Assert.True(model.AdjustPlatforms(+1));
        model.RollBack("地图生成失败：尝试次数耗尽。");

        Assert.Equal("gen:5:s1", model.CurrentId);
        Assert.Equal(5UL, model.MapSeed);
        Assert.Equal("5", model.SeedText);
        Assert.Equal(MapGenParameters.DefaultPlatforms, model.PlatformCount);
        Assert.Equal("地图生成失败：尝试次数耗尽。", model.Notice);

        // 从内置图切到随机图时失败：回到内置图那一项。
        MapSelectModel fromBuiltin = BuiltinSelected();
        Assert.True(fromBuiltin.Select(fromBuiltin.Options.Count - 1));
        fromBuiltin.RollBack("失败");
        Assert.False(fromBuiltin.IsRandomSelected);
        Assert.Equal(MapCatalog.BuiltinIds[0], fromBuiltin.CurrentId);
    }

    [Fact]
    public void 确认开局_给出已接受的标识_与按它解析出的地图标识一致()
    {
        // 规格 Scenario「确认后开局」：日志首部的地图标识取 MapData.Id；每一项各验一遍"界面显示的标识 == 解析出的地图的标识"。
        int options = new MapSelectModel(12345UL).Options.Count;
        Assert.True(options >= 6, $"样本口径：只有 {options} 项。");
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
            Assert.Equal(confirmed, MapCatalog.Resolve(confirmed).Id);
            Assert.True(model.IsConfirmed);
            Assert.Throws<InvalidOperationException>(() => model.Select(0));
            Assert.Throws<InvalidOperationException>(() => model.Reroll(1UL));
            Assert.Throws<InvalidOperationException>(() => model.SubmitSeed("1"));
            Assert.Throws<InvalidOperationException>(() => model.AdjustPlatforms(1));
            Assert.Throws<InvalidOperationException>(() => model.AdjustBoards(1));
        }

        // 还没被接受的候选（预览尚未搭成功）不会被带进对局：确认给出的是上一次接受的标识。
        MapSelectModel pending = RandomSelected();
        Assert.True(pending.Reroll(99UL));
        Assert.Equal("gen:424242:s1", pending.Confirm());
        Assert.Equal("gen:424242:s1", pending.CurrentId);
    }

    [Fact]
    public void 随机图缺省开新地表_预选标识保持原值_换一张一律开()
    {
        // terrain-surfaces design D7：选图界面随机出的生成图带 :s1；按标识预选时照标识原样（不带 :s1 就是不开），
        // 其后"换一张"一律开；输入种子、调平台数保持当前开关。
        // 变异验证 M-S5e（实跑）：Reroll 不改开关 → 本测试红。
        var model = new MapSelectModel(InitialSeed);
        Assert.True(model.TrySelectId("gen:42"));
        Assert.Equal("gen:42", model.CurrentId);
        Assert.True(model.SubmitSeed("43"));
        Assert.Equal("gen:43", model.CurrentId);
        Assert.True(model.AdjustPlatforms(+1));   // 调平台数同样保持"关"
        Assert.Equal("gen:43:p7", model.CurrentId);
        Assert.True(model.AdjustPlatforms(-1));
        Assert.True(model.Reroll(77UL));
        Assert.Equal("gen:77:s1", model.CurrentId);
        Assert.True(model.AdjustPlatforms(+1));
        Assert.Equal("gen:77:p7:s1", model.CurrentId);
        Assert.Contains(Surface.Desert, MapCatalog.Resolve(model.CurrentId).TerrainData.Surfaces.Values);
    }

    [Fact]
    public void 按标识预选_内置图与生成图标识可用_其余拒绝()
    {
        // 供 --map-select --map=<标识>（仅截图 / 自检）预选一项。
        var model = new MapSelectModel(InitialSeed);

        Assert.True(model.TrySelectId(MapCatalog.BuiltinIds[1]));
        Assert.Equal(2, model.SelectedIndex);   // 清单第 0 项是棋盘图
        Assert.Equal(MapCatalog.BuiltinIds[1], model.CurrentId);
        Assert.True(model.TrySelectId(" gen:12345:p7 "));
        Assert.True(model.IsRandomSelected);
        Assert.Equal("gen:12345:p7", model.CurrentId);
        Assert.Equal("12345", model.SeedText);
        Assert.True(model.TrySelectId("gen:42:p6"));
        Assert.Equal("gen:42", model.CurrentId);

        foreach (string bad in new[] { "gen", "gen:abc", "gen:1:p99", "no-such-map", "maps/x.json", "" })
        {
            Assert.False(model.TrySelectId(bad), bad);
            Assert.Equal("gen:42", model.CurrentId);
        }
    }

    [Fact]
    public void 时间戳折成便于人读写的种子_九位以内_相邻输入散开()
    {
        // 变异 MC-7：FriendlySeed 原样返回 → 本测试红（超过九位、相邻输入只差 1）。
        // 折叠函数在 Core 的标识唯一实现旁（三个入口共用：图形版裸 gen、选图界面"换一张"、批量 / 终端版裸 gen）。
        ulong[] raws = [0UL, 1UL, 2UL, 1789820032123456UL, 1789820032123457UL, ulong.MaxValue];
        ulong[] seeds = [.. raws.Select(GeneratedMapId.FriendlySeed)];

        Assert.All(seeds, s => Assert.InRange(s, 0UL, 999_999_999UL));
        Assert.Equal(seeds.Length, seeds.Distinct().Count());
        Assert.Equal(seeds, raws.Select(GeneratedMapId.FriendlySeed));   // 纯函数
        Assert.Equal(1_000_000_000UL, GeneratedMapId.FriendlySeedLimit);
        long gap = Math.Abs((long)seeds[3] - (long)seeds[4]);
        Assert.True(gap > 1000, $"相邻时间戳折出的种子只差 {gap}。");
    }
}
