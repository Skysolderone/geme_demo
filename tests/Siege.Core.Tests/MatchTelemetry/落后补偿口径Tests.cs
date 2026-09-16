using System.Text.Json;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// catch-up-recruit（implement.md 4.1）：Sim 日志首部记开关、小回合快照留痕两档补偿点数，平衡报告新增"落后补偿"段落。
/// 本段是报告层统计，没有 openspec Scenario；方法名按 implement.md 条目命名。
/// </summary>
public class 落后补偿口径Tests
{
    [Fact]
    public void 快照往返保留两档补偿点数()
    {
        // 两档取不同值（展示 1 / 选取 0），写反或漏写都能从值上看出来；旧日志无字段 → 解析为 null（未知），MUST NOT 回填成 0。
        // 变异验证 M-CU9：MatchSession 不写 CatchUpReveal（恒 null）→ 红 1（真实跑局把补偿写进快照与首部）；本用例走合成日志，另由上面的旧日志断言守门。
        MatchLog log = SimFixtures.Synthetic(
            31,
            [SimFixtures.Turn(1, 1, 0, [10, 5, 5, 5], showCount: 6, freePick: 3, catchUpReveal: 1, catchUpPick: 0)],
            [],
            SimFixtures.ResultOf(1, [0]),
            catchUpRecruit: true);

        TurnSnapshot restored = MatchLog.Parse(log.DeterministicText()).Turns[0];
        Assert.Equal((1, 0), (restored.CatchUpReveal, restored.CatchUpPick));

        const string oldTurnLine = "{\"Kind\":\"turn\",\"Turn\":1,\"MajorRound\":1,\"Player\":0,\"ShowCount\":5,\"FreePickCount\":3,\"TypeSlots\":5,\"DeployLimit\":3}";
        TurnSnapshot old = JsonSerializer.Deserialize<TurnSnapshot>(oldTurnLine, LogJson.Options)!;
        Assert.Null(old.CatchUpReveal);
        Assert.Null(old.CatchUpPick);
    }

    [Fact]
    public void 真实跑局把补偿写进快照与首部()
    {
        // 真实盘面 → 写入函数（LoggingController 旁录真实征募面板）→ 日志往返 → 报告：合成日志绕过写入路径，测不出写反。
        // 口径自洽：ShowCount = 5 + 探勘加成 + CatchUpReveal，FreePickCount = 3 + 征召加成 + CatchUpPick；
        //   这里用"基础 + 补偿"的下界反证——补偿为 1 的小回合展示数必 ≥ 6，补偿为 0 且无信物的小回合展示数为 5。
        // 样本口径下界：样本里必须真的出现过两档补偿，否则等于没验证写入路径。
        List<MatchLog> sample = SimFixtures.Sample.Value;
        Assert.All(sample, l => Assert.True(l.Header.CatchUpRecruit));

        List<TurnSnapshot> turns = [.. sample.SelectMany(l => l.Turns)];
        Assert.NotEmpty(turns);
        Assert.All(turns, t => Assert.NotNull(t.CatchUpReveal));
        Assert.All(turns, t => Assert.NotNull(t.CatchUpPick));
        Assert.All(turns, t => Assert.InRange(t.CatchUpReveal!.Value, 0, 1));
        Assert.All(turns, t => Assert.InRange(t.CatchUpPick!.Value, 0, 1));
        Assert.All(turns, t => Assert.True(t.ShowCount >= 5 + t.CatchUpReveal!.Value, $"第 {t.Turn} 个小回合展示 {t.ShowCount} 与补偿 {t.CatchUpReveal} 不自洽"));
        Assert.All(turns, t => Assert.True(t.FreePickCount >= 3 + t.CatchUpPick!.Value, $"第 {t.Turn} 个小回合选取 {t.FreePickCount} 与补偿 {t.CatchUpPick} 不自洽"));

        Assert.True(turns.Count(t => t.CatchUpReveal == 1) > 0, "样本里没有展示 +1 的小回合");
        Assert.True(turns.Count(t => t.CatchUpPick == 1) > 0, "样本里没有选取 +1 的小回合");

        // 与名次自洽：拿到选取 +1 的玩家在该小回合开始时必为最后一名——用上一条快照的名次独立核对。
        foreach (MatchLog log in sample)
        {
            for (int i = 1; i < log.Turns.Count; i++)
            {
                TurnSnapshot before = log.Turns[i - 1];
                TurnSnapshot now = log.Turns[i];
                List<int> ranks = [.. before.PlayersState.Where(p => p.Rank is not null).Select(p => p.Rank!.Value)];
                if (ranks.Count == 0 || before.PlayersState.All(p => p.Player != now.Player) || before.PlayersState.First(p => p.Player == now.Player).Rank is not { } rank)
                {
                    continue;
                }

                Assert.Equal(rank > (ranks.Count + 1) / 2 ? 1 : 0, now.CatchUpReveal);
                Assert.Equal(rank == ranks.Max() && rank > 1 ? 1 : 0, now.CatchUpPick);
            }
        }
    }

    [Fact]
    public void 报告落后补偿段落含被排除样本()
    {
        // 口径：分母是**纳入局**的小回合数，不是全部局。纳入 = 首部记录开启补偿且每条快照都带留痕。
        // 被排除样本：① 旧日志（快照缺 CatchUpReveal）②关闭补偿的局。缺一不可——分母写成"全部局"也能算对就说明没在守门。
        // 变异验证 M-CU10：skipped 判定去掉 `t.CatchUpReveal is null`（只看首部开关）→ 红 1（本测试）。
        //   注意两个排除条件是 `||`：若只放"首部无开关"的旧日志，去掉留痕那半个条件是等价变异（M-CU10 原本 0 红），
        //   所以这里额外放了 halfLogged——首部有开关、快照却缺留痕。
        // 变异验证 M-CU11：占比分母改成 logs.Sum(l => l.Turns.Count)（含被排除局）→ 红 1（本测试）。
        MatchLog included = SimFixtures.Synthetic(
            41,
            [
                SimFixtures.Turn(1, 1, 0, [10, 5, 5, 5], catchUpReveal: 0, catchUpPick: 0),
                SimFixtures.Turn(2, 1, 3, [10, 5, 5, 2], showCount: 6, freePick: 4, catchUpReveal: 1, catchUpPick: 1),
            ],
            [],
            SimFixtures.ResultOf(1, [0], ranks: [1, 2, 3, 4]),
            catchUpRecruit: true);
        MatchLog included2 = SimFixtures.Synthetic(
            42,
            [SimFixtures.Turn(1, 1, 2, [10, 8, 5, 5], showCount: 6, freePick: 3, catchUpReveal: 1, catchUpPick: 0)],
            [],
            SimFixtures.ResultOf(1, [0], ranks: [1, 2, 3, 4]),
            catchUpRecruit: true);
        MatchLog legacy = SimFixtures.Synthetic(   // 旧日志：开关字段与留痕都没有
            43,
            [SimFixtures.Turn(1, 1, 3, [10, 5, 5, 1]), SimFixtures.Turn(2, 1, 2, [10, 5, 5, 1])],
            [],
            SimFixtures.ResultOf(1, [0], ranks: [1, 2, 3, 4]));
        MatchLog switchedOff = SimFixtures.Synthetic(   // 关闭补偿的局：有留痕但不同口径
            44,
            [SimFixtures.Turn(1, 1, 3, [10, 5, 5, 1], catchUpReveal: 0, catchUpPick: 0)],
            [],
            SimFixtures.ResultOf(1, [0], ranks: [1, 2, 3, 4]),
            catchUpRecruit: false);

        MatchLog halfLogged = SimFixtures.Synthetic(   // 首部有开关、快照却缺留痕（写入路径漏写）：两个排除条件必须各自单独成立
            45,
            [SimFixtures.Turn(1, 1, 3, [10, 5, 5, 1]), SimFixtures.Turn(2, 1, 2, [10, 5, 5, 1])],
            [],
            SimFixtures.ResultOf(1, [0], ranks: [1, 2, 3, 4]),
            catchUpRecruit: true);
        MatchLog[] logs = [included, included2, legacy, switchedOff, halfLogged];

        CatchUpSection section = BalanceAnalyzer.Analyze(logs).CatchUp;

        Assert.Equal((2, 3), (section.Matches, section.Skipped));
        Assert.Equal(3, section.Turns);              // 被排除的 3 个小回合不进分母
        Assert.Equal(2, section.CompensatedTurns);
        Assert.Equal((1.0 * 2) / 3, section.CompensatedTurnRate.Value, 6);
        Assert.Equal((2, 1), (section.RevealTriggers, section.PickTriggers));
        Assert.Equal(["3×1", "4×1"], section.FinalRankOfCompensated.Select(kv => $"{kv.Key}×{kv.Value}"));

        string report = ReportWriter.Render(BalanceAnalyzer.Analyze(logs));
        Assert.Contains("### 4c. 落后者征募补偿", report);
        Assert.Contains("纳入 2 局（3 个小回合），排除关闭补偿 / 无补偿留痕的局 3 局", report);
        Assert.Contains("后半名次展示 +1 共 2 次，最后一名选取 +1 共 1 次", report);
        Assert.Contains("获补偿玩家的终局名次分布：3×1，4×1", report);
    }
}
