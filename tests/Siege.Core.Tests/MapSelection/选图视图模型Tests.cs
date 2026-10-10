using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Visibility;

namespace Siege.Core.Tests.MapSelection;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/map-selection —— Requirement: 开局选图界面（tasks 4.1，视图模型部分；实现裁决 D7）。
/// 新局界面只有 <c>link:&lt;地图种子&gt;</c> 一种地图：种子显示与输入、换一张、开始；旧地图清单与显示名对照表已删除。
/// 视图模型只产出"地图标识"；地图一律由调用方经 <see cref="MapCatalog.Resolve"/> 得到，成功后 <c>Accept</c>、失败则 <c>RollBack</c>。
/// </summary>
/// <remarks>
/// 变异验证（段 D-a 实跑）：M-D4 <c>SubmitSeed</c> 非法输入时把种子置 0 → 「非法种子_提示须为非负整数_当前种子不变」红；
/// M-D5 <c>Reroll</c> 忽略注入值（恒取当前种子 + 1）→ 「换一张_种子取调用方注入的值_标识随之变化」「生成失败_回到上一张成功的图并给出提示」红 2；
/// M-D6 <c>TrySelectId</c> 对解析不了的标识不返回 false 而是 <c>Move(0)</c> 并报成功 → 「按标识预填_只认完整的链接图标识」红 1。
/// </remarks>
public class 选图视图模型Tests
{
    private const ulong InitialSeed = 424242UL;

    /// <summary>某张图的默认棋盘视图模型（尚未插旗的对局，与新局界面的预览同构）。</summary>
    private static DefaultBoardView ViewOf(MapData map)
    {
        MatchFlow match = MatchFlow.CreateUnvalidated(
            map, MatchFixtures.Seed, [new PlayerId(0), new PlayerId(1), new PlayerId(2), new PlayerId(3)], MatchFixtures.Relics(map),
            MatchOptions.Immediate with { ContentSet = ContentSet.V1, Mode = MatchMode.Link });
        return DefaultBoardView.From(match.PublicWorldOf());
    }

    [Fact]
    public void 缺省进入新局界面_已取好种子并显示完整标识()
    {
        // 规格 Scenario「缺省进入新局界面」：已取好一个地图种子（由入口注入）并显示 link:<种子>；没有任何其他地图可选。
        var model = new MapSelectModel(InitialSeed);

        Assert.Equal(InitialSeed, model.MapSeed);
        Assert.Equal("424242", model.SeedText);
        Assert.Equal("link:424242", model.CurrentId);
        Assert.Equal(LinkMapId.Format(InitialSeed), model.CurrentId);
        Assert.Equal(string.Empty, model.Notice);
        Assert.False(model.IsConfirmed);
        Assert.Equal(AiDifficulty.Standard, model.Difficulty);

        // 批量 / 终端入口的缺省地图由目录决定（Q4：link:1），与界面随机取的种子无关。
        Assert.Equal("link:1", MapCatalog.DefaultId);
    }

    [Fact]
    public void 换一张_种子取调用方注入的值_标识随之变化()
    {
        // 规格 Scenario「换一张」：种子变化，界面显示新的完整标识（占位轮廓由调用方按新标识重搭预览）。
        var model = new MapSelectModel(InitialSeed);
        model.Accept();
        string before = model.CurrentId;

        Assert.True(model.Reroll(987654321UL));

        Assert.Equal(987654321UL, model.MapSeed);
        Assert.Equal("987654321", model.SeedText);
        Assert.Equal("link:987654321", model.CurrentId);
        Assert.NotEqual(before, model.CurrentId);

        // 注入值恰好与当前种子相同：仍然要"换"。
        model.Accept();
        Assert.True(model.Reroll(987654321UL));
        Assert.Equal("link:987654322", model.CurrentId);
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
    [InlineData("link:5")]
    [InlineData("18446744073709551616")]
    public void 非法种子_提示须为非负整数_当前种子不变(string text)
    {
        // 规格 Scenario「非法种子」：输入 abc 并确认 → 提示种子须为非负整数，当前种子不变；输入框保留用户敲的内容。
        var model = new MapSelectModel(InitialSeed);
        model.Accept();

        Assert.False(model.SubmitSeed(text));

        Assert.Contains("非负整数", model.Notice, StringComparison.Ordinal);
        Assert.Equal("link:424242", model.CurrentId);
        Assert.Equal(InitialSeed, model.MapSeed);
        Assert.Equal(text, model.SeedText);

        // 随后一次合法输入清掉提示；上界 2^64 − 1 可用。
        Assert.True(model.SubmitSeed("18446744073709551615"));
        Assert.Equal(string.Empty, model.Notice);
        Assert.Equal("link:18446744073709551615", model.CurrentId);
    }

    [Fact]
    public void 输入种子_同种子得到同一标识与同一张地图_前导零按数值()
    {
        // 两个视图模型（初始种子不同）输入同一个种子 → 同一标识、同一张图；与当前相同的输入不算变化；前导零按数值。
        var first = new MapSelectModel(1UL);
        var second = new MapSelectModel(2UL);
        Assert.NotEqual(first.CurrentId, second.CurrentId);

        Assert.True(first.SubmitSeed("12345"));
        Assert.True(second.SubmitSeed(" 12345 "));
        Assert.Equal("link:12345", first.CurrentId);
        Assert.Equal(first.CurrentId, second.CurrentId);
        Assert.Equal(MapFile.ToJson(MapCatalog.Resolve(first.CurrentId)), MapFile.ToJson(MapCatalog.Resolve(second.CurrentId)));
        Assert.Equal("12345", second.SeedText);

        Assert.False(first.SubmitSeed("12345"));
        Assert.Equal(string.Empty, first.Notice);
        Assert.True(first.SubmitSeed("007"));
        Assert.Equal("link:7", first.CurrentId);
        Assert.Equal("7", first.SeedText);
    }

    [Fact]
    public void 按标识预填_只认完整的链接图标识()
    {
        // 规格 Scenario「强制进入新局界面的自检选项」：--map-select --map=link:12345 同给时，该标识只作为预填种子。
        // 旧标识族（内置图、gen:、board:、文件路径）、裸 link 与写错的标识一律拒绝、状态不变——新局界面只有 link: 一种地图。
        var model = new MapSelectModel(InitialSeed);

        Assert.True(model.TrySelectId(" link:12345 "));
        Assert.Equal((12345UL, "12345", "link:12345"), (model.MapSeed, model.SeedText, model.CurrentId));
        Assert.True(model.TrySelectId("link:007"));
        Assert.Equal("link:7", model.CurrentId);   // 规范化

        foreach (string bad in new[] { "link", "link:abc", "link:1:n9", "siege-4p-base-v5", "siege-frontier-v2", "gen:12345", "board:12345", "no-such-map", "maps/x.json", "", null! })
        {
            Assert.False(model.TrySelectId(bad), bad ?? "<null>");
            Assert.Equal("link:7", model.CurrentId);
        }
    }

    [Fact]
    public void 生成失败_回到上一张成功的图并给出提示()
    {
        // 地图由调用方解析；解析 / 建预览失败时调用方 RollBack：回到上一次 Accept 的状态，面板显示原因。
        var model = new MapSelectModel(InitialSeed);
        Assert.True(model.Reroll(5UL));
        model.Accept();

        Assert.True(model.Reroll(6UL));
        model.RollBack("地图生成失败：尝试次数耗尽。");

        Assert.Equal("link:5", model.CurrentId);
        Assert.Equal(5UL, model.MapSeed);
        Assert.Equal("5", model.SeedText);
        Assert.Equal("地图生成失败：尝试次数耗尽。", model.Notice);
    }

    [Fact]
    public void 确认开局_给出已接受的标识_与按它解析出的地图标识一致()
    {
        // 规格 Scenario「确认后开局」：以当前种子建局，对局日志首部记录的地图标识（MapData.Id）与界面显示的一致。确认后不再接受操作。
        var model = new MapSelectModel(12345UL);
        model.Accept();
        string shown = model.CurrentId;

        string confirmed = model.Confirm();

        Assert.Equal(shown, confirmed);
        MapData map = MapCatalog.Resolve(confirmed);
        Assert.Equal(confirmed, map.Id);
        Assert.Equal(5, map.BirthZones.Length);   // 5 块出生棋盘的占位
        Assert.Equal(5, ViewOf(map).Boards.Length);
        Assert.True(model.IsConfirmed);
        Assert.Throws<InvalidOperationException>(() => model.Reroll(1UL));
        Assert.Throws<InvalidOperationException>(() => model.SubmitSeed("1"));
        Assert.Throws<InvalidOperationException>(() => model.TrySelectId("link:2"));
        Assert.Throws<InvalidOperationException>(() => model.SelectDifficulty(AiDifficulty.Easy));

        // 还没被接受的候选（预览尚未搭成功）不会被带进对局：确认给出的是上一次接受的标识。
        var pending = new MapSelectModel(InitialSeed);
        pending.Accept();
        Assert.True(pending.Reroll(99UL));
        Assert.Equal("link:424242", pending.Confirm());
        Assert.Equal("link:424242", pending.CurrentId);
    }

    [Fact]
    public void 预览说明读默认棋盘视图模型()
    {
        // 新局界面的一行说明由 MapPreviewInfo 给出：尺寸、出生区数、可落子格数、棋盘数——读默认棋盘视图模型，不读地图、不判规则。
        MapData map = MapCatalog.Resolve("link:1");
        string info = MapPreviewInfo.Of(ViewOf(map));
        Assert.StartsWith($"{map.Width}×{map.Height}，5 个出生区，可落子 {map.PlayableCount} 格", info, StringComparison.Ordinal);
        Assert.Contains("棋盘 5 块（出生 5、公共 0）", info, StringComparison.Ordinal);
    }

    [Fact]
    public void 时间戳折成便于人读写的种子_九位以内_相邻输入散开()
    {
        // 变异 MC-7：FriendlySeed 原样返回 → 本测试红（超过九位、相邻输入只差 1）。
        // 折叠函数在 Core 的标识唯一实现旁（三个入口共用：图形版裸 link、新局界面"换一张"、批量 / 终端版裸 link）。
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
