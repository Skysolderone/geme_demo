using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;

namespace Siege.Core.Tests.AiDecision;

/// <summary>规格：ai-decision —— Requirement: 高部署上限下的候选剪枝</summary>
public class 高部署上限下的候选剪枝Tests
{
    [Fact]
    public void 不穷举排列()
    {
        // 设计文档 §15.2 / 裁决 2：部署上限 8、60+ 合法空格 → 候选批次 ≤ M（标准 8），单点排序 ≤ N（12），
        // 预演次数 ≤ 空格数 × 类型数（单点）+ M × (N + 1)（组合），远小于 C(60, 8)。
        // 变异验证 M-A13：RankPoints 去掉 Take(N) → 红 1（本测试）。
        MatchFlow match = AiFixtures.Round5().Stones(AiFixtures.P1, "E5", "F6", "D7").Stones(AiFixtures.P2, "G3");
        match.SetDeployLimit(8);
        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0);
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

        Assert.Equal(AiSearchConfig.Standard.CandidatePointCount, ai.LastPointRanking.Length);
        Assert.InRange(ai.LastCandidates.Length, 1, AiSearchConfig.Standard.CandidateBatchCount);
        int types = batch.Context.Stock.Count(kv => kv.Value > 0);
        Assert.True(rehearsals <= (empties * types) + (AiSearchConfig.Standard.CandidateBatchCount * (AiSearchConfig.Standard.CandidatePointCount + 1)), $"预演 {rehearsals} 次");
        Assert.Equal(8, batch.Count);
        Assert.True(match.Confirm().Confirmed);
    }

    [Fact]
    public void 剪枝保留高价值落点()
    {
        // 单点评价第 1 名的落点（提子 + 抢信物的 F5）进入前 N，并出现在最终批次里；候选批次的每个落点都来自前 N。
        // 变异验证 M-A15：RankPoints 改为 OrderBy 升序 → 红 4（本测试、不穷举排列、必须找到的妙手、并列确定性打破）。
        MatchFlow match = 启发式评价维度Tests.CaptureRelicPosition();
        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0);
        StagedBatch batch = match.OpenDeploy();
        ai.Deploy(batch, match.Rehearse);

        Coord f5 = TestMaps.At("F5");
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
            match = AiFixtures.Round5().Stones(AiFixtures.P1, "E5", "F6").Stones(AiFixtures.P0, "C4");
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
    /// implement 3.4「必须找到的妙手」回归集（标准难度剪枝参数、部署上限 3、只有普通子）。
    /// 局面 a（提子 + 抢信物）见 <see cref="启发式评价维度Tests.CaptureRelicPosition"/>。
    /// 局面 b（补气 + 提子）：P0 的 D4-D5 只剩一气 E5，P1 的 E4-F4 也只剩一气 E5；E5 提两子后 P0 串与 F5 连成四气。
    /// <code>
    ///  6 . . . X X . . .      X = P1
    ///  5 . . X O ! O . .      O = P0
    ///  4 . . X O X X O .      ! = E5
    ///  3 . . . X O O . .
    ///    A B C D E F G H
    /// </code>
    /// 局面 c（一手提三子并夺回信物）：P1 的 E5-E6-F6 只剩一气 F5，E6 是已揭示的信物格（P1 占据中）。
    /// <code>
    ///  7 . . . . O O . .
    ///  6 . . . O X X O .
    ///  5 . . . O X ! . .      ! = F5
    ///  4 . . . . O . . .
    ///    A B C D E F G H
    /// </code>
    /// </summary>
    [Theory]
    [InlineData("a", "F5")]
    [InlineData("b", "E5")]
    [InlineData("c", "F5")]
    public void 必须找到的妙手(string position, string key)
    {
        // 变异验证 M-A17：Greedy 接受条件改为 next.Total < current.Total → 红 9（本 Theory、不穷举排列、剪枝保留高价值落点、种子扰动由种子驱动、
        // 同局面同决策、估计不得使用真实值、高难度不越权、接管记录可追溯、对局记录标注）。
        MatchFlow match = position switch
        {
            "a" => 启发式评价维度Tests.CaptureRelicPosition(),
            "b" => AiFixtures.Round5()
                .Stones(AiFixtures.P0, "D4", "D5", "F5", "G4", "E3", "F3")
                .Stones(AiFixtures.P1, "D6", "E6", "C5", "C4", "E4", "F4", "D3"),
            "c" => AiFixtures.Round5(relics: [("E6", RelicFixtures.Conscription())])
                .Stones(AiFixtures.P0, "D5", "D6", "E4", "E7", "F7", "G6")
                .Stones(AiFixtures.P1, "E5", "E6", "F6"),
            _ => throw new ArgumentOutOfRangeException(nameof(position)),
        };

        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Standard);
        StagedBatch batch = match.OpenDeploy();
        Assert.Equal(3, batch.Context.DeployLimit);
        ai.Deploy(batch, match.Rehearse);

        Assert.Contains(batch.Placements, p => p.Coord == TestMaps.At(key));
        SettlementOutcome outcome = match.Confirm();
        Assert.True(outcome.Confirmed);
        Assert.True(outcome.CaptureRecord is { } captured && captured.Captured.Length >= 1, "妙手必须真的提子");
        if (position == "b")
        {
            Group saved = match.Board.GroupAt(TestMaps.At("D4"))!;
            Assert.True(match.Board.LibertiesOf(saved).Length >= 4, $"补气后 {saved} 气数 {match.Board.LibertiesOf(saved).Length}");
        }

        if (position == "c")
        {
            Assert.Equal(3, outcome.CaptureRecord!.Captured.Length);
            Assert.True(match.Relics.ControlOf(TestMaps.At("E6")).GrantsEffectTo(AiFixtures.P0));
        }
    }
}
