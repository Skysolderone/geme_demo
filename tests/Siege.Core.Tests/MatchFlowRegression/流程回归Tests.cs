using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.MatchFlowRegression;

/// <summary>implement 8.3 的流程回归用例集中未被 Scenario 覆盖的部分：出局改变人数、最后一手抢先锋、人工接管、事件唯一发出点。</summary>
public class 流程回归Tests
{
    [Fact]
    public void 出局改变参赛人数后Pass计数与新人数重比()
    {
        // 裁决记录 3：连续 Pass 计数达到「当前」参赛人数即触发；出局让人数从 4 变 3 后，3 次 Pass 就够。
        // 变异验证 M-R1：CheckEndConditions 用 _players.Length 代替 ActiveCount → 红 1（本测试：第 3 次 Pass 后仍在进行）。
        // 段 C 改摆法：出局只能由提子造成（Pass 不改变任何人的势力），原"P0 Pass 后 P3 因盘面与手牌皆空出局"已不成立。
        // 改为 P0 一个批次提光 P3 的 A1（计数清零），随后 P1、P2 与第 6 大回合首位各 Pass 一次：3 ≥ 3 → 整轮 Pass（终局大回合 5 → 6）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P3, "A1")
            .Stones(MatchFixtures.P0, "B1");

        match.PlayTurn("A2");   // P0：提光 P3 → P3 出局，参赛人数 3
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(MatchFixtures.P3).Status);
        Assert.Equal(0, match.PassStreak);
        match.PassTurn();   // P1
        match.PassTurn();   // P2 → 第 5 大回合结束（P3 被跳过）
        Assert.Equal(6, match.MajorRound);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        match.PassTurn();   // 第 6 大回合首位 → 3 ≥ 3
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.AllPassed, match.Result!.Reason);
        Assert.Equal(6, match.Result.MajorRound);
    }

    [Fact]
    public void 弃赛改变参赛人数后立即重比Pass计数()
    {
        // 前三人 Pass（计数 3 < 4），第四人在小回合边界弃赛 → 参赛人数变 3，计数 3 ≥ 3 立即触发整轮 Pass。
        // 变异验证 M-R2：Resign 后不调用 CheckEndConditions → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "E5");
        match.PassTurn();
        match.PassTurn();
        match.PassTurn();
        Assert.Equal(3, match.PassStreak);
        Assert.Equal(MatchPhase.InProgress, match.Phase);

        match.Resign(MatchFixtures.P3);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.AllPassed, match.Result!.Reason);
        Assert.Equal(MatchFixtures.P0, match.Result.Winners.Single());
    }

    [Fact]
    public void 最后一手抢到的先锋本大回合结束即生效()
    {
        // design.md D4 / 设计文档 §8.1：先手修正在大回合结束时单次拉取、不缓存——本大回合最后一手抢到的先锋立刻影响下一轮顺序。
        // 盘面：P0 三孤子 15、P1 两孤子 10、P2 一孤子 5、P3 空；P0–P2 Pass，P3 最后落 E5（先锋）→ P3 势力 5 与 P2 并列第 3，
        // 先手值 (4−3)+1 = 2 与 P1 的 (4−2)+0 = 2 同值，同值链第 1 条（修正）让 P3 排在 P1 之前。
        // 变异验证 M-R3：把 ReadInitiativeBonuses 移到大回合开始时读取并缓存 → 红 1（本测试：P3 修正为 0，顺序 P0>P1>P2>P3）。
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Vanguard())])
            .AtRound(5, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3])
            .Stones(MatchFixtures.P0, "B2", "E2", "H2")
            .Stones(MatchFixtures.P1, "E8", "H8")
            .Stones(MatchFixtures.P2, "B8");
        match.PassTurn();
        match.PassTurn();
        match.PassTurn();
        match.PlayTurn("E5");

        InitiativeReport report = match.InitiativeReports.Single();
        Assert.Equal(1, report.Of(MatchFixtures.P3).Bonus);
        Assert.Equal(3, report.Of(MatchFixtures.P3).Rank);
        Assert.Equal(2, report.Of(MatchFixtures.P3).Value);
        Assert.Equal(2, report.Of(MatchFixtures.P1).Value);
        Assert.Equal(new[] { MatchFixtures.P0, MatchFixtures.P3, MatchFixtures.P1, MatchFixtures.P2 }, report.NextOrder);
    }

    [Fact]
    public void 任意阶段可替换决策来源()
    {
        // 设计文档 §15.3 / implement 2.5：运行器在每个阶段开始时重新解析控制者；在整理手牌阶段替换后，征募与部署阶段由新控制者接管。
        // 变异验证 M-R4：RunTurn 在小回合开始时只解析一次控制者 → 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started();
        var runner = new MatchRunner(match);
        foreach (PlayerId p in MatchFixtures.All)
        {
            runner.SetController(p, new RandomTurnController(MatchFixtures.Seed.Stream($"ai-{p}")));
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
