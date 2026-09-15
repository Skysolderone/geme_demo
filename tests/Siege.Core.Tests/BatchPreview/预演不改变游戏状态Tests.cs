using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Layers;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.BatchPreview;

/// <summary>规格：batch-preview —— Requirement: 预演不改变游戏状态</summary>
public class 预演不改变游戏状态Tests
{
    [Fact]
    public void 预演无副作用()
    {
        // implement 2.10：反复暂放、撤销、换位、替换并多次查看预演 → 正式盘面与全部玩家状态不变。
        // 两条腿（testing.md）：部署阶段逐项指纹不变；小回合结束后与"从未预演过"的孪生局 Serialize 逐字节一致，并做字段级投影比对。
        // 变异验证 M-S1：BatchPreviewBuilder 的将揭示改为在正式账本上 `relics.Reveal(projected, majorRound)` → 本测试红 1（指纹里揭示事件变化）。
        // 变异验证 M-S2：MatchFlow.ForecastInitiative 的势力改用 `Scoreboard.Recalculate(Board, roster, completed)` → 本测试红 1（势力榜版本号变化）。
        MatchFlow previewed = AiFixtures.Round5(relics: ("E5", RelicFixtures.Command()));
        MatchFlow twin = AiFixtures.Round5(relics: ("E5", RelicFixtures.Command()));
        previewed.Debug.SeedHand(P0, (PieceType.Basic, 5), (PieceType.Fortress, 2), (PieceType.Multiplier, 2));
        twin.Debug.SeedHand(P0, (PieceType.Basic, 5), (PieceType.Fortress, 2), (PieceType.Multiplier, 2));
        previewed.Pieces(P1, PieceType.Basic, "D5", "F5");
        twin.Pieces(P1, PieceType.Basic, "D5", "F5");

        StagedBatch batch = previewed.OpenDeploy();
        string fingerprint = Fingerprint(previewed);
        for (int i = 0; i < 20; i++)
        {
            Assert.Null(batch.Stage(TestMaps.At("E4"), PieceType.Basic));
            Assert.Null(batch.Stage(TestMaps.At("E6"), PieceType.Fortress));
            _ = previewed.PreviewCurrentBatch();
            Assert.Null(batch.Move(TestMaps.At("E6"), TestMaps.At("C5")));
            Assert.Null(batch.Replace(TestMaps.At("C5"), PieceType.Multiplier));
            ViewerWorld world = previewed.World(P0);
            _ = world.Preview(LibertyThresholds.Default);
            foreach (TacticalLayer layer in Enum.GetValues<TacticalLayer>())
            {
                _ = world.Layer(layer);
            }

            Assert.True(batch.Unstage(TestMaps.At("E4")));
            Assert.True(batch.Unstage(TestMaps.At("C5")));
            Assert.Equal(fingerprint, Fingerprint(previewed));
        }

        Assert.Null(batch.Stage(TestMaps.At("E4"), PieceType.Fortress));
        Core.Preview.BatchPreview last = previewed.PreviewCurrentBatch();
        Assert.Equal(batch.Placements, last.Placements);          // 结果对象与活对象两处都钉住
        Assert.Equal(batch.Count, last.DeployUsed);
        Assert.Equal(Fingerprint(previewed).Replace("E4:Fortress", string.Empty, StringComparison.Ordinal), fingerprint);

        Assert.True(previewed.Confirm().Confirmed);
        StagedBatch twinBatch = twin.OpenDeploy();
        Assert.Null(twinBatch.Stage(TestMaps.At("E4"), PieceType.Fortress));
        Assert.True(twin.Confirm().Confirmed);

        Assert.Equal(twin.Serialize(), previewed.Serialize());
        Assert.Equal(AiFixtures.PowerText(twin), AiFixtures.PowerText(previewed));
        Assert.Equal(AiFixtures.FlowText(twin), AiFixtures.FlowText(previewed));
        Assert.Equal(twin.Scoreboard.Version, previewed.Scoreboard.Version);
        Assert.Equal(twin.Relics.RevealEvents.Select(e => e.ToString()), previewed.Relics.RevealEvents.Select(e => e.ToString()));
        Assert.Equal(twin.Hands.RecruitStreamConsumed, previewed.Hands.RecruitStreamConsumed);
        Assert.True(twin.Serialize().Length > 1000);
    }
}
