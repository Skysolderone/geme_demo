using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Core.Recruit;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// relic-redesign AC7 / RQ7（design.md D4，接线段 W1-5）：旧十类地图信物、静态信物格与快照加成删除后——快照只含默认基础值（展示 5 / 选取 3 / 类型槽 5 / 分阶段部署上限），
/// 先手只按名次，征募只按基础权重，匠人改造范围固定相邻；代码里不再有旧信物命名空间与类型。
/// 变异验证（W1 实跑，只跑 PieceRelics 命名空间）：MD-1 BeginTurn 给快照展示数 +1 → 「小回合快照只含默认基础值」三行全红；MD-2 在任一测试源码里重新声明 <c>record RelicCellSpec</c> → 「代码中不再有旧信物」红。
/// </summary>
public class 旧信物删除后的默认口径Tests
{
    /// <summary>普通（非逐字）字符串字面量。</summary>
    private static readonly Regex StringLiteral = new(@"""(?:[^""\\\n]|\\.)*""", RegexOptions.None, TimeSpan.FromSeconds(5));

    [Theory]
    [InlineData(2, 3)]
    [InlineData(5, 4)]
    [InlineData(8, 5)]
    public void 小回合快照只含默认基础值(int majorRound, int deployLimit)
    {
        // 对局流程在小回合开始时生成的快照逐项等于默认快照：展示 5、选取 3、类型槽 5、部署上限按分阶段基础值；名次与盘面（含挂着的信物）都不影响它。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(majorRound).Stones(TestMaps.P0, "C3", "D3");
        match.Board.AttachRelic(TestMaps.At("C3"), RelicToken.Spawned(0, RelicKind.Banner));
        match.Debug.Recalculate();

        match.BeginTurn();
        EffectSnapshot snapshot = match.CurrentSnapshot!;

        Assert.Equal((5, 3, 5, deployLimit), (snapshot.RevealCount, snapshot.FreePickCount, snapshot.TypeSlots, snapshot.DeployLimit));
        Assert.Equal(EffectSnapshot.Defaults(snapshot.Player, majorRound, snapshot.HeldTypeCount), snapshot);
        Assert.Equal(
            ["DeployLimit", "FreePickCount", "HeldTypeCount", "MajorRound", "OverflowTypeCount", "Player", "RevealCount", "TypeSlots"],
            typeof(EffectSnapshot).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void 先手值只按名次()
    {
        // 先锋先手修正删除：先手值 = 参赛人数 − 势力名次；明细里不再有修正字段。
        Assert.Equal(0, Siege.Core.Match.InitiativeOrder.ValueOf(4, 4));
        Assert.Equal(3, Siege.Core.Match.InitiativeOrder.ValueOf(4, 1));
        Assert.DoesNotContain(typeof(InitiativeEntry).GetProperties(), p => p.Name == "Bonus");
    }

    [Fact]
    public void 征募只按基础权重()
    {
        // 流派徽记调权删除：抽样表 = 基础权重 × 4（整数倍不改变概率），全部玩家、全部大回合相同。
        int[] table = RecruitWeights.SamplingTable(RecruitWeights.DefaultArtisanWeight, ContentSet.V2);
        Assert.Equal(RecruitWeights.Order.Select(t => RecruitWeights.BaseWeightOf(t) * RecruitWeights.SamplingScale), table);
    }

    [Fact]
    public void 匠人改造范围固定相邻()
    {
        // 工坊删除：搭桥 / 烧林的目标只在几何四邻；同行隔一格的深水不是合法目标。
        GameBoard board = TestMaps.Blank(TestMaps.Terrain(surfaces: [("F6", Surface.DeepWater), ("H6", Surface.DeepWater)]), size: 11);

        Assert.Contains(TerrainEdit.Bridge(TestMaps.At("F6")), TerrainEditRules.LegalTargets(board.Map, TestMaps.At("E6")));
        Assert.DoesNotContain(TerrainEdit.Bridge(TestMaps.At("H6")), TerrainEditRules.LegalTargets(board.Map, TestMaps.At("F7")));
        Assert.DoesNotContain(TerrainEdit.Bridge(TestMaps.At("G6")), TerrainEditRules.LegalTargets(board.Map, TestMaps.At("E6")));
    }

    [Fact]
    public void 代码中不再有旧信物()
    {
        // AC7：src 与 tests 的源码（去掉 // 注释与普通字符串字面量——退役字段名单、旧日志字段名等以字符串出现是合法的）里不再出现旧信物命名空间、账本、静态信物格、计分信物计数与旧快照加成的标识。
        // 反面命中：判据在一段手写样例里确实命中（防止正则写错导致恒绿）；口径下界：扫到的文件数 ≥ 300。
        string root = PresentationFixtures.RepoRoot();
        string[] patterns =
        [
            @"Siege\.Core\.Relics(?![A-Za-z0-9_])", @"(?<![A-Za-z0-9_])RelicLedger(?![A-Za-z0-9_])", @"\.RelicCells(?![A-Za-z0-9_])", @"(?<![A-Za-z0-9_])RelicCells\s*=",
            @"(?<![A-Za-z0-9_])ScoringRelicCounts(?![A-Za-z0-9_])", @"(?<![A-Za-z0-9_])RelicCellSpec(?![A-Za-z0-9_])", @"(?<![A-Za-z0-9_])RelicZone(?![A-Za-z0-9_])",
            @"(?<![A-Za-z0-9_])RelicGenerator(?![A-Za-z0-9_])", @"(?<![A-Za-z0-9_])RelicPublicState(?![A-Za-z0-9_])", @"(?<![A-Za-z0-9_])EmblemCounts(?![A-Za-z0-9_])",
            @"(?<![A-Za-z0-9_])WorkshopActive(?![A-Za-z0-9_])", @"(?<![A-Za-z0-9_])ReadInitiativeBonuses(?![A-Za-z0-9_])", @"(?<![A-Za-z0-9_])WithRelics(?![A-Za-z0-9_])",
            @"(?<![A-Za-z0-9_])CreateWithRelics(?![A-Za-z0-9_])", @"(?<![A-Za-z0-9_])StartedWithRelics(?![A-Za-z0-9_])", @"LinkModeFixtures\.RelicMap(?![A-Za-z0-9_])",
        ];
        string self = Path.GetFileName(typeof(旧信物删除后的默认口径Tests).Name);
        string[] files =
        [
            .. new[] { "src", "tests" }
                .SelectMany(d => Directory.GetFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}.godot{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && Path.GetFileNameWithoutExtension(f) != self),
        ];
        Assert.True(files.Length >= 300, $"只扫到 {files.Length} 个文件");

        var hits = new List<string>();
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            string code = StringLiteral.Replace(LifeShape.空区与封闭眼空间Tests.StripComments(File.ReadAllText(file)), "\"\"");
            hits.AddRange(patterns.Where(p => Regex.IsMatch(code, p)).Select(p => $"{Path.GetRelativePath(root, file)}: {p}"));
        }

        Assert.Empty(hits);
        Assert.Matches(patterns[0], "using Siege.Core.Relics;\nvar x = map.RelicCells;");
        Assert.Matches(patterns[2], "var x = map.RelicCells;");
        Assert.DoesNotMatch(patterns[10], StringLiteral.Replace("string[] retired = [\"WorkshopActive\"];", "\"\""));   // 字符串字面量确被去掉
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Siege.Core", "Relics")));
    }
}
