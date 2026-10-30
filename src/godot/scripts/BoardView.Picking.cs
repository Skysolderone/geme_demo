using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using Siege.Core.Board;

namespace Siege.Godot;

public sealed partial class BoardView
{
    // 共享网格只提取一次三角面；弱引用不会把重搭时淘汰的回退网格永久留在内存中。
    private static readonly ConditionalWeakTable<Mesh, PickMesh> PickMeshCache = new();
    private readonly ConditionalWeakTable<Node3D, PickPiece> _pickPieces = new();
    private readonly Dictionary<Coord, List<MeshInstance3D>> _pickTerrain = [];
    private readonly Dictionary<Coord, List<(MultiMeshInstance3D Node, int Instance)>> _pickBatchedTerrain = [];
    private Node3D? _pickTerrainRoot;
    private float _pickTerrainHighest;
    private float? _pickTerrainScale;

    private sealed class PickMesh(Mesh mesh)
    {
        public Aabb Bounds { get; } = mesh.GetAabb();
        public Vector3[] Faces { get; } = mesh.GetFaces();
    }

    private sealed class PickPiece
    {
        public readonly List<MeshInstance3D> Meshes = [];
        public Aabb Bounds;
        private bool _hasBounds;

        public void Collect(Node node, Transform3D relative)
        {
            if (node is MeshInstance3D { Mesh: { } mesh } instance)
            {
                PickMesh data = PickMeshCache.GetValue(mesh, static key => new PickMesh(key));
                if (data.Faces.Length > 0)
                {
                    Meshes.Add(instance);
                    Vector3 min = data.Bounds.Position;
                    Vector3 max = min + data.Bounds.Size;
                    for (int mask = 0; mask < 8; mask++)
                    {
                        Vector3 corner = relative * new Vector3(
                            (mask & 1) == 0 ? min.X : max.X,
                            (mask & 2) == 0 ? min.Y : max.Y,
                            (mask & 4) == 0 ? min.Z : max.Z);
                        if (!_hasBounds)
                        {
                            Bounds = new Aabb(corner, Vector3.Zero);
                            _hasBounds = true;
                        }
                        else
                        {
                            Bounds = Bounds.Expand(corner);
                        }
                    }
                }
            }

            foreach (Node child in node.GetChildren())
            {
                Transform3D next = child is Node3D spatial ? relative * spatial.Transform : relative;
                Collect(child, next);
            }
        }

        public static PickPiece Create(Node3D root)
        {
            var result = new PickPiece();
            result.Collect(root, Transform3D.Identity);
            return result;
        }
    }

    /// <summary>
    /// 先取指针下可见棋子几何的最近交点，并检查地砖与崖壁是否在它前方；
    /// 没有可见棋子时沿用原有分层地面拾取。不会给棋子建立碰撞体或改变规则。
    /// </summary>
    public bool TryPickScreen(Vector2 screen, out Coord coord)
    {
        coord = default;
        if (!IsInstanceValid(Camera) || !IsInsideTree())
        {
            return false;
        }

        bool groundHit = BoardGeometry.TryPick(Camera, screen, _width, _height, LevelOf, out Coord ground);
        if (_boardPieces.Count == 0)
        {
            coord = ground;
            return groundHit;
        }

        Vector3 origin = Camera.ProjectRayOrigin(screen);
        Vector3 direction = Camera.ProjectRayNormal(screen);
        if (direction.LengthSquared() < 0.000001f)
        {
            coord = ground;
            return groundHit;
        }

        direction = direction.Normalized();
        float limit = Camera.Far;
        float nearest = limit;
        Coord pieceCoord = default;
        bool pieceHit = false;

        foreach ((Coord cell, PieceNode piece) in _boardPieces)
        {
            if (!IsInstanceValid(piece.Node) || !piece.Node.IsVisibleInTree() || LevelOf(cell) is null)
            {
                continue;
            }

            PickPiece data = _pickPieces.GetValue(piece.Node, static node => PickPiece.Create(node));
            if (data.Meshes.Count == 0 || MathF.Abs(piece.Node.GlobalTransform.Basis.Determinant()) < 0.000001f)
            {
                continue;
            }

            Transform3D inverseRoot = piece.Node.GlobalTransform.AffineInverse();
            if (!data.Bounds.IntersectsSegment(inverseRoot * origin, inverseRoot * (origin + direction * nearest)))
            {
                continue;
            }

            foreach (MeshInstance3D instance in data.Meshes)
            {
                if (!IsInstanceValid(instance) || !instance.IsVisibleInTree() || instance.Transparency >= 0.999f
                    || instance.Mesh is not { } mesh)
                {
                    continue;
                }

                if (HitMesh(instance, mesh, origin, direction, nearest, out float distance))
                {
                    nearest = distance;
                    pieceCoord = cell;
                    pieceHit = true;
                }
            }
        }

        if (pieceHit && !TerrainBlocks(origin, direction, nearest))
        {
            coord = pieceCoord;
            return true;
        }

        coord = ground;
        return groundHit;
    }

    private bool TerrainBlocks(Vector3 worldOrigin, Vector3 worldDirection, float pieceDistance)
    {
        EnsureTerrainPickIndex();
        if (_pickTerrain.Count == 0 && _pickBatchedTerrain.Count == 0)
        {
            return false;
        }

        Vector3 origin = ToLocal(worldOrigin);
        Vector3 direction = ToLocal(worldOrigin + worldDirection) - origin;
        float start = 0f;
        float highest = _pickTerrainHighest;
        if (direction.Y < -0.000001f && origin.Y > highest)
        {
            start = (highest - origin.Y) / direction.Y;
        }

        // 棋子位于最高地形之上时，射线在触及地形前已经命中棋子。
        if (start >= pieceDistance)
        {
            return false;
        }

        Vector3 first = origin + (direction * MathF.Max(0f, start));
        Vector3 last = origin + (direction * pieceDistance);
        Rect2I area = BoardGeometry.CellsAlongSegment(first, last, _width, _height);

        for (int y = area.Position.Y; y < area.End.Y; y++)
        {
            for (int x = area.Position.X; x < area.End.X; x++)
            {
                var cell = new Coord(x, y);
                if (_pickTerrain.TryGetValue(cell, out List<MeshInstance3D>? meshes))
                {
                    foreach (MeshInstance3D instance in meshes)
                    {
                        if (IsInstanceValid(instance) && instance.IsVisibleInTree() && instance.Transparency < 0.999f
                            && instance.Mesh is { } mesh
                            && HitMesh(instance, mesh, worldOrigin, worldDirection, pieceDistance - 0.0001f, out _))
                        {
                            return true;
                        }
                    }
                }

                if (_pickBatchedTerrain.TryGetValue(cell, out List<(MultiMeshInstance3D Node, int Instance)>? batches))
                {
                    foreach ((MultiMeshInstance3D node, int index) in batches)
                    {
                        if (!IsInstanceValid(node) || !node.IsVisibleInTree() || node.Transparency >= 0.999f
                            || node.Multimesh is not { Mesh: { } mesh } multi
                            || index >= multi.InstanceCount || (multi.VisibleInstanceCount >= 0 && index >= multi.VisibleInstanceCount))
                        {
                            continue;
                        }

                        Transform3D transform = node.GlobalTransform * multi.GetInstanceTransform(index);
                        if (HitMesh(transform, mesh, worldOrigin, worldDirection, pieceDistance - 0.0001f, out _))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private void EnsureTerrainPickIndex()
    {
        if (ReferenceEquals(_pickTerrainRoot, _tiles) && _pickTerrainScale == _batchScale)
        {
            return;
        }

        _pickTerrainRoot = _tiles;
        _pickTerrainScale = _batchScale;
        _pickTerrain.Clear();
        _pickBatchedTerrain.Clear();
        _pickTerrainHighest = float.NegativeInfinity;
        CollectOpaqueMeshes(_tiles);
        CollectOpaqueMeshes(_decoration);
        if (GetNodeOrNull<Node3D>("BoardPlates") is { } plates)
        {
            CollectOpaqueMeshes(plates);
        }

        foreach (Node child in _batch.GetChildren())
        {
            if (child is not MultiMeshInstance3D { Multimesh: { Mesh: { } mesh } multi } node
                || !IsOpaqueMultiMesh(node, mesh))
            {
                continue;
            }

            // 使用实际实例而非隐藏模板；缩放变化时 _batchScale 促使索引重建。
            for (int i = 0; i < multi.InstanceCount; i++)
            {
                int index = i;
                Transform3D toBoard = GlobalTransform.AffineInverse() * node.GlobalTransform * multi.GetInstanceTransform(index);
                AddToCoveredCells(mesh, toBoard, cell =>
                {
                    if (!_pickBatchedTerrain.TryGetValue(cell, out List<(MultiMeshInstance3D Node, int Instance)>? entries))
                    {
                        entries = [];
                        _pickBatchedTerrain.Add(cell, entries);
                    }

                    entries.Add((node, index));
                });
            }
        }

        // 栅栏、桥和台框可能跨过格边；以变换后的完整包围盒登记到所有覆盖格，
        // 而不是只登记部件原点所在的格。重建棋盘时根节点变化会重建索引。
        void CollectOpaqueMeshes(Node node)
        {
            if (node is MeshInstance3D { Mesh: { } mesh } instance && IsOpaque(instance, mesh))
            {
                Transform3D toBoard = GlobalTransform.AffineInverse() * instance.GlobalTransform;
                AddToCoveredCells(mesh, toBoard, cell =>
                {
                    if (!_pickTerrain.TryGetValue(cell, out List<MeshInstance3D>? entries))
                    {
                        entries = [];
                        _pickTerrain.Add(cell, entries);
                    }

                    entries.Add(instance);
                });
            }

            foreach (Node child in node.GetChildren())
            {
                CollectOpaqueMeshes(child);
            }
        }

        void AddToCoveredCells(Mesh mesh, Transform3D toBoard, Action<Coord> add)
        {
            Aabb bounds = PickMeshCache.GetValue(mesh, static key => new PickMesh(key)).Bounds;
            Vector3 min = bounds.Position;
            Vector3 max = min + bounds.Size;
            Aabb boardBounds = new(toBoard * min, Vector3.Zero);
            for (int mask = 1; mask < 8; mask++)
            {
                boardBounds = boardBounds.Expand(toBoard * new Vector3(
                    (mask & 1) == 0 ? min.X : max.X,
                    (mask & 2) == 0 ? min.Y : max.Y,
                    (mask & 4) == 0 ? min.Z : max.Z));
            }

            _pickTerrainHighest = MathF.Max(_pickTerrainHighest, boardBounds.End.Y);
            Rect2I area = BoardGeometry.CellsOverlapping(boardBounds, _width, _height);
            for (int y = area.Position.Y; y < area.End.Y; y++)
            {
                for (int x = area.Position.X; x < area.End.X; x++)
                {
                    add(new Coord(x, y));
                }
            }
        }

        static bool IsOpaque(MeshInstance3D instance, Mesh mesh)
        {
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                Material? material = instance.GetActiveMaterial(surface);
                if (material is ShaderMaterial || material is BaseMaterial3D baseMaterial
                    && (baseMaterial.Transparency != BaseMaterial3D.TransparencyEnum.Disabled || baseMaterial.AlbedoColor.A < 0.999f))
                {
                    return false;
                }
            }

            return true;
        }

        static bool IsOpaqueMultiMesh(MultiMeshInstance3D instance, Mesh mesh)
        {
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                Material? material = instance.MaterialOverride ?? mesh.SurfaceGetMaterial(surface);
                if (material is ShaderMaterial || material is BaseMaterial3D baseMaterial
                    && (baseMaterial.Transparency != BaseMaterial3D.TransparencyEnum.Disabled || baseMaterial.AlbedoColor.A < 0.999f))
                {
                    return false;
                }
            }

            return true;
        }
    }

    // 将射线变换到网格局部空间，保留射线参数 t；先过包围盒再测实际三角面。
    private static bool HitMesh(MeshInstance3D instance, Mesh mesh, Vector3 origin, Vector3 direction,
        float maximumDistance, out float hitDistance)
        => HitMesh(instance.GlobalTransform, mesh, origin, direction, maximumDistance, out hitDistance);

    private static bool HitMesh(Transform3D transform, Mesh mesh, Vector3 origin, Vector3 direction,
        float maximumDistance, out float hitDistance)
    {
        hitDistance = maximumDistance;
        if (maximumDistance <= 0f || MathF.Abs(transform.Basis.Determinant()) < 0.000001f)
        {
            return false;
        }

        PickMesh data = PickMeshCache.GetValue(mesh, static key => new PickMesh(key));
        if (data.Faces.Length < 3)
        {
            return false;
        }

        Transform3D inverse = transform.AffineInverse();
        Vector3 localOrigin = inverse * origin;
        Vector3 localDirection = inverse.Basis * direction;
        if (!data.Bounds.IntersectsSegment(localOrigin, localOrigin + (localDirection * maximumDistance)))
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i + 2 < data.Faces.Length; i += 3)
        {
            Vector3 a = data.Faces[i], edge1 = data.Faces[i + 1] - a, edge2 = data.Faces[i + 2] - a;
            Vector3 p = localDirection.Cross(edge2);
            float determinant = edge1.Dot(p);
            if (MathF.Abs(determinant) < 0.000001f)
            {
                continue;
            }

            float inverseDeterminant = 1f / determinant;
            Vector3 delta = localOrigin - a;
            float u = delta.Dot(p) * inverseDeterminant;
            if (u < 0f || u > 1f)
            {
                continue;
            }

            Vector3 q = delta.Cross(edge1);
            float v = localDirection.Dot(q) * inverseDeterminant;
            if (v < 0f || u + v > 1f)
            {
                continue;
            }

            float distance = edge2.Dot(q) * inverseDeterminant;
            if (distance > 0.0001f && distance < hitDistance)
            {
                hitDistance = distance;
                found = true;
            }
        }

        return found;
    }
}
