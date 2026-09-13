using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests;

/// <summary>
/// 在 <see cref="BatchFixtures.KoBoard"/>（初始 P1 持双劫）上的 20 步脚本，每步结算后盘面互不相同、也不等于初始盘面。
/// 状态记作 (劫 A 持有者+类型, 劫 B 持有者+类型)：
/// S1(0B,1B) S2(1F,1B) S3(0F,1B) S4(1L,1B) S5(0L,1B) S6(1M,1B) S7(0M,1B) S8(1S,1B) S9(0S,1B) S10(0S,0B)
/// S11(0S,1F) S12(1B,1F) S13(0B,0B) S14(1F,0B) S15(0F,0B) S16(1L,0B) S17(0L,0B) S18(0L,1L) S19(0L,0S) S20(1B,0S)。
/// 第 13 步是一次双劫同提的 2 枚批次，用来打破"每步只翻一个劫"的奇偶性；
/// S20 之后 P1 在劫 B 用堡垒子提劫会得到 (1B,1F) = S12。
/// </summary>
internal static class KoScript
{
    internal static readonly (PlayerId Player, string Kos, PieceType Type)[] Steps =
    [
        (TestMaps.P0, "A", PieceType.Basic),
        (TestMaps.P1, "A", PieceType.Fortress),
        (TestMaps.P0, "A", PieceType.Fortress),
        (TestMaps.P1, "A", PieceType.Line),
        (TestMaps.P0, "A", PieceType.Line),
        (TestMaps.P1, "A", PieceType.Multiplier),
        (TestMaps.P0, "A", PieceType.Multiplier),
        (TestMaps.P1, "A", PieceType.Synergy),
        (TestMaps.P0, "A", PieceType.Synergy),
        (TestMaps.P0, "B", PieceType.Basic),
        (TestMaps.P1, "B", PieceType.Fortress),
        (TestMaps.P1, "A", PieceType.Basic),
        (TestMaps.P0, "AB", PieceType.Basic),
        (TestMaps.P1, "A", PieceType.Fortress),
        (TestMaps.P0, "A", PieceType.Fortress),
        (TestMaps.P1, "A", PieceType.Line),
        (TestMaps.P0, "A", PieceType.Line),
        (TestMaps.P1, "B", PieceType.Line),
        (TestMaps.P0, "B", PieceType.Synergy),
        (TestMaps.P1, "A", PieceType.Basic),
    ];

    /// <summary>执行前 <paramref name="upTo"/> 步，每步都必须被接受；返回每步结算后的盘面序列化。</summary>
    internal static List<string> Play(SettlementDriver driver, int upTo)
    {
        var boards = new List<string>();
        for (int i = 0; i < upTo; i++)
        {
            (PlayerId player, string kos, PieceType type) = Steps[i];
            Placement[] placements = [.. kos.Select(ko => new Placement(BatchFixtures.KoPoint(ko, player), type))];
            SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(driver.Board, player), placements);
            Assert.True(outcome.Confirmed, $"脚本第 {i + 1} 步被拒绝：{outcome.Failure?.Message}");
            Assert.Equal(i + 1, outcome.CaptureRecord!.Sequence);
            boards.Add(driver.Board.Serialize());
        }

        return boards;
    }
}
