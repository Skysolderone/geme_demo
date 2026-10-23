using System.Collections.Generic;
using Godot;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// Editable scene bodies for the art sample. The common procedural plinth and emblem stay in
/// <see cref="LowPoly.Piece"/>; a missing or invalid body leaves its procedural piece intact.
/// </summary>
internal static class PieceModels
{
    private static readonly Dictionary<PieceSilhouette, PackedScene> Templates = new();

    internal static Node3D? TryBuild(
        PieceSilhouette silhouette, BannerEmblem emblem, Color faction, Color body, Color ink, int emphasisPercent)
    {
        string? path = silhouette switch
        {
            PieceSilhouette.RoundPawn => "res://parts/pieces/basic.tscn",
            PieceSilhouette.Tower => "res://parts/pieces/fortress.tscn",
            PieceSilhouette.Stele => "res://parts/pieces/boundary.tscn",
            _ => null,
        };
        if (path is null || !ResourceLoader.Exists(path))
        {
            return null;
        }

        if (!Templates.TryGetValue(silhouette, out PackedScene? template))
        {
            template = ResourceLoader.Load<PackedScene>(path);
            if (template is null)
            {
                return null;
            }

            Templates.Add(silhouette, template);
        }

        Node instance = template.Instantiate();
        if (instance is not Node3D artBody)
        {
            instance.Free();
            return null;
        }

        // A fresh override on every mesh keeps staged-piece alpha and glow from changing other
        // instances. BoardView.Translucent intentionally edits each instance's material in place.
        int meshCount = 0;
        if (!Tint(artBody, faction, body, ink, emphasisPercent, ref meshCount) || meshCount == 0)
        {
            artBody.Free();
            return null;
        }

        Node3D piece = LowPoly.Piece(silhouette, emblem, faction, body, ink);
        // LowPoly always adds the plinth, then its emblem, then the silhouette parts. Retain
        // those first two children so the 2D HUD and 3D plinth still use Emblems.Polygons.
        while (piece.GetChildCount() > 2)
        {
            Node oldPart = piece.GetChild(2);
            piece.RemoveChild(oldPart);
            oldPart.Free();
        }

        artBody.Name = "ArtBody";
        piece.AddChild(artBody);
        return piece;
    }

    private static bool Tint(Node root, Color faction, Color body, Color ink, int emphasisPercent, ref int meshCount)
    {
        if (root is MeshInstance3D mesh)
        {
            string name = mesh.Name.ToString();
            Color color;
            float roughness = 0.9f;
            if (name.StartsWith("Stone_", System.StringComparison.Ordinal))
            {
                // The body color already carries the faction and emphasis treatment. A small
                // warm-stone blend gives it a matte material cue without bleaching distant pieces.
                color = body.Lerp(Visuals.Damp(Color.Color8(218, 205, 183), emphasisPercent), 0.12f);
            }
            else if (name.StartsWith("Shade_", System.StringComparison.Ordinal))
            {
                color = Visuals.Damp(Color.Color8(132, 121, 107), emphasisPercent);
            }
            else if (name.StartsWith("Metal_", System.StringComparison.Ordinal))
            {
                color = Visuals.Damp(Color.Color8(182, 156, 107), emphasisPercent);
                roughness = 0.68f;
            }
            else if (name.StartsWith("Faction_", System.StringComparison.Ordinal))
            {
                color = faction;
            }
            else if (name.StartsWith("Ink_", System.StringComparison.Ordinal))
            {
                color = ink;
            }
            else
            {
                return false;
            }

            mesh.MaterialOverride = Visuals.Matte(color, roughness);
            meshCount++;
        }

        foreach (Node child in root.GetChildren())
        {
            if (!Tint(child, faction, body, ink, emphasisPercent, ref meshCount))
            {
                return false;
            }
        }

        return true;
    }
}
