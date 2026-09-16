using Godot;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// 程序生成的低多边形几何体（tactical-ui 裁决 6）：棋子按 <see cref="PieceSilhouette"/> 分派，
/// 本类<b>不认识棋子类型</b>——类型 → 轮廓的映射只在 <see cref="PieceStyleTable"/> 一处。
/// </summary>
public static class LowPoly
{
    /// <summary>棋子底座半径。</summary>
    public const float BaseRadius = 0.36f;

    /// <summary>底座厚度。</summary>
    public const float BaseHeight = 0.07f;

    /// <summary>一枚棋子：底座（阵营主色 + 旗帜图案）+ 轮廓本体。返回的节点原点在地砖上表面。</summary>
    public static Node3D Piece(PieceSilhouette silhouette, BannerEmblem emblem, Color faction, Color body, Color ink)
    {
        var root = new Node3D { Name = "Piece" };
        root.AddChild(Mesh(
            new CylinderMesh { TopRadius = BaseRadius, BottomRadius = BaseRadius, Height = BaseHeight, RadialSegments = 8, Rings = 0 },
            Visuals.Matte(faction), new Vector3(0f, BaseHeight * 0.5f, 0f)));

        // 旗帜图案平铺在底座前缘（朝摄像机一侧），去色后靠形状区分阵营。
        var badge = new MeshInstance3D
        {
            Mesh = EmblemMesh(emblem, 0.30f),
            MaterialOverride = Visuals.Flat(ink),
            Position = new Vector3(0f, BaseHeight + 0.006f, 0.175f),
        };
        root.AddChild(badge);

        foreach (Node3D part in Body(silhouette, body, faction))
        {
            root.AddChild(part);
        }

        return root;
    }

    private static Node3D[] Body(PieceSilhouette silhouette, Color body, Color faction) => silhouette switch
    {
        // 圆头兵：球 + 短柱底座，轮廓语言 = 圆润。
        PieceSilhouette.RoundPawn =>
        [
            Mesh(new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.19f, Height = 0.22f, RadialSegments = 8, Rings = 0 },
                Visuals.Matte(body), new Vector3(0f, BaseHeight + 0.11f, 0f)),
            Mesh(new SphereMesh { Radius = 0.17f, Height = 0.34f, RadialSegments = 8, Rings = 4 },
                Visuals.Matte(body), new Vector3(0f, BaseHeight + 0.38f, 0f)),
        ],

        // 塔楼：粗圆柱 + 四枚雉堞，轮廓语言 = 塔楼体块。
        PieceSilhouette.Tower => TowerParts(body, faction),

        // 双球连杆：两球 + 连杆，轮廓语言 = 连接关系。
        PieceSilhouette.TwinOrbBar =>
        [
            Mesh(new BoxMesh { Size = new Vector3(0.40f, 0.06f, 0.06f) },
                Visuals.Matte(body), new Vector3(0f, BaseHeight + 0.30f, 0f)),
            Mesh(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 0.24f, RadialSegments = 6, Rings = 0 },
                Visuals.Matte(body), new Vector3(0f, BaseHeight + 0.12f, 0f)),
            Mesh(new SphereMesh { Radius = 0.13f, Height = 0.26f, RadialSegments = 8, Rings = 4 },
                Visuals.Matte(body), new Vector3(-0.21f, BaseHeight + 0.30f, 0f)),
            Mesh(new SphereMesh { Radius = 0.13f, Height = 0.26f, RadialSegments = 8, Rings = 4 },
                Visuals.Matte(body), new Vector3(0.21f, BaseHeight + 0.30f, 0f)),
        ],

        // 金字塔：四棱锥，轮廓语言 = 放射状。
        PieceSilhouette.Pyramid =>
        [
            Mesh(new CylinderMesh { TopRadius = 0.001f, BottomRadius = 0.30f, Height = 0.46f, RadialSegments = 4, Rings = 0 },
                Visuals.Matte(body), new Vector3(0f, BaseHeight + 0.23f, 0f), new Vector3(0f, 45f, 0f)),
        ],

        // 多瓣水晶：一根主晶柱 + 四根外倾副晶柱，轮廓语言 = 多节点聚合。
        PieceSilhouette.CrystalCluster => CrystalParts(body),

        _ => throw new System.ArgumentOutOfRangeException(nameof(silhouette), silhouette, "未知棋子轮廓。"),
    };

    private static Node3D[] TowerParts(Color body, Color faction)
    {
        var parts = new Node3D[6];
        parts[0] = Mesh(new CylinderMesh { TopRadius = 0.21f, BottomRadius = 0.25f, Height = 0.44f, RadialSegments = 8, Rings = 0 },
            Visuals.Matte(body), new Vector3(0f, BaseHeight + 0.22f, 0f));
        parts[1] = Mesh(new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 0.06f, RadialSegments = 8, Rings = 0 },
            Visuals.Matte(faction), new Vector3(0f, BaseHeight + 0.47f, 0f));
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Pi * 2f * i / 4f;
            parts[i + 2] = Mesh(new BoxMesh { Size = new Vector3(0.10f, 0.13f, 0.10f) }, Visuals.Matte(body),
                new Vector3(Mathf.Cos(a) * 0.18f, BaseHeight + 0.56f, Mathf.Sin(a) * 0.18f));
        }

        return parts;
    }

    private static Node3D[] CrystalParts(Color body)
    {
        var parts = new Node3D[5];
        parts[0] = Mesh(new CylinderMesh { TopRadius = 0.001f, BottomRadius = 0.11f, Height = 0.52f, RadialSegments = 6, Rings = 0 },
            Visuals.Matte(body), new Vector3(0f, BaseHeight + 0.26f, 0f));
        for (int i = 0; i < 4; i++)
        {
            float a = (Mathf.Pi * 2f * i / 4f) + (Mathf.Pi / 4f);
            parts[i + 1] = Mesh(
                new CylinderMesh { TopRadius = 0.001f, BottomRadius = 0.08f, Height = 0.34f, RadialSegments = 6, Rings = 0 },
                Visuals.Matte(body),
                new Vector3(Mathf.Cos(a) * 0.15f, BaseHeight + 0.17f, Mathf.Sin(a) * 0.15f),
                new Vector3(Mathf.Sin(a) * 26f, 0f, -Mathf.Cos(a) * 26f));
        }

        return parts;
    }

    /// <summary>障碍格的低多边形岩石（装饰层，渲染顺序在一切判读信息之下）。</summary>
    public static Node3D Rock(int variant)
    {
        var root = new Node3D { Name = "Rock" };
        StandardMaterial3D material = Visuals.Matte(Visuals.Rock, 1f);
        root.AddChild(Mesh(new SphereMesh { Radius = 0.34f, Height = 0.52f, RadialSegments = 5, Rings = 2 }, material,
            new Vector3(0f, 0.16f, 0f), new Vector3(0f, variant * 37f, 0f), new Vector3(1f, 0.85f, 1.1f)));
        root.AddChild(Mesh(new SphereMesh { Radius = 0.20f, Height = 0.30f, RadialSegments = 5, Rings = 2 }, material,
            new Vector3(0.16f, 0.10f, -0.13f), new Vector3(0f, variant * 61f, 0f), new Vector3(1f, 0.9f, 1f)));
        return root;
    }

    /// <summary>
    /// 林地格的三棵小树（装饰层）：放在地砖三个角上、树冠半径 0.09、高 0.24，落在棋子底座（半径 0.36）之外，
    /// 不遮挡该格的落点、气与归属标记（visual-style-baseline「装饰不遮挡判读」）。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Trees(int variant)
    {
        var root = new Node3D { Name = "Trees" };
        StandardMaterial3D canopy = Visuals.Matte(Visuals.TreeCanopy, 1f);
        StandardMaterial3D trunk = Visuals.Matte(Visuals.Timber, 1f);
        Vector2[] corners = [new(-0.31f, -0.30f), new(0.30f, -0.29f), new(-0.02f, 0.31f)];
        for (int i = 0; i < corners.Length; i++)
        {
            float scale = 0.85f + (0.15f * (((variant + i) % 3) / 2f));
            Vector3 at = new(corners[i].X, 0f, corners[i].Y);
            root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.025f, Height = 0.08f, RadialSegments = 5, Rings = 0 }, trunk,
                at + new Vector3(0f, 0.04f, 0f)));
            root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.001f, BottomRadius = 0.09f * scale, Height = 0.20f * scale, RadialSegments = 5, Rings = 0 }, canopy,
                at + new Vector3(0f, 0.08f + (0.10f * scale), 0f), new Vector3(0f, (variant * 53f) + (i * 40f), 0f)));
        }

        return root;
    }

    /// <summary>
    /// 一段栅栏（terrain-model 边属性）：三根立柱 + 两根横杆，沿一格边长立起，厚度只有 0.05，
    /// 放在两格之间的缝上，不占任一格的落点。<paramref name="alongX"/> 为 <c>true</c> 时沿 X 轴（两格上下相邻），否则沿 Z 轴。
    /// 返回节点原点在缝中心、地砖上表面。
    /// </summary>
    public static Node3D Fence(bool alongX)
    {
        var root = new Node3D { Name = "Fence" };
        StandardMaterial3D timber = Visuals.Matte(Visuals.Timber, 1f);
        float length = BoardGeometry.TileSize + 0.06f;
        for (int i = -1; i <= 1; i++)
        {
            float offset = i * 0.40f;
            root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.05f, 0.30f, 0.05f) }, timber,
                alongX ? new Vector3(offset, 0.15f, 0f) : new Vector3(0f, 0.15f, offset)));
        }

        foreach (float y in new[] { 0.11f, 0.23f })
        {
            root.AddChild(Mesh(new BoxMesh { Size = alongX ? new Vector3(length, 0.035f, 0.03f) : new Vector3(0.03f, 0.035f, length) }, timber,
                new Vector3(0f, y, 0f)));
        }

        return root;
    }

    /// <summary>
    /// 预置桥（terrain-model 设施）：一块木板面 + 四根角柱，铺在深水格上，面与同层地砖齐平——桥格是普通可落子格，
    /// 棋子与标记照常放在面上。角柱只有 0.07 见方，不遮挡落点。返回节点原点在桥面（地砖上表面）。
    /// </summary>
    public static Node3D Bridge()
    {
        var root = new Node3D { Name = "Bridge" };
        StandardMaterial3D deck = Visuals.Matte(Visuals.BridgeDeck, 1f);
        StandardMaterial3D timber = Visuals.Matte(Visuals.Timber, 1f);
        const float half = BoardGeometry.TileSize * 0.5f;
        root.AddChild(Mesh(new BoxMesh { Size = new Vector3(BoardGeometry.TileSize, 0.06f, BoardGeometry.TileSize) }, deck, new Vector3(0f, -0.03f, 0f)));

        // 板缝：三条深色细槽，让桥面在缩略图里也读得出"木板"。
        for (int i = -1; i <= 1; i++)
        {
            root.AddChild(Mesh(new BoxMesh { Size = new Vector3(BoardGeometry.TileSize, 0.004f, 0.02f) }, timber, new Vector3(0f, 0.002f, i * 0.28f)));
        }

        foreach (float sx in new[] { -half + 0.05f, half - 0.05f })
        {
            foreach (float sz in new[] { -half + 0.05f, half - 0.05f })
            {
                root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.07f, 0.16f, 0.07f) }, timber, new Vector3(sx, 0.05f, sz)));
            }
        }

        return root;
    }

    /// <summary>贴在地砖上的扁平方形标记（叠加层用）。</summary>
    public static PlaneMesh Marker(float size) => new() { Size = new Vector2(size, size), Orientation = PlaneMesh.OrientationEnum.Y };

    /// <summary>旗帜图案的扁平 3D 网格（朝上）。与 HUD 图标共用 <see cref="Emblems.Polygons"/>。</summary>
    public static ArrayMesh EmblemMesh(BannerEmblem emblem, float size)
    {
        var tool = new SurfaceTool();
        tool.Begin(global::Godot.Mesh.PrimitiveType.Triangles);
        tool.SetNormal(Vector3.Up);
        foreach (Vector2[] polygon in Emblems.Polygons(emblem))
        {
            for (int i = 1; i + 1 < polygon.Length; i++)
            {
                tool.AddVertex(Lift(polygon[0], size));
                tool.AddVertex(Lift(polygon[i + 1], size));
                tool.AddVertex(Lift(polygon[i], size));
            }
        }

        return tool.Commit();
    }

    private static Vector3 Lift(Vector2 p, float size) => new(p.X * size, 0f, p.Y * size);

    private static MeshInstance3D Mesh(Mesh mesh, Material material, Vector3 position, Vector3? rotation = null, Vector3? scale = null)
    {
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position };
        if (rotation is { } r)
        {
            instance.RotationDegrees = r;
        }

        if (scale is { } s)
        {
            instance.Scale = s;
        }

        return instance;
    }
}
