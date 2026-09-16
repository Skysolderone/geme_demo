using System.Text;
using Siege.Core.Scoring;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。

/// <summary>把 <see cref="BalanceReport"/> 渲染成纯文本报告：§16 六项目标 + §17 七个方向各一段、收敛单独一段、旁证警告置顶。</summary>
public static class ReportWriter
{
    public static string Render(BalanceReport r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var sb = new StringBuilder();
        sb.AppendLine("# 平衡分析报告（离线，只读日志）");
        sb.AppendLine();
        sb.AppendLine($"日志 {r.TotalLogs} 局：纳入 {r.Included}，排除调试 AI / 人工接管局 {r.ExcludedContaminated}（{(r.Options.IncludeContaminated ? "已显式要求包含" : "默认排除")}），排除失败局 {r.ExcludedFailed}。玩家数 {r.PlayerCount}。");
        sb.AppendLine($"胜率类指标要求样本 ≥ {r.Options.RequiredMatches} 局；置信区间一律为 Wilson 得分区间（95%）。");
        sb.AppendLine();

        sb.AppendLine("## AI 决策质量旁证");
        AiQualitySection q = r.AiQuality;
        sb.AppendLine($"- 已确认批次 {q.SettledBatches}，提子 {q.Captures}，平均每批次提子 {Num(q.MeanCapturesPerBatch)}");
        sb.AppendLine($"- 自杀手尝试率 {q.SuicideAttemptRate}（阈值 {BalanceAnalyzer.SuicideUnreliableThreshold * 100:F0}%）");
        sb.AppendLine($"- Pass 率 {q.PassRate}（阈值 {BalanceAnalyzer.PassUnreliableThreshold * 100:F0}%）");
        sb.AppendLine($"- {q.Verdict}");
        sb.AppendLine();

        sb.AppendLine("## 收敛情况（裁决 13 / round-cap：按规则原因 MajorRoundLimit 统计）");
        ConvergenceSection c = r.Convergence;
        sb.AppendLine($"- 非达上限终局（含势力碾压）{c.Converged} 局；达大回合上限终局（MajorRoundLimit）{c.Capped} 局，不收敛率 {c.CappedRate}");
        sb.AppendLine($"- 终局原因：{Histogram(c.Reasons)}");
        sb.AppendLine($"- 平均大回合数：终局局 {Num(c.MeanMajorRoundsConverged)}，全部局 {Num(c.MeanMajorRoundsAll)}；平均小回合数 {Num(c.MeanTurnsPerMatch)}");
        sb.AppendLine();

        sb.AppendLine("## 势力碾压（dominance-victory：候选制，占比分母为纳入局）");
        DominanceSection d = r.Dominance;
        sb.AppendLine($"- 碾压胜 {d.DominanceWins} / {d.Matches} 局，占比 {d.Rate}");
        sb.AppendLine($"- 碾压局平均成立大回合 {Num(d.MeanTriggerRound)}");
        sb.AppendLine($"- 触发时获胜者势力 / 第 2 名势力：平均 {Num(d.MeanPowerRatio)}（样本 {d.RatioSamples}；第 2 名势力为 0、比值无定义 {d.RatioUndefined}）");
        sb.AppendLine();

        sb.AppendLine("## §16 数值目标回归");
        TargetsSection t = r.Targets;
        sb.AppendLine("### 1. 部署上限分阶段分布（growth-pass-1：基础值按大回合 3 / 4 / 5，军令在其上叠加；目标中位数 3 / 3–5 / 5–8）");
        sb.AppendLine($"- 第 1–3 大回合：{Histogram(t.DeployLimitRounds1To3)}；中位数 {t.DeployPhase1}");
        sb.AppendLine($"- 第 4–6 大回合：{Histogram(t.DeployLimitRounds4To6)}；中位数 {t.DeployPhase2}");
        sb.AppendLine($"- 第 7 大回合以后：{Histogram(t.DeployLimitRounds7Plus)}；中位数 {t.DeployPhase3}（允许极端构筑超过 8）");
        sb.AppendLine("### 2. 势力成长曲线");
        foreach ((int round, double mean, long maxGroup, int samples) in t.PowerCurve)
        {
            sb.AppendLine($"- 第 {round} 大回合结束：参赛玩家平均势力 {Num(mean)}，最高单串军势 {maxGroup}（样本 {samples}）");
        }

        sb.AppendLine($"- 开局（第 1–2 大回合，目标个位或十位）：{t.PowerOpening}");
        sb.AppendLine($"- 中期（第 4–6 大回合，目标几十至一百）：{t.PowerMid}");
        sb.AppendLine("### 3. 首次跨出生区冲突（首次提子所在大回合）");
        sb.AppendLine($"- 分布：{Histogram(t.FirstConflictRounds)}；整局无冲突 {t.MatchesWithoutConflict} 局");
        sb.AppendLine($"- 平均：{t.FirstConflict}");
        sb.AppendLine("### 4. 4 人完整大回合平均耗时");
        sb.AppendLine($"- {t.MajorRoundMinutes}：这是玩家体验目标，无头跑局不测（裁决 10）。代理：平均每大回合 {Num(t.MeanTurnsPerMajorRound)} 个小回合，AI 计算耗时 {Num(t.MeanAiMsPerMajorRound)} ms/大回合");
        sb.AppendLine("### 5. 对局结束的大回合数与整局时长");
        sb.AppendLine($"- 终局局的结束大回合分布：{Histogram(t.EndRounds)}；平均 {t.EndRound}");
        sb.AppendLine($"- 整局时长 {t.MatchMinutes}：不测（裁决 10）。代理：平均每局 {Num(r.Convergence.MeanTurnsPerMatch)} 个小回合，AI 计算耗时 {Num(t.MeanAiMsPerMatch)} ms/局");
        sb.AppendLine("### 6. 第 3 大回合领先者最终胜率（目标 ≤ 50%）");
        LeaderSection l = r.Leader;
        sb.AppendLine($"- 口径 A（并列组内任一人获胜）：{l.AnyOfGroupWins}；{t.LeaderWinRate}");
        sb.AppendLine($"- 口径 B（必须该具体玩家获胜，按领先者逐人计样本）：{l.SpecificPlayerWins}");
        sb.AppendLine($"- 样本 {l.Samples} 局（其中并列 {l.TiedSamples} 局）{(l.EnoughSamples ? "" : $"——不足 {r.Options.RequiredMatches} 局，结论不可靠")}");
        sb.AppendLine();

        sb.AppendLine("## §17 平衡分析方向");
        sb.AppendLine("### 1. 第 3 大回合领先者与最终胜者的相关性");
        sb.AppendLine($"- 领先者胜率（口径 A）{l.AnyOfGroupWins}，非领先者胜率 {l.NonLeaderWins}，基线（1/人数）{Pct(l.Baseline)}");
        sb.AppendLine($"- 判定：{(l.AnyOfGroupWins.IsEmpty ? "无样本" : l.AnyOfGroupWins.Excludes(l.Baseline) ? "领先者胜率显著偏离基线" : "领先者胜率与基线无显著差异")}");
        sb.AppendLine("### 2. 先手流是否形成不可逆滚雪球");
        foreach ((int position, int improved, int same, int worse) in r.Snowball.RankChangeByPosition)
        {
            sb.AppendLine($"- 行动位置 {position + 1}：下一大回合名次 上升 {improved} / 不变 {same} / 下降 {worse}");
        }

        sb.AppendLine($"- 先手者下一大回合仍为先手：{r.Snowball.FirstMoverStaysFirst}");
        foreach ((int position, double meanRank, int samples) in r.Snowball.FinalRankByRound3Position)
        {
            sb.AppendLine($"- 第 4 大回合行动位置 {position + 1} 的玩家最终平均名次 {Num(meanRank)}（样本 {samples}）");
        }

        sb.AppendLine("### 3. 各棋子与各信物的选择率、控制时长与胜率");
        foreach (PieceStat p in r.Selection.Pieces)
        {
            sb.AppendLine($"- 棋子 {p.Type}：展示 {p.Offered} 次，选取 {p.Picked} 次，选择率 {p.SelectionRate}；选过它的玩家胜率 {p.WinRateOfPickers}");
        }

        foreach (RelicStat s in r.Selection.Relics)
        {
            sb.AppendLine($"- 信物 {s.Type}：{s.Count} 枚，揭示 {s.Revealed} 枚，被控制 {s.ControlledTurns} 个小回合（平均每枚 {Num(s.MeanControlledTurnsPerRelic)}）；控制过它的玩家胜率 {s.WinRateOfControllers}");
        }

        sb.AppendLine("### 4. 高倍率棋串的形成轮次、峰值及被摧毁概率");
        MultiplierSection m = r.Multiplier;
        sb.AppendLine($"- 出现过倍增串的局 {m.MatchesWithPeak}；峰值倍增子数分布（原始数量）{Histogram(m.PeakCountDistribution)}；峰值生效倍率指数分布（封顶 {Multiplier.MaxExponent}，倍率上限 {new Multiplier(Multiplier.MaxExponent)}）{Histogram(m.PeakEffectiveExponentDistribution)}");
        sb.AppendLine($"- 峰值首次出现平均在第 {Num(m.MeanFormationRound)} 大回合，峰值军势平均 {Num(m.MeanPeakPower)}，最高 {m.MaxPeakPower}");
        sb.AppendLine($"- 峰值串之后被摧毁的概率 {m.DestroyedRate}");
        sb.AppendLine("### 4b. 各棋子势力占比（multiplier-rebalance：终局快照、参赛玩家的全部棋串；倍增子计其放大出的部分，其余计基础军势与分得的位置加值；不含领地分）");
        PieceShareSection ps = r.PieceShares;
        sb.AppendLine($"- 纳入 {ps.Matches} 局，跳过无棋子类型计数的旧日志 / 无快照局 {ps.Skipped} 局；盘面棋子 {ps.TotalStones} 枚，归因势力 {ps.TotalPower}");
        foreach (PieceShare p in ps.Pieces)
        {
            sb.AppendLine($"- 棋子 {p.Type}：盘面 {p.Stones} 枚（{Pct(p.StoneShare)}），势力 {p.Power}（{Pct(p.PowerShare)}），每颗平均 {Num(p.MeanPerStone)}");
        }

        sb.AppendLine("### 4c. 落后者征募补偿（catch-up-recruit：占比分母为纳入局的小回合数；关闭补偿的局与无留痕的旧日志整局排除）");
        CatchUpSection cu = r.CatchUp;
        sb.AppendLine($"- 纳入 {cu.Matches} 局（{cu.Turns} 个小回合），排除关闭补偿 / 无补偿留痕的局 {cu.Skipped} 局");
        sb.AppendLine($"- 获补偿小回合 {cu.CompensatedTurns}，占比 {cu.CompensatedTurnRate}");
        sb.AppendLine($"- 两档触发次数：后半名次展示 +1 共 {cu.RevealTriggers} 次，最后一名选取 +1 共 {cu.PickTriggers} 次");
        sb.AppendLine($"- 获补偿玩家的终局名次分布：{Histogram(cu.FinalRankOfCompensated)}");
        sb.AppendLine("### 5. 出生区随机资源是否造成显著胜率差异（裁决 8：按信物生成收敛分组）");
        BirthZoneSection z = r.BirthZones;
        sb.AppendLine($"- 基线 {Pct(z.Baseline)}；显著 = 基线落在该区 Wilson 区间之外");
        AppendZones(sb, "全部局", z.All);
        AppendZones(sb, $"信物生成收敛局（{z.ConvergedMatches}）", z.RelicsConverged);
        AppendZones(sb, $"信物生成未收敛局（{z.NotConvergedMatches}）", z.RelicsNotConverged);
        sb.AppendLine("### 6. 供给、部署、槽位、倍率四条成长轴的获取顺序");
        GrowthAxisSection g = r.GrowthAxes;
        sb.AppendLine($"- 胜者样本 {g.WinnerSamples}；首先获取的轴分布：{Histogram(g.FirstAxisCounts)}");
        foreach ((string sequence, int count) in g.SequenceCounts.OrderByDescending(kv => kv.Value).Take(10))
        {
            sb.AppendLine($"- 顺序 {sequence}：{count} 次");
        }

        sb.AppendLine($"- 判定：{(g.DominantSequence is null ? "无样本" : g.DominantShare > 0.5 ? $"存在占比 {Pct(g.DominantShare)} 的主导顺序 {g.DominantSequence}，疑似唯一最优解" : $"最多的顺序 {g.DominantSequence} 占 {Pct(g.DominantShare)}，未见唯一最优顺序")}");
        sb.AppendLine("### 7. 最小落子规避 Pass 撤销（落 1 枚且势力无变化）");
        sb.AppendLine($"- 信号出现 {r.Stalling.SignalTurns} 次，占全部 {r.Stalling.TotalTurns} 个小回合的 {r.Stalling.Ratio}");
        return sb.ToString();
    }

    private static void AppendZones(StringBuilder sb, string label, List<ZoneStat> zones)
    {
        sb.AppendLine($"- {label}：");
        foreach (ZoneStat zone in zones)
        {
            sb.AppendLine($"  - 出生区 {zone.Zone}：胜率 {zone.WinRate}{(zone.Significant ? "，显著" : "")}");
        }
    }

    private static string Histogram<TKey>(SortedDictionary<TKey, int> h) where TKey : notnull =>
        h.Count == 0 ? "无" : string.Join("，", h.Select(kv => $"{kv.Key}×{kv.Value}"));

    private static string Num(double v) => double.IsNaN(v) ? "无样本" : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string Pct(double v) => double.IsNaN(v) ? "无样本" : $"{v * 100:F1}%";
}
