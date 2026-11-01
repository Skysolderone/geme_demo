using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Presentation.Camera;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 右下角小地图（big-map 裁决 8 / D9，只在大图上显示）：建局时按地图数据一次绘出地形色底图（岛面地表 / 深水 / 障碍，虚空透明），每次刷新叠棋子点、出生区框与相机视口框；
/// 点击 → 相机注视点跳到该格（<see cref="CellClicked"/>），M 键显隐（GameRoot）。格 ↔ 像素只做等比缩放（北在上），映射本体在 <see cref="BoardGeometry.MinimapPixelOf"/> / <see cref="BoardGeometry.MinimapCellAt"/>；
/// 世界 → 格经 <see cref="BoardGeometry.CellPositionOf"/> 那一份映射——本类不另写任何换算（coordinates.md「映射唯一」）。
/// 只读默认棋盘视图模型与相机位姿，不读地图、不判规则。
/// </summary>
public sealed partial class Minimap : Control
{
    /// <summary>边长（像素）。</summary>
    public const int SizePx = 180;

    private DefaultBoardView? _board;
    private (int Width, int Height, int Cells, int Boards, int Edits) _key;
    private ImageTexture? _terrain;
    private readonly List<(Vector2 At, Color Color)> _pieces = [];
    private readonly List<(Rect2 Rect, Color Color)> _zones = [];
    private Rect2 _view;

    /// <summary>点了小地图上的某格。</summary>
    public event Action<Coord>? CellClicked;

    public Minimap()
    {
        CustomMinimumSize = new Vector2(SizePx, SizePx);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <summary>按当前世界刷新：底图只在地图尺寸 / 格数 / 棋盘清单 / 改造数变了才重画；棋子点与出生区框每次重取。</summary>
    public void Refresh(DefaultBoardView board, IReadOnlyDictionary<int, PlayerId> zoneOwners, CameraPose pose, float visibleWidth, float visibleDepth)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(zoneOwners);
        _board = board;
        (int, int, int, int, int) key = (board.Width, board.Height, board.Cells.Length, board.Boards.IsDefault ? 0 : board.Boards.Length, board.Edits.IsDefault ? 0 : board.Edits.Length);
        if (_terrain is null || key != _key)
        {
            _key = key;
            _terrain = BuildTerrain(board);
        }

        _pieces.Clear();
        var zoneCells = new Dictionary<int, (int MinX, int MinY, int MaxX, int MaxY)>();
        foreach (BoardCellView cell in board.Cells)
        {
            if (cell.Occupant is { } occupant)
            {
                _pieces.Add((PixelOf(cell.Coord.X + 0.5f, cell.Coord.Y + 0.5f), Visuals.FactionColorOf(occupant.Owner)));
            }

            if (cell.BirthZone is { } zone)
            {
                (int minX, int minY, int maxX, int maxY) = zoneCells.TryGetValue(zone, out var box) ? box : (cell.Coord.X, cell.Coord.Y, cell.Coord.X, cell.Coord.Y);
                zoneCells[zone] = (Math.Min(minX, cell.Coord.X), Math.Min(minY, cell.Coord.Y), Math.Max(maxX, cell.Coord.X), Math.Max(maxY, cell.Coord.Y));
            }
        }

        _zones.Clear();
        foreach ((int zone, (int minX, int minY, int maxX, int maxY)) in zoneCells)
        {
            Color color = zoneOwners.TryGetValue(zone, out PlayerId owner) ? Visuals.FactionColorOf(owner) : Visuals.BirthHint;
            Vector2 a = PixelOf(minX, maxY + 1);
            Vector2 b = PixelOf(maxX + 1, minY);
            _zones.Add((new Rect2(a, b - a), color));
        }

        SetView(pose, visibleWidth, visibleDepth);
        QueueRedraw();
    }

    /// <summary>只更新视口框（相机每帧平移时调用，便宜）：注视点 ± 所见范围之半（视图模型的线性近似）。</summary>
    public void SetView(CameraPose pose, float visibleWidth, float visibleDepth)
    {
        if (_board is null)
        {
            return;
        }

        Vector2 focus = BoardGeometry.CellPositionOf(new Vector3(pose.FocusX, 0f, pose.FocusZ), _board.Width, _board.Height);
        Vector2 a = PixelOf(focus.X + 0.5f - (visibleWidth * 0.5f), focus.Y + 0.5f + (visibleDepth * 0.5f));
        Vector2 b = PixelOf(focus.X + 0.5f + (visibleWidth * 0.5f), focus.Y + 0.5f - (visibleDepth * 0.5f));
        var view = new Rect2(a, b - a);
        if (view != _view)
        {
            _view = view;
            QueueRedraw();
        }
    }

    /// <summary>格坐标系里的点（x 向东、y 向北，格左下角为整数）→ 控件像素（北在上）。</summary>
    private Vector2 PixelOf(float x, float y) => BoardGeometry.MinimapPixelOf(x, y, _board?.Width ?? 1, _board?.Height ?? 1, Size);

    private Coord? CellAt(Vector2 pixel) => _board is null ? null : BoardGeometry.MinimapCellAt(pixel, _board.Width, _board.Height, Size);

    /// <summary>地形色底图：每格一个像素，岛面按地表取地砖色（与 3D 地砖同一份 <see cref="BoardView.SurfaceTileColorOf"/>）、深水取水色、岛内障碍取障碍色、虚空透明（岛面判定与 3D 场景同一份 <see cref="BoardView.IslandCells"/>）。</summary>
    private static ImageTexture BuildTerrain(DefaultBoardView board)
    {
        var image = Image.CreateEmpty(board.Width, board.Height, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        HashSet<Coord> island = BoardView.IslandCells(board);
        foreach (BoardCellView cell in board.Cells)
        {
            if (!island.Contains(cell.Coord))
            {
                continue;
            }

            Color color = cell.Surface == Surface.DeepWater ? (cell.HasBridge ? Visuals.BridgeDeck : Visuals.DeepWater)
                : cell.Terrain != Terrain.Playable ? Visuals.TileObstacle
                : BoardView.SurfaceTileColorOf(cell.Surface) ?? Visuals.TilePlayableGreen;
            if (cell.Terrain == Terrain.Playable && cell.Height > 0)
            {
                color = color.Lightened(cell.Height == 1 ? 0.12f : 0.26f);
            }

            image.SetPixel(cell.Coord.X, board.Height - 1 - cell.Coord.Y, color);
        }

        return ImageTexture.CreateFromImage(image);
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        var full = new Rect2(Vector2.Zero, Size);
        DrawRect(full, new Color(0.08f, 0.07f, 0.11f, 0.72f));
        if (_terrain is not null)
        {
            DrawTextureRect(_terrain, full, false);
        }

        foreach ((Rect2 rect, Color color) in _zones)
        {
            DrawRect(rect, color, false, 1.5f);
        }

        foreach ((Vector2 at, Color color) in _pieces)
        {
            DrawRect(new Rect2(at - new Vector2(1.5f, 1.5f), new Vector2(3f, 3f)), color);
        }

        if (_view.Size.X > 0f && _view.Size.Y > 0f)
        {
            DrawRect(_view.Intersection(full), Colors.White, false, 1.5f);
        }

        DrawRect(full, Ui.PanelBorder, false, 1f);
    }

    /// <inheritdoc/>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click && CellAt(click.Position) is { } cell)
        {
            CellClicked?.Invoke(cell);
            AcceptEvent();
        }
    }
}
