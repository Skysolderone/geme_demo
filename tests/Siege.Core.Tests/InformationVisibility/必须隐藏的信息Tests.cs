using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Presentation.Hand;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.InformationVisibility;

/// <summary>规格：information-visibility —— Requirement: 必须隐藏的信息</summary>
public class 必须隐藏的信息Tests
{
    [Fact]
    public void 未发现信物内容隐藏()
    {
        // 设计文档 §13.2：信物格未进入任何覆盖 → 所有玩家只看到"未知信物"，看不到类型与强度。
        // 同种子两局只改真实内容：全部观察者的默认棋盘与信物层投影逐字相同。
        // 变异验证 M-H1：Core `RelicLedger.RelicState.ToPublic` 的 Content 恒填真实内容（`IsRevealed ? Content : null` → `Content`）→ 本测试红 1。
        //   表现层本身拿不到未揭示内容，隐藏由 Core 的公开状态结构保证；本测试钉住表现层没有把它"补"回来的路径。
        string command = Views(RelicFixtures.Command());
        string depot = Views(RelicFixtures.Depot(2));

        Assert.Equal(command, depot);
        Assert.Contains("未知信物", command);
        Assert.DoesNotContain("军令", command);
        Assert.DoesNotContain("Command", command);

        MatchFlow match = AiFixtures.Round5(relics: ("E5", RelicFixtures.Command()));
        BoardCellView cell = match.World(P1).Board().CellAt(TestMaps.At("E5"));
        Assert.Equal((RelicMarker.Unknown, (RelicType?)null), (cell.Relic, cell.RevealedType));
    }

    [Fact]
    public void 手牌数量隐藏()
    {
        // 设计文档 §13.2：B 查看 A 的手牌 → 只见类型。A 持普通子×7、堡垒子×2 与 普通子×1、堡垒子×1 两种情形下，B 看到的敌方区域逐字相同。
        // 变异验证 M-H2：给非根类型 StructureView 加 `public Siege.Core.Recruit.HandPrivateView? Leak { get; init; }` → 本测试红 1（闭包含 HandPrivateView、HandEntry）。
        string many = OpponentAreaOfP0(7, 2);
        string few = OpponentAreaOfP0(1, 1);

        Assert.Equal(many, few);
        Assert.Contains("普通子", many);
        Assert.Contains("堡垒子", many);
        Assert.Empty(PrivateLeaks(typeof(OpponentHandAreaView)));
    }

    [Fact]
    public void 未确认批次隐藏()
    {
        // 设计文档 §13.2：A 正在暂放 → 其他玩家界面不出现任何暂放提示：默认棋盘与五层内容与暂放前逐字相同，没有预演，也塞不进 A 的预演。
        // 变异验证 M-H3：ViewerWorld.Build 删掉"预演属于他人即抛出"的检查 → 本测试红 1。
        MatchFlow match = AiFixtures.Round5();
        StagedBatch batch = match.OpenDeploy();
        string before = OthersView(match, P1);

        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Basic));
        Assert.Null(batch.Stage(TestMaps.At("F6"), PieceType.Basic));
        Core.Preview.BatchPreview previewOfA = match.PreviewCurrentBatch();

        Assert.Equal(before, OthersView(match, P1));
        ViewerWorld seenByB = match.World(P1);
        Assert.Null(seenByB.OwnPreview);
        Assert.Null(seenByB.Preview());
        Assert.Null(seenByB.Board().CellAt(TestMaps.At("E5")).Occupant);
        Assert.Throws<ArgumentException>(() =>
            ViewerWorld.Build(P1, match.Publish(), match.PublishSupplement(), match.Hands.AccessFor(P1).PrivateView(), previewOfA));
    }

    [Fact]
    public void 数据层边界()
    {
        // 设计文档 §13.2 / D1：提供给非持有者的数据结构里<b>不存在</b>四类隐藏信息的字段（不是存在但被遮挡）。
        // 覆盖：公开世界、默认棋盘、五层内容、敌方手牌区——闭包中没有私有手牌视图 / 两段账 / 征募面板 / 暂放批次 / 预演 / 账本 / 种子。
        // 与 heuristic-ai 共用同一个公开视图类型（implement 1.4）：公开世界的快照就是正式 AI 闭包里的 MatchPublicView。
        // 变异验证 M-H4：给非根类型 BoardCellView 加 `public Siege.Core.Batch.Placement? Staged { get; init; }` → 本测试红 1（且"信息层不越权"不红——BoardCellView 不在层闭包里）。
        // 变异验证 M-H5：给非根类型 GroupScoreView 加 `public Siege.Core.Recruit.HandEntry? Leak { get; init; }` → 本测试红 1、"信息层不越权"红 1。
        Type[] roots =
        [
            typeof(PublicWorld), typeof(DefaultBoardView), typeof(OpponentHandAreaView),
            typeof(TerritoryLayerContent), typeof(LibertyLayerContent), typeof(PowerLayerContent), typeof(RelicLayerContent), typeof(OrderLayerContent),
        ];
        foreach (Type root in roots)
        {
            Assert.True(PrivateLeaks(root).Length == 0, $"{root.Name} 闭包泄漏：{string.Join(",", PrivateLeaks(root))}");
        }

        // 反面：闭包确实展开到了非根类型（否则"空"只是没扫到）
        ImmutableHashSet<Type> world = ReachableTypes(typeof(PublicWorld));
        Assert.Contains(typeof(RelicPublicState), world);
        Assert.Contains(typeof(Preview.GroupLiberties), world);
        Assert.Contains(typeof(Scoring.GroupPower), world);
        Assert.NotEmpty(PrivateLeaks(typeof(ViewerWorld)));

        // 同一个公开视图类型
        Assert.Equal(typeof(MatchPublicView), typeof(PublicWorld).GetProperty(nameof(PublicWorld.View))!.PropertyType);
        Assert.Contains(typeof(MatchPublicView), AiFixtures.ReachableTypes(typeof(HeuristicTurnController)));
    }

    private static string Views(RelicContent content)
    {
        MatchFlow match = AiFixtures.Round5(relics: ("E5", content)).Pieces(P1, PieceType.Basic, "B8");
        return string.Join("\n", MatchFixtures.All.Select(viewer =>
        {
            ViewerWorld world = match.World(viewer);
            return Dump(world.Board()) + Dump(world.Layer(TacticalLayer.Relics));
        }));
    }

    private static string OpponentAreaOfP0(int basic, int fortress)
    {
        MatchFlow match = AiFixtures.Round5();
        match.Debug.SeedHand(P0, (PieceType.Basic, basic), (PieceType.Fortress, fortress));
        HandInfoPanelView panel = match.World(P1).HandPanel();
        return Dump(panel.Opponents.Single(o => o.Player == P0));
    }

    private static string OthersView(MatchFlow match, PlayerId viewer)
    {
        ViewerWorld world = match.World(viewer);
        return Dump(world.Board()) + string.Concat(Enum.GetValues<TacticalLayer>().Select(l => Dump(world.Layer(l)))) + Dump(world.HandPanel().Opponents);
    }
}
