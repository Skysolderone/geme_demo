using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Batch;
using Siege.Presentation.Style;
using Siege.Presentation.Text;

namespace Siege.Presentation.Show;

/// <summary>
/// 对手回合摘要（settlement-show「对手回合摘要」）：由一次结算的节拍序列生成一行文案——谁、落了几枚什么子、提了几枚、势力增量与分项。
/// 数值全部取自节拍（落子 / 提子节拍的棋子、势力节拍里行动者那一条），不另算。
/// </summary>
public static class TurnSummary
{
    /// <summary>
    /// 例：「蓝方：落 3 子（堡垒子、普通子 ×2） · 提 2 子 · 势力 +8（领地 +3、军势 +5）」；没有落子为「蓝方：Pass」。
    /// 类型按首次出现的顺序列出，一枚不写"×1"；没有提子不写提子一段；行动者不在势力节拍里（势力无变化）不写势力一段。
    /// </summary>
    public static string Of(PlayerId actor, ImmutableArray<SettlementBeat> beats)
    {
        var parts = new List<string>();
        PlacedPiece[] placed = [.. beats.OfType<PlacementBeat>().SelectMany(b => b.Pieces)];
        if (placed.Length == 0)
        {
            parts.Add("Pass");
        }
        else
        {
            var types = new List<PieceType>();
            foreach (PlacedPiece piece in placed)
            {
                if (!types.Contains(piece.Type))
                {
                    types.Add(piece.Type);
                }
            }

            IEnumerable<string> names = types.Select(type =>
            {
                int count = placed.Count(p => p.Type == type);
                return count == 1 ? Labels.Piece(type) : $"{Labels.Piece(type)} ×{count}";
            });
            parts.Add($"落 {placed.Length} 子（{string.Join("、", names)}）");
        }

        int captured = beats.OfType<CaptureBeat>().Sum(b => b.Pieces.Length);
        if (captured > 0)
        {
            parts.Add($"提 {captured} 子");
        }

        if (beats.OfType<PowerBeat>().SelectMany(b => b.Changes).FirstOrDefault(c => c.Player == actor) is { } change)
        {
            parts.Add($"势力 {change.DeltaText}（{change.TerritoryText}、{change.GroupText}）");
        }

        return $"{FactionTable.For(actor).Name}：{string.Join(" · ", parts)}";
    }
}
