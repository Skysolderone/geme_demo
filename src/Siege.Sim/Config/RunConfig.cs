using System.Text.Json;
using System.Text.Json.Serialization;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Sim.Config;

/// <summary>事件流保留策略（design.md D5 / implement 7.5）。</summary>
public enum EventRetention
{
    /// <summary>只留每小回合快照 + 关键事件（征募、结算、拒绝、揭示、控制 / 名次变化、流程事件）；丢弃预演失败与候选批次明细。</summary>
    SnapshotsOnly,

    /// <summary>保留完整事件流。失败局与按种子抽样命中的局强制为此模式。</summary>
    Full,
}

/// <summary>一名玩家的 AI 配置。<see cref="DebugAi"/> 为 <c>true</c> 时用调试 AI（测试专用，日志标注、分析默认排除）。</summary>
public sealed record PlayerAiConfig
{
    public AiDifficulty Difficulty { get; init; } = AiDifficulty.Standard;

    /// <summary>七维权重；<c>null</c> 用 <see cref="EvaluationWeights.Default"/>。</summary>
    public EvaluationWeights? Weights { get; init; }

    /// <summary>剪枝参数 N / M；<c>null</c> 用难度默认。</summary>
    public AiSearchConfig? Search { get; init; }

    public bool DebugAi { get; init; }
}

/// <summary>
/// 批量跑局配置（simulation-harness「批量跑局」）：地图、玩家、种子范围、局数、并行度、大回合上限、事件保留。可完整序列化并随结果保存。
/// </summary>
public sealed record RunConfig
{
    /// <summary>默认大回合上限 = 规则层标准局初值（round-cap D3：跑局层只传值，不再有自己的"达上限"语义）。</summary>
    public const int DefaultMaxMajorRounds = MatchOptions.DefaultMaxMajorRounds;

    /// <summary>默认小回合硬停（round-cap D3：上限为 0 时的防死锁保险，以异常记为失败局，不是终局原因）。</summary>
    public const int DefaultMaxTurns = 1_000;

    /// <summary>地图标识或地图 JSON 文件路径。</summary>
    public string MapId { get; init; } = MapCatalog.DefaultId;

    /// <summary>各玩家配置；玩家编号即下标（P0、P1……），插旗时 P<i>i</i> 锁定出生区 <i>i</i>。</summary>
    public List<PlayerAiConfig> Players { get; init; } = [new(), new(), new(), new()];

    /// <summary>首个种子；第 <i>i</i> 局的种子 = SeedStart + i。</summary>
    public ulong SeedStart { get; init; } = 1;

    /// <summary>局数。</summary>
    public int Count { get; init; } = 1;

    /// <summary>并行度；0 = 处理器数。</summary>
    public int Parallelism { get; init; }

    /// <summary>大回合上限，直接写入对局配置 <see cref="MatchOptions.MaxMajorRounds"/>（0 = 不限，只受其余终局条件与 <see cref="MaxTurns"/> 约束）。</summary>
    public int MaxMajorRounds { get; init; } = DefaultMaxMajorRounds;

    /// <summary>碾压起始大回合，直接写入对局配置 <see cref="MatchOptions.DominanceStartRound"/>（0 = 关闭势力碾压；默认 = 规则层标准局初值）。</summary>
    public int DominanceStartRound { get; init; } = MatchOptions.DefaultDominanceStartRound;

    /// <summary>落后者征募补偿开关，直接写入对局配置 <see cref="MatchOptions.CatchUpRecruit"/>（默认 = 规则层标准局初值，开启）。</summary>
    public bool CatchUpRecruit { get; init; } = MatchOptions.DefaultCatchUpRecruit;

    /// <summary>
    /// 据点分值（营帐 / 篝火 / 石碑），直接写入对局配置 <see cref="MatchOptions.SiteValues"/>（simulation-harness「批量跑局」：未配置取标准局值 5 / 15 / 45）。
    /// 命令行 <c>--site-values 3/8/24</c> 或配置文件 <c>"SiteValues": { "Tent": 3, "Campfire": 8, "Stele": 24 }</c>。
    /// </summary>
    public SiteValues SiteValues { get; init; } = SiteValues.Standard;

    /// <summary>
    /// 匠人征募权重，直接写入对局配置 <see cref="MatchOptions.ArtisanWeight"/>（artisan-terrain-edit R-2：未配置取 10）。
    /// 其余五种类型的基础权重不随它变化。
    /// </summary>
    public int ArtisanWeight { get; init; } = MatchOptions.DefaultArtisanWeight;

    /// <summary>
    /// AI 候选格上限 K（<see cref="AiSearchConfig.CandidateCellLimit"/>，frontier-map 裁决 12）：<c>null</c> = 按地图的可落子格数自动取
    /// （<see cref="AiSearchConfig.DefaultCellLimitFor"/>：大图取缺省 K，其余 0）；0 = 不限制；大于 0 = 显式上限。
    /// 只作用于未显式配置 <see cref="PlayerAiConfig.Search"/> 的玩家——显式的剪枝参数原样生效。命令行 <c>--cell-limit</c>。
    /// </summary>
    public int? CandidateCellLimit { get; init; }

    /// <summary>单局小回合数硬停（防死锁），超出即抛异常记为失败局；上限为 0 时是唯一的兜底。</summary>
    public int MaxTurns { get; init; } = DefaultMaxTurns;

    public EventRetention EventRetention { get; init; } = EventRetention.SnapshotsOnly;

    /// <summary>按种子抽样保留完整事件流的千分比（子流 <c>sim-sample</c>，与对局子流无关）。</summary>
    public int FullEventSamplePermille { get; init; } = 20;

    /// <summary>日志文件是否 GZip 压缩（约 10×；分析工具自动解压）。</summary>
    public bool Compress { get; init; }

    /// <summary><b>测试专用</b>：在第 N 个小回合结束后模拟一次断言失败，用于验证失败局的保留与复现。</summary>
    public int? InjectFailureAtTurn { get; init; }

    public int PlayerCount => Players.Count;

    /// <summary>第 <paramref name="index"/> 局的种子。</summary>
    public ulong SeedAt(int index) => checked(SeedStart + (ulong)index);

    /// <summary>玩家编号列表。</summary>
    public PlayerId[] PlayerIds() => [.. Enumerable.Range(0, Players.Count).Select(i => new PlayerId(i))];

    /// <summary>有效并行度。</summary>
    public int EffectiveParallelism => Parallelism > 0 ? Parallelism : Environment.ProcessorCount;

    /// <summary>基本校验：人数、局数、上限。</summary>
    public RunConfig Validated()
    {
        if (Players.Count < 2)
        {
            throw new ArgumentException("对局至少需要两名玩家。");
        }

        if (Count < 1 || MaxTurns < 1)
        {
            throw new ArgumentException("局数与小回合硬停至少为 1。");
        }

        if (MaxMajorRounds < 0)
        {
            throw new ArgumentException("大回合上限须为非负整数（0 = 不限）。");
        }

        if (DominanceStartRound < 0)
        {
            throw new ArgumentException("碾压起始大回合须为非负整数（0 = 关闭）。");
        }

        if (SiteValues is null)
        {
            throw new ArgumentException("据点分值不得为空。");
        }

        SiteValues.Validated();

        if (ArtisanWeight < 0)
        {
            throw new ArgumentException("匠人征募权重须为非负整数（0 = 匠人不进池）。");
        }

        if (CandidateCellLimit < 0)
        {
            throw new ArgumentException("候选格上限须为非负整数（0 = 不限制）。");
        }

        if (FullEventSamplePermille is < 0 or > 1000)
        {
            throw new ArgumentException("抽样千分比须在 0..1000。");
        }

        foreach (PlayerAiConfig p in Players)
        {
            p.Search?.Validated();
        }

        return this;
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// 实际生效的配置：未显式配置权重的玩家填入 <see cref="EvaluationWeights.Default"/>（AI 侧同样按 <c>weights ?? Default</c> 取值，行为不变）。
    /// 批次 <c>config.json</c> 写它，使四名玩家的完整权重与据点分值如实可查（simulation-harness「扫档配置可追溯」）。
    /// </summary>
    public RunConfig Effective() =>
        this with { Players = [.. Players.Select(p => p with { Weights = p.Weights ?? EvaluationWeights.Default })] };

    /// <summary>
    /// 把"按地图自动"的候选格上限落成具体数值，使批次 <c>config.json</c> 与日志首部如实记录实际生效的 K。
    /// 已显式配置、或自动值为 0（小图）时原样返回——标准图上的配置记录与日志首部与引入本项之前逐字节相同。幂等。
    /// </summary>
    public RunConfig ResolvedFor(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return CandidateCellLimit is null && AiSearchConfig.DefaultCellLimitFor(map.PlayableCount) is > 0 and int auto
            ? this with { CandidateCellLimit = auto }
            : this;
    }

    /// <summary>解析 <c>营帐/篝火/石碑</c> 形式的据点分值（如 <c>3/8/24</c>），并做 <see cref="SiteValues.Validated"/> 校验。</summary>
    public static SiteValues ParseSiteValues(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] parts = text.Split('/');
        if (parts.Length != 3)
        {
            throw new ArgumentException($"据点分值须写成 营帐/篝火/石碑（如 5/15/45），实际 {text}。");
        }

        int[] values = [.. parts.Select(p => int.Parse(p.Trim(), System.Globalization.CultureInfo.InvariantCulture))];
        return new SiteValues(values[0], values[1], values[2]).Validated();
    }

    public static RunConfig FromJson(string json) =>
        (JsonSerializer.Deserialize<RunConfig>(json, JsonOptions) ?? throw new FormatException("配置 JSON 为空。")).Validated();
}
