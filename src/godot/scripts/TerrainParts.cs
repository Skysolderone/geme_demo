using Godot;

namespace Siege.Godot;

/// <summary>
/// 地形 / 设施部件目录：部件名、变体档数与程序生成函数的唯一一张表。<see cref="PartExport"/> 按它导出 <c>.tscn</c>，
/// <see cref="BoardView"/> 按它取部件——<b>有资源就加载资源</b>（<c>res://parts/terrain/&lt;名&gt;_&lt;变体 % 档数&gt;.tscn</c>），
/// 没有才退回 <see cref="LowPoly"/> 程序生成。两边共用同一张表，变体档数不会对不上。
/// </summary>
/// <remarks>
/// <para><b>锚点</b>（map-elements-v2 D2）：每类部件的原点落在哪，写在 <see cref="Kind.Anchor"/> 里；装饰与设施类一律是所在格的地砖上表面中心。</para>
/// <para><b>覆盖材质</b>：<see cref="Kind.Recolored"/> 的部件（地砖顶板、衬底、铺面）导出时带中性白材质，棋盘取用时换成按底色共用的那一份——
/// 信息层压暗、出生区淡染、明暗棋盘格都只改那份材质的底色。</para>
/// <para><b>单网格部件</b>（地块类）不逐格实例化场景：<see cref="ShapeOf"/> 把网格、局部变换与内置材质取出来缓存，棋盘逐格只建一个网格节点。</para>
/// <para>资源是 <see cref="LowPoly"/> 的烘焙快照，也可以被美术替换成正式模型：只要文件名不变、原点仍在地砖上表面，棋盘就直接用新模型。</para>
/// <para>各部件的造型只有目录里登记的那几档：程序建模按档数取模，与资源版是同一组造型。</para>
/// <para>每条路径的 <see cref="PackedScene"/> 只加载一次并缓存；不存在的路径也记下，不重复探测。</para>
/// </remarks>
public static class TerrainParts
{
    /// <summary>部件资源目录。</summary>
    public const string Directory = "res://parts/terrain";

    /// <summary>
    /// 一类部件：名字、变体档数（1 表示没有变体后缀）、程序生成函数、锚点说明（部件原点落在哪）、材质是否由棋盘覆盖。
    /// </summary>
    public sealed record Kind(string Name, int Variants, Func<int, Node3D> Build, string Anchor = "地砖上表面中心", bool Recolored = false)
    {
        /// <summary>第 <paramref name="variant"/> 档的资源文件名（不含目录）。</summary>
        public string FileName(int variant) => Variants == 1 ? $"{Name}.tscn" : $"{Name}_{((variant % Variants) + Variants) % Variants}.tscn";
    }

    public static readonly Kind Rock = new("rock", 6, LowPoly.Rock);
    public static readonly Kind Ruins = new("ruins", 6, LowPoly.Ruins);
    public static readonly Kind Pines = new("pines", 8, LowPoly.Pines);
    public static readonly Kind Trees = new("trees", 3, LowPoly.Trees);
    public static readonly Kind Desert = new("desert", 6, LowPoly.Desert);
    public static readonly Kind Marsh = new("marsh", 6, LowPoly.Marsh);
    public static readonly Kind Crag = new("crag", 4, LowPoly.Crag);
    public static readonly Kind Shallows = new("shallows", 6, LowPoly.Shallows);
    public static readonly Kind FenceX = new("fence_x", 1, _ => LowPoly.Fence(alongX: true));
    public static readonly Kind FenceZ = new("fence_z", 1, _ => LowPoly.Fence(alongX: false));
    public static readonly Kind Bridge = new("bridge", 1, _ => LowPoly.Bridge());

    // 地块（map-elements-v2 段 A）：单网格部件，经 ShapeOf 取用。
    public static readonly Kind TileTop = new("tile_top", 3, LowPoly.TileTop, Recolored: true);
    public static readonly Kind SideSlope = new("side_slope", 2, LowPoly.SideSlope, "该层带的顶面中心");
    public static readonly Kind SideCliff = new("side_cliff", 3, LowPoly.SideCliff, "该层带的顶面中心");
    public static readonly Kind Liner = new("liner", 1, _ => LowPoly.Liner(), "衬底中心", Recolored: true);
    public static readonly Kind SceneSlab = new("scene_slab", 1, _ => LowPoly.SceneSlab(), Recolored: true);

    // 水系（段 B）。
    public static readonly Kind WaterBed = new("water_bed", 2, LowPoly.WaterBed, "水格的地砖上表面中心", Recolored: true);
    public static readonly Kind BankLip = new("bank_lip", 3, LowPoly.BankLip, "水格一条边的中点（地砖上表面高度），边沿 X 轴、水在 +Z 一侧");

    // 外框与底座（段 D）。
    public static readonly Kind Rim = new("rim", 3, LowPoly.Rim, "底座边缘一段的中点（底座顶面高度），段沿 X 轴、棋盘在 +Z 一侧");
    public static readonly Kind ZoneStrip = new("zone_strip", 1, _ => LowPoly.ZoneStrip(), "格边中点（地砖上表面高度），条沿 Z 轴、格外在 +X 一侧", Recolored: true);
    public static readonly Kind PlateFrame = new("plate_frame", 2, LowPoly.PlateFrame, "边框一段的中点（地砖上表面高度）；第 0 档是沿 X 轴的一段，第 1 档是角块", Recolored: true);
    public static readonly Kind IslandLayer = new("island_layer", 3, LowPoly.IslandLayer, "岩层顶面中心（单位尺寸，由棋盘缩放）");
    public static readonly Kind IslandSpike = new("island_spike", 4, LowPoly.IslandSpike, "垂岩高度中点（单位尺寸，由棋盘缩放）");
    public static readonly Kind Cloud = new("cloud", 3, LowPoly.Cloud, "云心（单位尺寸，由棋盘缩放）");

    /// <summary>全部部件，导出顺序即此顺序。</summary>
    public static readonly Kind[] All =
    [
        Rock, Ruins, Pines, Trees, Desert, Marsh, Crag, Shallows, FenceX, FenceZ, Bridge,
        TileTop, SideSlope, SideCliff, Liner, SceneSlab,
        WaterBed, BankLip,
        Rim, ZoneStrip, PlateFrame, IslandLayer, IslandSpike, Cloud,
    ];

    /// <summary>单网格部件取出来的"形"：网格、网格节点在部件里的局部变换、部件自带的材质（覆盖材质的部件不用它）、是否来自资源。</summary>
    public sealed record Shape(Mesh Mesh, Transform3D Local, Material? Material, bool FromResource);

    private static readonly Dictionary<string, Shape> Shapes = [];

    private static readonly Dictionary<string, PackedScene?> Cache = [];

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
        return kind.Build(((variant % kind.Variants) + kind.Variants) % kind.Variants);
    }

    /// <summary>
    /// 合批用的模板（board-render-perf D2）：该档资源的一份实例，<b>不计数</b>；没有资源时返回 <c>null</c>——
    /// 没有资源就没有可复用的模板实例，调用方应退回 <see cref="Create"/> 逐件画（程序建模）。
    /// </summary>
    public static Node3D? Template(Kind kind, int variant) => SceneOf(kind, variant)?.Instantiate() as Node3D;

    /// <summary>合批放置了一件来自资源的部件（实例由 MultiMesh 画，不再各建节点）：只记数，让启动日志的"资源 N 件"仍是放置件数。</summary>
    public static void CountLoaded() => LoadedCount++;

    /// <summary>
    /// 单网格部件的形：有资源就取资源里的网格，否则程序建模（按档数取模，与资源版同一组造型）。每档只取一次并缓存——
    /// 同一档的各格共用同一份网格，渲染器把"同网格 + 同材质"的节点合成一次绘制。
    /// 资源不是"根下恰好一个网格节点"的形状时（比如被换成了多件模型）也退回程序建模。
    /// </summary>
    public static Shape ShapeOf(Kind kind, int variant)
    {
        string key = kind.FileName(variant);
        if (!Shapes.TryGetValue(key, out Shape? shape))
        {
            shape = Extract(SceneOf(kind, variant)?.Instantiate() as Node3D, fromResource: true)
                ?? Extract(kind.Build(((variant % kind.Variants) + kind.Variants) % kind.Variants), fromResource: false)
                ?? throw new InvalidOperationException($"部件 {key} 不是单网格部件。");
            Shapes[key] = shape;
        }

        return shape;
    }

    /// <summary>按 <see cref="ShapeOf"/> 的形摆了一件：记入"资源 / 程序生成"件数（启动日志自证部件来源）。</summary>
    public static void CountPlaced(Shape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.FromResource)
        {
            LoadedCount++;
        }
        else
        {
            GeneratedCount++;
        }
    }

    private static Shape? Extract(Node3D? template, bool fromResource)
    {
        if (template is null)
        {
            return null;
        }

        Shape? shape = template.GetChildCount() == 1 && template.GetChild(0) is MeshInstance3D { Mesh: { } mesh } part
            ? new Shape(mesh, part.Transform, part.MaterialOverride, fromResource)
            : null;
        template.Free();
        return shape;
    }

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
