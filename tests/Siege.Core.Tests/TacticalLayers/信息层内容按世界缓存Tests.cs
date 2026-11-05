using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>
/// 信息层内容按观察者世界缓存（power-perf）：结算演出期间势力排名栏与个人势力卡片每帧都要读势力层，
/// 大图（150×150）上每次现算要按座位过滤两万多格，逐帧重复算是卡顿的根因。<see cref="ViewerWorld"/> 是不可变快照，
/// 同一实例上同一层同一读法的内容只算一次。
/// </summary>
public class 信息层内容按世界缓存Tests
{
    [Fact]
    public void 同一世界重复读同一层只算一次()
    {
        MatchFlow match = AiFixtures.Round5().Pieces(P0, PieceType.Basic, "F5").Pieces(P1, PieceType.Basic, "H5");
        ViewerWorld world = match.World(P2);

        Assert.Same(world.Layer(TacticalLayer.Power), world.Layer(TacticalLayer.Power));
        Assert.Same(world.Layer(TacticalLayer.Board, BoardReading.Groups), world.Layer(TacticalLayer.Board, BoardReading.Groups));

        // 盘面层两种读法各算各的；非盘面层忽略读法，两种读法取到同一份。
        Assert.NotSame(world.Layer(TacticalLayer.Board, BoardReading.Ownership), world.Layer(TacticalLayer.Board, BoardReading.Groups));
        Assert.Same(world.Layer(TacticalLayer.Power, BoardReading.Ownership), world.Layer(TacticalLayer.Power, BoardReading.Groups));
    }

    [Fact]
    public void 显式阈值不走缓存()
    {
        MatchFlow match = AiFixtures.Round5().Pieces(P0, PieceType.Basic, "F5");
        ViewerWorld world = match.World(P0);
        var loose = new LibertyThresholds(danger: 4, urgent: 1);

        var cached = (LibertyLayerContent)world.Layer(TacticalLayer.Board, BoardReading.Groups);
        var custom = (LibertyLayerContent)world.Layer(TacticalLayer.Board, BoardReading.Groups, loose);
        Assert.NotSame(cached, custom);
        Assert.Same(loose, custom.Thresholds);
        Assert.Same(cached, world.Layer(TacticalLayer.Board, BoardReading.Groups));
    }
}
