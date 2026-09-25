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
/// 批量跑局配置（simulation-harness「批量跑局」）：地图、玩家、种子范围、局数、并行度、事件保留。可完整序列化并随结果保存。
/// </summary>
public sealed record RunConfig
{
    /// <summary>
    /// 默认小回合数截断（restore-go-core-rules D5，对应基准文档的 <c>MAX_TURNS</c>）：跑满即停、该局记 <c>turn_limit</c>、无名次。
    /// 只存在于跑局驱动循环，<b>不是</b>终局条件，Siege.Core 不知道它。
    /// </summary>
    public const int DefaultTurnLimit = 600;

    /// <summary>默认小回合硬停（防死锁保险，以异常记为失败局，不是终局原因）。截断为 0（不截断，仅供调试）时它是唯一兜底。</summary>
    public const int DefaultMaxTurns = 1_000;

    /// <summary>地图标识或地图 JSON 文件路径。</summary>
    public string MapId { get; init; } = MapCatalog.DefaultId;

    /// <summary>
    /// 每局换图（map-generator D6）：<see cref="MapId"/> 须是生成图标识 <c>gen:&lt;起始地图种子&gt;[:p&lt;平台数&gt;]</c>，第 <i>i</i> 局用地图种子 <c>起始 + i</c>、
    /// 平台数不变（<see cref="MapIdAt"/>）。起始地图种子与平台数就写在 <see cref="MapId"/> 里（平台数为缺省 6 时按规范化写法省略），不另设字段——
    /// 同一件事只有一处记录。各局的完整地图标识写入该局日志首部的 <see cref="Logging.LogHeader.MapId"/>；首部里的 <see cref="MapId"/> 仍是批次的起始标识。
    /// 为 <c>false</c> 时不写出（标准批次的配置记录与日志首部与引入本项之前逐字节相同）。命令行 <c>--map-per-match</c>。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MapPerMatch { get; init; }

    /// <summary>各玩家配置；玩家编号即下标（P0、P1……）。标准图上冒险概率为 0 时插旗 P<i>i</i> 锁定出生区 <i>i</i>，否则见 <see cref="FlagRisk"/>。</summary>
    public List<PlayerAiConfig> Players { get; init; } = [new(), new(), new(), new()];

    /// <summary>首个种子；第 <i>i</i> 局的种子 = SeedStart + i。</summary>
    public ulong SeedStart { get; init; } = 1;

    /// <summary>局数。</summary>
    public int Count { get; init; } = 1;

    /// <summary>并行度；0 = 处理器数。</summary>
    public int Parallelism { get; init; }

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

    /// <summary>
    /// AI 停手阈值（<see cref="AiSearchConfig.PassThreshold"/>，ai-eye D4）：<c>null</c> = 新建的局取 <see cref="AiSearchConfig.DefaultPassThreshold"/>，
    /// 并由 <see cref="ResolvedFor"/> 落成具体数值写进批次 <c>config.json</c> 与日志首部；按日志首部重建（回放）时缺该项即该项出现之前的旧日志，按 0 重建。
    /// 与候选格上限同口径：只作用于未显式配置 <see cref="PlayerAiConfig.Search"/> 的玩家——显式的剪枝参数（含其中的阈值）原样生效。命令行 <c>--pass-threshold</c>。
    /// </summary>
    public int? PassThreshold { get; init; }

    /// <summary>
    /// 原型插旗的冒险概率（<see cref="MatchOptions.FlagRisk"/>，flag-contest D2；0–100）：<c>null</c> = 新建的局取 <see cref="MatchOptions.DefaultFlagRisk"/>，
    /// 并由 <see cref="ResolvedFor"/> 落成具体数值写进批次 <c>config.json</c> 与日志首部；按日志首部重建（回放）时缺该项即该项出现之前的旧日志，按 0 重建
    /// （p = 0 与引入之前逐项相同）。命令行 <c>--flag-risk</c>。
    /// </summary>
    public int? FlagRisk { get; init; }

    /// <summary>单局小回合数硬停（防死锁），超出即抛异常记为失败局；上限为 0 时是唯一的兜底。</summary>
    public int MaxTurns { get; init; } = DefaultMaxTurns;

    public EventRetention EventRetention { get; init; } = EventRetention.SnapshotsOnly;

    /// <summary>
    /// 小回合数截断（D5；0 = 不截断，仅供调试）：已跑满这么多个小回合仍未终局即停止驱动，该局以结束原因 <see cref="Logging.LogResult.TurnLimitReason"/> 记录，
    /// MUST NOT 产生名次与胜者。规则层对它一无所知——人机对局不受其约束。
    /// </summary>
    public int TurnLimit { get; init; } = DefaultTurnLimit;

    /// <summary>按种子抽样保留完整事件流的千分比（子流 <c>sim-sample</c>，与对局子流无关）。</summary>
    public int FullEventSamplePermille { get; init; } = 20;

    /// <summary>日志文件是否 GZip 压缩（约 10×；分析工具自动解压）。</summary>
    public bool Compress { get; init; }

    /// <summary><b>测试专用</b>：在第 N 个小回合结束后模拟一次断言失败，用于验证失败局的保留与复现。</summary>
    public int? InjectFailureAtTurn { get; init; }

    public int PlayerCount => Players.Count;

    /// <summary>第 <paramref name="index"/> 局的种子。</summary>
    public ulong SeedAt(int index) => checked(SeedStart + (ulong)index);

    /// <summary>第 <paramref name="index"/> 局的地图标识：每局换图时为 <c>gen:&lt;起始 + index&gt;[:p&lt;N&gt;]</c>（规范化），否则恒为 <see cref="MapId"/>。</summary>
    public string MapIdAt(int index)
    {
        if (!MapPerMatch)
        {
            return MapId;
        }

        (ulong start, MapGenParameters parameters) = GeneratedMapId.Parse(MapId);
        return GeneratedMapId.Format(checked(start + (ulong)index), parameters);
    }

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

        if (TurnLimit < 0)
        {
            throw new ArgumentException("小回合数截断须为非负整数（0 = 不截断）。");
        }

        if (MapPerMatch)
        {
            if (!GeneratedMapId.IsGenerated(MapId) || GeneratedMapId.IsBareRequest(MapId))
            {
                throw new ArgumentException($"每局换图要求地图标识是带起始地图种子的生成图标识（gen:<起始地图种子>[:p<平台数>]），实际为 {MapId}。");
            }

            try
            {
                _ = MapIdAt(Count - 1);   // 格式、平台数范围与种子上溢在开跑之前报出
            }
            catch (OverflowException)
            {
                throw new ArgumentException($"每局换图：起始地图标识 {MapId} 加上 {Count} 局会超出地图种子的范围（无符号 64 位）。");
            }
        }

        if (ArtisanWeight < 0)
        {
            throw new ArgumentException("匠人征募权重须为非负整数（0 = 匠人不进池）。");
        }

        if (CandidateCellLimit < 0)
        {
            throw new ArgumentException("候选格上限须为非负整数（0 = 不限制）。");
        }

        if (PassThreshold < 0)
        {
            throw new ArgumentException("停手阈值须为非负整数（0 = 严格提高即保留）。");
        }

        if (FlagRisk is < 0 or > 100)
        {
            throw new ArgumentException("冒险概率须为 0–100 的整数百分比（0 = 不冒险，与引入之前逐项相同）。");
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
    /// 批次 <c>config.json</c> 写它，使四名玩家的完整权重如实可查（simulation-harness「扫档配置可追溯」）。
    /// </summary>
    public RunConfig Effective() =>
        this with { Players = [.. Players.Select(p => p with { Weights = p.Weights ?? EvaluationWeights.Default })] };

    /// <summary>
    /// 把"按地图自动"的候选格上限与缺省停手阈值、缺省冒险概率落成具体数值，使批次 <c>config.json</c> 与日志首部如实记录实际生效的 K、阈值与 p。
    /// K 已显式配置、或自动值为 0（小图）时不写（标准图上这一项与引入之前相同）；阈值未配置时一律落成 <see cref="AiSearchConfig.DefaultPassThreshold"/>，
    /// 冒险概率未配置时一律落成 <see cref="MatchOptions.DefaultFlagRisk"/>（两者缺省都非 0，不落成就无法与"首部缺该项 = 旧日志 = 0"区分）。幂等。
    /// </summary>
    public RunConfig ResolvedFor(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        RunConfig resolved = CandidateCellLimit is null && AiSearchConfig.DefaultCellLimitFor(map.PlayableCount) is > 0 and int auto
            ? this with { CandidateCellLimit = auto }
            : this;
        resolved = resolved.PassThreshold is null ? resolved with { PassThreshold = AiSearchConfig.DefaultPassThreshold } : resolved;
        return resolved.FlagRisk is null ? resolved with { FlagRisk = MatchOptions.DefaultFlagRisk } : resolved;
    }

    /// <summary>
    /// 已删除的配置项（restore-go-core-rules）→ 删除说明。配置文件（<c>run --config</c>）里出现任何一个都 MUST 报错点名，MUST NOT 静默忽略：
    /// 一份旧扫档配置照常"跑通"、实际却没按它写的上限 / 分值跑，是最难发现的口径错误（testing.md「静默忽略的输入会产出口径错误的数据」）。
    /// 比对大小写不敏感（与地图文件、存档的废弃字段同口径）；其余未知键照旧宽容（<c>_comment</c> 等）。
    /// 旧日志首部里的配置不经本方法（直接反序列化），照常可读。
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> RetiredKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SiteValues"] = "据点已整体移除（restore-go-core-rules 裁决 #14），没有据点分值可配",
        ["MaxMajorRounds"] = "大回合上限终局已删除（restore-go-core-rules 裁决 #4），对局只剩三类终局；跑局要限长请用 TurnLimit（小回合数截断）",
        ["DominanceStartRound"] = "势力碾压已删除（restore-go-core-rules 裁决 #4）",
        ["CatchUpRecruit"] = "落后者征募补偿已删除（restore-go-core-rules 裁决 #7）",
    };

    public static RunConfig FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                {
                    if (RetiredKeys.TryGetValue(property.Name, out string? why))
                    {
                        throw new FormatException($"配置项 {property.Name} 已删除：{why}。请从配置文件里去掉它。");
                    }
                }
            }
        }

        return (JsonSerializer.Deserialize<RunConfig>(json, JsonOptions) ?? throw new FormatException("配置 JSON 为空。")).Validated();
    }
}
