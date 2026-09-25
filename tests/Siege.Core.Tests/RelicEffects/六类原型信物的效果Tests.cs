using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Relics;

namespace Siege.Core.Tests.RelicEffectsSpec;

/// <summary>规格：relic-effects —— Requirement: 六类原型信物的效果</summary>
public class 六类原型信物的效果Tests
{
    [Fact]
    public void 军令提高部署上限()
    {
        // 设计文档 §8.1 / growth-pass-1 relic-effects 规格：第 2 大回合控制 1 枚普通军令，该阶段基础部署上限 3 → 下一次快照 4。
        // growth-pass-1 改写：原用第 1 大回合，按规格改为第 2 大回合（同在第一阶段，期望值不变）。
        // 变异验证 M-E2：BuildSnapshot 把 Command 分支并入 Depot → 红 5，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("E7", TestMaps.P0);
        ledger.Settle(board, 2);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 2);

        Assert.Equal(4, snapshot.DeployLimit);
        Assert.Equal((5, 3, 5), (snapshot.RevealCount, snapshot.FreePickCount, snapshot.TypeSlots));
    }

    [Fact]
    public void 军令加成叠在分阶段基础值上()
    {
        // growth-pass-1 relic-effects 规格：第 8 大回合控制 1 枚普通军令，该阶段基础部署上限 5 → 下一次快照 6。
        // 对照：同一盘面第 3 大回合（基础 3）为 4——加成 +1 不随阶段变，变的只是基础值。
        // 变异验证 M-GP3（BuildSnapshot 基础值改回第一阶段 `BaseDeployLimitFor(1)`，军令照常累加）→ 全套红 47，含本测试（4 ≠ 6）；M-GP2（阶段表写死 3）→ 红 19，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Command()));
        board.Place("E7", TestMaps.P0);
        ledger.Settle(board, 8);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 8);

        Assert.Equal(6, snapshot.DeployLimit);
        Assert.Equal(8, snapshot.MajorRound);
        Assert.Equal(4, ledger.SnapshotFor(TestMaps.P0, board, 0, 3).DeployLimit);
        Assert.Equal(new DeployLimitPeak(6, 8, TestMaps.P0), ledger.DeployLimitPeak);
    }

    [Fact]
    public void 高阶结构信物()
    {
        // 控制 1 枚效果 +2 的兵站，基础手牌类型槽 5 → 7。
        // 变异验证 M-E3：BuildSnapshot 各分支改 `+= 1` → 红 4，含本测试。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Depot(2)));
        board.Place("E6", TestMaps.P0);
        ledger.Settle(board, 1);

        Assert.Equal(7, ledger.SnapshotFor(TestMaps.P0, board, 0, 1).TypeSlots);
    }

    [Theory]
    [InlineData(RelicType.Prospecting, 6, 3, 5, 3)]
    [InlineData(RelicType.Conscription, 5, 4, 5, 3)]
    [InlineData(RelicType.Depot, 5, 3, 6, 3)]
    [InlineData(RelicType.Command, 5, 3, 5, 4)]
    [InlineData(RelicType.Vanguard, 5, 3, 5, 3)]
    public void 效果映射(RelicType type, int reveal, int freePick, int slots, int deploy)
    {
        // 探勘→展示数、征召→选取数、兵站→类型槽、军令→部署上限；先锋不进快照（另有先手修正读取）。
        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", new RelicContent(type, 1)));
        board.Place("E7", TestMaps.P0);
        ledger.Settle(board, 1);

        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);

        Assert.Equal((reveal, freePick, slots, deploy), (snapshot.RevealCount, snapshot.FreePickCount, snapshot.TypeSlots, snapshot.DeployLimit));
        Assert.Empty(snapshot.EmblemCounts);
        Assert.Equal(type == RelicType.Vanguard ? 1 : 0, ledger.ReadInitiativeBonuses(board)[TestMaps.P0]);
    }

    [Fact]
    public void 流派徽记绑定棋子类型并随内容揭示()
    {
        // 徽记 MUST 绑定一种具体棋子类型；绑定在生成时确定、随内容一同公开；效果是提高该类型的征募权重（§9.1：40 × (1 + 0.75) = 70 → 整数形式 40 × 7 = 280 / 4）。
        Assert.Throws<ArgumentException>(() => new RelicContent(RelicType.SchoolEmblem, 1));
        Assert.Throws<ArgumentException>(() => new RelicContent(RelicType.Command, 1, PieceType.Basic));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelicContent(RelicType.Command, 3));

        (GameBoard board, RelicLedger ledger) = RelicFixtures.Scene(("E7", RelicFixtures.Emblem(PieceType.Basic)));
        board.Place("E6", TestMaps.P0);
        ledger.Settle(board, 1);

        RelicPublicState state = ledger.PublicStateOf(TestMaps.At("E7"));
        Assert.Equal(PieceType.Basic, state.Content!.Value.EmblemPiece);
        EffectSnapshot snapshot = ledger.SnapshotFor(TestMaps.P0, board, 0, 1);
        Assert.Equal(1, snapshot.EmblemCountOf(PieceType.Basic));
        Assert.Equal(7, snapshot.EmblemWeightNumerator(PieceType.Basic));
        Assert.Equal(280, snapshot.AdjustedWeight(PieceType.Basic, 40));
        Assert.Equal(80, snapshot.AdjustedWeight(PieceType.Fortress, 20));
        Assert.Equal((5, 3, 5, 3), (snapshot.RevealCount, snapshot.FreePickCount, snapshot.TypeSlots, snapshot.DeployLimit));
    }

    [Fact]
    public void 新四类信物没有高阶版()
    {
        // more-pieces-relics MODIFIED（D7）：连营、犄角、驿站、工坊只有基础版——构造强度 +2 即响亮失败；
        // 用任意种子在任意地图上生成，每一枚新四类的强度都为 +1。v2、各内置图、种子 0–999，只跑信物生成、不跑对局。
        foreach (RelicType fresh in new[] { RelicType.Encampment, RelicType.Pincer, RelicType.Relay, RelicType.Workshop })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RelicContent(fresh, 2));
            Assert.Equal(1, new RelicContent(fresh, 1).Magnitude);
            Assert.False(RelicContent.HasAdvancedTier(fresh));
        }

        foreach (RelicType original in new[] { RelicType.Prospecting, RelicType.Conscription, RelicType.Depot, RelicType.Command, RelicType.Vanguard, RelicType.SchoolEmblem })
        {
            Assert.True(RelicContent.HasAdvancedTier(original));
        }

        int fresh4 = 0;
        foreach (string mapId in MapCatalog.BuiltinIds)
        {
            MapData map = MapCatalog.Resolve(mapId);
            for (ulong seed = 0; seed < 1000; seed++)
            {
                foreach (RelicPlacement p in RelicGenerator.Generate(map, new GameSeed(seed), ContentSet.V2).Placements)
                {
                    if (RelicWeights.IndexOf(p.Content.Type) >= 6)
                    {
                        fresh4++;
                        Assert.Equal(1, p.Content.Magnitude);
                    }
                }
            }
        }

        // 样本口径下界：新四类确实被生成过，不是"一枚都没有所以恒为 +1"。
        Assert.True(fresh4 > 1000, $"新四类只生成了 {fresh4} 枚");
    }
}
