using Siege.Core.Board;

namespace Siege.Core.Tests.BoardTopology;

/// <summary>规格：board-topology —— Requirement: 盘面状态可序列化且确定性</summary>
public class 盘面序列化Tests
{
    [Fact]
    public void 同位置不同类型不等价()
    {
        // 序列化保留棋子类型（存档、日志、结算核对要用）；同形比对另走比对键，见「同形比对键忽略棋子类型」。
        GameBoard basic = TestMaps.Blank(size: 5).Place("C3", TestMaps.P0, PieceType.Basic);
        GameBoard fortress = TestMaps.Blank(size: 5).Place("C3", TestMaps.P0, PieceType.Fortress);

        Assert.NotEqual(basic.Serialize(), fortress.Serialize());
    }

    [Fact]
    public void 同形比对键忽略棋子类型()
    {
        // superko-occupancy：两份盘面占用者、设施与地表相同，只有 C3 的类型不同 → 序列化不等，比对键相等。
        // 另钉住比对键不丢占用者与设施：换玩家、多一处改造都必须让比对键不同。
        // 变异验证（superko-occupancy 1.2）：M-K1 SuperkoKey 保留类型 → 红（本测试）。
        TerrainData terrain = TestMaps.Terrain(surfaces: [("D4", Surface.DeepWater)]);
        GameBoard basic = TestMaps.Blank(terrain, size: 7).Place("C3", TestMaps.P0, PieceType.Basic).Place("E5", TestMaps.P1, PieceType.Line);
        GameBoard fortress = TestMaps.Blank(terrain, size: 7).Place("C3", TestMaps.P0, PieceType.Fortress).Place("E5", TestMaps.P1, PieceType.Artisan);
        basic.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4"))]);
        fortress.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4"))]);

        Assert.NotEqual(basic.Serialize(), fortress.Serialize());
        Assert.Equal(GameBoard.SuperkoKey(basic.Serialize()), GameBoard.SuperkoKey(fortress.Serialize()));

        GameBoard otherOwner = TestMaps.Blank(terrain, size: 7).Place("C3", TestMaps.P1, PieceType.Basic).Place("E5", TestMaps.P1, PieceType.Line);
        otherOwner.ApplyTerrainEdits([TerrainEdit.Bridge(TestMaps.At("D4"))]);
        Assert.NotEqual(GameBoard.SuperkoKey(basic.Serialize()), GameBoard.SuperkoKey(otherOwner.Serialize()));

        GameBoard noBridge = TestMaps.Blank(terrain, size: 7).Place("C3", TestMaps.P0, PieceType.Basic).Place("E5", TestMaps.P1, PieceType.Line);
        Assert.NotEqual(GameBoard.SuperkoKey(basic.Serialize()), GameBoard.SuperkoKey(noBridge.Serialize()));

        // 比对键是序列化的纯函数：同一盘面重复导出逐字节一致，且序列化本身不因导出比对键而改变。
        string before = basic.Serialize();
        Assert.Equal(GameBoard.SuperkoKey(before), GameBoard.SuperkoKey(basic.Serialize()));
        Assert.Equal(before, basic.Serialize());
    }

    [Fact]
    public void 六种类型的盘面码两两不同且往返保留类型()
    {
        // 设计文档 §6.2 + artisan-terrain-edit 段 A：盘面序列化是存档的表示（同形比对键由它投影、不含类型），六种棋子类型 MUST 各有一个码。
        // 漏码会在匠人落子后存档时抛 FormatException（响亮），撞码则是**静默**的：读回来变成另一种棋子、两个不同盘面还会被判成同形。
        // 变异验证 M-C2（检查阶段）：GameBoard 的 Artisan 码由 'A' 改成 'S'（与协同子撞码）→ 补本测试前全绿 826（缺口），补后本测试红。
        PieceType[] all = Enum.GetValues<PieceType>();
        Assert.Equal(6, all.Length);
        var texts = new List<string>();
        foreach (PieceType type in all)
        {
            GameBoard board = TestMaps.Blank(size: 5).Place("C3", TestMaps.P0, type);
            string text = board.Serialize();
            texts.Add(text);

            GameBoard back = GameBoard.RestoreUnvalidated(board.Map, text);
            Assert.Equal(type, back[TestMaps.At("C3")].Occupant!.Value.Type);
            Assert.Equal(text, back.Serialize());
        }

        // 两两不同：任何两种类型撞码都会让这里少一个元素（同时意味着两种盘面同形）。
        Assert.Equal(all.Length, texts.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void 非盘面信息不影响序列化()
    {
        // 两份盘面的格子占用完全一致；手牌与信物控制不属于序列化内容，因此结果必须相等。
        // 这里用"地图的信物标注不同但占用相同"来代表非盘面差异。
        // v3 基准图 13×13，B2 是出生区信物格；空盘取同尺寸。
        GameBoard plain = TestMaps.Blank(size: 13).Place("B2", TestMaps.P0, PieceType.Line);
        GameBoard withRelics = GameBoard.LoadUnvalidated(Siege.Core.Board.Maps.FourPlayerBaseMap.Create());
        withRelics.Place(TestMaps.At("B2"), TestMaps.P0, PieceType.Line);

        Assert.Equal(plain.Serialize(), withRelics.Serialize());
    }

    [Fact]
    public void 重复序列化逐字节一致()
    {
        GameBoard board = TestMaps.Blank(size: 7)
            .Place("B2", TestMaps.P0, PieceType.Synergy)
            .Place("F6", TestMaps.P1, PieceType.Multiplier);

        string first = board.Serialize();
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(first, board.Serialize());
        }
    }

    [Fact]
    public void 落子顺序不影响序列化()
    {
        GameBoard a = TestMaps.Blank(size: 7)
            .Place("B2", TestMaps.P0)
            .Place("F6", TestMaps.P1)
            .Place("D4", TestMaps.P0);
        GameBoard b = TestMaps.Blank(size: 7)
            .Place("D4", TestMaps.P0)
            .Place("F6", TestMaps.P1)
            .Place("B2", TestMaps.P0);

        Assert.Equal(a.Serialize(), b.Serialize());
    }

    [Fact]
    public void 不同玩家不等价()
    {
        GameBoard a = TestMaps.Blank(size: 5).Place("C3", TestMaps.P0);
        GameBoard b = TestMaps.Blank(size: 5).Place("C3", TestMaps.P1);

        Assert.NotEqual(a.Serialize(), b.Serialize());
    }

    [Fact]
    public void 玩家编号超出0到15时序列化拒绝()
    {
        // 每格占用者用一位十六进制表示，编号 16 会溢出成两位并破坏定长格式，必须显式拒绝
        GameBoard ok = TestMaps.Blank(size: 5).Place("C3", new PlayerId(15));
        Assert.Contains("FB", ok.Serialize(), StringComparison.Ordinal);

        GameBoard overflow = TestMaps.Blank(size: 5).Place("C3", new PlayerId(16));
        SiegeRuleException ex = Assert.Throws<SiegeRuleException>(() => overflow.Serialize());
        Assert.Contains("0–15", ex.Message, StringComparison.Ordinal);
        Assert.Contains("16", ex.Message, StringComparison.Ordinal);
    }
}
