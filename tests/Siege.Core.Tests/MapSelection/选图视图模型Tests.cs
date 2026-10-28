using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

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

    /// <summary>某张链接图的默认棋盘视图模型（尚未插旗的对局，与新局界面的预览同构）。retire-legacy-rules 1.7：去掉非链接图走旧模式的分支（唯一的调用点已删）。</summary>
    private static DefaultBoardView ViewOf(MapData map)
    {
        MatchFlow match = MatchFlow.CreateUnvalidated(
            map, TestMaps.Seed, [new PlayerId(0), new PlayerId(1), new PlayerId(2), new PlayerId(3)],
            MatchOptions.Immediate with { ContentSet = ContentSet.V1 });
        return DefaultBoardView.From(match.PublicWorldOf());
    }

    [Fact]
    public void 缺省进入新局界面_已取好种子并显示完整标识()
    {
        // 规格 Scenario「缺省进入新局界面」：已取好一个地图种子（由入口注入）并显示 link:<种子>；没有任何其他地图可选。
        var model = new MapSelectModel(InitialSeed);

        Assert.Equal(InitialSeed, model.MapSeed);
        Assert.Equal("424242", model.SeedText);
        Assert.Equal("link:424242:4", model.CurrentId);
        Assert.Equal(LinkMapId.Format(InitialSeed, 4), model.CurrentId);
        Assert.Equal(string.Empty, model.Notice);
        Assert.False(model.IsConfirmed);
        Assert.Equal(AiDifficulty.Standard, model.Difficulty);

        // 批量 / 终端入口的缺省地图由目录决定（Q4：link:1），与界面随机取的种子无关。
        Assert.Equal("link:1:4", MapCatalog.DefaultId);
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
        Assert.Equal("link:987654321:4", model.CurrentId);
        Assert.NotEqual(before, model.CurrentId);

        // 注入值恰好与当前种子相同：仍然要"换"。
        model.Accept();
        Assert.True(model.Reroll(987654321UL));
        Assert.Equal("link:987654322:4", model.CurrentId);
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
    [InlineData("link:5:4")]
    [InlineData("18446744073709551616")]
    public void 非法种子_提示须为非负整数_当前种子不变(string text)
    {
        // 规格 Scenario「非法种子」：输入 abc 并确认 → 提示种子须为非负整数，当前种子不变；输入框保留用户敲的内容。
        var model = new MapSelectModel(InitialSeed);
        model.Accept();

        Assert.False(model.SubmitSeed(text));

        Assert.Contains("非负整数", model.Notice, StringComparison.Ordinal);
        Assert.Equal("link:424242:4", model.CurrentId);
        Assert.Equal(InitialSeed, model.MapSeed);
        Assert.Equal(text, model.SeedText);

        // 随后一次合法输入清掉提示；上界 2^64 − 1 可用。
        Assert.True(model.SubmitSeed("18446744073709551615"));
        Assert.Equal(string.Empty, model.Notice);
        Assert.Equal("link:18446744073709551615:4", model.CurrentId);
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
        Assert.Equal("link:12345:4", first.CurrentId);
        Assert.Equal(first.CurrentId, second.CurrentId);
        Assert.Equal(MapFile.ToJson(MapCatalog.Resolve(first.CurrentId)), MapFile.ToJson(MapCatalog.Resolve(second.CurrentId)));
        Assert.Equal("12345", second.SeedText);

        Assert.False(first.SubmitSeed("12345"));
        Assert.Equal(string.Empty, first.Notice);
        Assert.True(first.SubmitSeed("007"));
        Assert.Equal("link:7:4", first.CurrentId);
        Assert.Equal("7", first.SeedText);
    }

    [Fact]
    public void 按标识预填_只认完整的链接图标识()
    {
        // 规格 Scenario「强制进入新局界面的自检选项」：--map-select --map=link:12345 同给时，该标识只作为预填种子。
        // 旧标识族（内置图、gen:、board:、文件路径）、裸 link 与写错的标识一律拒绝、状态不变——新局界面只有 link: 一种地图。
        var model = new MapSelectModel(InitialSeed);

        // map-expand-25 段 A2：标识改为 link:<种子>:<人数>，旧两段格式 link:<种子> 报错作废（裁决 Q4），归入拒绝一列。
        Assert.True(model.TrySelectId(" link:12345:4 "));
        Assert.Equal((12345UL, "12345", "link:12345:4"), (model.MapSeed, model.SeedText, model.CurrentId));
        Assert.True(model.TrySelectId("link:007:4"));
        Assert.Equal("link:7:4", model.CurrentId);   // 规范化

        foreach (string bad in new[] { "link", "link:12345", "link:abc", "link:1:n9", "siege-4p-base-v5", "siege-frontier-v2", "gen:12345", "board:12345", "no-such-map", "maps/x.json", "", null! })
        {
            Assert.False(model.TrySelectId(bad), bad ?? "<null>");
            Assert.Equal("link:7:4", model.CurrentId);
        }
    }

    [Fact]
    public void 人数选项_2到4人_标识带人数_缺省4人()
    {
        // map-expand-25 D1：新局界面加人数选项（2 / 3 / 4，缺省 4）；人数是标识的一部分——改人数返回"标识变了"、种子不变，
        // 换一张 / 输入种子保持人数，预填完整标识带上其中的人数；回退把种子与人数一起退回上一张成功的图；确认后不再接受。
        // 变异（实跑，只跑本类）：MB-7 RollBack 只退种子、不退人数 → 本测试红 1；MB-8 CurrentId 的人数写死 4 → 本测试红 1。
        var model = new MapSelectModel(InitialSeed);
        Assert.Equal([2, 3, 4], MapSelectModel.PlayerOptions);
        Assert.Equal((4, "link:424242:4"), (model.Players, model.CurrentId));
        model.Accept();

        Assert.True(model.SelectPlayers(2));
        Assert.Equal((2, InitialSeed, "link:424242:2"), (model.Players, model.MapSeed, model.CurrentId));
        Assert.False(model.SelectPlayers(2));   // 同一人数：标识没变
        Assert.Throws<ArgumentOutOfRangeException>(() => model.SelectPlayers(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.SelectPlayers(1));

        Assert.True(model.Reroll(77UL));
        Assert.Equal("link:77:2", model.CurrentId);
        Assert.True(model.SubmitSeed("12"));
        Assert.Equal("link:12:2", model.CurrentId);
        Assert.True(model.TrySelectId("link:9:3"));
        Assert.Equal((3, "link:9:3"), (model.Players, model.CurrentId));

        // 预览失败回退：种子与人数都回到上一次接受的 link:424242:4。
        model.RollBack("这张图没有生成出来。");
        Assert.Equal((4, "link:424242:4"), (model.Players, model.CurrentId));

        // 按所选人数解析出的地图人数即开局人数。
        Assert.True(model.SelectPlayers(3));
        model.Accept();
        string confirmed = model.Confirm();
        Assert.Equal("link:424242:3", confirmed);
        Assert.Equal(3, MapCatalog.Resolve(confirmed).MaxPlayers);
        Assert.Throws<InvalidOperationException>(() => model.SelectPlayers(2));
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

        Assert.Equal("link:5:4", model.CurrentId);
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
        Assert.Equal(4, map.BirthZones.Length);   // seat-gap-25：4 人浮空岛恰 4 个出生座位（map-expand-25 时为 10–12 块平台）
        Assert.Equal(map.Boards.Length, ViewOf(map).Boards.Length);   // 棋盘清单原样进视图（座位 + 中立平台）
        Assert.Equal(map.BirthZones.Length, ViewOf(map).Boards.Count(b => b.Kind == BoardPlateKind.Birth));
        Assert.True(model.IsConfirmed);
        Assert.Throws<InvalidOperationException>(() => model.Reroll(1UL));
        Assert.Throws<InvalidOperationException>(() => model.SubmitSeed("1"));
        Assert.Throws<InvalidOperationException>(() => model.TrySelectId("link:2:4"));
        Assert.Throws<InvalidOperationException>(() => model.SelectDifficulty(AiDifficulty.Easy));

        // 还没被接受的候选（预览尚未搭成功）不会被带进对局：确认给出的是上一次接受的标识。
        var pending = new MapSelectModel(InitialSeed);
        pending.Accept();
        Assert.True(pending.Reroll(99UL));
        Assert.Equal("link:424242:4", pending.Confirm());
        Assert.Equal("link:424242:4", pending.CurrentId);
    }

    [Fact]
    public void 预览说明读默认棋盘视图模型()
    {
        // 新局界面的一行说明由 MapPreviewInfo 给出。段 D-b（D18）：预览会话按座位过滤后只有 5 个占位——文案只提画布与 5 块出生棋盘（边长范围），
        // 不提信物格与可落子格。读默认棋盘视图模型，不读地图、不判规则。retire-legacy-rules 1.7（清单"删反面段"、P5）：旧地图（无占位、9×9 手写夹具）文案那一行删除——旧模式退役后不再有无占位的对局。
        MapData map = MapCatalog.Resolve("link:1:4");
        MatchFlow match = MatchFlow.CreateUnvalidated(
            map, TestMaps.Seed, [new PlayerId(0), new PlayerId(1), new PlayerId(2), new PlayerId(3)],
            MatchOptions.Immediate with { ContentSet = ContentSet.V1 });
        DefaultBoardView preview = DefaultBoardView.From(match.PublicWorldOf()).ForSeat(match.PublishFor(new PlayerId(0)), null);
        string info = MapPreviewInfo.Of(preview);
        Assert.StartsWith($"{map.Width}×{map.Height} 画布，{map.BirthZones.Length} 块出生棋盘（边长 ", info, StringComparison.Ordinal);   // map-expand-25：块数随平台数；seat-gap-25：只数出生座位
        Assert.DoesNotContain("信物", info, StringComparison.Ordinal);
        Assert.DoesNotContain("可落子", info, StringComparison.Ordinal);
        Assert.Empty(preview.Cells);
    }

    [Fact]
    public void 新局界面背景占位不带主题名_其余与插旗阶段相同()
    {
        // link-map-terrain 裁决 Q5（按建议）：新局界面的占位轮廓本期不显示主题，主题只在插旗阶段显示。背景占位由 MapPreviewInfo.Backdrop 给出：
        // 主题清空（ThemeName 为 null，渲染层不画主题名），编号、外接矩形、展开 / 本机、格与棋盘清单原样；LayoutKey 不含主题，因此与插旗阶段的视图同一指纹，
        // 刷新路径不会因此重搭成带主题的画面。图形版的接线（新局界面两处 Build 都经 Backdrop）由 选图界面守门Tests 的源码扫描守住。
        // 变异 MLT-G5（Backdrop 原样返回，实跑）→ 红 1（本测试）。
        MapData map = MapCatalog.Resolve("link:1:4");
        MatchFlow match = MatchFlow.CreateUnvalidated(
            map, TestMaps.Seed, [new PlayerId(0), new PlayerId(1), new PlayerId(2), new PlayerId(3)],
            MatchOptions.Immediate with { ContentSet = ContentSet.V1 });
        DefaultBoardView flags = DefaultBoardView.From(match.PublicWorldOf()).ForSeat(match.PublishFor(new PlayerId(0)), null);
        DefaultBoardView backdrop = MapPreviewInfo.Backdrop(flags);

        Assert.All(flags.Placeholders, p => Assert.NotNull(p.ThemeName));   // 样本口径：插旗阶段的占位确实带主题
        Assert.Equal(map.BirthZones.Length, backdrop.Placeholders.Length);   // map-expand-25：占位数 = 平台数；seat-gap-25：占位数 = 出生座位数（中立平台不给占位）
        Assert.All(backdrop.Placeholders, p => Assert.Null(p.ThemeName));
        Assert.Equal(
            flags.Placeholders.Select(p => (p.Zone, p.Origin, p.Width, p.Height, p.Expanded, p.Own)),
            backdrop.Placeholders.Select(p => (p.Zone, p.Origin, p.Width, p.Height, p.Expanded, p.Own)));
        Assert.Equal(flags.LayoutKey(), backdrop.LayoutKey());
        Assert.Equal(MapPreviewInfo.Of(flags), MapPreviewInfo.Of(backdrop));
    }

    [Fact]
    public void 时间戳折成便于人读写的种子_九位以内_相邻输入散开()
    {
        // 变异 MC-7：FriendlySeed 原样返回 → 本测试红（超过九位、相邻输入只差 1）。
        // 折叠函数在 Core 的标识唯一实现旁（三个入口共用：图形版裸 link、新局界面"换一张"、批量 / 终端版裸 link）。
        ulong[] raws = [0UL, 1UL, 2UL, 1789820032123456UL, 1789820032123457UL, ulong.MaxValue];
        // retire-legacy-maps 2.1：折叠函数自 GeneratedMapId 迁入 LinkMapId（原样搬运），断言不变。
        ulong[] seeds = [.. raws.Select(LinkMapId.FriendlySeed)];

        Assert.All(seeds, s => Assert.InRange(s, 0UL, 999_999_999UL));
        Assert.Equal(seeds.Length, seeds.Distinct().Count());
        Assert.Equal(seeds, raws.Select(LinkMapId.FriendlySeed));   // 纯函数
        Assert.Equal(1_000_000_000UL, LinkMapId.FriendlySeedLimit);
        long gap = Math.Abs((long)seeds[3] - (long)seeds[4]);
        Assert.True(gap > 1000, $"相邻时间戳折出的种子只差 {gap}。");
    }

    [Fact]
    public void 短种子折叠迁入后输出逐位相同()
    {
        // retire-legacy-maps 2.1：LinkMapId.FriendlySeed 自已删除的 GeneratedMapId.FriendlySeed 原样迁入。迁移当时对同一批输入与旧实现逐位比对（见 migration.md 第 2 组），
        // 旧实现删除后改为钉值：钉的是迁移当时旧实现的输出。
        ulong[] raws = [0UL, 1UL, 2UL, 12345UL, 1789820032123456UL, 1789820032123457UL, ulong.MaxValue];
        Assert.Equal(Golden, raws.Select(LinkMapId.FriendlySeed));
    }

    private static readonly ulong[] Golden = [0UL, 969246250UL, 774983618UL, 268246356UL, 102909540UL, 97086102UL, 142409162UL];
}
