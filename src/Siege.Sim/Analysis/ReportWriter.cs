using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Sim.Running;

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
        AppendNewContent(sb, r.NewContent);
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
        if (r.LinkMode is { } link)
        {
            AppendLinkMode(sb, link);
        }

        // link-map-terrain 5.2：只在样本里有带主题的链接模式局时出现（旧日志的报告逐字节不变）。
        if (r.Themes is { } themes)
        {
            AppendThemes(sb, themes);
        }

        // ai-pass-review 1.2 / 1.3：两段只在链接模式样本里出现（旧日志的报告逐字节不变）。
        if (r.DefeatReview is { } review)
        {
            AppendDefeatReview(sb, review);
        }

        if (r.Waiters is { } waiters)
        {
            AppendWaiters(sb, waiters);
        }

        // ai-terrain-review 1.3 / 1.2：两段追加在最末（缺省报告只多「主题表现」，旧报告前缀逐字节不变）；改造诊断只在 --review-artisan 时出现。
        if (r.Themes is { } performance)
        {
            AppendThemePerformance(sb, performance);
        }

        if (r.ArtisanReview is { } artisan)
        {
            AppendArtisanReview(sb, artisan);
        }

        return sb.ToString();
    }

    /// <summary>链接模式一段（board-link-mode 3.5）：只在样本里有链接模式局时出现，旧日志的报告逐字节不变。</summary>
    private static void AppendLinkMode(StringBuilder sb, LinkModeSection l)
    {
        sb.AppendLine();
        sb.AppendLine("## 链接模式（board-link-mode）");
        sb.AppendLine($"- 纳入 {l.Matches} 局，其中截断 {l.Truncated} 局；终局原因：{Histogram(l.EndReasons)}");
        sb.AppendLine($"- 同区率（锁定时有棋盘旗数 ≥ 2）：{l.SharedMatches}/{l.Matches}（{Pct(l.SharedShare)}）；开局布局：{Histogram(l.Layouts)}");
        sb.AppendLine($"- 首轮配对分布（棋盘编号 1–5，\"等\" = 等待者）：{Histogram(l.FirstRoundPairings)}");
        sb.AppendLine($"- 各阶段平均大回合数：构筑 {Num(l.BuildRounds)}，首轮交战 {Num(l.FirstFightRounds)}，决赛 {Num(l.FinalRounds)}（到达决赛 {l.FinalMatches} 局）");
        sb.AppendLine($"- 链接（拼接）{l.Links} 次，共享边长度分布：{Histogram(l.SharedEdges)}；整体越界平移 {l.GlobalShifts} 次");
        sb.AppendLine($"- 决赛合并战场可落子格数（每局一个）：{Histogram(l.FinalBattlefieldCells)}；终局全图可落子格均值 {Num(l.EndPlayableCells)}");
        sb.AppendLine($"- 等待大回合数：全部玩家均值 {Num(l.WaitingMean)}，等待过的 {l.Waiters} 人次均值 {Num(l.WaitingMeanOfWaiters)}，最大 {l.WaitingMax}");
        sb.AppendLine($"- 出局：战场落败 {l.BattlefieldDefeats} 人次，势力归零 {l.PowerZeroEliminations} 人次；整轮 Pass 结算落败 {l.AllPassedDefeats} 人次，其中过早 Pass 落败（结算那一大回合本人 Pass 且手牌仍有子；未逐格核对禁入与部署上限，为上界）{l.PrematurePassDefeats} 人次");
        sb.AppendLine($"- 单个小回合耗时：中位 {Num(l.TurnMedianMs)} ms，最大 {l.TurnMaxMs} ms；整局耗时合计 {l.TotalMatchMs} ms");
    }

    /// <summary>按主题统计（link-map-terrain 5.2）：每个主题一行，归属口径写进段首。</summary>
    private static void AppendThemes(StringBuilder sb, ThemeSection t)
    {
        sb.AppendLine();
        sb.AppendLine("## 按主题统计（link-map-terrain）");
        sb.AppendLine($"- 纳入带主题的链接模式 {t.Matches} 局，其中有名次 {t.RankedMatches} 局。口径：名次与胜场按玩家锁定的出生棋盘归主题，只计有名次的局（截断局只计锁定人次）；"
            + "匠人改造按目标所在棋盘归主题（立栅取第一个落在棋盘内的一端）；公共信物按所在棋盘归主题，\"被控制\"只数变为有人控制的次数");
        foreach (ThemeStat s in t.Themes)
        {
            string edits = s.EditTotal == 0
                ? "0 次"
                : $"{s.EditTotal} 次（{string.Join(" / ", s.Edits.Select(kv => $"{EditActionName(kv.Key)} {kv.Value}"))}）";
            sb.AppendLine($"- {s.Theme.DisplayName()}：棋盘 {s.Boards} 块，锁定 {s.Seats} 人次（有名次 {s.RankedSeats} 人次），平均名次 {Num(s.MeanRank)}，胜 {s.Wins}；"
                + $"匠人改造 {edits}；公共信物 {s.PublicRelics} 个，被控制 {s.PublicControls} 次");
        }
    }

    /// <summary>战场落败复盘（ai-pass-review 1.2）：汇总在前、逐人明细在后。口径写进报告：最好一手 = AI 自己的单点排序第一名，"本可翻盘"假设对手不回应。</summary>
    private static void AppendDefeatReview(StringBuilder sb, DefeatReviewSection d)
    {
        sb.AppendLine();
        sb.AppendLine("## 战场落败复盘（ai-pass-review：整轮 Pass 结算的落败者，复盘其结算那一大回合最后一次 Pass；按日志回放到该时刻，用其自身 AI 配置的候选、预演与评价）");
        sb.AppendLine($"- 涉及 {d.Logs} 局，整轮 Pass 结算落败 {d.Defeats} 人次，复盘到 {d.Reviewed} 人次；未能复盘的局：{Histogram(d.SkippedLogs)}；找不到本人 Pass 快照 {d.MissingPass} 人次");
        sb.AppendLine($"- 复盘时实际停手阈值分布：{Histogram(d.Thresholds)}；复盘结论与实际 Pass 一致 {d.Consistent}/{d.Reviewed}");
        sb.AppendLine($"- 决胜一手（ai-pass-review 2.1：本将 Pass、该 Pass 会让本战场整轮 Pass 结算、自己落后时改落可追平 / 反超的批次）：触发 {d.Decisive.Count} 次"
            + $"（单点 {d.Decisive.Count(x => !x.Entry.Move.FromForcedBatch)} / 强制批次 {d.Decisive.Count(x => x.Entry.Move.FromForcedBatch)}，涉及 {d.Decisive.Select(x => (x.Entry.Seed, x.Entry.Player)).Distinct().Count()} 人）；"
            + $"其后该玩家赢下所在战场下一次结算 {d.Decisive.Count(x => x.WonBattlefield == true)} 次，输 {d.Decisive.Count(x => x.WonBattlefield == false)} 次，无后续结算 {d.Decisive.Count(x => x.WonBattlefield is null)} 次");
        foreach (DecisiveOutcome x in d.Decisive)
        {
            DecisiveMove m = x.Entry.Move;
            sb.AppendLine($"  - 决胜 {x.Entry.Seed} R{x.Entry.MajorRound} P{x.Entry.Player} T{x.Entry.Turn}：{(m.FromForcedBatch ? "强制批次" : "单点")} {Moves(m.Outcome)} 收益 {m.Outcome.Gain}；"
                + $"势力 {m.PowerBefore} vs {m.RivalBefore} → {m.Outcome.PowerAfter} vs {Opt(m.Outcome.RivalAfter)}；下一次结算 {(x.WonBattlefield is { } won ? (won ? "胜" : "负") : "无")}");
        }

        sb.AppendLine($"- 本可翻盘（最好一手落下、对手不回应时己方势力 ≥ 本战场最高者）：{d.Comebacks}/{d.Reviewed}（{Pct(Share(d.Comebacks, d.Reviewed))}）；"
            + $"合法候选中存在单点翻盘手 {d.AnyComebacks}/{d.Reviewed}；强制落子批次（同一排序前 N 的贪心、合法即保留，直到部署上限）可翻盘 {d.ForcedComebacks}/{d.Reviewed}");
        sb.AppendLine($"- 能提子：最好一手提子 {d.BestCaptures}/{d.Reviewed}；合法候选中存在提子手 {d.AnyCaptures}/{d.Reviewed}");
        sb.AppendLine($"- 无任何可落的一手：{d.NoMove.Values.Sum()}/{d.Reviewed}（{Histogram(d.NoMove)}）；其中落选原因（无合法候选者的完整枚举落选单点合计）：{Histogram(d.NoMoveRejections)}");
        sb.AppendLine($"- 最好一手收益（与停手阈值同口径：单点总分 − Pass 总分；有合法候选 {d.BestGains.Count} 人次）分位数：{Quantiles(d.BestGains)}");
        sb.AppendLine($"  - 分段：{Histogram(GainBands(d.BestGains))}");
        sb.AppendLine($"- 复盘时势力差（本战场最高者 − 己方）分位数：{Quantiles(d.GapsBefore)}");
        sb.AppendLine("- 逐人明细（种子 / 大回合 / 战场（出生棋盘编号）/ 玩家 / 小回合；阈值；势力 己方 vs 最高；合法候选（空格 / 枚举格）；最好一手 收益 / 提子 / 落下后势力 vs 最高 / 翻盘；强制批次；提子候选 / 翻盘候选）：");
        foreach (DefeatReviewEntry e in d.Entries)
        {
            BestMoveReview v = e.Review;
            DefeatTarget t = e.Target;
            string best = v.Best is { } b ? $"{Moves(b)} 收益 {b.Gain} 提子 {b.Captures} 落下后 {b.PowerAfter} vs {Opt(b.RivalAfter)} 翻盘 {YesNo(b.ComesBack)}" : $"无（{v.NoMove}）";
            string forced = v.Forced is { } f ? $"{Moves(f)} 收益 {f.Gain} 提子 {f.Captures} 落下后 {f.PowerAfter} vs {Opt(f.RivalAfter)} 翻盘 {YesNo(f.ComesBack)}" : "无";
            sb.AppendLine($"  - {e.Seed} R{t.MajorRound} 战场{BirthZoneLabel.Number(t.Battlefield)} P{t.Player} T{t.Turn}：阈值 {v.PassThreshold}；势力 {v.PowerBefore} vs {Opt(v.RivalBefore)}；"
                + $"候选 {v.Candidates}（空格 {v.LegalCells} / 枚举格 {v.CandidateCells}，库存 {v.Stock}，上限 {v.DeployLimit}）；最好 {best}；强制 {forced}；提子候选 {v.CaptureCandidates}，翻盘候选 {v.ComebackCandidates}；落选 {(v.Rejections.IsEmpty ? "无" : string.Join("，", v.Rejections.Select(kv => $"{kv.Key}×{kv.Value}")))}");
        }
    }

    /// <summary>等待者评估（ai-pass-review 1.3）：等待者 vs 首轮刚结算的胜者，决赛胜率、决赛开始势力差、等待大回合数分布。</summary>
    private static void AppendWaiters(StringBuilder sb, WaiterSection w)
    {
        sb.AppendLine();
        sb.AppendLine("## 等待者评估（ai-pass-review：决赛参与者按进入决赛时的等待大回合数分组；等待者 = 等过 ≥ 1 个大回合，首轮刚结算的胜者 = 0）");
        sb.AppendLine($"- 纳入到达决赛、未截断的 {w.Finals} 局，决赛参与者 {w.Participants.Count} 人次");
        foreach ((FinalistGroup group, string name) in new[] { (FinalistGroup.Waiter, "等待者"), (FinalistGroup.FreshWinner, "首轮刚结算的胜者") })
        {
            List<FinalistEntry> g = [.. w.Participants.Where(p => p.Group == group)];
            int wins = g.Count(p => p.Won);
            sb.AppendLine($"- {name}：{g.Count} 人次，决赛胜 {wins}（{Pct(Share(wins, g.Count))}）；决赛开始势力差（己方 − 其余决赛者最高）均值 {Num(Statistics.Mean(g.Select(p => (double)p.PowerGap)))}，"
                + $"中位 {Num(Statistics.Median(g.Select(p => (double)p.PowerGap)))}；等待大回合数分布：{Histogram(new SortedDictionary<int, int>(g.GroupBy(p => p.WaitingRounds).ToDictionary(x => x.Key, x => x.Count())))}");
        }

        sb.AppendLine($"- 等待者来由：{Histogram(new SortedDictionary<string, int>(w.Participants.Where(p => p.Group == FinalistGroup.Waiter).GroupBy(p => p.Origin.ToString()).ToDictionary(x => x.Key, x => x.Count()), StringComparer.Ordinal))}");
        sb.AppendLine("- 逐人明细（种子 / 玩家 / 分组 / 来由 / 等待大回合 / 决赛开始势力 / 势力差 / 决赛胜）：");
        foreach (FinalistEntry p in w.Participants)
        {
            sb.AppendLine($"  - {p.Seed} P{p.Player} {(p.Group == FinalistGroup.Waiter ? "等待者" : "首轮胜者")} {p.Origin} 等 {p.WaitingRounds} 势力 {p.Power} 差 {p.PowerGap} {(p.Won ? "胜" : "负")}");
        }
    }

    /// <summary>主题表现（ai-terrain-review 1.3）：按玩家锁定的出生棋盘主题分组，首轮战场胜率、名次均值、第 3 大回合结束时势力、该主题棋盘上的改造次数与类别。</summary>
    private static void AppendThemePerformance(StringBuilder sb, ThemeSection t)
    {
        sb.AppendLine();
        sb.AppendLine("## 主题表现（ai-terrain-review：按玩家锁定的出生棋盘主题分组，同区多人各计）");
        sb.AppendLine($"- 纳入带主题的链接模式 {t.Matches} 局（有名次 {t.RankedMatches} 局）。口径：首轮战场 = 该玩家出现在名单里的第一次战场结算、且早于决赛配对事件；未经结算即在决赛配对前出局（势力归零等）记首轮落败；"
            + "决赛前既未经历结算也未出局者（3+1 单人棋盘、首轮未配对的等待者、截断前未结算）单列「无首轮」、不进分母；名次只计有名次的局；"
            + $"第 {ThemeAnalyzer.Round3} 大回合结束时 = 该大回合最后一条快照，势力名次 = 本局四人里的名次（1 最高，并列取高）；改造按目标所在棋盘归主题（同「按主题统计」）");
        foreach (ThemeStat s in t.Themes)
        {
            string edits = string.Join(" / ", ArtisanEditReview.Kinds.Select(k => $"{EditActionName(k.ToString())} {s.Edits.GetValueOrDefault(k.ToString())}"));
            sb.AppendLine($"- {s.Theme.DisplayName()}：锁定 {s.Seats} 人次；首轮战场胜 {s.FirstFieldWins}/{s.FirstFieldSeats}（{Pct(Share(s.FirstFieldWins, s.FirstFieldSeats))}；其中未经结算即出局 {s.FirstFieldEliminated}），无首轮 {s.NoFirstField}；"
                + $"平均名次 {Num(s.MeanRank)}（有名次 {s.RankedSeats} 人次），胜 {s.Wins}；"
                + $"第 {ThemeAnalyzer.Round3} 大回合结束时势力 均值 {Num(Statistics.Mean(s.Round3Powers.Select(v => (double)v)))}，中位 {Num(Statistics.Median(s.Round3Powers.Select(v => (double)v)))}，"
                + $"范围 {(s.Round3Powers.Count == 0 ? "无样本" : $"{s.Round3Powers[0]}–{s.Round3Powers[^1]}")}，势力名次均值 {Num(Statistics.Mean(s.Round3PowerRanks.Select(v => (double)v)))}（样本 {s.Round3Powers.Count}）；"
                + $"该主题棋盘上的匠人改造 {s.EditTotal} 次（{edits}）");
        }
    }

    /// <summary>
    /// AI 改造诊断（ai-terrain-review 1.2）：汇总在前、按主题与逐次明细在后。口径写进段首：三层计数、处境判定次序、收益口径、实际选择取自日志。
    /// </summary>
    private static void AppendArtisanReview(StringBuilder sb, ArtisanReviewSection a)
    {
        List<ArtisanReviewItem> items = [.. a.Items];
        sb.AppendLine();
        sb.AppendLine("## AI 改造诊断（ai-terrain-review：每次 AI 匠人可改造的时刻 = 手里有匠人、部署上限 > 0 的 AI 部署；按日志回放到该时刻，用其自身 AI 配置的候选格预筛、单点枚举、预演、活形硬约束与评价）");
        sb.AppendLine("- 口径：① 规则合法目标 = 合法落子范围内全部可落子空格上的（匠人落点, 目标）对，经唯一实现 TerrainEditRules.LegalTargets，不预演；② 其中落点进入完整枚举（候选格上限预筛之后）的对数；"
            + "③ 合法候选 = ② 里经暂放、预演与活形硬约束留下的匠人单点。收益 = 单点加权总分 − Pass 加权总分（与停手阈值同口径）；名次 = 在全部合法单点排序（未截断）里的名次，贪心组批只看前 N。"
            + "处境按次序取第一条：无合法目标（① = 0）→ 候选生成阶段排除（① > 0、② = 0：目标所在格全被预筛挡掉）→ 候选全部落选（③ = 0）→ 排在前 N 之外 → 落后于第一名 → 第一名。"
            + "预筛外 = ① − ② 那些格上的搭桥 / 烧林经同一打分路径的结果（立栅不做）。实际选择取自原日志该小回合已确认的批次");
        sb.AppendLine($"- 涉及 {a.Logs} 局，未能复盘的局：{Histogram(a.SkippedLogs)}；匠人可改造时刻 {items.Count} 次");
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine($"- 实际选择分布：{Histogram(Count(items.Select(i => ChoiceText(i.Entry.Choice))))}；实际改造：{string.Join(" / ", ArtisanEditReview.Kinds.Select(k => $"{EditActionName(k.ToString())} {items.Sum(i => i.Entry.Choice.Count(c => c.ToString() == k.ToString()))}"))}");
        sb.AppendLine($"- 单点排序第一名的类别：{Histogram(Count(items.Select(i => TopText(i.Entry.Review))))}；第一名收益分位数：{Quantiles([.. items.Where(i => i.Entry.Review.Top is not null).Select(i => i.Entry.Review.Top!.Gain).Order()])}");
        List<ArtisanReviewItem> withNone = [.. items.Where(i => i.Entry.Review.NoEdit is not null)];
        sb.AppendLine($"- 匠人不改造：有合法候选 {withNone.Count}/{items.Count} 次，最好一手收益分位数：{Quantiles([.. withNone.Select(i => i.Entry.Review.NoEdit!.Gain).Order()])}");
        foreach (TerrainEditKind kind in ArtisanEditReview.Kinds)
        {
            string name = EditActionName(kind.ToString());
            List<ArtisanKindReview> ks = [.. items.Select(i => i.Entry.Review.Of(kind))];
            SortedDictionary<string, int> statuses = Count(items.Select(i => StatusText(i.Entry.Review.StatusOf(kind))));
            int behind = items.Count(i => i.Entry.Review.StatusOf(kind) is ArtisanKindStatus.OutsideTopN or ArtisanKindStatus.Behind);
            int behindNotChosen = items.Count(i => i.Entry.Review.StatusOf(kind) is ArtisanKindStatus.OutsideTopN or ArtisanKindStatus.Behind && !i.Entry.Choice.Any(c => c.ToString() == kind.ToString()));
            sb.AppendLine($"- {name}：处境 {Histogram(statuses)}；无合法目标 {items.Count(i => i.Entry.Review.StatusOf(kind) == ArtisanKindStatus.NoTarget)} 次，"
                + $"候选生成阶段排除 {items.Count(i => i.Entry.Review.StatusOf(kind) == ArtisanKindStatus.ExcludedByPrefilter)} 次，有目标但评价落后（前 N 之外 + 落后于第一名）{behind} 次（其中实际未选{name} {behindNotChosen} 次）");
            sb.AppendLine($"  - ① 规则合法目标对数分位数：{Quantiles([.. ks.Select(k => (BigInteger)k.RangeTargets).Order()])}；② 进入枚举：{Quantiles([.. ks.Select(k => (BigInteger)k.EnumeratedTargets).Order()])}；"
                + $"③ 合法候选数：{Quantiles([.. ks.Select(k => (BigInteger)k.Candidates).Order()])}");
            List<ArtisanReviewItem> withBest = [.. items.Where(i => i.Entry.Review.Of(kind).Best is not null)];
            sb.AppendLine($"  - 最好一手收益分位数（有合法候选 {withBest.Count} 次）：{Quantiles([.. withBest.Select(i => i.Entry.Review.Of(kind).Best!.Gain).Order()])}；"
                + $"名次分位数：{Quantiles([.. withBest.Select(i => (BigInteger)i.Entry.Review.Of(kind).Best!.Rank!.Value).Order()])}");
            List<ArtisanReviewItem> paired = [.. withBest.Where(i => i.Entry.Review.NoEdit is not null)];
            sb.AppendLine($"  - 最好一手 − 匠人不改造最好一手（两者都有 {paired.Count} 次）：{Quantiles([.. paired.Select(i => i.Entry.Review.Of(kind).Best!.Gain - i.Entry.Review.NoEdit!.Gain).Order()])}");
            List<ImmutableArray<BigInteger>> effects = [.. ks.Where(k => !k.EditEffect.IsEmpty).Select(k => k.EditEffect)];
            sb.AppendLine($"  - 改造本身的加权效果（最好一手 − 同一落点匠人不改造，逐维加权贡献之差的均值；{effects.Count} 次）："
                + (effects.Count == 0 ? "无样本" : string.Join("，", Enum.GetValues<EvaluationDimension>().Select(d => $"{d} {Num(effects.Average(e => (double)e[(int)d]))}")))
                + (effects.Count == 0 ? string.Empty : $"；合计 {Num(effects.Average(e => (double)e.Aggregate(BigInteger.Zero, (x, y) => x + y)))}"));
            if (kind != TerrainEditKind.Fence)
            {
                List<ArtisanReviewItem> outside = [.. items.Where(i => i.Entry.Review.Of(kind).RangeTargets > i.Entry.Review.Of(kind).EnumeratedTargets)];
                List<ArtisanReviewItem> outsideBest = [.. outside.Where(i => i.Entry.Review.Of(kind).OutsideBest is not null)];
                int beatsTop = outsideBest.Count(i => i.Entry.Review.Top is { } top && i.Entry.Review.Of(kind).OutsideBest!.Gain > top.Gain);
                sb.AppendLine($"  - 预筛外：有预筛外目标 {outside.Count} 次，其中有合法候选 {outsideBest.Count} 次（候选合计 {outside.Sum(i => i.Entry.Review.Of(kind).OutsideCandidates ?? 0)}）；"
                    + $"预筛外最好一手收益分位数：{Quantiles([.. outsideBest.Select(i => i.Entry.Review.Of(kind).OutsideBest!.Gain).Order()])}；高于当时第一名收益 {beatsTop} 次");
            }
        }

        sb.AppendLine("- 按出生棋盘主题（被诊断玩家锁定的棋盘）：");
        foreach (IGrouping<BoardTheme?, ArtisanReviewItem> g in items.GroupBy(i => i.Theme).OrderBy(g => g.Key is { } k ? (int)k : int.MaxValue))
        {
            string theme = g.Key is { } th ? th.DisplayName() : "无主题";
            string kinds = string.Join("；", ArtisanEditReview.Kinds.Select(k => $"{EditActionName(k.ToString())} {Histogram(Count(g.Select(i => StatusText(i.Entry.Review.StatusOf(k)))))}"));
            sb.AppendLine($"  - {theme}：{g.Count()} 次；实际选择 {Histogram(Count(g.Select(i => ChoiceText(i.Entry.Choice))))}；{kinds}");
        }

        sb.AppendLine("- 逐次明细（种子 / 大回合 / 玩家 / 小回合（主题）；阈值；匠人库存 / 部署上限；空格 / 枚举格；实际选择；第一名；匠人不改造；三类 ①/②/③ 最好一手 收益（名次）处境，搭桥 / 烧林另列预筛外）：");
        foreach (ArtisanReviewItem i in items)
        {
            ArtisanEditReview v = i.Entry.Review;
            string kinds = string.Join("；", v.KindReviews.Select(k =>
                $"{EditActionName(k.Kind.ToString())} {k.RangeTargets}/{k.EnumeratedTargets}/{k.Candidates} {OptionText(k.Best)} {StatusText(v.StatusOf(k.Kind))}"
                + (k.OutsideCandidates is { } n ? $" 预筛外 {n} {OptionText(k.OutsideBest)}" : string.Empty)));
            sb.AppendLine($"  - {i.Entry.Seed} R{i.Entry.MajorRound} P{v.Player.Value} T{i.Entry.Turn}（{(i.Theme is { } th ? th.DisplayName() : "无主题")}）：阈值 {v.PassThreshold}；匠人 {v.ArtisanStock} / 上限 {v.DeployLimit}；"
                + $"空格 {v.LegalCells} / 枚举格 {v.CandidateCells}；选择 {ChoiceText(i.Entry.Choice)}；第一名 {OptionText(v.Top)}；不改造 {OptionText(v.NoEdit)}；{kinds}");
        }
    }

    private static SortedDictionary<string, int> Count(IEnumerable<string> keys)
    {
        var h = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            h[key] = h.GetValueOrDefault(key) + 1;
        }

        return h;
    }

    private static string OptionText(ArtisanOption? o) =>
        o is null ? "无" : $"[{o.Placement}] {o.Gain}{(o.Rank is { } r ? $"（#{r}）" : string.Empty)}";

    private static string ChoiceText(IEnumerable<ArtisanChoice> choices) => string.Join("+", choices.Select(c => c switch
    {
        ArtisanChoice.Pass => "Pass",
        ArtisanChoice.NoArtisan => "未落匠人",
        ArtisanChoice.ArtisanNoEdit => "匠人不改造",
        _ => EditActionName(c.ToString()),
    }));

    private static string TopText(ArtisanEditReview v) =>
        v.Top is not { } top ? "无合法单点"
        : top.Placement.Type != TerrainEditRules.EditorType ? "其他类型"
        : top.Placement.Edit is { } edit ? EditActionName(edit.Kind.ToString())
        : "匠人不改造";

    private static string StatusText(ArtisanKindStatus status) => status switch
    {
        ArtisanKindStatus.NoTarget => "a.无合法目标",
        ArtisanKindStatus.ExcludedByPrefilter => "b.预筛排除",
        ArtisanKindStatus.AllRejected => "c.全部落选",
        ArtisanKindStatus.OutsideTopN => "d.前N之外",
        ArtisanKindStatus.Behind => "e.落后于第一名",
        ArtisanKindStatus.Leading => "f.第一名",
        _ => status.ToString(),
    };

    private static double Share(int part, int whole) => whole == 0 ? double.NaN : (double)part / whole;

    private static string Opt(BigInteger? value) => value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "—";

    private static string YesNo(bool? value) => value switch { true => "是", false => "否", null => "—" };

    private static string Moves(MoveOutcome outcome) => $"[{string.Join(" ", outcome.Placements)}]";

    /// <summary>最近秩分位数（不插值、不经浮点）：最小 / P10 / P25 / 中位 / P75 / P90 / 最大。</summary>
    private static string Quantiles(IReadOnlyList<BigInteger> sorted)
    {
        if (sorted.Count == 0)
        {
            return "无样本";
        }

        int n = sorted.Count;
        BigInteger At(int percent) => sorted[Math.Max(0, ((percent * n) + 99) / 100 - 1)];
        return $"最小 {sorted[0]}，P10 {At(10)}，P25 {At(25)}，中位 {At(50)}，P75 {At(75)}，P90 {At(90)}，最大 {sorted[^1]}";
    }

    /// <summary>收益分段：相对停手阈值（缺省 20 / 落后者 0）看"差多少"。</summary>
    private static SortedDictionary<string, int> GainBands(IReadOnlyList<BigInteger> gains)
    {
        var h = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (BigInteger g in gains)
        {
            string band = g <= -500 ? "a.≤-500" : g <= -100 ? "b.(-500,-100]" : g <= -20 ? "c.(-100,-20]" : g < 0 ? "d.(-20,0)" : g == 0 ? "e.=0" : g <= 20 ? "f.(0,20]" : "g.>20";
            h[band] = h.GetValueOrDefault(band) + 1;
        }

        return h;
    }

    /// <summary>
    /// 第 12 项：新棋子与新信物（more-pieces-relics D12）。v1 对局只单列"不适用"、不进任何比例；有 v2 对局时六行来源、驿站、工坊逐行给出，0 也不省略。
    /// 本段各行连续、不含空行（报告回归测试据此把它整段切出）。
    /// </summary>
    private static void AppendNewContent(StringBuilder sb, NewContentSection n)
    {
        sb.AppendLine("### 12. 新棋子与新信物（more-pieces-relics：终局快照、参赛玩家的全部棋串；驿站按每条小回合快照；内容集 v1 的对局单列不适用）");
        sb.AppendLine($"- 内容集 v2 纳入 {n.Matches} 局；内容集 v1（含首部缺内容集的旧日志）{n.NotApplicable} 局：本项不适用，不计入下列各项");
        if (n.Matches == 0)
        {
            return;
        }

        sb.AppendLine($"- 终局位置加值合计 {n.FinalPositionBonus}，其中：");
        foreach (NewSourceShare s in n.Sources)
        {
            string note = s.Name switch
            {
                "连营" => "（并入连珠的额外加值）",
                "犄角" => "（并入协同的额外加值）",
                _ => string.Empty,
            };
            sb.AppendLine($"  - {s.Name}{note}：{s.Bonus}（{Pct(s.Share)}）");
        }

        sb.AppendLine($"- 驿站平均展示数加成：每小回合 {Num(n.MeanRelayBonus)}（样本 {n.RelayTurns} 个小回合）；有驿站加成的小回合 {n.TurnsWithRelay} 个，平均 {Num(n.MeanRelayBonusWhenPresent)}");
        sb.AppendLine($"- 经工坊扩展的改造 {n.WorkshopEdits} 次，占全部改造 {n.Edits} 次的 {Pct(n.WorkshopShare)}");
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
