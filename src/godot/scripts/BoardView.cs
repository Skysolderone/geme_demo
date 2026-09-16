using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
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
/// 3D 棋盘表现（tactical-ui 裁决 6）：方块地砖 + 固定倾斜俯视镜头 + 程序生成的低多边形棋子与叠加标记。
/// </summary>
/// <remarks>
/// 本类<b>不做任何规则计算</b>：画什么全部来自 <see cref="ViewerWorld"/>（默认棋盘、预演呈现、信息层内容）。
/// 绘制顺序遵守 <see cref="RenderLayer"/>：装饰（岩石）在最下，网格 / 归属 / 标记 / 棋子 / 预览依次在上。
/// </remarks>
public sealed partial class BoardView : Node3D
{
    private readonly Dictionary<Coord, StandardMaterial3D> _tileMaterials = [];
    private readonly Dictionary<Coord, Color> _tileBase = [];
    private Node3D _decoration = null!;
    private Node3D _overlay = null!;
    private Node3D _pieces = null!;
    private Node3D _preview = null!;
    private MeshInstance3D _cursor = null!;
    private WorldEnvironment _environment = null!;
    private int _width;
    private int _height;

    /// <summary>固定倾斜俯视镜头（约 40 度）。</summary>
    public Camera3D Camera { get; private set; } = null!;

    /// <summary>一次性搭出地形、装饰、灯光与镜头。</summary>
    public void Build(MatchPublicView view, IReadOnlyDictionary<int, PlayerId> zoneOwners)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(zoneOwners);

        // 幂等：插旗锁定后要按出生区归属重染地砖，会再搭一次。
        Clear(this);
        _tileMaterials.Clear();
        _tileBase.Clear();
        _width = view.Board.Width;
        _height = view.Board.Height;

        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3((_width + 1.4f) * BoardGeometry.CellSize, BoardGeometry.TileHeight, (_height + 1.4f) * BoardGeometry.CellSize) },
            MaterialOverride = Visuals.Matte(Visuals.GridInk),
            Position = new Vector3(0f, -0.02f, 0f),
        });

        _decoration = new Node3D { Name = "Decoration" };
        AddChild(_decoration);

        var tiles = new Node3D { Name = "Tiles" };
        AddChild(tiles);

        int variant = 0;
        foreach (Coord coord in view.Board.AllCoords())
        {
            Cell cell = view.Board[coord];
            Vector3 center = BoardGeometry.Center(coord, _width, _height);
            Color color = Visuals.TilePlayable;
            if (cell.Terrain == Terrain.Obstacle)
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

            StandardMaterial3D material = Visuals.Matte(color);
            _tileMaterials[coord] = material;
            _tileBase[coord] = color;
            tiles.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.TileSize, BoardGeometry.TileHeight, BoardGeometry.TileSize) },
                MaterialOverride = material,
                Position = center - new Vector3(0f, BoardGeometry.TopY, 0f),
            });
        }

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

        Camera = new Camera3D { Position = new Vector3(0f, 9.0f, 10.4f), Fov = 54f };
        AddChild(Camera);
        Camera.LookAt(new Vector3(0f, 0f, 0.3f), Vector3.Up);
    }

    /// <summary>把光标移到某格（null 表示隐藏）。</summary>
    public void SetCursor(Coord? coord)
    {
        _cursor.Visible = coord is not null;
        if (coord is { } c)
        {
            _cursor.Position = BoardGeometry.Center(c, _width, _height) + new Vector3(0f, 0.014f, 0f);
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
        foreach (Node3D rock in _decoration.GetChildren().OfType<Node3D>())
        {
            rock.Scale = Vector3.One * decorationScale;
        }

        Clear(_overlay);
        Clear(_pieces);
        Clear(_preview);

        foreach ((Coord coord, StandardMaterial3D material) in _tileMaterials)
        {
            material.AlbedoColor = Visuals.Damp(_tileBase[coord], Math.Max(treatment.DecorationContrastPercent, 55));
        }

        DrawRelicMarkers(board);
        DrawPieces(board, treatment);
        DrawLayer(content);
        DrawPreview(world.Preview(thresholds));
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
                Position = BoardGeometry.Center(cell.Coord, _width, _height) + new Vector3(0f, 0.09f, 0f),
                RotationDegrees = new Vector3(0f, 45f, 0f),
            });
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
            piece.Position = BoardGeometry.Center(cell.Coord, _width, _height);
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
        foreach (TerritoryContributionView cell in power.TerritoryCells)
        {
            AddTint(cell.Coord, Visuals.FactionColorOf(cell.Owner), 0.5f);
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
            piece.Position = BoardGeometry.Center(staged.Coord, _width, _height) + new Vector3(0f, 0.02f, 0f);
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
            Position = BoardGeometry.Center(coord, _width, _height) + new Vector3(0f, 0.008f, 0f),
        });
    }

    private void AddDot(Coord coord, Color color, float size)
    {
        _overlay.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = size * 0.5f, BottomRadius = size * 0.5f, Height = 0.02f, RadialSegments = 8 },
            MaterialOverride = Visuals.Glow(color, 0.8f, false),
            Position = BoardGeometry.Center(coord, _width, _height) + new Vector3(0f, 0.02f, 0f),
        });
    }

    private void AddPillar(Coord coord, Color color, float height)
    {
        _overlay.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.10f, height, 0.10f) },
            MaterialOverride = Visuals.Glow(color, 0.9f, false),
            Position = BoardGeometry.Center(coord, _width, _height) + new Vector3(0f, 0.92f + (height * 0.5f), 0f),
        });
    }

    private void AddBadge(Coord coord, Color color)
    {
        _preview.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.10f, Height = 0.20f, RadialSegments = 6, Rings = 3 },
            MaterialOverride = Visuals.Glow(color, 1.2f, false),
            Position = BoardGeometry.Center(coord, _width, _height) + new Vector3(0f, 0.74f, 0f),
        });
    }

    private void AddCross(Coord coord, Color color)
    {
        Vector3 center = BoardGeometry.Center(coord, _width, _height) + new Vector3(0f, 0.045f, 0f);
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
        Vector3 center = BoardGeometry.Center(coord, _width, _height) + new Vector3(0f, y, 0f);
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
    /// 棋盘四边的围棋记法坐标标注（visual-style-baseline「棋盘坐标标注」）：列字母沿上下两边，行数字沿左右两边。
    /// 文本一律取 <see cref="Coord.Column"/> / <see cref="Coord.Row"/>，位置一律取 <see cref="BoardGeometry"/> 的锚点——
    /// 本层不得再写一份跳过 <c>I</c> 的字母表，否则界面与日志迟早指向不同的格子。
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
