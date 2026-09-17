using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Presentation.Layers;
using Siege.Presentation.Preview;
using Siege.Presentation.Style;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>AI 小回合的落子 / 提子演出数据。</summary>
public sealed record TurnFlash(ImmutableArray<Coord> Placed, ImmutableArray<Coord> Captured)
{
    /// <summary>无演出。</summary>
    public static readonly TurnFlash None = new([], []);

    /// <summary>是否为空。</summary>
    public bool IsEmpty => Placed.IsEmpty && Captured.IsEmpty;
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
    private Node3D _sites = null!;
    private Node3D _overlay = null!;
    private Node3D _pieces = null!;
    private Node3D _preview = null!;
    private MeshInstance3D _cursor = null!;
    private WorldEnvironment _environment = null!;
    private int _width;
    private int _height;

    /// <summary>固定倾斜俯视镜头（俯角 60 度）。</summary>
    public Camera3D Camera { get; private set; } = null!;

    /// <summary>某可落子格的层数（视图模型的高度）；不可落子格（岩石、未架桥深水）或盘外为 <c>null</c>。供分层拾取使用。</summary>
    public int? LevelOf(Coord coord) => _levels.TryGetValue(coord, out int level) ? level : null;

    /// <summary>某格格心（含高度）的世界坐标：盘内一切叠加物都从这里取位置。</summary>
    public Vector3 CenterOf(Coord coord) => BoardGeometry.Center(coord, _width, _height, LevelOf(coord) ?? 0);

    /// <summary>一次性搭出地形、装饰、灯光与镜头。只消费默认棋盘视图模型。</summary>
    public void Build(DefaultBoardView board, IReadOnlyDictionary<int, PlayerId> zoneOwners)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(zoneOwners);

        // 幂等：插旗锁定后要按出生区归属重染地砖，会再搭一次。
        Clear(this);
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
            MaterialOverride = Visuals.Matte(Visuals.GridInk),
            Position = new Vector3(0f, BoardGeometry.TopY - WaterDrop - (BoardGeometry.TileHeight * 0.5f), 0f),
        });

        _decoration = new Node3D { Name = "Decoration" };
        AddChild(_decoration);

        var tiles = new Node3D { Name = "Tiles" };
        AddChild(tiles);

        int variant = 0;
        foreach (BoardCellView cell in board.Cells)
        {
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
                _tileMaterials[cell.Coord] = AddWater(tiles, waterCenter, water, variant++);
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
                color = Visuals.TileObstacle;
                Node3D rock = LowPoly.Rock(variant++);
                rock.Position = center;
                _decoration.AddChild(rock);
            }
            else if (cell.BirthZone is int zone)
            {
                // 插旗阶段还没有归属，先用统一的出生区高亮让玩家看得见可点的区域；锁定后改染该阵营主色。
                color = zoneOwners.TryGetValue(zone, out PlayerId owner)
                    ? color.Lerp(Visuals.FactionColorOf(owner), 0.34f)
                    : color.Lerp(Visuals.BirthHint, 0.62f);
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

        BuildCoordinateLabels();

        _sites = new Node3D { Name = "Sites" };
        AddChild(_sites);
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
            RotationDegrees = new Vector3(-58f, -42f, 0f),
            LightColor = Color.Color8(255, 246, 226),
            LightEnergy = 1.15f,
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
                BackgroundMode = global::Godot.Environment.BGMode.Color,
                BackgroundColor = Color.Color8(22, 26, 34),
                AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Color.Color8(120, 130, 150),
                AmbientLightEnergy = 0.55f,
                AdjustmentEnabled = true,
                AdjustmentSaturation = 1f,
            },
        };
        AddChild(_environment);

        // 固定俯视相机：俯角取 60°。h=2 高台（0.70 高）在这个角度下向远处只投 0.70 / tan 60° ≈ 0.40 格的遮挡，
        // 小于半格——紧贴崖壁身后的 h=0 格格心仍露出来，能被点到（--pick-check 钉住）；45° 时会被挡住。
        // 崖壁侧面在 60° 下仍有 cos 60° = 0.5 的投影高度，看得见。距离随棋盘（含标注外圈）的跨度缩放。
        const float pitchDegrees = 60f;
        float span = Math.Max(_width, _height) + (2f * BoardGeometry.FarLabelMargin);
        float distance = 14.6f * span / 12.7f;
        Vector3 target = new(0f, 0.2f, 0.3f);
        Camera = new Camera3D
        {
            Position = target + new Vector3(0f, distance * Mathf.Sin(Mathf.DegToRad(pitchDegrees)), distance * Mathf.Cos(Mathf.DegToRad(pitchDegrees))),
            Fov = 54f,
        };
        AddChild(Camera);
        Camera.LookAt(target, Vector3.Up);
    }

    /// <summary>
    /// 一格地砖：h=0 只有一块面砖；h=1 在面砖下垫一层土色侧面；h=2 再垫一层岩灰侧面。
    /// 相邻格高度差越大露出的色带越多（Δh=1 一条土色，Δh=2 土色 + 岩灰），崖壁与缓坡因此可分。
    /// </summary>
    private static void AddTileStack(Node3D parent, Vector3 top, int level, StandardMaterial3D surface)
    {
        const float half = BoardGeometry.TileHeight * 0.5f;
        float bandBottom = -half;
        for (int layer = 1; layer <= level; layer++)
        {
            float bandTop = BoardGeometry.TopYOf(layer) - BoardGeometry.TileHeight;
            parent.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.TileSize, bandTop - bandBottom, BoardGeometry.TileSize) },
                MaterialOverride = Visuals.Matte(layer == 1 ? Visuals.SlopeSide : Visuals.CliffSide),
                Position = new Vector3(top.X, (bandTop + bandBottom) * 0.5f, top.Z),
            });
            bandBottom = bandTop;
        }

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
    private static StandardMaterial3D AddWater(Node3D parent, Vector3 top, Color color, int variant)
    {
        StandardMaterial3D material = Visuals.Matte(color, 0.55f);
        const float drop = WaterDrop;
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.TileSize, BoardGeometry.TileHeight, BoardGeometry.TileSize) },
            MaterialOverride = material,
            Position = top - new Vector3(0f, drop + (BoardGeometry.TileHeight * 0.5f), 0f),
        });
        StandardMaterial3D ripple = Visuals.Flat(Visuals.WaterRipple);
        for (int i = 0; i < 2; i++)
        {
            float z = ((variant + i) % 3 * 0.22f) - 0.24f;
            parent.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.004f, 0.03f) },
                MaterialOverride = ripple,
                Position = top + new Vector3(i == 0 ? -0.18f : 0.16f, -drop + 0.002f, z),
            });
        }

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
        TurnFlash flash)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(treatment);
        ArgumentNullException.ThrowIfNull(flash);
        DefaultBoardView board = world.Board();
        LayerContent? content = layer is { } active ? world.Layer(active, reading, thresholds) : null;

        _environment.Environment.AdjustmentSaturation = treatment.SaturationPercent / 100f;
        float decorationScale = 0.72f + (0.28f * treatment.DecorationContrastPercent / 100f);
        foreach (Node3D ornament in _decoration.GetChildren().OfType<Node3D>())
        {
            ornament.Scale = Vector3.One * decorationScale;
        }

        Clear(_sites);
        Clear(_overlay);
        Clear(_pieces);
        Clear(_preview);

        foreach ((Coord coord, StandardMaterial3D material) in _tileMaterials)
        {
            material.AlbedoColor = Visuals.Damp(_tileBase[coord], Math.Max(treatment.DecorationContrastPercent, 55));
        }

        PreviewPresentation? preview = world.Preview(thresholds);
        DrawRelicMarkers(board);
        DrawSites(board, preview, compact: content is not null);
        DrawPieces(board, treatment);
        DrawLayer(content);
        DrawPreview(preview);
        DrawFlash(flash);
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
    /// 据点地标（visual-style-baseline「据点地标」）：档位 → 营帐 / 篝火 / 石碑；控制状态 → 被控制插主色 + 徽记旗、争议插交叉警示旗、无人不插旗。
    /// 状态与控制者全部取视图模型 <see cref="DefaultBoardView.Sites"/>，本方法不判定控制。
    /// 格上有正式棋子或本人暂放棋子、或打开了任一信息层（要读气点与着色）时，地标整体缩小并退到远侧格角（相机在 +Z 一侧，
    /// 格角 (−0.35, −0.35) 在棋子身后），棋子与叠加标记完整可见；地标只是 MeshInstance3D，不参与拾取。
    /// </summary>
    private void DrawSites(DefaultBoardView board, PreviewPresentation? preview, bool compact)
    {
        HashSet<Coord> staged = preview is null ? [] : [.. preview.StagedPieces.Select(p => p.Coord)];
        foreach (SiteView site in board.Sites)
        {
            bool occupied = board.CellAt(site.Coord).Occupant is not null || staged.Contains(site.Coord);
            bool aside = occupied || compact;
            Node3D landmark = site.Tier switch
            {
                SiteTier.Tent => LowPoly.Tent(),
                SiteTier.Campfire => LowPoly.Campfire(),
                SiteTier.Stele => LowPoly.Stele(),
                _ => throw new ArgumentOutOfRangeException(nameof(board), site.Tier, "未知据点档位。"),
            };

            Vector3 center = CenterOf(site.Coord);
            landmark.Position = center + (aside ? new Vector3(-SiteAsideOffset, 0f, -SiteAsideOffset) : Vector3.Zero);
            landmark.Scale = Vector3.One * (aside ? SiteAsideScale : 1f);
            _sites.AddChild(landmark);

            // 旗帜单独缩放（缩得比地标少，徽记在对局相机下仍读得出）。空格：插在地标右后侧；退到格角时：插在远边、棋子正后方偏左，
            // 旗面在屏幕上高过棋子顶部（远 0.38 格、高 0.6 的旗面投影在高 0.6 的棋子之上）。
            Vector3 flagFoot = center + (aside ? new Vector3(-0.10f, 0f, -0.38f) : new Vector3(0.32f, 0f, -0.24f));
            float flagScale = aside ? SiteAsideFlagScale : 1f;
            Node3D? flag = site.Kind switch
            {
                SiteControlKind.Occupied or SiteControlKind.UniqueCoverage => ControllerFlag(site.Controller!.Value),
                SiteControlKind.Contested => LowPoly.ContestedFlags(SiteFlagHeight * 0.85f),
                // 无人：不插旗。
                _ => null,
            };
            if (flag is not null)
            {
                flag.Position = flagFoot;
                flag.Scale = Vector3.One * flagScale;
                _sites.AddChild(flag);
            }
        }
    }

    private static Node3D ControllerFlag(PlayerId controller)
    {
        FactionStyle faction = FactionTable.For(controller);
        return LowPoly.SiteFlag(faction.Emblem, Visuals.ToColor(faction.Primary), Visuals.EmblemInk, SiteFlagHeight);
    }

    /// <summary>退到格角时旗帜的缩放（杆高约 0.62，与棋子同高、立在棋子身后）。</summary>
    private const float SiteAsideFlagScale = 0.62f;

    /// <summary>
    /// 有棋子 / 打开信息层时地标的缩放。与 <see cref="SiteAsideOffset"/> 联立：营帐（最宽，0.54 × 0.46）缩后最近角距格心 0.363、
    /// 石碑基座 0.41、篝火石圈 0.38，都在棋子底座（半径 0.36）之外；营帐外缘 0.451 ≈ 地砖半宽 0.45。
    /// </summary>
    private const float SiteAsideScale = 0.375f;

    /// <summary>退到格角时相对格心的偏移（沿 −X、−Z，即远离相机的一角）。</summary>
    private const float SiteAsideOffset = 0.35f;

    /// <summary>旗杆高度：高过石碑（0.80），让三档地标上的旗帜在对局相机下都露得出来。</summary>
    private const float SiteFlagHeight = 1.0f;

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
            Color color = group.Level switch
            {
                DangerLevel.Urgent or DangerLevel.NoLiberty => Visuals.Urgent,
                DangerLevel.Danger => Visuals.Danger,
                _ => Visuals.FactionColorOf(group.Owner),
            };
            foreach (Coord stone in group.Stones)
            {
                AddRing(_overlay, stone, color, group.Level == DangerLevel.Safe, 0.024f);
            }

            foreach (Coord liberty in group.Liberties)
            {
                AddDot(liberty, Visuals.Liberty, 0.26f);
            }
        }
    }

    private void DrawPower(PowerLayerContent power)
    {
        // 势力层只给据点格着色（控制者主色 / 争议金色 / 无人暗灰），不再给任何空格着领地贡献色（tactical-layers「势力层显示据点控制」）。
        foreach (SiteView site in power.Sites)
        {
            (Color color, float alpha) = site.Kind switch
            {
                SiteControlKind.Occupied or SiteControlKind.UniqueCoverage => (Visuals.FactionColorOf(site.Controller!.Value), 0.6f),
                SiteControlKind.Contested => (Visuals.Contested, 0.6f),
                _ => (Visuals.Neutral, 0.3f),
            };
            AddTint(site.Coord, color, alpha);
            AddRing(_overlay, site.Coord, color, site.Kind == SiteControlKind.Contested, 0.03f);
        }

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

    private void DrawPreview(PreviewPresentation? preview)
    {
        if (preview is null)
        {
            return;
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

    private void AddCross(Coord coord, Color color)
    {
        Vector3 center = CenterOf(coord) + new Vector3(0f, 0.045f, 0f);
        StandardMaterial3D material = Visuals.Flat(color);
        for (int i = 0; i < 2; i++)
        {
            _preview.AddChild(new MeshInstance3D
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
