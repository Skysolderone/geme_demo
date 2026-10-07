using System.Text;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision（ai-turn-speed）—— Requirement: 决策序列基线与耗时 / Scenario: 决策序列与基线相同。
/// 固定种子、4 名标准难度 AI 的真实对局（跑局层建局：候选格上限与停手阈值按地图落成缺省值），逐小回合导出 AI 的决策序列，
/// 与改动前钉入的基线（<c>AiDecision/Fixtures/decisions-*.txt</c>）逐字节比对——任何性能优化 MUST NOT 改变任何一步。
/// </summary>
/// <remarks>
/// 基线生成于 ai-turn-speed 的任何 Core 改动之前（`git diff --stat -- src` 为空时）。规则或 AI 行为有意变更后重建：
/// 设 <c>SIEGE_REBUILD_DECISION_BASELINE=1</c> 再跑本测试，会把当前序列写回源码目录的夹具（仍做比对，重建当次即绿），并在提交记录里写明变更原因。
/// </remarks>
public class 决策序列基线Tests
{
    [Theory]
    // retire-legacy-maps 段 A2：v5 那一行（种子 1 跑到终局，基线 decisions-siege-4p-base-v5-seed1.txt）随 v5 删除，夹具文件一并删除；
    // 棋盘图上的同一守门由下面 board:1 一行承担（design D2「删只测旧图的测试与对应黄金值」）。
    [InlineData("board:1", 1UL, 12, "decisions-board1-seed1.txt")]
    public void 决策序列与基线相同(string mapId, ulong seed, int maxMajorRounds, string fixture)
    {
        string actual = DecisionSequence.Export(mapId, seed, maxMajorRounds);
        if (Environment.GetEnvironmentVariable("SIEGE_REBUILD_DECISION_BASELINE") == "1")
        {
            File.WriteAllText(Path.Combine(SourceFixtureDirectory(), fixture), actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        string path = Path.Combine(AppContext.BaseDirectory, "AiDecision", "Fixtures", fixture);
        Assert.True(File.Exists(path), $"找不到决策序列基线：{path}");
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(expected.Length < 200_000, $"基线 {fixture} 超过 200 KB（{expected.Length} 字符）");
        Assert.Equal(expected, actual);
    }

    private static string SourceFixtureDirectory([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "Fixtures");
}

/// <summary>固定种子对局的 AI 决策序列导出（ai-turn-speed 1.1）：比对测试与基线重建共用这一份。</summary>
internal static class DecisionSequence
{
    /// <summary>
    /// 在 <paramref name="mapId"/> 上以种子 <paramref name="seed"/> 建一局（4 名标准难度 AI，跑局层缺省配置、不截断），逐小回合驱动并记录每名 AI 本小回合新增的决策条目
    /// （<c>O:</c> 弃牌、<c>R:</c> 征募选择、<c>D:</c> 落点与类型列表或 <c>pass</c>、<c>X:</c> 被拒补救）。<paramref name="maxMajorRounds"/> 大于 0 时只跑到该大回合结束，
    /// 为 0 时跑到终局（未终局即失败，不让截断冒充终局）。输出为 <c>\n</c> 行尾的文本，首部记下影响走法的全部生效配置。
    /// </summary>
    internal static string Export(string mapId, ulong seed, int maxMajorRounds)
    {
        // formation-tiers D2：基线钉在引入阵型之前，显式指定计分规则 v1（v1 与引入之前逐步相同），不重录；其余仍取跑局层缺省。
        // 首部注释行不加这一项——那会改动基线文件的字节。
        var config = new RunConfig { MapId = mapId, SeedStart = seed, Count = 1, TurnLimit = 0, ScoringVersion = Siege.Core.Scoring.ScoringVersion.V1 };
        MatchSession session = MatchSession.Create(config, seed);
        MatchFlow match = session.Match;
        var sb = new StringBuilder();
        sb.Append("# AI 决策序列基线（ai-turn-speed 1.1）\n");
        sb.Append($"# map={match.Map.Id} digest={MapFile.Digest(match.Board.BaseMap)} seed={seed} players={match.Players.Length} difficulty=Standard\n");
        sb.Append($"# cellLimit={session.CellLimit} passThreshold={session.PassThreshold} contentSet={match.ContentSet} flagRisk={config.ResolvedFor(match.Board.BaseMap).FlagRisk} artisanWeight={match.ArtisanWeight}\n");
        sb.Append($"# maxMajorRounds={maxMajorRounds}（0 = 到终局）\n");
        sb.Append("# 每行：大回合<TAB>玩家<TAB>本小回合的决策条目（空格分隔；O: 弃牌 R: 征募 D: 部署 X: 被拒）\n");

        var cursors = new Dictionary<PlayerId, int>();
        while (match.Phase == MatchPhase.InProgress)
        {
            int round = match.MajorRound;
            if (maxMajorRounds > 0 && round > maxMajorRounds)
            {
                break;
            }

            PlayerId player = match.CurrentPlayer ?? throw new InvalidOperationException("对局进行中却没有当前玩家。");
            if (!session.RunTurn())
            {
                break;
            }

            HeuristicTurnController ai = session.AiOf(player) ?? throw new InvalidOperationException($"玩家 {player} 不是启发式 AI。");
            int from = cursors.TryGetValue(player, out int cursor) ? cursor : 0;
            IReadOnlyList<string> decisions = ai.Decisions;
            sb.Append(round).Append('\t').Append(player).Append('\t').AppendJoin(' ', decisions.Skip(from)).Append('\n');
            cursors[player] = decisions.Count;
        }

        if (maxMajorRounds == 0 && match.Phase != MatchPhase.Ended)
        {
            throw new InvalidOperationException($"要求跑到终局，对局却停在 {match.Phase}（第 {match.MajorRound} 大回合）。");
        }

        sb.Append($"# end phase={match.Phase} majorRound={match.MajorRound} reason={match.Result?.Reason.ToString() ?? "-"} turns={session.TurnCount}\n");
        return sb.ToString();
    }
}
