using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 高部署上限下的候选剪枝</summary>
public class 高部署上限下的候选剪枝Tests
{
    /// <summary>
    /// 整批候选（ai-whole-batch）一次决策的预演次数上界：Pass 评价 1 + 强制贪心 (N + 1) + 留一法剪枝（至多 D 轮、每轮至多 D 次）
    /// + 配对枚举 C(N, 2) + 配对后继续贪心 N。<paramref name="deployLimit"/> = D。
    /// </summary>
    internal static int WholeBatchRehearsalBound(int n, int deployLimit) => 1 + (n + 1) + (deployLimit * deployLimit) + (n * (n - 1) / 2) + n;

    [Fact]
    public void 不穷举排列()
    {
        // 设计文档 §15.2 / 裁决 2：部署上限 8、60+ 合法空格 → 候选批次 ≤ M（标准 8），单点排序 ≤ N（12），
        // 预演次数 ≤ 空格数 × 类型数（单点）+ M × (N + 1)（组合），远小于 C(60, 8)。
        // 变异验证 M-A13：RankPoints 去掉 Take(N) → 红 1（本测试）。
        MatchFlow match = AiFixtures.Round5().Stones(AiFixtures.P1, "G7", "H8", "F9").Stones(AiFixtures.P2, "J5");
        match.SetDeployLimit(8);
        // ai-eye 段 D2（4.5）：末尾"落满 8 枚"依赖走法，权重与停手阈值写死为定值之前的缺省（默认阈值 80 下只落 7 枚）。
        HeuristicTurnController ai = HeuristicAi.Create(
            match, AiFixtures.P0, AiDifficulty.Standard, SimFixtures.PreCalibrationWeights,
            AiSearchConfig.Standard with { PassThreshold = SimFixtures.PreCalibrationPassThreshold });
        StagedBatch batch = match.OpenDeploy();
        Assert.Equal(8, batch.Context.DeployLimit);
        int empties = batch.Context.LegalRange.Count(c => batch.Board[c].IsPlayableEmpty);
        Assert.True(empties >= 60, $"合法空格 {empties}");

        int rehearsals = 0;
        RehearsalResult Counted()
        {
            rehearsals++;
            return match.Rehearse();
        }

        ai.Deploy(batch, Counted);

        // ai-whole-batch（规格 MODIFIED）：标准预设开着整批候选，候选数 ≤ M + 2，预演另加整批候选的上界（仍远小于 C(60, 8)）。
        // ai-bridge-candidate（规格 MODIFIED）：预设也开着搭桥候选，候选数 ≤ M + 3；本局面没有深水，搭桥候选不预演（留痕无起点），预演上界不变。
        Assert.Equal(AiSearchConfig.Standard.CandidatePointCount, ai.LastPointRanking.Length);
        Assert.InRange(ai.LastCandidates.Length, 1, AiSearchConfig.Standard.CandidateBatchCount + 3);
        Assert.Null(ai.LastBridgeSeed!.Seed);
        Assert.Equal(0, ai.LastBridgeSeed.Scored);
        int types = batch.Context.Stock.Count(kv => kv.Value > 0);
        Assert.True(
            rehearsals <= (empties * types) + (AiSearchConfig.Standard.CandidateBatchCount * (AiSearchConfig.Standard.CandidatePointCount + 1))
                + WholeBatchRehearsalBound(AiSearchConfig.Standard.CandidatePointCount, 8),
            $"预演 {rehearsals} 次");
        Assert.Equal(8, batch.Count);
        Assert.True(match.Confirm().Confirmed);
    }

    [Fact]
    public void 整批候选不计入候选批次上限()
    {
        // ai-whole-batch（规格 MODIFIED「高部署上限下的候选剪枝」：开启整批候选时另有至多 2 个整批候选，不计入上限 M）。
        // 构造局面同 整批候选Tests（G7 只剩 F7 / H7 两口气）：M = 1 时贪心只得 Pass，开启后另加整批候选 {F7, H7}，候选数 2 = M + 1 ≤ M + 2；关闭时 ≤ M。
        static HeuristicTurnController Run(int wholeBatch)
        {
            MatchFlow match = 整批候选Tests.Atari();
            (StagedBatch batch, SettlementDriver driver) = 活形硬约束Tests.Staging(match, PieceType.Basic, 2, "F7", "H7");
            var weights = new EvaluationWeights(PowerGain: 1, EnemyLoss: 0, Relic: 0, Safety: 0, Growth: 0, Initiative: 0, Supply: 0, Eye: 595, Threat: 0);
            AiSearchConfig config = AiSearchConfig.Standard with { CandidateBatchCount = 1, PassThreshold = 20, WholeBatch = wholeBatch };
            var ai = new HeuristicTurnController(AiFixtures.P0, match.Publish, match.Seed.Stream(HeuristicAi.StreamName(AiFixtures.P0)), AiDifficulty.Standard, weights, config);
            ai.Deploy(batch, () => driver.Rehearse(batch.Context, batch.Placements));
            return ai;
        }

        HeuristicTurnController off = Run(0);
        Assert.Single(off.LastCandidates);
        HeuristicTurnController on = Run(1);
        Assert.Equal(2, on.LastCandidates.Length);
        Assert.InRange(on.LastCandidates.Length, 1, on.Config.CandidateBatchCount + 2);
        Assert.Contains(on.LastCandidates, c => c.Key == on.LastWholeBatch!.Pruned!.Key);
    }

    [Fact]
    public void 整批候选与搭桥候选都不计入候选批次上限()
    {
        // ai-bridge-candidate（规格 MODIFIED「高部署上限下的候选剪枝」：开启搭桥候选时另有至多 1 个搭桥候选，与整批候选一样不计入上限 M）。
        // 构造局面同 搭桥候选Tests（搭桥单点在前 N 之外）：M = 1 时贪心只得 {G8}，开启后另加搭桥候选 {E6 匠人 + 搭桥 E5, G8}，候选数 2 ≤ M + 3；关闭时 ≤ M + 2（整批候选与贪心同键被去重）。
        static HeuristicTurnController Run(int editSeed) =>
            搭桥候选Tests.Deploy(AiDifficulty.Standard, AiSearchConfig.Standard with { CandidateBatchCount = 1, PassThreshold = 20, EditSeed = editSeed }).Ai;

        HeuristicTurnController off = Run(0);
        Assert.Single(off.LastCandidates);
        HeuristicTurnController on = Run(1);
        Assert.Equal(1, on.Config.WholeBatch);
        Assert.Equal(2, on.LastCandidates.Length);
        Assert.InRange(on.LastCandidates.Length, 1, on.Config.CandidateBatchCount + 3);
        Assert.Contains(on.LastCandidates, c => c.Key == on.LastBridgeSeed!.Candidate!.Key);
    }

    [Fact]
    public void 剪枝保留高价值落点()
    {
        // 单点评价第 1 名的落点（提子 + 抢信物的 G6）进入前 N，并出现在最终批次里；候选批次的每个落点都来自前 N。
        // 变异验证 M-A15：RankPoints 改为 OrderBy 升序 → 红 4（本测试、不穷举排列、必须找到的妙手、并列确定性打破）。
        // retire-legacy-rules 1.4（清单 P2）：给 P0 在远离妙手的盘角补一块 2×2，使其居本战场首位（链接规则下落后者的停手阈值取 0）。
        MatchFlow match = 启发式评价维度Tests.CaptureRelicPosition().AsLeader(AiFixtures.P0, "J3", "K3", "J4", "K4");
        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0);
        StagedBatch batch = match.OpenDeploy();
        ai.Deploy(batch, match.Rehearse);

        Coord f5 = TestMaps.At("G6");
        Assert.Equal(f5, ai.LastPointRanking[0].Coord);
        Assert.Contains(batch.Placements, p => p.Coord == f5);
        var topN = ai.LastPointRanking.Select(p => p.Coord).ToHashSet();
        Assert.All(ai.LastCandidates, c => Assert.All(c.Placements, p => Assert.Contains(p.Coord, topN)));
        Assert.True(ai.LastPointRanking[0].Total >= ai.LastPointRanking[^1].Total);
    }

    [Fact]
    public void 种子扰动由种子驱动()
    {
        // implement 3.3：同种子 + 同局面 → 候选集合（键与总分）完全一致；扰动只消费 AI 自己的子流。
        // 变异验证 M-A16：Perturb 用 new Random() 取 j → 红 4（本测试、同局面同决策、估计不得使用真实值、高难度不越权）。
        static HeuristicTurnController Run(out MatchFlow match)
        {
            // retire-legacy-rules 1.4（清单 P2）：P0 另在两个盘角各补一子，使其居本战场首位（链接规则下落后者的停手阈值取 0）。
            match = AiFixtures.Round5().Stones(AiFixtures.P1, "G7", "H8").Stones(AiFixtures.P0, "E6").AsLeader(AiFixtures.P0, "C3", "K3");
            match.SetDeployLimit(4);
            HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Hard);
            ai.Deploy(match.OpenDeploy(), match.Rehearse);
            return ai;
        }

        HeuristicTurnController a = Run(out MatchFlow ma);
        HeuristicTurnController b = Run(out MatchFlow mb);
        Assert.Equal(ma.Seed, mb.Seed);
        Assert.Equal(a.LastCandidates.Select(c => $"{c.Key}={c.Total}"), b.LastCandidates.Select(c => $"{c.Key}={c.Total}"));
        Assert.Equal(a.LastChoice!.Key, b.LastChoice!.Key);
        Assert.True(a.LastCandidates.Length > 1, "高难 M=32 在开放局面上应产生多条不同候选，否则扰动没有生效");
        Assert.Equal(0, ma.Hands.RecruitStreamConsumed - mb.Hands.RecruitStreamConsumed);
    }

    /// <summary>
    /// implement 3.4「必须找到的妙手」回归集（标准难度剪枝参数、第 5 大回合部署上限 4——growth-pass-1 分阶段基础值，原 3、只有普通子）。
    /// 局面 a（提子 + 抢信物）见 <see cref="启发式评价维度Tests.CaptureRelicPosition"/>。retire-legacy-rules 1.4：三个局面都在平坦 8×8 链接载体棋盘 0 上（原 9×9 平移 (+1, +1)）。
    /// 局面 b（补气 + 提子）：P0 的 E5-E6 只剩一气 F6，P1 的 F5-G5 也只剩一气 F6；F6 提两子后 P0 串与 G6 连成四气。
    /// <code>
    ///  7 . . . X X . . .      X = P1
    ///  6 . . X O ! O . .      O = P0
    ///  5 . . X O X X O .      ! = F6
    ///  4 . . . X O O . .
    ///    B C D E F G H J
    /// </code>
    /// 局面 c（一手提三子并夺回信物）：P1 的 F6-F7-G7 只剩一气 G6，F7 是已揭示的信物格（P1 占据中）。
    /// <code>
    ///  8 . . . . O O . .
    ///  7 . . . O X X O .
    ///  6 . . . O X ! . .      ! = G6
    ///  5 . . . . O . . .
    ///    B C D E F G H J
    /// </code>
    /// </summary>
    [Theory]
    [InlineData("a", "G6")]
    [InlineData("b", "F6")]
    [InlineData("c", "G6")]
    public void 必须找到的妙手(string position, string key)
    {
        // 变异验证 M-A17：Greedy 接受条件改为 next.Total < current.Total → 红 9（本 Theory、不穷举排列、剪枝保留高价值落点、种子扰动由种子驱动、
        // 同局面同决策、估计不得使用真实值、高难度不越权、接管记录可追溯、对局记录标注）。
        MatchFlow match = position switch
        {
            // retire-legacy-rules 1.4（清单 P2）：局面 a / b 里 P0 原本势力落后（a 的先手位用例就靠这一点），链接规则下落后者的停手阈值取 0；
            // 这里在远离妙手的盘角给 P0 补一块 2×2，使其居本战场首位（AsLeader 断言前提）。局面 c 里 P0 本就领先。
            "a" => 启发式评价维度Tests.CaptureRelicPosition().AsLeader(AiFixtures.P0, "J3", "K3", "J4", "K4"),
            "b" => AiFixtures.Round5()
                .Stones(AiFixtures.P0, "E5", "E6", "G6", "H5", "F4", "G4")
                .Stones(AiFixtures.P1, "E7", "F7", "D6", "D5", "F5", "G5", "E4")
                .AsLeader(AiFixtures.P0, "J9", "K9", "J10", "K10"),
            "c" => AiFixtures.Round5(relics: [("F7", RelicFixtures.Conscription())])
                .Stones(AiFixtures.P0, "E6", "E7", "F5", "F8", "G8", "H7")
                .Stones(AiFixtures.P1, "F6", "F7", "G7"),
            _ => throw new ArgumentOutOfRangeException(nameof(position)),
        };

        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Standard);
        StagedBatch batch = match.OpenDeploy();
        Assert.Equal(4, batch.Context.DeployLimit);
        ai.Deploy(batch, match.Rehearse);

        Assert.Contains(batch.Placements, p => p.Coord == TestMaps.At(key));
        SettlementOutcome outcome = match.Confirm();
        Assert.True(outcome.Confirmed);
        Assert.True(outcome.CaptureRecord is { } captured && captured.Captured.Length >= 1, "妙手必须真的提子");
        if (position == "b")
        {
            Group saved = match.Board.GroupAt(TestMaps.At("E5"))!;
            Assert.True(match.Board.LibertiesOf(saved).Length >= 4, $"补气后 {saved} 气数 {match.Board.LibertiesOf(saved).Length}");
        }

        if (position == "c")
        {
            Assert.Equal(3, outcome.CaptureRecord!.Captured.Length);
            Assert.True(match.Relics.ControlOf(TestMaps.At("F7")).GrantsEffectTo(AiFixtures.P0));
        }
    }
}
