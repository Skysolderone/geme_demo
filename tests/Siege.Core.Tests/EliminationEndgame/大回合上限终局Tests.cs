using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 大回合上限终局（round-cap D1 / D2）</summary>
public class 大回合上限终局Tests
{
    private static readonly PlayerId[] Order = [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3];

    /// <summary>每名玩家自己角落里的 9 个格（9×9 合成图，围棋记法跳过 I）；按下标取，保证同一玩家不重复落同一格、彼此不相邻。</summary>
    private static readonly Dictionary<PlayerId, string[]> CornerCells = new()
    {
        [MatchFixtures.P0] = ["A1", "B1", "C1", "A2", "B2", "C2", "A3", "B3", "C3"],
        [MatchFixtures.P1] = ["G1", "H1", "J1", "G2", "H2", "J2", "G3", "H3", "J3"],
        [MatchFixtures.P2] = ["A7", "B7", "C7", "A8", "B8", "C8", "A9", "B9", "C9"],
        [MatchFixtures.P3] = ["G7", "H7", "J7", "G8", "H8", "J8", "G9", "H9", "J9"],
    };

    /// <summary>当前玩家在自己角落落 1 子（每人各自计数）。</summary>
    private static void PlayCornerStone(MatchFlow match, Dictionary<PlayerId, int> used)
    {
        PlayerId p = match.CurrentPlayer!.Value;
        int i = used.TryGetValue(p, out int n) ? n : 0;
        used[p] = i + 1;
        match.PlayTurn(CornerCells[p][i]);
    }

    [Fact]
    public void 第15大回合结束仍未终局()
    {
        // 设计文档 §12.3 条件 4：上限 15，第 15 大回合最后一名参赛玩家完成小回合后仍不满足条件 1–3 → 立即以「达大回合上限」终局，按势力排名。
        // 变异验证 M-R1：EndMajorRound 删除上限检查 → 全套红 14（本类 3 条 + 达上限时按同一规则排名 + 上限写入对局配置并以规则原因终局 + 9 条依赖跑局终止的既有 Sim 测试）。
        // 变异验证 M-R3：比较改用推进后的序号 `completed + 1 >= 上限` → 红 5（本测试：第 14 轮就终局；上限随存档往返；上限写入对局配置并以规则原因终局；默认排除调试局；领先者胜率回归）。
        // 变异验证 M-RC1（check）：EndMajorRound 在 Finish(MajorRoundLimit) 之后仍执行 `MajorRound = completed + 1` → 红 1（本测试：序号推进到 16；Result.MajorRound 仍为 15，只靠 match.MajorRound 断言红）。
        // 变异验证 M-R7：Publish 把上限写死为 0 → 红 5（本测试、上限随存档往返、插旗阶段可见上限、上限开局固定进行中不可改、上限进入存档且旧存档回填15）。
        MatchFlow match = MatchFixtures.Started().AtRound(14, Order);
        Assert.Equal(15, match.MaxMajorRounds);
        var used = new Dictionary<PlayerId, int>();

        // 第 14 大回合：4 人各落 1 子（Pass 计数为 0），结束后对局继续——上限是 15 不是 14
        for (int i = 0; i < 4; i++)
        {
            PlayCornerStone(match, used);
        }

        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(15, match.MajorRound);
        Assert.Null(match.Result);

        // 第 15 大回合：前 3 人落子后仍进行；最后一人落子后立即终局
        for (int i = 0; i < 3; i++)
        {
            PlayCornerStone(match, used);
            Assert.Equal(MatchPhase.InProgress, match.Phase);
        }

        PlayCornerStone(match, used);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.MajorRoundLimit, match.Result!.Reason);
        Assert.Equal(15, match.Result.MajorRound);
        Assert.Equal(15, match.MajorRound);   // 序号停在刚结束的那一轮，没有推进到 16
        Assert.Equal(4, match.Result.Standings.Length);
        Assert.Equal(15, match.Publish().MaxMajorRounds);
        Assert.Contains(match.Events, e => e.Kind == FlowEventKind.MatchEnded && e.MajorRound == 15 && e.Detail.Contains(nameof(EndReason.MajorRoundLimit)));
        Assert.DoesNotContain(match.Events, e => e.Kind == FlowEventKind.MajorRoundEnded && e.Detail.Contains("第 15 大回合结束"));   // 未生成第 16 轮顺序
        Assert.Throws<SiegeRuleException>(() => match.BeginTurn());
    }

    [Fact]
    public void 上限之前照常结束()
    {
        // 上限 15，第 9 大回合整轮 Pass → 第 9 大回合结束时以条件 2 终局，不等第 15 大回合。
        // 本 Scenario 守"上限不是固定轮数"：第 9 轮整轮 Pass 照常以条件 2 终局。
        // 变异验证 M-R16：CheckEndConditions 的条件 2 改为 `_passStreak >= active && MaxMajorRounds == 0`（有上限就只等上限）→ 红 5（本测试、同时满足条件2与上限、整轮Pass、两条流程回归 Pass 计数重比）。
        MatchFlow match = MatchFixtures.Started().AtRound(9, Order);
        for (int i = 0; i < 4; i++)
        {
            match.PassTurn();
        }

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.AllPassed, match.Result!.Reason);
        Assert.Equal(9, match.Result.MajorRound);
    }

    [Fact]
    public void 同时满足条件2与上限()
    {
        // D2：第 15 大回合恰好整轮 Pass → 原因记「整轮 Pass」，不是「达大回合上限」——条件 2 描述盘面事实，上限只是兜底。
        // 变异验证 M-R2（优先级反转）：CompleteTurn 在处理 `_pendingEnd` 之前先查「最后一位 && MajorRound >= 上限 → Finish(MajorRoundLimit)」→ 红 1（本测试）。
        MatchFlow match = MatchFixtures.Started().AtRound(15, Order);
        for (int i = 0; i < 4; i++)
        {
            match.PassTurn();
        }

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.AllPassed, match.Result!.Reason);
        Assert.Equal(15, match.Result.MajorRound);
    }

    [Fact]
    public void 上限为0不设上限()
    {
        // 上限 0 → 只受条件 1–3 约束：第 15、30、100 大回合结束时均不因上限终局（§18.2：上限是兜底不是固定轮数，0 保留原行为）。
        // 变异验证 M-R4：条件改为 `MaxMajorRounds >= 0`（0 也触发）→ 红 3（本测试、上限为0时硬停以失败局记录、TurnSequence 无固定轮数）。
        MatchFlow match = MatchFixtures.Started(options: MatchOptions.Immediate with { MaxMajorRounds = 0 });
        Assert.Equal(0, match.MaxMajorRounds);
        Assert.Equal(0, match.Publish().MaxMajorRounds);
        var used = new Dictionary<PlayerId, int>();

        foreach (int round in new[] { 15, 30, 100 })
        {
            match.AtRound(round, Order);
            for (int i = 0; i < 4; i++)
            {
                PlayCornerStone(match, used);
            }

            Assert.Equal(MatchPhase.InProgress, match.Phase);
            Assert.Equal(round + 1, match.MajorRound);
            Assert.Null(match.Result);
        }
    }

    [Fact]
    public void 上限检查只在大回合结束()
    {
        // D1：第 15 大回合中第 2 名参赛玩家刚完成小回合、仍有玩家未行动 → 对局继续；直到最后一名完成后才检查上限。
        // 变异验证 M-R5：CompleteTurn 每个小回合后都查 `MajorRound >= 上限` → 红 7（本测试：P0 完成后即终局；另红 第15大回合、上限随存档往返、同时满足条件2与上限、达上限时按同一规则排名、非法批次也记录、上限写入对局配置并以规则原因终局）。
        MatchFlow match = MatchFixtures.Started().AtRound(15, Order);
        var used = new Dictionary<PlayerId, int>();

        PlayCornerStone(match, used);   // P0
        PlayCornerStone(match, used);   // P1：第 2 名参赛玩家刚完成
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(MatchFixtures.P2, match.CurrentPlayer);
        Assert.Equal(15, match.MajorRound);
        Assert.Null(match.Result);

        PlayCornerStone(match, used);   // P2
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(MatchFixtures.P3, match.CurrentPlayer);

        PlayCornerStone(match, used);   // P3：最后一名 → 才检查上限
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.MajorRoundLimit, match.Result!.Reason);
    }

    [Fact]
    public void 上限随存档往返()
    {
        // 上限 15 的对局在第 7 大回合存档并恢复 → 恢复后公开视图可读到 15，且续跑到第 15 大回合结束时按上限终局（第 8–14 轮都不终局）。
        // 变异验证 M-R6：Serialize 不写 MaxMajorRounds → 红 2（本测试：值恰为回填的 15，只靠 MaxMajorRoundsBackfilled 断言红；上限进入存档且旧存档回填15 用非回填值 12 证伪）。
        // M-R1 / M-R3 / M-R5 / M-R7 同样使本测试红（见上）。
        MatchFlow match = MatchFixtures.Started().AtRound(7, Order);
        string json = match.Serialize();
        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, json);

        Assert.Equal(7, restored.MajorRound);
        Assert.Equal(15, restored.MaxMajorRounds);
        Assert.Equal(15, restored.Publish().MaxMajorRounds);
        Assert.False(restored.MaxMajorRoundsBackfilled);

        var used = new Dictionary<PlayerId, int>();
        int guard = 0;
        while (restored.Phase == MatchPhase.InProgress)
        {
            Assert.InRange(restored.MajorRound, 7, 15);
            PlayCornerStone(restored, used);
            Assert.True(++guard < 100, "对局没有在上限处结束。");
        }

        Assert.Equal(EndReason.MajorRoundLimit, restored.Result!.Reason);
        Assert.Equal(15, restored.Result.MajorRound);
        Assert.Equal(9 * 4, guard);   // 第 7–15 大回合各 4 个小回合
        Assert.Equal(15, restored.Publish().MaxMajorRounds);
    }
}
