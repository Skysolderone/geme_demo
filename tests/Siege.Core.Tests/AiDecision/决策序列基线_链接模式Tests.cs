using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision —— 决策序列基线（board-link-mode 实现裁决 C5，tasks 3.4）：<c>link:1</c> + 脚本固定配对（<see cref="ScriptedPairingSource"/> 缺省：候选升序两两相配、
/// 奇数时最大者等待、决赛唯一结果）+ 4 名标准难度 AI（跑局层缺省配置：候选格上限与停手阈值按地图落成，裁决 C11 下 K = 24、链接后不变），截断到第 12 大回合；
/// 逐小回合导出 AI 的决策（弃牌 / 征募 / 落点与类型 / Pass / 被拒补救）与配对、链接、战场结算事件，与钉入的基线
/// （<c>AiDecision/Fixtures/decisions-link1-seed3.txt</c>）逐字节比对。旧图的两条基线（v5 / board:1）在 <see cref="决策序列基线Tests"/>，不受影响。
/// </summary>
/// <remarks>
/// 对局种子 3 是 <c>link:1</c> 上第一个"四名 AI 盲选到四块不同出生棋盘"的种子：首轮恰两对、配对脚本的次序会改变链接与之后的一切（变异 MC-27 据此可红）。
/// 规则或 AI 行为有意变更后重建：设 <c>SIEGE_REBUILD_DECISION_BASELINE=1</c> 再跑本测试（写回源码目录的夹具，仍做比对），并在提交记录里写明原因。
/// 变异验证（段 C 后半实跑）：MC-27 <c>ScriptedPairingSource.Default</c> 首轮改为首尾交错相配（(0,2)(1,3)）→ 「决策序列与基线相同」红 1。
/// 裁决 C15（链接模式落后者停手阈值取 0，tasks 3.7）引入后按本口径重钉：逐字节不变。前 12 大回合 48 个小回合里 18 个处于"本战场势力落后"，
/// 但这些小回合的最好单点收益均 ≥ 260、没有边际提升落在 (0, 20] 而只因阈值被撤回的落点，Pass 次数 0 → 0；因此本基线不守 C15，C15 由
/// <see cref="链接模式下落后者的停手阈值Tests"/> 守。
/// </remarks>
public class 决策序列基线_链接模式Tests
{
    private const string Fixture = "decisions-link1-seed3.txt";
    private const ulong Seed = 3;
    private const int MaxMajorRounds = 12;

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

        // 样本口径：四人四区、首轮两对两次链接；基线里确有配对与链接事件，且跑过了第 3 大回合。
        string zones = expected.Split('\n')[1].Split(' ').Single(f => f.StartsWith("zones=", StringComparison.Ordinal))["zones=".Length..];
        Assert.Equal(4, zones.Split(',').Distinct().Count());
        Assert.Equal(2, expected.Split('\n').Count(l => l.StartsWith("L\t", StringComparison.Ordinal) && l.Contains("\tpairing=1\t", StringComparison.Ordinal)));
        Assert.Contains(expected.Split('\n'), l => l.StartsWith("4\t", StringComparison.Ordinal));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 同种子重跑逐字节相同()
    {
        // 基线的前提：同一种子、同一配对脚本重跑两次，导出逐字节相同（不依赖墙钟、并行或真随机）。截断到第 5 大回合（含首轮配对与链接）以省时。
        string a = LinkDecisionSequence.Export(Seed, 5);
        string b = LinkDecisionSequence.Export(Seed, 5);
        Assert.Contains(a.Split('\n'), l => l.StartsWith("P\t", StringComparison.Ordinal));
        Assert.Equal(a, b);
    }

    private static string SourceFixtureDirectory([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "Fixtures");
}

/// <summary>链接模式固定种子对局的 AI 决策序列导出（tasks 3.4）：格式沿用 <see cref="DecisionSequence"/>，另加配对（<c>P</c>）、链接（<c>L</c>）与战场结算（<c>S</c>）行。</summary>
internal static class LinkDecisionSequence
{
    /// <summary>
    /// 在 <c>link:1</c> 上以种子 <paramref name="seed"/> 建一局（4 名标准难度 AI，跑局层缺省配置、不截断小回合），挂脚本固定配对，逐小回合驱动到第 <paramref name="maxMajorRounds"/> 大回合结束或终局。
    /// 每行：<c>大回合 TAB 玩家 TAB 决策条目</c>；本小回合内发生的配对 / 链接 / 结算紧随其后各占一行。<c>\n</c> 行尾。
    /// </summary>
    internal static string Export(ulong seed, int maxMajorRounds)
    {
        var config = new RunConfig { MapId = "link:1", SeedStart = seed, Count = 1, TurnLimit = 0 };
        MapData map = MapCatalog.Resolve(config.MapId);
        MatchSession session = MatchSession.Create(config, seed, map, recorded: false, carry: null, pairing: new ScriptedPairingSource());
        MatchFlow match = session.Match;
        var sb = new StringBuilder();
        sb.Append("# AI 决策序列基线（board-link-mode 3.4，裁决 C5）\n");
        sb.Append($"# map={match.Map.Id} digest={MapFile.Digest(match.Board.BaseMap)} seed={seed} players={match.Players.Length} difficulty=Standard zones={string.Join(",", match.PlayerStates.Select(s => s.BirthZone))}\n");
        sb.Append($"# cellLimit={session.CellLimit} passThreshold={session.PassThreshold} contentSet={match.ContentSet} artisanWeight={match.ArtisanWeight} mode={match.Mode} pairing=script（缺省：升序两两配）\n");
        sb.Append($"# maxMajorRounds={maxMajorRounds}\n");
        sb.Append("# 每行：大回合<TAB>玩家<TAB>本小回合的决策条目（O: 弃牌 R: 征募 D: 部署 X: 被拒）；P 配对、L 链接、S 战场结算\n");

        var cursors = new Dictionary<PlayerId, int>();
        int pairings = 0, links = 0, settlements = 0;
        while (match.Phase == MatchPhase.InProgress && match.MajorRound <= maxMajorRounds)
        {
            int round = match.MajorRound;
            PlayerId player = match.CurrentPlayer ?? throw new InvalidOperationException("对局进行中却没有当前玩家。");
            bool more = session.RunTurn();
            HeuristicTurnController ai = session.AiOf(player) ?? throw new InvalidOperationException($"玩家 {player} 不是启发式 AI。");
            int from = cursors.TryGetValue(player, out int cursor) ? cursor : 0;
            sb.Append(round).Append('\t').Append(player).Append('\t').AppendJoin(' ', ai.Decisions.Skip(from)).Append('\n');
            cursors[player] = ai.Decisions.Count;

            for (; pairings < match.Pairings.Count; pairings++)
            {
                PairingRecord p = match.Pairings[pairings];
                sb.Append($"P\t{p.Sequence}\t{p.Kind}\tround={p.MajorRound}\tcandidates={string.Join(",", p.Candidates)}\tpairs={string.Join(" ", p.Pairs.Select(x => $"{x.X}-{x.Y}"))}\twaiter={p.Waiter?.ToString() ?? "-"}\tsource={p.Source}\n");
            }

            for (; links < match.Links.Count; links++)
            {
                LinkRecord l = match.Links[links];
                sb.Append($"L\t{l.Sequence}\tpairing={l.PairingSequence}\t{l.X}-{l.Y}\tversion={l.MapVersion}\tdegraded={l.Degraded}\tdigest={l.MapDigest}\trelics={string.Join(",", l.NewRelics.Select(r => $"{r.Coord.ToNotation()}:{r.Content.Type}"))}\n");
            }

            for (; settlements < match.Settlements.Count; settlements++)
            {
                BattlefieldSettlementRecord s = match.Settlements[settlements];
                sb.Append($"S\t{s.Sequence}\tround={s.MajorRound}\tfield={s.Battlefield}\t{s.Reason}\twinner={s.Winner}\t{string.Join(" ", s.Entries.Select(e => $"{e.Rank}.{e.Player}={e.Power}"))}\n");
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
