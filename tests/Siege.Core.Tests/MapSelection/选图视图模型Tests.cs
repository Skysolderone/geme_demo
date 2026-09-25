using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Presentation.MapSelect;

namespace Siege.Core.Tests.MapSelection;

/// <summary>
/// 规格：openspec/changes/map-generator/specs/map-selection —— Requirement: 开局选图界面（tasks 3.1，视图模型部分）。
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

    [Fact]
    public void 缺省进入选图_清单是目录的内置表加随机图_缺省选中缺省地图()
    {
        // 变异 MC-1：清单改成视图模型里自带的字面量数组 → 守门「选图界面不自带地图清单」红；
        // 变异 MC-2：缺省选中第 2 项 → 本测试红。
        var model = new MapSelectModel(InitialSeed);

        Assert.Equal([.. MapCatalog.BuiltinIds, null], model.Options.Select(o => o.BuiltinId));
        // 选项标题是目录登记的显示名（裁决 3）：视图模型不自带"标识 → 名字"对照表。变异 MC-21：标题退回显示标识 → 本测试红。
        Assert.Equal(MapCatalog.BuiltinMaps.Select(m => m.Title), model.Options.Where(o => !o.IsRandom).Select(o => o.Title));
        Assert.Equal(MapCatalog.BuiltinIds, MapCatalog.BuiltinMaps.Select(m => m.Id));
        Assert.True(Assert.Single(model.Options, o => o.IsRandom).Title.Contains("随机", StringComparison.Ordinal));
        // 规格（small-maps）：标准图、2 人图、3 人图、手工边疆图、随机图。
        Assert.Equal(5, model.Options.Count);
        Assert.Equal(TwoPlayerBaseMap.Id, model.Options[1].BuiltinId);
        Assert.Equal(ThreePlayerBaseMap.Id, model.Options[2].BuiltinId);

        Assert.Equal(MapCatalog.DefaultId, model.CurrentId);
        Assert.Equal(MapCatalog.DefaultId, model.Options[model.SelectedIndex].BuiltinId);
        Assert.False(model.IsRandomSelected);
        Assert.Equal(string.Empty, model.Notice);
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
        Assert.True(model.Select(1));
        Assert.Equal(MapCatalog.BuiltinIds[1], model.CurrentId);
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
        var model = new MapSelectModel(InitialSeed);

        Assert.False(model.Reroll(1UL));
        Assert.False(model.SubmitSeed("12345"));
        Assert.False(model.AdjustPlatforms(+1));
        Assert.False(model.CanIncreasePlatforms || model.CanDecreasePlatforms);

        Assert.Equal(MapCatalog.DefaultId, model.CurrentId);
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
        var fromBuiltin = new MapSelectModel(InitialSeed);
        Assert.True(fromBuiltin.Select(fromBuiltin.Options.Count - 1));
        fromBuiltin.RollBack("失败");
        Assert.False(fromBuiltin.IsRandomSelected);
        Assert.Equal(MapCatalog.DefaultId, fromBuiltin.CurrentId);
    }

    [Fact]
    public void 确认开局_给出已接受的标识_与按它解析出的地图标识一致()
    {
        // 规格 Scenario「确认后开局」：日志首部的地图标识取 MapData.Id；每一项各验一遍"界面显示的标识 == 解析出的地图的标识"。
        int options = new MapSelectModel(12345UL).Options.Count;
        Assert.True(options >= 4, $"样本口径：只有 {options} 项。");
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
        Assert.Equal(1, model.SelectedIndex);
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
