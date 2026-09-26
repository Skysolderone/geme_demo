using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Nodes;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Carry;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Tests.AiDecision;
using Siege.Core.Tests.CarryInOut;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：carry-in-out match-setup —— Requirement: 带入带出配置</summary>
/// <remarks>
/// "逐步相同"两条以 <see cref="候选格上限Tests.V4GoldenTurnHash"/> 为"引入带入带出之前记录过的对局"（种子 31、Standard、24 个小回合，
/// 权重 / 阈值 / 冒险概率 / 内容集写死为 <see cref="SimFixtures.PinPreCalibration"/> 的口径）。带入带出开关在这里<b>显式写死</b>（关闭 / 开启且无人带入），
/// 不跟随缺省值；另跑一局全员带备用子的对照，证明带入确实抵达对局（否则两条"相同"是空证）。
/// </remarks>
public class 带入带出配置Tests
{
    private static readonly PlayerId[] Four = CarryFixtures.Four;

    /// <summary>
    /// 复刻 <see cref="MatchSession.Create(RunConfig, ulong, MapData?)"/> 的建局三步，只在对局选项上多写带入带出两项。
    /// 另返回建局时（插旗前）四名玩家的开局手牌与征募子流消费数——小回合快照只记手牌类型不记枚数，开局手牌要单独钉住。
    /// </summary>
    private static (MatchLog Log, string OpeningHands) Golden(bool carryInOut, ImmutableSortedDictionary<PlayerId, CarryIn> carryIns)
    {
        RunConfig raw = SimFixtures.PinPreCalibration(SimFixtures.Config(seedStart: 31, turnLimit: 24, difficulty: AiDifficulty.Standard));
        MapData map = MapCatalog.Resolve(raw.MapId);
        RunConfig config = raw.ResolvedFor(map);
        MatchFlow match = MatchFlow.Create(
            map, new GameSeed(31), config.PlayerIds(),
            MatchOptions.Immediate with
            {
                ArtisanWeight = config.ArtisanWeight,
                FlagRisk = config.FlagRisk!.Value,
                ContentSet = config.ContentSet!.Value,
                CarryInOut = carryInOut,
                CarryIns = carryIns,
            });
        string opening = string.Join(" | ", match.Players.Select(p => $"{p}:{CarryFixtures.HandText(match, p)}"))
            + $" | recruit={match.Hands.Export().RecruitConsumed}";
        match.PlantPrototype();
        MatchLog log = MatchSession.ForMatch(match, config).Run();
        Assert.False(log.IsFailed);
        Assert.True(log.Turns.Count >= 24, $"小回合 {log.Turns.Count}");
        return (log, opening);
    }

    private const string PlainOpening = "P0:Basic×5+0 | P1:Basic×5+0 | P2:Basic×5+0 | P3:Basic×5+0 | recruit=0";

    [Fact]
    public void 缺省关闭()
    {
        // 规格 Scenario：不显式配置带入带出开一局 → 开关关闭、全员无带入，存档写明这一点（日志首部在段 C）。
        Assert.False(MatchOptions.Default.CarryInOut);
        Assert.Empty(MatchOptions.Default.CarryIns);
        Assert.False(MatchOptions.Immediate.CarryInOut);
        Assert.Empty(MatchOptions.Immediate.CarryIns);

        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve(MapCatalog.DefaultId), new GameSeed(5), Four);
        Assert.False(match.CarryInOut);
        Assert.Empty(match.CarryIns);
        Assert.False(match.CarryInOutBackfilled);
        Assert.False(match.Publish().CarryInOut);
        Assert.Empty(match.Publish().CarryIns);

        JsonNode saved = JsonNode.Parse(match.Serialize())!;
        Assert.False(saved["CarryInOut"]!.GetValue<bool>());
        Assert.Empty(saved["CarryIns"]!.AsArray());
    }

    [Fact]
    public void 关闭时逐步相同()
    {
        // 规格 Scenario：关闭带入带出，以同一种子与配置重跑此前记录过的 AI 对局 → 每一步与引入之前逐项相同，黄金哈希不变。
        (MatchLog off, string opening) = Golden(carryInOut: false, ImmutableSortedDictionary<PlayerId, CarryIn>.Empty);
        Assert.Equal(PlainOpening, opening);
        Assert.Equal(候选格上限Tests.V4GoldenTurnHash, 候选格上限Tests.TurnHash(off));

        // 反面：同一局全员带换型令（堡垒子），小回合快照不同——带入确实抵达对局，"相同"不是空证。
        // （全员带备用子在这 24 个小回合里快照哈希不变：快照只记手牌类型不记枚数，多 1 枚普通子没有改变 AI 在这段内的落点。）
        (MatchLog commission, string carried) = Golden(carryInOut: true, Four.ToImmutableSortedDictionary(p => p, _ => CarryFixtures.Commission(PieceType.Fortress)));
        Assert.NotEqual(PlainOpening, carried);
        Assert.NotEqual(候选格上限Tests.V4GoldenTurnHash, 候选格上限Tests.TurnHash(commission));
    }

    [Fact]
    public void 开启但无人带入时逐步相同()
    {
        // 规格 Scenario：开启带入带出、全员无带入，以同一种子重跑同一局 → 逐步与关闭时相同（唯一差别是局终多出带出结算，结算是局外的纯函数，不进对局）。
        (MatchLog on, string opening) = Golden(carryInOut: true, ImmutableSortedDictionary<PlayerId, CarryIn>.Empty);
        Assert.Equal(PlainOpening, opening);
        Assert.Equal(候选格上限Tests.V4GoldenTurnHash, 候选格上限Tests.TurnHash(on));
    }

    [Fact]
    public void 旧存档按无带入读取()
    {
        // 规格 Scenario：恢复一个引入带入带出之前保存的存档 → 恢复成功，开关关闭、全员无带入，并留有回填痕迹。
        // 旧存档样本：一局有人弃赛的对局存档后删掉 CarryInOut / CarryIns 与弃赛记录的 RankAtResign（引入之前的存档结构就是如此）。
        // 旧存档的弃赛记录没有弃赛时名次：恢复后为空，MUST NOT 重算。
        MatchFlow match = MatchFixtures.Started().AtRound(5, MatchFixtures.All).Stones(MatchFixtures.P3, "H8");
        match.Resign(MatchFixtures.P3);
        string json = match.Serialize();

        JsonObject node = JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("CarryInOut"), "新存档应写出开关");
        Assert.True(node.Remove("CarryIns"), "新存档应写出各玩家带入");
        Assert.True(node["Resignations"]![0]!.AsObject().Remove("RankAtResign"), "新存档应写出弃赛时名次");
        string legacy = node.ToJsonString();

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Relics.Generation, legacy);

        Assert.False(restored.CarryInOut);
        Assert.Empty(restored.CarryIns);
        Assert.True(restored.CarryInOutBackfilled);
        Assert.Null(restored.Resignations.Single().RankAtResign);
        Assert.Equal(PlayerStatus(restored), PlayerStatus(match));

        // 新存档不留回填痕迹。
        Assert.False(MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Relics.Generation, json).CarryInOutBackfilled);

        static string PlayerStatus(MatchFlow m) => string.Join(" ", m.PlayerStates.Select(s => $"{s.Player}:{s.Status}"));
    }

    [Fact]
    public void 新存档往返()
    {
        // 规格 Scenario：保存一局开启带入带出、玩家 1 带入换型令（堡垒子）、玩家 3 带入征召签（抽得连珠子）的对局并恢复 → 开关与各玩家带入逐项相同。
        // 开启 + 有带入是非回填值（缺字段回填为关闭、无带入），同时证伪"写入路径漏写"。
        // 样本：种子 1–64 中第一颗让 P3 的征召签抽出连珠子的种子（v2 下 18 / 80）。
        MatchOptions options = CarryFixtures.On((1, CarryFixtures.Commission(PieceType.Fortress)), (3, CarryFixtures.Draft));
        GameSeed? seed = Enumerable.Range(1, 64).Select(i => (GameSeed?)new GameSeed((ulong)i))
            .FirstOrDefault(s => MatchFixtures.Create(s, options).CarryIns[MatchFixtures.P3].Type == PieceType.Line);
        Assert.True(seed is not null, "样本口径：种子 1–64 中应有 P3 抽出连珠子的一颗");

        MatchFlow match = MatchFixtures.Create(seed, options);
        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        match.PlayTurn();
        string json = match.Serialize();

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Relics.Generation, json);

        Assert.True(restored.CarryInOut);
        Assert.False(restored.CarryInOutBackfilled);
        Assert.Equal("P1:Commission>Fortress P3:DraftLot>Line", CarryFixtures.CarryText(restored.CarryIns));
        Assert.Equal(CarryFixtures.CarryText(match.CarryIns), CarryFixtures.CarryText(restored.CarryIns));
        Assert.Equal(CarryFixtures.CarryText(match.Publish().CarryIns), CarryFixtures.CarryText(restored.Publish().CarryIns));
        Assert.Equal(json, restored.Serialize());
    }

    [Fact]
    public void 对局中不可改()
    {
        // 规格 Scenario：对局进入插旗阶段之后尝试修改任一玩家的带入 → 系统拒绝。
        // 对局没有任何修改带入的入口（结构守门）：对局配置只在建局时给出，MatchFlow.Options 没有公开的写入口；
        // 带入是不可变字典；拿到配置副本再 with 出一份新配置，对局里的带入不变。
        MatchFlow match = MatchFixtures.Create(options: CarryFixtures.On((0, CarryFixtures.Spare)));
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);

        PropertyInfo options = typeof(MatchFlow).GetProperty(nameof(MatchFlow.Options))!;
        Assert.True(options.SetMethod is null || !options.SetMethod.IsPublic, "MatchFlow.Options 不得有公开的写入口");
        Assert.Null(typeof(MatchFlow).GetProperty(nameof(MatchFlow.CarryIns))!.SetMethod);
        Assert.Null(typeof(MatchFlow).GetProperty(nameof(MatchFlow.CarryInOut))!.SetMethod);
        Assert.Equal(typeof(ImmutableSortedDictionary<PlayerId, CarryIn>), typeof(MatchOptions).GetProperty(nameof(MatchOptions.CarryIns))!.PropertyType);
        Assert.Equal(typeof(ImmutableSortedDictionary<PlayerId, CarryIn>), typeof(MatchPublicView).GetProperty(nameof(MatchPublicView.CarryIns))!.PropertyType);
        Assert.Contains(typeof(MatchOptions).GetProperty(nameof(MatchOptions.CarryIns))!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(),
            t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");

        MatchOptions copy = match.Options with { CarryIns = match.Options.CarryIns.SetItem(MatchFixtures.P0, CarryFixtures.Commission(PieceType.Fortress)) };
        Assert.Equal(SupplyKind.Commission, copy.CarryIns[MatchFixtures.P0].Kind);
        Assert.Equal("P0:SpareStone>-", CarryFixtures.CarryText(match.CarryIns));
        Assert.Equal("Basic×6+0", CarryFixtures.HandText(match, MatchFixtures.P0));
    }

    [Fact]
    public void 关闭时有带入即拒绝()
    {
        // 规格正文：开关关闭时各玩家带入 MUST 为空——关闭却给了带入，拒绝建局，不静默丢弃。
        ArgumentException ex = Assert.ThrowsAny<ArgumentException>(() => MatchFixtures.Create(
            options: MatchOptions.Immediate with { CarryIns = ImmutableSortedDictionary<PlayerId, CarryIn>.Empty.Add(MatchFixtures.P0, CarryFixtures.Spare) }));
        Assert.Contains("关闭", ex.Message, StringComparison.Ordinal);

        // 带入者不在名册：响亮失败（boundaries.md「显式输入的名册：未知玩家必须响亮失败」）。
        Assert.ThrowsAny<ArgumentException>(() => MatchFixtures.Create(options: CarryFixtures.On((7, CarryFixtures.Spare))));
    }

    [Fact]
    public void 征召签与其他玩家的带入无关()
    {
        // carry-in-out D7：每名玩家的征召签用自己的子流 carry-draft:<编号>，任一玩家的结果与其他玩家带了什么无关
        // （「AI 带入」Scenario「与本机玩家的选择无关」的征召签部分；AI 抽取在段 B）。
        // P2 带征召签；P0 分别不带、带备用子、带征召签（在编号更小的位置消费抽签）——8 颗种子下 P2 抽得的类型逐项相同。
        for (ulong s = 1; s <= 8; s++)
        {
            var seed = new GameSeed(s);
            PieceType? alone = MatchFixtures.Create(seed, CarryFixtures.On((2, CarryFixtures.Draft))).CarryIns[MatchFixtures.P2].Type;
            PieceType? withSpare = MatchFixtures.Create(seed, CarryFixtures.On((0, CarryFixtures.Spare), (2, CarryFixtures.Draft))).CarryIns[MatchFixtures.P2].Type;
            PieceType? withDraft = MatchFixtures.Create(seed, CarryFixtures.On((0, CarryFixtures.Draft), (2, CarryFixtures.Draft))).CarryIns[MatchFixtures.P2].Type;
            Assert.NotNull(alone);
            Assert.Equal(alone, withSpare);
            Assert.Equal(alone, withDraft);
        }
    }
}
