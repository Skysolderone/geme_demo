using Godot;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// HUD 里的旗帜图案图标。多边形取自 <see cref="Emblems"/>——与棋子底座上的 3D 图案是同一组几何，
/// 保证「盘上看到的旗帜」与「面板里看到的旗帜」一致（5.2）。
/// </summary>
public sealed partial class EmblemIcon : Control
{
    private BannerEmblem _emblem = BannerEmblem.Triangle;
    private Color _color = Colors.White;

    /// <summary>设置图案与颜色。</summary>
    public void Set(BannerEmblem emblem, Color color)
    {
        _emblem = emblem;
        _color = color;
        QueueRedraw();
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        float scale = Mathf.Min(Size.X, Size.Y);
        Vector2 center = Size * 0.5f;
        foreach (Vector2[] polygon in Emblems.Polygons(_emblem))
        {
            var points = new Vector2[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
            {
                points[i] = center + (polygon[i] * scale);
            }

            DrawColoredPolygon(points, _color);
        }
    }
}
