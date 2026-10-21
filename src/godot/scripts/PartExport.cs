using Godot;

namespace Siege.Godot;

/// <summary>
/// 把 <see cref="LowPoly"/> 程序生成的地形 / 设施部件导出成 Godot 场景资源（<c>.tscn</c>），供编辑器里摆放、做关卡或替换美术时直接引用。
/// 部件本体仍只有 <see cref="LowPoly"/> 一处定义，这里只是"烘焙一份快照"——改了 <see cref="LowPoly"/> 就重跑一次导出，不手改产物。
/// </summary>
/// <remarks>
/// 同色同粗糙度的材质合并成一份 <c>materials/*.tres</c>，各部件场景外部引用它（改一处材质，全部部件跟着变）；网格作为场景内子资源内嵌。
/// 变体只导出"看得出差别"的那几档（档数见 <see cref="TerrainParts"/>）：岩石 / 遗迹的朝向随 variant 连续变，取前 4 档；松树丛布局按 variant % 3、秋色按 variant % 7 == 3，取 0–6 共 7 档。
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
        foreach (TerrainParts.Kind kind in TerrainParts.All)
        {
            for (int v = 0; v < kind.Variants; v++)
            {
                parts.Add((Path.GetFileNameWithoutExtension(kind.FileName(v)), kind.Build(v)));
            }
        }

        var materials = new Dictionary<string, StandardMaterial3D>();
        int failures = 0;
        var triangles = new List<string>();
        foreach ((string name, Node3D node) in parts)
        {
            node.Name = ToPascal(name);
            ShareMaterials(node, node, materials, materialDir, ref failures);
            if (StableMeshIds(node, name) is int count and > 0)
            {
                triangles.Add($"{name} {count}");
            }

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
        GD.Print($"[export-parts] 程序建模网格（LowPolyMesh）三角形数：{string.Join("、", triangles)}");
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
                // 打开顶点色的材质（LowPolyMesh 部件）键里多一个 _vc：与同色同粗糙度、不用顶点色的既有材质分开存，既有材质文件名不变。
                string key = $"{m.AlbedoColor.ToHtml(false)}_r{m.Roughness * 100f:0}{(m.VertexColorUseAsAlbedo ? "_vc" : "")}";
                if (!materials.TryGetValue(key, out StandardMaterial3D? shared))
                {
                    shared = m;
                    string path = $"{materialDir}/matte_{key}.tres";
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

    /// <summary>
    /// 程序建模的网格（<see cref="ArrayMesh"/>，LowPolyMesh 产出）按"部件名_序号"给定子资源 id，免得每次导出随机取名；返回这些网格的三角形总数（导出日志自证面数）。
    /// 内置几何体（BoxMesh 等）不动——既有部件的产物与引入本条之前相同。
    /// </summary>
    private static int StableMeshIds(Node node, string name)
    {
        int triangles = 0, index = 0;
        Visit(node);
        return triangles;

        void Visit(Node current)
        {
            foreach (Node child in current.GetChildren())
            {
                if (child is MeshInstance3D { Mesh: ArrayMesh mesh })
                {
                    mesh.ResourceSceneUniqueId = $"{name}_{index++}";
                    triangles += mesh.GetFaces().Length / 3;
                }

                Visit(child);
            }
        }
    }

    private static string ToPascal(string name) =>
        string.Concat(name.Split('_').Select(s => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..]));
}
