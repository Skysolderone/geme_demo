using Godot;

namespace Siege.Godot;

/// <summary>
/// 地形 / 设施部件目录：部件名、变体档数与程序生成函数的唯一一张表。<see cref="PartExport"/> 按它导出 <c>.tscn</c>，
/// <see cref="BoardView"/> 按它取部件——<b>有资源就加载资源</b>（<c>res://parts/terrain/&lt;名&gt;_&lt;变体 % 档数&gt;.tscn</c>），
/// 没有才退回 <see cref="LowPoly"/> 程序生成。两边共用同一张表，变体档数不会对不上。
/// </summary>
/// <remarks>
/// <para>资源是 <see cref="LowPoly"/> 的烘焙快照，也可以被美术替换成正式模型：只要文件名不变、原点仍在地砖上表面，棋盘就直接用新模型。</para>
/// <para>代价：程序生成里连续变化的量（岩石 / 松树的朝向）在加载资源时只剩导出的那几档。</para>
/// <para>每条路径的 <see cref="PackedScene"/> 只加载一次并缓存；不存在的路径也记下，不重复探测。</para>
/// </remarks>
public static class TerrainParts
{
    /// <summary>部件资源目录。</summary>
    public const string Directory = "res://parts/terrain";

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

    public static readonly Kind Rock = new("rock", 4, LowPoly.Rock);
    public static readonly Kind Ruins = new("ruins", 4, LowPoly.Ruins);
    public static readonly Kind Pines = new("pines", 7, LowPoly.Pines);
    public static readonly Kind Trees = new("trees", 3, LowPoly.Trees);
    public static readonly Kind Desert = new("desert", 6, LowPoly.Desert);
    public static readonly Kind Marsh = new("marsh", 6, LowPoly.Marsh);
    public static readonly Kind Crag = new("crag", 4, LowPoly.Crag);
    public static readonly Kind Shallows = new("shallows", 6, LowPoly.Shallows);
    public static readonly Kind FenceX = new("fence_x", 1, _ => LowPoly.Fence(alongX: true));
    public static readonly Kind FenceZ = new("fence_z", 1, _ => LowPoly.Fence(alongX: false));
    public static readonly Kind Bridge = new("bridge", 1, _ => LowPoly.Bridge());

    // —— 地块部件（map-elements-v2 段 A，design D2 / D3）：每件只有一个 MeshInstance3D、一个 surface，棋盘逐格只取它的网格（见 PieceOf）。

    /// <summary>衬底厚度：面砖之下垫的那块铺满整格的薄板（BoardView 与 <see cref="Liner"/> 共用这一个值）。</summary>
    public const float LinerThickness = 0.03f;

    /// <summary>地砖顶板的倒角宽。</summary>
    public const float TileBevel = 0.03f;

    /// <summary>
    /// 地砖顶板（七种地表共用网格）：TileSize 见方、TileHeight 厚、顶面四边倒角，顶面四个三角面带去均值的细微色差（各档 ±2%，O-1：不盖过明暗棋盘格的 5%）。
    /// 材质由 BoardView 按底色覆盖；导出时带中性白材质。
    /// </summary>
    public static readonly Kind TileTop = new("tile_top", 3, v => Single("TileTop", TileTopMesh(v), NeutralMaterial()), "地砖上表面中心");

    /// <summary>第 1 层侧面（缓坡）：铺满整格、高一层，三条土层色带，棱与格边齐平。内置土色。</summary>
    public static readonly Kind SideSlope = new("side_slope", 2, v => Single("SideSlope", SideSlopeMesh(v), SideMaterial(Visuals.SlopeSide)), "该层带的顶面中心");

    /// <summary>第 2 层侧面（崖壁）：铺满整格、高一层，顶边岩沿与格边齐平、岩体内缩、每面一道深岩缝。内置岩灰。</summary>
    public static readonly Kind SideCliff = new("side_cliff", 3, v => Single("SideCliff", SideCliffMesh(v), SideMaterial(Visuals.CliffSide)), "该层带的顶面中心");

    /// <summary>砖缝衬底：铺满整格、厚 <see cref="LinerThickness"/>。材质由 BoardView 按底色覆盖；导出时带中性白材质。</summary>
    public static readonly Kind Liner = new("liner", 1, _ => Single("Liner", LinerMesh(), NeutralMaterial()), "衬底中心");

    /// <summary>全部部件，导出顺序即此顺序（新部件追加在末尾，既有部件的导出顺序不变）。</summary>
    public static readonly Kind[] All = [Rock, Ruins, Pines, Trees, Desert, Marsh, Crag, Shallows, FenceX, FenceZ, Bridge, TileTop, SideSlope, SideCliff, Liner];

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
    /// 程序生成的造型随 <paramref name="variant"/> 连续变化（朝向不按档数取模），归并不成有限几种，调用方应退回 <see cref="Create"/> 逐件画。
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

    private static ArrayMesh TileTopMesh(int variant) => LowPolyMesh.BeveledSlab(
        new Vector3(BoardGeometry.TileSize, BoardGeometry.TileHeight, BoardGeometry.TileSize), TileBevel, 0.98f,
        seed: 101 + Wrap(variant, TileTop.Variants), facetJitter: 0.02f, sideShade: 0.96f);

    private static ArrayMesh SideSlopeMesh(int variant) => LowPolyMesh.Strata(
        new Vector3(BoardGeometry.CellSize, BoardGeometry.LayerHeight, BoardGeometry.CellSize), bands: 3, jitter: 0.014f, seed: 201 + Wrap(variant, SideSlope.Variants));

    private static ArrayMesh SideCliffMesh(int variant) => LowPolyMesh.Strata(
        new Vector3(BoardGeometry.CellSize, BoardGeometry.LayerHeight, BoardGeometry.CellSize), bands: 3, jitter: 0.02f, seed: 301 + Wrap(variant, SideCliff.Variants),
        lip: 0.05f, crevice: 0.05f);

    private static ArrayMesh LinerMesh() => LowPolyMesh.BeveledSlab(
        new Vector3(BoardGeometry.CellSize, LinerThickness, BoardGeometry.CellSize), 0f, 1f, top: LinerThickness * 0.5f);

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
