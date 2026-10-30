using System.Collections.Generic;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

public sealed partial class BoardView
{
    /// <summary>
    /// 崖脚的贴地暗带和坡道的短阶线只作视觉提示：不建立碰撞体，也不参与高度或行走规则。
    /// 全部提示共用一个网格与材质，棋盘重搭时随父节点释放并按当前视图重建。
    /// </summary>
    private void AddTerrainRelief(DefaultBoardView board, Node3D parent)
    {
        var byCoord = board.Cells.ToDictionary(cell => cell.Coord);
        var fences = new HashSet<FenceEdge>(board.Fences);
        (int X, int Y)[] offsets = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        int quadCount = 0;

        foreach (BoardCellView cell in board.Cells)
        {
            // 深水与崖壁相邻时也需要崖脚阴影；桥面保持不变。
            if (cell.Surface == Surface.DeepWater)
            {
                if (cell.Height == 0 && !cell.HasBridge)
                {
                    Vector3 waterCenter = BoardGeometry.Center(cell.Coord, board.Width, board.Height, cell.Height)
                        + Vector3.Up * (-WaterDrop + WaterSurfaceLift + 0.001f);
                    const float halfCell = BoardGeometry.CellSize * 0.5f;
                    const float bandWidth = BoardGeometry.CellSize * 0.06f;
                    foreach ((int dx, int dy) in offsets)
                    {
                        if (!TryCell(cell.Coord, dx, dy, out BoardCellView higher) || higher.Height != 2)
                        {
                            continue;
                        }

                        Vector3 toward = Direction(cell.Coord, higher.Coord);
                        Vector3 across = Vector3.Up.Cross(toward);
                        Vector3 edge = waterCenter + toward * (halfCell - (bandWidth * 0.5f));
                        Vector3 along = across * (halfCell - bandWidth);
                        Vector3 depth = toward * (bandWidth * 0.5f);
                        Quad(edge - along - depth, edge + along - depth,
                            edge + along + depth, edge - along + depth,
                            new Color(0.26f, 0.20f, 0.32f, 0.34f));
                    }
                }

                continue;
            }

            // 不把阴影贴到障碍装饰上，桥面与低地的格心保持清晰。
            if (cell.Terrain != Terrain.Playable)
            {
                continue;
            }

            Vector3 center = BoardGeometry.Center(cell.Coord, board.Width, board.Height, cell.Height);
            AddSurfaceHint(center, cell.Surface);
            if (cell.Height == 0)
            {
                foreach ((int dx, int dy) in offsets)
                {
                    if (!TryCell(cell.Coord, dx, dy, out BoardCellView higher) || higher.Height != 2)
                    {
                        continue;
                    }

                    Vector3 toward = Direction(cell.Coord, higher.Coord);
                    Vector3 across = Vector3.Up.Cross(toward);
                    // 带位于低地格边内侧，留出格心与棋子底座；顶面略高于地砖，低于所有标记。
                    Vector3 edge = center + (toward * (BoardGeometry.TileSize * 0.5f)) + (Vector3.Up * 0.002f);
                    Vector3 inner = edge - (toward * (BoardGeometry.CellSize * 0.3f));
                    Vector3 along = across * (BoardGeometry.TileSize * 0.5f);
                    Quad(inner - along, inner + along, edge + along, edge - along,
                        new Color(0.24f, 0.18f, 0.30f, 0f), new Color(0.24f, 0.18f, 0.30f, 0.62f));
                }
            }
            else if (cell.Height == 1)
            {
                // 仅对邻接 h2 的过渡格画阶线，朝向相邻 h0 低地；不推断规则上的连通性。
                bool touchesHigh = offsets.Any(d => TryCell(cell.Coord, d.X, d.Y, out BoardCellView next)
                    && next.Height == 2 && next.Terrain == Terrain.Playable
                    && next.Surface != Surface.DeepWater
                    && !fences.Contains(new FenceEdge(cell.Coord, next.Coord)));
                if (!touchesHigh)
                {
                    continue;
                }

                foreach ((int dx, int dy) in offsets)
                {
                    if (!TryCell(cell.Coord, dx, dy, out BoardCellView lower) || lower.Height != 0
                        || lower.Terrain != Terrain.Playable || lower.Surface == Surface.DeepWater
                        || fences.Contains(new FenceEdge(cell.Coord, lower.Coord)))
                    {
                        continue;
                    }

                    Vector3 toward = Direction(cell.Coord, lower.Coord);
                    Stair(center, toward);
                }
            }
            else if (cell.Height == 2)
            {
                // 高台与 h1 通行格相接的一边同样画阶面，形成完整的两段坡道。
                foreach ((int dx, int dy) in offsets)
                {
                    if (TryCell(cell.Coord, dx, dy, out BoardCellView lower) && lower.Height == 1
                        && lower.Terrain == Terrain.Playable && lower.Surface != Surface.DeepWater
                        && !fences.Contains(new FenceEdge(cell.Coord, lower.Coord)))
                    {
                        Stair(center, Direction(cell.Coord, lower.Coord));
                    }
                }
            }
        }

        if (quadCount > 0)
        {
            StandardMaterial3D material = Visuals.Flat(new Color(1f, 1f, 1f, 0.99f));
            material.VertexColorUseAsAlbedo = true;
            parent.AddChild(new MeshInstance3D
            {
                Name = "TerrainRelief",
                Mesh = mesh.Commit(),
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        bool TryCell(Coord from, int dx, int dy, out BoardCellView result)
        {
            int x = from.X + dx, y = from.Y + dy;
            if (x >= 0 && x < board.Width && y >= 0 && y < board.Height
                && byCoord.TryGetValue(new Coord(x, y), out BoardCellView? found) && found is not null)
            {
                result = found;
                return true;
            }

            result = null!;
            return false;
        }

        Vector3 Direction(Coord from, Coord to)
        {
            Vector3 delta = BoardGeometry.Center(to, board.Width, board.Height) - BoardGeometry.Center(from, board.Width, board.Height);
            return delta.Normalized();
        }

        void AddSurfaceHint(Vector3 center, Surface surface)
        {
            // 边角纹与贴地纹都合入此网格；它们低于棋子和玩法标记，且不参与拾取。
            Vector3 top = center + new Vector3(0f, 0.003f, 0f);
            Color light = new(0.87f, 0.89f, 0.80f, 0.92f);
            Color dark = new(0.28f, 0.31f, 0.29f, 0.84f);
            const float edge = 0.405f, corner = 0.34f, width = 0.07f, length = 0.14f;
            switch (HintForSurface(surface))
            {
                case SurfaceHint.Road:
                    // 断续的双车辙。
                    foreach (float x in new[] { -edge, edge })
                        foreach (float z in new[] { -corner, corner })
                            Mark(top, x, z, width, length, dark);
                    // 两条较宽的贴地车辙让土路在灰度全图中与石面区分。
                    Mark(top, -0.22f, 0f, 0.08f, 0.52f, dark);
                    Mark(top, 0.22f, 0f, 0.08f, 0.52f, dark);
                    break;
                case SurfaceHint.Forest:
                    // 两组枝叶形：短竖纹与朝格角的横纹。
                    Mark(top, -edge, -corner, width, length, light);
                    Mark(top, -corner, -edge, length, width, light);
                    Mark(top, edge, corner, width, length, light);
                    Mark(top, corner, edge, length, width, light);
                    break;
                case SurfaceHint.Marsh:
                    // 分散水洼。
                    foreach (float x in new[] { -edge, edge })
                        foreach (float z in new[] { -corner, corner })
                            Mark(top, x, z, width, width, light);
                    break;
                case SurfaceHint.Desert:
                    // 两侧断开的沙纹，与沿行方向的土路车辙垂直。
                    foreach (float z in new[] { -edge, edge })
                        foreach (float x in new[] { -corner, corner })
                            Mark(top, x, z, length, width, dark);
                    break;
                case SurfaceHint.Crag:
                    // 对角断裂石纹。
                    Mark(top, -edge, corner, width, length, light);
                    Mark(top, -corner, edge, length, width, light);
                    Mark(top, edge, -corner, width, length, light);
                    Mark(top, corner, -edge, length, width, light);
                    break;
                case SurfaceHint.Shallows:
                    // 错位的两段水纹，区别于荒漠的四段沙纹。
                    Mark(top, -corner, -edge, length, width, dark);
                    Mark(top, corner, edge, length, width, dark);
                    // 两行错开的深色短波纹贴在浅水面，不遮挡棋子。
                    Vector3 wave = top + Vector3.Up * 0.001f;
                    Color waveInk = new(0.12f, 0.30f, 0.32f, 0.84f);
                    Mark(wave, -0.17f, -0.20f, 0.20f, 0.012f, waveInk);
                    Mark(wave, 0.18f, -0.20f, 0.18f, 0.012f, waveInk);
                    Mark(wave, -0.18f, 0.20f, 0.18f, 0.012f, waveInk);
                    Mark(wave, 0.17f, 0.20f, 0.20f, 0.012f, waveInk);
                    break;
            }
        }

        void Mark(Vector3 origin, float x, float z, float width, float length, Color color)
        {
            Vector3 p = origin + new Vector3(x, 0f, z);
            Vector3 a = new(width * 0.5f, 0f, length * 0.5f);
            Quad(p + new Vector3(-a.X, 0f, -a.Z), p + new Vector3(a.X, 0f, -a.Z),
                p + new Vector3(a.X, 0f, a.Z), p + new Vector3(-a.X, 0f, a.Z), color);
        }

        void Stair(Vector3 center, Vector3 toward)
        {
            // 踏面只进入低一层格的边缘 0.21 格，格心保持净空。
            Vector3 across = Vector3.Up.Cross(toward);
            Vector3 edge = center + (toward * (BoardGeometry.CellSize * 0.5f));
            float lowerY = center.Y - BoardGeometry.LayerHeight;
            float upperY = center.Y - BoardGeometry.TileHeight;
            const float treadDepth = 0.07f, treadHalfWidth = 0.26f;
            Vector3 left = across * treadHalfWidth;
            for (int step = 0; step < 3; step++)
            {
                float y = lowerY + ((upperY - lowerY) * (3 - step) / 4f);
                float nextY = lowerY + ((upperY - lowerY) * (2 - step) / 4f);
                Vector3 inner = edge + (toward * (step * treadDepth));
                Vector3 outer = edge + (toward * ((step + 1) * treadDepth));
                Quad(new Vector3(inner.X - left.X, y, inner.Z - left.Z), new Vector3(inner.X + left.X, y, inner.Z + left.Z),
                    new Vector3(outer.X + left.X, y, outer.Z + left.Z), new Vector3(outer.X - left.X, y, outer.Z - left.Z), new Color(0.82f, 0.76f, 0.70f, 0.96f));
                Quad(new Vector3(outer.X - left.X, y, outer.Z - left.Z), new Vector3(outer.X + left.X, y, outer.Z + left.Z),
                    new Vector3(outer.X + left.X, nextY, outer.Z + left.Z), new Vector3(outer.X - left.X, nextY, outer.Z - left.Z), new Color(0.58f, 0.50f, 0.52f, 0.96f));

            }
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Color? endColor = null)
        {
            Vector3[] points = [a, b, c, a, c, d];
            for (int i = 0; i < points.Length; i++)
            {
                mesh.SetNormal(Vector3.Up);
                mesh.SetColor(i is 2 or 4 or 5 ? endColor ?? color : color);
                mesh.AddVertex(points[i]);
            }

            quadCount++;
        }
    }
}
