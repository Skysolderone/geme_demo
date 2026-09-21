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

/// <summary>收敛情况（裁决 13 → round-cap D5）：非达上限终局（含势力碾压）vs 以规则原因 <c>MajorRoundLimit</c>（达大回合上限）终局，单独成段。</summary>
public sealed record ConvergenceSection(
    int Converged,
    int Capped,
    Proportion CappedRate,
    SortedDictionary<string, int> Reasons,
    double MeanMajorRoundsConverged,
    double MeanMajorRoundsAll,
    double MeanTurnsPerMatch);

/// <summary>
/// 势力碾压（match-telemetry 平衡分析方向 8，dominance-victory）：碾压胜占比（分母 = 纳入分析的局）、这些局碾压成立（获胜）的平均大回合、
/// 触发时获胜者势力与第 2 名势力之比。第 2 名势力为 0 时比值无定义，不计入均值，单独计数。
/// </summary>
public sealed record DominanceSection(
    int Matches,
    int DominanceWins,
    Proportion Rate,
    double MeanTriggerRound,
    double MeanPowerRatio,
    int RatioSamples,
    int RatioUndefined);

/// <summary>
/// 落后者征募补偿（catch-up-recruit 裁决 3 的报告口径）：占比分母是<b>纳入局</b>的小回合总数，不是全部局。
/// 纳入 = 日志首部记录本局开启补偿，且每条小回合快照都带补偿留痕；旧日志（<see cref="TurnSnapshot.CatchUpReveal"/> 为 <c>null</c>）
/// 与关闭补偿的局整局计入 <see cref="Skipped"/>，MUST NOT 把缺字段回填成 0——那会把旧局错算成"从未补偿"。
/// </summary>
/// <param name="FinalRankOfCompensated">至少获得过一次补偿的玩家，其终局名次 → 人次。</param>
public sealed record CatchUpSection(
    int Matches,
    int Skipped,
    int Turns,
    int CompensatedTurns,
    Proportion CompensatedTurnRate,
    int RevealTriggers,
    int PickTriggers,
    SortedDictionary<int, int> FinalRankOfCompensated);

/// <summary>§16 六项数值目标。</summary>
public sealed record TargetsSection(
    SortedDictionary<int, int> DeployLimitRounds1To3,
    SortedDictionary<int, int> DeployLimitRounds4To6,
    SortedDictionary<int, int> DeployLimitRounds7Plus,
    Deviation DeployPhase1,
    Deviation DeployPhase2,
    Deviation DeployPhase3,
    List<(int Round, double MeanPower, long MaxGroupPower, int Samples)> PowerCurve,
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

/// <summary>§17.4 高倍率棋串。<see cref="PeakCountDistribution"/> 按原始倍增子数量（堆了多少），<see cref="PeakEffectiveExponentDistribution"/> 按生效倍率指数（封顶后生效了多少）。</summary>
public sealed record MultiplierSection(
    int MatchesWithPeak,
    SortedDictionary<int, int> PeakCountDistribution,
    SortedDictionary<int, int> PeakEffectiveExponentDistribution,
    double MeanFormationRound,
    double MeanPeakPower,
    long MaxPeakPower,
    Proportion DestroyedRate);

/// <summary>一种棋子的势力归因：盘面枚数、盘面占比、归因势力、势力占比、每颗平均贡献。</summary>
public sealed record PieceShare(string Type, long Stones, double StoneShare, long Power, double PowerShare, double MeanPerStone);

/// <summary>
/// 各棋子势力占比（multiplier-rebalance 裁决 3，proposal 表格口径）：逐局取终局快照（最后一条小回合快照）中参赛玩家（<c>Active</c>）的全部棋串；
/// 普通 / 堡垒 / 连珠 / 协同计各自基础军势，连珠与协同再分得本串对应的位置加值；倍增子计各自基础军势 1，再分得本串"放大出来的部分"
/// <c>⌊基础 × 倍率⌋ − 基础</c>（整数，经唯一的 <see cref="Siege.Core.Scoring.Multiplier.Apply"/> 按日志里的生效指数算）。势力占比的分母是纳入局归因势力之和（棋串军势，不含据点分；scoring-sites 的高地加值不属于任何棋子类型，同样不计入）。
/// 终局快照里有任何参赛玩家棋串缺 <see cref="GroupEntry.PieceCounts"/>（旧日志）或整局无快照的局计入 <see cref="Skipped"/>，不参与任何分子分母。
/// </summary>
public sealed record PieceShareSection(int Matches, int Skipped, long TotalStones, long TotalPower, List<PieceShare> Pieces);

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
/// 据点一档的统计（match-telemetry 平衡分析方向 10）。分母"据点小回合" = 纳入局的小回合快照数 × 该档据点数。
/// </summary>
/// <param name="SiteInstances">纳入局中该档据点的个数之和（局 × 个）。</param>
/// <param name="ControlledShare">被控制（占据或唯一覆盖）的据点小回合 ÷ 据点小回合。</param>
/// <param name="ContestedShare">争议的据点小回合 ÷ 据点小回合。</param>
/// <param name="MeanFirstControlledRound">曾被控制过的据点首次被控制所在大回合的平均；<paramref name="NeverControlled"/> 个整局从未被控制，不进均值。</param>
/// <param name="WinRateOfControllers">样本 = 每局中至少在一条小回合快照里控制过该档任一据点的玩家；成功 = 该玩家是本局获胜者。</param>
public sealed record SiteTierStat(
    string Tier,
    int SiteInstances,
    long SiteTurns,
    long ControlledTurns,
    long ContestedTurns,
    double ControlledShare,
    double ContestedShare,
    double MeanFirstControlledRound,
    int NeverControlled,
    Proportion WinRateOfControllers);

/// <summary>
/// 据点"主人"口径（篝火主人 / 石碑桥头那家）：主人 = 日志首部 <see cref="SiteEntry.HomeZone"/> 所对应出生区上的玩家（经 <see cref="LogHeader.Zones"/> 映射）。
/// 首部推不出主人（<c>HomeZone</c> 为 <c>null</c>）的据点计入 <paramref name="UnknownOwner"/>，不进任何分子分母。
/// </summary>
/// <param name="OwnerShare">主人控制的据点小回合 ÷ 据点小回合。</param>
/// <param name="OwnerShareOfControlled">主人控制的据点小回合 ÷ 被控制的据点小回合。</param>
/// <param name="MeanOwnerFirstControlRound">主人首次控制所在大回合的平均（只算主人曾控制过的据点；<paramref name="OwnerNeverControlled"/> 个从未）。</param>
public sealed record SiteOwnerStat(
    long SiteTurns,
    long ControlledTurns,
    long OwnerTurns,
    double OwnerShare,
    double OwnerShareOfControlled,
    double MeanOwnerFirstControlRound,
    int OwnerNeverControlled,
    int UnknownOwner);

/// <summary>
/// 据点分析（match-telemetry 平衡分析方向 10，scoring-sites 3.3）。
/// 纳入 = 日志首部有据点表（<see cref="LogHeader.Sites"/>）且每条小回合快照都有据点状态（<see cref="TurnSnapshot.Sites"/>）；
/// scoring-sites 之前的旧日志整局计入 <paramref name="Skipped"/>（R-7），MUST NOT 回填成"无人"。
/// </summary>
/// <param name="Campfire">篝火由所在低地主人控制（主人 = 篝火所在河外低地所属出生区的玩家）。</param>
/// <param name="SteleBridgehead">石碑由相邻桥头那家控制（桥头信物所在河外低地的主人）。</param>
/// <param name="MeanFinalSiteShare">逐局取终局快照（最后一条小回合快照）中参赛玩家的 Σ据点分 ÷ Σ总势力，再对纳入局取平均；Σ总势力为 0 的局不进均值。</param>
/// <param name="HighGroundShare">终局快照中参赛玩家全部棋串的 Σ高地加值 ÷ Σ位置加值（连珠 + 协同 + 高地），全批次合并计算。</param>
public sealed record SiteSection(
    int Matches,
    int Skipped,
    List<SiteTierStat> Tiers,
    SiteOwnerStat Campfire,
    SiteOwnerStat SteleBridgehead,
    double MeanFinalSiteShare,
    int FinalShareSamples,
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
    DominanceSection Dominance,
    TargetsSection Targets,
    LeaderSection Leader,
    SnowballSection Snowball,
    SelectionSection Selection,
    MultiplierSection Multiplier,
    PieceShareSection PieceShares,
    CatchUpSection CatchUp,
    BirthZoneSection BirthZones,
    GrowthAxisSection GrowthAxes,
    StallingSection Stalling,
    AiQualitySection AiQuality,
    SiteSection Sites,
    TerrainEditSection TerrainEdits);

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
        return new BalanceReport(
            logs.Count, included.Count, contaminated, failed, playerCount, options,
            Convergence(included),
            Dominance(included),
            Targets(included, options),
            Leader(included, options),
            Snowball(included),
            Selection(included),
            Multiplier(included),
            PieceShares(included),
            CatchUp(included),
            BirthZones(included),
            GrowthAxes(included),
            Stalling(included),
            AiQuality(included),
            Sites(included),
            TerrainEdits(included));
    }

    // ---------- §17 第 11 项 地形改造（artisan-terrain-edit 3.4） ----------

    private static TerrainEditSection TerrainEdits(List<MatchLog> logs)
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
            foreach (int editor in editors)
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

    // ---------- §17 第 10 项 据点（scoring-sites 3.3） ----------

    private static SiteSection Sites(List<MatchLog> logs)
    {
        string[] tiers = Enum.GetNames<SiteTier>();
        var instances = tiers.ToDictionary(t => t, _ => 0);
        var siteTurns = tiers.ToDictionary(t => t, _ => 0L);
        var controlled = tiers.ToDictionary(t => t, _ => 0L);
        var contested = tiers.ToDictionary(t => t, _ => 0L);
        var firstRounds = tiers.ToDictionary(t => t, _ => new List<int>());
        var never = tiers.ToDictionary(t => t, _ => 0);
        var controllerSamples = tiers.ToDictionary(t => t, _ => (Wins: 0, Samples: 0));
        var owner = new Dictionary<string, OwnerAccumulator>
        {
            [nameof(SiteTier.Campfire)] = new(),
            [nameof(SiteTier.Stele)] = new(),
        };
        int matches = 0;
        int skipped = 0;
        var finalShares = new List<double>();
        long highGround = 0;
        long positionBonus = 0;

        foreach (MatchLog log in logs)
        {
            if (log.Header.Sites is not { } sites || log.Turns.Count == 0 || log.Turns.Any(t => t.Sites is null))
            {
                skipped++;
                continue;
            }

            matches++;
            List<int> winners = log.Result!.Winners;
            var controllersByTier = tiers.ToDictionary(t => t, _ => new HashSet<int>());
            foreach (SiteEntry site in sites)
            {
                string tier = site.Tier;
                instances[tier]++;
                int[] owners = site.HomeZone is { } zone
                    ? [.. Enumerable.Range(0, log.Header.Zones.Count).Where(p => log.Header.Zones[p] == zone)]
                    : [];
                owner.TryGetValue(tier, out OwnerAccumulator? acc);
                int? first = null;
                int? ownerFirst = null;
                foreach (TurnSnapshot turn in log.Turns)
                {
                    SiteStateEntry state = turn.Sites!.Single(s => s.Coord == site.Coord);
                    siteTurns[tier]++;
                    if (state.Control == nameof(SiteControlKind.Contested))
                    {
                        contested[tier]++;
                    }

                    if (state.Holder is { } holder)
                    {
                        controlled[tier]++;
                        first ??= turn.MajorRound;
                        controllersByTier[tier].Add(holder);
                    }

                    if (acc is not null && site.HomeZone is not null)
                    {
                        acc.SiteTurns++;
                        if (state.Holder is { } h)
                        {
                            acc.ControlledTurns++;
                            if (owners.Contains(h))
                            {
                                acc.OwnerTurns++;
                                ownerFirst ??= turn.MajorRound;
                            }
                        }
                    }
                }

                if (first is { } f)
                {
                    firstRounds[tier].Add(f);
                }
                else
                {
                    never[tier]++;
                }

                if (acc is not null)
                {
                    if (site.HomeZone is null)
                    {
                        acc.Unknown++;
                    }
                    else if (ownerFirst is { } of)
                    {
                        acc.FirstRounds.Add(of);
                    }
                    else
                    {
                        acc.Never++;
                    }
                }
            }

            foreach (string tier in tiers)
            {
                foreach (int player in controllersByTier[tier])
                {
                    (int wins, int samples) = controllerSamples[tier];
                    controllerSamples[tier] = (wins + (winners.Contains(player) ? 1 : 0), samples + 1);
                }
            }

            List<PlayerEntry> active = [.. log.Turns[^1].PlayersState.Where(p => p.Status == nameof(PlayerStatus.Active))];
            long total = active.Sum(p => p.Total);
            if (total > 0 && active.All(p => p.SiteScore is not null))
            {
                finalShares.Add((double)active.Sum(p => p.SiteScore!.Value) / total);
            }

            List<GroupEntry> groups = [.. active.SelectMany(p => p.Groups)];
            if (groups.All(g => g.HighGroundBonus is not null))
            {
                highGround += groups.Sum(g => (long)g.HighGroundBonus!.Value);
                positionBonus += groups.Sum(g => (long)g.LineBonus + g.SynergyBonus + g.HighGroundBonus!.Value);
            }
        }

        static double Ratio(long a, long b) => b == 0 ? double.NaN : (double)a / b;

        List<SiteTierStat> stats = [.. tiers.Select(t => new SiteTierStat(
            t, instances[t], siteTurns[t], controlled[t], contested[t],
            Ratio(controlled[t], siteTurns[t]), Ratio(contested[t], siteTurns[t]),
            firstRounds[t].Count == 0 ? double.NaN : firstRounds[t].Average(), never[t],
            Statistics.Wilson(controllerSamples[t].Wins, controllerSamples[t].Samples)))];
        SiteOwnerStat OwnerStat(OwnerAccumulator a) => new(
            a.SiteTurns, a.ControlledTurns, a.OwnerTurns, Ratio(a.OwnerTurns, a.SiteTurns), Ratio(a.OwnerTurns, a.ControlledTurns),
            a.FirstRounds.Count == 0 ? double.NaN : a.FirstRounds.Average(), a.Never, a.Unknown);
        return new SiteSection(
            matches, skipped, stats,
            OwnerStat(owner[nameof(SiteTier.Campfire)]), OwnerStat(owner[nameof(SiteTier.Stele)]),
            finalShares.Count == 0 ? double.NaN : finalShares.Average(), finalShares.Count,
            highGround, positionBonus, Ratio(highGround, positionBonus));
    }

    private sealed class OwnerAccumulator
    {
        internal long SiteTurns { get; set; }

        internal long ControlledTurns { get; set; }

        internal long OwnerTurns { get; set; }

        internal int Never { get; set; }

        internal int Unknown { get; set; }

        internal List<int> FirstRounds { get; } = [];
    }

    // ---------- 落后者征募补偿 ----------

    private static CatchUpSection CatchUp(List<MatchLog> logs)
    {
        int matches = 0;
        int skipped = 0;
        int turns = 0;
        int compensated = 0;
        int revealTriggers = 0;
        int pickTriggers = 0;
        var finalRanks = new SortedDictionary<int, int>();
        foreach (MatchLog log in logs)
        {
            // 被排除样本：关闭补偿的局、以及 catch-up-recruit 之前没有留痕字段的旧日志。
            if (log.Header.CatchUpRecruit != true || log.Turns.Count == 0 || log.Turns.Any(t => t.CatchUpReveal is null || t.CatchUpPick is null))
            {
                skipped++;
                continue;
            }

            matches++;
            var compensatedPlayers = new HashSet<int>();
            foreach (TurnSnapshot turn in log.Turns)
            {
                turns++;
                int reveal = turn.CatchUpReveal!.Value;
                int pick = turn.CatchUpPick!.Value;
                revealTriggers += reveal;
                pickTriggers += pick;
                if (reveal + pick > 0)
                {
                    compensated++;
                    compensatedPlayers.Add(turn.Player);
                }
            }

            foreach (StandingEntry entry in log.Result!.Standings.Where(s => compensatedPlayers.Contains(s.Player)))
            {
                finalRanks[entry.Rank] = finalRanks.TryGetValue(entry.Rank, out int n) ? n + 1 : 1;
            }
        }

        return new CatchUpSection(
            matches, skipped, turns, compensated, Statistics.Wilson(compensated, turns), revealTriggers, pickTriggers, finalRanks);
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

        // round-cap D5：不收敛口径 = 终局原因为规则级 MajorRoundLimit 的局占比。
        int capped = logs.Count(l => l.Result!.Reason == nameof(EndReason.MajorRoundLimit));
        return new ConvergenceSection(
            logs.Count - capped,
            capped,
            Statistics.Wilson(capped, logs.Count),
            reasons,
            Statistics.Mean(logs.Where(l => l.Result!.Converged).Select(l => (double)l.Result!.MajorRound)),
            Statistics.Mean(logs.Select(l => (double)l.Result!.MajorRound)),
            Statistics.Mean(logs.Select(l => (double)l.Result!.TurnCount)));
    }

    // ---------- 势力碾压 ----------

    private static DominanceSection Dominance(List<MatchLog> logs)
    {
        List<LogResult> wins = [.. logs.Select(l => l.Result!).Where(r => r.Reason == nameof(EndReason.PowerDominance))];
        var ratios = new List<double>();
        int undefined = 0;
        foreach (LogResult r in wins)
        {
            // 名次序列即 FinalStandings 的输出顺序：碾压获胜者第 1，其后第一项即第 2 名。
            List<StandingEntry> ordered = [.. r.Standings.OrderBy(s => s.Rank)];
            if (ordered.Count < 2 || ordered[1].Power == 0)
            {
                undefined++;
                continue;
            }

            ratios.Add((double)ordered[0].Power / ordered[1].Power);
        }

        return new DominanceSection(
            logs.Count,
            wins.Count,
            Statistics.Wilson(wins.Count, logs.Count),
            Statistics.Mean(wins.Select(r => (double)r.MajorRound)),
            Statistics.Mean(ratios),
            ratios.Count,
            undefined);
    }

    // ---------- §16 ----------

    private static TargetsSection Targets(List<MatchLog> logs, AnalysisOptions options)
    {
        var phase1 = new SortedDictionary<int, int>();
        var phase2 = new SortedDictionary<int, int>();
        var phase3 = new SortedDictionary<int, int>();
        var powerByRound = new SortedDictionary<int, List<double>>();
        var maxGroupByRound = new SortedDictionary<int, long>();
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

                foreach (PlayerEntry p in snapshot.PlayersState.Where(p => p.Status == "Active"))
                {
                    list.Add(p.Total);
                    long maxGroup = p.Groups.Count == 0 ? 0 : p.Groups.Max(g => g.Power);
                    maxGroupByRound[round] = Math.Max(maxGroupByRound.TryGetValue(round, out long m) ? m : 0, maxGroup);
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

        List<(int, double, long, int)> curve = [.. powerByRound.Select(kv =>
            (kv.Key, Statistics.Mean(kv.Value), maxGroupByRound.TryGetValue(kv.Key, out long m) ? m : 0, kv.Value.Count))];
        double opening = Statistics.Mean(powerByRound.Where(kv => kv.Key <= 2).SelectMany(kv => kv.Value));
        double mid = Statistics.Mean(powerByRound.Where(kv => kv.Key is >= 4 and <= 6).SelectMany(kv => kv.Value));
        double conflictMean = Statistics.Mean(firstConflict.SelectMany(kv => Enumerable.Repeat((double)kv.Key, kv.Value)));
        double endMean = Statistics.Mean(endRounds.SelectMany(kv => Enumerable.Repeat((double)kv.Key, kv.Value)));
        LeaderSection leader = Leader(logs, options);

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

        return [.. log.Header.Players.Where(p => e.Values.TryGetValue($"P{p}.Rank", out long rank) && rank == 1)];
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

    private static SnowballSection Snowball(List<MatchLog> logs)
    {
        var byPosition = new SortedDictionary<int, (int Improved, int Same, int Worse)>();
        int firstSamples = 0;
        int firstStays = 0;
        var finalByRound3 = new SortedDictionary<int, List<double>>();
        foreach (MatchLog log in logs)
        {
            List<LogEvent> ends = [.. log.Events.Where(e => e.Type == LogEventType.MajorRoundEnded && e.Values is not null).OrderBy(e => e.MajorRound)];
            for (int i = 0; i + 1 < ends.Count; i++)
            {
                Dictionary<string, long> now = ends[i].Values!;
                Dictionary<string, long> next = ends[i + 1].Values!;
                foreach (int p in log.Header.Players)
                {
                    if (!now.TryGetValue($"P{p}.Next", out long position) || !now.TryGetValue($"P{p}.Rank", out long rank)
                        || !next.TryGetValue($"P{p}.Rank", out long nextRank))
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
                        if (next.TryGetValue($"P{p}.Next", out long nextPosition) && nextPosition == 0)
                        {
                            firstStays++;
                        }
                    }
                }
            }

            LogEvent? third = ends.FirstOrDefault(e => e.MajorRound == 3);
            if (third is not null)
            {
                foreach (int p in log.Header.Players)
                {
                    if (third.Values!.TryGetValue($"P{p}.Next", out long position))
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

    private static SelectionSection Selection(List<MatchLog> logs)
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

            foreach ((int player, HashSet<string> types) in pickedBy)
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

            foreach ((int player, HashSet<string> types) in controlledBy)
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
        var effective = new SortedDictionary<int, int>();
        var rounds = new List<double>();
        var powers = new List<double>();
        long max = 0;
        int destroyed = 0;
        int peaks = 0;
        foreach (PeakEntry peak in logs.Select(l => l.Result!).Where(r => r.Peak is not null).Select(r => r.Peak!))
        {
            peaks++;
            distribution[peak.MultiplierCount] = distribution.TryGetValue(peak.MultiplierCount, out int n) ? n + 1 : 1;
            effective[peak.EffectiveMultiplierCount] = effective.TryGetValue(peak.EffectiveMultiplierCount, out int e) ? e + 1 : 1;
            rounds.Add(peak.MajorRound);
            powers.Add(peak.Power);
            max = Math.Max(max, peak.Power);
        }

        foreach (LogResult r in logs.Select(l => l.Result!).Where(r => r.PeakDestroyed == true))
        {
            destroyed++;
        }

        return new MultiplierSection(peaks, distribution, effective, Statistics.Mean(rounds), Statistics.Mean(powers), max, Statistics.Wilson(destroyed, peaks));
    }

    // ---------- 各棋子势力占比（multiplier-rebalance 裁决 3） ----------

    private static PieceShareSection PieceShares(List<MatchLog> logs)
    {
        PieceType[] types = Enum.GetValues<PieceType>();
        var stones = types.ToDictionary(t => t, _ => 0L);
        var power = types.ToDictionary(t => t, _ => 0L);
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
                long amplified = new Siege.Core.Scoring.Multiplier(g.EffectiveMultiplierCount).Apply(g.Base) - g.Base;
                foreach (PieceType type in types)
                {
                    int count = g.PieceCounts!.TryGetValue(type.ToString(), out int n) ? n : 0;
                    if (count == 0)
                    {
                        continue;
                    }

                    stones[type] += count;
                    power[type] += (long)PieceEffects.BasePower(type) * count + type switch
                    {
                        PieceType.Line => g.LineBonus,
                        PieceType.Synergy => g.SynergyBonus,
                        PieceType.Multiplier => amplified,
                        _ => 0,
                    };
                }
            }
        }

        long totalStones = stones.Values.Sum();
        long totalPower = power.Values.Sum();
        List<PieceShare> pieces = [.. types.Select(t => new PieceShare(
            t.ToString(),
            stones[t],
            totalStones == 0 ? double.NaN : (double)stones[t] / totalStones,
            power[t],
            totalPower == 0 ? double.NaN : (double)power[t] / totalPower,
            stones[t] == 0 ? double.NaN : (double)power[t] / stones[t]))];
        return new PieceShareSection(matches, skipped, totalStones, totalPower, pieces);
    }

    // ---------- §17.5 出生区（裁决 8：收敛 / 未收敛分组） ----------

    private static BirthZoneSection BirthZones(List<MatchLog> logs)
    {
        // 行数 = 地图出生区数，取自日志首部；旧日志（frontier-map 之前）无该字段，回填为被选到过的最大区号 + 1（标准档上即真值）。
        // 首部区数与被选区号取较大者：首部偏小（手改日志 / 混批）时不得把越界区的样本静默丢掉。
        int zones = logs.Count == 0 ? 0 : logs.Max(l => Math.Max(l.Header.ZoneCount ?? 0, l.Header.Zones.Count == 0 ? 0 : l.Header.Zones.Max() + 1));
        // 基线 = 1 / 参赛人数：各区胜率的分母是"该区被选中的局数"，被选中的区上那名玩家的期望胜率是 1/人数，与地图有几个区无关。
        // 出生区多于玩家的图（边疆档 6 区 4 人）若按 1/区数 = 1/6 取基线，公平的 25% 会被系统性判成"显著偏高"（frontier-map 2.4）。
        double baseline = zones == 0 ? double.NaN : 1.0 / logs[0].Header.Players.Count;
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
            BySide = logs.Any(l => l.Header.Config.MapPerMatch) ? SideStats(logs, baseline) : null,
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

                long before = log.Turns[i - 1].PlayersState.First(p => p.Player == turn.Player).Total;
                long after = turn.PlayersState.First(p => p.Player == turn.Player).Total;
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
