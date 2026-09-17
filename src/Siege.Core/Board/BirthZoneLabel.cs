namespace Siege.Core.Board;

/// <summary>
/// 出生区编号的对人显示口径（裁决 S-14）：内部索引 <c>MapData.BirthZones[z]</c> 从 0 起，对人显示一律从 1 起，
/// 校验器报文、对称检查报文、遥测分析报告、插旗锁定事件文本，以及 <c>map</c> 子命令文本图、<c>play</c> 棋盘标记与插旗提示都经这里换算；
/// 日志 JSON 中的出生区字段（如 <c>HomeZone</c>、<c>Zones</c>）属于数据，保持 0 起，不经这里。
/// </summary>
public static class BirthZoneLabel
{
    /// <summary>对人显示的编号：内部索引 + 1。</summary>
    public static int Number(int zoneIndex) => zoneIndex + 1;

    /// <summary>对人显示的名称，如内部索引 0 → "出生区 1"。</summary>
    public static string Of(int zoneIndex) => $"出生区 {Number(zoneIndex)}";
}
