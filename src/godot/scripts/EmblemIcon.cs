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
        if (scale <= 0f)
        {
            return;
        }

        Vector2 center = Size * 0.5f;
        // 八边形徽章底片与细金内环仅属于 HUD；盘上棋子仍用 Emblems 的同一组图形。
        var plate = new Vector2[8];
        for (int i = 0; i < plate.Length; i++)
        {
            float angle = (Mathf.Pi / 8f) + (Mathf.Pi * 2f * i / plate.Length);
            plate[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (scale * 0.46f);
        }

        DrawColoredPolygon(plate, new Color(0.055f, 0.095f, 0.12f, 0.96f));
        float stroke = Mathf.Max(1f, scale * 0.045f);
        for (int i = 0; i < plate.Length; i++)
        {
            DrawLine(plate[i], plate[(i + 1) % plate.Length], new Color(_color, 0.88f), stroke, true);
        }

        foreach (Vector2[] polygon in Emblems.Polygons(_emblem))
        {
            var points = new Vector2[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
            {
                points[i] = center + (polygon[i] * scale * 0.66f);
            }

            DrawColoredPolygon(points, _color);
        }

        if (scale >= 24f)
        {
            DrawLine(center + new Vector2(-scale * 0.18f, scale * 0.37f),
                center + new Vector2(scale * 0.18f, scale * 0.37f), Ui.PanelBorder, 1f, true);
        }
    }
}
