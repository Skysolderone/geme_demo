using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Presentation.Camera;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Style;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 一个小回合的落子 / 提子 / 改造演出数据。
/// </summary>
/// <param name="Edits">
/// 本次刚落成的改造（visual-style-baseline「改造的可视表现」：改造完成的那一刻 SHALL 有一次可察觉的反馈，使对手知道地形变了）。
/// 只是<b>瞬时</b>反馈的定位信息；设施本身一律照 <see cref="DefaultBoardView.Fences"/> / <see cref="BoardCellView.HasBridge"/> /
/// <see cref="BoardCellView.Surface"/> 画，新旧同形。
/// </param>
public sealed record TurnFlash(ImmutableArray<Coord> Placed, ImmutableArray<Coord> Captured, ImmutableArray<TerrainEdit> Edits)
{
    /// <summary>无演出。</summary>
    public static readonly TurnFlash None = new([], [], []);

    /// <summary>是否为空。</summary>
    public bool IsEmpty => Placed.IsEmpty && Captured.IsEmpty && Edits.IsEmpty;
}

/// <summary>
/// 3D 棋盘表现（tactical-ui 裁决 6）：分层地砖 + 固定倾斜俯视镜头 + 程序生成的低多边形棋子与叠加标记。
/// </summary>
/// <remarks>
/// <para>本类<b>不做任何规则计算、不读地图</b>：画什么全部来自 <see cref="ViewerWorld"/>（默认棋盘视图模型含每格高度 / 地表 / 桥与栅栏边、预演呈现、信息层内容）。
/// 绘制顺序遵守 <see cref="RenderLayer"/>：装饰（岩石、小树）在最下，网格 / 归属 / 标记 / 棋子 / 预览依次在上。</para>
/// <para>地形（terrain-model D-H）：高度 → 地砖按层堆叠，h=1 层侧面土色、h=2 层侧面岩灰，Δh=2 的崖壁露出两条色带、Δh=1 的缓坡一条；
/// 深水 → 低于地砖的蓝色水面；桥 → 与同层地砖齐平的木板面；林地 → 深绿地表 + 角落小树；栅栏 → 沿两格公共边立起的木栅。
/// 格心高度统一由 <see cref="BoardGeometry.Center(Coord, int, int, int)"/> 给出，本类只把视图模型的高度传进去。</para>
/// </remarks>
public sealed partial class BoardView : Node3D
{
    private readonly Dictionary<Coord, StandardMaterial3D> _tileMaterials = [];
    private readonly Dictionary<Coord, Color> _tileBase = [];
    private readonly Dictionary<Coord, int> _levels = [];
    private Node3D _decoration = null!;
    private Node3D _overlay = null!;
    private Node3D _pieces = null!;
    private Node3D _preview = null!;
    private MeshInstance3D _cursor = null!;
    private WorldEnvironment _environment = null!;
    private IReadOnlyDictionary<int, PlayerId> _zoneOwners = new Dictionary<int, PlayerId>();
    private string _terrainKey = string.Empty;
    private int _width;
    private int _height;
    private CameraPose? _appliedPose;

    /// <summary>倾斜俯视镜头节点（俯角恒为 60 度）。位姿只由 <see cref="ApplyCameraPose"/> 写入，取自 <see cref="Rig"/>。</summary>
    public Camera3D Camera { get; private set; } = null!;

    /// <summary>
    /// 相机视图模型（viewport-camera D6）：注视点 / 距离 / 夹取的纯计算都在 Presentation。
    /// 跨 <see cref="Build"/> 保留——插旗锁定与地形改造都会重搭场景，玩家推到哪、缩到多大不能因此复位。
    /// </summary>
    public BoardCamera Rig { get; private set; } = null!;

    /// <summary>某可落子格的层数（视图模型的高度）；不可落子格（岩石、未架桥深水）或盘外为 <c>null</c>。供分层拾取使用。</summary>
    public int? LevelOf(Coord coord) => _levels.TryGetValue(coord, out int level) ? level : null;

    /// <summary>某格格心（含高度）的世界坐标：盘内一切叠加物都从这里取位置。</summary>
    public Vector3 CenterOf(Coord coord) => BoardGeometry.Center(coord, _width, _height, LevelOf(coord) ?? 0);

    /// <summary>一次性搭出地形、装饰、灯光与镜头。只消费默认棋盘视图模型。</summary>
    public void Build(DefaultBoardView board, IReadOnlyDictionary<int, PlayerId> zoneOwners)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(zoneOwners);

        // 幂等：插旗锁定后要按出生区归属重染地砖，会再搭一次；地形被改造后 Refresh 也会再搭一次（见 TerrainKeyOf）。
        Clear(this);
        _zoneOwners = zoneOwners;
        _terrainKey = TerrainKeyOf(board);
        _tileMaterials.Clear();
        _tileBase.Clear();
        _levels.Clear();
        _width = board.Width;
        _height = board.Height;

        // 底座：盖住棋盘外圈的标注平面（标注在 h=0 平面上、离边缘格最远 FarLabelMargin），再向外留 0.45 格的边。
        // 顶面压到地砖上表面之下 WaterDrop：深水面与底座齐平、地砖高出一截，水才读得出是"沟"；地砖缝里露出的深色底座就是网格线。
        float apron = 2f * (BoardGeometry.FarLabelMargin + 0.45f);
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3((_width + apron) * BoardGeometry.CellSize, BoardGeometry.TileHeight, (_height + apron) * BoardGeometry.CellSize) },
            MaterialOverride = Visuals.Matte(Visuals.IslandRim),
            Position = new Vector3(0f, BoardGeometry.TopY - WaterDrop - (BoardGeometry.TileHeight * 0.5f), 0f),
        });

        _decoration = new Node3D { Name = "Decoration" };
        AddChild(_decoration);

        var tiles = new Node3D { Name = "Tiles" };
        AddChild(tiles);

        // 出生区的外圈格（四邻里有不属于同一区的格）：归属色只重染外圈，内部只淡淡带一点，让草地本色露出来。只是渲染，不进规则。
        Dictionary<Coord, int> zoneOf = board.Cells.Where(c => c.BirthZone is not null).ToDictionary(c => c.Coord, c => c.BirthZone!.Value);
        Dictionary<int, (int MinX, int MinY, int MaxX, int MaxY)> zoneBox = zoneOf
            .GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => (g.Min(kv => kv.Key.X), g.Min(kv => kv.Key.Y), g.Max(kv => kv.Key.X), g.Max(kv => kv.Key.Y)));
        HashSet<Coord> blocked = [.. board.Cells.Where(c => c.Terrain != Terrain.Playable).Select(c => c.Coord)];

        // 某格朝 (dx, dy) 方向是不是出生区的外缘：邻格不属于同一区即是；区外接矩形之内的不可落子格（平台里的岩石洞）不算外缘，否则洞的四周也会描一圈。
        bool IsZoneEdge(Coord c, int zone, int dx, int dy)
        {
            int x = c.X + dx, y = c.Y + dy;
            if (x < 0 || y < 0 || x >= _width || y >= _height)
            {
                return true;
            }

            var next = new Coord(x, y);
            if (zoneOf.TryGetValue(next, out int other))
            {
                return other != zone;
            }

            (int minX0, int minY0, int maxX0, int maxY0) = zoneBox[zone];
            bool hole = blocked.Contains(next) && x >= minX0 && x <= maxX0 && y >= minY0 && y <= maxY0;
            return !hole;
        }

        int variant = 0;
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (BoardCellView cell in board.Cells)
        {
            // 地图外接矩形由格心推出（格心只有 BoardGeometry.Center 一份映射），供相机夹取用。
            Vector3 flat = BoardGeometry.Center(cell.Coord, _width, _height);
            minX = Math.Min(minX, flat.X);
            maxX = Math.Max(maxX, flat.X);
            minZ = Math.Min(minZ, flat.Z);
            maxZ = Math.Max(maxZ, flat.Z);

            bool playable = cell.Terrain == Terrain.Playable;
            if (playable)
            {
                _levels[cell.Coord] = cell.Height;
            }

            if (cell.Surface == Surface.DeepWater)
            {
                // 深水：水面低于同层地砖，不可落子；架桥后桥面与地砖齐平、可落子。
                Vector3 waterCenter = BoardGeometry.Center(cell.Coord, _width, _height, cell.Height);
                Color water = Visuals.DeepWater;
                _tileMaterials[cell.Coord] = AddWater(tiles, waterCenter, water);
                _tileBase[cell.Coord] = water;
                if (cell.HasBridge)
                {
                    Node3D bridge = LowPoly.Bridge();
                    bridge.Position = waterCenter;
                    tiles.AddChild(bridge);
                }

                continue;
            }

            Vector3 center = BoardGeometry.Center(cell.Coord, _width, _height, cell.Height);
            Color color = cell.Surface switch
            {
                Surface.Road => Visuals.TileRoad,
                Surface.Forest => Visuals.TileForest,
                _ => Visuals.TilePlayable,
            };
            if (!playable)
            {
                // 障碍格的造型按坐标散列挑（同一张图永远同一副样子，不用随机数）：巨石 / 松树丛 / 断柱遗迹。都只是"此格不可落子"的装饰。
                color = Visuals.TileObstacle;
                int pick = unchecked((int)(((uint)(cell.Coord.X * 73856093) ^ (uint)(cell.Coord.Y * 19349663)) % 100u));
                Node3D obstacle = pick < 40 ? LowPoly.Rock(variant++) : pick < 82 ? LowPoly.Pines(variant++) : LowPoly.Ruins(variant++);
                obstacle.Position = center;
                _decoration.AddChild(obstacle);
            }
            else if (cell.BirthZone is int zone)
            {
                // 插旗阶段还没有归属，先用统一的出生区高亮让玩家看得见可点的区域；锁定后有主的平台改染该阵营主色，
                // 没人选的平台（平台数 > 人数的地图，frontier-map D9）褪成中性色——区号独立于玩家色，几个平台都一样处理。
                // 归属靠外缘描边读出来，地砖只淡淡带一点色，草地本色留着（整片染色会把红 / 金混成土褐、土黄）。
                bool owned = zoneOwners.TryGetValue(zone, out PlayerId owner);
                Color zoneColor = owned ? Visuals.FactionColorOf(owner) : zoneOwners.Count > 0 ? Visuals.Neutral : Visuals.BirthHint;
                color = color.Lerp(zoneColor, owned ? 0.10f : zoneOwners.Count > 0 ? 0.08f : 0.30f);

                StandardMaterial3D edge = Visuals.Flat(zoneColor);
                foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    if (!IsZoneEdge(cell.Coord, zone, dx, dy))
                    {
                        continue;
                    }

                    // Coord 的 y 向上（北）对应世界 −Z。描边条贴在格内侧边缘、略高于面砖，不占落点。
                    const float strip = 0.07f;
                    float offset = (BoardGeometry.CellSize * 0.5f) - (strip * 0.5f);
                    tiles.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = dx != 0 ? new Vector3(strip, 0.03f, BoardGeometry.CellSize) : new Vector3(BoardGeometry.CellSize, 0.03f, strip) },
                        MaterialOverride = edge,
                        Position = center + new Vector3(dx * offset, 0.015f, -dy * offset),
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
                }
            }

            // 棋盘格式的轻微明暗交替（参考图的草地拼块感），只动亮度、不动色相；障碍格不参与。
            if (playable && ((cell.Coord.X + cell.Coord.Y) & 1) == 0)
            {
                color = color.Darkened(0.05f);
            }

            if (playable && cell.Surface == Surface.Forest)
            {
                Node3D trees = LowPoly.Trees(variant++);
                trees.Position = center;
                _decoration.AddChild(trees);
            }

            StandardMaterial3D material = Visuals.Matte(color);
            _tileMaterials[cell.Coord] = material;
            _tileBase[cell.Coord] = color;
            AddTileStack(tiles, center, cell.Height, material);
        }

        foreach (FenceEdge fence in board.Fences)
        {
            AddFence(tiles, fence);
        }

        AddFloatingIsland(board, apron);

        BuildCoordinateLabels();

        _overlay = new Node3D { Name = "Overlay" };
        AddChild(_overlay);
        _pieces = new Node3D { Name = "Pieces" };
        AddChild(_pieces);
        _preview = new Node3D { Name = "Preview" };
        AddChild(_preview);

        _cursor = new MeshInstance3D
        {
            Mesh = LowPoly.Marker(BoardGeometry.TileSize),
            MaterialOverride = Visuals.Flat(new Color(Visuals.Cursor, 0.22f)),
            Visible = false,
        };
        AddChild(_cursor);

        AddChild(new DirectionalLight3D
        {
            // 暖色主光 + 柔和阴影：高台、树、棋子落下影子，层次才读得出来（基准图的"温暖自然光"）。
            RotationDegrees = new Vector3(-52f, -38f, 0f),
            LightColor = Color.Color8(255, 240, 208),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
            ShadowBlur = 1.6f,
            ShadowOpacity = 0.55f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = 70f,
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-20f, 140f, 0f),
            LightColor = Color.Color8(168, 190, 226),
            LightEnergy = 0.35f,
        });

        _environment = new WorldEnvironment
        {
            Environment = new global::Godot.Environment
            {
                // 明亮的天空渐变：沙盘悬浮在天上（基准图），不再是深色虚空。地平线以下同样取浅色，俯视时画面边缘是云海的颜色。
                BackgroundMode = global::Godot.Environment.BGMode.Sky,
                Sky = new Sky
                {
                    SkyMaterial = new ProceduralSkyMaterial
                    {
                        SkyTopColor = Color.Color8(86, 148, 222),
                        SkyHorizonColor = Color.Color8(206, 228, 246),
                        GroundHorizonColor = Color.Color8(206, 228, 246),
                        GroundBottomColor = Color.Color8(148, 190, 232),
                        SunAngleMax = 0f,
                    },
                },
                AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Color.Color8(186, 200, 222),
                AmbientLightEnergy = 0.62f,
                TonemapMode = global::Godot.Environment.ToneMapper.Filmic,
                TonemapWhite = 1.4f,
                AdjustmentEnabled = true,
                AdjustmentSaturation = 1f,
            },
        };
        AddChild(_environment);

        // 俯视相机：俯角恒为 60°。h=2 高台（0.70 高）在这个角度下向远处只投 0.70 / tan 60° ≈ 0.40 格的遮挡，
        // 小于半格——紧贴崖壁身后的 h=0 格格心仍露出来，能被点到（--pick-check 多位姿钉住）；45° 时会被挡住。
        // 崖壁侧面在 60° 下仍有 cos 60° = 0.5 的投影高度，看得见。位姿（注视点、距离）全部来自视图模型：
        // 一屏看全的地图（v4）在最远缩放下两方向锁中线，与引入推屏之前的固定相机逐位相同。
        float outer = (0.5f * BoardGeometry.CellSize) + BoardGeometry.FarLabelMargin;
        var bounds = new PlaneRect(minX - outer, minZ - outer, maxX + outer, maxZ + outer);
        if (Rig is null || Rig.Bounds != bounds)
        {
            Rig = new BoardCamera(bounds);
        }

        Camera = new Camera3D { Fov = CameraPose.FovDegrees };
        AddChild(Camera);
        ApplyCameraPose();
    }

    /// <summary>
    /// 浮空岛（纯装饰，visual-style-baseline「悬浮于奇幻世界中的立体战争沙盘」）：底座之下逐层收窄的岩体与垂下的石笋、
    /// 岛下的云海、以及地图边缘深水格外侧垂落的瀑布。全部在底座平面之下或地图外接矩形之外，不进拾取、不遮挡任何格。
    /// 形状只由地图尺寸与格坐标决定，不用随机数——同一张图永远同一副样子。
    /// </summary>
    private void AddFloatingIsland(DefaultBoardView board, float apron)
    {
        var island = new Node3D { Name = "Island" };
        AddChild(island);

        float baseTop = BoardGeometry.TopY - WaterDrop - BoardGeometry.TileHeight;
        float spanX = (_width + apron) * BoardGeometry.CellSize;
        float spanZ = (_height + apron) * BoardGeometry.CellSize;
        StandardMaterial3D earth = Visuals.Matte(Visuals.SlopeSide.Darkened(0.12f), 1f);
        StandardMaterial3D rock = Visuals.Matte(Visuals.CliffSide.Darkened(0.10f), 1f);
        StandardMaterial3D deepRock = Visuals.Matte(Visuals.CliffSide.Darkened(0.30f), 1f);

        // 逐层收窄的岩体：一层土、两层岩，越往下越窄越暗。
        (float Shrink, float Thickness, StandardMaterial3D Material)[] layers =
        [
            (0.985f, 0.9f, earth),
            (0.90f, 1.6f, rock),
            (0.72f, 2.2f, rock),
            (0.48f, 2.6f, deepRock),
            (0.22f, 2.4f, deepRock),
        ];
        float y = baseTop;
        foreach ((float shrink, float thickness, StandardMaterial3D material) in layers)
        {
            island.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(spanX * shrink, thickness, spanZ * shrink) },
                MaterialOverride = material,
                Position = new Vector3(0f, y - (thickness * 0.5f), 0f),
            });
            y -= thickness;
        }

        // 沿底座四边垂下的石笋：位置与长短按序号散列。
        int count = Math.Max(10, (_width + _height) / 2);
        for (int i = 0; i < count; i++)
        {
            int h = unchecked((i * 40503) ^ (i * i * 9973));
            float t = ((h & 0x3FF) / 1023f) - 0.5f;
            float length = 1.2f + (((h >> 10) & 0xFF) / 255f * 2.4f);
            bool alongX = (i & 1) == 0;
            float side = ((i >> 1) & 1) == 0 ? -1f : 1f;
            var at = alongX
                ? new Vector3(t * spanX * 0.92f, baseTop - 0.9f - (length * 0.5f), side * spanZ * 0.46f)
                : new Vector3(side * spanX * 0.46f, baseTop - 0.9f - (length * 0.5f), t * spanZ * 0.92f);
            island.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.55f + (length * 0.12f), BottomRadius = 0.02f, Height = length, RadialSegments = 5, Rings = 0 },
                MaterialOverride = (i % 3) == 0 ? deepRock : rock,
                Position = at,
                RotationDegrees = new Vector3(0f, i * 37f, 0f),
            });
        }

        // 云海：岛下与四周的几团扁平白云（不投影、不受光）。
        StandardMaterial3D cloud = Visuals.Flat(new Color(0.94f, 0.97f, 1f, 0.55f));
        float reach = Math.Max(spanX, spanZ);
        for (int i = 0; i < 26; i++)
        {
            float angle = i * 2.399963f;
            float radius = reach * (0.50f + (0.42f * ((i * 7) % 5) / 4f));
            float size = reach * (0.07f + (0.03f * (i % 3)));
            island.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = size, Height = size * 0.55f, RadialSegments = 8, Rings = 3 },
                MaterialOverride = cloud,
                Position = new Vector3(MathF.Cos(angle) * radius, baseTop - 5.5f - (i % 4 * 1.1f), MathF.Sin(angle) * radius * 0.9f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        // 瀑布：贴着地图边缘的深水格，沿外侧垂下一道水帘，落到云海里。
        const float drop = 9f;
        foreach (BoardCellView cell in board.Cells)
        {
            if (cell.Surface != Surface.DeepWater || cell.HasBridge)
            {
                continue;
            }

            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = cell.Coord.X + dx, ny = cell.Coord.Y + dy;
                if (nx >= 0 && ny >= 0 && nx < _width && ny < _height)
                {
                    continue;
                }

                // 河道出口才挂瀑布：这一格朝里（反方向）的邻格也得是水，否则只是贴边的一格水塘。
                int ix = cell.Coord.X - dx, iy = cell.Coord.Y - dy;
                BoardCellView? inner = board.Cells.FirstOrDefault(c => c.Coord.X == ix && c.Coord.Y == iy);
                if (inner is null || inner.Surface != Surface.DeepWater)
                {
                    continue;
                }

                Vector3 water = BoardGeometry.Center(cell.Coord, _width, _height, cell.Height);
                float outward = (apron * 0.5f * BoardGeometry.CellSize) + (BoardGeometry.CellSize * 0.5f);
                var lip = new Vector3(water.X + (dx * outward), 0f, water.Z - (dy * outward));
                float top = water.Y - WaterDrop;

                // 从水格到底座边缘的一段水道（盖在底座上），再接垂直水帘。
                float channel = outward - (BoardGeometry.CellSize * 0.5f);
                island.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = dx != 0 ? new Vector3(channel, 0.02f, BoardGeometry.TileSize) : new Vector3(BoardGeometry.TileSize, 0.02f, channel) },
                    MaterialOverride = Visuals.Matte(Visuals.DeepWater, 0.55f),
                    Position = new Vector3(water.X + (dx * (outward + (BoardGeometry.CellSize * 0.5f)) * 0.5f), baseTop + 0.012f, water.Z - (dy * (outward + (BoardGeometry.CellSize * 0.5f)) * 0.5f)),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
                island.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = dx != 0 ? new Vector3(0.10f, drop, BoardGeometry.TileSize) : new Vector3(BoardGeometry.TileSize, drop, 0.10f) },
                    // 向下滚动的亮暗条纹由着色器画（Visuals.Waterfall），落到云海高度渐隐。
                    MaterialOverride = Visuals.Waterfall,
                    Position = new Vector3(lip.X + (dx * 0.05f), top - (drop * 0.5f), lip.Z - (dy * 0.05f)),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        }
    }

    /// <summary>把视图模型的位姿写到相机节点。<b>全仓唯一</b>写相机位置 / 朝向的地方；返回位姿是否变了。</summary>
    public bool ApplyCameraPose()
    {
        CameraPose pose = Rig.Pose;
        bool changed = pose != _appliedPose;
        _appliedPose = pose;
        Camera.Position = new Vector3(pose.Eye.X, pose.Eye.Y, pose.Eye.Z);
        Camera.LookAt(new Vector3(pose.Target.X, pose.Target.Y, pose.Target.Z), Vector3.Up);
        return changed;
    }

    /// <summary>某格格心在棋盘平面上的 (x, z)：相机回家目标用。仍是 <see cref="BoardGeometry.Center(Coord, int, int)"/> 那一份映射。</summary>
    public (float X, float Z) PlaneCenterOf(Coord coord)
    {
        Vector3 center = BoardGeometry.Center(coord, _width, _height);
        return (center.X, center.Z);
    }

    /// <summary>
    /// 一格地砖：h=0 只有一块面砖；h=1 在面砖下垫一层土色侧面；h=2 再垫一层岩灰侧面。
    /// 相邻格高度差越大露出的色带越多（Δh=1 一条土色，Δh=2 土色 + 岩灰），崖壁与缓坡因此可分。
    /// </summary>
    private static readonly StandardMaterial3D SlopeMaterial = Visuals.Matte(Visuals.SlopeSide);
    private static readonly StandardMaterial3D CliffMaterial = Visuals.Matte(Visuals.CliffSide);

    private static void AddTileStack(Node3D parent, Vector3 top, int level, StandardMaterial3D surface)
    {
        const float half = BoardGeometry.TileHeight * 0.5f;
        float bandBottom = -half;
        for (int layer = 1; layer <= level; layer++)
        {
            float bandTop = BoardGeometry.TopYOf(layer) - BoardGeometry.TileHeight;
            parent.AddChild(new MeshInstance3D
            {
                // 侧面色带铺满整格（CellSize）：高台读作一整块实心的土 / 岩，而不是一根根立柱之间透着黑缝。
                Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.CellSize, bandTop - bandBottom, BoardGeometry.CellSize) },
                MaterialOverride = layer == 1 ? SlopeMaterial : CliffMaterial,
                Position = new Vector3(top.X, (bandTop + bandBottom) * 0.5f, top.Z),
            });
            bandBottom = bandTop;
        }

        // 面砖之下垫一块铺满整格的薄衬底，颜色取面砖压暗：面砖之间那 0.10 的缝露出的就是它——
        // 网格线仍然清楚（visual-style-baseline「方格边界始终清晰」），但是同色系的细线，不再是黑缝。
        const float liner = 0.03f;
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.CellSize, liner, BoardGeometry.CellSize) },
            MaterialOverride = Visuals.Matte(surface.AlbedoColor.Darkened(0.30f)),
            Position = top - new Vector3(0f, BoardGeometry.TileHeight - (liner * 0.5f), 0f),
        });

        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.TileSize, BoardGeometry.TileHeight, BoardGeometry.TileSize) },
            MaterialOverride = surface,
            Position = top - new Vector3(0f, half, 0f),
        });
    }

    /// <summary>深水面低于同层地砖上表面的距离：够让相邻地砖露出一段侧面，读得出"沟"。</summary>
    private const float WaterDrop = 0.10f;

    /// <summary>深水格：比同层地砖低 <see cref="WaterDrop"/> 的蓝色水面 + 两道浅色波纹（装饰，贴在水面上）。返回水面材质以便信息层降饱和。</summary>
    private static readonly PlaneMesh FlowPlane = new() { Size = new Vector2(BoardGeometry.CellSize, BoardGeometry.CellSize), Orientation = PlaneMesh.OrientationEnum.Y };

    private static StandardMaterial3D AddWater(Node3D parent, Vector3 top, Color color)
    {
        StandardMaterial3D material = Visuals.Matte(color, 0.55f);
        const float drop = WaterDrop;
        parent.AddChild(new MeshInstance3D
        {
            // 水体铺满整格：相邻水格连成一片，河才读作"一条在流的河"而不是一格格水池。
            Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.CellSize, BoardGeometry.TileHeight, BoardGeometry.CellSize) },
            MaterialOverride = material,
            // 水面比底座顶面高出一丝：两者原本同高，会 z-fight，大片水格闪成底座的颜色。
            Position = top - new Vector3(0f, drop - 0.008f + (BoardGeometry.TileHeight * 0.5f), 0f),
        });

        // 流动层：全图共用一份按世界坐标画波纹的材质，相邻格的纹路自然接上（Visuals.WaterFlow）。
        parent.AddChild(new MeshInstance3D
        {
            Mesh = FlowPlane,
            MaterialOverride = Visuals.WaterFlow,
            Position = top + new Vector3(0f, -drop + 0.012f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        return material;
    }

    /// <summary>栅栏沿两格的公共边立起：放在两格格心的中点（即缝上）、取两格中较高一层的地砖面。</summary>
    private void AddFence(Node3D parent, FenceEdge fence)
    {
        int level = Math.Max(LevelOf(fence.A) ?? 0, LevelOf(fence.B) ?? 0);
        Vector3 a = BoardGeometry.Center(fence.A, _width, _height, level);
        Vector3 b = BoardGeometry.Center(fence.B, _width, _height, level);
        Node3D post = LowPoly.Fence(alongX: fence.A.X == fence.B.X);
        post.Position = (a + b) * 0.5f;
        parent.AddChild(post);
    }

    /// <summary>把光标移到某格（null 表示隐藏）。</summary>
    public void SetCursor(Coord? coord)
    {
        _cursor.Visible = coord is not null;
        if (coord is { } c)
        {
            _cursor.Position = CenterOf(c) + new Vector3(0f, 0.014f, 0f);
        }
    }

    /// <summary>
    /// 按当前世界重画棋子、叠加层与预览。<paramref name="treatment"/> 来自 <see cref="TacticalLayerState.Treatment"/>：
    /// 打开信息层时临时降饱和、压低装饰对比并按需弱化棋子（5.7），退出即恢复。
    /// </summary>
    public void Refresh(
        ViewerWorld world,
        TacticalLayer? layer,
        BoardReading reading,
        SceneTreatment treatment,
        LibertyThresholds thresholds,
        TurnFlash flash,
        Coord? focus = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(treatment);
        ArgumentNullException.ThrowIfNull(flash);
        DefaultBoardView board = world.Board();

        // 地形不再是对局内不变量（artisan-terrain-edit）：架桥 / 立栅 / 烧林都改地砖、水面、栅栏与 _levels（新桥格要能被拾取），
        // 而这些只在 Build 里搭。地形指纹一变就整体重搭一次——2.6 第 12 条原写"GameRoot 每次刷新都重跑 Build"，实际不是，这里补上。
        if (TerrainKeyOf(board) != _terrainKey)
        {
            Build(board, _zoneOwners);
        }

        LayerContent? content = layer is { } active ? world.Layer(active, reading, thresholds) : null;

        _environment.Environment.AdjustmentSaturation = treatment.SaturationPercent / 100f;
        float decorationScale = 0.72f + (0.28f * treatment.DecorationContrastPercent / 100f);
        foreach (Node3D ornament in _decoration.GetChildren().OfType<Node3D>())
        {
            ornament.Scale = Vector3.One * decorationScale;
        }

        Clear(_overlay);
        Clear(_pieces);
        Clear(_preview);

        // 水面流动层与装饰对比同步压淡：信息层打开时它不该比判读信息更抢眼。
        Visuals.WaterFlow.SetShaderParameter("dim", 0.35f + (0.65f * treatment.DecorationContrastPercent / 100f));

        foreach ((Coord coord, StandardMaterial3D material) in _tileMaterials)
        {
            material.AlbedoColor = Visuals.Damp(_tileBase[coord], Math.Max(treatment.DecorationContrastPercent, 55));
        }

        PreviewPresentation? preview = world.Preview(thresholds);
        DrawRelicMarkers(board);
        DrawLifeSeals(board);
        DrawPieces(board, treatment);
        DrawLayer(content);
        DrawPreview(preview, focus);
        DrawFlash(flash);
    }

    /// <summary>
    /// 地形指纹：可落子 / 高度 / 地表 / 桥 + 全部栅栏边。只读默认棋盘视图模型，不读地图、不判改造。
    /// 预置设施与本局改造在这里<b>本来就分不出</b>——指纹变了只说明"要重搭"，不说明是谁改的。
    /// </summary>
    private static string TerrainKeyOf(DefaultBoardView board)
    {
        var sb = new System.Text.StringBuilder();
        foreach (BoardCellView cell in board.Cells)
        {
            sb.Append((int)cell.Terrain).Append(cell.Height).Append((int)cell.Surface).Append(cell.HasBridge ? '1' : '0').Append(';');
        }

        sb.Append('|');
        foreach (FenceEdge fence in board.Fences)
        {
            sb.Append(fence).Append(';');
        }

        return sb.ToString();
    }

    private static void Clear(Node container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void DrawRelicMarkers(DefaultBoardView board)
    {
        foreach (BoardCellView cell in board.Cells)
        {
            if (cell.Relic == RelicMarker.None)
            {
                continue;
            }

            // 未发现的信物统一标记，不区分预算分区（裁决 5）。
            Color color = cell.Relic == RelicMarker.Revealed ? Visuals.RelicRevealed : Visuals.RelicUnknown;
            _overlay.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.001f, BottomRadius = 0.12f, Height = 0.16f, RadialSegments = 4 },
                MaterialOverride = Visuals.Glow(color, 0.5f, false),
                Position = CenterOf(cell.Coord) + new Vector3(0f, 0.09f, 0f),
                RotationDegrees = new Vector3(0f, 45f, 0f),
            });
        }
    }

    /// <summary>
    /// 默认棋盘上当前行动玩家的禁入格（tactical-layers「活形与禁入格的标示」）：压暗底 + 所有者阵营色方框 + 叉。
    /// 哪些格禁入、归谁，一律读 <see cref="BoardCellView.Block"/> / <see cref="BoardCellView.LifeForbiddenBy"/>，本类不判活形。
    /// 地形不可落子靠地形本身、范围外不加标记（<see cref="PlacementBlocks.StyleOf"/>），三者外观互不相同。
    /// </summary>
    private void DrawLifeSeals(DefaultBoardView board)
    {
        foreach (BoardCellView cell in board.Cells)
        {
            if (PlacementBlocks.StyleOf(cell.Block) != PlacementMarkStyle.LifeSeal || cell.LifeForbiddenBy is not { } owner)
            {
                continue;
            }

            Color faction = Visuals.FactionColorOf(owner);
            AddTint(cell.Coord, Visuals.ForbiddenShade, 0.45f);
            AddRing(_overlay, cell.Coord, faction, false, 0.03f);
            AddCross(cell.Coord, faction, _overlay);
        }
    }

    private void DrawPieces(DefaultBoardView board, SceneTreatment treatment)
    {
        foreach (BoardCellView cell in board.Cells)
        {
            if (cell.Occupant is not { } occupant)
            {
                continue;
            }

            Node3D piece = BuildPiece(occupant, treatment.PieceEmphasisPercent);
            piece.Position = CenterOf(cell.Coord);
            _pieces.AddChild(piece);
        }
    }

    private static Node3D BuildPiece(Occupant occupant, int emphasisPercent)
    {
        FactionStyle faction = FactionTable.For(occupant.Owner);
        PieceStyle style = PieceStyleTable.For(occupant.Type);
        Color primary = Visuals.Damp(Visuals.ToColor(faction.Primary), emphasisPercent);
        Color body = Visuals.Damp(Visuals.ToColor(faction.Primary).Lerp(Colors.White, 0.42f), emphasisPercent);
        Color ink = Visuals.Damp(Visuals.EmblemInk, Math.Max(emphasisPercent, 60));
        return LowPoly.Piece(style.Silhouette, faction.Emblem, primary, body, ink);
    }

    // ---------- 信息层 ----------

    private void DrawLayer(LayerContent? content)
    {
        switch (content)
        {
            case TerritoryLayerContent territory:
                DrawTerritory(territory);
                break;
            case LibertyLayerContent liberties:
                DrawLiberties(liberties);
                break;
            case PowerLayerContent power:
                DrawPower(power);
                break;
            case RelicLayerContent relics:
                DrawRelics(relics);
                break;
            default:
                // 顺序层是纯文字层（顶部顺序条的展开视图），3D 场景不叠加任何东西。
                break;
        }
    }

    private void DrawTerritory(TerritoryLayerContent territory)
    {
        foreach (TerritoryCellView cell in territory.Cells)
        {
            (Color color, float alpha) = cell.State switch
            {
                TerritoryState.Occupied => (Visuals.FactionColorOf(cell.Owner!.Value).Lerp(Colors.White, 0.35f), 0.34f),
                TerritoryState.Exclusive => (Visuals.FactionColorOf(cell.Owner!.Value), 0.62f),
                TerritoryState.Contested => (Visuals.Contested, 0.55f),
                _ => (Visuals.Neutral, 0.16f),
            };
            AddTint(cell.Coord, color, alpha);
        }
    }

    private void DrawLiberties(LibertyLayerContent liberties)
    {
        foreach (LibertyGroupView group in liberties.Groups)
        {
            // 标记形状来自 Presentation（GroupMarks.ShapeOf）：已活 = 实线环 + 悬浮"眼"徽记，危险 = 危险色实线环，普通 = 阵营色虚线环。
            // 形状是第一通道、颜色是第二通道（已活 MUST NOT 只靠颜色区分）。
            GroupMarkShape shape = GroupMarks.ShapeOf(group.Mark);
            Color color = shape switch
            {
                GroupMarkShape.SolidRingWithEyeBadge => Visuals.Alive,
                GroupMarkShape.SolidRing => group.Level is DangerLevel.Urgent or DangerLevel.NoLiberty ? Visuals.Urgent : Visuals.Danger,
                _ => Visuals.FactionColorOf(group.Owner),
            };
            foreach (Coord stone in group.Stones)
            {
                AddRing(_overlay, stone, color, shape == GroupMarkShape.DashedRing, 0.024f);
                if (shape == GroupMarkShape.SolidRingWithEyeBadge)
                {
                    AddEyeBadge(_overlay, stone, color);
                }
            }

            foreach (Coord liberty in group.Liberties)
            {
                AddDot(liberty, Visuals.Liberty, 0.26f);
            }
        }
    }

    private void DrawPower(PowerLayerContent power)
    {
        // 领地分：独占空格按独占者的阵营着色（restore-go-core-rules 段 E，tasks 5.4 / 5.5）。格子来自势力明细的独占集合，
        // 争议格与中立格不在其中、不着色——它们不是任何玩家的得分，与独占格一眼可分。
        foreach (TerritoryCellView cell in power.Territory)
        {
            AddTint(cell.Coord, Visuals.FactionColorOf(cell.Owner!.Value), 0.5f);
        }

        // 倍率热区：柱高按显示档位 HeatLevel（min(倍增子数量, 3)），只是显示档位，不是倍率封顶。
        foreach (GroupScoreView group in power.Groups.Where(g => g.HeatLevel > 0))
        {
            foreach (Coord stone in group.Stones)
            {
                AddPillar(stone, Visuals.Contested, 0.12f + (0.14f * group.HeatLevel));
            }
        }
    }

    private void DrawRelics(RelicLayerContent relics)
    {
        foreach (RelicCellView relic in relics.Relics)
        {
            Color color = relic.Control switch
            {
                RelicControlKind.Controlled => Visuals.FactionColorOf(relic.Holder!.Value),
                RelicControlKind.Contested => Visuals.Contested,
                RelicControlKind.Blocked => Visuals.Urgent,
                _ => Visuals.Neutral,
            };
            AddTint(relic.Coord, color, 0.5f);
            AddRing(_overlay, relic.Coord, relic.IsRevealed ? Visuals.RelicRevealed : Visuals.RelicUnknown, !relic.IsRevealed, 0.032f);
        }
    }

    // ---------- 批次预演 ----------

    /// <summary>
    /// 预演叠加。<paramref name="focus"/> 是当前光标所在格：盘上同时暂放多枚匠人时，只显示<b>一枚</b>的候选目标——
    /// 光标停在某枚已暂放的匠人上就显示那枚，否则显示最后暂放的那枚。
    /// </summary>
    /// <remarks>
    /// 16 条候选边（裁决 T-11）同屏的读法：候选边一律<b>贴在边上</b>画半透明矮栏，不做任何偏移。
    /// 一枚匠人的 16 条边恰好构成"十字五格区域的 12 条外轮廓边 + 匠人格自身的 4 条边"——
    /// 屏幕上就是一个十字轮廓套一个小方框，中心正是匠人，读法自解释，不需要"朝落点收缩"。
    /// （收缩要判断边的哪一端靠匠人，而 <see cref="FenceEdge"/> 构造即归一、丢掉了端点角色，
    /// 表现层重新判邻接就是第二份邻接实现——禁。）一次只画一枚匠人的候选，是为了避免两枚匠人的十字叠在一起看不清。
    /// 已选目标不受 <paramref name="focus"/> 限制，全部匠人的都画：它是要提交的动作，必须一直可见。
    /// </remarks>
    private void DrawPreview(PreviewPresentation? preview, Coord? focus = null)
    {
        if (preview is null)
        {
            return;
        }

        // 破坏活形：触发的立栅目标是边（FailurePresentation.EdgeHighlights），沿边立一道警示色亮栏。
        foreach (EdgeHighlight edge in preview.EdgeHighlights.Where(h => h.Kind == HighlightKind.FailureFocus))
        {
            AddEditFence(edge.Edge, Visuals.Urgent, 1f);
        }

        // 已选目标：全部匠人的都画，实心亮色（HighlightStyle.EditChosenMark）。
        foreach (EdgeHighlight edge in preview.EdgeHighlights.Where(h => h.Kind == HighlightKind.ChosenEdit))
        {
            AddEditFence(edge.Edge, Visuals.EditChosen, 1f);
        }

        foreach (CellHighlight cell in preview.Highlights.Where(h => h.Kind == HighlightKind.ChosenEdit))
        {
            AddTint(cell.Coord, Visuals.EditChosen, 0.55f, _preview);
            AddRing(_preview, cell.Coord, Visuals.EditChosen, false, 0.062f);
        }

        // 候选目标：只画当前那一枚匠人的，半透明（HighlightStyle.EditTargetHint），明显弱于已选。
        ArtisanEditView? current = preview.ArtisanEdits.FirstOrDefault(a => focus is { } f && a.ArtisanCell == f)
            ?? preview.ArtisanEdits.LastOrDefault();
        if (current is not null)
        {
            foreach (FenceEdge edge in current.FenceEdges.Where(e => current.Chosen is not { Kind: TerrainEditKind.Fence } c || c.Edge != e))
            {
                AddEditFence(edge, Visuals.EditTarget, 0.42f);
            }

            foreach (Coord cell in current.BridgeCells.Concat(current.BurnCells))
            {
                if (current.Chosen is { Kind: not TerrainEditKind.Fence } chosen && chosen.Cell == cell)
                {
                    continue;
                }

                AddTint(cell, Visuals.EditTarget, 0.30f, _preview);
                AddRing(_preview, cell, Visuals.EditTarget, true, 0.046f);
            }
        }

        foreach (StagedPieceView staged in preview.StagedPieces)
        {
            // 暂放：半透明发光（HighlightStyle.TranslucentGlow），单独渲染在 StagedPieces 层。
            Node3D piece = BuildPiece(new Occupant(preview.Player, staged.Type), 100);
            piece.Position = CenterOf(staged.Coord) + new Vector3(0f, 0.02f, 0f);
            Translucent(piece, 0.55f);
            _preview.AddChild(piece);
            AddTint(staged.Coord, Visuals.FactionColorOf(preview.Player).Lerp(Colors.White, 0.4f), 0.42f, _preview);
        }

        foreach (CellHighlight highlight in preview.Highlights)
        {
            switch (VisualLayering.StyleOf(highlight.Kind))
            {
                case HighlightStyle.DashedOutline:
                    // 预计提子：虚线轮廓，与暂放的实心发光在手法上就分得开（5.6）。
                    AddRing(_preview, highlight.Coord, Visuals.Urgent, true, 0.05f);
                    break;
                case HighlightStyle.WarningOutline:
                    AddRing(_preview, highlight.Coord, Visuals.Danger, false, 0.056f);
                    AddRing(_preview, highlight.Coord, Visuals.Danger, false, 0.076f);
                    break;
                case HighlightStyle.RevealBadge:
                    AddBadge(highlight.Coord, Visuals.RelicUnknown);
                    break;
                case HighlightStyle.FailureOutline:
                    AddCross(highlight.Coord, Visuals.Urgent);
                    break;
                case HighlightStyle.LifeOutline:
                    // 活棋禁入 / 破坏活形涉及的活形棋串：与棋串读法的"已活"同形（实线环 + 悬浮眼徽记）。
                    AddRing(_preview, highlight.Coord, Visuals.Alive, false, 0.066f);
                    AddEyeBadge(_preview, highlight.Coord, Visuals.Alive);
                    break;
                default:
                    break;
            }
        }
    }

    private void DrawFlash(TurnFlash flash)
    {
        foreach (Coord coord in flash.Placed)
        {
            AddRing(_preview, coord, Colors.White, false, 0.07f);
        }

        foreach (Coord coord in flash.Captured)
        {
            AddCross(coord, Visuals.Urgent);
        }

        // 改造落成的一次可察觉反馈（visual-style-baseline「改造的可视表现」）：格目标亮环 + 亮底，边目标沿边立一道亮栏。
        // 反馈只持续 FlashSeconds；设施本身此刻已经由 Build 按新地形画成了与预置设施一模一样的样子。
        foreach (TerrainEdit edit in flash.Edits)
        {
            if (edit.Kind == TerrainEditKind.Fence)
            {
                AddEditFence(edit.Edge, Visuals.EditDone, 1f);
            }
            else
            {
                AddTint(edit.Cell, Visuals.EditDone, 0.5f, _preview);
                AddRing(_preview, edit.Cell, Visuals.EditDone, false, 0.09f);
            }
        }
    }

    /// <summary>
    /// 沿一条边画改造标记。位置与 <see cref="AddFence"/> 同一套算式（两格格心的中点），不引入第二份坐标换算。
    /// <paramref name="alpha"/> 满值时立起亮栏（已选 / 落成），否则贴地画虚线短条（候选）。
    /// </summary>
    private void AddEditFence(FenceEdge edge, Color color, float alpha)
    {
        int level = Math.Max(LevelOf(edge.A) ?? 0, LevelOf(edge.B) ?? 0);
        Vector3 a = BoardGeometry.Center(edge.A, _width, _height, level);
        Vector3 b = BoardGeometry.Center(edge.B, _width, _height, level);
        Node3D mark = LowPoly.EditEdgeMark(
            alongX: edge.A.X == edge.B.X, Visuals.Flat(new Color(color, alpha)), upright: alpha >= 1f);
        mark.Position = ((a + b) * 0.5f) + new Vector3(0f, 0.012f, 0f);
        _preview.AddChild(mark);
    }

    // ---------- 图元 ----------

    private void AddTint(Coord coord, Color color, float alpha, Node3D? parent = null)
    {
        (parent ?? _overlay).AddChild(new MeshInstance3D
        {
            Mesh = LowPoly.Marker(BoardGeometry.TileSize),
            MaterialOverride = Visuals.Flat(new Color(color, alpha)),
            Position = CenterOf(coord) + new Vector3(0f, 0.008f, 0f),
        });
    }

    private void AddDot(Coord coord, Color color, float size)
    {
        _overlay.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = size * 0.5f, BottomRadius = size * 0.5f, Height = 0.02f, RadialSegments = 8 },
            MaterialOverride = Visuals.Glow(color, 0.8f, false),
            Position = CenterOf(coord) + new Vector3(0f, 0.02f, 0f),
        });
    }

    private void AddPillar(Coord coord, Color color, float height)
    {
        _overlay.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.10f, height, 0.10f) },
            MaterialOverride = Visuals.Glow(color, 0.9f, false),
            Position = CenterOf(coord) + new Vector3(0f, 0.92f + (height * 0.5f), 0f),
        });
    }

    private void AddBadge(Coord coord, Color color)
    {
        _preview.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.10f, Height = 0.20f, RadialSegments = 6, Rings = 3 },
            MaterialOverride = Visuals.Glow(color, 1.2f, false),
            Position = CenterOf(coord) + new Vector3(0f, 0.74f, 0f),
        });
    }

    /// <summary>已活标记的"眼"徽记：棋子上方悬浮一枚平放的圆环（形状通道，去色后仍与危险棋串的单环可分）。</summary>
    private void AddEyeBadge(Node3D parent, Coord coord, Color color)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.07f, OuterRadius = 0.13f, Rings = 12, RingSegments = 6 },
            MaterialOverride = Visuals.Glow(color, 1.1f, false),
            Position = CenterOf(coord) + new Vector3(0f, 1.02f, 0f),
        });
    }

    private void AddCross(Coord coord, Color color, Node3D? parent = null)
    {
        Vector3 center = CenterOf(coord) + new Vector3(0f, 0.045f, 0f);
        StandardMaterial3D material = Visuals.Flat(color);
        for (int i = 0; i < 2; i++)
        {
            (parent ?? _preview).AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.62f, 0.02f, 0.07f) },
                MaterialOverride = material,
                Position = center,
                RotationDegrees = new Vector3(0f, i == 0 ? 45f : -45f, 0f),
            });
        }
    }

    /// <summary>格子轮廓环。<paramref name="dashed"/> 为虚线（预计提子），否则实线（警示 / 落子演出）。</summary>
    private void AddRing(Node3D parent, Coord coord, Color color, bool dashed, float y)
    {
        Vector3 center = CenterOf(coord) + new Vector3(0f, y, 0f);
        StandardMaterial3D material = Visuals.Flat(color);
        const float half = BoardGeometry.TileSize * 0.5f;
        const float thickness = 0.055f;
        float[] offsets = dashed ? [-0.30f, 0f, 0.30f] : [0f];
        float length = dashed ? 0.20f : BoardGeometry.TileSize;
        for (int side = 0; side < 4; side++)
        {
            bool horizontal = side is 0 or 1;
            float sign = side is 0 or 2 ? 1f : -1f;
            foreach (float offset in offsets)
            {
                Vector3 size = horizontal ? new Vector3(length, 0.02f, thickness) : new Vector3(thickness, 0.02f, length);
                Vector3 position = horizontal
                    ? center + new Vector3(offset, 0f, sign * half)
                    : center + new Vector3(sign * half, 0f, offset);
                parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = position });
            }
        }
    }

    private static void Translucent(Node node, float alpha)
    {
        if (node is MeshInstance3D instance && instance.MaterialOverride is StandardMaterial3D material)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            material.AlbedoColor = new Color(material.AlbedoColor, alpha);
            material.EmissionEnabled = true;
            material.Emission = material.AlbedoColor;
            material.EmissionEnergyMultiplier = 0.45f;
        }

        foreach (Node child in node.GetChildren())
        {
            Translucent(child, alpha);
        }
    }

    /// <summary>
    /// 棋盘四边的围棋记法坐标标注（visual-style-baseline「棋盘坐标标注」）：列字母沿上下两边，行数字沿左右两边，个数随棋盘宽高。
    /// 文本一律取 <see cref="Coord.Column"/> / <see cref="Coord.Row"/>，位置一律取 <see cref="BoardGeometry"/> 的锚点（棋盘外圈 h=0 平面，
    /// 边缘格是高台也不影响）——本层不得再写一份跳过 <c>I</c> 的字母表，否则界面与日志迟早指向不同的格子。
    /// </summary>
    private void BuildCoordinateLabels()
    {
        var labels = new Node3D { Name = "CoordinateLabels" };
        AddChild(labels);

        for (int x = 0; x < _width; x++)
        {
            string text = new Coord(x, 0).Column.ToString();
            labels.AddChild(Label(text, BoardGeometry.ColumnLabelAnchor(x, _width, _height, far: false)));
            labels.AddChild(Label(text, BoardGeometry.ColumnLabelAnchor(x, _width, _height, far: true)));
        }

        for (int y = 0; y < _height; y++)
        {
            string text = new Coord(0, y).Row.ToString(System.Globalization.CultureInfo.InvariantCulture);
            labels.AddChild(Label(text, BoardGeometry.RowLabelAnchor(y, _width, _height, right: false)));
            labels.AddChild(Label(text, BoardGeometry.RowLabelAnchor(y, _width, _height, right: true)));
        }
    }

    /// <summary>
    /// 一个坐标标注：绕 X 轴 −90° 平铺在棋盘平面上，与地砖共面，像围棋棋盘边缘印刷的坐标。
    /// </summary>
    /// <remarks>
    /// <b>不要改用 billboard。</b>Godot 的 Label3D 在 billboard 下渲染的是文字面的背面——billboard 让节点的
    /// −Z 轴指向相机，而文字画在 +Z 面——四边标注会全部左右镜像。<c>Enabled</c> 与 <c>FixedY</c> 都如此。
    /// 数字里的 1 / 0 / 8 字形左右对称，看不出异常，2 / 3 / 5 / 7 与字母 B / K / L 才暴露，
    /// 所以这个缺陷靠"扫一眼截图"发现不了，必须逐字辨认。对局相机本身是固定的（见 <see cref="Build"/> 里的
    /// <see cref="Camera3D"/>，没有任何旋转绑定），不存在"转到背面"的情形，平铺没有代价。（board-coordinates D2 修订）
    /// </remarks>
    private static Label3D Label(string text, Vector3 position) => new()
    {
        Text = text,
        Position = position,
        FontSize = 96,
        // FixedSize：标注在屏幕上大小恒定，不随距相机远近缩放。坐标是读数不是景物，
        // 近边的 A 与远边的 A 必须一样大——否则近端会胀到压住底部面板，远端小到看不清。
        FixedSize = true,
        PixelSize = 0.00035f,
        Modulate = Visuals.CoordinateLabel,
        OutlineModulate = Visuals.CoordinateLabelOutline,
        OutlineSize = 10,
        Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
        RotationDegrees = new Vector3(-90f, 0f, 0f),
        NoDepthTest = false,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
    };
}
