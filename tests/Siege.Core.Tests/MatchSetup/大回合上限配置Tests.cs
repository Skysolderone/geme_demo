using System.Text.Json.Nodes;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// 规格：openspec/changes/cycle-stop/specs/match-setup —— Requirement: 大回合上限配置（整数 ≥ 0，缺省 100，0 = 不启用；始终公开，入存档、日志首部与批次配置；
/// 负数建局报错；旧存档缺字段按 100 回填留痕——负责人裁决 C4 / D4：安全网默认开启）。
/// </summary>
/// <remarks>
/// 持久化守门两条腿（testing.md「带回填兜底的字段，写入路径要用非回填值证伪」）：往返用 60（≠ 回填值 100）；旧存档从 JSON 删字段 → 100 + <c>RoundCapBackfilled</c>；活对象与恢复对象两边都断言。
/// 变异（2.1 实跑，记录见任务 notes.md）：M-RC6 <c>Serialize</c> 不写 <c>RoundCap</c> → 「旧存档按缺省读取」红（往返 60 变 100）；M-RC7 <c>RestoreCore</c> 缺字段按 0 回填 → 同条红；
/// M-RC8 <c>RequireValidRoundCap</c> 不抛 → 「负数报错」红。
/// </remarks>
public class 大回合上限配置Tests
{
    [Fact]
    public void 缺省值()
    {
        // Scenario：不显式配置开一局 → 大回合上限 = 100，存档与日志首部都写明（不是缺字段）。
        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve(MapCatalog.DefaultId), new Siege.Core.Determinism.GameSeed(5), All, MatchOptions.Default);
        Assert.Equal(100, MatchOptions.DefaultRoundCap);
        Assert.Equal(100, match.RoundCap);
        Assert.False(match.RoundCapBackfilled);
        Assert.Equal(100, match.Publish().RoundCap);   // 公开视图：插旗阶段即可读
        Assert.Equal(100, JsonNode.Parse(match.Serialize())!["RoundCap"]!.GetValue<int>());

        // 跑局层：配置里不写上限 → 新建的局取 100，并落成具体值写进 config.json 与日志首部。
        RunConfig config = SimFixtures.Config(turnLimit: 1);
        Assert.Null(config.RoundCap);
        Assert.Equal(100, config.ResolvedFor(MapCatalog.Resolve(MapCatalog.DefaultId)).RoundCap);
        MatchSession session = MatchSession.Create(config, 5);
        Assert.Equal(100, session.Match.RoundCap);
        MatchLog log = session.Run();
        Assert.Equal(100, log.Header.Config.RoundCap);
        Assert.Equal(100, log.RoundCap);
        Assert.Contains("\"RoundCap\":100", log.DeterministicText().Split('\n')[0], StringComparison.Ordinal);

        // 终端 / 图形版入口的缺省选项同样落成 100（MatchOptions.Immediate 只改插旗时限；big-map D6 起选项里缺省为 null = 建局时按档位取，小图 100）。
        Assert.Null(MatchOptions.Immediate.RoundCap);
        Assert.Equal(100, MatchOptions.DefaultRoundCapFor(Siege.Core.Board.MapTier.Small));
        Assert.Equal(100, Create().RoundCap);
        Assert.True(Create().RoundCapIsDefault);

        // 批次配置显式给出即原样进对局配置（非缺省值证伪"会话建局漏写"）。
        Assert.Equal(7, MatchSession.Create(SimFixtures.Config(turnLimit: 1) with { RoundCap = 7 }, 5).Match.RoundCap);
    }

    [Fact]
    public void 负数报错()
    {
        // Scenario：以大回合上限 −1 建局 → 建局报错，不开始对局。批次配置同样在跑局之前报错；0 合法（= 不启用）。
        ArgumentException ex = Assert.Throws<ArgumentException>(() => Create(options: Options with { RoundCap = -1 }));
        Assert.Contains("大回合上限", ex.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => (SimFixtures.Config(turnLimit: 1) with { RoundCap = -1 }).Validated());
        Assert.Equal(0, Create(options: Options with { RoundCap = 0 }).RoundCap);
        Assert.Equal(0, (SimFixtures.Config(turnLimit: 1) with { RoundCap = 0 }).Validated().RoundCap);
    }

    [Fact]
    public void 旧存档按缺省读取()
    {
        // Scenario：恢复一个不含该字段的存档 → 大回合上限 = 100，恢复结果留痕"按缺省回填"，其余行为与保存前一致。
        // 往返用非回填值 60：带字段恢复 → 60、不留痕、再存档逐字节相等；删掉字段恢复 → 100、留痕；两份恢复对象的其余状态相同。
        MatchFlow match = Started([1, 1, 3, 2], options: Options with { RoundCap = 60 }).PlayRounds(2, P0, P2);
        match.PassTurn();
        string json = match.Serialize();
        Assert.Equal(60, JsonNode.Parse(json)!["RoundCap"]!.GetValue<int>());

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, json);
        Assert.Equal(60, restored.RoundCap);
        Assert.Equal(60, restored.Options.RoundCap);
        Assert.Equal(60, restored.Publish().RoundCap);
        Assert.False(restored.RoundCapBackfilled);
        Assert.Equal(json, restored.Serialize());

        JsonObject node = JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("RoundCap"));
        MatchFlow legacy = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, node.ToJsonString());
        Assert.Equal(100, legacy.RoundCap);
        Assert.True(legacy.RoundCapBackfilled);
        Assert.Equal(100, legacy.Publish().RoundCap);
        Assert.Equal(100, JsonNode.Parse(legacy.Serialize())!["RoundCap"]!.GetValue<int>());   // 再存档按回填值补写
        Assert.Equal(State(match), State(legacy));
        Assert.Equal(match.PassedThisRound, legacy.PassedThisRound);

        // 回填值进了判定链：回填出的局在第 100 大回合结束时按上限结算，带字段的局在第 60 大回合。
        legacy.AtRound(100, [P0, P1, P2, P3]).PlayRound(P0);
        Assert.Equal(SettlementReason.RoundCap, Assert.Single(legacy.Settlements).Reason);
        restored.AtRound(60, [P0, P1, P2, P3]).PlayRound(P0);
        Assert.Equal(SettlementReason.RoundCap, Assert.Single(restored.Settlements).Reason);

        // 存档里的负数同样拒绝（与建局同一处校验）。
        node["RoundCap"] = -5;
        Assert.Throws<FormatException>(() => MatchFlow.RestoreUnvalidated(match.Board.BaseMap, node.ToJsonString()));

        static string State(MatchFlow m) => string.Join(" ", m.PlayerStates.Select(s => $"{s.Player}:{s.Status}:{s.BirthZone}")) + "|" + m.MajorRound + "|" + m.Board.Serialize();
    }

    [Fact]
    public void 大图缺省同小图100_显式优先()
    {
        // big-map 裁决 6 / D6 原为大图 60；large-60（2026-11-07）起大图缺省与小图同为 100；显式给值优先；RoundCapIsDefault 供界面写"第 X / 100 大回合"。
        Siege.Core.Board.MapData large = MapCatalog.Resolve("link:1:4:l");
        Assert.Equal(100, MatchOptions.DefaultRoundCapLarge);
        Assert.Equal(100, MatchOptions.DefaultRoundCapFor(Siege.Core.Board.MapTier.Large));
        MatchFlow match = MatchFlow.Create(large, TestMaps.Seed, All, Options);
        Assert.Equal(100, match.RoundCap);
        Assert.True(match.RoundCapIsDefault);
        Assert.Equal((100, true), (match.Publish().RoundCap, match.Publish().RoundCapIsDefault));
        Assert.Equal(100, JsonNode.Parse(match.Serialize())!["RoundCap"]!.GetValue<int>());   // 存的是解析后的整数

        MatchFlow explicitCap = MatchFlow.Create(large, TestMaps.Seed, All, Options with { RoundCap = 60 });
        Assert.Equal(60, explicitCap.RoundCap);
        Assert.False(explicitCap.RoundCapIsDefault);
        MatchFlow smallExplicit = Create(options: Options with { RoundCap = 40 });
        Assert.Equal((40, false), (smallExplicit.RoundCap, smallExplicit.RoundCapIsDefault));

        // 存档往返（大图）：显式 60 恢复后仍 60、不留痕、不算缺省；缺省 100 恢复后仍 100 且算缺省。
        explicitCap.PlantPrototype();
        MatchFlow restored = MatchFlow.Restore(large, explicitCap.Serialize());
        Assert.Equal((60, false, false), (restored.RoundCap, restored.RoundCapIsDefault, restored.RoundCapBackfilled));
        match.PlantPrototype();
        MatchFlow restoredDefault = MatchFlow.Restore(large, match.Serialize());
        Assert.Equal((100, true, false), (restoredDefault.RoundCap, restoredDefault.RoundCapIsDefault, restoredDefault.RoundCapBackfilled));

        // 跑局层：配置不写上限 → 大图落成 100、小图落成 100 写进 config.json 与首部；--round-cap 显式给值原样生效。
        RunConfig config = SimFixtures.Config(turnLimit: 1) with { MapId = "link:1:4:l" };
        Assert.Equal(100, config.ResolvedFor(large).RoundCap);
        Assert.Equal(100, SimFixtures.Config(turnLimit: 1).ResolvedFor(MapCatalog.Resolve("link:1:4")).RoundCap);
        MatchSession session = MatchSession.Create(config, 5);
        Assert.Equal(100, session.Match.RoundCap);
        Assert.Equal(100, session.Run().Header.Config.RoundCap);
        Assert.Equal(7, MatchSession.Create(config with { RoundCap = 7 }, 5).Match.RoundCap);

        // 到点按势力结算（沿用上限安全网机制）：第 100 大回合结束时仍有人落子也结算。
        MatchFlow capped = MatchFlow.Create(large, TestMaps.Seed, All, Options);
        capped.PlantPrototype();
        foreach (PlayerId p in All)
        {
            capped.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        capped.AtRound(100, [P0, P1, P2, P3]).PlayRound(P0);
        Assert.Single(capped.Settlements);
        Assert.Equal(SettlementReason.RoundCap, capped.Settlements[0].Reason);
        Assert.Equal(MatchPhase.Ended, capped.Phase);
    }

    [Fact]
    public void 不启用()
    {
        // Scenario：以大回合上限 0 建局 → 对局不会因大回合数结算（第 300 大回合仍有人落子即继续）；公开视图与存档都写 0（不是缺字段）。
        MatchFlow match = Started([0, 0, 3, 2], options: Options with { RoundCap = 0 });
        Assert.Equal(0, match.RoundCap);
        Assert.Equal(0, match.Publish().RoundCap);
        Assert.Equal(0, JsonNode.Parse(match.Serialize())!["RoundCap"]!.GetValue<int>());

        match.PlayRound(P0).AtRound(300, [P0, P1, P2, P3]).PlayRound(P1);
        Assert.Empty(match.Settlements);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(301, match.MajorRound);
    }
}
