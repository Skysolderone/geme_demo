using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.TerrainEditing;

/// <summary>
/// 规格：terrain-edit —— Requirement: 盘面同形禁则纳入设施；
/// capture-resolution —— Requirement: 盘面同形禁则（设施差异不构成同形）。
/// tasks 2.5。
/// </summary>
public class 同形与存档纳入设施Tests
{
    private static GameBoard Board() => TestMaps.Blank(
        TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater), ("F4", Surface.Forest)]), size: 9);

    [Fact]
    public void 棋子分布相同但多一道栅栏即不同形()
    {
        // capture-resolution「设施差异不构成同形」：棋子逐格一致，其间新增一道栅栏 / 一座桥 / 一处被烧的林地 → 两个盘面不同。
        GameBoard plain = Board();
        plain.Place(TestMaps.At("C4"), TestMaps.P0, PieceType.Artisan);

        GameBoard fenced = Board();
        fenced.Place(TestMaps.At("C4"), TestMaps.P0, PieceType.Artisan);
        fenced.ApplyTerrainEdits([TerrainEdit.Fence(TestMaps.At("C4"), TestMaps.At("C5"))]);

        GameBoard bridged = Board();
        bridged.Place(TestMaps.At("C4"), TestMaps.P0, PieceType.Artisan);
        bridged.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4"))]);

        GameBoard burned = Board();
        burned.Place(TestMaps.At("C4"), TestMaps.P0, PieceType.Artisan);
        burned.ApplyTerrainEdits([TerrainEdit.Burn(TestMaps.At("F4"))]);

        string[] texts = [plain.Serialize(), fenced.Serialize(), bridged.Serialize(), burned.Serialize()];
        Assert.Equal(4, texts.Distinct(StringComparer.Ordinal).Count());

        // 同形禁则真的读到了差异：把"无设施"的盘面记进历史后，带设施的盘面不触发同形。
        var history = new BoardHistory();
        history.Record(plain.Serialize());
        Assert.NotNull(history.FindDuplicate(plain.Serialize()));
        Assert.Null(history.FindDuplicate(fenced.Serialize()));
        Assert.Null(history.FindDuplicate(bridged.Serialize()));
        Assert.Null(history.FindDuplicate(burned.Serialize()));
    }

    [Fact]
    public void 无改造时序列化与改造上线之前逐字节相同()
    {
        // 改造段是增量且只在非空时写出：没有改造的盘面输出不含 '|'，
        // 旧存档与旧的已提交盘面集合因此天然按"无改造"读入（R-6），既有字面量期望一条都不用改。
        GameBoard board = Board();
        board.Place(TestMaps.At("C4"), TestMaps.P0, PieceType.Basic);

        Assert.DoesNotContain('|', board.Serialize());

        // 地图预置的桥与栅栏也不写进改造段：预置设施一局内恒定，写不写对同形比对等价。
        GameBoard preset = TestMaps.Blank(
            TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater)], bridges: ["D4"], fences: [("G6", "H6")]), size: 9);
        preset.Place(TestMaps.At("C4"), TestMaps.P0, PieceType.Basic);
        Assert.DoesNotContain('|', preset.Serialize());
        Assert.Equal(board.Serialize(), preset.Serialize());
    }

    [Fact]
    public void 改造段的排序是规范形()
    {
        // 改造不可逆且同一目标只能改一次 → "已应用集合"与"当前地形"一一对应；排序后即规范形，
        // 因此同一套改造以不同顺序完成时同形比对结果一致。
        TerrainEdit[] edits =
        [
            TerrainEdit.Bridge(TestMaps.At("D4")),
            TerrainEdit.Burn(TestMaps.At("F4")),
            TerrainEdit.Fence(TestMaps.At("C4"), TestMaps.At("C5")),
        ];

        GameBoard forward = Board();
        forward.ApplyTerrainEdits(edits);
        GameBoard split = Board();
        split.ApplyTerrainEdits([edits[2]]);
        split.ApplyTerrainEdits([edits[0], edits[1]]);

        Assert.Equal(forward.Serialize(), split.Serialize());
        Assert.EndsWith("|B:D4,F:C4-C5,X:F4", forward.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void 盘面序列化往返保留改造()
    {
        // 改造经盘面序列化持久化：Restore 先读改造段再写棋子——否则本局架过桥的格会被当成未架桥深水而拒绝落子。
        GameBoard board = Board();
        board.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4")), TerrainEdit.Burn(TestMaps.At("F4"))]);
        board.Place(TestMaps.At("D4"), TestMaps.P0, PieceType.Basic);   // 站在新桥上
        board.Place(TestMaps.At("F4"), TestMaps.P1, PieceType.Artisan); // 站在被烧过的地上
        string text = board.Serialize();

        GameBoard back = GameBoard.RestoreUnvalidated(board.BaseMap, text);

        Assert.Equal(text, back.Serialize());
        Assert.True(back.Map.HasBridge(TestMaps.At("D4")));
        Assert.Equal(Surface.Grass, back.Map.SurfaceAt(TestMaps.At("F4")));
        Assert.Equal(PieceType.Basic, back[TestMaps.At("D4")].Occupant!.Value.Type);
        Assert.Equal(2, back.TerrainEdits.Length);

        // 旧存档（无改造段）按"无改造"回填，不抛：另起一份棋子没落在被改造格上的盘面。
        GameBoard elsewhere = Board();
        elsewhere.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4"))]);
        elsewhere.Place(TestMaps.At("C4"), TestMaps.P0, PieceType.Basic);
        string stripped = elsewhere.Serialize();
        stripped = stripped[..stripped.IndexOf('|', StringComparison.Ordinal)];

        GameBoard legacy = GameBoard.RestoreUnvalidated(elsewhere.BaseMap, stripped);
        Assert.Empty(legacy.TerrainEdits);
        Assert.False(legacy.Map.HasBridge(TestMaps.At("D4")));
        Assert.Equal(PieceType.Basic, legacy[TestMaps.At("C4")].Occupant!.Value.Type);

        // 损坏的改造段响亮失败，不静默丢弃。
        Assert.Throws<FormatException>(() => GameBoard.RestoreUnvalidated(board.BaseMap, text + ",B:B2"));
    }

    [Fact]
    public void 对局存档往返保留改造与匠人权重()
    {
        // 段 A 检查第 5 项：ArtisanWeight 进 MatchPublicView（R-2 照对局配置的口径：开局固定、始终公开、插旗阶段即可读）。
        // 这里一并钉住"地形改造随对局存档往返"——地图由调用方提供，改造只能靠盘面序列化带过去。
        MatchFlow match = MatchFixtures.Started(options: MatchOptions.Immediate with { ArtisanWeight = 18 }).AtRound(5);
        TerrainEdit fence = TerrainEdit.Fence(TestMaps.At("E5"), TestMaps.At("E6"));
        match.Board.ApplyTerrainEdits([fence]);
        string json = match.Serialize();

        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Relics.Generation, json);

        Assert.True(restored.Board.Map.HasFence(TestMaps.At("E5"), TestMaps.At("E6")));
        Assert.Equal([fence], restored.Board.TerrainEdits);
        Assert.Equal(match.Board.Serialize(), restored.Board.Serialize());
        Assert.Equal(json, restored.Serialize());
        Assert.False(restored.ArtisanWeightBackfilled);

        // 活对象与公开视图两处都钉（testing.md：结果对象与活对象各读一次）。
        Assert.Equal(18, restored.ArtisanWeight);
        Assert.Equal(18, restored.Publish().ArtisanWeight);
        Assert.True(restored.Publish().Board.Map.HasFence(TestMaps.At("E5"), TestMaps.At("E6")));

        // 旧存档（无 ArtisanWeight 字段）回填 10，公开视图同样读到 10。
        string legacy = System.Text.RegularExpressions.Regex.Replace(json, "\\s*\"ArtisanWeight\": 18,", string.Empty);
        Assert.DoesNotContain("ArtisanWeight", legacy, StringComparison.Ordinal);
        MatchFlow fromLegacy = MatchFlow.RestoreUnvalidated(match.Board.BaseMap, match.Relics.Generation, legacy);
        Assert.True(fromLegacy.ArtisanWeightBackfilled);
        Assert.Equal(MatchOptions.DefaultArtisanWeight, fromLegacy.ArtisanWeight);
        Assert.Equal(MatchOptions.DefaultArtisanWeight, fromLegacy.Publish().ArtisanWeight);
    }
}
