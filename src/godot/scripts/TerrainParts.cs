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

    /// <summary>一类部件：名字、变体档数（1 表示没有变体后缀）、程序生成函数。</summary>
    public sealed record Kind(string Name, int Variants, Func<int, Node3D> Build)
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

    /// <summary>全部部件，导出顺序即此顺序。</summary>
    public static readonly Kind[] All = [Rock, Ruins, Pines, Trees, Desert, Marsh, Crag, Shallows, FenceX, FenceZ, Bridge];

    private static readonly Dictionary<string, PackedScene?> Cache = [];

    /// <summary>本进程里从资源加载的部件数（供启动日志自证"确实在用资源"）。</summary>
    public static int LoadedCount { get; private set; }

    /// <summary>本进程里退回程序生成的部件数。</summary>
    public static int GeneratedCount { get; private set; }

    /// <summary>取一件部件：有资源就实例化资源，否则程序生成。返回节点原点在地砖上表面（与 <see cref="LowPoly"/> 同一约定）。</summary>
    public static Node3D Create(Kind kind, int variant = 0)
    {
        string path = $"{Directory}/{kind.FileName(variant)}";
        if (!Cache.TryGetValue(path, out PackedScene? scene))
        {
            scene = ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
            Cache[path] = scene;
        }

        if (scene?.Instantiate() is Node3D node)
        {
            LoadedCount++;
            return node;
        }

        GeneratedCount++;
        return kind.Build(variant);
    }
}
