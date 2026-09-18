using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>
/// 批次内的一枚暂放：落点 + 棋子类型 + 可选的地形改造目标。批次是有序序列，序列下标即"批次内落子顺序"。
/// 落子顺序不影响任何规则判定（合法性以整批最终状态判定，全部改造同时生效），只随提子记录保留，
/// 为将来的击杀归功预留（裁决记录 3）。
/// </summary>
/// <remarks>
/// <para><see cref="Edit"/> 只有匠人能带（terrain-edit「匠人落子即改造」），且与该匠人<b>共用同一枚部署额度</b>、不另计（batch-deployment）。
/// 改造可选：<c>null</c> 即按普通棋子处理。</para>
/// <para><see cref="ToString"/> 含改造——AI 候选批次去重的键（<c>CandidateBatch.Key</c>）就是拼它，
/// 漏了会让"同一落点带 / 不带改造"的两个候选被静默去重成一个。</para>
/// </remarks>
public readonly record struct Placement(Coord Coord, PieceType Type, TerrainEdit? Edit = null)
{
    public override string ToString() =>
        Edit is { } edit ? $"{Coord.ToNotation()}:{Type}+{edit}" : $"{Coord.ToNotation()}:{Type}";
}

/// <summary>被提走的一枚棋子：坐标、原所有者与类型。足以完整重建被提棋串。</summary>
public readonly record struct CapturedStone(Coord Coord, PlayerId Owner, PieceType Type)
{
    public override string ToString() => $"{Coord.ToNotation()}:{Owner}:{Type}";
}

/// <summary>
/// 本批次应用的一次地形改造，随结算记录留痕（match-telemetry 第 4 条）。
/// </summary>
/// <param name="Edit">动作与目标。</param>
/// <param name="ArtisanCoord">携带该改造的匠人落点。<b>只供日志与分析</b>——设施无归属，公开视图与盘面 MUST NOT 显示改造者（R-3）。</param>
/// <param name="CausedCapture">
/// 该次改造是否<b>直接导致提子</b>。口径（design 未定义，本实现取定并写入 implement.md）：
/// 在同一批次里把这一条改造去掉后重算，无气敌串集合<b>严格变小</b>即为 <c>true</c>。
/// 因此两道栅栏合围一条敌串时两条都记 <c>true</c>（各自都是必要的），顺带架的桥记 <c>false</c>。
/// </param>
public readonly record struct AppliedTerrainEdit(TerrainEdit Edit, Coord ArtisanCoord, bool CausedCapture)
{
    public override string ToString() => $"{Edit}@{ArtisanCoord.ToNotation()}{(CausedCapture ? "!" : string.Empty)}";
}

/// <summary>
/// 提子记录：结算的产物，供 AI 与遥测统计提子规模与地形改造。
/// <see cref="Placements"/> 保持批次内落子顺序（下标即顺序）。
/// </summary>
public sealed record CaptureRecord(
    int Sequence,
    PlayerId Capturer,
    ImmutableArray<Placement> Placements,
    ImmutableArray<CapturedStone> Captured,
    ImmutableArray<AppliedTerrainEdit> Edits = default)
{
    /// <summary>本批次应用的全部改造（按批次内落子顺序）；没有改造时为空。</summary>
    public ImmutableArray<AppliedTerrainEdit> Edits { get; init; } = Edits.IsDefault ? [] : Edits;
}
