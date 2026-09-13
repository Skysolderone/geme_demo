using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>
/// 批次内的一枚暂放：落点 + 棋子类型。批次是有序序列，序列下标即"批次内落子顺序"。
/// 落子顺序不影响任何规则判定（合法性以整批最终状态判定），只随提子记录保留，
/// 为将来的击杀归功预留（裁决记录 3）。
/// </summary>
public readonly record struct Placement(Coord Coord, PieceType Type)
{
    public override string ToString() => $"{Coord.ToNotation()}:{Type}";
}

/// <summary>被提走的一枚棋子：坐标、原所有者与类型。足以完整重建被提棋串。</summary>
public readonly record struct CapturedStone(Coord Coord, PlayerId Owner, PieceType Type)
{
    public override string ToString() => $"{Coord.ToNotation()}:{Owner}:{Type}";
}

/// <summary>
/// 提子记录：结算第 3 步的产物，供 AI 与遥测统计提子规模。
/// <see cref="Placements"/> 保持批次内落子顺序（下标即顺序）。
/// </summary>
public sealed record CaptureRecord(
    int Sequence,
    PlayerId Capturer,
    ImmutableArray<Placement> Placements,
    ImmutableArray<CapturedStone> Captured);
