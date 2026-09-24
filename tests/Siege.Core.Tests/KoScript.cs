using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests;

/// <summary>
/// 在 <see cref="BatchFixtures.KoBoard"/>（初始 P1 持双劫）上的 20 步脚本，每步结算后盘面的同形比对键互不相同、也不等于初始盘面。
/// 状态记作 (劫 A 持有者, 劫 B 持有者) + 远离劫争的填子格：
/// S1(0,1)+A1 S2(1,1)+B1 S3(0,1)+C1 S4(1,1)+D1 S5(0,1)+E1 S6(1,1)+F1 S7(0,1)+G1 S8(1,1)+H1 S9(0,1)+J1
/// S10(0,0)+A9 S11(0,1)+B9 S12(1,1)+C9 S13(0,0)+D9 S14(1,0)+E9 S15(0,0)+F9 S16(1,0)+A7 S17(0,0)+B7
/// S18(0,1) S19(1,1) S20(1,0)。
/// </summary>
/// <remarks>
/// superko-occupancy 起同形比对不看棋子类型，双劫只有 4 种占用态：旧脚本靠"每轮换类型"造出 20 个互异盘面，新规则下第 3 步即同形。
/// 现改为前 17 步每步另填一子（第 1 行、第 9 行 A–F、A7 / B7，每子都有空的外侧气，不引发提子），
/// 占用集合单调增长，盘面必然互异；第 18–20 步只翻劫，把 4 种占用态中剩下的 3 种走完。
/// 第 13 步是一次双劫同提 + 填子的 3 枚批次。
/// 第 12 步之后 P0 在劫 A 提劫、P1 再提回 → 与第 12 次提交同形；第 20 步之后 P0 在劫 A 提劫 → 与第 17 次提交同形。
/// </remarks>
internal static class KoScript
{
    internal static readonly (PlayerId Player, string Kos, PieceType Type, string? Fill)[] Steps =
    [
        (TestMaps.P0, "A", PieceType.Basic, "A1"),
        (TestMaps.P1, "A", PieceType.Fortress, "B1"),
        (TestMaps.P0, "A", PieceType.Fortress, "C1"),
        (TestMaps.P1, "A", PieceType.Line, "D1"),
        (TestMaps.P0, "A", PieceType.Line, "E1"),
        (TestMaps.P1, "A", PieceType.Multiplier, "F1"),
        (TestMaps.P0, "A", PieceType.Multiplier, "G1"),
        (TestMaps.P1, "A", PieceType.Synergy, "H1"),
        (TestMaps.P0, "A", PieceType.Synergy, "J1"),
        (TestMaps.P0, "B", PieceType.Basic, "A9"),
        (TestMaps.P1, "B", PieceType.Fortress, "B9"),
        (TestMaps.P1, "A", PieceType.Basic, "C9"),
        (TestMaps.P0, "AB", PieceType.Basic, "D9"),
        (TestMaps.P1, "A", PieceType.Fortress, "E9"),
        (TestMaps.P0, "A", PieceType.Fortress, "F9"),
        (TestMaps.P1, "A", PieceType.Line, "A7"),
        (TestMaps.P0, "A", PieceType.Line, "B7"),
        (TestMaps.P1, "B", PieceType.Line, null),
        (TestMaps.P1, "A", PieceType.Synergy, null),
        (TestMaps.P0, "B", PieceType.Basic, null),
    ];

    /// <summary>执行前 <paramref name="upTo"/> 步，每步都必须被接受；返回每步结算后的盘面序列化。</summary>
    internal static List<string> Play(SettlementDriver driver, int upTo)
    {
        var boards = new List<string>();
        for (int i = 0; i < upTo; i++)
        {
            (PlayerId player, string kos, PieceType type, string? fill) = Steps[i];
            List<Placement> placements = [.. kos.Select(ko => new Placement(BatchFixtures.KoPoint(ko, player), type))];
            if (fill is not null)
            {
                placements.Add(new Placement(TestMaps.At(fill), type));
            }

            SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(driver.Board, player), placements);
            Assert.True(outcome.Confirmed, $"脚本第 {i + 1} 步被拒绝：{outcome.Failure?.Message}");
            Assert.Equal(i + 1, outcome.CaptureRecord!.Sequence);
            Assert.Equal(kos.Length, outcome.CaptureRecord.Captured.Length);
            boards.Add(driver.Board.Serialize());
        }

        return boards;
    }
}
