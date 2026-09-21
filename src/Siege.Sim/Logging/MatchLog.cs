using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Siege.Core.Scoring;
using Siege.Sim.Config;

namespace Siege.Sim.Logging;

/// <summary>
/// 一局的完整日志（design.md D5：事件流 + 每小回合快照）。文件格式为 JSON Lines：首行 <see cref="LogHeader"/>，
/// 随后每小回合一条 <see cref="TurnSnapshot"/> 与若干 <see cref="LogEvent"/>，末行 <see cref="LogResult"/> 或 <see cref="LogFailure"/>。
/// 日志是事后记录，允许含全部结果；AI 在跑局中仍只拿正式接口。
/// </summary>
/// <remarks>
/// <para>设计文档 §17 八类记录 → 字段映射：</para>
/// <list type="table">
/// <item><term>1. 地图、种子、完整信物分布及揭示时间</term><description><see cref="LogHeader.MapId"/> / <see cref="LogHeader.Seed"/> 与 <see cref="LogHeader.Relics"/>（含真实内容）；揭示大回合在 <see cref="LogResult.RelicReveals"/>（未揭示为 <c>null</c>），过程中的揭示为 <c>Reveal</c> 事件</description></item>
/// <item><term>2. 每轮征募候选、玩家选择、被 Pass 撤销的征募数</term><description><c>Recruit</c> 事件：<see cref="LogEvent.Detail"/> 为候选 / 选取 / 弃牌文本，<see cref="LogEvent.Values"/> 含 <c>Recruited</c> / <c>Revoked</c> / <c>Deployed</c>（私有量，来源玩家 = <see cref="LogEvent.Player"/>）</description></item>
/// <item><term>3. 每次批次落子、合法性结果、提子数、同形检查</term><description><c>Settled</c> 事件（落点、提子、<c>SuperkoPassed</c>）、<c>Rejected</c> 事件（失败类别 + 坐标）、<c>Rehearsal</c> 事件（预演失败，仅完整模式）；快照的 <see cref="TurnSnapshot.Placements"/> / <see cref="TurnSnapshot.Captures"/></description></item>
/// <item><term>4. 每次地形改造（大回合、小回合、改造方、动作、目标、是否致提子）</term><description><see cref="TurnSnapshot.Edits"/>（大回合 / 小回合由所在快照给出）；配置的匠人权重在 <see cref="LogHeader.ArtisanWeight"/></description></item>
/// <item><term>5. 信物控制变化、结构参数、行动顺序</term><description><c>ControlChanged</c> 事件（信物）；<see cref="TurnSnapshot.ShowCount"/> / <see cref="TurnSnapshot.FreePickCount"/> / <see cref="TurnSnapshot.TypeSlots"/> / <see cref="TurnSnapshot.DeployLimit"/>；先手修正在 <c>MajorRoundEnded</c> 事件的 <see cref="LogEvent.Values"/>（<c>P0.Bonus</c>）；<see cref="TurnSnapshot.ActionOrder"/></description></item>
/// <item><term>6. 每个棋串的基础军势、位置加值（来源拆分）、倍率、最终军势</term><description><see cref="GroupEntry"/>：<c>Base</c> / <c>LineBonus</c> / <c>SynergyBonus</c> / <c>HighGroundBonus</c> / <c>MultiplierCount</c>（原始数量）/ <c>EffectiveMultiplierCount</c>（生效指数）/ <c>Power</c> / <c>PieceCounts</c>（各棋子类型计数）</description></item>
/// <item><term>7. 势力排名变化、Pass、出局、弃赛、最终结果</term><description><c>RankChanged</c> 事件；<see cref="TurnSnapshot.Passed"/>；<c>PlayerEliminated</c> / <c>PlayerResigned</c> 事件；<see cref="LogResult"/>（达大回合上限是规则级终局原因 <c>MajorRoundLimit</c>）</description></item>
/// <item><term>8. 小回合、大回合与整局耗时</term><description><see cref="TurnSnapshot.ElapsedMs"/>；<see cref="LogResult.MajorRoundMs"/>；<see cref="LogResult.TotalMs"/>（只记录，不参与任何决定）</description></item>
/// </list>
/// </remarks>
public sealed class MatchLog
{
    public const int SchemaVersion = 1;

    public required LogHeader Header { get; init; }

    public List<TurnSnapshot> Turns { get; init; } = [];

    public List<LogEvent> Events { get; init; } = [];

    public LogResult? Result { get; set; }

    public LogFailure? Failure { get; set; }

    public ulong Seed => Convert.ToUInt64(Header.Seed, 16);

    public bool IsFailed => Failure is not null;

    /// <summary>本局是否用过调试 AI 或发生过人工接管（design.md D7：默认排除）。</summary>
    public bool IsContaminated => (Result?.UsedDebugAi ?? Failure?.UsedDebugAi ?? false)
        || (Result?.Takeovers.Count ?? Failure?.Takeovers.Count ?? 0) > 0;

    /// <summary>文件名：失败局带 <c>failed</c>；压缩时加 <c>.gz</c>。</summary>
    public string FileName => FileNameFor(compress: false);

    public string FileNameFor(bool compress) => $"match-{Header.Seed}{(IsFailed ? "-failed" : string.Empty)}.jsonl{(compress ? ".gz" : string.Empty)}";

    /// <summary>确定性文本：去掉全部耗时字段，用于串 / 并行与回放比对。</summary>
    public string DeterministicText() => Serialize(withTiming: false);

    /// <summary>完整文本（含耗时）。</summary>
    public string FullText() => Serialize(withTiming: true);

    private string Serialize(bool withTiming)
    {
        var sb = new System.Text.StringBuilder();
        Append(sb, Header);
        var events = new Queue<LogEvent>(Events);
        foreach (TurnSnapshot turn in Turns)
        {
            while (events.Count > 0 && events.Peek().Turn < turn.Turn)
            {
                Append(sb, events.Dequeue());
            }

            Append(sb, withTiming ? turn : turn with { ElapsedMs = null });
            while (events.Count > 0 && events.Peek().Turn == turn.Turn)
            {
                Append(sb, events.Dequeue());
            }
        }

        while (events.Count > 0)
        {
            Append(sb, events.Dequeue());
        }

        if (Result is { } r)
        {
            Append(sb, withTiming ? r : r with { TotalMs = null, MajorRoundMs = null });
        }

        if (Failure is { } f)
        {
            Append(sb, withTiming ? f : f with { ElapsedMs = null });
        }

        return sb.ToString();
    }

    private static void Append<T>(System.Text.StringBuilder sb, T line) =>
        sb.Append(JsonSerializer.Serialize(line, LogJson.Options)).Append('\n');

    /// <summary>写入文件（含耗时），返回路径。<paramref name="compress"/> 为 GZip（implement 7.5：事件流可选压缩）。</summary>
    public string WriteTo(string directory, bool compress = false)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileNameFor(compress));
        string text = FullText();
        if (!compress)
        {
            File.WriteAllText(path, text);
            return path;
        }

        using FileStream file = File.Create(path);
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Optimal);
        using var writer = new StreamWriter(gzip, new System.Text.UTF8Encoding(false));
        writer.Write(text);
        return path;
    }

    /// <summary>解析 JSON Lines 文本；容忍 CRLF。</summary>
    public static MatchLog Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        LogHeader? header = null;
        var turns = new List<TurnSnapshot>();
        var events = new List<LogEvent>();
        LogResult? result = null;
        LogFailure? failure = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            using JsonDocument doc = JsonDocument.Parse(line);
            string kind = doc.RootElement.GetProperty("Kind").GetString() ?? throw new FormatException("日志行缺少 Kind。");
            switch (kind)
            {
                case LogHeader.KindName:
                    header = JsonSerializer.Deserialize<LogHeader>(line, LogJson.Options);
                    break;
                case TurnSnapshot.KindName:
                    turns.Add(JsonSerializer.Deserialize<TurnSnapshot>(line, LogJson.Options)!);
                    break;
                case LogEvent.KindName:
                    events.Add(JsonSerializer.Deserialize<LogEvent>(line, LogJson.Options)!);
                    break;
                case LogResult.KindName:
                    result = JsonSerializer.Deserialize<LogResult>(line, LogJson.Options);
                    break;
                case LogFailure.KindName:
                    failure = JsonSerializer.Deserialize<LogFailure>(line, LogJson.Options);
                    break;
                default:
                    throw new FormatException($"未知日志行类型 {kind}。");
            }
        }

        return new MatchLog
        {
            Header = header ?? throw new FormatException("日志缺少 header 行。"),
            Turns = turns,
            Events = events,
            Result = result,
            Failure = failure,
        };
    }

    /// <summary>读取文件；<c>.gz</c> 后缀自动解压。</summary>
    public static MatchLog Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return Parse(File.ReadAllText(path));
        }

        using FileStream file = File.OpenRead(path);
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>读取目录下全部 <c>match-*.jsonl</c> / <c>match-*.jsonl.gz</c>，按种子排序。</summary>
    public static List<MatchLog> ReadDirectory(string directory) =>
        [.. Directory.GetFiles(directory, "match-*.jsonl").Concat(Directory.GetFiles(directory, "match-*.jsonl.gz")).Select(Read).OrderBy(l => l.Seed)];
}

internal static class LogJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(), new BigIntegerJsonConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>首行：配置 + 地图 + 种子 + 完整信物分布 + 配置的调试 AI 玩家。</summary>
public sealed record LogHeader
{
    public const string KindName = "header";

    public string Kind { get; init; } = KindName;

    public int Schema { get; init; } = MatchLog.SchemaVersion;

    public required string MapId { get; init; }

    /// <summary>
    /// 本局地图的可落子格总数，供"冲突时的盘面占用率"用（match-telemetry 第 9 条）。
    /// 必须取自对局本身而不是分析端按 <see cref="MapId"/> 重建地图——否则改图之后旧日志会被按新图的格数换算。
    /// denser-map 之前的旧日志没有该字段（<c>null</c>）：分析时整局排除出占用率口径，MUST NOT 回填。
    /// </summary>
    public int? PlayableCells { get; init; }

    /// <summary>
    /// 本局地图的出生区（平台）数（frontier-map 3.5）。取自对局本身：边疆档区数多于人数，没人选的平台不会出现在 <see cref="Zones"/> 里，
    /// 各区胜率报告要靠它固定行数（6 号台整批没人选也要有一行"被选 0 次"）。
    /// frontier-map 之前的旧日志没有该字段（<c>null</c>）：分析端回填成"被选到过的最大区号 + 1"。这里回填是安全的——
    /// 旧日志全部来自区数 = 人数的标准档图，每个区必被选到，回填值就是真值；与本文件其余"MUST NOT 回填"的字段不同，它不会造出假样本。
    /// </summary>
    public int? ZoneCount { get; init; }

    /// <summary>
    /// 本局<b>开局地图</b>的内容摘要（<see cref="Siege.Core.Board.MapFile.Digest"/>，map-generator D5）：对地图导出文本取的 SHA-256。内置图、生成图、地图文件一律写。
    /// 回放按标识重建地图后先比它，不同即在首部报"地图不一致"并停止——生成器改版之后旧日志里的 <c>gen:</c> 标识会重建出另一张图，
    /// 带着它逐步比对只会得到一个看起来像非确定性 bug 的中途分歧。map-generator 之前的旧日志没有该字段（<c>null</c>）：跳过比对，MUST NOT 回填。
    /// </summary>
    public string? MapDigest { get; init; }

    /// <summary>
    /// 各出生区（平台）的边长，下标 = 区号：取该区格子外接矩形的较长边（生成图的平台是正方形，即其边长）。
    /// <b>只在每局换图的批次里写</b>（<see cref="RunConfig.MapPerMatch"/>）：那时平台编号跨局不可比，分析报告改按边长分组（map-generator D6）；
    /// 取自对局本身而不是分析端按 <see cref="MapId"/> 重新生成——理由同 <see cref="PlayableCells"/>。其余批次为 <c>null</c>，首部不多这一项。
    /// </summary>
    public List<int>? ZoneSides { get; init; }

    /// <summary>种子，十六进制（<c>GameSeed.ToString</c>）。</summary>
    public required string Seed { get; init; }

    /// <summary>本局对局配置的大回合上限（0 = 不限；match-setup「上限进入对局记录」）。取自对局本身，不是 <see cref="Config"/>。</summary>
    public int MaxMajorRounds { get; init; }

    /// <summary>本局对局配置的碾压起始大回合（0 = 关闭；match-setup「对局配置公开碾压起始大回合」）。取自对局本身；dominance-victory 之前的旧日志为 <c>null</c>。</summary>
    public int? DominanceStartRound { get; init; }

    /// <summary>本局对局配置的落后者征募补偿开关（match-setup「对局配置公开落后补偿开关」）。取自对局本身；catch-up-recruit 之前的旧日志为 <c>null</c>。</summary>
    public bool? CatchUpRecruit { get; init; }

    /// <summary>
    /// 本局对局配置的匠人征募权重（artisan-terrain-edit R-2 / match-telemetry 第 1 条）。取自对局本身，不是 <see cref="Config"/>——
    /// 扫档三档的口径由它标注。artisan-terrain-edit 之前的旧日志为 <c>null</c>，MUST NOT 回填成 10。
    /// </summary>
    public int? ArtisanWeight { get; init; }

    public required RunConfig Config { get; init; }

    public List<int> Players { get; init; } = [];

    /// <summary>各玩家锁定的出生区（下标 = 玩家编号）。</summary>
    public List<int> Zones { get; init; } = [];

    /// <summary>首回合行动顺序。</summary>
    public List<int> FirstOrder { get; init; } = [];

    public List<RelicEntry> Relics { get; init; } = [];

    public bool RelicsConverged { get; init; }

    public int RelicRerolls { get; init; }

    public List<int> DebugAiPlayers { get; init; } = [];

    /// <summary>本局实际采用的事件保留策略（抽样 / 失败提升后的结果）。</summary>
    public EventRetention Retention { get; init; }
}

/// <summary>
/// 本小回合完成的一次地形改造（match-telemetry 第 4 条）：动作、目标、改造方与是否直接导致提子。
/// 大回合与小回合由所在的 <see cref="TurnSnapshot"/> 给出。
/// </summary>
/// <remarks>
/// <para><see cref="Target"/> 是 <c>TerrainEdit</c> 的规范记法（<c>B:F7</c> / <c>F:F6-G6</c> / <c>X:H3</c>），
/// 按日志顺序重放全部 <see cref="Target"/> 即可离线重建终局地形（「地形可离线重建」）。</para>
/// <para><see cref="Player"/> 只在日志里有，公开视图与盘面不记改造者（R-3）。</para>
/// </remarks>
public sealed record TerrainEditEntry
{
    /// <summary>动作名（<c>TerrainEditKind</c>：Bridge / Fence / Burn）。</summary>
    public required string Action { get; init; }

    /// <summary>目标的规范记法。</summary>
    public required string Target { get; init; }

    /// <summary>改造方。</summary>
    public int Player { get; init; }

    /// <summary>携带该改造的匠人落点。</summary>
    public required string Artisan { get; init; }

    /// <summary>该次改造是否直接导致提子。</summary>
    public bool CausedCapture { get; init; }
}

/// <summary>一枚信物的完整内容（事后记录）。</summary>
public sealed record RelicEntry
{
    public required string Coord { get; init; }

    public required string Zone { get; init; }

    public required string Budget { get; init; }

    public required string Type { get; init; }

    public int Magnitude { get; init; }

    public string? EmblemPiece { get; init; }
}

/// <summary>每小回合结束时的快照：各玩家势力明细、结构参数、控制信物、手牌类型（无数量）。</summary>
public sealed record TurnSnapshot
{
    public const string KindName = "turn";

    public string Kind { get; init; } = KindName;

    /// <summary>小回合序号，从 1 起。</summary>
    public int Turn { get; init; }

    public int MajorRound { get; init; }

    public int Player { get; init; }

    /// <summary>本大回合中的行动位置（0 基）。</summary>
    public int Position { get; init; }

    public bool Passed { get; init; }

    public List<string> Placements { get; init; } = [];

    public List<string> Captures { get; init; } = [];

    /// <summary>确认被拒绝的次数。</summary>
    public int Rejections { get; init; }

    public int ShowCount { get; init; }

    public int FreePickCount { get; init; }

    /// <summary>
    /// 本小回合落后者征募补偿给<b>展示数</b>的点数（0 或 1）。取自该玩家真实征募面板上的快照留痕，不由分析端按名次重算。
    /// catch-up-recruit 之前的旧日志没有该字段（<c>null</c>）：分析时整局跳过落后补偿口径，MUST NOT 回填成 0。
    /// </summary>
    public int? CatchUpReveal { get; init; }

    /// <summary>本小回合落后者征募补偿给<b>免费选取数</b>的点数（0 或 1）；旧日志为 <c>null</c>，同 <see cref="CatchUpReveal"/>。</summary>
    public int? CatchUpPick { get; init; }

    public int TypeSlots { get; init; }

    public int DeployLimit { get; init; }

    public List<int> ActionOrder { get; init; } = [];

    public int PassStreak { get; init; }

    public List<PlayerEntry> PlayersState { get; init; } = [];

    public List<RelicStateEntry> Relics { get; init; } = [];

    /// <summary>
    /// 本小回合完成的地形改造（坐标序由结算顺序给出）。<b>没有改造时写空表 <c>[]</c>，不是 <c>null</c></b>——
    /// artisan-terrain-edit 之前的旧日志才是 <c>null</c>，分析时整局排除并计数（R-6），MUST NOT 回填成空表。
    /// </summary>
    public List<TerrainEditEntry>? Edits { get; init; }

    /// <summary>小回合墙钟耗时（毫秒）；只记录，不参与任何决定。</summary>
    public long? ElapsedMs { get; init; }
}

public sealed record PlayerEntry
{
    public int Player { get; init; }

    public required string Status { get; init; }

    public bool Protection { get; init; }

    /// <summary>总势力，精确整数（restore-go-core-rules D1：任意精度，写出为不失真的十进制整数）。</summary>
    public BigInteger Total { get; init; }

    /// <summary>竞争名次；不参赛为 <c>null</c>。</summary>
    public int? Rank { get; init; }

    public List<string> HandTypes { get; init; } = [];

    public List<GroupEntry> Groups { get; init; } = [];
}

public sealed record GroupEntry
{
    public List<string> Stones { get; init; } = [];

    public int Base { get; init; }

    public int LineBonus { get; init; }

    public int SynergyBonus { get; init; }

    /// <summary>高地压制加值。旧日志为 <c>null</c>：高地加值占比整局排除，MUST NOT 回填成 0。</summary>
    public int? HighGroundBonus { get; init; }

    /// <summary>倍增子数量，即倍率指数（restore-go-core-rules：不封顶，"生效倍率指数"字段已删；旧日志里的该字段读入时忽略）。</summary>
    public int MultiplierCount { get; init; }

    /// <summary>取整后军势，精确整数。</summary>
    public BigInteger Power { get; init; }

    /// <summary>
    /// 本串各棋子类型的数量，键为 <see cref="Siege.Core.Board.PieceType"/> 名，六种全写（含 0）；multiplier-rebalance 裁决 3，供"各棋子势力占比"归因。
    /// 此前的旧日志没有该字段，解析为 <c>null</c>（未知）：分析时跳过该局的占比统计，MUST NOT 回填成 0——那会把旧局错算成"全是某种棋子"。
    /// </summary>
    public Dictionary<string, int>? PieceCounts { get; init; }
}

public sealed record RelicStateEntry
{
    public required string Coord { get; init; }

    public bool Revealed { get; init; }

    public required string Control { get; init; }

    public int? Holder { get; init; }
}

/// <summary>一条事件。<see cref="Type"/> 见 <see cref="LogEventType"/>。</summary>
public sealed record LogEvent
{
    public const string KindName = "event";

    public string Kind { get; init; } = KindName;

    public int Seq { get; init; }

    /// <summary>所属小回合（事件发生在该小回合内；小回合开始前的事件记 0）。</summary>
    public int Turn { get; init; }

    public int MajorRound { get; init; }

    public required string Type { get; init; }

    public int? Player { get; init; }

    public string Detail { get; init; } = string.Empty;

    public List<string>? Coords { get; init; }

    public Dictionary<string, BigInteger>? Values { get; init; }

    /// <summary>失败类别（<c>Rejected</c> / <c>Rehearsal</c>）。</summary>
    public string? FailureKind { get; init; }
}

/// <summary>事件类型常量。</summary>
public static class LogEventType
{
    public const string Recruit = "Recruit";
    public const string Rehearsal = "Rehearsal";
    public const string Rejected = "Rejected";
    public const string Settled = "Settled";
    public const string Candidates = "Candidates";
    public const string Reveal = "Reveal";
    public const string ControlChanged = "ControlChanged";
    public const string RankChanged = "RankChanged";
    public const string MajorRoundEnded = "MajorRoundEnded";
    public const string ProtectionLifted = "ProtectionLifted";
    public const string PlayerEliminated = "PlayerEliminated";
    public const string PlayerResigned = "PlayerResigned";
    public const string MatchEnded = "MatchEnded";
    public const string Takeover = "Takeover";
    public const string FlagsLocked = "FlagsLocked";

    /// <summary>只在完整模式保留的细粒度事件。</summary>
    public static bool IsFineGrained(string type) => type is Rehearsal or Candidates;
}

/// <summary>末行：终局原因、名次、标注、揭示表、峰值遥测、耗时。</summary>
public sealed record LogResult
{
    public const string KindName = "result";

    public string Kind { get; init; } = KindName;

    /// <summary><c>EndReason</c> 名（round-cap 后达上限也是规则级原因 <c>MajorRoundLimit</c>）。</summary>
    public required string Reason { get; init; }

    /// <summary>是否以非达上限原因终局（含势力碾压；round-cap D5：不收敛 = 以「达大回合上限」终局）。由 <see cref="Reason"/> 派生，读旧日志时忽略文件里的同名字段。</summary>
    public bool Converged => Reason != nameof(Siege.Core.Match.EndReason.MajorRoundLimit);

    public int MajorRound { get; init; }

    public int TurnCount { get; init; }

    public List<StandingEntry> Standings { get; init; } = [];

    public List<int> Winners { get; init; } = [];

    public bool UsedDebugAi { get; init; }

    public List<int> DebugAiPlayers { get; init; } = [];

    public List<TakeoverEntry> Takeovers { get; init; } = [];

    /// <summary>每枚信物的揭示大回合；整局未揭示为 <c>null</c>。</summary>
    public List<RelicRevealEntry> RelicReveals { get; init; } = [];

    public PeakEntry? Peak { get; init; }

    /// <summary>峰值串是否在之后被摧毁（日志层比对相邻快照，裁决 6）；无峰值为 <c>null</c>。</summary>
    public bool? PeakDestroyed { get; init; }

    public int? DeployLimitPeak { get; init; }

    public long? TotalMs { get; init; }

    public List<long>? MajorRoundMs { get; init; }
}

public sealed record StandingEntry
{
    public int Rank { get; init; }

    public int Player { get; init; }

    public required string Group { get; init; }

    public required string Status { get; init; }

    public BigInteger Power { get; init; }

    public int ControlledRelics { get; init; }

    /// <summary>独占空格数（并列链第 3 级，restore-go-core-rules D4）。旧日志为 <c>null</c>。</summary>
    public int? ExclusiveCells { get; init; }

    public int Stones { get; init; }

    public int? EliminationOrder { get; init; }
}

public sealed record TakeoverEntry
{
    public int Sequence { get; init; }

    public int Player { get; init; }

    public int MajorRound { get; init; }

    public required string Stage { get; init; }

    public required string Kind { get; init; }
}

public sealed record RelicRevealEntry
{
    public required string Coord { get; init; }

    public int? RevealedInMajorRound { get; init; }
}

public sealed record PeakEntry
{
    /// <summary>峰值串的倍增子数量（峰值按它取），即倍率指数。</summary>
    public int MultiplierCount { get; init; }

    public int MajorRound { get; init; }

    public int Player { get; init; }

    public BigInteger Power { get; init; }

    public List<string> Stones { get; init; } = [];
}

/// <summary>失败局末行：终止位置、异常与标注；种子、配置与终止前事件流都已在前面各行。</summary>
public sealed record LogFailure
{
    public const string KindName = "failed";

    public string Kind { get; init; } = KindName;

    public int Turn { get; init; }

    public int MajorRound { get; init; }

    public required string ExceptionType { get; init; }

    public required string Message { get; init; }

    public string? StackTrace { get; init; }

    public bool UsedDebugAi { get; init; }

    public List<TakeoverEntry> Takeovers { get; init; } = [];

    public long? ElapsedMs { get; init; }
}
