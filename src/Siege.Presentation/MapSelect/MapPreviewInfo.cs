using Siege.Core.Board;
using Siege.Presentation.Visibility;

namespace Siege.Presentation.MapSelect;

/// <summary>
/// 新局 / 选图界面上当前预览地图的一行说明。链接图（board-link-mode D18）：画布尺寸与 5 块出生棋盘的占位（块数、边长范围），
/// MUST NOT 提及棋盘内部布局（信物格位置）与可落子格数——预览会话尚未插旗，视图里本来就没有格。
/// 旧地图（没有占位）：地图尺寸、出生区数、可落子格数；带棋盘清单的另写棋盘数（文案与引入链接图之前相同）。
/// 只读默认棋盘视图模型，不读地图、不判规则。
/// </summary>
/// <remarks>规格：openspec/changes/board-link-mode/specs/map-selection —— 开局选图界面（背景 5 个占位轮廓，MUST NOT 显示棋盘内部布局）。</remarks>
public static class MapPreviewInfo
{
    /// <summary>一行说明。</summary>
    public static string Of(DefaultBoardView board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (!board.Placeholders.IsDefaultOrEmpty)
        {
            int smallest = board.Placeholders.Min(p => Math.Min(p.Width, p.Height));
            int largest = board.Placeholders.Max(p => Math.Max(p.Width, p.Height));
            string edge = smallest == largest ? $"{smallest}×{smallest}" : $"{smallest}–{largest}";
            return $"{board.Width}×{board.Height} 画布，{board.Placeholders.Length} 块出生棋盘（边长 {edge}）；各自盲选一块，三个大回合后随机配对链接";
        }

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
