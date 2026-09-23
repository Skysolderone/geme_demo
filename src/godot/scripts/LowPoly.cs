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
        PieceSilhouette.Scaffold => ScaffoldParts(body, faction),

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

    /// <summary>
    /// 匠人的工具轮廓（artisan-terrain-edit 4.2，Open Question 3 定稿）：一根<b>偏心斜立</b>的木柄 +
    /// 柄顶横置的宽槌头 + 一道斜撑，读起来是"立在支架上的槌"。
    /// </summary>
    /// <remarks>
    /// 去色缩略图下的判据是<b>不对称</b>：其余五种（球 / 塔 + 雉堞 / 双球连杆 / 四棱锥 / 晶簇）全部关于竖轴对称，
    /// 匠人是唯一整体向一侧倾斜、且顶部是横向实块的轮廓。
    /// 段 A 的占位（四根立柱 + 横梁）刻意换掉：四根立柱在小尺寸下与塔楼子的四枚雉堞太像。
    /// </remarks>
    private static Node3D[] ScaffoldParts(Color body, Color faction)
    {
        const float lean = 14f;
        return
        [
            // 斜撑：从底座右前方斜插到柄的中段，是"支架"感的来源，也是不对称的第二个信号。
            Mesh(new BoxMesh { Size = new Vector3(0.05f, 0.34f, 0.05f) }, Visuals.Matte(body),
                new Vector3(0.14f, BaseHeight + 0.15f, 0.02f), new Vector3(0f, 0f, 34f)),

            // 木柄：偏心（−0.05）且向左倾 14°。
            Mesh(new BoxMesh { Size = new Vector3(0.08f, 0.46f, 0.08f) }, Visuals.Matte(body),
                new Vector3(-0.05f, BaseHeight + 0.23f, 0f), new Vector3(0f, 0f, lean)),

            // 槌头：柄顶的宽横块，阵营色；与柄同角度，整体重心偏向一侧。
            Mesh(new BoxMesh { Size = new Vector3(0.36f, 0.14f, 0.16f) }, Visuals.Matte(faction),
                new Vector3(-0.12f, BaseHeight + 0.49f, 0f), new Vector3(0f, 0f, lean)),
        ];
    }

    /// <summary>
    /// 一条边上的改造标记（artisan-terrain-edit 4.2）：<b>贴在边上</b>，不向任何一格偏移。
    /// <paramref name="upright"/> 为 <c>false</c> 时是三段贴地虚线短条（候选目标），为 <c>true</c> 时另立一道 0.18 高的亮板（已选 / 落成）。
    /// 刻意不是木栅的样子（木栅是三柱两杆的棕木、高 0.30），玩家一眼分得出"这里还没有栅栏、只是可以立"。
    /// </summary>
    public static Node3D EditEdgeMark(bool alongX, StandardMaterial3D material, bool upright)
    {
        var root = new Node3D { Name = "EditEdgeMark" };
        const float dash = 0.24f;
        for (int i = -1; i <= 1; i++)
        {
            float offset = i * 0.34f;
            root.AddChild(Mesh(
                new BoxMesh { Size = alongX ? new Vector3(dash, 0.012f, 0.09f) : new Vector3(0.09f, 0.012f, dash) },
                material,
                alongX ? new Vector3(offset, 0.006f, 0f) : new Vector3(0f, 0.006f, offset)));
        }

        if (upright)
        {
            float length = BoardGeometry.TileSize + 0.06f;
            root.AddChild(Mesh(
                new BoxMesh { Size = alongX ? new Vector3(length, 0.18f, 0.04f) : new Vector3(0.04f, 0.18f, length) },
                material,
                new Vector3(0f, 0.09f, 0f)));
        }

        return root;
    }

    /// <summary>障碍格的低多边形岩石（装饰层，渲染顺序在一切判读信息之下）。</summary>
    public static Node3D Rock(int variant)
    {
        var root = new Node3D { Name = "Rock" };
        StandardMaterial3D material = Visuals.Matte(Visuals.Rock, 1f);
        StandardMaterial3D shade = Visuals.Matte(Visuals.Rock.Darkened(0.14f), 1f);
        root.AddChild(Mesh(new SphereMesh { Radius = 0.30f, Height = 0.56f, RadialSegments = 5, Rings = 2 }, material,
            new Vector3(-0.06f, 0.18f, 0.04f), new Vector3(8f, variant * 37f, 6f), new Vector3(1f, 0.9f, 1.1f)));
        root.AddChild(Mesh(new SphereMesh { Radius = 0.20f, Height = 0.34f, RadialSegments = 5, Rings = 2 }, shade,
            new Vector3(0.20f, 0.11f, -0.14f), new Vector3(0f, variant * 61f, 10f), new Vector3(1f, 0.9f, 1f)));
        root.AddChild(Mesh(new SphereMesh { Radius = 0.13f, Height = 0.20f, RadialSegments = 4, Rings = 1 }, material,
            new Vector3(0.10f, 0.07f, 0.24f), new Vector3(0f, variant * 23f, 0f)));
        return root;
    }

    /// <summary>
    /// 障碍格的松树丛（装饰层）：两三棵叠层锥形松树。障碍格不可落子，树可以长在格中央；最高约 0.75（树尖很细），
    /// 俯角 60° 下向身后投不到半格，不盖住后一格的格心。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Pines(int variant)
    {
        var root = new Node3D { Name = "Pines" };
        StandardMaterial3D trunk = Visuals.Matte(Visuals.Timber, 1f);
        StandardMaterial3D[] canopies =
        [
            Visuals.Matte(Visuals.TreeCanopy, 1f),
            Visuals.Matte(Visuals.TreeCanopy.Lightened(0.12f), 1f),
            Visuals.Matte(Visuals.PineAutumn, 1f),
        ];
        (Vector2 At, float Scale)[] spots = (variant % 3) switch
        {
            0 => [(new(-0.16f, 0.10f), 1.2f), (new(0.18f, -0.12f), 0.9f)],
            1 => [(new(0.02f, 0.02f), 1.25f), (new(-0.24f, -0.20f), 0.75f), (new(0.24f, 0.20f), 0.85f)],
            _ => [(new(0.14f, 0.12f), 1.15f), (new(-0.18f, -0.10f), 1.0f)],
        };
        for (int i = 0; i < spots.Length; i++)
        {
            (Vector2 at, float s) = spots[i];
            // 每七丛里有一棵秋色的（基准图里点缀的黄松），其余两种绿交替。
            StandardMaterial3D canopy = i == 0 && variant % 7 == 3 ? canopies[2] : canopies[(variant + i) % 2];
            var origin = new Vector3(at.X, 0f, at.Y);
            root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.03f * s, BottomRadius = 0.04f * s, Height = 0.14f * s, RadialSegments = 5, Rings = 0 }, trunk,
                origin + new Vector3(0f, 0.07f * s, 0f)));
            for (int tier = 0; tier < 3; tier++)
            {
                float radius = (0.20f - (tier * 0.05f)) * s;
                float height = 0.22f * s;
                root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.001f, BottomRadius = radius, Height = height, RadialSegments = 6, Rings = 0 }, canopy,
                    origin + new Vector3(0f, (0.12f + (tier * 0.13f)) * s + (height * 0.5f), 0f), new Vector3(0f, (variant * 47f) + (tier * 30f), 0f)));
            }
        }

        return root;
    }

    /// <summary>
    /// 障碍格的断柱遗迹（装饰层）：一块石台、一根立着的断柱、一段倒伏的柱身。高度压在 0.55 以内。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Ruins(int variant)
    {
        var root = new Node3D { Name = "Ruins" };
        StandardMaterial3D stone = Visuals.Matte(Visuals.RuinStone, 1f);
        StandardMaterial3D dark = Visuals.Matte(Visuals.RuinStone.Darkened(0.18f), 1f);
        float turn = variant * 90f;
        root.RotationDegrees = new Vector3(0f, turn, 0f);
        root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.62f, 0.07f, 0.62f) }, dark, new Vector3(0f, 0.035f, 0f)));
        root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.24f, 0.06f, 0.24f) }, stone, new Vector3(-0.14f, 0.10f, -0.12f)));
        root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.085f, BottomRadius = 0.095f, Height = 0.36f + (variant % 2 * 0.10f), RadialSegments = 6, Rings = 0 }, stone,
            new Vector3(-0.14f, 0.13f + ((0.36f + (variant % 2 * 0.10f)) * 0.5f), -0.12f)));
        root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 0.34f, RadialSegments = 6, Rings = 0 }, stone,
            new Vector3(0.12f, 0.15f, 0.14f), new Vector3(90f, 35f, 0f)));
        root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.16f, 0.10f, 0.14f) }, dark, new Vector3(0.18f, 0.12f, -0.18f), new Vector3(0f, 25f, 8f)));
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
    /// 荒漠格的点缀（terrain-surfaces 段 1）：两道贴地沙纹 + 一株带侧臂的仙人掌 + 一株矮仙人掌 + 一副贴地兽骨，点缀分放三个角，高度压在 0.25 以内、
    /// 全部落在棋子底座（半径 0.36）之外，不遮挡落点、气与归属标记。仙人掌是"竖柱 + 侧臂"，与林地的锥形小树、沼泽的芦苇轮廓都不同。
    /// 返回节点原点在地砖上表面；<paramref name="variant"/> 决定放哪一组对角与朝向。
    /// </summary>
    public static Node3D Desert(int variant)
    {
        var root = new Node3D { Name = "Desert" };
        StandardMaterial3D cactus = Visuals.Matte(Visuals.Cactus, 1f);
        StandardMaterial3D bone = Visuals.Matte(Visuals.Bone, 1f);
        StandardMaterial3D ripple = Visuals.Matte(Visuals.TileDesert.Darkened(0.16f), 1f);
        float sx = variant % 2 == 0 ? 1f : -1f;

        // 沙纹：两道贴地的深沙色细条，斜穿地砖中部——灰度下也读得出"这块地是沙"，且贴地不遮挡任何标记。
        for (int i = -1; i <= 1; i += 2)
        {
            root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.62f, 0.006f, 0.035f) }, ripple,
                new Vector3(0f, 0.003f, i * 0.12f), new Vector3(0f, 18f * sx, 0f)));
        }

        // 仙人掌：主柱 + 一侧的曲臂（短横段 + 竖段），顶高 0.24；放在一个角上。
        var cactusAt = new Vector3(0.30f * sx, 0f, -0.29f);
        float arm = variant % 3 == 0 ? -1f : 1f;
        root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 0.24f, RadialSegments = 6, Rings = 0 }, cactus,
            cactusAt + new Vector3(0f, 0.12f, 0f)));
        root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.09f, 0.045f, 0.045f) }, cactus,
            cactusAt + new Vector3(0.065f * arm, 0.10f, 0f)));
        root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.035f, Height = 0.11f, RadialSegments = 5, Rings = 0 }, cactus,
            cactusAt + new Vector3(0.11f * arm, 0.15f, 0f)));

        // 第二株矮仙人掌放在对角，只有主柱（顶高 0.14）。
        root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 0.14f, RadialSegments = 6, Rings = 0 }, cactus,
            new Vector3(-0.30f * sx, 0.07f, 0.30f)));

        // 兽骨：两根交叉的细骨贴地横放在第三个角，高 0.035。
        var boneAt = new Vector3(0.28f * sx, 0f, 0.30f);
        float turn = (variant * 37f) % 180f;
        root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.22f, 0.035f, 0.045f) }, bone, boneAt + new Vector3(0f, 0.018f, 0f), new Vector3(0f, turn, 0f)));
        root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.14f, 0.03f, 0.04f) }, bone, boneAt + new Vector3(0f, 0.02f, 0f), new Vector3(0f, turn + 70f, 0f)));
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

    /// <summary>
    /// 程序化直棱柱（低多边形硬边）：<paramref name="profile"/> 是 XY 平面上的凸多边形，沿 Z 轴居中拉伸 <paramref name="depth"/>。
    /// 每个面按"朝外"方向定绕序与法线，不依赖输入多边形的顺逆时针。
    /// </summary>
    private static ArrayMesh Prism(Vector2[] profile, float depth)
    {
        var tool = new SurfaceTool();
        tool.Begin(global::Godot.Mesh.PrimitiveType.Triangles);
        float half = depth * 0.5f;

        Vector2 centroid = Vector2.Zero;
        foreach (Vector2 p in profile)
        {
            centroid += p;
        }

        centroid /= profile.Length;
        for (int i = 1; i + 1 < profile.Length; i++)
        {
            PrismFace(tool, At(profile[0], half), At(profile[i], half), At(profile[i + 1], half), Vector3.Back);
            PrismFace(tool, At(profile[0], -half), At(profile[i], -half), At(profile[i + 1], -half), Vector3.Forward);
        }

        for (int i = 0; i < profile.Length; i++)
        {
            Vector2 a = profile[i];
            Vector2 b = profile[(i + 1) % profile.Length];
            Vector2 edge = b - a;
            var outward2 = new Vector2(edge.Y, -edge.X);
            if (outward2.Dot(((a + b) * 0.5f) - centroid) < 0f)
            {
                outward2 = -outward2;
            }

            var outward = new Vector3(outward2.X, outward2.Y, 0f);
            PrismFace(tool, At(a, half), At(b, half), At(b, -half), outward);
            PrismFace(tool, At(a, half), At(b, -half), At(a, -half), outward);
        }

        return tool.Commit();

        static Vector3 At(Vector2 p, float z) => new(p.X, p.Y, z);
    }

    /// <summary>棱柱的一个三角面：Godot 以顺时针（从正面看）为正面，按期望的朝外法线调整绕序。</summary>
    private static void PrismFace(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        Vector3 normal = outward.Normalized();
        bool counterClockwise = (b - a).Cross(c - a).Dot(outward) > 0f;
        tool.SetNormal(normal);
        tool.AddVertex(a);
        tool.SetNormal(normal);
        tool.AddVertex(counterClockwise ? c : b);
        tool.SetNormal(normal);
        tool.AddVertex(counterClockwise ? b : c);
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
