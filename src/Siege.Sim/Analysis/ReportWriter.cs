using System.Numerics;
using System.Text;
using Siege.Core.Scoring;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。

/// <summary>把 <see cref="BalanceReport"/> 渲染成纯文本报告：§16 七项目标（第 7 项为截断率）+ §17 各方向各一段（含第 8 项终局原因分布）、收敛单独一段、旁证警告置顶。</summary>
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
        EndingSection e = r.Ending;
        sb.AppendLine($"胜率 / 名次类指标排除截断局 {e.Truncated} 局，有效样本 {e.Ranked} 局（被小回合数截断的局没有名次与胜者，restore-go-core-rules D5）。");
        sb.AppendLine();

        sb.AppendLine("## AI 决策质量旁证");
        AiQualitySection q = r.AiQuality;
        sb.AppendLine($"- 已确认批次 {q.SettledBatches}，提子 {q.Captures}，平均每批次提子 {Num(q.MeanCapturesPerBatch)}");
        sb.AppendLine($"- 自杀手尝试率 {q.SuicideAttemptRate}（阈值 {BalanceAnalyzer.SuicideUnreliableThreshold * 100:F0}%）");
        sb.AppendLine($"- Pass 率 {q.PassRate}（阈值 {BalanceAnalyzer.PassUnreliableThreshold * 100:F0}%）");
        sb.AppendLine($"- {q.Verdict}");
        sb.AppendLine();

        sb.AppendLine("## 收敛情况（未以规则级原因终局 = 旧日志达大回合上限 MajorRoundLimit / 跑局层截断 turn_limit）");
        ConvergenceSection c = r.Convergence;
        sb.AppendLine($"- 规则级终局 {c.Converged} 局；未收敛（达上限 / 截断）{c.Capped} 局，不收敛率 {c.CappedRate}");
        sb.AppendLine($"- 终局原因：{Histogram(c.Reasons)}");
        sb.AppendLine($"- 平均大回合数：终局局 {Num(c.MeanMajorRoundsConverged)}，全部局 {Num(c.MeanMajorRoundsAll)}；平均小回合数 {Num(c.MeanTurnsPerMatch)}");
        sb.AppendLine();

        sb.AppendLine("## §16 数值目标回归");
        TargetsSection t = r.Targets;
        sb.AppendLine("### 1. 部署上限分阶段分布（growth-pass-1：基础值按大回合 3 / 4 / 5，军令在其上叠加；目标中位数 3 / 3–5 / 5–8）");
        sb.AppendLine($"- 第 1–3 大回合：{Histogram(t.DeployLimitRounds1To3)}；中位数 {t.DeployPhase1}");
        sb.AppendLine($"- 第 4–6 大回合：{Histogram(t.DeployLimitRounds4To6)}；中位数 {t.DeployPhase2}");
        sb.AppendLine($"- 第 7 大回合以后：{Histogram(t.DeployLimitRounds7Plus)}；中位数 {t.DeployPhase3}（允许极端构筑超过 8）");
        sb.AppendLine("### 2. 势力成长曲线");
        foreach ((int round, double mean, double territory, BigInteger maxGroup, int samples) in t.PowerCurve)
        {
            string split = double.IsNaN(territory) ? "领地分无样本（旧日志）" : $"其中领地分 {Num(territory)}、棋串军势 {Num(mean - territory)}";
            sb.AppendLine($"- 第 {round} 大回合结束：参赛玩家平均势力 {Num(mean)}（{split}），最高单串军势 {maxGroup}（样本 {samples}）");
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
        sb.AppendLine($"- 终局局的结束大回合：中位 {MedianOf(t.EndRounds)}，最长 {(t.EndRounds.Count == 0 ? "无样本" : t.EndRounds.Keys.Max().ToString(System.Globalization.CultureInfo.InvariantCulture))}");
        sb.AppendLine($"- 整局时长 {t.MatchMinutes}：不测（裁决 10）。代理：平均每局 {Num(r.Convergence.MeanTurnsPerMatch)} 个小回合，AI 计算耗时 {Num(t.MeanAiMsPerMatch)} ms/局");
        MapScaleSection ms = r.MapScale;
        string playableSkip = ms.PlayableSkipped > 0 ? $"未记可落子格的旧日志 {ms.PlayableSkipped} 局" : string.Empty;
        sb.AppendLine(
            $"- 本批地图规模（对局时长的调节手段，取自日志首部）：地图可落子格 {Scale(ms.PlayableMatches, ms.MinPlayable, ms.MaxPlayable, ms.MeanPlayable, playableSkip)}，"
            + $"信物格 {Scale(ms.RelicMatches, ms.MinRelics, ms.MaxRelics, ms.MeanRelics, string.Empty)}");
        sb.AppendLine("### 6. 第 3 大回合领先者最终胜率（目标 ≤ 50%）");
        LeaderSection l = r.Leader;
        sb.AppendLine($"- 口径 A（并列组内任一人获胜）：{l.AnyOfGroupWins}；{t.LeaderWinRate}");
        sb.AppendLine($"- 口径 B（必须该具体玩家获胜，按领先者逐人计样本）：{l.SpecificPlayerWins}");
        sb.AppendLine($"- 样本 {l.Samples} 局（其中并列 {l.TiedSamples} 局；已排除截断局 {e.Truncated} 局）{(l.EnoughSamples ? "" : $"——不足 {r.Options.RequiredMatches} 局，结论不可靠")}");
        sb.AppendLine("### 7. 以 turn_limit 截断的局数与占比（目标 0；截断是跑局层的技术设施，不是规则终局）");
        sb.AppendLine($"- {TruncatedLine(e)}，平均截断于第 {Num(e.MeanTruncatedRound)} 大回合；{e.TruncationTarget}");
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
        sb.AppendLine($"- 出现过倍增串的局 {m.MatchesWithPeak}；峰值倍增子数分布（即倍率指数，不封顶）{Histogram(m.PeakCountDistribution)}");
        sb.AppendLine($"- 峰值首次出现平均在第 {Num(m.MeanFormationRound)} 大回合，峰值军势平均 {Num(m.MeanPeakPower)}，最高 {m.MaxPeakPower}");
        sb.AppendLine($"- 峰值串之后被摧毁的概率 {m.DestroyedRate}");
        sb.AppendLine("### 4b. 各棋子势力占比（multiplier-rebalance：终局快照、参赛玩家的全部棋串；倍增子计其放大出的部分，其余计基础军势与分得的位置加值；不含高地加值）");
        PieceShareSection ps = r.PieceShares;
        sb.AppendLine($"- 纳入 {ps.Matches} 局，跳过无棋子类型计数的旧日志 / 无快照局 {ps.Skipped} 局；盘面棋子 {ps.TotalStones} 枚，归因势力 {ps.TotalPower}");
        foreach (PieceShare p in ps.Pieces)
        {
            sb.AppendLine($"- 棋子 {p.Type}：盘面 {p.Stones} 枚（{Pct(p.StoneShare)}），势力 {p.Power}（{Pct(p.PowerShare)}），每颗平均 {Num(p.MeanPerStone)}");
        }

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
        sb.AppendLine("### 8. 三类终局原因各自的占比与平均结束大回合（占比分母为全部纳入局；截断局单列）");
        foreach (EndReasonStat reason in e.Reasons)
        {
            sb.AppendLine($"- {EndReasonName(reason.Reason)}（{reason.Reason}）{reason.Count} 局（{Pct(reason.Share)}），平均结束大回合 {Num(reason.MeanEndRound)}");
        }

        sb.AppendLine($"- {TruncatedLine(e)}：不属于三类规则终局，单列");
        if (e.Other > 0)
        {
            sb.AppendLine($"- 其它原因（旧日志的达大回合上限等）{e.Other} 局");
        }

        sb.AppendLine("### 9. 首次跨出生区冲突发生时的盘面占用率（denser-map D5：改图 / 改部署上限后必须复看这条）");
        sb.AppendLine(
            $"- 平均占用率 {Pct(t.MeanFirstConflictOccupancy)}，与冲突大回合 {t.FirstConflict} 并列；"
            + $"分布 {Histogram(t.FirstConflictOccupancy)}；未纳入 {t.MatchesWithoutOccupancy} 局");
        sb.AppendLine("### 10. 领地与高地（终局快照、参赛玩家）");
        TerritoryShareSection ts = r.TerritoryShare;
        sb.AppendLine($"- 终局领地分占参赛玩家总势力：全批次平均 {Pct(ts.MeanShare)}（纳入 {ts.Matches} 局；排除缺领地分字段的旧日志 / 参赛玩家势力为 0 的局 {ts.Skipped} 局）");
        HighGroundSection hg = r.HighGround;
        sb.AppendLine($"- 终局高地加值占全部位置加值：{Pct(hg.HighGroundShare)}（{hg.FinalHighGroundBonus}/{hg.FinalPositionBonus}）");
        sb.AppendLine("### 11. 插旗同区（flag-contest：原型插旗冒险概率；同区 = 日志首部锁定区里有两名及以上玩家同区；只报告）");
        SharedZoneSection sz = r.SharedZones;
        sb.AppendLine($"- 同区对局 {sz.SharedMatches} 局，占纳入局 {sz.Matches} 局的 {Pct(sz.SharedShare)}");
        sb.AppendLine($"- 有名次的同区局 {sz.RankedSharedMatches} 局、同区玩家 {sz.SharedWinRate.Trials} 人次：平均名次 {Num(sz.MeanSharedRank)}，胜率 {sz.SharedWinRate}（截断局不计）");
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
        sb.AppendLine();

        AppendLifeShape(sb, r.LifeShape);
        return sb.ToString();
    }

    /// <summary>活形一段（life-shape 4.2）。口径写在各行里：禁入格占比的分子是受保护眼空间格（至少对一名玩家禁入），分母是当时地形的可落子格。</summary>
    private static void AppendLifeShape(StringBuilder sb, LifeShapeSection l)
    {
        sb.AppendLine("## 活形（life-shape）");
        sb.AppendLine($"- 纳入 {l.Matches} 局，排除缺活形字段的旧日志 {l.Skipped} 局");
        sb.AppendLine($"- 首次活形确立平均第 {Num(l.MeanFirstEstablishedRound)} 大回合（有确立的 {l.MatchesWithLife} 局；整局无确立 {l.MatchesWithoutLife} 局）");
        string byRank = l.FirstRoundByRank.Count == 0
            ? "无样本"
            : string.Join("，", l.FirstRoundByRank.Select(kv => $"第 {kv.Key} 名 {Num(kv.Value.Mean)}（样本 {kv.Value.Samples}）"));
        sb.AppendLine($"- 按终局名次分组（每名玩家取自己最早一次确立；只取有名次的局）：{byRank}；从未确立 {l.PlayersNeverAlive} 人次");
        sb.AppendLine($"- 终局每名玩家平均：已确定活形棋串 {Num(l.MeanFinalAliveGroups)} 条，受保护眼空间 {Num(l.MeanFinalEyeCells)} 格");
        sb.AppendLine($"- 终局禁入格占可落子格：平均 {Pct(l.MeanForbiddenShare)}（分子 = 全部已确定活形的眼空间之并，即至少对一名玩家禁入的格；分母 = 终局地形的可落子格）");
        sb.AppendLine($"- 终局有活形玩家的胜率 {l.WinRateWithLife}；无活形玩家 {l.WinRateWithoutLife}");
        sb.AppendLine($"- 活棋禁入：暂放被拒 {l.ForbiddenStaged}，预演 {l.ForbiddenRehearsed}，确认被拒 {l.ForbiddenRejected}");
        sb.AppendLine($"- 破坏活形：预演 {l.BreaksRehearsed}，确认被拒 {l.BreaksRejected}");
        sb.AppendLine($"- 活形失去按原因：{Histogram(l.LostByCause)}");
        sb.AppendLine($"- 他人致失活（规则缺陷，应为 0）：{l.Defects.Count} 次");
        foreach (LifeDefect d in l.Defects)
        {
            sb.AppendLine($"  - 种子 {d.Seed} 第 {d.Turn} 小回合（第 {d.MajorRound} 大回合）：玩家 {d.Actor} 的批次使玩家 {d.Owner} 的活形失去（代表坐标 {d.At}）");
        }

        sb.AppendLine("### R8：活形过易的两项单列");
        sb.AppendLine($"- 单子活形棋串：终局 {Ratio(l.FinalSingleStoneAlive, l.FinalAliveGroups)}；确立事件 {Ratio(l.EstablishedSingleStone, l.EstablishedTotal)}");
        sb.AppendLine($"- 贴地形小空区（≤ 3 格且贴地形墙）形成的眼空间：终局 {Ratio(l.FinalTerrainSmallEyeSpaces, l.FinalEyeSpaces)}（地形墙 = 盘内几何方向上没有气边：岩石 / 深水 / 崖壁 / 栅栏；棋盘外沿不算）");
    }

    private static string Ratio(int part, int whole) =>
        $"{part}/{whole}（{(whole == 0 ? "无样本" : Pct((double)part / whole))}）";

    /// <summary>直方图（值 → 次数）的中位数；偶数个样本取中间两值的平均。</summary>
    private static string MedianOf(SortedDictionary<int, int> histogram)
    {
        List<double> values = [.. histogram.SelectMany(kv => Enumerable.Repeat((double)kv.Key, kv.Value))];
        return Num(Statistics.Median(values));
    }

    private static string TruncatedLine(EndingSection e) =>
        $"截断（turn_limit）{e.Truncated} 局（{(e.TruncatedRate.IsEmpty ? "无样本" : Pct(e.TruncatedRate.Value))}）";

    private static string EndReasonName(string reason) => reason switch
    {
        nameof(Core.Match.EndReason.LastPlayerStanding) => "只剩一名参赛玩家",
        nameof(Core.Match.EndReason.BoardFull) => "棋盘填满",
        nameof(Core.Match.EndReason.AllPassed) => "整轮 Pass",
        _ => reason,
    };

    /// <summary>范围写法：各局相同只给一个数，否则"最小–最大（平均 …）"；<paramref name="note"/> 附在括号里。</summary>
    private static string Scale(int samples, int min, int max, double mean, string note)
    {
        if (samples == 0)
        {
            return note.Length == 0 ? "无样本" : $"无样本（{note}）";
        }

        if (min == max)
        {
            return note.Length == 0 ? $"{min}" : $"{min}（{note}）";
        }

        return $"{min}–{max}（平均 {Num(mean)}{(note.Length == 0 ? string.Empty : "；" + note)}）";
    }

    private static string EditActionName(string action) => action switch
    {
        nameof(Core.Board.TerrainEditKind.Bridge) => "搭桥",
        nameof(Core.Board.TerrainEditKind.Fence) => "立栅",
        nameof(Core.Board.TerrainEditKind.Burn) => "烧林",
        _ => action,
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
