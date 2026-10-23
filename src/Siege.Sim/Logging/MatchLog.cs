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
/// <para>match-telemetry「对局日志的记录内容」八类记录 → 字段映射：</para>
/// <list type="table">
/// <item><term>1. 地图、种子、对局内容集、完整信物分布及揭示时间</term><description><see cref="LogHeader.MapId"/> / <see cref="LogHeader.Seed"/>、内容集在首部配置 <c>Config.ContentSet</c>（缺项的旧日志按 v1，见 <see cref="ContentSet"/>）与 <see cref="LogHeader.Relics"/>（含真实内容）；揭示大回合在 <see cref="LogResult.RelicReveals"/>（未揭示为 <c>null</c>），过程中的揭示为 <c>Reveal</c> 事件</description></item>
/// <item><term>2. 每轮征募候选、玩家选择、被 Pass 撤销的征募数</term><description><c>Recruit</c> 事件：<see cref="LogEvent.Detail"/> 为候选 / 选取 / 弃牌文本，<see cref="LogEvent.Values"/> 含 <c>Recruited</c> / <c>Revoked</c> / <c>Deployed</c>（私有量，来源玩家 = <see cref="LogEvent.Player"/>）</description></item>
/// <item><term>3. 每次批次落子、合法性结果、提子数、同形检查</term><description><c>Settled</c> 事件（落点、提子、<c>SuperkoPassed</c>）、<c>Rejected</c> 事件（失败类别 + 坐标）、<c>Rehearsal</c> 事件（预演失败，仅完整模式）；快照的 <see cref="TurnSnapshot.Placements"/> / <see cref="TurnSnapshot.Captures"/></description></item>
/// <item><term>4. 每次地形改造（大回合、小回合、改造方、动作、目标、是否致提子）</term><description><see cref="TurnSnapshot.Edits"/>（大回合 / 小回合由所在快照给出；是否经工坊扩展 <see cref="TerrainEditEntry.ViaWorkshop"/>）；配置的匠人权重在 <see cref="LogHeader.ArtisanWeight"/></description></item>
/// <item><term>5. 信物控制变化、结构参数、行动顺序</term><description><c>ControlChanged</c> 事件（信物）；<see cref="TurnSnapshot.ShowCount"/>（驿站来源 <see cref="TurnSnapshot.RelaySources"/>、工坊 <see cref="TurnSnapshot.WorkshopActive"/>）/ <see cref="TurnSnapshot.FreePickCount"/> / <see cref="TurnSnapshot.TypeSlots"/> / <see cref="TurnSnapshot.DeployLimit"/>；先手修正在 <c>MajorRoundEnded</c> 事件的 <see cref="LogEvent.Values"/>（<c>P0.Bonus</c>）；<see cref="TurnSnapshot.ActionOrder"/></description></item>
/// <item><term>6. 每个棋串的基础军势、位置加值（来源拆分）、倍增子数量、倍率与最终军势；每名玩家的领地分</term><description><see cref="GroupEntry"/>：<c>Base</c> / <c>LineBonus</c> / <c>SynergyBonus</c> / <c>HighGroundBonus</c> / <c>BannerBonus</c> / <c>ChainBonus</c> / <c>SentryBonus</c> / <c>BoundaryBonus</c>（七项来源；另有连营 / 犄角子拆分）/ <c>MultiplierCount</c>（即倍率指数，倍率 = 1.5^n 由它得出）/ <c>Power</c> / <c>PieceCounts</c>（各棋子类型计数）；
/// <see cref="PlayerEntry.TerritoryScore"/>（独占空格数）与 <see cref="PlayerEntry.Total"/>（= 领地分 + Σ军势）。势力与军势一律精确十进制整数（JSON 数字，不加引号、无指数）</description></item>
/// <item><term>7. 势力排名变化、Pass、出局、弃赛、最终结果与结束原因</term><description><c>RankChanged</c> 事件；<see cref="TurnSnapshot.Passed"/>；<c>PlayerEliminated</c> / <c>PlayerResigned</c> 事件；<see cref="PlayerEntry.HasEstablishedPower"/>（出局判据的"曾建立正势力"标记）；
/// <see cref="LogResult"/>：<see cref="LogResult.Reason"/> 为规则终局原因（链接局为 LastPlayerStanding / LastBattlefieldSettled；旧日志另有 BoardFull / AllPassed，见 <see cref="LogResult.LegacyBoardFull"/> / <see cref="LogResult.LegacyAllPassed"/>），或跑局层截断 <see cref="LogResult.TurnLimitReason"/>（此时无名次与胜者）</description></item>
/// <item><term>8. 小回合、大回合与整局耗时</term><description><see cref="TurnSnapshot.ElapsedMs"/>；<see cref="LogResult.MajorRoundMs"/>；<see cref="LogResult.TotalMs"/>（只记录，不参与任何决定）</description></item>
/// </list>
/// <para>open-map 段 B：地图整局不变，日志各行坐标全部按开局布局解释；配对事件、链接事件、决赛倒计时记录与等待大回合数已随拼接规则删除——
/// 含这些字段的改动前日志在 <see cref="Parse"/> 时明确拒绝并提示属于已移除的拼接规则（match-telemetry「旧拼接日志被拒绝」）。驿站来源与前瞻记录是小回合开始时的决策量。</para>
/// <para>life-shape「活形记录与统计」：快照的 <see cref="TurnSnapshot.Life"/>（本小回合的活形确立 / 失去、两类拒绝计数、结算后逐玩家活形状态）；
/// 暂放或确认环节因活棋禁入 / 破坏活形被拒的尝试另记 <c>LifeRefused</c> 事件（非细粒度，任何保留策略下都在）。</para>
/// <para>
/// 已删除、<b>不属于</b>日志契约的旧字段（restore-go-core-rules 段 E 裁决）：据点（首部 <c>SiteValues</c> / <c>Sites</c>、快照 <c>Sites</c>、<c>PlayerEntry.SiteScore</c>、<c>SiteControlChanged</c> 事件）、
/// 首部 <c>MaxMajorRounds</c> / <c>DominanceStartRound</c> / <c>CatchUpRecruit</c>、<c>PlayerEntry.Protection</c>、快照 <c>CatchUpReveal</c> / <c>CatchUpPick</c>、
/// <c>EffectiveMultiplierCount</c>。旧日志里出现它们时读入忽略（System.Text.Json 缺省行为），其余字段照常可读；分析端缺新字段（如领地分）的旧日志按各段约定整局排除并计数。
/// </para>
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

    /// <summary>
    /// 本局的对局内容集（match-telemetry 第 1 条；记在首部配置 <see cref="RunConfig.ContentSet"/>）。首部缺该项的旧日志按 v1 读（more-pieces-relics D8）。
    /// </summary>
    public Siege.Core.Board.ContentSet ContentSet => Header.Config.ContentSet ?? Siege.Core.Board.ContentSets.Legacy;

    /// <summary>旧日志缺版本时沿用 v1 计分。</summary>
    public ScoringVersion ScoringVersion => Header.Config.ScoringVersion ?? ScoringVersions.Legacy;

    /// <summary>本局是否开启带入带出（首部 <see cref="LogHeader.CarryInOut"/>）。引入带入带出之前的旧日志缺该项，按关闭读。</summary>
    public bool CarryInOut => Header.CarryInOut ?? false;

    /// <summary>
    /// 本局的大回合上限安全网（match-telemetry 第 1 条；记在首部配置 <see cref="RunConfig.RoundCap"/>，0 = 不启用）。
    /// 首部缺该项的旧日志按 100 读、不拒绝（cycle-stop D2 / D4）。
    /// </summary>
    public int RoundCap => Header.Config.RoundCap ?? Siege.Core.Match.MatchOptions.DefaultRoundCap;

    /// <summary>
    /// 各玩家的带入（首部 <see cref="LogHeader.CarryIns"/>，解析成对局配置的形状）。旧日志缺该项，按全员无带入读；
    /// 补给名或类型名解析不了即 <see cref="FormatException"/>（不静默当成无带入）。
    /// </summary>
    public System.Collections.Immutable.ImmutableSortedDictionary<Siege.Core.Board.PlayerId, Siege.Core.Carry.CarryIn> CarryIns
    {
        get
        {
            var carries = System.Collections.Immutable.ImmutableSortedDictionary.CreateBuilder<Siege.Core.Board.PlayerId, Siege.Core.Carry.CarryIn>();
            foreach (CarryInEntry entry in Header.CarryIns ?? [])
            {
                var carry = new Siege.Core.Carry.CarryIn(
                    ParseName<Siege.Core.Carry.SupplyKind>(entry.Supply, "补给"),
                    entry.Type is null ? null : ParseName<Siege.Core.Board.PieceType>(entry.Type, "棋子类型"));
                if (!carries.TryAdd(new Siege.Core.Board.PlayerId(entry.Player), carry))
                {
                    throw new FormatException($"日志首部的带入重复记录了玩家 {entry.Player}。");
                }
            }

            return carries.ToImmutable();
        }
    }

    /// <summary>按枚举名解析（区分大小写、不接受数字）；解析不了即 <see cref="FormatException"/>。</summary>
    private static T ParseName<T>(string text, string what)
        where T : struct, Enum =>
        Enum.GetNames<T>().Contains(text, StringComparer.Ordinal) ? Enum.Parse<T>(text) : throw new FormatException($"日志首部的带入里有未知{what} {text}。");

    /// <summary>是否为链接模式的局（首部 <see cref="LogHeader.Mode"/>）。</summary>
    public bool IsLinkMode => Header.Mode == "Link";

    /// <summary>带前瞻记录的小回合（专家的部署决策，expert-lookahead）。旧日志与只含非专家的日志为空。</summary>
    public IEnumerable<TurnSnapshot> LookaheadTurns => Turns.Where(t => t.Lookahead is not null);

    /// <summary>
    /// 某玩家实际生效的前瞻宽度：取首部配置里该玩家的搜索配置（<c>Players[].Search.LookaheadWidth</c>）。缺搜索配置或缺该字段
    /// （引入专家难度之前的旧日志、非专家玩家）按 0 读。
    /// </summary>
    public int LookaheadWidthOf(int player) => Header.Config.Players[player].Search?.LookaheadWidth ?? 0;

    /// <summary>某玩家实际生效的多样补充上限（expert-strength D7）：口径同 <see cref="LookaheadWidthOf"/>，缺搜索配置或缺该字段按 0 读。</summary>
    public int DiverseSupplementLimitOf(int player) => Header.Config.Players[player].Search?.DiverseSupplementLimit ?? 0;

    /// <summary>某玩家实际生效的两层权重（千分数，expert-strength D7）：口径同 <see cref="LookaheadWidthOf"/>，缺搜索配置或缺该字段按 0 读。</summary>
    public int TwoPlyWeightPermilleOf(int player) => Header.Config.Players[player].Search?.TwoPlyWeightPermille ?? 0;

    /// <summary>某玩家实际生效的两层平局带宽（expert-tiebreak D4）：口径同 <see cref="LookaheadWidthOf"/>，缺搜索配置或缺该字段按 0 读。</summary>
    public int TwoPlyTieBandOf(int player) => Header.Config.Players[player].Search?.TwoPlyTieBand ?? 0;

    /// <summary>
    /// 某玩家实际生效的整批候选开关（ai-whole-batch D4）：显式搜索配置的值优先（缺该字段按 0），否则取首部跑局配置的开关（缺该项 = 旧日志或显式 0，按 0）。
    /// 与会话建 AI 的派生同一口径（<c>MatchSession</c>：只作用于未显式配置搜索配置的玩家）。
    /// </summary>
    public int WholeBatchOf(int player) => Header.Config.Players[player].Search is { } search ? search.WholeBatch : Header.Config.WholeBatch ?? 0;

    /// <summary>
    /// 某玩家实际生效的搭桥候选开关（ai-bridge-candidate D3）：口径同 <see cref="WholeBatchOf"/>——显式搜索配置的值优先（缺该字段按 0），否则取首部跑局配置的开关（缺该项按 0）。
    /// </summary>
    public int EditSeedOf(int player) => Header.Config.Players[player].Search is { } search ? search.EditSeed : Header.Config.EditSeed ?? 0;

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

    /// <summary>含已删除字段的旧日志的拒绝文案前缀（simulation-harness「旧拼接日志被拒」/ match-telemetry「旧拼接日志被拒绝」）。</summary>
    public const string RetiredLinkMessage = "拼接规则已移除：该日志属于改动前的链接模式";

    /// <summary>
    /// 已随拼接规则删除的日志字段（open-map 裁决 B2），按固定位置查、不做全树递归（免得日后别处新增同名字段被误伤）：
    /// 首部 <c>Config</c> 顶层的配对种子 / K / 视野；事件行顶层的配对 / 链接 / 倒计时载荷、<c>Type</c> 取已删除的事件类型、结算里的期限；快照 <c>PlayersState[*]</c> 的等待大回合数。
    /// 任一命中即拒绝解析并点名，MUST NOT 静默忽略——这些字段只可能来自改动前的链接模式日志。
    /// </summary>
    internal static readonly HashSet<string> RetiredConfigFields = new(StringComparer.Ordinal) { "PairingSeed", "FinalCountdownRounds", "SeatVision" };

    /// <summary>事件行顶层的已删除载荷键。</summary>
    internal static readonly HashSet<string> RetiredEventFields = new(StringComparer.Ordinal) { "Pairing", "Link", "Countdown" };

    /// <summary>已随拼接规则删除的事件类型名。</summary>
    internal static readonly HashSet<string> RetiredLinkEventTypes = new(StringComparer.Ordinal) { "PairingApplied", "BattlefieldsLinked", "FinalCountdown" };

    /// <summary>全部已删除字段名（测试与文档引用）。</summary>
    internal static readonly HashSet<string> RetiredLinkFields = new(StringComparer.Ordinal)
    {
        "PairingSeed", "FinalCountdownRounds", "SeatVision", "Pairing", "Link", "Countdown", "WaitingRounds", "Deadline",
    };

    private static string? FindRetired(JsonElement root, string kind)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        switch (kind)
        {
            case LogHeader.KindName:
                if (root.TryGetProperty(nameof(LogHeader.Config), out JsonElement config) && config.ValueKind == JsonValueKind.Object)
                {
                    string? key = config.EnumerateObject().Select(p => p.Name).FirstOrDefault(RetiredConfigFields.Contains);
                    return key is null ? null : $"header.Config.{key}";
                }

                return null;
            case LogEvent.KindName:
                if (root.TryGetProperty("Type", out JsonElement type) && type.ValueKind == JsonValueKind.String && RetiredLinkEventTypes.Contains(type.GetString()!))
                {
                    return $"event.Type={type.GetString()}";
                }

                string? payload = root.EnumerateObject().Select(p => p.Name).FirstOrDefault(RetiredEventFields.Contains);
                if (payload is not null)
                {
                    return $"event.{payload}";
                }

                return root.TryGetProperty(nameof(LogEvent.Settlement), out JsonElement settlement) && settlement.ValueKind == JsonValueKind.Object && settlement.TryGetProperty("Deadline", out _)
                    ? "event.Settlement.Deadline"
                    : null;
            case TurnSnapshot.KindName:
                if (root.TryGetProperty(nameof(TurnSnapshot.PlayersState), out JsonElement players) && players.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (JsonElement player in players.EnumerateArray())
                    {
                        if (player.ValueKind == JsonValueKind.Object && player.TryGetProperty("WaitingRounds", out _))
                        {
                            return $"turn.PlayersState[{index}].WaitingRounds";
                        }

                        index++;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    /// <summary>解析 JSON Lines 文本；容忍 CRLF。含配对 / 链接事件或决赛倒计时字段的改动前日志在首部或该行即拒绝（<see cref="RetiredLinkMessage"/>）。</summary>
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
            if (FindRetired(doc.RootElement, kind) is { } retired)
            {
                throw new FormatException($"{RetiredLinkMessage}（{retired}：配对 / 链接 / 决赛倒计时 / 等待者已随 open-map 删除），不再支持解析与回放。");
            }

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

    // 大回合上限 / 碾压起始大回合 / 落后补偿开关三项已随 restore-go-core-rules（match-setup 三条 REMOVED）从首部删除；
    // 旧日志里的同名字段读入时被忽略。

    /// <summary>
    /// 本局对局配置的匠人征募权重（artisan-terrain-edit R-2 / match-telemetry 第 1 条）。取自对局本身，不是 <see cref="Config"/>——
    /// 扫档三档的口径由它标注。artisan-terrain-edit 之前的旧日志为 <c>null</c>，MUST NOT 回填成 10。
    /// </summary>
    public int? ArtisanWeight { get; init; }

    /// <summary>
    /// 本局 AI 的评价版本（<see cref="Siege.Core.Ai.EvaluationBreakdown.Version"/>，ai-eye 1.1）：维度数与各维算法随版本变，
    /// 旧版本日志的 AI 决策不能用新评价重算（design Risks「回放兼容」）。ai-eye 之前的旧日志为 <c>null</c>（即版本 1、七维），MUST NOT 回填。
    /// 回放按它处理（ai-eye R10）：与当前版本不同即在首部报分歧、不重跑（<see cref="Siege.Sim.Running.ReplayResult.AiVersionMismatch"/>）。
    /// </summary>
    public int? AiEvaluationVersion { get; init; }

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

    /// <summary>
    /// 带入带出开关（carry-in-out / match-telemetry 第 1 条）。取自对局本身：新日志总是写出（关闭时写 <c>false</c>）；
    /// 引入带入带出之前的旧日志为 <c>null</c>，按关闭读（<see cref="MatchLog.CarryInOut"/>），回放按旧日志重建、首部仍不写它。
    /// </summary>
    public bool? CarryInOut { get; init; }

    /// <summary>
    /// 各玩家的带入（按玩家编号升序；无带入的玩家不列）：补给种类、换型令指定类型 / 征召签抽得类型。新日志总是写出（无人带入时为空表），
    /// 旧日志为 <c>null</c>，按全员无带入读（<see cref="MatchLog.CarryIns"/>）。回放按它重建开局手牌（simulation-harness「可复现回放」）。
    /// </summary>
    public List<CarryInEntry>? CarryIns { get; init; }

    /// <summary>
    /// 对局模式（board-link-mode 实现裁决 B1 / C3）：恒写常量 <c>"Link"</c>（retire-legacy-rules R10）；只有引入之前的旧日志为 <c>null</c>——分析器经 <see cref="MatchLog.IsLinkMode"/> 跳过它们，回放拒绝它们。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; init; }

    /// <summary>
    /// 锁定时各出生棋盘上的旗数（match-telemetry 第 2 项；下标 = 出生棋盘编号 0 起，没人选的棋盘为 0）。只在链接模式的局写出；
    /// 各玩家锁定的出生棋盘见 <see cref="Zones"/>。离线统计同区率与各出生棋盘被选次数的数据源。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? LockedFlagCounts { get; init; }

    /// <summary>
    /// 开局的出生棋盘（board-link-mode F.3；下标 = 出生棋盘编号）：只在链接模式的局写出。离线即可按它把坐标归到棋盘与主题，不必按地图标识重建地图（open-map 起地图整局不变）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BoardEntry>? Boards { get; init; }
}

/// <summary>日志首部的一名玩家的带入：补给种类名（<see cref="Siege.Core.Carry.SupplyKind"/>）与换入类型名（备用子为 <c>null</c>）。</summary>
public sealed record CarryInEntry
{
    public int Player { get; init; }

    public required string Supply { get; init; }

    public string? Type { get; init; }
}

/// <summary>
/// 结果行的一名玩家的带出结算（match-telemetry 第 7 条）：结局类别（<see cref="Siege.Core.Carry.CarryOutcome"/> 名）、所用名次（出局 / 未结算为 <c>null</c>）、
/// 带出补给点、带入的补给（无带入为 <c>null</c>）与它是否返还。截断局全员为 <c>Unsettled</c>。
/// </summary>
public sealed record CarryOutEntry
{
    public int Player { get; init; }

    public required string Outcome { get; init; }

    public int? Rank { get; init; }

    public int Points { get; init; }

    public string? Supply { get; init; }

    public string? Type { get; init; }

    public bool Returned { get; init; }
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

    /// <summary>
    /// 目标是否经工坊扩展（隔一格，more-pieces-relics D12），取自 Core 留痕 <c>TerrainEditRecord.ViaWorkshop</c>。
    /// 只在内容集 v2 的对局写出（含 <c>false</c>）；v1 局与旧日志为 <c>null</c>，按"否"读。
    /// 本类新字段一律 <c>WhenWritingNull</c>（属性级，任何序列化选项下都生效）：v1 局日志与快照比对文本逐字节不变。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ViaWorkshop { get; init; }
}

/// <summary>
/// 一个小回合的活形记录：本次结算的活形确立 / 失去、两类拒绝尝试的计数、结算后的逐玩家活形状态。
/// 全部取自公开视图里那一份全量活形分析（<c>MatchPublicView.LifeShape</c>，唯一实现），日志层不调 <c>Analyze</c>、不自己判活形。
/// </summary>
public sealed record LifeTurnEntry
{
    /// <summary>本次结算后新确立 / 失去的已确定活形，确立在前、失去在后，各按代表坐标排序。</summary>
    public List<LifeChangeEntry> Changes { get; init; } = [];

    /// <summary>暂放环节因活棋禁入被拒的次数（预演第 1 步；AI 的候选来自已扣除禁入格的合法范围，正常为 0）。</summary>
    public int ForbiddenStaged { get; init; }

    /// <summary>控制者主动预演时因活棋禁入被判非法的次数。</summary>
    public int ForbiddenRehearsed { get; init; }

    /// <summary>确认时因活棋禁入被拒的次数。</summary>
    public int ForbiddenRejected { get; init; }

    /// <summary>控制者主动预演时因破坏活形被判非法的次数（AI 的候选预演，细粒度 <c>Rehearsal</c> 事件只在完整模式保留，这里在任何模式下都计）。</summary>
    public int BreaksRehearsed { get; init; }

    /// <summary>确认时因破坏活形被拒的次数。</summary>
    public int BreaksRejected { get; init; }

    /// <summary>结算后逐玩家的活形状态（按玩家编号）。</summary>
    public List<LifePlayerEntry> Players { get; init; } = [];

    /// <summary>结算后受保护的眼空间格数（全部已确定活形的封闭眼空间之并；至少对一名玩家禁入）。</summary>
    public int ProtectedCells { get; init; }

    /// <summary>结算后全图可落子格数（按当时地形：本局架的桥计入），是禁入格占比的分母。</summary>
    public int PlayableCells { get; init; }
}

/// <summary>一次活形确立或失去（match-telemetry「活形记录与统计」第 1、2 条）。大回合 / 小回合由所在快照给出。</summary>
public sealed record LifeChangeEntry
{
    public const string Established = "Established";
    public const string Lost = "Lost";

    /// <summary>失去原因：所有者的落点落进了原眼空间（自拆）。</summary>
    public const string OwnerFill = "OwnerFill";

    /// <summary>失去原因：所有者本批带了改造，且落点不在原眼空间里。</summary>
    public const string OwnerEdit = "OwnerEdit";

    /// <summary>失去原因：所有者本批既没有填眼也没有改造（兜底，正常不应出现）。</summary>
    public const string OwnerOther = "OwnerOther";

    /// <summary>失去原因：行动者不是所有者——规则缺陷（D3 + D4 应使其不可能），分析单列。</summary>
    public const string NonOwner = "NonOwner";

    /// <summary><see cref="Established"/> / <see cref="Lost"/>。</summary>
    public required string Kind { get; init; }

    public int Owner { get; init; }

    /// <summary>本小回合的行动者。</summary>
    public int Actor { get; init; }

    /// <summary>代表坐标：棋串坐标序的首格。</summary>
    public required string At { get; init; }

    /// <summary>棋串坐标，坐标序。确立取结算后，失去取结算前。</summary>
    public List<string> Stones { get; init; } = [];

    /// <summary>该棋串的封闭眼空间与眼值，形如 <c>A1,B1=1</c>；确立取结算后，失去取结算前。</summary>
    public List<string> EyeSpaces { get; init; } = [];

    /// <summary>失去原因；确立为 <c>null</c>。</summary>
    public string? Cause { get; init; }
}

/// <summary>某名玩家在某次结算后的活形状态。</summary>
public sealed record LifePlayerEntry
{
    public int Player { get; init; }

    /// <summary>已确定活形棋串数。</summary>
    public int AliveGroups { get; init; }

    /// <summary>其中只有一枚子的（R8：贴地形的小空区单子即活）。</summary>
    public int SingleStoneAlive { get; init; }

    /// <summary>受保护的眼空间块数（至少属于一条已确定活形棋串的封闭眼空间）。</summary>
    public int EyeSpaces { get; init; }

    /// <summary>受保护的眼空间格数。</summary>
    public int EyeCells { get; init; }

    /// <summary>
    /// 受保护眼空间里"贴地形的小空区"块数（R8）：不超过 3 格，且至少一格在盘内某个几何方向上没有气边（岩石 / 深水 / 崖壁 / 栅栏；棋盘外沿不算）。
    /// 这是遥测分类，不是规则判断。
    /// </summary>
    public int TerrainSmallEyeSpaces { get; init; }
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

    /// <summary>
    /// 行动玩家本小回合展示数的驿站来源（坐标 → 该枚加成，含 +0，逐枚；more-pieces-relics D4 / D12），取自效果快照。
    /// 只在内容集 v2 的对局写出（没有驿站即空表）；v1 局与旧日志为 <c>null</c>，按"无驿站加成"读。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, int>? RelaySources { get; init; }

    /// <summary>行动玩家本小回合工坊是否生效（快照标记，D5）。只在内容集 v2 的对局写出；v1 局与旧日志为 <c>null</c>，按"否"读。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WorkshopActive { get; init; }

    /// <summary>
    /// 活形记录（life-shape 4.1）。新日志每条快照都写（没有变化就是空表与 0），life-shape 之前的旧日志为 <c>null</c>：
    /// 活形分析整局排除并计数，MUST NOT 回填成"这局没人活"。
    /// </summary>
    public LifeTurnEntry? Life { get; init; }

    /// <summary>
    /// 专家前瞻记录（expert-lookahead / match-telemetry「专家前瞻的记录」）：行动玩家本小回合的部署决策是专家（前瞻宽度大于 0）时写出，
    /// 大回合 / 小回合 / 玩家由本快照给出。非专家的小回合与引入之前的旧日志为 <c>null</c>，不写出——只含简单 / 标准 / 高难的日志与改动前逐字节相同。
    /// 只含确定性内容（不含墙钟耗时），回放时重新产生、随确定性文本逐行核对。
    /// 属性级 <c>WhenWritingNull</c>（同 <see cref="RelaySources"/>）：任何序列化选项下都不写出 <c>null</c>，既有快照文本与黄金哈希不变。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LookaheadLogEntry? Lookahead { get; init; }

    /// <summary>
    /// 停滞停手（stagnation-trace S1，ai-decision「循环提子停手」留痕）：行动玩家本小回合的部署决策条目为 <c>D:stall</c>（停滞判定成立且最终 Pass；决胜一手接管不算）时写 <c>true</c>。
    /// 其余小回合与引入之前的旧日志为 <c>null</c>、不写出——不含停手的日志与改动前逐字节相同；离线分析按 <c>true</c> 计停滞停手次数，缺字段计 0。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stalled { get; init; }

    /// <summary>小回合墙钟耗时（毫秒）；只记录，不参与任何决定。</summary>
    public long? ElapsedMs { get; init; }
}

/// <summary>
/// 专家一次部署决策的前瞻记录（match-telemetry「专家前瞻的记录」第 2–4 项；第 1 项由所在快照给出）。取自 Core 的 <see cref="Siege.Core.Ai.LookaheadRecord"/>，
/// 分数为不失真的十进制整数。
/// </summary>
public sealed record LookaheadLogEntry
{
    /// <summary>前瞻状态：<c>Applied</c>（已前瞻）/ <c>NotApplied</c>（不前瞻：宽度不大于 1 或前瞻集只有 1 个候选）/ <c>Pass</c>（候选生成只得到空批次）。</summary>
    public required string Status { get; init; }

    /// <summary>前瞻集（按候选选择规则排序）；Pass 时为空表。</summary>
    public List<LookaheadCandidateEntry> Candidates { get; init; } = [];

    /// <summary>被选候选在 <see cref="Candidates"/> 中的下标；Pass 为 −1。</summary>
    public int Chosen { get; init; }

    /// <summary>被选候选是否与前瞻集的第一个（即同一局面上高难的选择）不同。</summary>
    public bool Changed { get; init; }

    /// <summary>模拟对手一方的预演次数（确定性计数，不是耗时）。</summary>
    public int SimulatedRehearsals { get; init; }

    /// <summary>
    /// 多样补充的排除重跑做的预演次数（expert-strength D8 / D9，确定性计数）。为 0 时不写出（与 <see cref="Siege.Core.Ai.LookaheadRecord.ToText"/> 同一规则），
    /// 一层配置下的记录与改动前逐字节相同；缺字段的旧日志按 0 读。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SupplementRehearsals { get; init; }

    /// <summary>两层扫描的单点预演次数（expert-strength D8 / D9）。为 0 时不写出，缺字段按 0 读。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TwoPlyRehearsals { get; init; }

    /// <summary>被选候选；Pass 为 <c>null</c>。</summary>
    [JsonIgnore]
    public LookaheadCandidateEntry? ChosenCandidate => Chosen >= 0 && Chosen < Candidates.Count ? Candidates[Chosen] : null;

    /// <summary>由 Core 的前瞻记录转写。</summary>
    public static LookaheadLogEntry From(Siege.Core.Ai.LookaheadRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new LookaheadLogEntry
        {
            Status = record.Status.ToString(),
            Candidates = [.. record.Entries.Select(e => new LookaheadCandidateEntry
            {
                Batch = e.CandidateKey,
                Before = e.ScoreBefore,
                Responder = e.Responder?.Value,
                ResponderRound = e.ResponderRound,
                DeployLimit = e.SimulatedDeployLimit,
                Response = e.ResponseKey,
                TwoPlyBonus = e.TwoPlyBonus,
                After = e.ScoreAfter,
                Source = e.Source,
            })],
            Chosen = record.ChosenIndex,
            Changed = record.ChangedChoice,
            SimulatedRehearsals = record.SimulatedRehearsals,
            SupplementRehearsals = record.SupplementRehearsals,
            TwoPlyRehearsals = record.TwoPlyRehearsals,
        };
    }
}

/// <summary>前瞻集中的一个候选（match-telemetry「专家前瞻的记录」第 3 项）。</summary>
public sealed record LookaheadCandidateEntry
{
    /// <summary>候选的批次键（形如 <c>E5:Basic,F5:Basic</c>）。</summary>
    public required string Batch { get; init; }

    /// <summary>前瞻前分数：候选自身的加权总分。</summary>
    public BigInteger Before { get; init; }

    /// <summary>下一名对手的玩家编号；没有（只剩自己）为 <c>null</c>，不写出。</summary>
    public int? Responder { get; init; }

    /// <summary>下一名对手的行动大回合；没有对手为 <c>null</c>。</summary>
    public int? ResponderRound { get; init; }

    /// <summary>模拟部署上限（= 模拟对手持有的普通子数）；没有对手为 <c>null</c>。</summary>
    public int? DeployLimit { get; init; }

    /// <summary>模拟回应的批次键：Pass 为空串；没有对手为 <c>null</c>。</summary>
    public string? Response { get; init; }

    /// <summary>
    /// 两层加分（expert-strength D4 / D9）：⌊λ‰ × max(0, 专家下一手最佳单点增量) / 1000⌋，不失真的十进制整数。为 0 时不写出，
    /// 缺字段（本 change 之前的日志）按 0 读。expert-tiebreak D4 起不计入 <see cref="After"/>（版本 8 起 <see cref="After"/> 即一层分数；版本 7 的日志里 After = 一层 + 本项）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public BigInteger TwoPlyBonus { get; init; }

    /// <summary>前瞻后分数：即一层分数（回应之后的局面上、以决策起点为"前"的加权总分；expert-tiebreak D4 起不含 <see cref="TwoPlyBonus"/>）；不做模拟时等于 <see cref="Before"/>。平局集可由各候选的本项与首部的两层平局带宽推得。</summary>
    public BigInteger After { get; init; }

    /// <summary>
    /// 候选来源（expert-strength D3 / D9）：<c>Original</c>（原排序）/ <c>Supplement</c>（多样补充）。原排序是缺省值，不写出——
    /// 一层配置下的记录与改动前逐字节相同；缺字段（本 change 之前的日志）按原排序读。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Siege.Core.Ai.CandidateSource Source { get; init; }
}

public sealed record PlayerEntry
{
    public int Player { get; init; }

    public required string Status { get; init; }

    /// <summary>是否曾建立正势力（restore-go-core-rules D3 出局标记）。</summary>
    public bool HasEstablishedPower { get; init; }

    /// <summary>总势力，精确整数（restore-go-core-rules D1：任意精度，写出为不失真的十进制整数）。</summary>
    public BigInteger Total { get; init; }

    /// <summary>
    /// 领地分 = 独占空格数（restore-go-core-rules D2；总势力 = 领地分 + 全部棋串军势）。取自对局内的势力明细，不在分析端按盘面重算。
    /// 段 E 之前的旧日志没有该字段（<c>null</c>）：领地分占比整局排除并计数，MUST NOT 回填成 0。
    /// </summary>
    public int? TerritoryScore { get; init; }

    /// <summary>竞争名次；不参赛为 <c>null</c>。</summary>
    public int? Rank { get; init; }

    public List<string> HandTypes { get; init; } = [];

    public List<GroupEntry> Groups { get; init; } = [];

    /// <summary>出局原因（match-telemetry 第 11 项：<c>PowerZero</c> 势力归零 / <c>BattlefieldDefeat</c> 战场落败）。只在链接模式的局、已出局的玩家写出。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EliminationReason { get; init; }

    /// <summary>战场落败时的势力（可以大于 0）。只在链接模式的局、战场落败的玩家写出。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BigInteger? PowerAtDefeat { get; init; }

}

public sealed record GroupEntry
{
    public List<string> Stones { get; init; } = [];

    public int Base { get; init; }

    public int LineBonus { get; init; }

    public int SynergyBonus { get; init; }

    /// <summary>高地压制加值。旧日志为 <c>null</c>：高地加值占比整局排除，MUST NOT 回填成 0。</summary>
    public int? HighGroundBonus { get; init; }

    /// <summary>
    /// 旗手子来源（more-pieces-relics D1 / D12）。以下四项新来源与两项计分信物子拆分只在内容集 v2 的对局写出（含 0）；
    /// v1 局与旧日志为 <c>null</c>，按 0 读（规格明文允许回填：v1 局里不存在这些来源，0 就是真值；与 <see cref="HighGroundBonus"/> 等"MUST NOT 回填"的字段不同）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BannerBonus { get; init; }

    /// <summary>铁链子来源（同上）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ChainBonus { get; init; }

    /// <summary>哨兵子来源（同上）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SentryBonus { get; init; }

    /// <summary>界碑子来源（同上）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BoundaryBonus { get; init; }

    /// <summary><see cref="LineBonus"/> 中由连营带来的额外部分（子拆分，不是第八项来源；取自 <c>GroupPower.EncampmentBonus</c>）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EncampmentBonus { get; init; }

    /// <summary><see cref="SynergyBonus"/> 中由犄角带来的额外部分（子拆分；取自 <c>GroupPower.PincerBonus</c>）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PincerBonus { get; init; }

    /// <summary>四种新来源之和，缺字段（v1 局 / 旧日志）按 0 读。只读派生量，不写进日志。</summary>
    [JsonIgnore]
    public int NewSourceBonus => (BannerBonus ?? 0) + (ChainBonus ?? 0) + (SentryBonus ?? 0) + (BoundaryBonus ?? 0);

    /// <summary>倍增子数量，即倍率指数（restore-go-core-rules：不封顶，"生效倍率指数"字段已删；旧日志里的该字段读入时忽略）。</summary>
    public int MultiplierCount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FormationTier { get; init; }

    [JsonIgnore]
    public int MultiplierExponent => MultiplierCount + (FormationTier ?? 0);

    [JsonIgnore]
    public Multiplier Multiplier => new(MultiplierExponent);

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

    /// <summary>出局原因（链接模式的 <c>PlayerEliminated</c> 事件：<c>PowerZero</c> / <c>BattlefieldDefeat</c>，出局时势力在 <see cref="Values"/> 的 <c>Power</c>）。旧模式不写。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    /// <summary>战场结算（<c>BattlefieldSettled</c>，match-telemetry 第 8 项）。其余事件为 <c>null</c>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SettlementLogEntry? Settlement { get; init; }
}

/// <summary>开局的一块出生棋盘（链接模式日志首部 <see cref="LogHeader.Boards"/>，下标 = 出生棋盘编号）。</summary>
public sealed record BoardEntry
{
    /// <summary>左下角格（围棋记法）。</summary>
    public required string Origin { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>主题（link-map-terrain 第 1 组裁决②，写成枚举名）；没有主题的棋盘（手写夹具）不写出。只供离线统计与重建，不参与回放比对之外的任何计算。</summary>
    public Siege.Core.Board.BoardTheme? Theme { get; init; }
}

/// <summary>一次战场结算（match-telemetry 第 8 项）：大回合、战场标识、原因（<c>AllPassed</c> / <c>BoardFull</c> / <c>Attrition</c>）、名次与各玩家势力、胜者。</summary>
public sealed record SettlementLogEntry
{
    public int Sequence { get; init; }

    public int MajorRound { get; init; }

    public int Battlefield { get; init; }

    public required string Reason { get; init; }

    public int Winner { get; init; }

    public int? EliminationOrder { get; init; }

    public List<SettlementRankEntry> Entries { get; init; } = [];
}

/// <summary>战场结算的一名玩家：名次与结算时的比较量（势力 → 控制信物 → 独占空格 → 棋子数）。</summary>
public sealed record SettlementRankEntry
{
    public int Player { get; init; }

    public int Rank { get; init; }

    public BigInteger Power { get; init; }

    public int ControlledRelics { get; init; }

    public int ExclusiveCells { get; init; }

    public int Stones { get; init; }
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
    public const string PlayerEliminated = "PlayerEliminated";
    public const string PlayerResigned = "PlayerResigned";
    public const string MatchEnded = "MatchEnded";
    public const string Takeover = "Takeover";
    public const string FlagsLocked = "FlagsLocked";

    /// <summary>战场结算（<see cref="LogEvent.Settlement"/>）。</summary>
    public const string BattlefieldSettled = "BattlefieldSettled";

    /// <summary>暂放或确认环节因活棋禁入 / 破坏活形被拒的尝试（life-shape 4.1）；预演环节的同类失败见细粒度 <see cref="Rehearsal"/> 与快照计数。</summary>
    public const string LifeRefused = "LifeRefused";

    /// <summary>只在完整模式保留的细粒度事件。</summary>
    public static bool IsFineGrained(string type) => type is Rehearsal or Candidates;
}

/// <summary>末行：终局原因、名次、标注、揭示表、峰值遥测、耗时。</summary>
public sealed record LogResult
{
    public const string KindName = "result";

    public string Kind { get; init; } = KindName;

    /// <summary><c>EndReason</c> 名。</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// 旧日志里「达大回合上限」的终局原因名。该原因已随 restore-go-core-rules（裁决 #4）从 <c>EndReason</c> 删除，
    /// 这里只为读旧日志保留字面量；段 E（tasks 5.2）换成 <c>turn_limit</c> 截断口径。
    /// </summary>
    public const string LegacyMajorRoundLimit = "MajorRoundLimit";

    /// <summary>
    /// 旧日志里「整轮 Pass」的终局原因名。该原因只属于旧模式，已随 retire-legacy-rules（R9）从 <c>EndReason</c> 删除；
    /// 这里只为读旧日志保留字面量（平衡报告的终局原因分布仍按旧口径统计，报告字节不变）。
    /// </summary>
    public const string LegacyAllPassed = "AllPassed";

    /// <summary>旧日志里「棋盘填满」的终局原因名。同 <see cref="LegacyAllPassed"/>：随 retire-legacy-rules（R9）从 <c>EndReason</c> 删除，只为读旧日志保留字面量。</summary>
    public const string LegacyBoardFull = "BoardFull";

    /// <summary>
    /// 跑局层小回合数截断的结束原因（restore-go-core-rules D5）：不是 <c>EndReason</c> 的成员（规则层没有这种终局），
    /// 这样的局 <see cref="Standings"/> 与 <see cref="Winners"/> 为空。
    /// </summary>
    public const string TurnLimitReason = "turn_limit";

    /// <summary>是否被跑局层截断（<see cref="TurnLimitReason"/>）。</summary>
    public bool Truncated => Reason == TurnLimitReason;

    /// <summary>是否以规则级原因终局：被截断的局与旧日志的「达大回合上限」局为 <c>false</c>。由 <see cref="Reason"/> 派生，读旧日志时忽略文件里的同名字段。</summary>
    public bool Converged => Reason != LegacyMajorRoundLimit && Reason != TurnLimitReason;

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

    /// <summary>
    /// 带出结算（按玩家编号升序，每名玩家一条）：只在带入带出开启的局写出；关闭的局与旧日志为 <c>null</c>（无带出结算）。
    /// 截断局全员 <c>Unsettled</c>（carry-in-out「中途退出与截断」）。
    /// </summary>
    public List<CarryOutEntry>? CarryOut { get; init; }
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

    /// <summary>出局原因（<c>PowerZero</c> / <c>BattlefieldDefeat</c>）。只在链接模式的局、出局者写出。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EliminationReason { get; init; }
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FormationTier { get; init; }

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
