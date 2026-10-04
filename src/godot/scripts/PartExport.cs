using Godot;

namespace Siege.Godot;

/// <summary>
/// 把 <see cref="LowPoly"/> 程序生成的地形 / 设施部件导出成 Godot 场景资源（<c>.tscn</c>），供编辑器里摆放、做关卡或替换美术时直接引用。
/// 部件本体仍只有 <see cref="LowPoly"/> 一处定义，这里只是"烘焙一份快照"——改了 <see cref="LowPoly"/> 就重跑一次导出，不手改产物。
/// </summary>
/// <remarks>
/// 同色同粗糙度的材质合并成一份 <c>materials/*.tres</c>，各部件场景外部引用它（改一处材质，全部部件跟着变）；网格作为场景内子资源内嵌。
/// 带顶点色亮度系数的材质（<see cref="Visuals.Shaded"/>）另存为 <c>shaded_*.tres</c>，文件名取还原后的底色；不受光的材质存为 <c>flat_*.tres</c>；由棋盘覆盖材质的部件带的是中性白那一份。
/// <see cref="LowPolyMesh"/> 建的网格内嵌进场景；导出时每件部件建两遍、逐项比对顶点数组，不一致即记为失败（确定性自检）。
/// 每类部件导出 <see cref="TerrainParts"/> 里登记的全部档数。
/// </remarks>
public static class PartExport
{
    /// <summary>导出全部部件到 <paramref name="dir"/>（<c>res://</c> 或绝对路径），返回出错的条数。</summary>
    public static int Run(string dir)
    {
        dir = dir.TrimEnd('/', '\\');
        string materialDir = $"{dir}/materials";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(materialDir));

        // 部件清单与变体档数只在 TerrainParts 一处（棋盘加载资源用的也是这张表）。导出一律走程序生成，不读已有资源——资源是它的快照。
        var parts = new List<(string Name, Node3D Node)>();
        int failures = 0;
        foreach (TerrainParts.Kind kind in TerrainParts.All)
        {
            int triangles = 0;
            for (int v = 0; v < kind.Variants; v++)
            {
                string name = Path.GetFileNameWithoutExtension(kind.FileName(v));
                Node3D node = kind.Build(v);
                Node3D again = kind.Build(v);
                if (!SameMeshes(node, again))
                {
                    GD.PrintErr($"[export-parts] {name} 两次建模的顶点数组不一致（建模里用了不确定的量）");
                    failures++;
                }

                again.Free();
                triangles = Math.Max(triangles, TrianglesOf(node));
                parts.Add((name, node));
            }

            GD.Print($"[export-parts] {kind.Name} ×{kind.Variants}：锚点 {kind.Anchor}，材质{(kind.Recolored ? "由棋盘覆盖（中性白）" : "内置")}，单件最多 {triangles} 个三角形");
        }

        var materials = new Dictionary<string, StandardMaterial3D>();
        foreach ((string name, Node3D node) in parts)
        {
            node.Name = ToPascal(name);
            ShareMaterials(node, node, materials, materialDir, ref failures);
            var scene = new PackedScene();
            Error packed = scene.Pack(node);
            Error saved = packed == Error.Ok ? ResourceSaver.Save(scene, $"{dir}/{name}.tscn") : packed;
            if (saved != Error.Ok)
            {
                GD.PrintErr($"[export-parts] {name}.tscn 失败：{saved}");
                failures++;
            }

            node.Free();
        }

        GD.Print($"[export-parts] 部件 {parts.Count - failures} / {parts.Count}、共享材质 {materials.Count} → {dir}");
        return failures;
    }

    /// <summary>设 Owner（Pack 只收 Owner 为根的节点），并把材质换成按颜色去重、已存盘的共享资源。</summary>
    private static void ShareMaterials(Node3D root, Node node, Dictionary<string, StandardMaterial3D> materials, string materialDir, ref int failures)
    {
        foreach (Node child in node.GetChildren())
        {
            child.Owner = root;
            if (child is MeshInstance3D { MaterialOverride: StandardMaterial3D m } mesh)
            {
                // 带顶点色亮度系数的材质底色里含增益，起名用还原后的底色；与同色的普通哑光材质分开存。
                string key = m.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded
                    ? $"flat_{m.AlbedoColor.ToHtml(true)}"
                    : m.VertexColorUseAsAlbedo
                        ? $"shaded_{LowPolyMesh.BaseOf(m.AlbedoColor).ToHtml(false)}_r{m.Roughness * 100f:0}"
                        : $"matte_{m.AlbedoColor.ToHtml(false)}_r{m.Roughness * 100f:0}";
                if (!materials.TryGetValue(key, out StandardMaterial3D? shared))
                {
                    shared = m;
                    string path = $"{materialDir}/{key}.tres";
                    Error saved = ResourceSaver.Save(shared, path);
                    if (saved != Error.Ok)
                    {
                        GD.PrintErr($"[export-parts] 材质 {path} 失败：{saved}");
                        failures++;
                    }

                    shared.TakeOverPath(path);
                    materials[key] = shared;
                }

                mesh.MaterialOverride = shared;
            }

            ShareMaterials(root, child, materials, materialDir, ref failures);
        }
    }

    private static IEnumerable<MeshInstance3D> MeshNodes(Node node) =>
        node.GetChildren().SelectMany(child => (child is MeshInstance3D mesh ? new[] { mesh } : []).Concat(MeshNodes(child)));

    private static int TrianglesOf(Node3D part) => MeshNodes(part).Sum(m => m.Mesh?.GetFaces().Length / 3 ?? 0);

    /// <summary>两件部件的各个网格是否逐项相同：变换相同，且每个表面的顶点、法线、顶点色数组逐项相等。</summary>
    private static bool SameMeshes(Node3D a, Node3D b)
    {
        MeshInstance3D[] left = [.. MeshNodes(a)], right = [.. MeshNodes(b)];
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            Mesh? x = left[i].Mesh, y = right[i].Mesh;
            if (left[i].Transform != right[i].Transform || x is null || y is null || x.GetSurfaceCount() != y.GetSurfaceCount())
            {
                return false;
            }

            for (int surface = 0; surface < x.GetSurfaceCount(); surface++)
            {
                global::Godot.Collections.Array p = x.SurfaceGetArrays(surface), q = y.SurfaceGetArrays(surface);
                if (!p[(int)Mesh.ArrayType.Vertex].AsVector3Array().SequenceEqual(q[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    || !p[(int)Mesh.ArrayType.Normal].AsVector3Array().SequenceEqual(q[(int)Mesh.ArrayType.Normal].AsVector3Array())
                    || !p[(int)Mesh.ArrayType.Color].AsColorArray().SequenceEqual(q[(int)Mesh.ArrayType.Color].AsColorArray()))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string ToPascal(string name) =>
        string.Concat(name.Split('_').Select(s => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..]));
}
