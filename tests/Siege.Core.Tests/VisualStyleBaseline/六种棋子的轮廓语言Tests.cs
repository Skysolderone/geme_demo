using Siege.Core.Board;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.VisualStyleBaseline;

/// <summary>规格：visual-style-baseline —— Requirement: 五种棋子的轮廓语言（artisan-terrain-edit 段 A：类型增至六种；more-pieces-relics 段 D：十种）</summary>
public class 六种棋子的轮廓语言Tests
{
    [Fact]
    public void 轮廓可辨()
    {
        // 设计文档 §20 / 裁决 6：普通子简洁圆润（圆头兵）、堡垒子塔楼体块（塔楼）、连珠子表现连接（双球连杆）、倍增子放射状（金字塔）、协同子多节点聚合（多瓣水晶）。
        // 数据层：每种棋子恰有一条标识，几何标识与轮廓语言各自两两不同。去色小尺寸缩略图可辨归阶段 B + 人工检查清单。
        // 变异验证 M-PS1：PieceStyleTable 中协同子的轮廓改为 PieceSilhouette.Pyramid → 本测试红 1。
        // more-pieces-relics 段 A 改写：类型增至十种，表必须覆盖全部枚举值（v2 局的新棋子查不到即抛）；段 A 按 D11 补四条占位标识，6 → 10。
        Assert.Equal(Enum.GetValues<PieceType>().Order(), PieceStyleTable.All.Select(s => s.Type).Order());
        Assert.Equal(10, PieceStyleTable.All.Select(s => s.Silhouette).Distinct().Count());
        Assert.Equal(10, PieceStyleTable.All.Select(s => s.Language).Distinct().Count());

        Assert.Equal((PieceSilhouette.RoundPawn, SilhouetteLanguage.Rounded), Style(PieceType.Basic));
        Assert.Equal((PieceSilhouette.Tower, SilhouetteLanguage.TowerMass), Style(PieceType.Fortress));
        Assert.Equal((PieceSilhouette.TwinOrbBar, SilhouetteLanguage.Connection), Style(PieceType.Line));
        Assert.Equal((PieceSilhouette.Pyramid, SilhouetteLanguage.Radial), Style(PieceType.Multiplier));
        Assert.Equal((PieceSilhouette.CrystalCluster, SilhouetteLanguage.MultiNode), Style(PieceType.Synergy));
        // artisan-terrain-edit Open Question 3（段 C 定稿）：匠人轮廓"工具或支架状"，几何在 LowPoly.ScaffoldParts——
        // 偏心斜立的木柄 + 柄顶横置宽槌头 + 一道斜撑；去色缩略图下靠"唯一不对称"与其余五种分开（见 art/artisan-v4/README）。
        Assert.Equal((PieceSilhouette.Scaffold, SilhouetteLanguage.Tooling), Style(PieceType.Artisan));

        // more-pieces-relics D11 / 裁决 ⑩：旗手竖杆方旗、铁链双环相扣、哨兵交叉双矛、界碑矮宽石碑；几何在 LowPoly（PennantParts / ChainLinkParts / CrossedSpearParts / SteleParts）。
        Assert.Equal((PieceSilhouette.Pennant, SilhouetteLanguage.Banner), Style(PieceType.Bannerman));
        Assert.Equal((PieceSilhouette.ChainLinks, SilhouetteLanguage.Interlock), Style(PieceType.Chain));
        Assert.Equal((PieceSilhouette.CrossedSpears, SilhouetteLanguage.Crossed), Style(PieceType.Sentry));
        Assert.Equal((PieceSilhouette.Stele, SilhouetteLanguage.Slab), Style(PieceType.Boundary));
    }

    [Fact]
    public void 映射覆盖全部类型()
    {
        // more-pieces-relics visual-style-baseline MODIFIED Scenario「映射覆盖全部类型」：对每一种棋子类型查询其轮廓 → 十种类型各得到一个轮廓，且两两不同。
        // 逐个枚举值经唯一映射 PieceStyleTable.For 查询（查不到即抛），不是只数表的行数；另要求轮廓枚举没有无人使用的值（每个轮廓恰对应一种类型）。
        // 骨架态即绿（段 A 已补四行占位标识）。变异验证 MD-V1：界碑子的轮廓改为 PieceSilhouette.Tower（与堡垒子同）→ 见 implement.md 段 D。
        PieceType[] all = Enum.GetValues<PieceType>();
        Assert.Equal(10, all.Length);

        PieceStyle[] styles = [.. all.Select(PieceStyleTable.For)];
        Assert.Equal(all, styles.Select(s => s.Type));
        Assert.Equal(all.Length, styles.Select(s => s.Silhouette).Distinct().Count());
        Assert.Equal(all.Length, styles.Select(s => s.Language).Distinct().Count());
        Assert.Equal(Enum.GetValues<PieceSilhouette>().Order(), styles.Select(s => s.Silhouette).Order());
        Assert.Equal(Enum.GetValues<SilhouetteLanguage>().Order(), styles.Select(s => s.Language).Order());
        Assert.Equal(all.Length, PieceStyleTable.All.Length);
    }

    [Theory]
    [InlineData(PieceType.Chain, PieceType.Line)]
    [InlineData(PieceType.Boundary, PieceType.Fortress)]
    [InlineData(PieceType.Bannerman, PieceType.Artisan)]
    public void 易混对可辨(PieceType newcomer, PieceType lookalike)
    {
        // more-pieces-relics visual-style-baseline Scenario「易混对可辨」：铁链 / 连珠、界碑 / 堡垒、旗手 / 匠人三对在去色缩略图中可区分。
        // 数据层只钉得住"映射给出不同的轮廓与不同的轮廓语言"；去色缩略图是否真的分得开是视觉判断，见 art/more-pieces/README.md 的人工检查清单
        // （铁链：相扣双环、中空，而非双球连杆；界碑：矮宽单板、顶部圆弧，而非高塔体块；旗手：竖直的杆 + 杆顶一侧的旗面，而非斜立的柄 + 横置槌头）。
        // 几何分支互不相同由下一条 Godot 源码扫描钉住。
        PieceStyle a = PieceStyleTable.For(newcomer);
        PieceStyle b = PieceStyleTable.For(lookalike);
        Assert.NotEqual(a.Silhouette, b.Silhouette);
        Assert.NotEqual(a.Language, b.Language);
    }

    [Fact]
    public void Godot几何对每种轮廓各有一个独立分支()
    {
        // 规格「"棋子类型 → 轮廓"由唯一映射给出，覆盖全部十种类型」在 Godot 侧的落点：LowPoly.Body 按轮廓分派几何。
        // src/godot 不在 siege.sln 里，IL / 反射守门对它隐身（testing.md「不在解决方案里的工程对守门测试完全隐身」），只能扫源码：
        // Body 的 switch 里每个 PieceSilhouette 值恰有一个 `PieceSilhouette.X =>` 分支，且十个分支体两两不同——
        // 段 A 的"四种新轮廓共用一枚方柱"（`Pennant or ChainLinks or CrossedSpears or Stele =>`）与"复制粘贴同一份几何"都会红。
        // 先红：段 D 改几何之前本测试红（只有 Stele 一个分支名，Pennant / ChainLinks / CrossedSpears 缺失）。
        string path = Path.Combine(PresentationFixtures.RepoRoot(), "src", "godot", "scripts", "LowPoly.cs");
        Assert.True(File.Exists(path), path);
        string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(text.Length >= 10_000, $"LowPoly.cs 只有 {text.Length} 字符");

        int start = text.IndexOf("private static Node3D[] Body(", StringComparison.Ordinal);
        Assert.True(start >= 0, "找不到 LowPoly.Body");
        int end = text.IndexOf("_ => throw", start, StringComparison.Ordinal);
        Assert.True(end > start, "找不到 Body 的兜底分支");
        string body = text[start..end];

        System.Text.RegularExpressions.MatchCollection arms = System.Text.RegularExpressions.Regex.Matches(
            body, @"PieceSilhouette\.(\w+)\s*=>", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5));
        string[] names = [.. arms.Select(m => m.Groups[1].Value)];
        Assert.Equal(Enum.GetNames<PieceSilhouette>().Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));

        // 分支体：从本分支的 `=>` 到下一个分支开头（去掉注释行与空白后比较）。
        string[] bodies = [.. arms.Select((m, i) => Normalize(body[(m.Index + m.Length)..(i + 1 < arms.Count ? arms[i + 1].Index : body.Length)]))];
        Assert.All(bodies, b => Assert.True(b.Length >= 10, $"分支体过短：{b}"));
        Assert.Equal(bodies.Length, bodies.Distinct(StringComparer.Ordinal).Count());

        static string Normalize(string armBody) => string.Concat(armBody.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal))
            .Select(l => string.Concat(l.Where(c => !char.IsWhiteSpace(c)))));
    }

    private static (PieceSilhouette, SilhouetteLanguage) Style(PieceType type)
    {
        PieceStyle style = PieceStyleTable.For(type);
        return (style.Silhouette, style.Language);
    }
}
