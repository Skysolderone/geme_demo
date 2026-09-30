using Siege.Core.Board;
using Siege.Presentation.Visibility;

namespace Siege.Presentation.MapSelect;

/// <summary>
/// 选图界面上当前预览地图的一行说明：地图尺寸、出生区数、可落子格数；带棋盘清单的地图（棋盘档）另写棋盘数。
/// 只读默认棋盘视图模型，不读地图、不判规则。棋盘清单为空的地图，文案与引入棋盘图之前相同。
/// </summary>
/// <remarks>规格：openspec/changes/board-map/specs/map-selection —— 选中棋盘图时 SHALL 显示当前地图种子、棋盘数与地图尺寸。</remarks>
public static class MapPreviewInfo
{
    /// <summary>一行说明。</summary>
    public static string Of(DefaultBoardView board)
    {
        ArgumentNullException.ThrowIfNull(board);
        int zones = board.Cells.Where(c => c.BirthZone is not null).Select(c => c.BirthZone).Distinct().Count();
        int playable = board.Cells.Count(c => c.Terrain == Terrain.Playable);
        string text = $"{board.Width}×{board.Height}，{zones} 个出生区，可落子 {playable} 格";
        if (board.Boards.IsDefaultOrEmpty)
        {
            return text;
        }

        int births = board.Boards.Count(b => b.Kind == BoardPlateKind.Birth);
        return $"{text}，棋盘 {board.Boards.Length} 块（出生 {births}、公共 {board.Boards.Length - births}）";
    }
}
