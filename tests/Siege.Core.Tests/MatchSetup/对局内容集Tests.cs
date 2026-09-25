using System.Text.Json.Nodes;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Tests.AiDecision;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：more-pieces-relics match-setup —— Requirement: 对局内容集</summary>
/// <remarks>
/// v1 = 原六种棋子 + 原六类信物（与引入新内容之前逐步相同），v2 = 十 + 十；新局缺省 v2，旧存档 / 旧日志缺字段按 v1 并留痕（design.md D8）。
/// 段 A 只有征募棋池随内容集变化（信物表的 v1 / v2 分流在段 B），因此"信物分布与保存时一致"在段 A 由同一生成器保证，段 B 起才有分流可测。
/// </remarks>
public class 对局内容集Tests
{
    private static readonly PlayerId[] Four = [new(0), new(1), new(2), new(3)];

    /// <summary>按指定内容集跑 <paramref name="turns"/> 个小回合的真实 AI 对局（权重、阈值、冒险概率写死为既有口径）。</summary>
    private static MatchSession Played(ContentSet? set, ulong seed, int turns)
    {
        MatchSession session = MatchSession.Create(SimFixtures.PinPreCalibration(SimFixtures.Config(turnLimit: 0)) with { ContentSet = set }, seed);
        for (int i = 0; i < turns && session.RunTurn(); i++)
        {
        }

        return session;
    }

    private static (bool Revealed, RelicContent? Content)[] RelicStates(MatchFlow match) =>
        [.. match.Relics.Coords.Order().Select(c => (match.Relics.IsRevealed(c), match.Relics.PublicStateOf(c).Content))];

    [Fact]
    public void 旧存档照常读取()
    {
        // 规格 Scenario：恢复一个引入新棋子与新信物之前保存、已揭示若干信物的存档 → 恢复成功，内容集为 v1，每个信物格的类型与强度与保存时一致，后续征募不出现新棋子。
        // 旧存档的样本：v1 对局跑 16 个小回合后存档，再删掉 ContentSet 字段（引入之前的存档结构就是如此，其余字段不变）。
        MatchSession session = Played(ContentSet.V1, seed: 11, turns: 16);
        MatchFlow match = session.Match;
        Assert.True(match.Relics.Coords.Count(match.Relics.IsRevealed) > 0, "样本口径：存档时应已揭示若干信物");
        JsonObject node = JsonNode.Parse(match.Serialize())!.AsObject();
        Assert.True(node.Remove("ContentSet"), "新存档应写出内容集字段");
        string legacy = node.ToJsonString();

        MatchFlow restored = MatchFlow.Restore(match.Board.BaseMap, legacy);

        Assert.Equal(ContentSet.V1, restored.ContentSet);
        Assert.True(restored.ContentSetBackfilled);
        Assert.Equal(ContentSet.V1, restored.Hands.ContentSet);
        Assert.Equal(match.Relics.Generation, restored.Relics.Generation);
        Assert.Equal(RelicStates(match), RelicStates(restored));

        // 后续征募：恢复后的局再跑 24 个小回合，全部候选都在原六种之内。
        int before = restored.Hands.Records.Count;
        MatchSession resumed = MatchSession.ForMatch(restored, session.Config);
        for (int i = 0; i < 24 && resumed.RunTurn(); i++)
        {
        }

        PieceType[] candidates = [.. restored.Hands.Records.Skip(before).SelectMany(r => r.Candidates)];
        Assert.True(candidates.Length >= 60, $"样本口径：恢复后只抽了 {candidates.Length} 个候选位");
        Assert.All(candidates, t => Assert.Contains(t, ContentSets.PieceTypesOf(ContentSet.V1)));
    }

    [Fact]
    public void v1逐步相同()
    {
        // 规格 Scenario：以内容集 v1 用同一种子与同一配置重跑一局此前记录过的 AI 对局 → 每一步与引入内容集之前逐项相同。
        // "此前记录过"= 候选格上限Tests.V4GoldenTurnHash：种子 31、Standard、24 个小回合，在引入内容集之前钉下（快照含每步落子、提子、手牌类型、征募面板规模与势力明细）。
        // 反面：同一配置按 v2 跑出的局不同——否则内容集没有抵达对局，守门是空证。
        RunConfig config = SimFixtures.PinPreCalibration(SimFixtures.Config(seedStart: 31, turnLimit: 24, difficulty: AiDifficulty.Standard));

        MatchLog v1 = BatchRunner.Execute(config with { ContentSet = ContentSet.V1 }, parallelism: 1)[0];
        MatchLog v2 = BatchRunner.Execute(config with { ContentSet = ContentSet.V2 }, parallelism: 1)[0];

        Assert.Equal(ContentSet.V1, v1.Header.Config.ContentSet);
        Assert.True(v1.Turns.Count >= 24, $"小回合 {v1.Turns.Count}");
        Assert.Equal(候选格上限Tests.V4GoldenTurnHash, 候选格上限Tests.TurnHash(v1));
        Assert.NotEqual(候选格上限Tests.TurnHash(v1), 候选格上限Tests.TurnHash(v2));
    }

    [Fact]
    public void 新局缺省v2()
    {
        // 规格 Scenario：不显式配置内容集开一局 → 内容集为 v2，写入日志首部与存档，任何玩家都可在插旗阶段看到。
        Assert.Equal(ContentSet.V2, MatchOptions.Default.ContentSet);
        Assert.Equal(ContentSet.V2, MatchOptions.Immediate.ContentSet);

        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve(MapCatalog.DefaultId), new GameSeed(5), Four);
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);
        Assert.Equal(ContentSet.V2, match.ContentSet);
        Assert.False(match.ContentSetBackfilled);
        Assert.Equal(ContentSet.V2, match.Publish().ContentSet);   // 公开视图：插旗阶段即可读
        Assert.Equal("V2", JsonNode.Parse(match.Serialize())!["ContentSet"]!.GetValue<string>());

        // 跑局：配置里不写内容集 → 新建的局取 v2，并落成具体值写进日志首部；样本里确实抽到了新棋子。
        MatchLog log = BatchRunner.Execute(SimFixtures.PinPreCalibration(SimFixtures.Config(turnLimit: 8)) with { ContentSet = null }, parallelism: 1)[0];
        Assert.Equal(ContentSet.V2, log.Header.Config.ContentSet);
        Assert.Contains("\"ContentSet\":\"V2\"", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);
        MatchSession session = Played(null, seed: 1, turns: 8);
        Assert.Equal(ContentSet.V2, session.Match.ContentSet);
        Assert.Contains(session.Match.Hands.Records.SelectMany(r => r.Candidates), t => !ContentSets.PieceTypesOf(ContentSet.V1).Contains(t));

        // 日志的棋子计数按内容集写键：v2 局十种，且逐串计数之和等于棋子数。
        List<GroupEntry> groups = [.. log.Turns.SelectMany(t => t.PlayersState).SelectMany(p => p.Groups)];
        Assert.NotEmpty(groups);
        Assert.All(groups, g =>
        {
            Assert.Equal(ContentSets.PieceTypesOf(ContentSet.V2).Select(t => t.ToString()), g.PieceCounts!.Keys);
            Assert.Equal(g.Stones.Count, g.PieceCounts.Values.Sum());
        });
    }

    [Fact]
    public void 新存档往返()
    {
        // 规格 Scenario：保存一局内容集为 v2 的对局并恢复 → 恢复后的内容集仍为 v2，信物分布与保存时一致。
        // v2 不是回填值（缺字段回填 v1），所以本用例同时证伪"写入路径漏写内容集"（testing.md「带回填兜底的字段，写入路径要用非回填值证伪」）。
        // 再各自续跑 8 个小回合：恢复局与原局的征募候选逐位相同（征募子流与棋池一起恢复）。
        MatchSession session = Played(ContentSet.V2, seed: 11, turns: 8);
        string json = session.Match.Serialize();

        MatchFlow restored = MatchFlow.Restore(session.Match.Board.BaseMap, json);

        Assert.Equal(ContentSet.V2, restored.ContentSet);
        Assert.False(restored.ContentSetBackfilled);
        Assert.Equal(session.Match.Relics.Generation, restored.Relics.Generation);
        Assert.Equal(RelicStates(session.Match), RelicStates(restored));

        int original = session.Match.Hands.Records.Count;
        int resumedFrom = restored.Hands.Records.Count;
        MatchSession resumed = MatchSession.ForMatch(restored, session.Config);
        for (int i = 0; i < 8; i++)
        {
            Assert.True(session.RunTurn());
            Assert.True(resumed.RunTurn());
        }

        Assert.Equal(
            session.Match.Hands.Records.Skip(original).SelectMany(r => r.Candidates),
            restored.Hands.Records.Skip(resumedFrom).SelectMany(r => r.Candidates));
    }

    [Fact]
    public void 首部缺内容集的旧日志按v1回放()
    {
        // match-setup「对局内容集」正文：回放不含该字段的旧日志按 v1 处理，MUST NOT 按 v2 静默重建。
        // 样本：v1 跑出的局去掉首部配置里的这一项；同一种子按 v2 跑出的局与之不同（样本口径：否则按 v2 回放也会"相同"，守门是空证）。
        RunConfig config = SimFixtures.PinPreCalibration(SimFixtures.Config(seedStart: 11, turnLimit: 8));
        MatchLog v1 = BatchRunner.Execute(config with { ContentSet = ContentSet.V1 }, parallelism: 1)[0];
        MatchLog v2 = BatchRunner.Execute(config with { ContentSet = ContentSet.V2 }, parallelism: 1)[0];
        Assert.NotEqual(候选格上限Tests.TurnHash(v1), 候选格上限Tests.TurnHash(v2));

        string text = v1.DeterministicText();
        Assert.Contains("\"ContentSet\":\"V1\",", text, StringComparison.Ordinal);
        MatchLog old = MatchLog.Parse(text.Replace("\"ContentSet\":\"V1\",", string.Empty, StringComparison.Ordinal));
        Assert.Null(old.Header.Config.ContentSet);

        ReplayResult replay = Replayer.Replay(old);

        Assert.True(replay.Identical, replay.ToString());
        Assert.True(replay.LineCount >= 9, $"比对行数 {replay.LineCount}");
        Assert.Null(replay.Replayed.Header.Config.ContentSet);   // 重建的首部不多出一项

        // 会话层：新建的局缺字段取 v2 并落成具体值；按首部重建的局缺字段取 v1，有字段取记录值。
        RunConfig unset = config with { ContentSet = null };
        Assert.Equal(ContentSet.V2, MatchSession.Create(unset, 11).Match.ContentSet);
        Assert.Equal(ContentSet.V2, MatchSession.Create(unset, 11).Config.ContentSet);
        Assert.Equal(ContentSet.V1, MatchSession.Create(unset, 11, map: null, recorded: true).Match.ContentSet);
        Assert.Equal(ContentSet.V2, MatchSession.Create(config with { ContentSet = ContentSet.V2 }, 11, map: null, recorded: true).Match.ContentSet);
    }

    [Fact]
    public void 内容集须为已定义的值()
    {
        // 0 不是合法内容集（显式编号 1 / 2）：对局配置、跑局配置与存档里的非法值都要响亮失败，不静默当成某个内容集。
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MatchFlow.Create(MapCatalog.Resolve(MapCatalog.DefaultId), new GameSeed(1), Four, MatchOptions.Immediate with { ContentSet = 0 }));
        Assert.Throws<ArgumentException>(() => (SimFixtures.Config() with { ContentSet = (ContentSet)3 }).Validated());

        MatchSession session = Played(ContentSet.V1, seed: 11, turns: 4);
        JsonObject node = JsonNode.Parse(session.Match.Serialize())!.AsObject();
        node["ContentSet"] = "V9";
        Assert.ThrowsAny<Exception>(() => MatchFlow.Restore(session.Match.Board.BaseMap, node.ToJsonString()));
    }
}
