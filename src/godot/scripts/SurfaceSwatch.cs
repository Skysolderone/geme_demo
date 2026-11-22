using Godot;
using Siege.Core.Board;
using Siege.Presentation.Camera;

namespace Siege.Godot;

/// <summary>
/// 地表色块对照图（chibi-world 段 ②，人工检查清单用）：<c>--surface-swatch=&lt;PNG 路径&gt;</c> 时不建局，按当前色板（<c>--palette=</c>）拍一张图后退出。
/// 三行：上行草地三片 + 六种地表 + 深水的平地色块；中行同样十块，每块四个象限各叠一个阵营的独占领地着色（与对局 α 0.62 相同）；
/// 下行一座 h=1 缓坡台、一座 h=2 崖壁台（侧面、提亮色与高差描边都按对局的画法）和一块障碍格地面。去色版由 README 里的 PIL 命令从本图生成。
/// </summary>
/// <remarks>
/// 纯渲染：材质与对局同一套工厂（<see cref="Visuals.ToonVertex"/>、<see cref="TerrainParts.PieceOf"/> 的卡通侧面、<see cref="TerrainParts.EdgeOutlineMesh"/>）；
/// 光照与环境同 <see cref="TerrainGallery"/>，相机俯角 60°。截图后逐块打印色块中心的像素坐标，供 README 的取样检查。
/// </remarks>
public sealed partial class SurfaceSwatch : Node3D
{
    private const float Spacing = 1.2f;

    private readonly string _path;
    private readonly List<(string Name, Vector3 At)> _probes = [];
    private Camera3D? _camera;
    private int _frame;
    private bool _pending;

    /// <param name="path">PNG 输出路径。</param>
    public SurfaceSwatch(string path) => _path = path;

    /// <inheritdoc/>
    public override void _Ready()
    {
        AddStudio(this);

        TerrainPalette palette = TerrainPalette.Current;
        (string Name, Color Color)[] swatches =
        [
            ("grass", palette.GrassPatchA), ("grass-2", palette.GrassPatchB), ("grass-3", palette.GrassPatchC),
            ("road", palette.Road), ("forest", palette.Forest), ("desert", palette.Desert), ("marsh", palette.Marsh),
            ("crag", palette.Crag), ("shallows", palette.Shallows), ("water", palette.DeepWater),
        ];
        Color[] factions = [.. Enumerable.Range(0, 4).Select(p => Visuals.FactionColorOf(new PlayerId(p)))];

        for (int i = 0; i < swatches.Length; i++)
        {
            float x = (i - ((swatches.Length - 1) * 0.5f)) * Spacing;
            (string name, Color color) = swatches[i];
            bool water = name == "water";
            Slab(new Vector3(x, water ? -0.1f : 0f, -1.4f), color, water);
            Label(name, new Vector3(x, 0.01f, -0.78f));
            _probes.Add((name, new Vector3(x, water ? -0.1f : 0f, -1.4f)));

            // 中行：同色地块，四个象限各叠一个阵营的独占领地着色（α 0.62，与 BoardView.DrawTerritory 的独占档相同）。
            Slab(new Vector3(x, water ? -0.1f : 0f, 0.1f), color, water);
            if (!water)
            {
                for (int q = 0; q < 4; q++)
                {
                    var offset = new Vector3(((q & 1) == 0 ? -0.5f : 0.5f) * 0.45f, 0.008f, ((q & 2) == 0 ? -0.5f : 0.5f) * 0.45f);
                    AddChild(new MeshInstance3D
                    {
                        Mesh = new PlaneMesh { Size = new Vector2(0.43f, 0.43f) },
                        MaterialOverride = Visuals.Flat(new Color(factions[q], 0.62f)),
                        Position = new Vector3(x, 0f, 0.1f) + offset,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
                    _probes.Add(($"{name}+P{q}", new Vector3(x, 0f, 0.1f) + offset));
                }
            }
        }

        // 下行：2×2 的 h=1 缓坡台与 h=2 崖壁台（草地色），一块障碍格地面。
        Plateau(new Vector3(-3.6f, 0f, 2.1f), 1, palette.GrassPatchA);
        Plateau(new Vector3(0f, 0f, 2.1f), 2, palette.GrassPatchA);
        Slab(new Vector3(3.6f, 0f, 2.1f), palette.Obstacle, water: false);
        Label("obstacle", new Vector3(3.6f, 0.01f, 2.72f));
        _probes.Add(("obstacle", new Vector3(3.6f, 0f, 2.1f)));

        float pitch = Mathf.DegToRad(60f);
        const float distance = 8.2f;
        var focus = new Vector3(0f, 0f, 0.55f);
        AddChild(_camera = new Camera3D
        {
            Fov = CameraPose.FovDegrees,
            Position = focus + new Vector3(0f, distance * Mathf.Sin(pitch), distance * Mathf.Cos(pitch)),
            RotationDegrees = new Vector3(-60f, 0f, 0f),
            Current = true,
        });
        GD.Print($"[surface-swatch] 色板 {palette.Name}：{palette.Intent}");
    }

    /// <summary>对照图的灯光与环境（与 <see cref="TerrainGallery"/> 相同；<see cref="MarkerSwatch"/> 共用）。</summary>
    internal static void AddStudio(Node3D parent)
    {
        parent.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52f, -38f, 0f),
            LightColor = Color.Color8(255, 240, 208),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
            ShadowBlur = 1.6f,
            ShadowOpacity = 0.55f,
        });
        parent.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-20f, 140f, 0f),
            LightColor = Color.Color8(168, 190, 226),
            LightEnergy = 0.35f,
        });
        parent.AddChild(new WorldEnvironment
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
    }

    /// <summary>一块铺满整格的地表平板（大图同款：CellSize 见方、TileHeight 厚，上表面在 <paramref name="top"/>）。</summary>
    private void Slab(Vector3 top, Color color, bool water) => AddChild(new MeshInstance3D
    {
        Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.TileSize, BoardGeometry.TileHeight, BoardGeometry.TileSize) },
        MaterialOverride = water ? Visuals.Toon(color, specular: true) : Visuals.ToonVertex(color),
        Position = top - new Vector3(0f, BoardGeometry.TileHeight * 0.5f, 0f),
    });

    /// <summary>
    /// 2×2 格的高台：每格按对局画法堆 <paramref name="level"/> 层侧面（h=1 缓坡、h=2 两层崖壁，卡通材质取 <see cref="TerrainParts.PieceOf"/>），
    /// 顶上铺满格的面砖（向色板提亮色拉 h=1 / h=2 的比例），四周外缘与凸角摆高差描边——与 BoardView.AddReliefOutlines 同一网格、同一摆法。
    /// </summary>
    private void Plateau(Vector3 center, int level, Color color)
    {
        TerrainPalette palette = TerrainPalette.Current;
        Color tile = color.Lerp(palette.HighWash, level == 1 ? palette.HighWashLow : palette.HighWashHigh);
        StandardMaterial3D surface = Visuals.ToonVertex(tile);
        float half = BoardGeometry.CellSize * 0.5f;
        float topY = level * BoardGeometry.LayerHeight;
        var cells = new[] { new Vector3(-half, 0f, -half), new Vector3(half, 0f, -half), new Vector3(-half, 0f, half), new Vector3(half, 0f, half) };
        // 底下一块 4×4 格的 h=0 草地（大图同款无缝地面），高台立在它中央。
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(4f * BoardGeometry.CellSize, BoardGeometry.TileHeight, 4f * BoardGeometry.CellSize) },
            MaterialOverride = Visuals.ToonVertex(color),
            Position = center - new Vector3(0f, BoardGeometry.TileHeight * 0.5f, 0f),
        });

        foreach (Vector3 offset in cells)
        {
            Vector3 top = center + offset + new Vector3(0f, topY, 0f);
            for (int layer = 1; layer <= level; layer++)
            {
                float bandTop = (layer * BoardGeometry.LayerHeight) - BoardGeometry.TileHeight;
                TerrainParts.Piece side = TerrainParts.PieceOf(level == 2 ? TerrainParts.SideCliff : TerrainParts.SideSlope, layer);
                AddChild(new MeshInstance3D { Mesh = side.Mesh, MaterialOverride = side.Material, Position = new Vector3(top.X, center.Y + bandTop, top.Z) });
            }

            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.CellSize, BoardGeometry.TileHeight, BoardGeometry.CellSize) },
                MaterialOverride = surface,
                Position = top - new Vector3(0f, BoardGeometry.TileHeight * 0.5f, 0f),
            });

            // 2×2 的每格都有两条外缘、一个凸角：外法线即格相对台心的两个方向。
            var outX = new Vector3(Math.Sign(offset.X), 0f, 0f);
            var outZ = new Vector3(0f, 0f, Math.Sign(offset.Z));
            foreach (Vector3 outward in new[] { outX, outZ })
            {
                AddOutline(TerrainParts.EdgeOutlineMesh, new Transform3D(Facing(-outward), top + (outward * half)));
                var side = new Vector3(-outward.Z, 0f, outward.X);
                if (side == outX || side == outZ)
                {
                    AddOutline(TerrainParts.CornerOutlineMesh, new Transform3D(Facing(-outward), top + ((outward + side) * half)));
                }
            }
        }

        // 描边取样点：三条外缘（南 = 朝相机、北 = 远侧、东）的棱线外侧半个描边宽处应是墨色；台面中缝（两格同高相邻）不应有墨色。
        float edge = BoardGeometry.CellSize + (TerrainParts.ReliefOutlineWidth * 0.5f);
        string tag = level == 1 ? "slope" : "cliff";
        _probes.Add(($"{tag}-crest-S", center + new Vector3(-half, topY, edge)));
        _probes.Add(($"{tag}-crest-N", center + new Vector3(-half, topY, -edge)));
        _probes.Add(($"{tag}-crest-E", center + new Vector3(edge, topY, half)));
        _probes.Add(($"{tag}-seam", center + new Vector3(0f, topY, half)));
        Label(level == 1 ? "slope h1" : "cliff h2", center + new Vector3(0f, 0.01f, 2.0f));
        _probes.Add((level == 1 ? "slope-side" : "cliff-side", center + new Vector3(0f, topY * 0.45f, BoardGeometry.CellSize)));
    }

    private void AddOutline(Mesh mesh, Transform3D transform) => AddChild(new MeshInstance3D
    {
        Name = PieceModels.OutlineName,
        Mesh = mesh,
        MaterialOverride = TerrainParts.ReliefOutline,
        Transform = transform,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    });

    /// <summary>与 BoardView.Facing 相同：局部 +Z → <paramref name="inward"/>。</summary>
    private static Basis Facing(Vector3 inward) => new(new Vector3(inward.Z, 0f, -inward.X), Vector3.Up, inward);

    private void Label(string text, Vector3 at) => AddChild(new Label3D
    {
        Text = text,
        Position = at,
        RotationDegrees = new Vector3(-90f, 0f, 0f),
        FontSize = 52,
        PixelSize = 0.004f,
        Modulate = Visuals.CoordinateLabel,
        OutlineModulate = Visuals.CoordinateLabelOutline,
        OutlineSize = 10,
    });

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
            GD.Print("[surface-swatch] 无头模式没有可截取的画面，已跳过截图。");
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
        GD.Print($"[surface-swatch] 截图 {_path}：{error}（{image.GetWidth()}×{image.GetHeight()}，第 {_frame} 帧）");
        foreach ((string name, Vector3 at) in _probes)
        {
            Vector2 p = _camera!.UnprojectPosition(at);
            GD.Print($"[surface-swatch] 取样 {name} {p.X:0} {p.Y:0}");
        }

        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
