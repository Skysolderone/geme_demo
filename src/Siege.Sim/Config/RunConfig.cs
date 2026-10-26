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
    /// <summary>
    /// 难度。配置文件与日志首部按名称读写，读入经 Core 的唯一解析 <see cref="AiDifficultyNames"/>（属性级转换器优先于序列化选项里的
    /// <see cref="JsonStringEnumConverter"/>——后者会接受数字与数字串）：数字、未知名称、空值与 <c>null</c> 在跑局之前报错（expert-lookahead D10）。
    /// 写出仍是规范名称，既有配置记录与日志首部逐字节不变。
    /// </summary>
    [JsonConverter(typeof(AiDifficultyNameConverter))]
    public AiDifficulty Difficulty { get; init; } = AiDifficulty.Standard;

    /// <summary>七维权重；<c>null</c> 用 <see cref="EvaluationWeights.Default"/>。</summary>
    public EvaluationWeights? Weights { get; init; }

    /// <summary>剪枝参数 N / M；<c>null</c> 用难度默认。</summary>
    public AiSearchConfig? Search { get; init; }

    public bool DebugAi { get; init; }
}

/// <summary>
/// <see cref="PlayerAiConfig.Difficulty"/> 的读写：读入只认字符串名称并经 <see cref="AiDifficultyNames.Parse"/>，不合法抛 <see cref="ArgumentException"/>（列出可用名称）；
/// 写出为枚举名（与 <see cref="JsonStringEnumConverter"/> 的输出相同）。
/// </summary>
internal sealed class ScoringVersionNameConverter : JsonConverter<ScoringVersion>
{
    public override ScoringVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? ScoringVersions.Parse(reader.GetString())
            : throw new ArgumentException($"计分规则版本须为名称字符串（{ScoringVersions.Usage}），不接受 {reader.TokenType}。");

    public override void Write(Utf8JsonWriter writer, ScoringVersion value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ScoringVersions.RequireValid(value).ToString());
}

internal sealed class AiDifficultyNameConverter : JsonConverter<AiDifficulty>
{
    public override bool HandleNull => true;

    public override AiDifficulty Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? AiDifficultyNames.Parse(reader.GetString())
            : throw new ArgumentException($"玩家难度须写成名称字符串（{AiDifficultyNames.Usage}），不接受数字或 {reader.TokenType}。");

    public override void Write(Utf8JsonWriter writer, AiDifficulty value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
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

    /// <summary>地图标识：链接图 <c>link:&lt;地图种子&gt;</c>。缺省 <see cref="MapCatalog.DefaultId"/>（<c>link:1</c>；retire-legacy-maps：原为 v5，随旧地图删除）。</summary>
    public string MapId { get; init; } = MapCatalog.DefaultId;

    /// <summary>
    /// 每局换图（map-generator D6）：<see cref="MapId"/> 须是带起始地图种子的链接图标识 <c>link:&lt;起始地图种子&gt;</c>，第 <i>i</i> 局用地图种子 <c>起始 + i</c>
    /// （<see cref="MapIdAt"/>；retire-legacy-maps：<c>gen:</c> 生成图随旧生成器删除）。起始地图种子就写在 <see cref="MapId"/> 里，不另设字段——
    /// 同一件事只有一处记录。各局的完整地图标识写入该局日志首部的 <see cref="Logging.LogHeader.MapId"/>；首部里的 <see cref="MapId"/> 仍是批次的起始标识。
    /// 为 <c>false</c> 时不写出（标准批次的配置记录与日志首部与引入本项之前逐字节相同）。命令行 <c>--map-per-match</c>。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MapPerMatch { get; init; }

    /// <summary>各玩家配置；玩家编号即下标（P0、P1……）。出生棋盘由盲选插旗决定（<see cref="MatchFlow.PlantPrototype"/>）。</summary>
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
    /// 大回合上限安全网（<see cref="MatchOptions.RoundCap"/>，cycle-stop D2；0 = 不启用）：<c>null</c> = 新建的局取 <see cref="MatchOptions.DefaultRoundCap"/>，
    /// 并由 <see cref="ResolvedFor"/> 落成具体数值写进批次 <c>config.json</c> 与日志首部；按日志首部重建（回放）时缺该项即该项出现之前的旧日志，
    /// 按 100 重建（裁决 C4 / D4：安全网默认开启，与候选格上限 / 停手阈值"缺项按引入前行为"相反；现存旧日志最长 94 个大回合，到不了上限）。命令行 <c>--round-cap</c>。
    /// </summary>
    public int? RoundCap { get; init; }

    /// <summary>
    /// AI 停滞大回合数 K（<see cref="AiSearchConfig.StagnationRounds"/>，cycle-stop D3；0 = 不启用）：<c>null</c> = 新建的局取 <see cref="AiSearchConfig.DefaultStagnationRounds"/>，
    /// 并由 <see cref="ResolvedFor"/> 落成具体数值写进批次 <c>config.json</c> 与日志首部；按日志首部重建（回放）时缺该项即该项出现之前的旧日志，按 0 重建。
    /// 与停手阈值同口径：只作用于未显式配置 <see cref="PlayerAiConfig.Search"/> 的玩家。命令行 <c>--stagnation-rounds</c>。
    /// </summary>
    public int? StagnationRounds { get; init; }

    /// <summary>
    /// AI 整批候选开关（<see cref="AiSearchConfig.WholeBatch"/>，ai-whole-batch D4；0 / 1）：<c>null</c> = 新建的局取 <see cref="AiSearchConfig.DefaultWholeBatch"/>（1），
    /// 并由 <see cref="ResolvedFor"/> 落成具体数值写进批次 <c>config.json</c> 与日志首部；与停滞大回合数同口径：只作用于未显式配置 <see cref="PlayerAiConfig.Search"/> 的玩家。
    /// 值为 0 时 MUST NOT 写出（规格「整批候选」：关闭时日志首部不含该字段，与引入之前逐字节相同），按日志首部重建（回放）时缺该项按 0 重建。
    /// 命令行 <c>--whole-batch</c>。序列化只经 <see cref="WholeBatchRecorded"/>。
    /// </summary>
    [JsonIgnore]
    public int? WholeBatch { get; init; }

    /// <summary>
    /// <see cref="WholeBatch"/> 的序列化影子：0 写成缺省（不写出），读入原样。于是"显式 0"与"缺该项的旧记录"在文本上相同、读回都按 0 重建（会话里 <c>recorded</c> 取 0）；
    /// 代价：把一份 <c>--whole-batch 0</c> 批次的 <c>config.json</c> 当作新配置再跑时，缺该项按缺省 1 落成（见任务 10-22-ai-whole-batch 的 implement.md 待决项）。
    /// </summary>
    [JsonInclude]
    [JsonPropertyName(nameof(WholeBatch))]
    private int? WholeBatchRecorded
    {
        get => WholeBatch == 0 ? null : WholeBatch;
        init => WholeBatch = value;
    }

    /// <summary>
    /// AI 搭桥候选开关（<see cref="AiSearchConfig.EditSeed"/>，ai-bridge-candidate D3；0 / 1）：<c>null</c> = 新建的局取 <see cref="AiSearchConfig.DefaultEditSeed"/>（1），
    /// 并由 <see cref="ResolvedFor"/> 落成具体数值写进批次 <c>config.json</c> 与日志首部；与整批候选开关同口径：只作用于未显式配置 <see cref="PlayerAiConfig.Search"/> 的玩家。
    /// 值为 0 时 MUST NOT 写出（规格「搭桥候选」：关闭时日志首部不含该字段，与引入之前逐字节相同），按日志首部重建（回放）时缺该项按 0 重建。
    /// 命令行 <c>--edit-seed</c>。序列化只经 <see cref="EditSeedRecorded"/>。
    /// </summary>
    [JsonIgnore]
    public int? EditSeed { get; init; }

    /// <summary>
    /// <see cref="EditSeed"/> 的序列化影子（同 <see cref="WholeBatchRecorded"/>）：0 写成缺省（不写出），读入原样；"显式 0"与"缺该项的旧记录"读回都按 0 重建。
    /// </summary>
    [JsonInclude]
    [JsonPropertyName(nameof(EditSeed))]
    private int? EditSeedRecorded
    {
        get => EditSeed == 0 ? null : EditSeed;
        init => EditSeed = value;
    }

    // retire-legacy-maps 3.2：冒险概率字段 FlagRisk 删除；配置文件写了即报错（见 RetiredKeys）。

    /// <summary>
    /// 对局内容集（<see cref="MatchOptions.ContentSet"/>，more-pieces-relics D8；v1 = 原六 + 六，v2 = 十 + 十）：<c>null</c> = 新建的局取 <see cref="ContentSets.Default"/>（v2），
    /// 并由 <see cref="ResolvedFor"/> 落成具体值写进批次 <c>config.json</c> 与日志首部；按日志首部重建（回放）时缺该项即该项出现之前的旧日志，按 v1 重建
    /// （v1 与引入之前逐步相同）。按枚举名写出（"V1" / "V2"）。
    /// </summary>
    public ContentSet? ContentSet { get; init; }

    /// <summary>新局缺省 v2；旧日志缺字段时按 v1 回放。</summary>
    [JsonConverter(typeof(ScoringVersionNameConverter))]
    public ScoringVersion? ScoringVersion { get; init; }

    /// <summary>
    /// 每名 AI 玩家的带入数量（carry-in-out D11 / simulation-harness「批量跑局」；取 0 或 1）：0 = 关闭带入带出，批次与引入之前逐局相同；
    /// 1 = 开启，每名 AI 按 <see cref="Siege.Core.Carry.CarryAi.Draw"/> 从 <c>carry-ai</c> 子流随机带入 1 件补给，带出结算只写日志、不读写任何档案。
    /// <c>null</c> = 未配置，新建的局由 <see cref="ResolvedFor"/> 落成 0 写进 <c>config.json</c> 与日志首部；按日志首部重建（回放）时缺该项即该项出现之前的旧日志，
    /// 回放不读它——带入按首部记录的各玩家带入重建（<see cref="Logging.LogHeader.CarryIns"/>）。命令行 <c>--carry-in</c>。
    /// </summary>
    public int? CarryIn { get; init; }

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

    /// <summary>第 <paramref name="index"/> 局的地图标识：每局换图时为 <c>link:&lt;起始 + index&gt;</c>（规范化），否则恒为 <see cref="MapId"/>。</summary>
    public string MapIdAt(int index) =>
        // 链接图每局换图（simulation-harness「每局换图的批次」）：第 i 局用 link:<起始 + i>。
        MapPerMatch ? LinkMapId.Format(checked(LinkMapId.Parse(MapId).Seed + (ulong)index), LinkMapId.Parse(MapId).Players) : MapId;   // map-expand-25 段 A1：标识带人数，换图时人数不变

    /// <summary>玩家编号列表。</summary>
    public PlayerId[] PlayerIds() => [.. Enumerable.Range(0, Players.Count).Select(i => new PlayerId(i))];

    /// <summary>有效并行度。</summary>
    [JsonIgnore]
    public int EffectiveParallelism => Parallelism > 0 ? Parallelism : Environment.ProcessorCount;

    private int? _recordedParallelism;

    /// <summary>保留旧日志记载的并行度；新配置只记录显式指定的并行度。</summary>
    [JsonPropertyName("EffectiveParallelism")]
    public int? RecordedParallelism
    {
        get => _recordedParallelism ?? (Parallelism > 0 ? Parallelism : null);
        init => _recordedParallelism = value;
    }

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
            if (!LinkMapId.IsLinkMap(MapId) || LinkMapId.IsBareRequest(MapId))
            {
                throw new ArgumentException($"每局换图要求地图标识带起始地图种子：链接图 link:<起始地图种子>；实际为 {MapId}。");
            }

            try
            {
                _ = MapIdAt(Count - 1);   // 格式与种子上溢在开跑之前报出
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

        if (RoundCap < 0)
        {
            throw new ArgumentException("大回合上限须为非负整数（0 = 不启用）。");
        }

        if (StagnationRounds < 0)
        {
            throw new ArgumentException("停滞大回合数须为非负整数（0 = 不启用）。");
        }

        if (WholeBatch is { } wholeBatch && wholeBatch is not (0 or 1))
        {
            throw new ArgumentException($"整批候选开关须为 0 或 1（0 = 关闭），实际为 {wholeBatch}。");
        }

        if (EditSeed is { } editSeed && editSeed is not (0 or 1))
        {
            throw new ArgumentException($"搭桥候选开关须为 0 或 1（0 = 关闭），实际为 {editSeed}。");
        }

        if (ContentSet is { } contentSet && !Enum.IsDefined(contentSet))
        {
            throw new ArgumentException($"对局内容集须为 V1 或 V2，实际为 {contentSet}。");
        }

        if (ScoringVersion is { } scoring && !Enum.IsDefined(scoring))
        {
            throw new ArgumentException($"计分规则版本须为 V1 或 V2，实际为 {scoring}。");
        }

        if (CarryIn is { } carryIn && carryIn is not (0 or 1))
        {
            throw new ArgumentException($"带入数量须为 0 或 1（每名玩家每局至多带入 1 件补给；0 = 关闭带入带出），实际为 {carryIn}。");
        }

        if (CarryIn == 1 && !Siege.Core.Carry.CarryPoints.Supports(Players.Count))
        {
            throw new ArgumentException($"{Players.Count} 人局没有名次补给点表（只有 2–4 人），不能开启带入。");
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
    /// 把"按地图自动"的候选格上限与缺省停手阈值落成具体数值，使批次 <c>config.json</c> 与日志首部如实记录实际生效的 K 与阈值。
    /// K 已显式配置、或自动值为 0（小图）时不写（标准图上这一项与引入之前相同）；阈值未配置时一律落成 <see cref="AiSearchConfig.DefaultPassThreshold"/>
    /// （缺省非 0，不落成就无法与"首部缺该项 = 旧日志 = 0"区分；retire-legacy-maps 3.2：冒险概率的落成随字段删除），
    /// 内容集未配置时一律落成 <see cref="ContentSets.Default"/>（同理：首部缺该项 = 旧日志 = v1），带入数量未配置时落成 0（关闭）。
    /// 权重不动（retire-legacy-maps 3.3：地图专属评价权重覆盖随 2 人图删除）。幂等。
    /// </summary>
    public RunConfig ResolvedFor(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        RunConfig resolved = CandidateCellLimit is null && AiSearchConfig.DefaultCellLimitFor(map.PlayableCount) is > 0 and int auto
            ? this with { CandidateCellLimit = auto }
            : this;
        resolved = resolved.PassThreshold is null ? resolved with { PassThreshold = AiSearchConfig.DefaultPassThreshold } : resolved;
        // 大回合上限与停滞大回合数（cycle-stop D2 / D3）：未配置一律落成缺省值，config.json 与首部写明实际生效值。
        resolved = resolved.RoundCap is null ? resolved with { RoundCap = MatchOptions.DefaultRoundCap } : resolved;
        resolved = resolved.StagnationRounds is null ? resolved with { StagnationRounds = AiSearchConfig.DefaultStagnationRounds } : resolved;
        // 整批候选开关（ai-whole-batch D4）：未配置落成缺省 1，首部写明；显式 0 保持 0（序列化时不写出，回放缺该项按 0）。
        resolved = resolved.WholeBatch is null ? resolved with { WholeBatch = AiSearchConfig.DefaultWholeBatch } : resolved;
        // 搭桥候选开关（ai-bridge-candidate D3）同上：未配置落成缺省 1；显式 0 保持 0（不写出，回放缺该项按 0）。
        resolved = resolved.EditSeed is null ? resolved with { EditSeed = AiSearchConfig.DefaultEditSeed } : resolved;
        // 内容集同理（more-pieces-relics D8）：缺省 v2 与"首部缺该项 = 旧日志 = v1"必须可区分，未配置一律落成 v2。
        resolved = resolved.ContentSet is null ? resolved with { ContentSet = ContentSets.Default } : resolved;
        resolved = resolved.ScoringVersion is null ? resolved with { ScoringVersion = ScoringVersions.Default } : resolved;
        // 带入数量（carry-in-out D11）：未配置落成 0，config.json 与首部写明"关闭"；首部缺该项 = 该项出现之前的旧日志（回放不读它，带入按首部的各玩家带入重建）。
        resolved = resolved.CarryIn is null ? resolved with { CarryIn = 0 } : resolved;
        // 前瞻宽度（expert-lookahead D9）：未显式配置搜索参数、而难度预设带前瞻的玩家（专家），把实际生效的搜索配置落成具体值写进 config.json 与首部，
        // 使"专家、前瞻宽度 4"如实可查、回放按首部重建。落成值与会话建 AI 时按难度取的完全相同（候选格上限与停手阈值取上面已落成的值），走法不变。
        // 必须放在候选格上限与停手阈值落成之后。前瞻宽度为 0 的三档旧难度不落成（Search 保持缺省），配置记录与首部与引入之前逐字节相同。
        return resolved.Players.Any(p => p.Search is null && AiSearchConfig.ForDifficulty(p.Difficulty).LookaheadWidth > 0)
            ? resolved with
            {
                Players = [.. resolved.Players.Select(p => p.Search is null && AiSearchConfig.ForDifficulty(p.Difficulty).LookaheadWidth > 0
                    ? p with { Search = AiSearchConfig.ForMap(p.Difficulty, map.PlayableCount, resolved.CandidateCellLimit ?? 0) with { PassThreshold = resolved.PassThreshold!.Value, StagnationRounds = resolved.StagnationRounds!.Value, WholeBatch = resolved.WholeBatch!.Value, EditSeed = resolved.EditSeed!.Value } }
                    : p)],
            }
            : resolved;
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
        ["MaxMajorRounds"] = "固定轮数终局已删除（restore-go-core-rules 裁决 #4）；大回合上限安全网请用 RoundCap（cycle-stop，到达按势力结算而非直接终局），跑局要限长请用 TurnLimit（小回合数截断）",
        ["DominanceStartRound"] = "势力碾压已删除（restore-go-core-rules 裁决 #4）",
        ["CatchUpRecruit"] = "落后者征募补偿已删除（restore-go-core-rules 裁决 #7）",
        ["FlagRisk"] = "冒险概率已移除（retire-legacy-maps 3.2；board-link-mode 起原型插旗为盲选）",
        ["PairingSeed"] = "拼接规则已移除（open-map 裁决 B1：配对、链接、等待者与决赛倒计时随之删除，没有配对种子可配）",
        ["FinalCountdownRounds"] = "拼接规则已移除（open-map 裁决 B1：决赛倒计时 K 随决赛删除）",
        ["SeatVision"] = "拼接规则已移除（open-map 裁决 B1：锁定后整张地图是唯一战场，座位视野即全图，全知开关随之删除）",
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

                // open-map 段 B 质检：玩家搜索配置里的视野开关（原 Players[].Search.SeatVision，ai-seat-vision 时期即报错）随 AiSearchConfig.SeatVision 删除后
                // 不再是已知成员，System.Text.Json 会静默忽略——与顶层 RetiredKeys 同口径点名拒绝（大小写不敏感）。
                foreach (JsonProperty top in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(top.Name, nameof(Players), StringComparison.OrdinalIgnoreCase) || top.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    int index = 0;
                    foreach (JsonElement player in top.Value.EnumerateArray())
                    {
                        bool retired = player.ValueKind == JsonValueKind.Object && player.EnumerateObject().Any(p =>
                            string.Equals(p.Name, nameof(PlayerAiConfig.Search), StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object
                            && p.Value.EnumerateObject().Any(s => string.Equals(s.Name, "SeatVision", StringComparison.OrdinalIgnoreCase)));
                        if (retired)
                        {
                            throw new FormatException($"配置项 Players[{index}].Search.SeatVision 已删除：{RetiredKeys["SeatVision"]}。请从配置文件里去掉它。");
                        }

                        index++;
                    }
                }

            }
        }

        RunConfig config = JsonSerializer.Deserialize<RunConfig>(json, JsonOptions) ?? throw new FormatException("配置 JSON 为空。");
        return (config with { RecordedParallelism = null }).Validated();
    }
}
