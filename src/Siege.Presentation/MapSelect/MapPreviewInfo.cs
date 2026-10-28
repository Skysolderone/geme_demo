using Siege.Core.Board;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Presentation.MapSelect;

/// <summary>
/// 新局 / 选图界面上当前预览地图的一行说明。链接图（board-link-mode D18 → map-expand-25 → seat-gap-25）：画布尺寸、出生座位的占位（个数、边长范围）与中立平台块数，
/// MUST NOT 提及棋盘内部布局（信物格位置）与可落子格数——预览会话尚未插旗，视图里本来就没有格。
/// 旧地图（没有占位）：地图尺寸、出生区数、可落子格数；带棋盘清单的另写棋盘数（文案与引入链接图之前相同）。
/// 只读默认棋盘视图模型，不读地图、不判规则。
/// </summary>
/// <remarks>规格：openspec/changes/board-link-mode/specs/map-selection —— 开局选图界面（背景是各块出生平台的占位轮廓，MUST NOT 显示棋盘内部布局）。</remarks>
public static class MapPreviewInfo
{
    /// <summary>
    /// 新局界面背景用的棋盘视图（link-map-terrain 裁决 Q5：新局界面的占位本期不显示主题，主题只在插旗阶段显示）：占位的主题清空，其余原样。
    /// <see cref="DefaultBoardView.LayoutKey"/> 不含主题，所以与插旗阶段的视图同一指纹——刷新路径不会因此重搭成带主题的画面。
    /// </summary>
    public static DefaultBoardView Backdrop(DefaultBoardView board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.Placeholders.IsDefaultOrEmpty ? board : board with { Placeholders = [.. board.Placeholders.Select(p => p with { Theme = null })] };
    }

    /// <summary>一行说明。</summary>
    public static string Of(DefaultBoardView board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (!board.Placeholders.IsDefaultOrEmpty)
        {
            int smallest = board.Placeholders.Min(p => Math.Min(p.Width, p.Height));
            int largest = board.Placeholders.Max(p => Math.Max(p.Width, p.Height));
            string edge = smallest == largest ? $"{smallest}×{smallest}" : $"{smallest}–{largest}";
            // seat-gap-25 E6：玩家各占一个出生座位（占位只给座位），座位间距与"其余为中立平台"的说法经 Labels.SeatLayout（中立平台轮廓另列，不可出生）。
            int neutral = board.NeutralPlates.IsDefault ? 0 : board.NeutralPlates.Length;
            return $"{board.Width}×{board.Height} 画布，{board.Placeholders.Length} 个出生座位（边长 {edge}）、{neutral} 块中立平台；{Labels.SeatLayout}（不可出生）。"
                + "各自盲选一个座位（撞座位按插旗先后改派到空座位），前 3 个大回合只在自己座位构筑、第 4 大回合起全岛开战";
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
