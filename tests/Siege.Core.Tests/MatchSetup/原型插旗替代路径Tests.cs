using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Sim.Config;

// retire-legacy-maps：仓库根目录改取 TestPaths.RepoRoot()（原 FrontierFixtures.RepoRoot，随旧地图夹具删除而原样迁出）。
namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：match-setup —— Requirement: 原型插旗替代路径</summary>
public class 原型插旗替代路径Tests
{
    // retire-legacy-maps：种子选区 / 顺排黄金值 / 中立平台 / 冒险概率各用例的载体（v4、v5、边疆图、gen:）随旧地图删除，
    // 所守的 PrototypeZoneAssignment.Assign / FlagRisk / ZonePick 在第 3 组删除；这里只留不依赖旧地图的三条（链接模式的盲选由 盲选插旗Tests 守）。

    [Fact]
    public void 调试依次插旗()
    {
        // 设计文档 §4.1：原型可由 AI 或调试界面依次完成插旗，产出与正式插旗完全相同的对局状态（implement 1.4：逐字段一致）。
        // 变异验证 M-S8：PlantSequentially 不经 LockAll 而是另起一套顺序生成 → 红 1（本测试的存档逐字节比较）。
        // retire-legacy-rules 1.5：载体由 9×9 旧模式夹具换为 link:1（链接模式盲选，K = 0），选区与断言不变。
        MatchFlow simultaneous = LinkModeFixtures.Create();
        simultaneous.Flags.Plant(TestMaps.P2, 1);
        simultaneous.Flags.Plant(TestMaps.P0, 3);
        simultaneous.Flags.Plant(TestMaps.P3, 1);
        simultaneous.Flags.Plant(TestMaps.P1, 0);
        simultaneous.LockFlags();

        MatchFlow sequential = LinkModeFixtures.Create();
        sequential.PlantSequentially([(TestMaps.P0, 3), (TestMaps.P1, 0), (TestMaps.P2, 1), (TestMaps.P3, 1)]);

        Assert.Equal(MatchPhase.InProgress, sequential.Phase);
        Assert.Equal(1, sequential.MajorRound);
        Assert.Equal(simultaneous.Serialize(), sequential.Serialize());

        // 可正常开始第一大回合
        sequential.BeginTurn();
        Assert.Equal(TurnStage.OrganizeHand, sequential.Stage);
    }

    [Fact]
    public void 出生区锁定结果可持久化()
    {
        // 插旗完成后保存对局 → 每名玩家锁定的出生区编号被完整保存并可恢复。
        // 变异验证 M-S9：Serialize 不写 BirthZone → 红 1（本测试）。
        // retire-legacy-rules 1.5：载体由 9×9 旧模式夹具换为 link:1，恢复走公开入口 MatchFlow.Restore（链接图过校验），断言不变。
        // seat-gap-25 E4：座位互斥，原选区 2 / 2 / 0 / 3（P0 / P1 同区）改为互不相同的 2 / 1 / 0 / 3（同区会被改派，撞座位改派的持久化由下面的恢复同样覆盖）。
        MatchFlow match = LinkModeFixtures.Create();
        match.PlantSequentially([(TestMaps.P0, 2), (TestMaps.P1, 1), (TestMaps.P2, 0), (TestMaps.P3, 3)]);

        MatchFlow restored = MatchFlow.Restore(LinkModeFixtures.Map, match.Serialize());
        Assert.Equal(new int?[] { 2, 1, 0, 3 }, restored.PlayerStates.Select(s => s.BirthZone));
        Assert.Equal(new int?[] { 2, 1, 0, 3 }, TestMaps.All.Select(restored.Flags.FlagOf));
        Assert.True(restored.Flags.IsLocked);
        Assert.Equal(match.ActionOrder, restored.ActionOrder);
        Assert.Equal(match.LegalRangeFor(TestMaps.P1), restored.LegalRangeFor(TestMaps.P1));
    }

    [Fact]
    public void 三个入口的选区都走内核的唯一实现()
    {
        // tasks 2.3：批量 MatchSession、终端 PlayCommand、图形 MatchSession.ChooseZone 共用一份选区逻辑。
        // src/godot 不在 siege.sln 里，对行为测试完全隐身（testing.md）——把图形版改回手写顺排，6 区图上 AI 又会挤在前几个编号，
        // 而没有任何行为测试会红；只能做源码文本扫描。判据：入口层（Siege.Sim 全部 + src/godot/scripts 全部）不得直接调
        // PlantSequentially(、不得自己派生选区子流；三个调用点确实调了 PlantPrototype(。
        // 变异 M-C5：src/godot/scripts/MatchSession.cs 的 ChooseZone 改回 Match.PlantSequentially(…) → 本测试红 1（检查阶段补，此前 0 红）。
        string root = TestPaths.RepoRoot();
        string[] entryFiles =
        [
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "Siege.Sim"), "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "godot", "scripts"), "*.cs", SearchOption.AllDirectories),
        ];
        entryFiles = [.. entryFiles.Where(p =>
            !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];
        Assert.True(entryFiles.Length >= 20, $"样本口径：只扫到 {entryFiles.Length} 个入口层源文件。");

        // retire-legacy-maps 3.1：zone-pick / flag-risk 子流随旧选区删除，禁用记号换成盲选子流 flag-pick（只禁类型常量与带引号的字面量——图形版注释里会自然写到 flag-pick）。
        string[] forbidden = ["PlantSequentially(", "PrototypeZoneAssignment", "GameSeed.FlagPick", "\"flag-pick\""];   // 冒险子流只禁类型常量："flag-risk" 同时是 Siege.Sim 的命令行选项名（--flag-risk），不能按字面量禁
        string[] offenders = [.. entryFiles
            .Where(p => forbidden.Any(token => File.ReadAllText(p).Contains(token, StringComparison.Ordinal)))
            .Select(p => Path.GetRelativePath(root, p))];
        Assert.True(offenders.Length == 0, "入口层绕过了 MatchFlow.PlantPrototype：" + string.Join("；", offenders));

        foreach (string[] caller in new[]
        {
            new[] { "src", "Siege.Sim", "Running", "MatchSession.cs" },
            ["src", "Siege.Sim", "Play", "PlayCommand.cs"],
            ["src", "godot", "scripts", "MatchSession.cs"],
        })
        {
            Assert.Contains("PlantPrototype(", File.ReadAllText(Path.Combine([root, .. caller])), StringComparison.Ordinal);
        }

        // 反面：被禁的记号在它的归属处确实命中（扫描不是因为拼错了记号才干净）。
        string flow = File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Match", "MatchFlow.cs"));
        Assert.Contains("PlantSequentially(", flow, StringComparison.Ordinal);
        Assert.Contains("PrototypeZoneAssignment.AssignBlind(", flow, StringComparison.Ordinal);
        Assert.Contains("GameSeed.FlagPick", File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Match", "PrototypeZoneAssignment.cs")), StringComparison.Ordinal);   // 盲选子流只在唯一实现里派生
    }
}
