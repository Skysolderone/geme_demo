using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Nodes;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Tests.AiDecision;
using Siege.Core.PieceRelics;
using Siege.Core.Tests.PieceRelics;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>规格：carry-in-out match-setup —— Requirement: 带入带出配置（relic-redesign 接线段 W2 起带入的是信物清单，D5）</summary>
/// <remarks>
/// "逐步相同"两条以 <see cref="候选格上限Tests.LinkGoldenTurnHash"/> 为"此前记录过的对局"（link:1、种子 31、Standard、24 个小回合，
/// 权重 / 阈值 / 内容集写死为 <see cref="SimFixtures.PinPreCalibration"/> 的口径）。retire-legacy-maps 1.8：原为 v5 上在引入带入带出之前钉下的
/// <c>V4GoldenTurnHash</c>；v5 删除前在 link:1 上以同口径重钉，断言不变。带入带出开关在这里<b>显式写死</b>（关闭 / 开启且无人带入），
/// 不跟随缺省值；另跑一局全员带信物的对照（AI 部署时挂出），证明带入确实抵达对局（否则两条"相同"是空证）。
/// </remarks>
public class 带入带出配置Tests
{
    private static readonly PlayerId[] Four = [new(0), new(1), new(2), new(3)];

    /// <summary>
    /// 复刻 <see cref="MatchSession.Create(RunConfig, ulong, MapData?)"/> 的建局三步，只在对局选项上多写带入带出两项。
    /// 另返回建局时（插旗前）四名玩家的开局手牌与征募子流消费数——小回合快照只记手牌类型不记枚数，开局手牌要单独钉住。
    /// </summary>
    private static (MatchLog Log, string OpeningHands) Golden(bool carryInOut, ImmutableSortedDictionary<PlayerId, RelicLoadout> carryIns)
    {
        // retire-legacy-maps 1.8：复刻的是链接图的建局（Link 模式、种子派生配对，与 MatchSession.Create 对 link: 的处理逐项相同）；原为 v5 的 Legacy 建局（含冒险概率）。
        RunConfig raw = 候选格上限Tests.LinkGoldenConfig();
        MapData map = MapCatalog.Resolve(raw.MapId);
        RunConfig config = raw.ResolvedFor(map);
        MatchFlow match = MatchFlow.Create(
            map, new GameSeed(31), config.PlayerIds(),
            MatchOptions.Immediate with
            {
                ArtisanWeight = config.ArtisanWeight,
                ContentSet = config.ContentSet!.Value,
                ScoringVersion = config.ScoringVersion!.Value,
                CarryInOut = carryInOut,
                CarryIns = carryIns,
            });
        string opening = string.Join(" | ", match.Players.Select(p => $"{p}:{CarryWiringFixtures.HandText(match, p)}"))
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

        MatchFlow match = MatchFlow.Create(MapCatalog.Resolve(MapCatalog.DefaultId), new GameSeed(5), Four, MatchOptions.Default);   // retire-legacy-maps：建局由 v5（Legacy）改为 link:1 链接模式
        Assert.False(match.CarryInOut);
        Assert.Empty(match.CarryIns);
        Assert.False(match.CarryInOutBackfilled);
        Assert.False(match.Publish().CarryInOut);

        JsonNode saved = JsonNode.Parse(match.Serialize())!;
        Assert.False(saved["CarryInOut"]!.GetValue<bool>());
        Assert.Empty(saved["CarryIns"]!.AsArray());

        // 日志首部同样写明（段 C）：不配置带入数量的跑局，首部开关写 false、带入写空表，不是缺字段。
        JsonNode header = JsonNode.Parse(MatchSession.Create(SimFixtures.Config(turnLimit: 1), 5).Run().DeterministicText().Split('\n')[0])!;   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        Assert.False(header["CarryInOut"]!.GetValue<bool>());
        Assert.Empty(header["CarryIns"]!.AsArray());
    }

    [Fact]
    public void 关闭时逐步相同()
    {
        // 规格 Scenario：关闭带入带出，以同一种子与配置重跑此前记录过的 AI 对局 → 每一步与引入之前逐项相同，黄金哈希不变。
        (MatchLog off, string opening) = Golden(carryInOut: false, ImmutableSortedDictionary<PlayerId, RelicLoadout>.Empty);
        Assert.Equal(PlainOpening, opening);
        候选格上限Tests.AssertLinkGolden(off);

        // 反面：同一局全员带 2 件战鼓（AI 前几批按落子顺序挂出，R21 临时规则），小回合快照不同——带入确实抵达对局，"相同"不是空证。
        // 带入不改开局手牌（补给已删除），开局手牌仍与关闭时相同；差别来自挂出后战鼓的势力。
        (MatchLog drums, string carried) = Golden(carryInOut: true,
            Four.ToImmutableSortedDictionary(p => p, _ => CarryWiringFixtures.Loadout((RelicKind.WarDrum, true), (RelicKind.WarDrum, false))));
        Assert.Equal(PlainOpening, carried);
        Assert.NotEqual(候选格上限Tests.LinkGoldenTurnHash, 候选格上限Tests.TurnHash(drums));
    }

    [Fact]
    public void 开启但无人带入时逐步相同()
    {
        // 规格 Scenario：开启带入带出、全员无带入，以同一种子重跑同一局 → 逐步与关闭时相同（唯一差别是局终多出带出结算，结算是局外的纯函数，不进对局）。
        (MatchLog on, string opening) = Golden(carryInOut: true, ImmutableSortedDictionary<PlayerId, RelicLoadout>.Empty);
        Assert.Equal(PlainOpening, opening);
        候选格上限Tests.AssertLinkGolden(on);
    }

    [Fact]
    public void 旧存档按无带入读取()
    {
        // 规格 Scenario：恢复一个引入带入带出之前保存的存档 → 恢复成功，开关关闭、全员无带入，并留有回填痕迹。
        // 旧存档样本：一局有人弃赛的对局存档后删掉 CarryInOut / CarryIns 与弃赛记录的 RankAtResign（引入之前的存档结构就是如此）。
        // 旧存档的弃赛记录没有弃赛时名次：恢复后为空，MUST NOT 重算。
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All).Stones(TestMaps.P3, "K10");
        match.Resign(TestMaps.P3);
        string json = match.Serialize();

        JsonObject node = JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("CarryInOut"), "新存档应写出开关");
        Assert.True(node.Remove("CarryIns"), "新存档应写出各玩家带入");
        Assert.True(node["Resignations"]![0]!.AsObject().Remove("RankAtResign"), "新存档应写出弃赛时名次");
        string legacy = node.ToJsonString();

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, legacy);

        Assert.False(restored.CarryInOut);
        Assert.Empty(restored.CarryIns);
        Assert.True(restored.CarryInOutBackfilled);
        Assert.Null(restored.Resignations.Single().RankAtResign);
        Assert.Equal(PlayerStatus(restored), PlayerStatus(match));

        // 新存档不留回填痕迹。
        Assert.False(MatchFlow.RestoreUnvalidated(match.Board.BaseMap, json).CarryInOutBackfilled);

        static string PlayerStatus(MatchFlow m) => string.Join(" ", m.PlayerStates.Select(s => $"{s.Player}:{s.Status}"));
    }

    [Fact]
    public void 新存档往返()
    {
        // 规格 Scenario：保存一局开启带入带出、玩家 1 带入两件（一件保险）、玩家 3 带入一件的对局并恢复 → 开关与各玩家带入清单逐项相同。
        // 开启 + 有带入是非回填值（缺字段回填为关闭、无带入），同时证伪"写入路径漏写"。挂出后的逐件编号往返见 PieceRelics/带入接入对局Tests。
        MatchOptions options = CarryWiringFixtures.On(
            (1, CarryWiringFixtures.Loadout((RelicKind.Banner, true), (RelicKind.Command, false))),
            (3, CarryWiringFixtures.Loadout((RelicKind.Watchtower, false))));
        MatchFlow match = LinkModeFixtures.PlainCreate(options: options);
        match.PlantSequentially(TestMaps.All.Select((p, i) => (p, i)));
        match.PlayTurn();
        string json = match.Serialize();

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, json);

        Assert.True(restored.CarryInOut);
        Assert.False(restored.CarryInOutBackfilled);
        Assert.Equal(match.CarryIns, restored.CarryIns);
        Assert.Equal("军旗（保险）、号令", restored.CarryIns[TestMaps.P1].ToString());
        Assert.Equal(match.PendingCarriedOf(TestMaps.P1), restored.PendingCarriedOf(TestMaps.P1));
        Assert.Equal(json, restored.Serialize());
    }

    [Fact]
    public void 对局中不可改()
    {
        // 规格 Scenario：对局进入插旗阶段之后尝试修改任一玩家的带入 → 系统拒绝。
        // 对局没有任何修改带入的入口（结构守门）：对局配置只在建局时给出，MatchFlow.Options 没有公开的写入口；
        // 带入是不可变字典；拿到配置副本再 with 出一份新配置，对局里的带入不变。
        // relic-redesign R22：公开视图没有带入一项（清单挂出前只对本人可见）。
        MatchFlow match = LinkModeFixtures.PlainCreate(options: CarryWiringFixtures.On((0, CarryWiringFixtures.Loadout((RelicKind.WarDrum, false)))));
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);

        PropertyInfo options = typeof(MatchFlow).GetProperty(nameof(MatchFlow.Options))!;
        Assert.True(options.SetMethod is null || !options.SetMethod.IsPublic, "MatchFlow.Options 不得有公开的写入口");
        Assert.Null(typeof(MatchFlow).GetProperty(nameof(MatchFlow.CarryIns))!.SetMethod);
        Assert.Null(typeof(MatchFlow).GetProperty(nameof(MatchFlow.CarryInOut))!.SetMethod);
        Assert.Equal(typeof(ImmutableSortedDictionary<PlayerId, RelicLoadout>), typeof(MatchOptions).GetProperty(nameof(MatchOptions.CarryIns))!.PropertyType);
        Assert.Null(typeof(MatchPublicView).GetProperty("CarryIns"));
        Assert.Contains(typeof(MatchOptions).GetProperty(nameof(MatchOptions.CarryIns))!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(),
            t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");

        MatchOptions copy = match.Options with { CarryIns = match.Options.CarryIns.SetItem(TestMaps.P0, CarryWiringFixtures.Loadout((RelicKind.Banner, true))) };
        Assert.Equal(RelicKind.Banner, copy.CarryIns[TestMaps.P0].Items[0].Kind);
        Assert.Equal("战鼓", match.CarryIns[TestMaps.P0].ToString());
        Assert.Equal("Basic×5+0", CarryWiringFixtures.HandText(match, TestMaps.P0));
    }

    [Fact]
    public void 关闭时有带入即拒绝()
    {
        // 规格正文：开关关闭时各玩家带入 MUST 为空——关闭却给了带入，拒绝建局，不静默丢弃。
        RelicLoadout one = CarryWiringFixtures.Loadout((RelicKind.WarDrum, false));
        ArgumentException ex = Assert.ThrowsAny<ArgumentException>(() => LinkModeFixtures.PlainCreate(
            options: MatchOptions.Immediate with { CarryIns = ImmutableSortedDictionary<PlayerId, RelicLoadout>.Empty.Add(TestMaps.P0, one) }));
        Assert.Contains("关闭", ex.Message, StringComparison.Ordinal);

        // 带入者不在名册：响亮失败（boundaries.md「显式输入的名册：未知玩家必须响亮失败」）。
        Assert.ThrowsAny<ArgumentException>(() => LinkModeFixtures.PlainCreate(options: CarryWiringFixtures.On((7, one))));
    }
}
