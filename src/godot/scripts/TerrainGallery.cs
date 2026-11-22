using System.Linq;
using Godot;
using Siege.Presentation.Camera;

namespace Siege.Godot;

/// <summary>
/// 地形装饰对照图（chibi-world 段 ①，人工检查清单用）：<c>--terrain-gallery=&lt;PNG 路径&gt;</c> 时不建局，把 18 类装饰（栅栏只放沿 X 的一段）各取第 0 档、
/// 立在各自所在地表色的一块地砖上，三行六列、同光照拍一张图后退出；加 <c>--terrain-gallery-variants</c> 改拍全部档（每类一行，分左右两栏）。
/// </summary>
/// <remarks>
/// 纯渲染：部件取 <see cref="TerrainParts.Create"/>（与对局同一条加载链：卡通 glb → <c>.tscn</c> → 程序生成），删掉 <c>terrain_chibi/</c> 再拍即得改前样式。
/// 光照与环境同 <see cref="PieceGallery"/>；相机俯角 60°、视场角与对局相机相同。
/// </remarks>
public sealed partial class TerrainGallery : Node3D
{
    /// <summary>第 0 档对照图的排列：上行障碍格（不可落子），中行可落子格的地表点缀，下行格角点缀与设施。</summary>
    private static readonly TerrainParts.Kind[][] Rows =
    [
        [TerrainParts.Rock, TerrainParts.Crystal, TerrainParts.Ruins, TerrainParts.Pines, TerrainParts.Arch, TerrainParts.Brazier],
        [TerrainParts.Chest, TerrainParts.Trees, TerrainParts.Desert, TerrainParts.Marsh, TerrainParts.Crag, TerrainParts.Shallows],
        [TerrainParts.Grass, TerrainParts.Pebble, TerrainParts.Flower, TerrainParts.Stele, TerrainParts.FenceX, TerrainParts.Bridge],
    ];

    private static readonly TerrainParts.Kind[] AllKinds =
    [
        TerrainParts.Rock, TerrainParts.Ruins, TerrainParts.Pines, TerrainParts.Crystal, TerrainParts.Brazier, TerrainParts.Arch, TerrainParts.Chest,
        TerrainParts.Trees, TerrainParts.Desert, TerrainParts.Marsh, TerrainParts.Crag, TerrainParts.Shallows,
        TerrainParts.Grass, TerrainParts.Pebble, TerrainParts.Flower, TerrainParts.Stele, TerrainParts.FenceX, TerrainParts.FenceZ, TerrainParts.Bridge,
    ];

    private const float Spacing = 1.25f;
    private const float RowGap = 1.55f;
    private const float VariantSpacing = 1.08f;
    private const float VariantRowGap = 1.12f;

    private readonly string _path;
    private readonly bool _variants;
    private readonly List<(string Name, Node3D Part)> _placed = [];
    private Camera3D? _camera;
    private int _frame;
    private bool _pending;

    /// <param name="path">PNG 输出路径。</param>
    /// <param name="variants">true 时拍全部档（<c>--terrain-gallery-variants</c>）。</param>
    public TerrainGallery(string path, bool variants = false)
    {
        _path = path;
        _variants = variants;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52f, -38f, 0f),
            LightColor = Color.Color8(255, 240, 208),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
            ShadowBlur = 1.6f,
            ShadowOpacity = 0.55f,
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-20f, 140f, 0f),
            LightColor = Color.Color8(168, 190, 226),
            LightEnergy = 0.35f,
        });
        AddChild(new WorldEnvironment
        {
            Environment = new global::Godot.Environment
            {
                BackgroundMode = global::Godot.Environment.BGMode.Color,
                BackgroundColor = Color.Color8(206, 228, 246),
                AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Color.Color8(186, 200, 222),
                AmbientLightEnergy = 0.62f,
                TonemapMode = global::Godot.Environment.ToneMapper.Filmic,
                TonemapWhite = 1.4f,
            },
        });

        if (_variants)
        {
            BuildVariantRows();
            return;
        }

        for (int r = 0; r < Rows.Length; r++)
        {
            float z = (r - 1f) * RowGap;
            for (int i = 0; i < Rows[r].Length; i++)
            {
                float x = (i - 2.5f) * Spacing;
                Place(Rows[r][i], 0, new Vector3(x, 0f, z));
                AddLabel(Rows[r][i].Name, new Vector3(x, 0.01f, z + 0.62f));
            }
        }

        AddCamera(5.6f, new Vector3(0f, 0.25f, 0.1f));
        GD.Print("[terrain-gallery] 18 类装饰第 0 档（三行）：" + string.Join(" / ", Rows.Select(row => string.Join("、", row.Select(k => k.Name)))));
    }

    /// <summary>全部档：每类一行（左栏前 10 类、右栏后 9 类），行首标类名。</summary>
    private void BuildVariantRows()
    {
        const int perColumn = 10;
        for (int k = 0; k < AllKinds.Length; k++)
        {
            int column = k / perColumn;
            int row = k % perColumn;
            float left = column == 0 ? -8.6f : 0.6f;
            float z = (row - ((perColumn - 1) * 0.5f)) * VariantRowGap;
            TerrainParts.Kind kind = AllKinds[k];
            AddLabel(kind.Name, new Vector3(left, 0.01f, z), fontSize: 52);
            for (int v = 0; v < kind.Variants; v++)
            {
                Place(kind, v, new Vector3(left + 1.05f + (v * VariantSpacing), 0f, z));
            }
        }

        AddCamera(16.5f, new Vector3(0f, 0.2f, 0.4f));
        GD.Print("[terrain-gallery] 全部档（每类一行）：" + string.Join("、", AllKinds.Select(k => $"{k.Name}×{k.Variants}")));
    }

    /// <summary>一件装饰立在它所在地表色的一块地砖上（地砖上表面 y = 0，与对局同一锚点约定）。</summary>
    private void Place(TerrainParts.Kind kind, int variant, Vector3 at)
    {
        bool water = kind == TerrainParts.Bridge;
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.TileSize, BoardGeometry.TileHeight, BoardGeometry.TileSize) },
            MaterialOverride = Visuals.Matte(TileColorOf(kind)),
            Position = at + new Vector3(0f, (water ? -0.092f : 0f) - (BoardGeometry.TileHeight * 0.5f), 0f),
        });
        Node3D part = TerrainParts.Create(kind, variant);
        part.Position = at;
        AddChild(part);
        _placed.Add((kind.Variants == 1 ? kind.Name : $"{kind.Name}_{variant}", part));
    }

    /// <summary>该类装饰在对局里所在格的地表色（障碍格用障碍色，桥下是深水）。</summary>
    private static Color TileColorOf(TerrainParts.Kind kind) => kind.Name switch
    {
        "trees" => Visuals.TileForest,
        "desert" => Visuals.TileDesert,
        "marsh" => Visuals.TileMarsh,
        "crag" => Visuals.TileCrag,
        "shallows" => Visuals.TileShallows,
        "bridge" => Visuals.DeepWater,
        "grass" or "pebble" or "flower" or "stele" or "fence_x" or "fence_z" => Visuals.TilePlayableGreen,
        _ => Visuals.TileObstacle,
    };

    private void AddLabel(string text, Vector3 at, int fontSize = 60) => AddChild(new Label3D
    {
        Text = text,
        Position = at,
        RotationDegrees = new Vector3(-90f, 0f, 0f),
        FontSize = fontSize,
        PixelSize = 0.004f,
        Modulate = Visuals.CoordinateLabel,
        OutlineModulate = Visuals.CoordinateLabelOutline,
        OutlineSize = 10,
    });

    private void AddCamera(float distance, Vector3 focus)
    {
        float pitch = Mathf.DegToRad(60f);
        AddChild(_camera = new Camera3D
        {
            Fov = CameraPose.FovDegrees,
            Position = focus + new Vector3(0f, distance * Mathf.Sin(pitch), distance * Mathf.Cos(pitch)),
            RotationDegrees = new Vector3(-60f, 0f, 0f),
            Current = true,
        });
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_pending || ++_frame < 5)
        {
            return;
        }

        _pending = true;
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("[terrain-gallery] 无头模式没有可截取的画面，已跳过截图。");
            GetTree().Quit(0);
            return;
        }

        CaptureWhenDrawn();
    }

    private async void CaptureWhenDrawn()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(_path);
        GD.Print($"[terrain-gallery] 截图 {_path}：{error}（{image.GetWidth()}×{image.GetHeight()}，第 {_frame} 帧）；"
            + $"装饰卡通 glb {TerrainParts.ChibiCount} 件、回退旧样式 {TerrainParts.ChibiFallbackCount} 件");
        PrintFrames();
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    /// <summary>逐件打印画面里的外接框（像素），供 README 里的去色 / 墨色占比检查按件裁图。</summary>
    private void PrintFrames()
    {
        foreach ((string name, Node3D part) in _placed)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (MeshInstance3D mesh in part.GetChildren().OfType<MeshInstance3D>())
            {
                Aabb box = mesh.GlobalTransform * mesh.GetAabb();
                for (int c = 0; c < 8; c++)
                {
                    Vector2 p = _camera!.UnprojectPosition(box.GetEndpoint(c));
                    min = min.Min(p);
                    max = max.Max(p);
                }
            }

            GD.Print($"[terrain-gallery] 框 {name} {min.X:0} {min.Y:0} {max.X:0} {max.Y:0}");
        }
    }
}
