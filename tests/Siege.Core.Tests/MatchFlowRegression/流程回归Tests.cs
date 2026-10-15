using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.MatchFlowRegression;

/// <summary>implement 8.3 的流程回归用例集中未被 Scenario 覆盖的部分：最后一手抢先锋、人工接管、事件唯一发出点。</summary>
public class 流程回归Tests
{
    [Fact]
    public void 最后一手抢到的先锋本大回合结束即生效()
    {
        // design.md D4 / 设计文档 §8.1：先手修正在大回合结束时单次拉取、不缓存——本大回合最后一手抢到的先锋立刻影响下一轮顺序。
        // 盘面：P0 三孤子 15、P1 两孤子 10、P2 一孤子 5、P3 空；P0–P2 Pass，P3 最后落 G7（先锋）→ P3 势力 5 与 P2 并列第 3，
        // 先手值 (4−3)+1 = 2 与 P1 的 (4−2)+0 = 2 同值，同值链第 1 条（修正）让 P3 排在 P1 之前。
        // 变异验证 M-R3：把 ReadInitiativeBonuses 移到大回合开始时读取并缓存 → 红 1（本测试：P3 修正为 0，顺序 P0>P1>P2>P3）。
        // retire-legacy-rules 1.5：载体为平坦 8×8 链接档图（四人同锁棋盘 0；P3 落子 → 本战场不是整轮 Pass，不结算）。原 9×9 的七个孤子分布在第 2 / 5 / 8 行，
        // 8×8 棋盘放不下"三行都不贴边"，重排为棋盘 0 内圈两两曼哈顿距离 ≥ 3 的七格（每子仍是四邻独占的孤子，势力 15 / 10 / 5 / 5 不变）。
        MatchFlow match = LinkModeFixtures.PlainStarted(relics: [("G7", RelicFixtures.Vanguard())])
            .AtRound(5, [TestMaps.P0, TestMaps.P1, TestMaps.P2, TestMaps.P3])
            .Stones(TestMaps.P0, "D4", "G4", "J5")
            .Stones(TestMaps.P1, "D8", "J8")
            .Stones(TestMaps.P2, "E6");
        Assert.Equal([15, 10, 5, 0], TestMaps.All.Select(p => (int)match.Scoreboard.Latest!.Of(p).Total));   // 前提：与原局面相同的势力
        match.PassTurn();
        match.PassTurn();
        match.PassTurn();
        match.PlayTurn("G7");

        InitiativeReport report = match.InitiativeReports.Single();
        Assert.Equal(1, report.Of(TestMaps.P3).Bonus);
        Assert.Equal(3, report.Of(TestMaps.P3).Rank);
        Assert.Equal(2, report.Of(TestMaps.P3).Value);
        Assert.Equal(2, report.Of(TestMaps.P1).Value);
        Assert.Equal(new[] { TestMaps.P0, TestMaps.P3, TestMaps.P1, TestMaps.P2 }, report.NextOrder);
    }

    [Fact]
    public void 任意阶段可替换决策来源()
    {
        // 设计文档 §15.3 / implement 2.5：运行器在每个阶段开始时重新解析控制者；在整理手牌阶段替换后，征募与部署阶段由新控制者接管。
        // 变异验证 M-R4：RunTurn 在小回合开始时只解析一次控制者 → 红 1（本测试）。
        MatchFlow match = LinkModeFixtures.PlainStarted();
        var runner = new MatchRunner(match);
        foreach (PlayerId p in TestMaps.All)
        {
            runner.SetController(p, new RandomTurnController(TestMaps.Seed.Stream($"ai-{p}")));
        }

        PlayerId first = match.CurrentPlayer!.Value;
        var human = new ProbeController();
        var ai = new ProbeController { OnOrganize = () => runner.SetController(first, human) };
        runner.SetController(first, ai);

        runner.RunTurn();
        Assert.Equal(["Organize"], ai.Stages);
        Assert.Equal(["Recruit", "Deploy"], human.Stages);
        Assert.Equal(TurnStage.Idle, match.Stage);
        Assert.Same(human, runner.ControllerOf(first));
    }

    [Fact]
    public void 流程事件只有一个发出点()
    {
        // implement 2.2：全仓搜索确认流程事件只由 MatchFlow.Emit 发出。
        string root = RepoRoot();
        string[] files = Directory.GetFiles(Path.Combine(root, "src", "Siege.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).ToArray();
        var emitters = new List<string>();
        var constructors = new List<string>();
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            if (text.Contains("_events.Add(", StringComparison.Ordinal))
            {
                emitters.Add(Path.GetFileName(file));
            }

            if (text.Contains("new FlowEvent(", StringComparison.Ordinal))
            {
                constructors.Add(Path.GetFileName(file));
            }
        }

        Assert.Equal(["MatchFlow.cs"], emitters);
        Assert.Equal(["MatchFlow.cs"], constructors);
        Assert.Equal(1, File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Match", "MatchFlow.cs")).Split("new FlowEvent(").Length - 1);
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "siege.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根目录（siege.sln）。");
    }

    private sealed class ProbeController : ITurnController
    {
        internal List<string> Stages { get; } = [];

        internal Action? OnOrganize { get; init; }

        public void OrganizeHand(PlayerHandAccess hand, int overflow)
        {
            Stages.Add("Organize");
            OnOrganize?.Invoke();
        }

        public void Recruit(PlayerHandAccess hand, RecruitPanelView panel) => Stages.Add("Recruit");

        public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse) => Stages.Add("Deploy");

        public bool OnRejected(StagedBatch batch, BatchFailure failure) => false;
    }
}
