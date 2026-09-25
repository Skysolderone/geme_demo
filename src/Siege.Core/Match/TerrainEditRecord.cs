using Siege.Core.Board;

namespace Siege.Core.Match;

/// <summary>
/// 一次已完成的地形改造在对局层的留痕（match-telemetry「对局日志的记录内容」第 4 条）：
/// 大回合、本局第几次改造、改造方、动作与目标、携带该改造的匠人落点，以及该次改造是否直接导致提子。
/// </summary>
/// <remarks>
/// <para><b>只供日志与分析</b>。设施无归属：公开视图、盘面与表现层 MUST NOT 显示 <see cref="Player"/> 或 <see cref="ArtisanCoord"/>（R-3）。</para>
/// <para>本记录不随存档往返（与事件日志、征募记录同列）；地形本身由盘面序列化的改造段持久化。</para>
/// </remarks>
/// <param name="MajorRound">该改造发生时的大回合序号。</param>
/// <param name="Sequence">本局第几次改造，从 1 起。按日志顺序重放全部改造即可离线重建终局地形。</param>
/// <param name="Player">改造方。</param>
/// <param name="Edit">动作与目标。</param>
/// <param name="ArtisanCoord">携带该改造的匠人落点。</param>
/// <param name="CausedCapture">是否直接导致提子；口径见 <see cref="Batch.AppliedTerrainEdit.CausedCapture"/>。</param>
/// <param name="ViaWorkshop">目标是否经工坊扩展（隔一格，more-pieces-relics D12）；由 <see cref="TerrainEditRules.IsWorkshopReach"/> 在结算留痕时判定。</param>
public readonly record struct TerrainEditRecord(
    int MajorRound,
    int Sequence,
    PlayerId Player,
    TerrainEdit Edit,
    Coord ArtisanCoord,
    bool CausedCapture,
    bool ViaWorkshop = false)
{
    public override string ToString() =>
        $"R{MajorRound} P{Player.Value} {TerrainEdit.DisplayName(Edit.Kind)} {Edit}{(CausedCapture ? " →提子" : string.Empty)}";
}
