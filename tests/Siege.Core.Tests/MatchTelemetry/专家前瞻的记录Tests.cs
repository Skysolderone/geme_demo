using System.Security.Cryptography;
using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：expert-lookahead / match-telemetry —— Requirement: 专家前瞻的记录。
/// 样本一律写死权重与停手阈值（<see cref="LookaheadFixtures.V5Config"/>），种子按段 B 探针挑选：v5 种子 3 整局中专家有已前瞻、Pass 与一次"前瞻改变选择"。
/// 日志端的每条记录都与跑局时从活控制者取下的 <see cref="LookaheadRecord"/> 逐项比对（写入端漏写 / 写错在这里红，而不是只证明"读得出来"）。
/// </summary>
public class 专家前瞻的记录Tests
{
    private static readonly PlayerId P0 = LookaheadFixtures.P0;

    /// <summary>
    /// 1 名专家（P0）+ 3 名标准，v5 种子 3 整局：日志与 P0 每个小回合的活记录（小回合序号 → 记录）。
    /// expert-strength 1.3：本样本钉的是一层前瞻的记录（"种子 3 恰有一次改变选择"等），改为显式的一层配置（专家预设 + 多样补充上限 0 + 两层权重 0，
    /// 阈值 80、候选格上限 0）——与改动前"未显式给搜索配置"时首部落成的搜索配置逐字段相同，断言一字不改。未显式给配置的专家见「回放核对前瞻记录」。
    /// </summary>
    private static readonly Lazy<(MatchLog Log, Dictionary<int, LookaheadRecord?> Live)> ExpertSample = new(() =>
        RunCapturing(LookaheadFixtures.V5Config(OneLayerExpert, LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard), 3, P0));

    private static PlayerAiConfig OneLayerExpert => new() { Difficulty = AiDifficulty.Expert, Search = LookaheadFixtures.OneLayerConfig() };

    private static PlayerAiConfig Expert => new() { Difficulty = AiDifficulty.Expert };

    /// <summary>
    /// 样本两层权重写死 750‰（expert-strength 2.1）：不取专家预设，扫档替换 λ* 不改变样本口径；750 也不等于 0 与任何扫档值，
    /// 「回放核对前瞻记录」靠它区分"回放按首部重建"与"归 0 / 回落到预设"。
    /// </summary>
    private const int SamplePermille = 750;

    /// <summary>显式打开多样候选（上限 8）与两层加分（<see cref="SamplePermille"/>）的专家，阈值 80、候选格上限 0。</summary>
    private static PlayerAiConfig StrengthExpert(int width = 4) => new()
    {
        Difficulty = AiDifficulty.Expert,
        Search = LookaheadFixtures.StrengthConfig(LookaheadFixtures.PassThreshold, permille: SamplePermille, supplement: 8, width: width) with { CandidateCellLimit = 0 },
    };

    /// <summary>
    /// 1 名打开多样候选与两层加分的专家（P0）+ 3 名标准，v5 种子 2 整局。2.1 探针（种子 1–6）：六颗都有多样补充成员与非零两层加分，"选中多样补充成员"只有种子 2、4 各 1 次，取较小的 2。
    /// </summary>
    private static readonly Lazy<(MatchLog Log, Dictionary<int, LookaheadRecord?> Live)> StrengthSample = new(() =>
        RunCapturing(LookaheadFixtures.V5Config(StrengthExpert(), LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard), StrengthSeed, P0));

    private const ulong StrengthSeed = 2;

    /// <summary>本 change 之前的真实专家日志：expert-lookahead 冒烟 `sim-out/expert-lookahead/smoke20/match-0000000000000008.jsonl`（gzip，原文 226 365 字节）。</summary>
    private static string LegacyExpertLogPath => Path.Combine(AppContext.BaseDirectory, "MatchTelemetry", "Fixtures", "expert-lookahead-smoke20-match-0000000000000008.jsonl.gz");

    [Fact]
    public void 前瞻前后分数可查()
    {
        (MatchLog log, Dictionary<int, LookaheadRecord?> live) = ExpertSample.Value;
        MatchLog parsed = MatchLog.Parse(log.FullText());   // 落盘文本往返：写出时漏字段在这里红

        TurnSnapshot[] expertTurns = [.. parsed.Turns.Where(t => t.Player == P0.Value)];
        Assert.Equal(live.Count, expertTurns.Length);
        Assert.Equal(expertTurns.Select(t => t.Turn), parsed.LookaheadTurns.Select(t => t.Turn));
        foreach (TurnSnapshot turn in expertTurns)
        {
            Assert.Equal(LiveText(live[turn.Turn]!), LogText(turn.Lookahead!));
        }

        // 打开多样候选与两层加分的样本：每条日志记录（含候选来源、两层加分、两类新增预演次数）与活记录逐项相同。
        (MatchLog strength, Dictionary<int, LookaheadRecord?> strengthLive) = StrengthSample.Value;
        MatchLog strengthParsed = MatchLog.Parse(strength.FullText());
        TurnSnapshot[] strengthTurns = [.. strengthParsed.Turns.Where(t => t.Player == P0.Value)];
        Assert.Equal(strengthLive.Count, strengthTurns.Length);
        Assert.Equal(strengthTurns.Select(t => t.Turn), strengthParsed.LookaheadTurns.Select(t => t.Turn));
        foreach (TurnSnapshot turn in strengthTurns)
        {
            Assert.Equal(LiveText(strengthLive[turn.Turn]!), LogText(turn.Lookahead!));
        }

        Assert.Contains(strengthParsed.LookaheadTurns, t => t.Lookahead!.Candidates.Any(c => c.Source == CandidateSource.Supplement));
        Assert.Contains(strengthParsed.LookaheadTurns, t => t.Lookahead!.Candidates.Any(c => c.TwoPlyBonus > 0));
        Assert.Contains(strengthParsed.LookaheadTurns, t => t.Lookahead!.TwoPlyRehearsals > 0);
        Assert.Contains(strengthParsed.LookaheadTurns, t => t.Lookahead!.SupplementRehearsals > 0);

        // 样本口径下界：至少一次已前瞻、候选里有真实的模拟回应（非 Pass），且有候选的前瞻后分数不同于前瞻前分数。
        LookaheadLogEntry[] applied = [.. parsed.LookaheadTurns.Select(t => t.Lookahead!).Where(l => l.Status == nameof(LookaheadStatus.Applied))];
        Assert.True(applied.Length >= 3, $"已前瞻 {applied.Length} 次");
        Assert.Contains(applied, l => l.Candidates.Any(c => c.Responder is not null && !string.IsNullOrEmpty(c.Response)));
        Assert.Contains(applied, l => l.Candidates.Any(c => c.Before != c.After));
        Assert.All(applied, l => Assert.InRange(l.Candidates.Count, 2, AiSearchConfig.DefaultLookaheadWidth));
        Assert.All(applied, l => Assert.True(l.SimulatedRehearsals > 0));

        // 被选候选的前瞻前后分数：与日志快照里实际落下的批次一致。
        foreach (TurnSnapshot turn in parsed.LookaheadTurns.Where(t => t.Lookahead!.Status == nameof(LookaheadStatus.Applied)))
        {
            LookaheadCandidateEntry chosen = turn.Lookahead!.ChosenCandidate!;
            Assert.Equal(string.Join(",", turn.Placements.Select(p => p.Split(':')[0]).Order(StringComparer.Ordinal)),
                string.Join(",", LookaheadFixtures.CoordsOf(chosen.Batch).Select(c => c.ToNotation()).Order(StringComparer.Ordinal)));
        }
    }

    [Fact]
    public void 选择变化可查()
    {
        (MatchLog log, _) = ExpertSample.Value;
        LookaheadLogEntry[] records = [.. MatchLog.Parse(log.FullText()).LookaheadTurns.Select(t => t.Lookahead!)];
        LookaheadLogEntry changed = Assert.Single(records, r => r.Changed);   // 样本口径：种子 3 恰有一次（段 B 探针）
        Assert.True(changed.Chosen >= 1);
        LookaheadCandidateEntry first = changed.Candidates[0];
        LookaheadCandidateEntry chosen = changed.ChosenCandidate!;
        Assert.NotEqual(first.Batch, chosen.Batch);
        Assert.True(first.Before >= chosen.Before, "前瞻集按自身分排序，第一个的前瞻前分数不低于被选者");
        Assert.True(chosen.After > first.After, "被选者的前瞻后分数严格高于第一个（同分取原次序）");
        Assert.All(records, r => Assert.Equal(r.Chosen > 0, r.Changed));
    }

    [Fact]
    public void 不前瞻与Pass也有记录()
    {
        // Pass：整局以整轮 Pass 结束，专家最后的决策是 Pass，记录状态为 Pass、前瞻集为空、下标 −1。
        (MatchLog log, Dictionary<int, LookaheadRecord?> live) = ExpertSample.Value;
        LookaheadLogEntry[] pass = [.. MatchLog.Parse(log.FullText()).LookaheadTurns.Select(t => t.Lookahead!).Where(l => l.Status == nameof(LookaheadStatus.Pass))];
        Assert.NotEmpty(pass);
        Assert.All(pass, l => Assert.Empty(l.Candidates));
        Assert.All(pass, l => Assert.Equal(-1, l.Chosen));
        Assert.All(pass, l => Assert.Null(l.ChosenCandidate));
        Assert.Contains(live.Values, r => r!.Status == LookaheadStatus.Pass);

        // 不前瞻：前瞻宽度 1 的专家（经配置文件的 Search 显式给出）每次非 Pass 的决策都记"不前瞻"，前瞻集只有 1 个候选、前后分数相同、无对手。
        RunConfig w1 = LookaheadFixtures.V5Config(
            new PlayerAiConfig { Difficulty = AiDifficulty.Expert, Search = LookaheadFixtures.ExpertConfig(LookaheadFixtures.PassThreshold, width: 1) with { CandidateCellLimit = AiSearchConfig.LargeMapCellLimit } },
            LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard) with { TurnLimit = 8 };
        (MatchLog small, _) = RunCapturing(w1, 1, P0);
        LookaheadLogEntry[] notApplied = [.. MatchLog.Parse(small.FullText()).LookaheadTurns.Select(t => t.Lookahead!)];
        Assert.NotEmpty(notApplied);
        Assert.Contains(notApplied, l => l.Status == nameof(LookaheadStatus.NotApplied));
        Assert.All(notApplied.Where(l => l.Status == nameof(LookaheadStatus.NotApplied)), l =>
        {
            LookaheadCandidateEntry only = Assert.Single(l.Candidates);
            Assert.Equal(0, l.Chosen);
            Assert.False(l.Changed);
            Assert.Equal(only.Before, only.After);
            Assert.Null(only.Responder);
        });
    }

    [Fact]
    public void 一层配置的记录与改动前相同()
    {
        // 一层配置（多样补充上限 0、两层权重 0）下的专家日志：确定性文本整份与改动前逐字节相同。黄金值在 expert-strength 2.1 改日志端之前、
        // 同一样本上取下（G1 已证一层配置走法不变，所以当时的文本就是"改动前"的文本）；G1 黄金值只钉 ToText()，这里钉 JSON。
        (MatchLog log, _) = ExpertSample.Value;
        string text = log.DeterministicText();
        Assert.Equal(39, log.Turns.Count);
        Assert.Equal(10, log.LookaheadTurns.Count());
        Assert.Equal("078C5B1778226D464689A8CA559BCCBD27357C26D1E21AA57EA99E2AF007D972", Sha256(text));
        Assert.DoesNotContain("\"Source\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"TwoPly", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SupplementRehearsals\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 候选来源可查()
    {
        // 规格原样：前瞻集 1 个原排序 + 3 个多样补充，选中其中一个补充成员 → 后 3 个写来源"多样补充"，第 1 个不写来源字段（按原排序读）。
        LookaheadRecord record = new(LookaheadStatus.Applied,
        [
            Entry("A1:Basic", 100, 90, CandidateSource.Original),
            Entry("B1:Basic", 80, 70, CandidateSource.Supplement),
            Entry("C1:Basic", 70, 95, CandidateSource.Supplement),
            Entry("D1:Basic", 60, 50, CandidateSource.Supplement),
        ], 2, 0);
        string json = SnapshotLine(record);
        Assert.Equal(3, Count(json, "\"Source\":\"Supplement\""));
        Assert.Equal(3, Count(json, "\"Source\""));
        Assert.DoesNotContain("\"Source\":\"Original\"", json, StringComparison.Ordinal);
        LookaheadLogEntry parsed = ParseSnapshot(json);
        Assert.Equal([CandidateSource.Original, CandidateSource.Supplement, CandidateSource.Supplement, CandidateSource.Supplement], parsed.Candidates.Select(c => c.Source));
        Assert.Equal(CandidateSource.Supplement, parsed.ChosenCandidate!.Source);
        Assert.True(parsed.Changed);

        // 真实样本：多样补充成员的来源随日志写出、按原样读回（逐项比对见「前瞻前后分数可查」）；样本里有被选中的多样补充成员。
        (MatchLog log, _) = StrengthSample.Value;
        LookaheadLogEntry[] records = [.. MatchLog.Parse(log.FullText()).LookaheadTurns.Select(t => t.Lookahead!)];
        Assert.Contains(records, r => r.ChosenCandidate?.Source == CandidateSource.Supplement);
        Assert.All(records.Where(r => r.Candidates.Count > 0), r => Assert.Equal(CandidateSource.Original, r.Candidates[0].Source));   // 原排序在前
        Assert.All(records, r => Assert.True(r.Candidates.SkipWhile(c => c.Source == CandidateSource.Original).All(c => c.Source == CandidateSource.Supplement)));
    }

    [Fact]
    public void 两层加分可查()
    {
        // 规格原样：一层分数 −40、两层加分 65 → 记录写两层加分 65 与前瞻后分数 25。
        LookaheadRecord record = new(LookaheadStatus.Applied,
        [
            Entry("A1:Basic", 100, 10, CandidateSource.Original),
            Entry("B1:Basic", 90, 25, CandidateSource.Original, twoPly: 65),
        ], 1, 0, TwoPlyRehearsals: 7);
        string json = SnapshotLine(record);
        Assert.Contains("\"TwoPlyBonus\":65,", json, StringComparison.Ordinal);
        Assert.Contains("\"After\":25", json, StringComparison.Ordinal);
        Assert.Equal(1, Count(json, "\"TwoPlyBonus\""));   // 加分为 0 的第 1 个候选不写
        Assert.Contains("\"TwoPlyRehearsals\":7", json, StringComparison.Ordinal);
        LookaheadLogEntry parsed = ParseSnapshot(json);
        Assert.Equal([0, 65], parsed.Candidates.Select(c => (int)c.TwoPlyBonus));
        Assert.Equal(-40, (int)(parsed.Candidates[1].After - parsed.Candidates[1].TwoPlyBonus));   // 一层分数 = 前瞻后分数 − 两层加分
        Assert.Equal(7, parsed.TwoPlyRehearsals);
        Assert.Equal(0, parsed.SupplementRehearsals);

        // 真实样本：有非零两层加分，且都不为负。
        (MatchLog log, _) = StrengthSample.Value;
        LookaheadCandidateEntry[] candidates = [.. MatchLog.Parse(log.FullText()).LookaheadTurns.SelectMany(t => t.Lookahead!.Candidates)];
        Assert.Contains(candidates, c => c.TwoPlyBonus > 0);
        Assert.All(candidates, c => Assert.True(c.TwoPlyBonus >= 0));
    }

    [Fact]
    public void 本change之前的专家日志照常解析()
    {
        // expert-lookahead 冒烟的真实专家日志（首部只有前瞻宽度 4）：离线解析成功，候选来源读为原排序、两层加分读为 0，
        // 前瞻后分数等于原文里的值；解析后再写出与原文逐字节相同（不多写新字段）；回放逐字节一致（一层配置 = 改动前的专家，G1）。
        string raw = ReadGzip(LegacyExpertLogPath);
        MatchLog legacy = MatchLog.Parse(raw);
        Assert.Equal(26, legacy.Turns.Count);
        Assert.Equal(4, legacy.LookaheadWidthOf(0));
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(legacy.DiverseSupplementLimitOf));
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(legacy.TwoPlyWeightPermilleOf));
        TurnSnapshot[] lookahead = [.. legacy.LookaheadTurns];
        Assert.True(lookahead.Count(t => t.Lookahead!.Status == nameof(LookaheadStatus.Applied)) >= 5);
        Assert.All(lookahead.SelectMany(t => t.Lookahead!.Candidates), c =>
        {
            Assert.Equal(CandidateSource.Original, c.Source);
            Assert.Equal(0, (int)c.TwoPlyBonus);
        });
        Assert.All(lookahead, t => Assert.Equal((0, 0), (t.Lookahead!.SupplementRehearsals, t.Lookahead.TwoPlyRehearsals)));

        // 前瞻后分数：与原文 JSON 里逐条读出的 After 相同（独立解析，不经 MatchLog）。
        string[] rawAfter = [.. raw.Split('\n').Where(l => l.Contains("\"Lookahead\":", StringComparison.Ordinal))
            .SelectMany(l => System.Text.Json.JsonDocument.Parse(l).RootElement.GetProperty("Lookahead").GetProperty("Candidates").EnumerateArray()
                .Select(c => c.GetProperty("After").GetRawText()))];
        Assert.NotEmpty(rawAfter);
        Assert.Equal(rawAfter, lookahead.SelectMany(t => t.Lookahead!.Candidates).Select(c => c.After.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(raw, legacy.FullText());

        ReplayResult replay = Replayer.Replay(legacy);
        Assert.True(replay.Identical, replay.ToString());
        Assert.Equal(lookahead.Select(t => LogText(t.Lookahead!)), replay.Replayed.LookaheadTurns.Select(t => LogText(t.Lookahead!)));
    }

    [Fact]
    public void 非专家没有前瞻记录()
    {
        // 4 名标准：日志中没有任何前瞻记录，确定性文本（含首部）与改动前逐字节相同——黄金值与 难度分级Tests.三档旧难度逐步不变 的标准档同一个（HEAD 03d45f6 钉下）。
        // 变异 M-B3a（非专家也写前瞻记录：控制者没有记录时写一条 Pass）→ 本测试红。
        MatchSession session = MatchSession.Create(LookaheadFixtures.V5Config(
            LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard), 1);
        MatchLog log = session.Run();
        string text = log.DeterministicText();
        Assert.Empty(log.LookaheadTurns);
        Assert.DoesNotContain("Lookahead", log.FullText(), StringComparison.Ordinal);
        Assert.Equal(28, log.Turns.Count);
        Assert.Equal("29353FC976C82867E9DD76AE7229223B876146D5DA53F523366824A7A407736A", Sha256(text));
    }

    [Fact]
    public void 旧日志照常解析()
    {
        // 引入专家难度之前产生的日志：4 名标准的日志与改动前逐字节相同（上一条的黄金值），直接当作旧日志离线解析、回放。
        // 另造一份首部里带显式 Search（无前瞻宽度字段）的旧式日志：前瞻宽度读为 0。
        MatchLog fresh = MatchSession.Create(LookaheadFixtures.V5Config(
            LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard) with { TurnLimit = 12 }, 1).Run();
        MatchLog old = MatchLog.Parse(fresh.FullText());
        Assert.Empty(old.LookaheadTurns);
        Assert.All(old.Turns, t => Assert.Null(t.Lookahead));
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(old.LookaheadWidthOf));
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(old.DiverseSupplementLimitOf));
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(old.TwoPlyWeightPermilleOf));
        ReplayResult replay = Replayer.Replay(old);
        Assert.True(replay.Identical, replay.ToString());

        const string legacySearch = """{"CandidatePointCount":24,"CandidateBatchCount":32,"ImmediateOnly":false,"CandidateCellLimit":24,"PassThreshold":80}""";
        string header = fresh.DeterministicText().Split('\n')[0];
        string withSearch = header.Replace("\"Difficulty\":\"Standard\"", "\"Difficulty\":\"Hard\",\"Search\":" + legacySearch, StringComparison.Ordinal);
        Assert.NotEqual(header, withSearch);
        MatchLog legacy = MatchLog.Parse(withSearch + "\n");
        Assert.All(legacy.Header.Config.Players, p => Assert.NotNull(p.Search));
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(legacy.LookaheadWidthOf));
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(legacy.DiverseSupplementLimitOf));
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(legacy.TwoPlyWeightPermilleOf));
    }

    [Fact]
    public void 回放核对前瞻记录()
    {
        // 1 名未显式给搜索配置的专家（首部落成专家预设：前瞻宽度 4、多样补充上限 8、两层权重 λ*）+ 1 名显式前瞻宽度 2、多样补充上限 8、两层权重 750‰ 的专家
        // + 2 名标准；回放按首部重建三项配置，重新产生的每条前瞻记录与日志逐项相同。
        // 显式配置的那名让"回放不读首部的前瞻宽度 / 两层权重"两种写法（归 0 / 回落到难度预设）都会分歧。
        // 变异 M-B3c（回放不读首部的前瞻宽度）、E-B3（回放不读首部的两层权重）→ 本测试红。
        PlayerAiConfig w2 = StrengthExpert(width: 2) with { Search = StrengthExpert(width: 2).Search! with { CandidateCellLimit = AiSearchConfig.LargeMapCellLimit } };
        RunConfig config = LookaheadFixtures.V5Config(Expert, w2, LookaheadFixtures.Standard, LookaheadFixtures.Standard) with { TurnLimit = 16 };
        MatchLog log = MatchSession.Create(config, 2).Run();
        string path = log.WriteTo(SimFixtures.TempDir("lookahead-replay"));

        string header = File.ReadLines(path).First();
        Assert.Contains("\"LookaheadWidth\":4", header, StringComparison.Ordinal);
        Assert.Contains("\"LookaheadWidth\":2", header, StringComparison.Ordinal);
        MatchLog original = MatchLog.Read(path);
        Assert.Equal([4, 2, 0, 0], Enumerable.Range(0, 4).Select(original.LookaheadWidthOf));
        Assert.Equal([AiSearchConfig.DefaultDiverseSupplementLimit, 8, 0, 0], Enumerable.Range(0, 4).Select(original.DiverseSupplementLimitOf));
        Assert.Equal([AiSearchConfig.DefaultTwoPlyWeightPermille, SamplePermille, 0, 0], Enumerable.Range(0, 4).Select(original.TwoPlyWeightPermilleOf));
        Assert.Contains($"\"TwoPlyWeightPermille\":{SamplePermille}", header, StringComparison.Ordinal);
        Assert.Contains(original.LookaheadTurns, t => t.Player == 1 && t.Lookahead!.Candidates.Any(c => c.TwoPlyBonus > 0));
        Assert.Contains(original.LookaheadTurns, t => t.Player == 0 && t.Lookahead!.Status == nameof(LookaheadStatus.Applied));
        Assert.Contains(original.LookaheadTurns, t => t.Player == 1 && t.Lookahead!.Status == nameof(LookaheadStatus.Applied));
        Assert.All(original.LookaheadTurns.Where(t => t.Player == 1), t => Assert.True(t.Lookahead!.Candidates.Count <= 2));

        ReplayResult replay = Replayer.Replay(original);
        Assert.True(replay.Identical, replay.ToString());
        Assert.Equal(original.LookaheadTurns.Select(t => LogText(t.Lookahead!)), replay.Replayed.LookaheadTurns.Select(t => LogText(t.Lookahead!)));

        // 篡改原日志里一条前瞻记录的前瞻后分数：回放 MUST 在该小回合那一行报分歧（前瞻记录参与逐行核对，不是回放输入）。
        TurnSnapshot target = original.LookaheadTurns.First(t => t.Lookahead!.Status == nameof(LookaheadStatus.Applied));
        LookaheadCandidateEntry tampered = target.Lookahead!.Candidates[0] with { After = target.Lookahead.Candidates[0].After + 1 };
        var forged = new MatchLog
        {
            Header = original.Header,
            Turns = [.. original.Turns.Select(t => t.Turn == target.Turn
                ? t with { Lookahead = t.Lookahead! with { Candidates = [tampered, .. t.Lookahead.Candidates.Skip(1)] } }
                : t)],
            Events = original.Events,
            Result = original.Result,
        };
        ReplayResult diverged = Replayer.Replay(forged);
        Assert.False(diverged.Identical);
        Assert.Contains($"\"Turn\":{target.Turn},", diverged.Expected, StringComparison.Ordinal);
    }

    /// <summary>逐小回合跑一局，记下 <paramref name="expert"/> 每个小回合结束时的活记录（小回合序号 → 记录），最后收尾出日志。</summary>
    private static (MatchLog Log, Dictionary<int, LookaheadRecord?> Live) RunCapturing(RunConfig config, ulong seed, PlayerId expert)
    {
        MatchSession session = MatchSession.Create(config, seed);
        var live = new Dictionary<int, LookaheadRecord?>();
        while (true)
        {
            PlayerId? current = session.Match.CurrentPlayer;
            int before = session.TurnCount;
            bool more = session.RunTurn();
            if (session.TurnCount > before && current == expert)
            {
                live[session.TurnCount] = session.AiOf(expert)!.LastLookahead;
            }

            if (!more)
            {
                break;
            }
        }

        return (session.Run(), live);
    }

    /// <summary>活记录的独立文本投影（逐字段，不经日志端的转写函数）。含候选来源、两层加分与两类新增预演次数（expert-strength 2.1）。</summary>
    private static string LiveText(LookaheadRecord record) =>
        $"{record.Status}|{record.ChosenIndex}|{record.ChangedChoice}|{record.SimulatedRehearsals}|{record.SupplementRehearsals}|{record.TwoPlyRehearsals}|"
        + string.Join(";", record.Entries.Select(e =>
            $"{e.CandidateKey}/{e.Source}/{e.ScoreBefore}/{e.Responder?.Value.ToString() ?? "-"}/{e.ResponderRound?.ToString() ?? "-"}/{e.SimulatedDeployLimit?.ToString() ?? "-"}/{e.ResponseKey ?? "-"}/{e.TwoPlyBonus}/{e.ScoreAfter}"));

    /// <summary>日志记录的同形文本投影。</summary>
    private static string LogText(LookaheadLogEntry entry) =>
        $"{entry.Status}|{entry.Chosen}|{entry.Changed}|{entry.SimulatedRehearsals}|{entry.SupplementRehearsals}|{entry.TwoPlyRehearsals}|"
        + string.Join(";", entry.Candidates.Select(c =>
            $"{c.Batch}/{c.Source}/{c.Before}/{c.Responder?.ToString() ?? "-"}/{c.ResponderRound?.ToString() ?? "-"}/{c.DeployLimit?.ToString() ?? "-"}/{c.Response ?? "-"}/{c.TwoPlyBonus}/{c.After}"));

    /// <summary>合成一个候选（有下一名对手 P1、大回合 3、部署上限 2、回应 E5）。</summary>
    private static LookaheadEntry Entry(string key, long before, long after, CandidateSource source, long twoPly = 0) =>
        new(key, before, LookaheadFixtures.P1, 3, 2, "E5:Basic", after, source, twoPly);

    /// <summary>把一条前瞻记录挂在小回合快照上写成日志行（与跑局时同一转写与序列化选项）。</summary>
    private static string SnapshotLine(LookaheadRecord record)
    {
        MatchLog log = ExpertSample.Value.Log;
        TurnSnapshot turn = log.LookaheadTurns.First() with { Lookahead = LookaheadLogEntry.From(record) };
        MatchLog one = new() { Header = log.Header, Turns = [turn] };
        return one.DeterministicText().Split('\n')[1];
    }

    private static LookaheadLogEntry ParseSnapshot(string line)
    {
        MatchLog log = MatchLog.Parse(ExpertSample.Value.Log.DeterministicText().Split('\n')[0] + "\n" + line + "\n");
        return Assert.Single(log.Turns).Lookahead!;
    }

    private static int Count(string text, string part) => (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;

    private static string ReadGzip(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
