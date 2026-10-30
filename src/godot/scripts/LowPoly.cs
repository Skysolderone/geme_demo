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

    // —— 障碍与装饰（map-elements-v2 段 C）：用 LowPolyMesh 拼成按材质合并的网格，节点名写死，根下一层各一个 MeshInstance3D
    //    （场景格的合批 NewPartBatch 要求这个形状）。变体号先按档数取模，程序生成与资源同形；形状变化只取自整数散列与变体号。

    /// <summary>岩石档数（棋盘内岩石 + 场景格巨石共用）。</summary>
    public const int RockVariants = 6;

    /// <summary>林地小树档数（每档空出一个不同的格角）。</summary>
    public const int TreesVariants = 4;

    /// <summary>荒漠点缀档数。</summary>
    public const int DesertVariants = 6;

    /// <summary>沼泽点缀档数。</summary>
    public const int MarshVariants = 6;

    /// <summary>岩台点缀档数。</summary>
    public const int CragVariants = 4;

    /// <summary>浅滩点缀档数。</summary>
    public const int ShallowsVariants = 6;

    /// <summary>草丛档数（map-restyle 段C；每档对应一个格角，角序同林地小树）。</summary>
    public const int GrassVariants = 4;

    /// <summary>水晶簇档数。</summary>
    public const int CrystalVariants = 3;

    /// <summary>火盆档数。</summary>
    public const int BrazierVariants = 2;

    /// <summary>石拱门档数。</summary>
    public const int ArchVariants = 2;

    /// <summary>宝箱档数。</summary>
    public const int ChestVariants = 2;

    /// <summary>
    /// 岩石（棋盘内岩石格与场景格巨石）：一大一中两块多面岩（大块浅、中块深）+ 一块小石 + 两粒碎石，挤在一格之内、底部没入地砖，
    /// 读作"一格岩石"——灰色、块状、没有任何锥形或绿色，不会被读成林地。返回节点原点在地砖上表面。
    /// </summary>
    /// <remarks>
    /// 两个 surface：浅色岩（<see cref="Visuals.Rock"/>）与深色岩（同色压暗 0.14），顶点色只当亮度系数（多面岩自带 0.94–1.0 的面差）。
    /// 外形采用更粗的岩面与不对称轮廓；整组按变体绕 Y 每档转 60°，保持在地砖范围内。
    /// </remarks>
    public static Node3D Rock(int variant)
    {
        int v = Wrap(variant, RockVariants);
        int seed = 701 + (v * 16);
        var turn = new Basis(Vector3.Up, Mathf.DegToRad((v * 60f) + 15f));

        // 位置在格内按散列错开至多 ±0.015；每块各自绕 Y 转一个散列角，岩块的抖动种子逐块不同。
        Transform3D At(float x, float y, float z, int salt) => new(
            turn * new Basis(Vector3.Up, LowPolyMesh.Unit(seed, salt) * Mathf.Tau),
            turn * new Vector3(x + (0.015f * LowPolyMesh.Signed(seed, salt + 1)), y, z + (0.015f * LowPolyMesh.Signed(seed, salt + 2))));

        var light = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.FacetRock(0.27f, 6, 0.20f, seed, new Vector3(1.10f, 0.92f, 0.96f)), At(-0.05f, 0.16f, 0.03f, 20)),
            (LowPolyMesh.FacetRock(0.12f, 6, 0.18f, seed + 2, new Vector3(1.1f, 0.85f, 1.0f)), At(0.10f, 0.07f, 0.25f, 30)),
            (LowPolyMesh.FacetRock(0.05f, 5, 0.25f, seed + 3, new Vector3(1.2f, 0.7f, 1.0f)), At(-0.27f, 0.025f, 0.22f, 40)),
        };
        var dark = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.FacetRock(0.17f, 5, 0.22f, seed + 1, new Vector3(1.08f, 0.95f, 1.00f)), At(0.17f, 0.11f, -0.12f, 50)),
            (LowPolyMesh.FacetRock(0.06f, 5, 0.25f, seed + 4, new Vector3(1.2f, 0.75f, 1.0f)), At(-0.22f, 0.03f, -0.24f, 60)),
        };

        var root = new Node3D { Name = "Rock" };
        root.AddChild(Merged("Stone", light, Visuals.Rock));
        root.AddChild(Merged("Shade", dark, Visuals.Rock.Darkened(0.14f)));
        return root;
    }

    /// <summary>
    /// 障碍格的松树丛（装饰层）：两三棵叠层锥形松树。障碍格不可落子，树可以长在格中央；最高约 0.75（树尖很细），
    /// 俯角 60° 下向身后投不到半格，不盖住后一格的格心。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Pines(int variant)
    {
        int v = Wrap(variant, 7);
        (Vector2 At, float Scale)[] spots = (v % 3) switch
        {
            0 => [(new(-0.16f, 0.10f), 1.15f), (new(0.18f, -0.12f), 0.88f)],
            1 => [(new(0.02f, 0.02f), 1.22f), (new(-0.24f, -0.20f), 0.74f), (new(0.24f, 0.20f), 0.82f)],
            _ => [(new(0.14f, 0.12f), 1.12f), (new(-0.18f, -0.10f), 0.98f)],
        };
        var trunks = new List<(ArrayMesh, Transform3D)>();
        var teal = new List<(ArrayMesh, Transform3D)>();
        var light = new List<(ArrayMesh, Transform3D)>();
        var dusk = new List<(ArrayMesh, Transform3D)>();
        for (int i = 0; i < spots.Length; i++)
        {
            (Vector2 at, float s) = spots[i];
            var origin = new Vector3(at.X, 0f, at.Y);
            int seed = 1501 + (v * 17) + i;
            float yaw = LowPolyMesh.Unit(seed, 0) * Mathf.Tau;
            trunks.Add((LowPolyMesh.Frustum(0.039f * s, 0.024f * s, 0.17f * s, 5, 0.97f, yaw: yaw), new Transform3D(Basis.Identity, origin)));
            List<(ArrayMesh, Transform3D)> crown = i == 0 && v == 3 ? dusk : ((v + i) & 1) == 0 ? teal : light;
            for (int tier = 0; tier < 3; tier++)
            {
                float radius = (0.17f - (tier * 0.036f)) * s;
                float height = (0.20f - (tier * 0.01f)) * s;
                float bottom = (0.13f + (tier * 0.14f)) * s;
                var lean = new Vector2(LowPolyMesh.Signed(seed, tier + 2), LowPolyMesh.Signed(seed, tier + 5)) * 0.01f * s;
                crown.Add((LowPolyMesh.Frustum(radius, tier == 2 ? 0f : 0.015f * s, height, 6,
                    0.95f + (0.04f * LowPolyMesh.Unit(seed, tier + 8)), lean, yaw + (tier * Mathf.Pi / 6f), bottomCap: true),
                    new Transform3D(Basis.Identity, origin + new Vector3(0f, bottom, 0f))));
            }
        }

        var root = new Node3D { Name = "Pines" };
        root.AddChild(Merged("Trunk", trunks, Visuals.Timber));
        if (teal.Count > 0) root.AddChild(Merged("CanopyTeal", teal, Visuals.TreeCanopy));
        if (light.Count > 0) root.AddChild(Merged("CanopyLight", light, Visuals.TreeCanopy.Lightened(0.12f)));
        if (dusk.Count > 0) root.AddChild(Merged("CanopyDusk", dusk, Visuals.PineAutumn));
        return root;
    }

    /// <summary>
    /// 障碍格的断柱遗迹（装饰层）：一块石台、一根立着的断柱、一段倒伏的柱身。高度压在 0.55 以内。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Ruins(int variant)
    {
        int v = Wrap(variant, 4);
        var pale = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.BeveledSlab(new Vector3(0.24f, 0.07f, 0.24f), 0.018f, 0.98f, top: 0.13f),
                new Transform3D(Basis.Identity, new Vector3(-0.15f, 0f, -0.12f))),
            (LowPolyMesh.Frustum(0.105f, 0.075f, 0.30f + ((v & 1) * 0.06f), 4, 0.98f, yaw: v * Mathf.Pi / 7f),
                new Transform3D(Basis.Identity, new Vector3(-0.15f, 0.13f, -0.12f))),
            (LowPolyMesh.Beam(new Vector3(0.05f, 0.095f, 0.12f), new Vector3(0.29f, 0.10f, 0.21f), 0.14f, 0.10f, 0.96f), Transform3D.Identity),
        };
        var dark = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.BeveledSlab(new Vector3(0.64f, 0.07f, 0.64f), 0.025f, 0.97f, top: 0.07f), Transform3D.Identity),
            (LowPolyMesh.FacetRock(0.09f, 6, 0.15f, 1601 + v, new Vector3(1.2f, 0.55f, 1f)),
                new Transform3D(Basis.Identity, new Vector3(-0.15f, 0.43f + ((v & 1) * 0.06f), -0.12f))),
            (LowPolyMesh.BeveledSlab(new Vector3(0.15f, 0.10f, 0.14f), 0.01f, 0.94f, top: 0.15f),
                new Transform3D(new Basis(Vector3.Up, 0.4f), new Vector3(0.20f, 0f, -0.19f))),
        };
        var root = new Node3D { Name = "Ruins", RotationDegrees = new Vector3(0f, v * 90f, 0f) };
        root.AddChild(Merged("Stone", pale, Visuals.RuinStone));
        root.AddChild(Merged("Shade", dark, Visuals.RuinStone.Darkened(0.18f)));
        return root;
    }

    /// <summary>
    /// 林地格的三棵小树（装饰层）：四个格角里每档空出一个、其余三个各一棵；短干上叠三层多面树冠，
    /// 与沼泽的芦苇、荒漠的仙人掌分得开。树冠最宽半径 0.087、树高 ≤ 0.26，整棵落在棋子底座（半径 0.36）之外，
    /// 不遮挡该格的落点、气与归属标记（visual-style-baseline「装饰不遮挡判读」）。返回节点原点在地砖上表面。
    /// </summary>
    /// <remarks>两个 surface：树冠（<see cref="Visuals.TreeCanopy"/>）与树干（<see cref="Visuals.Timber"/>）。每棵的大小、朝向与明暗按散列错开。</remarks>
    public static Node3D Trees(int variant)
    {
        int v = Wrap(variant, TreesVariants);
        var canopy = new List<(ArrayMesh, Transform3D)>();
        var trunk = new List<(ArrayMesh, Transform3D)>();
        Vector2[] corners = [new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f)];
        for (int k = 0; k < corners.Length; k++)
        {
            if (k == v)
            {
                continue;
            }

            int seed = 801 + (v * 8) + k;
            float s = 0.90f + (0.10f * LowPolyMesh.Unit(seed, 0));
            float reach = 0.34f + (0.005f * LowPolyMesh.Unit(seed, 1));  // 留出树冠倾斜余量，最近角点在棋子底座之外
            var at = new Vector3(corners[k].X * reach, 0f, corners[k].Y * reach);
            float yaw = LowPolyMesh.Unit(seed, 2) * Mathf.Tau;
            float tone = 0.95f + (0.04f * LowPolyMesh.Unit(seed, 3));
            var lean = new Vector2(LowPolyMesh.Signed(seed, 4), LowPolyMesh.Signed(seed, 5)) * 0.008f * s;
            trunk.Add((LowPolyMesh.Frustum(0.028f * s, 0.018f * s, 0.09f * s, 5, 0.96f, yaw: yaw), new Transform3D(Basis.Identity, at)));
            canopy.Add((LowPolyMesh.Frustum(0.087f * s, 0.026f * s, 0.11f * s, 5, tone * 0.97f, lean, yaw, bottomCap: true),
                new Transform3D(Basis.Identity, at + new Vector3(0f, 0.055f * s, 0f))));
            canopy.Add((LowPolyMesh.Frustum(0.075f * s, 0.016f * s, 0.11f * s, 5, tone, lean, yaw + (Mathf.Pi / 5f), bottomCap: true),
                new Transform3D(Basis.Identity, at + new Vector3(0f, 0.115f * s, 0f))));
            canopy.Add((LowPolyMesh.Frustum(0.052f * s, 0f, 0.08f * s, 5, Mathf.Min(1f, tone * 1.02f), lean * 0.5f, yaw, bottomCap: true),
                new Transform3D(Basis.Identity, at + new Vector3(0f, 0.175f * s, 0f))));
        }

        var root = new Node3D { Name = "Trees" };
        root.AddChild(Merged("Canopy", canopy, Visuals.TreeCanopy));
        root.AddChild(Merged("Trunk", trunk, Visuals.Timber));
        return root;
    }

    // —— 粉彩浮岛装饰套件（map-restyle 段C）：草丛散在可落子格的格角，水晶 / 火盆 / 拱门 / 宝箱只放不可落子格（BoardView 按坐标散列挑）。
    //    结构同 Rock：根下一层各一个 MeshInstance3D（NewPartBatch 认这个形状才能合批）；底色只是占位，TerrainParts.Themed 按部件名重染成 Art 色。
    //    高度上限沿用松树的依据：障碍格件 ≤ 0.70，60° 俯角下向身后投不到半格；草丛 ≤ 0.12 且落在棋子底座（0.36）与出生区亮条（0.43 起）之间。

    /// <summary>
    /// 草丛（可落子格的格角）：七片三棱草叶挤成一簇，簇心在第 <paramref name="variant"/> 个格角（角序同 <see cref="Trees"/>）内侧 0.40 处，
    /// 叶底沿两轴都在 0.38–0.42、叶尖最远到 0.426——整簇落在格角 0.12 带内（作业单口径），也在棋子底座（0.36）之外、出生区亮条（0.43 起）之内；
    /// 高 ≤ 0.12，不遮落点与标记。返回节点原点在地砖上表面。
    /// </summary>
    public static Node3D Grass(int variant)
    {
        int v = Wrap(variant, GrassVariants);
        Vector2[] corners = [new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f)];
        int seed = 2101 + (v * 11);
        var tuft = new Vector3(corners[v].X * 0.40f, -0.004f, corners[v].Y * 0.40f);
        var deep = new List<(ArrayMesh, Transform3D)>();
        var light = new List<(ArrayMesh, Transform3D)>();
        for (int i = 0; i < 7; i++)
        {
            float a = (Mathf.Tau * i / 7f) + (LowPolyMesh.Unit(seed, i) * 0.7f);
            float r = i == 0 ? 0f : 0.004f + (0.004f * LowPolyMesh.Unit(seed, 10 + i));
            var at = tuft + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            float height = 0.075f + (0.045f * LowPolyMesh.Unit(seed, 20 + i));
            var lean = new Vector2(MathF.Cos(a), MathF.Sin(a)) * 0.006f;   // 叶尖朝簇外微倾
            (((i + v) & 1) == 0 ? deep : light).Add((LowPolyMesh.Frustum(0.012f, 0f, height, 3, 0.92f + (0.08f * LowPolyMesh.Unit(seed, 30 + i)), lean, a),
                new Transform3D(Basis.Identity, at)));
        }

        var root = new Node3D { Name = "Grass" };
        root.AddChild(Merged("Blades", deep, Visuals.TreeCanopy));
        root.AddChild(Merged("BladesLight", light, Visuals.TreeCanopy.Lightened(0.12f)));
        return root;
    }

    /// <summary>
    /// 紫水晶簇（不可落子格）：一块扁岩座上立一根主晶柱 + 三根外倾副晶柱 + 一根发亮的小晶，六棱尖顶；顶 ≤ 0.57、占地 ±0.31。
    /// 返回节点原点在地砖上表面；三档差朝向与主晶长短。
    /// </summary>
    public static Node3D Crystal(int variant)
    {
        int v = Wrap(variant, CrystalVariants);
        int seed = 2201 + (v * 13);
        var stone = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.FacetRock(0.21f, 6, 0.16f, seed, new Vector3(1.25f, 0.42f, 1.05f)), new Transform3D(Basis.Identity, new Vector3(0f, 0.05f, 0f))),
        };
        var shade = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.FacetRock(0.09f, 5, 0.2f, seed + 1, new Vector3(1.1f, 0.6f, 1f)), new Transform3D(Basis.Identity, new Vector3(0.22f, 0.03f, -0.14f))),
            (LowPolyMesh.FacetRock(0.06f, 5, 0.2f, seed + 2, new Vector3(1.2f, 0.6f, 1f)), new Transform3D(Basis.Identity, new Vector3(-0.20f, 0.02f, 0.18f))),
        };
        var crystal = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.Frustum(0.075f, 0f, 0.42f + (0.04f * v), 6, 0.98f, new Vector2(0.02f, -0.015f), LowPolyMesh.Unit(seed, 5) * Mathf.Tau),
                new Transform3D(Basis.Identity, new Vector3(0f, 0.07f, 0f))),
        };
        for (int i = 0; i < 3; i++)
        {
            float a = (Mathf.Tau * i / 3f) + (LowPolyMesh.Unit(seed, 20 + i) * 0.9f);
            float r = 0.13f + (0.03f * LowPolyMesh.Unit(seed, 30 + i));
            float height = 0.22f + (0.12f * LowPolyMesh.Unit(seed, 40 + i));
            var lean = new Vector2(MathF.Cos(a), MathF.Sin(a)) * (0.05f + (0.04f * LowPolyMesh.Unit(seed, 50 + i)));
            crystal.Add((LowPolyMesh.Frustum(0.04f + (0.012f * LowPolyMesh.Unit(seed, 60 + i)), 0f, height, 6, 0.96f, lean, a),
                new Transform3D(Basis.Identity, new Vector3(MathF.Cos(a) * r, 0.06f, MathF.Sin(a) * r))));
        }

        float g = LowPolyMesh.Unit(seed, 70) * Mathf.Tau;
        var glow = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.Frustum(0.03f, 0f, 0.20f, 6, 1f, new Vector2(MathF.Cos(g), MathF.Sin(g)) * 0.03f, g),
                new Transform3D(Basis.Identity, new Vector3(MathF.Cos(g) * 0.08f, 0.08f, MathF.Sin(g) * 0.08f))),
        };
        var root = new Node3D { Name = "Crystal", RotationDegrees = new Vector3(0f, (v * 120f) + 25f, 0f) };
        root.AddChild(Merged("Base", stone, Visuals.Rock));
        root.AddChild(Merged("Shade", shade, Visuals.Rock.Darkened(0.14f)));
        root.AddChild(Merged("Crystal", crystal, Visuals.Rock));
        root.AddChild(Merged("Glow", glow, Visuals.Rock));
        return root;
    }

    /// <summary>
    /// 火盆（不可落子格）：方石座 + 六棱石柱 + 外张的八棱石盆，盆里一堆余烬与四条火舌（不受光的暖色）；焰尖 ≤ 0.60、占地 ±0.21。
    /// 返回节点原点在地砖上表面；两档差朝向与火舌的歪斜。
    /// </summary>
    public static Node3D Brazier(int variant)
    {
        int v = Wrap(variant, BrazierVariants);
        int seed = 2301 + (v * 7);
        float yaw = LowPolyMesh.Unit(seed, 0) * Mathf.Tau;
        var stone = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.Frustum(0.13f, 0.10f, 0.22f, 6, 0.97f, yaw: yaw), new Transform3D(Basis.Identity, new Vector3(0f, 0.05f, 0f))),
            (LowPolyMesh.Frustum(0.10f, 0.19f, 0.11f, 8, 1f, yaw: yaw), new Transform3D(Basis.Identity, new Vector3(0f, 0.27f, 0f))),
            (LowPolyMesh.Frustum(0.19f, 0.17f, 0.03f, 8, 0.94f, yaw: yaw), new Transform3D(Basis.Identity, new Vector3(0f, 0.38f, 0f))),
        };
        var shade = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.BeveledSlab(new Vector3(0.34f, 0.05f, 0.34f), 0.015f, 0.97f, seed: seed, facetJitter: 0.03f, top: 0.05f, sideShade: 0.92f),
                new Transform3D(new Basis(Vector3.Up, yaw * 0.25f), Vector3.Zero)),
            (LowPolyMesh.FacetRock(0.05f, 5, 0.2f, seed + 1, new Vector3(1.2f, 0.7f, 1f)), new Transform3D(Basis.Identity, new Vector3(0.24f, 0.02f, 0.20f))),
        };
        var ember = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.Frustum(0.16f, 0f, 0.10f, 8, 1f, yaw: yaw + 0.3f), new Transform3D(Basis.Identity, new Vector3(0f, 0.36f, 0f))),
        };
        var flame = new List<(ArrayMesh, Transform3D)>();
        for (int i = 0; i < 4; i++)
        {
            float a = yaw + (Mathf.Tau * i / 4f);
            float r = i == 0 ? 0f : 0.07f;
            float height = i == 0 ? 0.20f : 0.13f + (0.04f * LowPolyMesh.Unit(seed, 10 + i));
            var lean = new Vector2(LowPolyMesh.Signed(seed, 20 + i), LowPolyMesh.Signed(seed, 30 + i)) * 0.03f;
            flame.Add((LowPolyMesh.Frustum(i == 0 ? 0.06f : 0.045f, 0f, height, 4, 1f, lean, a),
                new Transform3D(Basis.Identity, new Vector3(MathF.Cos(a) * r, 0.40f, MathF.Sin(a) * r))));
        }

        var root = new Node3D { Name = "Brazier" };
        root.AddChild(Merged("Stone", stone, Visuals.Rock));
        root.AddChild(Merged("Shade", shade, Visuals.Rock.Darkened(0.14f)));
        root.AddChild(Glowing("Ember", ember, Visuals.Rock));
        root.AddChild(Glowing("Flame", flame, Visuals.Rock));
        return root;
    }

    /// <summary>
    /// 石拱门（不可落子格）：两根方柱（础 / 身 / 柱头）架一道过梁、梁顶一块拱心石，旁边一块坍落的碎石；顶 0.70、占地 x ±0.37、z ±0.11。
    /// 返回节点原点在地砖上表面；两档差 90° 朝向。
    /// </summary>
    public static Node3D Arch(int variant)
    {
        int v = Wrap(variant, ArchVariants);
        int seed = 2401 + (v * 5);
        var stone = new List<(ArrayMesh, Transform3D)>();
        var shade = new List<(ArrayMesh, Transform3D)>();
        for (int k = 0; k < 2; k++)
        {
            var at = new Transform3D(Basis.Identity, new Vector3(k == 0 ? -0.27f : 0.27f, 0f, 0f));
            shade.Add((LowPolyMesh.BeveledSlab(new Vector3(0.20f, 0.06f, 0.20f), 0.015f, 0.97f, seed: seed + k, facetJitter: 0.03f, top: 0.06f, sideShade: 0.92f), at));
            stone.Add((LowPolyMesh.BeveledSlab(new Vector3(0.14f, 0.44f, 0.14f), 0.012f, 0.98f, top: 0.50f, sideShade: 0.90f), at));
            stone.Add((LowPolyMesh.BeveledSlab(new Vector3(0.19f, 0.06f, 0.19f), 0.015f, 1f, top: 0.56f, sideShade: 0.92f), at));
        }

        stone.Add((LowPolyMesh.Beam(new Vector3(-0.37f, 0.61f, 0f), new Vector3(0.37f, 0.61f, 0f), 0.18f, 0.10f, 0.96f), Transform3D.Identity));
        stone.Add((LowPolyMesh.BeveledSlab(new Vector3(0.12f, 0.04f, 0.20f), 0.01f, 1f, top: 0.70f, sideShade: 0.92f), Transform3D.Identity));
        shade.Add((LowPolyMesh.FacetRock(0.07f, 6, 0.2f, seed + 3, new Vector3(1.2f, 0.6f, 1f)), new Transform3D(Basis.Identity, new Vector3(0.12f, 0.03f, 0.28f))));
        var root = new Node3D { Name = "Arch", RotationDegrees = new Vector3(0f, v * 90f, 0f) };
        root.AddChild(Merged("Stone", stone, Visuals.RuinStone));
        root.AddChild(Merged("Shade", shade, Visuals.RuinStone.Darkened(0.18f)));
        return root;
    }

    /// <summary>
    /// 宝箱（不可落子格）：木箱体 + 略宽的圆角箱盖 + 两道金箍与一枚锁扣，旁边两粒碎石；顶 0.25、占地 ±0.31。
    /// 返回节点原点在地砖上表面；两档差 90° 朝向并各自按散列再歪一点。
    /// </summary>
    public static Node3D Chest(int variant)
    {
        int v = Wrap(variant, ChestVariants);
        int seed = 2501 + (v * 3);
        var body = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.BeveledSlab(new Vector3(0.30f, 0.15f, 0.20f), 0.012f, 0.98f, top: 0.17f, sideShade: 0.90f), Transform3D.Identity),
        };
        var lid = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.BeveledSlab(new Vector3(0.32f, 0.07f, 0.22f), 0.03f, 1f, top: 0.24f, sideShade: 0.92f), Transform3D.Identity),
        };
        var band = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.BeveledSlab(new Vector3(0.05f, 0.05f, 0.02f), 0.004f, 1f, top: 0.19f), new Transform3D(Basis.Identity, new Vector3(0f, 0f, 0.105f))),
        };
        foreach (float x in new[] { -0.09f, 0.09f })
        {
            band.Add((LowPolyMesh.BeveledSlab(new Vector3(0.03f, 0.235f, 0.235f), 0.004f, 1f, top: 0.247f, sideShade: 0.94f), new Transform3D(Basis.Identity, new Vector3(x, 0f, 0f))));
        }

        var shade = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.FacetRock(0.06f, 5, 0.2f, seed, new Vector3(1.2f, 0.6f, 1f)), new Transform3D(Basis.Identity, new Vector3(0.24f, 0.02f, 0.17f))),
            (LowPolyMesh.FacetRock(0.045f, 5, 0.2f, seed + 1, new Vector3(1.1f, 0.6f, 1f)), new Transform3D(Basis.Identity, new Vector3(-0.22f, 0.015f, -0.15f))),
        };
        var root = new Node3D { Name = "Chest", RotationDegrees = new Vector3(0f, (v * 90f) + 18f + (LowPolyMesh.Signed(seed, 9) * 12f), 0f) };
        root.AddChild(Merged("Body", body, Visuals.Timber));
        root.AddChild(Merged("Lid", lid, Visuals.Timber));
        root.AddChild(Merged("Band", band, Visuals.Timber));
        root.AddChild(Merged("Shade", shade, Visuals.Rock.Darkened(0.14f)));
        return root;
    }

    /// <summary>
    /// 荒漠格的点缀（terrain-surfaces 段 1）：两道贴地沙纹 + 一株带侧臂的仙人掌 + 一株矮仙人掌 + 一副贴地兽骨，点缀分放三个角，高度压在 0.25 以内、
    /// 全部落在棋子底座（半径 0.36）之外，不遮挡落点、气与归属标记。仙人掌是"竖柱 + 侧臂"，与林地的锥形小树、沼泽的芦苇轮廓都不同。
    /// 返回节点原点在地砖上表面；<paramref name="variant"/> 决定放哪一组对角与朝向。
    /// </summary>
    /// <remarks>
    /// map-elements-v2 段 C：三个 surface（仙人掌绿、兽骨白、沙纹深沙——三种底色差得远，顶点色 0.92–1 的亮度系数拉不开，只能分开）。
    /// 布局先按变体镜像（x 取反，只改位置、不翻网格）再整组转 90° 的整数倍，六档各不相同。仙人掌的侧臂沿格角的切向伸出，不朝格心。
    /// </remarks>
    public static Node3D Desert(int variant)
    {
        int v = Wrap(variant, DesertVariants);
        int seed = 901 + (v * 16);
        float sx = v < 4 ? 1f : -1f;
        Basis turn = QuarterTurn(v % 4);
        var cactus = new List<(ArrayMesh, Transform3D)>();
        var bone = new List<(ArrayMesh, Transform3D)>();
        var ripple = new List<(ArrayMesh, Transform3D)>();
        Transform3D At(Vector3 p) => new(turn, turn * p);

        // 沙纹：两道贴地的波状细条斜穿地砖中部（高 0.004）——灰度下也读得出"这块地是沙"，且贴地不遮挡任何标记。
        float slant = Mathf.DegToRad(18f * sx);
        var along = new Vector3(Mathf.Cos(slant), 0f, Mathf.Sin(slant));
        var across = new Vector3(-along.Z, 0f, along.X);
        for (int i = -1; i <= 1; i += 2)
        {
            var points = new List<Vector3>();
            for (int k = 0; k <= 4; k++)
            {
                float t = -0.31f + (k * 0.155f);
                float wave = 0.022f * LowPolyMesh.Signed(seed, 10 + (i * 5) + k);
                points.Add((along * t) + (across * ((i * 0.12f) + wave)) + new Vector3(0f, 0.004f, 0f));
            }

            ripple.Add((LowPolyMesh.Ribbon(points, 0.026f, shade: 0.94f), At(Vector3.Zero)));
        }

        // 仙人掌：六棱主柱 + 尖顶，一侧沿格角切向伸出一条曲臂（横段 + 竖段 + 尖顶），顶高 0.23；放在一个格角上。
        var cactusAt = new Vector3(0.34f * sx, 0f, -0.34f);
        float arm = v % 3 == 0 ? -1f : 1f;
        var tangent = new Vector3(1f, 0f, sx).Normalized() * arm;
        cactus.Add((LowPolyMesh.Frustum(0.052f, 0.046f, 0.19f, 6, 1f, yaw: 0.3f), At(cactusAt)));
        cactus.Add((LowPolyMesh.Frustum(0.046f, 0f, 0.04f, 6, 0.97f, yaw: 0.3f), At(cactusAt + new Vector3(0f, 0.19f, 0f))));
        cactus.Add((LowPolyMesh.Beam(cactusAt + (tangent * 0.03f) + new Vector3(0f, 0.09f, 0f), cactusAt + (tangent * 0.09f) + new Vector3(0f, 0.09f, 0f), 0.04f, 0.04f, 0.97f),
            At(Vector3.Zero)));
        Vector3 elbow = cactusAt + (tangent * 0.09f);
        cactus.Add((LowPolyMesh.Frustum(0.026f, 0.022f, 0.07f, 5, 0.98f), At(elbow + new Vector3(0f, 0.07f, 0f))));
        cactus.Add((LowPolyMesh.Frustum(0.022f, 0f, 0.025f, 5, 0.96f), At(elbow + new Vector3(0f, 0.14f, 0f))));

        // 第二株矮仙人掌放在对角，只有主柱 + 尖顶（顶高 0.14）。
        var smallAt = new Vector3(-0.34f * sx, 0f, 0.34f);
        cactus.Add((LowPolyMesh.Frustum(0.044f, 0.038f, 0.11f, 6, 0.98f, yaw: 0.1f), At(smallAt)));
        cactus.Add((LowPolyMesh.Frustum(0.038f, 0f, 0.03f, 6, 0.96f, yaw: 0.1f), At(smallAt + new Vector3(0f, 0.11f, 0f))));

        // 兽骨：第三个格角上两根交叉的细骨（骨干 + 两端骨节），贴地横放，高约 0.04。长的那根沿切向，短的那根在格角外侧斜搭。
        var boneAt = new Vector3(0.32f * sx, 0f, 0.32f);
        var outward = new Vector3(sx, 0f, 1f).Normalized();
        var side = new Vector3(1f, 0f, -sx).Normalized();
        float tilt = 0.35f * LowPolyMesh.Signed(seed, 30);
        Vector3 longDir = (side * Mathf.Cos(tilt)) + (outward * Mathf.Sin(tilt));
        Vector3 shortDir = (side * Mathf.Cos(tilt + 1.2f)) + (outward * Mathf.Sin(tilt + 1.2f));
        foreach ((Vector3 center, Vector3 dir, float half) in new[] { (boneAt, longDir, 0.10f), (boneAt + (outward * 0.025f), shortDir, 0.065f) })
        {
            Vector3 lift = new(0f, 0.016f, 0f);
            bone.Add((LowPolyMesh.Beam(center - (dir * half) + lift, center + (dir * half) + lift, 0.03f, 0.026f, 0.98f), At(Vector3.Zero)));
            for (int end = -1; end <= 1; end += 2)
            {
                bone.Add((LowPolyMesh.FacetRock(0.02f, 5, 0.15f, seed + 40 + end, new Vector3(1f, 0.8f, 1f)), At(center + (dir * (half * end)) + new Vector3(0f, 0.017f, 0f))));
            }
        }

        var root = new Node3D { Name = "Desert" };
        root.AddChild(Merged("Cactus", cactus, Visuals.Cactus));
        root.AddChild(Merged("Bone", bone, Visuals.Bone));
        root.AddChild(Merged("Ripple", ripple, Visuals.TileDesert.Darkened(0.16f)));
        return root;
    }

    /// <summary>
    /// 沼泽格的点缀（terrain-surfaces 段 2）：两三块贴地的积水斑块 + 两个角上的芦苇丛（细长竖条成簇，顶高 ≤ 0.24）。
    /// 刻意不用锥形树冠（visual-style-baseline「沼泽不被读成林地」）；芦苇在棋子底座（半径 0.36）之外，水洼贴地，不遮挡判读。
    /// 返回节点原点在地砖上表面。
    /// </summary>
    /// <remarks>
    /// map-elements-v2 段 C：两个 surface（水洼、芦苇）。水洼是散列抖边的扁斑块（高 0.007，粗糙度 0.35 保留水光）；
    /// 芦苇是细长的三棱尖叶（闭合网格，两面都看得到），从丛心向外斜出，每丛最高那片叶顶上一枚香蒲穗（同色）。
    /// </remarks>
    public static Node3D Marsh(int variant)
    {
        int v = Wrap(variant, MarshVariants);
        int seed = 1001 + (v * 16);
        float sx = v % 2 == 0 ? 1f : -1f;
        var puddle = new List<(ArrayMesh, Transform3D)>();
        var reed = new List<(ArrayMesh, Transform3D)>();

        // 积水：扁的不规则多边形，贴地、各转一个角度。
        (Vector2 At, float R)[] puddles = v % 3 == 0
            ? [(new(-0.10f, 0.05f), 0.20f), (new(0.16f, -0.10f), 0.12f)]
            : [(new(0.08f, 0.10f), 0.17f), (new(-0.14f, -0.08f), 0.14f), (new(0.18f, -0.18f), 0.08f)];
        for (int i = 0; i < puddles.Length; i++)
        {
            (Vector2 at, float r) = puddles[i];
            float yaw = Mathf.DegToRad((v * 29f) + (i * 50f));
            puddle.Add((LowPolyMesh.Patch(r, 7, 0.007f, seed + i, 0.16f, 0.7f),
                new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(at.X * sx, 0f, at.Y))));
        }

        // 芦苇：两丛，各五片尖叶，从丛心向外斜出；叶高 0.15–0.21。
        int clumpIndex = 0;
        foreach (Vector3 clump in new[] { new Vector3(0.33f * sx, 0f, -0.33f), new Vector3(-0.33f * sx, 0f, 0.33f) })
        {
            for (int k = 0; k < 5; k++)
            {
                float rad = Mathf.DegToRad((k * 72f) + (v * 13f) + (clumpIndex * 36f));
                var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                float height = 0.14f + (0.025f * ((k + v) % 4));
                float leanBy = 0.02f + (0.015f * LowPolyMesh.Unit(seed, 20 + (clumpIndex * 10) + k));
                var basePoint = clump + new Vector3(dir.X * 0.035f, 0f, dir.Y * 0.035f);
                reed.Add((LowPolyMesh.Frustum(0.010f, 0f, height, 3, 0.96f + (0.04f * LowPolyMesh.Unit(seed, 40 + k)), dir * leanBy, rad),
                    new Transform3D(Basis.Identity, basePoint)));
                if (k == 0)
                {
                    // 香蒲穗：挂在这片叶 0.82 高处的一枚竖长多面体，顶不超过 0.24。
                    Vector3 spike = basePoint + new Vector3(dir.X * leanBy * 0.82f, height * 0.82f, dir.Y * leanBy * 0.82f);
                    reed.Add((LowPolyMesh.FacetRock(0.016f, 5, 0.1f, seed + 60 + clumpIndex, new Vector3(1f, 2.2f, 1f)), new Transform3D(Basis.Identity, spike)));
                }
            }

            clumpIndex++;
        }

        var root = new Node3D { Name = "Marsh" };
        root.AddChild(Merged("Puddle", puddle, Visuals.MarshPuddle, 0.35f));
        root.AddChild(Merged("Reed", reed, Visuals.Reed));
        return root;
    }

    /// <summary>
    /// 岩台格的石面细节（terrain-surfaces 段 3）：沿地砖内缘一圈 0.03 高的石沿（"加厚边缘"）+ 三四道贴地裂纹 + 两颗碎石。
    /// 石沿只高 0.03、远低于一层地砖（高差 1 的缓坡侧面明显更高），读起来是"一块石台"而不是高一层（visual-style-baseline「岩台不被读成高一层」）。
    /// 碎石在棋子底座（半径 0.36）之外，裂纹贴地，不遮挡判读。返回节点原点在地砖上表面。
    /// </summary>
    /// <remarks>
    /// map-elements-v2 段 C：两个 surface（石沿浅石色、裂纹与碎石深色）。石沿每边两块倒角石（分块点按散列错开、块间留 0.008 的缝、顶高 0.026–0.03），
    /// 块底下探到 −0.03，把地砖顶板倒角处的空隙填上；裂纹是贴地（高 0.004）的折线；碎石是压扁的多面岩。
    /// </remarks>
    public static Node3D Crag(int variant)
    {
        int v = Wrap(variant, CragVariants);
        int seed = 1101 + (v * 16);
        const float half = BoardGeometry.TileSize * 0.5f;
        const float band = 0.05f;
        const float lip = 0.03f;
        const float gap = 0.004f;
        var rim = new List<(ArrayMesh, Transform3D)>();
        var crack = new List<(ArrayMesh, Transform3D)>();

        // 石沿：上下两边铺满整条边，左右两边夹在中间（与改前四条矮条的占位相同），每边在散列分块点断成两块。
        (Vector3 Center, bool AlongX, float Length)[] sides =
        [
            (new Vector3(0f, 0f, -half + (band * 0.5f)), true, BoardGeometry.TileSize),
            (new Vector3(0f, 0f, half - (band * 0.5f)), true, BoardGeometry.TileSize),
            (new Vector3(-half + (band * 0.5f), 0f, 0f), false, BoardGeometry.TileSize - (2f * band)),
            (new Vector3(half - (band * 0.5f), 0f, 0f), false, BoardGeometry.TileSize - (2f * band)),
        ];
        for (int s = 0; s < sides.Length; s++)
        {
            (Vector3 center, bool alongX, float length) = sides[s];
            float cut = 0.12f * LowPolyMesh.Signed(seed, s);
            (float From, float To)[] blocks = [(-length * 0.5f, cut - gap), (cut + gap, length * 0.5f)];
            for (int b = 0; b < blocks.Length; b++)
            {
                (float from, float to) = blocks[b];
                int salt = 10 + (s * 4) + b;
                float top = lip - (0.004f * LowPolyMesh.Unit(seed, salt));
                float mid = (from + to) * 0.5f;
                Vector3 size = alongX ? new Vector3(to - from, top + 0.03f, band) : new Vector3(band, top + 0.03f, to - from);
                Vector3 at = center + (alongX ? new Vector3(mid, 0f, 0f) : new Vector3(0f, 0f, mid));
                rim.Add((LowPolyMesh.BeveledSlab(size, 0.008f, 0.96f + (0.04f * LowPolyMesh.Unit(seed, salt + 50)), seed + salt, 0.03f, top, 0.95f),
                    new Transform3D(Basis.Identity, at)));
            }
        }

        // 裂纹：三四道贴地折线（中点按散列侧偏），整组按变体转一个角度。
        float turn = Mathf.DegToRad((v * 41f) % 90f);
        (Vector2 At, float Len, float Angle)[] cracks =
        [
            (new(-0.12f, -0.08f), 0.26f, 25f), (new(0.02f, -0.02f), 0.18f, -40f), (new(0.12f, 0.12f), 0.22f, 70f), (new(-0.16f, 0.16f), 0.14f, -10f),
        ];
        for (int i = 0; i < cracks.Length - (v % 2); i++)
        {
            (Vector2 at, float len, float angle) = cracks[i];
            float a = Mathf.DegToRad(angle) + turn;
            var dir = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
            var normal = new Vector3(-dir.Z, 0f, dir.X);
            var c = new Vector3(at.X, 0.004f, at.Y);
            Vector3 bend = normal * (0.025f * LowPolyMesh.Signed(seed, 30 + i));
            Vector3[] points = [c - (dir * (len * 0.5f)), c - (dir * (len * 0.12f)) + bend, c + (dir * (len * 0.5f)) - (bend * 0.4f)];
            crack.Add((LowPolyMesh.Ribbon(points, 0.024f), Transform3D.Identity));
        }

        // 碎石：两颗压扁的多面岩，放在石沿以内的两个格角上。
        float sx = v % 2 == 0 ? 1f : -1f;
        crack.Add((LowPolyMesh.FacetRock(0.045f, 6, 0.2f, seed + 70, new Vector3(1.15f, 0.7f, 1f)), new Transform3D(Basis.Identity, new Vector3(0.31f * sx, 0.022f, -0.31f))));
        crack.Add((LowPolyMesh.FacetRock(0.032f, 5, 0.2f, seed + 71, new Vector3(1.1f, 0.75f, 1f)), new Transform3D(Basis.Identity, new Vector3(-0.31f * sx, 0.016f, 0.31f))));

        var root = new Node3D { Name = "Crag" };
        root.AddChild(Merged("Rim", rim, Visuals.TileCrag.Lightened(0.08f)));
        root.AddChild(Merged("Crack", crack, Visuals.CragCrack));
        return root;
    }

    /// <summary>
    /// 浅滩格的水面细节（terrain-surfaces 段 4）：地砖本身是与同层齐平的浅青水色，上面加两道近白水纹 + 五六颗贴地鹅卵石。
    /// 与深水的区分靠两条通道：高度（深水水面低于地砖、浅滩齐平）与纹理（深水没有鹅卵石）（visual-style-baseline「浅滩不被读成深水」）。
    /// 全部贴地（≤ 0.03），不遮挡落点、气点与"浅滩：不算气"的标记。返回节点原点在地砖上表面。
    /// </summary>
    /// <remarks>
    /// map-elements-v2 段 C：两个 surface（鹅卵石、水纹；水纹保留粗糙度 0.4 的水光）。鹅卵石排在格心半径 0.402 的一圈上（角度按散列错开），
    /// 自身水平半径 ≤ 0.04：内沿 ≥ 0.36（底座之外），沿轴方向外沿 ≤ 0.445（不出地砖）。水纹是两道贴地（高 0.003）的弧。
    /// </remarks>
    public static Node3D Shallows(int variant)
    {
        int v = Wrap(variant, ShallowsVariants);
        int seed = 1201 + (v * 16);
        float sx = v % 2 == 0 ? 1f : -1f;
        var pebble = new List<(ArrayMesh, Transform3D)>();
        var ripple = new List<(ArrayMesh, Transform3D)>();

        // 水纹：两道短弧，在浅水上读成自然波纹而非规则的横向条块。
        for (int i = -1; i <= 1; i += 2)
        {
            var center = new Vector3(0.06f * i * sx, 0.003f, 0.14f * i);
            float start = Mathf.DegToRad((-20f * sx) + (i * 8f) + (40f * LowPolyMesh.Signed(seed, 10 + i)));
            var points = new List<Vector3>();
            for (int k = 0; k <= 3; k++)
            {
                float a = start + (k * 0.38f);
                points.Add(center + new Vector3((Mathf.Cos(a) * 0.17f) - (Mathf.Cos(start + 0.57f) * 0.17f), 0f, (Mathf.Sin(a) * 0.08f) - (Mathf.Sin(start + 0.57f) * 0.08f)));
            }

            ripple.Add((LowPolyMesh.Ribbon(points, 0.015f), Transform3D.Identity));
        }

        // 鹅卵石：一圈压扁的多面小石，避开格心的棋子底座。
        const float ring = 0.402f;
        for (int i = 0; i < 6 - (v % 2); i++)
        {
            float r = 0.026f + (0.008f * LowPolyMesh.Unit(seed, 20 + i));
            float a = Mathf.DegToRad((i * 60f) + (v * 17f) + (20f * LowPolyMesh.Signed(seed, 30 + i)));
            var at = new Vector3(Mathf.Cos(a) * ring, r * 0.25f, Mathf.Sin(a) * ring);
            pebble.Add((LowPolyMesh.FacetRock(r, 6, 0.12f, seed + 40 + i, new Vector3(1.25f, 0.6f, 1f)),
                new Transform3D(new Basis(Vector3.Up, LowPolyMesh.Unit(seed, 50 + i) * Mathf.Tau), at)));
        }

        var root = new Node3D { Name = "Shallows" };
        root.AddChild(Merged("Pebble", pebble, Visuals.Pebble));
        root.AddChild(Merged("Ripple", ripple, Visuals.Ripple, 0.4f));
        return root;
    }

    /// <summary>
    /// 一段栅栏（terrain-model 边属性）：三根立柱（顶上一枚倒角柱帽）+ 两根横杆，横杆在两柱之间略下垂；沿一格边长立起，厚度只有 0.05，
    /// 放在两格之间的缝上，不占任一格的落点。<paramref name="alongX"/> 为 <c>true</c> 时沿 X 轴（两格上下相邻），否则沿 Z 轴。
    /// 返回节点原点在缝中心、地砖上表面。
    /// </summary>
    /// <remarks>
    /// map-elements-v2 段 B：用 <see cref="LowPolyMesh"/> 拼成一个网格（一个 surface、一份木料材质，顶点色只当亮度系数）。
    /// 外形不超出改前的包围盒：x ∈ ±0.48（横杆两端）、y ∈ [0, 0.30]（柱帽顶）、厚 ±0.025（柱帽，柱身与横杆更细）。
    /// </remarks>
    public static Node3D Fence(bool alongX)
    {
        const float half = (BoardGeometry.TileSize + 0.06f) * 0.5f;   // 横杆半长 0.48，与改前同
        const float post = 0.04f, cap = 0.05f, capHeight = 0.035f, height = 0.30f, sag = 0.006f;
        var parts = new List<(ArrayMesh, Transform3D)>();
        for (int i = -1; i <= 1; i++)
        {
            float x = i * 0.40f;
            float shade = 0.95f + (0.04f * LowPolyMesh.Unit(509, i + 1));
            parts.Add((LowPolyMesh.BeveledSlab(new Vector3(post, height - capHeight, post), 0f, shade, top: height - capHeight, sideShade: 0.96f), At(x)));
            parts.Add((LowPolyMesh.BeveledSlab(new Vector3(cap, capHeight, cap), 0.014f, 1f, top: height, sideShade: 0.95f), At(x)));
        }

        // 横杆：两柱之间的每一跨在跨中下垂 sag，柱外的短伸出段水平。
        float[] knots = [-half, -0.40f, -0.20f, 0f, 0.20f, 0.40f, half];
        foreach (float y in new[] { 0.11f, 0.23f })
        {
            for (int k = 0; k + 1 < knots.Length; k++)
            {
                var from = new Vector3(knots[k], y - Droop(knots[k]), 0f);
                var to = new Vector3(knots[k + 1], y - Droop(knots[k + 1]), 0f);
                parts.Add((LowPolyMesh.Beam(from, to, 0.03f, 0.04f, 0.97f), Transform3D.Identity));
            }
        }

        // 沿 Z 的那一段由沿 X 的绕 Y 转 90° 得到（造型对称，正反一样）；基向量直接写成 0 / ±1，免得三角函数的舍入让厚度超出 ±0.025 一丝。
        Basis turn = alongX ? Basis.Identity : new Basis(new Vector3(0f, 0f, -1f), Vector3.Up, new Vector3(1f, 0f, 0f));
        var root = new Node3D { Name = "Fence" };
        root.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = LowPolyMesh.Merge(parts.Select(p => (p.Item1, new Transform3D(turn, Vector3.Zero) * p.Item2))),
            MaterialOverride = Shaded(Visuals.Timber),
        });
        return root;

        static Transform3D At(float x) => new(Basis.Identity, new Vector3(x, 0f, 0f));

        // 跨中（±0.20）下垂 sag，立柱处（0、±0.40）与伸出端不垂。
        static float Droop(float x) => Mathf.Abs(Mathf.Abs(x) - 0.20f) < 0.001f ? sag : 0f;
    }

    /// <summary>
    /// 预置桥（terrain-model 设施）：一层木板桥面（八块板，板间露出下面深色的垫木，读作板缝）+ 两侧栏杆（四根角墩、两根中柱、一道扶手），
    /// 铺在深水格上，面与同层地砖齐平——桥格是普通可落子格，棋子与标记照常放在面上。栏杆贴在桥面两侧、只有 0.13 高，不遮挡落点。
    /// 返回节点原点在桥面（地砖上表面）；网格里过桥方向沿 Z（栏杆沿 Z 走），BoardView 按河道走向绕 Y 转。
    /// </summary>
    /// <remarks>
    /// map-elements-v2 段 B：用 <see cref="LowPolyMesh"/> 拼成几个网格（桥面木板、深色木料、绳栏各一份 surface，顶点色只当亮度系数）。
    /// map-restyle 段B：扶手与斜撑换成绳栏——两侧各三根立柱（两端角墩 + 中柱），柱间两道下垂的绳；桥面木板缝加宽到 0.018。
    /// 外形不超出改前的包围盒：x、z ∈ ±0.45（TileSize 见方）、y ∈ [−0.06, 0.13]；绳只在 x = ±0.415 两侧，格心上方净空（拾取按模型）。
    /// </remarks>
    public static Node3D Bridge()
    {
        const float half = BoardGeometry.TileSize * 0.5f;
        const float deckDepth = 0.04f, bottom = -0.06f;
        const int planks = 8;
        const float seam = 0.018f;
        float plank = (BoardGeometry.TileSize - ((planks - 1) * seam)) / planks;

        // 桥面：八块横板（沿 X 长、沿 Z 排开），每块亮度按散列错开一点。
        var deck = new List<(ArrayMesh, Transform3D)>();
        for (int i = 0; i < planks; i++)
        {
            float z = -half + (plank * 0.5f) + (i * (plank + seam));
            ArrayMesh board = LowPolyMesh.BeveledSlab(new Vector3(BoardGeometry.TileSize, deckDepth, plank), 0.006f, 0.95f + (0.05f * LowPolyMesh.Unit(601, i)),
                seed: 601 + i, facetJitter: 0.02f, sideShade: 0.94f);
            deck.Add((board, new Transform3D(Basis.Identity, new Vector3(0f, 0f, z))));
        }

        // 深色木料：板下的垫木、四根角墩、两根中柱；格角内的小木钉给桥近景细节。立柱位置与顶高由 BridgePostTops 一处给出（TerrainParts 按它加柱顶金帽）。
        var timber = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.BeveledSlab(new Vector3(BoardGeometry.TileSize, -bottom - deckDepth, BoardGeometry.TileSize), 0f, 0.96f, top: -deckDepth), Transform3D.Identity),
        };
        foreach ((float x, float z, float top, float size) in BridgePostTops)
        {
            bool pier = size > BridgePost;
            timber.Add((LowPolyMesh.BeveledSlab(new Vector3(size, top - (pier ? bottom : 0f), size), pier ? 0.012f : 0.008f, pier ? 1f : 0.97f, top: top, sideShade: 0.95f),
                new Transform3D(Basis.Identity, new Vector3(x, 0f, z))));
        }

        // 绳栏：每侧上下两道绳，各在角墩与中柱之间分两段下垂（Beam 需要水平分量，竖直段不用它）；绳比柱细得多，读作麻绳而不是横木。
        var rope = new List<(ArrayMesh, Transform3D)>();
        const float ropeSize = 0.013f, sag = 0.022f;
        float reach = half - (BridgePier * 0.5f) - 0.015f;
        foreach (float sx in new[] { -BridgeSide, BridgeSide })
        {
            foreach (float y in new[] { 0.104f, 0.058f })
            {
                foreach ((float from, float to) in new[] { (-reach, 0f), (0f, reach) })
                {
                    float mid = (from + to) * 0.5f;
                    rope.Add((LowPolyMesh.Beam(new Vector3(sx, y, from), new Vector3(sx, y - sag, mid), ropeSize, ropeSize, 0.98f), Transform3D.Identity));
                    rope.Add((LowPolyMesh.Beam(new Vector3(sx, y - sag, mid), new Vector3(sx, y, to), ropeSize, ropeSize, 0.98f), Transform3D.Identity));
                }
            }
        }

        // 每块板两颗低矮木钉；只做几何与顶点明暗，不增加材质或绘制批次。
        for (int i = 0; i < planks; i++)
        {
            float z = -half + (plank * 0.5f) + (i * (plank + seam));
            foreach (float x in new[] { -0.37f, 0.37f })
            {
                timber.Add((LowPolyMesh.BeveledSlab(new Vector3(0.018f, 0.004f, 0.018f), 0.002f, 0.96f,
                    top: 0.004f, sideShade: 0.94f), new Transform3D(Basis.Identity, new Vector3(x, 0f, z))));
            }
        }

        var root = new Node3D { Name = "Bridge" };
        root.AddChild(new MeshInstance3D { Name = "Deck", Mesh = LowPolyMesh.Merge(deck), MaterialOverride = Shaded(Visuals.BridgeDeck) });
        root.AddChild(new MeshInstance3D { Name = "Timber", Mesh = LowPolyMesh.Merge(timber), MaterialOverride = Shaded(Visuals.Timber) });
        root.AddChild(new MeshInstance3D { Name = "Rope", Mesh = LowPolyMesh.Merge(rope), MaterialOverride = Shaded(Visuals.BridgeDeck) });
        return root;
    }

    /// <summary>桥的角墩边长、中柱边长、立柱所在的 x（两侧）。</summary>
    private const float BridgePier = 0.07f, BridgePost = 0.04f, BridgeSide = (BoardGeometry.TileSize * 0.5f) - (BridgePier * 0.5f);

    /// <summary>
    /// 桥的六根立柱：位置 (x, z)、柱顶高、边长。角墩顶 0.122、中柱顶 0.112，各留 0.008 给 <see cref="TerrainParts"/> 的柱顶金帽（帽顶 0.13 / 0.12，不超出桥的包围盒）。
    /// </summary>
    public static readonly (float X, float Z, float Top, float Size)[] BridgePostTops = BuildBridgePostTops();

    private static (float X, float Z, float Top, float Size)[] BuildBridgePostTops()
    {
        const float half = BoardGeometry.TileSize * 0.5f;
        float end = half - (BridgePier * 0.5f) - 0.015f;
        var posts = new List<(float, float, float, float)>();
        foreach (float sx in new[] { -BridgeSide, BridgeSide })
        {
            posts.Add((sx, -end, 0.122f, BridgePier));
            posts.Add((sx, end, 0.122f, BridgePier));
            posts.Add((sx, 0f, 0.112f, BridgePost));
        }

        return [.. posts];
    }

    /// <summary>
    /// LowPolyMesh 部件的材质：沿用现有色（设施木料、岩石、点缀）、缺省粗糙度 1（水洼 / 水纹保留各自的低粗糙度），
    /// 打开顶点色（顶点色只当亮度系数，最终色 = 底色 × 顶点色）。
    /// </summary>
    private static StandardMaterial3D Shaded(Color color, float roughness = 1f)
    {
        StandardMaterial3D material = Visuals.Matte(color, roughness);
        material.VertexColorUseAsAlbedo = true;
        return material;
    }

    /// <summary>一组网格按各自变换并成一个 surface，挂在一个写死名字的 MeshInstance3D 上（导出产物不带进程内自增序号）。</summary>
    private static MeshInstance3D Merged(string name, IEnumerable<(ArrayMesh, Transform3D)> parts, Color color, float roughness = 1f) =>
        new() { Name = name, Mesh = LowPolyMesh.Merge(parts), MaterialOverride = Shaded(color, roughness) };

    /// <summary>不受光的合并网格（火盆的火舌 / 余烬）：Flat 材质打开顶点色，α 恒 1——仍是不透明件，拾取索引与合批照常收录。</summary>
    private static MeshInstance3D Glowing(string name, IEnumerable<(ArrayMesh, Transform3D)> parts, Color color)
    {
        StandardMaterial3D material = Visuals.Flat(color);
        material.VertexColorUseAsAlbedo = true;
        return new MeshInstance3D { Name = name, Mesh = LowPolyMesh.Merge(parts), MaterialOverride = material };
    }

    /// <summary>绕 Y 转 <paramref name="quarters"/> 个 90°：基向量直接写 0 / ±1，免得三角函数的舍入让点缀的外沿多出一丝。</summary>
    private static Basis QuarterTurn(int quarters) => (((quarters % 4) + 4) % 4) switch
    {
        0 => Basis.Identity,
        1 => new Basis(new Vector3(0f, 0f, -1f), Vector3.Up, new Vector3(1f, 0f, 0f)),
        2 => new Basis(new Vector3(-1f, 0f, 0f), Vector3.Up, new Vector3(0f, 0f, -1f)),
        _ => new Basis(new Vector3(0f, 0f, 1f), Vector3.Up, new Vector3(-1f, 0f, 0f)),
    };

    /// <summary>变体号按档数取模（负数也落在 0…档数 − 1）：程序生成与按档导出的资源同形。</summary>
    private static int Wrap(int variant, int variants) => ((variant % variants) + variants) % variants;

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
