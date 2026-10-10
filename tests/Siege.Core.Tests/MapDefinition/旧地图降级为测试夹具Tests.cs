using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Board.Maps;

namespace Siege.Core.Tests.MapDefinition;

/// <summary>
/// 规格：openspec/changes/board-link-mode/specs/map-definition —— Requirement: 旧地图降级为测试夹具（实现裁决 D7：段 D 收口 <see cref="MapCatalog.Resolve"/>）。
/// 内置图继续存在于 Core、只能经 <see cref="MapCatalog.Legacy"/> 加载；玩家入口（图形版、终端版、批量跑局的地图选项）一律报错并说明只接受 <c>link:&lt;地图种子&gt;</c>。
/// 终端 / 批量入口的命令行行为由 <c>各入口按地图标识选图_链接模式Tests.旧标识报错</c> 守住；图形版不在解决方案里，这里对其源码扫描（testing.md「不在解决方案里的工程对守门测试完全隐身」）。
/// </summary>
/// <remarks>
/// 变异验证（段 D-a 实跑）：M-D7 <c>MapCatalog.Resolve</c> 对非 link: 标识转交 <c>Legacy.Resolve</c>（段 A 形态）→ 「玩家入口不可达」红，
/// 同时 <c>链接档地图标识与确定性Tests.目录解析link标识</c> 同红。
/// </remarks>
public class 旧地图降级为测试夹具Tests
{
    [Fact]
    public void 夹具仍可加载()
    {
        // Scenario：Core 测试经夹具解析加载 siege-4p-base-v5 → 地图数据与引入本 change 之前逐项相同（与生成器直出的 v5 逐字节相同）。
        MapData map = MapCatalog.Legacy.Resolve("siege-4p-base-v5");
        Assert.Equal("siege-4p-base-v5", map.Id);
        Assert.Equal(MapFile.ToJson(FourPlayerBaseMap.Create()), MapFile.ToJson(map));
        Assert.Equal("siege-4p-base-v5", MapCatalog.Legacy.DefaultId);
        Assert.Equal("siege-4p-base-v5", MapCatalog.Legacy.Resolve(null).Id);
        Assert.Equal(["siege-4p-base-v5", "siege-2p-base-v1", "siege-3p-base-v1", "siege-frontier-v2"], MapCatalog.BuiltinIds);
        Assert.All(MapCatalog.BuiltinIds, id => Assert.Equal(id, MapCatalog.Legacy.Resolve(id).Id));
    }

    [Fact]
    public void 玩家入口不可达()
    {
        // Scenario：以 --map=siege-4p-base-v5 启动任一入口 → 报错退出并说明只接受 link:<地图种子>。
        // 三个入口共用的解析（MapCatalog.Resolve）对全部内置图标识、地图文件路径与缺省以外的空白都报错；缺省（未给选项）是 link:1。
        foreach (string id in MapCatalog.BuiltinIds.Concat(["maps/siege-4p-base-v5.json", "no-such-map"]))
        {
            FormatException ex = Assert.Throws<FormatException>(() => MapCatalog.Resolve(id));
            Assert.Contains("只接受 link:<地图种子>", ex.Message, StringComparison.Ordinal);
            Assert.Contains(id, ex.Message, StringComparison.Ordinal);
            Assert.Contains("测试夹具", ex.Message, StringComparison.Ordinal);
        }

        Assert.Equal("link:1", MapCatalog.Resolve(null).Id);
        Assert.Equal("link:1", MapCatalog.Resolve("  ").Id);
        Assert.Equal("link:1", MapCatalog.DefaultId);

        // 图形版：--map= 只经 LaunchArgs 读取后交 MapCatalog.Resolve，解析失败（FormatException）即报错退出码 1；没有第二条绕过目录的建局路径，
        // 也不再识别裸 gen / 裸 board（那两族已不可达，取随机种子只剩裸 link 一处）。
        string scripts = Path.Combine(FrontierFixtures.RepoRoot(), "src", "godot", "scripts");
        string root = File.ReadAllText(Path.Combine(scripts, "GameRoot.cs"));
        Assert.Contains("MapData map = MapCatalog.Resolve(mapId);", root, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"catch \(System\.Exception ex\) when \(ex is [^)]*System\.FormatException"), root);
        Assert.Contains("GetTree().Quit(1);", root, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(root, @"LinkMapId\.IsBareRequest\("));
        Assert.DoesNotContain("GeneratedMapId.IsBareRequest(", root, StringComparison.Ordinal);
        Assert.DoesNotContain("BoardMapId.IsBareRequest(", root, StringComparison.Ordinal);
        string[] files = [.. Directory.GetFiles(scripts, "*.cs")];
        Assert.True(files.Length >= 14, $"样本口径：只扫到 {files.Length} 个脚本。");
        Assert.Equal(["GameRoot.MapSelect.cs", "GameRoot.cs"], files.Where(f => File.ReadAllText(f).Contains("MapCatalog.Resolve(", StringComparison.Ordinal)).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(files, f => File.ReadAllText(f).Contains("MapCatalog.Legacy", StringComparison.Ordinal));   // 图形版没有任何一处经夹具解析
    }
}
