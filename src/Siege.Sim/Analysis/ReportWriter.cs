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
        sb.AppendLine(
            $"- 冲突时的盘面占用率（首次提子时盘面棋子数 ÷ 该局地图可落子格）：平均 {Pct(t.MeanFirstConflictOccupancy)}；"
            + $"分布（整数百分比向下取整）{Histogram(t.FirstConflictOccupancy)}；"
            + $"未纳入 {t.MatchesWithoutOccupancy} 局（整局无提子，或 denser-map 之前未记可落子格的旧日志）");
        sb.AppendLine("### 4. 4 人完整大回合平均耗时");
        sb.AppendLine($"- {t.MajorRoundMinutes}：这是玩家体验目标，无头跑局不测（裁决 10）。代理：平均每大回合 {Num(t.MeanTurnsPerMajorRound)} 个小回合，AI 计算耗时 {Num(t.MeanAiMsPerMajorRound)} ms/大回合");
        sb.AppendLine($"- AI 单步决策耗时（单步 = 一个小回合：整理 + 征募 + 整批部署）：均值 {Num(t.AiStep.MeanMs)} ms，最大 {t.AiStep.MaxMs} ms（样本 {t.AiStep.Samples} 个小回合；墙钟，并行跑局会被撑大，量耗时请用 --serial）");
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
        sb.AppendLine("### 4b. 各棋子势力占比（multiplier-rebalance：终局快照、参赛玩家的全部棋串；倍增子计其放大出的部分，其余计基础军势与分得的位置加值；不含据点分与高地加值）");
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
        sb.AppendLine($"- 区数 {z.ZoneCount}（地图出生区数；多于人数时没人选的平台是中立争夺区，被选 0 次的区同样列出）");
        sb.AppendLine($"- 基线 {Pct(z.Baseline)}；显著 = 基线落在该区 Wilson 区间之外");
        if (z.BySide is { } bySide)
        {
            // 每局换图：平台编号跨局不可比，按编号的胜率没有意义——改按平台边长分组（map-generator D6）。
            sb.AppendLine($"- 每局换图的批次：各局地图不同，平台编号跨局不可比，改按平台边长分组（纳入 {bySide.Matches} 局，首部无平台边长而排除 {bySide.Skipped} 局）");
            foreach (SideStat side in bySide.Sides)
            {
                sb.AppendLine($"  - 边长 {side.Side}：胜率 {side.WinRate}{(side.Significant ? "，显著" : "")}；被选 {side.Picks} 次（共出现 {side.Offered} 个）");
            }
        }
        else
        {
            AppendZones(sb, "全部局", z.All);
            AppendZones(sb, $"信物生成收敛局（{z.ConvergedMatches}）", z.RelicsConverged);
            AppendZones(sb, $"信物生成未收敛局（{z.NotConvergedMatches}）", z.RelicsNotConverged);
        }
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
        sb.AppendLine("### 9. 首次跨出生区冲突发生时的盘面占用率（denser-map D5：改图 / 改部署上限后必须复看这条）");
        sb.AppendLine(
            $"- 平均占用率 {Pct(t.MeanFirstConflictOccupancy)}，与冲突大回合 {t.FirstConflict} 并列；"
            + $"分布 {Histogram(t.FirstConflictOccupancy)}；未纳入 {t.MatchesWithoutOccupancy} 局");
        sb.AppendLine("### 10. 据点（scoring-sites：分母为纳入局的「小回合快照 × 该档据点数」；缺据点字段的旧日志整局排除）");
        SiteSection st = r.Sites;
        sb.AppendLine($"- 纳入 {st.Matches} 局，排除无据点字段的旧日志 {st.Skipped} 局");
        foreach (SiteTierStat tier in st.Tiers)
        {
            sb.AppendLine(
                $"- {SiteTierName(tier.Tier)}（{tier.SiteInstances} 个·局）：被控制 {Pct(tier.ControlledShare)}（{tier.ControlledTurns}/{tier.SiteTurns}），争议 {Pct(tier.ContestedShare)}（{tier.ContestedTurns}/{tier.SiteTurns}），"
                + $"首次被控制平均第 {Num(tier.MeanFirstControlledRound)} 大回合（从未被控制 {tier.NeverControlled} 个），控制过它的玩家胜率 {tier.WinRateOfControllers}");
        }

        AppendOwner(sb, "篝火由所在低地主人控制", st.Campfire);
        AppendOwner(sb, "石碑由相邻桥头那家控制", st.SteleBridgehead);
        sb.AppendLine($"- 终局据点分占参赛玩家总势力：平均 {Pct(st.MeanFinalSiteShare)}（样本 {st.FinalShareSamples} 局）");
        sb.AppendLine($"- 终局高地加值占全部位置加值：{Pct(st.HighGroundShare)}（{st.FinalHighGroundBonus}/{st.FinalPositionBonus}）");
        sb.AppendLine();

        sb.AppendLine("## §17-11 地形改造（artisan-terrain-edit）");
        TerrainEditSection te = r.TerrainEdits;
        sb.AppendLine($"- 纳入 {te.Matches} 局，排除缺改造字段的旧日志 {te.Skipped} 局");
        sb.AppendLine($"- 改造总次数 {te.TotalEdits}，每局平均 {Num(te.MeanEditsPerMatch)} 次；整局无改造 {te.MatchesWithoutEdit} 局");

        // 三种动作逐行输出：0 次也照常给出（规格「烧林无人使用也如实给出」MUST NOT 省略该行）。
        foreach (TerrainEditActionStat a in te.Actions)
        {
            sb.AppendLine($"  - {EditActionName(a.Action)}：{a.Count} 次（{Pct(a.Share)}），其中直接导致提子 {a.CausedCaptures} 次");
        }

        sb.AppendLine($"- 带改造的匠人占已落匠人：{Pct(te.EditingArtisanShare)}（{te.ArtisansWithEdit}/{te.ArtisansPlaced}）");
        sb.AppendLine($"- 改造直接导致提子 {te.CausedCaptures} 次；首次改造平均第 {Num(te.MeanFirstEditRound)} 大回合");
        sb.AppendLine($"- 改造过的玩家胜率 {te.WinRateOfEditors}");
        sb.AppendLine($"- 终局新增：桥 {te.FinalBridges} 座，栅栏 {te.FinalFences} 道，被烧林地 {te.FinalBurns} 格");
        return sb.ToString();
    }

    private static string EditActionName(string action) => action switch
    {
        nameof(Core.Board.TerrainEditKind.Bridge) => "搭桥",
        nameof(Core.Board.TerrainEditKind.Fence) => "立栅",
        nameof(Core.Board.TerrainEditKind.Burn) => "烧林",
        _ => action,
    };

    private static void AppendOwner(StringBuilder sb, string label, SiteOwnerStat o) =>
        sb.AppendLine(
            $"- {label}：占全部据点小回合 {Pct(o.OwnerShare)}（{o.OwnerTurns}/{o.SiteTurns}），占被控制小回合 {Pct(o.OwnerShareOfControlled)}（{o.OwnerTurns}/{o.ControlledTurns}）；"
            + $"主人首次控制平均第 {Num(o.MeanOwnerFirstControlRound)} 大回合（主人从未控制 {o.OwnerNeverControlled} 个）；推不出主人 {o.UnknownOwner} 个");

    private static string SiteTierName(string tier) => tier switch
    {
        nameof(Core.Board.SiteTier.Tent) => "营帐",
        nameof(Core.Board.SiteTier.Campfire) => "篝火",
        nameof(Core.Board.SiteTier.Stele) => "石碑",
        _ => tier,
    };

    private static void AppendZones(StringBuilder sb, string label, List<ZoneStat> zones)
    {
        sb.AppendLine($"- {label}：");
        foreach (ZoneStat zone in zones)
        {
            sb.AppendLine($"  - {Siege.Core.Board.BirthZoneLabel.Of(zone.Zone)}：胜率 {zone.WinRate}{(zone.Significant ? "，显著" : "")}；被选 {zone.Picks} 次");
        }
    }

    private static string Histogram<TKey>(SortedDictionary<TKey, int> h) where TKey : notnull =>
        h.Count == 0 ? "无" : string.Join("，", h.Select(kv => $"{kv.Key}×{kv.Value}"));

    private static string Num(double v) => double.IsNaN(v) ? "无样本" : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string Pct(double v) => double.IsNaN(v) ? "无样本" : $"{v * 100:F1}%";
}
