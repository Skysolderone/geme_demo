using Godot;

namespace Siege.Godot;

/// <summary>
/// 地形 / 设施部件目录：部件名、变体档数与程序生成函数的唯一一张表。<see cref="PartExport"/> 按它导出 <c>.tscn</c>，
/// <see cref="BoardView"/> 按它取部件——<b>有资源就加载资源</b>（<c>res://parts/terrain_full_art/&lt;名&gt;_&lt;变体 % 档数&gt;.tscn</c>），
/// 没有才退回 <see cref="LowPoly"/> 程序生成。两边共用同一张表，变体档数不会对不上。
/// </summary>
/// <remarks>
/// <para>资源是 <see cref="LowPoly"/> 的烘焙快照，也可以被美术替换成正式模型：只要文件名不变、原点仍在地砖上表面，棋盘就直接用新模型。</para>
/// <para>代价：程序生成里连续变化的量（松树 / 遗迹的朝向）在加载资源时只剩导出的那几档；段 C 重建的岩石与点缀在建模函数里先按档数取模，程序生成与资源同形。</para>
/// <para>每条路径的 <see cref="PackedScene"/> 只加载一次并缓存；不存在的路径也记下，不重复探测。</para>
/// </remarks>
public static class TerrainParts
{
    /// <summary>部件资源目录。</summary>
    public const string Directory = "res://parts/terrain_full_art";

    /// <summary>
    /// 一类部件：名字、变体档数（1 表示没有变体后缀）、程序生成函数、锚点约定。
    /// </summary>
    /// <param name="Name">部件名（资源文件名前缀）。</param>
    /// <param name="Variants">变体档数。</param>
    /// <param name="Build">程序生成函数：变体号 → 部件节点（原点即 <paramref name="Anchor"/>）。</param>
    /// <param name="Anchor">
    /// 锚点：部件节点原点在棋盘上对应的位置。装饰类部件（岩石、树、点缀、栅栏、桥）一律在所在格的地砖上表面（缺省值）；
    /// 地块部件各有约定——<c>tile_top</c> 在地砖上表面中心，<c>side_slope</c> / <c>side_cliff</c> 在该层带的顶面中心，<c>liner</c> 在衬底中心。
    /// </param>
    public sealed record Kind(string Name, int Variants, Func<int, Node3D> Build, string Anchor = "所在格的地砖上表面")
    {
        /// <summary>第 <paramref name="variant"/> 档的资源文件名（不含目录）。</summary>
        public string FileName(int variant) => Variants == 1 ? $"{Name}.tscn" : $"{Name}_{((variant % Variants) + Variants) % Variants}.tscn";
    }

    // 明亮的棋盘由 BoardView 保持地表判读；这些颜色只作用于部件自身。
    // map-restyle 段A（粉彩浮岛）：石材用淡紫灰，植物用青绿，结构件用暖木色，晶簇用亮紫。
    private static readonly Color ArtStone = Color.Color8(172, 164, 190);
    private static readonly Color ArtRimStone = Color.Color8(200, 190, 212);
    private static readonly Color ArtDarkStone = Color.Color8(112, 100, 134);
    private static readonly Color ArtSoil = Color.Color8(186, 156, 146);
    private static readonly Color ArtTeal = Color.Color8(62, 150, 118);
    private static readonly Color ArtLightTeal = Color.Color8(120, 196, 150);
    private static readonly Color ArtViolet = Color.Color8(70, 110, 120);
    private static readonly Color ArtCrystal = Color.Color8(168, 104, 214);
    private static readonly Color ArtWood = Color.Color8(124, 84, 58);
    private static readonly Color ArtDeck = Color.Color8(214, 166, 108);
    private static readonly Color ArtGold = Color.Color8(222, 190, 110);
    private static readonly Color ArtIvory = Color.Color8(236, 230, 226);
    private static readonly Color ArtWater = Color.Color8(70, 186, 190);

    // 障碍与装饰（map-elements-v2 段 C 用 LowPolyMesh 重建；档数在 LowPoly 一处，建模函数按它取模）。pines / ruins 只服务场景格，按裁决 M3 不重建、不扩档。
    public static readonly Kind Rock = new("rock", LowPoly.RockVariants, v => Themed("rock", LowPoly.Rock(v), v));
    public static readonly Kind Ruins = new("ruins", 4, v => Themed("ruins", LowPoly.Ruins(v), v));
    public static readonly Kind Pines = new("pines", 7, v => Themed("pines", LowPoly.Pines(v), v));
    public static readonly Kind Trees = new("trees", LowPoly.TreesVariants, v => Themed("trees", LowPoly.Trees(v), v));
    public static readonly Kind Desert = new("desert", LowPoly.DesertVariants, v => Themed("desert", LowPoly.Desert(v), v));
    public static readonly Kind Marsh = new("marsh", LowPoly.MarshVariants, v => Themed("marsh", LowPoly.Marsh(v), v));
    public static readonly Kind Crag = new("crag", LowPoly.CragVariants, v => Themed("crag", LowPoly.Crag(v), v));
    public static readonly Kind Shallows = new("shallows", LowPoly.ShallowsVariants, v => Themed("shallows", LowPoly.Shallows(v), v));
    public static readonly Kind FenceX = new("fence_x", 1, _ => Themed("fence", LowPoly.Fence(alongX: true), 0));
    public static readonly Kind FenceZ = new("fence_z", 1, _ => Themed("fence", LowPoly.Fence(alongX: false), 0));
    public static readonly Kind Bridge = new("bridge", 1, _ => Themed("bridge", LowPoly.Bridge(), 0));

    // —— 地块部件（map-elements-v2 段 A，design D2 / D3）：每件只有一个 MeshInstance3D、一个 surface，棋盘逐格只取它的网格（见 PieceOf）。

    /// <summary>衬底厚度：面砖之下垫的那块铺满整格的薄板（BoardView 与 <see cref="Liner"/> 共用这一个值）。</summary>
    public const float LinerThickness = 0.03f;

    /// <summary>地砖顶板的倒角宽。</summary>
    public const float TileBevel = 0.038f;

    /// <summary>
    /// 地砖顶板（七种地表共用网格）：TileSize 见方、TileHeight 厚、顶面四边倒角，顶面带细微面差与远离格心的两枚阴刻角饰。
    /// 材质由 BoardView 按底色覆盖；导出时带中性白材质。
    /// </summary>
    public static readonly Kind TileTop = new("tile_top", 3, v => Single("TileTop", TileTopMesh(v), NeutralMaterial()), "地砖上表面中心");

    /// <summary>第 1 层侧面（缓坡）：铺满整格、高一层，四条暖土色带，棱与格边齐平。</summary>
    public static readonly Kind SideSlope = new("side_slope", 2, v => Single("SideSlope", SideSlopeMesh(v), SideMaterial(ArtSoil)), "该层带的顶面中心");

    /// <summary>第 2 层侧面（崖壁）：铺满整格、高一层；亮岩沿下是压暗的竖向节理岩体。</summary>
    public static readonly Kind SideCliff = new("side_cliff", 3, v => Single("SideCliff", SideCliffMesh(v), SideMaterial(ArtStone.Lightened(0.42f))), "该层带的顶面中心");

    /// <summary>砖缝衬底：铺满整格、厚 <see cref="LinerThickness"/>。材质由 BoardView 按底色覆盖；导出时带中性白材质。</summary>
    public static readonly Kind Liner = new("liner", 1, _ => Single("Liner", LinerMesh(), NeutralMaterial()), "衬底中心");

    // —— 水系部件（map-elements-v2 段 B，design D4）。

    /// <summary>河岸石沿占水格边缘的宽度（O-2：一格宽河的水面可见宽度 = 1 − 2 × 0.06 = 0.88 ≥ 0.78）。</summary>
    public const float BankLipWidth = 0.06f;

    /// <summary>
    /// 河岸石沿（一条边一件）：沿水格一条边排三块倒角石，占水格边缘 <see cref="BankLipWidth"/>，顶面比地砖上表面低 0.015 起，底没入水面之下。
    /// 网格里边沿 X 轴、石沿朝 +Z（水格内侧）展开；BoardView 按边的朝向绕 Y 旋转。内置岩灰（与崖壁侧面同一份材质）。
    /// </summary>
    public static readonly Kind BankLip = new("bank_lip", 3, v => Single("BankLip", BankLipMesh(v), SideMaterial(ArtStone)), "水格边中点（高度取该格地砖上表面）");

    // —— 外框与底座部件（map-elements-v2 段 D，design D5）。

    /// <summary>底座外圈石沿：每件沿边的长度（节距约 1 格，余下的读作石块间的缝）。</summary>
    public const float RimLength = 0.95f;

    /// <summary>底座外圈石沿：自底座外缘向内的进深（石沿只占底座外缘那 0.45 格的边，标注离格外缘最远 1.2，这一带净空）。</summary>
    public const float RimDepth = 0.28f;

    /// <summary>底座外圈石沿：外侧面凸出底座侧面的量（与底座侧面不共面）。</summary>
    public const float RimOverhang = 0.02f;

    /// <summary>底座外圈石沿：顶面高出底座顶面的上限——底座顶面在 TopY − WaterDrop，所以石沿顶最高到 TopY − 0.02，比坐标标注所在的 h=0 平面低。</summary>
    public const float RimRise = 0.08f;

    /// <summary>
    /// 底座外圈石沿（一段一件）：沿边排 1–3 块倒角石（档号 + 1 块，分块点按散列），长 <see cref="RimLength"/>、进深 <see cref="RimDepth"/>、外凸 <see cref="RimOverhang"/>、
    /// 顶高 ≤ <see cref="RimRise"/>、底下探到底座底面之下。网格里边沿 X 轴、石沿朝 +Z（底座内侧）展开，BoardView 按边的朝向转。内置暖石材质与顶点古金短铭文。
    /// </summary>
    public static readonly Kind Rim = new("rim", 3, v => Single("Rim", RimMesh(v), SideMaterial(ArtRimStone)), "段中点（底座外缘线上，高度取底座顶面）");

    /// <summary>
    /// 出生区亮条（一格边一件）：长 1 格、宽 0.07、高 0.03，贴在格内侧边缘——与改前的描边盒逐值同包围盒；顶边细倒角、侧面压暗，读作嵌进地砖边缘的亮条。
    /// 网格里边沿 X 轴、亮条朝 +Z（格内侧）展开。材质由 BoardView 按归属色覆盖（不受光）；导出时带中性白材质。
    /// </summary>
    public static readonly Kind ZoneStrip = new("zone_strip", 1, _ => Single("ZoneStrip", ZoneStripMesh(), NeutralMaterial()), "格边中点（高度取该格地砖上表面）");

    /// <summary>
    /// 连体外轮廓边框（逐格边一件）：长 1 格、宽 0.13、高 0.065，骑在格边线上（内外各一半）；顶边倒角并带双道阴刻线，两档只差顶面色差。
    /// 网格里边沿 X 轴。材质由 BoardView 按台面边框色覆盖（不受光）；导出时带中性白材质。
    /// </summary>
    public static readonly Kind PlateFrame = new("plate_frame", 2, v => Single("PlateFrame", PlateFrameMesh(v), NeutralMaterial()), "段中点（逐格边一件：格边中点，高度取 h=0 地砖上表面）");

    /// <summary>
    /// 连体外轮廓边框的转角件：0.14 见方、高 0.07，盖在每段外轮廓的两端（与改前边框盒在段端外伸的 0.07 逐值同形），比边框件略高略宽，相交处不共面。材质同 <see cref="PlateFrame"/>。
    /// </summary>
    public static readonly Kind PlateCorner = new("plate_corner", 1, _ => Single("PlateCorner", PlateCornerMesh(), NeutralMaterial()), "外轮廓段端点（高度取 h=0 地砖上表面）");

    /// <summary>
    /// 浮岛岩层：单位块（顶面 1 × 1、高 1，锚在顶面中心），BoardView 按画幅与层厚整体缩放。八边形外廓（四角小切角）、三条色带、向下略收，侧面按散列微微凹凸。
    /// 三档即三种岩色（暗度递进）：0 土层、1 岩层、2 深岩，材质内置。
    /// </summary>
    public static readonly Kind IslandLayer = new("island_layer", 3, v => Single("IslandLayer", IslandLayerMesh(v), VertexMaterial(IslandColor(v), 1f)), "底座中心（单位块顶面中心，按画幅缩放）");

    /// <summary>
    /// 浮岛垂岩：单位长的倒锥（尖在原点、顶面在 y = 1、顶面半径 1），五 / 六棱、中段按散列歪斜；BoardView 按长短缩放。
    /// 0 档深岩、1–3 档岩灰，材质内置。
    /// </summary>
    public static readonly Kind IslandSpike = new("island_spike", 4, v => Single("IslandSpike", IslandSpikeMesh(v), VertexMaterial(IslandColor(Wrap(v, 4) == 0 ? 2 : 1), 1f)), "尖顶（单位长倒锥的尖端，按长短缩放）");

    /// <summary>浮岛云团：半径 1、厚 0.55 的扁平多面团（与改前的扁球同尺寸），BoardView 按大小等比缩放。不受光、半透明，材质内置。</summary>
    public static readonly Kind Cloud = new("cloud", 3, v => Single("Cloud", CloudMesh(v), CloudMaterial()), "云心");

    /// <summary>全部部件，导出顺序即此顺序（新部件追加在末尾，既有部件的导出顺序不变）。</summary>
    public static readonly Kind[] All =
    [
        Rock, Ruins, Pines, Trees, Desert, Marsh, Crag, Shallows, FenceX, FenceZ, Bridge, TileTop, SideSlope, SideCliff, Liner, BankLip,
        Rim, ZoneStrip, PlateFrame, PlateCorner, IslandLayer, IslandSpike, Cloud,
    ];

    /// <summary>单网格部件取出的网格与它自带的材质（自带材质可能为空：由调用方覆盖的部件只带中性白）。</summary>
    public readonly record struct Piece(Mesh Mesh, Material? Material);

    private static readonly Dictionary<string, PackedScene?> Cache = [];
    private static readonly Dictionary<string, Piece?> LoadedPieces = [];
    private static readonly Dictionary<string, Piece> GeneratedPieces = [];

    /// <summary>本进程里从资源加载的部件数（供启动日志自证"确实在用资源"）。</summary>
    public static int LoadedCount { get; private set; }

    /// <summary>本进程里退回程序生成的部件数。</summary>
    public static int GeneratedCount { get; private set; }

    /// <summary>取一件部件：有资源就实例化资源，否则程序生成。返回节点原点在地砖上表面（与 <see cref="LowPoly"/> 同一约定）。</summary>
    public static Node3D Create(Kind kind, int variant = 0)
    {
        if (SceneOf(kind, variant)?.Instantiate() is Node3D node)
        {
            LoadedCount++;
            return node;
        }

        GeneratedCount++;
        return kind.Build(variant);
    }

    /// <summary>
    /// 合批用的模板（board-render-perf D2）：该档资源的一份实例，<b>不计数</b>；没有资源时返回 <c>null</c>——
    /// 程序生成的松树 / 遗迹随 <paramref name="variant"/> 连续变化（朝向不按档数取模），归并不成有限几种，调用方应退回 <see cref="Create"/> 逐件画。
    /// </summary>
    public static Node3D? Template(Kind kind, int variant) => SceneOf(kind, variant)?.Instantiate() as Node3D;

    /// <summary>合批放置了一件来自资源的部件（实例由 MultiMesh 画，不再各建节点）：只记数，让启动日志的"资源 N 件"仍是放置件数。</summary>
    public static void CountLoaded() => LoadedCount++;

    /// <summary>
    /// 取单网格部件（地块用）的网格与自带材质：有资源就取资源里第一个 <see cref="MeshInstance3D"/> 的网格（计"资源"），没有就程序生成（计"程序生成"）。
    /// 每档只取一次并缓存，所以全图的地块网格只有"种类 × 档数"那么多份；计数按放置件数，与 <see cref="Create"/> 同一口径。
    /// </summary>
    /// <remarks>约定：单网格部件的根节点下第一个 MeshInstance3D 变换为单位、网格顶点已按 <see cref="Kind.Anchor"/> 摆好；替换美术时保持这一点。</remarks>
    public static Piece PieceOf(Kind kind, int variant)
    {
        ArgumentNullException.ThrowIfNull(kind);
        string file = kind.FileName(variant);
        if (!LoadedPieces.TryGetValue(file, out Piece? loaded))
        {
            loaded = SceneOf(kind, variant)?.Instantiate() is Node3D node ? Take(node) : null;
            LoadedPieces[file] = loaded;
        }

        if (loaded is { } piece)
        {
            LoadedCount++;
            return piece;
        }

        GeneratedCount++;
        if (!GeneratedPieces.TryGetValue(file, out Piece generated))
        {
            generated = Take(kind.Build(variant)) ?? throw new InvalidOperationException($"部件 {kind.Name} 的程序生成结果里没有网格");
            GeneratedPieces[file] = generated;
        }

        return generated;
    }

    /// <summary>取节点树里第一个带网格的 <see cref="MeshInstance3D"/> 的网格与材质，然后释放节点（网格与材质是引用计数资源，留在缓存里）。</summary>
    private static Piece? Take(Node3D root)
    {
        Piece? piece = FirstMesh(root) is { Mesh: { } mesh } instance ? new Piece(mesh, instance.MaterialOverride) : null;
        root.Free();
        return piece;

        static MeshInstance3D? FirstMesh(Node node)
        {
            foreach (Node child in node.GetChildren())
            {
                if (child is MeshInstance3D { Mesh: not null } found)
                {
                    return found;
                }

                if (FirstMesh(child) is { } deeper)
                {
                    return deeper;
                }
            }

            return null;
        }
    }

    /// <summary>单网格部件的节点：根节点下一个名为 Mesh 的 MeshInstance3D，单位变换（节点名写死，导出产物不带进程内自增序号）。</summary>
    private static Node3D Single(string name, ArrayMesh mesh, StandardMaterial3D material)
    {
        var root = new Node3D { Name = name };
        root.AddChild(new MeshInstance3D { Name = "Mesh", Mesh = mesh, MaterialOverride = material });
        return root;
    }

    /// <summary>中性白材质（打开顶点色）：由 BoardView 覆盖材质的部件导出时带这一份，加载后被换成按底色共用的那一份。</summary>
    private static StandardMaterial3D NeutralMaterial()
    {
        StandardMaterial3D material = Visuals.Matte(Colors.White);
        material.VertexColorUseAsAlbedo = true;
        return material;
    }

    /// <summary>侧面的内置材质：沿用现有侧面色（土色 / 岩灰），打开顶点色让色带读出来。</summary>
    private static StandardMaterial3D SideMaterial(Color color)
    {
        StandardMaterial3D material = Visuals.Matte(color);
        material.VertexColorUseAsAlbedo = true;
        return material;
    }

    /// <summary>给所有程序部件与导出快照使用同一套新材质；不改棋子所用的 LowPoly 源材质。</summary>
    private static Node3D Themed(string kind, Node3D root, int variant)
    {
        Paint(root);
        if (kind == "rock" || kind == "ruins")
        {
            // 只有障碍格有高于格面的晶簇；它们本来不可落子，晶簇保持在原有障碍高度内。
            float x = kind == "rock" ? -0.14f : 0.24f;
            float z = kind == "rock" ? 0.10f : -0.04f;
            float y = kind == "rock" ? 0.27f : 0.08f;
            var crystal = new MeshInstance3D
            {
                Name = "Crystal",
                Mesh = LowPolyMesh.Frustum(0.043f, 0f, kind == "rock" ? 0.13f : 0.17f, 5, 0.98f,
                    yaw: LowPolyMesh.Unit(variant + 1701, 0) * Mathf.Tau, bottomCap: true),
                MaterialOverride = SideMaterial(kind == "rock" ? ArtCrystal : ArtGold),
                Position = new Vector3(x, y, z),
            };
            root.AddChild(crystal);
        }
        else if (kind == "fence")
        {
            root.AddChild(new MeshInstance3D
            {
                Name = "Finial",
                Mesh = LowPolyMesh.BeveledSlab(new Vector3(0.045f, 0.008f, 0.045f), 0.002f, 1f, top: 0.303f),
                MaterialOverride = SideMaterial(ArtGold),
            });
        }
        else if (kind == "bridge")
        {
            var gilding = new List<(ArrayMesh, Transform3D)>();
            foreach (float x in new[] { -0.415f, 0.415f })
            {
                gilding.Add((LowPolyMesh.Beam(new Vector3(x, 0.121f, -0.32f), new Vector3(x, 0.121f, 0.32f),
                    0.008f, 0.006f, 0.98f), Transform3D.Identity));
            }

            root.AddChild(new MeshInstance3D
            {
                Name = "Gilding",
                Mesh = LowPolyMesh.Merge(gilding),
                MaterialOverride = SideMaterial(ArtGold),
            });
        }

        return root;

        void Paint(Node node)
        {
            foreach (Node child in node.GetChildren())
            {
                if (child is MeshInstance3D { MaterialOverride: StandardMaterial3D original } mesh)
                {
                    var material = (StandardMaterial3D)original.Duplicate();
                    material.AlbedoColor = ArtColor(kind, mesh.Name, original.AlbedoColor);
                    mesh.MaterialOverride = material;
                }

                Paint(child);
            }
        }
    }

    private static Color ArtColor(string kind, string part, Color original) => kind switch
    {
        "rock" => part == "Shade" ? ArtDarkStone : ArtStone,
        "ruins" => part == "Shade" ? ArtDarkStone : ArtIvory,
        "pines" => part == "Trunk" ? ArtWood : part == "CanopyDusk" ? ArtViolet
            : part == "CanopyLight" ? ArtLightTeal : ArtTeal,
        "trees" => part == "Trunk" ? ArtWood : ArtTeal,
        "desert" => part == "Cactus" ? ArtTeal : part == "Bone" ? ArtIvory : ArtDarkStone,
        "marsh" => part == "Puddle" ? ArtWater : ArtLightTeal,
        "crag" => part == "Rim" ? ArtStone.Lightened(0.13f) : ArtDarkStone,
        "shallows" => part == "Pebble" ? ArtStone.Lightened(0.24f) : ArtLightTeal.Lightened(0.45f),
        "fence" => ArtWood,
        "bridge" => part == "Deck" ? ArtDeck : ArtWood,
        _ => original,
    };

    private static ArrayMesh TileTopMesh(int variant)
    {
        // 短刻线留在地砖边角，格心保持清晰。
        int v = Wrap(variant, TileTop.Variants);
        var parts = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.BeveledSlab(new Vector3(BoardGeometry.TileSize, BoardGeometry.TileHeight, BoardGeometry.TileSize),
                TileBevel, 0.99f, seed: 101 + v, facetJitter: 0.025f, sideShade: 0.91f), Transform3D.Identity),
        };
        float corner = BoardGeometry.TileSize * 0.34f;
        float length = BoardGeometry.TileSize * 0.075f;
        for (int i = 0; i < 2; i++)
        {
            float sign = i == 0 ? 1f : -1f;
            float x = sign * corner;
            float z = (((v & 1) == 0) ? sign : -sign) * corner;
            parts.Add((LowPolyMesh.Ribbon([new Vector3(x - length * 0.5f, 0.0015f, z),
                new Vector3(x + length * 0.5f, 0.0015f, z)], 0.008f, shade: 0.82f, minShade: 0.8f), Transform3D.Identity));
        }
        return LowPolyMesh.Merge(parts);
    }

    private static ArrayMesh SideSlopeMesh(int variant) => LowPolyMesh.Strata(
        new Vector3(BoardGeometry.CellSize, BoardGeometry.LayerHeight, BoardGeometry.CellSize), bands: 3, jitter: 0.008f, seed: 201 + Wrap(variant, SideSlope.Variants));

    private static ArrayMesh SideCliffMesh(int variant)
    {
        // 单一 surface 以顶点色区分岩沿和岩体，既保留共享材质也不增加每格绘制。
        ArrayMesh source = LowPolyMesh.Strata(
            new Vector3(BoardGeometry.CellSize, BoardGeometry.LayerHeight, BoardGeometry.CellSize),
            bands: 4, jitter: 0.03f, seed: 301 + Wrap(variant, SideCliff.Variants), lip: 0.08f, crevice: 0.09f);
        global::Godot.Collections.Array arrays = source.SurfaceGetArrays(0);
        Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Color[] colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        for (int i = 0; i < vertices.Length; i++)
        {
            // 岩沿占顶部 0.045；下方岩体压暗，竖向裂缝由 Strata 的内凹保留。
            int triangle = i - (i % 3);
            float faceY = (vertices[triangle].Y + vertices[triangle + 1].Y + vertices[triangle + 2].Y) / 3f;
            float rockDepth = MathF.Max(0.001f, BoardGeometry.LayerHeight - 0.12f);
            int band = Math.Clamp((int)((-faceY - 0.07f) * 4f / rockDepth), 0, 3);
            float factor = vertices[i].Y >= -0.0451f ? 1f : ((band & 1) == 0 ? 0.62f : 0.76f);
            Color color = colors[i];
            colors[i] = new Color(color.R * factor, color.G * factor, color.B * factor, color.A);
        }

        arrays[(int)Mesh.ArrayType.Color] = colors;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static ArrayMesh LinerMesh() => LowPolyMesh.BeveledSlab(
        new Vector3(BoardGeometry.CellSize, LinerThickness, BoardGeometry.CellSize), 0f, 1f, top: LinerThickness * 0.5f);

    /// <summary>
    /// 石沿网格：三块石头沿边（x ∈ ±0.495，两端离格角 0.005，与相邻那条石沿的外侧面不共面）排开，块间留 0.008 的缝；
    /// 分块点、中间那块的顶高与内沿按散列错开（只往低、往窄走：顶不高于 −0.015，宽不超过 <see cref="BankLipWidth"/>）。底在 −0.10，没入水面（−0.092）。
    /// </summary>
    private static ArrayMesh BankLipMesh(int variant)
    {
        int seed = 401 + Wrap(variant, BankLip.Variants);
        const float end = 0.495f, gap = 0.004f, top = -0.015f, bottom = -0.10f;
        float cut1 = -0.165f + (LowPolyMesh.Signed(seed, 0) * 0.06f);
        float cut2 = 0.165f + (LowPolyMesh.Signed(seed, 1) * 0.06f);
        (float From, float To, float Top, float Width)[] stones =
        [
            (-end, cut1 - gap, top, BankLipWidth),
            (cut1 + gap, cut2 - gap, top - 0.003f - (0.006f * LowPolyMesh.Unit(seed, 2)), BankLipWidth - (0.01f * LowPolyMesh.Unit(seed, 3))),
            (cut2 + gap, end, top, BankLipWidth),
        ];
        var parts = new List<(ArrayMesh, Transform3D)>();
        for (int i = 0; i < stones.Length; i++)
        {
            (float from, float to, float stoneTop, float width) = stones[i];
            ArrayMesh stone = LowPolyMesh.BeveledSlab(new Vector3(to - from, stoneTop - bottom, width), 0.012f, 0.98f,
                seed: seed + (i * 7), facetJitter: 0.03f, top: stoneTop, sideShade: 0.95f);
            parts.Add((stone, new Transform3D(Basis.Identity, new Vector3((from + to) * 0.5f, 0f, width * 0.5f))));
        }

        return LowPolyMesh.Merge(parts);
    }

    /// <summary>
    /// 石沿网格：第 v 档沿边排 v + 1 块倒角石（总长 <see cref="RimLength"/>，分块点按散列错开、块间留 0.012 的缝），z ∈ [−<see cref="RimOverhang"/>, <see cref="RimDepth"/>]；
    /// 顶面 ≤ <see cref="RimRise"/>（各块按散列再低 0–0.012），底在 −0.14（底座厚 0.12，即探到底座底面之下 0.02）。
    /// </summary>
    private static ArrayMesh RimMesh(int variant)
    {
        int v = Wrap(variant, Rim.Variants);
        int seed = 501 + v;
        const float half = RimLength * 0.5f, gap = 0.006f, bottom = -0.14f;
        float[] cuts = v switch
        {
            0 => [],
            1 => [LowPolyMesh.Signed(seed, 0) * 0.18f],
            _ => [-0.17f + (LowPolyMesh.Signed(seed, 0) * 0.06f), 0.17f + (LowPolyMesh.Signed(seed, 1) * 0.06f)],
        };
        float[] edges = [-half, .. cuts, half];
        var parts = new List<(ArrayMesh, Transform3D)>();
        for (int i = 0; i + 1 < edges.Length; i++)
        {
            float from = edges[i] + (i > 0 ? gap : 0f), to = edges[i + 1] - (i + 2 < edges.Length ? gap : 0f);
            float top = RimRise - (0.012f * LowPolyMesh.Unit(seed, 10 + i));
            float depth = RimDepth + RimOverhang;
            ArrayMesh stone = LowPolyMesh.BeveledSlab(new Vector3(to - from, top - bottom, depth), 0.03f, 0.97f,
                seed: seed + (i * 7), facetJitter: 0.03f, top: top, sideShade: 0.94f);
            parts.Add((stone, new Transform3D(Basis.Identity, new Vector3((from + to) * 0.5f, 0f, (depth * 0.5f) - RimOverhang))));

            // 古金 V 字短铭文压在石沿顶面的中段；它与石块合成一个网格，不增加节点和绘制调用。
            float mid = (from + to) * 0.5f;
            float glyphHalf = Math.Min(0.055f, (to - from) * 0.22f);
            Vector3[] rune =
            [
                new(mid - glyphHalf, top + 0.002f, 0.08f),
                new(mid, top + 0.002f, 0.125f),
                new(mid + glyphHalf, top + 0.002f, 0.08f),
            ];
            parts.Add((LowPolyMesh.Ribbon(rune, 0.011f, tint: new Color(0.90f, 0.82f, 0.60f)), Transform3D.Identity));
        }

        return LowPolyMesh.Merge(parts);
    }

    /// <summary>亮条网格：x ∈ ±0.5、y ∈ [0, 0.03]、z ∈ [0, 0.07]（与改前描边盒逐值同包围盒），顶边倒角 0.008、侧面亮度 0.92。</summary>
    private static ArrayMesh ZoneStripMesh()
    {
        const float width = 0.07f, height = 0.03f;
        ArrayMesh strip = LowPolyMesh.BeveledSlab(new Vector3(BoardGeometry.CellSize, height, width), 0.008f, 1f, top: height, sideShade: 0.92f);
        return LowPolyMesh.Merge([(strip, new Transform3D(Basis.Identity, new Vector3(0f, 0f, width * 0.5f)))]);
    }

    /// <summary>边框网格：x ∈ ±0.5、y ∈ [0, 0.065]、z ∈ ±0.065，顶边倒角 0.015，顶面四个三角面按档散列带 ±2% 色差，侧面亮度 0.92。</summary>
    private static ArrayMesh PlateFrameMesh(int variant)
    {
        var parts = new List<(ArrayMesh, Transform3D)>
        {
            (LowPolyMesh.BeveledSlab(new Vector3(BoardGeometry.CellSize, 0.065f, 0.13f), 0.015f, 1f,
                seed: 701 + Wrap(variant, PlateFrame.Variants), facetJitter: 0.04f, top: 0.065f, sideShade: 0.92f), Transform3D.Identity),
        };
        foreach (float z in new[] { -0.032f, 0.032f })
        {
            parts.Add((LowPolyMesh.Ribbon([new Vector3(-0.43f, 0.0665f, z), new Vector3(0.43f, 0.0665f, z)],
                0.009f, shade: 0.70f, minShade: 0.68f), Transform3D.Identity));
        }

        return LowPolyMesh.Merge(parts);
    }

    /// <summary>转角件网格：x、z ∈ ±0.07，y ∈ [0, 0.07]，顶边倒角 0.02。</summary>
    private static ArrayMesh PlateCornerMesh() => LowPolyMesh.BeveledSlab(new Vector3(0.14f, 0.07f, 0.14f), 0.02f, 1f, top: 0.07f, sideShade: 0.92f);

    /// <summary>
    /// 岩层网格（单位块）：外廓是四角切去 0.035 的八边形，长边各分 4 段；自上而下 4 圈（3 条色带），每圈向内收 taper × 圈序 / 3（底圈收到 0.46），
    /// 中间两圈的段点按散列再向内凹 ≤ 0.012（外轮廓不超出顶圈）。顶面不建（压在上一层 / 底座下面），底面封上。
    /// </summary>
    private static ArrayMesh IslandLayerMesh(int variant)
    {
        int seed = 801 + Wrap(variant, IslandLayer.Variants);
        const float chamfer = 0.055f, taper = 0.065f, jitter = 0.018f;
        const int segments = 5, bands = 3;

        // 外廓（顶圈，半边长 0.5）：四条长边各 segments + 1 个点，相邻两边之间那一小段即切角。
        var outline = new List<Vector2>();
        Vector2[] corners = [new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0.5f, 0.5f), new(-0.5f, 0.5f)];
        for (int side = 0; side < 4; side++)
        {
            Vector2 a = corners[side], b = corners[(side + 1) % 4];
            Vector2 along = (b - a).Normalized();
            Vector2 start = a + (along * chamfer), end = b - (along * chamfer);
            for (int c = 0; c <= segments; c++)
            {
                outline.Add(start.Lerp(end, c / (float)segments));
            }
        }

        var rings = new List<Vector3[]>();
        var shades = new List<float>();
        for (int r = 0; r <= bands; r++)
        {
            float scale = 1f - (2f * taper * r / bands);
            var ring = new Vector3[outline.Count];
            for (int i = 0; i < outline.Count; i++)
            {
                bool rough = r > 0 && r < bands && (i % (segments + 1)) is > 0 and < segments;
                float dent = rough ? jitter * LowPolyMesh.Unit(seed, (r * 100) + i) * 2f : 0f;
                Vector2 p = outline[i] * (scale - dent);
                ring[i] = new Vector3(p.X, -r / (float)bands, p.Y);
            }

            rings.Add(ring);
            if (r < bands)
            {
                shades.Add(1f - (0.025f * r) - (0.01f * LowPolyMesh.Unit(seed, 900 + r)));
            }
        }

        return LowPolyMesh.Loft(rings, shades, capBottom: true);
    }

    /// <summary>
    /// 垂岩网格（单位长倒锥）：顶圈 y = 1、半径 1；中段两圈 y = 0.62 / 0.30、半径 0.58 / 0.27，按散列在 ±0.08 内横向歪斜、各点径向抖 ±12%；尖在原点。
    /// 0、2 档五棱，1、3 档六棱。顶面封上（贴着岩层底面，几乎看不见）。
    /// </summary>
    private static ArrayMesh IslandSpikeMesh(int variant)
    {
        int v = Wrap(variant, IslandSpike.Variants);
        int seed = 901 + v;
        int sides = 6 + (v & 1);
        (float Y, float Radius, float Lean)[] profile = [(1f, 1f, 0f), (0.68f, 0.67f, 0.055f), (0.32f, 0.31f, 0.055f), (0f, 0f, 0f)];
        var rings = new List<Vector3[]>();
        for (int r = 0; r < profile.Length; r++)
        {
            (float y, float radius, float lean) = profile[r];
            var offset = new Vector2(LowPolyMesh.Signed(seed, 50 + r) * lean, LowPolyMesh.Signed(seed, 60 + r) * lean);
            var ring = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float angle = (2f * MathF.PI * k / sides) + (LowPolyMesh.Signed(seed, 70) * 0.3f);
                float rr = radius * (1f + (0.12f * LowPolyMesh.Signed(seed, (r * 10) + k)));
                ring[k] = new Vector3(offset.X + (MathF.Cos(angle) * rr), y, offset.Y + (MathF.Sin(angle) * rr));
            }

            rings.Add(ring);
        }

        return LowPolyMesh.Loft(rings, [1f, 0.96f, 0.93f], capTop: true);
    }

    /// <summary>云团网格：多面扁团（经线 8 / 9 / 7 条，径向抖 ±10%），半径约 1、厚约 0.55，各档横向略拉长、错开。</summary>
    private static ArrayMesh CloudMesh(int variant)
    {
        int v = Wrap(variant, Cloud.Variants);
        Vector3 stretch = v switch
        {
            0 => new Vector3(1.04f, 0.275f, 0.96f),
            1 => new Vector3(0.98f, 0.29f, 1.02f),
            _ => new Vector3(1.08f, 0.26f, 0.92f),
        };
        return LowPolyMesh.FacetRock(1f, v switch { 0 => 8, 1 => 9, _ => 7 }, 0.10f, 1001 + v, stretch);
    }

    /// <summary>浮岛三档岩色（改前 AddFloatingIsland 的三份材质色）：0 土层、1 岩层、2 深岩。</summary>
    private static Color IslandColor(int tier) => tier switch
    {
        0 => ArtSoil.Darkened(0.10f),
        1 => ArtStone.Darkened(0.11f),
        _ => ArtDarkStone.Darkened(0.18f),
    };

    /// <summary>打开顶点色的哑光材质（浮岛部件内置）。</summary>
    private static StandardMaterial3D VertexMaterial(Color color, float roughness)
    {
        StandardMaterial3D material = Visuals.Matte(color, roughness);
        material.VertexColorUseAsAlbedo = true;
        return material;
    }

    /// <summary>云团材质：不受光的浅暮紫半透明色（Visuals.Flat，α 0.55），打开顶点色（顶点色 α 恒 1，乘出来仍是 0.55）。</summary>
    private static StandardMaterial3D CloudMaterial()
    {
        StandardMaterial3D material = Visuals.Flat(new Color(0.95f, 0.93f, 0.99f, 0.55f));
        material.VertexColorUseAsAlbedo = true;
        return material;
    }

    private static int Wrap(int variant, int variants) => ((variant % variants) + variants) % variants;

    private static PackedScene? SceneOf(Kind kind, int variant)
    {
        string path = $"{Directory}/{kind.FileName(variant)}";
        if (!Cache.TryGetValue(path, out PackedScene? scene))
        {
            scene = ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
            Cache[path] = scene;
        }

        return scene;
    }
}
