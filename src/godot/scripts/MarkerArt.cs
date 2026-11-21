using System.Collections.Generic;
using Godot;

namespace Siege.Godot;

/// <summary>
/// 地图标记的卡通描边（chibi-world 段 ③ R3.2）：只统一配色与描边，形状、大小、含义不变。
/// <list type="bullet">
/// <item>贴地的平标记（格子轮廓环、插旗预览框、预演的叉、气点）：底下垫一层墨色衬底，四周外扩 <see cref="Rim"/>、顶面低于彩色件，俯视时彩色件四周露出一圈墨线。</item>
/// <item>立起的标记（地图信物、悬浮徽记、倍率热柱、"眼"徽记）：套一层反向外壳（<see cref="Visuals.Outline"/>，外推 <see cref="Hull"/>），与单位 / 装饰同一画法。</item>
/// </list>
/// 墨色一律 <see cref="Visuals.OutlineInk"/>（28,24,30）。禁手 / 禁入印记（DrawPlacementMarks）与领地着色不走这里：
/// 前者规格钉住"近景外观与引入之前相同"，后者是整格半透明铺色，平地格之间不描边（段 ② 裁决 4 的同一口径）。
/// </summary>
public static class MarkerArt
{
    /// <summary>平标记墨色衬底的外扩宽度（世界单位，格宽 1）。</summary>
    public const float Rim = 0.014f;

    /// <summary>立体标记反向外壳的外推量。</summary>
    public const float Hull = 0.018f;

    /// <summary>地图信物底座色（铭牌底座，改前同值）。</summary>
    public static readonly Color RelicBase = Color.Color8(88, 73, 68);

    private static StandardMaterial3D? _inkFlat;
    private static StandardMaterial3D? _inkHull;
    private static readonly Dictionary<(bool Dashed, int Thickness), ArrayMesh> RingInks = [];

    /// <summary>平标记的墨色衬底材质（不受光、双面，全图共用一份）。</summary>
    public static StandardMaterial3D InkFlat => _inkFlat ??= Visuals.Flat(Visuals.OutlineInk);

    /// <summary>立体标记的反向外壳材质（只画背面、不受光，全图共用一份）。</summary>
    public static StandardMaterial3D InkHull => _inkHull ??= Visuals.Outline(Visuals.OutlineInk);

    /// <summary>
    /// 格子轮廓环的墨色衬底：与 <c>BoardView.AddRing</c> 同一摆法（四边各一段，虚线每边三段），每段四周外扩 <see cref="Rim"/>、
    /// 厚 0.016、中心比彩色件低 0.003（顶面低 0.005，不与彩色件顶面共面闪烁），合成一个网格，按（虚线, 粗细）缓存。
    /// 原点在环心（彩色件的中心高度）。
    /// </summary>
    public static ArrayMesh RingInk(bool dashed, float thickness)
    {
        var key = (dashed, (int)MathF.Round(thickness * 1000f));   // 粗细按千分之一取整作缓存键（不是坐标换算）
        if (RingInks.TryGetValue(key, out ArrayMesh? cached))
        {
            return cached;
        }

        const float half = BoardGeometry.TileSize * 0.5f;
        float[] offsets = dashed ? [-0.30f, 0f, 0.30f] : [0f];
        float length = dashed ? 0.20f : BoardGeometry.TileSize;
        var tool = new SurfaceTool();
        for (int side = 0; side < 4; side++)
        {
            bool horizontal = side is 0 or 1;
            float sign = side is 0 or 2 ? 1f : -1f;
            foreach (float offset in offsets)
            {
                Vector3 size = horizontal
                    ? new Vector3(length + (2f * Rim), 0.016f, thickness + (2f * Rim))
                    : new Vector3(thickness + (2f * Rim), 0.016f, length + (2f * Rim));
                Vector3 position = horizontal ? new Vector3(offset, -0.003f, sign * half) : new Vector3(sign * half, -0.003f, offset);
                tool.AppendFrom(new BoxMesh { Size = size }, 0, new Transform3D(Basis.Identity, position));
            }
        }

        ArrayMesh mesh = tool.Commit();
        RingInks[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// 格子轮廓环的彩色件（原 <c>BoardView.AddRing</c> 的摆法，搬到这里供 <c>--marker-swatch</c> 共用，节点、尺寸、位置不变）：
    /// 四边各一段（虚线每边三段 0.20 长），厚 0.02、宽 <paramref name="thickness"/>，不受光。<paramref name="center"/> 是环心（含高度）。
    /// </summary>
    public static void Ring(Node3D parent, Vector3 center, Color color, bool dashed, float thickness)
    {
        StandardMaterial3D material = Visuals.Flat(color);
        const float half = BoardGeometry.TileSize * 0.5f;
        float[] offsets = dashed ? [-0.30f, 0f, 0.30f] : [0f];
        float length = dashed ? 0.20f : BoardGeometry.TileSize;
        for (int side = 0; side < 4; side++)
        {
            bool horizontal = side is 0 or 1;
            float sign = side is 0 or 2 ? 1f : -1f;
            foreach (float offset in offsets)
            {
                Vector3 size = horizontal ? new Vector3(length, 0.02f, thickness) : new Vector3(thickness, 0.02f, length);
                Vector3 position = horizontal
                    ? center + new Vector3(offset, 0f, sign * half)
                    : center + new Vector3(sign * half, 0f, offset);
                parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = position });
            }
        }
    }

    /// <summary>一块墨色衬底（平标记用）：<paramref name="size"/> 是彩色件的尺寸，衬底四周外扩 <see cref="Rim"/>、厚度少 0.004、中心低 0.003。</summary>
    public static MeshInstance3D InkSlab(Vector3 size, Vector3 position, Vector3 rotationDegrees = default) => new()
    {
        Mesh = new BoxMesh { Size = new Vector3(size.X + (2f * Rim), Mathf.Max(0.004f, size.Y - 0.004f), size.Z + (2f * Rim)) },
        MaterialOverride = InkFlat,
        Position = position - new Vector3(0f, 0.003f, 0f),
        RotationDegrees = rotationDegrees,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>一层反向外壳（立体标记用）：网格由调用方按本体尺寸外推 <see cref="Hull"/> 给出；节点名 <c>Outline</c>，拾取跳过、不投影。</summary>
    public static MeshInstance3D HullOf(Mesh mesh, Vector3 position, Vector3 rotationDegrees = default) => new()
    {
        Name = PieceModels.OutlineName,
        Mesh = mesh,
        MaterialOverride = InkHull,
        Position = position,
        RotationDegrees = rotationDegrees,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>
    /// 地图信物（relic-redesign R25）：铭牌底座 + 四棱晶石，尺寸与改前相同。<paramref name="ink"/> 为真时底座改卡通分阶、两件各套一层墨色外壳；
    /// 为假即改前画法（哑光底座、无外壳），只给 <c>--marker-swatch</c> 的"改前"一行用。<paramref name="mark"/> 是晶石色（对局里恒为 <see cref="Visuals.RelicMark"/>）。
    /// </summary>
    public static Node3D RelicToken(Color mark, bool ink)
    {
        var token = new Node3D { Name = "RelicToken" };
        var baseMesh = new CylinderMesh { TopRadius = 0.125f, BottomRadius = 0.125f, Height = 0.026f, RadialSegments = 8 };
        var basePosition = new Vector3(0f, 0.023f, 0f);
        token.AddChild(new MeshInstance3D
        {
            Mesh = baseMesh,
            MaterialOverride = ink ? Visuals.Toon(RelicBase) : Visuals.Matte(RelicBase, 0.72f),
            Position = basePosition,
        });
        var crystalMesh = new CylinderMesh { TopRadius = 0.001f, BottomRadius = 0.100f, Height = 0.115f, RadialSegments = 4 };
        var crystalPosition = new Vector3(0f, 0.090f, 0f);
        var crystalRotation = new Vector3(0f, 45f, 0f);
        token.AddChild(new MeshInstance3D
        {
            Mesh = crystalMesh,
            MaterialOverride = Visuals.Glow(mark.Lightened(0.18f), 0.85f, false),
            Position = crystalPosition,
            RotationDegrees = crystalRotation,
        });
        if (ink)
        {
            // 四棱锥的面到中轴距离是半径 × cos 45°，面外推 Hull 折成半径外推 Hull × √2；锥尖另抬 1.5 Hull。
            token.AddChild(HullOf(
                new CylinderMesh { TopRadius = 0.125f + Hull, BottomRadius = 0.125f + Hull, Height = 0.026f + (2f * Hull), RadialSegments = 8 },
                basePosition));
            token.AddChild(HullOf(
                new CylinderMesh { TopRadius = 0.001f + Hull, BottomRadius = 0.100f + (Hull * 1.414f), Height = 0.115f + (2.5f * Hull), RadialSegments = 4 },
                crystalPosition + new Vector3(0f, 0.25f * Hull, 0f),
                crystalRotation));
        }

        return token;
    }
}
