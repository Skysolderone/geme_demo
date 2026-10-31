using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision —— 决策序列基线（board-link-mode 实现裁决 C5，tasks 3.4）：<c>link:1</c>（open-map 段 B：原"脚本固定配对 <c>ScriptedPairingSource</c>"已随配对 / 拼接删除，
/// 对局只凭对局种子复现）+ 4 名标准难度 AI（跑局层缺省配置：候选格上限与停手阈值按地图落成，裁决 C11 下 K = 24、链接后不变），截断到第 12 大回合；
/// 逐小回合导出 AI 的决策（弃牌 / 征募 / 落点与类型 / Pass / 被拒补救）与配对、链接、战场结算事件，与钉入的基线
/// （<c>AiDecision/Fixtures/decisions-link1-seed3.txt</c>）逐字节比对。同口径的种子 1 / 2 两条（retire-legacy-maps 新钉）在 <see cref="决策序列基线Tests"/>；旧图的两条基线（v5 / board:1）已随旧地图删除。
/// </summary>
/// <remarks>
/// 对局种子 3 是 <c>link:1</c> 上第一个"四名 AI 盲选到四块不同出生棋盘"的种子：首轮恰两对、配对脚本的次序会改变链接与之后的一切（变异 MC-27 据此可红）。
/// 规则或 AI 行为有意变更后重建：设 <c>SIEGE_REBUILD_DECISION_BASELINE=1</c> 再跑本测试（写回源码目录的夹具，仍做比对），并在提交记录里写明原因。
/// 变异验证（段 C 后半实跑，历史）：MC-27 原配对脚本首轮改为首尾交错相配 → 「决策序列与基线相同」红 1（配对已随 open-map 删除，该变异不再可做）。
/// 裁决 C15（链接模式落后者停手阈值取 0，tasks 3.7）引入后按本口径重钉：逐字节不变。前 12 大回合 48 个小回合里 18 个处于"本战场势力落后"，
/// 但这些小回合的最好单点收益均 ≥ 260、没有边际提升落在 (0, 20] 而只因阈值被撤回的落点，Pass 次数 0 → 0；因此本基线不守 C15，C15 由
/// <see cref="链接模式下落后者的停手阈值Tests"/> 守。
/// ai-pass-review 2.2（决胜一手，裁决 P7 = A 窄版）重钉：第 1–9 大回合与第 10 大回合前两手逐字节不变；第 10 大回合 P0（战场 2，势力 78 对 P2 88，P2 本大回合已 Pass）
/// 原为 D:pass → 整轮 Pass 结算 P2 胜；现走决胜一手落强制贪心批次 V6:Sentry,U7:Synergy（评价收益 −1355，预演后 105 对 88），战场 2 不结算、不进决赛；
/// 第 12 大回合 P2（86 对 104，P0 已 Pass）同样走决胜一手落单点 R5:Multiplier（预演后 148 对 104）。截到第 12 大回合时对局仍在进行（原第 12 大回合以最后战场结算终局）。
/// 其余四条链接图基线（种子 1 / 2、简单 / 困难种子 1）逐字节不变。
/// final-countdown 1.4（缺省 K = 2）：本条（四人四区）截到第 12 大回合两个首轮战场都未结算、不出现等待者，倒计时不启动，逐字节不变；
/// 改前基线另存为 <c>decisions-link1-seed3-k0.txt</c>（与本文件相同），由 <c>Battlefield.决赛倒计时Tests</c> 以 K = 0 比对。其余四条的重钉说明见 <see cref="决策序列基线Tests"/>。
/// ai-seat-vision 1.4（AI 缺省按座位视野）重钉：改前文件另存为 <c>decisions-link1-seed3-omniscient.txt</c>（显式全知逐字节相同，见 <c>正式对战AI的信息边界_链接模式Tests.全知配置下与改前相同</c>）。
/// 第 1 大回合 P1 起即分歧（四人四区，P1 独自在出生棋盘 1）：原落 <c>AA4:Boundary,AA6:Line,Z7:Basic</c>，现落 <c>AA6:Line,Z7:Boundary</c>。实测两批九维原始值只差先手维：
/// 全知下 P1 势力 0、全局名次 4（他战场 P0 17 / P2 11 / P3 8 可见），落子后升到 2，先手维 +2；按座位视野下他战场玩家势力为 0、名次只在同战场内比较，1 → 1，先手维为 0，
/// 第三枚子的边际不再越过停手阈值。此后整局走法都不同：首轮配对与两次链接不变；截到第 12 大回合不再出现决胜一手（原第 10 / 12 大回合各一次），
/// 第 12 大回合四人全部 Pass，两个首轮战场同时整轮 Pass 结算（P1 299 胜 P3 86；P2 148 胜 P0 108），随后决赛配对 0-2 与第三次链接；小回合数仍为 48。
/// link-map-surfaces 2.2 重钉：link:1 中间带投放新地表，首个分歧在第 3 个小回合（第 1 大回合 P0：原落 L5 / J6 / J8，J8 挨着新浅滩 K8；现落 M4 / L5 / J6），仍四人四区、截到第 12 大回合 48 个小回合。
/// link-map-surfaces 1.3（F1 / F2）再重钉：相对第一次重钉的首个分歧在第 5 个小回合（第 2 大回合 P0：原落 Q4 挨着新岩台 P4，现落 P4）；相对改前基线仍是第 3 个小回合。
/// ai-whole-batch 1.6 重钉（整批候选缺省开）：首个分歧在第 11 个小回合（第 3 大回合 P0）：原落 K7 普通 + K8 倍增 + L8 普通，现选中剪枝候选 K7 + L8（整批总分 496）；
/// 改前文件另存为 <c>decisions-link1-seed3-wb0.txt</c>，开关 0 下逐字节相同。
/// map-coastline（裁决 5：同标识出新图）重录：首个分歧在第 1 大回合 P3 的首批（原 H5 / E8 / G8，现 H5 / H6 / G8——座位 1 西侧边距环里的崖下空地成了岩区），逐份说明见 <see cref="决策序列基线Tests"/>。
/// map-coastline 裁决 F1（湖泊 / 岩区不进座位外 2 格）再录：第 1 大回合 P3 首批 H5 / H6 / G8 → H5 / E8 / G8（西侧岩区没了，回到 E8），逐份说明见 <see cref="决策序列基线Tests"/>。
/// </remarks>
public class 决策序列基线_链接模式Tests
{
    private const string Fixture = "decisions-link1-seed3.txt";
    private const ulong Seed = 3;
    private const int MaxMajorRounds = LinkDecisionSequence.BaselineRounds;

    [Fact]
    public void 决策序列与基线相同()
    {
        string actual = LinkDecisionSequence.Export(Seed, MaxMajorRounds);
        if (Environment.GetEnvironmentVariable("SIEGE_REBUILD_DECISION_BASELINE") == "1")
        {
            File.WriteAllText(Path.Combine(SourceFixtureDirectory(), Fixture), actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        string path = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", Fixture);
        Assert.True(File.Exists(path), $"找不到决策序列基线：{path}");
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(expected.Length < 200_000, $"基线 {Fixture} 超过 200 KB（{expected.Length} 字符）");

        // 样本口径（open-map 重钉）：四人四区；基线里没有任何配对与链接事件，且跑过了第 3 大回合（第 4 大回合起全图可落）。
        string zones = expected.Split('\n')[1].Split(' ').Single(f => f.StartsWith("zones=", StringComparison.Ordinal))["zones=".Length..];
        Assert.Equal(4, zones.Split(',').Distinct().Count());
        Assert.Equal(0, expected.Split('\n').Count(l => l.StartsWith("L\t", StringComparison.Ordinal) || l.StartsWith("P\t", StringComparison.Ordinal)));
        Assert.Contains(expected.Split('\n'), l => l.StartsWith("4\t", StringComparison.Ordinal));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 同种子重跑逐字节相同()
    {
        // 基线的前提：同一种子重跑两次，导出逐字节相同（不依赖墙钟、并行或真随机）。截断到第 5 大回合（跨过第 4 大回合的全图解禁）以省时。
        string a = LinkDecisionSequence.Export(Seed, 5);
        string b = LinkDecisionSequence.Export(Seed, 5);
        Assert.Contains(a.Split('\n'), l => l.StartsWith("4\t", StringComparison.Ordinal));
        Assert.DoesNotContain(a.Split('\n'), l => l.StartsWith("P\t", StringComparison.Ordinal));
        Assert.Equal(a, b);
    }

    private static string SourceFixtureDirectory([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "Fixtures");
}

/// <summary>
/// 链接模式固定种子对局的 AI 决策序列导出（tasks 3.4）：格式沿用 <see cref="DecisionSequence"/>，另加战场结算（<c>S</c>）行。
/// open-map 段 B：配对（<c>P</c>）/ 链接（<c>L</c>）/ 倒计时（<c>C</c>）行与 K / 视野参数随拼接规则删除。
/// stagnation-trace 2.1（负责人裁决 G2）：首部第 3 行删去 <c>mode=Link pairing=script（缺省：升序两两配）</c>、第 5 行删去"；P 配对、L 链接"（保留 S 战场结算），
/// 5 份基线连同首部重钉；决策行除停滞停手的 <c>D:pass</c> → <c>D:stall</c> 外逐字节不变（核对记录见任务 10-21-stagnation-trace 的 baseline-check.txt）。
/// </summary>
internal static class LinkDecisionSequence
{
    /// <summary>
    /// 链接图决策序列基线的截断大回合（全部 <c>decisions-link1-*.txt</c> 与引用它们的测试共用）。map-expand-25 段 A2：link:1:4 换成约 1500 格的浮空岛后，
    /// 4 名标准 / 困难 AI 跑满 12 个大回合单条要数分钟，截断改为 6（构筑期 3 个大回合 + 全图解禁后 3 个大回合，仍覆盖样本口径要求的第 4 大回合），基线随之全部重钉（原为 12）。
    /// </summary>
    internal const int BaselineRounds = 6;

    /// <summary>
    /// 在 <c>link:1</c> 上以种子 <paramref name="seed"/> 建一局（4 名标准难度 AI，跑局层缺省配置、不截断小回合），逐小回合驱动到第 <paramref name="maxMajorRounds"/> 大回合结束或终局。
    /// 每行：<c>大回合 TAB 玩家 TAB 决策条目</c>；本小回合内发生的战场结算紧随其后各占一行。<c>\n</c> 行尾。
    /// </summary>
    internal static string Export(ulong seed, int maxMajorRounds) => Export(seed, maxMajorRounds, AiDifficulty.Standard);

    /// <summary>
    /// 同上，4 名 AI 取难度 <paramref name="difficulty"/>（retire-legacy-maps 4b.4 / L21：简单、困难各补钉一条基线）。标准难度不改玩家配置，输出与上一个重载逐字节相同。
    /// </summary>
    internal static string Export(ulong seed, int maxMajorRounds, AiDifficulty difficulty) => Export(seed, maxMajorRounds, difficulty, attach: null);

    /// <summary>
    /// 同上，建局后先对会话执行 <paramref name="attach"/>（ai-pass-review 1.4「复盘不改对局」：挂部署前复盘回调）；为 <c>null</c> 时与上一个重载逐字节相同。
    /// </summary>
    internal static string Export(ulong seed, int maxMajorRounds, AiDifficulty difficulty, Action<MatchSession>? attach) =>
        Export(seed, maxMajorRounds, difficulty, attach, configure: null);

    /// <summary>
    /// 同上，建会话之前先对跑局配置执行 <paramref name="configure"/>（cycle-stop 2.2「K 为 0 零变化」：显式 <c>StagnationRounds = 0</c>）；为 <c>null</c> 时与上一个重载逐字节相同。
    /// 首部文案不随配置变化（5 份缺省基线 MUST 逐字节不变）。
    /// </summary>
    internal static string Export(ulong seed, int maxMajorRounds, AiDifficulty difficulty, Action<MatchSession>? attach, Func<RunConfig, RunConfig>? configure)
    {
        var config = new RunConfig { MapId = "link:1:4", SeedStart = seed, Count = 1, TurnLimit = 0, ScoringVersion = Siege.Core.Scoring.ScoringVersion.V1 };
        if (difficulty != AiDifficulty.Standard)
        {
            config = config with { Players = [.. Enumerable.Range(0, 4).Select(_ => new PlayerAiConfig { Difficulty = difficulty })] };
        }

        config = configure?.Invoke(config) ?? config;

        MapData map = MapCatalog.Resolve(config.MapId);
        MatchSession session = MatchSession.Create(config, seed, map, recorded: false, carry: null);
        attach?.Invoke(session);
        MatchFlow match = session.Match;
        var sb = new StringBuilder();
        sb.Append("# AI 决策序列基线（board-link-mode 3.4，裁决 C5）\n");
        sb.Append($"# map={match.Map.Id} digest={MapFile.Digest(match.Board.BaseMap)} seed={seed} players={match.Players.Length} difficulty={difficulty} zones={string.Join(",", match.PlayerStates.Select(s => s.BirthZone))}\n");
        sb.Append($"# cellLimit={session.CellLimit} passThreshold={session.PassThreshold} contentSet={match.ContentSet} artisanWeight={match.ArtisanWeight}\n");
        sb.Append($"# maxMajorRounds={maxMajorRounds}\n");
        sb.Append("# 每行：大回合<TAB>玩家<TAB>本小回合的决策条目（O: 弃牌 R: 征募 D: 部署 X: 被拒）；S 战场结算\n");

        var cursors = new Dictionary<PlayerId, int>();
        int settlements = 0;
        while (match.Phase == MatchPhase.InProgress && match.MajorRound <= maxMajorRounds)
        {
            int round = match.MajorRound;
            PlayerId player = match.CurrentPlayer ?? throw new InvalidOperationException("对局进行中却没有当前玩家。");
            bool more = session.RunTurn();
            HeuristicTurnController ai = session.AiOf(player) ?? throw new InvalidOperationException($"玩家 {player} 不是启发式 AI。");
            int from = cursors.TryGetValue(player, out int cursor) ? cursor : 0;
            sb.Append(round).Append('\t').Append(player).Append('\t').AppendJoin(' ', ai.Decisions.Skip(from)).Append('\n');
            cursors[player] = ai.Decisions.Count;

            for (; settlements < match.Settlements.Count; settlements++)
            {
                BattlefieldSettlementRecord s = match.Settlements[settlements];
                sb.Append($"S\t{s.Sequence}\tround={s.MajorRound}\tfield={s.Battlefield}\t{s.Reason}\twinner={s.Winner}\t{string.Join(" ", s.Entries.Select(e => $"{e.Rank}.{e.Player}={e.Power}"))}").Append('\n');
            }

            if (!more)
            {
                break;
            }
        }

        sb.Append($"# end phase={match.Phase} majorRound={match.MajorRound} reason={match.Result?.Reason.ToString() ?? "-"} turns={session.TurnCount}\n");
        return sb.ToString();
    }
}
