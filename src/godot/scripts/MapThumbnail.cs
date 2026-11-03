using System;
using Godot;
using Siege.Core.Board;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 新局面板大图缩略上的叠层（relic-sites 裁决 1 / 2 / 10）：盖在整块大陆的像素图之上（<c>AspectRatioContainer</c> 的同一块矩形），画信物遗址标记、
/// 悬停格的 13×13 构筑范围框（合规绿 / 不合规红）与已选格的常显框，并接鼠标：悬停 / 点击换算成格后交给主场景（<see cref="CellHovered"/> / <see cref="CellClicked"/>）。
/// 像素 ↔ 格只经 <see cref="BoardGeometry.MinimapPixelOf"/> / <see cref="BoardGeometry.MinimapCellAt"/> 那一份映射（coordinates.md「映射唯一」），本类不另写换算；
/// 合规与否、原因文案由主场景经表现层视图模型给出，这里只画、不判规则。全部内容在 <see cref="_Draw"/> 里画，不建节点（自检逐帧比节点数）。
/// </summary>
public sealed partial class MapThumbnail : Control
{
    private DefaultBoardView? _board;
    private bool _interactive;
    private Coord? _hover;
    private bool _hoverLegal;
    private Coord? _picked;
    private int _reach;

    /// <summary>指针悬停到某格；离开缩略为 <c>null</c>。</summary>
    public event Action<Coord?>? CellHovered;

    /// <summary>左键点了某格。</summary>
    public event Action<Coord>? CellClicked;

    public MapThumbnail()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>
    /// 刷新：<paramref name="board"/> 给尺寸与遗址清单（<c>null</c> 即不画）；<paramref name="interactive"/> 为假时只画遗址标记、不接鼠标（小图不启用面板插旗）；
    /// <paramref name="reach"/> 是旗心到构筑范围边的格数（Core 常量，由调用方经视图模型取）。
    /// </summary>
    public void Refresh(DefaultBoardView? board, bool interactive, Coord? hover, bool hoverLegal, Coord? picked, int reach)
    {
        _board = board;
        _interactive = interactive;
        _hover = hover;
        _hoverLegal = hoverLegal;
        _picked = picked;
        _reach = reach;
        MouseFilter = interactive ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    /// <summary>某格格心在本控件上的像素（自检用：反算一个已知格的像素再按点击路径送回去）。</summary>
    public Vector2 PixelOfCell(Coord cell) => PixelOf(cell.X + 0.5f, cell.Y + 0.5f);

    /// <summary>按控件像素点一下（与鼠标左键同一条路径）。</summary>
    public void Click(Vector2 pixel)
    {
        if (_interactive && CellAt(pixel) is { } cell)
        {
            CellClicked?.Invoke(cell);
        }
    }

    /// <inheritdoc/>
    public override void _GuiInput(InputEvent @event)
    {
        if (!_interactive)
        {
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            Coord? cell = CellAt(motion.Position);
            if (cell != _hover)
            {
                CellHovered?.Invoke(cell);
            }
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            Click(click.Position);
            AcceptEvent();
        }
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _interactive && _hover is not null)
        {
            CellHovered?.Invoke(null);
        }
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (_board is null)
        {
            return;
        }

        Minimap.DrawRelicSites(this, _board.RelicSites, PixelOf, Size.X / Math.Max(1, _board.Width));
        if (!_interactive)
        {
            return;
        }

        if (_picked is { } picked)
        {
            Frame(picked, Visuals.BirthHint, 2.5f, fill: true);
        }

        if (_hover is { } hover && hover != _picked)
        {
            Frame(hover, _hoverLegal ? Visuals.BirthHint : Visuals.Urgent, 1.5f, fill: false);
        }
    }

    /// <summary>旗心 ± reach 的方框（13×13 构筑范围）与旗心那一格的小框；格角 → 像素经唯一映射，画布外的部分照画（不夹取——越界本身就是不合规的原因之一）。</summary>
    private void Frame(Coord center, Color color, float width, bool fill)
    {
        Vector2 a = PixelOf(center.X - _reach, center.Y + _reach + 1);
        Vector2 b = PixelOf(center.X + _reach + 1, center.Y - _reach);
        var plate = new Rect2(a, b - a);
        if (fill)
        {
            DrawRect(plate, new Color(color, 0.18f));
        }

        DrawRect(plate, color, false, width);
        Vector2 c = PixelOf(center.X, center.Y + 1);
        Vector2 d = PixelOf(center.X + 1, center.Y);
        DrawRect(new Rect2(c, d - c), color, false, width);
    }

    private Vector2 PixelOf(float x, float y) => BoardGeometry.MinimapPixelOf(x, y, _board?.Width ?? 1, _board?.Height ?? 1, Size);

    private Coord? CellAt(Vector2 pixel) => _board is null || Size.X <= 0f || Size.Y <= 0f ? null : BoardGeometry.MinimapCellAt(pixel, _board.Width, _board.Height, Size);
}
