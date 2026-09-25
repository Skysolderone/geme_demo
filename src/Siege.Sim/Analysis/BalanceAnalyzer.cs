using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Sim.Logging;

namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。它只读日志对象（来自文件），不引用任何对局运行时对象（design.md D6）。

public sealed record AnalysisOptions
{
    /// <summary>是否把调试 AI 局 / 人工接管局也纳入（design.md D7 默认排除）。</summary>
    public bool IncludeContaminated { get; init; }

    /// <summary>胜率类指标的最低样本数（裁决 5：200 局是快速回归下限，默认基线 2000 局）。</summary>
    public int RequiredMatches { get; init; } = 200;
}

/// <summary>收敛情况（裁决 13 → round-cap D5）：非达上限终局 vs 以 <c>MajorRoundLimit</c>（达大回合上限）终局，单独成段。</summary>
public sealed record ConvergenceSection(
    int Converged,
    int Capped,
    Proportion CappedRate,
    SortedDictionary<string, int> Reasons,
    double MeanMajorRoundsConverged,
    double MeanMajorRoundsAll,
    double MeanTurnsPerMatch);

/// <summary>一类规则终局原因：局数、占全部纳入局的比例、平均结束大回合（该类一局都没有时为 NaN，报告照常给出该行）。</summary>
public sealed record EndReasonStat(string Reason, int Count, double Share, double MeanEndRound);

/// <summary>
/// 终局原因分布与截断（restore-go-core-rules：match-telemetry 平衡分析方向 8 + 数值目标回归「截断率」）。
/// <see cref="Reasons"/> 固定三行、按终局优先级（只剩一名 &gt; 棋盘填满 &gt; 整轮 Pass）；截断局（<c>turn_limit</c>）单列，目标 0；
/// 旧日志的「达大回合上限」等其它原因计入 <see cref="Other"/>（它们有规则名次，仍计入胜率类指标）。
/// </summary>
/// <param name="Matches">纳入局数（占比的分母）。</param>
/// <param name="Ranked">有名次的局 = 纳入局 − 截断局：胜率 / 名次类指标的有效样本数。</param>
public sealed record EndingSection(
    int Matches,
    List<EndReasonStat> Reasons,
    int Truncated,
    Proportion TruncatedRate,
    double MeanTruncatedRound,
    Deviation TruncationTarget,
    int Other,
    int Ranked);

/// <summary>
/// 对局时长的调节手段（match-telemetry 数值目标回归「时长与地图规模并列」）：批次地图的可落子格数与信物格数，取自日志首部
/// （<see cref="LogHeader.PlayableCells"/>、<see cref="LogHeader.Relics"/> 条数），不按地图标识重建地图。每局换图时给出范围与均值。
/// </summary>
/// <param name="PlayableSkipped">首部没有可落子格数的旧日志（denser-map 之前）：不进可落子格统计，计数。</param>
public sealed record MapScaleSection(
    int PlayableMatches,
    int PlayableSkipped,
    int MinPlayable,
    int MaxPlayable,
    double MeanPlayable,
    int RelicMatches,
    int MinRelics,
    int MaxRelics,
    double MeanRelics);

/// <summary>
/// 领地分占比（match-telemetry 平衡分析方向 10「领地分占比口径」）：逐局取终局快照（最后一条小回合快照）中参赛玩家（Active）的
/// Σ领地分 ÷ Σ总势力，再对纳入局取平均。任一参赛玩家缺领地分字段（段 E 之前的旧日志）或参赛玩家总势力为 0 的局整局排除并计数，MUST NOT 回填。
/// </summary>
public sealed record TerritoryShareSection(int Matches, int Skipped, double MeanShare);

/// <summary>§16 数值目标（restore-go-core-rules 段 E 起七项：第 7 项截断率在 <see cref="EndingSection"/>，报告里并入 §16）。</summary>
public sealed record TargetsSection(
    SortedDictionary<int, int> DeployLimitRounds1To3,
    SortedDictionary<int, int> DeployLimitRounds4To6,
    SortedDictionary<int, int> DeployLimitRounds7Plus,
    Deviation DeployPhase1,
    Deviation DeployPhase2,
    Deviation DeployPhase3,
    List<(int Round, double MeanPower, double MeanTerritory, BigInteger MaxGroupPower, int Samples)> PowerCurve,
    Deviation PowerOpening,
    Deviation PowerMid,
    SortedDictionary<int, int> FirstConflictRounds,
    int MatchesWithoutConflict,
    Deviation FirstConflict,
    SortedDictionary<int, int> FirstConflictOccupancy,
    double MeanFirstConflictOccupancy,
    int MatchesWithoutOccupancy,
    Deviation MajorRoundMinutes,
    double MeanTurnsPerMajorRound,
    double MeanAiMsPerMajorRound,
    SortedDictionary<int, int> EndRounds,
    Deviation EndRound,
    Deviation MatchMinutes,
    double MeanAiMsPerMatch,
    Deviation LeaderWinRate,
    AiStepTiming AiStep);

/// <summary>
/// AI 单步决策耗时（frontier-map 3.5）。"单步" = 一个小回合：控制者在其中整理手牌、征募、并一次性决定整批部署，是 AI 对外可观察的最小决策单位。
/// 取自 <see cref="TurnSnapshot.ElapsedMs"/>（墙钟，只记录、不参与任何决定、不进确定性文本）；没有耗时的快照（确定性文本读回的日志）不计入。
/// 并行跑局会把墙钟撑大，量耗时的批次应当 <c>--serial</c>。
/// </summary>
public sealed record AiStepTiming(int Samples, double MeanMs, long MaxMs);

/// <summary>第 3 大回合领先者（裁决 3 双口径）。</summary>
public sealed record LeaderSection(
    int Samples,
    int TiedSamples,
    Proportion AnyOfGroupWins,
    Proportion SpecificPlayerWins,
    Proportion NonLeaderWins,
    double Baseline,
    bool EnoughSamples);

/// <summary>先手滚雪球：按下一大回合行动位置统计名次变化。</summary>
public sealed record SnowballSection(
    List<(int Position, int Improved, int Same, int Worse)> RankChangeByPosition,
    Proportion FirstMoverStaysFirst,
    List<(int Position, double MeanFinalRank, int Samples)> FinalRankByRound3Position);

public sealed record PieceStat(string Type, int Offered, int Picked, Proportion SelectionRate, Proportion WinRateOfPickers);

public sealed record RelicStat(string Type, int Count, int Revealed, int ControlledTurns, double MeanControlledTurnsPerRelic, Proportion WinRateOfControllers);

public sealed record SelectionSection(List<PieceStat> Pieces, List<RelicStat> Relics);

/// <summary>§17.4 高倍率棋串。<see cref="PeakCountDistribution"/> 按峰值串的倍增子数量（即倍率指数，不封顶）。均值类统计量是报告口径的近似值；<see cref="MaxPeakPower"/> 是精确整数。</summary>
public sealed record MultiplierSection(
    int MatchesWithPeak,
    SortedDictionary<int, int> PeakCountDistribution,
    double MeanFormationRound,
    double MeanPeakPower,
    BigInteger MaxPeakPower,
    Proportion DestroyedRate);

/// <summary>一种棋子的势力归因：盘面枚数、盘面占比、归因势力、势力占比、每颗平均贡献。</summary>
public sealed record PieceShare(string Type, long Stones, double StoneShare, BigInteger Power, double PowerShare, double MeanPerStone);

/// <summary>
/// 各棋子势力占比（multiplier-rebalance 裁决 3，proposal 表格口径）：逐局取终局快照（最后一条小回合快照）中参赛玩家（<c>Active</c>）的全部棋串；
/// 普通 / 堡垒 / 连珠 / 协同计各自基础军势，连珠与协同再分得本串对应的位置加值；倍增子计各自基础军势 1，再分得本串"放大出来的部分"
/// <c>⌊基础 × 倍率⌋ − 基础</c>（整数，经唯一的 <see cref="Siege.Core.Scoring.Multiplier.Apply"/> 按日志里的生效指数算）。势力占比的分母是纳入局归因势力之和（棋串军势；高地加值不属于任何棋子类型，不计入）。
/// 终局快照里有任何参赛玩家棋串缺 <see cref="GroupEntry.PieceCounts"/>（旧日志）或整局无快照的局计入 <see cref="Skipped"/>，不参与任何分子分母。
/// </summary>
public sealed record PieceShareSection(int Matches, int Skipped, long TotalStones, BigInteger TotalPower, List<PieceShare> Pieces);

/// <param name="Picks">该区被选次数（= 胜率的分母）。出生区多于玩家的图上单列：没人选的平台是 0 次，不是缺行。</param>
public sealed record ZoneStat(int Zone, Proportion WinRate, bool Significant, int Picks);

/// <param name="ZoneCount">地图出生区数：取日志首部 <see cref="LogHeader.ZoneCount"/>；旧日志无该字段时回填为被选到过的最大区号 + 1。</param>
public sealed record BirthZoneSection(
    int ZoneCount,
    double Baseline,
    List<ZoneStat> All,
    List<ZoneStat> RelicsConverged,
    List<ZoneStat> RelicsNotConverged,
    int ConvergedMatches,
    int NotConvergedMatches)
{
    /// <summary>
    /// 按平台边长分组（map-generator D6）：只在每局换图的批次里给出（批内有任何一局的首部配置带 <see cref="Siege.Sim.Config.RunConfig.MapPerMatch"/>），其余为 <c>null</c>。
    /// 每局换图时平台编号跨局不可比（第 3 局的 2 号台与第 4 局的 2 号台不是同一个平台），按编号的胜率没有意义，报告改列这一节。
    /// </summary>
    public PlatformSideSection? BySide { get; init; }
}

/// <param name="Side">平台边长（外接矩形的较长边）。</param>
/// <param name="Offered">该边长的平台在纳入局里一共出现过多少个（每局每个平台计一次）——被选次数的分母，回答"大小即取舍"里有多少人愿意选它。</param>
/// <param name="Picks">被选次数（= 胜率的分母）。</param>
public sealed record SideStat(int Side, int Offered, int Picks, Proportion WinRate, bool Significant);

/// <param name="Matches">纳入局数（首部带各平台边长）。</param>
/// <param name="Skipped">首部没有各平台边长的局（不是每局换图批次的日志混了进来）：整局排除并计数，MUST NOT 按地图标识重建地图去补。</param>
/// <param name="Baseline">基线 = 1 / 参赛人数，口径同各区胜率。</param>
/// <param name="Sides">边长 5–9 各一行（没出现过 / 没人选同样列出），另含样本里出现的其它边长；按边长升序。</param>
public sealed record PlatformSideSection(int Matches, int Skipped, double Baseline, List<SideStat> Sides);

public sealed record GrowthAxisSection(
    int WinnerSamples,
    SortedDictionary<string, int> SequenceCounts,
    SortedDictionary<string, int> FirstAxisCounts,
    string? DominantSequence,
    double DominantShare);

public sealed record StallingSection(int SignalTurns, int TotalTurns, Proportion Ratio);

/// <summary>
/// 高地压制加值在位置加值里的占比（match-telemetry 平衡分析方向 10 的遗留项）。
/// 逐局取终局快照（最后一条小回合快照）中参赛玩家的全部棋串，全批次合并计算；缺高地加值字段的旧日志整局排除。
/// </summary>
/// <param name="HighGroundShare">Σ高地加值 ÷ Σ位置加值（连珠 + 协同 + 高地）。</param>
public sealed record HighGroundSection(
    long FinalHighGroundBonus,
    long FinalPositionBonus,
    double HighGroundShare);

/// <summary>某种改造动作的次数与占比。占比分母是纳入局的改造总次数；一次都没有时如实给出 0 与 0%（规格明令 MUST NOT 省略该行）。</summary>
public sealed record TerrainEditActionStat(string Action, int Count, double Share, int CausedCaptures);

/// <summary>
/// 地形改造分析（match-telemetry 平衡分析方向 11）。
/// 纳入 = 每条小回合快照都有改造字段（<see cref="TurnSnapshot.Edits"/> 非 <c>null</c>）；
/// artisan-terrain-edit 之前的旧日志整局计入 <paramref name="Skipped"/>（R-6），MUST NOT 回填成空表。
/// </summary>
/// <param name="MeanEditsPerMatch">每局平均改造次数（分母 = 纳入局数，含一次都没改造的局）。</param>
/// <param name="Actions">三种动作各自的次数与占比，按枚举顺序（搭桥 / 立栅 / 烧林），0 次也在列。</param>
/// <param name="ArtisansPlaced">纳入局中落盘的匠人总枚数（含不带改造的）。</param>
/// <param name="ArtisansWithEdit">其中带改造的枚数；<paramref name="EditingArtisanShare"/> 是它占已落匠人的比例。</param>
/// <param name="CausedCaptures">直接导致提子的改造次数（口径见 <c>AppliedTerrainEdit.CausedCapture</c>）。</param>
/// <param name="MeanFirstEditRound">首次改造所在大回合的平均；<paramref name="MatchesWithoutEdit"/> 局整局无改造，不进均值。</param>
/// <param name="WinRateOfEditors">样本 = 每局中至少改造过一次的玩家；成功 = 该玩家是本局获胜者。</param>
/// <param name="FinalBridges">终局时本局新增的桥数合计（不含地图预置）。</param>
/// <param name="FinalFences">终局时本局新增的栅栏数合计。</param>
/// <param name="FinalBurns">终局时被烧掉的林地数合计。</param>
public sealed record TerrainEditSection(
    int Matches,
    int Skipped,
    int TotalEdits,
    double MeanEditsPerMatch,
    List<TerrainEditActionStat> Actions,
    int ArtisansPlaced,
    int ArtisansWithEdit,
    double EditingArtisanShare,
    int CausedCaptures,
    double MeanFirstEditRound,
    int MatchesWithoutEdit,
    Proportion WinRateOfEditors,
    int FinalBridges,
    int FinalFences,
    int FinalBurns);

public sealed record AiQualitySection(
    int SettledBatches,
    int Captures,
    double MeanCapturesPerBatch,
    int SuicideAttempts,
    Proportion SuicideAttemptRate,
    Proportion PassRate,
    bool Unreliable,
    string Verdict);

/// <summary>完整平衡报告。</summary>
public sealed record BalanceReport(
    int TotalLogs,
    int Included,
    int ExcludedContaminated,
    int ExcludedFailed,
    int PlayerCount,
    AnalysisOptions Options,
    ConvergenceSection Convergence,
    TargetsSection Targets,
    LeaderSection Leader,
    SnowballSection Snowball,
    SelectionSection Selection,
    MultiplierSection Multiplier,
    PieceShareSection PieceShares,
    BirthZoneSection BirthZones,
    GrowthAxisSection GrowthAxes,
    StallingSection Stalling,
    AiQualitySection AiQuality,
    HighGroundSection HighGround,
    TerrainEditSection TerrainEdits,
    EndingSection Ending,
    MapScaleSection MapScale,
    TerritoryShareSection TerritoryShare,
    LifeShapeSection LifeShape,
    SharedZoneSection SharedZones);

/// <summary>
/// 插旗同区（flag-contest D3，只报告）：同区 = 该局日志首部锁定的出生区（<see cref="LogHeader.Zones"/>）里有两名及以上玩家落在同一个区；
/// 同区玩家 = 与至少一名其他玩家同区的玩家。
/// </summary>
/// <param name="Matches">纳入局数（占比的分母，含截断局——同区是开局属性，与名次无关）。</param>
/// <param name="SharedMatches">有同区的纳入局数。</param>
/// <param name="SharedShare">有同区的局占纳入局的比例（无纳入局时为 NaN）。</param>
/// <param name="RankedSharedMatches">有同区且有名次的局数（截断局没有名次与胜者，不计入名次与胜率）。</param>
/// <param name="MeanSharedRank">同区玩家的平均终局名次（有名次局里每名同区玩家计一次；无样本时为 NaN）。</param>
/// <param name="SharedWinRate">同区玩家的胜率（分母 = 有名次局里的同区玩家人次）。</param>
public sealed record SharedZoneSection(
    int Matches,
    int SharedMatches,
    double SharedShare,
    int RankedSharedMatches,
    double MeanSharedRank,
    Proportion SharedWinRate);

/// <summary>首次活形确立大回合的一个分组（均值与样本数）。</summary>
public sealed record RoundStat(double Mean, int Samples);

/// <summary>一条"非所有者的批次导致活形失去"的记录：规则缺陷（D3 + D4 应使其不可能），报告单列并给出种子与小回合序号。</summary>
public sealed record LifeDefect(string Seed, int Turn, int MajorRound, int Owner, int Actor, string At);

/// <summary>
/// 活形记录与统计（life-shape 4.2，match-telemetry「活形记录与统计」）。缺活形字段的旧日志整局排除并计数（<see cref="Skipped"/>）。
/// 终局口径取每局最后一条快照的 <see cref="LifeTurnEntry"/>；胜率类只取有名次的局。
/// </summary>
public sealed record LifeShapeSection(
    int Matches,
    int Skipped,
    int MatchesWithLife,
    int MatchesWithoutLife,
    double MeanFirstEstablishedRound,
    SortedDictionary<int, RoundStat> FirstRoundByRank,
    int PlayersNeverAlive,
    double MeanFinalAliveGroups,
    double MeanFinalEyeCells,
    double MeanForbiddenShare,
    Proportion WinRateWithLife,
    Proportion WinRateWithoutLife,
    int ForbiddenStaged,
    int ForbiddenRehearsed,
    int ForbiddenRejected,
    int BreaksRehearsed,
    int BreaksRejected,
    SortedDictionary<string, int> LostByCause,
    List<LifeDefect> Defects,
    int FinalAliveGroups,
    int FinalSingleStoneAlive,
    int EstablishedTotal,
    int EstablishedSingleStone,
    int FinalEyeSpaces,
    int FinalTerrainSmallEyeSpaces);

/// <summary>
/// 离线平衡分析（match-telemetry 三条 Requirement）：只读日志，不重跑对局，改口径无需重跑（design.md D6）。
/// </summary>
public static class BalanceAnalyzer
{
    /// <summary>"近乎随机"的旁证阈值：自杀手尝试率 ≥ 25% 或 Pass 率 ≥ 60%（初值，随基线数据校准）。</summary>
    public const double SuicideUnreliableThreshold = 0.25;

    public const double PassUnreliableThreshold = 0.60;

    private static readonly string[] AxisNames = ["供给", "部署", "槽位", "倍率"];

    public static BalanceReport Analyze(IReadOnlyList<MatchLog> logs, AnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(logs);
        options ??= new AnalysisOptions();
        int failed = logs.Count(l => l.IsFailed);
        var included = new List<MatchLog>();
        int contaminated = 0;
        foreach (MatchLog log in logs.Where(l => !l.IsFailed))
        {
            if (log.IsContaminated && !options.IncludeContaminated)
            {
                contaminated++;
                continue;
            }

            included.Add(log);
        }

        int playerCount = included.Count == 0 ? 0 : included[0].Header.Players.Count;

        // 胜率 / 名次类指标的唯一口径（restore-go-core-rules D5）：被小回合数截断的局没有名次与胜者，MUST NOT 计入——
        // 它们不排除就会以"无人获胜"混进分母。凡读 Winners / Standings 的段一律吃这一份 ranked（或按它判定单局是否计胜率样本），
        // 不各写一遍 Truncated 判断；其余统计（部署、势力曲线、选择率、改造次数……）仍用全部纳入局。
        List<MatchLog> ranked = [.. included.Where(l => !l.Result!.Truncated)];
        var rankable = new HashSet<MatchLog>(ranked, ReferenceEqualityComparer.Instance);
        return new BalanceReport(
            logs.Count, included.Count, contaminated, failed, playerCount, options,
            Convergence(included),
            Targets(included, ranked, options),
            Leader(ranked, options),
            Snowball(included, ranked),
            Selection(included, rankable),
            Multiplier(included),
            PieceShares(included),
            BirthZones(included, ranked),
            GrowthAxes(ranked),
            Stalling(included),
            AiQuality(included),
            HighGround(included),
            TerrainEdits(included, rankable),
            Ending(included, ranked.Count),
            MapScale(included),
            TerritoryShare(included),
            LifeShape(included, rankable),
            SharedZones(included, ranked));
    }

    // ---------- 插旗同区（flag-contest D3） ----------

    /// <param name="logs">全部纳入局：同区对局数与占比（同区是开局属性，截断局照样计）。</param>
    /// <param name="ranked">有名次的局：同区玩家的名次与胜率（截断局没有名次与胜者，MUST NOT 以"未胜"计入）。</param>
    private static SharedZoneSection SharedZones(List<MatchLog> logs, List<MatchLog> ranked)
    {
        int shared = logs.Count(l => SharedZonePlayers(l).Count > 0);
        int rankedShared = 0, wins = 0;
        var ranks = new List<double>();
        foreach (MatchLog log in ranked)
        {
            List<int> players = SharedZonePlayers(log);
            if (players.Count == 0)
            {
                continue;
            }

            rankedShared++;
            foreach (int player in players)
            {
                StandingEntry standing = log.Result!.Standings.FirstOrDefault(s => s.Player == player)
                    ?? throw new FormatException($"种子 {log.Header.Seed}：有名次的局里同区玩家 P{player} 没有终局名次。");
                ranks.Add(standing.Rank);
                wins += log.Result.Winners.Contains(player) ? 1 : 0;
            }
        }

        return new SharedZoneSection(
            logs.Count,
            shared,
            logs.Count == 0 ? double.NaN : (double)shared / logs.Count,
            rankedShared,
            Statistics.Mean(ranks),
            Statistics.Wilson(wins, ranks.Count));
    }

    /// <summary>
    /// 一局里的同区玩家（玩家编号升序）：日志首部锁定区（<see cref="LogHeader.Zones"/>，下标 = 玩家在 <see cref="LogHeader.Players"/> 里的位置）
    /// 与至少一名其他玩家相同的玩家。未锁定（区号 &lt; 0）的不算。
    /// </summary>
    internal static List<int> SharedZonePlayers(MatchLog log)
    {
        List<int> zones = log.Header.Zones;
        return [.. Enumerable.Range(0, zones.Count)
            .Where(i => zones[i] >= 0 && zones.Count(z => z == zones[i]) > 1)
            .Select(i => log.Header.Players[i])
            .Order()];
    }

    // ---------- 活形（life-shape 4.2） ----------

    /// <param name="rankable">有名次的局：只有它们计入胜率与"按名次分组"（截断局没有名次与胜者）。</param>
    private static LifeShapeSection LifeShape(List<MatchLog> logs, HashSet<MatchLog> rankable)
    {
        int matches = 0, skipped = 0, withoutLife = 0, neverAlive = 0;
        var firstRounds = new List<double>();
        var byRank = new SortedDictionary<int, List<double>>();
        var finalAlive = new List<double>();
        var finalEyeCells = new List<double>();
        var forbiddenShares = new List<double>();
        int withWins = 0, withTrials = 0, withoutWins = 0, withoutTrials = 0;
        int forbiddenStaged = 0, forbiddenRehearsed = 0, forbiddenRejected = 0, breaksRehearsed = 0, breaksRejected = 0;
        var lostByCause = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var defects = new List<LifeDefect>();
        int aliveTotal = 0, aliveSingle = 0, establishedTotal = 0, establishedSingle = 0, eyeSpaces = 0, terrainSmall = 0;

        foreach (MatchLog log in logs)
        {
            // 缺活形字段的旧日志整局排除并计数：哪怕只有一条快照缺也排除，MUST NOT 当成"这局没人活"。
            if (log.Turns.Count == 0 || log.Turns.Any(t => t.Life is null))
            {
                skipped++;
                continue;
            }

            matches++;
            var firstByPlayer = new SortedDictionary<int, int>();
            foreach (TurnSnapshot turn in log.Turns)
            {
                LifeTurnEntry life = turn.Life!;
                forbiddenStaged += life.ForbiddenStaged;
                forbiddenRehearsed += life.ForbiddenRehearsed;
                forbiddenRejected += life.ForbiddenRejected;
                breaksRehearsed += life.BreaksRehearsed;
                breaksRejected += life.BreaksRejected;
                foreach (LifeChangeEntry change in life.Changes)
                {
                    if (change.Kind == LifeChangeEntry.Established)
                    {
                        establishedTotal++;
                        establishedSingle += change.Stones.Count == 1 ? 1 : 0;
                        firstByPlayer.TryAdd(change.Owner, turn.MajorRound);
                        continue;
                    }

                    string cause = change.Cause ?? "?";
                    lostByCause[cause] = lostByCause.TryGetValue(cause, out int n) ? n + 1 : 1;
                    if (cause == LifeChangeEntry.NonOwner || change.Actor != change.Owner)
                    {
                        defects.Add(new LifeDefect(log.Header.Seed, turn.Turn, turn.MajorRound, change.Owner, change.Actor, change.At));
                    }
                }
            }

            if (firstByPlayer.Count == 0)
            {
                withoutLife++;
            }
            else
            {
                firstRounds.Add(firstByPlayer.Values.Min());
            }

            LifeTurnEntry last = log.Turns[^1].Life!;
            foreach (LifePlayerEntry p in last.Players)
            {
                finalAlive.Add(p.AliveGroups);
                finalEyeCells.Add(p.EyeCells);
                aliveTotal += p.AliveGroups;
                aliveSingle += p.SingleStoneAlive;
                eyeSpaces += p.EyeSpaces;
                terrainSmall += p.TerrainSmallEyeSpaces;
            }

            if (last.PlayableCells > 0)
            {
                forbiddenShares.Add((double)last.ProtectedCells / last.PlayableCells);
            }

            if (!rankable.Contains(log))
            {
                continue;
            }

            List<int> winners = log.Result?.Winners ?? [];
            foreach (LifePlayerEntry p in last.Players)
            {
                bool won = winners.Contains(p.Player);
                if (p.AliveGroups > 0)
                {
                    withTrials++;
                    withWins += won ? 1 : 0;
                }
                else
                {
                    withoutTrials++;
                    withoutWins += won ? 1 : 0;
                }
            }

            foreach (StandingEntry standing in log.Result!.Standings)
            {
                if (!firstByPlayer.TryGetValue(standing.Player, out int round))
                {
                    neverAlive++;
                    continue;
                }

                if (!byRank.TryGetValue(standing.Rank, out List<double>? rounds))
                {
                    byRank[standing.Rank] = rounds = [];
                }

                rounds.Add(round);
            }
        }

        var firstRoundByRank = new SortedDictionary<int, RoundStat>();
        foreach ((int rank, List<double> rounds) in byRank)
        {
            firstRoundByRank[rank] = new RoundStat(Statistics.Mean(rounds), rounds.Count);
        }

        return new LifeShapeSection(
            matches,
            skipped,
            firstRounds.Count,
            withoutLife,
            Statistics.Mean(firstRounds),
            firstRoundByRank,
            neverAlive,
            Statistics.Mean(finalAlive),
            Statistics.Mean(finalEyeCells),
            Statistics.Mean(forbiddenShares),
            Statistics.Wilson(withWins, withTrials),
            Statistics.Wilson(withoutWins, withoutTrials),
            forbiddenStaged,
            forbiddenRehearsed,
            forbiddenRejected,
            breaksRehearsed,
            breaksRejected,
            lostByCause,
            defects,
            aliveTotal,
            aliveSingle,
            establishedTotal,
            establishedSingle,
            eyeSpaces,
            terrainSmall);
    }

    // ---------- 终局原因分布与截断（restore-go-core-rules 段 E） ----------

    /// <summary>终局原因的报告顺序 = 终局优先级（只剩一名 &gt; 棋盘填满 &gt; 整轮 Pass）。</summary>
    private static readonly string[] RuleReasons = [nameof(EndReason.LastPlayerStanding), nameof(EndReason.BoardFull), nameof(EndReason.AllPassed)];

    private static EndingSection Ending(List<MatchLog> logs, int ranked)
    {
        List<EndReasonStat> reasons = [.. RuleReasons.Select(reason =>
        {
            List<LogResult> of = [.. logs.Select(l => l.Result!).Where(r => r.Reason == reason)];
            return new EndReasonStat(reason, of.Count, logs.Count == 0 ? double.NaN : (double)of.Count / logs.Count, Statistics.Mean(of.Select(r => (double)r.MajorRound)));
        })];
        List<LogResult> truncated = [.. logs.Select(l => l.Result!).Where(r => r.Truncated)];
        Proportion rate = Statistics.Wilson(truncated.Count, logs.Count);
        int other = logs.Count - truncated.Count - reasons.Sum(r => r.Count);
        return new EndingSection(
            logs.Count,
            reasons,
            truncated.Count,
            rate,
            Statistics.Mean(truncated.Select(r => (double)r.MajorRound)),
            Statistics.Assess(rate.IsEmpty ? double.NaN : rate.Value * 100, 0, 0, "%"),
            other,
            ranked);
    }

    // ---------- 对局时长与地图规模并列 ----------

    private static MapScaleSection MapScale(List<MatchLog> logs)
    {
        List<int> playable = [.. logs.Where(l => l.Header.PlayableCells is > 0).Select(l => l.Header.PlayableCells!.Value)];
        List<int> relics = [.. logs.Select(l => l.Header.Relics.Count)];
        return new MapScaleSection(
            playable.Count,
            logs.Count - playable.Count,
            playable.Count == 0 ? 0 : playable.Min(),
            playable.Count == 0 ? 0 : playable.Max(),
            Statistics.Mean(playable.Select(v => (double)v)),
            relics.Count,
            relics.Count == 0 ? 0 : relics.Min(),
            relics.Count == 0 ? 0 : relics.Max(),
            Statistics.Mean(relics.Select(v => (double)v)));
    }

    // ---------- §17 第 10 项 领地分占比 ----------

    private static TerritoryShareSection TerritoryShare(List<MatchLog> logs)
    {
        var shares = new List<double>();
        int skipped = 0;
        foreach (MatchLog log in logs)
        {
            List<PlayerEntry> active = log.Turns.Count == 0
                ? []
                : [.. log.Turns[^1].PlayersState.Where(p => p.Status == nameof(PlayerStatus.Active))];
            BigInteger total = active.Aggregate(BigInteger.Zero, (sum, p) => sum + p.Total);
            if (active.Count == 0 || active.Any(p => p.TerritoryScore is null) || total.IsZero)
            {
                skipped++;
                continue;
            }

            long territory = active.Sum(p => (long)p.TerritoryScore!.Value);
            shares.Add(territory / (double)total);
        }

        return new TerritoryShareSection(shares.Count, skipped, Statistics.Mean(shares));
    }

    // ---------- §17 第 11 项 地形改造（artisan-terrain-edit 3.4） ----------

    /// <param name="rankable">有名次的局：只有它们里的改造者计入"改造过的玩家胜率"样本（截断局没有胜者）。</param>
    private static TerrainEditSection TerrainEdits(List<MatchLog> logs, HashSet<MatchLog> rankable)
    {
        string[] actions = Enum.GetNames<TerrainEditKind>();
        var counts = actions.ToDictionary(a => a, _ => 0);
        var actionCaptures = actions.ToDictionary(a => a, _ => 0);
        int matches = 0, skipped = 0, artisans = 0, withEdit = 0, causedCaptures = 0, withoutEdit = 0;
        int editorWins = 0, editorSamples = 0;
        var firstRounds = new List<double>();

        foreach (MatchLog log in logs)
        {
            // 缺改造字段的旧日志整局排除并计数（R-6）：哪怕只有一条快照缺字段也排除，MUST NOT 把它当成"这局没改造"。
            if (log.Turns.Any(t => t.Edits is null))
            {
                skipped++;
                continue;
            }

            matches++;
            var editors = new HashSet<int>();
            int? firstRound = null;
            foreach (TurnSnapshot turn in log.Turns)
            {
                // 已落匠人：从落子记录里数（快照的 Placements 是"坐标:类型"，由前后盘面比对得出）。
                // 带改造的匠人不从落子记录推——那里没有改造信息；改造记录自带匠人落点，且一枚匠人至多带一个改造。
                artisans += turn.Placements.Count(IsArtisanPlacement);
                withEdit += turn.Edits!.Select(e => e.Artisan).Distinct(StringComparer.Ordinal).Count();
                foreach (TerrainEditEntry edit in turn.Edits!)
                {
                    counts[edit.Action]++;
                    causedCaptures += edit.CausedCapture ? 1 : 0;
                    actionCaptures[edit.Action] += edit.CausedCapture ? 1 : 0;
                    editors.Add(edit.Player);
                    firstRound ??= turn.MajorRound;
                }
            }

            if (firstRound is { } round)
            {
                firstRounds.Add(round);
            }
            else
            {
                withoutEdit++;
            }

            List<int> winners = log.Result?.Winners ?? [];
            foreach (int editor in editors.Where(_ => rankable.Contains(log)))
            {
                editorSamples++;
                editorWins += winners.Contains(editor) ? 1 : 0;
            }
        }

        int total = counts.Values.Sum();
        return new TerrainEditSection(
            matches,
            skipped,
            total,
            matches == 0 ? 0 : (double)total / matches,
            [.. actions.Select(a => new TerrainEditActionStat(
                a, counts[a], total == 0 ? 0 : (double)counts[a] / total, actionCaptures[a]))],
            artisans,
            withEdit,
            artisans == 0 ? 0 : (double)withEdit / artisans,
            causedCaptures,
            Statistics.Mean(firstRounds),
            withoutEdit,
            Statistics.Wilson(editorWins, editorSamples),

            // 终局地形 = 按日志重放全部改造（match-telemetry「地形可离线重建」）：三种动作各自的次数即新增桥 / 栅栏 / 被烧林地。
            counts[nameof(TerrainEditKind.Bridge)],
            counts[nameof(TerrainEditKind.Fence)],
            counts[nameof(TerrainEditKind.Burn)]);
    }

    /// <summary>
    /// 该条落子记录是不是一枚匠人。快照里的格式是 <c>坐标:类型</c>；
    /// 也容忍 <c>Placement.ToString</c> 的完整形式 <c>坐标:类型+改造</c>，免得格式一变这里就静默数成 0。
    /// </summary>
    private static bool IsArtisanPlacement(string placement)
    {
        int colon = placement.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return false;
        }

        int plus = placement.IndexOf('+', colon);
        string type = plus < 0 ? placement[(colon + 1)..] : placement[(colon + 1)..plus];
        return string.Equals(type, nameof(PieceType.Artisan), StringComparison.Ordinal);
    }

    // ---------- §17 第 10 项 高地加值占比 ----------

    private static HighGroundSection HighGround(List<MatchLog> logs)
    {
        long highGround = 0;
        long positionBonus = 0;

        foreach (MatchLog log in logs)
        {
            if (log.Turns.Count == 0)
            {
                continue;
            }

            List<GroupEntry> groups =
            [
                .. log.Turns[^1].PlayersState.Where(p => p.Status == nameof(PlayerStatus.Active)).SelectMany(p => p.Groups),
            ];
            if (groups.All(g => g.HighGroundBonus is not null))
            {
                highGround += groups.Sum(g => (long)g.HighGroundBonus!.Value);
                positionBonus += groups.Sum(g => (long)g.LineBonus + g.SynergyBonus + g.HighGroundBonus!.Value);
            }
        }

        return new HighGroundSection(
            highGround, positionBonus, positionBonus == 0 ? double.NaN : (double)highGround / positionBonus);
    }

    // ---------- 收敛 ----------

    private static ConvergenceSection Convergence(List<MatchLog> logs)
    {
        var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (MatchLog log in logs)
        {
            string r = log.Result!.Reason;
            reasons[r] = reasons.TryGetValue(r, out int n) ? n + 1 : 1;
        }

        // 不收敛口径 = 未以规则级原因终局的局占比：旧日志的 MajorRoundLimit 与跑局层截断 turn_limit（restore-go-core-rules D5）。
        // 与 LogResult.Converged、BatchRunner.Summarize 同一口径（三处都读 Converged，不各写一份原因名单）。段 E 5.2 另把截断率单列。
        int capped = logs.Count(l => !l.Result!.Converged);
        return new ConvergenceSection(
            logs.Count - capped,
            capped,
            Statistics.Wilson(capped, logs.Count),
            reasons,
            Statistics.Mean(logs.Where(l => l.Result!.Converged).Select(l => (double)l.Result!.MajorRound)),
            Statistics.Mean(logs.Select(l => (double)l.Result!.MajorRound)),
            Statistics.Mean(logs.Select(l => (double)l.Result!.TurnCount)));
    }

    // ---------- §16 ----------

    private static TargetsSection Targets(List<MatchLog> logs, List<MatchLog> ranked, AnalysisOptions options)
    {
        var phase1 = new SortedDictionary<int, int>();
        var phase2 = new SortedDictionary<int, int>();
        var phase3 = new SortedDictionary<int, int>();
        var powerByRound = new SortedDictionary<int, List<double>>();
        var territoryByRound = new SortedDictionary<int, List<double>>();
        var maxGroupByRound = new SortedDictionary<int, BigInteger>();
        var firstConflict = new SortedDictionary<int, int>();
        int noConflict = 0;
        var occupancyHistogram = new SortedDictionary<int, int>();
        var occupancyRatios = new List<double>();
        int noOccupancy = 0;
        var endRounds = new SortedDictionary<int, int>();
        long totalTurns = 0;
        long totalRounds = 0;
        var roundMs = new List<double>();
        var matchMs = new List<double>();
        var stepMs = new List<long>();

        foreach (MatchLog log in logs)
        {
            int? conflictRound = null;
            int? conflictStones = null;
            var lastOfRound = new Dictionary<int, TurnSnapshot>();
            foreach (TurnSnapshot turn in log.Turns)
            {
                if (turn.DeployLimit > 0)
                {
                    SortedDictionary<int, int> bucket = turn.MajorRound <= 3 ? phase1 : turn.MajorRound <= 6 ? phase2 : phase3;
                    bucket[turn.DeployLimit] = bucket.TryGetValue(turn.DeployLimit, out int n) ? n + 1 : 1;
                }

                if (conflictRound is null && turn.Captures.Count > 0)
                {
                    conflictRound = turn.MajorRound;

                    // 口径：首次提子那一小回合结束时盘面上的棋子数（提子已生效），
                    // 取各玩家棋串的棋子数之和——落子记录是增量，快照才是盘面。
                    conflictStones = turn.PlayersState.Sum(p => p.Groups.Sum(g => g.Stones.Count));
                }

                lastOfRound[turn.MajorRound] = turn;
            }

            foreach ((int round, TurnSnapshot snapshot) in lastOfRound)
            {
                if (!powerByRound.TryGetValue(round, out List<double>? list))
                {
                    powerByRound[round] = list = [];
                }

                if (!territoryByRound.TryGetValue(round, out List<double>? territories))
                {
                    territoryByRound[round] = territories = [];
                }

                foreach (PlayerEntry p in snapshot.PlayersState.Where(p => p.Status == "Active"))
                {
                    list.Add((double)p.Total);

                    // 领地分曲线（数值目标回归：势力成长曲线分领地分与棋串军势两条）；旧日志无该字段则不进领地分均值，MUST NOT 回填成 0。
                    if (p.TerritoryScore is { } territory)
                    {
                        territories.Add(territory);
                    }

                    BigInteger maxGroup = p.Groups.Count == 0 ? BigInteger.Zero : p.Groups.Max(g => g.Power);
                    maxGroupByRound[round] = BigInteger.Max(maxGroupByRound.TryGetValue(round, out BigInteger m) ? m : BigInteger.Zero, maxGroup);
                }
            }

            if (conflictRound is { } c)
            {
                firstConflict[c] = firstConflict.TryGetValue(c, out int n) ? n + 1 : 1;
            }
            else
            {
                noConflict++;
            }

            // 分母是"纳入局"：整局无提子的局、以及 denser-map 之前没记可落子格的旧日志，一律排除。
            if (conflictStones is { } stones && log.Header.PlayableCells is { } cells && cells > 0)
            {
                int percent = stones * 100 / cells;
                occupancyHistogram[percent] = occupancyHistogram.TryGetValue(percent, out int k) ? k + 1 : 1;
                occupancyRatios.Add((double)stones / cells);
            }
            else
            {
                noOccupancy++;
            }

            LogResult r = log.Result!;
            if (r.Converged)
            {
                endRounds[r.MajorRound] = endRounds.TryGetValue(r.MajorRound, out int n) ? n + 1 : 1;
            }

            stepMs.AddRange(log.Turns.Where(turn => turn.ElapsedMs is not null).Select(turn => turn.ElapsedMs!.Value));
            totalTurns += r.TurnCount;
            totalRounds += r.MajorRound;
            if (r.MajorRoundMs is { } ms)
            {
                roundMs.AddRange(ms.Select(v => (double)v));
            }

            if (r.TotalMs is { } total)
            {
                matchMs.Add(total);
            }
        }

        List<(int, double, double, BigInteger, int)> curve = [.. powerByRound.Select(kv =>
            (kv.Key, Statistics.Mean(kv.Value), Statistics.Mean(territoryByRound[kv.Key]), maxGroupByRound.TryGetValue(kv.Key, out BigInteger m) ? m : BigInteger.Zero, kv.Value.Count))];
        double opening = Statistics.Mean(powerByRound.Where(kv => kv.Key <= 2).SelectMany(kv => kv.Value));
        double mid = Statistics.Mean(powerByRound.Where(kv => kv.Key is >= 4 and <= 6).SelectMany(kv => kv.Value));
        double conflictMean = Statistics.Mean(firstConflict.SelectMany(kv => Enumerable.Repeat((double)kv.Key, kv.Value)));
        double endMean = Statistics.Mean(endRounds.SelectMany(kv => Enumerable.Repeat((double)kv.Key, kv.Value)));
        LeaderSection leader = Leader(ranked, options);

        return new TargetsSection(
            phase1, phase2, phase3,
            Statistics.Assess(Median(phase1), 3, 3),
            Statistics.Assess(Median(phase2), 3, 5),
            Statistics.Assess(Median(phase3), 5, 8),
            curve,
            Statistics.Assess(opening, 1, 99),
            Statistics.Assess(mid, 20, 150),
            firstConflict, noConflict,
            Statistics.Assess(conflictMean, 4, 5, " 大回合"),
            occupancyHistogram, Statistics.Mean(occupancyRatios), noOccupancy,
            Statistics.Unmeasurable(0, 3, " 分钟"),
            totalRounds == 0 ? double.NaN : (double)totalTurns / totalRounds,
            Statistics.Mean(roundMs),
            endRounds,
            Statistics.Assess(endMean, 7, 10, " 大回合"),
            Statistics.Unmeasurable(20, 30, " 分钟"),
            Statistics.Mean(matchMs),
            Statistics.Assess(leader.AnyOfGroupWins.IsEmpty ? double.NaN : leader.AnyOfGroupWins.Value * 100, 0, 50, "%"),
            new AiStepTiming(stepMs.Count, Statistics.Mean(stepMs.Select(v => (double)v)), stepMs.Count == 0 ? 0 : stepMs.Max()));
    }

    private static double Median(SortedDictionary<int, int> histogram) =>
        Statistics.Median(histogram.SelectMany(kv => Enumerable.Repeat((double)kv.Key, kv.Value)));

    // ---------- 领先者（§16.6 / §17.1，裁决 3） ----------

    /// <summary>第 3 大回合结束时势力名次为 1 的玩家（并列则多人）。没有第 3 大回合结束事件的局返回空。</summary>
    internal static List<int> LeadersAtRound3(MatchLog log)
    {
        LogEvent? e = log.Events.FirstOrDefault(ev => ev.Type == LogEventType.MajorRoundEnded && ev.MajorRound == 3);
        if (e?.Values is null)
        {
            return [];
        }

        return [.. log.Header.Players.Where(p => e.Values.TryGetValue($"P{p}.Rank", out BigInteger rank) && rank == 1)];
    }

    private static LeaderSection Leader(List<MatchLog> logs, AnalysisOptions options)
    {
        int samples = 0;
        int tied = 0;
        int anyWins = 0;
        int specificSamples = 0;
        int specificWins = 0;
        int nonLeaderSamples = 0;
        int nonLeaderWins = 0;
        foreach (MatchLog log in logs)
        {
            List<int> leaders = LeadersAtRound3(log);
            if (leaders.Count == 0)
            {
                continue;
            }

            samples++;
            if (leaders.Count > 1)
            {
                tied++;
            }

            List<int> winners = log.Result!.Winners;
            if (leaders.Any(winners.Contains))
            {
                anyWins++;
            }

            foreach (int leader in leaders)
            {
                specificSamples++;
                if (winners.Contains(leader))
                {
                    specificWins++;
                }
            }

            foreach (int p in log.Header.Players.Where(p => !leaders.Contains(p)))
            {
                nonLeaderSamples++;
                if (winners.Contains(p))
                {
                    nonLeaderWins++;
                }
            }
        }

        int players = logs.Count == 0 ? 0 : logs[0].Header.Players.Count;
        return new LeaderSection(
            samples, tied,
            Statistics.Wilson(anyWins, samples),
            Statistics.Wilson(specificWins, specificSamples),
            Statistics.Wilson(nonLeaderWins, nonLeaderSamples),
            players == 0 ? double.NaN : 1.0 / players,
            samples >= options.RequiredMatches);
    }

    // ---------- §17.2 先手滚雪球 ----------

    /// <param name="ranked">有名次的局：只有它们参与"第 3 大回合位置 → 最终名次"（截断局没有最终名次）。</param>
    private static SnowballSection Snowball(List<MatchLog> logs, List<MatchLog> ranked)
    {
        var rankable = new HashSet<MatchLog>(ranked, ReferenceEqualityComparer.Instance);
        var byPosition = new SortedDictionary<int, (int Improved, int Same, int Worse)>();
        int firstSamples = 0;
        int firstStays = 0;
        var finalByRound3 = new SortedDictionary<int, List<double>>();
        foreach (MatchLog log in logs)
        {
            List<LogEvent> ends = [.. log.Events.Where(e => e.Type == LogEventType.MajorRoundEnded && e.Values is not null).OrderBy(e => e.MajorRound)];
            for (int i = 0; i + 1 < ends.Count; i++)
            {
                Dictionary<string, BigInteger> now = ends[i].Values!;
                Dictionary<string, BigInteger> next = ends[i + 1].Values!;
                foreach (int p in log.Header.Players)
                {
                    if (!now.TryGetValue($"P{p}.Next", out BigInteger position) || !now.TryGetValue($"P{p}.Rank", out BigInteger rank)
                        || !next.TryGetValue($"P{p}.Rank", out BigInteger nextRank))
                    {
                        continue;
                    }

                    (int improved, int same, int worse) = byPosition.TryGetValue((int)position, out var t) ? t : (0, 0, 0);
                    if (nextRank < rank)
                    {
                        improved++;
                    }
                    else if (nextRank == rank)
                    {
                        same++;
                    }
                    else
                    {
                        worse++;
                    }

                    byPosition[(int)position] = (improved, same, worse);
                    if (position == 0)
                    {
                        firstSamples++;
                        if (next.TryGetValue($"P{p}.Next", out BigInteger nextPosition) && nextPosition == 0)
                        {
                            firstStays++;
                        }
                    }
                }
            }

            LogEvent? third = ends.FirstOrDefault(e => e.MajorRound == 3);
            if (third is not null && rankable.Contains(log))
            {
                foreach (int p in log.Header.Players)
                {
                    if (third.Values!.TryGetValue($"P{p}.Next", out BigInteger position))
                    {
                        StandingEntry? standing = log.Result!.Standings.FirstOrDefault(s => s.Player == p);
                        if (standing is not null)
                        {
                            if (!finalByRound3.TryGetValue((int)position, out List<double>? list))
                            {
                                finalByRound3[(int)position] = list = [];
                            }

                            list.Add(standing.Rank);
                        }
                    }
                }
            }
        }

        return new SnowballSection(
            [.. byPosition.Select(kv => (kv.Key, kv.Value.Improved, kv.Value.Same, kv.Value.Worse))],
            Statistics.Wilson(firstStays, firstSamples),
            [.. finalByRound3.Select(kv => (kv.Key, Statistics.Mean(kv.Value), kv.Value.Count))]);
    }

    // ---------- §17.3 棋子与信物 ----------

    /// <param name="rankable">有名次的局：选择率与控制时长用全部纳入局，"选过 / 控制过它的玩家胜率"只取这些局（截断局没有胜者）。</param>
    private static SelectionSection Selection(List<MatchLog> logs, HashSet<MatchLog> rankable)
    {
        var offered = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var picked = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var pickerSamples = new SortedDictionary<string, (int Wins, int Samples)>(StringComparer.Ordinal);
        var relicCount = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var relicRevealed = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var relicControlledTurns = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var controllerSamples = new SortedDictionary<string, (int Wins, int Samples)>(StringComparer.Ordinal);
        foreach (string type in Enum.GetNames<Core.Board.PieceType>())
        {
            offered[type] = 0;
            picked[type] = 0;
            pickerSamples[type] = (0, 0);
        }

        foreach (string type in Enum.GetNames<RelicType>())
        {
            relicCount[type] = 0;
            relicRevealed[type] = 0;
            relicControlledTurns[type] = 0;
            controllerSamples[type] = (0, 0);
        }

        foreach (MatchLog log in logs)
        {
            List<int> winners = log.Result!.Winners;
            var pickedBy = new Dictionary<int, HashSet<string>>();
            foreach (LogEvent e in log.Events.Where(e => e.Type == LogEventType.Recruit && e.Player is not null))
            {
                foreach (string type in Field(e.Detail, "candidates"))
                {
                    offered[type] = offered.TryGetValue(type, out int n) ? n + 1 : 1;
                }

                foreach (string type in Field(e.Detail, "picks"))
                {
                    picked[type] = picked.TryGetValue(type, out int n) ? n + 1 : 1;
                    if (!pickedBy.TryGetValue(e.Player!.Value, out HashSet<string>? set))
                    {
                        pickedBy[e.Player.Value] = set = [];
                    }

                    set.Add(type);
                }
            }

            foreach ((int player, HashSet<string> types) in pickedBy.Where(_ => rankable.Contains(log)))
            {
                foreach (string type in types)
                {
                    (int wins, int samples) = pickerSamples.TryGetValue(type, out var t) ? t : (0, 0);
                    pickerSamples[type] = (wins + (winners.Contains(player) ? 1 : 0), samples + 1);
                }
            }

            var typeOf = log.Header.Relics.ToDictionary(r => r.Coord, r => r.Type, StringComparer.Ordinal);
            foreach (RelicEntry relic in log.Header.Relics)
            {
                relicCount[relic.Type] = relicCount.TryGetValue(relic.Type, out int n) ? n + 1 : 1;
            }

            foreach (RelicRevealEntry reveal in log.Result.RelicReveals.Where(r => r.RevealedInMajorRound is not null))
            {
                string type = typeOf[reveal.Coord];
                relicRevealed[type] = relicRevealed.TryGetValue(type, out int n) ? n + 1 : 1;
            }

            var controlledBy = new Dictionary<int, HashSet<string>>();
            foreach (TurnSnapshot turn in log.Turns)
            {
                foreach (RelicStateEntry state in turn.Relics.Where(r => r.Control == nameof(RelicControlKind.Controlled) && r.Holder is not null))
                {
                    string type = typeOf[state.Coord];
                    relicControlledTurns[type] = relicControlledTurns.TryGetValue(type, out int n) ? n + 1 : 1;
                    if (!controlledBy.TryGetValue(state.Holder!.Value, out HashSet<string>? set))
                    {
                        controlledBy[state.Holder.Value] = set = [];
                    }

                    set.Add(type);
                }
            }

            foreach ((int player, HashSet<string> types) in controlledBy.Where(_ => rankable.Contains(log)))
            {
                foreach (string type in types)
                {
                    (int wins, int samples) = controllerSamples.TryGetValue(type, out var t) ? t : (0, 0);
                    controllerSamples[type] = (wins + (winners.Contains(player) ? 1 : 0), samples + 1);
                }
            }
        }

        List<PieceStat> pieces = [.. offered.Keys.Select(type => new PieceStat(
            type, offered[type], picked[type],
            Statistics.Wilson(picked[type], offered[type]),
            Statistics.Wilson(pickerSamples[type].Wins, pickerSamples[type].Samples)))];
        List<RelicStat> relics = [.. relicCount.Keys.Select(type => new RelicStat(
            type, relicCount[type], relicRevealed[type], relicControlledTurns[type],
            relicCount[type] == 0 ? double.NaN : (double)relicControlledTurns[type] / relicCount[type],
            Statistics.Wilson(controllerSamples[type].Wins, controllerSamples[type].Samples)))];
        return new SelectionSection(pieces, relics);
    }

    /// <summary>从 <c>key=a,b key2=</c> 形式的文本取某字段的列表。</summary>
    internal static IEnumerable<string> Field(string detail, string key)
    {
        foreach (string token in detail.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith(key + "=", StringComparison.Ordinal))
            {
                return token[(key.Length + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        return [];
    }

    // ---------- §17.4 高倍率棋串 ----------

    private static MultiplierSection Multiplier(List<MatchLog> logs)
    {
        var distribution = new SortedDictionary<int, int>();
        var rounds = new List<double>();
        var powers = new List<double>();
        BigInteger max = BigInteger.Zero;
        int destroyed = 0;
        int peaks = 0;
        foreach (PeakEntry peak in logs.Select(l => l.Result!).Where(r => r.Peak is not null).Select(r => r.Peak!))
        {
            peaks++;
            distribution[peak.MultiplierCount] = distribution.TryGetValue(peak.MultiplierCount, out int n) ? n + 1 : 1;
            rounds.Add(peak.MajorRound);
            powers.Add((double)peak.Power);
            max = BigInteger.Max(max, peak.Power);
        }

        foreach (LogResult r in logs.Select(l => l.Result!).Where(r => r.PeakDestroyed == true))
        {
            destroyed++;
        }

        return new MultiplierSection(peaks, distribution, Statistics.Mean(rounds), Statistics.Mean(powers), max, Statistics.Wilson(destroyed, peaks));
    }

    // ---------- 各棋子势力占比（multiplier-rebalance 裁决 3） ----------

    private static PieceShareSection PieceShares(List<MatchLog> logs)
    {
        // 类型列表按内容集（more-pieces-relics D8）：样本里只有 v1 对局（含首部缺内容集的旧日志）时列原六种，报告与引入新棋子之前逐字节相同；
        // 有 v2 对局时列十种。完整的"按内容集展开 / v1 单列不适用"属段 C（tasks 3.7）。
        PieceType[] types = [.. ContentSets.PieceTypesOf(logs.Any(l => l.Header.Config.ContentSet == ContentSet.V2) ? ContentSet.V2 : ContentSet.V1)];
        var stones = types.ToDictionary(t => t, _ => 0L);
        var power = types.ToDictionary(t => t, _ => BigInteger.Zero);
        int matches = 0;
        int skipped = 0;
        foreach (MatchLog log in logs)
        {
            if (log.Turns.Count == 0)
            {
                skipped++;
                continue;
            }

            List<GroupEntry> groups = [.. log.Turns[^1].PlayersState
                .Where(p => p.Status == nameof(PlayerStatus.Active))
                .SelectMany(p => p.Groups)];
            if (groups.Any(g => g.PieceCounts is null))
            {
                skipped++;
                continue;
            }

            matches++;
            foreach (GroupEntry g in groups)
            {
                // restore-go-core-rules：倍率整体放大"基础 + 位置加值"，放大部分 = 日志里的精确军势 − 基础 − 全部位置加值，整块归倍增子。
                BigInteger amplified = g.Power - g.Base - g.LineBonus - g.SynergyBonus - (g.HighGroundBonus ?? 0);
                foreach (PieceType type in types)
                {
                    int count = g.PieceCounts!.TryGetValue(type.ToString(), out int n) ? n : 0;
                    if (count == 0)
                    {
                        continue;
                    }

                    stones[type] += count;
                    power[type] += ((long)PieceEffects.BasePower(type) * count) + type switch
                    {
                        PieceType.Line => g.LineBonus,
                        PieceType.Synergy => g.SynergyBonus,
                        PieceType.Multiplier => amplified,
                        _ => BigInteger.Zero,
                    };
                }
            }
        }

        long totalStones = stones.Values.Sum();
        BigInteger totalPower = power.Values.Aggregate(BigInteger.Zero, (sum, v) => sum + v);
        List<PieceShare> pieces = [.. types.Select(t => new PieceShare(
            t.ToString(),
            stones[t],
            totalStones == 0 ? double.NaN : (double)stones[t] / totalStones,
            power[t],
            totalPower.IsZero ? double.NaN : (double)power[t] / (double)totalPower,
            stones[t] == 0 ? double.NaN : (double)power[t] / stones[t]))];
        return new PieceShareSection(matches, skipped, totalStones, totalPower, pieces);
    }

    // ---------- §17.5 出生区（裁决 8：收敛 / 未收敛分组） ----------

    /// <param name="all">全部纳入局：只用来定区数（地图属性，与名次无关）。</param>
    /// <param name="logs">有名次的局：各区胜率与被选次数的样本（截断局没有胜者，MUST NOT 以"未胜"计入）。</param>
    private static BirthZoneSection BirthZones(List<MatchLog> all, List<MatchLog> logs)
    {
        // 行数 = 地图出生区数，取自日志首部；旧日志（frontier-map 之前）无该字段，回填为被选到过的最大区号 + 1（标准档上即真值）。
        // 首部区数与被选区号取较大者：首部偏小（手改日志 / 混批）时不得把越界区的样本静默丢掉。
        int zones = all.Count == 0 ? 0 : all.Max(l => Math.Max(l.Header.ZoneCount ?? 0, l.Header.Zones.Count == 0 ? 0 : l.Header.Zones.Max() + 1));
        // 基线 = 1 / 参赛人数：各区胜率的分母是"该区被选中的局数"，被选中的区上那名玩家的期望胜率是 1/人数，与地图有几个区无关。
        // 出生区多于玩家的图（边疆档 6 区 4 人）若按 1/区数 = 1/6 取基线，公平的 25% 会被系统性判成"显著偏高"（frontier-map 2.4）。
        double baseline = zones == 0 ? double.NaN : 1.0 / all[0].Header.Players.Count;
        List<MatchLog> converged = [.. logs.Where(l => l.Header.RelicsConverged)];
        List<MatchLog> notConverged = [.. logs.Where(l => !l.Header.RelicsConverged)];
        return new BirthZoneSection(
            zones,
            baseline,
            ZoneStats(logs, zones, baseline),
            ZoneStats(converged, zones, baseline),
            ZoneStats(notConverged, zones, baseline),
            converged.Count,
            notConverged.Count)
        {
            BySide = all.Any(l => l.Header.Config.MapPerMatch) ? SideStats(logs, baseline) : null,
        };
    }

    /// <summary>生成图的平台边长范围（报告固定列出这几行）。</summary>
    private const int MinPlatformSide = 5;
    private const int MaxPlatformSide = 9;

    private static PlatformSideSection SideStats(List<MatchLog> logs, double baseline)
    {
        var offered = new SortedDictionary<int, int>();
        var picks = new SortedDictionary<int, int>();
        var wins = new SortedDictionary<int, int>();
        for (int side = MinPlatformSide; side <= MaxPlatformSide; side++)
        {
            offered[side] = picks[side] = wins[side] = 0;
        }

        int matches = 0;
        int skipped = 0;
        foreach (MatchLog log in logs)
        {
            if (log.Header.ZoneSides is not { } sides)
            {
                skipped++;
                continue;
            }

            matches++;
            foreach (int side in sides)
            {
                offered[side] = offered.GetValueOrDefault(side) + 1;
                picks.TryAdd(side, 0);
                wins.TryAdd(side, 0);
            }

            // 被选 / 获胜的计数与各区胜率走同一份（TallyPicks）：这里只是把"区号"换成"该区的边长"来归组。
            TallyPicks(log, sides.Count, zone => sides[zone], picks, wins);
        }

        return new PlatformSideSection(matches, skipped, baseline, [.. offered.Keys.Select(side =>
        {
            Proportion rate = Statistics.Wilson(wins[side], picks[side]);
            return new SideStat(side, offered[side], picks[side], rate, rate.Excludes(baseline));
        })]);
    }

    private static List<ZoneStat> ZoneStats(List<MatchLog> logs, int zones, double baseline)
    {
        var wins = new SortedDictionary<int, int>();
        var samples = new SortedDictionary<int, int>();
        for (int z = 0; z < zones; z++)
        {
            wins[z] = samples[z] = 0;
        }

        foreach (MatchLog log in logs)
        {
            TallyPicks(log, zones, zone => zone, samples, wins);
        }

        return [.. Enumerable.Range(0, zones).Select(z =>
        {
            Proportion rate = Statistics.Wilson(wins[z], samples[z]);
            return new ZoneStat(z, rate, rate.Excludes(baseline), samples[z]);
        })];
    }

    /// <summary>
    /// 一局的选区样本计数——"各区胜率"与"按平台边长分组"（map-generator D6）共用的唯一一份：每名玩家锁定的出生区计一次被选，
    /// 该玩家在胜者之列再计一次获胜；区号越界（不在 <c>[0, zoneCount)</c>）的样本不计。<paramref name="groupOf"/> 把区号映射到归组键
    /// （各区胜率：区号本身；按边长：该局该区的边长），键 MUST 已在两个计数表里。
    /// </summary>
    private static void TallyPicks(MatchLog log, int zoneCount, Func<int, int> groupOf, SortedDictionary<int, int> picks, SortedDictionary<int, int> wins)
    {
        for (int p = 0; p < log.Header.Zones.Count; p++)
        {
            int zone = log.Header.Zones[p];
            if (zone < 0 || zone >= zoneCount)
            {
                continue;
            }

            int group = groupOf(zone);
            picks[group]++;
            if (log.Result!.Winners.Contains(log.Header.Players[p]))
            {
                wins[group]++;
            }
        }
    }

    // ---------- §17.6 成长轴顺序 ----------

    /// <summary>某玩家在一局中四条成长轴的首次获取大回合（未获取为 <c>null</c>）：供给、部署、槽位、倍率。</summary>
    internal static int?[] AxisAcquisitionRounds(MatchLog log, int player)
    {
        int?[] rounds = new int?[4];
        foreach (TurnSnapshot turn in log.Turns)
        {
            if (turn.Player == player && turn.DeployLimit > 0)
            {
                if (rounds[0] is null && (turn.ShowCount > EffectSnapshot.BaseRevealCount || turn.FreePickCount > EffectSnapshot.BaseFreePickCount))
                {
                    rounds[0] = turn.MajorRound;
                }

                if (rounds[1] is null && turn.DeployLimit > EffectSnapshot.BaseDeployLimitFor(turn.MajorRound))
                {
                    rounds[1] = turn.MajorRound;
                }

                if (rounds[2] is null && turn.TypeSlots > EffectSnapshot.BaseTypeSlots)
                {
                    rounds[2] = turn.MajorRound;
                }
            }

            if (rounds[3] is null)
            {
                PlayerEntry? entry = turn.PlayersState.FirstOrDefault(p => p.Player == player);
                if (entry is not null && entry.Groups.Any(g => g.MultiplierCount > 0))
                {
                    rounds[3] = turn.MajorRound;
                }
            }
        }

        return rounds;
    }

    /// <summary>获取顺序文本：按大回合先后排列，同轮用 <c>=</c> 连接，未获取的轴不出现；一条都没有为 <c>无</c>。</summary>
    internal static string AxisSequence(int?[] rounds)
    {
        var groups = rounds
            .Select((r, i) => (Round: r, Axis: AxisNames[i]))
            .Where(t => t.Round is not null)
            .GroupBy(t => t.Round!.Value)
            .OrderBy(g => g.Key)
            .Select(g => string.Join("=", g.Select(t => t.Axis)));
        string text = string.Join(">", groups);
        return text.Length == 0 ? "无" : text;
    }

    private static GrowthAxisSection GrowthAxes(List<MatchLog> logs)
    {
        var sequences = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var first = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int samples = 0;
        foreach (MatchLog log in logs)
        {
            foreach (int winner in log.Result!.Winners)
            {
                samples++;
                int?[] rounds = AxisAcquisitionRounds(log, winner);
                string sequence = AxisSequence(rounds);
                sequences[sequence] = sequences.TryGetValue(sequence, out int n) ? n + 1 : 1;
                string head = sequence.Split('>')[0];
                first[head] = first.TryGetValue(head, out int m) ? m + 1 : 1;
            }
        }

        KeyValuePair<string, int> top = sequences.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).FirstOrDefault();
        double share = samples == 0 ? double.NaN : (double)top.Value / samples;
        return new GrowthAxisSection(samples, sequences, first, samples == 0 ? null : top.Key, share);
    }

    // ---------- §17.7 最小落子拖延 ----------

    private static StallingSection Stalling(List<MatchLog> logs)
    {
        int signal = 0;
        int total = 0;
        foreach (MatchLog log in logs)
        {
            for (int i = 0; i < log.Turns.Count; i++)
            {
                total++;
                TurnSnapshot turn = log.Turns[i];
                if (turn.Placements.Count != 1 || i == 0)
                {
                    continue;
                }

                BigInteger before = log.Turns[i - 1].PlayersState.First(p => p.Player == turn.Player).Total;
                BigInteger after = turn.PlayersState.First(p => p.Player == turn.Player).Total;
                if (before == after)
                {
                    signal++;
                }
            }
        }

        return new StallingSection(signal, total, Statistics.Wilson(signal, total));
    }

    // ---------- AI 决策质量旁证（design.md Risks / implement 8.7） ----------

    private static AiQualitySection AiQuality(List<MatchLog> logs)
    {
        int settled = 0;
        int captures = 0;
        int suicides = 0;
        int rejected = 0;
        int passed = 0;
        int turns = 0;
        foreach (MatchLog log in logs)
        {
            foreach (TurnSnapshot turn in log.Turns)
            {
                turns++;
                if (turn.Passed)
                {
                    passed++;
                }
                else
                {
                    settled++;
                    captures += turn.Captures.Count;
                }
            }

            foreach (LogEvent e in log.Events.Where(e => e.Type == LogEventType.Rejected))
            {
                rejected++;
                if (e.FailureKind == nameof(Core.Batch.BatchFailureKind.Suicide))
                {
                    suicides++;
                }
            }
        }

        Proportion suicideRate = Statistics.Wilson(suicides, settled + rejected);
        Proportion passRate = Statistics.Wilson(passed, turns);
        bool unreliable = (!suicideRate.IsEmpty && suicideRate.Value >= SuicideUnreliableThreshold)
            || (!passRate.IsEmpty && passRate.Value >= PassUnreliableThreshold);
        string verdict = unreliable
            ? "警告：AI 决策质量旁证显示近乎随机（自杀手尝试率或 Pass 率超过阈值），本报告的数值结论不可信，必须先提升 AI。"
            : "AI 决策质量旁证未触发近乎随机警告。";
        return new AiQualitySection(
            settled, captures, settled == 0 ? double.NaN : (double)captures / settled,
            suicides, suicideRate, passRate, unreliable, verdict);
    }
}
