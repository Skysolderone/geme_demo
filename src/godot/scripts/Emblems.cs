using Godot;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// 旗帜图案的<b>唯一</b>几何定义（tactical-ui 裁决 6 / 5.2）：同一组多边形同时供 HUD 的 2D 图标与棋子底座的 3D 网格使用，
/// 保证两处图形一致。坐标在 [-0.5, 0.5]² 单位方框内，y 向下（与 Godot Control 一致）。
/// 每个图案是<b>若干凸多边形</b>的并集——凸多边形可以直接扇形三角化，2D / 3D 两边都不需要额外的三角剖分。
/// </summary>
public static class Emblems
{
    /// <summary>某图案的凸多边形列表。</summary>
    public static Vector2[][] Polygons(BannerEmblem emblem) => emblem switch
    {
        BannerEmblem.Triangle => Triangle(),
        BannerEmblem.Tower => Tower(),
        BannerEmblem.Sun => Sun(),
        BannerEmblem.Lotus => Lotus(),
        _ => throw new System.ArgumentOutOfRangeException(nameof(emblem), emblem, "未知旗帜图案。"),
    };

    private static Vector2[][] Triangle() =>
    [
        [new Vector2(0f, -0.48f), new Vector2(0.46f, 0.40f), new Vector2(-0.46f, 0.40f)],
    ];

    private static Vector2[][] Tower()
    {
        Vector2[] Rect(float x0, float y0, float x1, float y1) =>
            [new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)];

        return
        [
            Rect(-0.26f, -0.16f, 0.26f, 0.46f),
            Rect(-0.40f, 0.28f, 0.40f, 0.46f),
            Rect(-0.26f, -0.46f, -0.10f, -0.16f),
            Rect(-0.08f, -0.46f, 0.08f, -0.16f),
            Rect(0.10f, -0.46f, 0.26f, -0.16f),
        ];
    }

    private static Vector2[][] Sun()
    {
        var polygons = new Vector2[9][];
        var core = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Pi * 2f * i / 8f;
            core[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.20f;
        }

        polygons[0] = core;
        for (int i = 0; i < 8; i++)
        {
            float a = (Mathf.Pi * 2f * i / 8f) + (Mathf.Pi / 8f);
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var side = new Vector2(-dir.Y, dir.X);
            polygons[i + 1] = [dir * 0.48f, (dir * 0.18f) + (side * 0.10f), (dir * 0.18f) - (side * 0.10f)];
        }

        return polygons;
    }

    private static Vector2[][] Lotus()
    {
        var polygons = new Vector2[5][];
        for (int i = 0; i < 5; i++)
        {
            float a = (-Mathf.Pi / 2f) + (Mathf.Pi * 2f * i / 5f);
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var side = new Vector2(-dir.Y, dir.X);
            polygons[i] = [dir * 0.48f, (dir * 0.22f) + (side * 0.16f), dir * 0.04f, (dir * 0.22f) - (side * 0.16f)];
        }

        return polygons;
    }
}
