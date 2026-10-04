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

        // more-pieces-relics D11 / 裁决 ⑩：四种新轮廓。易混对的判据写在各自的 Parts 注释里（灰度对照见 art/more-pieces/README.md）。
        PieceSilhouette.Pennant => PennantParts(body, faction),
        PieceSilhouette.ChainLinks => ChainLinkParts(body, faction),
        PieceSilhouette.CrossedSpears => CrossedSpearParts(body, faction),
        PieceSilhouette.Stele => SteleParts(body, faction),

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
    /// 匠人是唯一整体向一侧倾斜、且顶部是横向实块的轮廓。more-pieces-relics 之后旗手子的旗面也挂在杆的一侧，
    /// 但它的杆竖直——匠人仍是十种里唯一<b>主干斜立</b>的轮廓（见 <see cref="PennantParts"/>）。
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
    /// 旗手子（more-pieces-relics D11）：<b>竖直</b>细高旗杆 + 杆顶一面方旗（阵营色）+ 杆头小球，是十种里最高的轮廓。
    /// </summary>
    /// <remarks>
    /// 与匠人（易混对）的判据：匠人的柄斜 14°、顶上是横置实块、另有斜撑；旗手的杆绝对竖直、细得多（半径 0.025），
    /// 顶部是一块薄旗面挂在杆的一侧、杆顶高出旗面，整体高约 0.9（匠人约 0.63）。旗面朝摄像机（XY 平面），灰度下读成"杆 + 方块"。
    /// </remarks>
    private static Node3D[] PennantParts(Color body, Color faction) =>
    [
        // 杆脚：矮圆台，让细杆立得住。
        Mesh(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.08f, Height = 0.06f, RadialSegments = 8, Rings = 0 },
            Visuals.Matte(body), new Vector3(0f, BaseHeight + 0.03f, 0f)),

        // 旗杆：竖直，不倾斜。
        Mesh(new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.03f, Height = 0.80f, RadialSegments = 6, Rings = 0 },
            Visuals.Matte(body), new Vector3(0f, BaseHeight + 0.40f, 0f)),

        // 杆头小球。
        Mesh(new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 6, Rings = 3 },
            Visuals.Matte(body), new Vector3(0f, BaseHeight + 0.82f, 0f)),

        // 方旗：阵营色薄板，挂在杆右侧、贴近杆顶。
        Mesh(new BoxMesh { Size = new Vector3(0.26f, 0.20f, 0.02f) },
            Visuals.Matte(faction), new Vector3(0.155f, BaseHeight + 0.67f, 0f)),
    ];

    /// <summary>
    /// 铁链子（more-pieces-relics D11）：两枚竖立的长圆环上下相扣，<b>中空</b>——下环正对摄像机（XY 平面），上环侧立（YZ 平面）穿过它。
    /// </summary>
    /// <remarks>
    /// 与连珠子（易混对，双球连杆）的判据：连珠是左右两个<b>实心</b>球 + 一根横杆 + 中柱，轮廓是横放的"哑铃"；
    /// 铁链没有球也没有杆，是竖着叠起的两节环——正面那节中间透出背后的地砖（灰度下"环中有洞"），侧立那节是一道竖直窄椭圆（阵营色）。
    /// 相扣校核（环管中心半径 (0.13 + 0.085) / 2 ≈ 0.108，竖向拉长 1.35 倍）：两环中心竖向相距 0.20，下环顶管在上环环孔内（相距 0.055 &lt; 孔半高 0.115），
    /// 上环底管在下环环孔内，两环互相穿过而不相交。
    /// </remarks>
    private static Node3D[] ChainLinkParts(Color body, Color faction)
    {
        const float outer = 0.13f;
        const float inner = 0.085f;
        const float stretch = 1.35f;
        const float pitch = 0.20f;
        float lowerY = BaseHeight + (outer * stretch) + 0.01f;
        return
        [
            // 下环：绕 X 轴转 90° 立在 XY 平面（正对摄像机），沿竖直方向拉长（局部 Z → 世界 Y）。
            Mesh(new TorusMesh { InnerRadius = inner, OuterRadius = outer, Rings = 12, RingSegments = 6 },
                Visuals.Matte(body), new Vector3(0f, lowerY, 0f), new Vector3(90f, 0f, 0f), new Vector3(1f, 1f, stretch)),

            // 上环：绕 Z 轴转 90° 立在 YZ 平面（侧对摄像机），沿竖直方向拉长（局部 X → 世界 Y），阵营色。
            Mesh(new TorusMesh { InnerRadius = inner, OuterRadius = outer, Rings = 12, RingSegments = 6 },
                Visuals.Matte(faction), new Vector3(0f, lowerY + pitch, 0f), new Vector3(0f, 0f, 90f), new Vector3(stretch, 1f, 1f)),
        ];
    }

    /// <summary>
    /// 哨兵子（more-pieces-relics D11）：两支长矛在正面交叉成 X，矛尖朝上，交叉处一道阵营色绑绳。
    /// </summary>
    /// <remarks>
    /// 十种里唯一的"X"字形：其余要么是竖直体块，要么是单根斜柄（匠人）。两矛关于竖轴镜像对称，与匠人的"整体歪向一侧"分得开。
    /// </remarks>
    private static Node3D[] CrossedSpearParts(Color body, Color faction)
    {
        const float lean = 24f;
        const float shaft = 0.62f;
        float centerY = BaseHeight + 0.31f;
        var parts = new List<Node3D>();
        foreach (float sign in new[] { -1f, 1f })
        {
            float angle = sign * lean;
            float radians = Mathf.DegToRad(angle);
            var up = new Vector3(-Mathf.Sin(radians), Mathf.Cos(radians), 0f);
            parts.Add(Mesh(new BoxMesh { Size = new Vector3(0.035f, shaft, 0.035f) },
                Visuals.Matte(body), new Vector3(0f, centerY, 0f), new Vector3(0f, 0f, angle)));

            // 矛尖：四棱锥，沿矛杆方向接在上端。
            parts.Add(Mesh(new CylinderMesh { TopRadius = 0.001f, BottomRadius = 0.05f, Height = 0.13f, RadialSegments = 4, Rings = 0 },
                Visuals.Matte(body), new Vector3(0f, centerY, 0f) + (up * ((shaft * 0.5f) + 0.06f)), new Vector3(0f, 0f, angle)));
        }

        // 交叉处的绑绳。
        parts.Add(Mesh(new BoxMesh { Size = new Vector3(0.09f, 0.07f, 0.07f) },
            Visuals.Matte(faction), new Vector3(0f, centerY, 0f)));
        return [.. parts];
    }

    /// <summary>
    /// 界碑子（more-pieces-relics D11）：<b>矮宽</b>直立的单块石碑，顶部圆弧，碑面两道阵营色横刻痕，立在一块扁基座上。
    /// </summary>
    /// <remarks>
    /// 与堡垒子（易混对，塔楼）的判据：塔楼是高约 0.7 的粗圆柱 + 顶圈 + 四枚雉堞，横竖差不多粗；
    /// 界碑总高约 0.5、宽 0.52、厚仅 0.11，是十种里最扁的轮廓，顶上是一整道圆弧而不是一圈齿；碑面两道深色横纹。
    /// </remarks>
    private static Node3D[] SteleParts(Color body, Color faction)
    {
        const float width = 0.52f;
        const float slab = 0.26f;
        const float depth = 0.11f;
        const float plinth = 0.05f;
        const float tilt = 14f;

        // 碑体各部件挂在一个支点上整体后仰 14°（绕 X 轴、以碑脚为轴）：60° 俯视下直立薄板的正面只投出一半高，后仰让碑面多露出来，
        // 两道横纹在小尺寸下仍读得出。
        var stone = new Node3D { Position = new Vector3(0f, BaseHeight + plinth, 0f), RotationDegrees = new Vector3(-tilt, 0f, 0f) };
        stone.AddChild(Mesh(new BoxMesh { Size = new Vector3(width, slab, depth) }, Visuals.Matte(body), new Vector3(0f, slab * 0.5f, 0f)));

        // 碑顶圆弧：沿 Z 轴放倒的扁圆柱，下半截埋在碑身里，竖向压成半高。
        stone.AddChild(Mesh(new CylinderMesh { TopRadius = width * 0.5f, BottomRadius = width * 0.5f, Height = depth, RadialSegments = 12, Rings = 0 },
            Visuals.Matte(body), new Vector3(0f, slab, 0f), new Vector3(90f, 0f, 0f), new Vector3(1f, 1f, 0.5f)));

        // 碑面两道横刻痕（阵营色），贴在朝摄像机的一面：浅色碑面上的两条深色横纹，是灰度下界碑独有的纹样。
        foreach (float at in new[] { 0.35f, 0.75f })
        {
            stone.AddChild(Mesh(new BoxMesh { Size = new Vector3(width * 0.72f, 0.045f, 0.01f) },
                Visuals.Matte(faction), new Vector3(0f, slab * at, (depth * 0.5f) + 0.004f)));
        }

        return
        [
            // 扁基座。
            Mesh(new BoxMesh { Size = new Vector3(width + 0.08f, plinth, 0.20f) }, Visuals.Matte(body), new Vector3(0f, BaseHeight + (plinth * 0.5f), 0f)),
            stone,
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

    // ---------- 障碍与装饰（map-elements-v2 段 C 重建）：每件部件里同一材质的几何合成一个网格，摆放与缩放烘进顶点 ----------

    private static Transform3D Yaw(float degrees, bool mirror = false) =>
        new(new Basis(Vector3.Up, Mathf.DegToRad(degrees)).Scaled(new Vector3(mirror ? -1f : 1f, 1f, 1f)), Vector3.Zero);

    /// <summary>
    /// 障碍格的岩石（装饰层，渲染顺序在一切判读信息之下）：一大两小三块多面岩块，各档的朝向、布局与棱面不同。最高约 0.47。
    /// 压扁直接烘进顶点（节点上不带缩放），合批与逐格两种画法受光一致。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Rock(int variant)
    {
        int seed = 201 + variant;
        var mesh = new LowPolyMesh.Builder().Within(Yaw(variant * 60f, mirror: variant % 2 == 1));
        mesh.At(new Vector3(-0.06f, 0.18f, 0.04f), new Vector3(8f, 0f, 6f), new Vector3(1f, 0.9f, 1.1f)).Rock(0.30f, 6, 0.13f, seed);
        mesh.At(new Vector3(0.20f, 0.11f, -0.14f), new Vector3(0f, 31f, 10f), new Vector3(1f, 0.85f, 1f)).Rock(0.20f, 5, 0.15f, seed + 40, 0.88f);
        mesh.At(new Vector3(0.10f, 0.07f, 0.24f), new Vector3(0f, 17f, 0f), new Vector3(1f, 0.8f, 1f)).Rock(0.12f, 4, 0.16f, seed + 80);
        return Part("Rock", (mesh.Commit(), Visuals.Shaded(Visuals.Rock, 1f)));
    }

    /// <summary>
    /// 障碍格的松树丛（装饰层）：两三棵叠层锥形松树。障碍格不可落子，树可以长在格中央；最高约 0.75（树尖很细），
    /// 俯角 60° 下向身后投不到半格，不盖住后一格的格心。树冠三层自下而上渐亮，两种绿靠亮度系数区分；第 3 档里有一棵秋色的（单独一份材质）。
    /// 返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Pines(int variant)
    {
        var trunk = new LowPolyMesh.Builder();
        var green = new LowPolyMesh.Builder();
        var autumn = new LowPolyMesh.Builder();
        bool hasAutumn = variant == 3;
        (Vector2 At, float Scale)[] spots = (variant % 3) switch
        {
            0 => [(new(-0.16f, 0.10f), 1.2f), (new(0.18f, -0.12f), 0.9f)],
            1 => [(new(0.02f, 0.02f), 1.25f), (new(-0.24f, -0.20f), 0.75f), (new(0.24f, 0.20f), 0.85f)],
            _ => [(new(0.14f, 0.12f), 1.15f), (new(-0.18f, -0.10f), 1.0f)],
        };
        for (int i = 0; i < spots.Length; i++)
        {
            (Vector2 at, float s) = spots[i];
            var origin = new Vector3(at.X, 0f, at.Y);
            trunk.At(origin + new Vector3(0f, 0.07f * s, 0f)).Cone(0.04f * s, 0.03f * s, 0.14f * s, 5, 1f, variant + i, 0.05f);
            LowPolyMesh.Builder canopy = i == 0 && hasAutumn ? autumn : green;
            float tone = (variant + i) % 2 == 0 ? 0.96f : 1.07f;
            for (int tier = 0; tier < 3; tier++)
            {
                float radius = (0.20f - (tier * 0.05f)) * s;
                float height = 0.22f * s;
                canopy.At(origin + new Vector3(0f, ((0.12f + (tier * 0.13f)) * s) + (height * 0.5f), 0f), new Vector3(0f, (variant * 47f) + (tier * 30f), 0f))
                    .Cone(radius, 0f, height, 6, tone * (0.94f + (0.06f * tier)), (variant * 8) + (i * 3) + tier, 0.05f);
            }
        }

        var meshes = new List<(Mesh, Material)>
        {
            (trunk.Commit(), Visuals.Shaded(Visuals.Timber, 1f)),
            (green.Commit(), Visuals.Shaded(Visuals.TreeCanopy, 1f)),
        };
        if (hasAutumn)
        {
            meshes.Add((autumn.Commit(), Visuals.Shaded(Visuals.PineAutumn, 1f)));
        }

        return Part("Pines", [.. meshes]);
    }

    /// <summary>
    /// 障碍格的断柱遗迹（装饰层）：一块倒角石台、柱础、一根立着的断柱、一段倒伏的柱身、一块碎石与几粒石屑。高度压在 0.65 以内。
    /// 各档整体转向不同、前四档与后两档互为镜像、断柱高矮交替。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Ruins(int variant)
    {
        int seed = 301 + variant;
        var mesh = new LowPolyMesh.Builder().Within(Yaw(variant * 90f, mirror: variant >= 4));
        float column = 0.36f + (variant % 2 * 0.10f);
        mesh.At(new Vector3(0f, 0.07f, 0f)).Slab(new Vector3(0.62f, 0.07f, 0.62f), 0.02f, 0.84f, seed, 0.03f);
        mesh.At(new Vector3(-0.14f, 0.10f, -0.12f)).Box(new Vector3(0.24f, 0.06f, 0.24f), 1f);
        mesh.At(new Vector3(-0.14f, 0.13f + (column * 0.5f), -0.12f)).Cone(0.095f, 0.082f, column, 6, 1f, seed, 0.05f);
        mesh.At(new Vector3(-0.14f, 0.13f + column, -0.12f), new Vector3(14f, 20f, 0f), new Vector3(1f, 0.5f, 1f)).Rock(0.075f, 5, 0.2f, seed + 20, 0.92f);
        mesh.At(new Vector3(0.12f, 0.15f, 0.14f), new Vector3(90f, 35f, 0f)).Cone(0.08f, 0.08f, 0.34f, 6, 0.97f, seed + 7, 0.05f);
        mesh.At(new Vector3(0.18f, 0.12f, -0.18f), new Vector3(0f, 25f, 8f)).Box(new Vector3(0.16f, 0.10f, 0.14f), 0.84f);
        mesh.At(new Vector3(-0.20f, 0.085f, 0.20f), default, new Vector3(1f, 0.6f, 1f)).Rock(0.04f, 4, 0.2f, seed + 40, 0.9f);
        mesh.At(new Vector3(0.02f, 0.085f, -0.24f), default, new Vector3(1f, 0.6f, 1f)).Rock(0.03f, 4, 0.2f, seed + 60, 0.9f);
        return Part("Ruins", (mesh.Commit(), Visuals.Shaded(Visuals.RuinStone, 1f)));
    }

    /// <summary>
    /// 林地格的三棵小树（装饰层）：放在地砖三个角上、树冠半径 0.09、高不过 0.28，落在棋子底座（半径 0.36）之外，
    /// 不遮挡该格的落点、气与归属标记（visual-style-baseline「装饰不遮挡判读」）。树冠两层。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Trees(int variant)
    {
        var trunk = new LowPolyMesh.Builder();
        var canopy = new LowPolyMesh.Builder();
        Vector2[] corners = [new(-0.31f, -0.30f), new(0.30f, -0.29f), new(-0.02f, 0.31f)];
        for (int i = 0; i < corners.Length; i++)
        {
            float scale = 0.85f + (0.15f * (((variant + i) % 3) / 2f));
            Vector3 at = new(corners[i].X, 0f, corners[i].Y);
            var turn = new Vector3(0f, (variant * 53f) + (i * 40f), 0f);
            trunk.At(at + new Vector3(0f, 0.04f, 0f)).Cone(0.025f, 0.02f, 0.08f, 5);
            canopy.At(at + new Vector3(0f, 0.08f + (0.065f * scale), 0f), turn).Cone(0.09f * scale, 0f, 0.13f * scale, 5, 0.95f, (variant * 4) + i, 0.05f);
            canopy.At(at + new Vector3(0f, 0.08f + (0.14f * scale), 0f), turn + new Vector3(0f, 36f, 0f)).Cone(0.062f * scale, 0f, 0.12f * scale, 5, 1.05f, (variant * 4) + i + 16, 0.05f);
        }

        return Part("Trees", (trunk.Commit(), Visuals.Shaded(Visuals.Timber, 1f)), (canopy.Commit(), Visuals.Shaded(Visuals.TreeCanopy, 1f)));
    }

    /// <summary>
    /// 荒漠格的点缀（terrain-surfaces 段 1）：两道贴地的弯沙纹 + 一株带侧臂的仙人掌 + 一株矮仙人掌 + 一副贴地兽骨，点缀分放三个角，高度压在 0.25 以内、
    /// 全部落在棋子底座（半径 0.36）之外，不遮挡落点、气与归属标记。仙人掌是"竖柱 + 侧臂"，与林地的锥形小树、沼泽的芦苇轮廓都不同。
    /// 返回节点原点在地砖上表面；<paramref name="variant"/> 决定放哪一组对角与朝向。
    /// </summary>
    public static Node3D Desert(int variant)
    {
        float sx = variant % 2 == 0 ? 1f : -1f;
        var ripple = new LowPolyMesh.Builder();
        var cactus = new LowPolyMesh.Builder();
        var bone = new LowPolyMesh.Builder();

        // 沙纹：两道贴地的深沙色弯条，斜穿地砖中部——灰度下也读得出"这块地是沙"，且贴地不遮挡任何标记。
        for (int i = -1; i <= 1; i += 2)
        {
            ripple.At(new Vector3(0f, 0.004f, i * 0.12f), new Vector3(0f, 18f * sx, 0f));
            ripple.Ribbon([new(-0.31f, 0f, 0.02f * i), new(-0.10f, 0f, -0.025f * i), new(0.10f, 0f, 0.025f * i), new(0.31f, 0f, -0.02f * i)], 0.035f, Vector3.Up);
        }

        // 仙人掌：主柱 + 一侧的曲臂（短横段 + 竖段），顶高 0.24；放在一个角上。
        var cactusAt = new Vector3(0.30f * sx, 0f, -0.29f);
        float arm = variant % 3 == 0 ? -1f : 1f;
        cactus.At(cactusAt + new Vector3(0f, 0.12f, 0f)).Cone(0.06f, 0.045f, 0.24f, 6, 1f, variant, 0.06f);
        cactus.At(cactusAt + new Vector3(0.065f * arm, 0.10f, 0f)).Box(new Vector3(0.09f, 0.045f, 0.045f), 0.95f);
        cactus.At(cactusAt + new Vector3(0.11f * arm, 0.15f, 0f)).Cone(0.035f, 0.026f, 0.11f, 5, 1.04f, variant + 9, 0.06f);

        // 第二株矮仙人掌放在对角，只有主柱（顶高 0.14）。
        cactus.At(new Vector3(-0.30f * sx, 0.07f, 0.30f)).Cone(0.05f, 0.036f, 0.14f, 6, 0.97f, variant + 18, 0.06f);

        // 兽骨：两根交叉的细骨贴地横放在第三个角，高 0.035。
        var boneAt = new Vector3(0.28f * sx, 0f, 0.30f);
        float turn = (variant * 37f) % 180f;
        bone.At(boneAt + new Vector3(0f, 0.018f, 0f), new Vector3(0f, turn, 0f)).Box(new Vector3(0.22f, 0.035f, 0.045f), 1f);
        bone.At(boneAt + new Vector3(0f, 0.02f, 0f), new Vector3(0f, turn + 70f, 0f)).Box(new Vector3(0.14f, 0.03f, 0.04f), 0.95f);
        return Part(
            "Desert",
            (ripple.Commit(), Visuals.Shaded(Visuals.TileDesert.Darkened(0.16f), 1f)),
            (cactus.Commit(), Visuals.Shaded(Visuals.Cactus, 1f)),
            (bone.Commit(), Visuals.Shaded(Visuals.Bone, 1f)));
    }

    /// <summary>
    /// 沼泽格的点缀（terrain-surfaces 段 2）：两三块贴地的不规则积水斑块 + 两个角上的芦苇丛（细长竖条成簇，顶高 ≤ 0.24，其中两根带深色穗头）。
    /// 刻意不用锥形树冠（visual-style-baseline「沼泽不被读成林地」）；芦苇在棋子底座（半径 0.36）之外，水洼贴地，不遮挡判读。
    /// 返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Marsh(int variant)
    {
        float sx = variant % 2 == 0 ? 1f : -1f;
        var puddle = new LowPolyMesh.Builder();
        var reed = new LowPolyMesh.Builder();

        (Vector2 At, float R)[] puddles = variant % 3 == 0
            ? [(new(-0.10f, 0.05f), 0.20f), (new(0.16f, -0.10f), 0.12f)]
            : [(new(0.08f, 0.10f), 0.17f), (new(-0.14f, -0.08f), 0.14f), (new(0.18f, -0.18f), 0.08f)];
        for (int i = 0; i < puddles.Length; i++)
        {
            (Vector2 at, float r) = puddles[i];
            puddle.At(new Vector3(at.X * sx, 0.006f, at.Y), new Vector3(0f, (variant * 29f) + (i * 50f), 0f), new Vector3(1f, 1f, 0.7f))
                .Disc(r, 7, (variant * 8) + i, 0.16f);
        }

        // 芦苇：两丛，各五根细竖条，略向外倾。
        foreach (Vector3 clump in new[] { new Vector3(0.31f * sx, 0f, -0.30f), new Vector3(-0.30f * sx, 0f, 0.31f) })
        {
            for (int k = 0; k < 5; k++)
            {
                float a = (k * 72f) + (variant * 13f);
                float rad = Mathf.DegToRad(a);
                float height = 0.16f + (0.02f * ((k + variant) % 4));
                var offset = new Vector3(Mathf.Cos(rad) * 0.035f, 0f, Mathf.Sin(rad) * 0.035f);
                var lean = new Vector3(Mathf.Sin(rad) * 8f, 0f, -Mathf.Cos(rad) * 8f);
                reed.At(clump + offset + new Vector3(0f, height * 0.5f, 0f), lean).Box(new Vector3(0.018f, height, 0.018f), 1f + (0.05f * LowPolyMesh.Signed(variant, k)));
                if (k % 3 == 0)
                {
                    reed.At(clump + offset + new Vector3(0f, height - 0.02f, 0f), lean).Box(new Vector3(0.03f, 0.05f, 0.03f), 0.62f);
                }
            }
        }

        return Part("Marsh", (puddle.Commit(), Visuals.Shaded(Visuals.MarshPuddle, 0.35f)), (reed.Commit(), Visuals.Shaded(Visuals.Reed, 1f)));
    }

    /// <summary>
    /// 岩台格的石面细节（terrain-surfaces 段 3）：沿地砖内缘一圈 0.03 高的倒角石沿（"加厚边缘"）+ 三四道贴地的折线裂纹 + 两颗碎石。
    /// 石沿只高 0.03、远低于一层地砖（高差 1 的缓坡侧面明显更高），读起来是"一块石台"而不是高一层（visual-style-baseline「岩台不被读成高一层」）。
    /// 碎石在棋子底座（半径 0.36）之外，裂纹贴地，不遮挡判读。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Crag(int variant)
    {
        const float half = BoardGeometry.TileSize * 0.5f;
        const float band = 0.05f;
        const float lip = 0.03f;
        var rim = new LowPolyMesh.Builder();
        var crack = new LowPolyMesh.Builder();

        // 石沿：四条贴内缘的矮条，各分成两三段略有明暗的石块。
        foreach ((Vector3 at, Vector3 size, int id) in new[]
        {
            (new Vector3(0f, lip, -half + (band * 0.5f)), new Vector3(BoardGeometry.TileSize, lip, band), 0),
            (new Vector3(0f, lip, half - (band * 0.5f)), new Vector3(BoardGeometry.TileSize, lip, band), 1),
            (new Vector3(-half + (band * 0.5f), lip, 0f), new Vector3(band, lip, BoardGeometry.TileSize - (2f * band)), 2),
            (new Vector3(half - (band * 0.5f), lip, 0f), new Vector3(band, lip, BoardGeometry.TileSize - (2f * band)), 3),
        })
        {
            bool alongX = size.X > size.Z;
            float length = alongX ? size.X : size.Z;
            float cut = 0.18f * LowPolyMesh.Signed(401 + variant, id) * length;
            foreach ((float from, float to) in new[] { (-length * 0.5f, cut), (cut, length * 0.5f) })
            {
                float mid = (from + to) * 0.5f;
                rim.At(at + (alongX ? new Vector3(mid, 0f, 0f) : new Vector3(0f, 0f, mid)));
                rim.Slab(alongX ? new Vector3(to - from, lip, band) : new Vector3(band, lip, to - from), 0.008f, 1f + (0.05f * LowPolyMesh.Signed(421 + variant, (id * 2) + (from < cut ? 0 : 1))));
            }
        }

        // 裂纹：三四道贴地的折线。
        float turn = (variant * 41f) % 90f;
        (Vector2 At, float Len, float Angle)[] cracks =
        [
            (new(-0.12f, -0.08f), 0.26f, 25f), (new(0.02f, -0.02f), 0.18f, -40f), (new(0.12f, 0.12f), 0.22f, 70f), (new(-0.16f, 0.16f), 0.14f, -10f),
        ];
        for (int i = 0; i < cracks.Length - (variant % 2); i++)
        {
            (Vector2 at, float len, float angle) = cracks[i];
            float kink = 0.03f * LowPolyMesh.Signed(441 + variant, i);
            crack.At(new Vector3(at.X, 0.004f, at.Y), new Vector3(0f, angle + turn, 0f));
            crack.Ribbon([new(-len * 0.5f, 0f, 0f), new(-len * 0.1f, 0f, kink), new(len * 0.2f, 0f, -kink), new(len * 0.5f, 0f, 0f)], 0.022f, Vector3.Up);
        }

        // 碎石：两颗小多面体，放在两个角上。
        float sx = variant % 2 == 0 ? 1f : -1f;
        crack.At(new Vector3(0.30f * sx, 0.03f, -0.29f), new Vector3(0f, variant * 31f, 0f), new Vector3(1f, 0.7f, 1f)).Rock(0.05f, 5, 0.18f, 461 + variant, 1.25f);
        crack.At(new Vector3(-0.29f * sx, 0.02f, 0.30f), default, new Vector3(1f, 0.7f, 1f)).Rock(0.035f, 4, 0.18f, 481 + variant, 1.25f);
        return Part("Crag", (rim.Commit(), Visuals.Shaded(Visuals.TileCrag.Lightened(0.08f), 1f)), (crack.Commit(), Visuals.Shaded(Visuals.CragCrack, 1f)));
    }

    /// <summary>
    /// 浅滩格的水面细节（terrain-surfaces 段 4）：地砖本身是与同层齐平的浅青水色，上面加两道近白的弯水纹 + 五六颗贴地鹅卵石。
    /// 与深水的区分靠两条通道：高度（深水水面低于地砖、浅滩齐平）与纹理（深水没有鹅卵石）（visual-style-baseline「浅滩不被读成深水」）。
    /// 全部贴地（≤ 0.03），不遮挡落点、气点与"浅滩：不算气"的标记。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Shallows(int variant)
    {
        float sx = variant % 2 == 0 ? 1f : -1f;
        var ripple = new LowPolyMesh.Builder();
        var pebble = new LowPolyMesh.Builder();

        // 水纹：两道斜向的弯细条。
        for (int i = -1; i <= 1; i += 2)
        {
            ripple.At(new Vector3(0.06f * i * sx, 0.003f, 0.14f * i), new Vector3(0f, (-20f * sx) + (i * 8f), 0f));
            ripple.Ribbon([new(-0.17f, 0f, 0.012f * i), new(-0.05f, 0f, -0.014f * i), new(0.06f, 0f, 0.014f * i), new(0.17f, 0f, -0.012f * i)], 0.018f, Vector3.Up);
        }

        // 鹅卵石：沿地砖外圈散放的扁石，避开格心的棋子底座。
        Vector2[] spots = [new(-0.33f, -0.18f), new(-0.30f, 0.26f), new(0.32f, -0.30f), new(0.20f, 0.34f), new(0.34f, 0.10f), new(-0.10f, -0.35f)];
        for (int i = 0; i < spots.Length - (variant % 2); i++)
        {
            float r = 0.028f + (0.008f * ((i + variant) % 3));
            pebble.At(new Vector3(spots[i].X * sx, r * 0.25f, spots[i].Y), new Vector3(0f, (variant * 23f) + (i * 37f), 0f), new Vector3(1.3f, 0.4f, 1f))
                .Rock(r, 5, 0.14f, 501 + (variant * 8) + i, 1f + (0.05f * LowPolyMesh.Signed(521 + variant, i)));
        }

        return Part("Shallows", (ripple.Commit(), Visuals.Shaded(Visuals.Ripple, 0.4f)), (pebble.Commit(), Visuals.Shaded(Visuals.Pebble, 1f)));
    }

    /// <summary>
    /// 一段栅栏（terrain-model 边属性；map-elements-v2 段 B 重建）：三根带柱帽的立柱 + 两道略下垂的横杆，沿一格边长立起，厚度只有 0.05，
    /// 放在两格之间的缝上，不占任一格的落点。<paramref name="alongX"/> 为 <c>true</c> 时沿 X 轴（两格上下相邻），否则沿 Z 轴。
    /// 外形不超出改前的包围盒（长 0.96、厚 0.05、高 0.30）。整件一个网格、一份木色材质，明暗靠亮度系数。返回节点原点在缝中心、地砖上表面。
    /// </summary>
    public static Node3D Fence(bool alongX)
    {
        var mesh = new LowPolyMesh.Builder();
        Transform3D frame = alongX ? Transform3D.Identity : new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f), Vector3.Zero);
        void At(Vector3 position, float tiltDegrees = 0f) =>
            mesh.At(frame * new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad(tiltDegrees)), position));

        const float post = 0.40f;
        for (int i = -1; i <= 1; i++)
        {
            At(new Vector3(i * post, 0.135f, 0f));
            mesh.Box(new Vector3(0.042f, 0.27f, 0.042f), 0.97f + (0.04f * i));
            At(new Vector3(i * post, 0.285f, 0f));
            mesh.Box(new Vector3(0.05f, 0.03f, 0.05f), 1.09f);
        }

        // 横杆：每一跨分成两段，中点比两端低一点（略下垂）；两端各探出一小截。
        const float sag = 0.014f;
        float tilt = Mathf.RadToDeg(Mathf.Atan2(sag, post * 0.5f));
        foreach (float y in new[] { 0.11f, 0.225f })
        {
            for (int span = -1; span <= 0; span++)
            {
                float left = span * post;
                At(new Vector3(left + (post * 0.25f), y - (sag * 0.5f), 0f), -tilt);
                mesh.Box(new Vector3(post * 0.5f, 0.032f, 0.028f), 0.93f);
                At(new Vector3(left + (post * 0.75f), y - (sag * 0.5f), 0f), tilt);
                mesh.Box(new Vector3(post * 0.5f, 0.032f, 0.028f), 0.93f);
            }

            foreach (int end in new[] { -1, 1 })
            {
                At(new Vector3(end * (post + 0.04f), y, 0f));
                mesh.Box(new Vector3(0.08f, 0.032f, 0.028f), 0.93f);
            }
        }

        return Part("Fence", (mesh.Commit(), Visuals.Shaded(Visuals.Timber, 1f)));
    }

    /// <summary>
    /// 预置桥（terrain-model 设施；map-elements-v2 段 B 重建）：六块带缝的桥板 + 两侧低栏杆 + 板下纵梁与四根桥墩，铺在深水格上，
    /// 桥面与同层地砖齐平——桥格是普通可落子格，棋子与标记照常放在面上。栏杆贴在两侧边缘（棋子底座之外）、高 0.125，不遮挡落点。
    /// 通行方向沿 X 轴（桥板横铺、栏杆在 ±Z 两侧）；棋盘按两头接的是哪两格把它转到位。
    /// 水面以上的外形不超出改前的包围盒（0.9 见方、高 0.13）；桥墩在桥面之下探进水里。返回节点原点在桥面（地砖上表面）。
    /// </summary>
    public static Node3D Bridge()
    {
        const float half = BoardGeometry.TileSize * 0.5f;
        var deck = new LowPolyMesh.Builder();
        const int planks = 6;
        float pitch = BoardGeometry.TileSize / planks;
        for (int i = 0; i < planks; i++)
        {
            // 桥板之间留 0.014 的缝，露出下面的纵梁；各板亮度略有出入。
            deck.At(new Vector3(-half + ((i + 0.5f) * pitch), -0.022f, 0f));
            deck.Box(new Vector3(pitch - 0.014f, 0.044f, BoardGeometry.TileSize - 0.05f), 1f + (0.06f * LowPolyMesh.Signed(91, i)));
        }

        var timber = new LowPolyMesh.Builder();
        foreach (float z in new[] { -0.28f, 0.28f })
        {
            timber.At(new Vector3(0f, -0.062f, z));
            timber.Box(new Vector3(BoardGeometry.TileSize, 0.036f, 0.07f), 0.82f);
        }

        foreach (float z in new[] { -half + 0.03f, half - 0.03f })
        {
            // 栏杆：两端立柱 + 一道扶手。
            foreach (float x in new[] { -half + 0.04f, half - 0.04f })
            {
                timber.At(new Vector3(x, 0.035f, z));
                timber.Box(new Vector3(0.06f, 0.16f, 0.06f), 1f);
                timber.At(new Vector3(x * 0.78f, -0.115f, z * 0.86f));
                timber.Box(new Vector3(0.07f, 0.11f, 0.07f), 0.78f);
            }

            timber.At(new Vector3(0f, 0.105f, z));
            timber.Box(new Vector3(BoardGeometry.TileSize - 0.06f, 0.035f, 0.035f), 1.05f);
            timber.At(new Vector3(0f, 0.045f, z));
            timber.Box(new Vector3(BoardGeometry.TileSize - 0.14f, 0.022f, 0.022f), 0.92f);
        }

        return Part("Bridge", (deck.Commit(), Visuals.Shaded(Visuals.BridgeDeck, 1f)), (timber.Commit(), Visuals.Shaded(Visuals.Timber, 1f)));
    }

    // ---------- 水系（map-elements-v2 段 B） ----------

    /// <summary>
    /// 河床（深水格的水体）：铺满整格的一块，顶面分成几个亮度略有出入的小面（水深浅的变化）。水是不透明的，所以河床就是看得见的那片水色；
    /// 流纹层照旧盖在上面。原点在水格的地砖上表面中心，水面比它低 <see cref="BoardView.WaterDrop"/>（留 0.008 防止与底座共面闪烁）。材质由棋盘覆盖为全图共用的水色。
    /// </summary>
    public static Node3D WaterBed(int variant) => Single(
        "WaterBed",
        LowPolyMesh.BeveledSlab(new Vector3(BoardGeometry.CellSize, BoardGeometry.TileHeight, BoardGeometry.CellSize), 0f, seed: 71 + variant, facet: 0.03f),
        Visuals.Shaded(Colors.White, 0.55f),
        new Vector3(0f, -BoardView.WaterDrop + 0.008f, 0f));

    /// <summary>河岸石沿占水格边缘的宽度（design O-2：一格宽的河两侧各让出这么多，水面仍宽 0.88）。</summary>
    public const float BankLipDepth = 0.06f;

    /// <summary>
    /// 河岸石沿（一条边一件）：沿水格的一条边排开的四五块小石，只占水格边缘 <see cref="BankLipDepth"/>，顶面在水面之上、地砖上表面之下。
    /// 原点在这条边的中点、地砖上表面高度；边沿 X 轴，水在 +Z 一侧。
    /// </summary>
    public static Node3D BankLip(int variant)
    {
        var mesh = new LowPolyMesh.Builder();
        int seed = 101 + variant;
        int stones = 4 + (variant % 2);
        float cursor = -0.5f;
        for (int i = 0; i < stones; i++)
        {
            float next = i == stones - 1 ? 0.5f : -0.5f + ((i + 1f + (0.28f * LowPolyMesh.Signed(seed, i))) / stones);
            float top = -0.028f - (0.022f * LowPolyMesh.Unit(seed, 16 + i));
            float depth = BankLipDepth * (0.8f + (0.2f * LowPolyMesh.Unit(seed, 32 + i)));
            mesh.At(new Vector3((cursor + next) * 0.5f, top, depth * 0.5f));
            mesh.Slab(new Vector3(next - cursor - 0.012f, BoardView.WaterDrop + top + 0.02f, depth), 0.012f, 1f + (0.07f * LowPolyMesh.Signed(seed, 48 + i)));
            cursor = next;
        }

        return Part("BankLip", (mesh.Commit(), Visuals.Shaded(Visuals.Pebble, 1f)));
    }

    // ---------- 地块部件（map-elements-v2 段 A）：单网格，由 BoardView 取网格逐格摆放 ----------

    /// <summary>砖缝衬底的厚度。</summary>
    public const float LinerHeight = 0.03f;

    /// <summary>
    /// 地砖顶板（七种地表共用）：0.9 见方、厚 <see cref="BoardGeometry.TileHeight"/>，顶面四边倒角，顶面分成几个带亮度细差的小面。
    /// 原点在地砖上表面中心。材质是中性白，棋盘按底色换成共用的那一份。
    /// </summary>
    public static Node3D TileTop(int variant) => Single(
        "TileTop",
        LowPolyMesh.BeveledSlab(new Vector3(BoardGeometry.TileSize, BoardGeometry.TileHeight, BoardGeometry.TileSize), 0.045f, seed: 11 + variant, facet: 0.035f),
        Visuals.Shaded(Colors.White));

    /// <summary>第 1 层侧面（缓坡）：铺满整格、高一层的土色块，三条土层。原点在这层带的顶面中心。</summary>
    public static Node3D SideSlope(int variant) => Single(
        "SideSlope",
        LowPolyMesh.Strata(new Vector3(BoardGeometry.CellSize, BoardGeometry.LayerHeight, BoardGeometry.CellSize), bands: 3, jitter: 0.018f, seed: 31 + variant),
        Visuals.Shaded(Visuals.SlopeSide));

    /// <summary>第 2 层侧面（崖壁）：岩灰块，四条岩层、顶边一圈外突的岩沿、每面两道竖向岩缝——读起来比缓坡陡。原点同上。</summary>
    public static Node3D SideCliff(int variant) => Single(
        "SideCliff",
        LowPolyMesh.Strata(new Vector3(BoardGeometry.CellSize, BoardGeometry.LayerHeight, BoardGeometry.CellSize), bands: 4, jitter: 0.03f, seed: 51 + variant, ledge: true, cracks: 2),
        Visuals.Shaded(Visuals.CliffSide));

    /// <summary>砖缝衬底：铺满整格的薄板，原点在衬底中心。材质由棋盘按底色覆盖。</summary>
    public static Node3D Liner() => Single(
        "Liner",
        LowPolyMesh.BeveledSlab(new Vector3(BoardGeometry.CellSize, LinerHeight, BoardGeometry.CellSize), 0f),
        Visuals.Shaded(Colors.White),
        new Vector3(0f, LinerHeight * 0.5f, 0f));

    /// <summary>场景格铺面：铺满整格、不倒角（相邻场景格连成一片，不出现格线）。原点在上表面中心。材质由棋盘覆盖。</summary>
    public static Node3D SceneSlab() => Single(
        "SceneSlab",
        LowPolyMesh.BeveledSlab(new Vector3(BoardGeometry.CellSize, BoardGeometry.TileHeight, BoardGeometry.CellSize), 0f),
        Visuals.Shaded(Colors.White));

    private static Node3D Single(string name, Mesh mesh, Material material, Vector3 position = default)
    {
        var root = new Node3D { Name = name };
        root.AddChild(Mesh(mesh, material, position));
        return root;
    }

    /// <summary>多网格部件：根下一层，每个网格一个节点（各带一份材质），都在原点、不带变换——摆放已烘进顶点。</summary>
    private static Node3D Part(string name, params (Mesh Mesh, Material Material)[] meshes)
    {
        var root = new Node3D { Name = name };
        foreach ((Mesh mesh, Material material) in meshes)
        {
            root.AddChild(Mesh(mesh, material, Vector3.Zero));
        }

        return root;
    }

    private static ArrayMesh Prism(Vector2[] profile, float depth) => LowPolyMesh.Prism(profile, depth);

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
