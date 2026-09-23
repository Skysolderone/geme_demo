using Godot;

namespace Siege.Godot;

/// <summary>
/// 把 <see cref="LowPoly"/> 程序生成的地形 / 设施部件导出成 Godot 场景资源（<c>.tscn</c>），供编辑器里摆放、做关卡或替换美术时直接引用。
/// 部件本体仍只有 <see cref="LowPoly"/> 一处定义，这里只是"烘焙一份快照"——改了 <see cref="LowPoly"/> 就重跑一次导出，不手改产物。
/// </summary>
/// <remarks>
/// 同色同粗糙度的材质合并成一份 <c>materials/*.tres</c>，各部件场景外部引用它（改一处材质，全部部件跟着变）；网格作为场景内子资源内嵌。
/// 变体只导出"看得出差别"的那几档：岩石 / 遗迹的朝向随 variant 连续变，取前 4 档；松树丛布局按 variant % 3、秋色按 variant % 7 == 3，取 0–6 共 7 档。
/// </remarks>
public static class PartExport
{
    /// <summary>导出全部部件到 <paramref name="dir"/>（<c>res://</c> 或绝对路径），返回出错的条数。</summary>
    public static int Run(string dir)
    {
        dir = dir.TrimEnd('/', '\\');
        string materialDir = $"{dir}/materials";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(materialDir));

        var parts = new List<(string Name, Node3D Node)>();
        for (int v = 0; v < 4; v++)
        {
            parts.Add(($"rock_{v}", LowPoly.Rock(v)));
            parts.Add(($"ruins_{v}", LowPoly.Ruins(v)));
        }

        for (int v = 0; v < 7; v++)
        {
            parts.Add(($"pines_{v}", LowPoly.Pines(v)));
        }

        for (int v = 0; v < 3; v++)
        {
            parts.Add(($"trees_{v}", LowPoly.Trees(v)));
        }

        // 新地表（terrain-surfaces）：荒漠朝向按 variant % 2、侧臂按 variant % 3，取 0–5 共 6 档。
        for (int v = 0; v < 6; v++)
        {
            parts.Add(($"desert_{v}", LowPoly.Desert(v)));
        }

        parts.Add(("fence_x", LowPoly.Fence(alongX: true)));
        parts.Add(("fence_z", LowPoly.Fence(alongX: false)));
        parts.Add(("bridge", LowPoly.Bridge()));

        var materials = new Dictionary<string, StandardMaterial3D>();
        int failures = 0;
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
                string key = $"{m.AlbedoColor.ToHtml(false)}_r{m.Roughness * 100f:0}";
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

    private static string ToPascal(string name) =>
        string.Concat(name.Split('_').Select(s => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..]));
}
