using System;
using System.Collections.Immutable;
using Godot;
using Siege.Core.Board;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 新局面板大图缩略上的叠层（relic-sites 裁决 1 / 2 / 10，relic-zones 裁决 1 / 2）：盖在整块大陆的像素图之上（<c>AspectRatioContainer</c> 的同一块矩形），先把不可插旗的格整层压暗，再画信物遗址标记（稀有只画区域）、
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
    private ImmutableArray<bool> _mask = [];
    private ImageTexture? _maskTexture;

    /// <summary>不可插格的压暗色（relic-zones 裁决 1）：半透明墨色盖在缩略上，可插格保持原色。</summary>
    private static readonly Color Blocked = new(0.05f, 0.04f, 0.08f, 0.62f);

    /// <summary>压暗层纹理的像素尺寸（自检探针：须等于地图画布尺寸；没有压暗层为 0）。</summary>
    public Vector2I MaskSize => _maskTexture is null ? Vector2I.Zero : new Vector2I(_maskTexture.GetWidth(), _maskTexture.GetHeight());

    /// <summary>指针悬停到某格；离开缩略为 <c>null</c>。</summary>
    public event Action<Coord?>? CellHovered;

    /// <summary>左键点了某格。</summary>
    public event Action<Coord>? CellClicked;

    public MapThumbnail()
    {
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;   // 压暗层每格一个像素，放大不糊（与底下的地形像素图同口径）
    }

    /// <summary>
    /// 刷新：<paramref name="board"/> 给尺寸与遗址清单（<c>null</c> 即不画）；<paramref name="interactive"/> 为假时只画遗址标记、不接鼠标（小图不启用面板插旗）；
    /// <paramref name="reach"/> 是旗心到构筑范围边的格数（Core 常量，由调用方经视图模型取）；
    /// <paramref name="mask"/> 是视图模型的可插旗遮罩（<paramref name="maskWidth"/> × <paramref name="maskHeight"/>，真 = 合规旗格），换了才重建压暗层纹理（relic-zones 裁决 1）。
    /// </summary>
    public void Refresh(DefaultBoardView? board, bool interactive, Coord? hover, bool hoverLegal, Coord? picked, int reach, ImmutableArray<bool> mask, int maskWidth, int maskHeight)
    {
        if (!interactive || board is null || mask.IsDefaultOrEmpty)
        {
            _mask = [];
            _maskTexture = null;
        }
        else if (mask != _mask)
        {
            _mask = mask;
            _maskTexture = BuildMask(mask, maskWidth, maskHeight);
        }

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

        // 先压暗不可插格（relic-zones 裁决 1），再画遗址（稀有只画区域）与框——区域和框都要压在暗层之上才看得清。
        if (_interactive && _maskTexture is not null)
        {
            DrawTextureRect(_maskTexture, new Rect2(Vector2.Zero, Size), false);
        }

        Minimap.DrawRelicSites(this, _board.RelicSites, PixelOf, Size.X / Math.Max(1, _board.Width), _board.Width, _board.Height);
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

    /// <summary>压暗层：每格一个像素（北在上，与 <see cref="Minimap.BuildTerrain"/> 同一朝向），不可插格为 <see cref="Blocked"/>、可插格透明。</summary>
    private static ImageTexture BuildMask(ImmutableArray<bool> mask, int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(Blocked);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (mask[(y * width) + x])
                {
                    image.SetPixel(x, BoardGeometry.MinimapImageRow(y, height), Colors.Transparent);
                }
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private Vector2 PixelOf(float x, float y) => BoardGeometry.MinimapPixelOf(x, y, _board?.Width ?? 1, _board?.Height ?? 1, Size);

    private Coord? CellAt(Vector2 pixel) => _board is null || Size.X <= 0f || Size.Y <= 0f ? null : BoardGeometry.MinimapCellAt(pixel, _board.Width, _board.Height, Size);
}
