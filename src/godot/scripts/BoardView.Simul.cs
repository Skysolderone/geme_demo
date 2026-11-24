using System.Collections.Generic;
using Godot;
using Siege.Core.Board;

namespace Siege.Godot;

/// <summary>
/// 新玩法（simul-a）的盘面附加层（A3 裁决 D5）：本机可部署格的半透明铺色。数据只来自 Core（<c>SimulPrep.DeployableCells</c>，经 <see cref="SimulSession.DeployableCells"/>），
/// 本层不判任何合法性；只在本机准备中、未提交时由 GameRoot 给出非空集合，其余时候清空。经典模式从不调用，场景里没有这一层。
/// </summary>
public sealed partial class BoardView
{
    private MeshInstance3D? _deployable;
    private int _deployableKey;

    /// <summary>可部署格铺色的格数（截图与自检日志用）。</summary>
    public int DeployableShown { get; private set; }

    /// <summary>换上一组可部署格（空集合即清空）。集合没变不重建网格。</summary>
    public void SetDeployable(IReadOnlyCollection<Coord> cells)
    {
        int key = Key(cells);
        if (_deployable is not null && GodotObject.IsInstanceValid(_deployable) && key == _deployableKey && DeployableShown == cells.Count)
        {
            return;
        }

        if (_deployable is not null && GodotObject.IsInstanceValid(_deployable))
        {
            _deployable.QueueFree();
        }

        _deployable = null;
        _deployableKey = key;
        DeployableShown = cells.Count;
        if (cells.Count == 0)
        {
            return;
        }

        _deployable = new MeshInstance3D
        {
            Name = "SimulDeployable",
            Mesh = DeployableMesh(cells),
            MaterialOverride = DeployableInk,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_deployable);
    }

    private static readonly StandardMaterial3D DeployableInk = Visuals.Flat(Visuals.DeployableFill);

    private static int Key(IReadOnlyCollection<Coord> cells)
    {
        int key = cells.Count;
        foreach (Coord c in cells)
        {
            key = unchecked((key * 31) + (c.X * 1009) + c.Y);
        }

        return key;
    }

    /// <summary>每格一块略小于格的平面（边长 0.82 格），贴在格面之上一点；位置只经 <see cref="CenterOf"/> 推出。</summary>
    private ArrayMesh DeployableMesh(IReadOnlyCollection<Coord> cells)
    {
        const float lift = 0.012f;
        float half = BoardGeometry.CellSize * 0.41f;
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (Coord c in cells)
        {
            Vector3 center = CenterOf(c);
            float y = center.Y + lift;
            Vector3 a = new(center.X - half, y, center.Z - half);
            Vector3 b = new(center.X + half, y, center.Z - half);
            Vector3 d = new(center.X + half, y, center.Z + half);
            Vector3 e = new(center.X - half, y, center.Z + half);
            foreach (Vector3 v in new[] { a, b, d, a, d, e })
            {
                tool.SetNormal(Vector3.Up);
                tool.AddVertex(v);
            }
        }

        return tool.Commit();
    }
}
