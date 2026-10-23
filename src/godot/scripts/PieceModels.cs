using System.Collections.Generic;
using Godot;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// The ten editable full-art pieces. PieceStyleTable and FactionTable remain the only
/// type and owner mappings; BoardView uses LowPoly.Piece if a scene is unavailable.
/// </summary>
internal static class PieceModels
{
    // Scene parts are merged once by material slot. The editable .tscn files remain the source,
    // while each on-board piece uses only a few shared meshes and its own materials.
    private static readonly Dictionary<PieceSilhouette, Dictionary<string, ArrayMesh>> Templates = new();
    private static readonly Dictionary<BannerEmblem, ArrayMesh> EmblemMeshes = new();
    private static readonly HashSet<PieceSilhouette> ReportedFallbacks = new();

    private static readonly CylinderMesh FootMesh = new()
    {
        BottomRadius = 0.36f, TopRadius = 0.35f, Height = 0.042f, RadialSegments = 10,
    };

    private static readonly CylinderMesh EnamelBandMesh = new()
    {
        BottomRadius = 0.345f, TopRadius = 0.325f, Height = 0.05f, RadialSegments = 10,
    };

    private static readonly CylinderMesh UpperLipMesh = new()
    {
        BottomRadius = 0.326f, TopRadius = 0.31f, Height = 0.022f, RadialSegments = 10,
    };

    private static readonly CylinderMesh SealBackMesh = new()
    {
        BottomRadius = 0.151f, TopRadius = 0.151f, Height = 0.006f, RadialSegments = 12,
    };

    private static readonly SphereMesh StudMesh = new()
    {
        Radius = 0.023f, Height = 0.046f, RadialSegments = 6, Rings = 3,
    };

    private static readonly ArrayMesh DuskPlinthMesh = Merge(
        (FootMesh, new Vector3(0f, 0.021f, 0f)),
        (SealBackMesh, new Vector3(0f, 0.116f, 0.194f)));

    private static readonly ArrayMesh EnamelPlinthMesh = Merge(
        (EnamelBandMesh, new Vector3(0f, 0.065f, 0f)));

    private static readonly ArrayMesh StonePlinthMesh = Merge(
        (UpperLipMesh, new Vector3(0f, 0.101f, 0f)));

    private static readonly ArrayMesh StudsMesh = Merge(
        (StudMesh, new Vector3(-0.263f, 0.115f, 0f)),
        (StudMesh, new Vector3(0.263f, 0.115f, 0f)),
        (StudMesh, new Vector3(0f, 0.115f, -0.263f)));

    internal static Node3D? TryBuild(
        PieceSilhouette silhouette, BannerEmblem emblem, Color faction, Color body, Color ink, int emphasisPercent)
    {
        string path = silhouette switch
        {
            PieceSilhouette.RoundPawn => "res://parts/pieces/basic.tscn",
            PieceSilhouette.Tower => "res://parts/pieces/fortress.tscn",
            PieceSilhouette.TwinOrbBar => "res://parts/pieces/line.tscn",
            PieceSilhouette.Pyramid => "res://parts/pieces/multiplier.tscn",
            PieceSilhouette.CrystalCluster => "res://parts/pieces/synergy.tscn",
            PieceSilhouette.Scaffold => "res://parts/pieces/artisan.tscn",
            PieceSilhouette.Pennant => "res://parts/pieces/bannerman.tscn",
            PieceSilhouette.ChainLinks => "res://parts/pieces/chain.tscn",
            PieceSilhouette.CrossedSpears => "res://parts/pieces/sentry.tscn",
            PieceSilhouette.Stele => "res://parts/pieces/boundary.tscn",
            _ => string.Empty,
        };
        if (path.Length == 0)
        {
            ReportFallback(silhouette, path);
            return null;
        }

        if (!Templates.TryGetValue(silhouette, out Dictionary<string, ArrayMesh>? meshes))
        {
            if (!ResourceLoader.Exists(path))
            {
                ReportFallback(silhouette, path);
                return null;
            }

            PackedScene? template = ResourceLoader.Load<PackedScene>(path);
            if (template is null)
            {
                ReportFallback(silhouette, path);
                return null;
            }

            try
            {
                Node instance = template.Instantiate();
                try
                {
                    meshes = MergeScene(instance);
                }
                finally
                {
                    instance.Free();
                }
            }
            catch (System.Exception error)
            {
                GD.PushWarning($"[piece-models] 无法合并 {path}：{error.Message}");
                ReportFallback(silhouette, path);
                return null;
            }
            if (meshes is null)
            {
                ReportFallback(silhouette, path);
                return null;
            }

            Templates.Add(silhouette, meshes);
            GD.Print($"[piece-models] 场景已加载 {silhouette}：{path}（{Templates.Count}/10）");
        }

        Node3D piece = BuildPieceRoot(emblem, ink);
        var artBody = new Node3D { Name = "ArtBody" };
        foreach ((string slot, ArrayMesh mesh) in meshes)
        {
            // BoardView.Translucent edits MaterialOverride for staged pieces. The mesh is shared,
            // but its material is never shared between instances.
            artBody.AddChild(new MeshInstance3D
            {
                Name = slot,
                Mesh = mesh,
                MaterialOverride = MaterialFor(slot, faction, body, ink, emphasisPercent),
            });
        }

        piece.AddChild(artBody);
        return piece;
    }

    private static void ReportFallback(PieceSilhouette silhouette, string path)
    {
        if (ReportedFallbacks.Add(silhouette))
        {
            GD.Print($"[piece-models] 程序回退 {silhouette}：{path}");
        }
    }

    private static Node3D BuildPieceRoot(BannerEmblem emblem, Color ink)
    {
        var root = new Node3D { Name = "Piece" };
        if (!EmblemMeshes.TryGetValue(emblem, out ArrayMesh? mesh))
        {
            mesh = LowPoly.EmblemMesh(emblem, 0.30f);
            EmblemMeshes.Add(emblem, mesh);
        }

        // The plinth's stone, enamel and metal are merged into the matching body slots below.
        // This emblem remains its own surface so the HUD and 3D seal use the same geometry.
        root.AddChild(new MeshInstance3D
        {
            Name = "Emblem",
            Mesh = mesh,
            MaterialOverride = Visuals.Flat(ink),
            Position = new Vector3(0f, 0.120f, 0.194f),
        });
        return root;
    }

    private static ArrayMesh Merge(params (Mesh Mesh, Vector3 Offset)[] parts)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach ((Mesh mesh, Vector3 offset) in parts)
        {
            tool.AppendFrom(mesh, 0, new Transform3D(Basis.Identity, offset));
        }

        return tool.Commit();
    }

    private static Dictionary<string, ArrayMesh>? MergeScene(Node scene)
    {
        var tools = new Dictionary<string, SurfaceTool>();
        int partCount = 0;
        if (!Append(scene, Transform3D.Identity, tools, ref partCount) || partCount == 0)
        {
            return null;
        }

        // These are the same shared plinth for all ten silhouettes. Fold them into the body
        // material slots so a richer base costs no additional draw calls per piece.
        AppendPart(tools, "Shade", DuskPlinthMesh);
        AppendPart(tools, "Enamel", EnamelPlinthMesh);
        AppendPart(tools, "Stone", StonePlinthMesh);
        AppendPart(tools, "Metal", StudsMesh);

        var meshes = new Dictionary<string, ArrayMesh>();
        foreach ((string slot, SurfaceTool tool) in tools)
        {
            meshes.Add(slot, tool.Commit());
        }

        return meshes;
    }

    private static void AppendPart(Dictionary<string, SurfaceTool> tools, string slot, Mesh mesh)
    {
        if (!tools.TryGetValue(slot, out SurfaceTool? tool))
        {
            tool = new SurfaceTool();
            tool.Begin(Mesh.PrimitiveType.Triangles);
            tools.Add(slot, tool);
        }

        tool.AppendFrom(mesh, 0, Transform3D.Identity);
    }

    private static bool Append(Node node, Transform3D parent,
        Dictionary<string, SurfaceTool> tools, ref int partCount)
    {
        Transform3D transform = node is Node3D spatial ? parent * spatial.Transform : parent;
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
        {
            string? slot = SlotOf(instance.Name.ToString());
            if (slot is null || mesh.GetSurfaceCount() == 0)
            {
                return false;
            }

            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                if (!tools.TryGetValue(slot, out SurfaceTool? tool))
                {
                    tool = new SurfaceTool();
                    tool.Begin(Mesh.PrimitiveType.Triangles);
                    tools.Add(slot, tool);
                }

                tool.AppendFrom(mesh, surface, transform);
            }

            partCount++;
        }

        foreach (Node child in node.GetChildren())
        {
            if (!Append(child, transform, tools, ref partCount))
            {
                return false;
            }
        }

        return true;
    }

    private static string? SlotOf(string name)
    {
        int split = name.IndexOf('_');
        if (split <= 0)
        {
            return null;
        }

        return name[..split] switch
        {
            "Stone" => "Stone",
            "Shade" => "Shade",
            "Metal" => "Metal",
            "Enamel" => "Enamel",
            "Cloth" => "Cloth",
            "Crystal" => "Crystal",
            "Ink" => "Ink",
            _ => null,
        };
    }

    private static StandardMaterial3D MaterialFor(
        string slot, Color faction, Color body, Color ink, int emphasisPercent)
    {
        Color color = slot switch
        {
            "Stone" => body.Lerp(Visuals.Damp(Color.Color8(193, 179, 158), emphasisPercent), 0.58f),
            "Shade" => Visuals.Damp(Color.Color8(92, 82, 91), emphasisPercent),
            "Metal" => Visuals.Damp(Color.Color8(177, 145, 91), emphasisPercent),
            "Enamel" => faction,
            "Cloth" => faction.Lerp(Visuals.Damp(Color.Color8(207, 185, 163), emphasisPercent), 0.12f),
            "Crystal" => body.Lerp(Visuals.Damp(Color.Color8(166, 190, 199), emphasisPercent), 0.34f),
            "Ink" => ink,
            _ => throw new System.ArgumentOutOfRangeException(nameof(slot), slot, "Unknown piece material slot."),
        };
        float roughness = slot switch
        {
            "Metal" => 0.70f,
            "Enamel" => 0.68f,
            "Crystal" => 0.58f,
            _ => 0.88f,
        };
        return Visuals.Matte(color, roughness);
    }
}
