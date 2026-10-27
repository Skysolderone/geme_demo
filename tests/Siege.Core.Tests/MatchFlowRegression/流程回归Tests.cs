using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.MatchFlowRegression;

/// <summary>implement 8.3 的流程回归用例集中未被 Scenario 覆盖的部分：最后一手抢先锋、人工接管、事件唯一发出点。</summary>
public class 流程回归Tests
{
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
