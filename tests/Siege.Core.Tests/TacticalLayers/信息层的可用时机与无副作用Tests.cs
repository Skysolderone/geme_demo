using System.Reflection;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Hand;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>规格：tactical-layers —— Requirement: 信息层的可用时机与无副作用</summary>
public class 信息层的可用时机与无副作用Tests
{
    [Fact]
    public void 他人行动时可查看()
    {
        // 设计文档 §14.2 / implement 3.10：P1 正在部署（已暂放）时，P0 打开势力层 → 正常显示，内容与公开势力明细一致，
        // 不影响 P1：P1 的暂放与预演在 P0 查看前后逐字相同。
        // 变异验证 M-A1：ViewerWorld.Build 要求 `view.CurrentPlayer == viewer`（只在自己回合可建世界）→ 本测试红 1。
        MatchFlow match = MatchFixtures.Started().AtRound(5, [P1, P0, P2, P3]).Pieces(P2, PieceType.Basic, "C3", "C4");
        StagedBatch batch = match.OpenDeploy();
        Assert.Equal(P1, match.CurrentPlayer);
        Assert.Null(batch.Stage(TestMaps.At("D3"), PieceType.Basic));
        string p1Preview = Dump(match.PreviewCurrentBatch());

        var state = new TacticalLayerState();
        state.Press(TacticalLayer.Power);
        var layer = (PowerLayerContent)match.World(P0).Layer(state.Active!.Value);
        state.Release(TacticalLayer.Power);

        foreach (Scoring.PlayerPower truth in match.Scoreboard.Latest!.Players)
        {
            Assert.Equal(truth.Total, layer.Players.Single(r => r.Player == truth.Player).Total);
            Assert.Equal(truth.Sites.Select(s => s.Coord).Notations(), layer.Sites.Where(s => s.Controller == truth.Player).Select(s => s.Coord).Notations());
            Assert.Equal(truth.Groups.Select(g => g.Power), layer.Groups.Where(g => g.Owner == truth.Player).Select(g => g.Power.Power));
        }

        Assert.Equal(2, layer.Groups.Single(g => g.Owner == P2).Power.BaseTotal);
        Assert.Equal(p1Preview, Dump(match.PreviewCurrentBatch()));
        Assert.Equal(["D3:Basic"], batch.Placements.Select(p => p.ToString()));
    }

    [Fact]
    public void 查看无副作用()
    {
        // 设计文档 §14.2 / D4 / implement 3.11：部署期间反复开关各信息层（两种模式）并构建内容 → 暂放状态、正式盘面与全部对局状态不变。
        // 两条腿：部署阶段指纹不变；结束小回合后与孪生局 Serialize 逐字节一致。状态机结构上不引用任何 Core 对象。
        // 变异验证 M-A2：MatchFlow.ForecastInitiative 的势力改用 `Scoreboard.Recalculate(Board, roster, completed)` → 本测试红 1（势力榜版本号）。
        // 变异验证 M-A3：MatchFlow.StructuresOf 改用正式账本 `Relics.SnapshotFor(...)`（部署上限峰值遥测被推进）→ 本测试红 1——P1 控制军令，副本外求快照会把部署上限峰值从 3 推到 4。
        MatchFlow viewed = AiFixtures.Round5(relics: ("C3", RelicFixtures.Command())).Pieces(P1, PieceType.Basic, "C4");
        MatchFlow twin = AiFixtures.Round5(relics: ("C3", RelicFixtures.Command())).Pieces(P1, PieceType.Basic, "C4");
        StagedBatch batch = viewed.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("F6"), PieceType.Basic));
        string fingerprint = Fingerprint(viewed);

        var state = new TacticalLayerState();
        var panel = new HandPanelState();
        for (int i = 0; i < 30; i++)
        {
            state.SetMode(i % 2 == 0 ? LayerInputMode.HoldToShow : LayerInputMode.ClickToToggle);
            foreach (TacticalLayer layer in Enum.GetValues<TacticalLayer>())
            {
                state.Press(layer);
                ViewerWorld world = viewed.World(P0);
                _ = world.Layer(layer);
                _ = world.HandPanel();
                panel.ClickButton();
                state.Release(layer);
            }

            _ = viewed.World(P1).Layer(TacticalLayer.Order);
        }

        Assert.Equal(fingerprint, Fingerprint(viewed));
        Assert.Equal(["F6:Basic"], batch.Placements.Select(p => p.ToString()));

        Assert.True(viewed.Confirm().Confirmed);
        StagedBatch twinBatch = twin.OpenDeploy();
        Assert.Null(twinBatch.Stage(TestMaps.At("F6"), PieceType.Basic));
        Assert.True(twin.Confirm().Confirmed);
        Assert.Equal(twin.Serialize(), viewed.Serialize());
        Assert.Equal(twin.Scoreboard.Version, viewed.Scoreboard.Version);
        Assert.Equal(twin.Relics.DeployLimitPeak, viewed.Relics.DeployLimitPeak);

        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.DoesNotContain(typeof(TacticalLayerState).GetFields(all), f => f.FieldType.Assembly == typeof(GameBoard).Assembly);
    }

    [Fact]
    public void 插旗阶段也能发布补充载荷()
    {
        // 非 Scenario 的接口回归（阶段 B 需要）：插旗阶段还没有势力快照，公开补充载荷仍可发布——
        // 结构参数为各自基础值、气快照为空、顺序预测为空；领地层与势力层退化为空内容而不是抛出。
        // 变异验证 M-A5：MatchFlow.ForecastInitiative 去掉 `Phase != MatchPhase.InProgress` 判断 → 插旗阶段也产出顺序预测，本测试红 1（实测）。
        MatchFlow match = MatchFixtures.Create();
        Assert.Equal(MatchPhase.FlagPlanting, match.Phase);

        Core.Preview.PublicSupplement supplement = match.PublishSupplement();
        Assert.Equal(4, supplement.Structures.Length);
        Assert.All(supplement.Structures, s => Assert.Equal(3, s.Parameters!.DeployLimit.Value));
        Assert.Empty(supplement.Liberties);
        Assert.Null(supplement.NextOrderForecast);
        Assert.Null(supplement.CurrentOrderReport);

        ViewerWorld world = match.World(P0);
        Assert.Empty(((TerritoryLayerContent)world.Layer(TacticalLayer.Board, BoardReading.Ownership)).Cells);
        Assert.Empty(((PowerLayerContent)world.Layer(TacticalLayer.Power)).Groups);
        Assert.Empty(((OrderLayerContent)world.Layer(TacticalLayer.Order)).Rows);
        Assert.Equal(81, world.Board().Cells.Length);
    }

    [Fact]
    public void 信息层不越权()
    {
        // 设计文档 §14.2：任意信息层都不显示对手手牌数量、对手未确认批次或未发现的信物内容。
        // 结构：五层内容类型闭包里没有私有类型；行为：P1 手牌数量、P1 暂放、E5 真实内容三者任意变化，P0 看到的五层投影逐字相同。
        // 变异验证 M-A4（非根类型）：给 GroupScoreView 加 `public Siege.Core.Recruit.HandEntry? Leak { get; init; }` → 本测试红 1。
        foreach (Type content in new[] { typeof(LayerContent), typeof(TerritoryLayerContent), typeof(LibertyLayerContent), typeof(PowerLayerContent), typeof(RelicLayerContent), typeof(OrderLayerContent) })
        {
            Assert.True(PrivateLeaks(content).Length == 0, $"{content.Name}：{string.Join(",", PrivateLeaks(content))}");
        }

        Assert.All(typeof(Siege.Presentation.Layers.TacticalLayers).GetMethods(BindingFlags.Public | BindingFlags.Static).SelectMany(m => m.GetParameters()),
            p => Assert.DoesNotContain(p.ParameterType, new[] { typeof(ViewerWorld), typeof(Recruit.HandPrivateView), typeof(Core.Preview.BatchPreview) }));

        string Layers(Core.Relics.RelicContent hidden, int p1Basic, bool p1Stages)
        {
            MatchFlow match = MatchFixtures.Started(relics: ("E5", hidden)).AtRound(5, [P1, P0, P2, P3]).Pieces(P2, PieceType.Basic, "C3");
            match.Debug.SeedHand(P1, (PieceType.Basic, p1Basic), (PieceType.Fortress, 1));
            StagedBatch batch = match.OpenDeploy();
            if (p1Stages)
            {
                Assert.Null(batch.Stage(TestMaps.At("E4"), PieceType.Fortress));
            }

            ViewerWorld world = match.World(P0);
            return string.Concat(Enum.GetValues<TacticalLayer>().Select(l => Dump(world.Layer(l))));
        }

        string baseline = Layers(RelicFixtures.Command(), 9, p1Stages: false);
        Assert.Equal(baseline, Layers(RelicFixtures.Depot(2), 9, p1Stages: false));
        Assert.Equal(baseline, Layers(RelicFixtures.Command(), 2, p1Stages: false));
        Assert.Equal(baseline, Layers(RelicFixtures.Command(), 9, p1Stages: true));
        Assert.Contains("未知信物", baseline);
    }
}
