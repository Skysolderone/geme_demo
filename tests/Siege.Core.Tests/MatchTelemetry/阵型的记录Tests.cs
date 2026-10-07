using System.Numerics;
using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;
using Siege.Sim.Analysis;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>规格：formation-tiers match-telemetry —— Requirement: 阵型的记录</summary>
/// <remarks>
/// 首部（配置里的 <c>ScoringVersion</c>）记计分规则版本，棋串条目记阵型阶数；缺这两个字段的旧日志读作 v1 / 0 并照常解析；
/// 分析端还原倍率用"倍增子数量 + 阵型阶数"，阶数只读日志（design.md D3）。
/// <para>变异验证（formation-tiers 段 A，实跑，明细见任务 implement 记录）：</para>
/// <list type="bullet">
/// <item>M-T1 <c>MatchSession.PlayerEntries</c> 不写棋串条目的阵型阶数 → 红「首部与棋串条目」。</item>
/// <item>M-T2 <c>MatchSession.FormationField</c> 在 v1 局也写出 → 红「旧日志照常解析」（v1 文本多出字段）、<c>计分规则版本Tests.v1逐步相同</c> 与逐字节黄金值。</item>
/// <item>M-T3 <c>GroupEntry.MultiplierExponent</c> 丢掉阵型阶数 → 红「分析端的倍率还原」。</item>
/// <item>M-T4 <c>MatchSession.Create</c> 按首部重建时缺字段取缺省 v2 → 红「旧日志照常解析」。</item>
/// <item>M-T5 峰值条目不写阵型阶数 → 红「首部与棋串条目」。</item>
/// <item>M-T6 <c>BalanceAnalyzer.AmplifiedByMultipliers</c> 对成阵棋串仍把整块放大量归倍增子 → 红「分析端的倍率还原」。</item>
/// </list>
/// </remarks>
public class 阵型的记录Tests
{
    private static readonly PlayerId P0 = MatchFixtures.P0;

    /// <summary>测试内独立的阶梯（不调 <see cref="FormationTiers"/>）：棋子数 → 阶数。</summary>
    private static int Ladder(int stones) => stones >= 12 ? 4 : stones >= 8 ? 3 : stones >= 5 ? 2 : stones >= 3 ? 1 : 0;

    [Fact]
    public void 首部与棋串条目()
    {
        // 规格 Scenario：以计分规则 v2 跑一局并读取日志 → 首部的计分规则版本为 v2；一条由 5 枚棋子组成的棋串的条目里阵型阶数为 2。
        // ① 真实盘面 → 写入函数 → 文本 → 解析（testing.md「真实跑局覆盖不到的写入路径，要补真实盘面端到端测试」）：
        //    v2 对局里 P0 的 C5–G5 五枚普通子 → 条目 阵型阶数 2、倍增子数量 0、军势 ⌊5 × 2.25⌋ = 11。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.V2).AtRound(5).Stones(P0, "C5", "D5", "E5", "F5", "G5");
        MatchLog written = SimFixtures.Synthetic(
            1, [SimFixtures.Turn(1, 5, 0, [0, 0, 0, 0]) with { PlayersState = MatchSession.PlayerEntries(match.Publish()) }], [], SimFixtures.ResultOf(5, [0]));
        string text = written.FullText();
        Assert.Contains("\"FormationTier\":2", text, StringComparison.Ordinal);

        GroupEntry five = Assert.Single(MatchLog.Parse(text).Turns[0].PlayersState.Single(p => p.Player == 0).Groups);
        Assert.Equal(5, five.Stones.Count);
        Assert.Equal((2, 0, 2), (five.FormationTier, five.MultiplierCount, five.MultiplierExponent));
        Assert.Equal(11, five.Power);

        // 同一块盘面在 v1 对局里：条目不写阵型阶数（整项省略），军势 5。
        MatchFlow legacy = MatchFixtures.Started(options: MatchFixtures.V1).AtRound(5).Stones(P0, "C5", "D5", "E5", "F5", "G5");
        GroupEntry v1Entry = Assert.Single(MatchSession.PlayerEntries(legacy.Publish()).Single(p => p.Player == 0).Groups);
        Assert.Null(v1Entry.FormationTier);
        Assert.Equal((5, 0), ((int)v1Entry.Power, v1Entry.MultiplierExponent));

        // ② 真实跑局（v2、标准难度、4 人棋盘图种子 31 / 32、各 16 个小回合；retire-legacy-maps 段 A 由 v5 各 40 个小回合改来，样本口径不变）：首部写 v2；每条棋串条目都写了阶数，且等于按棋子数查独立阶梯；
        //    样本口径：确有成阵的棋串（非默认值，testing.md「期望值是 0 / null 的遥测断言抓不到写入端漏写」）。落盘文本往返后读回同样的值。
        RunConfig config = SimFixtures.PinPreCalibration(SimFixtures.Config(count: 2, seedStart: 31, turnLimit: 16, difficulty: AiDifficulty.Standard)) with
        {
            ScoringVersion = ScoringVersion.V2,
        };
        List<MatchLog> logs = [.. BatchRunner.Execute(config, parallelism: 2).Select(l => MatchLog.Parse(l.FullText()))];
        Assert.All(logs, log =>
        {
            Assert.Equal(ScoringVersion.V2, log.Header.Config.ScoringVersion);
            Assert.Equal(ScoringVersion.V2, log.ScoringVersion);
            Assert.Contains("\"ScoringVersion\":\"V2\"", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);
        });
        GroupEntry[] groups = [.. logs.SelectMany(l => l.Turns).SelectMany(t => t.PlayersState).SelectMany(p => p.Groups)];
        Assert.True(groups.Length >= 100, $"样本口径：只有 {groups.Length} 条棋串条目");
        Assert.Contains(groups, g => g.FormationTier >= 2);
        Assert.All(groups, g =>
        {
            Assert.Equal(Ladder(g.Stones.Count), g.FormationTier);
            // 记录的军势与"倍增子数量 + 阵型阶数"自洽（独立整数式）。
            BigInteger sum = g.Base + g.LineBonus + g.SynergyBonus + (g.HighGroundBonus ?? 0) + g.NewSourceBonus;
            int exponent = g.MultiplierCount + g.FormationTier!.Value;
            Assert.Equal(sum * BigInteger.Pow(3, exponent) / BigInteger.Pow(2, exponent), g.Power);
        });

        // 峰值条目同样带阵型阶数：等于峰值串当时的棋子数查阶梯；样本里至少一局的峰值串是成阵的。
        PeakEntry[] peaks = [.. logs.Select(l => l.Result!.Peak).Where(p => p is not null).Select(p => p!)];
        Assert.NotEmpty(peaks);
        Assert.All(peaks, p => Assert.Equal(Ladder(p.Stones.Count), p.FormationTier));
        Assert.Contains(peaks, p => p.FormationTier > 0);
    }

    [Fact]
    public void 旧日志照常解析()
    {
        // 规格 Scenario：解析一份在引入阵型之前写出的日志 → 解析成功，计分规则版本为 v1，全部棋串的阵型阶数为 0。
        // ① 真实旧日志：expert-lookahead 冒烟留下的专家对局（仓库夹具，远早于本 change，v5、1 专家 + 3 标准、整局 26 个小回合）。
        //    首部没有计分规则版本、棋串条目没有阵型阶数；按 v1 读。
        //    retire-legacy-maps 段 B：这份日志钉在已删除的 siege-4p-base-v5 上，原"按首部回放逐行一致"（v1 与引入阵型之前逐步相同的真实旧数据证据）不再可能，
        //    改为断言回放报"已删除"；"缺字段按 v1 回放逐行一致"由下面第 ② 段在棋盘图上的现跑日志守住。
        string raw = ReadGzip(Path.Combine(AppContext.BaseDirectory, "MatchTelemetry", "Fixtures", "expert-lookahead-smoke20-match-0000000000000008.jsonl.gz"));
        Assert.DoesNotContain("ScoringVersion", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("FormationTier", raw, StringComparison.Ordinal);
        MatchLog legacy = MatchLog.Parse(raw);
        Assert.Null(legacy.Header.Config.ScoringVersion);
        Assert.Equal(ScoringVersion.V1, legacy.ScoringVersion);
        GroupEntry[] legacyGroups = [.. legacy.Turns.SelectMany(t => t.PlayersState).SelectMany(p => p.Groups)];
        Assert.True(legacyGroups.Length >= 100, $"样本口径：只有 {legacyGroups.Length} 条棋串条目");
        Assert.Contains(legacyGroups, g => g.Stones.Count >= 3);   // 样本口径：按 v2 本该成阵的棋串确实存在
        Assert.All(legacyGroups, g => Assert.Equal((null, g.MultiplierCount), (g.FormationTier, g.MultiplierExponent)));

        Assert.Equal("siege-4p-base-v5", legacy.Header.MapId);
        Assert.Throws<Siege.Core.Board.Maps.RetiredMapException>(() => Replayer.Replay(legacy));

        // ② 现跑的 v1 局去掉首部配置里的这一项，就是引入之前的日志形状：照常解析、按 v1 回放逐行一致。
        //    样本口径：同一种子按 v2 跑出的局不同（否则按 v2 回放也会"一致"，守门是空证）。
        // retire-legacy-maps 段 A：4 人棋盘图上改用黄金值同口径的种子与截断（候选格上限Tests.GoldenSeed / GoldenTurns；原 v5 种子 31、24 个小回合）。
        RunConfig config = SimFixtures.PinPreCalibration(SimFixtures.Config(seedStart: AiDecision.候选格上限Tests.GoldenSeed, turnLimit: AiDecision.候选格上限Tests.GoldenTurns, difficulty: AiDifficulty.Standard));
        MatchLog v1 = BatchRunner.Execute(config with { ScoringVersion = ScoringVersion.V1 }, parallelism: 1)[0];
        MatchLog v2 = BatchRunner.Execute(config with { ScoringVersion = ScoringVersion.V2 }, parallelism: 1)[0];
        Assert.NotEqual(SimFixtures.TurnTexts(v1.Turns), SimFixtures.TurnTexts(v2.Turns));

        MatchLog old = MatchLog.Parse(SimFixtures.StripScoringV1(v1.DeterministicText()));
        Assert.Null(old.Header.Config.ScoringVersion);
        Assert.Equal(ScoringVersion.V1, old.ScoringVersion);
        Assert.All(old.Turns.SelectMany(t => t.PlayersState).SelectMany(p => p.Groups), g => Assert.Null(g.FormationTier));

        ReplayResult replay = Replayer.Replay(old);
        Assert.True(replay.Identical, replay.ToString());
        Assert.True(replay.LineCount >= 13, $"比对行数 {replay.LineCount}");   // 首部 + GoldenTurns（12）个小回合（retire-legacy-maps 段 A：原 24 个小回合时为 25）
        Assert.Null(replay.Replayed.Header.Config.ScoringVersion);

        // 会话层：新建的局缺字段取 v2 并落成具体值；按首部重建的局缺字段取 v1，有字段取记录值（v2 的日志同样可回放）。
        RunConfig unset = config with { ScoringVersion = null };
        Assert.Equal(ScoringVersion.V2, MatchSession.Create(unset, 31).Match.ScoringVersion);
        Assert.Equal(ScoringVersion.V2, MatchSession.Create(unset, 31).Config.ScoringVersion);
        Assert.Equal(ScoringVersion.V1, MatchSession.Create(unset, 31, map: null, recorded: true).Match.ScoringVersion);
        Assert.Equal(ScoringVersion.V2, MatchSession.Create(config with { ScoringVersion = ScoringVersion.V2 }, 31, map: null, recorded: true).Match.ScoringVersion);
        ReplayResult v2Replay = Replayer.Replay(SimFixtures.Clone(v2));
        Assert.True(v2Replay.Identical, v2Replay.ToString());
        Assert.Equal(ScoringVersion.V2, v2Replay.Replayed.Header.Config.ScoringVersion);
    }

    [Fact]
    public void 分析端的倍率还原()
    {
        // 规格 Scenario：分析一条倍增子数量 2、阵型阶数 2、基础加位置加值为 9、军势 45 的棋串记录 → 还原出的倍率为 1.5^4，与记录的军势自洽。
        // 条目：普通子×3 + 堡垒子×1 + 倍增子×2（基础 3 + 4 + 2 = 9，无位置加值）。
        GroupEntry entry = new()
        {
            Stones = ["B2", "C2", "D2", "E2", "F2", "G2"],
            Base = 9,
            MultiplierCount = 2,
            FormationTier = 2,
            Power = 45,
            PieceCounts = ContentSets.PieceTypesOf(ContentSet.V1).ToDictionary(
                t => t.ToString(),
                t => t switch { PieceType.Basic => 3, PieceType.Fortress => 1, PieceType.Multiplier => 2, _ => 0 }),
        };

        Assert.Equal(4, entry.MultiplierExponent);
        Assert.Equal(new Multiplier(4), entry.Multiplier);
        Assert.Equal((81, 16), ((int)entry.Multiplier.Numerator, (int)entry.Multiplier.Denominator));
        Assert.Equal("5.0625", entry.Multiplier.ToString());
        Assert.Equal(entry.Power, (BigInteger)entry.Base * 81 / 16);   // 与记录的军势自洽（独立整数式）

        // 缺阵型阶数的条目（v1 局 / 旧日志）：指数就是倍增子数量，同一条棋串当时的军势是 20。
        GroupEntry legacy = entry with { FormationTier = null, Power = 20 };
        Assert.Equal((2, "2.25"), (legacy.MultiplierExponent, legacy.Multiplier.ToString()));

        // 落盘往返后分析报告按日志里的阶数归因，不在分析端数棋子：把条目的棋子清单改短（只剩 2 枚的写法），阶数仍读记录的 2。
        // 各棋子势力占比：归倍增子的只是倍增子自己放大的部分 ⌊9 × 2.25⌋ − 9 = 11（阵型多放大的 45 − 20 = 25 不属于任何棋子类型，同高地加值）
        // → 普通 3、堡垒 4、倍增 2 + 11 = 13，合计 20。缺阶数的旧条目走原算式：倍增 2 + (20 − 9) = 13，同样合计 20。
        static BalanceReport Report(GroupEntry g) => BalanceAnalyzer.Analyze([MatchLog.Parse(SimFixtures.Synthetic(
            61, [SimFixtures.Turn(1, 1, 0, [(long)g.Power, 0, 0, 0], ["B2:Basic"], groupsOfPlayer: [g])], [], SimFixtures.ResultOf(1, [0])).DeterministicText())]);

        string[] expected = ["Basic:3", "Fortress:4", "Line:0", "Multiplier:13", "Synergy:0", "Artisan:0"];
        BalanceReport formed = Report(entry with { Stones = ["B2", "C2"] });
        Assert.Equal(expected, formed.PieceShares.Pieces.Select(p => $"{p.Type}:{p.Power}"));
        Assert.Equal((BigInteger)20, formed.PieceShares.TotalPower);
        Assert.Equal(expected, Report(legacy).PieceShares.Pieces.Select(p => $"{p.Type}:{p.Power}"));
        Assert.Equal(11, BalanceAnalyzer.AmplifiedByMultipliers(entry));
        Assert.Equal(11, BalanceAnalyzer.AmplifiedByMultipliers(legacy));
    }

    private static string ReadGzip(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
