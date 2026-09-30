using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Presentation.Preview;
using Siege.Presentation.Show;
using Siege.Presentation.Text;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 落子与提子节拍的内容（turn-settlement-show 1.2；settlement-show-callouts 1.1 增飘字与合计）。
/// 飘字算例取自 settlement-show-callouts spec「落子飘字带算式」：堡垒子所在棋串基础 5、位置加值 2、倍增子 1 枚 → 军势 ⌊7 × 1.5⌋ = 10。
/// </summary>
public class 落子与提子节拍的内容Tests
{
    private static readonly GameBoard Before = Stones(("D3", P2));
    private static readonly GameBoard After = Stones(("D3", P2), ("F6", P1), ("E5", P1), ("E6", P1));
    private static readonly ImmutableArray<PowerReading> Power = [Reading(P1, 3, 1), Reading(P2, 1, 2)];

    [Fact]
    public void 落子顺序与放置顺序一致()
    {
        // 有结算记录：放置顺序 F6、E5、E6 → 落子节拍顺序 F6、E5、E6（不是字典序）。
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(Before, After, Power, Power, Order("F6", "E5", "E6"));
        PlacementBeat placement = Assert.IsType<PlacementBeat>(Assert.Single(beats));
        Assert.Equal(["F6", "E5", "E6"], placement.Pieces.Select(p => p.Coord.ToNotation()));
        Assert.All(placement.Pieces, p => Assert.Equal((PieceType.Basic, P1), (p.Type, p.Owner)));
    }

    [Fact]
    public void 没有结算记录时按坐标字典序()
    {
        // 没有结算记录（AI 小回合经运行器推进）：同样三枚按坐标字典序 E5、E6、F6（行优先，与 Coord.CompareTo 一致）。
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(Before, After, Power, Power, null);
        PlacementBeat placement = Assert.IsType<PlacementBeat>(Assert.Single(beats));
        Assert.Equal(["E5", "E6", "F6"], placement.Pieces.Select(p => p.Coord.ToNotation()));
    }

    [Fact]
    public void 多名玩家的棋子同时被提()
    {
        // 一次结算同时提掉 P2 在 D3 的棋子与 P3 在 G7、G8 的棋子 → 提子节拍按字典序列出 D3（P2）、G7（P3）、G8（P3），原归属取结算前盘面。
        GameBoard before = Stones(("D3", P2), ("G7", P3), ("G8", P3), ("A1", P1));
        GameBoard after = Stones(("A1", P1), ("C3", P1));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, Power, Power, Order("C3"));
        CaptureBeat capture = Assert.IsType<CaptureBeat>(beats[1]);
        Assert.Equal(["D3:P2", "G7:P3", "G8:P3"], capture.Pieces.Select(p => $"{p.Coord.ToNotation()}:P{p.Owner.Value}"));
    }

    [Fact]
    public void 落子飘字带算式()
    {
        // C3 落一枚堡垒子，结算后它所在棋串（C3、C4）基础 5、连珠 2、倍增 1 枚 → 军势 10。
        // 飘字 = 棋子类型名（Labels.Piece）+ " · " + 该棋串的军势短算式（GroupPowerView.ShortFormulaText，只列非零项），
        // 与势力层的完整算式 FormulaText 由同一个 GroupPowerView 生成、数值一致（裁决 1：不复制拼法，也不改势力层）。
        GameBoard before = Stones(("C4", P1));
        GameBoard after = Stones(("C4", P1));
        after.Place("C3", P1, PieceType.Fortress);
        GroupPower group = new(P1, [Coord.Parse("C3"), Coord.Parse("C4")], BaseTotal: 5, LineBonus: 2, SynergyBonus: 0, HighGroundBonus: 0,
            BannerBonus: 0, ChainBonus: 0, SentryBonus: 0, BoundaryBonus: 0, MultiplierCount: 1, Power: 10);
        SettlementSide afterSide = Side(after, Reading(P1, 0, 10, 1)) with { Groups = [group] };

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(Side(before, Reading(P1, 0, 3, 1)), afterSide, Order("C3"));
        PlacedPiece piece = Assert.Single(Assert.IsType<PlacementBeat>(beats[0]).Pieces);
        GroupPowerView view = GroupPowerView.From(group);
        Assert.Equal(view.ShortFormulaText, piece.Formula);
        Assert.Equal("堡垒子 · (5+2)×1.5 = 10", piece.CalloutText);
        Assert.Equal(Labels.Piece(PieceType.Fortress) + " · " + view.ShortFormulaText, piece.CalloutText);
        Assert.Equal("（基础 5 + 位置加值 2（连珠 2 / 协同 0 / 高地 0））× 1.5 = 10", view.FormulaText);   // 势力层算式一字不改
        Assert.EndsWith($"= {view.Power}", view.FormulaText);
        Assert.EndsWith($"= {view.Power}", view.ShortFormulaText);

        // 短算式只列非零项：无加值 → "5×1.5 = 7"；无倍增 → "5+2 = 7"；两者都无 → "5"。
        Assert.Equal("5×1.5 = 7", GroupPowerView.From(group with { LineBonus = 0, Power = 7 }).ShortFormulaText);
        Assert.Equal("5+2 = 7", GroupPowerView.From(group with { MultiplierCount = 0, Power = 7 }).ShortFormulaText);
        Assert.Equal("5", GroupPowerView.From(group with { LineBonus = 0, MultiplierCount = 0, Power = 5 }).ShortFormulaText);

        // 找不到棋串（快照里没有含该格的棋串）时只显示类型名。
        ImmutableArray<SettlementBeat> bare = SettlementBeats.Generate(Side(before, Reading(P1, 0, 3, 1)), Side(after, Reading(P1, 0, 10, 1)), Order("C3"));
        PlacedPiece unknown = Assert.Single(Assert.IsType<PlacementBeat>(bare[0]).Pieces);
        Assert.Null(unknown.Formula);
        Assert.Equal("堡垒子", unknown.CalloutText);
    }

    [Fact]
    public void 提子飘字与合计()
    {
        // 本次结算提走 3 枚：每枚飘字 "提"，合计文案 "提 3 子"。
        GameBoard before = Stones(("D3", P2), ("G7", P3), ("G8", P3), ("A1", P1));
        GameBoard after = Stones(("A1", P1), ("C3", P1));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, Power, Power, Order("C3"));
        CaptureBeat capture = Assert.IsType<CaptureBeat>(beats[1]);
        Assert.Equal(3, capture.Pieces.Length);
        Assert.Equal("提", CaptureBeat.CalloutText);
        Assert.Equal("提 3 子", capture.SummaryText);

        // 播放中：三枚各有一条 "提" 飘字，合计文案在提子节拍进行中给出。
        var timeline = new ShowTimeline([capture, new PowerBeat([new PowerChange(P1, 3, 4, 1, 1)])], ShowDuration.Normal);
        timeline.Advance(300);
        ShowMask mask = timeline.Mask();
        Assert.Equal("提 3 子", mask.CaptureSummary);
        Assert.Equal(["D3:Capture:提:500", "G7:Capture:提:500", "G8:Capture:提:500"], mask.Callouts.Select(c => $"{c.Coord.ToNotation()}:{c.Kind}:{c.Text}:{c.AgePermille}"));
        timeline.Advance(400);
        Assert.IsType<PowerBeat>(timeline.Current);
        Assert.Null(timeline.Mask().CaptureSummary);
    }

    [Fact]
    public void 无记录时按坐标序()
    {
        // AI 小回合在 D4、B2 落子且没有结算记录 → 落子节拍顺序 B2、D4。
        GameBoard before = Stones();
        GameBoard after = Stones(("D4", P1), ("B2", P1));
        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, Power, Power, null);
        PlacementBeat placement = Assert.IsType<PlacementBeat>(Assert.Single(beats));
        Assert.Equal(["B2", "D4"], placement.Pieces.Select(p => p.Coord.ToNotation()));
    }

    [Fact]
    public void 飘字寿命跨节拍()
    {
        // design.md D4：每枚棋子开始出现时飘字同时出现，0.6 秒内淡出，可跨到下一节拍。两枚落子（0.5 秒拍）后接势力节拍：
        // 第 1 枚 0 ms 出现、第 2 枚 250 ms 出现；300 ms 时两条都在（年龄 300 / 50），600 ms（势力拍内 100 ms）第 1 枚已过寿命、第 2 枚年龄 350。
        var placement = new PlacementBeat([new PlacedPiece(Coord.Parse("C3"), PieceType.Basic, P1, "1"), new PlacedPiece(Coord.Parse("C4"), PieceType.Fortress, P1)]);
        var timeline = new ShowTimeline([placement, new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)])], ShowDuration.Normal);
        Assert.Equal(600, Callout.LifetimeMs);

        timeline.Advance(100);
        Assert.Equal(["C3:Placement:普通子 · 1:166"], timeline.Mask().Callouts.Select(Line));
        timeline.Advance(200);
        Assert.Equal(["C3:Placement:普通子 · 1:500", "C4:Placement:堡垒子:83"], timeline.Mask().Callouts.Select(Line));
        timeline.Advance(300);
        Assert.IsType<PowerBeat>(timeline.Current);
        Assert.Equal(["C4:Placement:堡垒子:583"], timeline.Mask().Callouts.Select(Line));
        timeline.Advance(300);
        Assert.Empty(timeline.Mask().Callouts);

        // 压缩落子（本机确认）：全部飘字同时出现。
        var compressed = new ShowTimeline([placement with { Compressed = true }, new PowerBeat([new PowerChange(P1, 3, 5, 1, 1)])], ShowDuration.Normal);
        compressed.Advance(60);
        Assert.Equal([100, 100], compressed.Mask().Callouts.Select(c => c.AgePermille));

        // 零时长：没有任何飘字。
        var zero = new ShowTimeline([placement], ShowDuration.Zero);
        Assert.Empty(zero.Mask().Callouts);

        static string Line(Callout c) => $"{c.Coord.ToNotation()}:{c.Kind}:{c.Text}:{c.AgePermille}";
    }

    [Fact]
    public void 节拍内容只取自公开信息()
    {
        // 节拍类型的闭包里不得出现私有视图、暂放批次、征募面板等私有类型（information-visibility）。
        foreach (Type root in new[] { typeof(PlacementBeat), typeof(CaptureBeat), typeof(PowerBeat), typeof(RelicRevealBeat), typeof(BannerBeat), typeof(ShowMask) })
        {
            Assert.Empty(PresentationFixtures.PrivateLeaks(root));
        }
    }
}
