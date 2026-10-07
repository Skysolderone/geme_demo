using System.Text.RegularExpressions;
using Siege.Core.Ai;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Text;

namespace Siege.Core.Tests.MapSelection;

/// <summary>
/// 规格：expert-lookahead / simulation-harness「各入口的难度选项」—— Scenario: 图形版选专家（tasks 2.4）。
/// 选图界面的难度选择状态在零引擎依赖的 <see cref="MapSelectModel"/> 里（缺省标准，<c>--difficulty=</c> 预选）；
/// <c>src/godot</c> 不在解决方案里，接线部分只能源码扫描（testing.md「不在解决方案里的工程对守门测试完全隐身」），配样本口径与反面命中。
/// 运行时行为（<c>--difficulty=Expert</c> 后 AI 按专家决策、<c>--auto-demo</c> 为标准）见段 B 实施记录的 Godot 实测。
/// </summary>
public class 选图界面难度选择Tests
{
    private static readonly string Scripts = Path.Combine(TestMaps.RepoRoot(), "src", "godot", "scripts");

    [Fact]
    public void 难度选择缺省标准_四档按次序_可预选可切换()
    {
        var model = new MapSelectModel(1UL);
        Assert.Equal(AiDifficulty.Standard, model.Difficulty);
        Assert.Equal([AiDifficulty.Easy, AiDifficulty.Standard, AiDifficulty.Hard, AiDifficulty.Expert], MapSelectModel.DifficultyOptions);
        Assert.Equal(["简单", "标准", "高难", "专家"], MapSelectModel.DifficultyOptions.Select(Labels.Difficulty));

        model.SelectDifficulty(AiDifficulty.Expert);
        Assert.Equal(AiDifficulty.Expert, model.Difficulty);
        string id = model.CurrentId;
        Assert.Equal(id, model.Confirm());   // 难度不影响地图标识
        Assert.Equal(AiDifficulty.Expert, model.Difficulty);
        Assert.Throws<InvalidOperationException>(() => model.SelectDifficulty(AiDifficulty.Easy));   // 确认后不再接受操作
        Assert.Equal(AiDifficulty.Expert, model.Difficulty);

        var preset = new MapSelectModel(1UL, AiDifficulty.Hard);   // --difficulty=Hard 预选
        Assert.Equal(AiDifficulty.Hard, preset.Difficulty);
        Assert.Throws<ArgumentOutOfRangeException>(() => preset.SelectDifficulty((AiDifficulty)7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapSelectModel(1UL, (AiDifficulty)(-1)));
    }

    [Fact]
    public void 图形版难度参数经共用解析且在结算之前读取()
    {
        // --difficulty=<名称>：经 Core 的唯一解析（AiDifficultyNames.TryParse），非法即由 LaunchArgs 结算报错退出；图形版不得自带 Enum.Parse。
        string root = File.ReadAllText(Path.Combine(Scripts, "GameRoot.cs"));
        int read = Regex.Match(root, @"args\.Value<AiDifficulty>\(\s*""difficulty""").Index;
        int settle = root.IndexOf("args.EnsureRecognized();", StringComparison.Ordinal);
        Assert.True(read > 0 && settle > read, "--difficulty= 必须经 LaunchArgs 读取，且在结算之前。");
        Assert.Contains("AiDifficultyNames.TryParse(", root[read..settle], StringComparison.Ordinal);

        (string Path, string Text)[] files = [.. Directory.EnumerateFiles(Scripts, "*.cs").Order(StringComparer.Ordinal).Select(p => (Path.GetFileName(p), File.ReadAllText(p)))];
        Assert.True(files.Length >= 14, $"样本口径：只扫到 {files.Length} 个脚本。");
        var ownParse = new Regex(@"Enum\.(Try)?Parse\s*<\s*AiDifficulty|Enum\.(Try)?Parse\s*\(\s*typeof\s*\(\s*AiDifficulty");
        Assert.Empty(files.Where(f => ownParse.IsMatch(f.Text)).Select(f => f.Path));
        Assert.Matches(ownParse, "Enum.Parse<AiDifficulty>(text, true)");   // 反面命中：判据确实能抓到自带解析
    }

    [Fact]
    public void 建局用所选难度_只有预览固定标准()
    {
        // 真正开局的每一处 MatchSession.Create 都用所选难度（_difficulty）；只有选图预览（尚未插旗、AI 不行动）写死标准。
        // 变异 M-B4a（「开始」建局仍写死 AiDifficulty.Standard）→ 本测试红。
        string[] creates = [.. Directory.EnumerateFiles(Scripts, "*.cs")
            .SelectMany(f => File.ReadLines(f).Select(l => (File: Path.GetFileName(f), Line: l.Trim())))
            .Where(x => x.Line.Contains("MatchSession.Create(", StringComparison.Ordinal))
            .Select(x => $"{x.File}: {x.Line}")];
        Assert.True(creates.Length >= 5, $"样本口径：只扫到 {creates.Length} 处建局。");
        string preview = Assert.Single(creates, c => c.Contains("AiDifficulty.Standard", StringComparison.Ordinal));
        Assert.StartsWith("GameRoot.MapSelect.cs: MatchSession preview = MatchSession.Create(", preview, StringComparison.Ordinal);
        Assert.All(creates.Where(c => c != preview), c => Assert.Contains(", _difficulty, ", c, StringComparison.Ordinal));
    }

    [Fact]
    public void 无人值守固定标准难度()
    {
        // 自动演示、拾取自检与截图模式固定标准：所选难度只在非无人值守时取 --difficulty= 的值；无人值守时显式给出即报错（不静默忽略，也不改难度）。
        // 变异 M-B4b（去掉无人值守的拒绝，按 --difficulty= 取值）→ 本测试红。
        string root = File.ReadAllText(Path.Combine(Scripts, "GameRoot.cs"));
        Assert.Matches(new Regex(@"if \(difficulty is not null && Unattended\)\s*\{\s*throw new System\.FormatException\("), root);
        Assert.Contains("_difficulty = difficulty ?? AiDifficulty.Standard;", root, StringComparison.Ordinal);
        string stage = File.ReadAllText(Path.Combine(Scripts, "GameRoot.MapSelect.cs"));
        Assert.Contains("new MapSelectModel(NewMapSeed(), _difficulty)", stage, StringComparison.Ordinal);
        Assert.Contains("_difficulty = _select.Difficulty;", stage, StringComparison.Ordinal);
    }
}
